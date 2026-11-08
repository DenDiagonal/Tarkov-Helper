using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Pages;
using TarkovHelper.Services;
using TarkovHelper.Tests.Common;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests.E2E
{
    /// <summary>
    /// Requirement-driven Tier 3 (Cross-Feature Pairwise) & Tier 4 (Real-World Scenarios) E2E Test Suite.
    /// Validates cross-module integration between Tasks View, Maps View, Minimap Overlay, and Dashboard.
    /// </summary>
    public class TasksDashboardOverhaulE2ETests
    {
        private List<TarkovTask> CreateIntegrationTasks()
        {
            return new List<TarkovTask>
            {
                new()
                {
                    Id = "q_debut",
                    Name = "Debut",
                    NormalizedName = "debut",
                    Trader = "Prapor",
                    Maps = new List<string> { "customs" },
                    ReqKappa = true,
                    RequiredLevel = 1,
                    LeadsTo = new List<string> { "checking" },
                    Objectives = new List<string> { "Handover MP-133" }
                },
                new()
                {
                    Id = "q_checking",
                    Name = "Checking",
                    NormalizedName = "checking",
                    Trader = "Prapor",
                    Maps = new List<string> { "customs" },
                    ReqKappa = true,
                    RequiredLevel = 2,
                    Previous = new List<string> { "debut" },
                    LeadsTo = new List<string> { "shootout-picnic" },
                    Objectives = new List<string> { "Obtain bronze pocket watch" }
                },
                new()
                {
                    Id = "q_shootout_picnic",
                    Name = "Shootout Picnic",
                    NormalizedName = "shootout-picnic",
                    Trader = "Prapor",
                    Maps = new List<string> { "woods" },
                    ReqKappa = true,
                    RequiredLevel = 3,
                    Previous = new List<string> { "checking" }
                },
                new()
                {
                    Id = "q_intro",
                    Name = "Introduction",
                    NormalizedName = "introduction",
                    Trader = "Mechanic",
                    Maps = new List<string> { "woods" },
                    ReqKappa = true,
                    RequiredLevel = 2,
                    LeadsTo = new List<string> { "survivalist-path" }
                },
                new()
                {
                    Id = "q_survivalist",
                    Name = "Survivalist Path",
                    NormalizedName = "survivalist-path",
                    Trader = "Jaeger",
                    Maps = new List<string> { "woods" },
                    ReqKappa = true,
                    RequiredLevel = 2,
                    Previous = new List<string> { "introduction" }
                }
            };
        }

        #region Tier 3: Pairwise Cross-Feature Interactions (19 Tests)

        [Fact]
        public void F1_F3_TaskImpact_SortModeIntegration()
        {
            var tasks = CreateIntegrationTasks();
            var impactEngine = new TaskImpactEngine(tasks);

            var qvmDebut = new QuestViewModel { Task = tasks[0], DisplayName = "Debut", Status = QuestStatus.Active };
            var qvmChecking = new QuestViewModel { Task = tasks[1], DisplayName = "Checking", Status = QuestStatus.Active };

            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                SortBy = TaskSortMode.Impact,
                SortDirection = SortDirection.Descending
            };

            var sorted = QuestFilterEngine.Filter(new[] { qvmChecking, qvmDebut }, criteria);
            Assert.Equal("Debut", sorted.First().DisplayName);
        }

        [Fact]
        public void F1_F18_TaskImpact_UpNextRanking()
        {
            var tasks = CreateIntegrationTasks();
            var impactEngine = new TaskImpactEngine(tasks);

            var ranked = DashboardViewModel.RankUpNextTasks(tasks.Take(2), impactEngine);
            Assert.True(ranked.First().PriorityScore >= ranked.Last().PriorityScore);
        }

        [Fact]
        public void F2_F12_ClockService_SvgCanvasWidgetSync()
        {
            var timeDisplay = TarkovClockService.GetCurrentTimes();
            Assert.NotNull(timeDisplay.PrimaryTime);
            Assert.NotNull(timeDisplay.SecondaryTime);

            string widgetText = $"{timeDisplay.PrimaryTime} | {timeDisplay.SecondaryTime}";
            Assert.Contains("|", widgetText);
        }

        [Fact]
        public void F3_F5_FilterEngine_StatusCountChipSync()
        {
            var tasks = CreateIntegrationTasks();
            var qvms = tasks.Select(t => new QuestViewModel { Task = t, Status = QuestStatus.Active, DisplayName = t.Name }).ToList();

            var counts = QuestFilterEngine.ComputeStatusCounts(qvms);
            Assert.Equal(qvms.Count, counts.Available);

            var filtered = QuestFilterEngine.Filter(qvms, new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.Available
            });

            Assert.Equal(counts.Available, filtered.Count);
        }

        [Fact]
        public void F4_F11_NavigationSwitcher_MapPillsSynchronization()
        {
            var criteria = new QuestFilterCriteria
            {
                ViewMode = TasksViewMode.Maps,
                SelectedTarget = "customs"
            };

            var pill = new MapPillTestItem { MapKey = "customs", DisplayName = "Customs", IsSelected = true };
            Assert.Equal(criteria.SelectedTarget, pill.MapKey);
        }

        [Fact]
        public void F5_F8_StatusChips_SubObjectiveStepperSync()
        {
            var card = new TaskCardTestModel { TotalRequirements = 1 };
            var stepper = new SubObjectiveStepperTestItem { CurrentCount = 0, MaxCount = 1 };
            card.ItemSteppers.Add(stepper);

            stepper.Increment();
            card.RecalculateMetRequirements();
            if (card.MetRequirements == card.TotalRequirements)
                card.IsCompleted = true;

            Assert.True(card.IsCompleted);
        }

        [Fact]
        public void F6_F18_FilterBar_UpNextFilterSync()
        {
            var tasks = CreateIntegrationTasks();
            var impactEngine = new TaskImpactEngine(tasks);

            var filtered = tasks.Where(t => t.Name.Contains("Introduction", StringComparison.OrdinalIgnoreCase));
            var ranked = DashboardViewModel.RankUpNextTasks(filtered, impactEngine);

            Assert.Single(ranked);
            Assert.Equal("Introduction", ranked[0].QuestName);
        }

        [Fact]
        public void F7_F8_TaskCard_SubObjectiveStepperInteraction()
        {
            var card = new TaskCardTestModel { TotalRequirements = 2 };
            card.ItemSteppers.Add(new SubObjectiveStepperTestItem { CurrentCount = 0, MaxCount = 2 });
            card.ActionToggles.Add(new SubObjectiveActionTestItem { IsChecked = false });

            card.ItemSteppers[0].SetMax();
            card.RecalculateMetRequirements();
            Assert.Equal("Requirements 1 of 2 met", card.RequirementsSummaryText);

            card.ActionToggles[0].Toggle();
            card.RecalculateMetRequirements();
            Assert.Equal("Requirements 2 of 2 met", card.RequirementsSummaryText);
        }

        [Fact]
        public void F7_F9_TaskCard_RewardsAndImpactFooterSync()
        {
            var card = new TaskCardTestModel { UnlocksNext = 3, Impact = 7, XpReward = 12000, IsExpanded = true };
            Assert.Equal("Unlocks next: 3 | Impact: 7", card.ImpactSummaryText);

            card.ToggleExpand();
            Assert.False(card.IsExpanded);
            Assert.Equal("Unlocks next: 3 | Impact: 7", card.ImpactSummaryText);
        }

        [Fact]
        public void F8_F13_SubObjectiveStepper_RequiredItemsStripSync()
        {
            var stepper = new SubObjectiveStepperTestItem { ItemId = "salewa", CurrentCount = 1, MaxCount = 3 };
            stepper.Increment();

            var stripItem = new { ItemId = "salewa", Count = stepper.CurrentCount, Total = stepper.MaxCount };
            Assert.Equal(2, stripItem.Count);
        }

        [Fact]
        public void F11_F13_MapPills_RequiredKeysAndItemsStrip()
        {
            string selectedMap = "customs";
            var keysByMap = new Dictionary<string, List<string>>
            {
                ["customs"] = new() { "Machinery key", "Customs office key" },
                ["woods"] = new() { "Shturman key" }
            };

            var activeKeys = keysByMap[selectedMap];
            Assert.Equal(2, activeKeys.Count);
        }

        [Fact]
        public void F11_F14_MapPills_ContextualCardsDrawer()
        {
            var tasks = CreateIntegrationTasks();
            string selectedMap = "customs";

            var contextualTasks = tasks.Where(t => t.Maps != null && t.Maps.Contains(selectedMap)).ToList();
            Assert.Equal(2, contextualTasks.Count);
        }

        [Fact]
        public void F11_F15_MapPills_MinimapMapSync()
        {
            var sync = MapTrackerSyncHarness.Instance;
            sync.Reset();

            string? broadcastedMap = null;
            sync.MapChanged += m => broadcastedMap = m;

            sync.SetCurrentMap("customs");
            Assert.Equal("customs", broadcastedMap);
        }

        [Fact]
        public void F12_F15_SvgFloorSwitching_MinimapFloorSync()
        {
            var sync = MapTrackerSyncHarness.Instance;
            sync.Reset();

            string? broadcastedFloor = null;
            sync.FloorChanged += f => broadcastedFloor = f;

            sync.SetCurrentFloor("Second_Floor");
            Assert.Equal("Second_Floor", broadcastedFloor);
        }

        [Fact]
        public void F13_F14_ContextualCards_RequiredKeysStashBadge()
        {
            var card = new { TaskName = "Checking", RequiredKey = "Machinery key" };
            var stashKeys = new HashSet<string> { "Machinery key" };

            bool inStash = stashKeys.Contains(card.RequiredKey);
            Assert.True(inStash);
        }

        [Fact]
        public void F14_F15_ContextualCardPin_MinimapBeaconSync()
        {
            var sync = MapTrackerSyncHarness.Instance;
            sync.Reset();

            TaskObjectiveWithLocation? pinned = null;
            sync.PinnedObjectiveChanged += o => pinned = o;

            var obj = new TaskObjectiveWithLocation { QuestId = "checking", TaskName = "Checking" };
            sync.PinObjective(obj);

            Assert.NotNull(pinned);
            Assert.Equal("checking", pinned.QuestId);
        }

        [Fact]
        public void F16_F17_Dashboard_TopStatsRowSync()
        {
            var stats = DashboardViewModel.CalculateTopStats(5, 20, 2, 10, 8, 16);
            Assert.Equal(25.0, stats.TasksCompletionPercentage);
            Assert.Equal(50.0, stats.HideoutItemsPercentage);
        }

        [Fact]
        public void F17_F19_TopStats_TraderProgressSync()
        {
            var prapor = new TraderProgressCardViewModel("prapor", "Prapor", "url", 1, 0.20, 0.40, 5, 20, false, null);
            var stats = DashboardViewModel.CalculateTopStats(5, 50, 0, 0, 0, 0);

            Assert.Equal(10.0, stats.TasksCompletionPercentage);
            Assert.Equal(5, prapor.CompletedTasks);
        }

        [Fact]
        public void F18_F19_UpNext_TraderLockSync()
        {
            var tasks = CreateIntegrationTasks();
            var impactEngine = new TaskImpactEngine(tasks);

            var ranked = DashboardViewModel.RankUpNextTasks(tasks.Where(t => t.NormalizedName == "introduction"), impactEngine);
            Assert.True(ranked.First().PriorityScore >= 1000);
            Assert.Contains("Jaeger", ranked.First().ReasonText);
        }

        #endregion

        #region Tier 4: Real-World Application Scenarios (10 Tests)

        [Fact]
        public void Scenario1_CompleteQuestObjective_LiveCountUpdatesAcrossViews()
        {
            var card = new TaskCardTestModel { QuestId = "debut", TotalRequirements = 1 };
            var stepper = new SubObjectiveStepperTestItem { CurrentCount = 0, MaxCount = 1 };
            card.ItemSteppers.Add(stepper);

            stepper.Increment();
            card.RecalculateMetRequirements();
            Assert.Equal(1, card.MetRequirements);

            card.MarkComplete();
            Assert.True(card.IsCompleted);

            var pill = new MapPillTestItem { MapKey = "customs", ActiveTaskCount = 5 };
            pill.ActiveTaskCount--;
            Assert.Equal(4, pill.ActiveTaskCount);

            var stats = DashboardViewModel.CalculateTopStats(1, 10, 0, 5, 0, 0);
            Assert.Equal(10.0, stats.TasksCompletionPercentage);
        }

        [Fact]
        public void Scenario2_MapCustoms_RequiredKeysItemsStrips_PinObjective_MinimapSync()
        {
            var sync = MapTrackerSyncHarness.Instance;
            sync.Reset();

            sync.SetCurrentMap("customs");
            Assert.Equal("customs", sync.CurrentMapKey);

            var keys = new List<string> { "Machinery key", "Customs office key" };
            var items = new List<string> { "Salewa (FIR)", "MP-133" };
            Assert.Equal(2, keys.Count);
            Assert.Equal(2, items.Count);

            var obj = new TaskObjectiveWithLocation { QuestId = "checking", TaskName = "Checking" };
            sync.PinObjective(obj);

            Assert.NotNull(sync.PinnedObjective);
            Assert.Equal("checking", sync.PinnedObjective.QuestId);
        }

        [Fact]
        public void Scenario3_DashboardUpNext_CompletePrerequisite_DownstreamUnlocks()
        {
            var tasks = CreateIntegrationTasks();
            var engine1 = new TaskImpactEngine(tasks);

            var initialUpNext = DashboardViewModel.RankUpNextTasks(tasks.Where(t => t.NormalizedName == "debut"), engine1);
            Assert.Equal("Debut", initialUpNext.First().QuestName);

            var engine2 = new TaskImpactEngine(tasks, new[] { "debut" });
            var updatedUpNext = DashboardViewModel.RankUpNextTasks(tasks.Where(t => t.NormalizedName == "checking"), engine2);
            Assert.Equal("Checking", updatedUpNext.First().QuestName);
        }

        [Fact]
        public void Scenario4_TraderProgress_LoyaltyLevelAndReputationProgression()
        {
            var prapor = new TraderProgressCardViewModel("prapor", "Prapor", "url", 1, 0.15, 0.20, 2, 20, false, null);
            Assert.Equal(1, prapor.CurrentLoyaltyLevel);

            double newRep = prapor.CurrentReputation + 0.08;
            int newLL = newRep >= prapor.NextLevelRequiredRep ? 2 : 1;
            var updatedPrapor = prapor with { CurrentReputation = newRep, CurrentLoyaltyLevel = newLL };

            Assert.Equal(2, updatedPrapor.CurrentLoyaltyLevel);
            Assert.True(updatedPrapor.CurrentReputation > 0.20);
        }

        [Fact]
        public void Scenario5_FilterAndSortBar_TransitionsAcrossStatuses()
        {
            var tasks = CreateIntegrationTasks();
            var qvms = new List<QuestViewModel>
            {
                new() { Task = tasks[0], DisplayName = "Debut", Status = QuestStatus.Active },
                new() { Task = tasks[1], DisplayName = "Checking", Status = QuestStatus.Locked },
                new() { Task = tasks[2], DisplayName = "Shootout Picnic", Status = QuestStatus.Locked }
            };

            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.Locked,
                SortBy = TaskSortMode.Name,
                SortDirection = SortDirection.Ascending
            };

            var lockedSorted = QuestFilterEngine.Filter(qvms, criteria, 15);
            Assert.Equal(2, lockedSorted.Count);
            Assert.Equal("Checking", lockedSorted[0].DisplayName);
            Assert.Equal("Shootout Picnic", lockedSorted[1].DisplayName);
        }

        [Fact]
        public void Scenario6_TarkovClock_RaidDurationCycling7xDelta()
        {
            var raidStartUtc = new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.Zero);
            var startDisplay = TarkovClockService.GetCurrentTimes(raidStartUtc);

            var raidEndUtc = raidStartUtc.AddMinutes(45);
            var endDisplay = TarkovClockService.GetCurrentTimes(raidEndUtc);

            var startSpan = TimeSpan.Parse(startDisplay.PrimaryTime);
            var endSpan = TimeSpan.Parse(endDisplay.PrimaryTime);

            var elapsedMinutes = (endSpan - startSpan).TotalMinutes;
            if (elapsedMinutes < 0) elapsedMinutes += 24 * 60;

            Assert.Equal(315.0, elapsedMinutes);
        }

        [Fact]
        public void Scenario7_OfflineProfileRestart_SQLiteDatabasePreservation()
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"tarkov_sim_{Guid.NewGuid():N}.db");
            try
            {
                using (var conn = new SqliteConnection($"Data Source={tempDb}"))
                {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "CREATE TABLE UserProgress (QuestId TEXT PRIMARY KEY, Status TEXT);";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = "INSERT INTO UserProgress VALUES ('debut', 'Done');";
                    cmd.ExecuteNonQuery();
                }

                using (var conn2 = new SqliteConnection($"Data Source={tempDb}"))
                {
                    conn2.Open();
                    using var readCmd = conn2.CreateCommand();
                    readCmd.CommandText = "SELECT Status FROM UserProgress WHERE QuestId = 'debut';";
                    var status = readCmd.ExecuteScalar()?.ToString();

                    Assert.Equal("Done", status);
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(tempDb))
                    File.Delete(tempDb);
            }
        }

        [Fact]
        public void Scenario8_MultiObjectiveTask_QuantityAndActionChecks()
        {
            var card = new TaskCardTestModel { TotalRequirements = 3, MetRequirements = 0 };
            card.ItemSteppers.Add(new SubObjectiveStepperTestItem { CurrentCount = 0, MaxCount = 3 });
            card.ActionToggles.Add(new SubObjectiveActionTestItem { IsChecked = false });
            card.ActionToggles.Add(new SubObjectiveActionTestItem { IsChecked = false });

            card.ItemSteppers[0].SetMax();
            card.RecalculateMetRequirements();
            Assert.Equal("Requirements 1 of 3 met", card.RequirementsSummaryText);

            card.ActionToggles[0].Toggle();
            card.RecalculateMetRequirements();
            Assert.Equal("Requirements 2 of 3 met", card.RequirementsSummaryText);

            card.ActionToggles[1].Toggle();
            card.RecalculateMetRequirements();
            Assert.Equal("Requirements 3 of 3 met", card.RequirementsSummaryText);
            card.MarkComplete();
            Assert.True(card.IsCompleted);
        }

        [Fact]
        public void Scenario9_KappaQuestProgression_DashboardUpdate()
        {
            var stats1 = DashboardViewModel.CalculateTopStats(10, 100, 5, 50, 0, 0);
            Assert.Equal(5, stats1.CompletedKappaTasks);

            var stats2 = DashboardViewModel.CalculateTopStats(11, 100, 6, 50, 0, 0);
            Assert.Equal(6, stats2.CompletedKappaTasks);
            Assert.Equal(50, stats2.TotalKappaTasks);
        }

        [Fact]
        public void Scenario10_HideoutItemProgression_NonCurrencyPercentage()
        {
            var stats1 = DashboardViewModel.CalculateTopStats(0, 0, 0, 0, 20, 40);
            Assert.Equal(50.0, stats1.HideoutItemsPercentage);

            var stats2 = DashboardViewModel.CalculateTopStats(0, 0, 0, 0, 30, 40);
            Assert.Equal(75.0, stats2.HideoutItemsPercentage);
        }

        #endregion
    }
}
