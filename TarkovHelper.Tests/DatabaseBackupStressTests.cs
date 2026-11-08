using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Services.DataAudit;
using Xunit;

namespace TarkovHelper.Tests
{
    public class DatabaseBackupStressTests
    {
        private static string GetProductionDatabasePath()
        {
            return DatabaseBackupManager.ResolveDatabasePath(null);
        }

        private static async Task CreateSampleSqliteDatabaseAsync(string path, int rowCount = 50)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

            await using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE TestEntities (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Value INTEGER NOT NULL,
                    CreatedUtc TEXT NOT NULL
                );
                CREATE TABLE TestMetadata (
                    Key TEXT PRIMARY KEY,
                    Val TEXT NOT NULL
                );
                INSERT INTO TestMetadata VALUES ('Version', '1.0');
            ";
            await cmd.ExecuteNonQueryAsync();

            await using var tx = conn.BeginTransaction();
            for (int i = 1; i <= rowCount; i++)
            {
                await using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = "INSERT INTO TestEntities (Name, Value, CreatedUtc) VALUES ($n, $v, $d);";
                insertCmd.Parameters.AddWithValue("$n", $"Entity_{i}");
                insertCmd.Parameters.AddWithValue("$v", i * 10);
                insertCmd.Parameters.AddWithValue("$d", DateTime.UtcNow.ToString("o"));
                await insertCmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();

            SqliteConnection.ClearAllPools();
        }

        #region 1. Concurrent Database Reads During Backup

        [Fact]
        public async Task CreateBackupAsync_UnderHeavyConcurrentReads_SucceedsAndProducesConsistentBackup()
        {
            var dbPath = GetProductionDatabasePath();
            Assert.True(File.Exists(dbPath), $"Production database missing at {dbPath}");

            var backupManager = DatabaseBackupManager.Instance;
            using var cts = new CancellationTokenSource();
            var readExceptions = new List<Exception>();
            int completedReadQueries = 0;

            // Spin up 8 concurrent reader tasks querying the production database continuously
            var readerTasks = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
            {
                var connStr = new SqliteConnectionStringBuilder
                {
                    DataSource = dbPath,
                    Mode = SqliteOpenMode.ReadOnly
                }.ToString();

                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await using var conn = new SqliteConnection(connStr);
                        await conn.OpenAsync(cts.Token);

                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT COUNT(*), Trader FROM Quests GROUP BY Trader;";
                        await using var reader = await cmd.ExecuteReaderAsync(cts.Token);
                        while (await reader.ReadAsync(cts.Token))
                        {
                            _ = reader.GetInt32(0);
                            _ = reader.GetString(1);
                        }

                        Interlocked.Increment(ref completedReadQueries);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        lock (readExceptions)
                        {
                            readExceptions.Add(ex);
                        }
                    }

                    await Task.Delay(10);
                }
            })).ToList();

            string? backupPath = null;
            try
            {
                // Ensure readers have started executing
                await Task.Delay(150);

                // Execute backup under concurrent load
                backupPath = await backupManager.CreateBackupAsync(dbPath);

                Assert.NotNull(backupPath);
                Assert.True(File.Exists(backupPath), $"Backup file was not created: {backupPath}");

                // Cancel readers and await completion
                cts.Cancel();
                await Task.WhenAll(readerTasks);

                Assert.Empty(readExceptions);
                Assert.True(completedReadQueries > 10, $"Expected > 10 reads during backup, but got {completedReadQueries}");

                // Verify backup integrity with PRAGMA integrity_check
                var backupConnStr = new SqliteConnectionStringBuilder
                {
                    DataSource = backupPath,
                    Mode = SqliteOpenMode.ReadOnly
                }.ToString();

                await using var backupConn = new SqliteConnection(backupConnStr);
                await backupConn.OpenAsync();

                await using var pragmaCmd = backupConn.CreateCommand();
                pragmaCmd.CommandText = "PRAGMA integrity_check;";
                var integrityResult = (string?)await pragmaCmd.ExecuteScalarAsync();
                Assert.Equal("ok", integrityResult);

                await using var countCmd = backupConn.CreateCommand();
                countCmd.CommandText = "SELECT COUNT(*) FROM Quests;";
                var questCount = (long?)await countCmd.ExecuteScalarAsync();
                Assert.True(questCount >= 535, $"Expected at least 535 quests, got {questCount}");
            }
            finally
            {
                cts.Cancel();
                SqliteConnection.ClearAllPools();
                if (backupPath != null && File.Exists(backupPath))
                {
                    try { File.Delete(backupPath); } catch { }
                }
            }
        }

        #endregion

        #region 2. Sequential vs Concurrent Multiple Backup Invocations

        [Fact]
        public async Task CreateBackupAsync_SequentialMultipleInvocationsInSameSecond_CreatesDistinctNumberedBackups()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_Stress_SeqBackup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var dbPath = Path.Combine(tempDir, "tarkov_data.db");
            await CreateSampleSqliteDatabaseAsync(dbPath, 20);

            var backupManager = DatabaseBackupManager.Instance;

            try
            {
                // Trigger 3 sequential backups rapidly
                var bkp1 = await backupManager.CreateBackupAsync(dbPath);
                var bkp2 = await backupManager.CreateBackupAsync(dbPath);
                var bkp3 = await backupManager.CreateBackupAsync(dbPath);

                Assert.True(File.Exists(bkp1));
                Assert.True(File.Exists(bkp2));
                Assert.True(File.Exists(bkp3));

                // All 3 paths should be distinct
                var set = new HashSet<string> { bkp1, bkp2, bkp3 };
                Assert.Equal(3, set.Count);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public async Task CreateBackupAsync_ConcurrentInvocationsInSameSecond_SynchronizesAndCreatesDistinctBackupsWithoutConflict()
        {
            // Thread-safe backup synchronization with SemaphoreSlim:
            // When multiple concurrent calls run CreateBackupAsync at the exact same millisecond,
            // the calls are serialized. All 5 backups succeed without collision, and each receives a unique path.
            var tempDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_Stress_MultiBackup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var dbPath = Path.Combine(tempDir, "tarkov_data.db");
            await CreateSampleSqliteDatabaseAsync(dbPath, 30);

            var backupManager = DatabaseBackupManager.Instance;

            try
            {
                var exceptions = new List<Exception>();
                var successfulPaths = new List<string>();

                var tasks = Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
                {
                    try
                    {
                        var path = await backupManager.CreateBackupAsync(dbPath);
                        lock (successfulPaths) successfulPaths.Add(path);
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions) exceptions.Add(ex);
                    }
                })).ToList();

                await Task.WhenAll(tasks);

                Assert.Empty(exceptions);
                Assert.Equal(5, successfulPaths.Count);
                var distinctPaths = new HashSet<string>(successfulPaths);
                Assert.Equal(5, distinctPaths.Count);
                foreach (var path in successfulPaths)
                {
                    Assert.True(File.Exists(path));
                    Assert.True(new FileInfo(path).Length > 0);
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        #endregion

        #region 3. Restore with Active Connection Handles & Locks

        [Fact]
        public async Task RestoreBackupAsync_WhenTargetHasPooledConnection_SuccessfullyRestores()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_Stress_RestorePool_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var targetDb = Path.Combine(tempDir, "target.db");
                var backupDb = Path.Combine(tempDir, "target.db.bak_20261021_120000");

                // Target DB has 10 rows
                await CreateSampleSqliteDatabaseAsync(targetDb, 10);

                // Backup DB has 50 rows
                await CreateSampleSqliteDatabaseAsync(backupDb, 50);

                // Open and close a connection to targetDb so it sits in the ADO.NET connection pool
                var targetConnStr = $"Data Source={targetDb};";
                using (var pooledConn = new SqliteConnection(targetConnStr))
                {
                    pooledConn.Open();
                    using var cmd = pooledConn.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM TestEntities;";
                    var count = (long?)cmd.ExecuteScalar();
                    Assert.Equal(10, count);
                } // Closed, but pooled!

                // Now execute restore
                var backupManager = DatabaseBackupManager.Instance;
                var restored = await backupManager.RestoreBackupAsync(backupDb, targetDb);
                Assert.True(restored);

                // Verify targetDb now has 50 rows
                using (var verifyConn = new SqliteConnection(targetConnStr))
                {
                    verifyConn.Open();
                    using var cmd = verifyConn.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM TestEntities;";
                    var count = (long?)cmd.ExecuteScalar();
                    Assert.Equal(50, count);
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public async Task RestoreBackupAsync_WhenTargetHasBrieflyLockedHandle_RetriesAndSucceeds()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_Stress_RestoreRetry_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var targetDb = Path.Combine(tempDir, "target.db");
                var backupDb = Path.Combine(tempDir, "target.db.bak_20261021_120000");

                await CreateSampleSqliteDatabaseAsync(targetDb, 5);
                await CreateSampleSqliteDatabaseAsync(backupDb, 25);

                var targetConnStr = $"Data Source={targetDb};Pooling=False;";

                // Hold an unpooled open connection with an exclusive transaction for 250ms in a background task
                var lockTask = Task.Run(async () =>
                {
                    await using var activeConn = new SqliteConnection(targetConnStr);
                    await activeConn.OpenAsync();
                    await using var tx = activeConn.BeginTransaction();
                    await Task.Delay(250); // Lock held during the first attempt of RestoreBackupAsync
                    await tx.RollbackAsync();
                });

                // Small delay to ensure background task locked the connection
                await Task.Delay(50);

                var backupManager = DatabaseBackupManager.Instance;
                var restored = await backupManager.RestoreBackupAsync(backupDb, targetDb);

                await lockTask;

                Assert.True(restored);

                // Verify target has 25 rows
                using (var verifyConn = new SqliteConnection($"Data Source={targetDb};"))
                {
                    verifyConn.Open();
                    using var cmd = verifyConn.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM TestEntities;";
                    var count = (long?)cmd.ExecuteScalar();
                    Assert.Equal(25, count);
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public async Task RestoreBackupAsync_WhenTargetIsPermanentlyLocked_ThrowsIOExceptionAfterRetries()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_Stress_RestoreLocked_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var targetDb = Path.Combine(tempDir, "target.db");
                var backupDb = Path.Combine(tempDir, "target.db.bak_20261021_120000");

                await CreateSampleSqliteDatabaseAsync(targetDb, 5);
                await CreateSampleSqliteDatabaseAsync(backupDb, 25);

                // Lock the target file with FileShare.None
                using (var fileLock = new FileStream(targetDb, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var backupManager = DatabaseBackupManager.Instance;

                    // Should retry 5 times and then throw IOException
                    var ex = await Assert.ThrowsAsync<IOException>(() =>
                        backupManager.RestoreBackupAsync(backupDb, targetDb));

                    Assert.NotNull(ex);
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        #endregion

        #region 4. Invalid Paths & Adversarial Inputs

        [Fact]
        public async Task CreateBackupAsync_NonExistentSourceFile_ThrowsFileNotFoundException()
        {
            var backupManager = DatabaseBackupManager.Instance;
            var nonExistentPath = Path.Combine(Path.GetTempPath(), "non_existent_" + Guid.NewGuid().ToString("N"), "missing.db");

            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                backupManager.CreateBackupAsync(nonExistentPath));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        public async Task RestoreBackupAsync_EmptyOrWhitespaceBackupPath_ThrowsArgumentException(string invalidPath)
        {
            var backupManager = DatabaseBackupManager.Instance;

            await Assert.ThrowsAsync<ArgumentException>(() =>
                backupManager.RestoreBackupAsync(invalidPath, "target.db"));
        }

        [Fact]
        public async Task RestoreBackupAsync_NonExistentBackupFile_ThrowsFileNotFoundException()
        {
            var backupManager = DatabaseBackupManager.Instance;
            var nonExistentBackup = Path.Combine(Path.GetTempPath(), "missing_backup_" + Guid.NewGuid().ToString("N") + ".bak");

            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                backupManager.RestoreBackupAsync(nonExistentBackup, "target.db"));
        }

        #endregion

        #region 5. Corrupted Files, Zero-Byte Files & Non-Atomic Restore Failures

        [Fact]
        public async Task CreateBackupAsync_WhenSourceIsZeroBytes_ThrowsInvalidOperationExceptionAndAborts()
        {
            // When the source file exists but is 0 bytes (e.g. truncated), CreateBackupAsync
            // validates file length > 0 and throws InvalidOperationException instead of creating an empty DB.
            var tempDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_Stress_ZeroByteSource_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var zeroByteDb = Path.Combine(tempDir, "empty_source.db");
                File.WriteAllBytes(zeroByteDb, Array.Empty<byte>());

                var backupManager = DatabaseBackupManager.Instance;
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    backupManager.CreateBackupAsync(zeroByteDb));

                Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void GetAvailableBackups_FiltersOutZeroByteFiles_AndDemonstratesExtensionMatchingBehavior()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_Stress_GetBackups_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var validDatedBackup = Path.Combine(tempDir, "tarkov_data.db.bak_20261021_100000");
                var validBakExtension = Path.Combine(tempDir, "tarkov_data.db.bak");
                var zeroByteBackup = Path.Combine(tempDir, "tarkov_data.db.bak_20261021_090000");
                var irrelevantFile = Path.Combine(tempDir, "notes.txt");
                var tempFileWithBakUnderscore = Path.Combine(tempDir, "tarkov_data.db.bak_temp.tmp");

                File.WriteAllText(validDatedBackup, "valid backup data 1");
                File.SetLastWriteTimeUtc(validDatedBackup, DateTime.UtcNow.AddMinutes(-10));

                File.WriteAllText(validBakExtension, "valid backup data 2");
                File.SetLastWriteTimeUtc(validBakExtension, DateTime.UtcNow.AddMinutes(-5));

                File.WriteAllBytes(zeroByteBackup, Array.Empty<byte>()); // 0 bytes!
                File.WriteAllText(irrelevantFile, "not a backup");
                File.WriteAllText(tempFileWithBakUnderscore, "temporary file with .bak_ in name");

                var backupManager = DatabaseBackupManager.Instance;
                var backups = backupManager.GetAvailableBackups(tempDir);

                // 0-byte file is successfully filtered out
                Assert.DoesNotContain(zeroByteBackup, backups);

                // Irrelevant notes.txt is successfully filtered out
                Assert.DoesNotContain(irrelevantFile, backups);

                // Valid files are included
                Assert.Contains(validDatedBackup, backups);
                Assert.Contains(validBakExtension, backups);

                // EMPIRICAL FINDING: Because GetAvailableBackups checks `name.Contains(".bak_")`,
                // any temporary or partial file matching that substring (like .bak_temp.tmp) is also included:
                Assert.Contains(tempFileWithBakUnderscore, backups);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void GetAvailableBackups_NonExistentDirectory_ReturnsEmptyList()
        {
            var backupManager = DatabaseBackupManager.Instance;
            var nonExistentDir = Path.Combine(Path.GetTempPath(), "non_existent_dir_" + Guid.NewGuid().ToString("N"));

            var results = backupManager.GetAvailableBackups(nonExistentDir);
            Assert.NotNull(results);
            Assert.Empty(results);
        }

        [Fact]
        public async Task RestoreBackupAsync_WhenBackupIsZeroBytes_ThrowsInvalidOperationExceptionAndProtectsTargetDb()
        {
            // Pre-validation protects target database:
            // When restoring a 0-byte backup file, RestoreBackupAsync validates backupInfo.Length >= 100
            // and throws InvalidOperationException BEFORE touching the target database.
            // Target database remains intact with its original size and data.
            var tempDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_Stress_ZeroByteRestore_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var targetDb = Path.Combine(tempDir, "target.db");
                var zeroByteBackup = Path.Combine(tempDir, "target.db.bak_20261021_000000");

                // Target DB has 10 valid rows
                await CreateSampleSqliteDatabaseAsync(targetDb, 10);
                var originalTargetLength = new FileInfo(targetDb).Length;
                Assert.True(originalTargetLength > 0);

                File.WriteAllBytes(zeroByteBackup, Array.Empty<byte>());

                var backupManager = DatabaseBackupManager.Instance;

                // Restoring a 0-byte backup throws InvalidOperationException
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    backupManager.RestoreBackupAsync(zeroByteBackup, targetDb));
                Assert.Contains("Invalid or incomplete backup file", ex.Message, StringComparison.OrdinalIgnoreCase);

                // Healthy target database is completely preserved
                var targetAfterRestore = new FileInfo(targetDb);
                Assert.True(targetAfterRestore.Exists);
                Assert.Equal(originalTargetLength, targetAfterRestore.Length);

                // Target data is queryable and has all 10 rows
                var connStr = $"Data Source={targetDb};";
                using var conn = new SqliteConnection(connStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM TestEntities;";
                var count = (long?)cmd.ExecuteScalar();
                Assert.Equal(10, count);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public async Task RestoreBackupAsync_WhenBackupIsCorruptedTextFile_ThrowsInvalidOperationExceptionAndProtectsTargetDb()
        {
            // Magic header pre-validation protects target database:
            // When restoring a file that does not have the SQLite magic header ("SQLite format 3\0"),
            // RestoreBackupAsync validates the header and throws InvalidOperationException before modifying target.
            // Target database remains intact and completely queryable.
            var tempDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_Stress_CorruptRestore_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var targetDb = Path.Combine(tempDir, "target.db");
                var corruptBackup = Path.Combine(tempDir, "target.db.bak_20261021_120000");

                await CreateSampleSqliteDatabaseAsync(targetDb, 10);
                var originalTargetLength = new FileInfo(targetDb).Length;

                // Create a corrupt text file that is >= 100 bytes to test magic header verification specifically
                File.WriteAllText(corruptBackup, "THIS IS CORRUPT TEXT DATA NOT A SQLITE DATABASE. Padding with extra characters to exceed 100 bytes for magic header test.");

                var backupManager = DatabaseBackupManager.Instance;

                // Throws InvalidOperationException due to magic header mismatch
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    backupManager.RestoreBackupAsync(corruptBackup, targetDb));
                Assert.Contains("magic header mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);

                // Healthy target database is completely preserved and queryable
                var targetAfterRestore = new FileInfo(targetDb);
                Assert.True(targetAfterRestore.Exists);
                Assert.Equal(originalTargetLength, targetAfterRestore.Length);

                var connStr = $"Data Source={targetDb};";
                using var conn = new SqliteConnection(connStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM TestEntities;";
                var count = (long?)cmd.ExecuteScalar();
                Assert.Equal(10, count);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public async Task RestoreBackupAsync_WhenPostCopyIntegrityCheckFails_RollsBackToPreRestoreState()
        {
            // Atomic staging rollback:
            // If a backup file passes magic header verification (first 16 bytes match "SQLite format 3\0"),
            // but is internally corrupt so PRAGMA quick_check fails, RestoreBackupAsync catches the failure,
            // rolls back the target database from .pre_restore, and rethrows.
            var tempDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_Stress_Rollback_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var targetDb = Path.Combine(tempDir, "target.db");
                var pseudoValidBackup = Path.Combine(tempDir, "target.db.bak_20261021_120000");

                // Target DB has 10 valid rows
                await CreateSampleSqliteDatabaseAsync(targetDb, 10);
                var originalTargetLength = new FileInfo(targetDb).Length;

                // Build a file that starts with "SQLite format 3\0" (16 bytes) but contains 512 bytes of corrupted zeros
                byte[] pseudoHeader = new byte[512];
                byte[] magic = System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0");
                Array.Copy(magic, pseudoHeader, 16);
                File.WriteAllBytes(pseudoValidBackup, pseudoHeader);

                var backupManager = DatabaseBackupManager.Instance;

                // Throws SQLite or InvalidOperationException during integrity check
                await Assert.ThrowsAnyAsync<Exception>(() =>
                    backupManager.RestoreBackupAsync(pseudoValidBackup, targetDb));

                // Target database was rolled back from .pre_restore and still has all 10 rows
                var targetAfter = new FileInfo(targetDb);
                Assert.True(targetAfter.Exists);
                Assert.Equal(originalTargetLength, targetAfter.Length);

                var connStr = $"Data Source={targetDb};";
                using var conn = new SqliteConnection(connStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM TestEntities;";
                var count = (long?)cmd.ExecuteScalar();
                Assert.Equal(10, count);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        #endregion

        #region 6. Strict Protection of user_data.db (Adversarial Path Probes)

        [Theory]
        [InlineData("user_data.db")]
        [InlineData("USER_DATA.DB")]
        [InlineData("UserData.db")]
        [InlineData("userdata.db")]
        [InlineData(@"C:\Users\test\AppData\Roaming\TarkovHelper\Config\user_data.db")]
        [InlineData(@"C:/Users/test/AppData/Roaming/TarkovHelper/Config/user_data.db")]
        [InlineData(@"Config\user_data.db")]
        [InlineData(@"Config/user_data.db")]
        [InlineData(@"..\..\user_data.db")]
        [InlineData(@"Assets\..\Config\user_data.db")]
        [InlineData(@"C:\Data\userdata.db")]
        [InlineData(@"./user_data.db")]
        public async Task DatabaseBackupManager_RejectsAllUserDataDbVariants(string maliciousPath)
        {
            var backupManager = DatabaseBackupManager.Instance;

            // 1. CreateBackupAsync with user_data.db as source
            var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                backupManager.CreateBackupAsync(maliciousPath));
            Assert.Contains("user_data.db", ex1.Message, StringComparison.OrdinalIgnoreCase);

            // 2. RestoreBackupAsync with user_data.db as target
            var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                backupManager.RestoreBackupAsync("some_valid_backup.bak", maliciousPath));
            Assert.Contains("user_data.db", ex2.Message, StringComparison.OrdinalIgnoreCase);

            // 3. RestoreBackupAsync with user_data.db as backup source
            var ex3 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                backupManager.RestoreBackupAsync(maliciousPath, "tarkov_data.db"));
            Assert.Contains("user_data.db", ex3.Message, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region 7. Full Cycle Backup & Restore with PRAGMA Integrity Check

        [Fact]
        public async Task FullCycle_BackupAndRestore_PreservesIntegrityAndAllRows()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_Stress_FullCycle_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var dbPath = Path.Combine(tempDir, "tarkov_data.db");
                await CreateSampleSqliteDatabaseAsync(dbPath, 50);

                var backupManager = DatabaseBackupManager.Instance;

                // 1. Create backup
                var backupPath = await backupManager.CreateBackupAsync(dbPath);
                Assert.NotNull(backupPath);
                Assert.True(File.Exists(backupPath));

                // 2. Mutate the original database (insert new rows, delete old rows)
                var connStr = $"Data Source={dbPath};";
                await using (var conn = new SqliteConnection(connStr))
                {
                    await conn.OpenAsync();
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "DELETE FROM TestEntities WHERE Id <= 25; INSERT INTO TestEntities (Name, Value, CreatedUtc) VALUES ('MutatedEntity', 9999, 'now');";
                    await cmd.ExecuteNonQueryAsync();

                    // Verify mutation occurred
                    cmd.CommandText = "SELECT COUNT(*) FROM TestEntities;";
                    var mutatedCount = (long?)await cmd.ExecuteScalarAsync();
                    Assert.Equal(26, mutatedCount); // 25 left + 1 inserted
                }

                SqliteConnection.ClearAllPools();

                // 3. Restore from backup
                var restored = await backupManager.RestoreBackupAsync(backupPath, dbPath);
                Assert.True(restored);

                // 4. Verify original state restored exactly (50 original rows, no MutatedEntity)
                await using (var verifyConn = new SqliteConnection(connStr))
                {
                    await verifyConn.OpenAsync();

                    await using var pragmaCmd = verifyConn.CreateCommand();
                    pragmaCmd.CommandText = "PRAGMA integrity_check;";
                    var integrity = (string?)await pragmaCmd.ExecuteScalarAsync();
                    Assert.Equal("ok", integrity);

                    await using var countCmd = verifyConn.CreateCommand();
                    countCmd.CommandText = "SELECT COUNT(*) FROM TestEntities;";
                    var restoredCount = (long?)await countCmd.ExecuteScalarAsync();
                    Assert.Equal(50, restoredCount);

                    await using var searchCmd = verifyConn.CreateCommand();
                    searchCmd.CommandText = "SELECT COUNT(*) FROM TestEntities WHERE Name = 'MutatedEntity';";
                    var mutatedFound = (long?)await searchCmd.ExecuteScalarAsync();
                    Assert.Equal(0, mutatedFound);
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        #endregion
    }
}
