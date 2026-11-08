using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services.DataAudit;
using Xunit;

namespace TarkovHelper.Tests
{
    /// <summary>
    /// Empirical challenger test suite for DatabaseTransactionApplier edge cases:
    /// - Reverse foreign key cascading (RequiredQuestId, AlternativeQuestId).
    /// - Missing child tables in the SQLite schema.
    /// - Cyclic quest dependencies and optional quests.
    /// - Null IDs, empty IDs, and missing child rows.
    /// - Historical orphan sanitization.
    /// - Strict PRAGMA integrity_check and PRAGMA foreign_key_check verification.
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class TransactionApplierEdgeCasesChallengerTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _testDbPath;

        public TransactionApplierEdgeCasesChallengerTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_TxEdgeTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _testDbPath = Path.Combine(_testDir, "tarkov_data.db");
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_testDir))
            {
                try { Directory.Delete(_testDir, recursive: true); } catch { }
            }
        }

        private void CreateFullSchema(string path)
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
                    NameEN TEXT
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

                CREATE TABLE OptionalQuests (
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
                    ItemId TEXT,
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
            ";
            cmd.ExecuteNonQuery();
        }

        private static void AssertDatabaseIntegrity(string dbPath)
        {
            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            using var intCmd = conn.CreateCommand();
            intCmd.CommandText = "PRAGMA integrity_check;";
            var integrityResult = (string?)intCmd.ExecuteScalar();
            Assert.Equal("ok", integrityResult);

            using var fkCmd = conn.CreateCommand();
            fkCmd.CommandText = "PRAGMA foreign_key_check;";
            using var fkReader = fkCmd.ExecuteReader();
            Assert.False(fkReader.Read(), "PRAGMA foreign_key_check found foreign key violations!");
        }

        #region Challenge 1: Reverse Foreign Keys & Complex Cascade

        [Fact]
        public async Task RemovedQuest_WithReverseForeignKeysInRequirementsAndOptional_CascadesCleanly()
        {
            CreateFullSchema(_testDbPath);

            // Seed 3 quests: A, B, C
            // B has RequiredQuestId = A
            // C has AlternativeQuestId = A
            // A also has RequiredQuestId = B (cycle)
            // A has child rows in all tables
            using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadWrite"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Quests (Id, Name, Trader, MinLevel) VALUES ('q_A', 'Quest A', 'Prapor', 1);
                    INSERT INTO Quests (Id, Name, Trader, MinLevel) VALUES ('q_B', 'Quest B', 'Prapor', 2);
                    INSERT INTO Quests (Id, Name, Trader, MinLevel) VALUES ('q_C', 'Quest C', 'Therapist', 3);

                    -- B requires A (reverse FK from B to A)
                    INSERT INTO QuestRequirements (Id, QuestId, RequiredQuestId) VALUES ('req_B_A', 'q_B', 'q_A');
                    -- A requires B (direct FK from A to B)
                    INSERT INTO QuestRequirements (Id, QuestId, RequiredQuestId) VALUES ('req_A_B', 'q_A', 'q_B');

                    -- C has A as alternative (reverse FK from C to A)
                    INSERT INTO OptionalQuests (Id, QuestId, AlternativeQuestId) VALUES ('opt_C_A', 'q_C', 'q_A');
                    -- A has C as alternative (direct FK from A to C)
                    INSERT INTO OptionalQuests (Id, QuestId, AlternativeQuestId) VALUES ('opt_A_C', 'q_A', 'q_C');

                    -- Child records of A
                    INSERT INTO QuestObjectives (Id, QuestId, Description) VALUES ('obj_A_1', 'q_A', 'Objective 1');
                    INSERT INTO QuestTraderRequirements (Id, QuestId, TraderName, RequiredLevel) VALUES ('tr_A_1', 'q_A', 'Prapor', 2);
                    INSERT INTO QuestPoolRequirements (Id, QuestId, PoolTrader, RequiredCompletedCount) VALUES ('pool_A_1', 'q_A', 'Prapor', 3);
                    INSERT INTO QuestRequiredItems (Id, QuestId, ItemName, Count) VALUES ('item_A_1', 'q_A', 'Salewa', 1);
                ";
                cmd.ExecuteNonQuery();
            }

            AssertDatabaseIntegrity(_testDbPath);

            // Apply diff removing Quest A
            var diff = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Removed,
                        QuestId = "q_A",
                        QuestName = "Quest A"
                    }
                }
            };

            var applier = DatabaseTransactionApplier.Instance;
            var success = await applier.ApplyDiffAsync(diff, _testDbPath);

            Assert.True(success, $"ApplyDiffAsync failed: {applier.LastError}");
            Assert.Null(applier.LastError);

            // Verify database state:
            // Quest A is gone, B and C remain
            // All requirements referencing A (both direct and reverse) are gone
            // All optional quests referencing A (both direct and reverse) are gone
            using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        (SELECT COUNT(*) FROM Quests WHERE Id = 'q_A'),
                        (SELECT COUNT(*) FROM Quests WHERE Id IN ('q_B', 'q_C')),
                        (SELECT COUNT(*) FROM QuestRequirements WHERE QuestId = 'q_A' OR RequiredQuestId = 'q_A'),
                        (SELECT COUNT(*) FROM OptionalQuests WHERE QuestId = 'q_A' OR AlternativeQuestId = 'q_A'),
                        (SELECT COUNT(*) FROM QuestObjectives WHERE QuestId = 'q_A'),
                        (SELECT COUNT(*) FROM QuestTraderRequirements WHERE QuestId = 'q_A'),
                        (SELECT COUNT(*) FROM QuestPoolRequirements WHERE QuestId = 'q_A'),
                        (SELECT COUNT(*) FROM QuestRequiredItems WHERE QuestId = 'q_A');
                ";
                using var r = cmd.ExecuteReader();
                Assert.True(r.Read());
                Assert.Equal(0, r.GetInt32(0)); // Quest A deleted
                Assert.Equal(2, r.GetInt32(1)); // Quests B and C still exist
                Assert.Equal(0, r.GetInt32(2)); // All requirements referencing A removed
                Assert.Equal(0, r.GetInt32(3)); // All optional quests referencing A removed
                Assert.Equal(0, r.GetInt32(4)); // QuestObjectives of A removed
                Assert.Equal(0, r.GetInt32(5)); // QuestTraderRequirements of A removed
                Assert.Equal(0, r.GetInt32(6)); // QuestPoolRequirements of A removed
                Assert.Equal(0, r.GetInt32(7)); // QuestRequiredItems of A removed
            }

            AssertDatabaseIntegrity(_testDbPath);
        }

        #endregion

        #region Challenge 2: Missing Child Tables

        [Fact]
        public async Task DatabaseWithMissingOptionalChildTables_DoesNotThrowNoSuchTable_AppliesGracefully()
        {
            // Create a database that contains Quests, QuestRequirements, and QuestRequiredItems,
            // but NO QuestObjectives, QuestTraderRequirements, QuestPoolRequirements, OptionalQuests,
            // HideoutStations, HideoutLevels, HideoutItemRequirements!
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = _testDbPath,
                Mode = SqliteOpenMode.ReadWriteCreate
            };

            using (var conn = new SqliteConnection(csb.ConnectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    PRAGMA foreign_keys = ON;

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
                        FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE
                    );

                    INSERT INTO Quests (Id, Name, Trader, MinLevel) VALUES ('q_existing', 'Existing Quest', 'Prapor', 5);
                ";
                cmd.ExecuteNonQuery();
            }

            // Create diff modifying the existing quest, adding a new quest with objectives,
            // and adding trader requirements, pool requirements, hideout stations, etc.
            var diff = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_new_orphan",
                        QuestName = "New Quest With Objectives",
                        NewTrader = "Mechanic",
                        NewMinLevel = 10,
                        RemoteQuest = new NormalizedQuest
                        {
                            Id = "q_new_orphan",
                            Name = "New Quest With Objectives",
                            Objectives = new List<string> { "Objective text that has no table" }
                        }
                    },
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Modified,
                        QuestId = "q_existing",
                        QuestName = "Existing Quest Modified",
                        NewMinLevel = 12
                    }
                },
                QuestTraderRequirementDiffs = new List<QuestTraderRequirementDiff>
                {
                    new QuestTraderRequirementDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_existing",
                        TraderName = "Prapor",
                        NewRequiredLevel = 3
                    }
                },
                QuestPoolRequirementDiffs = new List<QuestPoolRequirementDiff>
                {
                    new QuestPoolRequirementDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_existing",
                        PoolTrader = "Prapor",
                        NewRequiredCompletedCount = 4
                    }
                },
                HideoutStationDiffs = new List<HideoutStationDiff>
                {
                    new HideoutStationDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        StationId = "st_ghost",
                        StationName = "Ghost Station",
                        NewMaxLevel = 3
                    }
                }
            };

            var applier = DatabaseTransactionApplier.Instance;
            var success = await applier.ApplyDiffAsync(diff, _testDbPath);

            Assert.True(success, $"ApplyDiffAsync should succeed gracefully when optional child tables don't exist: {applier.LastError}");
            Assert.Null(applier.LastError);

            // Now test removing a quest when optional child tables don't exist
            var removeDiff = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Removed,
                        QuestId = "q_existing",
                        QuestName = "Existing Quest Modified"
                    }
                }
            };

            var removeSuccess = await applier.ApplyDiffAsync(removeDiff, _testDbPath);
            Assert.True(removeSuccess, $"Removing quest with missing child tables failed: {applier.LastError}");

            AssertDatabaseIntegrity(_testDbPath);
        }

        [Fact]
        public async Task DatabaseWithMissingCoreChildTables_AppliesGracefullyWithoutCrashing()
        {
            // If QuestRequiredItems or QuestRequirements is completely missing from the schema,
            // the historical orphan cleanup query at transaction start is guarded by TableExistsAsync
            // and applies changes to Quests gracefully.
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = _testDbPath,
                Mode = SqliteOpenMode.ReadWriteCreate
            };

            using (var conn = new SqliteConnection(csb.ConnectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
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
                    INSERT INTO Quests (Id, Name) VALUES ('q_test', 'Test Quest');
                ";
                cmd.ExecuteNonQuery();
            }

            var diff = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff { ChangeType = DiffChangeType.Modified, QuestId = "q_test", QuestName = "Modified" }
                }
            };

            var applier = DatabaseTransactionApplier.Instance;
            var success = await applier.ApplyDiffAsync(diff, _testDbPath);

            // Confirms that missing QuestRequiredItems and QuestRequirements are guarded by TableExistsAsync
            // and do not crash startup orphan cleanup.
            Assert.True(success, $"ApplyDiffAsync should succeed when QuestRequiredItems/QuestRequirements tables are missing: {applier.LastError}");
            Assert.Null(applier.LastError);
        }

        #endregion

        #region Challenge 3: Cyclic Dependencies

        [Fact]
        public async Task CyclicQuestDependencies_AppliesAndDeletesWithoutConstraintViolations()
        {
            CreateFullSchema(_testDbPath);

            // Cycle: Q1 -> Q2 -> Q3 -> Q1
            var diff = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff { ChangeType = DiffChangeType.Added, QuestId = "q_cycle_1", QuestName = "Cycle 1", NewTrader = "Prapor" },
                    new QuestDiff { ChangeType = DiffChangeType.Added, QuestId = "q_cycle_2", QuestName = "Cycle 2", NewTrader = "Prapor" },
                    new QuestDiff { ChangeType = DiffChangeType.Added, QuestId = "q_cycle_3", QuestName = "Cycle 3", NewTrader = "Prapor" }
                },
                QuestRequirementDiffs = new List<QuestRequirementDiff>
                {
                    new QuestRequirementDiff { ChangeType = DiffChangeType.Added, QuestId = "q_cycle_1", RequiredQuestId = "q_cycle_2", RequirementId = "req_c1_c2" },
                    new QuestRequirementDiff { ChangeType = DiffChangeType.Added, QuestId = "q_cycle_2", RequiredQuestId = "q_cycle_3", RequirementId = "req_c2_c3" },
                    new QuestRequirementDiff { ChangeType = DiffChangeType.Added, QuestId = "q_cycle_3", RequiredQuestId = "q_cycle_1", RequirementId = "req_c3_c1" }
                }
            };

            var applier = DatabaseTransactionApplier.Instance;
            var success = await applier.ApplyDiffAsync(diff, _testDbPath);
            Assert.True(success, $"Cyclic diff application failed: {applier.LastError}");

            AssertDatabaseIntegrity(_testDbPath);

            // Now break the cycle by removing q_cycle_2
            var breakDiff = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff { ChangeType = DiffChangeType.Removed, QuestId = "q_cycle_2", QuestName = "Cycle 2" }
                }
            };

            var breakSuccess = await applier.ApplyDiffAsync(breakDiff, _testDbPath);
            Assert.True(breakSuccess, $"Cycle break deletion failed: {applier.LastError}");

            // Verify q_cycle_2 is gone, req_c1_c2 and req_c2_c3 are gone, req_c3_c1 remains
            using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        (SELECT COUNT(*) FROM Quests WHERE Id = 'q_cycle_2'),
                        (SELECT COUNT(*) FROM QuestRequirements WHERE RequiredQuestId = 'q_cycle_2' OR QuestId = 'q_cycle_2'),
                        (SELECT COUNT(*) FROM QuestRequirements WHERE QuestId = 'q_cycle_3' AND RequiredQuestId = 'q_cycle_1');
                ";
                using var r = cmd.ExecuteReader();
                Assert.True(r.Read());
                Assert.Equal(0, r.GetInt32(0)); // Quest 2 gone
                Assert.Equal(0, r.GetInt32(1)); // All links to/from Quest 2 gone
                Assert.Equal(1, r.GetInt32(2)); // Link 3 -> 1 remains intact
            }

            AssertDatabaseIntegrity(_testDbPath);
        }

        #endregion

        #region Challenge 4: Null, Empty, and Whitespace IDs

        [Fact]
        public async Task NullAndEmptyRequirementIds_AutoGenerateDeterministicOrUniqueIdsWithoutCollision()
        {
            CreateFullSchema(_testDbPath);

            // Add base quests
            using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadWrite"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Quests (Id, Name, Trader, MinLevel) VALUES ('q_base_1', 'Base 1', 'Prapor', 1);
                    INSERT INTO Quests (Id, Name, Trader, MinLevel) VALUES ('q_base_2', 'Base 2', 'Prapor', 1);
                ";
                cmd.ExecuteNonQuery();
            }

            var diff = new DataDiffReport
            {
                QuestRequirementDiffs = new List<QuestRequirementDiff>
                {
                    // Null RequirementId
                    new QuestRequirementDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = null!,
                        QuestId = "q_base_1",
                        RequiredQuestId = "q_base_2",
                        NewRequirementType = "Complete"
                    }
                },
                QuestTraderRequirementDiffs = new List<QuestTraderRequirementDiff>
                {
                    // Empty RequirementId
                    new QuestTraderRequirementDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = "   ",
                        QuestId = "q_base_1",
                        TraderName = "Prapor",
                        NewRequiredLevel = 2
                    }
                },
                QuestPoolRequirementDiffs = new List<QuestPoolRequirementDiff>
                {
                    // Whitespace RequirementId
                    new QuestPoolRequirementDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = "",
                        QuestId = "q_base_1",
                        PoolTrader = "Prapor",
                        NewRequiredCompletedCount = 3
                    }
                },
                QuestItemDiffs = new List<QuestItemDiff>
                {
                    // Null RequirementId
                    new QuestItemDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = null!,
                        QuestId = "q_base_1",
                        ItemName = "Salewa",
                        NewQuantity = 1
                    }
                }
            };

            var applier = DatabaseTransactionApplier.Instance;
            var success = await applier.ApplyDiffAsync(diff, _testDbPath);

            Assert.True(success, $"Null/Empty ID application failed: {applier.LastError}");

            using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        (SELECT Id FROM QuestRequirements WHERE QuestId = 'q_base_1'),
                        (SELECT Id FROM QuestTraderRequirements WHERE QuestId = 'q_base_1'),
                        (SELECT Id FROM QuestPoolRequirements WHERE QuestId = 'q_base_1'),
                        (SELECT Id FROM QuestRequiredItems WHERE QuestId = 'q_base_1');
                ";
                using var r = cmd.ExecuteReader();
                Assert.True(r.Read());
                Assert.Equal("q_base_1_q_base_2", r.GetString(0)); // Deterministic fallback
                Assert.False(string.IsNullOrWhiteSpace(r.GetString(1))); // GUID fallback
                Assert.False(string.IsNullOrWhiteSpace(r.GetString(2))); // GUID fallback
                Assert.False(string.IsNullOrWhiteSpace(r.GetString(3))); // GUID fallback
            }

            AssertDatabaseIntegrity(_testDbPath);
        }

        #endregion

        #region Challenge 5: Pre-Sanitization of Historical Orphan Records

        [Fact]
        public async Task HistoricalOrphansInLegacyDatabase_AreCleanedBeforeApplyingDiff()
        {
            CreateFullSchema(_testDbPath);

            // Deliberately disable foreign keys to insert dirty orphan rows (simulating corrupt legacy database)
            using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadWrite"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    PRAGMA foreign_keys = OFF;

                    INSERT INTO Quests (Id, Name, Trader, MinLevel) VALUES ('q_clean_valid', 'Clean Valid Quest', 'Prapor', 1);

                    -- Orphan requirement referencing non-existent quests
                    INSERT INTO QuestRequirements (Id, QuestId, RequiredQuestId) VALUES ('req_orphan_1', 'ghost_q1', 'ghost_q2');
                    -- Orphan trader requirement referencing non-existent quest
                    INSERT INTO QuestTraderRequirements (Id, QuestId, TraderName, RequiredLevel) VALUES ('tr_orphan_1', 'ghost_q1', 'Prapor', 2);
                    -- Orphan pool requirement referencing non-existent quest
                    INSERT INTO QuestPoolRequirements (Id, QuestId, PoolTrader, RequiredCompletedCount) VALUES ('pool_orphan_1', 'ghost_q1', 'Prapor', 3);
                    -- Orphan required item referencing non-existent quest
                    INSERT INTO QuestRequiredItems (Id, QuestId, ItemName, Count) VALUES ('item_orphan_1', 'ghost_q1', 'Salewa', 1);
                    -- Orphan optional quest referencing non-existent quest
                    INSERT INTO OptionalQuests (Id, QuestId, AlternativeQuestId) VALUES ('opt_orphan_1', 'ghost_q1', 'ghost_q2');
                ";
                cmd.ExecuteNonQuery();
            }

            // Verify dirty state exists before transaction
            using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        (SELECT COUNT(*) FROM QuestRequirements WHERE Id = 'req_orphan_1'),
                        (SELECT COUNT(*) FROM QuestTraderRequirements WHERE Id = 'tr_orphan_1'),
                        (SELECT COUNT(*) FROM QuestPoolRequirements WHERE Id = 'pool_orphan_1'),
                        (SELECT COUNT(*) FROM QuestRequiredItems WHERE Id = 'item_orphan_1'),
                        (SELECT COUNT(*) FROM OptionalQuests WHERE Id = 'opt_orphan_1');
                ";
                using var r = cmd.ExecuteReader();
                Assert.True(r.Read());
                Assert.Equal(1, r.GetInt32(0));
                Assert.Equal(1, r.GetInt32(1));
                Assert.Equal(1, r.GetInt32(2));
                Assert.Equal(1, r.GetInt32(3));
                Assert.Equal(1, r.GetInt32(4));
            }

            // Now apply any diff with PRAGMA foreign_keys = ON
            var diff = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q_new_after_cleanup",
                        QuestName = "New After Cleanup",
                        NewTrader = "Prapor",
                        NewMinLevel = 2
                    }
                }
            };

            var applier = DatabaseTransactionApplier.Instance;
            var success = await applier.ApplyDiffAsync(diff, _testDbPath);

            Assert.True(success, $"ApplyDiffAsync failed on database with orphans: {applier.LastError}");

            // Verify all orphan rows were cleaned and new quest exists
            using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        (SELECT COUNT(*) FROM QuestRequirements WHERE Id = 'req_orphan_1'),
                        (SELECT COUNT(*) FROM QuestTraderRequirements WHERE Id = 'tr_orphan_1'),
                        (SELECT COUNT(*) FROM QuestPoolRequirements WHERE Id = 'pool_orphan_1'),
                        (SELECT COUNT(*) FROM QuestRequiredItems WHERE Id = 'item_orphan_1'),
                        (SELECT COUNT(*) FROM OptionalQuests WHERE Id = 'opt_orphan_1'),
                        (SELECT COUNT(*) FROM Quests WHERE Id = 'q_new_after_cleanup');
                ";
                using var r = cmd.ExecuteReader();
                Assert.True(r.Read());
                Assert.Equal(0, r.GetInt32(0)); // Orphan cleaned
                Assert.Equal(0, r.GetInt32(1)); // Orphan cleaned
                Assert.Equal(0, r.GetInt32(2)); // Orphan cleaned
                Assert.Equal(0, r.GetInt32(3)); // Orphan cleaned
                Assert.Equal(0, r.GetInt32(4)); // Orphan cleaned
                Assert.Equal(1, r.GetInt32(5)); // New quest added
            }

            AssertDatabaseIntegrity(_testDbPath);
        }

        #endregion

        #region Challenge 6: Non-Existent Child Rows on Update or Delete

        [Fact]
        public async Task RemoveOrModify_NonExistentChildRows_SucceedsSilentlyWithoutAffectingOtherRows()
        {
            CreateFullSchema(_testDbPath);

            using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadWrite"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO Quests (Id, Name, Trader, MinLevel) VALUES ('q_silent_test', 'Silent Test', 'Prapor', 1);";
                cmd.ExecuteNonQuery();
            }

            var diff = new DataDiffReport
            {
                // Remove non-existent prerequisite
                QuestRequirementDiffs = new List<QuestRequirementDiff>
                {
                    new QuestRequirementDiff
                    {
                        ChangeType = DiffChangeType.Removed,
                        RequirementId = "req_non_existent",
                        QuestId = "q_silent_test",
                        RequiredQuestId = "q_silent_test"
                    }
                },
                // Remove non-existent trader requirement
                QuestTraderRequirementDiffs = new List<QuestTraderRequirementDiff>
                {
                    new QuestTraderRequirementDiff
                    {
                        ChangeType = DiffChangeType.Removed,
                        RequirementId = "tr_non_existent",
                        QuestId = "q_silent_test",
                        TraderName = "NonExistentTrader"
                    }
                },
                // Remove non-existent pool requirement
                QuestPoolRequirementDiffs = new List<QuestPoolRequirementDiff>
                {
                    new QuestPoolRequirementDiff
                    {
                        ChangeType = DiffChangeType.Removed,
                        RequirementId = "pool_non_existent",
                        QuestId = "q_silent_test",
                        PoolTrader = "NonExistentTrader"
                    }
                },
                // Remove non-existent item requirement
                QuestItemDiffs = new List<QuestItemDiff>
                {
                    new QuestItemDiff
                    {
                        ChangeType = DiffChangeType.Removed,
                        RequirementId = "item_non_existent",
                        QuestId = "q_silent_test",
                        ItemName = "NonExistentItem"
                    }
                }
            };

            var applier = DatabaseTransactionApplier.Instance;
            var success = await applier.ApplyDiffAsync(diff, _testDbPath);

            Assert.True(success, $"Removing non-existent child rows failed: {applier.LastError}");
            Assert.Null(applier.LastError);

            AssertDatabaseIntegrity(_testDbPath);
        }

        #endregion
    }
}
