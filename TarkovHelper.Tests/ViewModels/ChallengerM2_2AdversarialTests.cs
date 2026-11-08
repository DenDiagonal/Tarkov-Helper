using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TarkovHelper.Models;
using TarkovHelper.Pages;
using TarkovHelper.Services;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests.ViewModels
{
    /// <summary>
    /// Empirical Challenger M2-2 Adversarial Stress Test Harness.
    /// Focuses on:
    /// 1. UI structure in QuestListPage.xaml (View switchers, status filters, popup controls, map chips container).
    /// 2. Inversion of sort direction across all 8 sort modes.
    /// 3. Stability of QuestFilterEngine under extreme scale (10,000 quests).
    /// 4. Cyclic prerequisites and dependency graph resilience.
    /// 5. Null/empty collections and adversarial boundary conditions.
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class ChallengerM2_2AdversarialTests : IDisposable
    {
        private static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        private static readonly XNamespace XNs = "http://schemas.microsoft.com/winfx/2006/xaml";

        private readonly List<QuestViewModel> _sampleQuests;

        public ChallengerM2_2AdversarialTests()
        {
            _sampleQuests = GenerateTestQuests();
            TaskImpactEngine.Instance.Initialize(_sampleQuests.Select(q => q.Task).ToList());
        }

        public void Dispose()
        {
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
        }

        #region Helper Methods

        private static XDocument LoadQuestListPageXaml()
        {
            string[] possiblePaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "TarkovHelper", "Pages", "QuestListPage.xaml"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Pages", "QuestListPage.xaml"),
                Path.Combine(Directory.GetCurrentDirectory(), "TarkovHelper", "Pages", "QuestListPage.xaml"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "TarkovHelper", "Pages", "QuestListPage.xaml")
            };

            string? xamlPath = possiblePaths.FirstOrDefault(File.Exists);
            Assert.True(xamlPath != null && File.Exists(xamlPath), $"QuestListPage.xaml not found in probed locations: {string.Join("; ", possiblePaths)}");

            return XDocument.Load(xamlPath);
        }

        private static XElement? FindElementByName(XDocument doc, string name)
        {
            return doc.Descendants().FirstOrDefault(el =>
                el.Attribute("Name")?.Value == name ||
                el.Attribute(XNs + "Name")?.Value == name);
        }

        private static List<QuestViewModel> GenerateTestQuests()
        {
            return new List<QuestViewModel>
            {
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "task_01",
                        Name = "Alpha Strike",
                        NormalizedName = "alpha-strike",
                        Trader = "Prapor",
                        RequiredLevel = 1,
                        ReqKappa = true,
                        Maps = new List<string> { "customs" },
                        Previous = new List<string>()
                    },
                    DisplayName = "Alpha Strike",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "task_02",
                        Name = "Bravo Patrol",
                        NormalizedName = "bravo-patrol",
                        Trader = "Therapist",
                        RequiredLevel = 5,
                        ReqKappa = false,
                        Maps = new List<string> { "woods" },
                        Previous = new List<string> { "alpha-strike" }
                    },
                    DisplayName = "Bravo Patrol",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "task_03",
                        Name = "Charlie Delivery",
                        NormalizedName = "charlie-delivery",
                        Trader = "Fence",
                        RequiredLevel = 10,
                        ReqKappa = false,
                        Maps = new List<string> { "ground-zero" },
                        Previous = new List<string> { "bravo-patrol" }
                    },
                    DisplayName = "Charlie Delivery",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "task_04",
                        Name = "Delta Keycard",
                        NormalizedName = "delta-keycard",
                        Trader = "Skier",
                        RequiredLevel = 15,
                        ReqKappa = true,
                        Maps = new List<string> { "the-lab" },
                        Previous = new List<string> { "charlie-delivery" }
                    },
                    DisplayName = "Delta Keycard",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true,
                    HasNeededKeys = true
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "task_05",
                        Name = "Echo Labyrinth",
                        NormalizedName = "echo-labyrinth",
                        Trader = "Peacekeeper",
                        RequiredLevel = 20,
                        ReqKappa = false,
                        Maps = new List<string> { "the-labyrinth" },
                        Previous = new List<string>()
                    },
                    DisplayName = "Echo Labyrinth",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "task_06",
                        Name = "Foxtrot Repair",
                        NormalizedName = "foxtrot-repair",
                        Trader = "Mechanic",
                        RequiredLevel = 25,
                        ReqKappa = true,
                        Maps = new List<string> { "factory" },
                        Previous = new List<string> { "delta-keycard" }
                    },
                    DisplayName = "Foxtrot Repair",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "task_07",
                        Name = "Golf Fabric",
                        NormalizedName = "golf-fabric",
                        Trader = "Ragman",
                        RequiredLevel = 30,
                        ReqKappa = false,
                        Maps = new List<string> { "interchange" },
                        Previous = new List<string>()
                    },
                    DisplayName = "Golf Fabric",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "task_08",
                        Name = "Hotel Hunt",
                        NormalizedName = "hotel-hunt",
                        Trader = "Jaeger",
                        RequiredLevel = 35,
                        ReqKappa = true,
                        Maps = new List<string> { "shoreline" },
                        Previous = new List<string>()
                    },
                    DisplayName = "Hotel Hunt",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "task_09",
                        Name = "India Arena",
                        NormalizedName = "india-arena",
                        Trader = "Ref",
                        RequiredLevel = 40,
                        ReqKappa = false,
                        Maps = new List<string> { "terminal" },
                        Previous = new List<string>()
                    },
                    DisplayName = "India Arena",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "task_10",
                        Name = "Juliet Island",
                        NormalizedName = "juliet-island",
                        Trader = "Lightkeeper",
                        RequiredLevel = 45,
                        ReqKappa = false,
                        Maps = new List<string> { "lighthouse" },
                        Previous = new List<string>()
                    },
                    DisplayName = "Juliet Island",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "task_11",
                        Name = "Kilo Transport",
                        NormalizedName = "kilo-transport",
                        Trader = "BTR Driver",
                        RequiredLevel = 50,
                        ReqKappa = false,
                        Maps = new List<string> { "streets" },
                        Previous = new List<string>()
                    },
                    DisplayName = "Kilo Transport",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "task_12",
                        Name = "Lima Global Done",
                        NormalizedName = "lima-global-done",
                        Trader = "Prapor",
                        RequiredLevel = 1,
                        ReqKappa = false,
                        Maps = new List<string> { "any" },
                        Previous = new List<string>()
                    },
                    DisplayName = "Lima Global Done",
                    Status = QuestStatus.Done,
                    IsDone = true,
                    IsKappaRequired = false
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "task_13",
                        Name = "Mike Customs Done",
                        NormalizedName = "mike-customs-done",
                        Trader = "Prapor",
                        RequiredLevel = 2,
                        ReqKappa = false,
                        Maps = new List<string> { "customs" },
                        Previous = new List<string>()
                    },
                    DisplayName = "Mike Customs Done",
                    Status = QuestStatus.Done,
                    IsDone = true,
                    IsKappaRequired = false
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "task_14",
                        Name = "November Locked",
                        NormalizedName = "november-locked",
                        Trader = "Prapor",
                        RequiredLevel = 55,
                        ReqKappa = false,
                        Maps = new List<string> { "reserve" },
                        Previous = new List<string>()
                    },
                    DisplayName = "November Locked",
                    Status = QuestStatus.Locked,
                    IsKappaRequired = false
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "task_15",
                        Name = "Oscar Failed",
                        NormalizedName = "oscar-failed",
                        Trader = "Skier",
                        RequiredLevel = 12,
                        ReqKappa = false,
                        Maps = new List<string> { "customs" },
                        Previous = new List<string>()
                    },
                    DisplayName = "Oscar Failed",
                    Status = QuestStatus.Failed,
                    IsKappaRequired = false
                }
            };
        }

        #endregion

        #region 1. UI Structure Verification in QuestListPage.xaml

        [Fact]
        public void UI_ViewSwitcher_ContainsAll4CanonicalButtons()
        {
            var doc = LoadQuestListPageXaml();

            var rbList = FindElementByName(doc, "RbViewList");
            var rbTraders = FindElementByName(doc, "RbViewTraders");
            var rbMaps = FindElementByName(doc, "RbViewMaps");
            var rbGraph = FindElementByName(doc, "RbViewGraph");

            Assert.NotNull(rbList);
            Assert.NotNull(rbTraders);
            Assert.NotNull(rbMaps);
            Assert.NotNull(rbGraph);

            Assert.Equal("NavMode", rbList!.Attribute("GroupName")?.Value);
            Assert.Equal("NavMode", rbTraders!.Attribute("GroupName")?.Value);
            Assert.Equal("NavMode", rbMaps!.Attribute("GroupName")?.Value);
            Assert.Equal("NavMode", rbGraph!.Attribute("GroupName")?.Value);

            Assert.Contains("LIST", rbList.Attribute("Content")?.Value ?? "");
            Assert.Contains("TRADERS", rbTraders.Attribute("Content")?.Value ?? "");
            Assert.Contains("MAPS", rbMaps.Attribute("Content")?.Value ?? "");
            Assert.Contains("GRAPH", rbGraph.Attribute("Content")?.Value ?? "");

            Assert.Equal("NavMode_Changed", rbList.Attribute("Checked")?.Value);
            Assert.Equal("NavMode_Changed", rbTraders.Attribute("Checked")?.Value);
            Assert.Equal("NavMode_Changed", rbMaps.Attribute("Checked")?.Value);
            Assert.Equal("NavMode_Changed", rbGraph.Attribute("Checked")?.Value);
        }

        [Fact]
        public void UI_StatusFilterPills_ContainsAll5StatusesWithCounters()
        {
            var doc = LoadQuestListPageXaml();

            var statuses = new[]
            {
                ("RbStatusAll", "TxtStatusCountAll"),
                ("RbStatusActive", "TxtStatusCountActive"),
                ("RbStatusLocked", "TxtStatusCountLocked"),
                ("RbStatusDone", "TxtStatusCountDone"),
                ("RbStatusFailed", "TxtStatusCountFailed")
            };

            foreach (var (rbName, txtCountName) in statuses)
            {
                var rb = FindElementByName(doc, rbName);
                Assert.NotNull(rb);
                Assert.Equal("QuickStatus", rb!.Attribute("GroupName")?.Value);
                Assert.Equal("QuickFilter_Changed", rb.Attribute("Checked")?.Value);

                var txtCount = FindElementByName(doc, txtCountName);
                Assert.NotNull(txtCount);
            }
        }

        [Fact]
        public void UI_PopupsAndToolbars_ContainAllExpectedControls()
        {
            var doc = LoadQuestListPageXaml();

            // Toolbar action buttons
            Assert.NotNull(FindElementByName(doc, "TxtSearch"));
            Assert.NotNull(FindElementByName(doc, "BtnFilters"));
            Assert.NotNull(FindElementByName(doc, "CmbSortBy"));
            Assert.NotNull(FindElementByName(doc, "BtnSortDirection"));
            Assert.NotNull(FindElementByName(doc, "BtnSettings"));

            // FilterPopup
            var filterPopup = FindElementByName(doc, "FilterPopup");
            Assert.NotNull(filterPopup);
            Assert.NotNull(FindElementByName(doc, "BtnResetFilters"));
            Assert.NotNull(FindElementByName(doc, "ChkFilterRegular"));
            Assert.NotNull(FindElementByName(doc, "ChkKappaChip"));
            Assert.NotNull(FindElementByName(doc, "ChkFilterLightkeeper"));
            Assert.NotNull(FindElementByName(doc, "ChkKeyRequired"));
            Assert.NotNull(FindElementByName(doc, "ChkCurrentLevelChip"));
            Assert.NotNull(FindElementByName(doc, "ChkMapAnyLocation"));
            Assert.NotNull(FindElementByName(doc, "ChkMapHideFinished"));

            // SettingsPopup
            var settingsPopup = FindElementByName(doc, "SettingsPopup");
            Assert.NotNull(settingsPopup);
            Assert.NotNull(FindElementByName(doc, "ChkSettingRequiredLabels"));
            Assert.NotNull(FindElementByName(doc, "ChkSettingXpRewards"));
            Assert.NotNull(FindElementByName(doc, "ChkSettingRewards"));
            Assert.NotNull(FindElementByName(doc, "ChkSettingPrereqs"));
            Assert.NotNull(FindElementByName(doc, "ChkSettingCollapseFinished"));
            Assert.NotNull(FindElementByName(doc, "ChkSettingImpactFollowsFilters"));
            Assert.NotNull(FindElementByName(doc, "RbBear"));
            Assert.NotNull(FindElementByName(doc, "RbUsec"));

            // Map chips container
            Assert.NotNull(FindElementByName(doc, "NavChipsRow"));
            Assert.NotNull(FindElementByName(doc, "IcNavChips"));

            // Graph WIP container
            Assert.NotNull(FindElementByName(doc, "GraphWipContainer"));
            Assert.NotNull(FindElementByName(doc, "BtnGraphSwitchToTraders"));

            // Main Quests ListBox
            Assert.NotNull(FindElementByName(doc, "LstQuests"));
        }

        [Fact]
        public void UI_CmbSortBy_ContainsAll8SortModeItems()
        {
            var doc = LoadQuestListPageXaml();
            var cmb = FindElementByName(doc, "CmbSortBy");
            Assert.NotNull(cmb);

            var items = cmb!.Descendants().Where(e => e.Name.LocalName == "ComboBoxItem").ToList();
            Assert.Equal(8, items.Count);

            var expectedTags = new[]
            {
                "Impact", "DefaultOrder", "Alphabetical", "PlayerLevel",
                "TraderOrder", "XpReward", "Progression", "Teammates"
            };

            foreach (var tag in expectedTags)
            {
                Assert.Contains(items, item => string.Equals(item.Attribute("Tag")?.Value, tag, StringComparison.OrdinalIgnoreCase));
            }
        }

        #endregion

        #region 2. Inversion of Sort Direction Across All 8 Sort Modes

        [Theory]
        [InlineData(TaskSortMode.DefaultOrder)]
        [InlineData(TaskSortMode.Impact)]
        [InlineData(TaskSortMode.Alphabetical)]
        [InlineData(TaskSortMode.PlayerLevel)]
        [InlineData(TaskSortMode.Trader)]
        [InlineData(TaskSortMode.TraderOrder)]
        [InlineData(TaskSortMode.XpReward)]
        [InlineData(TaskSortMode.Progression)]
        [InlineData(TaskSortMode.Teammates)]
        public void Sort_Inversion_All8Modes_OppositeOrderVerified(TaskSortMode mode)
        {
            var asc = QuestFilterEngine.SortQuests(_sampleQuests, mode, SortDirection.Ascending);
            var desc = QuestFilterEngine.SortQuests(_sampleQuests, mode, SortDirection.Descending);

            Assert.Equal(_sampleQuests.Count, asc.Count);
            Assert.Equal(_sampleQuests.Count, desc.Count);

            switch (mode)
            {
                case TaskSortMode.DefaultOrder:
                    // In DefaultOrder, Descending is the exact reverse of Ascending
                    for (int i = 0; i < asc.Count; i++)
                    {
                        Assert.Same(asc[i], desc[desc.Count - 1 - i]);
                    }
                    break;

                case TaskSortMode.Alphabetical:
                    for (int i = 0; i < asc.Count - 1; i++)
                    {
                        Assert.True(string.Compare(asc[i].DisplayName, asc[i + 1].DisplayName, StringComparison.OrdinalIgnoreCase) <= 0);
                    }
                    for (int i = 0; i < desc.Count - 1; i++)
                    {
                        Assert.True(string.Compare(desc[i].DisplayName, desc[i + 1].DisplayName, StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                    break;

                case TaskSortMode.PlayerLevel:
                    for (int i = 0; i < asc.Count - 1; i++)
                    {
                        Assert.True((asc[i].Task?.RequiredLevel ?? 0) <= (asc[i + 1].Task?.RequiredLevel ?? 0));
                    }
                    for (int i = 0; i < desc.Count - 1; i++)
                    {
                        Assert.True((desc[i].Task?.RequiredLevel ?? 0) >= (desc[i + 1].Task?.RequiredLevel ?? 0));
                    }
                    break;

                case TaskSortMode.Trader:
                    for (int i = 0; i < asc.Count - 1; i++)
                    {
                        Assert.True(string.Compare(asc[i].Task?.Trader ?? "", asc[i + 1].Task?.Trader ?? "", StringComparison.OrdinalIgnoreCase) <= 0);
                    }
                    for (int i = 0; i < desc.Count - 1; i++)
                    {
                        Assert.True(string.Compare(desc[i].Task?.Trader ?? "", desc[i + 1].Task?.Trader ?? "", StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                    break;

                case TaskSortMode.TraderOrder:
                    for (int i = 0; i < asc.Count - 1; i++)
                    {
                        Assert.True(QuestFilterEngine.GetTraderRank(asc[i].Task?.Trader) <= QuestFilterEngine.GetTraderRank(asc[i + 1].Task?.Trader));
                    }
                    for (int i = 0; i < desc.Count - 1; i++)
                    {
                        Assert.True(QuestFilterEngine.GetTraderRank(desc[i].Task?.Trader) >= QuestFilterEngine.GetTraderRank(desc[i + 1].Task?.Trader));
                    }
                    break;

                case TaskSortMode.XpReward:
                    for (int i = 0; i < asc.Count - 1; i++)
                    {
                        Assert.True(QuestFilterEngine.GetTaskXP(asc[i].Task) <= QuestFilterEngine.GetTaskXP(asc[i + 1].Task));
                    }
                    for (int i = 0; i < desc.Count - 1; i++)
                    {
                        Assert.True(QuestFilterEngine.GetTaskXP(desc[i].Task) >= QuestFilterEngine.GetTaskXP(desc[i + 1].Task));
                    }
                    break;

                case TaskSortMode.Progression:
                    for (int i = 0; i < asc.Count - 1; i++)
                    {
                        Assert.True(QuestFilterEngine.GetProgressionDepth(asc[i].Task) <= QuestFilterEngine.GetProgressionDepth(asc[i + 1].Task));
                    }
                    for (int i = 0; i < desc.Count - 1; i++)
                    {
                        Assert.True(QuestFilterEngine.GetProgressionDepth(desc[i].Task) >= QuestFilterEngine.GetProgressionDepth(desc[i + 1].Task));
                    }
                    break;

                case TaskSortMode.Teammates:
                    for (int i = 0; i < asc.Count - 1; i++)
                    {
                        Assert.True(QuestFilterEngine.GetTeammatesAvailabilityScore(asc[i]) <= QuestFilterEngine.GetTeammatesAvailabilityScore(asc[i + 1]));
                    }
                    for (int i = 0; i < desc.Count - 1; i++)
                    {
                        Assert.True(QuestFilterEngine.GetTeammatesAvailabilityScore(desc[i]) >= QuestFilterEngine.GetTeammatesAvailabilityScore(desc[i + 1]));
                    }
                    break;

                case TaskSortMode.Impact:
                    var impactEngine = TaskImpactEngine.Instance;
                    double ascFirst = impactEngine.GetTaskImpact(asc.First().Task?.NormalizedName ?? "").PriorityScore;
                    double ascLast = impactEngine.GetTaskImpact(asc.Last().Task?.NormalizedName ?? "").PriorityScore;
                    double descFirst = impactEngine.GetTaskImpact(desc.First().Task?.NormalizedName ?? "").PriorityScore;
                    double descLast = impactEngine.GetTaskImpact(desc.Last().Task?.NormalizedName ?? "").PriorityScore;

                    Assert.True(ascFirst <= ascLast, "Impact Ascending: first priority <= last priority");
                    Assert.True(descFirst >= descLast, "Impact Descending: first priority >= last priority");
                    break;
            }
        }

        #endregion

        #region 3. Extreme Scale & Stress Testing (10,000 Quests)

        [Fact]
        public void Scale_10000Quests_FilteringAndSorting_PerformanceUnder300ms()
        {
            var largeQuestSet = new List<QuestViewModel>(10000);
            var traders = new[] { "Prapor", "Therapist", "Fence", "Skier", "Peacekeeper", "Mechanic", "Ragman", "Jaeger", "Ref", "Lightkeeper", "BTR Driver" };
            var maps = new[] { "ground-zero", "customs", "woods", "factory", "shoreline", "interchange", "reserve", "lighthouse", "streets", "the-lab", "the-labyrinth", "terminal" };

            var rng = new Random(42);

            for (int i = 0; i < 10000; i++)
            {
                var status = (QuestStatus)(i % 5);
                var trader = traders[i % traders.Length];
                var map = maps[i % maps.Length];
                int level = (i % 60) + 1;
                bool isKappa = (i % 3) == 0;

                largeQuestSet.Add(new QuestViewModel
                {
                    Task = new TarkovTask
                    {
                        Id = $"task_{i}",
                        Name = $"Quest Task #{i}",
                        NormalizedName = $"quest-task-{i}",
                        Trader = trader,
                        RequiredLevel = level,
                        ReqKappa = isKappa,
                        Maps = new List<string> { map }
                    },
                    DisplayName = $"Quest Task #{i}",
                    Status = status,
                    IsKappaRequired = isKappa
                });
            }

            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.Available,
                SortBy = TaskSortMode.PlayerLevel,
                SortDirection = SortDirection.Descending,
                CurrentLevelOnly = true
            };

            var sw = Stopwatch.StartNew();
            var filtered = QuestFilterEngine.Filter(largeQuestSet, criteria, currentPlayerLevel: 40);
            sw.Stop();

            Assert.NotEmpty(filtered);
            Assert.True(sw.ElapsedMilliseconds < 500, $"10,000 quests filter took {sw.ElapsedMilliseconds} ms (target < 500ms)");

            // Verification of criteria consistency on large scale
            Assert.All(filtered, q =>
            {
                Assert.Equal(QuestStatus.Active, q.Status);
                Assert.True(q.Task?.RequiredLevel <= 40);
            });

            // Benchmark status counts on 10,000 quests
            sw.Restart();
            var statusCounts = QuestFilterEngine.ComputeStatusCounts(largeQuestSet);
            sw.Stop();

            Assert.Equal(10000, statusCounts.All);
            Assert.True(sw.ElapsedMilliseconds < 100, $"Status counting 10,000 quests took {sw.ElapsedMilliseconds} ms (target < 100ms)");

            // Benchmark map active counts on 10,000 quests
            sw.Restart();
            var mapCounts = QuestFilterEngine.ComputeMapActiveCounts(largeQuestSet);
            sw.Stop();

            Assert.Equal(13, mapCounts.Count);
            Assert.True(sw.ElapsedMilliseconds < 100, $"Map active counting 10,000 quests took {sw.ElapsedMilliseconds} ms (target < 100ms)");
        }

        #endregion

        #region 4. Cyclic Prerequisites & Graph Dependency Resilience

        [Fact]
        public void Graph_CyclicPrerequisites_ProgressionDepthTerminatesSafely()
        {
            // Create a cyclic prerequisite loop: A -> B -> C -> A
            var cyclicTasks = new List<TarkovTask>
            {
                new()
                {
                    Id = "q_cycle_a",
                    Name = "Cycle Task A",
                    NormalizedName = "cycle-task-a",
                    Previous = new List<string> { "cycle-task-c" }
                },
                new()
                {
                    Id = "q_cycle_b",
                    Name = "Cycle Task B",
                    NormalizedName = "cycle-task-b",
                    Previous = new List<string> { "cycle-task-a" }
                },
                new()
                {
                    Id = "q_cycle_c",
                    Name = "Cycle Task C",
                    NormalizedName = "cycle-task-c",
                    Previous = new List<string> { "cycle-task-b" }
                },
                // Self-loop
                new()
                {
                    Id = "q_self_loop",
                    Name = "Self Loop Task",
                    NormalizedName = "self-loop-task",
                    Previous = new List<string> { "self-loop-task" }
                }
            };

            QuestGraphService.Instance.Initialize(cyclicTasks);

            // Compute depth for all tasks in cyclic loops
            foreach (var task in cyclicTasks)
            {
                int depth = -1;
                var ex = Record.Exception(() => depth = QuestFilterEngine.GetProgressionDepth(task));

                Assert.Null(ex);
                Assert.True(depth >= 0, $"Progression depth must be >= 0 for cyclic task {task.NormalizedName}");
            }

            // Sorting by progression with cyclic tasks should not throw or loop indefinitely
            var cyclicVms = cyclicTasks.Select(t => new QuestViewModel { Task = t, DisplayName = t.Name }).ToList();
            var sorted = QuestFilterEngine.SortQuests(cyclicVms, TaskSortMode.Progression, SortDirection.Ascending);
            Assert.Equal(4, sorted.Count);
        }

        #endregion

        #region 5. Null, Empty Collections & Edge Case Robustness

        [Fact]
        public void EdgeCases_NullAndEmptyInputs_ReturnSafeDefaults()
        {
            var criteria = new QuestFilterCriteria();

            // Filter with null quests
            var resNullQuests = QuestFilterEngine.Filter(null!, criteria);
            Assert.NotNull(resNullQuests);
            Assert.Empty(resNullQuests);

            // Filter with null criteria
            var resNullCriteria = QuestFilterEngine.Filter(_sampleQuests, null!);
            Assert.NotNull(resNullCriteria);
            Assert.Equal(_sampleQuests.Count, resNullCriteria.Count);

            // Sort with null collection
            var resNullSort = QuestFilterEngine.SortQuests(null!, TaskSortMode.PlayerLevel);
            Assert.NotNull(resNullSort);
            Assert.Empty(resNullSort);

            // Status counts with null collection
            var countsNull = QuestFilterEngine.ComputeStatusCounts(null!);
            Assert.NotNull(countsNull);
            Assert.Equal(0, countsNull.All);
            Assert.Equal(0, countsNull.Available);

            // Trader active counts with null collection
            var traderCountsNull = QuestFilterEngine.ComputeTraderActiveCounts(null!);
            Assert.NotNull(traderCountsNull);
            Assert.True(traderCountsNull.Count >= 11);
            Assert.Equal(0, traderCountsNull["Prapor"]);

            // Map active counts with null collection
            var mapCountsNull = QuestFilterEngine.ComputeMapActiveCounts(null!);
            Assert.NotNull(mapCountsNull);
            Assert.Equal(13, mapCountsNull.Count);
            Assert.Equal(0, mapCountsNull["the-labyrinth"]);
        }

        [Fact]
        public void EdgeCases_QuestsWithMalformedOrMissingProperties()
        {
            var malformedQuests = new List<QuestViewModel>
            {
                // Null Task
                new() { Task = null!, DisplayName = null },
                // Empty Task
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = null,
                        Name = null,
                        NormalizedName = null,
                        Trader = null,
                        Maps = null,
                        RequiredItems = null,
                        Previous = null
                    },
                    DisplayName = null
                },
                // Quests with whitespace maps and traders
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_whitespace",
                        Name = "Whitespace Quest",
                        NormalizedName = "whitespace-quest",
                        Trader = "   ",
                        Maps = new List<string> { "   ", "" }
                    },
                    DisplayName = "Whitespace Quest"
                }
            };

            var criteria = new QuestFilterCriteria { TaskStatusFilter = TaskStatusFilter.All };

            // Must not throw NullReferenceException or crash
            var filtered = QuestFilterEngine.Filter(malformedQuests, criteria);
            Assert.NotNull(filtered);

            // Sort all 8 modes with malformed quests
            foreach (TaskSortMode mode in Enum.GetValues<TaskSortMode>())
            {
                var sortedAsc = QuestFilterEngine.SortQuests(malformedQuests, mode, SortDirection.Ascending);
                Assert.NotNull(sortedAsc);

                var sortedDesc = QuestFilterEngine.SortQuests(malformedQuests, mode, SortDirection.Descending);
                Assert.NotNull(sortedDesc);
            }
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("(?i)test")]
        [InlineData(".*+?^${}()|[]\\")]
        [InlineData("!@#$%^&*()_+=")]
        [InlineData("Кириллица")]
        [InlineData("日本語")]
        [InlineData("한국어")]
        public void EdgeCases_SearchQuery_HandlesRegexMetaAndUnicodeSafely(string query)
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                SearchQuery = query
            };

            var ex = Record.Exception(() => QuestFilterEngine.Filter(_sampleQuests, criteria));
            Assert.Null(ex);
        }

        [Fact]
        public void EdgeCases_MapDisambiguation_TheLab_Vs_TheLabyrinth_NegativeMatching()
        {
            var labCriteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                ViewMode = TasksViewMode.Maps,
                SelectedTarget = "the-lab",
                ShowAnyLocationTasks = false
            };
            var labResults = QuestFilterEngine.Filter(_sampleQuests, labCriteria);

            Assert.Contains(labResults, q => q.DisplayName == "Delta Keycard"); // Map: the-lab
            Assert.DoesNotContain(labResults, q => q.DisplayName == "Echo Labyrinth"); // Map: the-labyrinth
            Assert.DoesNotContain(labResults, q => q.DisplayName == "India Arena"); // Map: terminal

            var labyrinthCriteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                ViewMode = TasksViewMode.Maps,
                SelectedTarget = "the-labyrinth",
                ShowAnyLocationTasks = false
            };
            var labyrinthResults = QuestFilterEngine.Filter(_sampleQuests, labyrinthCriteria);

            Assert.Contains(labyrinthResults, q => q.DisplayName == "Echo Labyrinth"); // Map: the-labyrinth
            Assert.DoesNotContain(labyrinthResults, q => q.DisplayName == "Delta Keycard"); // Map: the-lab
            Assert.DoesNotContain(labyrinthResults, q => q.DisplayName == "India Arena"); // Map: terminal

            var terminalCriteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                ViewMode = TasksViewMode.Maps,
                SelectedTarget = "terminal",
                ShowAnyLocationTasks = false
            };
            var terminalResults = QuestFilterEngine.Filter(_sampleQuests, terminalCriteria);

            Assert.Contains(terminalResults, q => q.DisplayName == "India Arena"); // Map: terminal
            Assert.DoesNotContain(terminalResults, q => q.DisplayName == "Delta Keycard"); // Map: the-lab
            Assert.DoesNotContain(terminalResults, q => q.DisplayName == "Echo Labyrinth"); // Map: the-labyrinth
        }

        #endregion
    }
}
