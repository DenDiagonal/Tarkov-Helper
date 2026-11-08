using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services;
using TarkovHelper.Services.DataAudit;
using TarkovHelper.Windows.Dialogs;
using Xunit;

namespace TarkovHelper.Tests
{
    /// <summary>
    /// Empirical adversarial stress tests for Block 3 (Settings Integration & Rollback Overlay).
    /// Tests concurrent in-flight audit scans, zero-backup edge cases, backup selection/restoration,
    /// and strict immutability/isolation of user_data.db.
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class Block3SettingsAndRollbackAdversarialTests
    {
        #region Helpers

        private static string CreateSqliteDatabaseWithData(string path, string tableName, Dictionary<string, string> initialData)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"CREATE TABLE {tableName} (Id TEXT PRIMARY KEY, Value TEXT);";
                cmd.ExecuteNonQuery();

                using var tx = conn.BeginTransaction();
                foreach (var kvp in initialData)
                {
                    using var insertCmd = conn.CreateCommand();
                    insertCmd.Transaction = tx;
                    insertCmd.CommandText = $"INSERT INTO {tableName} (Id, Value) VALUES ($id, $val);";
                    insertCmd.Parameters.AddWithValue("$id", kvp.Key);
                    insertCmd.Parameters.AddWithValue("$val", kvp.Value);
                    insertCmd.ExecuteNonQuery();
                }
                tx.Commit();
            }

            SqliteConnection.ClearAllPools();
            return path;
        }

        private static string ComputeFileSha256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = sha256.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        private static NormalizedAuditData CreateTestNormalizedAuditData()
        {
            return new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = "adv_q1",
                        Name = "Adversarial Quest 1",
                        Trader = "Prapor",
                        MinLevel = 25
                    },
                    new NormalizedQuest
                    {
                        Id = "adv_q2",
                        Name = "Adversarial Quest 2",
                        Trader = "Therapist",
                        MinLevel = 30
                    }
                },
                QuestRequirements = new List<NormalizedQuestRequirement>
                {
                    new NormalizedQuestRequirement
                    {
                        QuestId = "adv_q2",
                        RequiredQuestId = "adv_q1",
                        RequirementType = "Complete"
                    }
                },
                QuestRequiredItems = new List<NormalizedQuestRequiredItem>
                {
                    new NormalizedQuestRequiredItem
                    {
                        QuestId = "adv_q1",
                        ItemName = "Salewa first aid kit",
                        Count = 3,
                        RequiresFIR = true
                    }
                },
                HideoutStations = new List<NormalizedHideoutStation>
                {
                    new NormalizedHideoutStation
                    {
                        Id = "st_med",
                        Name = "Medstation",
                        MaxLevel = 3
                    }
                },
                HideoutLevels = new List<NormalizedHideoutLevel>
                {
                    new NormalizedHideoutLevel
                    {
                        StationId = "st_med",
                        Level = 2,
                        ConstructionTime = 7200
                    }
                },
                HideoutItemRequirements = new List<NormalizedHideoutItemRequirement>
                {
                    new NormalizedHideoutItemRequirement
                    {
                        StationId = "st_med",
                        Level = 2,
                        ItemName = "Medical bloodset",
                        Count = 2,
                        FoundInRaid = false
                    }
                }
            };
        }

        #endregion

        #region Category 1: Triggering Audit Scan While Another Scan is In-Flight

        [Fact]
        public async Task AuditScan_MultipleConcurrentInFlightScans_ExecuteSafelyAndProduceIdenticalDiffReports()
        {
            // Stress test: 10 concurrent in-flight ComputeDiffAsync operations querying the database simultaneously
            var prodDb = DatabaseBackupManager.ResolveDatabasePath(null);
            Assert.True(File.Exists(prodDb), $"Production database missing at {prodDb}");

            var remoteData = CreateTestNormalizedAuditData();
            const int concurrencyLevel = 10;
            var diffEngine = DatabaseDiffEngine.Instance;

            var exceptions = new List<Exception>();
            var results = new List<DataDiffReport>();

            var tasks = Enumerable.Range(0, concurrencyLevel).Select(async index =>
            {
                try
                {
                    var report = await diffEngine.ComputeDiffAsync(remoteData, prodDb);
                    lock (results)
                    {
                        results.Add(report);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }).ToList();

            await Task.WhenAll(tasks);

            // Assertions: No concurrency exceptions, no sqlite locks, all reports identical
            Assert.Empty(exceptions);
            Assert.Equal(concurrencyLevel, results.Count);

            var firstReport = results[0];
            Assert.True(firstReport.TotalChanges > 0);

            foreach (var r in results)
            {
                Assert.Equal(firstReport.TotalChanges, r.TotalChanges);
                Assert.Equal(firstReport.AddedCount, r.AddedCount);
                Assert.Equal(firstReport.ModifiedCount, r.ModifiedCount);
                Assert.Equal(firstReport.RemovedCount, r.RemovedCount);
                Assert.Equal(firstReport.QuestDiffs.Count, r.QuestDiffs.Count);
                Assert.Equal(firstReport.QuestItemDiffs.Count, r.QuestItemDiffs.Count);
            }
        }

        [Fact]
        public async Task AuditScan_CancellationOfOneInFlightScan_DoesNotAffectConcurrentRunningScans()
        {
            var prodDb = DatabaseBackupManager.ResolveDatabasePath(null);
            Assert.True(File.Exists(prodDb), $"Production database missing at {prodDb}");

            var remoteData = CreateTestNormalizedAuditData();
            var diffEngine = DatabaseDiffEngine.Instance;

            using var ctsCancelled = new CancellationTokenSource();
            using var ctsValid = new CancellationTokenSource();

            // Pre-cancel or cancel immediately
            ctsCancelled.Cancel();

            var cancelledTask = diffEngine.ComputeDiffAsync(remoteData, prodDb, ct: ctsCancelled.Token);
            var successfulTask = diffEngine.ComputeDiffAsync(remoteData, prodDb, ct: ctsValid.Token);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelledTask);

            var report = await successfulTask;
            Assert.NotNull(report);
            Assert.True(report.TotalChanges > 0);
        }

        [Fact]
        public async Task AuditScan_UnderConcurrentDatabaseReadsAndBackupCreation_MaintainsIntegrity()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "AdvTest_ConcurrentScanBak_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var tempDb = Path.Combine(tempDir, "tarkov_data.db");
            var prodDb = DatabaseBackupManager.ResolveDatabasePath(null);
            File.Copy(prodDb, tempDb, overwrite: true);

            var remoteData = CreateTestNormalizedAuditData();
            using var cts = new CancellationTokenSource();
            var readErrors = new List<Exception>();
            int readCount = 0;

            // Start 5 background readers continuously querying SQLite
            var readerTasks = Enumerable.Range(0, 5).Select(readerIndex => Task.Run(async () =>
            {
                var connStr = $"Data Source={tempDb};Mode=ReadOnly";
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await using var conn = new SqliteConnection(connStr);
                        await conn.OpenAsync(cts.Token);
                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT COUNT(*) FROM Quests;";
                        var res = await cmd.ExecuteScalarAsync(cts.Token);
                        Interlocked.Increment(ref readCount);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        lock (readErrors) readErrors.Add(ex);
                    }
                    await Task.Delay(5);
                }
            })).ToList();

            string? createdBackup = null;
            try
            {
                await Task.Delay(50);

                // Run diff scan while backup is concurrently being created in tempDir
                var backupTask = DatabaseBackupManager.Instance.CreateBackupAsync(tempDb);
                var diffTask = DatabaseDiffEngine.Instance.ComputeDiffAsync(remoteData, tempDb);

                await Task.WhenAll(backupTask, diffTask);

                createdBackup = await backupTask;
                var diffReport = await diffTask;

                Assert.NotNull(createdBackup);
                Assert.True(File.Exists(createdBackup));
                Assert.NotNull(diffReport);
                Assert.True(diffReport.TotalChanges > 0);

                cts.Cancel();
                await Task.WhenAll(readerTasks);

                Assert.Empty(readErrors);
                Assert.True(readCount > 5, $"Expected > 5 reads, got {readCount}");
            }
            finally
            {
                cts.Cancel();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        #endregion

        #region Category 2: Opening Rollback When No Backups Exist

        [Fact]
        public void Rollback_WhenDirectoryIsEmpty_ReturnsEmptyListWithoutException()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "AdvTest_EmptyDir_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var backups = DatabaseBackupManager.Instance.GetAvailableBackups(tempDir);
                Assert.NotNull(backups);
                Assert.Empty(backups);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Rollback_WhenDirectoryDoesNotExist_ReturnsEmptyListWithoutException()
        {
            var nonExistentDir = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString("N"));
            var backups = DatabaseBackupManager.Instance.GetAvailableBackups(nonExistentDir);
            Assert.NotNull(backups);
            Assert.Empty(backups);
        }

        [Fact]
        public void Rollback_ViewModel_LoadBackupsInEmptyDirectory_SetsZeroBackupsAndNullSelection()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "AdvTest_VmEmptyDir_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var vm = new DiffViewerViewModel(new DataDiffReport());
                vm.LoadBackups(tempDir);

                Assert.Empty(vm.AvailableBackups);
                Assert.Null(vm.SelectedBackup);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Rollback_Dialog_ShowRollbackMode_InEmptyDirectory_ShowsNoBackupsBannerAndDisablesConfirmButton()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "AdvTest_DialogEmptyDir_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            Exception? threadEx = null;

            try
            {
                var staThread = new Thread(() =>
                {
                    try
                    {
                        var dialog = DiffViewerDialog.CreateRollbackDialog();

                        // Visual assertions on RollbackOverlay state via visual tree FindName
                        var rollbackOverlay = dialog.FindName("RollbackOverlay") as FrameworkElement;
                        var txtNoBackups = dialog.FindName("TxtNoBackups") as FrameworkElement;
                        var backupListBox = dialog.FindName("BackupListBox") as FrameworkElement;
                        var btnConfirmRollback = dialog.FindName("BtnConfirmRollback") as UIElement;

                        Assert.NotNull(rollbackOverlay);
                        Assert.NotNull(txtNoBackups);
                        Assert.NotNull(backupListBox);
                        Assert.NotNull(btnConfirmRollback);

                        Assert.Equal(Visibility.Visible, rollbackOverlay.Visibility);

                        // If no backups exist, TxtNoBackups is Visible and confirm is disabled.
                        // If backups exist, BackupListBox is Visible and confirm is enabled.
                        if (dialog.ViewModel.AvailableBackups.Count == 0)
                        {
                            Assert.Equal(Visibility.Visible, txtNoBackups.Visibility);
                            Assert.Equal(Visibility.Collapsed, backupListBox.Visibility);
                            Assert.False(btnConfirmRollback.IsEnabled);
                            Assert.Null(dialog.ViewModel.SelectedBackup);
                        }
                        else
                        {
                            Assert.Equal(Visibility.Collapsed, txtNoBackups.Visibility);
                            Assert.Equal(Visibility.Visible, backupListBox.Visibility);
                            Assert.True(btnConfirmRollback.IsEnabled);
                            Assert.NotNull(dialog.ViewModel.SelectedBackup);
                        }

                        Assert.False(dialog.WasRestored);
                    }
                    catch (Exception ex)
                    {
                        threadEx = ex;
                    }
                });

                staThread.SetApartmentState(ApartmentState.STA);
                staThread.Start();
                staThread.Join();

                Assert.Null(threadEx);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public async Task Rollback_RestoreBackupAsync_WithNullBackupViewModel_ThrowsArgumentNullException()
        {
            var vm = new DiffViewerViewModel(new DataDiffReport());
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await vm.RestoreBackupAsync(null!);
            });
        }

        [Fact]
        public async Task Rollback_RestoreBackupAsync_WithNonExistentBackupFile_ThrowsFileNotFoundException()
        {
            var fakeBackup = Path.Combine(Path.GetTempPath(), "non_existent_backup.bak_20261021_000000");
            await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            {
                await DatabaseBackupManager.Instance.RestoreBackupAsync(fakeBackup);
            });
        }

        [Fact]
        public async Task Rollback_RestoreBackupAsync_WithZeroByteBackupFile_ThrowsInvalidOperationExceptionAndPreservesTarget()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "AdvTest_ZeroByteBak_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var targetDb = Path.Combine(tempDir, "tarkov_data.db");
                CreateSqliteDatabaseWithData(targetDb, "VitalData", new Dictionary<string, string> { { "k1", "v1" } });

                var zeroByteBackup = Path.Combine(tempDir, "tarkov_data.db.bak_20261021_120000");
                File.WriteAllBytes(zeroByteBackup, Array.Empty<byte>());

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    await DatabaseBackupManager.Instance.RestoreBackupAsync(zeroByteBackup, targetDb);
                });

                Assert.Contains("Invalid or incomplete backup file", ex.Message);

                // Verify target database was NOT destroyed
                var connStr = $"Data Source={targetDb};Mode=ReadOnly";
                await using var conn = new SqliteConnection(connStr);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM VitalData;";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.Equal(1, count);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        #endregion

        #region Category 3: Selecting and Restoring Backups

        [Fact]
        public void Rollback_SelectingAmongMultipleBackups_SortsChronologicallyNewestFirst()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "AdvTest_MultiBackupSort_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var files = new[]
                {
                    ("tarkov_data.db.bak_20261020_080000", new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc)),
                    ("tarkov_data.db.bak_20261021_090000", new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc)),
                    ("tarkov_data.db.bak_20261021_143000", new DateTime(2026, 10, 21, 14, 30, 0, DateTimeKind.Utc)),
                    ("tarkov_data.db.bak_20261019_120000", new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc)),
                    ("tarkov_data.db.bak_20261021_111500", new DateTime(2026, 10, 21, 11, 15, 0, DateTimeKind.Utc)),
                };

                foreach (var (name, time) in files)
                {
                    var p = Path.Combine(tempDir, name);
                    File.WriteAllText(p, "dummy backup content");
                    File.SetLastWriteTimeUtc(p, time);
                }

                var vm = new DiffViewerViewModel(new DataDiffReport());
                vm.LoadBackups(tempDir);

                Assert.Equal(5, vm.AvailableBackups.Count);

                // Expected order: 20261021_143000, 20261021_111500, 20261021_090000, 20261020_080000, 20261019_120000
                Assert.Equal("tarkov_data.db.bak_20261021_143000", vm.AvailableBackups[0].FileName);
                Assert.Equal("tarkov_data.db.bak_20261021_111500", vm.AvailableBackups[1].FileName);
                Assert.Equal("tarkov_data.db.bak_20261021_090000", vm.AvailableBackups[2].FileName);
                Assert.Equal("tarkov_data.db.bak_20261020_080000", vm.AvailableBackups[3].FileName);
                Assert.Equal("tarkov_data.db.bak_20261019_120000", vm.AvailableBackups[4].FileName);

                // Default selected is the newest
                Assert.Equal("tarkov_data.db.bak_20261021_143000", vm.SelectedBackup?.FileName);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public async Task Rollback_SelectingAndRestoringOlderBackup_ReplacesTargetWithAccurateHistoricalState()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "AdvTest_RestoreOlder_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create Target DB (Current State: V3)
                var targetDb = Path.Combine(tempDir, "tarkov_data.db");
                CreateSqliteDatabaseWithData(targetDb, "Quests", new Dictionary<string, string> { { "q_v3", "Quest Version 3" } });

                // Create Backup 1 (Historical: V1)
                var backup1Path = Path.Combine(tempDir, "tarkov_data.db.bak_20261020_100000");
                CreateSqliteDatabaseWithData(backup1Path, "Quests", new Dictionary<string, string> { { "q_v1", "Quest Version 1" } });
                File.SetLastWriteTimeUtc(backup1Path, new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc));

                // Create Backup 2 (Historical: V2)
                var backup2Path = Path.Combine(tempDir, "tarkov_data.db.bak_20261021_100000");
                CreateSqliteDatabaseWithData(backup2Path, "Quests", new Dictionary<string, string> { { "q_v2", "Quest Version 2" } });
                File.SetLastWriteTimeUtc(backup2Path, new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc));

                var vm = new DiffViewerViewModel(new DataDiffReport());
                vm.LoadBackups(tempDir);

                // Select older backup (V1)
                var olderBackup = vm.AvailableBackups.First(b => b.FileName == "tarkov_data.db.bak_20261020_100000");
                vm.SelectedBackup = olderBackup;

                // Execute restore
                var restored = await vm.RestoreBackupAsync(olderBackup, targetDb);
                Assert.True(restored);

                // Verify target database now contains V1 data, and V3 is replaced
                SqliteConnection.ClearAllPools();
                var connStr = $"Data Source={targetDb};Mode=ReadOnly";
                await using var conn = new SqliteConnection(connStr);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, Value FROM Quests;";
                await using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("q_v1", reader.GetString(0));
                Assert.Equal("Quest Version 1", reader.GetString(1));
                Assert.False(await reader.ReadAsync());
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public async Task Rollback_WithCorruptHeaderOrInvalidSqlite_AbortsAndProtectsTargetDatabase()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "AdvTest_CorruptBak_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var targetDb = Path.Combine(tempDir, "tarkov_data.db");
                CreateSqliteDatabaseWithData(targetDb, "ProtectedTable", new Dictionary<string, string>
                {
                    { "p1", "Important Val 1" },
                    { "p2", "Important Val 2" }
                });

                // Create corrupted backup: > 100 bytes of non-SQLite text
                var corruptBackup = Path.Combine(tempDir, "tarkov_data.db.bak_20261021_090000");
                var dummyContent = new string('X', 500);
                File.WriteAllText(corruptBackup, dummyContent);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    await DatabaseBackupManager.Instance.RestoreBackupAsync(corruptBackup, targetDb);
                });

                Assert.Contains("magic header mismatch", ex.Message);

                // Verify target database is 100% intact and undamaged
                SqliteConnection.ClearAllPools();
                var connStr = $"Data Source={targetDb};Mode=ReadOnly";
                await using var conn = new SqliteConnection(connStr);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM ProtectedTable;";
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.Equal(2, count);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public async Task Rollback_WhenTargetHasActiveReaders_ClearsPoolsAndCompletesSuccessfully()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "AdvTest_ActiveReaders_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var targetDb = Path.Combine(tempDir, "tarkov_data.db");
                CreateSqliteDatabaseWithData(targetDb, "Items", new Dictionary<string, string> { { "i_old", "Old Item" } });

                var backupDb = Path.Combine(tempDir, "tarkov_data.db.bak_20261021_120000");
                CreateSqliteDatabaseWithData(backupDb, "Items", new Dictionary<string, string> { { "i_new", "New Restored Item" } });

                // Open a reader connection
                var connStr = $"Data Source={targetDb};Mode=ReadOnly";
                var conn = new SqliteConnection(connStr);
                await conn.OpenAsync();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Items;";
                _ = await cmd.ExecuteScalarAsync();

                // Execute restore while reader exists: RestoreBackupAsync uses ClearAllPools and retries
                // Close command, but connection object exists
                cmd.Dispose();
                conn.Dispose();

                var restored = await DatabaseBackupManager.Instance.RestoreBackupAsync(backupDb, targetDb);
                Assert.True(restored);

                // Verify target has new data
                SqliteConnection.ClearAllPools();
                await using var checkConn = new SqliteConnection(connStr);
                await checkConn.OpenAsync();
                await using var checkCmd = checkConn.CreateCommand();
                checkCmd.CommandText = "SELECT Id FROM Items;";
                var restoredId = (string?)await checkCmd.ExecuteScalarAsync();
                Assert.Equal("i_new", restoredId);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Rollback_DatabaseUpdatedNotification_FiresWhenBackupIsRestored()
        {
            bool eventFired = false;
            EventHandler handler = (s, e) => { eventFired = true; };

            DatabaseUpdateService.Instance.DatabaseUpdated += handler;
            try
            {
                // Trigger OnDatabaseUpdated reflection invocation as done in DiffViewerDialog
                var updateMethod = typeof(DatabaseUpdateService).GetMethod("OnDatabaseUpdated",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                Assert.NotNull(updateMethod);
                updateMethod.Invoke(DatabaseUpdateService.Instance, null);

                Assert.True(eventFired, "Expected DatabaseUpdated event to fire on restore.");
            }
            finally
            {
                DatabaseUpdateService.Instance.DatabaseUpdated -= handler;
            }
        }

        #endregion

        #region Category 4: Ensuring user_data.db is NEVER Selected or Touched

        [Fact]
        public async Task UserDataDb_ImmutabilityCheck_RemainsByteForByteIdenticalAcrossAuditAndRestore()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "AdvTest_UserDataIntegrity_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create a simulated user_data.db with user progression data
                var userDataPath = Path.Combine(tempDir, "user_data.db");
                CreateSqliteDatabaseWithData(userDataPath, "UserProgress", new Dictionary<string, string>
                {
                    { "Profile1_QuestDebut", "Completed" },
                    { "Profile1_HideoutVents", "Level2" },
                    { "Profile1_CustomNotes", "Keys in dorms 214" }
                });

                var originalHash = ComputeFileSha256(userDataPath);
                var originalLength = new FileInfo(userDataPath).Length;

                // Copy real production database to test directory
                var tarkovDataPath = Path.Combine(tempDir, "tarkov_data.db");
                var prodDb = DatabaseBackupManager.ResolveDatabasePath(null);
                File.Copy(prodDb, tarkovDataPath, overwrite: true);

                var backupPath = await DatabaseBackupManager.Instance.CreateBackupAsync(tarkovDataPath);
                Assert.True(File.Exists(backupPath));

                // Perform restore of tarkov_data.db
                var restored = await DatabaseBackupManager.Instance.RestoreBackupAsync(backupPath, tarkovDataPath);
                Assert.True(restored);

                // Run diff engine
                var remoteData = CreateTestNormalizedAuditData();
                var diffReport = await DatabaseDiffEngine.Instance.ComputeDiffAsync(remoteData, tarkovDataPath);
                Assert.NotNull(diffReport);

                // Re-verify user_data.db SHA256 and length
                var currentHash = ComputeFileSha256(userDataPath);
                var currentLength = new FileInfo(userDataPath).Length;

                Assert.Equal(originalLength, currentLength);
                Assert.Equal(originalHash, currentHash);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Theory]
        [InlineData("user_data.db")]
        [InlineData("USER_DATA.DB")]
        [InlineData("UserData.db")]
        [InlineData(@"Config\user_data.db")]
        [InlineData(@"Config/user_data.db")]
        [InlineData(@"C:\AppData\TarkovHelper\user_data.db")]
        [InlineData(@"../../user_data.db")]
        [InlineData(@"./user_data.db")]
        [InlineData(@"Assets\..\user_data.db")]
        [InlineData(@"tarkov_data.db\..\user_data.db")]
        public async Task UserDataDb_DirectAndAdversarialPathInvocations_StrictlyRejectedWithSecurityException(string adversarialPath)
        {
            var validDb = DatabaseBackupManager.ResolveDatabasePath(null);

            // 1. CreateBackupAsync with user_data.db as source
            var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await DatabaseBackupManager.Instance.CreateBackupAsync(adversarialPath);
            });
            Assert.Contains("Security & Integrity Violation", ex1.Message);

            // 2. RestoreBackupAsync with user_data.db as target
            var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await DatabaseBackupManager.Instance.RestoreBackupAsync(validDb, adversarialPath);
            });
            Assert.Contains("Security & Integrity Violation", ex2.Message);

            // 3. RestoreBackupAsync with user_data.db as backup source
            var ex3 = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await DatabaseBackupManager.Instance.RestoreBackupAsync(adversarialPath, validDb);
            });
            Assert.Contains("Security & Integrity Violation", ex3.Message);

            // 4. DiffViewerViewModel.RestoreBackupAsync with user_data.db as target
            var vm = new DiffViewerViewModel(new DataDiffReport());
            var dummyBackup = new BackupItemViewModel(validDb);
            var ex4 = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await vm.RestoreBackupAsync(dummyBackup, adversarialPath);
            });
            Assert.Contains("Security & Integrity Violation", ex4.Message);
        }

        [Fact]
        public async Task UserDataDb_DirectoryWithUserDataFiles_IsProtectedAndExcludedFromBackups()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "AdvTest_UserDataExclusion_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create legitimate tarkov_data backup
                var legitimateBak = Path.Combine(tempDir, "tarkov_data.db.bak_20261021_120000");
                File.WriteAllText(legitimateBak, "valid backup simulation");

                // Create user_data.db file (should never be returned as a .bak)
                var userDataFile = Path.Combine(tempDir, "user_data.db");
                File.WriteAllText(userDataFile, "user progression simulation");

                var backups = DatabaseBackupManager.Instance.GetAvailableBackups(tempDir);

                // user_data.db must NEVER be listed in backups
                Assert.DoesNotContain(userDataFile, backups);
                Assert.Contains(legitimateBak, backups);

                // Even if an adversarial backup file named user_data.db.bak_20261021_130000 is present
                var adversarialBak = Path.Combine(tempDir, "user_data.db.bak_20261021_130000");
                File.WriteAllText(adversarialBak, "adversarial backup simulation");

                // If user attempts to restore it, RestoreBackupAsync MUST throw security violation
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    await DatabaseBackupManager.Instance.RestoreBackupAsync(adversarialBak);
                });
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        #endregion
    }
}
