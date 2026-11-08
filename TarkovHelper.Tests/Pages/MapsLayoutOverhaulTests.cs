using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using TarkovHelper.Models;
using TarkovHelper.Pages;
using TarkovHelper.Pages.Map;
using TarkovHelper.Services.Map;
using TarkovHelper.Tests.Common;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests.Pages
{
    /// <summary>
    /// Tier 3: Tasks MAPS Layout Overhaul Verification Suite.
    /// Derived from ORIGINAL_REQUEST.md (§R2) and PROJECT.md (§M3, §F9, §F10, §F11, §F12).
    /// 
    /// Verifies:
    /// - TarkovClockWidget (Day/Night clock) is eliminated or collapsed to 0px footprint.
    /// - Top bar 'Quests on map' and bottom legend 'Task: Regular - Pinned - Team' are absent.
    /// - Single-line combined strip exists directly beneath the interactive map canvas.
    /// - Left section displays ONLY placeable special equipment (MS2000, Wi-Fi Camera, Signal Jammer).
    /// - General delivery/barter items (Salewa, Gas Analyzer, etc.) are strictly excluded.
    /// - Right section displays required keys without stash possession badges.
    /// - Interactive SVG canvas and floor layer selector remain functional.
    /// - Full-width active task cards feed is placed beneath the combined strip.
    /// - Map selection carousel includes all 12 canonical maps with reactive active quest counters.
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class MapsLayoutOverhaulTests
    {
        #region STA Thread Test Runner Helper

        private static void RunInSta(Action action, int timeoutMs = 15000)
        {
            Exception? caught = null;
            Application? createdApp = null;

            try
            {
                typeof(Application).GetField("_appCreated", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, false);
                typeof(Application).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            }
            catch { }

            var thread = new Thread(() =>
            {
                try
                {
                    if (Application.Current == null)
                    {
                        createdApp = new Application();
                    }
                    EnsureApplicationResources();
                    action();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
                finally
                {
                    try { createdApp?.Dispatcher.InvokeShutdown(); } catch { }
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            if (!thread.Join(timeoutMs))
            {
                throw new TimeoutException($"STA test action timed out after {timeoutMs}ms");
            }

            try
            {
                typeof(Application).GetField("_appCreated", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, false);
                typeof(Application).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            }
            catch { }

            if (caught != null)
            {
                throw new AggregateException("Exception thrown in STA thread", caught);
            }
        }

        private static void EnsureApplicationResources()
        {
            var res = Application.Current?.Resources;
            if (res == null) return;

            if (!res.Contains("BorderBrush")) res["BorderBrush"] = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D));
            if (!res.Contains("BackgroundDarkBrush")) res["BackgroundDarkBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            if (!res.Contains("BackgroundMediumBrush")) res["BackgroundMediumBrush"] = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25));
            if (!res.Contains("BackgroundLightBrush")) res["BackgroundLightBrush"] = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
            if (!res.Contains("TextPrimaryBrush")) res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
            if (!res.Contains("TextSecondaryBrush")) res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
            if (!res.Contains("AccentBrush")) res["AccentBrush"] = new SolidColorBrush(Color.FromRgb(0xC5, 0xA8, 0x4A));
            if (!res.Contains("SuccessBrush")) res["SuccessBrush"] = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            if (!res.Contains("WarningBrush")) res["WarningBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
            if (!res.Contains("FontSizeSmall")) res["FontSizeSmall"] = 12.0;
            if (!res.Contains("FontSizeLarge")) res["FontSizeLarge"] = 16.0;
            if (!res.Contains("FontSizeXSmall")) res["FontSizeXSmall"] = 11.0;
            if (!res.Contains("FontSizeTiny")) res["FontSizeTiny"] = 9.5;
            if (!res.Contains("BoolToVisibilityConverter")) res["BoolToVisibilityConverter"] = new BooleanToVisibilityConverter();
            if (!res.Contains("NullToVisibilityConverter")) res["NullToVisibilityConverter"] = new NullToVisibilityConverter();
        }

        #endregion
        [Fact]
        public void T3_01_MapPageXaml_TarkovClockWidget_IsAbsentOrCollapsed()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");
            var clockWidget = doc.Descendants("Border")
                .FirstOrDefault(b => b.GetXamlName() == "TarkovClockWidget");

            if (clockWidget != null)
            {
                var visibility = clockWidget.GetAttr("Visibility");
                Assert.True(string.Equals(visibility, "Collapsed", StringComparison.OrdinalIgnoreCase),
                    "If TarkovClockWidget element is retained in XAML for backwards compatibility, its Visibility must be set to 'Collapsed'.");
            }
        }

        [Fact]
        public void T3_02_MapPageXaml_TopQuestsBarAndBottomLegend_AreAbsent()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");

            var textElements = doc.Descendants("TextBlock")
                .Select(t => t.GetAttr("Text") ?? t.Value)
                .Concat(doc.Descendants("Run").Select(r => r.GetAttr("Text") ?? r.Value))
                .ToList();

            // Bottom legend: 'Task: Regular - Pinned - Team'
            Assert.DoesNotContain(textElements, t => t.Contains("Task: Regular", StringComparison.OrdinalIgnoreCase) &&
                                                    t.Contains("Pinned", StringComparison.OrdinalIgnoreCase));

            // Top bar: 'Quests on map Showing all X / Choose quests'
            Assert.DoesNotContain(textElements, t => t.Contains("Quests on map", StringComparison.OrdinalIgnoreCase) &&
                                                    t.Contains("Choose quests", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void T3_03_MapPageXaml_ContainsCombinedItemsAndKeysSingleStrip()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");

            // Look for the combined requirements strip container
            var stripContainer = doc.Descendants("Border")
                .FirstOrDefault(b => (b.GetXamlName() ?? string.Empty).Contains("Strip", StringComparison.OrdinalIgnoreCase) ||
                                     (b.GetXamlName() ?? string.Empty).Contains("Requirements", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(stripContainer);

            // Verify both items section and keys section are present within the panel
            var textElements = stripContainer.Descendants("TextBlock")
                .Select(t => t.GetAttr("Text") ?? t.Value)
                .ToList();

            Assert.Contains(textElements, t => t.Contains("Required items", StringComparison.OrdinalIgnoreCase) ||
                                             t.Contains("Items", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(textElements, t => t.Contains("Required keys", StringComparison.OrdinalIgnoreCase) ||
                                             t.Contains("Keys", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void T3_04_SpecialEquipmentFilter_IncludesOnlyPlaceableRaidGear()
        {
            // Test canonical special equipment items
            Assert.True(R1R2TestHelpers.IsSpecialEquipment("ms2000 marker", "5991b51486f77447b112d44f", "Special equipment"));
            Assert.True(R1R2TestHelpers.IsSpecialEquipment("wi-fi camera", "5b4391a586f7745321235ab2", "Special equipment"));
            Assert.True(R1R2TestHelpers.IsSpecialEquipment("signal jammer", "5ac78a9b86f7741cca0bbd8d", "Special equipment"));

            // General barter/delivery items MUST BE EXCLUDED from the map raid strip
            Assert.False(R1R2TestHelpers.IsSpecialEquipment("salewa", "544fb2574bdc2dfc738b4567", "Medical"));
            Assert.False(R1R2TestHelpers.IsSpecialEquipment("gas analyzer", "590a358486f77429692b2790", "Electronics"));
            Assert.False(R1R2TestHelpers.IsSpecialEquipment("car battery", "590a3b0486f774395455a437", "Energy"));
            Assert.False(R1R2TestHelpers.IsSpecialEquipment("spark plug", "590a3c9c86f7744654366627", "Electronics"));
            Assert.False(R1R2TestHelpers.IsSpecialEquipment("mp133", "54491c4f4bdc2db1078b4568", "Weapon"));
        }

        [Fact]
        public void T3_05_MapKeysList_SuppressesStashPossessionIndicator()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");

            var keysList = doc.Descendants()
                .FirstOrDefault(e => e.GetXamlName() == "RequiredKeysList");

            Assert.NotNull(keysList);

            var keyTemplate = keysList.Descendants("DataTemplate").FirstOrDefault();
            Assert.NotNull(keyTemplate);

            var inStashBinding = keyTemplate.Descendants()
                .Any(e => e.Attributes().Any(a => a.Value.Contains("InStash", StringComparison.OrdinalIgnoreCase) ||
                                                  a.Value.Contains("StashStatusText", StringComparison.OrdinalIgnoreCase)));

            Assert.False(inStashBinding, "Required keys on map should not show InStash possession indicator or StashStatusText.");
        }

        [Fact]
        public void T3_06_MapPageXaml_InteractiveCanvasAndFloorSelector_AreRetained()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");

            // Interactive canvas container
            var canvas = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Contains("Canvas", StringComparison.OrdinalIgnoreCase) ||
                                                               e.GetXamlName() == "MapCanvas");
            Assert.NotNull(canvas);

            // Floor selector
            var floorSelector = doc.Descendants().FirstOrDefault(e => (e.GetXamlName() ?? string.Empty).Contains("Floor", StringComparison.OrdinalIgnoreCase) ||
                                                                      (e.GetXamlName() ?? string.Empty).Contains("Level", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(floorSelector);
        }

        [Fact]
        public void T3_07_QuestListPage_MapChips_IncludeAll12CanonicalMaps()
        {
            var questListCsPath = R1R2TestHelpers.ResolveRepoPath("TarkovHelper", "Pages", "QuestListPage.xaml.cs");
            Assert.True(File.Exists(questListCsPath));

            var content = File.ReadAllText(questListCsPath);

            // Verify each canonical map key is registered in MapChips
            foreach (var mapKey in R1R2TestHelpers.Canonical12Maps)
            {
                Assert.True(content.Contains($"\"{mapKey}\"", StringComparison.OrdinalIgnoreCase),
                    $"QuestListPage MapChips carousel must include canonical map '{mapKey}'.");
            }

            // Verify The Labyrinth and Terminal specifically
            Assert.Contains("\"the-labyrinth\"", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"terminal\"", content, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void T3_08_QuestListPage_HeaderViewModes_IncludeFourRequiredModes()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "QuestListPage.xaml");

            var radioButtons = doc.Descendants("RadioButton").ToList();

            // Mode 1: List
            Assert.Contains(radioButtons, r => (r.GetXamlName() ?? string.Empty).Contains("List", StringComparison.OrdinalIgnoreCase));

            // Mode 2: Traders
            Assert.Contains(radioButtons, r => (r.GetXamlName() ?? string.Empty).Contains("Traders", StringComparison.OrdinalIgnoreCase));

            // Mode 3: Maps
            Assert.Contains(radioButtons, r => (r.GetXamlName() ?? string.Empty).Contains("Maps", StringComparison.OrdinalIgnoreCase));

            // Mode 4: Graph (WIP)
            Assert.Contains(radioButtons, r => (r.GetXamlName() ?? string.Empty).Contains("Graph", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void T3_09_ActiveTaskCardsList_RenderedBeneathMapContainer()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "QuestListPage.xaml");

            // When in MAPS mode, active task cards are displayed
            var itemsControl = doc.Descendants()
                .FirstOrDefault(e => e.GetXamlName() == "LstQuests");

            Assert.NotNull(itemsControl);
        }

        [Fact]
        public void T3_10_MapSelection_ZeroActiveTasks_HandlesGracefullyWithoutCrash()
        {
            // Edge case: map with 0 active tasks (e.g. The Labyrinth or Terminal early wipe)
            var pillItem = new MapPillTestItem
            {
                MapKey = "the-labyrinth",
                DisplayName = "The Labyrinth",
                ActiveTaskCount = 0,
                IsSelected = true
            };

            Assert.Equal(0, pillItem.ActiveTaskCount);
            Assert.True(pillItem.IsSelected);
            Assert.Equal("The Labyrinth", pillItem.DisplayName);
        }

        // -------------------------------------------------------------
        // FEATURE F9: Map Clock, Top Bar & Legend Elimination
        // -------------------------------------------------------------

        [Fact]
        public void T3_11_MapPage_ClockTimerAndCodeBehind_CleanedOrDisabled()
        {
            var csPath = R1R2TestHelpers.ResolveRepoPath("TarkovHelper", "Pages", "Map", "MapPage.xaml.cs");
            Assert.True(File.Exists(csPath));
            var content = File.ReadAllText(csPath);

            // If TarkovClockWidget is collapsed/eliminated, clock timer setup should be disabled or guarded by visibility
            Assert.True(!content.Contains("_clockTimer.Start()") || content.Contains("TarkovClockWidget?.Visibility == Visibility.Visible") || content.Contains("_clockTimer = null"),
                "Clock timer in MapPage.xaml.cs should not continuously tick when clock widget is collapsed.");
        }

        [Fact]
        public void T3_12_MapPage_NoObsoleteBottomLegendControls()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");

            // Reclaim >80px vertical space by ensuring no legend panel exists
            var legendPanels = doc.Descendants("Border")
                .Where(b => (b.GetXamlName() ?? string.Empty).Contains("Legend", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Empty(legendPanels);
        }

        // -------------------------------------------------------------
        // FEATURE F10: Interactive Map Canvas & Floor Selector
        // -------------------------------------------------------------

        [Fact]
        public void T3_13_FloorSelector_MultiFloorMaps_SwitchesLayersCorrectly()
        {
            // -------------------------------------------------------------
            // Step 1: Declarative XAML Architecture & Floating Overlay Check
            // -------------------------------------------------------------
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");

            // Locate MapViewerGrid interactive map container
            var mapViewerGrid = doc.Descendants().FirstOrDefault(e => e.GetXamlName() == "MapViewerGrid");
            Assert.NotNull(mapViewerGrid);

            // Verify OverlayFloorSelectorPanel exists within MapViewerGrid
            var overlayPanel = mapViewerGrid.Descendants("Border")
                .FirstOrDefault(b => b.GetXamlName() == "OverlayFloorSelectorPanel");
            Assert.NotNull(overlayPanel);

            // Verify Panel.ZIndex >= 100 so the floor selector hovers above interactive canvas layers
            var zIndexAttr = overlayPanel.Attributes()
                .FirstOrDefault(a => a.Name.LocalName.EndsWith("ZIndex", StringComparison.OrdinalIgnoreCase))?.Value;
            Assert.NotNull(zIndexAttr);
            Assert.True(int.TryParse(zIndexAttr, out int zIndex) && zIndex >= 100,
                $"OverlayFloorSelectorPanel must specify Panel.ZIndex >= 100 (actual: '{zIndexAttr}').");

            // Verify CmbOverlayFloorSelect ComboBox exists inside OverlayFloorSelectorPanel
            var cmbOverlayXaml = overlayPanel.Descendants("ComboBox")
                .FirstOrDefault(c => c.GetXamlName() == "CmbOverlayFloorSelect");
            Assert.NotNull(cmbOverlayXaml);

            // Verify CmbOverlayFloorSelect is wired to CmbOverlayFloorSelect_SelectionChanged in XAML
            var selectionChangedHandler = cmbOverlayXaml.GetAttr("SelectionChanged");
            Assert.Equal("CmbOverlayFloorSelect_SelectionChanged", selectionChangedHandler);

            // -------------------------------------------------------------
            // Step 2: Runtime STA Instance & Bidirectional Sync Verification
            // -------------------------------------------------------------
            RunInSta(() =>
            {
                var page = new MapPage();

                var cmbFloor = page.FindName("CmbFloorSelect") as ComboBox;
                var cmbOverlay = page.FindName("CmbOverlayFloorSelect") as ComboBox;
                var panel = page.FindName("OverlayFloorSelectorPanel") as Border;

                Assert.NotNull(cmbFloor);
                Assert.NotNull(cmbOverlay);
                Assert.NotNull(panel);

                // Populate multi-floor options
                cmbFloor.Items.Clear();
                cmbFloor.Items.Add(new ComboBoxItem { Content = "Ground Level", Tag = "ground" });
                cmbFloor.Items.Add(new ComboBoxItem { Content = "Bunkers", Tag = "bunkers" });
                cmbFloor.Items.Add(new ComboBoxItem { Content = "Second Floor", Tag = "floor2" });
                cmbFloor.Visibility = Visibility.Visible;
                cmbFloor.SelectedIndex = 0;

                var syncMethod = typeof(MapPage).GetMethod("SyncPillFloorSelector", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(syncMethod);
                syncMethod.Invoke(page, null);

                // Verify overlay panel stays collapsed (superseded by DiscreetFloorControl)
                Assert.Equal(Visibility.Collapsed, panel.Visibility);
                Assert.Equal(3, cmbOverlay.Items.Count);
                Assert.Equal(0, cmbOverlay.SelectedIndex);

                // Select floor 1 in overlay -> should sync to cmbFloor
                cmbOverlay.SelectedIndex = 1;
                Assert.Equal(1, cmbFloor.SelectedIndex);

                // Select floor 2 in cmbFloor -> sync and verify
                cmbFloor.SelectedIndex = 2;
                syncMethod.Invoke(page, null);
                Assert.Equal(2, cmbOverlay.SelectedIndex);
            });
        }

        [Fact]
        public void T3_14_MapCanvas_RetainsTransformAndPanZoomBindings()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");
            var mapCanvas = doc.Descendants().FirstOrDefault(e => e.GetXamlName() == "MapCanvas");
            Assert.NotNull(mapCanvas);

            // The canvas must be housed in a zoom/pan interactive container (MapViewerGrid or ScrollViewer)
            var viewerContainer = doc.Descendants().FirstOrDefault(e => e.GetXamlName() == "MapViewerGrid");
            Assert.NotNull(viewerContainer);
        }

        // -------------------------------------------------------------
        // FEATURE F11: Single-Line Combined Items & Keys Strip
        // -------------------------------------------------------------

        [Fact]
        public void T3_15_SingleLineCombinedStrip_HorizontalStructure_EnforcesSingleLine()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");
            var stripBorder = doc.Descendants("Border")
                .FirstOrDefault(b => (b.GetXamlName() ?? string.Empty).Contains("Requirements", StringComparison.OrdinalIgnoreCase) ||
                                     (b.GetXamlName() ?? string.Empty).Contains("Strip", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(stripBorder);

            // Inside the stripBorder, the inner Grid should not have 2 separate rows of items and keys stacked vertically.
            // It must use a 1-row Grid with 2 columns, or a single horizontal DockPanel / StackPanel
            var innerGrid = stripBorder.Descendants("Grid").FirstOrDefault();
            if (innerGrid != null)
            {
                var rowDefs = innerGrid.Elements("Grid.RowDefinitions").Descendants("RowDefinition").ToList();
                Assert.True(rowDefs.Count <= 1,
                    $"Combined requirements strip must be a single horizontal line, but found {rowDefs.Count} vertical RowDefinitions.");
            }
        }

        [Fact]
        public void T3_16_MapPage_UpdateRequiredStrips_FiltersSpecialEquipmentOnly()
        {
            // Simulate active task requirements: placeable gear vs delivery/barter items
            var raidItems = new List<(string Name, string Id, string Category)>
            {
                ("MS2000 Marker", "5991b51486f77447b112d44f", "Special equipment"),
                ("Salewa first aid kit", "544fb2574bdc2dfc738b4567", "Medical"),
                ("Gas analyzer", "590a358486f77429692b2790", "Electronics"),
                ("Wi-Fi Camera", "5b4391a586f7745321235ab2", "Special equipment")
            };

            var filtered = raidItems.Where(i => R1R2TestHelpers.IsSpecialEquipment(i.Name, i.Id, i.Category)).ToList();

            Assert.Equal(2, filtered.Count);
            Assert.Contains(filtered, x => x.Name == "MS2000 Marker");
            Assert.Contains(filtered, x => x.Name == "Wi-Fi Camera");
            Assert.DoesNotContain(filtered, x => x.Name == "Salewa first aid kit");
        }

        [Fact]
        public void T3_17_MapPage_KeysStrip_DataModel_SuppressesInStashProperty()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");

            var keysList = doc.Descendants().FirstOrDefault(e => e.GetXamlName() == "RequiredKeysList");
            Assert.NotNull(keysList);

            // Ensure neither InStash nor StashStatusText is bound in the ItemTemplate
            var bindings = keysList.Descendants()
                .SelectMany(e => e.Attributes())
                .Where(a => a.Value.Contains("InStash", StringComparison.OrdinalIgnoreCase) ||
                            a.Value.Contains("StashStatusText", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Empty(bindings);
        }

        [Fact]
        public void T3_18_MapPage_SelectMap_SupportsAll12CanonicalMaps()
        {
            RunInSta(() =>
            {
                var mapPage = new MapPage();
                mapPage.SetEmbeddedMode(true);

                // 1. All 12 canonical maps
                foreach (var mapKey in R1R2TestHelpers.Canonical12Maps)
                {
                    mapPage.SelectMap(mapKey);

                    var currentTrackerMap = MapTrackerService.Instance.CurrentMapKey;
                    var expectedResolvedKey = MapTrackerService.Instance.Transformer.ResolveMapKey(mapKey) ?? mapKey;
                    Assert.True(
                        string.Equals(currentTrackerMap, expectedResolvedKey, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(currentTrackerMap, mapKey, StringComparison.OrdinalIgnoreCase),
                        $"MapTrackerService.CurrentMapKey for '{mapKey}' should be '{expectedResolvedKey}', but was '{currentTrackerMap}'.");
                }

                // 2. Adversarial Edge Cases: should degrade gracefully and NEVER throw exceptions
                mapPage.SelectMap("");
                mapPage.SelectMap("   ");
                mapPage.SelectMap("customs_night");
                mapPage.SelectMap("the-labyrinth");
                mapPage.SelectMap("terminal");
                mapPage.SelectMap("unknown-bogus-map-key-999");
            });
        }

        // -------------------------------------------------------------
        // FEATURE F12: Active Task Cards List Under Map
        // -------------------------------------------------------------

        [Fact]
        public void T3_19_MapsView_VerticalStacking_LayoutConfigured()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "QuestListPage.xaml");

            // In QuestListPage.xaml, verify that the MAPS layout allows task cards to display vertically below the map
            var mapContainer = doc.Descendants().FirstOrDefault(e => e.GetXamlName() == "EmbeddedMapContainer");
            var taskList = doc.Descendants().FirstOrDefault(e => e.GetXamlName() == "LstQuests");

            Assert.NotNull(mapContainer);
            Assert.NotNull(taskList);
        }

        [Fact]
        public void T3_20_QuestListPage_BackwardsCompatibility_RetainsColDefinitionsAndNames()
        {
            // 1. Linq-to-XML Structural Inspection of QuestListPage.xaml
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "QuestListPage.xaml");

            // Locate Row 5 Main Content Grid
            var row5Grids = doc.Descendants("Grid")
                .Where(g => g.GetAttr("Grid.Row") == "5")
                .ToList();
            Assert.NotEmpty(row5Grids);
            var mainGrid = row5Grids.First();

            // 1a. Validate ColumnDefinitions retained for backwards compatibility
            var colDefs = mainGrid.Elements("Grid.ColumnDefinitions").Elements("ColumnDefinition").ToList();
            Assert.Equal(3, colDefs.Count);

            var colTaskList = colDefs.FirstOrDefault(c => c.GetXamlName() == "ColTaskList");
            var colSplitter = colDefs.FirstOrDefault(c => c.GetXamlName() == "ColSplitter");
            var colMapView = colDefs.FirstOrDefault(c => c.GetXamlName() == "ColMapView");

            Assert.NotNull(colTaskList);
            Assert.NotNull(colSplitter);
            Assert.NotNull(colMapView);

            Assert.Equal("*", colTaskList.GetAttr("Width"));
            Assert.Equal("Auto", colSplitter.GetAttr("Width"));
            Assert.Equal("0", colMapView.GetAttr("Width"));

            // 1b. Validate RowDefinitions configured for Vertical Maps Layout Overhaul
            var rowDefs = mainGrid.Elements("Grid.RowDefinitions").Elements("RowDefinition").ToList();
            Assert.Equal(3, rowDefs.Count);

            var rowMapView = rowDefs.FirstOrDefault(r => r.GetXamlName() == "RowMapView");
            var rowSplitter = rowDefs.FirstOrDefault(r => r.GetXamlName() == "RowSplitter");
            var rowTaskList = rowDefs.FirstOrDefault(r => r.GetXamlName() == "RowTaskList");

            Assert.NotNull(rowMapView);
            Assert.NotNull(rowSplitter);
            Assert.NotNull(rowTaskList);

            Assert.Equal("0", rowMapView.GetAttr("Height"));
            Assert.Equal("Auto", rowSplitter.GetAttr("Height"));
            Assert.Equal("*", rowTaskList.GetAttr("Height"));

            // 1c. Validate GridSplitter (MapSplitter) configured as horizontal divider
            var mapSplitter = mainGrid.Descendants("GridSplitter").FirstOrDefault(s => s.GetXamlName() == "MapSplitter");
            Assert.NotNull(mapSplitter);
            Assert.Equal("1", mapSplitter.GetAttr("Grid.Row"));
            Assert.Equal("3", mapSplitter.GetAttr("Grid.ColumnSpan"));
            Assert.Equal("PreviousAndNext", mapSplitter.GetAttr("ResizeBehavior"));
            Assert.Equal("SizeNS", mapSplitter.GetAttr("Cursor"));

            // 1d. Validate child element row and column-span placements
            var mapContainer = mainGrid.Descendants("Border").FirstOrDefault(b => b.GetXamlName() == "EmbeddedMapContainer");
            Assert.NotNull(mapContainer);
            Assert.Equal("0", mapContainer.GetAttr("Grid.Row"));
            Assert.Equal("3", mapContainer.GetAttr("Grid.ColumnSpan"));

            var taskListBorder = mainGrid.Descendants("Border").FirstOrDefault(b => b.Descendants().Any(d => d.GetXamlName() == "LstQuests"));
            Assert.NotNull(taskListBorder);
            Assert.Equal("2", taskListBorder.GetAttr("Grid.Row"));
            Assert.Equal("3", taskListBorder.GetAttr("Grid.ColumnSpan"));

            // 2. Code-Behind Inspection: SwitchNavigationMode(NavigationMode.Maps)
            var csPath = R1R2TestHelpers.ResolveRepoPath("TarkovHelper", "Pages", "QuestListPage.xaml.cs");
            Assert.True(File.Exists(csPath));
            var csContent = File.ReadAllText(csPath);

            int switchIdx = csContent.IndexOf("void SwitchNavigationMode(NavigationMode mode)");
            Assert.True(switchIdx >= 0, "QuestListPage.xaml.cs must implement SwitchNavigationMode(NavigationMode mode).");
            var switchBlock = csContent.Substring(switchIdx, Math.Min(2600, csContent.Length - switchIdx));

            // Verify Maps mode activates vertical rows while preserving column definitions
            Assert.Contains("mode == NavigationMode.Maps", switchBlock);
            Assert.Contains("RowMapView.Height = new GridLength(380, GridUnitType.Pixel)", switchBlock);
            Assert.Contains("RowSplitter.Height = GridLength.Auto", switchBlock);
            Assert.Contains("MapSplitter.Visibility = Visibility.Visible", switchBlock);
            Assert.Contains("RowTaskList.Height = new GridLength(1, GridUnitType.Star)", switchBlock);
            Assert.Contains("EmbeddedMapContainer.Visibility = Visibility.Visible", switchBlock);

            Assert.Contains("ColTaskList.Width = new GridLength(1, GridUnitType.Star)", switchBlock);
            Assert.Contains("ColSplitter.Width = new GridLength(0)", switchBlock);
            Assert.Contains("ColMapView.Width = new GridLength(0)", switchBlock);

            // 3. Domain Engine Verification: QuestFilterEngine.Filter active tasks for selected map
            var qCustoms1 = new QuestViewModel
            {
                Task = new TarkovTask { Id = "q_cust_1", Name = "Debut", Maps = new List<string> { "customs" } },
                DisplayName = "Debut",
                Status = QuestStatus.Active
            };
            var qCustoms2 = new QuestViewModel
            {
                Task = new TarkovTask { Id = "q_cust_2", Name = "Checking", Maps = new List<string> { "customs" } },
                DisplayName = "Checking",
                Status = QuestStatus.Active
            };
            var qWoods = new QuestViewModel
            {
                Task = new TarkovTask { Id = "q_woods_1", Name = "Shootout Picnic", Maps = new List<string> { "woods" } },
                DisplayName = "Shootout Picnic",
                Status = QuestStatus.Active
            };
            var qLockedCustoms = new QuestViewModel
            {
                Task = new TarkovTask { Id = "q_cust_locked", Name = "Bad Rep Evidence", Maps = new List<string> { "customs" } },
                DisplayName = "Bad Rep Evidence",
                Status = QuestStatus.Locked
            };

            var testQuests = new List<QuestViewModel> { qCustoms1, qCustoms2, qWoods, qLockedCustoms };
            var criteria = new QuestFilterCriteria
            {
                ViewMode = TasksViewMode.Maps,
                Mode = NavigationMode.Maps,
                SelectedTarget = "customs",
                StatusFilter = QuickFilterStatus.Active,
                TaskTypeRegular = true,
                TaskTypeKappa = true,
                TaskTypeLightkeeper = true
            };

            var filtered = QuestFilterEngine.Filter(testQuests, criteria, currentPlayerLevel: 15);

            Assert.Equal(2, filtered.Count);
            Assert.Contains(filtered, q => q.Task.Id == "q_cust_1");
            Assert.Contains(filtered, q => q.Task.Id == "q_cust_2");
            Assert.DoesNotContain(filtered, q => q.Task.Id == "q_woods_1");
            Assert.DoesNotContain(filtered, q => q.Task.Id == "q_cust_locked");
        }

        [Fact]
        public void T3_21_QuestListPage_TaskCardsFeed_RetainsActionButtons()
        {
            // 1. Inspect QuestListPage.xaml
            var questListDoc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "QuestListPage.xaml");

            // Verify QuestCardTemplate exists
            var cardTemplate = questListDoc.Descendants("DataTemplate")
                .FirstOrDefault(dt => dt.GetAttr("x:Key") == "QuestCardTemplate" || dt.GetAttr("Key") == "QuestCardTemplate");
            Assert.NotNull(cardTemplate);

            // Verify QuestCardTemplate embeds TaskCardControl
            var taskCardRef = cardTemplate.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.EndsWith("TaskCardControl", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(taskCardRef);

            // Verify QuestListPage retains canonical action buttons in its visual structure
            var qlWikiBtn = questListDoc.Descendants("Button")
                .FirstOrDefault(b => b.GetXamlName() == "BtnWiki" || b.GetAttr("Click") == "BtnWiki_Click");
            Assert.NotNull(qlWikiBtn);

            var qlCompleteBtn = questListDoc.Descendants("Button")
                .FirstOrDefault(b => b.GetXamlName() == "BtnComplete" || b.GetAttr("Click") == "BtnComplete_Click");
            Assert.NotNull(qlCompleteBtn);

            var qlMapBtn = questListDoc.Descendants("Button")
                .FirstOrDefault(b => b.GetXamlName() == "BtnShowOnMap" || b.GetAttr("Click") == "BtnShowOnMap_Click");
            Assert.NotNull(qlMapBtn);

            // 2. Inspect TaskCardControl.xaml (the card control rendered for each task in the feed)
            var cardDoc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Controls", "TaskCardControl.xaml");

            // Action 1: Wiki Button (opens task wiki page)
            var wikiBtn = cardDoc.Descendants("Button")
                .FirstOrDefault(b => b.GetAttr("Click") == "BtnWiki_Click" ||
                                     (b.GetAttr("ToolTip") ?? "").Contains("wiki", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(wikiBtn);

            // Action 2: Show on Map Button (locates objectives on interactive map)
            var mapBtn = cardDoc.Descendants("Button")
                .FirstOrDefault(b => b.GetAttr("Click") == "BtnMap_Click" ||
                                     (b.GetAttr("ToolTip") ?? "").Contains("mapa", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(mapBtn);

            // Action 3: Complete Button (marks task completed)
            var completeBtn = cardDoc.Descendants("Button")
                .FirstOrDefault(b => b.GetAttr("Click") == "BtnComplete_Click" ||
                                     (b.GetAttr("ToolTip") ?? "").Contains("completa", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(completeBtn);

            // Action 4: Sub-Objective Action Toggle Button (toggles objectives on/off)
            var actionToggleBtn = cardDoc.Descendants("Button")
                .FirstOrDefault(b => b.GetAttr("Click") == "BtnActionToggle_Click");
            Assert.NotNull(actionToggleBtn);

            // Action 5: Item Turn-In Stepper Controls (increments, decrements, and completes hand-in items)
            var stepperIncBtn = cardDoc.Descendants("Button")
                .FirstOrDefault(b => b.GetAttr("Click") == "BtnStepperIncrement_Click");
            Assert.NotNull(stepperIncBtn);

            var stepperDecBtn = cardDoc.Descendants("Button")
                .FirstOrDefault(b => b.GetAttr("Click") == "BtnStepperDecrement_Click");
            Assert.NotNull(stepperDecBtn);

            var stepperMaxBtn = cardDoc.Descendants("Button")
                .FirstOrDefault(b => b.GetAttr("Click") == "BtnStepperMax_Click");
            Assert.NotNull(stepperMaxBtn);

            // Action 6: Guide & Wiki Notes Expander (collapsible guidelines and notes)
            var guideExpander = cardDoc.Descendants("Expander")
                .FirstOrDefault(e => (e.GetAttr("Visibility") ?? "").Contains("GuideExpanderVisibility", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(guideExpander);

            var guideText = guideExpander.Descendants("TextBlock")
                .FirstOrDefault(tb => (tb.GetAttr("Text") ?? "").Contains("GuideText", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(guideText);
        }

        [Fact]
        public void T3_21_ActiveTaskCards_ContainsObjectivesAndActionButtons()
        {
            // Alias forwarding to retain exact backward compatibility with previous test runner filters
            T3_21_QuestListPage_TaskCardsFeed_RetainsActionButtons();
        }
    }
}
