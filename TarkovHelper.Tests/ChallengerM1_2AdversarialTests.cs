using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using TarkovHelper.Pages;
using TarkovHelper.Tests.Common;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests
{
    /// <summary>
    /// Empirical Challenger M1-2 Adversarial Stress Test Suite.
    /// Stress-tests edge cases and compound filtering on Milestone 1 (5-Tab Navigation & Needed Items Kappa Integration).
    /// </summary>
    public class ChallengerM1_2AdversarialTests
    {
        #region 1. Compound Filtering Adversarial Tests

        [Fact]
        public void Challenge_CompoundFilter_IsTabKappa_With_FirFilter_StressTest()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                // ReqKappa quest item - FIR
                R1R2TestHelpers.CreateCard("k_fir", "Quest", isKappaRequired: true, foundInRaid: true, itemDisplayName: "Loot Lord Plushie"),
                // ReqKappa quest item - Non-FIR
                R1R2TestHelpers.CreateCard("k_non_fir", "Quest", isKappaRequired: true, foundInRaid: false, itemDisplayName: "Kite Gunpowder"),
                // Fence "Collector" streamer item - FIR
                R1R2TestHelpers.CreateCard("c_fir", "Quest", isKappaRequired: false, questNormalizedName: "collector", foundInRaid: true, itemDisplayName: "Kotton Beanie"),
                // Fence "Collector" streamer item - Non-FIR
                R1R2TestHelpers.CreateCard("c_non_fir", "Quest", isKappaRequired: false, questNormalizedName: "collector", foundInRaid: false, itemDisplayName: "Can of Dr Lupo"),
                // Non-Kappa quest item - FIR (should NEVER appear on Kappa tab)
                R1R2TestHelpers.CreateCard("reg_fir", "Quest", isKappaRequired: false, questNormalizedName: "debut", foundInRaid: true, itemDisplayName: "MP-133 Shotgun"),
                // Non-Kappa quest item - Non-FIR
                R1R2TestHelpers.CreateCard("reg_non_fir", "Quest", isKappaRequired: false, questNormalizedName: "supplier", foundInRaid: false, itemDisplayName: "TOZ-106"),
                // Hideout item - FIR (should NEVER appear on Kappa tab)
                R1R2TestHelpers.CreateCard("hid_fir", "Hideout", isKappaRequired: false, foundInRaid: true, itemDisplayName: "Corrugated Hose"),
                // Hideout item - Non-FIR
                R1R2TestHelpers.CreateCard("hid_non_fir", "Hideout", isKappaRequired: false, foundInRaid: false, itemDisplayName: "Relay")
            };

            vm.SetCardsForTesting(cards);
            vm.ActiveTab = "Kappa";

            // Baseline on Kappa tab (All FIR modes): must contain exactly the 4 Kappa/Collector cards
            Assert.Equal(4, vm.FilteredCards.Count);
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "k_fir");
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "k_non_fir");
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "c_fir");
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "c_non_fir");

            // Compound 1: Kappa + FirFilter = "FIR"
            vm.FirFilter = "FIR";
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "k_fir");
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "c_fir");
            Assert.DoesNotContain(vm.FilteredCards, c => c.NeedKey == "k_non_fir");
            Assert.DoesNotContain(vm.FilteredCards, c => c.NeedKey == "c_non_fir");
            Assert.DoesNotContain(vm.FilteredCards, c => c.NeedKey == "reg_fir");
            Assert.DoesNotContain(vm.FilteredCards, c => c.NeedKey == "hid_fir");

            // Compound 2: Kappa + FirFilter = "Non-FIR"
            vm.FirFilter = "Non-FIR";
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "k_non_fir");
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "c_non_fir");
            Assert.DoesNotContain(vm.FilteredCards, c => c.NeedKey == "k_fir");
            Assert.DoesNotContain(vm.FilteredCards, c => c.NeedKey == "c_fir");

            // Reset FirFilter back to "All"
            vm.FirFilter = "All";
            Assert.Equal(4, vm.FilteredCards.Count);

            // Counter invariant: KappaCount badge remains unaffected by FirFilter
            Assert.Equal(4, vm.KappaCount);
        }

        [Fact]
        public void Challenge_CompoundFilter_IsTabKappa_With_SearchText_StressTest()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                R1R2TestHelpers.CreateCard("c1", "Quest", isKappaRequired: false, questNormalizedName: "collector", itemDisplayName: "Kotton Beanie", sourceName: "Collector", traderName: "Fence"),
                R1R2TestHelpers.CreateCard("c2", "Quest", isKappaRequired: false, questNormalizedName: "collector", itemDisplayName: "Silver Badge", sourceName: "Collector", traderName: "Fence"),
                R1R2TestHelpers.CreateCard("k1", "Quest", isKappaRequired: true, questNormalizedName: "the_punisher_part_4", itemDisplayName: "Scav Vest", sourceName: "The Punisher - Part 4", traderName: "Prapor"),
                R1R2TestHelpers.CreateCard("k2", "Quest", isKappaRequired: true, questNormalizedName: "grenadier", itemDisplayName: "F-1 Grenade", sourceName: "Grenadier", traderName: "Prapor"),
                // Non-kappa quest item with identical item name
                R1R2TestHelpers.CreateCard("non_k", "Quest", isKappaRequired: false, questNormalizedName: "other_quest", itemDisplayName: "Kotton Beanie", sourceName: "Other", traderName: "Skier")
            };

            vm.SetCardsForTesting(cards);
            vm.ActiveTab = "Kappa";

            // 1. Exact item search
            vm.SearchText = "Kotton Beanie";
            Assert.Single(vm.FilteredCards);
            Assert.Equal("c1", vm.FilteredCards[0].NeedKey);

            // 2. Case-insensitive substring with leading/trailing whitespaces
            vm.SearchText = "   kotton   ";
            Assert.Single(vm.FilteredCards);
            Assert.Equal("c1", vm.FilteredCards[0].NeedKey);

            // 3. Search by Trader Name on Kappa tab
            vm.SearchText = "Fence";
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.All(vm.FilteredCards, c => Assert.Equal("Fence", c.TraderName));

            vm.SearchText = "Prapor";
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.All(vm.FilteredCards, c => Assert.Equal("Prapor", c.TraderName));

            // 4. Search by Quest Name (SourceName)
            vm.SearchText = "Punisher";
            Assert.Single(vm.FilteredCards);
            Assert.Equal("k1", vm.FilteredCards[0].NeedKey);

            // 5. Query matching non-Kappa item only -> must return empty on Kappa tab
            vm.SearchText = "Skier";
            Assert.Empty(vm.FilteredCards);

            // 6. Non-existent query
            vm.SearchText = "unknown_item_404";
            Assert.Empty(vm.FilteredCards);

            // 7. Clear search text -> restores all 4 Kappa cards
            vm.SearchText = string.Empty;
            Assert.Equal(4, vm.FilteredCards.Count);

            // Invariance: KappaCount remains 4 throughout search
            Assert.Equal(4, vm.KappaCount);
        }

        [Fact]
        public void Challenge_CompoundFilter_IsTabKappa_With_HideOwned_StressTest()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                // Incomplete Kappa item (0/2)
                R1R2TestHelpers.CreateCard("k_zero", "Quest", amount: 2, currentCount: 0, isKappaRequired: true, itemDisplayName: "Zero Item"),
                // Partially completed Kappa item (1/3)
                R1R2TestHelpers.CreateCard("k_partial", "Quest", amount: 3, currentCount: 1, isKappaRequired: true, itemDisplayName: "Partial Item"),
                // Fully completed Kappa item (2/2)
                R1R2TestHelpers.CreateCard("k_complete", "Quest", amount: 2, currentCount: 2, isKappaRequired: true, itemDisplayName: "Complete Item"),
                // Over-completed Kappa item (3/2)
                R1R2TestHelpers.CreateCard("k_over", "Quest", amount: 2, currentCount: 3, isKappaRequired: true, itemDisplayName: "Overcomplete Item"),
                // Incomplete Collector item (0/1)
                R1R2TestHelpers.CreateCard("c_inc", "Quest", amount: 1, currentCount: 0, isKappaRequired: false, questNormalizedName: "collector", itemDisplayName: "Collector Item"),
                // Complete Collector item (1/1)
                R1R2TestHelpers.CreateCard("c_comp", "Quest", amount: 1, currentCount: 1, isKappaRequired: false, questNormalizedName: "collector", itemDisplayName: "Collector Done")
            };

            vm.SetCardsForTesting(cards);
            vm.ActiveTab = "Kappa";

            // Baseline: All 6 cards displayed
            Assert.Equal(6, vm.FilteredCards.Count);
            Assert.Equal(6, vm.KappaCount);

            // Verify visual state on complete items before hide
            var compItem = vm.FilteredCards.First(c => c.NeedKey == "k_complete");
            Assert.True(compItem.IsComplete);
            Assert.Equal(0.45, compItem.CardOpacity);

            var incItem = vm.FilteredCards.First(c => c.NeedKey == "k_partial");
            Assert.False(incItem.IsComplete);
            Assert.Equal(1.0, incItem.CardOpacity);

            // Toggle HideOwned = true
            vm.HideOwned = true;
            Assert.Equal(3, vm.FilteredCards.Count); // k_zero, k_partial, c_inc
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "k_zero");
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "k_partial");
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "c_inc");
            Assert.DoesNotContain(vm.FilteredCards, c => c.NeedKey == "k_complete");
            Assert.DoesNotContain(vm.FilteredCards, c => c.NeedKey == "k_over");
            Assert.DoesNotContain(vm.FilteredCards, c => c.NeedKey == "c_comp");

            // Invariance: KappaCount remains 6
            Assert.Equal(6, vm.KappaCount);

            // Toggle HideOwned = false -> all 6 reappear
            vm.HideOwned = false;
            Assert.Equal(6, vm.FilteredCards.Count);

            // Rapid toggle idempotency
            vm.HideOwned = true;
            vm.HideOwned = true;
            Assert.Equal(3, vm.FilteredCards.Count);
            vm.HideOwned = false;
            vm.HideOwned = false;
            Assert.Equal(6, vm.FilteredCards.Count);
        }

        [Fact]
        public void Challenge_CompoundFilter_MultiCombinatorial_StressTest()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                // Kappa, FIR, Incomplete, "Loot"
                R1R2TestHelpers.CreateCard("c1", "Quest", amount: 2, currentCount: 0, isKappaRequired: true, foundInRaid: true, itemDisplayName: "Loot Lord Plushie"),
                // Kappa, FIR, Complete, "Loot"
                R1R2TestHelpers.CreateCard("c2", "Quest", amount: 1, currentCount: 1, isKappaRequired: true, foundInRaid: true, itemDisplayName: "Loot Bag"),
                // Kappa, Non-FIR, Incomplete, "Loot"
                R1R2TestHelpers.CreateCard("c3", "Quest", amount: 1, currentCount: 0, isKappaRequired: true, foundInRaid: false, itemDisplayName: "Loot Gold"),
                // Non-Kappa, FIR, Incomplete, "Loot"
                R1R2TestHelpers.CreateCard("c4", "Quest", amount: 1, currentCount: 0, isKappaRequired: false, questNormalizedName: "other", foundInRaid: true, itemDisplayName: "Loot Quest Item")
            };

            vm.SetCardsForTesting(cards);
            vm.ActiveTab = "Kappa";

            // 4-way compound filter: Tab=Kappa + Search="Loot" + FIR=true + HideOwned=true
            vm.SearchText = "Loot";
            vm.FirFilter = "FIR";
            vm.HideOwned = true;

            // Only c1 satisfies all 4 conditions
            Assert.Single(vm.FilteredCards);
            Assert.Equal("c1", vm.FilteredCards[0].NeedKey);

            // Switch to ActiveTab="Tasks" -> now c1 and c4 satisfy FIR + HideOwned + Search
            vm.ActiveTab = "Tasks";
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "c1");
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "c4");

            // Switch back to "Kappa"
            vm.ActiveTab = "Kappa";
            Assert.Single(vm.FilteredCards);
            Assert.Equal("c1", vm.FilteredCards[0].NeedKey);

            // ResetFilters restores defaults
            vm.ResetFilters();
            Assert.False(vm.HideOwned);
            Assert.Equal("All", vm.FirFilter);
            // SearchText is separate from popup filters; clearing it reveals all 3 Kappa items
            vm.SearchText = string.Empty;
            Assert.Equal(3, vm.FilteredCards.Count);
        }

        [Fact]
        public void Challenge_Steppers_AccuratelyUpdateProgressAndStateOnKappaCards()
        {
            var card = R1R2TestHelpers.CreateCard(
                needKey: "test_stepper_key",
                sourceType: "Quest",
                amount: 3,
                currentCount: 0,
                isKappaRequired: true,
                questNormalizedName: "collector",
                itemDisplayName: "Old Firesteel"
            );

            var propertyChanges = new List<string>();
            card.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != null) propertyChanges.Add(e.PropertyName);
            };

            // Initial State
            Assert.Equal(0, card.CurrentCount);
            Assert.Equal("0/3", card.ProgressText);
            Assert.False(card.IsComplete);
            Assert.Equal(1.0, card.CardOpacity);

            // 1. Increment
            propertyChanges.Clear();
            card.CurrentCount++;
            Assert.Equal(1, card.CurrentCount);
            Assert.Equal("1/3", card.ProgressText);
            Assert.False(card.IsComplete);
            Assert.Equal(1.0, card.CardOpacity);
            Assert.Contains(nameof(card.CurrentCount), propertyChanges);
            Assert.Contains(nameof(card.ProgressText), propertyChanges);
            Assert.Contains(nameof(card.IsComplete), propertyChanges);
            Assert.Contains(nameof(card.CardOpacity), propertyChanges);
            Assert.Contains(nameof(card.StatusBrush), propertyChanges);

            // 2. Increment to Amount (Completion threshold)
            card.CurrentCount = 3;
            Assert.Equal("3/3", card.ProgressText);
            Assert.True(card.IsComplete);
            Assert.Equal(0.45, card.CardOpacity);

            // 3. Decrement from completion
            card.CurrentCount--;
            Assert.Equal(2, card.CurrentCount);
            Assert.Equal("2/3", card.ProgressText);
            Assert.False(card.IsComplete);
            Assert.Equal(1.0, card.CardOpacity);

            // 4. ToggleComplete simulation
            card.CurrentCount = card.Amount; // complete
            Assert.True(card.IsComplete);
            Assert.Equal(0.45, card.CardOpacity);

            card.CurrentCount = 0; // uncomplete
            Assert.False(card.IsComplete);
            Assert.Equal(1.0, card.CardOpacity);
        }

        #endregion

        #region 2. Navigation Edge Cases Adversarial Tests

        [Fact]
        public void Challenge_Navigation_StorylinePage_BtnGoToTasks_Invokes_NavigateToTasks()
        {
            // Verify StorylinePage XAML wireup
            var doc = R1R2TestHelpers.LoadXamlDocument("TarkovHelper", "Pages", "StorylinePage.xaml");
            var btn = doc.Descendants("Button").FirstOrDefault(b => b.Attributes().Any(a => string.Equals(a.Name.LocalName, "Name", StringComparison.OrdinalIgnoreCase) && a.Value == "BtnGoToTasks"));
            Assert.NotNull(btn);
            Assert.Equal("BtnGoToTasks_Click", (string?)btn.Attribute("Click"));

            // Verify StorylinePage.xaml.cs calls mw.NavigateToTasks()
            var csPath = R1R2TestHelpers.ResolveRepoPath("TarkovHelper", "Pages", "StorylinePage.xaml.cs");
            var code = System.IO.File.ReadAllText(csPath);
            Assert.Contains("NavigateToTasks()", code);

            // Verify MainWindow exposes NavigateToTasks()
            var method = typeof(MainWindow).GetMethod("NavigateToTasks", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(method);
        }

        [Fact]
        public void Challenge_Navigation_MainWindow_All5Tabs_HaveCheckedHandlers_AndNoNpe()
        {
            // Verify MainWindow.xaml.cs declares all 5 tabs and routes them in Tab_Checked and RefreshCurrentProfileDataAsync
            var csPath = R1R2TestHelpers.ResolveRepoPath("TarkovHelper", "MainWindow.xaml.cs");
            var code = System.IO.File.ReadAllText(csPath);

            // All 5 tabs checked in Tab_Checked
            Assert.Contains("sender == TabDashboard", code);
            Assert.Contains("sender == TabStoryline", code);
            Assert.Contains("sender == TabTasks", code);
            Assert.Contains("sender == TabNeededItems", code);
            Assert.Contains("sender == TabHideout", code);

            // All 5 tabs handled in RefreshCurrentProfileDataAsync without referencing removed TabCollector or TabMap
            Assert.Contains("TabDashboard.IsChecked == true", code);
            Assert.Contains("TabStoryline.IsChecked == true", code);
            Assert.Contains("TabTasks.IsChecked == true", code);
            Assert.Contains("TabNeededItems.IsChecked == true", code);
            Assert.Contains("TabHideout.IsChecked == true", code);

            // Verify absence of removed tabs in navigation logic
            Assert.DoesNotContain("TabCollector.IsChecked", code);
            Assert.DoesNotContain("TabMap.IsChecked", code);
        }

        [Fact]
        public void Challenge_Navigation_NavigateToNeededItems_SupportsKappaFilterParameter()
        {
            var csPath = R1R2TestHelpers.ResolveRepoPath("TarkovHelper", "MainWindow.xaml.cs");
            var code = System.IO.File.ReadAllText(csPath);

            // NavigateToNeededItems routing to Kappa
            Assert.Contains("string.Equals(filter, \"Kappa\"", code);
            Assert.Contains("_itemsPage.ViewModel.ActiveTab = \"Kappa\"", code);
        }

        #endregion
    }
}
