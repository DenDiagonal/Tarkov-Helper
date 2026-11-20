using System.Collections.Concurrent;
using System.IO;
using TarkovHelper.Models.Map;

namespace TarkovHelper.Services.Map;

/// <summary>
/// 스크린샷 폴더를 감시하고 새 파일 생성 시 이벤트를 발생시키는 서비스.
/// </summary>
public sealed class ScreenshotWatcherService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly IScreenshotCoordinateParser _parser;
    private readonly ConcurrentDictionary<string, DateTime> _recentFiles = new();
    private readonly SemaphoreSlim _processingLimiter = new(4, 4);
    private readonly object _lock = new();
    private CancellationTokenSource? _watchCts;
    private int _cleanupCounter;
    private bool _isDisposed;

    /// <summary>
    /// 디바운싱 대기 시간 (밀리초)
    /// </summary>
    public int DebounceDelayMs { get; set; } = 300;

    /// <summary>
    /// 현재 감시 중인 폴더 경로
    /// </summary>
    public string? CurrentWatchPath { get; private set; }

    /// <summary>
    /// 감시 중 여부
    /// </summary>
    public bool IsWatching => _watcher?.EnableRaisingEvents ?? false;

    /// <summary>
    /// 새 스크린샷에서 좌표가 감지되었을 때 발생하는 이벤트
    /// </summary>
    public event EventHandler<PositionDetectedEventArgs>? PositionDetected;

    /// <summary>
    /// 파싱 실패 시 발생하는 이벤트
    /// </summary>
    public event EventHandler<ParsingFailedEventArgs>? ParsingFailed;

    /// <summary>
    /// 감시 상태가 변경되었을 때 발생하는 이벤트
    /// </summary>
    public event EventHandler<WatcherStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 오류 발생 시 이벤트
    /// </summary>
    public event EventHandler<WatcherErrorEventArgs>? Error;

    public ScreenshotWatcherService(IScreenshotCoordinateParser parser)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    /// <summary>
    /// 지정된 폴더 감시를 시작합니다.
    /// </summary>
    /// <param name="folderPath">스크린샷 폴더 경로</param>
    /// <returns>시작 성공 여부</returns>
    public bool StartWatching(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return false;

        if (!Directory.Exists(folderPath))
        {
            OnError($"Folder does not exist: {folderPath}");
            return false;
        }

        lock (_lock)
        {
            if (_isDisposed) return false;

            try
            {
                // 기존 watcher 및 취소 토큰 정리
                StopWatching();

                _watchCts = new CancellationTokenSource();

                _watcher = new FileSystemWatcher(folderPath)
                {
                    Filter = "*.png",
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false,
                    InternalBufferSize = 65536 // 64KB para evitar desbordamiento del búfer bajo ráfagas
                };

                _watcher.Created += OnFileCreated;
                _watcher.Changed += OnFileChanged;
                _watcher.Renamed += OnFileRenamed;
                _watcher.Error += OnWatcherError;

                _watcher.EnableRaisingEvents = true;
                CurrentWatchPath = folderPath;

                OnStateChanged(true, folderPath);

                // Procesar la captura más reciente existente si ya hay capturas en la carpeta
                var token = _watchCts.Token;
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(150, token).ConfigureAwait(false);
                        if (!token.IsCancellationRequested)
                        {
                            ProcessLatestExistingFile(folderPath);
                        }
                    }
                    catch { }
                }, token);

                return true;
            }
            catch (Exception ex)
            {
                OnError($"Failed to start screenshot watcher: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// 감시를 중지합니다.
    /// </summary>
    public void StopWatching()
    {
        lock (_lock)
        {
            if (_watchCts != null)
            {
                try
                {
                    _watchCts.Cancel();
                    _watchCts.Dispose();
                }
                catch { }
                _watchCts = null;
            }

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileCreated;
                _watcher.Changed -= OnFileChanged;
                _watcher.Renamed -= OnFileRenamed;
                _watcher.Error -= OnWatcherError;
                _watcher.Dispose();
                _watcher = null;

                var previousPath = CurrentWatchPath;
                CurrentWatchPath = null;
                _recentFiles.Clear();

                OnStateChanged(false, previousPath);
            }
        }
    }

    /// <summary>
    /// 감시 폴더를 변경합니다.
    /// </summary>
    public bool ChangeWatchPath(string newFolderPath)
    {
        StopWatching();
        return StartWatching(newFolderPath);
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        ProcessFile(e.FullPath);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        ProcessFile(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        ProcessFile(e.FullPath);
    }

    private void ProcessLatestExistingFile(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath)) return;
            var directoryInfo = new DirectoryInfo(folderPath);
            var latestFile = directoryInfo.GetFiles("*.png")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latestFile != null && latestFile.Exists)
            {
                if (_parser.TryParse(latestFile.Name, out var position) && position != null)
                {
                    OnPositionDetected(position, latestFile.FullName);
                }
            }
        }
        catch { }
    }

    private void ProcessFile(string filePath)
    {
        if (_isDisposed) return;

        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName)) return;

        // Fast-path: Si el nombre del archivo no cumple el patrón de coordenadas EFT,
        // no realizar I/O ni bloquear hilos.
        if (!_parser.TryParse(fileName, out var position) || position == null)
        {
            OnParsingFailed(fileName, "Pattern does not match");
            return;
        }

        var now = DateTime.UtcNow;
        bool shouldProcess = false;

        // Operación atómica de actualización para eliminar condición de carrera entre Created y Changed
        _recentFiles.AddOrUpdate(
            fileName,
            addValueFactory: _ =>
            {
                shouldProcess = true;
                return now;
            },
            updateValueFactory: (_, lastProcessed) =>
            {
                if ((now - lastProcessed).TotalMilliseconds >= DebounceDelayMs)
                {
                    shouldProcess = true;
                    return now;
                }
                return lastProcessed;
            });

        if (!shouldProcess) return;

        var token = _watchCts?.Token ?? CancellationToken.None;

        Task.Run(async () =>
        {
            if (_isDisposed || token.IsCancellationRequested) return;

            // Limitar concurrencia de procesamiento de I/O a un máximo de 4 hilos simultáneos
            if (!await _processingLimiter.WaitAsync(2000, token).ConfigureAwait(false))
                return;

            try
            {
                if (_isDisposed || token.IsCancellationRequested) return;

                // Esperar a que el archivo tenga tamaño estable y sea accesible en lectura
                if (!await WaitForStableReadableFileAsync(filePath, TimeSpan.FromSeconds(3), token).ConfigureAwait(false))
                {
                    OnError($"Cannot access file: {fileName}");
                    return;
                }

                if (_isDisposed || token.IsCancellationRequested) return;

                OnPositionDetected(position, filePath);
            }
            catch (OperationCanceledException)
            {
                // Normal al detener el watcher o cerrar la aplicación
            }
            catch (Exception ex)
            {
                OnError($"File processing error ({fileName}): {ex.Message}");
            }
            finally
            {
                _processingLimiter.Release();

                // Limpieza amortizada para evitar recolección de basura continua
                if ((Interlocked.Increment(ref _cleanupCounter) % 32) == 0)
                {
                    CleanupRecentFiles();
                }
            }
        }, token);
    }

    private static async Task<bool> WaitForStableReadableFileAsync(string filePath, TimeSpan timeout, CancellationToken ct)
    {
        var endTime = DateTime.UtcNow + timeout;
        long lastLen = -1;

        while (DateTime.UtcNow < endTime && !ct.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    long currentLen = fs.Length;
                    if (currentLen > 0 && currentLen == lastLen)
                    {
                        return true;
                    }
                    lastLen = currentLen;
                }
            }
            catch (IOException)
            {
                // El archivo está bloqueado por el proceso de escritura de EFT
            }
            catch (UnauthorizedAccessException)
            {
            }

            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        try
        {
            if (File.Exists(filePath))
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length > 0)
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    private void CleanupRecentFiles()
    {
        var threshold = DateTime.UtcNow.AddMinutes(-1);
        foreach (var kvp in _recentFiles)
        {
            if (kvp.Value < threshold)
            {
                _recentFiles.TryRemove(kvp.Key, out _);
            }
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        OnError($"FileSystemWatcher error: {e.GetException().Message}");

        if (_isDisposed) return;

        var currentPath = CurrentWatchPath;
        if (string.IsNullOrWhiteSpace(currentPath)) return;

        var token = _watchCts?.Token ?? CancellationToken.None;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000, token).ConfigureAwait(false);
                if (_isDisposed || token.IsCancellationRequested) return;

                lock (_lock)
                {
                    if (_isDisposed || token.IsCancellationRequested) return;
                    StartWatching(currentPath);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                OnError($"Watcher auto-recovery failed: {ex.Message}");
            }
        }, token);
    }

    private void OnPositionDetected(EftPosition position, string filePath)
    {
        PositionDetected?.Invoke(this, new PositionDetectedEventArgs(position, filePath));
    }

    private void OnParsingFailed(string fileName, string reason)
    {
        ParsingFailed?.Invoke(this, new ParsingFailedEventArgs(fileName, reason));
    }

    private void OnStateChanged(bool isWatching, string? path)
    {
        StateChanged?.Invoke(this, new WatcherStateChangedEventArgs(isWatching, path));
    }

    private void OnError(string message)
    {
        Error?.Invoke(this, new WatcherErrorEventArgs(message));
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopWatching();
        _processingLimiter.Dispose();
    }
}

/// <summary>
/// 좌표 감지 이벤트 인자
/// </summary>
public sealed class PositionDetectedEventArgs : EventArgs
{
    public EftPosition Position { get; }
    public string FilePath { get; }
    public DateTime DetectedAt { get; } = DateTime.Now;

    public PositionDetectedEventArgs(EftPosition position, string filePath)
    {
        Position = position;
        FilePath = filePath;
    }
}

/// <summary>
/// 파싱 실패 이벤트 인자
/// </summary>
public sealed class ParsingFailedEventArgs : EventArgs
{
    public string FileName { get; }
    public string Reason { get; }

    public ParsingFailedEventArgs(string fileName, string reason)
    {
        FileName = fileName;
        Reason = reason;
    }
}

/// <summary>
/// 감시 상태 변경 이벤트 인자
/// </summary>
public sealed class WatcherStateChangedEventArgs : EventArgs
{
    public bool IsWatching { get; }
    public string? WatchPath { get; }

    public WatcherStateChangedEventArgs(bool isWatching, string? watchPath)
    {
        IsWatching = isWatching;
        WatchPath = watchPath;
    }
}

/// <summary>
/// 오류 이벤트 인자
/// </summary>
public sealed class WatcherErrorEventArgs : EventArgs
{
    public string Message { get; }

    public WatcherErrorEventArgs(string message)
    {
        Message = message;
    }
}
