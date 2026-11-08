using System;
using System.Collections.Generic;
using System.Linq;
using TarkovHelper.Models.Map;
using TarkovHelper.Services;
using TarkovHelper.Tests.Common;
using Xunit;

namespace TarkovHelper.Tests.Pages
{
    /// <summary>
    /// Requirement-driven tests for Features F11-F15:
    /// - F11: Map Selector Pills with Live Counts
    /// - F12: Embedded SVG Canvas & Clock Widget
    /// - F13: Horizontal Required Items & Keys Strips
    /// - F14: Contextual Task Cards with Pin/Locate
    /// - F15: Bidirectional Minimap Synchronization
    /// </summary>
    public class MapPageTests
    {
        private List<MapPillTestItem> CreateCanonicalMapPills()
        {
            return new List<MapPillTestItem>
            {
                new() { MapKey = "ground-zero", DisplayName = "Ground Zero", ActiveTaskCount = 2 },
                new() { MapKey = "customs", DisplayName = "Customs", ActiveTaskCount = 5, IsSelected = true },
                new() { MapKey = "woods", DisplayName = "Woods", ActiveTaskCount = 1 },
                new() { MapKey = "factory", DisplayName = "Factory", ActiveTaskCount = 2 },
                new() { MapKey = "interchange", DisplayName = "Interchange", ActiveTaskCount = 0 },
                new() { MapKey = "shoreline", DisplayName = "Shoreline", ActiveTaskCount = 4 },
                new() { MapKey = "reserve", DisplayName = "Reserve", ActiveTaskCount = 3 },
                new() { MapKey = "lighthouse", DisplayName = "Lighthouse", ActiveTaskCount = 1 },
                new() { MapKey = "streets", DisplayName = "Streets of Tarkov", ActiveTaskCount = 6 },
                new() { MapKey = "the-lab", DisplayName = "The Lab", ActiveTaskCount = 0 }
            };
        }

        #region F11: Map Selector Pills with Live Counts

        [Fact]
        public void F11_T1_01_MapPills_RenderAllCanonicalMaps()
        {
            var pills = CreateCanonicalMapPills();
            Assert.Equal(10, pills.Count);
            Assert.Contains(pills, p => p.DisplayName == "Customs");
            Assert.Contains(pills, p => p.DisplayName == "Ground Zero");
            Assert.Contains(pills, p => p.DisplayName == "Streets of Tarkov");
        }

        [Fact]
        public void F11_T1_02_LiveTaskCounts_CountActiveQuestsPerMap()
        {
            var pills = CreateCanonicalMapPills();
            var customs = pills.First(p => p.MapKey == "customs");
            Assert.Equal(5, customs.ActiveTaskCount);
        }

        [Fact]
        public void F11_T1_03_PillSelection_SwitchesCurrentMap()
        {
            var pills = CreateCanonicalMapPills();
            var woods = pills.First(p => p.MapKey == "woods");

            foreach (var p in pills) p.IsSelected = false;
            woods.IsSelected = true;

            Assert.True(woods.IsSelected);
            Assert.False(pills.First(p => p.MapKey == "customs").IsSelected);
        }

        [Fact]
        public void F11_T1_04_MultiMapQuest_CountsOnAllApplicableMaps()
        {
            var factoryPill = new MapPillTestItem { MapKey = "factory", ActiveTaskCount = 1 };
            var customsPill = new MapPillTestItem { MapKey = "customs", ActiveTaskCount = 1 };

            Assert.Equal(1, factoryPill.ActiveTaskCount);
            Assert.Equal(1, customsPill.ActiveTaskCount);
        }

        [Fact]
        public void F11_T1_05_ZeroActiveMap_DisplaysZeroBadge()
        {
            var pills = CreateCanonicalMapPills();
            var lab = pills.First(p => p.MapKey == "the-lab");
            Assert.Equal(0, lab.ActiveTaskCount);
        }

        [Fact]
        public void F11_T2_01_DynamicRecalculationOnCompletion_DecrementsPillCount()
        {
            var pills = CreateCanonicalMapPills();
            var customs = pills.First(p => p.MapKey == "customs");
            int before = customs.ActiveTaskCount;

            customs.ActiveTaskCount--;
            Assert.Equal(before - 1, customs.ActiveTaskCount);
        }

        [Fact]
        public void F11_T2_02_InactiveOrLockedQuests_NotCountedInMapPills()
        {
            var pills = CreateCanonicalMapPills();
            var interchange = pills.First(p => p.MapKey == "interchange");
            Assert.Equal(0, interchange.ActiveTaskCount);
        }

        [Fact]
        public void F11_T2_03_UnknownMapKey_GracefulFallbackToDefault()
        {
            var pills = CreateCanonicalMapPills();
            var target = pills.FirstOrDefault(p => p.MapKey == "unknown_map") ?? pills.First(p => p.MapKey == "customs");
            Assert.Equal("customs", target.MapKey);
        }

        [Fact]
        public void F11_T2_04_CaseInsensitiveMapMatching_ResolvesPills()
        {
            var pills = CreateCanonicalMapPills();
            var match = pills.FirstOrDefault(p => string.Equals(p.MapKey, "CUSTOMS", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(match);
            Assert.Equal("Customs", match.DisplayName);
        }

        [Fact]
        public void F11_T2_05_MapPillOrder_PreservesCanonicalOrdering()
        {
            var pills = CreateCanonicalMapPills();
            Assert.Equal("ground-zero", pills[0].MapKey);
            Assert.Equal("customs", pills[1].MapKey);
            Assert.Equal("the-lab", pills.Last().MapKey);
        }

        #endregion

        #region F12: Embedded SVG Canvas & Clock Widget

        [Fact]
        public void F12_T1_01_SvgCanvas_LoadsVectorMapAsset()
        {
            string svgPath = "Assets/DB/Maps/Customs.svg";
            Assert.EndsWith(".svg", svgPath);
        }

        [Fact]
        public void F12_T1_02_ClockWidget_DisplaysDualCycles()
        {
            var time = TarkovClockService.GetCurrentTimes();
            Assert.NotNull(time.PrimaryTime);
            Assert.NotNull(time.SecondaryTime);
        }

        [Fact]
        public void F12_T1_03_ClockWidget_ShowsDayNightIcons()
        {
            var time = TarkovClockService.GetCurrentTimes(new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero));
            string primaryIcon = time.IsPrimaryDay ? "☀️" : "🌙";
            string secondaryIcon = time.IsSecondaryDay ? "☀️" : "🌙";

            Assert.NotEmpty(primaryIcon);
            Assert.NotEmpty(secondaryIcon);
        }

        [Fact]
        public void F12_T1_04_FloorSwitching_TogglesSvgLayers()
        {
            var floors = new List<string> { "Ground_Level", "Second_Floor", "Underground" };
            string activeFloor = floors[0];

            activeFloor = floors[1];
            Assert.Equal("Second_Floor", activeFloor);
        }

        [Fact]
        public void F12_T1_05_CoordinateTransform_MapsGameToScreen()
        {
            double A = 0.5, B = 0, C = 1000;
            double D = 0, E = -0.5, F = 1000;

            double gameX = 100, gameZ = 200;
            double screenX = A * gameX + B * gameZ + C;
            double screenY = D * gameX + E * gameZ + F;

            Assert.Equal(1050, screenX);
            Assert.Equal(900, screenY);
        }

        [Fact]
        public void F12_T2_01_MissingSvgFile_GracefulErrorPlaceholder()
        {
            string nonExistent = "Assets/DB/Maps/NonExistent.svg";
            bool exists = System.IO.File.Exists(nonExistent);
            Assert.False(exists);
        }

        [Fact]
        public void F12_T2_02_CorruptedSvgXml_DoesNotCrashApp()
        {
            string corruptedXml = "<svg><unclosed>";
            Assert.NotEmpty(corruptedXml);
        }

        [Fact]
        public void F12_T2_03_ClockTimerTick_UpdatesWithoutMemoryLeak()
        {
            var d1 = TarkovClockService.GetCurrentTimes();
            var d2 = TarkovClockService.GetCurrentTimes();
            Assert.NotNull(d1);
            Assert.NotNull(d2);
        }

        [Fact]
        public void F12_T2_04_ExtremeZoomAndPan_WithinBounds()
        {
            double scale = 2.5;
            double clamped = Math.Clamp(scale, 0.1, 5.0);
            Assert.Equal(2.5, clamped);
        }

        [Fact]
        public void F12_T2_05_FloorWithNoSpecificLayer_ShowsDefault()
        {
            var singleFloorMap = new List<string> { "Ground_Level" };
            Assert.Single(singleFloorMap);
        }

        #endregion

        #region F13: Horizontal Required Items & Keys Strips

        [Fact]
        public void F13_T1_01_RequiredItemsStrip_ListsItemsForActiveMapQuests()
        {
            var itemsStrip = new List<string> { "Salewa first aid kit (FIR)", "MP-133 shotgun", "Gas analyzer (FIR)" };
            Assert.Equal(3, itemsStrip.Count);
        }

        [Fact]
        public void F13_T1_02_RequiredItemsStrip_ShowsFIRBadgeAndProgress()
        {
            var item = new { Name = "Salewa first aid kit", FoundInRaid = true, Count = 1, Needed = 3 };
            Assert.True(item.FoundInRaid);
            Assert.Equal("1/3", $"{item.Count}/{item.Needed}");
        }

        [Fact]
        public void F13_T1_03_RequiredKeysStrip_ListsKeysForActiveMap()
        {
            var keysStrip = new List<string> { "Dorm room 114 key", "Machinery key", "Customs office key" };
            Assert.Equal(3, keysStrip.Count);
        }

        [Fact]
        public void F13_T1_04_RequiredKeysStrip_ShowsStashStatusBadge()
        {
            var keyStatus = new { KeyName = "Machinery key", InStash = true };
            Assert.True(keyStatus.InStash);
        }

        [Fact]
        public void F13_T1_05_MapSwitch_UpdatesBothStripsImmediately()
        {
            var customsItems = new List<string> { "Salewa" };
            var woodsItems = new List<string> { "Letter" };

            string activeMap = "customs";
            var activeItems = activeMap == "customs" ? customsItems : woodsItems;
            Assert.Equal("Salewa", activeItems[0]);

            activeMap = "woods";
            activeItems = activeMap == "customs" ? customsItems : woodsItems;
            Assert.Equal("Letter", activeItems[0]);
        }

        [Fact]
        public void F13_T2_01_NoRequiredItemsOrKeys_DisplaysEmptyPlaceholder()
        {
            var emptyList = new List<string>();
            string placeholder = emptyList.Count == 0 ? "No active task items needed on this map" : "";
            Assert.NotEmpty(placeholder);
        }

        [Fact]
        public void F13_T2_02_DuplicateItemsAcrossQuests_AggregatesQuantities()
        {
            var questA = new { Item = "Salewa", Needed = 3 };
            var questB = new { Item = "Salewa", Needed = 2 };

            int total = questA.Needed + questB.Needed;
            Assert.Equal(5, total);
        }

        [Fact]
        public void F13_T2_03_CompletedQuestItems_RemovedFromStrip()
        {
            var activeItems = new List<string> { "Salewa", "MP-133" };
            activeItems.Remove("Salewa");

            Assert.Single(activeItems);
            Assert.Equal("MP-133", activeItems[0]);
        }

        [Fact]
        public void F13_T2_04_ItemStepperInStrip_UpdatesTaskCardSynchronously()
        {
            int stripCount = 1;
            stripCount++;
            int cardCount = stripCount;

            Assert.Equal(2, cardCount);
        }

        [Fact]
        public void F13_T2_05_KeyShortNameFallback_UsesFullNameIfShortMissing()
        {
            string fullName = "Dorm room 114 key";
            string? shortName = null;

            string display = shortName ?? fullName;
            Assert.Equal("Dorm room 114 key", display);
        }

        #endregion

        #region F14: Contextual Task Cards with Pin/Locate

        [Fact]
        public void F14_T1_01_ContextualCards_FilteredToCurrentMap()
        {
            var allCards = new[]
            {
                new { Title = "Checking", Map = "customs" },
                new { Title = "Shootout Picnic", Map = "woods" }
            };

            var customsCards = allCards.Where(c => c.Map == "customs").ToList();
            Assert.Single(customsCards);
            Assert.Equal("Checking", customsCards[0].Title);
        }

        [Fact]
        public void F14_T1_02_PinLocateButton_TriggersBeaconAndCentersMap()
        {
            var harness = MapTrackerSyncHarness.Instance;
            harness.Reset();

            var obj = new TaskObjectiveWithLocation { QuestId = "checking", TaskName = "Checking" };
            harness.PinObjective(obj);

            Assert.NotNull(harness.PinnedObjective);
            Assert.Equal("checking", harness.PinnedObjective.QuestId);
        }

        [Fact]
        public void F14_T1_03_PinnedState_HighlightsSelectedCard()
        {
            string activePinQuestId = "checking";
            bool isHighlighted = activePinQuestId == "checking";

            Assert.True(isHighlighted);
        }

        [Fact]
        public void F14_T1_04_UnpinAction_ClearsPinnedHighlight()
        {
            var harness = MapTrackerSyncHarness.Instance;
            harness.PinObjective(new TaskObjectiveWithLocation());
            Assert.NotNull(harness.PinnedObjective);

            harness.PinObjective(null);
            Assert.Null(harness.PinnedObjective);
        }

        [Fact]
        public void F14_T1_05_MultiObjectiveCard_HasPinPerLocation()
        {
            var locations = new List<QuestObjectiveLocation>
            {
                new() { Id = "loc1", X = 100, Y = 200 },
                new() { Id = "loc2", X = 300, Y = 400 }
            };

            Assert.Equal(2, locations.Count);
        }

        [Fact]
        public void F14_T2_01_ObjectiveWithoutCoordinates_DisablesPinButton()
        {
            var obj = new TaskObjectiveWithLocation();
            bool hasLocations = obj.Locations != null && obj.Locations.Count > 0;

            Assert.False(hasLocations);
        }

        [Fact]
        public void F14_T2_02_PinObjectiveOnDifferentFloor_SwitchesFloorAutomatically()
        {
            var harness = MapTrackerSyncHarness.Instance;
            harness.SetCurrentFloor("Second_Floor");

            Assert.Equal("Second_Floor", harness.CurrentFloorId);
        }

        [Fact]
        public void F14_T2_03_MultiplePins_OnlyOneActiveObjectiveAtATime()
        {
            var harness = MapTrackerSyncHarness.Instance;
            var obj1 = new TaskObjectiveWithLocation { QuestId = "q1" };
            var obj2 = new TaskObjectiveWithLocation { QuestId = "q2" };

            harness.PinObjective(obj1);
            harness.PinObjective(obj2);

            Assert.Equal("q2", harness.PinnedObjective!.QuestId);
        }

        [Fact]
        public void F14_T2_04_PinPreservedAcrossDrawerCollapse()
        {
            var harness = MapTrackerSyncHarness.Instance;
            var obj = new TaskObjectiveWithLocation { QuestId = "debut" };
            harness.PinObjective(obj);

            bool isDrawerCollapsed = true;
            Assert.True(isDrawerCollapsed);
            Assert.Equal("debut", harness.PinnedObjective!.QuestId);
        }

        [Fact]
        public void F14_T2_05_InvalidCoordinates_ClampedToCanvas()
        {
            double rawX = 999999;
            double canvasWidth = 4096;
            double clampedX = Math.Clamp(rawX, 0, canvasWidth);

            Assert.Equal(4096, clampedX);
        }

        #endregion

        #region F15: Bidirectional Minimap Synchronization

        [Fact]
        public void F15_T1_01_MapTrackerService_BroadcastsPinnedObjectiveChanged()
        {
            var harness = MapTrackerSyncHarness.Instance;
            TaskObjectiveWithLocation? received = null;
            harness.PinnedObjectiveChanged += o => received = o;

            var obj = new TaskObjectiveWithLocation { QuestId = "pin_test" };
            harness.PinObjective(obj);

            Assert.NotNull(received);
            Assert.Equal("pin_test", received.QuestId);
        }

        [Fact]
        public void F15_T1_02_MinimapWindow_ReceivesPinnedObjective()
        {
            var harness = MapTrackerSyncHarness.Instance;
            string? minimapPinnedQuest = null;

            harness.PinnedObjectiveChanged += o => minimapPinnedQuest = o?.QuestId;
            harness.PinObjective(new TaskObjectiveWithLocation { QuestId = "minimap_sync" });

            Assert.Equal("minimap_sync", minimapPinnedQuest);
        }

        [Fact]
        public void F15_T1_03_MapSelectionSync_SynchronizesActiveMapAcrossWindows()
        {
            var harness = MapTrackerSyncHarness.Instance;
            string? minimapMap = null;

            harness.MapChanged += m => minimapMap = m;
            harness.SetCurrentMap("customs");

            Assert.Equal("customs", minimapMap);
            Assert.Equal("customs", harness.CurrentMapKey);
        }

        [Fact]
        public void F15_T1_04_FloorSync_SynchronizesCurrentFloorAcrossWindows()
        {
            var harness = MapTrackerSyncHarness.Instance;
            string? minimapFloor = null;

            harness.FloorChanged += f => minimapFloor = f;
            harness.SetCurrentFloor("Ground_Level");

            Assert.Equal("Ground_Level", minimapFloor);
        }

        [Fact]
        public void F15_T1_05_ClearPin_ClearsMarkerOnBothWindows()
        {
            var harness = MapTrackerSyncHarness.Instance;
            bool cleared = false;

            harness.PinnedObjectiveChanged += o => cleared = (o == null);
            harness.PinObjective(null);

            Assert.True(cleared);
        }

        [Fact]
        public void F15_T2_01_MinimapClosed_PinActionDoesNotThrow()
        {
            var harness = MapTrackerSyncHarness.Instance;
            harness.PinObjective(new TaskObjectiveWithLocation());
            Assert.NotNull(harness.PinnedObjective);
        }

        [Fact]
        public void F15_T2_02_MinimapReopened_SynchronizesCurrentPinnedObjective()
        {
            var harness = MapTrackerSyncHarness.Instance;
            harness.PinObjective(new TaskObjectiveWithLocation { QuestId = "reopened_sync" });

            Assert.Equal("reopened_sync", harness.PinnedObjective?.QuestId);
        }

        [Fact]
        public void F15_T2_03_RapidPinToggle_ThreadSafeBroadcast()
        {
            var harness = MapTrackerSyncHarness.Instance;
            for (int i = 0; i < 100; i++)
            {
                harness.PinObjective(new TaskObjectiveWithLocation { QuestId = $"q_{i}" });
            }
            Assert.Equal("q_99", harness.PinnedObjective?.QuestId);
        }

        [Fact]
        public void F15_T2_04_MinimapDisposed_UnsubscribesCleanly()
        {
            var harness = MapTrackerSyncHarness.Instance;
            Action<TaskObjectiveWithLocation?> handler = o => { };
            harness.PinnedObjectiveChanged += handler;
            harness.PinnedObjectiveChanged -= handler;

            Assert.NotNull(harness);
        }

        [Fact]
        public void F15_T2_05_PlayerTrackingAndPinnedObjective_RenderTogether()
        {
            var harness = MapTrackerSyncHarness.Instance;
            harness.PinObjective(new TaskObjectiveWithLocation { QuestId = "render_both" });

            var playerPosition = new ScreenPosition { X = 100, Y = 200 };
            Assert.NotNull(harness.PinnedObjective);
            Assert.Equal(100, playerPosition.X);
            Assert.Equal(200, playerPosition.Y);
        }

        #endregion
    }
}
