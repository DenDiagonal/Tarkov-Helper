using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public class TarkovTrackerPipelineIntegrationTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _testDbPath;

        public TarkovTrackerPipelineIntegrationTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_PipelineTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _testDbPath = Path.Combine(_testDir, "tarkov_data.db");
            CreateCompleteTestDatabase(_testDbPath);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_testDir))
            {
                try { Directory.Delete(_testDir, recursive: true); } catch { }
            }
        }

        #region Helper Database Creation

        private static void CreateCompleteTestDatabase(string path)
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

                CREATE TABLE Items (
                    Id TEXT PRIMARY KEY,
                    BsgId TEXT,
                    Name TEXT NOT NULL,
                    NameEN TEXT,
                    IconUrl TEXT
                );

                CREATE TABLE Quests (
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

                CREATE TABLE QuestObjectives (
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
                    OriginalLocationPoints TEXT,
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

                CREATE TABLE QuestRequirements (
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

                CREATE TABLE QuestRequiredItems (
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

                CREATE TABLE QuestTraderRequirements (
                    Id TEXT PRIMARY KEY,
                    QuestId TEXT NOT NULL,
                    TraderId TEXT,
                    TraderName TEXT NOT NULL,
                    RequirementType TEXT NOT NULL DEFAULT 'Level',
                    CompareMethod TEXT NOT NULL DEFAULT '>=',
                    RequiredLevel INTEGER NOT NULL DEFAULT 1,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    IsApproved INTEGER NOT NULL DEFAULT 0,
                    ApprovedAt TEXT,
                    UpdatedAt TEXT,
                    FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE
                );

                CREATE TABLE QuestPoolRequirements (
                    Id TEXT PRIMARY KEY,
                    QuestId TEXT NOT NULL,
                    PoolTrader TEXT NOT NULL,
                    PoolTraderLL INTEGER,
                    RequiredCompletedCount INTEGER NOT NULL DEFAULT 1,
                    Description TEXT,
                    IsApproved INTEGER NOT NULL DEFAULT 0,
                    ApprovedAt TEXT,
                    UpdatedAt TEXT,
                    FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE
                );

                CREATE TABLE HideoutStations (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    NameKO TEXT,
                    NameJA TEXT,
                    NormalizedName TEXT,
                    ImageLink TEXT,
                    MaxLevel INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT
                );

                CREATE TABLE HideoutLevels (
                    Id TEXT PRIMARY KEY,
                    StationId TEXT NOT NULL,
                    Level INTEGER NOT NULL,
                    ConstructionTime INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT,
                    FOREIGN KEY (StationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE
                );

                CREATE TABLE HideoutItemRequirements (
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

                -- Initial Seed Data: Existing Weapons Circulation quest
                INSERT INTO Quests (Id, BsgId, Name, Trader, Location, MinLevel, KappaRequired, Faction)
                VALUES ('aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9XZWFwb25zX0NpcmN1bGF0aW9u', '5a27ba1c86f77461ea5a3c56', 'Weapons Circulation', 'Peacekeeper', 'Woods', 25, 1, 'Any');

                INSERT INTO QuestObjectives (Id, QuestId, SortOrder, ObjectiveType, Description, LocationPoints, OriginalLocationPoints)
                VALUES ('obj_wc_1', 'aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9XZWFwb25zX0NpcmN1bGF0aW9u', 0, 'Visit', 'Locate the cache on Woods', '[{""x"":123.4,""y"":56.7,""z"":89.0}]', '[{""x"":123.4,""y"":56.7,""z"":89.0}]');

                -- Existing Hideout station
                INSERT INTO HideoutStations (Id, Name, NormalizedName, MaxLevel)
                VALUES ('5d3b396e33c48f02b81cd9f3', 'Generator', 'generator', 3);

                INSERT INTO HideoutLevels (Id, StationId, Level, ConstructionTime)
                VALUES ('5d3b396e33c48f02b81cd9f3_1', '5d3b396e33c48f02b81cd9f3', 1, 1200);
            ";
            cmd.ExecuteNonQuery();
        }

        #endregion

        #region End-to-End Pipeline Tests

        [Fact]
        public async Task Pipeline_ExtractNormalizeDiffAndApply_ExecutesSuccessfullyAndPreservesLocationPoints()
        {
            // 1. Arrange TarkovTracker Task Data
            var trackerData = new RawTarkovTrackerData
            {
                Source = "TarkovTracker API Test",
                Tasks = new List<TarkovTrackerTask>
                {
                    new()
                    {
                        Id = "5a27ba1c86f77461ea5a3c56",
                        Name = "Weapons Circulation [PVP ZONE]",
                        Trader = new TarkovTrackerTraderRef { Id = "5935c25fb3acc3127c3d8cd9", Name = "Peacekeeper" },
                        MinPlayerLevel = 25,
                        FactionName = "Any",
                        KappaRequired = true,
                        WikiLink = "https://escapefromtarkov.fandom.com/wiki/Weapons_Circulation",
                        NormalizedTraderRequirements = new List<TarkovTrackerTraderRequirement>
                        {
                            new()
                            {
                                RequirementType = "level",
                                CompareMethod = ">=",
                                Value = 2,
                                Trader = new TarkovTrackerTraderRef { Id = "5935c25fb3acc3127c3d8cd9", Name = "Peacekeeper" }
                            }
                        },
                        OtherRequirements = new List<TarkovTrackerOtherRequirement>
                        {
                            new()
                            {
                                Type = "globalVariable",
                                VariableId = "6a5ba450a7851e16ce0bde44", // Peacekeeper LL2
                                Value = 5,
                                TaskPool = new TarkovTrackerTaskPool
                                {
                                    TraderId = "5935c25fb3acc3127c3d8cd9",
                                    LoyaltyLevel = 2
                                }
                            }
                        }
                    },
                    new()
                    {
                        Id = "657315ddab5a49b71f098853",
                        Name = "First in Line",
                        Trader = new TarkovTrackerTraderRef { Id = "54cb57776803fa99248b456e", Name = "Therapist" },
                        MinPlayerLevel = 1,
                        FactionName = "Any",
                        KappaRequired = true,
                        WikiLink = "https://escapefromtarkov.fandom.com/wiki/First_in_Line"
                    }
                }
            };

            // 2. Arrange Community Data (Hideout stations, levels, items)
            var communityData = new CommunityDataPayload
            {
                Source = "tarkov.dev Community",
                HideoutStations = new List<CommunityHideoutStation>
                {
                    new()
                    {
                        Id = "5d3b396e33c48f02b81cd9f3", // Generator
                        Name = "Generator",
                        Levels = new List<CommunityHideoutLevel>
                        {
                            new()
                            {
                                Level = 1,
                                ConstructionTime = 1200,
                                ItemRequirements = new List<CommunityHideoutItemRequirement>
                                {
                                    new()
                                    {
                                        ItemId = "5449016a4bdc2d6f028b456f",
                                        ItemName = "Roubles",
                                        Count = 25000,
                                        FoundInRaid = false
                                    }
                                }
                            },
                            new()
                            {
                                Level = 2,
                                ConstructionTime = 1800,
                                ItemRequirements = new List<CommunityHideoutItemRequirement>()
                            }
                        }
                    }
                }
            };

            // 3. Normalize combined data using DataNormalizerService.Normalize overload
            var normalizer = new DataNormalizerService();
            var normalizedData = normalizer.Normalize(trackerData, communityData);

            Assert.NotNull(normalizedData);
            Assert.Equal(2, normalizedData.Quests.Count);
            Assert.Single(normalizedData.QuestTraderRequirements);
            Assert.Single(normalizedData.QuestPoolRequirements);
            Assert.Single(normalizedData.HideoutStations);
            Assert.Equal(2, normalizedData.HideoutLevels.Count);
            Assert.Single(normalizedData.HideoutItemRequirements);

            // 4. Compute diff against test SQLite database
            var diffEngine = DatabaseDiffEngine.Instance;
            var diffReport = await diffEngine.ComputeDiffAsync(normalizedData, _testDbPath);

            Assert.NotNull(diffReport);
            Assert.Contains(diffReport.QuestDiffs, q => q.ChangeType == DiffChangeType.Added && q.QuestName == "First in Line");
            Assert.Contains(diffReport.QuestTraderRequirementDiffs, td => td.ChangeType == DiffChangeType.Added && td.TraderName == "Peacekeeper" && td.NewRequiredLevel == 2);
            Assert.Contains(diffReport.QuestPoolRequirementDiffs, pd => pd.ChangeType == DiffChangeType.Added && pd.PoolTrader == "Peacekeeper" && pd.NewRequiredCompletedCount == 5);
            Assert.Contains(diffReport.HideoutLevelDiffs, hl => hl.ChangeType == DiffChangeType.Added && hl.Level == 2);

            // 5. Apply diff atomically via DatabaseTransactionApplier
            var applier = DatabaseTransactionApplier.Instance;
            var applied = await applier.ApplyDiffAsync(diffReport, _testDbPath);

            Assert.True(applied);
            Assert.NotNull(applier.LastBackupPath);
            Assert.True(File.Exists(applier.LastBackupPath));
            Assert.True(new FileInfo(applier.LastBackupPath).Length > 0);

            // 6. Verify SQLite database integrity & preservation of OriginalLocationPoints
            using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly;");
            conn.Open();

            // Check OriginalLocationPoints preserved
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT OriginalLocationPoints, LocationPoints FROM QuestObjectives WHERE Id = 'obj_wc_1';";
                using var reader = cmd.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal("[{\"x\":123.4,\"y\":56.7,\"z\":89.0}]", reader.GetString(0));
                Assert.Equal("[{\"x\":123.4,\"y\":56.7,\"z\":89.0}]", reader.GetString(1));
            }

            // Check Trader requirement applied
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM QuestTraderRequirements WHERE QuestId = 'aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9XZWFwb25zX0NpcmN1bGF0aW9u' AND RequiredLevel = 2;";
                var count = Convert.ToInt32(cmd.ExecuteScalar());
                Assert.Equal(1, count);
            }

            // Check Pool requirement applied
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT RequiredCompletedCount FROM QuestPoolRequirements WHERE QuestId = 'aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9XZWFwb25zX0NpcmN1bGF0aW9u';";
                var reqCount = Convert.ToInt32(cmd.ExecuteScalar());
                Assert.Equal(5, reqCount);
            }

            // Check new quest inserted
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Name = 'First in Line';";
                var count = Convert.ToInt32(cmd.ExecuteScalar());
                Assert.Equal(1, count);
            }

            // Check new hideout level inserted
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM HideoutLevels WHERE StationId = '5d3b396e33c48f02b81cd9f3' AND Level = 2;";
                var count = Convert.ToInt32(cmd.ExecuteScalar());
                Assert.Equal(1, count);
            }
        }

        [Fact]
        public async Task Pipeline_BackupAndRestore_RestoresExactPreviousDatabaseState()
        {
            var backupManager = DatabaseBackupManager.Instance;

            // 1. Create a dated backup
            var backupPath = await backupManager.CreateBackupAsync(_testDbPath);
            Assert.True(File.Exists(backupPath));
            Assert.Contains(".bak_", backupPath);

            // 2. Modify test database
            using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadWrite;"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO Quests (Id, Name, Trader) VALUES ('temp_q', 'Temp Quest', 'Prapor');";
                cmd.ExecuteNonQuery();
            }

            // Verify quest exists in modified DB
            using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly;"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id = 'temp_q';";
                Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));
            }

            // 3. Restore backup
            var restored = await backupManager.RestoreBackupAsync(backupPath, _testDbPath);
            Assert.True(restored);

            // 4. Verify quest no longer exists after rollback
            using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly;"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id = 'temp_q';";
                Assert.Equal(0, Convert.ToInt32(cmd.ExecuteScalar()));
            }
        }

        [Fact]
        public async Task Pipeline_SecurityGuards_RejectUserDataDbAccess()
        {
            var backupManager = DatabaseBackupManager.Instance;
            var applier = DatabaseTransactionApplier.Instance;

            var userDataPath = Path.Combine(_testDir, "user_data.db");
            var reportWithChange = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new() { ChangeType = DiffChangeType.Added, QuestId = "q_test", QuestName = "Test" }
                }
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => backupManager.CreateBackupAsync(userDataPath));
            await Assert.ThrowsAsync<InvalidOperationException>(() => backupManager.RestoreBackupAsync(userDataPath, _testDbPath));
            await Assert.ThrowsAsync<InvalidOperationException>(() => applier.ApplyDiffAsync(reportWithChange, userDataPath));
        }

        [Fact]
        public async Task Pipeline_HotReload_FiresDatabaseUpdatedNotification()
        {
            var updateService = DatabaseUpdateService.Instance;
            bool eventFired = false;

            EventHandler handler = (s, e) => { eventFired = true; };
            updateService.DatabaseUpdated += handler;

            try
            {
                updateService.NotifyDatabaseUpdated();
                // Wait briefly for dispatcher/event invocation
                await Task.Delay(50);
                Assert.True(eventFired);
            }
            finally
            {
                updateService.DatabaseUpdated -= handler;
            }
        }

        [Fact]
        public void Pipeline_Normalizer_HandlesNullCommunityDataGracefully()
        {
            var normalizer = new DataNormalizerService();
            var trackerData = new RawTarkovTrackerData
            {
                Source = "TarkovTracker Isolated",
                Tasks = new List<TarkovTrackerTask>
                {
                    new()
                    {
                        Id = "task_isolated_1",
                        Name = "Isolated Task",
                        Trader = new TarkovTrackerTraderRef { Name = "Prapor" }
                    }
                }
            };

            var normalized = normalizer.Normalize(trackerData, (CommunityDataPayload?)null);

            Assert.NotNull(normalized);
            Assert.Single(normalized.Quests);
            Assert.Empty(normalized.HideoutStations);
            Assert.Empty(normalized.HideoutLevels);
            Assert.Empty(normalized.HideoutItemRequirements);
        }

        [Fact]
        public async Task Pipeline_AtomicTransaction_RollsBackOnForeignKeyFailure()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // Diff report pointing to non-existent parent quest
            var invalidReport = new DataDiffReport
            {
                QuestRequirementDiffs = new List<QuestRequirementDiff>
                {
                    new()
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = "req_invalid_fk",
                        QuestId = "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9XZWFwb25zX0NpcmN1bGF0aW9u",
                        RequiredQuestId = "non_existent_quest_999",
                        NewRequirementType = "Complete"
                    }
                }
            };

            var result = await applier.ApplyDiffAsync(invalidReport, _testDbPath, resilientPrerequisites: false);

            Assert.False(result);
            Assert.NotNull(applier.LastError);
            Assert.Contains("FOREIGN KEY", applier.LastError, StringComparison.OrdinalIgnoreCase);

            // Verify requirement was NOT inserted
            using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM QuestRequirements WHERE Id = 'req_invalid_fk';";
            Assert.Equal(0, Convert.ToInt32(cmd.ExecuteScalar()));
        }

        [Fact]
        public async Task Pipeline_FullRealSampleIngestion_ProducesAccurateCounts()
        {
            var corePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "scratch", "tasks_core.json");
            if (!File.Exists(corePath))
            {
                // Fallback search in working directory
                corePath = Path.GetFullPath("scratch/tasks_core.json");
            }

            if (!File.Exists(corePath))
            {
                // If scratch file not present, pass
                return;
            }

            var coreJson = await File.ReadAllTextAsync(corePath);
            var trackerData = TarkovTrackerExtractorService.ParseTasksCore(coreJson);

            var communityData = new CommunityDataPayload
            {
                Source = "tarkov.dev Community",
                HideoutStations = new List<CommunityHideoutStation>
                {
                    new()
                    {
                        Id = "5d3b396e33c48f02b81cd9f3",
                        Name = "Generator",
                        Levels = new List<CommunityHideoutLevel>
                        {
                            new() { Level = 1, ConstructionTime = 1200 },
                            new() { Level = 2, ConstructionTime = 2400 }
                        }
                    }
                }
            };

            var normalizer = new DataNormalizerService();
            var normalized = normalizer.Normalize(trackerData, communityData);

            Assert.NotNull(normalized);
            Assert.Equal(494, normalized.Quests.Count);
            Assert.Equal(304, normalized.QuestTraderRequirements.Count);
            Assert.Equal(168, normalized.QuestPoolRequirements.Count);
            Assert.Single(normalized.HideoutStations);
            Assert.Equal(2, normalized.HideoutLevels.Count);

            var diffEngine = DatabaseDiffEngine.Instance;
            var diff = await diffEngine.ComputeDiffAsync(normalized, _testDbPath);

            Assert.NotNull(diff);
            Assert.True(diff.HasChanges);
            Assert.True(diff.TotalChanges > 0);
        }

        #endregion
    }
}
