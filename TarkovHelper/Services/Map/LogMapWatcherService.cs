using System.IO;
using System.Text.RegularExpressions;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services.Map;

/// <summary>
/// 게임 로그 파일을 감시하여 맵 변경을 감지하는 서비스.
/// application.log 파일에서 맵 로딩 이벤트를 실시간으로 감지합니다.
/// </summary>
public sealed class LogMapWatcherService : IDisposable
{
    private static readonly ILogger _log = Log.For<LogMapWatcherService>();
    private static readonly Lazy<LogMapWatcherService> _lazyInstance = new(() => new LogMapWatcherService());
    public static LogMapWatcherService Instance => _lazyInstance.Value;

    private FileSystemWatcher? _logWatcher;
    private readonly object _watcherLock = new();
    private readonly object _readLock = new();
    private CancellationTokenSource? _watchCts;
    private System.Threading.Timer? _debounceTimer;
    private string? _pendingChangedPath;
    private string? _currentLogFolderPath;
    private string? _lastDetectedMap;
    private readonly Dictionary<string, long> _filePositions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastFileEventTimes = new(StringComparer.OrdinalIgnoreCase);
    private bool _isWatching;
    private bool _isDisposed;

    // 맵 이름 매핑 (게임 내부 이름 -> 맵 설정 키)
    // 맵 설정 키는 map_configs.json의 "key" 필드와 일치해야 함
    internal static readonly Dictionary<string, string> MapNameMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        // Woods
        { "woods", "Woods" },
        { "woods_preset", "Woods" },

        // Customs
        { "customs", "Customs" },
        { "customs_preset", "Customs" },
        { "bigmap", "Customs" },
        { "bigmap_preset", "Customs" },

        // Shoreline
        { "shoreline", "Shoreline" },
        { "shoreline_preset", "Shoreline" },

        // Interchange
        { "interchange", "Interchange" },
        { "interchange_preset", "Interchange" },
        { "shopping_mall", "Interchange" },
        { "shopping_mall_preset", "Interchange" },

        // Reserve
        { "reserve", "Reserve" },
        { "rezerv", "Reserve" },
        { "rezervbase", "Reserve" },
        { "rezerv_base", "Reserve" },
        { "rezerv_base_preset", "Reserve" },

        // Lighthouse
        { "lighthouse", "Lighthouse" },
        { "lighthouse_preset", "Lighthouse" },

        // Streets of Tarkov
        { "tarkovstreets", "StreetsOfTarkov" },
        { "streets", "StreetsOfTarkov" },
        { "city", "StreetsOfTarkov" },
        { "city_preset", "StreetsOfTarkov" },

        // Factory - Day/Night are same map
        { "factory", "Factory" },
        { "factory4_day", "Factory" },
        { "factory4_night", "Factory" },
        { "factory_day", "Factory" },
        { "factory_night", "Factory" },
        { "factory_day_preset", "Factory" },
        { "factory_night_preset", "Factory" },

        // Ground Zero - All levels are same map
        { "groundzero", "GroundZero" },
        { "sandbox", "GroundZero" },
        { "sandbox_high", "GroundZero" },
        { "sandbox_start", "GroundZero" },
        { "sandbox_preset", "GroundZero" },
        { "sandbox_high_preset", "GroundZero" },

        // Labs
        { "laboratory", "Labs" },
        { "laboratory_preset", "Labs" },
        { "labs", "Labs" },

        // Labyrinth
        { "labyrinth", "Labyrinth" },
        { "labyrinth_preset", "Labyrinth" },

        // Terminal
        { "terminal", "Terminal" },
        { "terminal_preset", "Terminal" },
    };

    // 로그에서 맵 로딩을 감지하는 정규식 패턴들
    // 실제 로그 형식 예시:
    // - scene preset path:maps/laboratory_preset.bundle rcid:laboratory.ScenesPreset.asset
    // - scene preset path:maps/rezerv_base_preset.bundle rcid:Rezerv_Base.scenespreset.asset
    // - TRACE-NetworkGameCreate profileStatus: '..., Location: RezervBase, Sid: ...'
    // - Location: factory4_day, Sid: ...
    internal static readonly Regex[] MapDetectionPatterns =
    [
        // 1. 번들 이름 전체 (예: maps/rezerv_base_preset.bundle -> rezerv_base_preset)
        new Regex(@"maps/([a-zA-Z0-9_-]+)\.bundle", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        // 2. _preset 접미사 이전 이름 (예: maps/rezerv_base_preset.bundle -> rezerv_base)
        new Regex(@"maps/([a-zA-Z0-9_-]+)_preset\.bundle", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        // 3. Location 필드 (예: Location: RezervBase, Sid: ... 또는 Location: factory4_day,)
        new Regex(@"Location:\s*([a-zA-Z0-9_-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    ];

    /// <summary>
    /// 맵 변경 감지 이벤트
    /// </summary>
    public event EventHandler<MapChangedEventArgs>? MapChanged;

    /// <summary>
    /// 감시 상태 변경 이벤트
    /// </summary>
    public event EventHandler<bool>? WatchingStateChanged;

    /// <summary>
    /// 오류 발생 이벤트
    /// </summary>
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// 현재 감시 중인지 여부
    /// </summary>
    public bool IsWatching => _isWatching;

    /// <summary>
    /// 마지막으로 감지된 맵
    /// </summary>
    public string? LastDetectedMap => _lastDetectedMap;

    /// <summary>
    /// 레이드가 종료되었을 때 마지막 감지된 맵 상태를 초기화합니다.
    /// 동일한 맵에 다시 진입할 때 정상적으로 감지되도록 보장합니다.
    /// </summary>
    public void ResetLastDetectedMap()
    {
        lock (_readLock)
        {
            _lastDetectedMap = null;
        }
        _log.Info("LogMapWatcherService: LastDetectedMap reset (RaidEnded)");
    }

    private LogMapWatcherService() { }

    /// <summary>
    /// 로그 폴더 감시를 시작합니다.
    /// </summary>
    /// <param name="logFolderPath">로그 폴더 경로. null이면 기본 경로 사용.</param>
    public bool StartWatching(string? logFolderPath = null)
    {
        bool started = false;
        string? latestFolder = null;

        lock (_watcherLock)
        {
            if (_isDisposed) return false;
            StopWatching();

            // 경로가 지정되지 않거나 존재하지 않으면 자동 감지
            var folderPath = logFolderPath;
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                folderPath = GetDefaultLogFolderPath();
            }

            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                _log.Warning($"Log folder is not set or does not exist: '{folderPath}'");
                folderPath = null;
            }
            else
            {
                try
                {
                    if (string.IsNullOrEmpty(SettingsService.Instance.LogFolderPath) || !Directory.Exists(SettingsService.Instance.LogFolderPath))
                    {
                        SettingsService.Instance.LogFolderPath = folderPath;
                    }
                }
                catch { }

                _currentLogFolderPath = folderPath;

                try
                {
                    _watchCts = new CancellationTokenSource();
                    _debounceTimer = new System.Threading.Timer(OnDebounceTimerTick, null, Timeout.Infinite, Timeout.Infinite);

                    _logWatcher = new FileSystemWatcher(folderPath)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                        Filter = "*application*.log",
                        IncludeSubdirectories = true,
                        InternalBufferSize = 65536,
                        EnableRaisingEvents = true
                    };

                    _logWatcher.Changed += OnLogFileChanged;
                    _logWatcher.Created += OnLogFileCreated;
                    _logWatcher.Error += OnWatcherError;

                    _isWatching = true;
                    lock (_readLock)
                    {
                        _filePositions.Clear();
                        _lastFileEventTimes.Clear();
                    }

                    _log.Info($"LogMapWatcherService started watching: '{folderPath}' (Filter=*application*.log)");
                    latestFolder = FindLatestLogSubfolder(folderPath);
                    started = true;
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to start log watcher: {ex.Message}", ex);
                    _isWatching = false;
                    ErrorOccurred?.Invoke(this, $"Failed to start log watcher: {ex.Message}");
                }
            }

            if (folderPath == null)
            {
                ErrorOccurred?.Invoke(this, "Log folder is not set or does not exist.");
                return false;
            }
        }

        // Invoke scans & events OUTSIDE _watcherLock to prevent lock reentrancy deadlocks
        if (started)
        {
            if (latestFolder != null)
            {
                ScanLatestLogForMap(latestFolder);
            }
            WatchingStateChanged?.Invoke(this, true);
        }
        else
        {
            WatchingStateChanged?.Invoke(this, false);
        }

        return started;
    }

    /// <summary>
    /// 기본 EFT 로그 폴더 경로를 반환합니다.
    /// </summary>
    private static string? GetDefaultLogFolderPath()
    {
        // 1. Settings configured path
        try
        {
            var settingsPath = SettingsService.Instance?.LogFolderPath;
            if (!string.IsNullOrEmpty(settingsPath) && Directory.Exists(settingsPath))
                return settingsPath;
        }
        catch { }

        // 2. Standard game install paths
        var candidates = new List<string>
        {
            @"C:\Battlestate Games\Escape from Tarkov\Logs",
            @"C:\Battlestate Games\EFT\Logs",
            @"D:\Battlestate Games\Escape from Tarkov\Logs",
            @"D:\Battlestate Games\EFT\Logs",
            @"E:\Battlestate Games\Escape from Tarkov\Logs",
            @"E:\Battlestate Games\EFT\Logs",
            @"F:\Battlestate Games\Escape from Tarkov\Logs",
            @"F:\Battlestate Games\EFT\Logs",
        };

        // 3. LocalAppData path
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(localAppData, "Battlestate Games", "EFT", "Logs"));
            candidates.Add(Path.Combine(localAppData, "Battlestate Games", "Escape from Tarkov", "Logs"));
        }
        catch { }

        // 4. Registry lookup
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov")
                         ?? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov");
            if (key?.GetValue("InstallLocation") is string installLoc && !string.IsNullOrWhiteSpace(installLoc))
            {
                var regLogPath = Path.Combine(installLoc, "Logs");
                candidates.Insert(0, regLogPath);
            }
        }
        catch { }

        foreach (var path in candidates)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// 감시를 중지합니다.
    /// </summary>
    public void StopWatching()
    {
        bool wasWatching;
        lock (_watcherLock)
        {
            wasWatching = _isWatching || _logWatcher != null;
            _isWatching = false;

            try
            {
                _watchCts?.Cancel();
                _watchCts?.Dispose();
            }
            catch { }
            _watchCts = null;

            _debounceTimer?.Dispose();
            _debounceTimer = null;
            _pendingChangedPath = null;

            if (_logWatcher != null)
            {
                _logWatcher.EnableRaisingEvents = false;
                _logWatcher.Changed -= OnLogFileChanged;
                _logWatcher.Created -= OnLogFileCreated;
                _logWatcher.Error -= OnWatcherError;
                _logWatcher.Dispose();
                _logWatcher = null;
            }

            lock (_readLock)
            {
                _filePositions.Clear();
                _lastFileEventTimes.Clear();
            }
        }

        if (wasWatching)
        {
            WatchingStateChanged?.Invoke(this, false);
            _log.Info("LogMapWatcherService stopped watching");
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        _log.Warning($"LogMapWatcher FileSystemWatcher error: {ex.Message}");
        ErrorOccurred?.Invoke(this, ex.Message);
    }

    /// <summary>
    /// 최신 로그 서브폴더를 찾습니다 (날짜별 폴더가 있는 경우).
    /// </summary>
    private string FindLatestLogSubfolder(string basePath)
    {
        try
        {
            // 로그 폴더 형식 찾기 (예: log_2025.12.04_20-43-45_1.0.0.2.42157)
            var subdirs = Directory.GetDirectories(basePath)
                .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^log_\d{4}\.\d{2}\.\d{2}"))
                .OrderByDescending(d => Directory.GetLastWriteTime(d))
                .ToList();

            if (subdirs.Count > 0)
            {
                return subdirs[0];
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Error finding latest log subfolder in '{basePath}': {ex.Message}");
        }

        return basePath;
    }

    /// <summary>
    /// 최신 로그 파일에서 마지막 맵을 스캔합니다.
    /// </summary>
    private void ScanLatestLogForMap(string folderPath)
    {
        try
        {
            // application.log 파일 찾기
            var logFiles = Directory.GetFiles(folderPath, "*application*.log", SearchOption.AllDirectories)
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();

            if (logFiles.Count == 0) return;

            var latestLog = logFiles[0];
            var fileInfo = new FileInfo(latestLog);

            // 파일 끝에서 일정량만 읽기 (최근 이벤트)
            var bytesToRead = Math.Min(fileInfo.Length, 150000); // 150KB
            lock (_readLock)
            {
                _filePositions[latestLog] = fileInfo.Length;
            }

            using var stream = new FileStream(latestLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (bytesToRead < fileInfo.Length)
            {
                stream.Seek(-bytesToRead, SeekOrigin.End);
            }

            using var reader = new StreamReader(stream);
            if (bytesToRead < fileInfo.Length)
            {
                reader.ReadLine(); // 첫 줄은 잘릴 수 있으므로 스킵
            }

            string? lastMap = null;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var map = TryExtractMapFromLine(line);
                if (map != null)
                {
                    lastMap = map;
                }
            }

            if (lastMap != null)
            {
                string? previousMap;
                lock (_readLock)
                {
                    previousMap = _lastDetectedMap;
                    _lastDetectedMap = lastMap;
                }

                _log.Info($"Initial scan detected map: '{lastMap}' from '{Path.GetFileName(latestLog)}'");

                // 초기 스캔에서도 맵 변경 이벤트 발생 (맵 탭 진입 시 현재 맵 자동 선택)
                MapChanged?.Invoke(this, new MapChangedEventArgs
                {
                    NewMapKey = lastMap,
                    PreviousMapKey = previousMap,
                    Timestamp = DateTime.Now
                });
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Error scanning latest log for map in '{folderPath}': {ex.Message}");
        }
    }

    private void OnLogFileCreated(object sender, FileSystemEventArgs e)
    {
        var fileName = Path.GetFileName(e.FullPath);
        if (!fileName.Contains("application", StringComparison.OrdinalIgnoreCase))
            return;

        _log.Info($"New application log detected: '{e.FullPath}'");
        CancellationToken token;
        lock (_readLock)
        {
            _lastDetectedMap = null;
            _filePositions[e.FullPath] = 0;
            _lastFileEventTimes[e.FullPath] = DateTime.Now;
            token = _watchCts?.Token ?? CancellationToken.None;
        }

        if (token.IsCancellationRequested) return;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(200, token).ConfigureAwait(false);
                if (!token.IsCancellationRequested && _isWatching)
                {
                    ProcessLogFileChanges(e.FullPath);
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void OnLogFileChanged(object sender, FileSystemEventArgs e)
    {
        var fileName = Path.GetFileName(e.FullPath);
        if (!fileName.Contains("application", StringComparison.OrdinalIgnoreCase))
            return;

        lock (_watcherLock)
        {
            if (!_isWatching || _isDisposed) return;
            _pendingChangedPath = e.FullPath;
            _debounceTimer?.Change(150, Timeout.Infinite);
        }
    }

    private void OnDebounceTimerTick(object? state)
    {
        string? path;
        lock (_watcherLock)
        {
            if (!_isWatching || _isDisposed) return;
            path = _pendingChangedPath;
            _pendingChangedPath = null;
        }

        if (!string.IsNullOrEmpty(path))
        {
            ProcessLogFileChanges(path);
        }
    }

    public void ProcessLogFileChanges(string filePath)
    {
        List<MapChangedEventArgs>? pendingEvents = null;

        lock (_readLock)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists) return;

                var lastPosition = _filePositions.GetValueOrDefault(filePath, 0);
                if (fileInfo.Length < lastPosition)
                {
                    lastPosition = 0;
                }

                if (fileInfo.Length <= lastPosition)
                {
                    return;
                }

                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(lastPosition, SeekOrigin.Begin);

                var bytesAvailable = stream.Length - lastPosition;
                if (bytesAvailable <= 0) return;

                var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent((int)Math.Min(bytesAvailable, 1024 * 1024));
                try
                {
                    int totalRead = 0;
                    int toRead = (int)Math.Min(bytesAvailable, buffer.Length);
                    while (totalRead < toRead)
                    {
                        int n = stream.Read(buffer, totalRead, toRead - totalRead);
                        if (n <= 0) break;
                        totalRead += n;
                    }

                    if (totalRead <= 0) return;

                    // Solo avanzar hasta el último salto de línea completo para no truncar líneas UTF-8 parciales
                    int lastNewline = Array.LastIndexOf(buffer, (byte)'\n', totalRead - 1);
                    if (lastNewline < 0)
                    {
                        // Si la línea aún no terminó en '\n' (o es extremadamente larga), esperar al siguiente flush o leer todo si supera el buffer
                        if (totalRead < buffer.Length) return;
                        lastNewline = totalRead - 1;
                    }

                    int completeLen = lastNewline + 1;
                    _filePositions[filePath] = lastPosition + completeLen;

                    var chunk = System.Text.Encoding.UTF8.GetString(buffer, 0, completeLen);
                    foreach (var lineSpan in chunk.AsSpan().EnumerateLines())
                    {
                        if (lineSpan.IsEmpty) continue;
                        var map = TryExtractMapFromLine(lineSpan.ToString());
                        if (map != null && !string.Equals(map, _lastDetectedMap, StringComparison.OrdinalIgnoreCase))
                        {
                            var previousMap = _lastDetectedMap;
                            _lastDetectedMap = map;
                            _log.Info($"Map change detected from EFT logs: '{previousMap}' -> '{map}' (file={Path.GetFileName(filePath)})");
                            pendingEvents ??= new List<MapChangedEventArgs>();
                            pendingEvents.Add(new MapChangedEventArgs
                            {
                                NewMapKey = map,
                                PreviousMapKey = previousMap,
                                Timestamp = DateTime.Now
                            });
                        }
                    }
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Error reading log file '{filePath}': {ex.Message}");
            }
        }

        // Disparar MapChanged fuera de _readLock para evitar bloqueos cruzados con suscriptores
        if (pendingEvents != null)
        {
            foreach (var ev in pendingEvents)
            {
                MapChanged?.Invoke(this, ev);
            }
        }
    }

    /// <summary>
    /// 로그 라인에서 맵 이름을 추출합니다.
    /// </summary>
    public string? TryExtractMapFromLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        foreach (var pattern in MapDetectionPatterns)
        {
            var match = pattern.Match(line);
            if (match.Success && match.Groups.Count > 1)
            {
                var rawMapName = match.Groups[1].Value;
                var normalized = NormalizeMapName(rawMapName);
                if (normalized != null)
                {
                    return normalized;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 게임 내부 맵 이름을 표시용 키로 변환합니다.
    /// </summary>
    public string? NormalizeMapName(string rawMapName)
    {
        if (string.IsNullOrWhiteSpace(rawMapName))
            return null;

        var trimmed = rawMapName.Trim();

        // 1. MapNameMapping 확인
        if (MapNameMapping.TryGetValue(trimmed, out var mappedName))
        {
            return mappedName;
        }

        // 2. _preset 접미사 제거 후 재확인
        if (trimmed.EndsWith("_preset", StringComparison.OrdinalIgnoreCase))
        {
            var withoutPreset = trimmed[..^7];
            if (MapNameMapping.TryGetValue(withoutPreset, out var nameWithoutPreset))
            {
                return nameWithoutPreset;
            }
        }

        // 3. MapTrackerService의 별칭 해결 시도 (map_configs.json)
        try
        {
            var resolved = MapTrackerService.Instance.ResolveMapKey(trimmed);
            if (!string.IsNullOrEmpty(resolved))
            {
                return resolved;
            }
        }
        catch
        {
            // MapTrackerService 초기화 전 호출 대비
        }

        // 4. 알려진 맵 키 직접 일치 확인
        try
        {
            var allKeys = MapTrackerService.Instance.GetAllMapKeys();
            var directMatch = allKeys.FirstOrDefault(k => string.Equals(k, trimmed, StringComparison.OrdinalIgnoreCase));
            if (directMatch != null)
            {
                return directMatch;
            }
        }
        catch
        {
            // MapTrackerService 초기화 전 호출 대비
        }

        _log.Warning($"Unknown map identifier detected in logs: '{trimmed}'");
        return trimmed;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        StopWatching();
    }
}

/// <summary>
/// 맵 변경 이벤트 인자
/// </summary>
public class MapChangedEventArgs : EventArgs
{
    /// <summary>
    /// 새로 감지된 맵 키
    /// </summary>
    public required string NewMapKey { get; init; }

    /// <summary>
    /// 이전 맵 키 (처음 감지된 경우 null)
    /// </summary>
    public string? PreviousMapKey { get; init; }

    /// <summary>
    /// 감지 시간
    /// </summary>
    public DateTime Timestamp { get; init; }
}
