using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services.DataAudit;
using TarkovHelper.Windows.Dialogs;
using Xunit;

namespace TarkovHelper.Tests
{
    [Collection("DatabaseUpdateServiceCollection")]
    public class DiffViewerTests
    {
        #region Helper Methods

        private static string CreateTestSqliteDatabase(string filePath, string tableName = "TestTable")
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = filePath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

            using var conn = new SqliteConnection(connStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE TABLE {tableName} (Id TEXT PRIMARY KEY, Name TEXT); INSERT INTO {tableName} VALUES ('1', 'Alpha');";
            cmd.ExecuteNonQuery();

            return filePath;
        }

        #endregion

        #region ViewModel & Report Count Tests

        [Fact]
        public void DiffViewerViewModel_WithEmptyReport_ExposesZeroChangesAndNotHasChanges()
        {
            var report = new DataDiffReport();
            var vm = new DiffViewerViewModel(report);

            Assert.Equal(0, vm.TotalChanges);
            Assert.Equal(0, vm.AddedCount);
            Assert.Equal(0, vm.ModifiedCount);
            Assert.Equal(0, vm.RemovedCount);
            Assert.Equal(0, vm.UnchangedCount);
            Assert.False(vm.HasChanges);
            Assert.Empty(vm.AllCards);
            Assert.Empty(vm.FilteredCards);
            Assert.Equal(0, vm.AllCount);
            Assert.Equal(0, vm.QuestsCount);
            Assert.Equal(0, vm.RequirementsCount);
            Assert.Equal(0, vm.ItemsCount);
            Assert.Equal(0, vm.HideoutCount);
        }

        [Fact]
        public void DiffViewerViewModel_WithMultiCategoryDiffReport_CountsCategoriesAndChangesAccurately()
        {
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        QuestId = "q1",
                        QuestName = "New Quest",
                        NewTrader = "Prapor",
                        NewMinLevel = 10
                    },
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Modified,
                        QuestId = "q2",
                        QuestName = "Debut",
                        OldMinLevel = 1,
                        NewMinLevel = 15,
                        PropertyChanges = new List<PropertyDiff>
                        {
                            new PropertyDiff("MinLevel", "1", "15")
                        }
                    },
                    new QuestDiff
                    {
                        ChangeType = DiffChangeType.Removed,
                        QuestId = "q3",
                        QuestName = "Deprecated Quest"
                    }
                },
                QuestRequirementDiffs = new List<QuestRequirementDiff>
                {
                    new QuestRequirementDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = "req1",
                        QuestName = "Debut",
                        RequiredQuestName = "Introduction",
                        NewRequirementType = "Complete"
                    }
                },
                QuestItemDiffs = new List<QuestItemDiff>
                {
                    new QuestItemDiff
                    {
                        ChangeType = DiffChangeType.Modified,
                        RequirementId = "item1",
                        QuestName = "Sanitary Standards - Part 1",
                        ItemName = "Gas analyzer",
                        OldQuantity = 1,
                        NewQuantity = 2,
                        OldRequiresFIR = false,
                        NewRequiresFIR = true,
                        PropertyChanges = new List<PropertyDiff>
                        {
                            new PropertyDiff("Count", "1", "2"),
                            new PropertyDiff("RequiresFIR", "False", "True")
                        }
                    }
                },
                HideoutLevelDiffs = new List<HideoutLevelDiff>
                {
                    new HideoutLevelDiff
                    {
                        ChangeType = DiffChangeType.Modified,
                        LevelId = "hl1",
                        StationName = "Vents",
                        Level = 2,
                        OldConstructionTime = 1800,
                        NewConstructionTime = 3600,
                        PropertyChanges = new List<PropertyDiff>
                        {
                            new PropertyDiff("ConstructionTime", "1800", "3600")
                        }
                    }
                },
                HideoutItemDiffs = new List<HideoutItemDiff>
                {
                    new HideoutItemDiff
                    {
                        ChangeType = DiffChangeType.Added,
                        RequirementId = "hitem1",
                        StationName = "Vents",
                        Level = 2,
                        ItemName = "Car battery",
                        NewQuantity = 1,
                        NewFoundInRaid = false
                    }
                }
            };

            var vm = new DiffViewerViewModel(report);

            // Total = 1 (added quest) + 1 (mod quest) + 1 (rem quest) + 1 (added req) + 1 (mod item) + 1 (mod hideout level) + 1 (added hideout item) = 7
            Assert.Equal(7, vm.TotalChanges);
            Assert.Equal(3, vm.AddedCount);
            Assert.Equal(3, vm.ModifiedCount);
            Assert.Equal(1, vm.RemovedCount);
            Assert.True(vm.HasChanges);

            Assert.Equal(7, vm.AllCount);
            Assert.Equal(3, vm.QuestsCount);
            Assert.Equal(1, vm.RequirementsCount);
            Assert.Equal(1, vm.ItemsCount);
            Assert.Equal(2, vm.HideoutCount);
        }

        #endregion

        #region Filtering Tabs Tests

        [Fact]
        public void DiffViewerViewModel_FilteringTabs_FiltersCardsAccurately()
        {
            var report = new DataDiffReport
            {
                QuestDiffs = new List<QuestDiff>
                {
                    new QuestDiff { ChangeType = DiffChangeType.Added, QuestId = "q1", QuestName = "Quest 1" },
                    new QuestDiff { ChangeType = DiffChangeType.Modified, QuestId = "q2", QuestName = "Quest 2" }
                },
                QuestRequirementDiffs = new List<QuestRequirementDiff>
                {
                    new QuestRequirementDiff { ChangeType = DiffChangeType.Added, RequirementId = "r1", QuestName = "Q1", RequiredQuestName = "Q0" }
                },
                QuestItemDiffs = new List<QuestItemDiff>
                {
                    new QuestItemDiff { ChangeType = DiffChangeType.Modified, RequirementId = "i1", ItemName = "Salewa" }
                },
                HideoutStationDiffs = new List<HideoutStationDiff>
                {
                    new HideoutStationDiff { ChangeType = DiffChangeType.Modified, StationId = "s1", StationName = "Generator" }
                },
                HideoutItemDiffs = new List<HideoutItemDiff>
                {
                    new HideoutItemDiff { ChangeType = DiffChangeType.Added, RequirementId = "hi1", ItemName = "Spark plug" }
                }
            };

            var vm = new DiffViewerViewModel(report);

            // Default: All tab
            Assert.Equal(DiffFilterTab.All, vm.ActiveTab);
            Assert.Equal(6, vm.FilteredCards.Count);

            // Quests tab
            vm.SetFilter(DiffFilterTab.Quests);
            Assert.Equal(DiffFilterTab.Quests, vm.ActiveTab);
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.All(vm.FilteredCards, c => Assert.Equal(DiffCategory.Quest, c.Category));

            // Requirements tab
            vm.SetFilter(DiffFilterTab.Requirements);
            var reqCard = Assert.Single(vm.FilteredCards);
            Assert.Equal(DiffCategory.QuestRequirement, reqCard.Category);

            // Items tab
            vm.SetFilter(DiffFilterTab.Items);
            var itemCard = Assert.Single(vm.FilteredCards);
            Assert.Equal(DiffCategory.QuestItem, itemCard.Category);

            // Hideout tab (stations, levels, items)
            vm.SetFilter(DiffFilterTab.Hideout);
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.Contains(vm.FilteredCards, c => c.Category == DiffCategory.HideoutStation);
            Assert.Contains(vm.FilteredCards, c => c.Category == DiffCategory.HideoutItem);

            // Switch back to All
            vm.SetFilter(DiffFilterTab.All);
            Assert.Equal(6, vm.FilteredCards.Count);
        }

        #endregion

        #region Card & Property Diff Formatting Tests

        [Fact]
        public void DiffItemCardViewModel_FormatsAddedQuest_WithCorrectBadgesAndColors()
        {
            var questDiff = new QuestDiff
            {
                ChangeType = DiffChangeType.Added,
                QuestId = "q_new",
                QuestName = "Customs Reconnaissance",
                NewTrader = "Prapor",
                NewMinLevel = 18
            };

            var card = new DiffItemCardViewModel(questDiff);

            Assert.Equal("MISIÓN", card.CategoryLabel);
            Assert.Equal("+ AÑADIDO", card.ChangeBadgeText);
            Assert.Equal("#4CAF50", card.AccentColorHex);
            Assert.Equal("#1B5E20", card.BadgeBackgroundHex);
            Assert.Equal("Customs Reconnaissance", card.Title);
            Assert.Contains("Prapor", card.Description);
        }

        [Fact]
        public void DiffItemCardViewModel_FormatsModifiedQuest_ExposesOldVsNewPropertyChanges()
        {
            var questDiff = new QuestDiff
            {
                ChangeType = DiffChangeType.Modified,
                QuestId = "q_debut",
                QuestName = "Debut",
                OldMinLevel = 1,
                NewMinLevel = 15,
                OldTrader = "Prapor",
                NewTrader = "Skier",
                PropertyChanges = new List<PropertyDiff>
                {
                    new PropertyDiff("MinLevel", "1", "15"),
                    new PropertyDiff("Trader", "Prapor", "Skier")
                }
            };

            var card = new DiffItemCardViewModel(questDiff);

            Assert.Equal("MISIÓN", card.CategoryLabel);
            Assert.Equal("~ MODIFICADO", card.ChangeBadgeText);
            Assert.Equal("#FFB300", card.AccentColorHex);
            Assert.True(card.HasPropertyChanges);
            Assert.Equal(2, card.PropertyChanges.Count);

            var minLevelProp = card.PropertyChanges.First(p => p.PropertyName == "MinLevel");
            Assert.Equal("Nivel Mínimo", minLevelProp.DisplayPropertyName);
            Assert.Equal("1", minLevelProp.DisplayOldValue);
            Assert.Equal("15", minLevelProp.DisplayNewValue);
            Assert.Contains("1 ➔ 15", minLevelProp.FormattedSummary);

            var traderProp = card.PropertyChanges.First(p => p.PropertyName == "Trader");
            Assert.Equal("Comerciante", traderProp.DisplayPropertyName);
            Assert.Equal("Prapor", traderProp.DisplayOldValue);
            Assert.Equal("Skier", traderProp.DisplayNewValue);
            Assert.Contains("Prapor ➔ Skier", traderProp.FormattedSummary);
        }

        [Fact]
        public void DiffItemCardViewModel_FormatsPrerequisiteChanges_ShowsAddedAndRemovedPrerequisites()
        {
            var questDiff = new QuestDiff
            {
                ChangeType = DiffChangeType.Modified,
                QuestId = "q_complex",
                QuestName = "The Bunker - Part 2",
                AddedPrerequisiteNames = new List<string> { "The Bunker - Part 1" },
                RemovedPrerequisiteNames = new List<string> { "Postman Pat - Part 1" }
            };

            var card = new DiffItemCardViewModel(questDiff);

            Assert.True(card.HasPrerequisites);
            Assert.Single(card.AddedPrerequisites);
            Assert.Equal("The Bunker - Part 1", card.AddedPrerequisites[0]);
            Assert.Single(card.RemovedPrerequisites);
            Assert.Equal("Postman Pat - Part 1", card.RemovedPrerequisites[0]);
        }

        [Fact]
        public void DiffItemCardViewModel_FormatsRemovedEntity_WithRedAccent()
        {
            var removedItem = new QuestItemDiff
            {
                ChangeType = DiffChangeType.Removed,
                RequirementId = "req_removed",
                QuestName = "Deprecated",
                ItemName = "Old Token"
            };

            var card = new DiffItemCardViewModel(removedItem);

            Assert.Equal("OBJETO REQUERIDO", card.CategoryLabel);
            Assert.Equal("- ELIMINADO", card.ChangeBadgeText);
            Assert.Equal("#EF5350", card.AccentColorHex);
            Assert.Equal("#B71C1C", card.BadgeBackgroundHex);
        }

        [Fact]
        public void PropertyDiffViewModel_HandlesNullOrEmptyValuesGracefully()
        {
            var diff = new PropertyDiff("MinLevel", null, "18");
            var vm = new PropertyDiffViewModel(diff);

            Assert.Equal("Nivel Mínimo", vm.DisplayPropertyName);
            Assert.Equal("(vacío)", vm.DisplayOldValue);
            Assert.Equal("18", vm.DisplayNewValue);
            Assert.Equal("Nivel Mínimo: (vacío) ➔ 18", vm.FormattedSummary);
        }

        #endregion

        #region Backup Item & Backup Manager Integration Tests

        [Fact]
        public void BackupItemViewModel_ParsesDatedFileName_ExtractsTimestampAndSize()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "DiffViewerTests_BackupParse_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var backupPath = Path.Combine(tempDir, "tarkov_data.db.bak_20261021_123045");
                File.WriteAllText(backupPath, "SQLite backup test content simulation");

                var item = new BackupItemViewModel(backupPath);

                Assert.Equal("tarkov_data.db.bak_20261021_123045", item.FileName);
                Assert.Equal(new DateTime(2026, 10, 21, 12, 30, 45), item.Timestamp);
                Assert.Equal("21/10/2026 12:30:45", item.FormattedTimestamp);
                Assert.Equal(37, item.FileSizeBytes);
                Assert.Equal("37 B", item.FormattedSize);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Theory]
        [InlineData(512, "512 B")]
        [InlineData(2048, "2.0 KB")]
        [InlineData(1048576 * 3, "3.00 MB")]
        public void BackupItemViewModel_FormatFileSize_FormatsStandardSizes(long bytes, string expected)
        {
            var result = BackupItemViewModel.FormatFileSize(bytes);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void DiffViewerViewModel_LoadBackups_LoadsAndSortsAvailableBackups()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "DiffViewerTests_LoadBackups_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var f1 = Path.Combine(tempDir, "tarkov_data.db.bak_20261021_100000");
                var f2 = Path.Combine(tempDir, "tarkov_data.db.bak_20261021_120000");
                var f3 = Path.Combine(tempDir, "tarkov_data.db.bak_20261021_110000");

                File.WriteAllText(f1, "backup1");
                File.SetLastWriteTimeUtc(f1, new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc));

                File.WriteAllText(f2, "backup2");
                File.SetLastWriteTimeUtc(f2, new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc));

                File.WriteAllText(f3, "backup3");
                File.SetLastWriteTimeUtc(f3, new DateTime(2026, 10, 21, 11, 0, 0, DateTimeKind.Utc));

                var vm = new DiffViewerViewModel(new DataDiffReport());
                vm.LoadBackups(tempDir);

                Assert.Equal(3, vm.AvailableBackups.Count);
                // DatabaseBackupManager returns sorted descending by LastWriteTimeUtc
                Assert.Equal("tarkov_data.db.bak_20261021_120000", vm.AvailableBackups[0].FileName);
                Assert.Equal("tarkov_data.db.bak_20261021_110000", vm.AvailableBackups[1].FileName);
                Assert.Equal("tarkov_data.db.bak_20261021_100000", vm.AvailableBackups[2].FileName);

                Assert.NotNull(vm.SelectedBackup);
                Assert.Equal("tarkov_data.db.bak_20261021_120000", vm.SelectedBackup.FileName);
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
        public async Task DiffViewerViewModel_RestoreBackupAsync_RestoresTargetDatabaseSuccessfully()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "DiffViewerTests_Restore_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // Create backup SQLite database with known row
                var backupFile = Path.Combine(tempDir, "tarkov_data.db.bak_20261021_150000");
                CreateTestSqliteDatabase(backupFile, "BackupTable");

                // Create target SQLite database with different table
                var targetFile = Path.Combine(tempDir, "tarkov_data.db");
                CreateTestSqliteDatabase(targetFile, "TargetTable");

                var backupVm = new BackupItemViewModel(backupFile);
                var diffVm = new DiffViewerViewModel(new DataDiffReport());

                var restored = await diffVm.RestoreBackupAsync(backupVm, targetFile);

                Assert.True(restored);
                Assert.True(File.Exists(targetFile));

                // Verify target now has BackupTable from backup
                SqliteConnection.ClearAllPools();
                var connStr = $"Data Source={targetFile};Mode=ReadOnly";
                await using var conn = new SqliteConnection(connStr);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM BackupTable;";
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

        #region STA Thread Dialog Instantiation Tests

        [Fact]
        public void DiffViewerDialog_CanBeInstantiated_OnStaThreadWithoutExceptions()
        {
            Exception? threadEx = null;
            var staThread = new Thread(() =>
            {
                try
                {
                    var report = new DataDiffReport
                    {
                        QuestDiffs = new List<QuestDiff>
                        {
                            new QuestDiff
                            {
                                ChangeType = DiffChangeType.Added,
                                QuestId = "q_sta",
                                QuestName = "STA Test Quest"
                            }
                        }
                    };

                    var dialog = new DiffViewerDialog(report);
                    Assert.NotNull(dialog);
                    Assert.NotNull(dialog.ViewModel);
                    Assert.Equal(1, dialog.ViewModel.TotalChanges);
                    Assert.False(dialog.WasApplied);
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

        [Fact]
        public void DiffViewerDialog_CreateRollbackDialog_InitializesInRollbackMode()
        {
            Exception? threadEx = null;
            var staThread = new Thread(() =>
            {
                try
                {
                    var dialog = DiffViewerDialog.CreateRollbackDialog();
                    Assert.NotNull(dialog);
                    Assert.NotNull(dialog.ViewModel);
                    Assert.False(dialog.ViewModel.HasChanges);
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

        #endregion
    }
}
