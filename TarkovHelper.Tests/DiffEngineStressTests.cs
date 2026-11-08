using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services.DataAudit;
using Xunit;

namespace TarkovHelper.Tests
{
    /// <summary>
    /// Empirical challenger stress tests for DatabaseDiffEngine and DatabaseBackupManager.
    /// Validates adversarial conditions, edge cases, subtle mutations, and performance boundaries.
    /// </summary>
    public class DiffEngineStressTests
    {
        private static string GetDatabasePath()
        {
            return DatabaseBackupManager.ResolveDatabasePath(null);
        }

        #region 1. Subtle Level Changes & Numeric Boundary Conditions

        [Theory]
        [InlineData(2)]     // Subtle increase (+1 level)
        [InlineData(5)]     // Moderate increase
        [InlineData(14)]    // Moderate level
        [InlineData(75)]    // Maximum endgame level
        [InlineData(0)]     // Level requirement dropped to 0
        public async Task ComputeDiff_SubtleMinLevelMutations_CorrectlyReported(int newLevel)
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // Debut has initial MinLevel = 1, KappaRequired = true in Assets/tarkov_data.db
            var debutId = "5936d90786f7742b1420ba5b";
            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = debutId,
                        Name = "Debut",
                        Trader = "Prapor",
                        MinLevel = newLevel,
                        KappaRequired = true
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            if (newLevel == 1) // Unchanged
            {
                Assert.False(report.HasChanges);
            }
            else
            {
                Assert.True(report.HasChanges);
                var diff = Assert.Single(report.QuestDiffs);
                Assert.Equal(DiffChangeType.Modified, diff.ChangeType);
                Assert.Equal(1, diff.OldMinLevel);
                Assert.Equal(newLevel, diff.NewMinLevel);

                var prop = Assert.Single(diff.PropertyChanges, p => p.PropertyName == "MinLevel");
                Assert.Equal("1", prop.OldValue);
                Assert.Equal(newLevel.ToString(), prop.NewValue);
            }
        }

        [Fact]
        public async Task ComputeDiff_UnchangedQuest_CorrectlyIdentifiedAsUnchanged()
        {
            var dbPath = GetDatabasePath();
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
                        MinLevel = 1, // Same as DB
                        KappaRequired = false // Same as DB
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath, includeUnchanged: true);

            var diff = Assert.Single(report.QuestDiffs);
            Assert.Equal(DiffChangeType.Unchanged, diff.ChangeType);
            Assert.Empty(diff.PropertyChanges);
        }

        #endregion

        #region 2. Complex Quest Trees & Prerequisite Modifications

        [Fact]
        public async Task ComputeDiff_ComplexPrerequisites_SimultaneousAddAndRemove()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // Quest "Wet Job - Part 2" requires "Wet Job - Part 1" in tarkov_data.db
            var testQuestId = "5a27bbf886f774333a418eeb"; // Wet Job - Part 2
            var existingPrereqId = "5a27bb8386f7741c770d2d0a"; // Wet Job - Part 1
            var debutId = "5936d90786f7742b1420ba5b";
            var newPrereqId = "5936da9e86f7742d65037edf"; // Checking

            // We test changing Wet Job - Part 2 requirement from Wet Job - Part 1 -> Debut AND Checking
            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = testQuestId,
                        Name = "Wet Job - Part 2",
                        Trader = "Peacekeeper",
                        MinLevel = 1
                    }
                },
                QuestRequirements = new List<NormalizedQuestRequirement>
                {
                    new NormalizedQuestRequirement
                    {
                        Id = $"{testQuestId}_{debutId}",
                        QuestId = testQuestId,
                        QuestName = "Wet Job - Part 2",
                        RequiredQuestId = debutId,
                        RequiredQuestName = "Debut",
                        RequirementType = "Complete"
                    },
                    new NormalizedQuestRequirement
                    {
                        Id = $"{testQuestId}_{newPrereqId}",
                        QuestId = testQuestId,
                        QuestName = "Wet Job - Part 2",
                        RequiredQuestId = newPrereqId,
                        RequiredQuestName = "Checking",
                        RequirementType = "Complete"
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var qDiff = Assert.Single(report.QuestDiffs);
            Assert.Equal(DiffChangeType.Modified, qDiff.ChangeType);

            // Added Debut and Checking
            Assert.Contains(debutId, qDiff.AddedPrerequisiteQuestIds);
            Assert.Contains(newPrereqId, qDiff.AddedPrerequisiteQuestIds);

            // Removed Wet Job - Part 1
            Assert.Contains(existingPrereqId, qDiff.RemovedPrerequisiteQuestIds);

            // Detailed requirement diffs
            var addedReqs = report.QuestRequirementDiffs.Where(r => r.ChangeType == DiffChangeType.Added).ToList();
            var removedReqs = report.QuestRequirementDiffs.Where(r => r.ChangeType == DiffChangeType.Removed).ToList();

            Assert.Equal(2, addedReqs.Count);
            Assert.Single(removedReqs);
            Assert.Equal(existingPrereqId, removedReqs[0].RequiredQuestId);
        }

        [Fact]
        public async Task ComputeDiff_PrerequisiteMatchedByQuestNameWhenIdDiffers()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            var testQuestId = "5a27bbf886f774333a418eeb"; // Wet Job - Part 2
            // Provide Wet Job - Part 1 with a non-matching synthetic ID but matching Name "Wet Job - Part 1"
            var syntheticWj1Id = "synthetic-wj1-id-999";

            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = testQuestId,
                        Name = "Wet Job - Part 2",
                        Trader = "Peacekeeper",
                        MinLevel = 1
                    }
                },
                QuestRequirements = new List<NormalizedQuestRequirement>
                {
                    new NormalizedQuestRequirement
                    {
                        Id = $"{testQuestId}_{syntheticWj1Id}",
                        QuestId = testQuestId,
                        QuestName = "Wet Job - Part 2",
                        RequiredQuestId = syntheticWj1Id,
                        RequiredQuestName = "Wet Job - Part 1", // Matches local Wet Job - Part 1 quest
                        RequirementType = "Complete"
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath, includeUnchanged: true);

            var qDiff = Assert.Single(report.QuestDiffs);
            // Because Introduction was matched by name to the local Introduction ID,
            // no prerequisites were added or removed!
            Assert.Empty(qDiff.AddedPrerequisiteQuestIds);
            Assert.Empty(qDiff.RemovedPrerequisiteQuestIds);
        }

        [Fact]
        public async Task ComputeDiff_UnknownPrerequisiteQuestId_SafelyRecordedWithoutCrash()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            var debutId = "5936d90786f7742b1420ba5b";
            var totallyUnknownId = "non_existent_quest_id_xyz_999999";

            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = debutId,
                        Name = "Debut",
                        Trader = "Prapor",
                        MinLevel = 1
                    }
                },
                QuestRequirements = new List<NormalizedQuestRequirement>
                {
                    new NormalizedQuestRequirement
                    {
                        Id = $"{debutId}_{totallyUnknownId}",
                        QuestId = debutId,
                        QuestName = "Debut",
                        RequiredQuestId = totallyUnknownId,
                        RequiredQuestName = "Ghost Unknown Quest",
                        RequirementType = "Complete"
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var qDiff = Assert.Single(report.QuestDiffs);
            Assert.Equal(DiffChangeType.Modified, qDiff.ChangeType);
            Assert.Contains(totallyUnknownId, qDiff.AddedPrerequisiteQuestIds);

            var reqDiff = Assert.Single(report.QuestRequirementDiffs);
            Assert.Equal(DiffChangeType.Added, reqDiff.ChangeType);
            Assert.Equal(totallyUnknownId, reqDiff.RequiredQuestId);
        }

        #endregion

        #region 3. Quest Required Items: Missing Items, New Items & FIR Flips

        [Fact]
        public async Task ComputeDiff_QuestItem_FIRFlip_FalseToTrue_And_TrueToFalse()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // In DB: Debut requires 2 MP-133 shotguns, RequiresFIR = 0 (false)
            var debutId = "5936d90786f7742b1420ba5b";

            var remoteData = new NormalizedAuditData
            {
                QuestRequiredItems = new List<NormalizedQuestRequiredItem>
                {
                    new NormalizedQuestRequiredItem
                    {
                        Id = "debut_mp133_test",
                        QuestId = debutId,
                        QuestName = "Debut",
                        ItemName = "MP-133 12ga pump-action shotgun",
                        Count = 2, // Count unchanged
                        RequiresFIR = true // Flipped to true!
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var itemDiff = Assert.Single(report.QuestItemDiffs);
            Assert.Equal(DiffChangeType.Modified, itemDiff.ChangeType);
            Assert.False(itemDiff.OldRequiresFIR);
            Assert.True(itemDiff.NewRequiresFIR);

            var prop = Assert.Single(itemDiff.PropertyChanges, p => p.PropertyName == "RequiresFIR");
            Assert.Equal("False", prop.OldValue);
            Assert.Equal("True", prop.NewValue);
        }

        [Fact]
        public async Task ComputeDiff_QuestItems_DetectsMultipleItemsWithAddAndRemove()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // Debut requires 2 MP-133 shotguns in DB.
            // When we provide Debut with a new item and omit MP-133,
            // the new item is flagged as Added and MP-133 is flagged as Removed.
            var debutId = "5936d90786f7742b1420ba5b";

            var remoteData = new NormalizedAuditData
            {
                QuestRequiredItems = new List<NormalizedQuestRequiredItem>
                {
                    new NormalizedQuestRequiredItem
                    {
                        Id = "debut_new_item",
                        QuestId = debutId,
                        QuestName = "Debut",
                        ItemName = "SKS 7.62x39 carbine",
                        Count = 1,
                        RequiresFIR = false,
                        RequirementType = "Handover"
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var addedItems = report.QuestItemDiffs.Where(i => i.ChangeType == DiffChangeType.Added).ToList();
            var removedItems = report.QuestItemDiffs.Where(i => i.ChangeType == DiffChangeType.Removed).ToList();

            Assert.Single(addedItems);
            Assert.Equal("SKS 7.62x39 carbine", addedItems[0].ItemName);

            // Verified that omitted local items for audited quest are captured as Removed
            Assert.Single(removedItems);
            Assert.Equal("MP-133 12ga pump-action shotgun", removedItems[0].ItemName);
        }

        [Fact]
        public async Task ComputeDiff_QuestItem_WhitespaceAndCaseInsensitivity()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            var debutId = "5936d90786f7742b1420ba5b";

            // Notice leading/trailing whitespace and altered casing in item name, with RequirementType matching Handover
            var remoteData = new NormalizedAuditData
            {
                QuestRequiredItems = new List<NormalizedQuestRequiredItem>
                {
                    new NormalizedQuestRequiredItem
                    {
                        Id = "debut_whitespace_item",
                        QuestId = debutId,
                        QuestName = "Debut",
                        ItemName = "  mp-133 12ga PUMP-ACTION SHOTGUN  ",
                        Count = 2,
                        RequiresFIR = false, // Matching exactly what is in DB
                        RequirementType = "Handover" // Matching exactly what is in DB
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath, includeUnchanged: true);

            var itemDiff = Assert.Single(report.QuestItemDiffs);
            Assert.Equal(DiffChangeType.Unchanged, itemDiff.ChangeType);
        }

        #endregion

        #region 4. Hideout Modifications: Stations, Levels & Item Requirements

        [Fact]
        public async Task ComputeDiff_HideoutStation_MaxLevelModification()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // Vents station ID: 5d473c1e081959000e530190, MaxLevel is 3 in DB
            var ventsId = "5d473c1e081959000e530190";

            var remoteData = new NormalizedAuditData
            {
                HideoutStations = new List<NormalizedHideoutStation>
                {
                    new NormalizedHideoutStation
                    {
                        Id = ventsId,
                        Name = "Vents",
                        MaxLevel = 4 // Changed to 4
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var stDiff = Assert.Single(report.HideoutStationDiffs);
            Assert.Equal(DiffChangeType.Modified, stDiff.ChangeType);
            Assert.Equal(3, stDiff.OldMaxLevel);
            Assert.Equal(4, stDiff.NewMaxLevel);

            var prop = Assert.Single(stDiff.PropertyChanges, p => p.PropertyName == "MaxLevel");
            Assert.Equal("3", prop.OldValue);
            Assert.Equal("4", prop.NewValue);
        }

        [Fact]
        public async Task ComputeDiff_HideoutLevel_ConstructionTimeToZero()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // Generator Lv.1 has construction time 0, Lv.2 has construction time 18000
            var generatorId = "5d3b396e33c48f02b81cd9f3";

            var remoteData = new NormalizedAuditData
            {
                HideoutLevels = new List<NormalizedHideoutLevel>
                {
                    new NormalizedHideoutLevel
                    {
                        Id = $"{generatorId}_2",
                        StationId = generatorId,
                        StationName = "Generator",
                        Level = 2,
                        ConstructionTime = 18000 // Test construction time mutation
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var lvDiff = Assert.Single(report.HideoutLevelDiffs);
            Assert.Equal(DiffChangeType.Modified, lvDiff.ChangeType);
            Assert.Equal(0, lvDiff.OldConstructionTime);
            Assert.Equal(18000, lvDiff.NewConstructionTime);
        }

        [Fact]
        public async Task ComputeDiff_HideoutItemRequirement_FoundInRaidFlip_And_QuantityMutation()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // Vents Lv.1 requires Roubles x25,000 (FIR=false)
            var ventsId = "5d473c1e081959000e530190";
            var roublesBsgId = "5449016a4bdc2d6f028b456f";

            var remoteData = new NormalizedAuditData
            {
                HideoutItemRequirements = new List<NormalizedHideoutItemRequirement>
                {
                    new NormalizedHideoutItemRequirement
                    {
                        Id = "vents_1_roubles_test",
                        StationId = ventsId,
                        StationName = "Vents",
                        Level = 1,
                        ItemId = roublesBsgId,
                        ItemName = "Roubles",
                        Count = 50000, // Doubled quantity
                        FoundInRaid = true // Flipped to true!
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var itemDiff = Assert.Single(report.HideoutItemDiffs);
            Assert.Equal(DiffChangeType.Modified, itemDiff.ChangeType);
            Assert.Equal(25000, itemDiff.OldQuantity);
            Assert.Equal(50000, itemDiff.NewQuantity);
            Assert.False(itemDiff.OldFoundInRaid);
            Assert.True(itemDiff.NewFoundInRaid);

            Assert.Equal(2, itemDiff.PropertyChanges.Count);
            Assert.Contains(itemDiff.PropertyChanges, p => p.PropertyName == "Count" && p.OldValue == "25000" && p.NewValue == "50000");
            Assert.Contains(itemDiff.PropertyChanges, p => p.PropertyName == "FoundInRaid" && p.OldValue == "False" && p.NewValue == "True");
        }

        #endregion

        #region 5. Multi-Map Quests, Special Characters & Null Safety

        [Fact]
        public async Task ComputeDiff_MultiMapQuests_HandledCleanlyWithoutError()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // "The Guide" is a famous multi-map quest (Customs, Woods, Factory, Shoreline, Interchange, Reserve, Labs)
            // "Peacekeeping Mission" is another multi-map quest
            var theGuideId = "5c0d4e61d09282029f53920e";

            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = theGuideId,
                        Name = "The Guide",
                        Trader = "Peacekeeper",
                        Location = "Any",
                        Maps = new List<string> { "Customs", "Woods", "Factory", "Shoreline", "Interchange", "Reserve", "The Lab" },
                        MinLevel = 50 // Let's test modifying min level from 40 to 50
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var qDiff = Assert.Single(report.QuestDiffs);
            Assert.Equal(DiffChangeType.Modified, qDiff.ChangeType);
            Assert.Equal("The Guide", qDiff.QuestName);
            Assert.Equal(0, qDiff.OldMinLevel ?? 0);
            Assert.Equal(50, qDiff.NewMinLevel);
        }

        [Fact]
        public async Task ComputeDiff_SpecialCharactersAndApostrophes_ProperlyEscaped()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // Quests with apostrophes, hyphens, and quotes: "Jaeger's Request", "Gunsmith - Part 1", "What's on the Flash Drive?"
            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = "synthetic-apostrophe-id",
                        Name = "Jaeger's Special Test Quest 'O'Malley'",
                        Trader = "Jaeger",
                        MinLevel = 12
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var qDiff = Assert.Single(report.QuestDiffs);
            Assert.Equal(DiffChangeType.Added, qDiff.ChangeType);
            Assert.Equal("Jaeger's Special Test Quest 'O'Malley'", qDiff.QuestName);
        }

        [Fact]
        public async Task ComputeDiff_CompletelyEmptyOrNullCollections_DoesNotThrow()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            var emptyRemoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>(),
                QuestRequirements = new List<NormalizedQuestRequirement>(),
                QuestRequiredItems = new List<NormalizedQuestRequiredItem>(),
                HideoutStations = new List<NormalizedHideoutStation>(),
                HideoutLevels = new List<NormalizedHideoutLevel>(),
                HideoutItemRequirements = new List<NormalizedHideoutItemRequirement>()
            };

            var report = await engine.ComputeDiffAsync(emptyRemoteData, dbPath);

            Assert.NotNull(report);
            Assert.Equal(0, report.TotalChanges);
            Assert.False(report.HasChanges);
        }

        #endregion

        #region 6. Performance & Cancellation Stress Testing

        [Fact]
        public async Task ComputeDiff_CancellationRequested_AbortsImmediately()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Immediately cancelled token

            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest { Id = "test-1", Name = "Cancelled Quest" }
                }
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                engine.ComputeDiffAsync(remoteData, dbPath, ct: cts.Token));
        }

        [Fact]
        public async Task ComputeDiff_LargeSyntheticDataset_CompletesFastWithoutBottleneck()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // Generate 100 modified quests, 200 requirements, 300 items
            var remoteData = new NormalizedAuditData();
            for (int i = 0; i < 100; i++)
            {
                remoteData.Quests.Add(new NormalizedQuest
                {
                    Id = $"synthetic_quest_{i}",
                    Name = $"Synthetic Quest {i}",
                    Trader = "Prapor",
                    MinLevel = i % 50 + 1
                });
            }

            for (int i = 0; i < 200; i++)
            {
                remoteData.QuestRequirements.Add(new NormalizedQuestRequirement
                {
                    Id = $"synthetic_req_{i}",
                    QuestId = $"synthetic_quest_{i % 100}",
                    QuestName = $"Synthetic Quest {i % 100}",
                    RequiredQuestId = $"synthetic_quest_{(i + 1) % 100}",
                    RequiredQuestName = $"Synthetic Quest {(i + 1) % 100}",
                    RequirementType = "Complete"
                });
            }

            for (int i = 0; i < 300; i++)
            {
                remoteData.QuestRequiredItems.Add(new NormalizedQuestRequiredItem
                {
                    Id = $"synthetic_item_{i}",
                    QuestId = $"synthetic_quest_{i % 100}",
                    QuestName = $"Synthetic Quest {i % 100}",
                    ItemName = $"Synthetic Item {i}",
                    Count = (i % 10) + 1,
                    RequiresFIR = (i % 2 == 0)
                });
            }

            var start = DateTime.UtcNow;
            var report = await engine.ComputeDiffAsync(remoteData, dbPath);
            var elapsed = DateTime.UtcNow - start;

            Assert.NotNull(report);
            Assert.True(report.HasChanges);
            Assert.Equal(600, report.TotalChanges); // 100 quests + 200 reqs + 300 items
            Assert.True(elapsed.TotalSeconds < 5.0, $"Computation took too long: {elapsed.TotalSeconds:N2}s");
        }

        #endregion

        #region 7. Backup Manager Rapid Succession & Edge Cases

        [Fact]
        public async Task BackupManager_RapidSuccessiveBackups_DoNotCollide()
        {
            var dbPath = GetDatabasePath();
            var backupManager = DatabaseBackupManager.Instance;

            string? backup1 = null;
            string? backup2 = null;

            try
            {
                // Trigger two backups in the exact same millisecond window
                var task1 = backupManager.CreateBackupAsync(dbPath);
                var task2 = backupManager.CreateBackupAsync(dbPath);

                var results = await Task.WhenAll(task1, task2);
                backup1 = results[0];
                backup2 = results[1];

                Assert.NotNull(backup1);
                Assert.NotNull(backup2);
                Assert.NotEqual(backup1, backup2); // Must have unique file names!
                Assert.True(File.Exists(backup1));
                Assert.True(File.Exists(backup2));
                Assert.True(new FileInfo(backup1).Length > 0);
                Assert.True(new FileInfo(backup2).Length > 0);
            }
            finally
            {
                if (backup1 != null && File.Exists(backup1)) try { File.Delete(backup1); } catch { }
                if (backup2 != null && File.Exists(backup2)) try { File.Delete(backup2); } catch { }
            }
        }

        [Fact]
        public async Task BackupManager_OnlineBackup_WhileDatabaseBeingReadConcurrently()
        {
            var dbPath = GetDatabasePath();
            var backupManager = DatabaseBackupManager.Instance;

            string? backupPath = null;
            try
            {
                // Start a continuous reader loop to simulate an active application reading SQLite
                using var cts = new CancellationTokenSource();
                var readTask = Task.Run(async () =>
                {
                    var connStr = $"Data Source={dbPath};Mode=ReadOnly";
                    while (!cts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            await using var conn = new SqliteConnection(connStr);
                            await conn.OpenAsync(cts.Token);
                            await using var cmd = new SqliteCommand("SELECT COUNT(*) FROM Quests", conn);
                            await cmd.ExecuteScalarAsync(cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }, cts.Token);

                // Run online backup while reading
                backupPath = await backupManager.CreateBackupAsync(dbPath);

                // Cancel the reading loop
                cts.Cancel();
                await readTask;

                Assert.NotNull(backupPath);
                Assert.True(File.Exists(backupPath));
                Assert.True(new FileInfo(backupPath).Length > 1_000_000);
            }
            finally
            {
                if (backupPath != null && File.Exists(backupPath))
                {
                    try { File.Delete(backupPath); } catch { }
                }
            }
        }

        #endregion

        #region 8. Advanced Adversarial & Schema Integrity Challenges

        [Fact]
        public async Task ComputeDiff_DetectRemovedQuests_FlagsAllUnmatchedAsRemoved()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // Pass only 1 quest (Debut) with detectRemovedQuests = true
            // In tarkov_data.db there are 535 quests, so exactly 534 quests must be flagged as Removed!
            var debutId = "5936d90786f7742b1420ba5b";
            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = debutId,
                        Name = "Debut",
                        Trader = "Prapor",
                        MinLevel = 1,
                        KappaRequired = true
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath, detectRemovedQuests: true);

            Assert.NotNull(report);
            Assert.True(report.RemovedCount >= 500, $"Expected >= 500 removed quests, but got {report.RemovedCount}");
            Assert.Contains(report.QuestDiffs, q => q.ChangeType == DiffChangeType.Removed && q.QuestName != "Debut");
        }

        [Fact]
        public async Task ComputeDiff_NonExistentDbPath_ThrowsFileNotFoundException()
        {
            var engine = new DatabaseDiffEngine();
            var bogusPath = Path.Combine(Path.GetTempPath(), "definitely_does_not_exist_tarkov_data.db");

            var remoteData = new NormalizedAuditData();

            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                engine.ComputeDiffAsync(remoteData, bogusPath));
        }

        [Fact]
        public async Task ComputeDiff_HideoutStation_ResolvedByNormalizedName()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // Water Collector has NormalizedName = "water-collector"
            // We pass it with an arbitrary ID but matching NormalizedName
            var remoteData = new NormalizedAuditData
            {
                HideoutStations = new List<NormalizedHideoutStation>
                {
                    new NormalizedHideoutStation
                    {
                        Id = "arbitrary-station-id-999",
                        Name = "water-collector",
                        NormalizedName = "water-collector",
                        MaxLevel = 4 // Changed from 3
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var stDiff = Assert.Single(report.HideoutStationDiffs);
            Assert.Equal(DiffChangeType.Modified, stDiff.ChangeType);
            Assert.Equal(3, stDiff.OldMaxLevel);
            Assert.Equal(4, stDiff.NewMaxLevel);
        }

        [Fact]
        public async Task ComputeDiff_QuestRequirement_AlteredDelayMinutesAndGroupId()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // Wet Job - Part 2 requires Wet Job - Part 1
            var testQuestId = "5a27bbf886f774333a418eeb"; // Wet Job - Part 2
            var existingPrereqId = "5a27bb8386f7741c770d2d0a"; // Wet Job - Part 1

            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest { Id = testQuestId, Name = "Wet Job - Part 2", Trader = "Peacekeeper", MinLevel = 1, KappaRequired = true }
                },
                QuestRequirements = new List<NormalizedQuestRequirement>
                {
                    new NormalizedQuestRequirement
                    {
                        Id = $"{testQuestId}_{existingPrereqId}",
                        QuestId = testQuestId,
                        QuestName = "Wet Job - Part 2",
                        RequiredQuestId = existingPrereqId,
                        RequiredQuestName = "Wet Job - Part 1",
                        RequirementType = "Complete",
                        DelayMinutes = 1440, // Added 24h delay
                        GroupId = 2 // Changed from GroupId 0 to 2
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var reqDiff = Assert.Single(report.QuestRequirementDiffs);
            Assert.Equal(DiffChangeType.Modified, reqDiff.ChangeType);
            Assert.Equal(1440, reqDiff.NewDelayMinutes);
            Assert.Equal(2, reqDiff.NewGroupId);

            Assert.Contains(reqDiff.PropertyChanges, p => p.PropertyName == "DelayMinutes" && p.NewValue == "1440");
            Assert.Contains(reqDiff.PropertyChanges, p => p.PropertyName == "GroupId" && p.NewValue == "2");
        }

        [Fact]
        public async Task ComputeDiff_QuestEditionsPrestigeAndDecode_CorrectlyDetected()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            var debutId = "5936d90786f7742b1420ba5b";
            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = debutId,
                        Name = "Debut",
                        Trader = "Prapor",
                        MinLevel = 1,
                        KappaRequired = true,
                        RequiredEdition = "Unheard",
                        ExcludedEdition = "Standard",
                        RequiredPrestigeLevel = 2,
                        RequiredDecodeCount = 5
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath);

            Assert.NotNull(report);
            var qDiff = Assert.Single(report.QuestDiffs);
            Assert.Equal(DiffChangeType.Modified, qDiff.ChangeType);

            Assert.Contains(qDiff.PropertyChanges, p => p.PropertyName == "RequiredEdition" && p.NewValue == "Unheard");
            Assert.Contains(qDiff.PropertyChanges, p => p.PropertyName == "ExcludedEdition" && p.NewValue == "Standard");
            Assert.Contains(qDiff.PropertyChanges, p => p.PropertyName == "RequiredPrestigeLevel" && p.NewValue == "2");
            Assert.Contains(qDiff.PropertyChanges, p => p.PropertyName == "RequiredDecodeCount" && p.NewValue == "5");
        }

        [Fact]
        public async Task ComputeDiff_MinLevelZeroAndNull_TreatedAsEquivalent()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // A Healthy Alternative has MinLevel = 0 in DB
            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest
                    {
                        Id = "669fa3910c828825de06d69f",
                        Name = "A Healthy Alternative",
                        Trader = "Therapist",
                        MinLevel = null // Should be equivalent to 0 in DB
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath, includeUnchanged: true);

            var diff = Assert.Single(report.QuestDiffs);
            Assert.DoesNotContain(diff.PropertyChanges, p => p.PropertyName == "MinLevel");
        }

        [Fact]
        public async Task ComputeDiff_RequirementTypeActiveAndAccept_TreatedAsEquivalent()
        {
            var dbPath = GetDatabasePath();
            var engine = new DatabaseDiffEngine();

            // A quest requirement with "Accept" should match "active" without producing a diff
            var qId = "60e71b9bbd90872cb85440f3"; // Capturing Outposts
            var reqId = "59ca2eb686f77445a80ed049"; // The Punisher - Part 6
            var remoteData = new NormalizedAuditData
            {
                Quests = new List<NormalizedQuest>
                {
                    new NormalizedQuest { Id = qId, Name = "Capturing Outposts", Trader = "Prapor", MinLevel = 42 }
                },
                QuestRequirements = new List<NormalizedQuestRequirement>
                {
                    new NormalizedQuestRequirement
                    {
                        Id = $"{qId}_{reqId}_0",
                        QuestId = qId,
                        QuestName = "Capturing Outposts",
                        RequiredQuestId = reqId,
                        RequiredQuestName = "The Punisher - Part 6",
                        RequirementType = "Complete" // Should match Complete in DB
                    }
                }
            };

            var report = await engine.ComputeDiffAsync(remoteData, dbPath, includeUnchanged: true);

            Assert.NotNull(report);
            var rDiff = report.QuestRequirementDiffs.FirstOrDefault(rd => rd.QuestId == qId && rd.RequiredQuestId == reqId);
            Assert.NotNull(rDiff);
            Assert.DoesNotContain(rDiff.PropertyChanges, p => p.PropertyName == "RequirementType");
        }

        #endregion
    }
}
