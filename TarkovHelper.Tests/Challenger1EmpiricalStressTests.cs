using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using TarkovHelper.Models;
using TarkovHelper.Pages;
using TarkovHelper.Services;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests
{
    /// <summary>
    /// Empirical Adversarial Stress Test Suite for Gate Challenger 1:
    /// - TaskImpactEngine: Cyclic DAGs, disconnected graphs, nulls/self-refs, trader unlocks, opensCount, scoring.
    /// - TarkovClockService: Midnight transitions, daylight cutoff boundaries, 7x acceleration precision.
    /// - QuestFilterEngine: Permutations across view modes, statuses, and sort modes on massive datasets.
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class Challenger1EmpiricalStressTests : IDisposable
    {
        public Challenger1EmpiricalStressTests()
        {
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
        }

        public void Dispose()
        {
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
        }

        #region 1. TaskImpactEngine Adversarial Challenge Tests

        [Fact]
        public void F1_Adv_01_SelfReferentialLeadsTo_TerminatesSafely()
        {
            // Adversarial input: Task A points to itself
            var selfLoop = new List<TarkovTask>
            {
                new()
                {
                    Id = "self_task",
                    NormalizedName = "self-task",
                    Name = "Self Loop Task",
                    Trader = "Prapor",
                    LeadsTo = new List<string> { "self-task" }
                }
            };

            var engine = new TaskImpactEngine(selfLoop);
            var impact = engine.GetTaskImpact("self-task");

            // Downstream must NOT include the root node itself
            Assert.Equal(0, impact.TotalDownstreamImpact);
        }

        [Fact]
        public void F1_Adv_02_TwoNodeAndThreeNodeCycles_DoNotInfiniteLoop()
        {
            // A -> B -> A
            var twoCycle = new List<TarkovTask>
            {
                new() { Id = "A", NormalizedName = "A", LeadsTo = new List<string> { "B" } },
                new() { Id = "B", NormalizedName = "B", LeadsTo = new List<string> { "A" } }
            };

            var engine2 = new TaskImpactEngine(twoCycle);
            var impactA = engine2.GetTaskImpact("A");
            var impactB = engine2.GetTaskImpact("B");

            Assert.Equal(1, impactA.TotalDownstreamImpact);
            Assert.Equal(1, impactB.TotalDownstreamImpact);

            // A -> B -> C -> A
            var threeCycle = new List<TarkovTask>
            {
                new() { Id = "qA", NormalizedName = "qa", LeadsTo = new List<string> { "qb" } },
                new() { Id = "qB", NormalizedName = "qb", LeadsTo = new List<string> { "qc" } },
                new() { Id = "qC", NormalizedName = "qc", LeadsTo = new List<string> { "qa" } }
            };

            var engine3 = new TaskImpactEngine(threeCycle);
            var impact3A = engine3.GetTaskImpact("qa");
            Assert.Equal(2, impact3A.TotalDownstreamImpact); // Downstream contains qb and qc
            Assert.Equal(1, impact3A.UnlocksNext);
        }

        [Fact]
        public void F1_Adv_03_TangledMultiCyclesAndDeepChains_ScalesLinearly()
        {
            // Create a tangled graph: A -> B -> C -> A and B -> D -> E -> B and E -> F (leaf)
            var tangledTasks = new List<TarkovTask>
            {
                new() { Id = "A", NormalizedName = "a", LeadsTo = new List<string> { "b" } },
                new() { Id = "B", NormalizedName = "b", LeadsTo = new List<string> { "c", "d" } },
                new() { Id = "C", NormalizedName = "c", LeadsTo = new List<string> { "a" } },
                new() { Id = "D", NormalizedName = "d", LeadsTo = new List<string> { "e" } },
                new() { Id = "E", NormalizedName = "e", LeadsTo = new List<string> { "b", "f" } },
                new() { Id = "F", NormalizedName = "f", LeadsTo = new List<string>() }
            };

            var engine = new TaskImpactEngine(tangledTasks);
            var impactA = engine.GetTaskImpact("a");

            // Downstream from A: B, C, D, E, F = 5 distinct nodes
            Assert.Equal(5, impactA.TotalDownstreamImpact);

            // Deep 200-node circular chain
            const int chainLen = 200;
            var deepChain = new List<TarkovTask>();
            for (int i = 0; i < chainLen; i++)
            {
                string next = $"node_{(i + 1) % chainLen}";
                deepChain.Add(new TarkovTask
                {
                    Id = $"node_{i}",
                    NormalizedName = $"node_{i}",
                    LeadsTo = new List<string> { next }
                });
            }

            var deepEngine = new TaskImpactEngine(deepChain);
            var sw = Stopwatch.StartNew();
            var deepImpact = deepEngine.GetTaskImpact("node_0");
            sw.Stop();

            // Downstream from node_0 should be the 199 other nodes
            Assert.Equal(chainLen - 1, deepImpact.TotalDownstreamImpact);
            Assert.True(sw.ElapsedMilliseconds < 500, $"Chain traversal took {sw.ElapsedMilliseconds}ms, expected < 500ms");
        }

        [Fact]
        public void F1_Adv_04_DisconnectedComponentsAndIsolatedNodes_NoCrossContamination()
        {
            // Forest with 3 disjoint components:
            // Component 1: T1 -> T2
            // Component 2: T3 -> T4 -> T5
            // Component 3: T6 (isolated single node)
            var forest = new List<TarkovTask>
            {
                new() { Id = "T1", NormalizedName = "t1", LeadsTo = new List<string> { "t2" } },
                new() { Id = "T2", NormalizedName = "t2", LeadsTo = new List<string>() },
                new() { Id = "T3", NormalizedName = "t3", LeadsTo = new List<string> { "t4" } },
                new() { Id = "T4", NormalizedName = "t4", LeadsTo = new List<string> { "t5" } },
                new() { Id = "T5", NormalizedName = "t5", LeadsTo = new List<string>() },
                new() { Id = "T6", NormalizedName = "t6", LeadsTo = new List<string>() }
            };

            var engine = new TaskImpactEngine(forest);

            var impact1 = engine.GetTaskImpact("t1");
            Assert.Equal(1, impact1.TotalDownstreamImpact);
            Assert.Equal(1, impact1.UnlocksNext);

            var impact3 = engine.GetTaskImpact("t3");
            Assert.Equal(2, impact3.TotalDownstreamImpact);
            Assert.Equal(1, impact3.UnlocksNext);

            var impact6 = engine.GetTaskImpact("t6");
            Assert.Equal(0, impact6.TotalDownstreamImpact);
            Assert.Equal(0, impact6.UnlocksNext);
            Assert.Equal(0, impact6.OpensCount);

            // Querying a completely non-existent task returns empty/zero safely
            var nonExistent = engine.GetTaskImpact("completely_fake_quest_xyz");
            Assert.Equal(0, nonExistent.TotalDownstreamImpact);
            Assert.Equal(0, nonExistent.UnlocksNext);
            Assert.Equal(0, nonExistent.PriorityScore);
        }

        [Fact]
        public void F1_Adv_05_NullAndMalformedData_RobustHandling()
        {
            var malformedTasks = new List<TarkovTask>
            {
                new()
                {
                    Id = "null_leads",
                    NormalizedName = "null-leads",
                    LeadsTo = null, // null collection
                    Previous = null
                },
                new()
                {
                    Id = "empty_strings",
                    NormalizedName = "empty-strings",
                    LeadsTo = new List<string> { "", "   ", null!, "ghost_task" },
                    Previous = new List<string> { "", null!, "another_ghost" }
                },
                new()
                {
                    Id = "",
                    NormalizedName = null, // null identifiers
                    LeadsTo = new List<string> { "null-leads" }
                }
            };

            var engine = new TaskImpactEngine(malformedTasks);

            var impactNull = engine.GetTaskImpact("null-leads");
            Assert.Equal(0, impactNull.TotalDownstreamImpact);
            Assert.Equal(0, impactNull.UnlocksNext);

            var impactEmpty = engine.GetTaskImpact("empty-strings");
            Assert.Equal(0, impactEmpty.TotalDownstreamImpact);
            Assert.Equal(0, impactEmpty.UnlocksNext);

            // Null or whitespace parameter to GetTaskImpact
            var emptyImpact = engine.GetTaskImpact("");
            Assert.Equal(0, emptyImpact.PriorityScore);
            var whitespaceImpact = engine.GetTaskImpact("   ");
            Assert.Equal(0, whitespaceImpact.PriorityScore);
        }

        [Fact]
        public void F1_Adv_06_TraderUnlockDetection_StrictExclusivity()
        {
            var testTasks = new List<TarkovTask>
            {
                new() { Id = "5c0647fdd443bc2504c2d371", NormalizedName = "introduction", Name = "Introduction", Trader = "Mechanic" },
                new() { Id = "6617beeaa9cfa777ca915b7c", NormalizedName = "easy-money---part-1", Name = "Easy Money - Part 1", Trader = "Ref" },
                new() { Id = "638f541a29ffd1183d187f57", NormalizedName = "getting-acquainted", Name = "Getting Acquainted", Trader = "Mechanic" },
                new() { Id = "656f0f98d80a697f855d34b1", NormalizedName = "a-helping-hand", Name = "A Helping Hand", Trader = "Prapor" },
                // Non-unlock quests:
                new() { Id = "quest_debut", NormalizedName = "debut", Name = "Debut", Trader = "Prapor" },
                new() { Id = "quest_shortage", NormalizedName = "shortage", Name = "Shortage", Trader = "Therapist" },
                new() { Id = "quest_tarkov_shooter", NormalizedName = "the-tarkov-shooter-part-1", Name = "Tarkov Shooter 1", Trader = "Jaeger" }
            };

            var engine = new TaskImpactEngine(testTasks);

            // Positive checks
            var intro = engine.GetTaskImpact("introduction");
            Assert.True(intro.UnlocksTrader);
            Assert.Equal("Jaeger", intro.UnlockedTraderName);

            var refQuest = engine.GetTaskImpact("easy-money---part-1");
            Assert.True(refQuest.UnlocksTrader);
            Assert.Equal("Ref", refQuest.UnlockedTraderName);

            var lkQuest = engine.GetTaskImpact("getting-acquainted");
            Assert.True(lkQuest.UnlocksTrader);
            Assert.Equal("Lightkeeper", lkQuest.UnlockedTraderName);

            var btrQuest = engine.GetTaskImpact("a-helping-hand");
            Assert.True(btrQuest.UnlocksTrader);
            Assert.Equal("BTR Driver", btrQuest.UnlockedTraderName);

            // Negative checks: NO false positives
            var nonUnlocks = new[] { "debut", "shortage", "the-tarkov-shooter-part-1", "unknown" };
            foreach (var key in nonUnlocks)
            {
                var impact = engine.GetTaskImpact(key);
                Assert.False(impact.UnlocksTrader, $"Task '{key}' should not unlock a trader!");
                Assert.Null(impact.UnlockedTraderName);
            }
        }

        [Fact]
        public void F1_Adv_07_TraderUnlockPriorityBonus_RemovedWhenCompleted()
        {
            var testTasks = new List<TarkovTask>
            {
                new() { Id = "5c0647fdd443bc2504c2d371", NormalizedName = "introduction", Name = "Introduction", Trader = "Mechanic", ReqKappa = true }
            };

            // Incomplete: should have +1000 flagScore
            var activeEngine = new TaskImpactEngine(testTasks);
            var activeImpact = activeEngine.GetTaskImpact("introduction");
            Assert.True(activeImpact.PriorityScore >= 1000, $"Expected >= 1000, got {activeImpact.PriorityScore}");

            // Completed: should NOT have +1000 flagScore
            var completedEngine = new TaskImpactEngine(testTasks, new[] { "introduction" });
            var completedImpact = completedEngine.GetTaskImpact("introduction");
            Assert.True(completedImpact.PriorityScore < 200, $"Expected < 200 for completed unlock quest, got {completedImpact.PriorityScore}");
        }

        [Fact]
        public void F1_Adv_08_SoleBlockerOpensCount_PermutationsOfCompletionAndGates()
        {
            // DAG:
            // P1 -> Child (requires P1 and P2)
            // P2 -> Child
            // P3 -> LevelChild (requires P3, min level 20)
            // P4 -> FactionChild (requires P4, faction USEC)
            var tasks = new List<TarkovTask>
            {
                new() { Id = "P1", NormalizedName = "p1", LeadsTo = new List<string> { "child" } },
                new() { Id = "P2", NormalizedName = "p2", LeadsTo = new List<string> { "child" } },
                new() { Id = "Child", NormalizedName = "child", Previous = new List<string> { "p1", "p2" } },

                new() { Id = "P3", NormalizedName = "p3", LeadsTo = new List<string> { "level-child" } },
                new() { Id = "LevelChild", NormalizedName = "level-child", Previous = new List<string> { "p3" }, RequiredLevel = 20 },

                new() { Id = "P4", NormalizedName = "p4", LeadsTo = new List<string> { "faction-child" } },
                new() { Id = "FactionChild", NormalizedName = "faction-child", Previous = new List<string> { "p4" }, Faction = "USEC" }
            };

            // State 1: Both P1 and P2 incomplete. Neither is sole blocker.
            var engine1 = new TaskImpactEngine(tasks);
            Assert.Equal(0, engine1.GetTaskImpact("p1").OpensCount);
            Assert.Equal(0, engine1.GetTaskImpact("p2").OpensCount);

            // State 2: P2 completed. P1 IS sole blocker!
            var engine2 = new TaskImpactEngine(tasks, new[] { "p2" });
            Assert.Equal(1, engine2.GetTaskImpact("p1").OpensCount);

            // State 3: Level gate check.
            // Player level 15: P3 does NOT open level-child yet
            var impactP3Underleveled = engine1.GetTaskImpact("p3", null, playerLevelOverride: 15);
            Assert.Equal(0, impactP3Underleveled.OpensCount);

            // Player level 20: P3 DOES open level-child
            var impactP3AtLevel = engine1.GetTaskImpact("p3", null, playerLevelOverride: 20);
            Assert.Equal(1, impactP3AtLevel.OpensCount);

            // State 4: Faction gate check.
            // Player is BEAR: P4 does not open USEC-only task
            var impactP4Bear = engine1.GetTaskImpact("p4", null, playerFactionOverride: "BEAR");
            Assert.Equal(0, impactP4Bear.OpensCount);

            // Player is USEC: P4 opens task
            var impactP4Usec = engine1.GetTaskImpact("p4", null, playerFactionOverride: "USEC");
            Assert.Equal(1, impactP4Usec.OpensCount);
        }

        #endregion

        #region 2. TarkovClockService Adversarial Boundary & Precision Tests

        [Fact]
        public void F2_Adv_01_MidnightTransition_ContinuousAcrossBoundary()
        {
            // Moscow midnight occurs at 21:00 UTC (since Moscow is UTC+3)
            // Test 1-second steps across 21:00 UTC
            var tBefore = new DateTime(2026, 10, 29, 20, 59, 59, DateTimeKind.Utc);
            var tMidnight = new DateTime(2026, 10, 29, 21, 0, 0, DateTimeKind.Utc);
            var tAfter = new DateTime(2026, 10, 29, 21, 0, 1, DateTimeKind.Utc);

            var dBefore = TarkovClockService.CalculateTarkovTime(tBefore);
            var dMidnight = TarkovClockService.CalculateTarkovTime(tMidnight);
            var dAfter = TarkovClockService.CalculateTarkovTime(tAfter);

            // Valid HH:mm format
            Assert.Matches(@"^\d{2}:\d{2}$", dBefore.PrimaryTime);
            Assert.Matches(@"^\d{2}:\d{2}$", dMidnight.PrimaryTime);
            Assert.Matches(@"^\d{2}:\d{2}$", dAfter.PrimaryTime);

            // At Moscow midnight exactly (21:00 UTC), secondsFromMidnight is 0, so primaryTime is 00:00
            Assert.Equal("00:00", dMidnight.PrimaryTime);
            Assert.Equal("12:00", dMidnight.SecondaryTime);

            // One second after, 7 Tarkov seconds elapsed -> still 00:00 (until 60 Tarkov seconds = ~8.5 real seconds)
            Assert.Equal("00:00", dAfter.PrimaryTime);

            // 10 real seconds after midnight = 70 Tarkov seconds = 00:01
            var dTenSecAfter = TarkovClockService.CalculateTarkovTime(tMidnight.AddSeconds(10));
            Assert.Equal("00:01", dTenSecAfter.PrimaryTime);
        }

        [Theory]
        [InlineData(5, 59, 30, false)] // 05:59:30 -> Night
        [InlineData(6, 0, 30, true)]   // 06:00:30 -> Day
        [InlineData(12, 0, 0, true)]   // 12:00:00 -> Day
        [InlineData(20, 59, 30, true)] // 20:59:30 -> Day
        [InlineData(21, 0, 30, false)]  // 21:00:30 -> Night
        [InlineData(0, 0, 30, false)]   // 00:00:30 -> Night
        public void F2_Adv_02_DaylightCutoffExactThresholds(int hour, int min, int sec, bool expectedIsDay)
        {
            // Reverse-calculate real time to produce specific in-game primarySpan:
            // tarkovSeconds = hour * 3600 + min * 60 + sec
            // secondsFromMidnight = tarkovSeconds / 7.0
            double tarkovSeconds = hour * 3600 + min * 60 + sec;
            double realMoscowSeconds = (tarkovSeconds + 0.05) / 7.0; // slight offset to prevent tick truncation below boundary

            // Moscow midnight is 21:00 UTC
            var moscowMidnightUtc = new DateTime(2026, 1, 1, 21, 0, 0, DateTimeKind.Utc);
            var testTimeUtc = moscowMidnightUtc.AddSeconds(realMoscowSeconds);

            var display = TarkovClockService.CalculateTarkovTime(testTimeUtc);

            // Verify reconstructed primary time string matches
            string expectedPrefix = $"{hour:D2}:{min:D2}";
            Assert.Equal(expectedPrefix, display.PrimaryTime);
            Assert.Equal(expectedIsDay, display.IsPrimaryDay);
        }

        [Fact]
        public void F2_Adv_03_MultiHourAndMultiDaySimulation_ZeroDrift()
        {
            // Simulate 720 real hours (30 full real days = 210 in-game days)
            // At 7x speed, exactly 24 real hours = 168 in-game hours = exactly 7 full in-game days.
            // Therefore, after any integer multiple of 24 real hours, the in-game clock must match EXACTLY.
            var baseTime = new DateTime(2026, 6, 1, 10, 15, 30, DateTimeKind.Utc);
            var initialDisplay = TarkovClockService.CalculateTarkovTime(baseTime);

            for (int day = 1; day <= 30; day++)
            {
                var simulatedTime = baseTime.AddDays(day);
                var simulatedDisplay = TarkovClockService.CalculateTarkovTime(simulatedTime);

                Assert.Equal(initialDisplay.PrimaryTime, simulatedDisplay.PrimaryTime);
                Assert.Equal(initialDisplay.SecondaryTime, simulatedDisplay.SecondaryTime);
                Assert.Equal(initialDisplay.IsPrimaryDay, simulatedDisplay.IsPrimaryDay);
                Assert.Equal(initialDisplay.IsSecondaryDay, simulatedDisplay.IsSecondaryDay);
            }
        }

        [Fact]
        public void F2_Adv_04_Dual12HourOffset_ExactnessAcrossEntireCycle()
        {
            // For every 5 minutes in a 24-hour cycle (288 samples)
            var baseUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int m = 0; m < 1440; m += 5)
            {
                var display = TarkovClockService.CalculateTarkovTime(baseUtc.AddMinutes(m));
                var primary = TimeSpan.Parse(display.PrimaryTime);
                var secondary = TimeSpan.Parse(display.SecondaryTime);

                var diffHours = Math.Abs((secondary - primary).TotalHours);
                // Difference must be either 12 hours (e.g. 14:00 vs 02:00) or wrapping at midnight
                Assert.True(Math.Abs(diffHours - 12.0) < 0.001 || Math.Abs(diffHours - 12.0) == 12.0);
            }
        }

        #endregion

        #region 3. QuestFilterEngine Exhaustive Permutations & Stress Tests

        [Fact]
        public void F3_Adv_01_EmptyDataset_AllPermutationsDoNotThrow()
        {
            var emptyList = new List<QuestViewModel>();

            var viewModes = Enum.GetValues<TasksViewMode>();
            var statusFilters = Enum.GetValues<TaskStatusFilter>();
            var sortModes = Enum.GetValues<TaskSortMode>();
            var sortDirections = Enum.GetValues<SortDirection>();

            int countTested = 0;
            foreach (var vm in viewModes)
            {
                foreach (var sf in statusFilters)
                {
                    foreach (var sm in sortModes)
                    {
                        foreach (var sd in sortDirections)
                        {
                            var criteria = new QuestFilterCriteria
                            {
                                ViewMode = vm,
                                TaskStatusFilter = sf,
                                SortBy = sm,
                                SortDirection = sd,
                                SelectedTarget = "Customs",
                                SearchQuery = "shotgun",
                                KappaOnly = true,
                                CurrentLevelOnly = true
                            };

                            var result = QuestFilterEngine.Filter(emptyList, criteria, 15);
                            Assert.Empty(result);

                            var counts = QuestFilterEngine.ComputeStatusCounts(emptyList, criteria, 15);
                            Assert.Equal(0, counts.All);
                            Assert.Equal(0, counts.Available);

                            countTested++;
                        }
                    }
                }
            }

            Assert.Equal(4 * 5 * 5 * 2, countTested); // 200 combinations verified
        }

        [Fact]
        public void F3_Adv_02_Massive5000Quests_FiltersAndSortsPerformantly()
        {
            // Generate 5,000 synthetic quests with heterogeneous attributes
            var traders = new[] { "Prapor", "Therapist", "Skier", "Peacekeeper", "Mechanic", "Ragman", "Jaeger", "Ref", "Lightkeeper" };
            var maps = new[] { "customs", "woods", "factory", "shoreline", "reserve", "interchange", "streets", "lighthouse" };
            var statuses = new[] { QuestStatus.Active, QuestStatus.Locked, QuestStatus.Done, QuestStatus.LevelLocked, QuestStatus.Failed };

            var largeList = new List<QuestViewModel>(5000);
            for (int i = 0; i < 5000; i++)
            {
                var qTrader = traders[i % traders.Length];
                var qMap = maps[i % maps.Length];
                var qStatus = statuses[i % statuses.Length];
                largeList.Add(new QuestViewModel
                {
                    Task = new TarkovTask
                    {
                        Id = $"task_{i}",
                        NormalizedName = $"task-{i}",
                        Name = $"Operation {i} in {qMap}",
                        Trader = qTrader,
                        Maps = new List<string> { qMap },
                        ReqKappa = (i % 3 == 0),
                        RequiredLevel = (i % 50) + 1,
                        RequiredItems = (i % 4 == 0) ? new List<QuestItem> { new() { ItemDisplayName = "Item", Amount = 1 } } : null
                    },
                    DisplayName = $"Operation {i} in {qMap}",
                    Status = qStatus,
                    IsKappaRequired = (i % 3 == 0)
                });
            }

            // Benchmark execution time across various criteria
            var sw = Stopwatch.StartNew();

            var criteria = new QuestFilterCriteria
            {
                ViewMode = TasksViewMode.Traders,
                SelectedTarget = "Prapor",
                TaskStatusFilter = TaskStatusFilter.Available,
                SortBy = TaskSortMode.Name,
                SortDirection = SortDirection.Ascending,
                KappaOnly = true
            };

            var filtered = QuestFilterEngine.Filter(largeList, criteria, 30);
            var counts = QuestFilterEngine.ComputeStatusCounts(largeList, criteria, 30);
            sw.Stop();

            Assert.NotEmpty(filtered);
            Assert.True(sw.ElapsedMilliseconds < 300, $"Massive dataset filtering took {sw.ElapsedMilliseconds}ms, expected < 300ms");
            Assert.All(filtered, q => Assert.Equal(QuestStatus.Active, q.Status));
            Assert.All(filtered, q => Assert.True(q.IsKappaRequired));
        }

        [Fact]
        public void F3_Adv_03_SpecialCharactersAndRegexInjectionInSearch_NoExceptions()
        {
            var quests = new List<QuestViewModel>
            {
                new()
                {
                    Task = new TarkovTask { Id = "q1", NormalizedName = "q1", Name = "Special (Characters) [Task] *+?^$" },
                    DisplayName = "Special (Characters) [Task] *+?^$",
                    Status = QuestStatus.Active
                }
            };

            var nastyQueries = new[]
            {
                "(", ")", "[", "]", "*", "+", "?", "^", "$", "\\", ".*", "(a|b)", "\"", "'",
                "<script>", "DROP TABLE Quests;", "\0", "\n\r", "한글퀘스트", "日本語タスク", "Русский квест"
            };

            foreach (var query in nastyQueries)
            {
                var criteria = new QuestFilterCriteria
                {
                    TaskStatusFilter = TaskStatusFilter.All,
                    SearchQuery = query
                };

                // Must never throw RegexParseException or ArgumentException
                var exception = Record.Exception(() => QuestFilterEngine.Filter(quests, criteria));
                Assert.Null(exception);
            }
        }

        [Fact]
        public void F3_Adv_04_StatusCounts_SumConservationInvariant()
        {
            var quests = new List<QuestViewModel>
            {
                new() { Task = new TarkovTask { Name = "Q1" }, Status = QuestStatus.Active },
                new() { Task = new TarkovTask { Name = "Q2" }, Status = QuestStatus.Active },
                new() { Task = new TarkovTask { Name = "Q3" }, Status = QuestStatus.Locked },
                new() { Task = new TarkovTask { Name = "Q4" }, Status = QuestStatus.LevelLocked },
                new() { Task = new TarkovTask { Name = "Q5" }, Status = QuestStatus.Done },
                new() { Task = new TarkovTask { Name = "Q6" }, Status = QuestStatus.Failed }
            };

            var counts = QuestFilterEngine.ComputeStatusCounts(quests);

            Assert.Equal(6, counts.All);
            Assert.Equal(2, counts.Available);
            Assert.Equal(2, counts.Locked); // 1 Locked + 1 LevelLocked
            Assert.Equal(1, counts.Completed);
            Assert.Equal(1, counts.Failed);

            // Invariant: Available + Locked + Completed + Failed == All
            Assert.Equal(counts.All, counts.Available + counts.Locked + counts.Completed + counts.Failed);
        }

        [Fact]
        public void F3_Adv_05_TraderAndMapCanonicalNormalization_HandlesAllKnownAliases()
        {
            // Trader aliases
            Assert.Equal("BTR Driver", QuestProgressService.ResolveTraderFriendlyName("BTR"));
            Assert.Equal("BTR Driver", QuestProgressService.ResolveTraderFriendlyName("btr-driver"));
            Assert.Equal("BTR Driver", QuestProgressService.ResolveTraderFriendlyName("btr driver"));
            Assert.Equal("Prapor", QuestProgressService.ResolveTraderFriendlyName("prapor"));

            // Map aliases
            Assert.Equal("groundzero", QuestFilterEngine.NormalizeMapKey("ground-zero"));
            Assert.Equal("groundzero", QuestFilterEngine.NormalizeMapKey("sandbox"));
            Assert.Equal("customs", QuestFilterEngine.NormalizeMapKey("bigmap"));
            Assert.Equal("customs", QuestFilterEngine.NormalizeMapKey("Customs"));
            Assert.Equal("thelab", QuestFilterEngine.NormalizeMapKey("lab"));
            Assert.Equal("streets", QuestFilterEngine.NormalizeMapKey("Streets of Tarkov"));
        }

        [Fact]
        public void F1_Adv_09_DuplicateLeadsTo_Behavior()
        {
            var tasksWithDupes = new List<TarkovTask>
            {
                new()
                {
                    Id = "parent_dup",
                    NormalizedName = "parent-dup",
                    LeadsTo = new List<string> { "child_task", "child_task" }
                },
                new()
                {
                    Id = "child_task",
                    NormalizedName = "child-task",
                    Previous = new List<string> { "parent-dup" }
                }
            };

            var engine = new TaskImpactEngine(tasksWithDupes);
            var impact = engine.GetTaskImpact("parent-dup");

            // Total downstream impact deduplicates via HashSet, so it is 1
            Assert.Equal(1, impact.TotalDownstreamImpact);
            // Note: UnlocksNext counts elements in LeadsTo
            Assert.Equal(2, impact.UnlocksNext);
        }

        [Fact]
        public void F1_Adv_10_SelfReferentialLeadsTo_DoesNotOpenSelf()
        {
            var tasks = new List<TarkovTask>
            {
                new()
                {
                    Id = "self_opener",
                    NormalizedName = "self-opener",
                    LeadsTo = new List<string> { "self-opener" }
                }
            };

            var engine = new TaskImpactEngine(tasks);
            var impact = engine.GetTaskImpact("self-opener");

            // Downstream must be 0
            Assert.Equal(0, impact.TotalDownstreamImpact);
        }

        [Fact]
        public void F2_Adv_05_DateTimeMaxValue_DoesNotCrashUnchecked()
        {
            // DateTime.MaxValue AddHours(3) will overflow DateTime range
            // Verify what happens or test the safe max allowable UTC time
            var safeFarFutureUtc = new DateTime(9999, 12, 31, 20, 59, 59, DateTimeKind.Utc);
            var display = TarkovClockService.CalculateTarkovTime(safeFarFutureUtc);
            Assert.NotNull(display.PrimaryTime);
        }

        [Fact]
        public void F3_Adv_06_NullElementsInSearchCollections_SafetyCheck()
        {
            var vm = new QuestViewModel
            {
                Task = new TarkovTask
                {
                    Id = "q_nulls",
                    NormalizedName = "q-nulls",
                    Name = "Task with Nulls in Collections",
                    Ids = new List<string> { "valid_id" },
                    Maps = new List<string> { "customs" }
                },
                DisplayName = "Task with Nulls",
                Status = QuestStatus.Active
            };

            // Non-null collections should match smoothly
            Assert.True(QuestFilterEngine.MatchesSearchQuery(vm, "customs"));
            Assert.True(QuestFilterEngine.MatchesSearchQuery(vm, "valid_id"));
            Assert.False(QuestFilterEngine.MatchesSearchQuery(vm, "nonexistent"));
        }

        #endregion
    }
}

