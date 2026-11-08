using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services.DataAudit;
using Xunit;

namespace TarkovHelper.Tests
{
    public class DiffEngineTests
    {
        private static string GetDatabasePath()
        {
            return DatabaseBackupManager.ResolveDatabasePath(null);
        }

        #region Diff Engine Detection Tests

        [Fact]
        public async Task ComputeDiff_DetectsModifiedQuestMinLevel()
        {
            var dbPath = GetDatabasePath();
            Assert.True(File.Exists(dbPath), $"Database file not found at: {dbPath}");

            var engine = new DatabaseDiffEngine();
            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = "5936d90786f7742b1420ba5b",
                        Name = "Debut",
                        Trader = "Prapor",
                        MinLevel = 15 // Original in DB is 1
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            Assert.True(report.HasChanges);
            Assert.Equal(1, report.ModifiedCount);

            var questDiff = Assert.Single(report.QuestDiffs);
            Assert.Equal(DiffChangeType.Modified, questDiff.ChangeType);
            Assert.Equal("Debut", questDiff.QuestName);
            Assert.Equal(0, questDiff.OldMinLevel ?? 0);
            Assert.Equal(15, questDiff.NewMinLevel);

            var prop = Assert.Single(questDiff.PropertyChanges, p => p.PropertyName == "MinLevel");
            Assert.True(prop.OldValue is "0" or "1" or null);
            Assert.Equal("15", prop.NewValue);
        }

        [Fact]
        public async Task ComputeDiff_DetectsAddedQuest()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9PcGVyYXRpb25fVmFuZ3VhcmQ=",
                        Name = "Operation Vanguard Custom Quest",
                        Trader = "Peacekeeper",
                        MinLevel = 45,
                        Faction = "Usec"
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            Assert.Equal(1, report.AddedCount);
            Assert.Equal(1, report.TotalChanges);

            var questDiff = Assert.Single(report.QuestDiffs);
            Assert.Equal(DiffChangeType.Added, questDiff.ChangeType);
            Assert.Equal("Operation Vanguard Custom Quest", questDiff.QuestName);
            Assert.Equal("Peacekeeper", questDiff.NewTrader);
            Assert.Equal(45, questDiff.NewMinLevel);
            Assert.Equal("Usec", questDiff.NewFaction);
        }

        [Fact]
        public async Task ComputeDiff_MatchesQuestByBsgIdFallback()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // Debut BSG ID: 5936d90786f7742b1420ba5b, but with arbitrary/unmatched Id
            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = "arbitrary-non-matching-wiki-id",
                        BsgId = "5936d90786f7742b1420ba5b",
                        Name = "Debut Renamed Test",
                        Trader = "Prapor",
                        MinLevel = 22 // Changed from 1
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var questDiff = Assert.Single(report.QuestDiffs);
            Assert.Equal(DiffChangeType.Modified, questDiff.ChangeType);
            Assert.Equal("Debut", questDiff.QuestName);
            Assert.Equal(0, questDiff.OldMinLevel ?? 0);
            Assert.Equal(22, questDiff.NewMinLevel);
        }

        [Fact]
        public async Task ComputeDiff_MatchesQuestByNameFallback()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = "unknown-id",
                        BsgId = null,
                        Name = "Debut",
                        Trader = "Prapor",
                        MinLevel = 33 // Changed from 1
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var questDiff = Assert.Single(report.QuestDiffs);
            Assert.Equal(DiffChangeType.Modified, questDiff.ChangeType);
            Assert.Equal(0, questDiff.OldMinLevel ?? 0);
            Assert.Equal(33, questDiff.NewMinLevel);
        }

        [Fact]
        public async Task ComputeDiff_DetectsAlteredPrerequisites()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // Quest "Acquaintance" requires "Introduction" in tarkov_data.db
            // We pass Acquaintance with an added prerequisite
            var acquaintanceId = "5d24b81486f77439c92d6ba8";
            var bogusPrereqId = "bogus-new-prereq-quest-id";

            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = acquaintanceId,
                        Name = "Acquaintance",
                        Trader = "Jaeger",
                        MinLevel = 1
                    }
                },
                QuestRequirements = new List<NormalizedQuestRequirement>
                {
                    new NormalizedQuestRequirement
                    {
                        Id = $"{acquaintanceId}_{bogusPrereqId}",
                        QuestId = acquaintanceId,
                        QuestName = "Acquaintance",
                        RequiredQuestId = bogusPrereqId,
                        RequiredQuestName = "Bogus Prereq Quest",
                        RequirementType = "Complete"
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var questDiff = Assert.Single(report.QuestDiffs);
            Assert.Equal(DiffChangeType.Modified, questDiff.ChangeType);
            Assert.Equal("Acquaintance", questDiff.QuestName);
            Assert.Contains(bogusPrereqId, questDiff.AddedPrerequisiteQuestIds);

            // Detailed requirement diffs
            Assert.NotEmpty(report.QuestRequirementDiffs);
            Assert.Contains(report.QuestRequirementDiffs, d => d.ChangeType == DiffChangeType.Added && d.RequiredQuestId == bogusPrereqId);
        }

        [Fact]
        public async Task ComputeDiff_DetectsAlteredQuestItemRequirement()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // Debut requires 2 MP-133 shotguns in tarkov_data.db
            var debutId = "5936d90786f7742b1420ba5b";
            var remoteData = new NormalizedAuditData
            {
                QuestRequiredItems = new List<NormalizedQuestRequiredItem>
                {
                    new NormalizedQuestRequiredItem
                    {
                        Id = "debut_item_1",
                        QuestId = debutId,
                        QuestName = "Debut",
                        ItemName = "MP-133 12ga pump-action shotgun",
                        Count = 8, // Changed from 2
                        RequiresFIR = true, // Changed from false
                        RequirementType = "Handover"
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var itemDiff = Assert.Single(report.QuestItemDiffs);
            Assert.Equal(DiffChangeType.Modified, itemDiff.ChangeType);
            Assert.Equal(2, itemDiff.OldQuantity);
            Assert.Equal(8, itemDiff.NewQuantity);
            Assert.Equal(2, itemDiff.OldCount);
            Assert.Equal(8, itemDiff.NewCount);
            Assert.False(itemDiff.OldRequiresFIR);
            Assert.True(itemDiff.NewRequiresFIR);
        }

        [Fact]
        public async Task ComputeDiff_DetectsAlteredHideoutLevelConstructionTime()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // Vents station ID: 5d473c1e081959000e530190, Level 1 construction time in DB is 0
            var ventsId = "5d473c1e081959000e530190";
            var remoteData = new NormalizedAuditData
            {
                HideoutLevels = new List<NormalizedHideoutLevel>
                {
                    new NormalizedHideoutLevel
                    {
                        Id = $"{ventsId}_1",
                        StationId = ventsId,
                        StationName = "Vents",
                        Level = 1,
                        ConstructionTime = 7200 // Changed from 0
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var levelDiff = Assert.Single(report.HideoutLevelDiffs);
            Assert.Equal(DiffChangeType.Modified, levelDiff.ChangeType);
            Assert.Equal(1, levelDiff.Level);
            Assert.Equal(0, levelDiff.OldConstructionTime);
            Assert.Equal(7200, levelDiff.NewConstructionTime);
        }

        [Fact]
        public async Task ComputeDiff_DetectsAlteredHideoutItemRequirement()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // Vents Lv.1 requires Roubles x25,000 in tarkov_data.db (5449016a4bdc2d6f028b456f)
            var ventsId = "5d473c1e081959000e530190";
            var roublesBsgId = "5449016a4bdc2d6f028b456f";

            var remoteData = new NormalizedAuditData
            {
                HideoutItemRequirements = new List<NormalizedHideoutItemRequirement>
                {
                    new NormalizedHideoutItemRequirement
                    {
                        Id = "vents_1_roubles",
                        StationId = ventsId,
                        StationName = "Vents",
                        Level = 1,
                        ItemId = roublesBsgId,
                        ItemName = "Roubles",
                        Count = 30000, // Changed from 25000
                        FoundInRaid = false
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var itemDiff = Assert.Single(report.HideoutItemDiffs);
            Assert.Equal(DiffChangeType.Modified, itemDiff.ChangeType);
            Assert.Equal(25000, itemDiff.OldQuantity);
            Assert.Equal(30000, itemDiff.NewQuantity);
            Assert.False(itemDiff.OldFoundInRaid);
            Assert.False(itemDiff.NewFoundInRaid);
        }

        #endregion

        #region Backup Manager Tests

        [Fact]
        public async Task BackupManager_CreatesValidDatedBackupFile()
        {
            var dbPath = GetDatabasePath();
            var backupManager = DatabaseBackupManager.Instance;

            string? backupPath = null;
            try
            {
                backupPath = await backupManager.CreateBackupAsync(dbPath);

                Assert.NotNull(backupPath);
                Assert.True(File.Exists(backupPath), $"Backup file does not exist: {backupPath}");

                var fileName = Path.GetFileName(backupPath);
                var regex = new Regex(@"^tarkov_data\.db\.bak_\d{8}_\d{6}");
                Assert.Matches(regex, fileName);

                var fileInfo = new FileInfo(backupPath);
                Assert.True(fileInfo.Length > 1_000_000, $"Backup file is smaller than expected: {fileInfo.Length} bytes");
            }
            finally
            {
                if (backupPath != null && File.Exists(backupPath))
                {
                    try { File.Delete(backupPath); } catch { }
                }
            }
        }

        [Fact]
        public void BackupManager_GetAvailableBackups_ReturnsSortedList()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_BackupTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var file1 = Path.Combine(tempDir, "tarkov_data.db.bak_20261010_120000");
                var file2 = Path.Combine(tempDir, "tarkov_data.db.bak_20261015_120000");
                var file3 = Path.Combine(tempDir, "tarkov_data.db.bak_20261020_120000");

                File.WriteAllText(file1, "sample 1");
                File.SetLastWriteTimeUtc(file1, DateTime.UtcNow.AddDays(-10));

                File.WriteAllText(file2, "sample 2");
                File.SetLastWriteTimeUtc(file2, DateTime.UtcNow.AddDays(-5));

                File.WriteAllText(file3, "sample 3");
                File.SetLastWriteTimeUtc(file3, DateTime.UtcNow);

                var backupManager = DatabaseBackupManager.Instance;
                var backups = backupManager.GetAvailableBackups(tempDir);

                Assert.Equal(3, backups.Count);
                Assert.Equal(file3, backups[0]); // Newest first
                Assert.Equal(file2, backups[1]);
                Assert.Equal(file1, backups[2]);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public async Task BackupManager_RestoreBackup_SafelyRestoresFile()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_RestoreTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var targetDb = Path.Combine(tempDir, "test_target.db");
                var backupDb = Path.Combine(tempDir, "test_target.db.bak_20261021_120000");

                // Initialize a real SQLite database for testing
                var connStr = $"Data Source={backupDb};Mode=ReadWriteCreate";
                await using (var conn = new SqliteConnection(connStr))
                {
                    await conn.OpenAsync();
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "CREATE TABLE TestTable (Id INTEGER PRIMARY KEY, Val TEXT); INSERT INTO TestTable VALUES (1, 'OriginalBackupData');";
                    await cmd.ExecuteNonQueryAsync();
                }

                SqliteConnection.ClearAllPools();

                // Now simulate restoring backupDb into targetDb
                var backupManager = DatabaseBackupManager.Instance;
                var restored = await backupManager.RestoreBackupAsync(backupDb, targetDb);

                Assert.True(restored);
                Assert.True(File.Exists(targetDb));

                // Verify targetDb has the expected content
                var verifyConnStr = $"Data Source={targetDb};Mode=ReadOnly";
                await using (var verifyConn = new SqliteConnection(verifyConnStr))
                {
                    await verifyConn.OpenAsync();
                    await using var verifyCmd = verifyConn.CreateCommand();
                    verifyCmd.CommandText = "SELECT Val FROM TestTable WHERE Id = 1;";
                    var val = (string?)await verifyCmd.ExecuteScalarAsync();
                    Assert.Equal("OriginalBackupData", val);
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public async Task BackupManager_StrictlyProtectsUserDataDb()
        {
            var backupManager = DatabaseBackupManager.Instance;
            var protectedPath = @"C:\Users\test\AppData\Roaming\TarkovHelper\Config\user_data.db";

            // Attempting to back up user_data.db must throw InvalidOperationException
            var exBackup = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                backupManager.CreateBackupAsync(protectedPath));
            Assert.Contains("strictly forbidden", exBackup.Message, StringComparison.OrdinalIgnoreCase);

            // Attempting to restore into user_data.db must throw InvalidOperationException
            var exRestore = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                backupManager.RestoreBackupAsync("some_backup.bak", protectedPath));
            Assert.Contains("strictly forbidden", exRestore.Message, StringComparison.OrdinalIgnoreCase);

            // Attempting to use user_data.db as backup source must throw InvalidOperationException
            var exRestoreSource = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                backupManager.RestoreBackupAsync(protectedPath, "target.db"));
            Assert.Contains("strictly forbidden", exRestoreSource.Message, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region DataDiffReport Metrics Tests

        [Fact]
        public void DataDiffReport_CalculatesMetricsCorrectly()
        {
            var report = new DataDiffReport();

            report.QuestDiffs.Add(new QuestDiff { ChangeType = DiffChangeType.Added });
            report.QuestDiffs.Add(new QuestDiff { ChangeType = DiffChangeType.Modified });
            report.QuestDiffs.Add(new QuestDiff { ChangeType = DiffChangeType.Unchanged });

            report.QuestRequirementDiffs.Add(new QuestRequirementDiff { ChangeType = DiffChangeType.Added });
            report.QuestRequirementDiffs.Add(new QuestRequirementDiff { ChangeType = DiffChangeType.Removed });

            report.QuestItemDiffs.Add(new QuestItemDiff { ChangeType = DiffChangeType.Modified });
            report.HideoutStationDiffs.Add(new HideoutStationDiff { ChangeType = DiffChangeType.Added });
            report.HideoutLevelDiffs.Add(new HideoutLevelDiff { ChangeType = DiffChangeType.Modified });
            report.HideoutItemDiffs.Add(new HideoutItemDiff { ChangeType = DiffChangeType.Removed });

            Assert.Equal(3, report.AddedCount);
            Assert.Equal(3, report.ModifiedCount);
            Assert.Equal(2, report.RemovedCount);
            Assert.Equal(1, report.UnchangedCount);
            Assert.Equal(8, report.TotalChanges);
            Assert.True(report.HasChanges);

            var changes = report.AllChanges().ToList();
            Assert.Equal(8, changes.Count);

            var summary = report.GetSummaryText();
            Assert.Contains("8 total changes", summary);
            Assert.Contains("3 added", summary);
            Assert.Contains("3 modified", summary);
            Assert.Contains("2 removed", summary);
        }

        #endregion
    }
}
