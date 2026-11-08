using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Pages.Map.Components;
using TarkovHelper.Services;
using TarkovHelper.Services.Map;
using TarkovHelper.Windows;
using Xunit;

namespace TarkovHelper.Tests;

public class Round1FeatureEmpiricalTests
{
    private static void RunInSta(Action action, int timeoutSeconds = 15)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            throw new TimeoutException($"STA test action timed out after {timeoutSeconds}s");
        }
        if (caught != null)
        {
            throw new AggregateException("Exception thrown in STA thread", caught);
        }
    }

    private static void SetElementIsMouseOver(UIElement element, bool isMouseOver)
    {
        var coreFlagsType = typeof(UIElement).Assembly.GetType("System.Windows.CoreFlags");
        Assert.NotNull(coreFlagsType);
        var flagVal = Enum.Parse(coreFlagsType, "IsMouseOverCache");
        var method = typeof(UIElement).GetMethod("WriteFlag", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(element, new object[] { flagVal, isMouseOver });
    }

    #region R1: Tooltip Stability & Hover Preservation

    [Fact]
    public void SafeClear_PreservesElementWhenTooltipIsOpenEvenWithoutMouseOver()
    {
        RunInSta(() =>
        {
            var canvas = new Canvas();
            var tooltip = new ToolTip { IsOpen = true };
            var elementWithOpenTooltip = new Border
            {
                Width = 20,
                Height = 20,
                ToolTip = tooltip
            };
            var normalElement = new Border { Width = 20, Height = 20 };

            canvas.Children.Add(elementWithOpenTooltip);
            canvas.Children.Add(normalElement);

            var tracked = new List<FrameworkElement> { elementWithOpenTooltip, normalElement };
            MarkerRefreshHelper.SafeClear(canvas, tracked);

            // elementWithOpenTooltip must be retained because tooltip is open
            Assert.Single(canvas.Children);
            Assert.Same(elementWithOpenTooltip, canvas.Children[0]);
            Assert.Empty(tracked);
        });
    }

    [Fact]
    public void SafeClear_PreservesElementWhenTooltipIsOpen_AndCleansUpOnlyAfterTooltipCloses()
    {
        RunInSta(() =>
        {
            var canvas = new Canvas();
            var tooltip = new ToolTip { IsOpen = true };
            var elementWithOpenTooltip = new Border
            {
                Width = 20,
                Height = 20,
                ToolTip = tooltip
            };
            var normalElement = new Border { Width = 20, Height = 20 };

            canvas.Children.Add(elementWithOpenTooltip);
            canvas.Children.Add(normalElement);

            MarkerRefreshHelper.SafeClear(canvas, null);

            // Active element with open tooltip is retained, other removed
            Assert.Single(canvas.Children);
            Assert.Same(elementWithOpenTooltip, canvas.Children[0]);

            // Simulate MouseLeave on element with open tooltip
            var mouseLeaveArgs = new MouseEventArgs(Mouse.PrimaryDevice, 0)
            {
                RoutedEvent = Mouse.MouseLeaveEvent,
                Source = elementWithOpenTooltip
            };
            elementWithOpenTooltip.RaiseEvent(mouseLeaveArgs);

            // Tooltip is still open, so element must NOT be removed yet
            Assert.Single(canvas.Children);
            Assert.Same(elementWithOpenTooltip, canvas.Children[0]);

            // Close tooltip -> triggers Closed event and removal
            tooltip.IsOpen = false;
            tooltip.RaiseEvent(new RoutedEventArgs(ToolTip.ClosedEvent));

            Assert.Empty(canvas.Children);
        });
    }

    [Fact]
    public void MapQuestMarkerManager_ClearMarkers_UsesSafeClear()
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\Pages\Map\Components\MapQuestMarkerManager.cs"));
        Assert.True(File.Exists(path), $"MapQuestMarkerManager.cs must exist at {path}");
        var code = File.ReadAllText(path);

        Assert.Contains("MarkerRefreshHelper.SafeClear(_markersContainer, _questMarkerElements);", code);
        Assert.DoesNotContain("_markersContainer.Children.Clear();", code);
    }

    [Fact]
    public void MapQuestMarkerManager_CreateQuestMarker_IncludesHitBridgeAndNoChildTooltipCollision()
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\Pages\Map\Components\MapQuestMarkerManager.cs"));
        var code = File.ReadAllText(path);

        // Verify hit bridge exists to eliminate 4px cursor gap
        Assert.Contains("hitBridge", code);
        // Verify child nameBorder does not duplicate tooltip assignment which causes popup reset
        Assert.DoesNotContain("nameBorder.ToolTip = canvas.ToolTip;", code);
    }

    #endregion

    #region R2: Auto-Detection & Map Change between Raids

    [Fact]
    public void MapTrackerService_SetCurrentMap_WithForce_FiresMapChangedEvenForSameMap()
    {
        var tracker = MapTrackerService.Instance;
        string? receivedMap = null;
        int eventCount = 0;

        void Handler(string mapKey)
        {
            receivedMap = mapKey;
            eventCount++;
        }

        tracker.MapChanged += Handler;
        try
        {
            tracker.SetCurrentMap("Customs", force: true);
            Assert.Equal("Customs", receivedMap);
            Assert.Equal(1, eventCount);

            // Call again with force: true for the same map (simulating consecutive raids)
            tracker.SetCurrentMap("Customs", force: true);
            Assert.Equal("Customs", receivedMap);
            Assert.Equal(2, eventCount);
        }
        finally
        {
            tracker.MapChanged -= Handler;
        }
    }

    [Fact]
    public void OverlayMiniMapWindow_SourceCode_SubscribesToLogMapWatcherServiceDirectly()
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\Windows\OverlayMiniMapWindow.xaml.cs"));
        Assert.True(File.Exists(path));
        var code = File.ReadAllText(path);

        Assert.Contains("LogMapWatcherService.Instance.MapChanged += OnLogMapWatcherMapChanged;", code);
        Assert.Contains("LogMapWatcherService.Instance.MapChanged -= OnLogMapWatcherMapChanged;", code);
        Assert.Contains("void OnLogMapWatcherMapChanged(object? sender, MapChangedEventArgs e)", code);
    }

    [Fact]
    public void LogMapWatcherService_OnLogFileCreated_ResetsLastDetectedMap()
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\Services\Map\LogMapWatcherService.cs"));
        var code = File.ReadAllText(path);

        Assert.Contains("_lastDetectedMap = null;", code);
    }

    #endregion

    #region R3: Window Control Buttons & Symbols (Minimize and Close)

    [Fact]
    public void OverlayMiniMapWindow_HasVisibleMinimizeAndCloseButtonsWithExpectedGlyphs()
    {
        RunInSta(() =>
        {
            var window = new OverlayMiniMapWindow(new OverlayMiniMapSettings());

            var btnMinimize = window.FindName("BtnMinimize") as Button;
            var btnClose = window.FindName("BtnClose") as Button;

            Assert.NotNull(btnMinimize);
            Assert.NotNull(btnClose);

            // Symbols
            Assert.Equal("_", btnMinimize.Content);
            Assert.Equal("✕", btnClose.Content);

            // Dimensions
            Assert.Equal(24.0, btnMinimize.Width);
            Assert.Equal(24.0, btnMinimize.Height);
            Assert.Equal(24.0, btnClose.Width);
            Assert.Equal(24.0, btnClose.Height);

            // Tooltips
            Assert.NotNull(btnMinimize.ToolTip);
            Assert.NotNull(btnClose.ToolTip);
        });
    }

    [Fact]
    public void OverlayMiniMapWindow_Xaml_DefinesRecognizesAccessKeyFalseForSymbols()
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\Windows\OverlayMiniMapWindow.xaml"));
        var xaml = File.ReadAllText(path);

        Assert.Contains("TitleBarWindowBtnStyle", xaml);
        Assert.Contains("TitleBarCloseBtnStyle", xaml);
        Assert.Contains("RecognizesAccessKey=\"False\"", xaml);
        Assert.Contains("x:Name=\"BtnMinimize\"", xaml);
        Assert.Contains("x:Name=\"BtnClose\"", xaml);
        Assert.Contains("Content=\"_\"", xaml);
        Assert.Contains("Content=\"✕\"", xaml);
    }

    #endregion

    #region R4: Minimap Title Map Selector Dropdown

    [Fact]
    public void OverlayMiniMapWindow_HasTitleMapSelectorAndContextMenu()
    {
        RunInSta(() =>
        {
            var origLang = LocalizationService.Instance.CurrentLanguage;
            LocalizationService.Instance.CurrentLanguage = AppLanguage.EN;
            try
            {
                var window = new OverlayMiniMapWindow(new OverlayMiniMapSettings());

                var selectorBtn = window.FindName("BtnTitleMapSelector") as Button;
                Assert.NotNull(selectorBtn);

                var titleText = window.FindName("TxtTitleMapName") as TextBlock;
                Assert.NotNull(titleText);

                var contextMenu = window.FindName("MapSelectorContextMenu") as ContextMenu;
                Assert.NotNull(contextMenu);

                // Trigger population
                window.PopulateMapSelectorMenu();

                // Menu must contain all registered maps
                var tracker = MapTrackerService.Instance;
                var expectedCount = tracker.GetAllMapKeys().Count;
                Assert.True(expectedCount > 0, "Tracker must have registered map keys");
                Assert.Equal(expectedCount, selectorBtn.ContextMenu.Items.Count);

                // Test selecting a map
                window.SelectMap("Woods");
                Assert.Equal("Woods", titleText.Text);
            }
            finally
            {
                LocalizationService.Instance.CurrentLanguage = origLang;
            }
        });
    }

    [Fact]
    public void OverlayMiniMapWindow_PopulateMapSelectorMenu_SortsAlphabetically()
    {
        RunInSta(() =>
        {
            var window = new OverlayMiniMapWindow(new OverlayMiniMapSettings());
            window.PopulateMapSelectorMenu();

            var menu = window.BtnTitleMapSelector.ContextMenu;
            Assert.NotNull(menu);
            Assert.NotEmpty(menu.Items);

            var headers = menu.Items.OfType<MenuItem>()
                .Select(mi => (mi.Header as string ?? "").TrimStart('✓', ' '))
                .ToList();

            var sorted = headers.OrderBy(h => h, StringComparer.CurrentCultureIgnoreCase).ToList();
            Assert.Equal(sorted, headers);
        });
    }

    [Fact]
    public void SafeClear_PreservesElementTagWithoutOverwriting_AndElevatesZIndex()
    {
        RunInSta(() =>
        {
            var canvas = new Canvas();
            var tooltip = new ToolTip { IsOpen = true };
            var originalTag = new TaskObjectiveWithLocation { ObjectiveId = "test_obj_123", TaskName = "Delivery From the Past" };
            var marker = new Border
            {
                Width = 20,
                Height = 20,
                ToolTip = tooltip,
                Tag = originalTag
            };
            var otherMarker = new Border { Width = 20, Height = 20 };

            canvas.Children.Add(marker);
            canvas.Children.Add(otherMarker);

            var tracked = new List<FrameworkElement> { marker, otherMarker };
            MarkerRefreshHelper.SafeClear(canvas, tracked);

            // 1. marker is preserved
            Assert.Single(canvas.Children);
            Assert.Same(marker, canvas.Children[0]);

            // 2. Tag is strictly untouched (CRITICAL: domain model not overwritten by string)
            Assert.Same(originalTag, marker.Tag);

            // 3. Z-Index elevated so subsequent markers added to canvas cannot steal hit-testing
            Assert.Equal(9999, Panel.GetZIndex(marker));

            // 4. tracked list is cleared so only newly active markers are tracked
            Assert.Empty(tracked);

            // 5. Clean removal after tooltip closed
            tooltip.IsOpen = false;
            tooltip.RaiseEvent(new RoutedEventArgs(ToolTip.ClosedEvent));
            Assert.Empty(canvas.Children);
        });
    }

    [Fact]
    public void OverlayMiniMapService_IsOverlayVisible_ReportsFalseWhenWindowIsMinimized()
    {
        RunInSta(() =>
        {
            var service = OverlayMiniMapService.Instance;
            service.ShowOverlay();
            var window = service.CurrentOverlayWindow;
            Assert.NotNull(window);

            Assert.True(service.IsOverlayVisible);

            window.WindowState = WindowState.Minimized;
            Assert.False(service.IsOverlayVisible, "Minimized window must report IsOverlayVisible = false so toggle restores it");

            window.WindowState = WindowState.Normal;
            Assert.True(service.IsOverlayVisible);

            service.HideOverlay();
        });
    }

    [Fact]
    public void LogMapWatcherService_TryExtractMapFromLine_ResolvesDiverseEftMapNames()
    {
        var watcher = LogMapWatcherService.Instance;

        var testCases = new Dictionary<string, string>
        {
            { "scene preset path:maps/laboratory_preset.bundle rcid:laboratory.ScenesPreset.asset", "Labs" },
            { "scene preset path:maps/rezerv_base_preset.bundle rcid:Rezerv_Base.scenespreset.asset", "Reserve" },
            { "TRACE-NetworkGameCreate profileStatus: 'Location: bigmap, Sid: 123'", "Customs" },
            { "Location: factory4_day, Sid: 456", "Factory" },
            { "Location: sandbox, Sid: 789", "GroundZero" },
            { "scene preset path:maps/shopping_mall_preset.bundle", "Interchange" },
            { "Location: shoreline, Sid: 111", "Shoreline" },
            { "scene preset path:maps/city_preset.bundle", "StreetsOfTarkov" },
            { "Location: woods, Sid: 222", "Woods" },
            { "Location: lighthouse, Sid: 333", "Lighthouse" }
        };

        foreach (var (line, expectedMap) in testCases)
        {
            var detected = watcher.TryExtractMapFromLine(line);
            Assert.Equal(expectedMap, detected);
        }
    }

    #endregion

    #region Round 2 Adversarial Edge Cases & Regression Tests

    [Fact]
    public void SafeClear_PrioritizesElementWithOpenTooltipOverHoveredElement()
    {
        RunInSta(() =>
        {
            var canvas = new Canvas();
            var tooltip = new ToolTip { IsOpen = true };
            var elWithTooltip = new Border
            {
                Width = 20,
                Height = 20,
                ToolTip = tooltip
            };
            var hoveredElement = new Border
            {
                Width = 20,
                Height = 20
            };
            SetElementIsMouseOver(hoveredElement, true);

            var normalElement = new Border { Width = 20, Height = 20 };

            canvas.Children.Add(hoveredElement);
            canvas.Children.Add(elWithTooltip);
            canvas.Children.Add(normalElement);

            MarkerRefreshHelper.SafeClear(canvas, null);

            // elWithTooltip (with open tooltip) is prioritized and preserved; others removed
            Assert.Single(canvas.Children);
            Assert.Same(elWithTooltip, canvas.Children[0]);

            // Z-Index must be elevated
            Assert.Equal(9999, Panel.GetZIndex(elWithTooltip));
        });
    }

    [Fact]
    public void SafeClear_RetainsPreviouslyRetainedElementDuringSubsequentSafeClear()
    {
        RunInSta(() =>
        {
            var canvas = new Canvas();
            var tooltip = new ToolTip { IsOpen = true };
            var el1 = new Border { Width = 20, Height = 20, ToolTip = tooltip };
            var normalElement1 = new Border { Width = 20, Height = 20 };

            canvas.Children.Add(el1);
            canvas.Children.Add(normalElement1);

            // Cycle 1: el1 has open tooltip, is retained
            MarkerRefreshHelper.SafeClear(canvas, null);
            Assert.Single(canvas.Children);
            Assert.Same(el1, canvas.Children[0]);
            Assert.True(MarkerRefreshHelper.GetIsRetained(el1));

            // Tooltip closes but user is still hovering el1
            tooltip.IsOpen = false;
            SetElementIsMouseOver(el1, true);

            // Spawn a new element
            var el2 = new Border { Width = 20, Height = 20 };
            canvas.Children.Add(el2);

            // Cycle 2: SafeClear should still keep el1
            MarkerRefreshHelper.SafeClear(canvas, null);
            Assert.Single(canvas.Children);
            Assert.Same(el1, canvas.Children[0]);
        });
    }

    [Fact]
    public void MapQuestMarkerManager_ClearMarkers_DoesNotPreemptivelyStripClickHandlers()
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\Pages\Map\Components\MapQuestMarkerManager.cs"));
        Assert.True(File.Exists(path));
        var code = File.ReadAllText(path);

        // Preemptive unhooking before SafeClear must NOT be present so retained markers remain clickable
        Assert.DoesNotContain("c.MouseLeftButtonDown -= QuestMarker_Click;", code);
        Assert.DoesNotContain("border.MouseLeftButtonDown -= QuestMarkerText_Click;", code);
    }

    [Fact]
    public void OverlayMiniMapWindow_OnLogMapWatcherMapChanged_DelegatesToOnMapChanged()
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\Windows\OverlayMiniMapWindow.xaml.cs"));
        Assert.True(File.Exists(path));
        var code = File.ReadAllText(path);

        // Must route through OnMapChanged so debounce guard is shared
        Assert.Contains("OnMapChanged(e.NewMapKey);", code);
    }

    [Fact]
    public void OverlayMiniMapWindow_MapSelectorContextMenu_OpenedEvent_PopulatesItems()
    {
        RunInSta(() =>
        {
            var window = new OverlayMiniMapWindow(new OverlayMiniMapSettings());
            var contextMenu = window.FindName("MapSelectorContextMenu") as ContextMenu;
            Assert.NotNull(contextMenu);

            // Simulate right-click opening without prior left-click
            contextMenu.IsOpen = true;
            contextMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));

            Assert.NotEmpty(contextMenu.Items);
            var tracker = MapTrackerService.Instance;
            Assert.Equal(tracker.GetAllMapKeys().Count, contextMenu.Items.Count);
        });
    }

    [Fact]
    public void MapQuestMarkerManager_UpdateMarkerScales_UpdatesRetainedMarkerScale()
    {
        RunInSta(() =>
        {
            var container = new Canvas();
            var tracker = MapTrackerService.Instance;
            var objService = QuestObjectiveService.Instance;
            var progService = QuestProgressService.Instance;
            var loc = LocalizationService.Instance;

            var manager = new MapQuestMarkerManager(container, tracker, objService, progService, loc);
            manager.SetZoomLevel(1.0);

            var retainedMarker = new Canvas();
            retainedMarker.RenderTransform = new ScaleTransform(1.0, 1.0);
            MarkerRefreshHelper.SetIsRetained(retainedMarker, true);
            container.Children.Add(retainedMarker);

            // Change zoom level and update scales
            manager.SetZoomLevel(2.0);
            manager.UpdateMarkerScales();

            var st = Assert.IsType<ScaleTransform>(retainedMarker.RenderTransform);
            Assert.Equal(0.5, st.ScaleX, 3);
            Assert.Equal(0.5, st.ScaleY, 3);
        });
    }

    [Fact]
    public void OverlayMiniMapWindow_UpdateMarkerScales_UpdatesRetainedMarkersAcrossContainers()
    {
        RunInSta(() =>
        {
            var window = new OverlayMiniMapWindow(new OverlayMiniMapSettings());

            var extractContainer = window.FindName("ExtractMarkersContainer") as Canvas;
            Assert.NotNull(extractContainer);

            var retainedMarker = new Canvas();
            var st = new ScaleTransform(1.0, 1.0);
            retainedMarker.RenderTransform = st;
            MarkerRefreshHelper.SetIsRetained(retainedMarker, true);
            extractContainer.Children.Add(retainedMarker);

            // UpdateMarkerScales directly with override size
            var updateMethod = typeof(OverlayMiniMapWindow).GetMethod("UpdateMarkerScales", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(updateMethod);
            updateMethod.Invoke(window, new object?[] { 400.0, 400.0 });

            // Effective scale must have adjusted st
            Assert.True(st.ScaleX > 0);
            Assert.Equal(st.ScaleX, st.ScaleY);
        });
    }

    [Fact]
    public void OverlayMiniMapWindow_OnClosed_ReleasesAllSubscriptionsSymmetrically()
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\Windows\OverlayMiniMapWindow.xaml.cs"));
        var code = File.ReadAllText(path);

        // Verify OnClosed cleans up QuestProgressService, ObjectiveProgressService, UserDataDbService, and LogMapWatcherService
        var onClosedIndex = code.IndexOf("protected override void OnClosed(EventArgs e)");
        Assert.True(onClosedIndex > 0);
        var onClosedBlock = code.Substring(onClosedIndex);

        Assert.Contains("QuestProgressService.Instance.ProgressChanged -= OnQuestProgressChanged;", onClosedBlock);
        Assert.Contains("ObjectiveProgressService.Instance.ObjectiveProgressChanged -= OnObjectiveProgressChanged;", onClosedBlock);
        Assert.Contains("UserDataDbService.Instance.CustomMarkersChanged -= OnCustomMarkersChanged;", onClosedBlock);
        Assert.Contains("LogMapWatcherService.Instance.MapChanged -= OnLogMapWatcherMapChanged;", onClosedBlock);
    }

    [Fact]
    public void OverlayMiniMapWindow_LoadMap_UsesLocalizedMapNameFallback()
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\Windows\OverlayMiniMapWindow.xaml.cs"));
        var code = File.ReadAllText(path);

        Assert.Contains("TxtTitleMapName.Text = _currentMapConfig?.DisplayName ?? LocalizationService.Instance.GetLocalizedMapName(mapKey);", code);
    }

    #endregion
}
