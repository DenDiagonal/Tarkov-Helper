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
    [Collection("DatabaseUpdateServiceCollection")]
    public class ChallengerAdversarialTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _testDbPath;

        public ChallengerAdversarialTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_ChallengerAdv_" + Guid.NewGuid().ToString("N"));
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

        #region 1. Target database pointing to user_data.db (Adversarial Stress)

        [Theory]
        [InlineData("user_data.db")]
        [InlineData("USER_DATA.DB")]
        [InlineData("UserData.db")]
        [InlineData("nested/dir/user_data.db")]
        [InlineData("nested\\dir\\user_data.db")]
        [InlineData("custom_userdata.db")]
        public async Task ApplyDiffAsync_TargetingUserDataDb_ImmediatelyRejects_AndLeavesFileUntouched(string subPath)
        {
            var applier = DatabaseTransactionApplier.Instance;
            var targetPath = Path.Combine(_testDir, subPath);

            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            const string initialContent = "SENSITIVE_PLAYER_PROGRESS:QUESTS_COMPLETED=[1,2,3]";
            await File.WriteAllTextAsync(targetPath, initialContent, Encoding.UTF8);

            var initialSha = ComputeFileSha256(targetPath);
            var initialLength = new FileInfo(targetPath).Length;
            var initialLastModified = File.GetLastWriteTimeUtc(targetPath);

            var diffWithChanges = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "malicious_quest_attempt",
                        QuestName = "Malicious Quest Title"
                    }
                }
            };

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                applier.ApplyDiffAsync(diffWithChanges, targetPath));

            Assert.Contains("user_data.db", ex.Message, StringComparison.OrdinalIgnoreCase);

            // Assert file is strictly untouched
            Assert.True(File.Exists(targetPath));
            var finalLength = new FileInfo(targetPath).Length;
            var finalSha = ComputeFileSha256(targetPath);
            var finalContent = await File.ReadAllTextAsync(targetPath, Encoding.UTF8);
            var finalLastModified = File.GetLastWriteTimeUtc(targetPath);

            Assert.Equal(initialLength, finalLength);
            Assert.Equal(initialSha, finalSha);
            Assert.Equal(initialContent, finalContent);
            Assert.Equal(initialLastModified, finalLastModified);

            // Verify no backup was created
            var backups = Directory.GetFiles(_testDir, "*user_data.db.bak*", SearchOption.AllDirectories);
            Assert.Empty(backups);
        }

        [Fact]
        public async Task ApplyDiffAsync_TargetingUserDataDbBak_IsAlsoRejectedByValidation()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var targetPath = Path.Combine(_testDir, "user_data.db.bak");
            await File.WriteAllTextAsync(targetPath, "BACKUP_DATA", Encoding.UTF8);

            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff { ChangeType = DiffChangeType.Added, QuestId = "q_bak_exploit", QuestName = "Bak Exploit" }
                }
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                applier.ApplyDiffAsync(report, targetPath));
            Assert.Contains("user_data.db", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ApplyDiffAsync_TargetingUserDataDb_WithZeroChanges_BypassesValidation_DocumentedFinding()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var targetPath = Path.Combine(_testDir, "user_data.db");
            await File.WriteAllTextAsync(targetPath, "UNTOUCHED_CONTENT", Encoding.UTF8);

            var emptyReport = new DataDiffReport();

            // When diff has no changes, ApplyDiffAsync returns true without throwing
            // because HasChanges check precedes ValidateNotUserDataDb
            var result = await applier.ApplyDiffAsync(emptyReport, targetPath);

            Assert.True(result);
            Assert.Equal("UNTOUCHED_CONTENT", await File.ReadAllTextAsync(targetPath));
            Assert.Null(applier.LastBackupPath);
        }

        #endregion

        #region 2. Event Listener Exceptions During Hot-Reload

        [Fact]
        public async Task ApplyDiffAsync_DatabaseUpdatedListenerThrows_DbCommitRemainsCommittedAndIntact()
        {
            var applier = DatabaseTransactionApplier.Instance;
            const string questId = "q_listener_resilience_challenger";
            const string questName = "Resilient Quest Under Crashing Listener";

            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = questId,
                        QuestName = questName,
                        NewMinLevel = 35,
                        NewTrader = "Peacekeeper"
                    }
                }
            };

            bool listenerExecuted = false;
            EventHandler explodingHandler = (sender, args) =>
            {
                listenerExecuted = true;
                throw new InvalidOperationException("Exploding third party listener failure");
            };

            DatabaseUpdateService.Instance.DatabaseUpdated += explodingHandler;
            try
            {
                var success = await applier.ApplyDiffAsync(report, _testDbPath);
                Assert.True(success, "ApplyDiffAsync must succeed and return true even if hot-reload listeners throw.");
                Assert.True(listenerExecuted, "Listener must have executed and thrown.");

                // Verify DB was committed and contains the record
                SqliteConnection.ClearAllPools();
                using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Name, MinLevel, Trader FROM Quests WHERE Id = @Id;";
                cmd.Parameters.AddWithValue("@Id", questId);
                using var reader = cmd.ExecuteReader();

                Assert.True(reader.Read());
                Assert.Equal(questName, reader.GetString(0));
                Assert.Equal(35, reader.GetInt32(1));
                Assert.Equal("Peacekeeper", reader.GetString(2));
            }
            finally
            {
                DatabaseUpdateService.Instance.DatabaseUpdated -= explodingHandler;
            }
        }

        [Fact]
        public async Task ApplyDiffAsync_DiffAppliedListenerThrows_DbCommitRemainsCommitted()
        {
            var applier = DatabaseTransactionApplier.Instance;
            const string questId = "q_diffapplied_crash";
            const string questName = "DiffApplied Crash Quest";

            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = questId,
                        QuestName = questName,
                        NewMinLevel = 12
                    }
                }
            };

            EventHandler explodingDiffApplied = (sender, args) =>
            {
                throw new ApplicationException("Exploding DiffApplied handler!");
            };

            applier.DiffApplied += explodingDiffApplied;
            try
            {
                // DiffApplied?.Invoke is invoked after transaction.CommitAsync()
                // If DiffApplied throws, the exception escapes ApplyDiffAsync
                var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
                    applier.ApplyDiffAsync(report, _testDbPath));
                Assert.Equal("Exploding DiffApplied handler!", ex.Message);

                // BUT: Database transaction was ALREADY committed prior to DiffApplied invocation
                SqliteConnection.ClearAllPools();
                using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id = @Id;";
                cmd.Parameters.AddWithValue("@Id", questId);
                var count = Convert.ToInt32(cmd.ExecuteScalar());

                Assert.Equal(1, count); // Commit remains 100% intact!
            }
            finally
            {
                applier.DiffApplied -= explodingDiffApplied;
            }
        }

        #endregion

        #region 3. Concurrency Stress: Concurrent ApplyDiffAsync While In-Flight Reads

        [Fact]
        public async Task ApplyDiffAsync_HeavyConcurrentWritesWithContinuousReads_ZeroCorruptionOrDeadlock()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // Seed baseline quests
            using (var seedConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadWrite"))
            {
                seedConn.Open();
                using var cmd = seedConn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Quests (Id, Name, MinLevel, Trader) VALUES ('seed_q1', 'Seed 1', 1, 'Prapor');
                    INSERT INTO Quests (Id, Name, MinLevel, Trader) VALUES ('seed_q2', 'Seed 2', 5, 'Therapist');
                    INSERT INTO HideoutStations (Id, Name, MaxLevel) VALUES ('seed_station', 'Generator', 3);
                ";
                cmd.ExecuteNonQuery();
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var readerExceptions = new List<Exception>();
            int completedReads = 0;
            var readerLock = new object();

            // 12 continuous concurrent readers querying Quests and HideoutStations
            var readerTasks = Enumerable.Range(0, 12).Select(async readerId =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var csb = new SqliteConnectionStringBuilder
                        {
                            DataSource = _testDbPath,
                            Mode = SqliteOpenMode.ReadOnly,
                            DefaultTimeout = 20
                        };

                        await using var conn = new SqliteConnection(csb.ConnectionString);
                        await conn.OpenAsync(cts.Token);

                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT COUNT(*), AVG(MinLevel) FROM Quests;";
                        await using var reader = await cmd.ExecuteReaderAsync(cts.Token);
                        if (await reader.ReadAsync(cts.Token))
                        {
                            var count = reader.GetInt32(0);
                            Assert.True(count >= 2);
                        }

                        Interlocked.Increment(ref completedReads);
                        await Task.Delay(5, cts.Token);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        lock (readerLock) readerExceptions.Add(ex);
                    }
                }
            }).ToList();

            // 4 concurrent writers with different batches of diffs
            var writerDiffs = Enumerable.Range(1, 4).Select(writerIndex =>
            {
                return new DataDiffReport
                {
                    QuestDiffs = Enumerable.Range(1, 4).Select(qIndex => new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = $"cw_w{writerIndex}_q{qIndex}",
                        QuestName = $"Concurrent Writer {writerIndex} Quest {qIndex}",
                        NewMinLevel = writerIndex * 10 + qIndex,
                        NewTrader = "Prapor"
                    }).ToList()
                };
            }).ToList();

            var writerTasks = writerDiffs.Select(diff => applier.ApplyDiffAsync(diff, _testDbPath)).ToList();
            var results = await Task.WhenAll(writerTasks);

            // Allow readers slightly more time
            await Task.Delay(150);
            cts.Cancel();
            await Task.WhenAll(readerTasks);

            // Assertions
            Assert.All(results, res => Assert.True(res));
            Assert.Empty(readerExceptions);
            Assert.True(completedReads > 30, $"Expected substantial read operations, actual: {completedReads}");

            // Verify integrity
            SqliteConnection.ClearAllPools();
            using var verifyConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            verifyConn.Open();

            using var pragma = verifyConn.CreateCommand();
            pragma.CommandText = "PRAGMA integrity_check;";
            Assert.Equal("ok", pragma.ExecuteScalar());

            using var countCmd = verifyConn.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id LIKE 'cw_w%';";
            Assert.Equal(16, Convert.ToInt32(countCmd.ExecuteScalar())); // 4 writers * 4 quests = 16
        }

        [Fact]
        public async Task ConcurrentWrites_WhenOneFailsConstraint_OtherSucceedsWithoutInterference()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // Valid Diff: inserts a valid quest
            var validDiff = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_valid_concurrent",
                        QuestName = "Valid Concurrent Quest",
                        NewMinLevel = 10
                    }
                }
            };

            // Invalid Diff: inserts a quest objective referencing a non-existent QuestId (FK violation)
            var invalidDiff = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_invalid_fk_parent",
                        QuestName = "Invalid FK Parent",
                        RemoteQuest = new NormalizedQuest
                        {
                            Id = "q_invalid_fk_parent",
                            Name = "Invalid FK Parent",
                            Objectives = new List<string> { "Obj 1" }
                        }
                    }
                },
                QuestRequirementDiffs = new List<QuestRequirementDiff>
                {
                    new QuestRequirementDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "non_existent_foreign_quest_12345", // Violates FK Quests(Id)
                        RequiredQuestId = "also_non_existent_quest_67890",
                        NewRequirementType = "Complete"
                    }
                }
            };

            var validTask = applier.ApplyDiffAsync(validDiff, _testDbPath);
            var invalidTask = Task.Run(() => applier.ApplyDiffAsync(invalidDiff, _testDbPath, resilientPrerequisites: false));

            var validResult = await validTask;
            var invalidResult = await invalidTask;

            Assert.True(validResult, "Valid concurrent transaction must succeed.");
            Assert.False(invalidResult, "Invalid transaction with FK violation must fail and rollback.");

            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            conn.Open();

            // Valid quest must exist
            using var cmd1 = conn.CreateCommand();
            cmd1.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id = 'q_valid_concurrent';";
            Assert.Equal(1, Convert.ToInt32(cmd1.ExecuteScalar()));

            // Invalid quest must NOT exist (rolled back completely)
            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id = 'q_invalid_fk_parent';";
            Assert.Equal(0, Convert.ToInt32(cmd2.ExecuteScalar()));
        }

        #endregion

        #region 4. In-Memory Service State Verification: QuestDbService

        [Fact]
        public async Task QuestDbService_AfterApplyDiff_ImmediatelyReflectsAdditionsModificationsAndDeletions()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var questService = QuestDbService.Instance;

            var dbPathField = typeof(QuestDbService).GetField("_databasePath", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(dbPathField);

            var originalDbPath = (string)dbPathField.GetValue(questService)!;
            try
            {
                // Direct QuestDbService to our test database
                dbPathField.SetValue(questService, _testDbPath);

                var loaded = await questService.LoadQuestsAsync();
                Assert.True(loaded);

                const string testQuestId = "q_live_inmemory_test";
                Assert.Null(questService.GetQuestById(testQuestId));
                Assert.DoesNotContain(questService.GetQuests(), q => q.Ids != null && q.Ids.Contains(testQuestId));

                // 1. ADD QUEST
                var addDiff = new DataDiffReport
                {
                    QuestDiffs = new List<QuestDiff>
                    {
                        new QuestDiff
                        {
                            ChangeType = DiffChangeType.Added,
                            QuestId = testQuestId,
                            QuestName = "Live Memory Quest",
                            NewMinLevel = 22,
                            NewTrader = "Skier",
                            NewKappaRequired = true
                        }
                    }
                };

                var addResult = await applier.ApplyDiffAsync(addDiff, _testDbPath);
                Assert.True(addResult);

                // QuestDbService.GetQuests() immediately returns the new quest
                var allQuests = questService.GetQuests();
                Assert.Contains(allQuests, q => q.Ids != null && q.Ids.Contains(testQuestId));

                var addedQuest = questService.GetQuestById(testQuestId);
                Assert.NotNull(addedQuest);
                Assert.Equal("Live Memory Quest", addedQuest.Name);
                Assert.Equal(22, addedQuest.RequiredLevel);
                Assert.Equal("Skier", addedQuest.Trader);
                Assert.True(addedQuest.ReqKappa);

                // 2. MODIFY QUEST
                var modDiff = new DataDiffReport
                {
                    QuestDiffs = new List<QuestDiff>
                    {
                        new QuestDiff
                        {
                            ChangeType = DiffChangeType.Modified,
                            QuestId = testQuestId,
                            QuestName = "Live Memory Quest Renamed",
                            NewMinLevel = 48,
                            NewTrader = "Ragman",
                            NewKappaRequired = false
                        }
                    }
                };

                var modResult = await applier.ApplyDiffAsync(modDiff, _testDbPath);
                Assert.True(modResult);

                var modifiedQuest = questService.GetQuestById(testQuestId);
                Assert.NotNull(modifiedQuest);
                Assert.Equal("Live Memory Quest Renamed", modifiedQuest.Name);
                Assert.Equal(48, modifiedQuest.RequiredLevel);
                Assert.Equal("Ragman", modifiedQuest.Trader);
                Assert.False(modifiedQuest.ReqKappa);

                // 3. REMOVE QUEST
                var remDiff = new DataDiffReport
                {
                    QuestDiffs = new List<QuestDiff>
                    {
                        new QuestDiff
                        {
                            ChangeType = DiffChangeType.Removed,
                            QuestId = testQuestId,
                            QuestName = "Live Memory Quest Renamed"
                        }
                    }
                };

                var remResult = await applier.ApplyDiffAsync(remDiff, _testDbPath);
                Assert.True(remResult);

                Assert.Null(questService.GetQuestById(testQuestId));
                Assert.DoesNotContain(questService.GetQuests(), q => q.Ids != null && q.Ids.Contains(testQuestId));
            }
            finally
            {
                dbPathField.SetValue(questService, originalDbPath);
                if (File.Exists(originalDbPath))
                {
                    await questService.RefreshAsync();
                }
            }
        }

        #endregion
    }
}
