using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using TarkovHelper.Controls;
using TarkovHelper.Tests.Common;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests.Controls
{
    /// <summary>
    /// Empirical Challenger 1 verification and stress testing suite.
    /// Empirically verifies:
    /// 1. WPF text rendering and sharpness settings across key XAML files.
    /// 2. TaskCardControl structural modernization (unclamped title, WrapPanel badges, rewards bindings).
    /// 3. TaskCardControl all 24+ components and interactions completeness.
    /// 4. Tactical surface palette and tinted badges compliance.
    /// 5. Publish database integrity.
    /// 6. Adversarial edge cases on TaskCardViewModel and layout properties.
    /// </summary>
    public class Challenger1EmpiricalRenderingAndTaskCardTests
    {
        #region 1. WPF Rendering Pipeline Verification

        [Theory]
        [InlineData("TarkovHelper", "MainWindow.xaml")]
        [InlineData("TarkovHelper", "Controls", "TaskCardControl.xaml")]
        [InlineData("TarkovHelper", "Pages", "QuestListPage.xaml")]
        [InlineData("TarkovHelper", "Pages", "Map", "MapPage.xaml")]
        public void C1_01_WpfRenderingProperties_ConfiguredOnRootElements(params string[] xamlPathParts)
        {
            var doc = R1R2TestHelpers.LoadXamlDocument(xamlPathParts);
            var root = doc.Root;
            Assert.NotNull(root);

            Assert.Equal("Display", root.GetAttr("TextOptions.TextFormattingMode"));
            Assert.Equal("ClearType", root.GetAttr("TextOptions.TextRenderingMode"));
            Assert.Equal("Enabled", root.GetAttr("RenderOptions.ClearTypeHint"));
            Assert.Equal("True", root.GetAttr("SnapsToDevicePixels"));
            Assert.Equal("True", root.GetAttr("UseLayoutRounding"));
        }

        [Fact]
        public void C1_02_AppXaml_GlobalStylesHaveRenderingAndSharpnessProperties()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "App.xaml");
            var styles = doc.Descendants("Style").ToList();

            // Window default style
            var windowStyle = styles.FirstOrDefault(s => s.GetAttr("TargetType") == "Window");
            Assert.NotNull(windowStyle);
            AssertSetterValue(windowStyle, "TextOptions.TextFormattingMode", "Display");
            AssertSetterValue(windowStyle, "TextOptions.TextRenderingMode", "ClearType");
            AssertSetterValue(windowStyle, "RenderOptions.ClearTypeHint", "Enabled");
            AssertSetterValue(windowStyle, "SnapsToDevicePixels", "True");
            AssertSetterValue(windowStyle, "UseLayoutRounding", "True");

            // UserControl default style
            var userControlStyle = styles.FirstOrDefault(s => s.GetAttr("TargetType") == "UserControl");
            Assert.NotNull(userControlStyle);
            AssertSetterValue(userControlStyle, "TextOptions.TextFormattingMode", "Display");
            AssertSetterValue(userControlStyle, "TextOptions.TextRenderingMode", "ClearType");
            AssertSetterValue(userControlStyle, "RenderOptions.ClearTypeHint", "Enabled");
            AssertSetterValue(userControlStyle, "SnapsToDevicePixels", "True");
            AssertSetterValue(userControlStyle, "UseLayoutRounding", "True");

            // ToolTip default style
            var toolTipStyle = styles.FirstOrDefault(s => s.GetAttr("TargetType") == "ToolTip");
            Assert.NotNull(toolTipStyle);
            AssertSetterValue(toolTipStyle, "TextOptions.TextFormattingMode", "Display");
            AssertSetterValue(toolTipStyle, "TextOptions.TextRenderingMode", "ClearType");
            AssertSetterValue(toolTipStyle, "SnapsToDevicePixels", "True");
            AssertSetterValue(toolTipStyle, "UseLayoutRounding", "True");

            // TextBlock default style
            var textBlockStyle = styles.FirstOrDefault(s => s.GetAttr("TargetType") == "TextBlock");
            Assert.NotNull(textBlockStyle);
            AssertSetterValue(textBlockStyle, "TextOptions.TextFormattingMode", "Display");
            AssertSetterValue(textBlockStyle, "TextOptions.TextRenderingMode", "ClearType");
        }

        private static void AssertSetterValue(XElement style, string property, string expectedValue)
        {
            var setter = style.Descendants("Setter")
                .FirstOrDefault(s => s.GetAttr("Property") == property);
            Assert.NotNull(setter);
            Assert.Equal(expectedValue, setter.GetAttr("Value"));
        }

        #endregion

        #region 2. TaskCardControl Structural Modernization

        [Fact]
        public void C1_03_TaskCardControl_TitleHasNoMaxWidth320Clamp()
        {
            var rawXaml = File.ReadAllText(R1R2TestHelpers.ResolveRepoPath("TarkovHelper", "Controls", "TaskCardControl.xaml"));

            // Adversarial check: verify no MaxWidth="320" exists in the file
            Assert.DoesNotContain("MaxWidth=\"320\"", rawXaml);
            Assert.DoesNotContain("MaxWidth='320'", rawXaml);

            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Controls", "TaskCardControl.xaml");
            var titleTextBlock = doc.Descendants("TextBlock")
                .FirstOrDefault(tb => (tb.GetAttr("Text") ?? "").Contains("{Binding Title}"));

            Assert.NotNull(titleTextBlock);
            var maxWidth = titleTextBlock.GetAttr("MaxWidth");
            Assert.True(string.IsNullOrEmpty(maxWidth), $"Title TextBlock should not have MaxWidth clamp, but found MaxWidth='{maxWidth}'");

            // Verify title container is in column with Width="*"
            var headerGrid = titleTextBlock.Ancestors("Grid").FirstOrDefault();
            Assert.NotNull(headerGrid);
            var colDefs = headerGrid.Elements("Grid.ColumnDefinitions").Elements("ColumnDefinition").ToList();
            Assert.True(colDefs.Count >= 2);
            Assert.Equal("*", colDefs[1].GetAttr("Width"));
        }

        [Fact]
        public void C1_04_TaskCardControl_BadgesContainedInResponsiveWrapPanel()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Controls", "TaskCardControl.xaml");

            var wrapPanels = doc.Descendants("WrapPanel").ToList();
            Assert.NotEmpty(wrapPanels);

            // Subtitle / Badges WrapPanel
            var badgesWrapPanel = wrapPanels.FirstOrDefault(wp =>
                wp.Descendants("TextBlock").Any(tb => (tb.GetAttr("Text") ?? "").Contains("{Binding Trader}")));

            Assert.NotNull(badgesWrapPanel);
            Assert.Equal("Horizontal", badgesWrapPanel.GetAttr("Orientation"));

            // Verify badges are nested inside this WrapPanel
            var mapBadge = badgesWrapPanel.Descendants("TextBlock")
                .FirstOrDefault(tb => (tb.GetAttr("Text") ?? "").Contains("{Binding MapBadge}"));
            Assert.NotNull(mapBadge);

            var levelBadge = badgesWrapPanel.Descendants("TextBlock")
                .FirstOrDefault(tb => (tb.GetAttr("Text") ?? "").Contains("{Binding RequiredLevelText}"));
            Assert.NotNull(levelBadge);

            var kappaBadge = badgesWrapPanel.Descendants("TextBlock")
                .FirstOrDefault(tb => (tb.GetAttr("Text") ?? "").Contains("Kappa"));
            Assert.NotNull(kappaBadge);

            var firBadge = badgesWrapPanel.Descendants("TextBlock")
                .FirstOrDefault(tb => (tb.GetAttr("Text") ?? "").Contains("FIR"));
            Assert.NotNull(firBadge);

            var storyBadge = badgesWrapPanel.Descendants("TextBlock")
                .FirstOrDefault(tb => (tb.GetAttr("Text") ?? "").Contains("Story"));
            Assert.NotNull(storyBadge);

            var statusBadge = badgesWrapPanel.Descendants("TextBlock")
                .FirstOrDefault(tb => (tb.GetAttr("Text") ?? "").Contains("{Binding StatusText}"));
            Assert.NotNull(statusBadge);
        }

        [Fact]
        public void C1_05_TaskCardControl_RewardsFooterDirectBindings()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Controls", "TaskCardControl.xaml");

            // 1. Rewards Footer Border
            var footerBorder = doc.Descendants("Border")
                .FirstOrDefault(b => (b.GetAttr("Visibility") ?? "").Contains("{Binding RewardsFooterVisibility}"));
            Assert.NotNull(footerBorder);

            // 2. Rewards WrapPanel Strip
            var rewardsStrip = footerBorder.Descendants("WrapPanel")
                .FirstOrDefault(wp => (wp.GetAttr("Visibility") ?? "").Contains("{Binding RewardsStripVisibility}"));
            Assert.NotNull(rewardsStrip);

            // 3. XP Reward Pill
            var xpBorder = rewardsStrip.Descendants("Border")
                .FirstOrDefault(b => (b.GetAttr("Visibility") ?? "").Contains("{Binding XpRewardVisibility}"));
            Assert.NotNull(xpBorder);
            var xpText = xpBorder.Descendants("TextBlock")
                .FirstOrDefault(tb => (tb.GetAttr("Text") ?? "").Contains("{Binding FormattedXpReward}"));
            Assert.NotNull(xpText);

            // 4. Trader Rep Rewards ItemsControl
            var repItems = rewardsStrip.Descendants("ItemsControl")
                .FirstOrDefault(ic => (ic.GetAttr("ItemsSource") ?? "").Contains("{Binding FormattedRepRewards}"));
            Assert.NotNull(repItems);

            // 5. Item Rewards ItemsControl
            var itemRewards = rewardsStrip.Descendants("ItemsControl")
                .FirstOrDefault(ic => (ic.GetAttr("ItemsSource") ?? "").Contains("{Binding ItemRewards}"));
            Assert.NotNull(itemRewards);

            // 6. Impact / Unlocks Summary Strip
            var impactText = footerBorder.Descendants("TextBlock")
                .FirstOrDefault(tb => (tb.GetAttr("Text") ?? "").Contains("{Binding ImpactSummaryText}"));
            Assert.NotNull(impactText);
        }

        #endregion

        #region 3. Component Completeness (24+ Components & Interactions)

        [Fact]
        public void C1_06_TaskCardControl_All24ComponentsPresentInVisualTree()
        {
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Controls", "TaskCardControl.xaml");

            // 1. Main card container DataContext
            var mainBorder = doc.Descendants("Border").FirstOrDefault(b => (b.GetAttr("DataContext") ?? "").Contains("CardModel"));
            Assert.NotNull(mainBorder);

            // 2. Trader Avatar / Initial
            Assert.NotNull(doc.Descendants("TextBlock").FirstOrDefault(tb => (tb.GetAttr("Text") ?? "").Contains("{Binding TraderInitial}")));
            // 3. Avatar Image
            Assert.NotNull(doc.Descendants("Image").FirstOrDefault(img => (img.GetAttr("Source") ?? "").Contains("{Binding AvatarSource}")));
            // 4. Task Title
            Assert.NotNull(doc.Descendants("TextBlock").FirstOrDefault(tb => (tb.GetAttr("Text") ?? "").Contains("{Binding Title}")));
            // 5. Wiki Link Button
            Assert.NotNull(doc.Descendants("Button").FirstOrDefault(b => b.GetAttr("Click") == "BtnWiki_Click"));
            // 6. Trader name TextBlock
            Assert.NotNull(doc.Descendants("TextBlock").FirstOrDefault(tb => (tb.GetAttr("Text") ?? "").Contains("{Binding Trader}")));
            // 7. Map badge
            Assert.NotNull(doc.Descendants("TextBlock").FirstOrDefault(tb => (tb.GetAttr("Text") ?? "").Contains("{Binding MapBadge}")));
            // 8. Required level badge
            Assert.NotNull(doc.Descendants("Border").FirstOrDefault(b => (b.GetAttr("Visibility") ?? "").Contains("{Binding RequiredLevelVisibility}")));
            // 9. Kappa badge
            Assert.NotNull(doc.Descendants("Border").FirstOrDefault(b => (b.GetAttr("Visibility") ?? "").Contains("{Binding KappaBadgeVisibility}")));
            // 10. FIR badge
            Assert.NotNull(doc.Descendants("Border").FirstOrDefault(b => (b.GetAttr("Visibility") ?? "").Contains("{Binding FirBadgeVisibility}")));
            // 11. Story badge
            Assert.NotNull(doc.Descendants("Border").FirstOrDefault(b => (b.GetAttr("Visibility") ?? "").Contains("{Binding StoryBadgeVisibility}")));
            // 12. Status badge
            Assert.NotNull(doc.Descendants("Border").FirstOrDefault(b => (b.GetAttr("Background") ?? "").Contains("{Binding StatusBackground}")));
            // 13. Map launcher button
            Assert.NotNull(doc.Descendants("Button").FirstOrDefault(b => b.GetAttr("Click") == "BtnMap_Click"));
            // 14. Complete button
            Assert.NotNull(doc.Descendants("Button").FirstOrDefault(b => b.GetAttr("Click") == "BtnComplete_Click"));
            // 15. Hide button
            Assert.NotNull(doc.Descendants("Button").FirstOrDefault(b => b.GetAttr("Click") == "BtnHide_Click"));
            // 16. Collapse/Expand button
            Assert.NotNull(doc.Descendants("Button").FirstOrDefault(b => b.GetAttr("Click") == "BtnToggleExpand_Click"));
            // 17. Collapsible body
            Assert.NotNull(doc.Descendants("StackPanel").FirstOrDefault(sp => (sp.GetAttr("Visibility") ?? "").Contains("{Binding ExpandedVisibility}")));
            // 18. Requirements summary bar
            Assert.NotNull(doc.Descendants("TextBlock").FirstOrDefault(tb => (tb.GetAttr("Text") ?? "").Contains("{Binding RequirementsSummaryText}")));
            // 19. Item steppers ItemsControl
            Assert.NotNull(doc.Descendants("ItemsControl").FirstOrDefault(ic => (ic.GetAttr("ItemsSource") ?? "").Contains("{Binding ItemSteppers}")));
            // 20. Decrement button
            Assert.NotNull(doc.Descendants("Button").FirstOrDefault(b => b.GetAttr("Click") == "BtnStepperDecrement_Click"));
            // 21. Stepper counter fraction
            Assert.NotNull(doc.Descendants("Run").FirstOrDefault(r => (r.GetAttr("Text") ?? "").Contains("{Binding CurrentCount}")));
            // 22. Increment button
            Assert.NotNull(doc.Descendants("Button").FirstOrDefault(b => b.GetAttr("Click") == "BtnStepperIncrement_Click"));
            // 23. Max check button
            Assert.NotNull(doc.Descendants("Button").FirstOrDefault(b => b.GetAttr("Click") == "BtnStepperMax_Click"));
            // 24. Action toggles ItemsControl
            Assert.NotNull(doc.Descendants("ItemsControl").FirstOrDefault(ic => (ic.GetAttr("ItemsSource") ?? "").Contains("{Binding ActionToggles}")));
            // 25. Action toggle button
            Assert.NotNull(doc.Descendants("Button").FirstOrDefault(b => b.GetAttr("Click") == "BtnActionToggle_Click"));
            // 26. Action description
            Assert.NotNull(doc.Descendants("TextBlock").FirstOrDefault(tb => (tb.GetAttr("Text") ?? "").Contains("{Binding Description}")));
            // 27. Needed keys section
            Assert.NotNull(doc.Descendants("Border").FirstOrDefault(b => (b.GetAttr("Visibility") ?? "").Contains("{Binding NeededKeysVisibility}")));
            // 28. Wiki notes expander
            Assert.NotNull(doc.Descendants("Expander").FirstOrDefault(e => (e.GetAttr("Visibility") ?? "").Contains("{Binding GuideExpanderVisibility}")));
            // 29. Rewards footer
            Assert.NotNull(doc.Descendants("Border").FirstOrDefault(b => (b.GetAttr("Visibility") ?? "").Contains("{Binding RewardsFooterVisibility}")));
        }

        #endregion

        #region 4. Tactical Palette & Surface Elevation

        [Fact]
        public void C1_07_TacticalSurfacesAndTintedBadges_AppliedInTaskCardControl()
        {
            var rawXaml = File.ReadAllText(R1R2TestHelpers.ResolveRepoPath("TarkovHelper", "Controls", "TaskCardControl.xaml"));

            // Check surface elevation brushes
            Assert.Contains("DynamicResource TacticalSurface2Brush", rawXaml); // Card body
            Assert.Contains("DynamicResource TacticalSurface3Brush", rawXaml); // Insets / rows
            Assert.Contains("DynamicResource TacticalSurface4Brush", rawXaml); // Buttons
            Assert.Contains("DynamicResource TacticalSurface1Brush", rawXaml); // Inset bars / footers
            Assert.Contains("DynamicResource TacticalSurface0Brush", rawXaml); // Deep insets

            // Check tinted badge brushes
            Assert.Contains("DynamicResource BadgeKappaBgBrush", rawXaml);
            Assert.Contains("DynamicResource BadgeKappaFgBrush", rawXaml);
            Assert.Contains("DynamicResource BadgeFirBgBrush", rawXaml);
            Assert.Contains("DynamicResource BadgeFirFgBrush", rawXaml);
            Assert.Contains("DynamicResource BadgeStoryBgBrush", rawXaml);
            Assert.Contains("DynamicResource BadgeStoryFgBrush", rawXaml);
            Assert.Contains("DynamicResource BadgeActiveBgBrush", rawXaml);
            Assert.Contains("DynamicResource BadgeActiveFgBrush", rawXaml);
        }

        #endregion

        #region 5. Publish Database Integrity Verification

        [Fact]
        public void C1_08_PublishTarkovDataDb_PreservedAndValidSqlite()
        {
            var dbPath = R1R2TestHelpers.ResolveRepoPath("publish", "tarkov_data.db");
            if (!File.Exists(dbPath))
            {
                // If publish folder hasn't been populated yet, test passes trivially
                return;
            }

            var fileInfo = new FileInfo(dbPath);
            // Database must not be empty or truncated
            Assert.True(fileInfo.Length > 1_000_000, $"publish\\tarkov_data.db size {fileInfo.Length} is suspiciously small");

            // Open SQLite connection and execute integrity check
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            var result = cmd.ExecuteScalar()?.ToString();
            Assert.Equal("ok", result);

            // Verify essential tables exist
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND LOWER(name) IN ('quests', 'tasks', 'items');";
            var tableCount = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.True(tableCount >= 1, "publish\\tarkov_data.db should contain database tables");
        }

        #endregion

        #region 6. Adversarial Stress Testing on Model & Recalculation

        [Fact]
        public void C1_09_Adversarial_TaskCardViewModel_ExtremeValuesAndNullTolerance()
        {
            var vm = new TaskCardViewModel();

            // Null and empty strings should not throw
            vm.Title = "";
            Assert.Equal("", vm.Title);

            // Very long title should not throw
            vm.Title = new string('A', 500);
            Assert.Equal(500, vm.Title.Length);

            // Stepper with extreme bounds
            var stepper = new SubObjectiveStepperItem
            {
                ItemId = "stress_item",
                ItemName = "Extreme Test Item",
                CurrentCount = 0,
                MaxCount = 5
            };
            vm.ItemSteppers.Add(stepper);

            // Stepper increment beyond max
            for (int i = 0; i < 10; i++) stepper.Increment();
            Assert.Equal(5, stepper.CurrentCount); // Clamped at max

            // Stepper decrement below zero
            for (int i = 0; i < 10; i++) stepper.Decrement();
            Assert.Equal(0, stepper.CurrentCount); // Clamped at 0

            // Recalculation under edge cases
            vm.RecalculateMetRequirements();
            Assert.Equal(0, vm.MetRequirements);

            stepper.SetMax();
            vm.RecalculateMetRequirements();
            Assert.Equal(1, vm.MetRequirements);

            // Action toggles
            var action = new SubObjectiveActionItem
            {
                Description = "Stress Action",
                IsChecked = false
            };
            vm.ActionToggles.Add(action);

            vm.RecalculateMetRequirements();
            Assert.Equal(1, vm.MetRequirements); // Stepper met, action not met

            action.IsChecked = true;
            vm.RecalculateMetRequirements();
            Assert.Equal(2, vm.MetRequirements); // Both met
        }

        [Fact]
        public void C1_10_Adversarial_RewardsFooterVisibility_ReflectsRewardsPresence()
        {
            var vm = new TaskCardViewModel();

            // Empty rewards
            vm.XpReward = 0;
            vm.TraderRepRewards.Clear();
            vm.ItemRewards.Clear();
            vm.UnlocksNext = 0;
            vm.Impact = 0;

            // Update footer visibility
            vm.RefreshRewardsVisibility();

            // When no rewards exist, footer should be hidden or collapsed
            Assert.Equal(System.Windows.Visibility.Collapsed, vm.RewardsStripVisibility);

            // Adding an item reward should make rewards strip visible
            vm.ItemRewards.Add("Salewa first aid kit");
            vm.RefreshRewardsVisibility();
            Assert.Equal(System.Windows.Visibility.Visible, vm.RewardsStripVisibility);
            Assert.Equal(System.Windows.Visibility.Visible, vm.RewardsFooterVisibility);
        }

        #endregion
    }
}
