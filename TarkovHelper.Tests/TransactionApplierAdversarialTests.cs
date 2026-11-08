using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Models.Map;
using TarkovHelper.Services;
using TarkovHelper.Services.DataAudit;
using TarkovHelper.Services.Map;
using Xunit;

namespace TarkovHelper.Tests
{
    /// <summary>
    /// Empirical adversarial stress-test harness for DatabaseTransactionApplier.
    /// Exercises mid-batch foreign key violations, massive diff reports, safe spatial defaults,
    /// pre-write backup creation failure aborts, and strict user_data.db isolation.
    /// </summary>
    public class TransactionApplierAdversarialTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _testDbPath;

        public TransactionApplierAdversarialTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_TxAdvTests_" + Guid.NewGuid().ToString("N"));
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

        #region Helper Methods

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

                -- Initial Seed Data
                INSERT INTO Items (Id, BsgId, Name, NameEN) VALUES ('item_salewa', '544fb2574bdc2df7738b4567', 'Salewa first aid kit', 'Salewa first aid kit');
                INSERT INTO Items (Id, BsgId, Name, NameEN) VALUES ('item_roubles', '5449016a4bdc2d6f028b456f', 'Roubles', 'Roubles');

                INSERT INTO Quests (Id, BsgId, Name, Trader, Location, MinLevel, KappaRequired, Faction)
                VALUES ('quest_prereq', 'bsg_prereq', 'Initial Prereq Quest', 'Prapor', 'Customs', 1, 1, 'Any');

                INSERT INTO Quests (Id, BsgId, Name, Trader, Location, MinLevel, KappaRequired, Faction)
                VALUES ('quest_debut', 'bsg_debut', 'Debut', 'Prapor', 'Customs', 1, 1, 'Any');

                INSERT INTO QuestRequirements (Id, QuestId, RequiredQuestId, RequirementType, GroupId)
                VALUES ('req_debut_prereq', 'quest_debut', 'quest_prereq', 'Complete', 0);

                INSERT INTO QuestRequiredItems (Id, QuestId, ItemId, ItemName, Count, RequiresFIR)
                VALUES ('item_req_debut', 'quest_debut', 'item_salewa', 'Salewa first aid kit', 3, 1);

                INSERT INTO HideoutStations (Id, Name, NormalizedName, MaxLevel)
                VALUES ('station_vents', 'Vents', 'vents', 3);

                INSERT INTO HideoutLevels (Id, StationId, Level, ConstructionTime)
                VALUES ('level_vents_1', 'station_vents', 1, 1200);

                INSERT INTO HideoutItemRequirements (Id, StationId, Level, ItemId, ItemName, Count, FoundInRaid)
                VALUES ('hireq_vents_1', 'station_vents', 1, 'item_roubles', 'Roubles', 25000, 0);
            ";
            cmd.ExecuteNonQuery();
        }

        private static string ComputeFileSha256(string filePath)
        {
            SqliteConnection.ClearAllPools();
            using var sha = SHA256.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }

        private static Dictionary<string, int> GetTableCounts(string dbPath)
        {
            SqliteConnection.ClearAllPools();
            var counts = new Dictionary<string, int>();
            var tables = new[]
            {
                "Quests", "QuestObjectives", "QuestRequirements", "QuestRequiredItems",
                "HideoutStations", "HideoutLevels", "HideoutItemRequirements"
            };

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            foreach (var table in tables)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
                counts[table] = Convert.ToInt32(cmd.ExecuteScalar());
            }
            return counts;
        }

        #endregion

        #region Adversarial Test 1: Mid-Batch Foreign Key Violation & 100% Rollback

        [Fact]
        public async Task MidBatch_ForeignKeyConstraintViolation_RollsBackAllPrecedingAndSubsequentOperationsWithZeroPartialWrites()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var initialCounts = GetTableCounts(_testDbPath);

            // Craft an adversarial compound diff report:
            // 1. Hideout Stations (valid addition)
            // 2. Hideout Levels (valid addition)
            // 3. Quests (valid additions: q_batch_1, q_batch_2, q_batch_3)
            // 4. Mid-Batch QuestRequirement: REFERENCES A NON-EXISTENT QUEST (Will violate FK constraint!)
            // 5. Subsequent Quests (valid addition: q_batch_after_fail)
            // 6. Subsequent Hideout Stations (valid addition: st_after_fail)
            var report = new DataDiffReport
            {
                HideoutStationDiffs = new List<HideoutStationDiff>
                {
                    new HideoutStationDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        StationId = "station_generator",
                        StationName = "Generator",
                        NewMaxLevel = 3
                    },
                    new HideoutStationDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        StationId = "station_after_fail",
                        StationName = "After Fail Station",
                        NewMaxLevel = 1
                    }
                },
                HideoutLevelDiffs = new List<HideoutLevelDiff>
                {
                    new HideoutLevelDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        LevelId = "level_gen_1",
                        StationId = "station_generator",
                        Level = 1,
                        NewConstructionTime = 3600
                    }
                },
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_batch_1",
                        QuestName = "Batch Quest 1",
                        NewTrader = "Prapor"
                    },
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_batch_2",
                        QuestName = "Batch Quest 2",
                        NewTrader = "Therapist"
                    },
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_batch_after_fail",
                        QuestName = "After Fail Quest",
                        NewTrader = "Mechanic"
                    }
                },
                QuestRequirementDiffs = new List<QuestRequirementDiff>
                {
                    // Valid requirement
                    new QuestRequirementDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = "req_valid_b2_b1",
                        QuestId = "q_batch_2",
                        RequiredQuestId = "q_batch_1",
                        NewRequirementType = "Complete"
                    },
                    // ADVERSARIAL MID-BATCH FK VIOLATION: References non-existent quest!
                    new QuestRequirementDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = "req_MID_BATCH_FK_POISON",
                        QuestId = "q_batch_1",
                        RequiredQuestId = "quest_DOES_NOT_EXIST_GHOST_12345",
                        NewRequirementType = "Complete"
                    }
                }
            };

            // Act: Apply diff with mid-batch FK poison
            var success = await applier.ApplyDiffAsync(report, _testDbPath, resilientPrerequisites: false);

            // Assert: Transaction must return false
            Assert.False(success, "Transaction must fail and report false when a mid-batch FK violation occurs.");
            Assert.NotNull(applier.LastError);
            Assert.Contains("FOREIGN KEY", applier.LastError, StringComparison.OrdinalIgnoreCase);

            // Assert: 100% Rollback across ALL tables - zero partial writes!
            var currentCounts = GetTableCounts(_testDbPath);
            foreach (var kvp in initialCounts)
            {
                Assert.Equal(kvp.Value, currentCounts[kvp.Key]);
            }

            // Assert: Verify specific staged entities DO NOT exist
            SqliteConnection.ClearAllPools();
            await using var verifyConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            await verifyConn.OpenAsync();

            await using (var checkCmd = verifyConn.CreateCommand())
            {
                checkCmd.CommandText = @"
                    SELECT
                        (SELECT COUNT(*) FROM HideoutStations WHERE Id IN ('station_generator', 'station_after_fail')),
                        (SELECT COUNT(*) FROM HideoutLevels WHERE Id = 'level_gen_1'),
                        (SELECT COUNT(*) FROM Quests WHERE Id IN ('q_batch_1', 'q_batch_2', 'q_batch_after_fail')),
                        (SELECT COUNT(*) FROM QuestRequirements WHERE Id IN ('req_valid_b2_b1', 'req_MID_BATCH_FK_POISON'));";
                await using var reader = await checkCmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(0, reader.GetInt32(0)); // 0 hideout stations leaked
                Assert.Equal(0, reader.GetInt32(1)); // 0 hideout levels leaked
                Assert.Equal(0, reader.GetInt32(2)); // 0 quests leaked
                Assert.Equal(0, reader.GetInt32(3)); // 0 requirements leaked
            }

            // Assert: SQLite integrity is intact and zero FK orphans exist
            await using (var pragmaCmd = verifyConn.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA integrity_check;";
                var integrity = (string?)await pragmaCmd.ExecuteScalarAsync();
                Assert.Equal("ok", integrity);

                pragmaCmd.CommandText = "PRAGMA foreign_key_check;";
                await using var fkReader = await pragmaCmd.ExecuteReaderAsync();
                Assert.False(await fkReader.ReadAsync(), "Zero foreign key violations must exist after rollback.");
            }
        }

        [Fact]
        public async Task MidBatch_HideoutLevel_ForeignKeyViolation_RollsBackAllPriorOperations()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var initialCounts = GetTableCounts(_testDbPath);

            // Diff where valid HideoutStations are added, but a HideoutLevel references a non-existent station
            var report = new DataDiffReport
            {
                HideoutStationDiffs = new List<HideoutStationDiff>
                {
                    new HideoutStationDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        StationId = "station_medstation",
                        StationName = "Medstation",
                        NewMaxLevel = 3
                    }
                },
                HideoutLevelDiffs = new List<HideoutLevelDiff>
                {
                    new HideoutLevelDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        LevelId = "lvl_poison_fk",
                        StationId = "station_NON_EXISTENT_PHANTOM",
                        Level = 1
                    }
                }
            };

            var success = await applier.ApplyDiffAsync(report, _testDbPath);
            Assert.False(success);
            Assert.NotNull(applier.LastError);
            Assert.Contains("FOREIGN KEY", applier.LastError, StringComparison.OrdinalIgnoreCase);

            var postCounts = GetTableCounts(_testDbPath);
            Assert.Equal(initialCounts["HideoutStations"], postCounts["HideoutStations"]);
            Assert.Equal(initialCounts["HideoutLevels"], postCounts["HideoutLevels"]);
        }

        #endregion

        #region Adversarial Test 2: Massive Diff Reports (Stress & Performance Harness)

        [Fact]
        public async Task MassiveDiff_MultipleQuestsPrerequisitesHideoutLevels_AppliesAtScale()
        {
            var applier = DatabaseTransactionApplier.Instance;

            const int questCount = 200;
            const int reqCount = 300;
            const int stationCount = 40;
            const int levelCount = 120; // 3 levels per station
            const int itemReqCount = 200;

            var report = new DataDiffReport();

            // 1. Stations & Levels
            for (int s = 0; s < stationCount; s++)
            {
                var sId = $"st_massive_{s}";
                report.HideoutStationDiffs.Add(new HideoutStationDiff
                {
                    ChangeType = DiffChangeType.Added,
                    StationId = sId,
                    StationName = $"Massive Station {s}",
                    NewMaxLevel = 3
                });

                for (int l = 1; l <= 3; l++)
                {
                    var lId = $"lvl_massive_{s}_{l}";
                    report.HideoutLevelDiffs.Add(new HideoutLevelDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        LevelId = lId,
                        StationId = sId,
                        Level = l,
                        NewConstructionTime = l * 3600
                    });

                    // Item requirement
                    if (report.HideoutItemDiffs.Count < itemReqCount)
                    {
                        report.HideoutItemDiffs.Add(new HideoutItemDiff
                        {
                            ChangeType = DiffChangeType.Added,
                            RequirementId = $"hireq_massive_{s}_{l}",
                            StationId = sId,
                            Level = l,
                            ItemId = "item_roubles",
                            ItemName = "Roubles",
                            NewQuantity = l * 50000
                        });
                    }
                }
            }

            // 2. Quests
            for (int q = 0; q < questCount; q++)
            {
                var qId = $"q_massive_{q}";
                report.QuestDiffs.Add(new QuestDiff
                {
                    ChangeType = DiffChangeType.Added,
                    QuestId = qId,
                    QuestName = $"Massive Quest {q}",
                    NewTrader = q % 2 == 0 ? "Prapor" : "Therapist",
                    NewMinLevel = (q % 50) + 1,
                    NewKappaRequired = q % 3 == 0,
                    RemoteQuest = new NormalizedQuest
                    {
                        Id = qId,
                        Name = $"Massive Quest {q}",
                        Location = "Customs",
                        Objectives = new List<string>
                        {
                            $"Objective 1 for massive quest {q}",
                            $"Objective 2 for massive quest {q}"
                        }
                    }
                });

                // Required Items
                report.QuestItemDiffs.Add(new QuestItemDiff
                {
                    ChangeType = DiffChangeType.Added,
                    RequirementId = $"qitem_massive_{q}",
                    QuestId = qId,
                    ItemId = "item_salewa",
                    ItemName = "Salewa first aid kit",
                    NewQuantity = (q % 5) + 1,
                    NewRequiresFIR = q % 2 == 0
                });
            }

            // 3. Quest Requirements (chained: q_{i} requires q_{i-1})
            for (int r = 1; r < reqCount && r < questCount; r++)
            {
                report.QuestRequirementDiffs.Add(new QuestRequirementDiff
                {
                    ChangeType = DiffChangeType.Added,
                    RequirementId = $"req_massive_{r}",
                    QuestId = $"q_massive_{r}",
                    RequiredQuestId = $"q_massive_{r - 1}",
                    NewRequirementType = "Complete"
                });
            }

            int totalEntities = report.TotalChanges;
            Assert.True(totalEntities > 800, $"Total generated entities should be massive, was {totalEntities}");

            // Act: Apply massive diff with execution stopwatch
            var stopwatch = Stopwatch.StartNew();
            var success = await applier.ApplyDiffAsync(report, _testDbPath);
            stopwatch.Stop();

            // Assert: Must succeed cleanly within reasonable threshold (< 15 seconds)
            Assert.True(success, $"Massive diff application failed: {applier.LastError}");
            Assert.True(stopwatch.ElapsedMilliseconds < 15000,
                $"Massive diff took {stopwatch.ElapsedMilliseconds} ms, expected < 15000 ms.");

            // Assert: Database row count verification
            SqliteConnection.ClearAllPools();
            await using var verifyConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            await verifyConn.OpenAsync();

            await using (var cmd = verifyConn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id LIKE 'q_massive_%';";
                var actualQuests = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.Equal(questCount, actualQuests);

                cmd.CommandText = "SELECT COUNT(*) FROM QuestObjectives WHERE QuestId LIKE 'q_massive_%';";
                var actualObjectives = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.Equal(questCount * 2, actualObjectives);

                cmd.CommandText = "SELECT COUNT(*) FROM HideoutStations WHERE Id LIKE 'st_massive_%';";
                var actualStations = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.Equal(stationCount, actualStations);

                cmd.CommandText = "SELECT COUNT(*) FROM HideoutLevels WHERE Id LIKE 'lvl_massive_%';";
                var actualLevels = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.Equal(levelCount, actualLevels);

                cmd.CommandText = "PRAGMA integrity_check;";
                var integrity = (string?)await cmd.ExecuteScalarAsync();
                Assert.Equal("ok", integrity);

                cmd.CommandText = "PRAGMA foreign_key_check;";
                await using var r = await cmd.ExecuteReaderAsync();
                Assert.False(await r.ReadAsync());
            }
        }

        [Fact]
        public async Task MassiveDiff_LargeScaleModificationsAndDeletions_ScalesAccurately()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // 1. First stage 50 quests
            var seedReport = new DataDiffReport();
            for (int i = 0; i < 50; i++)
            {
                seedReport.QuestDiffs.Add(new QuestDiff
                {
                    ChangeType = DiffChangeType.Added,
                    QuestId = $"q_mod_scale_{i}",
                    QuestName = $"Initial Quest {i}",
                    NewTrader = "Prapor",
                    NewMinLevel = 1
                });
            }
            await applier.ApplyDiffAsync(seedReport, _testDbPath);

            // 2. Perform bulk modification on 30 quests and removal of 10 quests
            var modReport = new DataDiffReport();
            for (int i = 0; i < 30; i++)
            {
                modReport.QuestDiffs.Add(new QuestDiff
                {
                    ChangeType = DiffChangeType.Modified,
                    QuestId = $"q_mod_scale_{i}",
                    QuestName = $"Updated Quest {i}",
                    NewTrader = "Mechanic",
                    NewMinLevel = 42,
                    NewKappaRequired = true
                });
            }
            for (int i = 40; i < 50; i++)
            {
                modReport.QuestDiffs.Add(new QuestDiff
                {
                    ChangeType = DiffChangeType.Removed,
                    QuestId = $"q_mod_scale_{i}",
                    QuestName = $"Initial Quest {i}"
                });
            }

            var success = await applier.ApplyDiffAsync(modReport, _testDbPath);
            Assert.True(success);

            SqliteConnection.ClearAllPools();
            await using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            await conn.OpenAsync();

            // Verify modified quests
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id = 'q_mod_scale_0' AND Trader = 'Mechanic' AND MinLevel = 42 AND KappaRequired = 1;";
            var modMatch = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.Equal(1, modMatch);

            // Verify removed quests
            cmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id LIKE 'q_mod_scale_%';";
            var remaining = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.Equal(40, remaining); // 50 - 10 removed = 40
        }

        #endregion

        #region Adversarial Test 3: Safe Defaults for Spatial Coordinates

        [Fact]
        public async Task SafeDefaults_NullAndEmptyCoordinates_MapAndObjectivePipelinesNeverThrowOrProduceNaN()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // Adversarial quest with missing/empty coordinates across multiple objectives
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_adversarial_coords",
                        QuestName = "Adversarial Coordinates Quest",
                        RemoteQuest = new NormalizedQuest
                        {
                            Id = "q_adversarial_coords",
                            Name = "Adversarial Coordinates Quest",
                            Location = "Woods",
                            Objectives = new List<string>
                            {
                                "Objective 0: default null coords",
                                "Objective 1: empty string coords",
                                "Objective 2: empty array json coords"
                            }
                        }
                    }
                }
            };

            var success = await applier.ApplyDiffAsync(report, _testDbPath);
            Assert.True(success);

            SqliteConnection.ClearAllPools();
            await using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Description, LocationPoints, OptionalPoints, MapName FROM QuestObjectives WHERE QuestId = 'q_adversarial_coords' ORDER BY SortOrder;";
            await using var reader = await cmd.ExecuteReaderAsync();

            var loadedObjectives = new List<QuestObjective>();
            while (await reader.ReadAsync())
            {
                var obj = new QuestObjective
                {
                    Id = reader.GetString(0),
                    QuestId = "q_adversarial_coords",
                    Description = reader.GetString(1),
                    MapName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    LocationPointsJson = reader.IsDBNull(2) ? null : reader.GetString(2),
                    OptionalPointsJson = reader.IsDBNull(3) ? null : reader.GetString(3)
                };
                loadedObjectives.Add(obj);
            }

            Assert.Equal(3, loadedObjectives.Count);

            // Adversarial verification on each objective:
            foreach (var obj in loadedObjectives)
            {
                // 1. Must never throw or be null
                Assert.NotNull(obj.LocationPoints);
                Assert.NotNull(obj.OptionalPoints);

                // 2. Safe defaults: Count must be 0, HasCoordinates must be false
                Assert.Empty(obj.LocationPoints);
                Assert.Empty(obj.OptionalPoints);
                Assert.False(obj.HasCoordinates);
                Assert.False(obj.HasOptionalPoints);

                // 3. Coordinate conversion simulation: must not throw NullReferenceException or produce NaN
                var convertedLocations = new List<QuestObjectiveLocation>();
                var mapName = obj.EffectiveMapName ?? "Unknown";
                if (obj.LocationPoints.Count > 0)
                {
                    var pt = obj.LocationPoints[0];
                    convertedLocations.Add(new QuestObjectiveLocation
                    {
                        Id = $"{obj.Id}_loc",
                        MapId = mapName,
                        MapName = mapName,
                        X = pt.X,
                        Y = pt.Z,
                        Z = pt.Y
                    });
                }

                Assert.Empty(convertedLocations);
            }

            // Test extreme boundary values in LocationPointsJson directly
            var extremeObj = new QuestObjective();

            // Null JSON
            extremeObj.LocationPointsJson = null;
            Assert.NotNull(extremeObj.LocationPoints);
            Assert.Empty(extremeObj.LocationPoints);

            // Empty string
            extremeObj.LocationPointsJson = "";
            Assert.Empty(extremeObj.LocationPoints);

            // Whitespace string
            extremeObj.LocationPointsJson = "   ";
            Assert.Empty(extremeObj.LocationPoints);

            // "null" literal
            extremeObj.LocationPointsJson = "null";
            Assert.Empty(extremeObj.LocationPoints);

            // "[]" literal
            extremeObj.LocationPointsJson = "[]";
            Assert.Empty(extremeObj.LocationPoints);

            // Malformed JSON (syntax error)
            extremeObj.LocationPointsJson = "{broken json, not an array}";
            Assert.NotNull(extremeObj.LocationPoints);
            Assert.Empty(extremeObj.LocationPoints);

            // Corrupt object instead of array
            extremeObj.LocationPointsJson = "{\"X\": 12.3}";
            Assert.NotNull(extremeObj.LocationPoints);
            Assert.Empty(extremeObj.LocationPoints);

            // Valid JSON with normal coordinates
            extremeObj.LocationPointsJson = "[{\"X\": 100.5, \"Y\": 20.0, \"Z\": -50.2, \"FloorId\": \"Floor_1\"}]";
            Assert.Single(extremeObj.LocationPoints);
            Assert.True(extremeObj.HasCoordinates);
            Assert.False(double.IsNaN(extremeObj.LocationPoints[0].X));
            Assert.False(double.IsNaN(extremeObj.LocationPoints[0].Y));
            Assert.False(double.IsNaN(extremeObj.LocationPoints[0].Z));
            Assert.False(double.IsInfinity(extremeObj.LocationPoints[0].X));
        }

        #endregion

        #region Adversarial Test 4: Pre-Write Backup Creation Failure Simulation

        [Fact]
        public async Task PreWriteBackupCreationFailure_AbortsTransactionBeforeTouchingSqliteFile()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // 1. Record pre-test database checksum and counts
            var initialHash = ComputeFileSha256(_testDbPath);
            var initialCounts = GetTableCounts(_testDbPath);

            // 2. Prepare a diff with changes
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_backup_fail_staged",
                        QuestName = "Should Never Be Written",
                        NewTrader = "Prapor"
                    }
                }
            };

            // 3. Simulate backup creation failure:
            // Pre-create DIRECTORIES matching the expected backup timestamp naming pattern.
            // When DatabaseBackupManager calls File.Exists(backupPath), it returns false for directories!
            // When SqliteConnection tries to open the directory as a SQLite file, or File.Copy tries to copy onto a directory,
            // an UnauthorizedAccessException / IOException is thrown, simulating deterministic backup failure!
            var dir = Path.GetDirectoryName(_testDbPath)!;
            var fileName = Path.GetFileName(_testDbPath);
            var baseTime = DateTime.Now;

            var createdDirs = new List<string>();
            try
            {
                for (int offset = -2; offset <= 5; offset++)
                {
                    var ts = baseTime.AddSeconds(offset).ToString("yyyyMMdd_HHmmss");
                    var fakeBackupDir = Path.Combine(dir, $"{fileName}.bak_{ts}");
                    Directory.CreateDirectory(fakeBackupDir);
                    createdDirs.Add(fakeBackupDir);

                    // Also create counter suffixes
                    for (int c = 1; c <= 3; c++)
                    {
                        var counterDir = $"{fakeBackupDir}_{c}";
                        Directory.CreateDirectory(counterDir);
                        createdDirs.Add(counterDir);
                    }
                }

                // Act: Attempt to apply diff when backup creation fails
                // The method MUST throw an exception (UnauthorizedAccessException / IOException) and abort BEFORE touching the DB
                var ex = await Record.ExceptionAsync(() => applier.ApplyDiffAsync(report, _testDbPath));

                // Assert: An exception was thrown aborting the process
                Assert.NotNull(ex);
                Assert.True(ex is IOException || ex is UnauthorizedAccessException || ex is SqliteException,
                    $"Expected IOException or UnauthorizedAccessException during backup failure, but got {ex.GetType().Name}: {ex.Message}");

                // Assert: The SQLite file was NEVER touched or modified!
                // File checksum must be 100% IDENTICAL to initial checksum
                var postHash = ComputeFileSha256(_testDbPath);
                Assert.Equal(initialHash, postHash);

                // Row counts must be 100% IDENTICAL to initial counts
                var postCounts = GetTableCounts(_testDbPath);
                foreach (var kvp in initialCounts)
                {
                    Assert.Equal(kvp.Value, postCounts[kvp.Key]);
                }

                // Verify "q_backup_fail_staged" was NOT written
                SqliteConnection.ClearAllPools();
                await using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id = 'q_backup_fail_staged';";
                var stagedCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.Equal(0, stagedCount);
            }
            finally
            {
                // Cleanup fake backup directories
                foreach (var d in createdDirs)
                {
                    try { Directory.Delete(d, recursive: true); } catch { }
                }
            }
        }

        #endregion

        #region Adversarial Test 5: user_data.db Boundary & Path Traversals

        [Theory]
        [InlineData("user_data.db")]
        [InlineData("USER_DATA.DB")]
        [InlineData("subfolder/user_data.db")]
        [InlineData("subfolder\\user_data.db")]
        [InlineData("../userdata.db")]
        [InlineData("c:\\somepath\\user_data.db")]
        [InlineData("c:/somepath/userdata.db")]
        public async Task ApplyDiffAsync_UserDataDbInjections_ThrowsInvalidOperationExceptionImmediately(string adversarialPath)
        {
            var applier = DatabaseTransactionApplier.Instance;
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff { ChangeType = DiffChangeType.Added, QuestId = "q_attack", QuestName = "Attack" }
                }
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                applier.ApplyDiffAsync(report, adversarialPath));
        }

        #endregion
    }
}
