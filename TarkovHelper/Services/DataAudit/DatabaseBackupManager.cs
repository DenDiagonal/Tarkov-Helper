using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services.DataAudit
{
    /// <summary>
    /// Manages atomic backup creation, verification, and restoration for tarkov_data.db.
    /// Strictly protects user_data.db from any alteration.
    /// </summary>
    public class DatabaseBackupManager
    {
        private static readonly ILogger _log = Log.For<DatabaseBackupManager>();
        private static DatabaseBackupManager? _instance;
        public static DatabaseBackupManager Instance => _instance ??= new DatabaseBackupManager();

        private static readonly SemaphoreSlim _backupLock = new(1, 1);
        private static readonly byte[] SqliteHeaderMagic = new byte[]
        {
            0x53, 0x51, 0x4c, 0x69, 0x74, 0x65, 0x20, 0x66,
            0x6f, 0x72, 0x6d, 0x61, 0x74, 0x20, 0x33, 0x00
        };

        /// <summary>
        /// Resolves the canonical full path to tarkov_data.db.
        /// </summary>
        public static string ResolveDatabasePath(string? dbPath = null)
        {
            if (!string.IsNullOrWhiteSpace(dbPath))
                return Path.GetFullPath(dbPath);

            // 1. Try DatabaseUpdateService.Instance if available
            try
            {
                var svcPath = DatabaseUpdateService.Instance?.DatabasePath;
                if (!string.IsNullOrEmpty(svcPath) && File.Exists(svcPath))
                    return Path.GetFullPath(svcPath);
            }
            catch
            {
                // Ignore if in test runner without service context
            }

            // 2. Direct probe in AppDomain BaseDirectory
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var assetsDb = Path.Combine(baseDir, "Assets", "tarkov_data.db");
            if (File.Exists(assetsDb))
                return Path.GetFullPath(assetsDb);

            // 3. Ascend directory tree (useful during test runs or CLI builds)
            var current = new DirectoryInfo(baseDir);
            while (current != null)
            {
                var candidate1 = Path.Combine(current.FullName, "Assets", "tarkov_data.db");
                if (File.Exists(candidate1))
                    return Path.GetFullPath(candidate1);

                var candidate2 = Path.Combine(current.FullName, "TarkovHelper", "Assets", "tarkov_data.db");
                if (File.Exists(candidate2))
                    return Path.GetFullPath(candidate2);

                current = current.Parent;
            }

            return Path.GetFullPath(assetsDb);
        }

        /// <summary>
        /// Enforces strict isolation: user_data.db must never be touched by backup or restore.
        /// </summary>
        private static void ValidateNotUserDataDb(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var normalized = path.Replace('/', '\\').ToLowerInvariant();
            if (normalized.EndsWith("user_data.db") ||
                normalized.Contains("\\user_data.db") ||
                normalized.Contains("userdata.db"))
            {
                throw new InvalidOperationException("Security & Integrity Violation: Operations on 'user_data.db' are strictly forbidden in DatabaseBackupManager.");
            }
        }

        /// <summary>
        /// Creates a dated backup of the SQLite database: Assets/tarkov_data.db.bak_<yyyyMMdd_HHmmss>.
        /// Uses SQLite Online Backup API with safe copy fallback and validates non-zero file size.
        /// Serialized via SemaphoreSlim to prevent timestamp collisions and TOCTOU race conditions.
        /// </summary>
        public async Task<string> CreateBackupAsync(string? sourceDbPath = null)
        {
            var resolvedSource = ResolveDatabasePath(sourceDbPath);
            ValidateNotUserDataDb(resolvedSource);

            var sourceInfo = new FileInfo(resolvedSource);
            if (!sourceInfo.Exists)
            {
                throw new FileNotFoundException($"Source database file not found: {resolvedSource}", resolvedSource);
            }

            if (sourceInfo.Length == 0)
            {
                throw new InvalidOperationException($"Source database file '{resolvedSource}' is empty (0 bytes). Backup aborted to prevent backing up corrupt or truncated database.");
            }

            await _backupLock.WaitAsync();
            try
            {
                sourceInfo.Refresh();
                if (!sourceInfo.Exists)
                {
                    throw new FileNotFoundException($"Source database file not found: {resolvedSource}", resolvedSource);
                }

                if (sourceInfo.Length == 0)
                {
                    throw new InvalidOperationException($"Source database file '{resolvedSource}' is empty (0 bytes). Backup aborted.");
                }

                var dir = Path.GetDirectoryName(resolvedSource) ?? string.Empty;
                var fileName = Path.GetFileName(resolvedSource);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFileName = $"{fileName}.bak_{timestamp}";
                var backupPath = Path.Combine(dir, backupFileName);

                // Ensure unique path if multiple backups are triggered in the exact same second
                if (File.Exists(backupPath))
                {
                    int counter = 1;
                    while (File.Exists($"{backupPath}_{counter}"))
                    {
                        counter++;
                    }
                    backupPath = $"{backupPath}_{counter}";
                }

                _log.Info($"Creating dated database backup: {backupPath} from {resolvedSource}");

                bool onlineBackupSucceeded = false;
                try
                {
                    var srcConnStr = new SqliteConnectionStringBuilder
                    {
                        DataSource = resolvedSource,
                        Mode = SqliteOpenMode.ReadOnly
                    }.ToString();

                    var destConnStr = new SqliteConnectionStringBuilder
                    {
                        DataSource = backupPath,
                        Mode = SqliteOpenMode.ReadWriteCreate
                    }.ToString();

                    await using var sourceConn = new SqliteConnection(srcConnStr);
                    await sourceConn.OpenAsync();

                    await using var destConn = new SqliteConnection(destConnStr);
                    await destConn.OpenAsync();

                    sourceConn.BackupDatabase(destConn);
                    onlineBackupSucceeded = true;
                    _log.Debug("SQLite Online Backup API completed successfully.");
                }
                catch (Exception ex)
                {
                    _log.Warning($"SQLite Online Backup API failed: {ex.Message}. Attempting safe file copy fallback.");
                }

                if (!onlineBackupSucceeded)
                {
                    // Fallback to file copy after clearing pools
                    SqliteConnection.ClearAllPools();
                    await Task.Delay(50);
                    File.Copy(resolvedSource, backupPath, overwrite: true);
                }

                // Verify backup file exists and has non-zero size
                var fileInfo = new FileInfo(backupPath);
                if (!fileInfo.Exists || fileInfo.Length == 0)
                {
                    if (fileInfo.Exists)
                    {
                        try { fileInfo.Delete(); } catch { }
                    }
                    throw new IOException($"Backup verification failed: created backup file '{backupPath}' is missing or 0 bytes.");
                }

                _log.Info($"Database backup successfully created and verified: {backupPath} ({fileInfo.Length:N0} bytes)");
                return backupPath;
            }
            finally
            {
                _backupLock.Release();
            }
        }

        /// <summary>
        /// Safely restores the database from an existing backup file.
        /// Pre-validates backup file size (>= 100 bytes) and SQLite magic header before modifying target.
        /// Uses atomic staging with a .pre_restore safety copy to rollback target if any failure occurs.
        /// Clears SQLite connection pools to ensure file handles are released before and after restore.
        /// </summary>
        public async Task<bool> RestoreBackupAsync(string backupPath, string? targetDbPath = null)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
            {
                throw new ArgumentException("Backup path must not be null or empty.", nameof(backupPath));
            }

            ValidateNotUserDataDb(backupPath);
            ValidateNotUserDataDb(targetDbPath);

            var fullBackupPath = Path.GetFullPath(backupPath);
            var backupInfo = new FileInfo(fullBackupPath);
            if (!backupInfo.Exists)
            {
                throw new FileNotFoundException($"Backup file does not exist: {fullBackupPath}", fullBackupPath);
            }

            // 1. Pre-validate backup file: size >= 100 bytes (minimum SQLite header size)
            if (backupInfo.Length < 100)
            {
                throw new InvalidOperationException($"Invalid or incomplete backup file: '{fullBackupPath}' ({backupInfo.Length} bytes, minimum 100 bytes required). Restore aborted to protect target database.");
            }

            // Validate SQLite magic header: read first 16 bytes and verify ASCII "SQLite format 3\0"
            byte[] header = new byte[16];
            using (var stream = new FileStream(fullBackupPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int bytesRead = stream.Read(header, 0, 16);
                if (bytesRead < 16 || !header.SequenceEqual(SqliteHeaderMagic))
                {
                    throw new InvalidOperationException($"Backup file '{fullBackupPath}' is not a valid SQLite database (magic header mismatch). Restore aborted to protect target database.");
                }
            }

            var resolvedTarget = ResolveDatabasePath(targetDbPath);
            ValidateNotUserDataDb(resolvedTarget);

            _log.Info($"Restoring database from '{fullBackupPath}' to '{resolvedTarget}'...");

            // Clear connection pools to release open file handles
            SqliteConnection.ClearAllPools();
            await Task.Delay(100);

            // 2. Atomic restore staging: create a temporary backup copy of the target database before replacing it
            bool hasExistingTarget = File.Exists(resolvedTarget);
            string preRestorePath = $"{resolvedTarget}.pre_restore";
            bool preRestoreCreated = false;

            const int maxRetries = 5;
            if (hasExistingTarget)
            {
                for (int retry = 0; retry < maxRetries; retry++)
                {
                    try
                    {
                        File.Copy(resolvedTarget, preRestorePath, overwrite: true);
                        preRestoreCreated = true;
                        break;
                    }
                    catch (IOException ex) when (retry < maxRetries - 1)
                    {
                        _log.Warning($"Pre-restore safety backup retry {retry + 1}/{maxRetries}: {ex.Message}");
                        SqliteConnection.ClearAllPools();
                        await Task.Delay(200 * (retry + 1));
                    }
                }
            }

            bool restoreSucceeded = false;
            try
            {
                for (int retry = 0; retry < maxRetries; retry++)
                {
                    try
                    {
                        File.Copy(fullBackupPath, resolvedTarget, overwrite: true);
                        break;
                    }
                    catch (IOException ex) when (retry < maxRetries - 1)
                    {
                        _log.Warning($"File copy retry {retry + 1}/{maxRetries} during restore: {ex.Message}");
                        SqliteConnection.ClearAllPools();
                        await Task.Delay(200 * (retry + 1));
                    }
                }

                var targetInfo = new FileInfo(resolvedTarget);
                if (!targetInfo.Exists || targetInfo.Length < 100)
                {
                    throw new IOException($"Database restore failed: target file '{resolvedTarget}' is missing or too small ({targetInfo.Length} bytes).");
                }

                // Verify SQLite integrity of restored target
                SqliteConnection.ClearAllPools();
                var verifyConnStr = new SqliteConnectionStringBuilder
                {
                    DataSource = resolvedTarget,
                    Mode = SqliteOpenMode.ReadOnly
                }.ToString();

                await using (var verifyConn = new SqliteConnection(verifyConnStr))
                {
                    await verifyConn.OpenAsync();
                    await using var pragmaCmd = verifyConn.CreateCommand();
                    pragmaCmd.CommandText = "PRAGMA quick_check(1);";
                    var integrity = (string?)await pragmaCmd.ExecuteScalarAsync();
                    if (integrity != "ok")
                    {
                        throw new InvalidOperationException($"Integrity quick_check failed on restored target database: {integrity}");
                    }
                }

                restoreSucceeded = true;
                _log.Info($"Database successfully restored to '{resolvedTarget}' ({targetInfo.Length:N0} bytes)");
                return true;
            }
            catch (Exception restoreEx)
            {
                _log.Error($"Database restore operation failed ({restoreEx.Message}). Rolling back to pre-restore state...");
                if (hasExistingTarget && preRestoreCreated && File.Exists(preRestorePath))
                {
                    try
                    {
                        SqliteConnection.ClearAllPools();
                        File.Copy(preRestorePath, resolvedTarget, overwrite: true);
                        _log.Info($"Successfully rolled back target database to pre-restore state from '{preRestorePath}'.");
                    }
                    catch (Exception rollbackEx)
                    {
                        _log.Error($"Critical failure during rollback to pre-restore backup: {rollbackEx.Message}");
                    }
                }
                throw;
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (restoreSucceeded && File.Exists(preRestorePath))
                {
                    try { File.Delete(preRestorePath); } catch { }
                }
            }
        }

        /// <summary>
        /// Returns a list of existing .bak_* backup files sorted newest first.
        /// </summary>
        public List<string> GetAvailableBackups(string? dbDirectory = null)
        {
            string targetDir;
            if (!string.IsNullOrWhiteSpace(dbDirectory))
            {
                targetDir = Path.GetFullPath(dbDirectory);
            }
            else
            {
                var resolvedDb = ResolveDatabasePath(null);
                targetDir = Path.GetDirectoryName(resolvedDb) ?? string.Empty;
            }

            if (!Directory.Exists(targetDir))
            {
                return new List<string>();
            }

            return Directory.EnumerateFiles(targetDir, "*.bak*", SearchOption.TopDirectoryOnly)
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    if (name.EndsWith("-journal", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith("-shm", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    return name.Contains(".bak_", StringComparison.OrdinalIgnoreCase) ||
                           name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
                })
                .Select(f => new FileInfo(f))
                .Where(fi =>
                {
                    try
                    {
                        return fi.Exists && fi.Length > 0;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .Select(fi => fi.FullName)
                .ToList();
        }
    }
}
