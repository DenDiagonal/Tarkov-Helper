using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TarkovHelper.Models;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services;

/// <summary>
/// EFT 게임 로그를 모니터링하여 레이드 이벤트를 발생시키는 서비스.
/// Profile ID 감지, PMC/SCAV 구분, 레이드 시작/종료 등의 이벤트를 제공합니다.
/// </summary>
public sealed class EftRaidEventService : IDisposable
{
    private static readonly ILogger _log = Log.For<EftRaidEventService>();
    private static readonly Lazy<EftRaidEventService> _lazyInstance =
        new(() => new EftRaidEventService(), LazyThreadSafetyMode.ExecutionAndPublication);
    public static EftRaidEventService Instance => _lazyInstance.Value;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

    #region Regex Patterns

    // SelectProfile: ProfileId와 AccountId 추출
    private static readonly Regex SelectProfileRegex = new(
        @"SelectProfile ProfileId:([a-f0-9]+) AccountId:(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout);

    // Session mode: PVE/PVP 감지
    private static readonly Regex SessionModeRegex = new(
        @"Session mode: (Pve|Pvp|Regular)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout);

    // Matching with group id: 솔로/파티 구분
    private static readonly Regex MatchingGroupRegex = new(
        @"Matching with group id: (\d*)",
        RegexOptions.Compiled, RegexTimeout);

    // Local game matching cancelled: 매칭 취소
    private static readonly Regex MatchingCancelledRegex = new(
        @"Local game matching cancelled|Interrupted\. Interrupted by user",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout);

    // TRACE-NetworkGameCreate: 레이드 정보
    private static readonly Regex TraceNetworkCreateRegex = new(
        @"TRACE-NetworkGameCreate profileStatus: 'Profileid: ([^,]+), Status: ([^,]+), RaidMode: ([^,]+), Ip: ([^,]+), Port: ([^,]+), Location: ([^,]+), Sid: ([^,]+), GameMode: ([^,]+), shortId: ([^']+)'",
        RegexOptions.Compiled, RegexTimeout);

    // scene preset path: 맵 로딩
    private static readonly Regex ScenePresetRegex = new(
        @"scene preset path:maps/([^.]+)\.bundle",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout);

    // [Transit] 맵 전환
    private static readonly Regex TransitRegex = new(
        @"\[Transit\] Flag:([^,]+), RaidId:([^,]+), Count:(\d+), Locations:([^ ]+)",
        RegexOptions.Compiled, RegexTimeout);

    // [Transit] 레이드 종료 상세
    private static readonly Regex TransitEndRegex = new(
        @"\[Transit\] `([a-f0-9]+)` Count:(\d+), EventPlayer:(True|False)",
        RegexOptions.Compiled, RegexTimeout);

    // Connect (network-connection)
    private static readonly Regex ConnectRegex = new(
        @"(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}).*Connect \(address: ([\d.]+):(\d+)\)",
        RegexOptions.Compiled, RegexTimeout);

    // Disconnect (network-connection)
    private static readonly Regex DisconnectRegex = new(
        @"(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}).*Disconnect \(address: ([\d.]+):(\d+)\)",
        RegexOptions.Compiled, RegexTimeout);

    // Enter to the 'Connected' state
    private static readonly Regex ConnectedStateRegex = new(
        @"Enter to the 'Connected' state \(address: ([\d.]+):(\d+)",
        RegexOptions.Compiled, RegexTimeout);

    // Statistics (network-connection)
    private static readonly Regex StatisticsRegex = new(
        @"Statistics \(address: ([\d.]+):(\d+), rtt: ([\d.]+), lose: ([^,]+), sent: (\d+), received: (\d+)\)",
        RegexOptions.Compiled, RegexTimeout);

    // Timeout (network-connection)
    private static readonly Regex TimeoutRegex = new(
        @"Timeout: (Messages|Connection) timed out after not receiving any message for (\d+)ms \(address: ([\d.]+):(\d+)\)",
        RegexOptions.Compiled, RegexTimeout);

    // Timestamp 추출 (로그 라인 시작 - Fallback)
    private static readonly Regex TimestampRegex = new(
        @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})",
        RegexOptions.Compiled, RegexTimeout);

    // Init: 새 게임 세션 시작 (이전 레이드 종료 감지용 fallback)
    private static readonly Regex InitGameRegex = new(
        @"Init: pstrGameVersion:",
        RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex SubfolderLogRegex = new(
        @"^log_\d{4}\.\d{2}\.\d{2}",
        RegexOptions.Compiled, RegexTimeout);

    #endregion

    #region Map Name Mapping

    private static readonly Dictionary<string, string> MapNameMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        // Woods
        { "woods", "Woods" },
        { "woods_preset", "Woods" },

        // Customs
        { "customs", "Customs" },
        { "customs_preset", "Customs" },
        { "bigmap", "Customs" },

        // Shoreline
        { "shoreline", "Shoreline" },
        { "shoreline_preset", "Shoreline" },

        // Interchange
        { "interchange", "Interchange" },
        { "shopping_mall", "Interchange" },

        // Reserve
        { "reserve", "Reserve" },
        { "rezervbase", "Reserve" },
        { "rezerv_base_preset", "Reserve" },

        // Lighthouse
        { "lighthouse", "Lighthouse" },
        { "lighthouse_preset", "Lighthouse" },

        // Streets of Tarkov
        { "tarkovstreets", "StreetsOfTarkov" },
        { "streets", "StreetsOfTarkov" },
        { "city_preset", "StreetsOfTarkov" },

        // Factory
        { "factory", "Factory" },
        { "factory4_day", "Factory" },
        { "factory4_night", "Factory" },
        { "factory_day_preset", "Factory" },
        { "factory_night_preset", "Factory" },

        // Ground Zero
        { "groundzero", "GroundZero" },
        { "sandbox", "GroundZero" },
        { "sandbox_high", "GroundZero" },
        { "sandbox_start", "GroundZero" },
        { "sandbox_preset", "GroundZero" },
        { "sandbox_high_preset", "GroundZero" },
        { "sandbox_start_preset", "GroundZero" },

        // Labs
        { "laboratory", "Labs" },
        { "laboratory_preset", "Labs" },
        { "labs", "Labs" },
        { "lab", "Labs" },

        // Labyrinth
        { "labyrinth", "Labyrinth" },
        { "labyrinth_preset", "Labyrinth" },
    };

    #endregion

    #region Fields

    private FileSystemWatcher? _applicationLogWatcher;
    private FileSystemWatcher? _networkLogWatcher;
    private readonly object _watcherLock = new();
    private volatile bool _isWatching;
    private volatile bool _isDisposed;

    private readonly ConcurrentDictionary<string, long> _filePositions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new(StringComparer.OrdinalIgnoreCase);

    // Trailing-edge debounce timers + NTFS open-handle poll timer
    private Timer? _appLogDebounceTimer;
    private Timer? _netLogDebounceTimer;
    private Timer? _logPollTimer;
    private string? _monitoredFolderPath;
    private int _isPollingLogs;
    private string? _pendingAppLogPath;
    private string? _pendingNetLogPath;
    private readonly object _debounceLock = new();

    // State lock for current session / raid models
    private readonly object _stateLock = new();
    private EftProfileInfo? _currentProfile;
    private EftRaidInfo? _currentRaid;
    private GameMode _currentGameMode = GameMode.Unknown;
    private string? _pendingGroupId;

    #endregion

    #region Events

    public event EventHandler<EftProfileEventArgs>? ProfileChanged;
    public event EventHandler<EftRaidEventArgs>? RaidEvent;
    public event EventHandler<bool>? MonitoringStateChanged;
    public event EventHandler<string>? ErrorOccurred;

    #endregion

    #region Properties

    public EftProfileInfo? CurrentProfile
    {
        get
        {
            lock (_stateLock) return _currentProfile;
        }
    }

    public EftRaidInfo? CurrentRaid
    {
        get
        {
            lock (_stateLock) return _currentRaid;
        }
    }

    public GameMode CurrentGameMode
    {
        get
        {
            lock (_stateLock) return _currentGameMode;
        }
    }

    public bool IsMonitoring => _isWatching;

    #endregion

    private EftRaidEventService()
    {
        _appLogDebounceTimer = new Timer(OnAppLogTimerTick, null, Timeout.Infinite, Timeout.Infinite);
        _netLogDebounceTimer = new Timer(OnNetLogTimerTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    #region Public Methods

    public bool StartMonitoring(string? logFolderPath = null)
    {
        lock (_watcherLock)
        {
            _log.Debug($"StartMonitoring called with path: {logFolderPath ?? "(null, will use default)"}");
            StopMonitoring();

            var folderPath = logFolderPath ?? GetDefaultLogFolderPath();
            _log.Debug($"Resolved folder path: {folderPath}");

            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                _log.Warning($"Log folder not found or invalid: {folderPath}");
                RaiseEventSafe(ErrorOccurred, LocalizationService.Instance.CurrentLanguage switch
                {
                    AppLanguage.ES => "No se encontró la carpeta de registros de EFT.",
                    AppLanguage.KO => "EFT 로그 폴더를 찾을 수 없습니다.",
                    AppLanguage.JA => "EFTログフォルダーが見つかりません。",
                    _ => "Could not find EFT log folder."
                });
                return false;
            }

            try
            {
                _log.Debug("Creating FileSystemWatchers with 64KB buffer...");

                _applicationLogWatcher = new FileSystemWatcher(folderPath)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    Filter = "*application*.log",
                    IncludeSubdirectories = true,
                    InternalBufferSize = 65536,
                    EnableRaisingEvents = true
                };
                _applicationLogWatcher.Changed += OnApplicationLogChanged;
                _applicationLogWatcher.Created += OnLogFileCreated;
                _applicationLogWatcher.Error += OnWatcherError;

                _networkLogWatcher = new FileSystemWatcher(folderPath)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    Filter = "*network-connection*.log",
                    IncludeSubdirectories = true,
                    InternalBufferSize = 65536,
                    EnableRaisingEvents = true
                };
                _networkLogWatcher.Changed += OnNetworkLogChanged;
                _networkLogWatcher.Created += OnLogFileCreated;
                _networkLogWatcher.Error += OnWatcherError;

                _isWatching = true;
                _monitoredFolderPath = folderPath;
                _filePositions.Clear();

                // Order startup initialization deterministically: DB first, then InitialScan overrides with freshest log
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await LoadProfileFromDbAsync().ConfigureAwait(false);
                        if (_isWatching && !_isDisposed)
                        {
                            InitialScan(folderPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Warning($"Background startup scan failed: {ex.Message}");
                    }
                });

                _logPollTimer?.Dispose();
                _logPollTimer = new Timer(OnLogPollTimerTick, null, 2000, 2000);

                RaiseEventSafe(MonitoringStateChanged, true);
                _log.Info($"EftRaidEventService started monitoring: {folderPath}");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to start monitoring: {ex.Message}");
                RaiseEventSafe(ErrorOccurred, LocalizationService.Instance.CurrentLanguage switch
                {
                    AppLanguage.ES => $"Fallo al iniciar monitorización: {ex.Message}",
                    AppLanguage.KO => $"모니터링 시작 실패: {ex.Message}",
                    AppLanguage.JA => $"監視開始失敗: {ex.Message}",
                    _ => $"Failed to start monitoring: {ex.Message}"
                });
                _isWatching = false;
                RaiseEventSafe(MonitoringStateChanged, false);
                return false;
            }
        }
    }

    public void StopMonitoring()
    {
        lock (_watcherLock)
        {
            if (_logPollTimer != null)
            {
                _logPollTimer.Dispose();
                _logPollTimer = null;
            }

            if (_applicationLogWatcher != null)
            {
                _applicationLogWatcher.EnableRaisingEvents = false;
                _applicationLogWatcher.Changed -= OnApplicationLogChanged;
                _applicationLogWatcher.Created -= OnLogFileCreated;
                _applicationLogWatcher.Error -= OnWatcherError;
                _applicationLogWatcher.Dispose();
                _applicationLogWatcher = null;
            }

            if (_networkLogWatcher != null)
            {
                _networkLogWatcher.EnableRaisingEvents = false;
                _networkLogWatcher.Changed -= OnNetworkLogChanged;
                _networkLogWatcher.Created -= OnLogFileCreated;
                _networkLogWatcher.Error -= OnWatcherError;
                _networkLogWatcher.Dispose();
                _networkLogWatcher = null;
            }

            lock (_debounceLock)
            {
                _appLogDebounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _netLogDebounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _pendingAppLogPath = null;
                _pendingNetLogPath = null;
            }

            _filePositions.Clear();
            _fileLocks.Clear();

            _isWatching = false;
            RaiseEventSafe(MonitoringStateChanged, false);
        }
    }

    public async Task SetProfileAsync(string pmcProfileId, string? accountId = null)
    {
        EftProfileInfo profile;
        lock (_stateLock)
        {
            profile = new EftProfileInfo
            {
                PmcProfileId = pmcProfileId,
                ScavProfileId = CalculateScavProfileId(pmcProfileId),
                AccountId = accountId,
                UpdatedAt = DateTime.Now
            };
            _currentProfile = profile;
        }

        await SaveProfileToDbAsync(profile).ConfigureAwait(false);
        RaiseEventSafe(ProfileChanged, new EftProfileEventArgs { ProfileInfo = profile });
    }

    #endregion

    #region Private Methods - Log Processing & Watcher Callbacks

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        _log.Warning($"FileSystemWatcher buffer overflow or error: {ex.Message}");
    }

    private static string? GetDefaultLogFolderPath()
    {
        try
        {
            var configured = SettingsService.Instance?.LogFolderPath;
            if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
                return configured;
        }
        catch { }

        var candidates = new List<string>
        {
            @"C:\Battlestate Games\Escape from Tarkov\Logs",
            @"C:\Battlestate Games\EFT\Logs",
            @"D:\Battlestate Games\Escape from Tarkov\Logs",
            @"D:\Battlestate Games\EFT\Logs",
            @"E:\Battlestate Games\Escape from Tarkov\Logs",
            @"E:\Battlestate Games\EFT\Logs"
        };

        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(localAppData, "Battlestate Games", "EFT", "Logs"));
            candidates.Add(Path.Combine(localAppData, "Battlestate Games", "Escape from Tarkov", "Logs"));
        }
        catch { }

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static long GetActualFileLength(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return fs.Length;
        }
        catch
        {
            return new FileInfo(filePath).Length;
        }
    }

    private void InitialScan(string folderPath)
    {
        _log.Debug($"InitialScan started for: {folderPath}");
        try
        {
            var latestFolder = FindLatestLogSubfolder(folderPath);
            _log.Debug($"Latest log subfolder: {latestFolder}");
            if (latestFolder == null) return;

            var appLogs = Directory.GetFiles(latestFolder, "*application*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => Math.Max(File.GetCreationTimeUtc(f).Ticks, File.GetLastWriteTimeUtc(f).Ticks))
                .ToList();

            if (appLogs.Count > 0)
            {
                var latestAppLog = appLogs[0];
                var length = GetActualFileLength(latestAppLog);
                _filePositions[latestAppLog] = length;
                _log.Debug($"Latest app log: {latestAppLog}, initial pos={length}");
                ScanLogFileForProfile(latestAppLog);
            }

            var netLogs = Directory.GetFiles(latestFolder, "*network-connection*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => Math.Max(File.GetCreationTimeUtc(f).Ticks, File.GetLastWriteTimeUtc(f).Ticks))
                .ToList();

            if (netLogs.Count > 0)
            {
                var latestNetLog = netLogs[0];
                var length = GetActualFileLength(latestNetLog);
                _filePositions[latestNetLog] = length;
                _log.Debug($"Latest network log: {latestNetLog}, initial pos={length}");
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Initial scan failed: {ex.Message}");
        }
    }

    private void OnLogPollTimerTick(object? state)
    {
        if (!_isWatching || _isDisposed) return;
        if (Interlocked.CompareExchange(ref _isPollingLogs, 1, 0) != 0) return;

        try
        {
            var folderPath = _monitoredFolderPath;
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

            var latestFolder = FindLatestLogSubfolder(folderPath);
            if (string.IsNullOrEmpty(latestFolder) || !Directory.Exists(latestFolder)) return;

            var latestAppLog = Directory.GetFiles(latestFolder, "*application*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => Math.Max(File.GetCreationTimeUtc(f).Ticks, File.GetLastWriteTimeUtc(f).Ticks))
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(latestAppLog))
            {
                ProcessLogFileSerialized(latestAppLog, ParseApplicationLogLine);
            }

            var latestNetLog = Directory.GetFiles(latestFolder, "*network-connection*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => Math.Max(File.GetCreationTimeUtc(f).Ticks, File.GetLastWriteTimeUtc(f).Ticks))
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(latestNetLog))
            {
                ProcessLogFileSerialized(latestNetLog, ParseNetworkLogLine);
            }
        }
        catch
        {
        }
        finally
        {
            Interlocked.Exchange(ref _isPollingLogs, 0);
        }
    }

    private string? FindLatestLogSubfolder(string basePath)
    {
        try
        {
            var subdirs = Directory.GetDirectories(basePath)
                .Where(d => SubfolderLogRegex.IsMatch(Path.GetFileName(d)))
                .OrderByDescending(d => Math.Max(Directory.GetCreationTimeUtc(d).Ticks, Directory.GetLastWriteTimeUtc(d).Ticks))
                .ToList();

            return subdirs.Count > 0 ? subdirs[0] : basePath;
        }
        catch
        {
            return basePath;
        }
    }

    private void ScanLogFileForProfile(string filePath)
    {
        _log.Debug($"ScanLogFileForProfile started: {filePath}");
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            string? lastPmcProfileId = null;
            string? lastAccountId = null;
            GameMode lastGameMode = GameMode.Unknown;

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var selectMatch = SelectProfileRegex.Match(line);
                if (selectMatch.Success)
                {
                    lastPmcProfileId = selectMatch.Groups[1].Value;
                    lastAccountId = selectMatch.Groups[2].Value;
                }

                var sessionMatch = SessionModeRegex.Match(line);
                if (sessionMatch.Success)
                {
                    lastGameMode = sessionMatch.Groups[1].Value.ToLowerInvariant() switch
                    {
                        "pve" => GameMode.PVE,
                        "pvp" or "regular" => GameMode.PVP,
                        _ => GameMode.Unknown
                    };
                }
            }

            if (!string.IsNullOrEmpty(lastPmcProfileId))
            {
                EftProfileInfo? updatedProfile = null;
                lock (_stateLock)
                {
                    if (_currentProfile?.PmcProfileId != lastPmcProfileId)
                    {
                        var scavId = CalculateScavProfileId(lastPmcProfileId);
                        _currentProfile = new EftProfileInfo
                        {
                            PmcProfileId = lastPmcProfileId,
                            ScavProfileId = scavId,
                            AccountId = lastAccountId,
                            UpdatedAt = DateTime.Now
                        };
                        updatedProfile = _currentProfile;
                    }
                }

                if (updatedProfile != null)
                {
                    _ = SaveProfileToDbAsync(updatedProfile);
                    RaiseEventSafe(ProfileChanged, new EftProfileEventArgs { ProfileInfo = updatedProfile });
                }
            }

            if (lastGameMode != GameMode.Unknown)
            {
                lock (_stateLock)
                {
                    _currentGameMode = lastGameMode;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to scan log file: {ex.Message}");
        }
    }

    private void OnLogFileCreated(object sender, FileSystemEventArgs e)
    {
        _log.Debug($"[FileWatcher] New log file created: {e.FullPath}");

        // Prevent memory leak if user rotates dozens of files
        if (_filePositions.Count > 64)
        {
            foreach (var key in _filePositions.Keys.Take(32))
            {
                _filePositions.TryRemove(key, out _);
                _fileLocks.TryRemove(key, out _);
            }
        }

        _filePositions[e.FullPath] = 0;
    }

    private void OnApplicationLogChanged(object sender, FileSystemEventArgs e)
    {
        lock (_debounceLock)
        {
            if (!_isWatching || _isDisposed) return;
            _pendingAppLogPath = e.FullPath;
            _appLogDebounceTimer?.Change(150, Timeout.Infinite);
        }
    }

    private void OnNetworkLogChanged(object sender, FileSystemEventArgs e)
    {
        lock (_debounceLock)
        {
            if (!_isWatching || _isDisposed) return;
            _pendingNetLogPath = e.FullPath;
            _netLogDebounceTimer?.Change(150, Timeout.Infinite);
        }
    }

    private void OnAppLogTimerTick(object? state)
    {
        string? path;
        lock (_debounceLock)
        {
            path = _pendingAppLogPath;
        }

        if (!string.IsNullOrEmpty(path))
        {
            ProcessLogFileSerialized(path, ParseApplicationLogLine);
        }
    }

    private void OnNetLogTimerTick(object? state)
    {
        string? path;
        lock (_debounceLock)
        {
            path = _pendingNetLogPath;
        }

        if (!string.IsNullOrEmpty(path))
        {
            ProcessLogFileSerialized(path, ParseNetworkLogLine);
        }
    }

    private void ProcessLogFileSerialized(string filePath, Action<string> lineParser)
    {
        var gate = _fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(0))
        {
            // Another thread is already reading; the trailing timer will catch any remaining bytes
            return;
        }

        try
        {
            ReadCompleteLinesIncremental(filePath, lineParser);
        }
        finally
        {
            gate.Release();
        }
    }

    private void ReadCompleteLinesIncremental(string filePath, Action<string> lineParser)
    {
        const int maxRetries = 3;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                if (!File.Exists(filePath)) return;

                using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    FileOptions.SequentialScan);

                var lastPosition = _filePositions.GetValueOrDefault(filePath, 0L);
                if (stream.Length < lastPosition)
                {
                    // File truncated or recreated by EFT
                    lastPosition = 0L;
                }

                if (stream.Length <= lastPosition)
                    return;

                stream.Seek(lastPosition, SeekOrigin.Begin);
                var bytesToRead = (int)Math.Min(stream.Length - lastPosition, 4 * 1024 * 1024);
                var buffer = ArrayPool<byte>.Shared.Rent(bytesToRead);

                try
                {
                    int totalRead = 0;
                    while (totalRead < bytesToRead)
                    {
                        int read = stream.Read(buffer, totalRead, bytesToRead - totalRead);
                        if (read == 0) break;
                        totalRead += read;
                    }

                    if (totalRead == 0) return;

                    // Consume only up to the last '\n' to prevent truncated lines
                    int lastNewlineIdx = Array.LastIndexOf(buffer, (byte)'\n', totalRead - 1, totalRead);
                    if (lastNewlineIdx < 0)
                    {
                        // No full line written yet by Unity flush
                        return;
                    }

                    int completeBytesLength = lastNewlineIdx + 1;
                    var textChunk = Encoding.UTF8.GetString(buffer, 0, completeBytesLength);

                    // Advance position safely before parsing
                    _filePositions[filePath] = lastPosition + completeBytesLength;

                    foreach (var lineSpan in textChunk.AsSpan().EnumerateLines())
                    {
                        if (!lineSpan.IsEmpty)
                        {
                            lineParser(lineSpan.ToString());
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                return;
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                Thread.Sleep(25 * (attempt + 1));
            }
            catch (Exception ex)
            {
                _log.Warning($"Failed reading log '{filePath}': {ex.Message}");
                return;
            }
        }
    }

    private void ParseApplicationLogLine(string line)
    {
        var timestamp = ExtractTimestamp(line);

        // Init: new game session started - ends previous raid
        if (InitGameRegex.IsMatch(line))
        {
            EftRaidInfo? endedRaid = null;
            lock (_stateLock)
            {
                if (_currentRaid != null && _currentRaid.State != RaidState.Ended)
                {
                    _log.Debug($"[Parse] Init detected - ending current raid (fallback). Previous state: {_currentRaid.State}");
                    _currentRaid.State = RaidState.Ended;
                    _currentRaid.EndTime ??= timestamp;
                    endedRaid = _currentRaid;
                    _currentRaid = null;
                }
            }

            if (endedRaid != null)
            {
                _ = SaveRaidHistoryAsync(endedRaid);
                RaiseEventSafe(RaidEvent, new EftRaidEventArgs
                {
                    EventType = EftRaidEventType.Disconnected,
                    RaidInfo = endedRaid,
                    Timestamp = timestamp,
                    Message = "Raid ended (detected from Init)"
                });
            }
        }

        // Session mode
        var sessionMatch = SessionModeRegex.Match(line);
        if (sessionMatch.Success)
        {
            var detectedMode = sessionMatch.Groups[1].Value.ToLowerInvariant() switch
            {
                "pve" => GameMode.PVE,
                "pvp" or "regular" => GameMode.PVP,
                _ => GameMode.Unknown
            };

            lock (_stateLock)
            {
                _currentGameMode = detectedMode;
            }

            RaiseEventSafe(RaidEvent, new EftRaidEventArgs
            {
                EventType = EftRaidEventType.SessionModeDetected,
                Timestamp = timestamp,
                Message = $"Game mode: {detectedMode}"
            });
        }

        // SelectProfile
        var selectMatch = SelectProfileRegex.Match(line);
        if (selectMatch.Success)
        {
            var profileId = selectMatch.Groups[1].Value;
            var accountId = selectMatch.Groups[2].Value;

            EftProfileInfo? updatedProfile = null;
            lock (_stateLock)
            {
                if (_currentProfile?.PmcProfileId != profileId)
                {
                    var scavId = CalculateScavProfileId(profileId);
                    _currentProfile = new EftProfileInfo
                    {
                        PmcProfileId = profileId,
                        ScavProfileId = scavId,
                        AccountId = accountId,
                        UpdatedAt = DateTime.Now
                    };
                    updatedProfile = _currentProfile;
                }
            }

            if (updatedProfile != null)
            {
                _ = SaveProfileToDbAsync(updatedProfile);
                RaiseEventSafe(ProfileChanged, new EftProfileEventArgs
                {
                    ProfileInfo = updatedProfile,
                    Timestamp = timestamp
                });

                RaiseEventSafe(RaidEvent, new EftRaidEventArgs
                {
                    EventType = EftRaidEventType.ProfileSelected,
                    Timestamp = timestamp
                });
            }
        }

        // Matching with group id
        var matchingMatch = MatchingGroupRegex.Match(line);
        if (matchingMatch.Success)
        {
            string? leaderId;
            lock (_stateLock)
            {
                _pendingGroupId = matchingMatch.Groups[1].Value;
                leaderId = _pendingGroupId;
            }

            RaiseEventSafe(RaidEvent, new EftRaidEventArgs
            {
                EventType = EftRaidEventType.MatchingStarted,
                Timestamp = timestamp,
                Message = string.IsNullOrEmpty(leaderId) ? "Solo" : $"Party (Leader: {leaderId})"
            });
        }

        // Local game matching cancelled
        if (MatchingCancelledRegex.IsMatch(line))
        {
            lock (_stateLock)
            {
                if (_currentRaid != null && _currentRaid.State == RaidState.Matching)
                {
                    _currentRaid.State = RaidState.Ended;
                    _currentRaid = null;
                }
                _pendingGroupId = null;
            }
        }

        // TRACE-NetworkGameCreate
        var traceMatch = TraceNetworkCreateRegex.Match(line);
        if (traceMatch.Success)
        {
            var raidProfileId = traceMatch.Groups[1].Value;
            var mapName = traceMatch.Groups[6].Value;
            var mapKey = MapNameMapping.TryGetValue(mapName, out var key) ? key : mapName;

            EftRaidInfo createdRaid;
            lock (_stateLock)
            {
                var raidType = _currentProfile?.GetRaidType(raidProfileId) ?? RaidType.Unknown;
                createdRaid = new EftRaidInfo
                {
                    ProfileId = raidProfileId,
                    RaidType = raidType,
                    GameMode = _currentGameMode,
                    MapName = mapName,
                    MapKey = mapKey,
                    ServerIp = traceMatch.Groups[4].Value,
                    ServerPort = int.TryParse(traceMatch.Groups[5].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ? port : 0,
                    SessionId = traceMatch.Groups[7].Value,
                    ShortId = traceMatch.Groups[9].Value,
                    IsParty = !string.IsNullOrEmpty(_pendingGroupId),
                    PartyLeaderAccountId = string.IsNullOrEmpty(_pendingGroupId) ? null : _pendingGroupId,
                    State = RaidState.Matching
                };
                _currentRaid = createdRaid;
            }

            _log.Info($"[RaidStarted] Map={mapKey}, RaidType={createdRaid.RaidType}, GameMode={createdRaid.GameMode}");
            RaiseEventSafe(RaidEvent, new EftRaidEventArgs
            {
                EventType = EftRaidEventType.RaidStarted,
                RaidInfo = createdRaid,
                Timestamp = timestamp
            });
        }

        // scene preset (Map Loading)
        var sceneMatch = ScenePresetRegex.Match(line);
        if (sceneMatch.Success)
        {
            var bundleName = sceneMatch.Groups[1].Value;
            var mapKey = MapNameMapping.TryGetValue(bundleName, out var key) ? key : bundleName;

            EftRaidInfo currentOrNewRaid;
            bool wasCreatedAsFallback = false;

            lock (_stateLock)
            {
                if (_currentRaid == null || _currentRaid.State == RaidState.Ended)
                {
                    currentOrNewRaid = new EftRaidInfo
                    {
                        ProfileId = _currentProfile?.PmcProfileId,
                        RaidType = RaidType.Unknown,
                        GameMode = _currentGameMode,
                        MapName = bundleName,
                        MapKey = mapKey,
                        IsParty = !string.IsNullOrEmpty(_pendingGroupId),
                        PartyLeaderAccountId = string.IsNullOrEmpty(_pendingGroupId) ? null : _pendingGroupId,
                        State = RaidState.Matching
                    };
                    _currentRaid = currentOrNewRaid;
                    wasCreatedAsFallback = true;
                }
                else
                {
                    _currentRaid.MapKey = mapKey;
                    currentOrNewRaid = _currentRaid;
                }
            }

            if (wasCreatedAsFallback)
            {
                RaiseEventSafe(RaidEvent, new EftRaidEventArgs
                {
                    EventType = EftRaidEventType.RaidStarted,
                    RaidInfo = currentOrNewRaid,
                    Timestamp = timestamp,
                    Message = $"Raid started (fallback): {mapKey}"
                });
            }

            RaiseEventSafe(RaidEvent, new EftRaidEventArgs
            {
                EventType = EftRaidEventType.MapLoadingStarted,
                RaidInfo = currentOrNewRaid,
                Timestamp = timestamp,
                Message = $"Loading map: {mapKey}"
            });
        }

        // [Transit]
        var transitMatch = TransitRegex.Match(line);
        if (transitMatch.Success)
        {
            var raidId = transitMatch.Groups[2].Value;
            var locations = transitMatch.Groups[4].Value;

            var mapFromTransit = locations.Split(new[] { ' ', '-', '>' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var mapKey = mapFromTransit != null && MapNameMapping.TryGetValue(mapFromTransit, out var key)
                ? key
                : mapFromTransit;

            EftRaidInfo? fallbackRaid = null;
            lock (_stateLock)
            {
                if (_currentRaid == null || _currentRaid.State == RaidState.Ended)
                {
                    if (!string.IsNullOrEmpty(mapKey))
                    {
                        fallbackRaid = new EftRaidInfo
                        {
                            RaidId = raidId,
                            ProfileId = _currentProfile?.PmcProfileId,
                            RaidType = RaidType.Unknown,
                            GameMode = _currentGameMode,
                            MapName = mapFromTransit,
                            MapKey = mapKey,
                            IsParty = !string.IsNullOrEmpty(_pendingGroupId),
                            PartyLeaderAccountId = string.IsNullOrEmpty(_pendingGroupId) ? null : _pendingGroupId,
                            State = RaidState.Matching
                        };
                        _currentRaid = fallbackRaid;
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(_currentRaid.RaidId))
                        _currentRaid.RaidId = raidId;
                    if (!string.IsNullOrEmpty(mapKey) && string.IsNullOrEmpty(_currentRaid.MapKey))
                        _currentRaid.MapKey = mapKey;
                }
            }

            if (fallbackRaid != null)
            {
                RaiseEventSafe(RaidEvent, new EftRaidEventArgs
                {
                    EventType = EftRaidEventType.RaidStarted,
                    RaidInfo = fallbackRaid,
                    Timestamp = timestamp,
                    Message = $"Raid started (transit fallback): {mapKey}"
                });
            }
        }

        // [Transit] End
        var transitEndMatch = TransitEndRegex.Match(line);
        if (transitEndMatch.Success)
        {
            EftRaidInfo? endedRaid = null;
            lock (_stateLock)
            {
                if (_currentRaid != null)
                {
                    _currentRaid.State = RaidState.Ended;
                    _currentRaid.EndTime = timestamp;
                    endedRaid = _currentRaid;
                }
            }

            if (endedRaid != null)
            {
                _ = SaveRaidHistoryAsync(endedRaid);
                RaiseEventSafe(RaidEvent, new EftRaidEventArgs
                {
                    EventType = EftRaidEventType.RaidEnded,
                    RaidInfo = endedRaid,
                    Timestamp = timestamp
                });
            }
        }
    }

    private void ParseNetworkLogLine(string line)
    {
        var timestamp = ExtractTimestamp(line);

        // Connect
        var connectMatch = ConnectRegex.Match(line);
        if (connectMatch.Success)
        {
            var ip = connectMatch.Groups[2].Value;
            var port = int.TryParse(connectMatch.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0;

            EftRaidInfo? raidSnapshot;
            lock (_stateLock)
            {
                if (_currentRaid != null)
                {
                    _currentRaid.ServerIp = ip;
                    _currentRaid.ServerPort = port;
                    _currentRaid.State = RaidState.Connecting;
                }
                raidSnapshot = _currentRaid;
            }

            RaiseEventSafe(RaidEvent, new EftRaidEventArgs
            {
                EventType = EftRaidEventType.Connecting,
                RaidInfo = raidSnapshot,
                Timestamp = timestamp,
                Message = $"Connecting to {ip}:{port}"
            });
        }

        // Connected state
        var connectedMatch = ConnectedStateRegex.Match(line);
        if (connectedMatch.Success)
        {
            EftRaidInfo? raidSnapshot;
            lock (_stateLock)
            {
                if (_currentRaid != null)
                {
                    _currentRaid.State = RaidState.InRaid;
                    _currentRaid.StartTime = timestamp;
                }
                raidSnapshot = _currentRaid;
            }

            RaiseEventSafe(RaidEvent, new EftRaidEventArgs
            {
                EventType = EftRaidEventType.Connected,
                RaidInfo = raidSnapshot,
                Timestamp = timestamp
            });
        }

        // Disconnect
        var disconnectMatch = DisconnectRegex.Match(line);
        if (disconnectMatch.Success)
        {
            EftRaidInfo? endedRaid = null;
            lock (_stateLock)
            {
                if (_currentRaid != null)
                {
                    _currentRaid.State = RaidState.Ended;
                    _currentRaid.EndTime ??= timestamp;
                    endedRaid = _currentRaid;
                }
            }

            if (endedRaid != null)
            {
                _ = SaveRaidHistoryAsync(endedRaid);
                RaiseEventSafe(RaidEvent, new EftRaidEventArgs
                {
                    EventType = EftRaidEventType.Disconnected,
                    RaidInfo = endedRaid,
                    Timestamp = timestamp
                });
            }
        }

        // Statistics
        var statsMatch = StatisticsRegex.Match(line);
        if (statsMatch.Success)
        {
            var rtt = double.TryParse(statsMatch.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : (double?)null;
            var loss = double.TryParse(statsMatch.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var l) ? l : (double?)null;
            var sent = long.TryParse(statsMatch.Groups[5].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : (long?)null;
            var recv = long.TryParse(statsMatch.Groups[6].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rv) ? rv : (long?)null;

            lock (_stateLock)
            {
                if (_currentRaid != null)
                {
                    _currentRaid.Rtt = rtt;
                    _currentRaid.PacketLoss = loss;
                    _currentRaid.PacketsSent = sent;
                    _currentRaid.PacketsReceived = recv;
                }
            }
        }

        // Timeout
        var timeoutMatch = TimeoutRegex.Match(line);
        if (timeoutMatch.Success)
        {
            var timeoutType = timeoutMatch.Groups[1].Value;
            var timeoutMs = timeoutMatch.Groups[2].Value;

            EftRaidInfo? raidSnapshot;
            lock (_stateLock)
            {
                raidSnapshot = _currentRaid;
            }

            RaiseEventSafe(RaidEvent, new EftRaidEventArgs
            {
                EventType = EftRaidEventType.NetworkTimeout,
                RaidInfo = raidSnapshot,
                Timestamp = timestamp,
                Message = $"{timeoutType} timeout after {timeoutMs}ms"
            });
        }
    }

    private static DateTime ExtractTimestamp(string line)
    {
        if (line.Length >= 23 &&
            DateTime.TryParseExact(
                line.AsSpan(0, 23),
                "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dt))
        {
            return dt;
        }

        var match = TimestampRegex.Match(line);
        if (match.Success && DateTime.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallbackDt))
        {
            return fallbackDt;
        }

        return DateTime.Now;
    }

    private static string CalculateScavProfileId(string pmcProfileId)
    {
        if (string.IsNullOrEmpty(pmcProfileId)) return "";

        var baseId = pmcProfileId[..^1];
        var lastChar = pmcProfileId[^1];

        try
        {
            var hex = Convert.ToInt32(lastChar.ToString(), 16);
            var nextHex = (hex + 1) % 16;
            return baseId + nextHex.ToString("x");
        }
        catch
        {
            return pmcProfileId;
        }
    }

    private void RaiseEventSafe<TArgs>(EventHandler<TArgs>? eventHandler, TArgs args)
    {
        if (eventHandler == null) return;

        foreach (var singleDelegate in eventHandler.GetInvocationList())
        {
            try
            {
                ((EventHandler<TArgs>)singleDelegate).Invoke(this, args);
            }
            catch (Exception ex)
            {
                _log.Error($"Subscriber '{singleDelegate.Method.DeclaringType?.Name}.{singleDelegate.Method.Name}' threw unhandled exception: {ex.Message}");
            }
        }
    }

    #endregion

    #region Database Operations

    private async Task LoadProfileFromDbAsync()
    {
        try
        {
            var dbService = UserDataDbService.Instance;
            var pmcId = await dbService.GetSettingAsync("eft.pmcProfileId").ConfigureAwait(false);
            var scavId = await dbService.GetSettingAsync("eft.scavProfileId").ConfigureAwait(false);
            var accountId = await dbService.GetSettingAsync("eft.accountId").ConfigureAwait(false);

            if (!string.IsNullOrEmpty(pmcId))
            {
                var calculatedScav = scavId ?? CalculateScavProfileId(pmcId);
                lock (_stateLock)
                {
                    _currentProfile = new EftProfileInfo
                    {
                        PmcProfileId = pmcId,
                        ScavProfileId = calculatedScav,
                        AccountId = accountId,
                        UpdatedAt = DateTime.Now
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"[DB] Failed to load profile from DB: {ex.Message}");
        }
    }

    private async Task SaveProfileToDbAsync(EftProfileInfo profile)
    {
        try
        {
            var dbService = UserDataDbService.Instance;
            if (!string.IsNullOrEmpty(profile.PmcProfileId))
            {
                await dbService.SetSettingAsync("eft.pmcProfileId", profile.PmcProfileId).ConfigureAwait(false);
            }
            if (!string.IsNullOrEmpty(profile.ScavProfileId))
            {
                await dbService.SetSettingAsync("eft.scavProfileId", profile.ScavProfileId).ConfigureAwait(false);
            }
            if (!string.IsNullOrEmpty(profile.AccountId))
            {
                await dbService.SetSettingAsync("eft.accountId", profile.AccountId).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to save profile to DB: {ex.Message}");
        }
    }

    private async Task SaveRaidHistoryAsync(EftRaidInfo raid)
    {
        try
        {
            var dbService = UserDataDbService.Instance;
            await dbService.SaveRaidHistoryAsync(raid).ConfigureAwait(false);
            _log.Debug($"Saved raid history: {raid.MapKey} ({raid.RaidType})");
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to save raid history: {ex.Message}");
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        StopMonitoring();

        lock (_debounceLock)
        {
            _appLogDebounceTimer?.Dispose();
            _appLogDebounceTimer = null;
            _netLogDebounceTimer?.Dispose();
            _netLogDebounceTimer = null;
        }
    }

    #endregion
}
