using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TarkovHelper.Models.Map;
using TarkovHelper.Pages.Map;
using TarkovHelper.Pages.Map.Components;
using TarkovHelper.Services;
using TarkovHelper.Services.Map;
using TarkovHelper.Services.Settings;
using Xunit;

namespace TarkovHelper.Tests.Pages
{
    [Collection("DatabaseUpdateServiceCollection")]
    public class MapExtractRegressionTests : IDisposable
    {
        private readonly double _originalExtractNameSize;
        private readonly bool _originalShowPmcExtracts;
        private readonly bool _originalShowScavExtracts;
        private readonly bool _originalShowTransits;
        private readonly PerMapConfig _originalCustomsConfig;

        public MapExtractRegressionTests()
        {
            _originalExtractNameSize = SettingsService.Instance.MapExtractNameSize;
            _originalShowPmcExtracts = SettingsService.Instance.MapShowPmcExtracts;
            _originalShowScavExtracts = SettingsService.Instance.MapShowScavExtracts;
            _originalShowTransits = SettingsService.Instance.MapShowTransits;
            _originalCustomsConfig = MapSettings.Instance.GetPerMapConfig("Customs");
        }

        public void Dispose()
        {
            SettingsService.Instance.MapExtractNameSize = _originalExtractNameSize;
            SettingsService.Instance.MapShowPmcExtracts = _originalShowPmcExtracts;
            SettingsService.Instance.MapShowScavExtracts = _originalShowScavExtracts;
            SettingsService.Instance.MapShowTransits = _originalShowTransits;
            MapSettings.Instance.SavePerMapConfig("Customs", _originalCustomsConfig);
        }

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
                throw new TimeoutException($"STA test timed out after {timeoutMs}ms");
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

            if (!res.Contains("BorderBrush")) res["BorderBrush"] = new SolidColorBrush(Color.FromRgb(0x3d, 0x3d, 0x3d));
            if (!res.Contains("BackgroundDarkBrush")) res["BackgroundDarkBrush"] = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a));
            if (!res.Contains("BackgroundMediumBrush")) res["BackgroundMediumBrush"] = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25));
            if (!res.Contains("BackgroundLightBrush")) res["BackgroundLightBrush"] = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x2d));
            if (!res.Contains("TextPrimaryBrush")) res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0));
            if (!res.Contains("TextSecondaryBrush")) res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x9e, 0x9e, 0x9e));
            if (!res.Contains("AccentBrush")) res["AccentBrush"] = new SolidColorBrush(Color.FromRgb(0xc5, 0xa8, 0x4a));
            if (!res.Contains("SuccessBrush")) res["SuccessBrush"] = new SolidColorBrush(Color.FromRgb(0x4c, 0xaf, 0x50));
            if (!res.Contains("WarningBrush")) res["WarningBrush"] = new SolidColorBrush(Color.FromRgb(0xff, 0x98, 0x00));
            if (!res.Contains("BoolToVisibilityConverter")) res["BoolToVisibilityConverter"] = new BooleanToVisibilityConverter();
            if (!res.Contains("NullToVisibilityConverter")) res["NullToVisibilityConverter"] = new NullToVisibilityConverter();
        }

        [Fact]
        public void ExtractNameSize_ClampsCompactRange_AndDefaultLabelIsCompact()
        {
            SettingsService.Instance.MapExtractNameSize = 5.0;
            Assert.Equal(7.0, SettingsService.Instance.MapExtractNameSize);

            SettingsService.Instance.MapExtractNameSize = 10.0;
            Assert.Equal(10.0, SettingsService.Instance.MapExtractNameSize);

            RunInSta(() =>
            {
                var canvas = new Canvas();
                var manager = new MapExtractMarkerManager(
                    canvas,
                    MapTrackerService.Instance,
                    ExtractService.Instance,
                    LocalizationService.Instance);

                manager.SetCurrentMap("Customs");
                manager.SetExtractNameTextSize(10.0);

                var createMethod = typeof(MapExtractMarkerManager).GetMethod(
                    "CreateExtractMarker",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(createMethod);

                var extract = new MapExtract
                {
                    Id = "test_extract",
                    Name = "Railroad Passage",
                    Faction = ExtractFaction.Pmc,
                    X = 0,
                    Z = 0
                };

                var marker = createMethod!.Invoke(manager, new object?[]
                {
                    extract,
                    new ScreenPosition { MapKey = "Customs", X = 100, Y = 100 },
                    null,
                    true
                }) as Canvas;

                Assert.NotNull(marker);

                TextBlock? labelText = null;
                Border? labelBorder = null;
                foreach (var child in marker!.Children)
                {
                    if (child is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is Border b)
                    {
                        labelBorder = b;
                        labelText = b.Child as TextBlock;
                    }
                }

                Assert.NotNull(labelBorder);
                Assert.NotNull(labelText);
                Assert.True(labelText!.FontSize <= 10.0, $"Expected compact font size <= 10.0, got {labelText.FontSize}");
                Assert.Equal(FontWeights.SemiBold, labelText.FontWeight);
                Assert.True(labelBorder!.Padding.Top <= 1.5, $"Expected compact vertical padding <= 1.5, got {labelBorder.Padding.Top}");
            });
        }

        [Fact]
        public void MapSettings_StartupLoad_ResetsOverlayTogglesToQuestsOnly_WhilePreservingZoomPan()
        {
            var cfgWithOverlaysAndZoom = new PerMapConfig
            {
                ShowPmcExtracts = true,
                ShowScavExtracts = true,
                ShowSpawns = true,
                ShowBosses = true,
                ZoomLevel = 0.42,
                TranslateX = 123.5,
                TranslateY = -45.0
            };
            MapSettings.Instance.SavePerMapConfig("Customs", cfgWithOverlaysAndZoom);
            SettingsService.Instance.MapShowPmcExtracts = true;
            SettingsService.Instance.MapShowScavExtracts = true;
            SettingsService.Instance.MapShowTransits = true;

            var loadMethod = typeof(MapSettings).GetMethod("LoadSettings", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(loadMethod);
            loadMethod!.Invoke(MapSettings.Instance, null);

            Assert.False(SettingsService.Instance.MapShowPmcExtracts);
            Assert.False(SettingsService.Instance.MapShowScavExtracts);
            Assert.False(SettingsService.Instance.MapShowTransits);

            var reloaded = MapSettings.Instance.GetPerMapConfig("Customs");
            Assert.False(reloaded.ShowPmcExtracts);
            Assert.False(reloaded.ShowScavExtracts);
            Assert.False(reloaded.ShowSpawns);
            Assert.False(reloaded.ShowBosses);
            Assert.Equal(0.42, reloaded.ZoomLevel, 2);
            Assert.Equal(123.5, reloaded.TranslateX, 2);
            Assert.Equal(-45.0, reloaded.TranslateY, 2);
        }

        [Fact]
        public void MapPage_InitialAndPerMapLoad_KeepsExtractsHiddenWhenButtonsAreUnchecked()
        {
            // Simulate stale global settings being true while per-map config is default (false)
            SettingsService.Instance.MapShowPmcExtracts = true;
            SettingsService.Instance.MapShowScavExtracts = true;
            SettingsService.Instance.MapShowTransits = true;

            MapSettings.Instance.SavePerMapConfig("Customs", new PerMapConfig
            {
                ShowPmcExtracts = false,
                ShowScavExtracts = false,
                ShowSpawns = false
            });

            RunInSta(() =>
            {
                var page = new MapPage();
                page.SetEmbeddedMode(true);

                var btnPmc = page.FindName("BtnTogglePmcExtracts") as ToggleButton;
                var btnScav = page.FindName("BtnToggleScavExtracts") as ToggleButton;
                var btnSpawns = page.FindName("BtnToggleSpawns") as ToggleButton;

                Assert.NotNull(btnPmc);
                Assert.NotNull(btnScav);
                Assert.NotNull(btnSpawns);

                // XAML defaults must start unchecked
                Assert.False(btnPmc!.IsChecked);
                Assert.False(btnScav!.IsChecked);
                Assert.False(btnSpawns!.IsChecked);

                // Simulate QuestListPage calling SelectMap before Loaded, then LoadSettings + LoadAndApplyPerMapConfig
                page.SelectMap("customs");

                var loadSettingsMethod = typeof(MapPage).GetMethod("LoadSettings", BindingFlags.Instance | BindingFlags.NonPublic);
                var applyPerMapMethod = typeof(MapPage).GetMethod("LoadAndApplyPerMapConfig", BindingFlags.Instance | BindingFlags.Public);
                Assert.NotNull(loadSettingsMethod);
                Assert.NotNull(applyPerMapMethod);

                loadSettingsMethod!.Invoke(page, null);
                applyPerMapMethod!.Invoke(page, new object[] { "Customs" });

                var showPmcField = typeof(MapPage).GetField("_showPmcExtracts", BindingFlags.Instance | BindingFlags.NonPublic);
                var showScavField = typeof(MapPage).GetField("_showScavExtracts", BindingFlags.Instance | BindingFlags.NonPublic);
                var showTransitField = typeof(MapPage).GetField("_showTransitExtracts", BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.False((bool)showPmcField!.GetValue(page)!);
                Assert.False((bool)showScavField!.GetValue(page)!);
                Assert.False((bool)showTransitField!.GetValue(page)!);
                Assert.False(btnPmc.IsChecked);
                Assert.False(btnScav.IsChecked);

                // First click should toggle ON on the very first click
                btnPmc.IsChecked = true;
                var clickMethod = typeof(MapPage).GetMethod("BtnTogglePmcExtracts_Click", BindingFlags.Instance | BindingFlags.NonPublic);
                clickMethod!.Invoke(page, new object[] { btnPmc, new RoutedEventArgs() });
                Assert.True((bool)showPmcField.GetValue(page)!);
                Assert.True((bool)showTransitField.GetValue(page)!);

                // Second click should toggle OFF cleanly (not require an extra click)
                btnPmc.IsChecked = false;
                clickMethod.Invoke(page, new object[] { btnPmc, new RoutedEventArgs() });
                Assert.False((bool)showPmcField.GetValue(page)!);
                Assert.False((bool)showTransitField.GetValue(page)!);
            });
        }
    }
}
