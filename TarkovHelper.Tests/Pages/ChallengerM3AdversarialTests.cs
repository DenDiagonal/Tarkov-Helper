using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using TarkovHelper.Models;
using TarkovHelper.Pages.Map;
using TarkovHelper.Pages.Map.ViewModels;
using TarkovHelper.Services;
using TarkovHelper.Tests.Common;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests.Pages
{
    /// <summary>
    /// Empirical Adversarial Verification Suite for Milestone 3 (Tasks MAPS View Layout Overhaul).
    /// Rigorously stress-tests:
    /// 1. Special Equipment Filtering: Strictly MS2000, Wi-Fi Camera, Signal Jammer allowed;
    ///    barter, medical, quest delivery, hideout, and false-positive trick items 100% rejected.
    /// 2. Key Badges Cleanness: Only key name rendered; no InStash borders, triggers, or status text.
    /// 3. Map Clock Widget Collapse: Visibility == Collapsed, _clockTimer stopped/null, zero CPU overhead.
    /// 4. Interactive SVG Canvas & Multi-Floor Switching: Pan/zoom transforms intact; overlay floor
    ///    selector synchronizes bidirectionally without recursion or null references.
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class ChallengerM3AdversarialTests
    {
        #region STA Thread Test Runner Helper

        private static void RunInSta(Action action, int timeoutMs = 8000)
        {
            Exception? caught = null;
            Application? createdApp = null;

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

        #endregion

        #region Focus Area 1: Special Equipment Filtering Edge Cases (Feature F11)

        [Fact]
        public void Empirical_SpecialEquipment_PermitsAllCanonicalKeywordsAndAliases()
        {
            var validSpecialEquipment = new List<(string Name, string? Id, string? Category)>
            {
                ("MS2000 Marker", "5991b51486f77447b112d44f", "Special equipment"),
                ("ms2000 marker", "5991b51486f77447b112d44f", "Special equipment"),
                ("ms2000_marker", null, "Special equipment"),
                ("MS2000_MARKER", "5991b51486f77447b112d44f", null),
                ("Wi-Fi Camera", "5b4391a586f7745321235ab2", "Special equipment"),
                ("wi-fi camera", null, "Special equipment"),
                ("wi-fi_camera", "5b4391a586f7745321235ab2", null),
                ("wifi camera", null, "Special equipment"),
                ("WIFI CAMERA", "5b4391a586f7745321235ab2", null),
                ("Signal Jammer", "5ac78a9b86f7741cca0bbd8d", "Special equipment"),
                ("signal jammer", null, "Special equipment"),
                ("signal_jammer", "5ac78a9b86f7741cca0bbd8d", null),
                ("SIGNAL JAMMER", null, "Special equipment"),
                // By canonical ID alone
                ("Unknown Placeable Item", "5991b51486f77447b112d44f", null),
                ("Unknown Camera", "5b4391a586f7745321235ab2", null),
                ("Unknown Jammer", "5ac78a9b86f7741cca0bbd8d", null)
            };

            foreach (var item in validSpecialEquipment)
            {
                bool qualifies = R1R2TestHelpers.IsSpecialEquipment(item.Name, item.Id, item.Category) ||
                                 NeededItemsViewModel.SpecialEquipmentNames.Contains(item.Name) ||
                                 NeededItemsViewModel.SpecialEquipmentNames.Any(s => item.Name.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                                 (item.Id != null && NeededItemsViewModel.SpecialEquipmentNames.Contains(item.Id));

                Assert.True(qualifies, $"Item '{item.Name}' (ID: {item.Id}, Category: {item.Category}) must qualify as placeable special equipment.");
            }
        }

        [Fact]
        public void Empirical_SpecialEquipment_RejectsAllGeneralBarterMedicalAndQuestDeliveryItems()
        {
            var nonSpecialEquipmentItems = new List<(string Name, string Id, string Category)>
            {
                // Medical Items
                ("Salewa first aid kit", "544fb2574bdc2dfc738b4567", "Medical"),
                ("Morphine injector", "544fb3f34bdc2d03748b456a", "Medical"),
                ("Propital", "5c0e530286f7747fa14198cc", "Medical"),
                ("CMS kit", "5d02778e86f774203e7dedbe", "Medical"),
                ("Surv12 field surgical kit", "5d02797c86f774202f38930a", "Medical"),
                ("Grizzly medical kit", "590c657e86f77412b013051d", "Medical"),
                ("Analgin painkillers", "544fb29a4bdc2dfc738b4568", "Medical"),
                ("Golden Star Balm", "5751578e24597720a27126da", "Medical"),
                ("Vaseline", "5751577d24597720a27126d9", "Medical"),
                ("Aseptic bandage", "544fb2f34bdc2d03748b4569", "Medical"),
                ("Immobilizing splint", "544fb3364bdc2d35048b456b", "Medical"),

                // Electronics & Barter Items
                ("Gas analyzer", "590a358486f77429692b2790", "Electronics"),
                ("Car battery", "590a3b0486f774395455a437", "Energy"),
                ("Spark plug", "590a3c9c86f7744654366627", "Electronics"),
                ("Wires", "5c0126f486f774013444f5ce", "Electronics"),
                ("Capacitors", "5c01285a86f774394e38ee18", "Electronics"),
                ("Printed circuit board", "590a3bd386f774379a0ce938", "Electronics"),
                ("Military flash drive", "5c052f6886f7746b1e3db148", "Electronics"),
                ("Secure Flash drive", "590a358486f77429692b2790", "Info"),
                ("Graphics card", "57347ca924597744596b4e71", "Electronics"),
                ("CPU fan", "573477e124597737dd0427b5", "Electronics"),
                ("Power supply unit", "57347c2e24597744902c94a1", "Electronics"),
                ("T-Shaped plug", "590a3ef786f77438491e4539", "Electronics"),
                ("Relay", "5c01262a86f774394d13e242", "Electronics"),
                ("Rechargeable battery", "590a3c7786f774395455a43b", "Energy"),

                // Valuables
                ("Bronze lion", "59e3556c86f77430bb577143", "Valuables"),
                ("Wooden clock", "59e35da186f7742dbe4a9a42", "Valuables"),
                ("Rolex submariner gold wrist watch", "59e35cbb86f7741778269d83", "Valuables"),
                ("Cat figurine", "59e358a886f7741776641ac2", "Valuables"),
                ("Horse figurine", "573475fb24597737fb1379e1", "Valuables"),
                ("Silver badge", "59e35a6f86f77444234ced7d", "Valuables"),
                ("Raven figurine", "5df8a42886f77412674e2d3b", "Valuables"),

                // Quest Delivery Documents & Items
                ("Secure Folder 0052", "5c0a0c2086f7742cf6551b94", "Quest item"),
                ("0052", "5c0a0c2086f7742cf6551b94", "Quest item"),
                ("Tarcone Director's Office Key", "5780cf7f2459777de4559322", "Keys"),
                ("Chemical - Part 4 documents", "5c0a0c2086f7742cf6551b94", "Quest item"),
                ("Sealed chemical container", "5c0a0c2086f7742cf6551b94", "Quest item"),
                ("Package with confidential materials", "5c0a0c2086f7742cf6551b94", "Quest item"),
                ("Military COFDM Wireless Signal Transmitter", "5c05300686f7746dce324d24", "Electronics"),
                ("TerraGroup Labs access keycard", "5c94bb9d86f7747ee735e08f", "Keys"),
                ("TerraGroup blue keycard", "5c1d0efb86f7744b2038435b", "Keys"),
                ("Tarkov mystery item", "590a358486f77429692b2799", "Quest item"),

                // Weapons & Gear
                ("MP-133 12ga shotgun", "54491c4f4bdc2db1078b4568", "Weapon"),
                ("6B43 Zabralo-ShST body armor", "545cdb794bdc2d34768b456e", "Armor"),
                ("Kiver-M helmet", "5645bcc04bdc2d363b8b4572", "Armor"),
                ("M4A1", "5447a9cd4bdc2db2078b4567", "Weapon"),
                ("AK-74N", "5ac4da2286f7742d02529b3f", "Weapon"),
                ("5.45x39mm BT gs", "5656d7c34bdc2d9d198b4587", "Ammo"),

                // Hideout Components
                ("Corrugated hose", "590a392486f7744654366632", "Barter item"),
                ("Water filter", "5d1b327086f774252510c635", "Barter item"),
                ("Air filter for absorber", "5d1b333086f77425233984cf", "Barter item"),
                ("Xenomorph sealing foam", "5c01287086f774394e38ee19", "Barter item"),
                ("Bolts", "57347c5b245977448d35f6e1", "Barter item"),
                ("Screw nut", "57347c93245977448d35f6e2", "Barter item"),
                ("Duct tape", "5734779624597737dd0427b3", "Barter item")
            };

            foreach (var item in nonSpecialEquipmentItems)
            {
                bool qualifiesHelper = R1R2TestHelpers.IsSpecialEquipment(item.Name, item.Id, item.Category);
                Assert.False(qualifiesHelper,
                    $"R1R2TestHelpers: Delivery/barter/medical item '{item.Name}' MUST be rejected from special equipment.");

                string norm = item.Name.Trim();
                bool qualifiesMapPage = NeededItemsViewModel.SpecialEquipmentNames.Contains(norm) ||
                                       NeededItemsViewModel.SpecialEquipmentNames.Any(s => norm.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                                       NeededItemsViewModel.SpecialEquipmentNames.Contains(item.Id) ||
                                       string.Equals(item.Category, "Special equipment", StringComparison.OrdinalIgnoreCase);

                Assert.False(qualifiesMapPage,
                    $"MapPage logic: Delivery/barter/medical item '{item.Name}' MUST be rejected from special equipment.");
            }
        }

        [Fact]
        public void Empirical_SpecialEquipment_AdversarialPartialNames_DoNotFalseTrigger()
        {
            // Tricky adversary items that contain parts of names ("camera", "marker", "jammer", "signal")
            // but are NOT the placeable raid gear
            var deceptiveNames = new[]
            {
                "Security Camera",
                "High-End Camera Lens",
                "Micro Camera Module",
                "Digital Camera",
                "Permanent Marker Pen",
                "Marker Paint Stick",
                "Felt Marker",
                "Radio Jammer Antenna",
                "Jammer Frequency Analyzer",
                "Disposable Jammer Battery",
                "Military Signal Transmitter",
                "GPS Signal Amplifier",
                "Wi-Fi Router Antennas",
                "Wi-Fi Dongle Adapter"
            };

            foreach (var deceptive in deceptiveNames)
            {
                string norm = deceptive.Trim();
                bool isSpecial = NeededItemsViewModel.SpecialEquipmentNames.Contains(norm) ||
                                 NeededItemsViewModel.SpecialEquipmentNames.Any(s => norm.Contains(s, StringComparison.OrdinalIgnoreCase));

                Assert.False(isSpecial,
                    $"Adversarial partial name '{deceptive}' must NOT trigger SpecialEquipment filter.");
            }
        }

        [Fact]
        public void Empirical_SpecialEquipment_WhitespaceAndDelimiters_Handling()
        {
            var paddedInputs = new[]
            {
                "   MS2000 Marker   ",
                "\t\nWi-Fi Camera\r\n",
                "   Signal Jammer   ",
                " ms2000_marker ",
                " wi-fi_camera ",
                " signal_jammer "
            };

            foreach (var padded in paddedInputs)
            {
                string norm = padded.Trim();
                bool isSpecial = NeededItemsViewModel.SpecialEquipmentNames.Contains(norm) ||
                                 NeededItemsViewModel.SpecialEquipmentNames.Any(s => norm.Contains(s, StringComparison.OrdinalIgnoreCase));

                Assert.True(isSpecial, $"Padded input '{padded}' should normalize and match special equipment.");
            }
        }

        [Fact]
        public void Empirical_MapPage_RequiredItemsFilter_SimulationOnTaskCollection()
        {
            // Adversarial raid items payload containing 2 placeable special equipment and 6 non-special items
            var raidItemsPayload = new List<QuestItem>
            {
                new() { ItemNormalizedName = "ms2000_marker", ItemDisplayName = "MS2000 Marker", Amount = 3 },
                new() { ItemNormalizedName = "salewa", ItemDisplayName = "Salewa first aid kit", Amount = 3, FoundInRaid = true },
                new() { ItemNormalizedName = "gas_analyzer", ItemDisplayName = "Gas Analyzer", Amount = 2 },
                new() { ItemNormalizedName = "wi-fi_camera", ItemDisplayName = "Wi-Fi Camera", Amount = 1 },
                new() { ItemNormalizedName = "secure_folder_0052", ItemDisplayName = "Secure Folder 0052", Amount = 1 },
                new() { ItemNormalizedName = "bronze_lion", ItemDisplayName = "Bronze lion", Amount = 1 },
                new() { ItemNormalizedName = "car_battery", ItemDisplayName = "Car battery", Amount = 4 },
                new() { ItemNormalizedName = "morphine_injector", ItemDisplayName = "Morphine injector", Amount = 4 }
            };

            var acceptedKeys = new List<string>();

            foreach (var it in raidItemsPayload)
            {
                var key = !string.IsNullOrEmpty(it.ItemNormalizedName) ? it.ItemNormalizedName : it.ItemDisplayName ?? "";
                if (string.IsNullOrWhiteSpace(key)) continue;

                string norm = (it.ItemNormalizedName ?? it.ItemDisplayName ?? "").Trim();
                bool isSpecial = NeededItemsViewModel.SpecialEquipmentNames.Contains(norm) ||
                                 NeededItemsViewModel.SpecialEquipmentNames.Any(s => norm.Contains(s, StringComparison.OrdinalIgnoreCase));

                if (isSpecial)
                {
                    acceptedKeys.Add(key);
                }
            }

            // Exactly 2 items accepted (MS2000 and Wi-Fi Camera)
            Assert.Equal(2, acceptedKeys.Count);
            Assert.Contains("ms2000_marker", acceptedKeys);
            Assert.Contains("wi-fi_camera", acceptedKeys);

            // General delivery items 100% rejected
            Assert.DoesNotContain("salewa", acceptedKeys);
            Assert.DoesNotContain("gas_analyzer", acceptedKeys);
            Assert.DoesNotContain("secure_folder_0052", acceptedKeys);
            Assert.DoesNotContain("bronze_lion", acceptedKeys);
            Assert.DoesNotContain("car_battery", acceptedKeys);
            Assert.DoesNotContain("morphine_injector", acceptedKeys);
        }

        #endregion

        #region Focus Area 2: Key Badges Cleanness (Feature F11)

        [Fact]
        public void Empirical_RequiredKeysList_DataTemplate_VisualTreeHasOnlyKeyName()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");

            var keysList = doc.Descendants().FirstOrDefault(e => e.GetXamlName() == "RequiredKeysList");
            Assert.NotNull(keysList);

            var keyTemplate = keysList.Descendants("DataTemplate").FirstOrDefault();
            Assert.NotNull(keyTemplate);

            // Count TextBlocks inside the DataTemplate
            var textBlocks = keyTemplate.Descendants("TextBlock").ToList();
            Assert.Single(textBlocks);

            var keyTextBlock = textBlocks[0];
            string? textBinding = keyTextBlock.GetAttr("Text");
            Assert.NotNull(textBinding);
            Assert.Contains("DisplayName", textBinding);
        }

        [Fact]
        public void Empirical_RequiredKeysList_ZeroStashIndicatorsOrTriggers()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");

            var keysList = doc.Descendants().FirstOrDefault(e => e.GetXamlName() == "RequiredKeysList");
            Assert.NotNull(keysList);

            var keyTemplate = keysList.Descendants("DataTemplate").FirstOrDefault();
            Assert.NotNull(keyTemplate);

            // Assert NO triggers exist inside the DataTemplate
            var triggers = keyTemplate.Descendants().Where(e => e.Name.LocalName.Contains("Trigger")).ToList();
            Assert.Empty(triggers);

            // Assert NO element or attribute contains InStash or StashStatusText
            var stashAttributes = keyTemplate.Descendants()
                .SelectMany(e => e.Attributes())
                .Where(a => a.Value.Contains("InStash", StringComparison.OrdinalIgnoreCase) ||
                            a.Value.Contains("StashStatusText", StringComparison.OrdinalIgnoreCase) ||
                            a.Value.Contains("IsInStash", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Empty(stashAttributes);

            // Assert NO text elements contain "In Stash", "Missing", etc.
            var literalTexts = keyTemplate.Descendants()
                .Select(e => e.GetAttr("Text") ?? e.Value)
                .Where(t => t.Contains("In Stash", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("Missing", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("Owned", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Empty(literalTexts);

            // Assert NO green or red border styling indicative of stash possession
            var borderBrushes = keyTemplate.Descendants("Border")
                .Select(b => b.GetAttr("BorderBrush"))
                .Where(b => !string.IsNullOrEmpty(b))
                .ToList();

            foreach (var brush in borderBrushes)
            {
                Assert.DoesNotContain("Green", brush, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Red", brush, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("#2E7D32", brush, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("#C62828", brush, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void Empirical_MapActiveKeyStripViewModel_CleanRenderingWithoutBadges()
        {
            var keyInStash = new MapActiveKeyStripViewModel
            {
                KeyName = "Customs Office Key",
                ShortName = "Customs Office Key",
                InStash = true
            };

            var keyMissing = new MapActiveKeyStripViewModel
            {
                KeyName = "Dorm room 314 marked key",
                ShortName = "Dorm room 314 marked key",
                InStash = false
            };

            Assert.Equal("Customs Office Key", keyInStash.DisplayName);
            Assert.Equal("Dorm room 314 marked key", keyMissing.DisplayName);
            // Both models expose DisplayName cleanly without stash badge decorators
            Assert.True(keyInStash.InStash);
            Assert.False(keyMissing.InStash);
        }

        #endregion

        #region Focus Area 3: Map Clock Widget Collapse (Feature F9)

        [Fact]
        public void Empirical_MapClockWidget_Xaml_DeclaredAsCollapsed()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");

            var clockWidget = doc.Descendants("Border")
                .FirstOrDefault(b => b.GetXamlName() == "TarkovClockWidget");

            Assert.NotNull(clockWidget);
            string? visibility = clockWidget.GetAttr("Visibility");
            Assert.Equal("Collapsed", visibility);
        }

        [Fact]
        public void Empirical_MapClockWidget_Instance_VisibilityAndTimerStopped_WhenEmbedded()
        {
            RunInSta(() =>
            {
                var page = new MapPage();
                Assert.NotNull(page);

                var clockBorder = page.FindName("TarkovClockWidget") as Border;
                Assert.NotNull(clockBorder);
                Assert.Equal(Visibility.Collapsed, clockBorder.Visibility);

                page.SetEmbeddedMode(true);

                Assert.Equal(Visibility.Collapsed, clockBorder.Visibility);

                var timerField = typeof(MapPage).GetField("_clockTimer", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(timerField);

                var timer = timerField.GetValue(page) as DispatcherTimer;
                Assert.True(timer == null || !timer.IsEnabled,
                    "MapPage _clockTimer must be null or IsEnabled == false when embedded or collapsed.");
            });
        }

        [Fact]
        public void Empirical_MapClockWidget_InitializeClockWidget_GuardPreventsTimerStartWhenCollapsed()
        {
            RunInSta(() =>
            {
                var page = new MapPage();
                var clockBorder = page.FindName("TarkovClockWidget") as Border;
                Assert.NotNull(clockBorder);
                clockBorder.Visibility = Visibility.Collapsed;

                var initMethod = typeof(MapPage).GetMethod("InitializeClockWidget", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(initMethod);

                initMethod.Invoke(page, null);

                var timerField = typeof(MapPage).GetField("_clockTimer", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(timerField);

                var timer = timerField.GetValue(page) as DispatcherTimer;
                Assert.True(timer == null || !timer.IsEnabled,
                    "_clockTimer should remain null or disabled after InitializeClockWidget() when widget is Collapsed.");
            });
        }

        [Fact]
        public void Empirical_MapClockWidget_SetEmbeddedMode_ForcesStopEvenIfTimerWasActive()
        {
            RunInSta(() =>
            {
                var page = new MapPage();

                var timerField = typeof(MapPage).GetField("_clockTimer", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(timerField);

                var activeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                activeTimer.Start();
                Assert.True(activeTimer.IsEnabled);

                timerField.SetValue(page, activeTimer);

                page.SetEmbeddedMode(true);

                Assert.False(activeTimer.IsEnabled, "SetEmbeddedMode(true) must force stop an active _clockTimer.");
            });
        }

        #endregion

        #region Focus Area 4: Interactive SVG Canvas & Multi-Floor Switching (Feature F10)

        [Fact]
        public void Empirical_MapCanvas_PanZoomTransformsIntact()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");

            var mapCanvas = doc.Descendants("Canvas")
                .FirstOrDefault(c => c.GetXamlName() == "MapCanvas");
            Assert.NotNull(mapCanvas);

            var transformGroup = mapCanvas.Descendants("TransformGroup").FirstOrDefault();
            Assert.NotNull(transformGroup);

            var scaleTransform = transformGroup.Descendants("ScaleTransform").FirstOrDefault();
            Assert.NotNull(scaleTransform);
            Assert.Equal("MapScale", scaleTransform.GetXamlName());

            var translateTransform = transformGroup.Descendants("TranslateTransform").FirstOrDefault();
            Assert.NotNull(translateTransform);
            Assert.Equal("MapTranslate", translateTransform.GetXamlName());

            RunInSta(() =>
            {
                var page = new MapPage();
                var canvas = page.FindName("MapCanvas") as Canvas;
                Assert.NotNull(canvas);

                Assert.IsType<TransformGroup>(canvas.RenderTransform);
                var tg = (TransformGroup)canvas.RenderTransform;

                var st = tg.Children.OfType<ScaleTransform>().FirstOrDefault();
                var tt = tg.Children.OfType<TranslateTransform>().FirstOrDefault();

                Assert.NotNull(st);
                Assert.NotNull(tt);

                // Stress pan and zoom manipulations
                st.ScaleX = 2.5;
                st.ScaleY = 2.5;
                tt.X = -150.0;
                tt.Y = 275.5;

                Assert.Equal(2.5, st.ScaleX);
                Assert.Equal(2.5, st.ScaleY);
                Assert.Equal(-150.0, tt.X);
                Assert.Equal(275.5, tt.Y);
            });
        }

        [Fact]
        public void Empirical_MultiFloorSelector_OverlayPanelAndComboBox_ExistInViewerGrid()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "Map", "MapPage.xaml");

            var viewerGrid = doc.Descendants("Grid")
                .FirstOrDefault(g => g.GetXamlName() == "MapViewerGrid");
            Assert.NotNull(viewerGrid);

            var overlayPanel = viewerGrid.Descendants("Border")
                .FirstOrDefault(b => b.GetXamlName() == "OverlayFloorSelectorPanel");
            Assert.NotNull(overlayPanel);

            var overlayCombo = overlayPanel.Descendants("ComboBox")
                .FirstOrDefault(c => c.GetXamlName() == "CmbOverlayFloorSelect");
            Assert.NotNull(overlayCombo);
        }

        [Fact]
        public void Empirical_MultiFloorSelector_BidirectionalSynchronization()
        {
            RunInSta(() =>
            {
                var page = new MapPage();

                var cmbFloor = page.FindName("CmbFloorSelect") as ComboBox;
                var cmbOverlay = page.FindName("CmbOverlayFloorSelect") as ComboBox;
                var overlayPanel = page.FindName("OverlayFloorSelectorPanel") as Border;

                Assert.NotNull(cmbFloor);
                Assert.NotNull(cmbOverlay);
                Assert.NotNull(overlayPanel);

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
                Assert.Equal(Visibility.Collapsed, overlayPanel.Visibility);
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
        public void Empirical_MultiFloorSelector_ZeroFloors_GracefullyHidesOverlay()
        {
            RunInSta(() =>
            {
                var page = new MapPage();

                var cmbFloor = page.FindName("CmbFloorSelect") as ComboBox;
                var overlayPanel = page.FindName("OverlayFloorSelectorPanel") as Border;

                Assert.NotNull(cmbFloor);
                Assert.NotNull(overlayPanel);

                cmbFloor.Items.Clear();
                cmbFloor.Visibility = Visibility.Collapsed;

                var syncMethod = typeof(MapPage).GetMethod("SyncPillFloorSelector", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(syncMethod);
                syncMethod.Invoke(page, null);

                Assert.Equal(Visibility.Collapsed, overlayPanel.Visibility);
            });
        }

        [Fact]
        public void Empirical_MultiFloorSelector_RapidSwitching_NoRecursionOrNullRef()
        {
            RunInSta(() =>
            {
                var page = new MapPage();

                var cmbFloor = page.FindName("CmbFloorSelect") as ComboBox;
                var cmbOverlay = page.FindName("CmbOverlayFloorSelect") as ComboBox;

                Assert.NotNull(cmbFloor);
                Assert.NotNull(cmbOverlay);

                cmbFloor.Items.Add(new ComboBoxItem { Content = "Floor 0", Tag = "f0" });
                cmbFloor.Items.Add(new ComboBoxItem { Content = "Floor 1", Tag = "f1" });
                cmbFloor.Items.Add(new ComboBoxItem { Content = "Floor 2", Tag = "f2" });
                cmbFloor.Visibility = Visibility.Visible;
                cmbFloor.SelectedIndex = 0;

                var syncMethod = typeof(MapPage).GetMethod("SyncPillFloorSelector", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(syncMethod);
                syncMethod.Invoke(page, null);

                // Rapidly alternate selections to stress test bidirectional event handlers
                int[] sequence = { 1, 2, 0, 1, 0, 2, 1, 0 };
                foreach (int targetIndex in sequence)
                {
                    cmbOverlay.SelectedIndex = targetIndex;
                    Assert.Equal(targetIndex, cmbFloor.SelectedIndex);
                }
            });
        }

        #endregion
    }
}
