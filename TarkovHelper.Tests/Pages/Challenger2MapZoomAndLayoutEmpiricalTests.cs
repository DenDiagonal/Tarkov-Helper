using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Xml.Linq;
using TarkovHelper.Common;
using TarkovHelper.Controls;
using TarkovHelper.Models;
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
    /// Empirical Adversarial Verification Suite executed by Challenger 2.
    /// Strictly verifies Requirement R4 and Tasks - Maps view integration:
    /// 1. Zoom Reset Button (BtnResetView):
    ///    - Preserves Content="⟳" (strictly 1 glyph, vector icon)
    ///    - Width="28", Height="28", Padding="0"
    ///    - ToolTip bound to localized string _loc.ResetViewTooltip
    ///    - Never clips or displays truncated text like "Res" or "Reset" in button content across all languages
    ///    - Resetting behaves correctly under extreme zooms, zero sizes, and rapid spamming
    /// 2. Adjacent Controls:
    ///    - ZoomIn, ZoomOut, ToggleFullScreen alignment, styles, and tooltips
    /// 3. Tasks - Maps View Layout & Sharpness:
    ///    - Display text formatting, ClearType, pixel snapping, layout rounding
    ///    - 5-level tactical surface palette conformance
    ///    - Floor selector overlay and single-line combined requirements strip
    ///    - Vertical stacking in QuestListPage (RowMapView 380px, RowSplitter Auto, RowTaskList 1*)
    /// 4. Empirical Vulnerability & Edge Case Stress:
    ///    - Demonstrates lifecycle/event subscription risks in MapPage.OnLanguageChanged
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class Challenger2MapZoomAndLayoutEmpiricalTests : IDisposable
    {
        private readonly AppLanguage _originalLanguage;
        private readonly string? _originalMap;
        private readonly string? _originalFloor;

        public Challenger2MapZoomAndLayoutEmpiricalTests()
        {
            _originalLanguage = LocalizationService.Instance.CurrentLanguage;
            _originalMap = MapTrackerService.Instance.CurrentMapKey;
            _originalFloor = MapTrackerService.Instance.CurrentFloorId;
        }

        public void Dispose()
        {
            RunInSta(() =>
            {
                LocalizationService.Instance.CurrentLanguage = _originalLanguage;
            });

            if (!string.IsNullOrEmpty(_originalMap))
            {
                MapTrackerService.Instance.SetCurrentMap(_originalMap, force: true);
            }
            MapTrackerService.Instance.SetCurrentFloor(_originalFloor);
        }

        #region STA Thread Test Runner Helper

        private static void RunInSta(Action action, int timeoutMs = 12000)
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
            if (!res.Contains("HeaderActionButtonStyle"))
            {
                var btnStyle = new Style(typeof(Button));
                btnStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x29, 0x2E, 0x38))));
                btnStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))));
                res["HeaderActionButtonStyle"] = btnStyle;
            }
        }

        private static void DetachMapPageEvents(MapPage? page)
        {
            if (page == null) return;
            try
            {
                var method = typeof(MapPage).GetMethod("OnLanguageChanged", BindingFlags.Instance | BindingFlags.NonPublic);
                if (method != null)
                {
                    var handler = (EventHandler<AppLanguage>)Delegate.CreateDelegate(typeof(EventHandler<AppLanguage>), page, method);
                    LocalizationService.Instance.LanguageChanged -= handler;
                }

                var unloaded = typeof(MapPage).GetMethod("MapTrackerPage_Unloaded", BindingFlags.Instance | BindingFlags.NonPublic);
                unloaded?.Invoke(page, new object[] { page, new RoutedEventArgs() });
            }
            catch { }
        }

        #endregion

        #region Focus 1: Map Zoom Reset Button Verification

        [Fact]
        public void C2_01_MapPageXaml_BtnResetView_DeclarativeAttributes_StrictlyPreserved()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");

            var btnReset = doc.Descendants("Button").FirstOrDefault(b => b.GetXamlName() == "BtnResetView");
            Assert.NotNull(btnReset);

            // 1. Content MUST be exactly "⟳"
            var content = btnReset.GetAttr("Content");
            Assert.Equal("⟳", content);

            // 2. Strict 28x28px dimensions with 0 padding to prevent text overflow
            Assert.Equal("28", btnReset.GetAttr("Width"));
            Assert.Equal("28", btnReset.GetAttr("Height"));
            Assert.Equal("0", btnReset.GetAttr("Padding"));

            // 3. Click handler must be BtnResetView_Click
            Assert.Equal("BtnResetView_Click", btnReset.GetAttr("Click"));

            // 4. Fallback ToolTip must exist
            Assert.False(string.IsNullOrWhiteSpace(btnReset.GetAttr("ToolTip")), "BtnResetView must have a declarative ToolTip.");
        }

        [Fact]
        public void C2_02_BtnResetView_ContentIsNeverOverwritten_AndNoClippingAcrossAllLanguages()
        {
            RunInSta(() =>
            {
                var page = new MapPage();
                try
                {
                    var btnReset = page.FindName("BtnResetView") as Button;
                    Assert.NotNull(btnReset);

                    // Content must start as "⟳"
                    Assert.Equal("⟳", btnReset.Content?.ToString());

                    var loc = LocalizationService.Instance;
                    var updateMethod = typeof(MapPage).GetMethod("UpdateLocalizedText", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(updateMethod);

                    // Test across all defined languages in AppLanguage
                    foreach (AppLanguage lang in Enum.GetValues(typeof(AppLanguage)))
                    {
                        loc.CurrentLanguage = lang;
                        updateMethod.Invoke(page, null);

                        // 1. Content MUST remain strictly "⟳" and NEVER text like "Reset", "Res", "Restablecer"
                        var currentContent = btnReset.Content?.ToString();
                        Assert.Equal("⟳", currentContent);
                        Assert.DoesNotContain("Res", currentContent ?? "");
                        Assert.DoesNotContain("Reset", currentContent ?? "");

                        // 2. ToolTip MUST be populated with the localized text
                        var tooltip = btnReset.ToolTip?.ToString();
                        Assert.False(string.IsNullOrWhiteSpace(tooltip));
                        Assert.Equal(loc.ResetViewTooltip, tooltip);
                    }
                }
                finally
                {
                    DetachMapPageEvents(page);
                }
            });
        }

        [Fact]
        public void C2_03_BtnResetView_Click_ResetsScaleTo100Percent_FromExtremeZooms()
        {
            RunInSta(() =>
            {
                var page = new MapPage();
                try
                {
                    var btnReset = page.FindName("BtnResetView") as Button;
                    Assert.NotNull(btnReset);

                    var mapScale = page.FindName("MapScale") as ScaleTransform;
                    var mapTranslate = page.FindName("MapTranslate") as TranslateTransform;
                    Assert.NotNull(mapScale);
                    Assert.NotNull(mapTranslate);

                    var clickMethod = typeof(MapPage).GetMethod("BtnResetView_Click", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(clickMethod);

                    double[] extremeZooms = new[] { 0.05, 0.1, 0.5, 1.8, 4.0, 8.0, 15.0 };

                    foreach (var z in extremeZooms)
                    {
                        // Apply zoom and random offset
                        mapScale.ScaleX = z;
                        mapScale.ScaleY = z;
                        mapTranslate.X = -3500;
                        mapTranslate.Y = 1200;

                        // Execute Reset View click
                        clickMethod.Invoke(page, new object[] { btnReset, new RoutedEventArgs() });

                        // Scale must strictly be 1.0
                        Assert.Equal(1.0, mapScale.ScaleX, precision: 3);
                        Assert.Equal(1.0, mapScale.ScaleY, precision: 3);
                    }
                }
                finally
                {
                    DetachMapPageEvents(page);
                }
            });
        }

        [Fact]
        public void C2_04_BtnResetView_RapidClickSpamming_ThreadSafetyAndStability()
        {
            RunInSta(() =>
            {
                var page = new MapPage();
                try
                {
                    var btnReset = page.FindName("BtnResetView") as Button;
                    var btnZoomIn = page.FindName("BtnZoomIn") as Button;
                    var btnZoomOut = page.FindName("BtnZoomOut") as Button;
                    Assert.NotNull(btnReset);
                    Assert.NotNull(btnZoomIn);
                    Assert.NotNull(btnZoomOut);

                    var resetClick = typeof(MapPage).GetMethod("BtnResetView_Click", BindingFlags.Instance | BindingFlags.NonPublic);
                    var inClick = typeof(MapPage).GetMethod("BtnZoomIn_Click", BindingFlags.Instance | BindingFlags.NonPublic);
                    var outClick = typeof(MapPage).GetMethod("BtnZoomOut_Click", BindingFlags.Instance | BindingFlags.NonPublic);

                    var mapScale = page.FindName("MapScale") as ScaleTransform;
                    Assert.NotNull(mapScale);

                    // Stress test: rapid alternation 100 times
                    for (int i = 0; i < 100; i++)
                    {
                        inClick?.Invoke(page, new object[] { btnZoomIn, new RoutedEventArgs() });
                        outClick?.Invoke(page, new object[] { btnZoomOut, new RoutedEventArgs() });
                        if (i % 5 == 0)
                        {
                            resetClick?.Invoke(page, new object[] { btnReset, new RoutedEventArgs() });
                            Assert.Equal(1.0, mapScale.ScaleX, precision: 3);
                        }
                    }

                    // Final reset
                    resetClick?.Invoke(page, new object[] { btnReset, new RoutedEventArgs() });
                    Assert.Equal(1.0, mapScale.ScaleX, precision: 3);
                    Assert.Equal(1.0, mapScale.ScaleY, precision: 3);
                }
                finally
                {
                    DetachMapPageEvents(page);
                }
            });
        }

        #endregion

        #region Focus 2: Adjacent Controls Verification

        [Fact]
        public void C2_05_AdjacentControls_DimensionsAndTooltips_AlignedInStack()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");

            var btnZoomIn = doc.Descendants("Button").FirstOrDefault(b => b.GetXamlName() == "BtnZoomIn");
            var btnZoomOut = doc.Descendants("Button").FirstOrDefault(b => b.GetXamlName() == "BtnZoomOut");
            var btnReset = doc.Descendants("Button").FirstOrDefault(b => b.GetXamlName() == "BtnResetView");
            var btnFull = doc.Descendants("Button").FirstOrDefault(b => b.GetXamlName() == "BtnToggleFullScreen");

            Assert.NotNull(btnZoomIn);
            Assert.NotNull(btnZoomOut);
            Assert.NotNull(btnReset);
            Assert.NotNull(btnFull);

            // All 4 buttons must share strict 28x28px compact tactical bounds
            var buttons = new[] { btnZoomIn, btnZoomOut, btnReset, btnFull };
            foreach (var b in buttons)
            {
                Assert.Equal("28", b.GetAttr("Width"));
                Assert.Equal("28", b.GetAttr("Height"));
                Assert.Equal("0", b.GetAttr("Padding"));
                Assert.Equal("{StaticResource HeaderActionButtonStyle}", b.GetAttr("Style"));
            }

            // Check Content glyphs
            Assert.Equal("+", btnZoomIn.GetAttr("Content"));
            Assert.Equal("−", btnZoomOut.GetAttr("Content"));
            Assert.Equal("⟳", btnReset.GetAttr("Content"));
            Assert.Equal("⛶", btnFull.GetAttr("Content"));
        }

        [Fact]
        public void C2_06_AdjacentControls_TooltipsLocalizedAcrossLanguages()
        {
            RunInSta(() =>
            {
                var page = new MapPage();
                try
                {
                    var btnZoomIn = page.FindName("BtnZoomIn") as Button;
                    var btnZoomOut = page.FindName("BtnZoomOut") as Button;
                    var btnReset = page.FindName("BtnResetView") as Button;
                    Assert.NotNull(btnZoomIn);
                    Assert.NotNull(btnZoomOut);
                    Assert.NotNull(btnReset);

                    var loc = LocalizationService.Instance;
                    var updateMethod = typeof(MapPage).GetMethod("UpdateLocalizedText", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(updateMethod);

                    foreach (AppLanguage lang in Enum.GetValues(typeof(AppLanguage)))
                    {
                        loc.CurrentLanguage = lang;
                        updateMethod.Invoke(page, null);

                        Assert.Equal(loc.ZoomInTooltip, btnZoomIn.ToolTip?.ToString());
                        Assert.Equal(loc.ZoomOutTooltip, btnZoomOut.ToolTip?.ToString());
                        Assert.Equal(loc.ResetViewTooltip, btnReset.ToolTip?.ToString());
                    }
                }
                finally
                {
                    DetachMapPageEvents(page);
                }
            });
        }

        #endregion

        #region Focus 3: Tasks - Maps View Layout & Sharpness Pipeline

        [Fact]
        public void C2_07_SharpnessPipeline_ConfiguredOnRootsAndViews()
        {
            // 1. MapPage
            var mapDoc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");
            Assert.Equal("Display", mapDoc.Root?.GetAttr("TextOptions.TextFormattingMode"));
            Assert.Equal("ClearType", mapDoc.Root?.GetAttr("TextOptions.TextRenderingMode"));
            Assert.Equal("Enabled", mapDoc.Root?.GetAttr("RenderOptions.ClearTypeHint"));
            Assert.Equal("True", mapDoc.Root?.GetAttr("SnapsToDevicePixels"));
            Assert.Equal("True", mapDoc.Root?.GetAttr("UseLayoutRounding"));

            // 2. QuestListPage
            var questDoc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "QuestListPage.xaml");
            Assert.Equal("Display", questDoc.Root?.GetAttr("TextOptions.TextFormattingMode"));
            Assert.Equal("ClearType", questDoc.Root?.GetAttr("TextOptions.TextRenderingMode"));
            Assert.Equal("Enabled", questDoc.Root?.GetAttr("RenderOptions.ClearTypeHint"));
            Assert.Equal("True", questDoc.Root?.GetAttr("SnapsToDevicePixels"));
            Assert.Equal("True", questDoc.Root?.GetAttr("UseLayoutRounding"));
        }

        [Fact]
        public void C2_08_TacticalBrushes_SurfacesAndBorders_DefinedAndFrozen()
        {
            // Validate TacticalBrushes runtime constants
            Assert.True(TacticalBrushes.Surface0.IsFrozen);
            Assert.True(TacticalBrushes.Surface1.IsFrozen);
            Assert.True(TacticalBrushes.Surface2.IsFrozen);
            Assert.True(TacticalBrushes.Surface3.IsFrozen);
            Assert.True(TacticalBrushes.Surface4.IsFrozen);
            Assert.True(TacticalBrushes.BorderSubtle.IsFrozen);
            Assert.True(TacticalBrushes.BorderMedium.IsFrozen);
            Assert.True(TacticalBrushes.BorderHighlight.IsFrozen);

            // Validate colors match EFT / TarkovTracker specifications
            Assert.Equal(Color.FromRgb(0x0D, 0x0E, 0x11), TacticalBrushes.Surface0.Color);
            Assert.Equal(Color.FromRgb(0x13, 0x15, 0x19), TacticalBrushes.Surface1.Color);
            Assert.Equal(Color.FromRgb(0x1A, 0x1D, 0x23), TacticalBrushes.Surface2.Color);
            Assert.Equal(Color.FromRgb(0x21, 0x25, 0x2D), TacticalBrushes.Surface3.Color);
            Assert.Equal(Color.FromRgb(0x29, 0x2E, 0x38), TacticalBrushes.Surface4.Color);
        }

        [Fact]
        public void C2_09_TasksMapsView_RequirementsStripAndFloorSelector_TacticalSurfaceBrushes()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");

            // Requirements strip
            var reqStrip = doc.Descendants("Border").FirstOrDefault(b => b.GetXamlName() == "MapRequirementsStripsPanel");
            Assert.NotNull(reqStrip);
            Assert.Equal("{StaticResource TacticalSurface1Brush}", reqStrip.GetAttr("Background"));
            Assert.Equal("{StaticResource TacticalBorderSubtleBrush}", reqStrip.GetAttr("BorderBrush"));

            // Overlay Floor Selector Panel
            var floorPanel = doc.Descendants("Border").FirstOrDefault(b => b.GetXamlName() == "OverlayFloorSelectorPanel");
            Assert.NotNull(floorPanel);
            var zIndex = floorPanel.GetAttr("Panel.ZIndex");
            Assert.True(int.TryParse(zIndex, out int z) && z >= 100, $"Floor selector ZIndex must be >= 100, found {zIndex}");
        }

        [Fact]
        public void C2_10_QuestListPage_VerticalLayoutStacking_InMapsMode()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "QuestListPage.xaml");

            var row5Grids = doc.Descendants("Grid").Where(g => g.GetAttr("Grid.Row") == "5").ToList();
            Assert.NotEmpty(row5Grids);
            var mainGrid = row5Grids.First();

            var mapContainer = mainGrid.Descendants("Border").FirstOrDefault(b => b.GetXamlName() == "EmbeddedMapContainer");
            Assert.NotNull(mapContainer);
            Assert.Equal("0", mapContainer.GetAttr("Grid.Row"));
            Assert.Equal("3", mapContainer.GetAttr("Grid.ColumnSpan"));

            var splitter = mainGrid.Descendants("GridSplitter").FirstOrDefault(s => s.GetXamlName() == "MapSplitter");
            Assert.NotNull(splitter);
            Assert.Equal("1", splitter.GetAttr("Grid.Row"));
            Assert.Equal("SizeNS", splitter.GetAttr("Cursor"));

            var taskListBorder = mainGrid.Descendants("Border").FirstOrDefault(b => b.Descendants().Any(d => d.GetXamlName() == "LstQuests"));
            Assert.NotNull(taskListBorder);
            Assert.Equal("2", taskListBorder.GetAttr("Grid.Row"));
        }

        #endregion

        #region Focus 4: Empirical Vulnerability Challenge

        [Fact]
        public void C2_11_MapPage_Unloaded_DoesNotUnsubscribeFromLanguageChanged_EmpiricalProof()
        {
            // Adversarial test exposing the vulnerability:
            // MapPage subscribes to LocalizationService.Instance.LanguageChanged in its constructor.
            // When MapTrackerPage_Unloaded is called, it fails to unsubscribe from LanguageChanged.
            MapPage? page = null;
            RunInSta(() =>
            {
                page = new MapPage();
                var unloaded = typeof(MapPage).GetMethod("MapTrackerPage_Unloaded", BindingFlags.Instance | BindingFlags.NonPublic);
                unloaded?.Invoke(page, new object[] { page, new RoutedEventArgs() });
            });

            Assert.NotNull(page);

            // Verify that LocalizationService.Instance.LanguageChanged invocation list still contains page.OnLanguageChanged
            var langEvent = typeof(LocalizationService).GetField("LanguageChanged", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var eventDelegate = langEvent?.GetValue(LocalizationService.Instance) as MulticastDelegate;
            Assert.NotNull(eventDelegate);

            var targets = eventDelegate.GetInvocationList().Select(d => d.Target).ToList();
            bool stillSubscribed = targets.Contains(page);

            // Clean up to keep other tests healthy
            DetachMapPageEvents(page);

            Assert.True(stillSubscribed,
                "VULNERABILITY CONFIRMED: MapPage does not unsubscribe from LocalizationService.Instance.LanguageChanged in MapTrackerPage_Unloaded, causing an event handler memory leak and potential cross-thread exceptions.");
        }

        #endregion
    }
}
