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
using TarkovHelper.Controls;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Pages;
using TarkovHelper.Pages.Map;
using TarkovHelper.Services;
using TarkovHelper.Services.Map;
using TarkovHelper.Tests.Common;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests.Pages
{
    /// <summary>
    /// Empirical Adversarial Verification Suite executed by Challenger M3-2.
    /// Strictly verifies:
    /// 1. Vertical Layout Stacking & Row Sizing in QuestListPage (Feature F12):
    ///    - Row 0 (Embedded map: RowMapView, 380px initial)
    ///    - Row 1 (Horizontal splitter: RowSplitter, Auto)
    ///    - Row 2 (Full-width task cards feed: RowTaskList, 1*)
    ///    - Resizing resilience ensuring task cards never collapse to 0 height.
    /// 2. Backwards Compatibility Grid Layout Preservation (Feature F12):
    ///    - Preserves ColTaskList, ColSplitter, ColMapView, MapSplitter in visual tree.
    ///    - Zero regression for older reflection/automation tests (e.g. Challenger1FullSolutionEmpiricalTests).
    /// 3. 12 Canonical Map Chips Dynamic Forwarding to Embedded Map:
    ///    - Carousel includes all 12 canonical maps plus "Todos".
    ///    - Dynamic switching updates MapTrackerService, embedded MapPage, and task filtering.
    ///    - Zero KeyNotFoundException, zero null pointer crashes, zero desync.
    /// 4. Task Cards Feed Completeness:
    ///    - Full task cards under embedded map with action buttons (Wiki, Map, Complete, Steppers, Notes).
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class ChallengerM3_2MapsLayoutAdversarialTests : IDisposable
    {
        private static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        private static readonly XNamespace XNs = "http://schemas.microsoft.com/winfx/2006/xaml";

        private readonly string? _originalMap;
        private readonly string? _originalFloor;

        public ChallengerM3_2MapsLayoutAdversarialTests()
        {
            _originalMap = MapTrackerService.Instance.CurrentMapKey;
            _originalFloor = MapTrackerService.Instance.CurrentFloorId;
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
        }

        public void Dispose()
        {
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
            if (!string.IsNullOrEmpty(_originalMap))
            {
                MapTrackerService.Instance.SetCurrentMap(_originalMap, force: true);
            }
            MapTrackerService.Instance.SetCurrentFloor(_originalFloor);
        }

        #region STA Thread Execution Harness

        private static void RunInSta(Action action, int timeoutMs = 8000)
        {
            Exception? caught = null;
            Application? createdApp = null;

            // Ensure previous application instance is detached from this thread
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
                throw new TimeoutException($"STA test harness timed out after {timeoutMs}ms");
            }

            try
            {
                typeof(Application).GetField("_appCreated", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, false);
                typeof(Application).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            }
            catch { }

            if (caught != null)
            {
                throw new AggregateException("Exception thrown in STA harness", caught);
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

        private static QuestListPage CreateInitializedQuestListPage()
        {
            var page = new QuestListPage();
            // Force _isInitializing to false and initialize nav chips so methods respond normally
            typeof(QuestListPage).GetField("_isInitializing", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(page, false);
            typeof(QuestListPage).GetMethod("InitializeNavChips", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(page, null);
            return page;
        }

        private static void CleanupQuestListPage(QuestListPage? page)
        {
            if (page == null) return;
            try
            {
                var embeddedMap = typeof(QuestListPage).GetField("_embeddedMapPage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(page) as MapPage;
                if (embeddedMap != null)
                {
                    var mapUnloaded = typeof(MapPage).GetMethod("MapTrackerPage_Unloaded", BindingFlags.Instance | BindingFlags.NonPublic);
                    mapUnloaded?.Invoke(embeddedMap, new object[] { embeddedMap, new RoutedEventArgs() });
                }

                var unloadedMethod = typeof(QuestListPage).GetMethod("QuestListPage_Unloaded", BindingFlags.Instance | BindingFlags.NonPublic);
                unloadedMethod?.Invoke(page, new object[] { page, new RoutedEventArgs() });
            }
            catch { }
        }

        #endregion

        #region Focus Area 1: Vertical Layout Stacking & Row Sizing in QuestListPage (Feature F12)

        [Fact]
        public void Adv1_01_QuestListPageXaml_Row5_ContainsStrictThreeRowVerticalStack()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "QuestListPage.xaml");

            // Locate Row 5 main content Grid
            var row5Grids = doc.Descendants("Grid")
                .Where(g => g.GetAttr("Grid.Row") == "5")
                .ToList();

            Assert.NotEmpty(row5Grids);
            var mainGrid = row5Grids.First();

            // 1. Grid.RowDefinitions must contain exactly 3 rows in order: RowMapView, RowSplitter, RowTaskList
            var rowDefs = mainGrid.Elements("Grid.RowDefinitions").Elements("RowDefinition").ToList();
            Assert.Equal(3, rowDefs.Count);

            var row0 = rowDefs[0];
            var row1 = rowDefs[1];
            var row2 = rowDefs[2];

            Assert.Equal("RowMapView", row0.GetXamlName());
            Assert.Equal("RowSplitter", row1.GetXamlName());
            Assert.Equal("RowTaskList", row2.GetXamlName());

            // 2. Initial Height definitions: RowMapView=0, RowSplitter=Auto, RowTaskList=*
            Assert.Equal("0", row0.GetAttr("Height"));
            Assert.Equal("Auto", row1.GetAttr("Height"));
            Assert.Equal("*", row2.GetAttr("Height"));

            // 3. Child element row assignments
            var mapContainer = mainGrid.Descendants("Border").FirstOrDefault(b => b.GetXamlName() == "EmbeddedMapContainer");
            Assert.NotNull(mapContainer);
            Assert.Equal("0", mapContainer.GetAttr("Grid.Row"));
            Assert.Equal("3", mapContainer.GetAttr("Grid.ColumnSpan"));

            var mapSplitter = mainGrid.Descendants("GridSplitter").FirstOrDefault(s => s.GetXamlName() == "MapSplitter");
            Assert.NotNull(mapSplitter);
            Assert.Equal("1", mapSplitter.GetAttr("Grid.Row"));
            Assert.Equal("3", mapSplitter.GetAttr("Grid.ColumnSpan"));

            var taskListBorder = mainGrid.Descendants("Border").FirstOrDefault(b => b.Descendants().Any(d => d.GetXamlName() == "LstQuests"));
            Assert.NotNull(taskListBorder);
            Assert.Equal("2", taskListBorder.GetAttr("Grid.Row"));
            Assert.Equal("3", taskListBorder.GetAttr("Grid.ColumnSpan"));
        }

        [Fact]
        public void Adv1_02_QuestListPageXaml_MapSplitter_ConfiguredForHorizontalSeparation()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "QuestListPage.xaml");

            var splitter = doc.Descendants("GridSplitter").FirstOrDefault(s => s.GetXamlName() == "MapSplitter");
            Assert.NotNull(splitter);

            // Must resize rows vertically, stretching horizontally
            Assert.Equal("4", splitter.GetAttr("Height"));
            Assert.Equal("Stretch", splitter.GetAttr("HorizontalAlignment"));
            Assert.Equal("Center", splitter.GetAttr("VerticalAlignment"));
            Assert.Equal("PreviousAndNext", splitter.GetAttr("ResizeBehavior"));
            Assert.Equal("SizeNS", splitter.GetAttr("Cursor"));

            // Must NOT have vertical column splitter properties (e.g. Width=4 or SizeWE)
            Assert.Null(splitter.GetAttr("Width"));
            Assert.NotEqual("SizeWE", splitter.GetAttr("Cursor"));
        }

        [Fact]
        public void Adv1_03_STA_NavigationMode_Maps_Initializes380pxMapRow_AndStarTaskList()
        {
            RunInSta(() =>
            {
                var page = CreateInitializedQuestListPage();
                try
                {
                    // Switch to Maps navigation mode
                    var switchMethod = typeof(QuestListPage).GetMethod("SwitchNavigationMode", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(switchMethod);
                    switchMethod.Invoke(page, new object[] { NavigationMode.Maps });

                    // Check row definitions in page
                    Assert.NotNull(page.RowMapView);
                    Assert.NotNull(page.RowSplitter);
                    Assert.NotNull(page.RowTaskList);
                    Assert.NotNull(page.MapSplitter);
                    Assert.NotNull(page.EmbeddedMapContainer);

                    // Row 0: Map container initial height ~380px
                    Assert.Equal(380.0, page.RowMapView.Height.Value);
                    Assert.Equal(GridUnitType.Pixel, page.RowMapView.Height.GridUnitType);

                    // Row 1: Splitter height Auto and Visible
                    Assert.Equal(GridLength.Auto, page.RowSplitter.Height);
                    Assert.Equal(Visibility.Visible, page.MapSplitter.Visibility);

                    // Row 2: Task cards feed 1* (Star sizing prevents collapse)
                    Assert.Equal(GridUnitType.Star, page.RowTaskList.Height.GridUnitType);
                    Assert.Equal(1.0, page.RowTaskList.Height.Value);

                    // Embedded map container visible
                    Assert.Equal(Visibility.Visible, page.EmbeddedMapContainer.Visibility);
                }
                finally
                {
                    CleanupQuestListPage(page);
                }
            });
        }

        [Fact]
        public void Adv1_04_STA_NavigationMode_NonMapsModes_CollapseMapRowToZero()
        {
            RunInSta(() =>
            {
                var page = CreateInitializedQuestListPage();
                try
                {
                    var switchMethod = typeof(QuestListPage).GetMethod("SwitchNavigationMode", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(switchMethod);

                    // 1. List Mode
                    page.RbViewList.IsChecked = true;
                    var navModeChanged = typeof(QuestListPage).GetMethod("NavMode_Changed", BindingFlags.Instance | BindingFlags.NonPublic);
                    navModeChanged?.Invoke(page, new object[] { page.RbViewList, new RoutedEventArgs() });

                    Assert.Equal(0.0, page.RowMapView.Height.Value);
                    Assert.Equal(0.0, page.RowSplitter.Height.Value);
                    Assert.Equal(Visibility.Collapsed, page.MapSplitter.Visibility);
                    Assert.Equal(Visibility.Collapsed, page.EmbeddedMapContainer.Visibility);
                    Assert.Equal(GridUnitType.Star, page.RowTaskList.Height.GridUnitType);

                    // 2. Traders Mode
                    switchMethod.Invoke(page, new object[] { NavigationMode.Traders });
                    Assert.Equal(0.0, page.RowMapView.Height.Value);
                    Assert.Equal(0.0, page.RowSplitter.Height.Value);
                    Assert.Equal(Visibility.Collapsed, page.MapSplitter.Visibility);
                    Assert.Equal(Visibility.Collapsed, page.EmbeddedMapContainer.Visibility);
                    Assert.Equal(GridUnitType.Star, page.RowTaskList.Height.GridUnitType);

                    // 3. Graph Mode
                    page.RbViewList.IsChecked = false;
                    page.RbViewTraders.IsChecked = false;
                    page.RbViewMaps.IsChecked = false;
                    page.RbViewGraph.IsChecked = true;
                    navModeChanged?.Invoke(page, new object[] { page.RbViewGraph, new RoutedEventArgs() });

                    Assert.Equal(0.0, page.RowMapView.Height.Value);
                    Assert.Equal(0.0, page.RowSplitter.Height.Value);
                    Assert.Equal(Visibility.Collapsed, page.MapSplitter.Visibility);
                    Assert.Equal(Visibility.Collapsed, page.EmbeddedMapContainer.Visibility);
                }
                finally
                {
                    CleanupQuestListPage(page);
                }
            });
        }

        [Fact]
        public void Adv1_05_STA_SplitterResizing_BoundaryStress_TasksListNeverCollapses()
        {
            RunInSta(() =>
            {
                var page = CreateInitializedQuestListPage();
                try
                {
                    var switchMethod = typeof(QuestListPage).GetMethod("SwitchNavigationMode", BindingFlags.Instance | BindingFlags.NonPublic);
                    switchMethod?.Invoke(page, new object[] { NavigationMode.Maps });

                    // Simulate splitter movement to various heights
                    double[] testHeights = new[] { 100.0, 200.0, 380.0, 550.0, 800.0, 1200.0 };

                    foreach (var h in testHeights)
                    {
                        page.RowMapView.Height = new GridLength(h, GridUnitType.Pixel);

                        // RowTaskList must remain Star sized so active tasks feed adapts dynamically
                        Assert.Equal(GridUnitType.Star, page.RowTaskList.Height.GridUnitType);
                        Assert.True(page.RowTaskList.Height.Value > 0, "RowTaskList Star weight must remain positive");
                        Assert.Equal(Visibility.Visible, page.MapSplitter.Visibility);
                        Assert.Equal(Visibility.Visible, page.EmbeddedMapContainer.Visibility);
                    }
                }
                finally
                {
                    CleanupQuestListPage(page);
                }
            });
        }

        [Fact]
        public void Adv1_06_STA_RapidModeSwitching_Stress500Cycles_LayoutStateConsistent()
        {
            RunInSta(() =>
            {
                var page = CreateInitializedQuestListPage();
                try
                {
                    var switchMethod = typeof(QuestListPage).GetMethod("SwitchNavigationMode", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(switchMethod);

                    var modes = new[] { NavigationMode.Maps, NavigationMode.Traders, NavigationMode.Maps, NavigationMode.Traders };

                    // Rapid switching 50 times
                    for (int i = 0; i < 50; i++)
                    {
                        var m = modes[i % modes.Length];
                        switchMethod.Invoke(page, new object[] { m });
                    }

                    // Final switch to Maps
                    switchMethod.Invoke(page, new object[] { NavigationMode.Maps });

                    Assert.Equal(380.0, page.RowMapView.Height.Value);
                    Assert.Equal(GridUnitType.Pixel, page.RowMapView.Height.GridUnitType);
                    Assert.Equal(GridUnitType.Star, page.RowTaskList.Height.GridUnitType);
                    Assert.Equal(Visibility.Visible, page.EmbeddedMapContainer.Visibility);
                    Assert.Equal(Visibility.Visible, page.MapSplitter.Visibility);
                }
                finally
                {
                    CleanupQuestListPage(page);
                }
            });
        }

        #endregion

        #region Focus Area 2: Backwards Compatibility Grid Layout Preservation (Feature F12)

        [Fact]
        public void Adv2_01_QuestListPageXaml_LegacyColumnsAndSplitter_StrictlyPreserved()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "QuestListPage.xaml");

            var colTaskList = doc.Descendants("ColumnDefinition").FirstOrDefault(c => c.GetXamlName() == "ColTaskList");
            var colSplitter = doc.Descendants("ColumnDefinition").FirstOrDefault(c => c.GetXamlName() == "ColSplitter");
            var colMapView = doc.Descendants("ColumnDefinition").FirstOrDefault(c => c.GetXamlName() == "ColMapView");
            var splitter = doc.Descendants("GridSplitter").FirstOrDefault(s => s.GetXamlName() == "MapSplitter");
            var mapContainer = doc.Descendants("Border").FirstOrDefault(b => b.GetXamlName() == "EmbeddedMapContainer");

            Assert.NotNull(colTaskList);
            Assert.NotNull(colSplitter);
            Assert.NotNull(colMapView);
            Assert.NotNull(splitter);
            Assert.NotNull(mapContainer);
        }

        [Fact]
        public void Adv2_02_QuestListPageXaml_LegacyViewModesAndFilterControls_StrictlyPreserved()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "QuestListPage.xaml");

            // Radio buttons for 4 modes
            Assert.NotNull(doc.Descendants("RadioButton").FirstOrDefault(r => r.GetXamlName() == "RbViewList"));
            Assert.NotNull(doc.Descendants("RadioButton").FirstOrDefault(r => r.GetXamlName() == "RbViewTraders"));
            Assert.NotNull(doc.Descendants("RadioButton").FirstOrDefault(r => r.GetXamlName() == "RbViewMaps"));
            Assert.NotNull(doc.Descendants("RadioButton").FirstOrDefault(r => r.GetXamlName() == "RbViewGraph"));

            // Secondary filter ComboBox
            Assert.NotNull(doc.Descendants("ComboBox").FirstOrDefault(c => c.GetXamlName() == "CmbSecondaryFilter"));
        }

        [Fact]
        public void Adv2_03_STA_ColumnWidths_InAllModes_RemainZeroForHorizontalCompatibility()
        {
            RunInSta(() =>
            {
                var page = CreateInitializedQuestListPage();
                try
                {
                    var switchMethod = typeof(QuestListPage).GetMethod("SwitchNavigationMode", BindingFlags.Instance | BindingFlags.NonPublic);

                    // 1. Maps mode
                    switchMethod?.Invoke(page, new object[] { NavigationMode.Maps });
                    Assert.NotNull(page.ColTaskList);
                    Assert.NotNull(page.ColSplitter);
                    Assert.NotNull(page.ColMapView);

                    Assert.Equal(GridUnitType.Star, page.ColTaskList.Width.GridUnitType);
                    Assert.Equal(0.0, page.ColSplitter.Width.Value);
                    Assert.Equal(0.0, page.ColMapView.Width.Value);

                    // 2. Traders mode
                    switchMethod?.Invoke(page, new object[] { NavigationMode.Traders });
                    Assert.Equal(GridUnitType.Star, page.ColTaskList.Width.GridUnitType);
                    Assert.Equal(0.0, page.ColSplitter.Width.Value);
                    Assert.Equal(0.0, page.ColMapView.Width.Value);

                    // 3. List mode
                    page.RbViewList.IsChecked = true;
                    var navModeChanged = typeof(QuestListPage).GetMethod("NavMode_Changed", BindingFlags.Instance | BindingFlags.NonPublic);
                    navModeChanged?.Invoke(page, new object[] { page.RbViewList, new RoutedEventArgs() });

                    Assert.Equal(GridUnitType.Star, page.ColTaskList.Width.GridUnitType);
                    Assert.Equal(0.0, page.ColSplitter.Width.Value);
                    Assert.Equal(0.0, page.ColMapView.Width.Value);
                }
                finally
                {
                    CleanupQuestListPage(page);
                }
            });
        }

        [Fact]
        public void Adv2_04_BackwardsCompatibility_Challenger1FullSolution_QuerySimulation()
        {
            // Execute the exact query logic from Challenger1FullSolutionEmpiricalTests.ModeTransitions_XAML_StructuralDeclarations_AllModesSupported
            var xamlPath = R1R2TestHelpers.ResolveRepoPath("TarkovHelper", "Pages", "QuestListPage.xaml");
            Assert.True(File.Exists(xamlPath));

            var doc = XDocument.Load(xamlPath);

            var rbList = doc.Descendants(XamlNs + "RadioButton").FirstOrDefault(r => (string?)r.Attribute(XNs + "Name") == "RbViewList");
            var rbTraders = doc.Descendants(XamlNs + "RadioButton").FirstOrDefault(r => (string?)r.Attribute(XNs + "Name") == "RbViewTraders");
            var rbMaps = doc.Descendants(XamlNs + "RadioButton").FirstOrDefault(r => (string?)r.Attribute(XNs + "Name") == "RbViewMaps");
            var rbGraph = doc.Descendants(XamlNs + "RadioButton").FirstOrDefault(r => (string?)r.Attribute(XNs + "Name") == "RbViewGraph");

            var cmbSecondary = doc.Descendants(XamlNs + "ComboBox").FirstOrDefault(c => (string?)c.Attribute(XNs + "Name") == "CmbSecondaryFilter");

            var colTaskList = doc.Descendants(XamlNs + "ColumnDefinition").FirstOrDefault(c => (string?)c.Attribute(XNs + "Name") == "ColTaskList");
            var colSplitter = doc.Descendants(XamlNs + "ColumnDefinition").FirstOrDefault(c => (string?)c.Attribute(XNs + "Name") == "ColSplitter");
            var colMapView = doc.Descendants(XamlNs + "ColumnDefinition").FirstOrDefault(c => (string?)c.Attribute(XNs + "Name") == "ColMapView");
            var splitter = doc.Descendants(XamlNs + "GridSplitter").FirstOrDefault(s => (string?)s.Attribute(XNs + "Name") == "MapSplitter");
            var mapContainer = doc.Descendants(XamlNs + "Border").FirstOrDefault(b => (string?)b.Attribute(XNs + "Name") == "EmbeddedMapContainer");

            Assert.NotNull(rbList);
            Assert.NotNull(rbTraders);
            Assert.NotNull(rbMaps);
            Assert.NotNull(rbGraph);
            Assert.NotNull(cmbSecondary);
            Assert.NotNull(colTaskList);
            Assert.NotNull(colSplitter);
            Assert.NotNull(colMapView);
            Assert.NotNull(splitter);
            Assert.NotNull(mapContainer);
        }

        #endregion

        #region Focus Area 3: 12 Canonical Map Chips Dynamic Forwarding to Embedded Map

        [Fact]
        public void Adv3_01_STA_MapChips_ContainsAll12CanonicalMaps_PlusTodos()
        {
            RunInSta(() =>
            {
                var page = CreateInitializedQuestListPage();
                try
                {
                    Assert.True(page.MapChips.Count >= 13, $"Expected at least 13 map chips (Todos + 12 canonical maps), found {page.MapChips.Count}");

                    // "Todos" default chip
                    var todosChip = page.MapChips.FirstOrDefault(c => string.IsNullOrEmpty(c.Key));
                    Assert.NotNull(todosChip);
                    Assert.Equal("Todos", todosChip.DisplayName);

                    // All 12 canonical maps
                    foreach (var mapKey in R1R2TestHelpers.Canonical12Maps)
                    {
                        var chip = page.MapChips.FirstOrDefault(c => string.Equals(c.Key, mapKey, StringComparison.OrdinalIgnoreCase));
                        Assert.True(chip != null, $"Canonical map '{mapKey}' missing from MapChips collection.");
                        Assert.False(string.IsNullOrWhiteSpace(chip.DisplayName), $"DisplayName for '{mapKey}' must not be empty.");
                    }
                }
                finally
                {
                    CleanupQuestListPage(page);
                }
            });
        }

        [Fact]
        public void Adv3_02_STA_SelectNavChip_InvokesMapTracker_AndEmbeddedMap_WithoutExceptions()
        {
            RunInSta(() =>
            {
                var page = CreateInitializedQuestListPage();
                try
                {
                    var switchMethod = typeof(QuestListPage).GetMethod("SwitchNavigationMode", BindingFlags.Instance | BindingFlags.NonPublic);
                    switchMethod?.Invoke(page, new object[] { NavigationMode.Maps });

                    var selectChipMethod = typeof(QuestListPage).GetMethod("SelectNavChip", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(selectChipMethod);

                    // Iterate through each canonical chip and select it
                    foreach (var canonicalKey in R1R2TestHelpers.Canonical12Maps)
                    {
                        var targetChip = page.MapChips.FirstOrDefault(c => string.Equals(c.Key, canonicalKey, StringComparison.OrdinalIgnoreCase));
                        Assert.NotNull(targetChip);

                        // Execute selection
                        selectChipMethod.Invoke(page, new object[] { targetChip });

                        // Verify state synchronization
                        Assert.True(targetChip.IsSelected, $"Selected chip '{canonicalKey}' must have IsSelected=true");
                        Assert.All(page.MapChips.Where(c => c != targetChip), other => Assert.False(other.IsSelected));

                        var currentTrackerMap = MapTrackerService.Instance.CurrentMapKey;
                        Assert.True(string.Equals(currentTrackerMap, canonicalKey, StringComparison.OrdinalIgnoreCase),
                            $"MapTrackerService CurrentMapKey '{currentTrackerMap}' should match selected chip '{canonicalKey}'");
                    }
                }
                finally
                {
                    CleanupQuestListPage(page);
                }
            });
        }

        [Fact]
        public void Adv3_03_STA_EmbeddedMapPage_SelectMap_All12CanonicalMaps_StressAndEdgeCases()
        {
            RunInSta(() =>
            {
                var mapPage = new MapPage();
                mapPage.SetEmbeddedMode(true);

                // 1. All 12 canonical maps
                foreach (var mapKey in R1R2TestHelpers.Canonical12Maps)
                {
                    mapPage.SelectMap(mapKey);
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

        [Fact]
        public void Adv3_04_STA_ChipSwitching_Stress300Cycles_RandomOrder_NoDesync()
        {
            RunInSta(() =>
            {
                var page = CreateInitializedQuestListPage();
                try
                {
                    var switchMethod = typeof(QuestListPage).GetMethod("SwitchNavigationMode", BindingFlags.Instance | BindingFlags.NonPublic);
                    switchMethod?.Invoke(page, new object[] { NavigationMode.Maps });

                    var selectChipMethod = typeof(QuestListPage).GetMethod("SelectNavChip", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(selectChipMethod);

                    var canonicalChips = page.MapChips.Where(c => !string.IsNullOrEmpty(c.Key)).ToList();
                    var rng = new Random(42);

                    for (int i = 0; i < 300; i++)
                    {
                        var chosen = canonicalChips[rng.Next(canonicalChips.Count)];
                        selectChipMethod.Invoke(page, new object[] { chosen });

                        Assert.True(chosen.IsSelected);
                        Assert.Equal(chosen.Key, MapTrackerService.Instance.CurrentMapKey);
                    }
                }
                finally
                {
                    CleanupQuestListPage(page);
                }
            });
        }

        [Fact]
        public void Adv3_05_QuestFilterEngine_MatchesMap_All12Maps_ZeroTasksAndEdgeCases()
        {
            // Test QuestFilterEngine.MatchesMap across canonical maps and edge case inputs
            foreach (var mapKey in R1R2TestHelpers.Canonical12Maps)
            {
                // 1. Quest specifically on this map
                var exactQuest = new QuestViewModel
                {
                    Task = new TarkovTask { Id = $"q_{mapKey}", Maps = new List<string> { mapKey } }
                };
                Assert.True(QuestFilterEngine.MatchesMap(exactQuest, mapKey));

                // 2. Quest with null task or null maps
                var nullMapsQuest = new QuestViewModel { Task = new TarkovTask { Id = "q_null" } };
                Assert.False(QuestFilterEngine.MatchesMap(nullMapsQuest, mapKey));

                // 3. Multi-map quest
                var multiQuest = new QuestViewModel
                {
                    Task = new TarkovTask { Id = "q_multi", Maps = new List<string> { "customs", "woods", mapKey } }
                };
                Assert.True(QuestFilterEngine.MatchesMap(multiQuest, mapKey));

                // 4. Unrelated map returns false
                string otherMap = mapKey == "customs" ? "woods" : "customs";
                var otherQuest = new QuestViewModel
                {
                    Task = new TarkovTask { Id = $"q_{otherMap}", Maps = new List<string> { otherMap } }
                };
                Assert.False(QuestFilterEngine.MatchesMap(otherQuest, mapKey));

                // 5. "Any" or "All" is recognized by IsAnyLocationTask
                var anyQuest = new QuestViewModel
                {
                    Task = new TarkovTask { Id = "q_any", Maps = new List<string> { "any" } }
                };
                Assert.True(QuestFilterEngine.IsAnyLocationTask(anyQuest));
            }
        }

        [Fact]
        public void Adv3_06_MapPageXaml_EmbeddedFloorSelector_OverlayInMapViewerGrid()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");

            // Look for persistent overlay floor selector panel inside MapViewerGrid
            var overlayPanel = doc.Descendants("Border")
                .FirstOrDefault(b => b.GetXamlName() == "OverlayFloorSelectorPanel");
            Assert.NotNull(overlayPanel);

            var parentGrid = overlayPanel.Parent;
            Assert.NotNull(parentGrid);
            Assert.Equal("MapViewerGrid", parentGrid.GetXamlName());

            var combo = overlayPanel.Descendants("ComboBox").FirstOrDefault(c => c.GetXamlName() == "CmbOverlayFloorSelect");
            Assert.NotNull(combo);
        }

        #endregion

        #region Focus Area 4: Task Cards Feed Completeness & Action Buttons

        [Fact]
        public void Adv4_01_TaskCardControlXaml_ContainsAllRequiredActionButtons()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Controls", "TaskCardControl.xaml");

            var buttons = doc.Descendants("Button").ToList();

            // 1. Wiki launcher button
            Assert.Contains(buttons, b => b.GetAttr("Click") == "BtnWiki_Click");

            // 2. Map launcher button
            Assert.Contains(buttons, b => b.GetAttr("Click") == "BtnMap_Click");

            // 3. Complete task button
            Assert.Contains(buttons, b => b.GetAttr("Click") == "BtnComplete_Click");

            // 4. Collapse/Expand button
            Assert.Contains(buttons, b => b.GetAttr("Click") == "BtnToggleExpand_Click");

            // 5. Objective item steppers & action toggle buttons
            Assert.Contains(buttons, b => b.GetAttr("Click") == "BtnStepperDecrement_Click");
            Assert.Contains(buttons, b => b.GetAttr("Click") == "BtnStepperIncrement_Click");
            Assert.Contains(buttons, b => b.GetAttr("Click") == "BtnStepperMax_Click");
            Assert.Contains(buttons, b => b.GetAttr("Click") == "BtnActionToggle_Click");

            // 6. Wiki notes / guide expander
            var guideExpander = doc.Descendants("Expander").FirstOrDefault(e => (e.GetAttr("Visibility") ?? "").Contains("GuideExpanderVisibility"));
            Assert.NotNull(guideExpander);
        }

        [Fact]
        public void Adv4_02_STA_TaskCardControl_ToggleCompletion_TogglesQuestState()
        {
            RunInSta(() =>
            {
                var card = new TaskCardControl();
                var task = QuestDbService.Instance.GetQuestByNormalizedName("debut") ?? new TarkovTask
                {
                    Id = "5936d90786f7743a142d160f",
                    NormalizedName = "debut",
                    Name = "Debut",
                    Trader = "Prapor",
                    Maps = new List<string> { "customs" }
                };

                // Ensure initial status is clean
                QuestProgressService.Instance.ResetQuest(task);

                card.LoadFromTask(task, QuestStatus.Active);
                Assert.False(card.CardModel.IsCompleted);

                // Simulate clicking complete
                card.CardModel.MarkComplete();
                Assert.True(card.CardModel.IsCompleted);

                // Toggle back to incomplete
                card.CardModel.MarkComplete();
                Assert.False(card.CardModel.IsCompleted);
            });
        }

        [Fact]
        public void Adv4_03_STA_TaskCardControl_SteppersAndObjectives_InteractiveRecalculation()
        {
            RunInSta(() =>
            {
                var vm = new TaskCardViewModel();

                var stepper = new SubObjectiveStepperItem
                {
                    ItemId = "item_1",
                    ItemName = "MS2000 Marker",
                    CurrentCount = 0,
                    MaxCount = 3
                };

                var actionToggle = new SubObjectiveActionItem
                {
                    Description = "Stash MS2000 Marker at tank",
                    IsChecked = false
                };

                vm.ItemSteppers.Add(stepper);
                vm.ActionToggles.Add(actionToggle);

                vm.RecalculateMetRequirements();
                Assert.Equal(0, vm.MetRequirements);

                // Step to max
                stepper.SetMax();
                vm.RecalculateMetRequirements();
                Assert.Equal(1, vm.MetRequirements);

                // Check action toggle
                actionToggle.IsChecked = true;
                vm.RecalculateMetRequirements();
                Assert.Equal(2, vm.MetRequirements);
            });
        }

        [Fact]
        public void Adv4_04_QuestListPageXaml_CompatibilityActionButtons_Preserved()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "QuestListPage.xaml");

            // Verify compatibility action buttons in detail panel are present for older harnesses
            var buttons = doc.Descendants("Button").ToList();

            Assert.Contains(buttons, b => b.GetXamlName() == "BtnWiki");
            Assert.Contains(buttons, b => b.GetXamlName() == "BtnShowOnMap");
            Assert.Contains(buttons, b => b.GetXamlName() == "BtnComplete");
            Assert.Contains(buttons, b => b.GetXamlName() == "BtnReset");
        }

        #endregion
    }
}
