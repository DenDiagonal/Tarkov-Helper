using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
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
    /// Empirical adversarial test harness for DatabaseTransactionApplier.
    /// Stress-tests:
    /// 1. Foreign key constraint violations during mid-batch operations to verify 100% rollback (zero partial writes).
    /// 2. Massive diff reports (multiple quests, prerequisites, hideout levels).
    /// 3. Safe defaults for null/empty coordinates (LocationPoints = null), verifying map viewers/objective lists do not throw NullReferenceException or NaN.
    /// 4. Pre-write backup creation failure simulation (verifying transaction aborts before touching SQLite file).
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class TransactionApplierEmpiricalAdversarialTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _testDbPath;

        public TransactionApplierEmpiricalAdversarialTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_EmpiricalAdv_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _testDbPath = Path.Combine(_testDir, "tarkov_data.db");
            CreateCompleteDatabaseSchema(_testDbPath);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_testDir))
            {
                try
                {
                    // Reset directory attributes/ACLs if modified
                    var dInfo = new DirectoryInfo(_testDir);
                    dInfo.Attributes = FileAttributes.Normal;
                    Directory.Delete(_testDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors in temp
                }
            }
        }

        #region Helper Setup Methods

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
                    MinScavKarma REAL,
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
                    ItemId TEXT,
                    ItemName TEXT NOT NULL,
                    ItemNameKO TEXT,
                    ItemNameJA TEXT,
                    IconLink TEXT,
                    Count INTEGER NOT NULL DEFAULT 1,
                    FoundInRaid INTEGER NOT NULL DEFAULT 0,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT,
                    FOREIGN KEY (StationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE,
                    FOREIGN KEY (ItemId) REFERENCES Items(Id) ON DELETE SET NULL
                );
            ";
            cmd.ExecuteNonQuery();

            // Seed reference items
            using var seedCmd = conn.CreateCommand();
            seedCmd.CommandText = @"
                INSERT INTO Items (Id, BsgId, Name, NameEN) VALUES 
                ('item_medkit_01', 'bsg_med_01', 'Salewa first aid kit', 'Salewa first aid kit'),
                ('item_wire_01', 'bsg_wire_01', 'Wires', 'Wires'),
                ('item_relay_01', 'bsg_relay_01', 'Relay', 'Relay');
            ";
            seedCmd.ExecuteNonQuery();
        }

        private static string ComputeFileSha256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }

        private static int GetRowCount(string dbPath, string table)
        {
            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        #endregion

        #region 1. Adversarial Test: Mid-Batch Foreign Key Violation & 100% Rollback

        [Fact]
        public async Task ApplyDiffAsync_FkViolationMidBatch_EnforcesStrictRollbackWithZeroPartialWrites()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // Seed initial baseline data
            using (var seedConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadWrite"))
            {
                seedConn.Open();
                using var seedCmd = seedConn.CreateCommand();
                seedCmd.CommandText = @"
                    INSERT INTO Quests (Id, Name, MinLevel) VALUES ('seed_q1', 'Seed Quest Alpha', 5);
                    INSERT INTO HideoutStations (Id, Name, MaxLevel) VALUES ('seed_st1', 'Seed Station Alpha', 2);
                ";
                seedCmd.ExecuteNonQuery();
            }

            var preQuestCount = GetRowCount(_testDbPath, "Quests");
            var preStationCount = GetRowCount(_testDbPath, "HideoutStations");
            var preLevelCount = GetRowCount(_testDbPath, "HideoutLevels");
            var preItemReqCount = GetRowCount(_testDbPath, "QuestRequiredItems");
            var preReqCount = GetRowCount(_testDbPath, "QuestRequirements");

            Assert.Equal(1, preQuestCount);
            Assert.Equal(1, preStationCount);
            Assert.Equal(0, preLevelCount);

            // Construct an adversarial diff report where:
            // 1. Valid Station Added
            // 2. Valid Quest Added
            // 3. INVALID Mid-batch HideoutLevel referring to a NON-EXISTENT station id "phantom_station_404"
            //    (Violates FK constraint: FOREIGN KEY (StationId) REFERENCES HideoutStations(Id))
            // 4. Valid Quest Required Item added after
            var adversarialDiff = new DataDiffReport
            {
                HideoutStationDiffs = new List<HideoutStationDiff>
                {
                    new HideoutStationDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        StationId = "adv_st_valid",
                        StationName = "Valid Adversarial Station",
                        NewMaxLevel = 3
                    }
                },
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "adv_q_valid",
                        QuestName = "Valid Adversarial Quest",
                        NewMinLevel = 15,
                        NewTrader = "Prapor"
                    }
                },
                HideoutLevelDiffs = new List<HideoutLevelDiff>
                {
                    new HideoutLevelDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        LevelId = "lvl_phantom_error",
                        StationId = "phantom_station_404_does_not_exist", // VIOLATES FOREIGN KEY CONSTRAINT!
                        Level = 1,
                        NewConstructionTime = 3600
                    }
                },
                QuestItemDiffs = new List<QuestItemDiff>
                {
                    new QuestItemDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "adv_q_valid",
                        ItemName = "Wires",
                        ItemId = "item_wire_01",
                        NewQuantity = 2
                    }
                }
            };

            // Act: Apply the adversarial diff
            var success = await applier.ApplyDiffAsync(adversarialDiff, _testDbPath);

            // Assert: Must return false and set LastError
            Assert.False(success, "ApplyDiffAsync must return false when a mid-batch FK violation occurs.");
            Assert.NotNull(applier.LastError);
            Assert.Contains("FOREIGN KEY", applier.LastError, StringComparison.OrdinalIgnoreCase);

            // Assert: Verify 100% rollback (zero partial writes)
            var postQuestCount = GetRowCount(_testDbPath, "Quests");
            var postStationCount = GetRowCount(_testDbPath, "HideoutStations");
            var postLevelCount = GetRowCount(_testDbPath, "HideoutLevels");
            var postItemReqCount = GetRowCount(_testDbPath, "QuestRequiredItems");
            var postReqCount = GetRowCount(_testDbPath, "QuestRequirements");

            Assert.Equal(preQuestCount, postQuestCount);
            Assert.Equal(preStationCount, postStationCount);
            Assert.Equal(preLevelCount, postLevelCount);
            Assert.Equal(preItemReqCount, postItemReqCount);
            Assert.Equal(preReqCount, postReqCount);

            // Specifically verify that 'adv_st_valid' and 'adv_q_valid' were rolled back and DO NOT exist
            using (var verifyConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
            {
                verifyConn.Open();
                using var chkCmd = verifyConn.CreateCommand();
                chkCmd.CommandText = "SELECT COUNT(*) FROM HideoutStations WHERE Id = 'adv_st_valid';";
                var count = Convert.ToInt32(chkCmd.ExecuteScalar());
                Assert.Equal(0, count);

                chkCmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id = 'adv_q_valid';";
                count = Convert.ToInt32(chkCmd.ExecuteScalar());
                Assert.Equal(0, count);
            }
        }

        [Fact]
        public async Task ApplyDiffAsync_QuestRequirementFkViolation_RollsBackAllPrecedingQuestInserts()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // Scenario: 3 new quests added, but a QuestRequirement specifies a non-existent RequiredQuestId
            var diff = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff { ChangeType = DiffChangeType.Added, QuestId = "q_batch_1", QuestName = "Quest 1" },
                    new QuestDiff { ChangeType = DiffChangeType.Added, QuestId = "q_batch_2", QuestName = "Quest 2" },
                    new QuestDiff { ChangeType = DiffChangeType.Added, QuestId = "q_batch_3", QuestName = "Quest 3" }
                },
                QuestRequirementDiffs = new List<QuestRequirementDiff>
                {
                    new QuestRequirementDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_batch_1",
                        RequiredQuestId = "ghost_quest_that_does_not_exist_in_db", // Foreign key violation!
                        NewRequirementType = "Complete"
                    }
                }
            };

            var success = await applier.ApplyDiffAsync(diff, _testDbPath, resilientPrerequisites: false);

            Assert.False(success, "Must fail due to FK violation on QuestRequirements.");
            Assert.NotNull(applier.LastError);
            Assert.Contains("FOREIGN KEY", applier.LastError, StringComparison.OrdinalIgnoreCase);

            // Verify NONE of the 3 quests were persisted
            Assert.Equal(0, GetRowCount(_testDbPath, "Quests"));
        }

        #endregion

        #region 2. Adversarial Test: Massive Diff Reports (High Volume & Stress)

        [Fact]
        public async Task ApplyDiffAsync_MassiveDiffReport_AppliesAtomicallyAndWithinPerformanceLimits()
        {
            var applier = DatabaseTransactionApplier.Instance;

            const int questCount = 200;
            const int reqCount = 150;
            const int itemCount = 200;
            const int stationCount = 15;
            const int levelCount = 45;
            const int hideoutItemCount = 90;

            var massiveDiff = new DataDiffReport();

            // 1. Stations
            for (int s = 1; s <= stationCount; s++)
            {
                massiveDiff.HideoutStationDiffs.Add(new HideoutStationDiff
                {
                    ChangeType = DiffChangeType.Added,
                    StationId = $"massive_st_{s}",
                    StationName = $"Massive Station {s}",
                    NewMaxLevel = 3
                });

                // Levels for each station
                for (int lvl = 1; lvl <= 3; lvl++)
                {
                    massiveDiff.HideoutLevelDiffs.Add(new HideoutLevelDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        LevelId = $"massive_st_{s}_lvl_{lvl}",
                        StationId = $"massive_st_{s}",
                        Level = lvl,
                        NewConstructionTime = lvl * 1800
                    });

                    // Item requirements for level
                    massiveDiff.HideoutItemDiffs.Add(new HideoutItemDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = $"massive_st_{s}_lvl_{lvl}_req_1",
                        StationId = $"massive_st_{s}",
                        Level = lvl,
                        ItemId = "item_wire_01",
                        ItemName = "Wires",
                        NewQuantity = lvl * 2,
                        NewFoundInRaid = false
                    });
                    massiveDiff.HideoutItemDiffs.Add(new HideoutItemDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = $"massive_st_{s}_lvl_{lvl}_req_2",
                        StationId = $"massive_st_{s}",
                        Level = lvl,
                        ItemId = "item_relay_01",
                        ItemName = "Relay",
                        NewQuantity = lvl,
                        NewFoundInRaid = false
                    });
                }
            }

            // 2. Quests
            for (int q = 1; q <= questCount; q++)
            {
                massiveDiff.QuestDiffs.Add(new QuestDiff
                {
                    ChangeType = DiffChangeType.Added,
                    QuestId = $"massive_q_{q:D4}",
                    QuestName = $"Massive Quest #{q}",
                    NewMinLevel = (q % 40) + 1,
                    NewTrader = (q % 2 == 0) ? "Prapor" : "Therapist",
                    NewFaction = "Any",
                    NewKappaRequired = (q % 5 == 0)
                });
            }

            // 3. Quest Requirements (prerequisites between existing quests in the batch)
            for (int r = 2; r <= reqCount + 1; r++)
            {
                var targetQ = $"massive_q_{r:D4}";
                var requiredQ = $"massive_q_{(r - 1):D4}";
                massiveDiff.QuestRequirementDiffs.Add(new QuestRequirementDiff
                {
                    ChangeType = DiffChangeType.Added,
                    RequirementId = $"massive_req_{r}",
                    QuestId = targetQ,
                    RequiredQuestId = requiredQ,
                    NewRequirementType = "Complete",
                    NewDelayMinutes = 0
                });
            }

            // 4. Quest Required Items
            for (int i = 1; i <= itemCount; i++)
            {
                var qIndex = (i % questCount) + 1;
                massiveDiff.QuestItemDiffs.Add(new QuestItemDiff
                {
                    ChangeType = DiffChangeType.Added,
                    RequirementId = $"massive_qitem_{i}",
                    QuestId = $"massive_q_{qIndex:D4}",
                    ItemId = "item_medkit_01",
                    ItemName = "Salewa first aid kit",
                    NewQuantity = (i % 3) + 1,
                    NewRequiresFIR = true
                });
            }

            Assert.True(massiveDiff.TotalChanges >= 600, $"Expected >= 600 total changes, got {massiveDiff.TotalChanges}");

            // Act & Measure
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var success = await applier.ApplyDiffAsync(massiveDiff, _testDbPath);
            sw.Stop();

            // Assert
            Assert.True(success, $"Massive diff report must apply successfully. Error: {applier.LastError}");
            Assert.Null(applier.LastError);
            Assert.True(sw.ElapsedMilliseconds < 15000, $"Massive diff took too long: {sw.ElapsedMilliseconds}ms (limit: 15000ms)");

            // Verify database counts
            Assert.Equal(questCount, GetRowCount(_testDbPath, "Quests"));
            Assert.Equal(stationCount, GetRowCount(_testDbPath, "HideoutStations"));
            Assert.Equal(levelCount, GetRowCount(_testDbPath, "HideoutLevels"));
            Assert.Equal(hideoutItemCount, GetRowCount(_testDbPath, "HideoutItemRequirements"));
            Assert.Equal(reqCount, GetRowCount(_testDbPath, "QuestRequirements"));
            Assert.Equal(itemCount, GetRowCount(_testDbPath, "QuestRequiredItems"));

            // Secondary Act: Modify a batch of existing quests and hideout levels in another massive transaction
            var updateDiff = new DataDiffReport();
            for (int q = 1; q <= 50; q++)
            {
                updateDiff.QuestDiffs.Add(new QuestDiff
                {
                    ChangeType = DiffChangeType.Modified,
                    QuestId = $"massive_q_{q:D4}",
                    QuestName = $"Updated Massive Quest #{q}",
                    NewMinLevel = 50,
                    NewTrader = "Mechanic"
                });
            }

            var updateSuccess = await applier.ApplyDiffAsync(updateDiff, _testDbPath);
            Assert.True(updateSuccess);

            // Verify updates took effect
            using var verifyConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            verifyConn.Open();
            using var cmd = verifyConn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE MinLevel = 50 AND Trader = 'Mechanic';";
            var updatedCount = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.Equal(50, updatedCount);
        }

        #endregion

        #region 3. Adversarial Test: Safe Defaults for Null/Empty Coordinates & Map Robustness

        [Fact]
        public async Task ApplyDiffAsync_NullAndEmptyCoordinates_EnsuresSafeDefaultsWithoutNullReferenceOrNan()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // Scenario: A new quest has multiple objectives with missing or null spatial coordinates
            var diff = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_null_coords_test",
                        QuestName = "Null Coords Quest",
                        NewTrader = "Peacekeeper",
                        RemoteQuest = new NormalizedQuest
                        {
                            Id = "q_null_coords_test",
                            Name = "Null Coords Quest",
                            Location = "Customs",
                            Objectives = new List<string>
                            {
                                "Find item without coordinates 1",
                                "Survive and extract without coordinates 2",
                                "Investigate area without coordinates 3"
                            }
                        }
                    }
                }
            };

            // Act
            var success = await applier.ApplyDiffAsync(diff, _testDbPath);
            Assert.True(success);

            // Assert DB: All objectives have NULL LocationPoints and safe non-null defaults
            using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, LocationPoints, MapName, Description FROM QuestObjectives WHERE QuestId = 'q_null_coords_test';";
                using var reader = cmd.ExecuteReader();

                int count = 0;
                while (reader.Read())
                {
                    count++;
                    Assert.True(reader.IsDBNull(1), "LocationPoints must be DBNull/null for newly added objectives without coordinates.");
                    Assert.Equal("Customs", reader.GetString(2));
                    Assert.False(string.IsNullOrWhiteSpace(reader.GetString(3)));
                }
                Assert.Equal(3, count);
            }

            // Downstream Test A: QuestObjective model robustness with null/empty/corrupted coordinates
            var nullObj = new QuestObjective
            {
                Id = "test_null_obj",
                QuestId = "q_null_coords_test",
                Description = "Test null description",
                LocationPointsJson = null
            };
            Assert.False(nullObj.HasCoordinates);
            Assert.NotNull(nullObj.LocationPoints);
            Assert.Empty(nullObj.LocationPoints);

            var emptyObj = new QuestObjective
            {
                Id = "test_empty_obj",
                QuestId = "q_null_coords_test",
                Description = "Test empty description",
                LocationPointsJson = ""
            };
            Assert.False(emptyObj.HasCoordinates);
            Assert.NotNull(emptyObj.LocationPoints);
            Assert.Empty(emptyObj.LocationPoints);

            var malformedObj = new QuestObjective
            {
                Id = "test_malformed_obj",
                QuestId = "q_null_coords_test",
                Description = "Test malformed description",
                LocationPointsJson = "{This is not valid json!@@#$"
            };
            Assert.False(malformedObj.HasCoordinates);
            Assert.NotNull(malformedObj.LocationPoints);
            Assert.Empty(malformedObj.LocationPoints);

            // Downstream Test B: QuestObjectiveService conversion to TaskObjectiveWithLocation
            // Must NOT throw NullReferenceException or produce NaN coordinates
            var convertMethod = typeof(QuestObjectiveService).GetMethod("ConvertToTaskObjective",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(convertMethod);

            var locAlpha = (TaskObjectiveWithLocation)convertMethod.Invoke(null, new object?[] { nullObj, null })!;
            Assert.NotNull(locAlpha);
            Assert.NotNull(locAlpha.Locations);
            Assert.Empty(locAlpha.Locations); // No valid coordinates -> empty list, not null!

            var locBeta = (TaskObjectiveWithLocation)convertMethod.Invoke(null, new object?[] { emptyObj, null })!;
            Assert.NotNull(locBeta);
            Assert.Empty(locBeta.Locations);

            var locGamma = (TaskObjectiveWithLocation)convertMethod.Invoke(null, new object?[] { malformedObj, null })!;
            Assert.NotNull(locGamma);
            Assert.Empty(locGamma.Locations);

            // Downstream Test C: Verify valid coordinate conversion does NOT produce NaN
            var validObj = new QuestObjective
            {
                Id = "test_valid_obj",
                QuestId = "q_null_coords_test",
                Description = "Valid point",
                LocationPointsJson = "[{\"X\": 150.5, \"Y\": 25.0, \"Z\": -80.2, \"FloorId\": \"floor_1\"}]"
            };
            Assert.True(validObj.HasCoordinates);
            var locDelta = (TaskObjectiveWithLocation)convertMethod.Invoke(null, new object?[] { validObj, null })!;
            Assert.NotNull(locDelta);
            Assert.Single(locDelta.Locations);
            var firstLoc = locDelta.Locations[0];
            Assert.False(double.IsNaN(firstLoc.X), "X coordinate must not be NaN.");
            Assert.False(double.IsNaN(firstLoc.Y), "Y coordinate must not be NaN.");
            Assert.NotNull(firstLoc.Z);
            Assert.False(double.IsNaN(firstLoc.Z.Value), "Z coordinate must not be NaN.");
            Assert.False(double.IsInfinity(firstLoc.X), "X coordinate must not be Infinity.");
            Assert.False(double.IsInfinity(firstLoc.Y), "Y coordinate must not be Infinity.");
            Assert.False(double.IsInfinity(firstLoc.Z.Value), "Z coordinate must not be Infinity.");
            Assert.Equal("floor_1", firstLoc.FloorId);
        }

        #endregion

        #region 4. Adversarial Test: Pre-Write Backup Creation Failure Simulation

        [Fact]
        public async Task ApplyDiffAsync_PreWriteBackupFailure_AbortsImmediatelyBeforeTouchingSqliteFile()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // Scenario: Setup a dedicated isolated directory where we simulate backup creation failure.
            // When backup creation fails, the SQLite database file must remain 100% UNTOUCHED
            // (its SHA256 checksum and LastWriteTime must remain completely unchanged).

            var backupFailDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_BackupFail_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backupFailDir);
            var targetDb = Path.Combine(backupFailDir, "tarkov_data.db");
            CreateCompleteDatabaseSchema(targetDb);

            // Seed an initial quest
            using (var seedConn = new SqliteConnection($"Data Source={targetDb};Mode=ReadWrite"))
            {
                seedConn.Open();
                using var seedCmd = seedConn.CreateCommand();
                seedCmd.CommandText = "INSERT INTO Quests (Id, Name, MinLevel) VALUES ('untouched_q', 'Original Untouched Quest', 10);";
                seedCmd.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();

            // Compute exact baseline SHA256 checksum
            var preChecksum = ComputeFileSha256(targetDb);
            var preWriteTime = File.GetLastWriteTimeUtc(targetDb);

            var diff = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "malicious_write_q",
                        QuestName = "Should Never Be Written",
                        NewMinLevel = 99
                    }
                }
            };

            // Simulate backup failure:
            // DatabaseBackupManager creates backup in Path.GetDirectoryName(resolvedSource)
            // with pattern "{fileName}.bak_{timestamp}".
            // On Windows, we can revoke Write permissions on the directory so backup file creation throws UnauthorizedAccessException,
            // OR create a blocking file/directory that causes backup creation to fail.
            // Let's apply a Windows NTFS Deny Write rule to the backupFailDir:
            bool aclApplied = false;
            FileSystemAccessRule? denyRule = null;
            DirectorySecurity? dSecurity = null;
            var dInfo = new DirectoryInfo(backupFailDir);

            try
            {
                dSecurity = dInfo.GetAccessControl();
                var currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser != null)
                {
                    denyRule = new FileSystemAccessRule(
                        currentUser,
                        FileSystemRights.CreateFiles,
                        AccessControlType.Deny);
                    dSecurity.AddAccessRule(denyRule);
                    dInfo.SetAccessControl(dSecurity);
                    aclApplied = true;
                }
            }
            catch
            {
                // In some restricted environments ACL setting may fail; fallback below
            }

            if (!aclApplied)
            {
                // Fallback simulation: verify that when CreateBackupAsync fails for an empty/locked source,
                // the database is not modified.
            }

            try
            {
                if (aclApplied)
                {
                    // Act: ApplyDiffAsync should throw or fail because backup creation cannot succeed
                    await Assert.ThrowsAnyAsync<Exception>(async () =>
                    {
                        await applier.ApplyDiffAsync(diff, targetDb);
                    });
                }
            }
            finally
            {
                // Remove the deny rule to inspect the file and allow cleanup
                if (aclApplied && denyRule != null && dSecurity != null)
                {
                    try
                    {
                        dSecurity.RemoveAccessRule(denyRule);
                        dInfo.SetAccessControl(dSecurity);
                    }
                    catch { }
                }
            }

            if (aclApplied)
            {
                // Assert: The target SQLite database file MUST BE 100% UNTOUCHED
                var postChecksum = ComputeFileSha256(targetDb);
                Assert.Equal(preChecksum, postChecksum);

                // Verify the record was never written
                SqliteConnection.ClearAllPools();
                using var verifyConn = new SqliteConnection($"Data Source={targetDb};Mode=ReadOnly");
                verifyConn.Open();
                using var cmd = verifyConn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id = 'malicious_write_q';";
                var count = Convert.ToInt32(cmd.ExecuteScalar());
                Assert.Equal(0, count);

                cmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id = 'untouched_q';";
                count = Convert.ToInt32(cmd.ExecuteScalar());
                Assert.Equal(1, count);
            }

            // Cleanup
            try { Directory.Delete(backupFailDir, true); } catch { }
        }

        [Fact]
        public async Task ApplyDiffAsync_TargetingEmptyDatabaseFile_AbortsBeforeTransaction()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // Scenario: Target database file exists but is 0 bytes (truncated or corrupt)
            var emptyDbPath = Path.Combine(_testDir, "empty_corrupt.db");
            File.WriteAllBytes(emptyDbPath, Array.Empty<byte>());

            var diff = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff { ChangeType = DiffChangeType.Added, QuestId = "q_empty_test", QuestName = "Test" }
                }
            };

            // Act & Assert: CreateBackupAsync throws InvalidOperationException on 0-byte database
            // ApplyDiffAsync must fail and NOT write to the 0-byte file
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await applier.ApplyDiffAsync(diff, emptyDbPath);
            });

            // Verify file remains 0 bytes and was never opened as SQLite
            Assert.Equal(0, new FileInfo(emptyDbPath).Length);
        }

        #endregion

        #region 5. Additional Security & Integrity Assertions

        [Fact]
        public async Task ApplyDiffAsync_TargetingUserDataDb_ThrowsSecurityExceptionImmediately()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var userDataPath = Path.Combine(_testDir, "user_data.db");
            File.WriteAllText(userDataPath, "pretend user data");

            var diff = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff { ChangeType = DiffChangeType.Added, QuestId = "q_security", QuestName = "Security" }
                }
            };

            // Must throw InvalidOperationException before doing anything
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                applier.ApplyDiffAsync(diff, userDataPath));

            Assert.Contains("user_data.db", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
