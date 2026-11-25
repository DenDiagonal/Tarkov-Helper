using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services.DataAudit;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services;

/// <summary>
/// tarkov_data.db 업데이트를 관리하는 서비스.
/// GitHub에서 버전을 확인하고 새 버전이 있으면 자동으로 다운로드.
/// 5분마다 백그라운드에서 업데이트 체크.
/// </summary>
public sealed class DatabaseUpdateService : IDisposable
{
    private static readonly ILogger _log = Log.For<DatabaseUpdateService>();
    private static readonly Lazy<DatabaseUpdateService> _lazyInstance =
        new(() => new DatabaseUpdateService(), LazyThreadSafetyMode.ExecutionAndPublication);
    public static DatabaseUpdateService Instance => _lazyInstance.Value;

    private const string TARKOV_DEV_API_URL = "https://api.tarkov.dev/graphql";
    private const string VERSION_URL = "https://raw.githubusercontent.com/DenDiagonal/Tarkov-Helper/refs/heads/main/TarkovHelper/Assets/db_version.txt";
    private const string DATABASE_URL = "https://raw.githubusercontent.com/DenDiagonal/Tarkov-Helper/refs/heads/main/TarkovHelper/Assets/tarkov_data.db";
    private const string LOCAL_VERSION_FILE = "db_version.txt";
    private const string DATABASE_FILE = "tarkov_data.db";
    private const int UPDATE_INTERVAL_MS = 5 * 60 * 1000; // 5분

    private readonly string _assetsPath;
    private readonly string _databasePath;
    private readonly string _versionFilePath;
    private readonly HttpClient _httpClient;
    private readonly System.Threading.Timer _updateTimer;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    private volatile bool _isUpdating;
    private volatile bool _disposed;
    private int _internalHotReloadDepth;

    internal bool IsInternalHotReloadInProgress => Volatile.Read(ref _internalHotReloadDepth) > 0;

    internal void BeginInternalHotReload() => Interlocked.Increment(ref _internalHotReloadDepth);
    internal void EndInternalHotReload() => Interlocked.Decrement(ref _internalHotReloadDepth);

    /// <summary>
    /// 데이터베이스 파일 경로
    /// </summary>
    public string DatabasePath => _databasePath;

    /// <summary>
    /// 현재 로컬 버전
    /// </summary>
    public string? LocalVersion { get; private set; }

    /// <summary>
    /// 최신 원격 버전
    /// </summary>
    public string? RemoteVersion { get; private set; }

    /// <summary>
    /// 업데이트 진행 중 여부
    /// </summary>
    public bool IsUpdating => _isUpdating;

    /// <summary>
    /// 데이터베이스가 업데이트되었을 때 발생하는 이벤트.
    /// 모든 DB 서비스는 이 이벤트를 구독하여 데이터를 리로드해야 함.
    /// </summary>
    public event EventHandler? DatabaseUpdated;

    /// <summary>
    /// 업데이트 체크 시작 시 발생
    /// </summary>
    public event EventHandler? UpdateCheckStarted;

    /// <summary>
    /// 업데이트 체크 완료 시 발생 (업데이트 여부와 관계없이)
    /// </summary>
    public event EventHandler<UpdateCheckResult>? UpdateCheckCompleted;

    private DatabaseUpdateService()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _assetsPath = Path.Combine(appDir, "Assets");
        _databasePath = Path.Combine(_assetsPath, DATABASE_FILE);
        _versionFilePath = Path.Combine(_assetsPath, LOCAL_VERSION_FILE);

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TarkovHelper/1.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        // 로컬 버전 로드
        LoadLocalVersion();

        // 5분마다 업데이트 체크 타이머 설정
        _updateTimer = new System.Threading.Timer(
            OnUpdateTimerElapsed,
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    /// <summary>
    /// 로컬 버전 파일에서 버전 정보 로드
    /// </summary>
    private void LoadLocalVersion()
    {
        try
        {
            if (File.Exists(_versionFilePath))
            {
                LocalVersion = File.ReadAllText(_versionFilePath).Trim();
                _log.Debug($"Local version: {LocalVersion}");
            }
            else
            {
                LocalVersion = null;
                _log.Debug("No local version file found");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Error loading local version: {ex.Message}");
            LocalVersion = null;
        }
    }

    /// <summary>
    /// 백그라운드 업데이트 체크 시작
    /// </summary>
    public void StartBackgroundUpdates()
    {
        _log.Info("Starting background update checks (every 5 minutes)");
        _updateTimer.Change(0, UPDATE_INTERVAL_MS);
    }

    /// <summary>
    /// 백그라운드 업데이트 체크 중지
    /// </summary>
    public void StopBackgroundUpdates()
    {
        _log.Info("Stopping background update checks");
        _updateTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Safe timer tick: handles exceptions and avoids async void crash
    /// </summary>
    private void OnUpdateTimerElapsed(object? state)
    {
        if (_disposed || _cts.IsCancellationRequested) return;
        _ = RunTimerCheckSafeAsync();
    }

    private async Task RunTimerCheckSafeAsync()
    {
        try
        {
            await CheckAndUpdateAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error($"Unhandled error in background DB update timer: {ex.Message}");
        }
    }

    /// <summary>
    /// 업데이트 확인 및 필요시 다운로드
    /// </summary>
    public async Task<UpdateCheckResult> CheckAndUpdateAsync()
    {
        _log.Info("Update check disabled by user request");
        return await Task.FromResult(new UpdateCheckResult(true, false, "Update feature is disabled")).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks for updates against TarkovTracker + BSG Server and returns the diff report.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAndUpdateAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        try
        {
            var diffReport = await CheckTarkovTrackerUpdatesAsync(progress, ct).ConfigureAwait(false);
            return new UpdateCheckResult(true, false, "Comprobación completada", diffReport);
        }
        catch (Exception ex)
        {
            _log.Error($"CheckAndUpdateAsync failed: {ex.Message}");
            return new UpdateCheckResult(false, false, ex.Message);
        }
    }

    /// <summary>
    /// Runs the dual-source extraction and reconciliation (TarkovTracker + BSG Server)
    /// and calculates differences against the local database.
    /// </summary>
    public async Task<DataDiffReport> CheckTarkovTrackerUpdatesAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _log.Info("Starting TarkovTracker update check with dual-source BSG reconciliation...");
        using var extractor = new TarkovTrackerExtractorService();
        var rawData = await extractor.ExtractAsync(progress, ct).ConfigureAwait(false);

        progress?.Report("Normalizing TarkovTracker dataset...");
        var normalizer = new DataNormalizerService();
        var normalized = normalizer.Normalize(rawData, (CommunityDataPayload?)null);

        progress?.Report("Computing differences against local database...");
        var diffEngine = DatabaseDiffEngine.Instance;
        var diffReport = await diffEngine.ComputeDiffAsync(
            normalized, 
            _databasePath, 
            includeUnchanged: false, 
            detectRemovedQuests: false, 
            pruneBrokenPrerequisites: false, 
            ct: ct).ConfigureAwait(false);

        _log.Info($"TarkovTracker update check complete: {diffReport.TotalChanges} total differences found.");
        return diffReport;
    }

    /// <summary>
    /// 실제 업데이트 확인 및 필요시 다운로드 수행
    /// </summary>
    private async Task<UpdateCheckResult> DoCheckAndUpdateAsync()
    {
        UpdateCheckStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            // 1. 원격 버전 확인
            _log.Debug("Checking remote version...");
            var remoteVersion = await GetRemoteVersionAsync().ConfigureAwait(false);

            if (string.IsNullOrEmpty(remoteVersion))
            {
                var result = new UpdateCheckResult(false, false, "Failed to fetch remote version");
                UpdateCheckCompleted?.Invoke(this, result);
                return result;
            }

            RemoteVersion = remoteVersion;
            _log.Debug($"Remote version: {remoteVersion}, Local version: {LocalVersion}");

            // 2. 버전 비교
            if (LocalVersion == remoteVersion)
            {
                _log.Debug("Database is up to date");
                var result = new UpdateCheckResult(true, false, "Database is up to date");
                UpdateCheckCompleted?.Invoke(this, result);
                return result;
            }

            // 3. 새 버전 다운로드
            _log.Info($"New version available: {remoteVersion}");
            var downloadSuccess = await DownloadDatabaseAsync(_cts.Token).ConfigureAwait(false);

            if (!downloadSuccess)
            {
                var result = new UpdateCheckResult(false, false, "Failed to download database");
                UpdateCheckCompleted?.Invoke(this, result);
                return result;
            }

            // 4. 버전 파일 업데이트
            await UpdateLocalVersionAsync(remoteVersion).ConfigureAwait(false);

            // 5. 업데이트 완료 이벤트 발생
            _log.Info("Database updated successfully, notifying services...");
            OnDatabaseUpdated();

            var successResult = new UpdateCheckResult(true, true, $"Updated to version {remoteVersion}");
            UpdateCheckCompleted?.Invoke(this, successResult);
            return successResult;
        }
        catch (Exception ex)
        {
            _log.Error($"Error during update check: {ex.Message}");
            var result = new UpdateCheckResult(false, false, ex.Message);
            UpdateCheckCompleted?.Invoke(this, result);
            return result;
        }
    }

    /// <summary>
    /// 원격 버전 정보 가져오기 (tarkov.dev API 우선)
    /// </summary>
    private async Task<string?> GetRemoteVersionAsync()
    {
        try
        {
            var tarkovDevVersion = await GetTarkovDevStatusAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(tarkovDevVersion))
            {
                _log.Debug($"Latest tarkov.dev updated at: {tarkovDevVersion}");
                return tarkovDevVersion;
            }
            
            _log.Debug("Falling back to GitHub version check...");
            var response = await _httpClient.GetStringAsync(VERSION_URL, _cts.Token).ConfigureAwait(false);
            return response.Trim();
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to get remote version: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// tarkov.dev GraphQL API를 사용하여 최신 데이터 업데이트 시각 가져오기
    /// </summary>
    private async Task<string?> GetTarkovDevStatusAsync()
    {
        try
        {
            var query = "{ \"query\": \"{ tarkovDataStatus { updated } }\" }";
            var content = new StringContent(query, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(TARKOV_DEV_API_URL, content, _cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(_cts.Token).ConfigureAwait(false);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("tarkovDataStatus", out var status) &&
                status.TryGetProperty("updated", out var updated))
            {
                return updated.GetString();
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Tarkov.dev status check failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 데이터베이스 파일 다운로드 및 유효성 검증 후 안전한 원자적 스왑 수행
    /// </summary>
    private async Task<bool> DownloadDatabaseAsync(CancellationToken ct = default)
    {
        if (!await _updateLock.WaitAsync(0, ct).ConfigureAwait(false))
        {
            _log.Info("Database download already in progress; skipping concurrent invocation.");
            return false;
        }

        _isUpdating = true;
        var tempPath = _databasePath + $".{Guid.NewGuid():N}.tmp";
        var backupPath = _databasePath + ".bak";

        try
        {
            _log.Info("Downloading database...");

            if (!Directory.Exists(_assetsPath))
            {
                Directory.CreateDirectory(_assetsPath);
            }

            using (var response = await _httpClient.GetAsync(DATABASE_URL, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;
                _log.Debug($"Expected database size: {totalBytes?.ToString() ?? "unknown"} bytes");

                await using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
                long downloadedBytes = 0;

                try
                {
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, 81920), ct).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                        downloadedBytes += bytesRead;
                    }

                    if (totalBytes.HasValue && downloadedBytes != totalBytes.Value)
                    {
                        throw new IOException($"Truncated download: received {downloadedBytes} of {totalBytes.Value} bytes.");
                    }

                    if (downloadedBytes < 4096)
                    {
                        throw new InvalidDataException($"Downloaded file is too small ({downloadedBytes} bytes) to be a valid SQLite database.");
                    }

                    // Flush physically to disk before validating
                    fileStream.Flush(flushToDisk: true);
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            // 1. Validate downloaded candidate SQLite file strictly
            _log.Debug("Validating downloaded database file integrity...");
            await ValidateDownloadedSqliteFileAsync(tempPath, ct).ConfigureAwait(false);

            // 2. Clear target SQLite pool specifically
            ClearTargetDbPool(_databasePath);

            // 3. Perform atomic swap with retry
            const int maxRetries = 5;
            bool swapSucceeded = false;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    DeleteSidecarIfExists(_databasePath + "-wal");
                    DeleteSidecarIfExists(_databasePath + "-shm");

                    if (File.Exists(_databasePath))
                    {
                        // Atomic swap using Windows ReplaceFileW
                        File.Replace(tempPath, _databasePath, backupPath, ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(tempPath, _databasePath);
                    }

                    swapSucceeded = true;
                    _log.Info("Database downloaded, validated, and swapped successfully via atomic Replace.");
                    break;
                }
                catch (IOException ex) when (attempt < maxRetries - 1)
                {
                    _log.Warning($"Database swap retry {attempt + 1}/{maxRetries}: {ex.Message}");
                    ClearTargetDbPool(_databasePath);
                    await Task.Delay(250 * (attempt + 1), ct).ConfigureAwait(false);
                }
            }

            if (!swapSucceeded)
            {
                throw new IOException("Failed to atomically replace target database file after maximum retries.");
            }

            DeleteSidecarIfExists(backupPath);
            DeleteSidecarIfExists(_databasePath + "-wal");
            DeleteSidecarIfExists(_databasePath + "-shm");

            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to download/swap database: {ex.Message}");

            // Defensive recovery: if destination was somehow removed and backup exists, restore it
            if (!File.Exists(_databasePath) && File.Exists(backupPath))
            {
                try
                {
                    File.Move(backupPath, _databasePath);
                }
                catch { }
            }

            return false;
        }
        finally
        {
            DeleteSidecarIfExists(tempPath);
            _isUpdating = false;
            _updateLock.Release();
        }
    }

    private static async Task ValidateDownloadedSqliteFileAsync(string candidateDbPath, CancellationToken ct)
    {
        if (!File.Exists(candidateDbPath))
            throw new FileNotFoundException("Candidate SQLite database file not found.", candidateDbPath);

        // 1. Binary magic header check
        byte[] header = new byte[16];
        await using (var fs = new FileStream(candidateDbPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            int read = await fs.ReadAsync(header.AsMemory(0, 16), ct).ConfigureAwait(false);
            byte[] expectedMagic = System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0");
            if (read < 16 || !header.SequenceEqual(expectedMagic))
                throw new InvalidDataException("Downloaded file does not have a valid SQLite 3 binary header.");
        }

        // 2. Open connection without pooling and execute integrity check
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = candidateDbPath,
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 5,
            Pooling = false
        };

        await using var conn = new SqliteConnection(csb.ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using (var integrityCmd = new SqliteCommand("PRAGMA quick_check(1);", conn))
        {
            var checkResult = (await integrityCmd.ExecuteScalarAsync(ct).ConfigureAwait(false))?.ToString();
            if (!string.Equals(checkResult, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SQLite integrity check failed on downloaded DB: '{checkResult}'");
        }

        await using (var schemaCmd = new SqliteCommand(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('items', 'quests', 'Tasks');", conn))
        {
            var tableCount = Convert.ToInt32(await schemaCmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
            if (tableCount < 1)
                throw new InvalidDataException("Downloaded SQLite DB is missing required core tables.");
        }
    }

    private static void ClearTargetDbPool(string dbPath)
    {
        foreach (var mode in new[] { SqliteOpenMode.ReadOnly, SqliteOpenMode.ReadWrite, SqliteOpenMode.ReadWriteCreate })
        {
            try
            {
                using var dummy = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = dbPath,
                    Mode = mode,
                    Pooling = true
                }.ConnectionString);
                SqliteConnection.ClearPool(dummy);
            }
            catch { }
        }
    }

    private static void DeleteSidecarIfExists(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    /// <summary>
    /// 로컬 버전 파일 업데이트 (임시 파일 거친 원자적 저장)
    /// </summary>
    private async Task UpdateLocalVersionAsync(string version)
    {
        try
        {
            var tempVersionFile = _versionFilePath + ".tmp";
            await File.WriteAllTextAsync(tempVersionFile, version, _cts.Token).ConfigureAwait(false);
            File.Move(tempVersionFile, _versionFilePath, overwrite: true);
            LocalVersion = version;
            _log.Debug($"Local version updated to: {version}");
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to update local version file: {ex.Message}");
        }
    }

    /// <summary>
    /// 데이터베이스 업데이트 완료 이벤트 발생
    /// </summary>
    private void OnDatabaseUpdated()
    {
        var handler = DatabaseUpdated;
        if (handler == null) return;

        foreach (EventHandler listener in handler.GetInvocationList())
        {
            try
            {
                listener(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _log.Warning($"Error in DatabaseUpdated listener: {ex.Message}");
            }
        }
    }

    public void NotifyDatabaseUpdated()
    {
        _log.Info("NotifyDatabaseUpdated called, dispatching DatabaseUpdated event...");
        OnDatabaseUpdated();
    }

    public async Task<UpdateCheckResult> ForceUpdateCheckAsync()
    {
        _log.Info("Manual update check requested");
        return await CheckAndUpdateAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cts.Cancel();
            _cts.Dispose();
        }
        catch { }

        _updateTimer.Dispose();
        _updateLock.Dispose();
        _httpClient.Dispose();
    }
}

/// <summary>
/// 업데이트 체크 결과
/// </summary>
public class UpdateCheckResult
{
    public bool Success { get; }
    public bool WasUpdated { get; }
    public string Message { get; }
    public DataDiffReport? DiffReport { get; }

    public UpdateCheckResult(bool success, bool WasUpdated, string message, DataDiffReport? diffReport = null)
    {
        Success = success;
        this.WasUpdated = WasUpdated;
        Message = message;
        DiffReport = diffReport;
    }
}
