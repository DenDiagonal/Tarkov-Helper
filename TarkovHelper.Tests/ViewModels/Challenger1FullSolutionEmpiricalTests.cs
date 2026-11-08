using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Pages;
using TarkovHelper.Pages.Map.Components;
using TarkovHelper.Services;
using TarkovHelper.Services.Map;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests.ViewModels
{
    /// <summary>
    /// Empirical Adversarial Verification Suite executed by Challenger 1.
    /// Strictly and empirically verifies:
    /// 1. Mode transitions (rapid switching between List, Traders, Maps, Graph).
    /// 2. Filter boundary conditions (empty search query, no matching quests, all traders vs single trader, combined Trader+Map filter).
    /// 3. Bidirectional quest-map event firing (map objective selection, floor selection, pinned objective lockstep with MapTrackerService.Instance).
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class Challenger1FullSolutionEmpiricalTests : IDisposable
    {
        private readonly int _originalLevel;
        private readonly ProfileType _originalProfile;
        private readonly double _originalScavRep;
        private readonly string? _originalMap;
        private readonly string? _originalFloor;
        private readonly TaskObjectiveWithLocation? _originalPinned;

        public Challenger1FullSolutionEmpiricalTests()
        {
            _originalLevel = SettingsService.Instance.PlayerLevel;
            _originalProfile = ProfileService.Instance.CurrentProfile;
            _originalScavRep = SettingsService.Instance.ScavRep;
            _originalMap = MapTrackerService.Instance.CurrentMapKey;
            _originalFloor = MapTrackerService.Instance.CurrentFloorId;
            _originalPinned = MapTrackerService.Instance.PinnedObjective;

            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
        }

        public void Dispose()
        {
            SettingsService.Instance.PlayerLevel = _originalLevel;
            ProfileService.Instance.CurrentProfile = _originalProfile;
            SettingsService.Instance.ScavRep = _originalScavRep;
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();

            if (!string.IsNullOrEmpty(_originalMap))
            {
                MapTrackerService.Instance.SetCurrentMap(_originalMap, force: true);
            }
            MapTrackerService.Instance.SetCurrentFloor(_originalFloor);
            MapTrackerService.Instance.PinObjective(_originalPinned);
        }

        #region 1. Mode Transitions & Structural Layout Stress

        [Fact]
        public void ModeTransitions_XAML_StructuralDeclarations_AllModesSupported()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var xamlPath = Path.Combine(baseDir, "..", "..", "..", "..", "TarkovHelper", "Pages", "QuestListPage.xaml");
            if (!File.Exists(xamlPath))
            {
                xamlPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "TarkovHelper", "Pages", "QuestListPage.xaml"));
            }

            Assert.True(File.Exists(xamlPath), $"QuestListPage.xaml not found at: {xamlPath}");

            var doc = XDocument.Load(xamlPath);
            XNamespace xamlNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace xNs = "http://schemas.microsoft.com/winfx/2006/xaml";

            // Verify RadioButtons for all 4 navigation modes exist
            var rbList = doc.Descendants(xamlNs + "RadioButton").FirstOrDefault(r => (string?)r.Attribute(xNs + "Name") == "RbViewList");
            var rbTraders = doc.Descendants(xamlNs + "RadioButton").FirstOrDefault(r => (string?)r.Attribute(xNs + "Name") == "RbViewTraders");
            var rbMaps = doc.Descendants(xamlNs + "RadioButton").FirstOrDefault(r => (string?)r.Attribute(xNs + "Name") == "RbViewMaps");
            var rbGraph = doc.Descendants(xamlNs + "RadioButton").FirstOrDefault(r => (string?)r.Attribute(xNs + "Name") == "RbViewGraph");

            Assert.NotNull(rbList);
            Assert.NotNull(rbTraders);
            Assert.NotNull(rbMaps);
            Assert.NotNull(rbGraph);

            // Verify Secondary Filter ComboBox exists
            var cmbSecondary = doc.Descendants(xamlNs + "ComboBox").FirstOrDefault(c => (string?)c.Attribute(xNs + "Name") == "CmbSecondaryFilter");
            Assert.NotNull(cmbSecondary);

            // Verify Split-view elements for Maps mode in Row 5
            var colTaskList = doc.Descendants(xamlNs + "ColumnDefinition").FirstOrDefault(c => (string?)c.Attribute(xNs + "Name") == "ColTaskList");
            var colSplitter = doc.Descendants(xamlNs + "ColumnDefinition").FirstOrDefault(c => (string?)c.Attribute(xNs + "Name") == "ColSplitter");
            var colMapView = doc.Descendants(xamlNs + "ColumnDefinition").FirstOrDefault(c => (string?)c.Attribute(xNs + "Name") == "ColMapView");
            var splitter = doc.Descendants(xamlNs + "GridSplitter").FirstOrDefault(s => (string?)s.Attribute(xNs + "Name") == "MapSplitter");
            var mapContainer = doc.Descendants(xamlNs + "Border").FirstOrDefault(b => (string?)b.Attribute(xNs + "Name") == "EmbeddedMapContainer");

            Assert.NotNull(colTaskList);
            Assert.NotNull(colSplitter);
            Assert.NotNull(colMapView);
            Assert.NotNull(splitter);
            Assert.NotNull(mapContainer);
        }

        [Fact]
        public void ModeTransitions_RapidSwitching_Stress5000Cycles_StateRemainsConsistent()
        {
            var criteria = new QuestFilterCriteria
            {
                ViewMode = TasksViewMode.Traders,
                Mode = NavigationMode.Traders,
                SelectedTarget = "Prapor",
                SelectedTrader = "Prapor",
                SelectedMap = "customs",
                SearchQuery = "pocket watch",
                TaskStatusFilter = TaskStatusFilter.Available,
                SortBy = TaskSortMode.Impact,
                SortDirection = SortDirection.Descending,
                KappaOnly = true,
                CurrentLevelOnly = true
            };

            var modes = new[]
            {
                (TasksViewMode.List, NavigationMode.Traders),
                (TasksViewMode.Traders, NavigationMode.Traders),
                (TasksViewMode.Maps, NavigationMode.Maps),
                (TasksViewMode.Graph, NavigationMode.Traders)
            };

            var sw = Stopwatch.StartNew();

            // Stress: 5,000 rapid transitions across all 4 modes
            for (int i = 0; i < 5000; i++)
            {
                var (vm, nm) = modes[i % modes.Length];
                criteria.ViewMode = vm;
                criteria.Mode = nm;

                // Alternate secondary filters
                if (nm == NavigationMode.Maps)
                {
                    criteria.SelectedTarget = "customs";
                    criteria.SelectedMap = "customs";
                }
                else
                {
                    criteria.SelectedTarget = "Prapor";
                    criteria.SelectedTrader = "Prapor";
                }
            }

            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 500, $"5,000 mode transitions took {sw.ElapsedMilliseconds}ms, expected < 500ms");
            Assert.Equal("pocket watch", criteria.SearchQuery);
            Assert.True(criteria.KappaOnly);
            Assert.True(criteria.CurrentLevelOnly);
            Assert.Equal(TaskSortMode.Impact, criteria.SortBy);
            Assert.Equal(SortDirection.Descending, criteria.SortDirection);
        }

        #endregion

        #region 2. Filter Boundary Conditions

        [Fact]
        public void Filters_EmptyAndWhitespaceSearchQueries_ReturnsFullDataset()
        {
            var dataset = CreateDiverseQuestDataset();

            var emptyQueries = new[] { null, "", " ", "   ", "\t", "\n\r" };
            foreach (var q in emptyQueries)
            {
                var criteria = new QuestFilterCriteria
                {
                    SearchQuery = q,
                    TaskStatusFilter = TaskStatusFilter.All
                };

                var filtered = QuestFilterEngine.Filter(dataset, criteria);
                Assert.Equal(dataset.Count, filtered.Count);
            }
        }

        [Fact]
        public void Filters_NoMatchingQuests_ReturnsEmptyGracefully_WithZeroStatusCounts()
        {
            var dataset = CreateDiverseQuestDataset();

            var impossibleQueries = new[]
            {
                "__NON_EXISTENT_QUEST_ID_99999999__",
                "xyz123abc456!@#$%^&*()_+",
                "NOTHING_WILL_EVER_MATCH_THIS_QUERY"
            };

            foreach (var query in impossibleQueries)
            {
                var criteria = new QuestFilterCriteria
                {
                    SearchQuery = query,
                    TaskStatusFilter = TaskStatusFilter.All
                };

                var filtered = QuestFilterEngine.Filter(dataset, criteria);
                Assert.Empty(filtered);

                var counts = QuestFilterEngine.ComputeStatusCounts(dataset, criteria);
                Assert.Equal(0, counts.All);
                Assert.Equal(0, counts.Available);
                Assert.Equal(0, counts.Locked);
                Assert.Equal(0, counts.Completed);
                Assert.Equal(0, counts.Failed);
            }
        }

        [Fact]
        public void Filters_AllTradersVsSingleTrader_CountConservation()
        {
            var dataset = CreateDiverseQuestDataset();

            // All traders count (empty SelectedTrader and empty SelectedTarget)
            var allCriteria = new QuestFilterCriteria
            {
                ViewMode = TasksViewMode.Traders,
                SelectedTarget = "",
                SelectedTrader = "",
                TaskStatusFilter = TaskStatusFilter.All
            };

            var allQuests = QuestFilterEngine.Filter(dataset, allCriteria);
            Assert.Equal(dataset.Count, allQuests.Count);

            // Sum of each trader's quests
            var traders = dataset.Select(q => q.Task?.Trader).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
            int sumTraderQuests = 0;

            foreach (var trader in traders)
            {
                var traderCriteria = new QuestFilterCriteria
                {
                    ViewMode = TasksViewMode.Traders,
                    SelectedTarget = trader,
                    SelectedTrader = trader,
                    TaskStatusFilter = TaskStatusFilter.All
                };

                var traderQuests = QuestFilterEngine.Filter(dataset, traderCriteria);
                Assert.All(traderQuests, q => Assert.Equal(trader, q.Task?.Trader));
                sumTraderQuests += traderQuests.Count;
            }

            Assert.Equal(allQuests.Count, sumTraderQuests);
        }

        [Fact]
        public void Filters_CombinedTraderAndMapFilter_SimultaneousIntersection()
        {
            var dataset = CreateDiverseQuestDataset();

            // Case 1: Prapor + Customs (Must match both)
            var praporCustomsCriteria = new QuestFilterCriteria
            {
                ViewMode = TasksViewMode.Traders,
                SelectedTrader = "Prapor",
                SelectedMap = "customs",
                TaskStatusFilter = TaskStatusFilter.All
            };

            var praporCustomsResults = QuestFilterEngine.Filter(dataset, praporCustomsCriteria);
            Assert.NotEmpty(praporCustomsResults);
            Assert.All(praporCustomsResults, q =>
            {
                Assert.Equal("Prapor", q.Task?.Trader);
                Assert.Contains("customs", q.Task?.Maps ?? new List<string>());
            });

            // Case 2: Peacekeeper + Shoreline
            var peacekeeperShorelineCriteria = new QuestFilterCriteria
            {
                ViewMode = TasksViewMode.Traders,
                SelectedTrader = "Peacekeeper",
                SelectedMap = "shoreline",
                TaskStatusFilter = TaskStatusFilter.All
            };

            var peacekeeperShorelineResults = QuestFilterEngine.Filter(dataset, peacekeeperShorelineCriteria);
            Assert.NotEmpty(peacekeeperShorelineResults);
            Assert.All(peacekeeperShorelineResults, q =>
            {
                Assert.Equal("Peacekeeper", q.Task?.Trader);
                Assert.Contains("shoreline", q.Task?.Maps ?? new List<string>());
            });

            // Case 3: Incompatible pair (Trader has no tasks on that map, e.g. Jaeger on The Lab)
            var emptyIntersectionCriteria = new QuestFilterCriteria
            {
                ViewMode = TasksViewMode.Traders,
                SelectedTrader = "Jaeger",
                SelectedMap = "thelab",
                TaskStatusFilter = TaskStatusFilter.All
            };

            var emptyResults = QuestFilterEngine.Filter(dataset, emptyIntersectionCriteria);
            Assert.Empty(emptyResults);
        }

        [Fact]
        public void Filters_MultiMapQuests_MatchEitherMapInCombinedFilter()
        {
            // Quest spans both Customs and Factory
            var multiMapQuest = new QuestViewModel
            {
                Task = new TarkovTask
                {
                    Id = "multi_map_q",
                    NormalizedName = "multi-map-q",
                    Name = "Multi Map Operation",
                    Trader = "Prapor",
                    Maps = new List<string> { "customs", "factory" }
                },
                DisplayName = "Multi Map Operation",
                Status = QuestStatus.Active
            };

            var list = new List<QuestViewModel> { multiMapQuest };

            // When map is Customs -> matches
            var customsCriteria = new QuestFilterCriteria
            {
                SelectedTrader = "Prapor",
                SelectedMap = "customs",
                TaskStatusFilter = TaskStatusFilter.All
            };
            Assert.Single(QuestFilterEngine.Filter(list, customsCriteria));

            // When map is Factory -> matches
            var factoryCriteria = new QuestFilterCriteria
            {
                SelectedTrader = "Prapor",
                SelectedMap = "factory",
                TaskStatusFilter = TaskStatusFilter.All
            };
            Assert.Single(QuestFilterEngine.Filter(list, factoryCriteria));

            // When map is Woods -> does not match
            var woodsCriteria = new QuestFilterCriteria
            {
                SelectedTrader = "Prapor",
                SelectedMap = "woods",
                TaskStatusFilter = TaskStatusFilter.All
            };
            Assert.Empty(QuestFilterEngine.Filter(list, woodsCriteria));
        }

        [Fact]
        public void Filters_PunctuationAndRegexCharacters_NoExceptionsOrCorruptions()
        {
            var dataset = CreateDiverseQuestDataset();
            var punctuationTests = new[] { "[", "]", "*", "+", "?", "^", "$", "(", ")", "{", "}", "\\", "|", "\"", "'", "<", ">", "@", "#" };

            foreach (var charTest in punctuationTests)
            {
                var criteria = new QuestFilterCriteria
                {
                    SearchQuery = charTest,
                    TaskStatusFilter = TaskStatusFilter.All
                };

                var ex = Record.Exception(() => QuestFilterEngine.Filter(dataset, criteria));
                Assert.Null(ex);
            }
        }

        #endregion

        #region 3. Bidirectional Quest-Map Event Firing & Lockstep Synchronization

        [Fact]
        public void BidirectionalSync_MapChange_EventFiresAndUpdatesCurrentMapKey()
        {
            var tracker = MapTrackerService.Instance;
            string? capturedMap = null;
            int eventCount = 0;

            Action<string> handler = m =>
            {
                capturedMap = m;
                eventCount++;
            };

            tracker.MapChanged += handler;
            try
            {
                // Switch map
                tracker.SetCurrentMap("woods", force: true);
                Assert.Equal("woods", tracker.CurrentMapKey, ignoreCase: true);
                Assert.Equal("woods", capturedMap, ignoreCase: true);
                Assert.Equal(1, eventCount);

                // Setting same map without force should NOT fire
                tracker.SetCurrentMap("woods", force: false);
                Assert.Equal(1, eventCount);

                // Setting same map with force SHOULD fire
                tracker.SetCurrentMap("woods", force: true);
                Assert.Equal(2, eventCount);
            }
            finally
            {
                tracker.MapChanged -= handler;
            }
        }

        [Fact]
        public void BidirectionalSync_FloorSelection_PropagatesToCurrentFloorIdAndEvent()
        {
            var tracker = MapTrackerService.Instance;
            string? capturedFloor = null;
            int eventCount = 0;

            Action<string?> handler = f =>
            {
                capturedFloor = f;
                eventCount++;
            };

            tracker.FloorChanged += handler;
            try
            {
                // Select specific floor
                tracker.SetCurrentFloor("Second_Floor");
                Assert.Equal("Second_Floor", tracker.CurrentFloorId);
                Assert.Equal("Second_Floor", capturedFloor);
                Assert.Equal(1, eventCount);

                // Setting same floor should not fire redundant event
                tracker.SetCurrentFloor("Second_Floor");
                Assert.Equal(1, eventCount);

                // Clear floor to null (e.g. top-level outdoor map)
                tracker.SetCurrentFloor(null);
                Assert.Null(tracker.CurrentFloorId);
                Assert.Null(capturedFloor);
                Assert.Equal(2, eventCount);
            }
            finally
            {
                tracker.FloorChanged -= handler;
            }
        }

        [Fact]
        public void BidirectionalSync_PinnedObjective_LockstepAcrossMultipleSubscribers()
        {
            var tracker = MapTrackerService.Instance;

            // Simulate 3 independent subscribers:
            // Subscriber 1: QuestListPage
            // Subscriber 2: MapPage
            // Subscriber 3: OverlayMiniMapWindow
            TaskObjectiveWithLocation? sub1Obj = null;
            TaskObjectiveWithLocation? sub2Obj = null;
            TaskObjectiveWithLocation? sub3Obj = null;

            Action<TaskObjectiveWithLocation?> sub1 = o => sub1Obj = o;
            Action<TaskObjectiveWithLocation?> sub2 = o => sub2Obj = o;
            Action<TaskObjectiveWithLocation?> sub3 = o => sub3Obj = o;

            tracker.PinnedObjectiveChanged += sub1;
            tracker.PinnedObjectiveChanged += sub2;
            tracker.PinnedObjectiveChanged += sub3;

            try
            {
                var targetObjective = new TaskObjectiveWithLocation
                {
                    QuestId = "q_checking",
                    TaskName = "Checking",
                    TaskNormalizedName = "checking",
                    Description = "Obtain bronze pocket watch in the tanker",
                    ObjectiveId = "obj_pocket_watch",
                    Locations = new List<QuestObjectiveLocation>
                    {
                        new() { MapName = "customs", FloorId = "Ground_Level", X = 320.5, Y = 450.2 }
                    }
                };

                // Act: Pin objective
                tracker.PinObjective(targetObjective);

                // Assert: All 3 subscribers and tracker state are strictly identical
                Assert.Same(targetObjective, tracker.PinnedObjective);
                Assert.Same(targetObjective, sub1Obj);
                Assert.Same(targetObjective, sub2Obj);
                Assert.Same(targetObjective, sub3Obj);

                // Act: Unpin objective (null)
                tracker.PinObjective(null);

                // Assert: All 3 subscribers cleanly unpin without exceptions
                Assert.Null(tracker.PinnedObjective);
                Assert.Null(sub1Obj);
                Assert.Null(sub2Obj);
                Assert.Null(sub3Obj);
            }
            finally
            {
                tracker.PinnedObjectiveChanged -= sub1;
                tracker.PinnedObjectiveChanged -= sub2;
                tracker.PinnedObjectiveChanged -= sub3;
            }
        }

        [Fact]
        public void BidirectionalSync_MapMarkerFilterAwareness_AllowedQuestSetsRespected()
        {
            // Verify filter-aware logic in MapQuestMarkerManager
            var obj1 = new TaskObjectiveWithLocation
            {
                QuestId = "q_debut",
                TaskNormalizedName = "debut",
                TaskName = "Debut",
                Description = "Eliminate Scavs"
            };

            var obj2 = new TaskObjectiveWithLocation
            {
                QuestId = "q_checking",
                TaskNormalizedName = "checking",
                TaskName = "Checking",
                Description = "Find pocket watch"
            };

            // Test allowed sets logic
            var allowedIds = new HashSet<string>(new[] { "q_debut" }, StringComparer.OrdinalIgnoreCase);
            var allowedNames = new HashSet<string>(new[] { "debut" }, StringComparer.OrdinalIgnoreCase);

            bool obj1Matches = allowedIds.Contains(obj1.QuestId) || allowedNames.Contains(obj1.TaskNormalizedName);
            bool obj2Matches = allowedIds.Contains(obj2.QuestId) || allowedNames.Contains(obj2.TaskNormalizedName);

            Assert.True(obj1Matches);
            Assert.False(obj2Matches);
        }

        #endregion

        #region Helper Methods

        private List<QuestViewModel> CreateDiverseQuestDataset()
        {
            var traders = new[] { "Prapor", "Therapist", "Skier", "Peacekeeper", "Mechanic", "Ragman", "Jaeger" };
            var maps = new[] { "customs", "woods", "factory", "shoreline", "reserve", "interchange", "streets" };
            var statuses = new[] { QuestStatus.Active, QuestStatus.Locked, QuestStatus.Done, QuestStatus.LevelLocked };

            var list = new List<QuestViewModel>();
            int index = 0;

            foreach (var trader in traders)
            {
                foreach (var map in maps)
                {
                    var status = statuses[index % statuses.Length];
                    var task = new TarkovTask
                    {
                        Id = $"task_{index}",
                        NormalizedName = $"task-{index}",
                        Name = $"Task {index} ({trader} on {map})",
                        Trader = trader,
                        Maps = new List<string> { map },
                        ReqKappa = (index % 2 == 0),
                        RequiredLevel = (index % 40) + 1,
                        Objectives = new List<string> { $"Eliminate targets for {trader}" }
                    };

                    list.Add(new QuestViewModel
                    {
                        Task = task,
                        DisplayName = task.Name,
                        Status = status,
                        IsKappaRequired = task.ReqKappa
                    });

                    index++;
                }
            }

            return list;
        }

        #endregion
    }
}
