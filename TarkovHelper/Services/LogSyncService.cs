using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TarkovHelper.Models;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Service for synchronizing quest progress from Tarkov game logs
    /// </summary>
    public class LogSyncService : IDisposable
    {
        private static readonly ILogger _log = Log.For<LogSyncService>();
        private static readonly Lazy<LogSyncService> _lazyInstance =
            new(() => new LogSyncService(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static LogSyncService Instance => _lazyInstance.Value;

        [System.Diagnostics.Conditional("DEBUG")]
        private static void DebugLog(string message)
        {
            _log.Debug(message);
        }

        private FileSystemWatcher? _logWatcher;
        private readonly object _watcherLock = new();
        private readonly HashSet<string> _processedEventKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _processedEventOrder = new();
        private const int MaxProcessedKeys = 4096;
        private readonly ConcurrentDictionary<string, long> _pushLogOffsets = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _pushProcessGate = new(1, 1);

        private volatile bool _isWatching;
        private volatile bool _isDisposed;
        private string? _currentMapKey;
        private CancellationTokenSource? _monitoringCts;

        // Trailing debounce timer for push notification logs to prevent dropped events
        private Timer? _pushLogDebounceTimer;
        private Timer? _pushLogPollTimer;
        private readonly object _pushDebounceLock = new();
        private string? _pendingPushLogFile;
        private string? _monitoredLogFolderPath;
        private string? _activePushLogFile;
        private DateTime _lastActiveLogScanUtc = DateTime.MinValue;
        private string? _pendingLiveLogFile;
        private int _reprocessRequested;

        /// <summary>
        /// Event fired when a quest event is detected from logs
        /// </summary>
        public event EventHandler<QuestLogEvent>? QuestEventDetected;

        /// <summary>
        /// Event fired when a map change is detected from logs
        /// </summary>
        public event EventHandler<MapDetectedEventArgs>? MapDetected;

        /// <summary>
        /// Event fired when log monitoring status changes
        /// </summary>
        public event EventHandler<bool>? MonitoringStatusChanged;

        /// <summary>
        /// Whether log monitoring is currently active
        /// </summary>
        public bool IsMonitoring => _isWatching;

        /// <summary>
        /// Currently detected map key
        /// </summary>
        public string? CurrentMapKey => _currentMapKey;

        // Message type codes from logs
        private const int MSG_TYPE_STARTED = 10;
        private const int MSG_TYPE_FAILED = 11;
        private const int MSG_TYPE_COMPLETED = 12;

        private static readonly TimeSpan RegexSafetyTimeout = TimeSpan.FromMilliseconds(250);

        private static readonly Regex TransitMapRegex = new(
            @"\[Transit\][^\r\n]*?Locations:([a-zA-Z0-9_]+)\s*->",
            RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexSafetyTimeout);

        private static readonly Regex ScenePresetMapRegex = new(
            @"scene preset path:maps/([a-zA-Z0-9_]+)\.bundle",
            RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexSafetyTimeout);

        // Map name to key mapping (EFT log name -> map_configs.json key)
        private static readonly Dictionary<string, string> MapNameToKey = new(StringComparer.OrdinalIgnoreCase)
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

        private LogSyncService()
        {
            // Delegate map change detection to dedicated LogMapWatcherService
            TarkovHelper.Services.Map.LogMapWatcherService.Instance.MapChanged += OnExternalMapChanged;
        }

        private void OnExternalMapChanged(object? sender, TarkovHelper.Services.Map.MapChangedEventArgs e)
        {
            if (!_isWatching || _isDisposed) return;
            _currentMapKey = e.NewMapKey;
            RaiseEventSafe(MapDetected, new MapDetectedEventArgs(e.NewMapKey, DateTime.Now));
        }

        #region Log File Monitoring

        /// <summary>
        /// Start monitoring log folder for quest events and map detection
        /// </summary>
        public void StartMonitoring(string logFolderPath)
        {
            lock (_watcherLock)
            {
                StopMonitoring();

                DebugLog($"StartMonitoring called with path: {logFolderPath}");

                if (string.IsNullOrEmpty(logFolderPath) || !Directory.Exists(logFolderPath))
                {
                    DebugLog($"Invalid path or directory does not exist");
                    return;
                }

                try
                {
                    _monitoredLogFolderPath = logFolderPath;
                    _monitoringCts = new CancellationTokenSource();
                    var ct = _monitoringCts.Token;

                    _pushLogDebounceTimer = new Timer(OnPushDebounceTick, ct, Timeout.Infinite, Timeout.Infinite);

                    // Quest event watcher (push-notifications logs)
                    _logWatcher = new FileSystemWatcher(logFolderPath)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                        Filter = "*push-notifications*.log",
                        IncludeSubdirectories = true,
                        InternalBufferSize = 65536,
                        EnableRaisingEvents = false
                    };

                    _logWatcher.Changed += OnLogFileChanged;
                    _logWatcher.Created += OnLogFileChanged;
                    _logWatcher.Error += OnWatcherError;

                    _isWatching = true;

                    // Seed initial events before enabling live watcher notifications + NTFS open-handle fallback poller
                    _ = Task.Run(async () =>
                    {
                        await SeedLatestPushNotificationEventsAsync(logFolderPath, ct).ConfigureAwait(false);
                        lock (_watcherLock)
                        {
                            if (!ct.IsCancellationRequested && _logWatcher != null && _isWatching)
                            {
                                _logWatcher.EnableRaisingEvents = true;
                                _pushLogPollTimer?.Dispose();
                                _pushLogPollTimer = new Timer(OnPushPollTick, null, 2500, 2500);
                            }
                        }
                    }, ct);

                    RaiseEventSafe(MonitoringStatusChanged, true);
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed starting push log watcher: {ex.Message}");
                    _isWatching = false;
                    RaiseEventSafe(MonitoringStatusChanged, false);
                }
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            var ex = e.GetException();
            _log.Warning($"Push log FileSystemWatcher buffer overflow or error: {ex.Message}");
        }

        private string? ResolveLatestPushLogFile(string logFolderPath)
        {
            try
            {
                if (string.IsNullOrEmpty(logFolderPath) || !Directory.Exists(logFolderPath))
                    return null;

                return Directory.EnumerateFiles(logFolderPath, "*push-notifications*.log", SearchOption.AllDirectories)
                    .Select(f =>
                    {
                        DateTime created = DateTime.MinValue;
                        DateTime written = DateTime.MinValue;
                        try { created = File.GetCreationTimeUtc(f); } catch { }
                        try { written = File.GetLastWriteTimeUtc(f); } catch { }
                        return (Path: f, SortKey: created > written ? created : written);
                    })
                    .OrderByDescending(x => x.SortKey)
                    .Select(x => x.Path)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private void OnPushPollTick(object? state)
        {
            if (!_isWatching || _isDisposed) return;
            var folder = _monitoredLogFolderPath;
            if (string.IsNullOrEmpty(folder)) return;

            try
            {
                if (_activePushLogFile == null ||
                    !File.Exists(_activePushLogFile) ||
                    (DateTime.UtcNow - _lastActiveLogScanUtc).TotalSeconds >= 10)
                {
                    _activePushLogFile = ResolveLatestPushLogFile(folder);
                    _lastActiveLogScanUtc = DateTime.UtcNow;
                }

                var targetFile = _activePushLogFile;
                if (string.IsNullOrEmpty(targetFile) || !File.Exists(targetFile))
                    return;

                long actualLength;
                using (var fs = new FileStream(targetFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    actualLength = fs.Length;
                }

                long lastOffset = _pushLogOffsets.GetValueOrDefault(targetFile, 0L);
                if (actualLength != lastOffset)
                {
                    _ = ProcessLatestLogEvents(targetFile);
                }
            }
            catch (Exception ex)
            {
                DebugLog($"Poll tick error: {ex.Message}");
            }
        }

        private async Task SeedLatestPushNotificationEventsAsync(string logFolderPath, CancellationToken ct)
        {
            try
            {
                var latestLog = ResolveLatestPushLogFile(logFolderPath);

                if (latestLog != null && File.Exists(latestLog))
                {
                    _activePushLogFile = latestLog;
                    _lastActiveLogScanUtc = DateTime.UtcNow;

                    long streamLength;
                    string content;
                    using (var stream = new FileStream(latestLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        content = await reader.ReadToEndAsync().ConfigureAwait(false);
                        streamLength = stream.Position;
                    }

                    int safeLen = FindSafeCompleteJsonBoundary(content);
                    if (safeLen < content.Length)
                    {
                        var trailingBytes = Encoding.UTF8.GetByteCount(content.AsSpan(safeLen));
                        streamLength = Math.Max(0L, streamLength - trailingBytes);
                        content = content.Substring(0, safeLen);
                    }

                    _pushLogOffsets[latestLog] = streamLength;

                    var initialEvents = ParseLogContent(content, Path.GetFileName(latestLog));
                    var progressService = QuestProgressService.Instance;
                    var lookup = BuildQuestIdLookup(progressService.AllTasks);

                    foreach (var evt in initialEvents)
                    {
                        if (ct.IsCancellationRequested) return;

                        var eventKey = $"{evt.QuestId}_{evt.EventType}_{evt.Timestamp.Ticks}";
                        bool isNew;
                        lock (_processedEventKeys)
                        {
                            isNew = _processedEventKeys.Add(eventKey);
                            if (isNew)
                            {
                                _processedEventOrder.Enqueue(eventKey);
                                while (_processedEventOrder.Count > MaxProcessedKeys)
                                {
                                    var oldest = _processedEventOrder.Dequeue();
                                    _processedEventKeys.Remove(oldest);
                                }
                            }
                        }

                        if (isNew &&
                            (evt.EventType == QuestEventType.Completed || evt.EventType == QuestEventType.Failed) &&
                            Math.Abs((DateTime.Now - evt.Timestamp).TotalHours) < 24)
                        {
                            var task = FindTaskByQuestId(lookup, evt.QuestId ?? string.Empty);
                            if (task == null)
                            {
                                var cleanId = evt.QuestId?.Trim() ?? "";
                                if (cleanId.Contains(' ')) cleanId = cleanId.Split(' ')[0];
                                task = progressService.AllTasks.FirstOrDefault(t =>
                                    string.Equals(t.BsgId, cleanId, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(t.Id, cleanId, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(t.NormalizedName, cleanId, StringComparison.OrdinalIgnoreCase) ||
                                    (t.Ids != null && t.Ids.Contains(cleanId, StringComparer.OrdinalIgnoreCase)));
                            }

                            if (task != null)
                            {
                                var status = progressService.GetStatus(task);
                                if ((evt.EventType == QuestEventType.Completed && status != QuestStatus.Done) ||
                                    (evt.EventType == QuestEventType.Failed && status != QuestStatus.Failed))
                                {
                                    _log.Info($"Startup auto-sync: Catching missed quest event {evt.EventType}: {task.Name} ({evt.QuestId})");
                                    RaiseEventSafe(QuestEventDetected, evt);
                                }
                            }
                        }
                    }
                    DebugLog($"Initialized {_processedEventKeys.Count} existing push-notification events as seen");
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Error initializing push-notifications events: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop monitoring log folder
        /// </summary>
        public void StopMonitoring()
        {
            lock (_watcherLock)
            {
                _monitoringCts?.Cancel();
                _monitoringCts?.Dispose();
                _monitoringCts = null;

                if (_logWatcher != null)
                {
                    _logWatcher.EnableRaisingEvents = false;
                    _logWatcher.Changed -= OnLogFileChanged;
                    _logWatcher.Created -= OnLogFileChanged;
                    _logWatcher.Error -= OnWatcherError;
                    _logWatcher.Dispose();
                    _logWatcher = null;
                }

                _currentMapKey = null;

                lock (_processedEventKeys)
                {
                    _processedEventKeys.Clear();
                    _processedEventOrder.Clear();
                }

                _pushLogOffsets.Clear();

                _pushLogPollTimer?.Dispose();
                _pushLogPollTimer = null;
                _monitoredLogFolderPath = null;
                _activePushLogFile = null;
                _pendingLiveLogFile = null;
                Interlocked.Exchange(ref _reprocessRequested, 0);

                lock (_pushDebounceLock)
                {
                    _pushLogDebounceTimer?.Dispose();
                    _pushLogDebounceTimer = null;
                    _pendingPushLogFile = null;
                }

                _isWatching = false;
                RaiseEventSafe(MonitoringStatusChanged, false);
            }
        }

        private void OnLogFileChanged(object sender, FileSystemEventArgs e)
        {
            lock (_pushDebounceLock)
            {
                if (!_isWatching || _isDisposed) return;
                _pendingPushLogFile = e.FullPath;
                _pushLogDebounceTimer?.Change(300, Timeout.Infinite);
            }
        }

        private void OnPushDebounceTick(object? state)
        {
            string? fileToProcess;
            lock (_pushDebounceLock)
            {
                fileToProcess = _pendingPushLogFile;
            }

            if (!string.IsNullOrEmpty(fileToProcess) && _isWatching && !_isDisposed)
            {
                _ = ProcessLatestLogEvents(fileToProcess);
            }
        }

        /// <summary>
        /// Parse map name from log content using multiple detection patterns (returns latest match in content)
        /// </summary>
        public static string? ParseMapFromLogContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;

            try
            {
                var transitMatches = TransitMapRegex.Matches(content);
                for (int i = transitMatches.Count - 1; i >= 0; i--)
                {
                    var mapName = transitMatches[i].Groups[1].Value;
                    if (TryGetMapKey(mapName, out var mapKey))
                    {
                        return mapKey;
                    }
                }

                var sceneMatches = ScenePresetMapRegex.Matches(content);
                for (int i = sceneMatches.Count - 1; i >= 0; i--)
                {
                    var mapName = sceneMatches[i].Groups[1].Value;
                    if (TryGetMapKey(mapName, out var mapKey))
                    {
                        return mapKey;
                    }
                }
            }
            catch (RegexMatchTimeoutException ex)
            {
                _log.Warning($"Regex timeout parsing map content: {ex.Message}");
            }

            return null;
        }

        public static bool TryGetMapKey(string mapName, out string mapKey)
        {
            return MapNameToKey.TryGetValue(mapName, out mapKey!);
        }

        /// <summary>
        /// Find the last map from application logs (for initial map selection)
        /// </summary>
        /// <param name="logFolderPath">Log folder path</param>
        /// <returns>Last detected map key, or null if not found</returns>
        public string? FindLastMapFromLogs(string? logFolderPath = null)
        {
            var path = logFolderPath ?? SettingsService.Instance.LogFolderPath;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return null;

            try
            {
                var logFiles = Directory.GetFiles(path, "*application*.log", SearchOption.AllDirectories)
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .Take(3)
                    .ToList();

                if (logFiles.Count == 0)
                    return null;

                foreach (var logFile in logFiles)
                {
                    var mapKey = FindLastMapInFile(logFile);
                    if (!string.IsNullOrEmpty(mapKey))
                    {
                        _log.Info($"Found last map from logs: {mapKey}");
                        return mapKey;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Error finding last map: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Find the last map mentioned in a single log file (reads from end)
        /// </summary>
        private string? FindLastMapInFile(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                var readSize = Math.Min(stream.Length, 200 * 1024);
                if (readSize <= 0) return null;

                stream.Seek(-readSize, SeekOrigin.End);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                // Discard potentially split partial UTF-8 line at boundary
                if (stream.Position > 0)
                {
                    reader.ReadLine();
                }

                var content = reader.ReadToEnd();
                return ParseMapFromLogContent(content);
            }
            catch (Exception ex)
            {
                _log.Error($"Error reading log file {filePath}: {ex.Message}");
                return null;
            }
        }

        private async Task ProcessLatestLogEvents(string filePath)
        {
            Volatile.Write(ref _pendingLiveLogFile, filePath);
            Interlocked.Exchange(ref _reprocessRequested, 1);

            if (!await _pushProcessGate.WaitAsync(0).ConfigureAwait(false))
                return;

            try
            {
                while (Interlocked.Exchange(ref _reprocessRequested, 0) == 1)
                {
                    if (!_isWatching || _isDisposed) return;

                    var targetPath = Volatile.Read(ref _pendingLiveLogFile) ?? filePath;
                    await Task.Delay(100).ConfigureAwait(false);
                    if (!File.Exists(targetPath)) continue;

                    long lastOffset = _pushLogOffsets.GetValueOrDefault(targetPath, 0L);
                    var (events, newOffset) = await ParsePushLogIncrementalAsync(targetPath, lastOffset).ConfigureAwait(false);
                    _pushLogOffsets[targetPath] = newOffset;

                    foreach (var evt in events)
                    {
                        var eventKey = $"{evt.QuestId}_{evt.EventType}_{evt.Timestamp.Ticks}";

                        lock (_processedEventKeys)
                        {
                            if (!_processedEventKeys.Add(eventKey))
                                continue;

                            _processedEventOrder.Enqueue(eventKey);
                            while (_processedEventOrder.Count > MaxProcessedKeys)
                            {
                                var oldest = _processedEventOrder.Dequeue();
                                _processedEventKeys.Remove(oldest);
                            }
                        }

                        if (Math.Abs((DateTime.Now - evt.Timestamp).TotalHours) < 24)
                        {
                            _log.Info($"Live quest event detected: {evt.QuestId} ({evt.EventType}) at {evt.Timestamp}");
                            RaiseEventSafe(QuestEventDetected, evt);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Error in ProcessLatestLogEvents: {ex.Message}");
            }
            finally
            {
                _pushProcessGate.Release();
            }
        }

        private async Task<(List<QuestLogEvent> Events, long NewOffset)> ParsePushLogIncrementalAsync(string filePath, long lastOffset)
        {
            var events = new List<QuestLogEvent>();
            long currentOffset = lastOffset;

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length < currentOffset)
                {
                    currentOffset = 0L;
                }

                if (stream.Length <= currentOffset)
                {
                    return (events, stream.Length);
                }

                stream.Seek(currentOffset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var content = await reader.ReadToEndAsync().ConfigureAwait(false);
                long endStreamPos = stream.Position;

                int safeParseLength = FindSafeCompleteJsonBoundary(content);
                if (safeParseLength < content.Length)
                {
                    var trailingBytes = Encoding.UTF8.GetByteCount(content.AsSpan(safeParseLength));
                    currentOffset = Math.Max(currentOffset, endStreamPos - trailingBytes);
                    content = content.Substring(0, safeParseLength);
                }
                else
                {
                    currentOffset = endStreamPos;
                }

                var fileName = Path.GetFileName(filePath);
                events = ParseLogContent(content, fileName);
            }
            catch (Exception ex)
            {
                _log.Warning($"Incremental read failed for '{filePath}': {ex.Message}");
            }

            return (events, currentOffset);
        }

        private static int FindSafeCompleteJsonBoundary(string content)
        {
            int i = 0;
            while (i < content.Length)
            {
                if (content[i] == '{' && (i == 0 || content[i - 1] == '\n' || content[i - 1] == ' ' || content[i - 1] == '\t' || content[i - 1] == '\r'))
                {
                    int start = i;
                    int depth = 0;
                    bool inString = false;
                    bool escaped = false;
                    bool closed = false;
                    const int maxChars = 256 * 1024;

                    for (int j = i; j < content.Length && (j - start) < maxChars; j++)
                    {
                        char c = content[j];
                        if (inString)
                        {
                            if (escaped) escaped = false;
                            else if (c == '\\') escaped = true;
                            else if (c == '"') inString = false;
                        }
                        else
                        {
                            if (c == '"') inString = true;
                            else if (c == '{') depth++;
                            else if (c == '}')
                            {
                                depth--;
                                if (depth == 0)
                                {
                                    i = j + 1;
                                    closed = true;
                                    break;
                                }
                                if (depth < 0) break;
                            }
                        }
                    }

                    if (closed) continue;
                    if ((content.Length - start) < maxChars)
                    {
                        return start;
                    }
                }

                int nextNl = content.IndexOf('\n', i);
                if (nextNl < 0) break;
                i = nextNl + 1;
            }

            return content.Length;
        }

        #endregion

        #region Log Parsing

        /// <summary>
        /// Parse all log files in a directory for quest events
        /// </summary>
        public async Task<List<QuestLogEvent>> ParseLogDirectoryAsync(string logFolderPath, IProgress<string>? progress = null)
        {
            var allEvents = new List<QuestLogEvent>();

            if (!Directory.Exists(logFolderPath))
                return allEvents;

            var logFiles = Directory.GetFiles(logFolderPath, "*push-notifications*.log", SearchOption.AllDirectories)
                .OrderBy(f => File.GetLastWriteTime(f))
                .ToList();

            progress?.Report($"Found {logFiles.Count} log files");

            int processed = 0;
            foreach (var file in logFiles)
            {
                try
                {
                    var events = await ParseLogFileAsync(file).ConfigureAwait(false);
                    allEvents.AddRange(events);

                    processed++;
                    progress?.Report($"Analyzing {processed}/{logFiles.Count} files ({allEvents.Count} events)...");
                }
                catch
                {
                    // Skip files that can't be read
                }
            }

            allEvents = allEvents.OrderBy(e => e.Timestamp).ToList();
            return allEvents;
        }

        /// <summary>
        /// Parse a single log file for quest events synchronously
        /// </summary>
        public List<QuestLogEvent> ParseLogFile(string filePath, bool tailOnly = false)
        {
            var events = new List<QuestLogEvent>();

            if (!File.Exists(filePath))
                return events;

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                var fileName = Path.GetFileName(filePath);

                if (tailOnly && stream.Length > 50000)
                {
                    stream.Seek(-50000, SeekOrigin.End);
                    reader.ReadLine();
                }

                var content = reader.ReadToEnd();
                var parsedEvents = ParseLogContent(content, fileName);
                events.AddRange(parsedEvents);
            }
            catch
            {
            }

            return events;
        }

        /// <summary>
        /// Parse a single log file for quest events
        /// </summary>
        public async Task<List<QuestLogEvent>> ParseLogFileAsync(string filePath, bool tailOnly = false)
        {
            var events = new List<QuestLogEvent>();

            if (!File.Exists(filePath))
                return events;

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                var fileName = Path.GetFileName(filePath);

                if (tailOnly && stream.Length > 50000)
                {
                    stream.Seek(-50000, SeekOrigin.End);
                    reader.ReadLine();
                }

                var content = await reader.ReadToEndAsync().ConfigureAwait(false);
                var parsedEvents = ParseLogContent(content, fileName);
                events.AddRange(parsedEvents);
            }
            catch
            {
            }

            return events;
        }

        /// <summary>
        /// Robust parser for log content with nested/multiline JSON support immune to braces inside strings
        /// </summary>
        private List<QuestLogEvent> ParseLogContent(string content, string? sourceFile)
        {
            var events = new List<QuestLogEvent>();
            if (string.IsNullOrEmpty(content)) return events;

            int i = 0;
            while (i < content.Length)
            {
                if (content[i] == '{' && (i == 0 || content[i - 1] == '\n' || content[i - 1] == ' ' || content[i - 1] == '\t' || content[i - 1] == '\r'))
                {
                    int start = i;
                    int depth = 0;
                    bool inString = false;
                    bool escaped = false;
                    bool completed = false;
                    const int maxChars = 256 * 1024;

                    for (int j = i; j < content.Length && (j - start) < maxChars; j++)
                    {
                        char c = content[j];
                        if (inString)
                        {
                            if (escaped) escaped = false;
                            else if (c == '\\') escaped = true;
                            else if (c == '"') inString = false;
                        }
                        else
                        {
                            if (c == '"') inString = true;
                            else if (c == '{') depth++;
                            else if (c == '}')
                            {
                                depth--;
                                if (depth == 0)
                                {
                                    var jsonSlice = content.Substring(start, j - start + 1);
                                    var evt = ParseJsonBlock(jsonSlice, sourceFile);
                                    if (evt != null) events.Add(evt);

                                    i = j + 1;
                                    completed = true;
                                    break;
                                }
                                if (depth < 0) break;
                            }
                        }
                    }

                    if (completed) continue;
                }

                int nextNl = content.IndexOf('\n', i);
                if (nextNl < 0) break;
                i = nextNl + 1;
            }

            return events;
        }

        /// <summary>
        /// Parse a JSON block for quest event
        /// </summary>
        private QuestLogEvent? ParseJsonBlock(string jsonString, string? sourceFile)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeElement) ||
                    typeElement.GetString() != "new_message")
                    return null;

                if (!root.TryGetProperty("message", out var messageElement))
                    return null;

                if (!messageElement.TryGetProperty("type", out var msgTypeElement))
                    return null;

                int msgType = 0;
                if (msgTypeElement.ValueKind == JsonValueKind.Number)
                {
                    if (!msgTypeElement.TryGetInt32(out msgType)) return null;
                }
                else if (msgTypeElement.ValueKind == JsonValueKind.String)
                {
                    if (!int.TryParse(msgTypeElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out msgType)) return null;
                }
                else return null;

                if (msgType != MSG_TYPE_STARTED && msgType != MSG_TYPE_COMPLETED && msgType != MSG_TYPE_FAILED)
                    return null;

                if (!messageElement.TryGetProperty("templateId", out var templateIdElement))
                    return null;

                var templateId = templateIdElement.GetString();
                if (string.IsNullOrEmpty(templateId))
                    return null;

                var questId = templateId.Split(' ')[0];

                var traderId = "";
                if (root.TryGetProperty("dialogId", out var dialogIdElement))
                {
                    traderId = dialogIdElement.GetString() ?? "";
                }

                var timestamp = DateTime.Now;
                if (messageElement.TryGetProperty("dt", out var dtElement))
                {
                    double seconds = 0;
                    bool ok = dtElement.ValueKind switch
                    {
                        JsonValueKind.Number => dtElement.TryGetDouble(out seconds),
                        JsonValueKind.String => double.TryParse(dtElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds),
                        _ => false
                    };

                    if (ok && seconds > 946684800 && seconds < 4102444800)
                    {
                        timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000)).LocalDateTime;
                    }
                    else if (ok && seconds > 0)
                    {
                        timestamp = DateTimeOffset.FromUnixTimeSeconds((long)seconds).LocalDateTime;
                    }
                }

                var eventType = msgType switch
                {
                    MSG_TYPE_STARTED => QuestEventType.Started,
                    MSG_TYPE_COMPLETED => QuestEventType.Completed,
                    MSG_TYPE_FAILED => QuestEventType.Failed,
                    _ => QuestEventType.Started
                };

                return new QuestLogEvent
                {
                    QuestId = questId,
                    EventType = eventType,
                    TraderId = traderId,
                    Timestamp = timestamp,
                    OriginalLine = jsonString.Length > 200 ? jsonString.Substring(0, 200) : jsonString,
                    SourceFile = sourceFile
                };
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Quest Synchronization

        /// <summary>
        /// Synchronize quest progress from log files
        /// </summary>
        public async Task<SyncResult> SyncFromLogsAsync(string logFolderPath, IProgress<string>? progress = null, int daysRange = 0)
        {
            var result = new SyncResult();

            _log.Info($"Starting sync from: {logFolderPath}");
            progress?.Report("Scanning log files...");

            var events = await ParseLogDirectoryAsync(logFolderPath, progress).ConfigureAwait(false);
            _log.Info($"Found {events.Count} quest events in logs");

            if (daysRange > 0)
            {
                var cutoffDate = DateTime.Now.AddDays(-daysRange);
                var originalCount = events.Count;
                events = events.Where(e => e.Timestamp >= cutoffDate).ToList();
                progress?.Report($"Filtered to {events.Count}/{originalCount} events from last {daysRange} days");
            }

            result.TotalEventsFound = events.Count;

            if (events.Count == 0)
            {
                result.Errors.Add("No quest events found in logs");
                return result;
            }

            progress?.Report($"Analyzing {events.Count} quest events...");

            var progressService = QuestProgressService.Instance;
            var graphService = QuestGraphService.Instance;
            var tasksByQuestId = BuildQuestIdLookup(progressService.AllTasks);
            _log.Debug($"Built lookup with {tasksByQuestId.Count} quest IDs from {progressService.AllTasks.Count} tasks");

            var questFinalStates = new Dictionary<string, (QuestEventType EventType, DateTime Timestamp, TarkovTask Task)>(StringComparer.OrdinalIgnoreCase);
            var startedQuests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var evt in events)
            {
                var task = FindTaskByQuestId(tasksByQuestId, evt.QuestId);
                if (task == null)
                {
                    if (!evt.QuestId.StartsWith("61604", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!result.UnmatchedQuestIds.Contains(evt.QuestId))
                            result.UnmatchedQuestIds.Add(evt.QuestId);
                    }
                    continue;
                }

                var normalizedName = task.NormalizedName ?? "";
                if (string.IsNullOrEmpty(normalizedName)) continue;

                if (evt.EventType == QuestEventType.Started)
                {
                    startedQuests.Add(normalizedName);
                }

                questFinalStates[normalizedName] = (evt.EventType, evt.Timestamp, task);

                switch (evt.EventType)
                {
                    case QuestEventType.Started: result.QuestsStarted++; break;
                    case QuestEventType.Completed: result.QuestsCompleted++; break;
                    case QuestEventType.Failed: result.QuestsFailed++; break;
                }
            }

            var questsToComplete = new List<QuestChangeInfo>();
            var processedPrereqs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var terminalStateQuests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in questFinalStates)
            {
                if (kvp.Value.EventType == QuestEventType.Completed || kvp.Value.EventType == QuestEventType.Failed)
                {
                    terminalStateQuests.Add(kvp.Key);
                }
            }

            foreach (var kvp in questFinalStates)
            {
                var normalizedName = kvp.Key;
                var (eventType, timestamp, task) = kvp.Value;
                var currentStatus = progressService.GetStatus(task);

                switch (eventType)
                {
                    case QuestEventType.Started:
                        break;

                    case QuestEventType.Completed:
                        if (currentStatus != QuestStatus.Done)
                        {
                            questsToComplete.Add(new QuestChangeInfo
                            {
                                QuestName = task.Name,
                                NormalizedName = normalizedName,
                                Trader = task.Trader,
                                IsPrerequisite = false,
                                ChangeType = QuestEventType.Completed,
                                Timestamp = timestamp
                            });
                        }
                        break;

                    case QuestEventType.Failed:
                        if (currentStatus != QuestStatus.Failed)
                        {
                            questsToComplete.Add(new QuestChangeInfo
                            {
                                QuestName = task.Name,
                                NormalizedName = normalizedName,
                                Trader = task.Trader,
                                IsPrerequisite = false,
                                ChangeType = QuestEventType.Failed,
                                Timestamp = timestamp
                            });
                        }
                        break;
                }

                if (eventType == QuestEventType.Started)
                    continue;

                var prereqs = graphService.GetAllPrerequisites(normalizedName);
                foreach (var prereq in prereqs)
                {
                    if (prereq.NormalizedName == null) continue;
                    if (processedPrereqs.Contains(prereq.NormalizedName)) continue;

                    if (terminalStateQuests.Contains(prereq.NormalizedName)) continue;
                    if (startedQuests.Contains(prereq.NormalizedName) && !terminalStateQuests.Contains(prereq.NormalizedName))
                        continue;

                    var prereqStatus = progressService.GetStatus(prereq);
                    if (prereqStatus != QuestStatus.Done)
                    {
                        if (progressService.HasAlternativeQuests(prereq))
                        {
                            _log.Debug($"Skipping alternative quest prereq: {prereq.Name}");
                            continue;
                        }

                        if (prereqStatus == QuestStatus.Unavailable)
                        {
                            _log.Warning($"Skipping prerequisite auto-complete for {prereq.Name}: Quest is Unavailable for current player/faction/edition.");
                            continue;
                        }

                        if (!progressService.IsPoolRequirementMet(prereq))
                        {
                            _log.Warning($"Skipping prerequisite auto-complete for {prereq.Name}: Pool requirements not met.");
                            continue;
                        }

                        if (!progressService.IsLevelRequirementMet(prereq))
                        {
                            _log.Warning($"Skipping prerequisite auto-complete for {prereq.Name}: Level requirement not met.");
                            continue;
                        }

                        if (!progressService.IsTraderRequirementMet(prereq))
                        {
                            _log.Warning($"Skipping prerequisite auto-complete for {prereq.Name}: Trader loyalty level requirement not met.");
                            continue;
                        }

                        questsToComplete.Add(new QuestChangeInfo
                        {
                            QuestName = prereq.Name,
                            NormalizedName = prereq.NormalizedName,
                            Trader = prereq.Trader,
                            IsPrerequisite = true,
                            ChangeType = QuestEventType.Completed,
                            Timestamp = timestamp
                        });
                        processedPrereqs.Add(prereq.NormalizedName);
                        result.PrerequisitesAutoCompleted++;
                    }
                }
            }

            var gatedTraders = new (string Trader, string UnlockId, string UnlockNorm, string UnlockTitle)[]
            {
                ("Jaeger", "5d2495a886f77425cd51e403", "introduction", "Introduction"),
                ("Ref", "66058cb22cee99303f1ba067", "easy-money---part-1", "Easy Money - Part 1"),
                ("Lightkeeper", "625d700cc48e6c62a440fab5", "getting-acquainted", "Getting Acquainted"),
                ("BTR Driver", "6752f6d83038f7df520c83e8", "a-helping-hand", "A Helping Hand")
            };

            foreach (var (trader, unlockId, unlockNorm, unlockTitle) in gatedTraders)
            {
                bool hasTraderActivity = questFinalStates.Values.Any(v =>
                    v.EventType == QuestEventType.Completed &&
                    string.Equals(v.Task.Trader, trader, StringComparison.OrdinalIgnoreCase))
                    || progressService.GetTraderLoyaltyLevel(trader) >= 2;

                if (hasTraderActivity)
                {
                    var unlockTask = progressService.GetTaskById(unlockId) ?? progressService.GetTask(unlockNorm);
                    var currentUnlockStatus = unlockTask != null ? progressService.GetStatus(unlockTask) : QuestStatus.Locked;
                    bool alreadyInList = questsToComplete.Any(q =>
                        string.Equals(q.NormalizedName, unlockNorm, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(q.QuestName, unlockTitle, StringComparison.OrdinalIgnoreCase));

                    if (currentUnlockStatus != QuestStatus.Done && !alreadyInList)
                    {
                        var normName = unlockTask?.NormalizedName ?? unlockNorm;
                        questsToComplete.Add(new QuestChangeInfo
                        {
                            QuestName = unlockTask?.Name ?? unlockTitle,
                            NormalizedName = normName,
                            Trader = unlockTask?.Trader ?? "Mechanic",
                            IsPrerequisite = true,
                            ChangeType = QuestEventType.Completed,
                            Timestamp = DateTime.UtcNow
                        });
                        processedPrereqs.Add(normName);
                        result.PrerequisitesAutoCompleted++;
                    }
                }
            }

            result.QuestsToComplete = questsToComplete.OrderBy(q => q.Timestamp).ToList();
            result.AlternativeQuestGroups = CollectAlternativeQuestGroups(progressService, questFinalStates, terminalStateQuests);

            foreach (var kvp in questFinalStates)
            {
                var normalizedName = kvp.Key;
                var (eventType, _, task) = kvp.Value;

                if (eventType == QuestEventType.Started)
                {
                    var currentStatus = progressService.GetStatus(task);
                    if (currentStatus != QuestStatus.Done)
                    {
                        result.InProgressQuests.Add(task);
                    }
                }
            }

            foreach (var change in questsToComplete.Where(q => q.ChangeType == QuestEventType.Completed && !q.IsPrerequisite))
            {
                var task = progressService.GetTask(change.NormalizedName);
                if (task != null)
                {
                    result.CompletedQuests.Add(task);
                }
            }

            progress?.Report($"Found {questsToComplete.Count} quests to update");
            _log.Info($"Sync complete: {result.TotalEventsFound} events, {result.QuestsToComplete.Count} to complete, {result.InProgressQuests.Count} in progress, {result.UnmatchedQuestIds.Count} unmatched");

            return result;
        }

        /// <summary>
        /// Apply quest changes after user confirmation (batch processing for performance)
        /// </summary>
        public async Task ApplyQuestChangesAsync(List<QuestChangeInfo> changes)
        {
            var progressService = QuestProgressService.Instance;
            var selectedChanges = changes.Where(c => c.IsSelected).ToList();

            _log.Info($"ApplyQuestChangesAsync: {changes.Count} total changes, {selectedChanges.Count} selected");

            var batchChanges = new List<(TarkovTask Task, QuestStatus Status)>();

            foreach (var change in selectedChanges)
            {
                var task = progressService.GetTask(change.NormalizedName);
                if (task == null)
                {
                    _log.Warning($"Task not found for NormalizedName: {change.NormalizedName}");
                    continue;
                }

                var status = change.ChangeType switch
                {
                    QuestEventType.Completed => QuestStatus.Done,
                    QuestEventType.Failed => QuestStatus.Failed,
                    _ => QuestStatus.Active
                };

                if (status != QuestStatus.Active)
                {
                    batchChanges.Add((task, status));
                    _log.Debug($"Queued change: {change.NormalizedName} -> {change.ChangeType}");
                }
            }

            if (batchChanges.Count > 0)
            {
                await progressService.ApplyQuestChangesBatchAsync(batchChanges).ConfigureAwait(false);
                _log.Info($"Batch applied {batchChanges.Count} quest changes");
            }

            _log.Info("ApplyQuestChangesAsync completed");
        }

        /// <summary>
        /// Apply quest changes after user confirmation (legacy sync method, calls async safely)
        /// </summary>
        [Obsolete("Use ApplyQuestChangesAsync for better performance")]
        public void ApplyQuestChanges(List<QuestChangeInfo> changes)
        {
            _ = ApplyQuestChangesSafeWrapperAsync(changes);
        }

        private async Task ApplyQuestChangesSafeWrapperAsync(List<QuestChangeInfo> changes)
        {
            try
            {
                await ApplyQuestChangesAsync(changes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error($"Unhandled failure in ApplyQuestChanges: {ex.Message}");
            }
        }

        private Dictionary<string, TarkovTask> BuildQuestIdLookup(IReadOnlyList<TarkovTask> tasks)
        {
            var lookup = new Dictionary<string, TarkovTask>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in tasks)
            {
                if (task.Ids != null)
                {
                    foreach (var id in task.Ids)
                    {
                        if (!string.IsNullOrEmpty(id) && !lookup.ContainsKey(id))
                        {
                            lookup[id] = task;
                        }
                    }
                }
            }

            return lookup;
        }

        private TarkovTask? FindTaskByQuestId(Dictionary<string, TarkovTask> lookup, string questId)
        {
            return lookup.TryGetValue(questId, out var task) ? task : null;
        }

        private List<AlternativeQuestGroup> CollectAlternativeQuestGroups(
            QuestProgressService progressService,
            Dictionary<string, (QuestEventType EventType, DateTime Timestamp, TarkovTask Task)> questFinalStates,
            HashSet<string> terminalStateQuests)
        {
            var groups = new List<AlternativeQuestGroup>();
            var processedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var graphService = QuestGraphService.Instance;

            foreach (var kvp in questFinalStates)
            {
                var normalizedName = kvp.Key;
                var task = kvp.Value.Task;

                var prereqs = graphService.GetAllPrerequisites(normalizedName);

                foreach (var prereq in prereqs)
                {
                    if (prereq.NormalizedName == null) continue;
                    if (!progressService.HasAlternativeQuests(prereq)) continue;

                    var groupKey = GetAlternativeGroupKey(prereq, progressService);
                    if (processedGroups.Contains(groupKey)) continue;
                    processedGroups.Add(groupKey);

                    var group = new AlternativeQuestGroup { IsRequired = true };

                    var mainStatus = progressService.GetStatus(prereq);
                    group.Choices.Add(new AlternativeQuestChoice
                    {
                        Task = prereq,
                        IsCompleted = mainStatus == QuestStatus.Done,
                        IsFailed = mainStatus == QuestStatus.Failed,
                        IsSelected = mainStatus == QuestStatus.Done
                    });

                    if (prereq.AlternativeQuests != null)
                    {
                        foreach (var altName in prereq.AlternativeQuests)
                        {
                            var altTask = progressService.GetTask(altName) ?? progressService.GetTaskById(altName);
                            if (altTask != null)
                            {
                                var altStatus = progressService.GetStatus(altTask);
                                group.Choices.Add(new AlternativeQuestChoice
                                {
                                    Task = altTask,
                                    IsCompleted = altStatus == QuestStatus.Done,
                                    IsFailed = altStatus == QuestStatus.Failed,
                                    IsSelected = altStatus == QuestStatus.Done
                                });
                            }
                        }
                    }

                    if (group.Choices.Count > 1 && !group.Choices.Any(c => c.IsCompleted))
                    {
                        groups.Add(group);
                    }
                }
            }

            return groups;
        }

        private string GetAlternativeGroupKey(TarkovTask task, QuestProgressService progressService)
        {
            var names = new List<string> { task.NormalizedName ?? "" };

            if (task.AlternativeQuests != null)
            {
                names.AddRange(task.AlternativeQuests);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("|", names);
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

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            TarkovHelper.Services.Map.LogMapWatcherService.Instance.MapChanged -= OnExternalMapChanged;
            StopMonitoring();
            _pushProcessGate.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Event arguments for map detection
    /// </summary>
    public class MapDetectedEventArgs : EventArgs
    {
        public string MapKey { get; }
        public DateTime DetectedAt { get; }

        public MapDetectedEventArgs(string mapKey, DateTime detectedAt)
        {
            MapKey = mapKey;
            DetectedAt = detectedAt;
        }
    }
}
