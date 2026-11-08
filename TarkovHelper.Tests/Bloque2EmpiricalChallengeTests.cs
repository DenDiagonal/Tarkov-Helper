using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services.DataAudit;
using Xunit;

namespace TarkovHelper.Tests
{
    [Collection("DatabaseUpdateServiceCollection")]
    public class Bloque2EmpiricalChallengeTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _testDbPath;

        public Bloque2EmpiricalChallengeTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "TH_Bloque2_Empirical_" + Guid.NewGuid().ToString("N"));
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

        #region Helper Setup and Schema

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
                    OriginalLocationPoints TEXT,
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

                -- Populate Seed Data
                INSERT INTO Items (Id, BsgId, Name, NameEN) VALUES ('item_salewa', '544fb2574bdc2df7738b4567', 'Salewa kit', 'Salewa first aid kit');
                INSERT INTO Items (Id, BsgId, Name, NameEN) VALUES ('item_roubles', '5449016a4bdc2d6f028b456f', 'Roubles', 'Roubles');

                INSERT INTO Quests (Id, BsgId, Name, NameKO, NameJA, Trader, Location, MinLevel, KappaRequired, Faction)
                VALUES ('quest_debut', '5936ee7086f77439392e2122', 'Debut', '데뷔', '初陣', 'Prapor', 'Customs', 1, 1, 'Any');

                INSERT INTO Quests (Id, BsgId, Name, NameKO, NameJA, Trader, Location, MinLevel, KappaRequired, Faction)
                VALUES ('quest_wc', '5a27ba1c86f77461ea5a3c56', 'Weapons Circulation', '무기 순환', '武器の流通', 'Peacekeeper', 'Woods', 25, 1, 'Any');

                INSERT INTO QuestObjectives (Id, QuestId, SortOrder, ObjectiveType, Description, LocationPoints, OriginalLocationPoints)
                VALUES ('obj_wc_1', 'quest_wc', 0, 'Visit', 'Locate the cache on Woods', '[{""x"":123.45,""y"":67.89,""z"":101.11}]', '[{""x"":123.45,""y"":67.89,""z"":101.11}]');

                INSERT INTO QuestRequirements (Id, QuestId, RequiredQuestId, RequirementType)
                VALUES ('req_wc_debut', 'quest_wc', 'quest_debut', 'Complete');

                INSERT INTO QuestTraderRequirements (Id, QuestId, TraderName, RequiredLevel)
                VALUES ('tr_wc_pk', 'quest_wc', 'Peacekeeper', 2);

                INSERT INTO QuestPoolRequirements (Id, QuestId, PoolTrader, PoolTraderLL, RequiredCompletedCount)
                VALUES ('pool_wc_pk', 'quest_wc', 'Peacekeeper', 2, 5);

                INSERT INTO HideoutStations (Id, Name, NormalizedName, MaxLevel)
                VALUES ('station_gen', 'Generator', 'generator', 3);

                INSERT INTO HideoutLevels (Id, StationId, Level, ConstructionTime)
                VALUES ('level_gen_1', 'station_gen', 1, 1200);

                INSERT INTO HideoutItemRequirements (Id, StationId, Level, ItemId, ItemName, Count)
                VALUES ('hir_gen_1', 'station_gen', 1, 'item_roubles', 'Roubles', 100000);
            ";
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Reads all tables and dumps their contents into deterministic strings for bitwise hashing.
        /// </summary>
        private static Dictionary<string, string> DumpDatabaseTables(string dbPath)
        {
            var result = new Dictionary<string, string>();
            var tables = new[]
            {
                "Items", "Quests", "QuestObjectives", "QuestRequirements",
                "QuestRequiredItems", "QuestTraderRequirements", "QuestPoolRequirements",
                "HideoutStations", "HideoutLevels", "HideoutItemRequirements"
            };

            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
            conn.Open();

            foreach (var table in tables)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT * FROM {table} ORDER BY rowid ASC;";
                using var reader = cmd.ExecuteReader();

                var sb = new StringBuilder();
                int fieldCount = reader.FieldCount;
                while (reader.Read())
                {
                    for (int i = 0; i < fieldCount; i++)
                    {
                        if (reader.IsDBNull(i))
                            sb.Append("<NULL>|");
                        else
                            sb.Append(reader.GetValue(i)).Append('|');
                    }
                    sb.AppendLine();
                }

                using var sha = SHA256.Create();
                var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").ToLowerInvariant();
                result[table] = hash;
            }

            return result;
        }

        #endregion

        #region 1. Stress Test Atomic Transactions (Rollback on Failure)

        [Fact]
        public async Task AtomicTransaction_SimulatedForeignKeyFailure_TriggersFullRollbackAndLeavesDatabaseUnmodified()
        {
            var applier = DatabaseTransactionApplier.Instance;

            // 1. Capture snapshot of entire database state before transaction
            var initialSnapshots = DumpDatabaseTables(_testDbPath);

            // 2. Build a diff report that has multiple valid changes across several tables,
            // followed by an invalid foreign key reference that causes a mid-transaction failure.
            var maliciousReport = new DataDiffReport
            {
                // Valid Quests to add
                QuestDiffs = new List<QuestDiff>
                {
                    new()
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "quest_valid_added_1",
                        QuestName = "Valid Quest 1",
                        NewTrader = "Prapor",
                        NewMinLevel = 10
                    },
                    new()
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "quest_valid_added_2",
                        QuestName = "Valid Quest 2",
                        NewTrader = "Therapist",
                        NewMinLevel = 15
                    }
                },
                // Valid Trader Requirement
                QuestTraderRequirementDiffs = new List<QuestTraderRequirementDiff>
                {
                    new()
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = "tr_valid_new",
                        QuestId = "quest_wc",
                        TraderName = "Peacekeeper",
                        NewRequiredLevel = 3
                    }
                },
                // Valid Pool Requirement
                QuestPoolRequirementDiffs = new List<QuestPoolRequirementDiff>
                {
                    new()
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = "pool_valid_new",
                        QuestId = "quest_wc",
                        PoolTrader = "Peacekeeper",
                        PoolTraderLL = 3,
                        NewRequiredCompletedCount = 10
                    }
                },
                // Valid Hideout Level
                HideoutLevelDiffs = new List<HideoutLevelDiff>
                {
                    new()
                    {
                        ChangeType = DiffChangeType.Added,
                        StationId = "station_gen",
                        Level = 2,
                        NewConstructionTime = 2400
                    }
                },
                // CRITICAL INJECTION: Quest requirement pointing to completely non-existent parent quest (FK violation)
                QuestRequirementDiffs = new List<QuestRequirementDiff>
                {
                    new()
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = "req_fail_fk_invalid",
                        QuestId = "quest_wc",
                        RequiredQuestId = "non_existent_ghost_quest_99999",
                        NewRequirementType = "Complete"
                    }
                }
            };

            // 3. Execute with resilientPrerequisites = false to enforce strict transaction failure
            var result = await applier.ApplyDiffAsync(maliciousReport, _testDbPath, resilientPrerequisites: false);

            // 4. Assert transaction failed
            Assert.False(result);
            Assert.NotNull(applier.LastError);
            Assert.Contains("FOREIGN KEY", applier.LastError, StringComparison.OrdinalIgnoreCase);

            // 5. Assert SQLite PRAGMA integrity_check is ok
            SqliteConnection.ClearAllPools();
            using (var verifyConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly;"))
            {
                verifyConn.Open();
                using var pragmaCmd = verifyConn.CreateCommand();
                pragmaCmd.CommandText = "PRAGMA integrity_check;";
                var integrity = pragmaCmd.ExecuteScalar()?.ToString();
                Assert.Equal("ok", integrity);

                // Verify NONE of the valid changes leaked into the DB
                using var cmd1 = verifyConn.CreateCommand();
                cmd1.CommandText = "SELECT COUNT(*) FROM Quests WHERE Id LIKE 'quest_valid_added%';";
                Assert.Equal(0, Convert.ToInt32(cmd1.ExecuteScalar()));

                using var cmd2 = verifyConn.CreateCommand();
                cmd2.CommandText = "SELECT COUNT(*) FROM QuestTraderRequirements WHERE Id = 'tr_valid_new';";
                Assert.Equal(0, Convert.ToInt32(cmd2.ExecuteScalar()));

                using var cmd3 = verifyConn.CreateCommand();
                cmd3.CommandText = "SELECT COUNT(*) FROM QuestPoolRequirements WHERE Id = 'pool_valid_new';";
                Assert.Equal(0, Convert.ToInt32(cmd3.ExecuteScalar()));

                using var cmd4 = verifyConn.CreateCommand();
                cmd4.CommandText = "SELECT COUNT(*) FROM HideoutLevels WHERE StationId = 'station_gen' AND Level = 2;";
                Assert.Equal(0, Convert.ToInt32(cmd4.ExecuteScalar()));
            }

            // 6. Assert EVERY single table in the database is bitwise identical to initial snapshot
            var postFailureSnapshots = DumpDatabaseTables(_testDbPath);
            foreach (var kvp in initialSnapshots)
            {
                Assert.Equal(kvp.Value, postFailureSnapshots[kvp.Key]);
            }
        }

        #endregion

        #region 2. Stress Test Backup and Restoration (.bak)

        [Fact]
        public async Task BackupAndRestore_RestoresBitwiseIdenticalTableContents()
        {
            var backupManager = DatabaseBackupManager.Instance;

            // 1. Take initial hash of every table
            var preBackupTableHashes = DumpDatabaseTables(_testDbPath);

            // 2. Create dated backup (.bak)
            var backupPath = await backupManager.CreateBackupAsync(_testDbPath);
            Assert.True(File.Exists(backupPath));
            Assert.Contains(".bak_", backupPath);
            Assert.True(new FileInfo(backupPath).Length >= 100);

            // Verify backup file starts with SQLite magic header
            byte[] header = new byte[16];
            using (var stream = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.Read(header, 0, 16);
            }
            Assert.Equal("SQLite format 3\0", Encoding.ASCII.GetString(header));

            // 3. Mutate the database aggressively across multiple tables
            SqliteConnection.ClearAllPools();
            using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadWrite;"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    -- Insert 10 new quests
                    INSERT INTO Quests (Id, Name, Trader, MinLevel) VALUES ('mut_1', 'Mut 1', 'Skier', 12);
                    INSERT INTO Quests (Id, Name, Trader, MinLevel) VALUES ('mut_2', 'Mut 2', 'Peacekeeper', 14);
                    INSERT INTO Quests (Id, Name, Trader, MinLevel) VALUES ('mut_3', 'Mut 3', 'Prapor', 16);

                    -- Corrupt original location points in QuestObjectives
                    UPDATE QuestObjectives SET OriginalLocationPoints = 'CORRUPTED_POINTS' WHERE Id = 'obj_wc_1';

                    -- Delete an existing quest requirement
                    DELETE FROM QuestRequirements WHERE Id = 'req_wc_debut';

                    -- Mutate HideoutStations
                    INSERT INTO HideoutStations (Id, Name, NormalizedName, MaxLevel) VALUES ('mut_st_1', 'Solar Power', 'solar_power', 1);
                ";
                cmd.ExecuteNonQuery();
            }

            // Verify the database is now mutated and hashes differ
            var mutatedTableHashes = DumpDatabaseTables(_testDbPath);
            Assert.NotEqual(preBackupTableHashes["Quests"], mutatedTableHashes["Quests"]);
            Assert.NotEqual(preBackupTableHashes["QuestObjectives"], mutatedTableHashes["QuestObjectives"]);
            Assert.NotEqual(preBackupTableHashes["QuestRequirements"], mutatedTableHashes["QuestRequirements"]);
            Assert.NotEqual(preBackupTableHashes["HideoutStations"], mutatedTableHashes["HideoutStations"]);

            // 4. Restore database from backup
            var restored = await backupManager.RestoreBackupAsync(backupPath, _testDbPath);
            Assert.True(restored);

            // 5. Verify restored database tables are 100% bitwise identical to pre-backup state
            var restoredTableHashes = DumpDatabaseTables(_testDbPath);
            foreach (var kvp in preBackupTableHashes)
            {
                Assert.Equal(kvp.Value, restoredTableHashes[kvp.Key]);
            }

            // Specifically verify OriginalLocationPoints are exactly restored
            SqliteConnection.ClearAllPools();
            using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly;"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT OriginalLocationPoints FROM QuestObjectives WHERE Id = 'obj_wc_1';";
                var origPoints = cmd.ExecuteScalar()?.ToString();
                Assert.Equal("[{\"x\":123.45,\"y\":67.89,\"z\":101.11}]", origPoints);

                // Verify integrity
                using var pragma = conn.CreateCommand();
                pragma.CommandText = "PRAGMA integrity_check;";
                Assert.Equal("ok", pragma.ExecuteScalar()?.ToString());
            }

            // Cleanup backup file
            try { File.Delete(backupPath); } catch { }
        }

        [Fact]
        public async Task RestoreBackupAsync_CorruptedOrTruncatedBackup_RejectsAndLeavesTargetDatabaseUntouched()
        {
            var backupManager = DatabaseBackupManager.Instance;
            var initialHashes = DumpDatabaseTables(_testDbPath);

            // 1. Truncated backup (< 100 bytes)
            var truncatedBackup = Path.Combine(_testDir, "truncated.db.bak");
            await File.WriteAllBytesAsync(truncatedBackup, new byte[50]);

            var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                backupManager.RestoreBackupAsync(truncatedBackup, _testDbPath));
            Assert.Contains("minimum 100 bytes", ex1.Message, StringComparison.OrdinalIgnoreCase);

            // 2. Corrupted magic header
            var corruptHeaderBackup = Path.Combine(_testDir, "corrupt_header.db.bak");
            var junk = new byte[200];
            new Random(42).NextBytes(junk);
            await File.WriteAllBytesAsync(corruptHeaderBackup, junk);

            var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                backupManager.RestoreBackupAsync(corruptHeaderBackup, _testDbPath));
            Assert.Contains("magic header mismatch", ex2.Message, StringComparison.OrdinalIgnoreCase);

            // 3. Assert target database is 100% untouched
            var postCheckHashes = DumpDatabaseTables(_testDbPath);
            foreach (var kvp in initialHashes)
            {
                Assert.Equal(kvp.Value, postCheckHashes[kvp.Key]);
            }
        }

        #endregion

        #region 3. Verify Security Guards (ValidateNotUserDataDb)

        [Theory]
        [InlineData(@"%APPDATA%\TarkovHelper\Config\user_data.db")]
        [InlineData(@"%APPDATA%/TarkovHelper/Config/user_data.db")]
        [InlineData(@"%appdata%\tarkovhelper\config\user_data.db")]
        [InlineData(@"%USERPROFILE%\AppData\Roaming\TarkovHelper\Config\user_data.db")]
        [InlineData(@"C:\Users\Default\AppData\Roaming\TarkovHelper\Config\user_data.db")]
        [InlineData(@"C:/Users/Default/AppData/Roaming/TarkovHelper/Config/user_data.db")]
        [InlineData(@"user_data.db")]
        [InlineData(@"UserData.db")]
        [InlineData(@"USER_DATA.DB")]
        [InlineData(@"nested\path\user_data.db")]
        [InlineData(@"nested/path/user_data.db")]
        public async Task ValidateNotUserDataDb_AcrossAllServices_StrictlyRejectsTargetPaths(string testPath)
        {
            var backupManager = DatabaseBackupManager.Instance;
            var applier = DatabaseTransactionApplier.Instance;

            var diffWithChanges = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new()
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "malicious_quest_attempt",
                        QuestName = "Malicious Title"
                    }
                }
            };

            // 1. CreateBackupAsync must reject
            var exCreate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                backupManager.CreateBackupAsync(testPath));
            Assert.Contains("user_data.db", exCreate.Message, StringComparison.OrdinalIgnoreCase);

            // 2. RestoreBackupAsync (as source backupPath) must reject
            var exRestoreSrc = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                backupManager.RestoreBackupAsync(testPath, _testDbPath));
            Assert.Contains("user_data.db", exRestoreSrc.Message, StringComparison.OrdinalIgnoreCase);

            // 3. RestoreBackupAsync (as target targetDbPath) must reject
            // First make a valid dummy backup
            var validBackup = Path.Combine(_testDir, "valid_temp.db.bak");
            File.Copy(_testDbPath, validBackup, overwrite: true);
            try
            {
                var exRestoreTarget = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    backupManager.RestoreBackupAsync(validBackup, testPath));
                Assert.Contains("user_data.db", exRestoreTarget.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                try { File.Delete(validBackup); } catch { }
            }

            // 4. ApplyDiffAsync must reject
            var exApply = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                applier.ApplyDiffAsync(diffWithChanges, testPath));
            Assert.Contains("user_data.db", exApply.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ValidateNotUserDataDb_ExpandedAppDataPath_StrictlyRejectsAndProtectsRealUserDataDb()
        {
            var backupManager = DatabaseBackupManager.Instance;
            var applier = DatabaseTransactionApplier.Instance;

            // Generate the exact expanded AppData path
            var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var realConfigDir = Path.Combine(appDataFolder, "TarkovHelper", "Config");
            var realUserDataDb = Path.Combine(realConfigDir, "user_data.db");

            var diffWithChanges = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new()
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "malicious_quest_user_data",
                        QuestName = "Malicious Progress Overwrite"
                    }
                }
            };

            // Ensure guard triggers for expanded path
            var exCreate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                backupManager.CreateBackupAsync(realUserDataDb));
            Assert.Contains("user_data.db", exCreate.Message, StringComparison.OrdinalIgnoreCase);

            var exApply = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                applier.ApplyDiffAsync(diffWithChanges, realUserDataDb));
            Assert.Contains("user_data.db", exApply.Message, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
