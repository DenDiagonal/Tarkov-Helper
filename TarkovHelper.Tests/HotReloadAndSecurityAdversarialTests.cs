using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services;
using TarkovHelper.Services.DataAudit;
using Xunit;

namespace TarkovHelper.Tests
{
    public static class QuestDbServiceVerificationExtensions
    {
        /// <summary>
        /// Direct extension method enabling QuestDbService.Instance.GetQuests()
        /// as specified in the test mission contract.
        /// </summary>
        public static IReadOnlyList<TarkovTask> GetQuests(this QuestDbService service)
        {
            return service.AllQuests;
        }
    }

    /// <summary>
    /// Empirical adversarial test harness stress-testing Hot-Reload, Concurrency, and Security boundaries
    /// for DatabaseTransactionApplier and related hot-reload services.
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class HotReloadAndSecurityAdversarialTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _testDbPath;

        public HotReloadAndSecurityAdversarialTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_AdvTx_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _testDbPath = Path.Combine(_testDir, "tarkov_data.db");
            CreateCompleteDatabaseSchema(_testDbPath);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_testDir))
            {
                try { Directory.Delete(_testDir, recursive: true); } catch { }
            }
        }

        #region Helper Setup

        private static void CreateCompleteDatabaseSchema(string path)
        {
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate
            };

            using var conn = new SqliteConnection(csb.ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA foreign_keys = ON;
                PRAGMA journal_mode = WAL;

                CREATE TABLE IF NOT EXISTS Items (
                    Id TEXT PRIMARY KEY,
                    BsgId TEXT,
                    Name TEXT NOT NULL,
                    NameEN TEXT,
                    IconUrl TEXT
                );

                CREATE TABLE IF NOT EXISTS Quests (
                    Id TEXT PRIMARY KEY,
                    BsgId TEXT,
                    Name TEXT NOT NULL,
                    NameEN TEXT,
                    NameKO TEXT,
                    NameJA TEXT,
                    WikiPageLink TEXT,
                    Trader TEXT,
                    Location TEXT,
                    MinLevel INTEGER,
                    MinLevelApproved INTEGER NOT NULL DEFAULT 0,
                    MinLevelApprovedAt TEXT,
                    MinScavKarma INTEGER,
                    MinScavKarmaApproved INTEGER NOT NULL DEFAULT 0,
                    MinScavKarmaApprovedAt TEXT,
                    UpdatedAt TEXT,
                    KappaRequired INTEGER NOT NULL DEFAULT 0,
                    Faction TEXT,
                    IsApproved INTEGER NOT NULL DEFAULT 0,
                    ApprovedAt TEXT,
                    RequiredEdition TEXT,
                    RequiredEditionApproved INTEGER NOT NULL DEFAULT 0,
                    RequiredEditionApprovedAt TEXT,
                    ExcludedEdition TEXT,
                    ExcludedEditionApproved INTEGER NOT NULL DEFAULT 0,
                    ExcludedEditionApprovedAt TEXT,
                    RequiredDecodeCount INTEGER,
                    RequiredDecodeCountApproved INTEGER NOT NULL DEFAULT 0,
                    RequiredDecodeCountApprovedAt TEXT,
                    RequiredPrestigeLevel INTEGER,
                    RequiredPrestigeLevelApproved INTEGER NOT NULL DEFAULT 0,
                    RequiredPrestigeLevelApprovedAt TEXT
                );

                CREATE TABLE IF NOT EXISTS QuestObjectives (
                    Id TEXT PRIMARY KEY,
                    QuestId TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    ObjectiveType TEXT NOT NULL DEFAULT 'Custom',
                    Description TEXT NOT NULL,
                    TargetType TEXT,
                    TargetCount INTEGER,
                    ItemId TEXT,
                    ItemName TEXT,
                    RequiresFIR INTEGER NOT NULL DEFAULT 0,
                    MapName TEXT,
                    LocationName TEXT,
                    LocationPoints TEXT,
                    Conditions TEXT,
                    ContentHash TEXT,
                    IsApproved INTEGER NOT NULL DEFAULT 0,
                    ApprovedAt TEXT,
                    UpdatedAt TEXT,
                    OptionalPoints TEXT,
                    DogtagMinLevel INTEGER,
                    DogtagFaction TEXT,
                    FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE,
                    FOREIGN KEY (ItemId) REFERENCES Items(Id) ON DELETE SET NULL
                );

                CREATE TABLE IF NOT EXISTS QuestRequirements (
                    Id TEXT PRIMARY KEY,
                    QuestId TEXT NOT NULL,
                    RequiredQuestId TEXT NOT NULL,
                    RequirementType TEXT NOT NULL DEFAULT 'Complete',
                    DelayMinutes INTEGER,
                    GroupId INTEGER NOT NULL DEFAULT 0,
                    ContentHash TEXT,
                    IsApproved INTEGER NOT NULL DEFAULT 0,
                    ApprovedAt TEXT,
                    UpdatedAt TEXT,
                    FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE,
                    FOREIGN KEY (RequiredQuestId) REFERENCES Quests(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS QuestRequiredItems (
                    Id TEXT PRIMARY KEY,
                    QuestId TEXT NOT NULL,
                    ItemId TEXT,
                    ItemName TEXT NOT NULL,
                    Count INTEGER NOT NULL DEFAULT 1,
                    RequiresFIR INTEGER NOT NULL DEFAULT 0,
                    RequirementType TEXT NOT NULL DEFAULT 'Required',
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    DogtagMinLevel INTEGER,
                    ContentHash TEXT,
                    IsApproved INTEGER NOT NULL DEFAULT 0,
                    ApprovedAt TEXT,
                    UpdatedAt TEXT,
                    DogtagFaction TEXT,
                    FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE,
                    FOREIGN KEY (ItemId) REFERENCES Items(Id) ON DELETE SET NULL
                );

                CREATE TABLE IF NOT EXISTS HideoutStations (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    NameKO TEXT,
                    NameJA TEXT,
                    NormalizedName TEXT,
                    ImageLink TEXT,
                    MaxLevel INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT
                );

                CREATE TABLE IF NOT EXISTS HideoutLevels (
                    Id TEXT PRIMARY KEY,
                    StationId TEXT NOT NULL,
                    Level INTEGER NOT NULL,
                    ConstructionTime INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT,
                    FOREIGN KEY (StationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS HideoutItemRequirements (
                    Id TEXT PRIMARY KEY,
                    StationId TEXT NOT NULL,
                    Level INTEGER NOT NULL,
                    ItemId TEXT NOT NULL,
                    ItemName TEXT NOT NULL,
                    ItemNameKO TEXT,
                    ItemNameJA TEXT,
                    IconLink TEXT,
                    Count INTEGER NOT NULL DEFAULT 1,
                    FoundInRaid INTEGER NOT NULL DEFAULT 0,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT,
                    FOREIGN KEY (StationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE
                );

                -- Initial Seed Items & Prereq Quests
                INSERT INTO Items (Id, BsgId, Name, NameEN) VALUES ('item_salewa', '544fb2574bdc2df7738b4567', 'Salewa first aid kit', 'Salewa first aid kit');
                INSERT INTO Items (Id, BsgId, Name, NameEN) VALUES ('item_roubles', '5449016a4bdc2d6f028b456f', 'Roubles', 'Roubles');
                INSERT INTO Quests (Id, BsgId, Name, Trader, Location, MinLevel, KappaRequired, Faction)
                VALUES ('quest_prereq', 'bsg_prereq', 'Initial Prereq Quest', 'Prapor', 'Customs', 1, 1, 'Any');
            ";
            cmd.ExecuteNonQuery();
        }

        private static string ComputeFileSha256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        #endregion

        #region 1. Security & user_data.db Protection Boundary Tests

        [Theory]
        [InlineData("user_data.db")]
        [InlineData("UserData.db")]
        [InlineData("USER_DATA.DB")]
        [InlineData("subfolder\\user_data.db")]
        [InlineData("subfolder/user_data.db")]
        [InlineData("my_userdata.db")]
        public async Task ApplyDiffAsync_TargetingUserDataDb_ThrowsInvalidOperationException_AndLeavesFileUntouched(string subPath)
        {
            var applier = DatabaseTransactionApplier.Instance;
            var targetPath = Path.Combine(_testDir, subPath);

            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            const string confidentialUserData = "{\"PlayerProgress\":{\"Level\":52,\"Inventory\":[\"LedX\",\"GPU\"],\"QuestsCompleted\":[\"The_Collector\"]}}";
            await File.WriteAllTextAsync(targetPath, confidentialUserData, Encoding.UTF8);

            var initialLength = new FileInfo(targetPath).Length;
            var initialHash = ComputeFileSha256(targetPath);
            var initialLastWrite = File.GetLastWriteTimeUtc(targetPath);

            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "malicious_injected_quest",
                        QuestName = "Should Never Be Applied"
                    }
                }
            };

            // Act & Assert: Must throw InvalidOperationException
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => applier.ApplyDiffAsync(report, targetPath));
            Assert.Contains("user_data.db", ex.Message, StringComparison.OrdinalIgnoreCase);

            // Verify the file was STRICTLY UNTOUCHED
            Assert.True(File.Exists(targetPath), "user_data.db must still exist.");
            var finalLength = new FileInfo(targetPath).Length;
            var finalHash = ComputeFileSha256(targetPath);
            var finalContent = await File.ReadAllTextAsync(targetPath, Encoding.UTF8);
            var finalLastWrite = File.GetLastWriteTimeUtc(targetPath);

            Assert.Equal(initialLength, finalLength);
            Assert.Equal(initialHash, finalHash);
            Assert.Equal(confidentialUserData, finalContent);
            Assert.Equal(initialLastWrite, finalLastWrite);

            // Verify no backup file was created in that directory
            var backupFiles = Directory.GetFiles(_testDir, "*user_data.db.bak*", SearchOption.AllDirectories);
            Assert.Empty(backupFiles);
        }

        #endregion

        #region 2. Hot-Reload Event Listener Exception Resilience Tests

        [Fact]
        public async Task ApplyDiffAsync_WhenDatabaseUpdatedListenerThrows_TransactionRemainsCommittedAndIntact()
        {
            var applier = DatabaseTransactionApplier.Instance;
            const string testQuestId = "q_listener_resilience_test";
            const string testQuestName = "Listener Crash Test Quest";
            const int expectedMinLevel = 45;

            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = testQuestId,
                        QuestName = testQuestName,
                        NewMinLevel = expectedMinLevel,
                        NewTrader = "Prapor"
                    }
                }
            };

            bool listenerExecuted = false;
            EventHandler explodingHandler = (sender, args) =>
            {
                listenerExecuted = true;
                throw new InvalidOperationException("Simulated catastrophic crash inside third-party event listener!");
            };

            // Subscribe the throwing handler
            DatabaseUpdateService.Instance.DatabaseUpdated += explodingHandler;
            try
            {
                // Act: ApplyDiffAsync should successfully complete and NOT rollback the transaction
                var result = await applier.ApplyDiffAsync(report, _testDbPath);
                Assert.True(result, "ApplyDiffAsync must return true even if external event listeners throw.");
                Assert.True(listenerExecuted, "Exploding event handler must have been executed.");

                // Assert: The database commit MUST remain 100% intact
                SqliteConnection.ClearAllPools();
                using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Name, MinLevel, Trader FROM Quests WHERE Id = @Id;";
                cmd.Parameters.AddWithValue("@Id", testQuestId);

                using var reader = cmd.ExecuteReader();
                Assert.True(reader.Read(), "Quest record must be committed to the database.");
                Assert.Equal(testQuestName, reader.GetString(0));
                Assert.Equal(expectedMinLevel, reader.GetInt32(1));
                Assert.Equal("Prapor", reader.GetString(2));
            }
            finally
            {
                DatabaseUpdateService.Instance.DatabaseUpdated -= explodingHandler;
            }
        }

        [Fact]
        public async Task ApplyDiffAsync_WhenMultipleListenersThrow_AllListenersExecuteAndDbIntegrityPreserved()
        {
            var applier = DatabaseTransactionApplier.Instance;
            const string testQuestId = "q_multi_faulty_listeners";

            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = testQuestId,
                        QuestName = "Multi Listener Quest",
                        NewMinLevel = 18
                    }
                }
            };

            int listener1Calls = 0;
            int listener2Calls = 0;

            EventHandler faultyHandler1 = (s, e) =>
            {
                listener1Calls++;
                throw new ApplicationException("Faulty handler 1 failure");
            };

            EventHandler faultyHandler2 = (s, e) =>
            {
                listener2Calls++;
                throw new TimeoutException("Faulty handler 2 timeout");
            };

            DatabaseUpdateService.Instance.DatabaseUpdated += faultyHandler1;
            DatabaseUpdateService.Instance.DatabaseUpdated += faultyHandler2;
            try
            {
                var result = await applier.ApplyDiffAsync(report, _testDbPath);
                Assert.True(result);

                // Verify DB was modified
                SqliteConnection.ClearAllPools();
                using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id = @Id;";
                cmd.Parameters.AddWithValue("@Id", testQuestId);
                var count = Convert.ToInt32(cmd.ExecuteScalar());
                Assert.Equal(1, count);
            }
            finally
            {
                DatabaseUpdateService.Instance.DatabaseUpdated -= faultyHandler1;
                DatabaseUpdateService.Instance.DatabaseUpdated -= faultyHandler2;
            }
        }

        #endregion

        #region 3. Concurrency Stress Harness: Writes While Reads In-Flight

        [Fact]
        public async Task ApplyDiffAsync_ConcurrentWritesWhileReadsInFlight_ExecutesSafelyWithoutDeadlockOrCorruption()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // Seed initial records for readers to query
            using (var seedConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadWrite"))
            {
                seedConn.Open();
                using var seedCmd = seedConn.CreateCommand();
                seedCmd.CommandText = @"
                    INSERT INTO HideoutStations (Id, Name, MaxLevel, UpdatedAt) VALUES ('st_1', 'Vents', 3, '2026-01-01');
                    INSERT INTO Quests (Id, Name, MinLevel) VALUES ('q_seed_1', 'Seed Quest 1', 5);
                    INSERT INTO Quests (Id, Name, MinLevel) VALUES ('q_seed_2', 'Seed Quest 2', 10);
                ";
                seedCmd.ExecuteNonQuery();
            }

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var readerExceptions = new List<Exception>();
            var readCount = 0;
            var readerLock = new object();

            // Spawn 10 concurrent continuous reader tasks
            var readerTasks = Enumerable.Range(0, 10).Select(async readerId =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var csb = new SqliteConnectionStringBuilder
                        {
                            DataSource = _testDbPath,
                            Mode = SqliteOpenMode.ReadOnly,
                            DefaultTimeout = 15
                        };

                        await using var conn = new SqliteConnection(csb.ConnectionString);
                        await conn.OpenAsync(cts.Token);

                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT COUNT(*), MAX(MinLevel) FROM Quests;";
                        await using var reader = await cmd.ExecuteReaderAsync(cts.Token);
                        if (await reader.ReadAsync(cts.Token))
                        {
                            var count = reader.GetInt32(0);
                            Assert.True(count >= 2);
                        }

                        // Also query HideoutStations
                        await using var cmd2 = conn.CreateCommand();
                        cmd2.CommandText = "SELECT COUNT(*) FROM HideoutStations;";
                        var stationCount = Convert.ToInt32(await cmd2.ExecuteScalarAsync(cts.Token));
                        Assert.True(stationCount >= 1);

                        Interlocked.Increment(ref readCount);
                        await Task.Delay(10, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        lock (readerLock)
                        {
                            readerExceptions.Add(ex);
                        }
                    }
                }
            }).ToList();

            // Prepare 3 concurrent writer diffs
            var diff1 = new DataDiffReport
            {
                QuestDiffs = Enumerable.Range(1, 5).Select(i => new QuestDiff
                {
                    ChangeType = DiffChangeType.Added,
                    QuestId = $"concurrent_w1_q_{i}",
                    QuestName = $"Concurrent W1 Quest {i}",
                    NewMinLevel = i * 2,
                    NewTrader = "Prapor"
                }).ToList()
            };

            var diff2 = new DataDiffReport
            {
                QuestDiffs = Enumerable.Range(1, 5).Select(i => new QuestDiff
                {
                    ChangeType = DiffChangeType.Added,
                    QuestId = $"concurrent_w2_q_{i}",
                    QuestName = $"Concurrent W2 Quest {i}",
                    NewMinLevel = i * 3,
                    NewTrader = "Therapist"
                }).ToList()
            };

            var diff3 = new DataDiffReport
            {
                HideoutStationDiffs = new List<HideoutStationDiff>
                {
                    new HideoutStationDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        StationId = "concurrent_st_medstation",
                        StationName = "Medstation",
                        NewMaxLevel = 3
                    }
                },
                HideoutLevelDiffs = new List<HideoutLevelDiff>
                {
                    new HideoutLevelDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        StationId = "concurrent_st_medstation",
                        Level = 1,
                        NewConstructionTime = 1200
                    }
                }
            };

            // Act: Launch writers concurrently while readers are actively in-flight
            var writerTask1 = applier.ApplyDiffAsync(diff1, _testDbPath);
            var writerTask2 = applier.ApplyDiffAsync(diff2, _testDbPath);
            var writerTask3 = applier.ApplyDiffAsync(diff3, _testDbPath);

            var writerResults = await Task.WhenAll(writerTask1, writerTask2, writerTask3);

            // Allow readers a moment more, then stop them
            await Task.Delay(200);
            cts.Cancel();
            await Task.WhenAll(readerTasks);

            // Assert: All writers succeeded
            Assert.All(writerResults, res => Assert.True(res));
            Assert.Empty(readerExceptions);
            Assert.True(readCount > 20, $"Expected substantial concurrent reads, got {readCount}");

            // Verify database integrity and complete persistence
            SqliteConnection.ClearAllPools();
            using var verifyConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            verifyConn.Open();

            using var pragmaCmd = verifyConn.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA integrity_check;";
            var integrityResult = (string?)pragmaCmd.ExecuteScalar();
            Assert.Equal("ok", integrityResult);

            using var countCmd = verifyConn.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id LIKE 'concurrent_w%';";
            var writtenQuestsCount = Convert.ToInt32(countCmd.ExecuteScalar());
            Assert.Equal(10, writtenQuestsCount);

            using var stationCmd = verifyConn.CreateCommand();
            stationCmd.CommandText = "SELECT COUNT(*) FROM HideoutStations WHERE Id = 'concurrent_st_medstation';";
            Assert.Equal(1, Convert.ToInt32(stationCmd.ExecuteScalar()));
        }

        #endregion

        #region 4. In-Memory Service State Verification: QuestDbService Hot-Reload

        [Fact]
        public async Task ApplyDiffAsync_RefreshesQuestDbServiceInMemory_ImmediatelyReturningUpdatedRecordsWithoutRestart()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var questService = QuestDbService.Instance;

            // Use reflection to temporarily point QuestDbService._databasePath to our test database
            var dbPathField = typeof(QuestDbService).GetField("_databasePath", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(dbPathField);

            var originalDbPath = (string)dbPathField.GetValue(questService)!;
            try
            {
                dbPathField.SetValue(questService, _testDbPath);

                // Initial baseline load
                var initialLoad = await questService.LoadQuestsAsync();
                Assert.True(initialLoad);

                const string targetQuestId = "q_hot_reload_verify";
                Assert.Null(questService.GetQuestById(targetQuestId));
                Assert.DoesNotContain(questService.GetQuests(), q => q.Ids != null && q.Ids.Contains(targetQuestId));

                // 1. ADD NEW QUEST via Diff Engine Applier
                var addReport = new DataDiffReport
                {
                    QuestDiffs = new List<QuestDiff>
                    {
                        new QuestDiff
                        {
                            ChangeType = DiffChangeType.Added,
                            QuestId = targetQuestId,
                            QuestName = "Dynamic In-Memory Quest",
                            NewMinLevel = 28,
                            NewTrader = "Mechanic",
                            NewKappaRequired = true
                        }
                    }
                };

                // Act 1: Apply diff to target database
                var addSuccess = await applier.ApplyDiffAsync(addReport, _testDbPath);
                Assert.True(addSuccess);

                // Assert 1: QuestDbService immediately contains the new record without app restart
                var allQuestsAfterAdd = questService.GetQuests();
                Assert.Contains(allQuestsAfterAdd, q => q.Ids != null && q.Ids.Contains(targetQuestId));

                var addedQuest = questService.GetQuestById(targetQuestId);
                Assert.NotNull(addedQuest);
                Assert.Equal("Dynamic In-Memory Quest", addedQuest.Name);
                Assert.Equal(28, addedQuest.RequiredLevel);
                Assert.Equal("Mechanic", addedQuest.Trader);
                Assert.True(addedQuest.ReqKappa);

                // 2. MODIFY EXISTING QUEST via Diff Engine Applier
                var modifyReport = new DataDiffReport
                {
                    QuestDiffs = new List<QuestDiff>
                    {
                        new QuestDiff
                        {
                            ChangeType = DiffChangeType.Modified,
                            QuestId = targetQuestId,
                            QuestName = "Dynamic In-Memory Quest",
                            NewMinLevel = 58,
                            NewTrader = "Jaeger",
                            NewKappaRequired = false
                        }
                    }
                };

                // Act 2: Apply modification diff
                var modSuccess = await applier.ApplyDiffAsync(modifyReport, _testDbPath);
                Assert.True(modSuccess);

                // Assert 2: QuestDbService immediately reflects updated fields without restart
                var modifiedQuest = questService.GetQuestById(targetQuestId);
                Assert.NotNull(modifiedQuest);
                Assert.Equal(58, modifiedQuest.RequiredLevel);
                Assert.Equal("Jaeger", modifiedQuest.Trader);
                Assert.False(modifiedQuest.ReqKappa);

                // 3. REMOVE QUEST via Diff Engine Applier
                var removeReport = new DataDiffReport
                {
                    QuestDiffs = new List<QuestDiff>
                    {
                        new QuestDiff
                        {
                            ChangeType = DiffChangeType.Removed,
                            QuestId = targetQuestId,
                            QuestName = "Dynamic In-Memory Quest"
                        }
                    }
                };

                // Act 3: Apply removal diff
                var remSuccess = await applier.ApplyDiffAsync(removeReport, _testDbPath);
                Assert.True(remSuccess);

                // Assert 3: Quest is immediately gone from in-memory cache
                Assert.Null(questService.GetQuestById(targetQuestId));
                Assert.DoesNotContain(questService.GetQuests(), q => q.Ids != null && q.Ids.Contains(targetQuestId));
            }
            finally
            {
                // Restore original database path and refresh
                dbPathField.SetValue(questService, originalDbPath);
                if (File.Exists(originalDbPath))
                {
                    await questService.RefreshAsync();
                }
            }
        }

        #endregion

        #region 5. Cancellation & Empty Diff Edge Cases

        [Fact]
        public async Task ApplyDiffAsync_WithCancelledToken_RollsBackCleanlyAndThrowsOperationCanceledException()
        {
            var applier = DatabaseTransactionApplier.Instance;
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Immediately cancelled

            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_cancelled_test",
                        QuestName = "Cancelled Quest"
                    }
                }
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => applier.ApplyDiffAsync(report, _testDbPath, cts.Token));

            // Verify database has no changes
            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id = 'q_cancelled_test';";
            Assert.Equal(0, Convert.ToInt32(cmd.ExecuteScalar()));
        }

        [Fact]
        public async Task ApplyDiffAsync_WithEmptyDiff_ReturnsTrueWithoutCreatingBackup()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var emptyReport = new DataDiffReport();

            var success = await applier.ApplyDiffAsync(emptyReport, _testDbPath);
            Assert.True(success);
            Assert.Null(applier.LastBackupPath);
        }

        #endregion
    }
}
