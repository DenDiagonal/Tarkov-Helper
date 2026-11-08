using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using TarkovHelper.Pages;
using TarkovHelper.Tests.Common;
using Xunit;

namespace TarkovHelper.Tests.Pages
{
    /// <summary>
    /// Tier 1: 5-Tab Main Navigation Verification Suite.
    /// Strictly derived from ORIGINAL_REQUEST.md (§R1) and PROJECT.md (§M1, §Interface Contracts).
    /// 
    /// Verifies:
    /// - MainWindow top navigation contains strictly 5 tabs in required canonical sequence.
    /// - TabCollector and TabMap are eliminated from the top navigation bar.
    /// - Storyline (WIP) placeholder page exists with valid theme and content.
    /// - Public navigation methods (NavigateToTasks, NavigateToMap, NavigateToNeededItems) are exposed.
    /// - TaskCardControl no longer references removed TabMap.
    /// - Backward-compatible property aliases exist to prevent breaking legacy consumers.
    /// </summary>
    public class Navigation5TabTests
    {
        private static string? GetAttr(XElement el, string name)
        {
            return el.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value;
        }

        [Fact]
        public void T1_01_TabNavigation_BorderHeight_IsStreamlinedTo28px()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "MainWindow.xaml");
            var tabBorder = doc.Descendants("Border")
                .FirstOrDefault(b => GetAttr(b, "Name") == "TabNavigation");

            Assert.NotNull(tabBorder);
            var heightAttr = (string?)tabBorder.Attribute("Height");
            Assert.NotNull(heightAttr);

            Assert.True(double.TryParse(heightAttr, out double height));
            Assert.True(height <= 32.0, $"TabNavigation height must be compact (<= 32px), but was {height}px.");
        }

        [Fact]
        public void T1_02_TabNavigation_ContainsExactlyFiveTabs()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "MainWindow.xaml");
            var tabBorder = doc.Descendants("Border")
                .FirstOrDefault(b => GetAttr(b, "Name") == "TabNavigation");

            Assert.NotNull(tabBorder);
            var radioButtons = tabBorder.Descendants("RadioButton").ToList();

            Assert.True(radioButtons.Count == 5,
                $"MainWindow top navigation must contain strictly 5 tabs, but found {radioButtons.Count}. " +
                $"Found tabs: {string.Join(", ", radioButtons.Select(r => GetAttr(r, "Name") ?? (string?)r.Attribute("Content")))}");
        }

        [Fact]
        public void T1_03_TabNavigation_Sequence_IsDashboardStorylineTasksNeededItemsHideout()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "MainWindow.xaml");
            var tabBorder = doc.Descendants("Border")
                .FirstOrDefault(b => GetAttr(b, "Name") == "TabNavigation");

            Assert.NotNull(tabBorder);
            var radioButtons = tabBorder.Descendants("RadioButton").ToList();
            Assert.Equal(5, radioButtons.Count);

            string GetName(XElement el) => GetAttr(el, "Name") ?? string.Empty;
            string GetContent(XElement el) => (string?)el.Attribute("Content") ?? string.Empty;

            // 1. Dashboard
            Assert.True(GetName(radioButtons[0]).Contains("Dashboard", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Dashboard", GetContent(radioButtons[0]));

            // 2. Storyline (WIP)
            Assert.True(GetName(radioButtons[1]).Contains("Storyline", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("Storyline", GetContent(radioButtons[1]), StringComparison.OrdinalIgnoreCase);

            // 3. Tasks (Misiones)
            Assert.True(GetName(radioButtons[2]).Contains("Tasks", StringComparison.OrdinalIgnoreCase) ||
                        GetName(radioButtons[2]).Contains("Quests", StringComparison.OrdinalIgnoreCase));
            Assert.True(GetContent(radioButtons[2]) == "Tasks" || GetContent(radioButtons[2]) == "Quests");

            // 4. Needed Items
            Assert.True(GetName(radioButtons[3]).Contains("NeededItems", StringComparison.OrdinalIgnoreCase) ||
                        GetName(radioButtons[3]).Contains("Items", StringComparison.OrdinalIgnoreCase));
            Assert.True(GetContent(radioButtons[3]) == "Needed Items" || GetContent(radioButtons[3]) == "Items");

            // 5. Hideout
            Assert.True(GetName(radioButtons[4]).Contains("Hideout", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Hideout", GetContent(radioButtons[4]));
        }

        [Fact]
        public void T1_04_TabCollector_IsCompletelyAbsentFromTopNavigation()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "MainWindow.xaml");
            var tabBorder = doc.Descendants("Border")
                .FirstOrDefault(b => GetAttr(b, "Name") == "TabNavigation");

            Assert.NotNull(tabBorder);
            var collectorTab = tabBorder.Descendants("RadioButton")
                .FirstOrDefault(r => (GetAttr(r, "Name") ?? string.Empty)
                    .Contains("Collector", StringComparison.OrdinalIgnoreCase));

            Assert.Null(collectorTab);
        }

        [Fact]
        public void T1_05_TabMap_IsCompletelyAbsentFromTopNavigation()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "MainWindow.xaml");
            var tabBorder = doc.Descendants("Border")
                .FirstOrDefault(b => GetAttr(b, "Name") == "TabNavigation");

            Assert.NotNull(tabBorder);
            var mapTab = tabBorder.Descendants("RadioButton")
                .FirstOrDefault(r => string.Equals(GetAttr(r, "Name"), "TabMap", StringComparison.OrdinalIgnoreCase));

            Assert.Null(mapTab);
        }

        [Fact]
        public void T1_06_StorylinePage_FileExists_AndIsConfiguredAsWipLandingPage()
        {
            var xamlPath = R1R2TestHelpers.ResolveRepoPath("TarkovHelper", "Pages", "StorylinePage.xaml");
            var csPath = R1R2TestHelpers.ResolveRepoPath("TarkovHelper", "Pages", "StorylinePage.xaml.cs");

            Assert.True(File.Exists(xamlPath), $"Expected StorylinePage.xaml to exist at '{xamlPath}'.");
            Assert.True(File.Exists(csPath), $"Expected StorylinePage.xaml.cs to exist at '{csPath}'.");

            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "StorylinePage.xaml");
            Assert.Equal("UserControl", doc.Root?.Name.LocalName);

            var textBlocks = doc.Descendants("TextBlock").Select(t => (string?)t.Attribute("Text") ?? t.Value).ToList();
            Assert.Contains(textBlocks, t => t.Contains("PROGRESS", StringComparison.OrdinalIgnoreCase) ||
                                             t.Contains("Storyline", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void T1_07_MainWindow_PublicNavigationContract_MethodsExist()
        {
            var mainWinType = typeof(MainWindow);

            // NavigateToTasks / NavigateToQuest
            var navTasks = mainWinType.GetMethod("NavigateToTasks", BindingFlags.Public | BindingFlags.Instance) ??
                           mainWinType.GetMethod("NavigateToQuest", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(navTasks);

            // NavigateToNeededItems / NavigateToItem
            var navItems = mainWinType.GetMethod("NavigateToNeededItems", BindingFlags.Public | BindingFlags.Instance) ??
                           mainWinType.GetMethod("NavigateToItem", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(navItems);

            // NavigateToMap (redirects to QuestListPage MAPS view)
            var navMap = mainWinType.GetMethod("NavigateToMap", BindingFlags.Public | BindingFlags.Instance) ??
                         mainWinType.GetMethod("NavigateToTaskOnMap", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(navMap);
        }

        [Fact]
        public void T1_08_MainWindow_BackwardCompatibility_PropertiesExist()
        {
            var mainWinType = typeof(MainWindow);

            // Confirm TabQuests and TabItems resolve to properties or fields
            var tabQuests = mainWinType.GetProperty("TabQuests", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic) ??
                            (MemberInfo?)mainWinType.GetField("TabQuests", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            var tabItems = mainWinType.GetProperty("TabItems", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic) ??
                           (MemberInfo?)mainWinType.GetField("TabItems", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(tabQuests);
            Assert.NotNull(tabItems);
        }

        [Fact]
        public void T1_09_TaskCardControl_MapClick_EliminatedDirectTabMapReference()
        {
            var taskCardCsPath = R1R2TestHelpers.ResolveRepoPath("TarkovHelper", "Controls", "TaskCardControl.xaml.cs");
            Assert.True(File.Exists(taskCardCsPath));

            var content = File.ReadAllText(taskCardCsPath);

            // Should NOT set mainWin.TabMap.IsChecked = true
            Assert.DoesNotContain("mainWin.TabMap.IsChecked = true", content);
        }

        [Fact]
        public void T1_10_ItemsPage_ContainsKappaPillTab()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "ItemsPage.xaml");
            var tabKappa = doc.Descendants("RadioButton")
                .FirstOrDefault(r => GetAttr(r, "Name") == "TabKappa");

            Assert.NotNull(tabKappa);

            var textBlocks = tabKappa.Descendants("TextBlock").Select(t => (string?)t.Attribute("Text") ?? t.Value).ToList();
            Assert.Contains(textBlocks, t => t.Contains("KAPPA", StringComparison.OrdinalIgnoreCase));
        }
    }
}
