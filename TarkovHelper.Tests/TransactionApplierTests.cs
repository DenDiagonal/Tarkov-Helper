using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services;
using TarkovHelper.Services.DataAudit;
using TarkovHelper.Windows.Dialogs;
using Xunit;

namespace TarkovHelper.Tests
{
    [Collection("DatabaseUpdateServiceCollection")]
    public class TransactionApplierTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _testDbPath;

        public TransactionApplierTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_TxTests_" + Guid.NewGuid().ToString("N"));
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

        #endregion

        #region 1. Validation & Preconditions

        [Fact]
        public async Task ApplyDiffAsync_WhenDiffIsNull_ThrowsArgumentNullException()
        {
            var applier = DatabaseTransactionApplier.Instance;
            await Assert.ThrowsAsync<ArgumentNullException>(() => applier.ApplyDiffAsync(null!, _testDbPath));
        }

        [Fact]
        public async Task ApplyDiffAsync_WhenTargetIsUserDataDb_ThrowsInvalidOperationException()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff { ChangeType = DiffChangeType.Added, QuestId = "q_fail", QuestName = "Malicious" }
                }
            };

            var maliciousPath = Path.Combine(_testDir, "user_data.db");
            File.WriteAllText(maliciousPath, "dummy");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                applier.ApplyDiffAsync(report, maliciousPath));
        }

        [Fact]
        public async Task ApplyDiffAsync_WhenTargetDoesNotExist_ThrowsFileNotFoundException()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff { ChangeType = DiffChangeType.Added, QuestId = "q_new", QuestName = "New" }
                }
            };

            var nonExistent = Path.Combine(_testDir, "non_existent.db");
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                applier.ApplyDiffAsync(report, nonExistent));
        }

        [Fact]
        public async Task ApplyDiffAsync_WhenReportHasNoChanges_ReturnsTrueWithoutModifyingDb()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var report = new DataDiffReport();

            var result = await applier.ApplyDiffAsync(report, _testDbPath);
            Assert.True(result);
            Assert.Null(applier.LastBackupPath);
        }

        [Fact]
        public async Task ApplyDiffAsync_CreatesDatedBackupBeforeModifications()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_backup_check",
                        QuestName = "Backup Check Quest"
                    }
                }
            };

            var result = await applier.ApplyDiffAsync(report, _testDbPath);
            Assert.True(result);

            Assert.NotNull(applier.LastBackupPath);
            Assert.True(File.Exists(applier.LastBackupPath));
            Assert.Contains(".bak_", applier.LastBackupPath);
            Assert.True(new FileInfo(applier.LastBackupPath).Length > 0);
        }

        #endregion

        #region 2. Quest & Objectives Atomic Updates

        [Fact]
        public async Task ApplyDiffAsync_AddedQuest_InsertsQuestAndObjectivesWithSafeDefaults()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_new_quest",
                        BsgId = "bsg_new_quest",
                        QuestName = "Gunsmith - Part 1",
                        NewTrader = "Mechanic",
                        NewMinLevel = 2,
                        NewFaction = "Any",
                        NewKappaRequired = true,
                        RemoteQuest = new NormalizedQuest
                        {
                            Id = "q_new_quest",
                            Name = "Gunsmith - Part 1",
                            NameEN = "Gunsmith - Part 1",
                            Trader = "Mechanic",
                            Location = "Customs",
                            Objectives = new List<string>
                            {
                                "Modify an MP-133 shotgun according to specifications",
                                "Hand over the modified MP-133"
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

            // Verify quest insertion
            await using var questCmd = conn.CreateCommand();
            questCmd.CommandText = "SELECT Id, Name, Trader, MinLevel, KappaRequired, IsApproved, ApprovedAt FROM Quests WHERE Id = 'q_new_quest';";
            await using var reader = await questCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("q_new_quest", reader.GetString(0));
            Assert.Equal("Gunsmith - Part 1", reader.GetString(1));
            Assert.Equal("Mechanic", reader.GetString(2));
            Assert.Equal(2, reader.GetInt32(3));
            Assert.Equal(1, reader.GetInt32(4));
            Assert.Equal(0, reader.GetInt32(5)); // Safe default IsApproved = 0
            Assert.True(reader.IsDBNull(6)); // Safe default ApprovedAt = null

            // Verify objectives insertion with safe null coordinates
            await using var objCmd = conn.CreateCommand();
            objCmd.CommandText = "SELECT Id, Description, LocationPoints, IsApproved FROM QuestObjectives WHERE QuestId = 'q_new_quest' ORDER BY SortOrder;";
            await using var objReader = await objCmd.ExecuteReaderAsync();

            Assert.True(await objReader.ReadAsync());
            Assert.Equal("q_new_quest_0", objReader.GetString(0));
            Assert.Equal("Modify an MP-133 shotgun according to specifications", objReader.GetString(1));
            Assert.True(objReader.IsDBNull(2) || string.IsNullOrEmpty(objReader.GetString(2))); // LocationPoints safe null
            Assert.Equal(0, objReader.GetInt32(3)); // IsApproved = 0

            Assert.True(await objReader.ReadAsync());
            Assert.Equal("q_new_quest_1", objReader.GetString(0));
            Assert.Equal("Hand over the modified MP-133", objReader.GetString(1));
            Assert.True(objReader.IsDBNull(2) || string.IsNullOrEmpty(objReader.GetString(2)));
        }

        [Fact]
        public async Task ApplyDiffAsync_ModifiedQuest_UpdatesSpecifiedFieldsOnly()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Modified,
                        QuestId = "quest_debut",
                        QuestName = "Debut",
                        OldMinLevel = 1,
                        NewMinLevel = 5,
                        OldTrader = "Prapor",
                        NewTrader = "Therapist",
                        NewKappaRequired = false
                    }
                }
            };

            var success = await applier.ApplyDiffAsync(report, _testDbPath);
            Assert.True(success);

            SqliteConnection.ClearAllPools();
            await using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Name, Trader, MinLevel, KappaRequired, Location FROM Quests WHERE Id = 'quest_debut';";
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Debut", reader.GetString(0));
            Assert.Equal("Therapist", reader.GetString(1));
            Assert.Equal(5, reader.GetInt32(2));
            Assert.Equal(0, reader.GetInt32(3));
            Assert.Equal("Customs", reader.GetString(4)); // Unchanged field preserved
        }

        [Fact]
        public async Task ApplyDiffAsync_RemovedQuest_DeletesQuestAndCascadesRequirementsAndItems()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Removed,
                        QuestId = "quest_debut",
                        QuestName = "Debut"
                    }
                }
            };

            var success = await applier.ApplyDiffAsync(report, _testDbPath);
            Assert.True(success);

            SqliteConnection.ClearAllPools();
            await using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    (SELECT COUNT(*) FROM Quests WHERE Id = 'quest_debut'),
                    (SELECT COUNT(*) FROM QuestRequirements WHERE QuestId = 'quest_debut'),
                    (SELECT COUNT(*) FROM QuestRequiredItems WHERE QuestId = 'quest_debut');";
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0, reader.GetInt32(0)); // Quest removed
            Assert.Equal(0, reader.GetInt32(1)); // Cascaded requirements removed
            Assert.Equal(0, reader.GetInt32(2)); // Cascaded required items removed
        }

        #endregion

        #region 3. Quest Requirements & Items Atomic Updates

        [Fact]
        public async Task ApplyDiffAsync_QuestRequirements_AppliesAddModifyRemove()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // 1. First add a second quest to serve as prerequisite
            var reportAddSecond = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "quest_supplier",
                        QuestName = "Supplier",
                        NewTrader = "Skier"
                    }
                }
            };
            await applier.ApplyDiffAsync(reportAddSecond, _testDbPath);

            // 2. Add requirement on Supplier, modify existing requirement, and remove a non-existent one
            var reportReqs = new DataDiffReport
            {
                QuestRequirementDiffs = new List<QuestRequirementDiff>
                {
                    new QuestRequirementDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = "req_debut_supplier",
                        QuestId = "quest_debut",
                        RequiredQuestId = "quest_supplier",
                        NewRequirementType = "Active",
                        NewGroupId = 1
                    },
                    new QuestRequirementDiff
                    {
                        ChangeType = DiffChangeType.Modified,
                        RequirementId = "req_debut_prereq",
                        QuestId = "quest_debut",
                        RequiredQuestId = "quest_prereq",
                        NewRequirementType = "Fail",
                        NewDelayMinutes = 60
                    }
                }
            };

            var success = await applier.ApplyDiffAsync(reportReqs, _testDbPath);
            Assert.True(success);

            SqliteConnection.ClearAllPools();
            await using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            await conn.OpenAsync();

            // Verify new requirement
            await using var cmd1 = conn.CreateCommand();
            cmd1.CommandText = "SELECT RequirementType, GroupId FROM QuestRequirements WHERE Id = 'req_debut_supplier';";
            await using var r1 = await cmd1.ExecuteReaderAsync();
            Assert.True(await r1.ReadAsync());
            Assert.Equal("Active", r1.GetString(0));
            Assert.Equal(1, r1.GetInt32(1));

            // Verify modified requirement
            await using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = "SELECT RequirementType, DelayMinutes FROM QuestRequirements WHERE Id = 'req_debut_prereq';";
            await using var r2 = await cmd2.ExecuteReaderAsync();
            Assert.True(await r2.ReadAsync());
            Assert.Equal("Fail", r2.GetString(0));
            Assert.Equal(60, r2.GetInt32(1));
        }

        [Fact]
        public async Task ApplyDiffAsync_QuestRequiredItems_AppliesAddModifyRemove()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var report = new DataDiffReport
            {
                QuestItemDiffs = new List<QuestItemDiff>
                {
                    new QuestItemDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = "item_req_debut_roubles",
                        QuestId = "quest_debut",
                        ItemId = "item_roubles",
                        ItemName = "Roubles",
                        NewQuantity = 50000,
                        NewRequiresFIR = false,
                        NewRequirementType = "Handover"
                    },
                    new QuestItemDiff
                    {
                        ChangeType = DiffChangeType.Modified,
                        RequirementId = "item_req_debut",
                        QuestId = "quest_debut",
                        ItemId = "item_salewa",
                        ItemName = "Salewa first aid kit",
                        NewQuantity = 5,
                        NewRequiresFIR = true
                    }
                }
            };

            var success = await applier.ApplyDiffAsync(report, _testDbPath);
            Assert.True(success);

            SqliteConnection.ClearAllPools();
            await using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            await conn.OpenAsync();

            // Verify added item
            await using var cmd1 = conn.CreateCommand();
            cmd1.CommandText = "SELECT Count, RequiresFIR, RequirementType FROM QuestRequiredItems WHERE Id = 'item_req_debut_roubles';";
            await using var r1 = await cmd1.ExecuteReaderAsync();
            Assert.True(await r1.ReadAsync());
            Assert.Equal(50000, r1.GetInt32(0));
            Assert.Equal(0, r1.GetInt32(1));
            Assert.Equal("Handover", r1.GetString(2));

            // Verify modified item
            await using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = "SELECT Count, RequiresFIR FROM QuestRequiredItems WHERE Id = 'item_req_debut';";
            await using var r2 = await cmd2.ExecuteReaderAsync();
            Assert.True(await r2.ReadAsync());
            Assert.Equal(5, r2.GetInt32(0));
            Assert.Equal(1, r2.GetInt32(1));
        }

        #endregion

        #region 4. Hideout Atomic Updates

        [Fact]
        public async Task ApplyDiffAsync_HideoutStationsLevelsAndItems_AppliesAllAtomicUpdates()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var report = new DataDiffReport
            {
                HideoutStationDiffs = new List<HideoutStationDiff>
                {
                    new HideoutStationDiff
                    {
                        ChangeType = DiffChangeType.Modified,
                        StationId = "station_vents",
                        StationName = "Vents",
                        NewMaxLevel = 4
                    }
                },
                HideoutLevelDiffs = new List<HideoutLevelDiff>
                {
                    new HideoutLevelDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        LevelId = "level_vents_2",
                        StationId = "station_vents",
                        StationName = "Vents",
                        Level = 2,
                        NewConstructionTime = 7200
                    }
                },
                HideoutItemDiffs = new List<HideoutItemDiff>
                {
                    new HideoutItemDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = "hireq_vents_2",
                        StationId = "station_vents",
                        StationName = "Vents",
                        Level = 2,
                        ItemId = "item_roubles",
                        ItemName = "Roubles",
                        NewQuantity = 100000,
                        NewFoundInRaid = false
                    }
                }
            };

            var success = await applier.ApplyDiffAsync(report, _testDbPath);
            Assert.True(success);

            SqliteConnection.ClearAllPools();
            await using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            await conn.OpenAsync();

            // Verify station MaxLevel updated
            await using var cmdStation = conn.CreateCommand();
            cmdStation.CommandText = "SELECT MaxLevel FROM HideoutStations WHERE Id = 'station_vents';";
            var maxLvl = Convert.ToInt32(await cmdStation.ExecuteScalarAsync());
            Assert.Equal(4, maxLvl);

            // Verify level 2 inserted
            await using var cmdLevel = conn.CreateCommand();
            cmdLevel.CommandText = "SELECT ConstructionTime FROM HideoutLevels WHERE StationId = 'station_vents' AND Level = 2;";
            var constTime = Convert.ToInt32(await cmdLevel.ExecuteScalarAsync());
            Assert.Equal(7200, constTime);

            // Verify item requirement for level 2 inserted
            await using var cmdItem = conn.CreateCommand();
            cmdItem.CommandText = "SELECT Count, FoundInRaid FROM HideoutItemRequirements WHERE StationId = 'station_vents' AND Level = 2;";
            await using var itemReader = await cmdItem.ExecuteReaderAsync();
            Assert.True(await itemReader.ReadAsync());
            Assert.Equal(100000, itemReader.GetInt32(0));
            Assert.Equal(0, itemReader.GetInt32(1));
        }

        #endregion

        #region 5. Integrity & Rollback on Error (CRITICAL REQUIREMENT)

        [Fact]
        public async Task ApplyDiffAsync_WhenConstraintFails_RollsBackEntireTransactionCompletelyWithZeroPartialWrites()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // Record initial state
            SqliteConnection.ClearAllPools();
            int initialQuestCount;
            int initialReqCount;
            await using (var countConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
            {
                await countConn.OpenAsync();
                await using var cmd = countConn.CreateCommand();
                cmd.CommandText = "SELECT (SELECT COUNT(*) FROM Quests), (SELECT COUNT(*) FROM QuestRequirements);";
                await using var r = await cmd.ExecuteReaderAsync();
                Assert.True(await r.ReadAsync());
                initialQuestCount = r.GetInt32(0);
                initialReqCount = r.GetInt32(1);
            }

            // Create a compound diff report:
            // 1. Valid quest addition ("q_valid_staging")
            // 2. INVALID requirement addition referencing a non-existent parent quest ("quest_NON_EXISTENT")
            // With PRAGMA foreign_keys = ON, operation 2 WILL fail with SQLite Foreign Key constraint violation!
            var reportWithFkViolation = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_valid_staging",
                        QuestName = "Valid Staging Quest",
                        NewTrader = "Prapor"
                    }
                },
                QuestRequirementDiffs = new List<QuestRequirementDiff>
                {
                    new QuestRequirementDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = "req_invalid_fk",
                        QuestId = "q_valid_staging",
                        RequiredQuestId = "quest_NON_EXISTENT_ID_9999", // Violates FOREIGN KEY (RequiredQuestId) REFERENCES Quests(Id)
                        NewRequirementType = "Complete"
                    }
                }
            };

            // Act: Apply diff with FK constraint violation
            var result = await applier.ApplyDiffAsync(reportWithFkViolation, _testDbPath, resilientPrerequisites: false);

            // Assert: Transaction must fail and report error
            Assert.False(result);
            Assert.NotNull(applier.LastError);
            Assert.Contains("FOREIGN KEY", applier.LastError, StringComparison.OrdinalIgnoreCase);

            // Assert: ZERO partial writes! "q_valid_staging" must NOT exist in the database!
            SqliteConnection.ClearAllPools();
            await using (var verifyConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
            {
                await verifyConn.OpenAsync();

                // 1. Verify "q_valid_staging" was rolled back and does not exist
                await using var checkCmd = verifyConn.CreateCommand();
                checkCmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id = 'q_valid_staging';";
                var stagedExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                Assert.Equal(0, stagedExists);

                // 2. Verify total quest and requirement counts are 100% identical to initial counts
                await using var countsCmd = verifyConn.CreateCommand();
                countsCmd.CommandText = "SELECT (SELECT COUNT(*) FROM Quests), (SELECT COUNT(*) FROM QuestRequirements);";
                await using var r = await countsCmd.ExecuteReaderAsync();
                Assert.True(await r.ReadAsync());
                Assert.Equal(initialQuestCount, r.GetInt32(0));
                Assert.Equal(initialReqCount, r.GetInt32(1));

                // 3. Verify SQLite integrity is 100% intact
                await using var pragmaCmd = verifyConn.CreateCommand();
                pragmaCmd.CommandText = "PRAGMA integrity_check;";
                var integrity = (string?)await pragmaCmd.ExecuteScalarAsync();
                Assert.Equal("ok", integrity);
            }
        }

        #endregion

        #region 6. Spatial Coordinates Safe Handling

        [Fact]
        public async Task ApplyDiffAsync_SafeNullCoordinateHandling_AllowsQueryingWithoutNaNOrExceptions()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_spatial_test",
                        QuestName = "Spatial Test Quest",
                        RemoteQuest = new NormalizedQuest
                        {
                            Id = "q_spatial_test",
                            Name = "Spatial Test Quest",
                            Objectives = new List<string>
                            {
                                "Objective with safe null location points 1",
                                "Objective with safe null location points 2"
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
            cmd.CommandText = "SELECT LocationPoints, OptionalPoints FROM QuestObjectives WHERE QuestId = 'q_spatial_test';";
            await using var reader = await cmd.ExecuteReaderAsync();

            int objectiveCount = 0;
            while (await reader.ReadAsync())
            {
                objectiveCount++;
                var locPoints = reader.IsDBNull(0) ? null : reader.GetString(0);
                var optPoints = reader.IsDBNull(1) ? null : reader.GetString(1);

                // Safe null or empty coordinates: must not be "NaN" or throw
                Assert.True(string.IsNullOrEmpty(locPoints));
                Assert.True(string.IsNullOrEmpty(optPoints));
            }

            Assert.Equal(2, objectiveCount);
        }

        #endregion

        #region 7. Hot-Reload & Event Dispatch

        [Fact]
        public async Task ApplyDiffAsync_HotReload_DispatchesDatabaseUpdatedEventAndClearsPools()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_event_test",
                        QuestName = "Event Test Quest"
                    }
                }
            };

            bool eventFired = false;
            EventHandler handler = (s, e) => { eventFired = true; };

            DatabaseUpdateService.Instance.DatabaseUpdated += handler;
            try
            {
                var success = await applier.ApplyDiffAsync(report, _testDbPath);
                Assert.True(success);
                Assert.True(eventFired, "DatabaseUpdated event must be fired upon successful diff application.");
            }
            finally
            {
                DatabaseUpdateService.Instance.DatabaseUpdated -= handler;
            }
        }

        [Fact]
        public async Task OrchestrateHotReloadAsync_ExecutesCleanlyWithoutThrowingExceptions()
        {
            var applier = DatabaseTransactionApplier.Instance;
            var ex = await Record.ExceptionAsync(() => applier.OrchestrateHotReloadAsync());
            Assert.Null(ex);
        }

        #endregion

        #region 8. DiffViewerDialog Connection

        [Fact]
        public void DiffViewerDialog_ApplyChangesAsync_AppliesToTargetDatabaseAndSetsWasApplied()
        {
            DiffViewerDialog? dialog = null;
            var success = ExecuteOnStaThread(async () =>
            {
                var report = new DataDiffReport
                {
                    DatabasePath = _testDbPath,
                    QuestDiffs = new List<QuestDiff>
                    {
                        new QuestDiff
                        {
                            ChangeType = DiffChangeType.Added,
                            QuestId = "q_dialog_apply",
                            QuestName = "Dialog Apply Quest",
                            NewTrader = "Peacekeeper"
                        }
                    }
                };

                dialog = new DiffViewerDialog(report);
                Assert.NotNull(dialog);
                Assert.False(dialog.WasApplied);

                // Act: invoke programmatic ApplyChangesAsync
                var applied = await dialog.ApplyChangesAsync();
                Assert.True(dialog.WasApplied);
                return applied;
            });

            Assert.True(success);
            Assert.NotNull(dialog);
            Assert.True(dialog.WasApplied);

            // Verify quest was inserted into target db
            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id = 'q_dialog_apply';";
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.Equal(1, count);
        }

        private static T ExecuteOnStaThread<T>(Func<Task<T>> func)
        {
            T result = default!;
            Exception? threadEx = null;
            var staThread = new Thread(() =>
            {
                try
                {
                    result = func().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            bool finished = staThread.Join(TimeSpan.FromSeconds(15));
            if (!finished) throw new TimeoutException("STA thread timed out");
            if (threadEx != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadEx).Throw();
            return result;
        }

        #endregion

        #region 9. Milestone M1 Resilience & Multi-Table Cascade Tests

        [Fact]
        public async Task ApplyDiffAsync_WithMissingPrerequisite_DefaultsToResilient_SucceedsAndSkipsMissingReq()
        {
            var applier = DatabaseTransactionApplier.Instance;
            Assert.True(applier.ResilientPrerequisites, "ResilientPrerequisites must default to true.");

            // Construct diff report where a quest requirement references a non-existent prerequisite quest
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_m1_resilient_valid",
                        QuestName = "M1 Resilient Quest",
                        NewTrader = "Prapor",
                        NewMinLevel = 10
                    }
                },
                QuestRequirementDiffs = new List<QuestRequirementDiff>
                {
                    new QuestRequirementDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_m1_resilient_valid",
                        RequiredQuestId = "ghost_quest_non_existent_9999", // Does NOT exist in Quests
                        NewRequirementType = "Complete"
                    }
                }
            };

            // Act: Apply diff with resilientPrerequisites omitted (defaults to true)
            var success = await applier.ApplyDiffAsync(report, _testDbPath);

            // Assert: Transaction succeeds cleanly without Error 19
            Assert.True(success);
            Assert.Null(applier.LastError);

            SqliteConnection.ClearAllPools();
            await using var verifyConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            await verifyConn.OpenAsync();

            // Verify quest was inserted
            await using var chkCmd = verifyConn.CreateCommand();
            chkCmd.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id = 'q_m1_resilient_valid';";
            var questCount = Convert.ToInt32(await chkCmd.ExecuteScalarAsync());
            Assert.Equal(1, questCount);

            // Verify missing prerequisite was safely skipped
            await using var chkReqCmd = verifyConn.CreateCommand();
            chkReqCmd.CommandText = "SELECT COUNT(*) FROM QuestRequirements WHERE QuestId = 'q_m1_resilient_valid';";
            var reqCount = Convert.ToInt32(await chkReqCmd.ExecuteScalarAsync());
            Assert.Equal(0, reqCount);

            // Verify SQLite integrity is 100% ok
            await using var pragmaCmd = verifyConn.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA integrity_check;";
            var integrity = (string?)await pragmaCmd.ExecuteScalarAsync();
            Assert.Equal("ok", integrity);
        }

        [Fact]
        public async Task ApplyDiffAsync_RemovedQuest_CascadesAllSixDependentTablesWithoutError19()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // Ensure all 6 child tables exist in test database
            using (var initConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadWrite"))
            {
                initConn.Open();
                using var tableCmd = initConn.CreateCommand();
                tableCmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS QuestTraderRequirements (
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

                    CREATE TABLE IF NOT EXISTS QuestPoolRequirements (
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

                    CREATE TABLE IF NOT EXISTS OptionalQuests (
                        Id TEXT PRIMARY KEY,
                        QuestId TEXT NOT NULL,
                        AlternativeQuestId TEXT NOT NULL,
                        Description TEXT,
                        ContentHash TEXT,
                        IsApproved INTEGER NOT NULL DEFAULT 0,
                        ApprovedAt TEXT,
                        UpdatedAt TEXT,
                        FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE,
                        FOREIGN KEY (AlternativeQuestId) REFERENCES Quests(Id) ON DELETE CASCADE
                    );
                ";
                tableCmd.ExecuteNonQuery();

                // Seed target quest to remove and a peer quest
                using var seedCmd = initConn.CreateCommand();
                seedCmd.CommandText = @"
                    INSERT OR REPLACE INTO Quests (Id, Name, Trader, Location, MinLevel)
                    VALUES ('q_cascade_target', 'Cascade Target Quest', 'Prapor', 'Customs', 5);

                    INSERT OR REPLACE INTO Quests (Id, Name, Trader, Location, MinLevel)
                    VALUES ('q_cascade_peer', 'Cascade Peer Quest', 'Prapor', 'Customs', 5);

                    -- 1. QuestRequirements (direct & reverse)
                    INSERT OR REPLACE INTO QuestRequirements (Id, QuestId, RequiredQuestId, RequirementType, GroupId)
                    VALUES ('req_casc_1', 'q_cascade_target', 'q_cascade_peer', 'Complete', 0);
                    INSERT OR REPLACE INTO QuestRequirements (Id, QuestId, RequiredQuestId, RequirementType, GroupId)
                    VALUES ('req_casc_rev', 'q_cascade_peer', 'q_cascade_target', 'Complete', 0);

                    -- 2. QuestObjectives
                    INSERT OR REPLACE INTO QuestObjectives (Id, QuestId, SortOrder, Description)
                    VALUES ('obj_casc_1', 'q_cascade_target', 0, 'Objective for cascade quest');

                    -- 3. QuestTraderRequirements
                    INSERT OR REPLACE INTO QuestTraderRequirements (Id, QuestId, TraderName, RequiredLevel)
                    VALUES ('tr_casc_1', 'q_cascade_target', 'Prapor', 2);

                    -- 4. QuestPoolRequirements
                    INSERT OR REPLACE INTO QuestPoolRequirements (Id, QuestId, PoolTrader, RequiredCompletedCount)
                    VALUES ('pool_casc_1', 'q_cascade_target', 'Prapor', 3);

                    -- 5. QuestRequiredItems
                    INSERT OR REPLACE INTO QuestRequiredItems (Id, QuestId, ItemName, Count)
                    VALUES ('item_casc_1', 'q_cascade_target', 'Salewa first aid kit', 2);

                    -- 6. OptionalQuests (direct & reverse)
                    INSERT OR REPLACE INTO OptionalQuests (Id, QuestId, AlternativeQuestId, Description)
                    VALUES ('opt_casc_1', 'q_cascade_target', 'q_cascade_peer', 'Optional link');
                    INSERT OR REPLACE INTO OptionalQuests (Id, QuestId, AlternativeQuestId, Description)
                    VALUES ('opt_casc_rev', 'q_cascade_peer', 'q_cascade_target', 'Reverse optional link');
                ";
                seedCmd.ExecuteNonQuery();
            }

            // Verify all 6 child tables have records referencing q_cascade_target
            using (var chkPreConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
            {
                chkPreConn.Open();
                using var cmd = chkPreConn.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        (SELECT COUNT(*) FROM Quests WHERE Id = 'q_cascade_target'),
                        (SELECT COUNT(*) FROM QuestRequirements WHERE QuestId = 'q_cascade_target' OR RequiredQuestId = 'q_cascade_target'),
                        (SELECT COUNT(*) FROM QuestObjectives WHERE QuestId = 'q_cascade_target'),
                        (SELECT COUNT(*) FROM QuestTraderRequirements WHERE QuestId = 'q_cascade_target'),
                        (SELECT COUNT(*) FROM QuestPoolRequirements WHERE QuestId = 'q_cascade_target'),
                        (SELECT COUNT(*) FROM QuestRequiredItems WHERE QuestId = 'q_cascade_target'),
                        (SELECT COUNT(*) FROM OptionalQuests WHERE QuestId = 'q_cascade_target' OR AlternativeQuestId = 'q_cascade_target');
                ";
                using var r = cmd.ExecuteReader();
                Assert.True(r.Read());
                Assert.Equal(1, r.GetInt32(0)); // Quest
                Assert.Equal(2, r.GetInt32(1)); // QuestRequirements (direct + reverse)
                Assert.Equal(1, r.GetInt32(2)); // QuestObjectives
                Assert.Equal(1, r.GetInt32(3)); // QuestTraderRequirements
                Assert.Equal(1, r.GetInt32(4)); // QuestPoolRequirements
                Assert.Equal(1, r.GetInt32(5)); // QuestRequiredItems
                Assert.Equal(2, r.GetInt32(6)); // OptionalQuests (direct + reverse)
            }

            // Construct diff report removing q_cascade_target
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Removed,
                        QuestId = "q_cascade_target",
                        QuestName = "Cascade Target Quest"
                    }
                }
            };

            // Act: Apply diff with explicit cascade deletion under PRAGMA foreign_keys = ON
            var success = await applier.ApplyDiffAsync(report, _testDbPath);

            // Assert: Transaction succeeds without Error 19
            Assert.True(success);
            Assert.Null(applier.LastError);

            // Assert: Verify all 6 child tables and Quests table are 100% cleaned of q_cascade_target
            SqliteConnection.ClearAllPools();
            using (var verifyConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
            {
                verifyConn.Open();
                using var cmd = verifyConn.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        (SELECT COUNT(*) FROM Quests WHERE Id = 'q_cascade_target'),
                        (SELECT COUNT(*) FROM QuestRequirements WHERE QuestId = 'q_cascade_target' OR RequiredQuestId = 'q_cascade_target'),
                        (SELECT COUNT(*) FROM QuestObjectives WHERE QuestId = 'q_cascade_target'),
                        (SELECT COUNT(*) FROM QuestTraderRequirements WHERE QuestId = 'q_cascade_target'),
                        (SELECT COUNT(*) FROM QuestPoolRequirements WHERE QuestId = 'q_cascade_target'),
                        (SELECT COUNT(*) FROM QuestRequiredItems WHERE QuestId = 'q_cascade_target'),
                        (SELECT COUNT(*) FROM OptionalQuests WHERE QuestId = 'q_cascade_target' OR AlternativeQuestId = 'q_cascade_target'),
                        (SELECT COUNT(*) FROM Quests WHERE Id = 'q_cascade_peer');
                ";
                using var r = cmd.ExecuteReader();
                Assert.True(r.Read());
                Assert.Equal(0, r.GetInt32(0)); // Quest deleted
                Assert.Equal(0, r.GetInt32(1)); // All QuestRequirements deleted (including reverse!)
                Assert.Equal(0, r.GetInt32(2)); // QuestObjectives deleted
                Assert.Equal(0, r.GetInt32(3)); // QuestTraderRequirements deleted
                Assert.Equal(0, r.GetInt32(4)); // QuestPoolRequirements deleted
                Assert.Equal(0, r.GetInt32(5)); // QuestRequiredItems deleted
                Assert.Equal(0, r.GetInt32(6)); // All OptionalQuests deleted (including reverse!)
                Assert.Equal(1, r.GetInt32(7)); // Peer quest remains untouched

                // Foreign key consistency and database integrity checks
                using var fkCmd = verifyConn.CreateCommand();
                fkCmd.CommandText = "PRAGMA foreign_key_check;";
                using var fkReader = fkCmd.ExecuteReader();
                Assert.False(fkReader.Read(), "PRAGMA foreign_key_check must return 0 violations after cascade deletion.");

                using var pragmaCmd = verifyConn.CreateCommand();
                pragmaCmd.CommandText = "PRAGMA integrity_check;";
                var integrity = (string?)pragmaCmd.ExecuteScalar();
                Assert.Equal("ok", integrity);
            }
        }

        [Fact]
        public async Task ApplyDiffAsync_TraderAndPoolRequirements_WithMissingQuest_DefaultsToResilient_SucceedsAndSkips()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // Ensure tables exist
            using (var initConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadWrite"))
            {
                initConn.Open();
                using var tableCmd = initConn.CreateCommand();
                tableCmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS QuestTraderRequirements (
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

                    CREATE TABLE IF NOT EXISTS QuestPoolRequirements (
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
                ";
                tableCmd.ExecuteNonQuery();
            }

            var report = new DataDiffReport
            {
                QuestTraderRequirementDiffs = new List<QuestTraderRequirementDiff>
                {
                    new QuestTraderRequirementDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "ghost_quest_missing_trader_req",
                        TraderName = "Peacekeeper",
                        NewRequiredLevel = 2,
                        RequirementId = "tr_ghost_1"
                    }
                },
                QuestPoolRequirementDiffs = new List<QuestPoolRequirementDiff>
                {
                    new QuestPoolRequirementDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "ghost_quest_missing_pool_req",
                        PoolTrader = "Peacekeeper",
                        NewRequiredCompletedCount = 5,
                        RequirementId = "pool_ghost_1"
                    }
                }
            };

            // Act: Apply diff with resilientPrerequisites omitted (default true)
            var success = await applier.ApplyDiffAsync(report, _testDbPath);

            // Assert: Succeeds cleanly, skips missing quest requirements without Error 19
            Assert.True(success);
            Assert.Null(applier.LastError);

            SqliteConnection.ClearAllPools();
            using var verifyConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            verifyConn.Open();
            using var cmd = verifyConn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    (SELECT COUNT(*) FROM QuestTraderRequirements WHERE Id = 'tr_ghost_1'),
                    (SELECT COUNT(*) FROM QuestPoolRequirements WHERE Id = 'pool_ghost_1');
            ";
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(0, r.GetInt32(0));
            Assert.Equal(0, r.GetInt32(1));
        }

        #endregion
    }
}
