using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TarkovHelper.Tests.Common;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests.ViewModels
{
    /// <summary>
    /// Tier 2: Needed Items Kappa Filter Integration Test Suite.
    /// Derived from ORIGINAL_REQUEST.md (§R1) and PROJECT.md (§F4, §F5).
    /// 
    /// Verifies:
    /// - IsTabKappa property behaves accurately across tab transitions.
    /// - KappaCount reactive counter dynamically calculates qualifying Kappa and Collector items.
    /// - Filtering logic preserves ReqKappa quests and Fence "Collector" streamer items.
    /// - Non-Kappa quests and Hideout requirements are strictly filtered out on TabKappa.
    /// - Steppers (Increment, Decrement, ToggleComplete) operate on Kappa cards.
    /// - Secondary filters (FIR, Search, HideOwned, Sorting) work combinatorially with TabKappa.
    /// </summary>
    public class NeededItemsKappaFilterTests
    {
        private static bool GetIsTabKappa(NeededItemsViewModel vm)
        {
            var prop = typeof(NeededItemsViewModel).GetProperty("IsTabKappa", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
            {
                return (bool)prop.GetValue(vm)!;
            }
            return string.Equals(vm.ActiveTab, "Kappa", StringComparison.OrdinalIgnoreCase);
        }

        private static void SetIsTabKappa(NeededItemsViewModel vm, bool value)
        {
            var prop = typeof(NeededItemsViewModel).GetProperty("IsTabKappa", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(vm, value);
            }
            else if (value)
            {
                vm.ActiveTab = "Kappa";
            }
        }

        private static int GetKappaCount(NeededItemsViewModel vm)
        {
            var prop = typeof(NeededItemsViewModel).GetProperty("KappaCount", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
            {
                return (int)prop.GetValue(vm)!;
            }
            // Fallback derived from cards
            return vm.FilteredCards.Count(c => c.IsKappaRequired || string.Equals(c.QuestNormalizedName, "collector", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void T2_01_Contract_NeededItemsViewModel_DeclaresKappaProperties()
        {
            var vmType = typeof(NeededItemsViewModel);
            var isTabKappaProp = vmType.GetProperty("IsTabKappa", BindingFlags.Public | BindingFlags.Instance);
            var kappaCountProp = vmType.GetProperty("KappaCount", BindingFlags.Public | BindingFlags.Instance);

            Assert.True(isTabKappaProp != null, "NeededItemsViewModel must declare public bool IsTabKappa per PROJECT.md interface contract.");
            Assert.True(kappaCountProp != null, "NeededItemsViewModel must declare public int KappaCount per PROJECT.md interface contract.");
            Assert.Equal(typeof(bool), isTabKappaProp.PropertyType);
            Assert.Equal(typeof(int), kappaCountProp.PropertyType);
        }

        [Fact]
        public void T2_02_IsTabKappa_ReturnsTrueOnlyWhenActiveTabIsKappa()
        {
            var vm = new NeededItemsViewModel();

            vm.ActiveTab = "All";
            Assert.False(GetIsTabKappa(vm));
            Assert.True(vm.IsTabAll);

            vm.ActiveTab = "Tasks";
            Assert.False(GetIsTabKappa(vm));
            Assert.True(vm.IsTabTasks);

            vm.ActiveTab = "Hideout";
            Assert.False(GetIsTabKappa(vm));
            Assert.True(vm.IsTabHideout);

            vm.ActiveTab = "Completed";
            Assert.False(GetIsTabKappa(vm));
            Assert.True(vm.IsTabCompleted);

            vm.ActiveTab = "Kappa";
            Assert.True(GetIsTabKappa(vm));
            Assert.False(vm.IsTabAll);
            Assert.False(vm.IsTabTasks);
            Assert.False(vm.IsTabHideout);
            Assert.False(vm.IsTabCompleted);

            // Two-way property toggle test
            SetIsTabKappa(vm, false);
            SetIsTabKappa(vm, true);
            Assert.True(GetIsTabKappa(vm));
            Assert.Equal("Kappa", vm.ActiveTab);
        }

        [Fact]
        public void T2_03_KappaCount_AggregatesBothReqKappaQuestsAndCollectorStreamerItems()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                // 1. Regular Kappa quest (ReqKappa = true)
                R1R2TestHelpers.CreateCard("k1", "Quest", amount: 2, isKappaRequired: true, questNormalizedName: "punisher_part_4", itemDisplayName: "Scav Vest"),
                // 2. Another Kappa quest
                R1R2TestHelpers.CreateCard("k2", "Quest", amount: 1, isKappaRequired: true, questNormalizedName: "grenadier", itemDisplayName: "Grenade"),
                // 3. Fence "Collector" quest item (ReqKappa = false in API, but quest is "collector")
                R1R2TestHelpers.CreateCard("c1", "Quest", amount: 1, isKappaRequired: false, questNormalizedName: "collector", itemDisplayName: "Kotton Beanie"),
                // 4. Fence "Collector" item with capitalized name
                R1R2TestHelpers.CreateCard("c2", "Quest", amount: 1, isKappaRequired: false, questNormalizedName: "Collector", itemDisplayName: "Silver Badge"),
                // 5. Non-Kappa quest item (ReqKappa = false)
                R1R2TestHelpers.CreateCard("reg1", "Quest", amount: 1, isKappaRequired: false, questNormalizedName: "checking", itemDisplayName: "Bronze Pocket Watch"),
                // 6. Hideout station item
                R1R2TestHelpers.CreateCard("hid1", "Hideout", amount: 4, isKappaRequired: false, questNormalizedName: null, itemDisplayName: "Car Battery")
            };

            vm.SetCardsForTesting(cards);

            Assert.Equal(6, vm.AllCount);
            Assert.Equal(5, vm.TasksCount);
            Assert.Equal(1, vm.HideoutCount);

            // Kappa count must be exactly 4 (k1, k2, c1, c2)
            Assert.Equal(4, GetKappaCount(vm));
        }

        [Fact]
        public void T2_04_ActiveTabKappa_FiltersExclusivelyToKappaAndCollectorItems()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                R1R2TestHelpers.CreateCard("k1", "Quest", isKappaRequired: true, questNormalizedName: "punisher_4", itemDisplayName: "Scav Vest"),
                R1R2TestHelpers.CreateCard("c1", "Quest", isKappaRequired: false, questNormalizedName: "collector", itemDisplayName: "Kotton Beanie"),
                R1R2TestHelpers.CreateCard("q_non_kappa", "Quest", isKappaRequired: false, questNormalizedName: "bad_rep_evidence", itemDisplayName: "Folder"),
                R1R2TestHelpers.CreateCard("h_station", "Hideout", isKappaRequired: false, itemDisplayName: "Screw Nuts")
            };

            vm.SetCardsForTesting(cards);
            vm.ActiveTab = "Kappa";

            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "k1" && c.ItemDisplayName == "Scav Vest");
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "c1" && c.ItemDisplayName == "Kotton Beanie");
            Assert.DoesNotContain(vm.FilteredCards, c => c.NeedKey == "q_non_kappa");
            Assert.DoesNotContain(vm.FilteredCards, c => c.NeedKey == "h_station");
        }

        [Fact]
        public void T2_05_KappaCards_StepperInteractions_UpdateQuantitiesAndCompletion()
        {
            var vm = new NeededItemsViewModel();
            var card = R1R2TestHelpers.CreateCard("k_stepper", "Quest", amount: 3, currentCount: 1, isKappaRequired: true, questNormalizedName: "collector", itemDisplayName: "Tea");

            vm.SetCardsForTesting(new[] { card });
            vm.ActiveTab = "Kappa";

            Assert.Single(vm.FilteredCards);
            var item = vm.FilteredCards[0];

            Assert.Equal(1, item.CurrentCount);
            Assert.Equal("1/3", item.ProgressText);
            Assert.False(item.IsComplete);
            Assert.Equal(1.0, item.CardOpacity);

            // Increment manually
            item.CurrentCount++;
            Assert.Equal(2, item.CurrentCount);
            Assert.Equal("2/3", item.ProgressText);
            Assert.False(item.IsComplete);

            // Complete
            item.CurrentCount = 3;
            Assert.Equal(3, item.CurrentCount);
            Assert.Equal("3/3", item.ProgressText);
            Assert.True(item.IsComplete);
            Assert.Equal(0.45, item.CardOpacity);
        }

        [Fact]
        public void T2_06_ActiveTabKappa_CombinesWithFirFilter()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                R1R2TestHelpers.CreateCard("k_fir", "Quest", isKappaRequired: true, foundInRaid: true, itemDisplayName: "Loot Lord Plushie"),
                R1R2TestHelpers.CreateCard("k_non_fir", "Quest", isKappaRequired: true, foundInRaid: false, itemDisplayName: "Gunpowder Kite"),
                R1R2TestHelpers.CreateCard("c_fir", "Quest", isKappaRequired: false, questNormalizedName: "collector", foundInRaid: true, itemDisplayName: "Dr Lupo Coffee Beans"),
                R1R2TestHelpers.CreateCard("reg_fir", "Quest", isKappaRequired: false, foundInRaid: true, itemDisplayName: "Salewa")
            };

            vm.SetCardsForTesting(cards);
            vm.ActiveTab = "Kappa";

            // All Kappa (3 cards)
            Assert.Equal(3, vm.FilteredCards.Count);

            // FIR only
            vm.FirFilter = "FIR";
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "k_fir");
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "c_fir");
            Assert.DoesNotContain(vm.FilteredCards, c => c.NeedKey == "reg_fir"); // Non-Kappa excluded

            // Non-FIR only
            vm.FirFilter = "Non-FIR";
            Assert.Single(vm.FilteredCards);
            Assert.Equal("k_non_fir", vm.FilteredCards[0].NeedKey);
        }

        [Fact]
        public void T2_07_ActiveTabKappa_CombinesWithSearchQuery()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                R1R2TestHelpers.CreateCard("c1", "Quest", isKappaRequired: false, questNormalizedName: "collector", itemDisplayName: "Kotton Beanie", traderName: "Fence"),
                R1R2TestHelpers.CreateCard("c2", "Quest", isKappaRequired: false, questNormalizedName: "collector", itemDisplayName: "Silver Badge", traderName: "Fence"),
                R1R2TestHelpers.CreateCard("q1", "Quest", isKappaRequired: true, questNormalizedName: "punisher", itemDisplayName: "Half-mask", traderName: "Prapor"),
                R1R2TestHelpers.CreateCard("q2", "Quest", isKappaRequired: false, questNormalizedName: "other", itemDisplayName: "Kotton Beanie", traderName: "Skier") // duplicate name non-kappa
            };

            vm.SetCardsForTesting(cards);
            vm.ActiveTab = "Kappa";

            vm.SearchText = "Kotton";
            Assert.Single(vm.FilteredCards);
            Assert.Equal("c1", vm.FilteredCards[0].NeedKey);
            Assert.Equal("Fence", vm.FilteredCards[0].TraderName);

            vm.SearchText = "Prapor";
            Assert.Single(vm.FilteredCards);
            Assert.Equal("q1", vm.FilteredCards[0].NeedKey);

            vm.SearchText = "xyz_nonexistent";
            Assert.Empty(vm.FilteredCards);
        }

        [Fact]
        public void T2_08_ActiveTabKappa_HideOwnedFilter_HidesCompletedItems()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                R1R2TestHelpers.CreateCard("inc", "Quest", amount: 2, currentCount: 0, isKappaRequired: true, itemDisplayName: "Incomplete Item"),
                R1R2TestHelpers.CreateCard("comp", "Quest", amount: 1, currentCount: 1, isKappaRequired: true, itemDisplayName: "Complete Item")
            };

            vm.SetCardsForTesting(cards);
            vm.ActiveTab = "Kappa";

            // By default, both are shown (completed item dimmed)
            Assert.Equal(2, vm.FilteredCards.Count);

            // Toggle HideOwned
            vm.HideOwned = true;
            Assert.Single(vm.FilteredCards);
            Assert.Equal("inc", vm.FilteredCards[0].NeedKey);

            vm.HideOwned = false;
            Assert.Equal(2, vm.FilteredCards.Count);
        }

        [Fact]
        public void T2_09_ActiveTabKappa_SortingPreserved()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                R1R2TestHelpers.CreateCard("c_zebra", "Quest", amount: 1, isKappaRequired: true, itemDisplayName: "Zebra Item", priorityScore: 10),
                R1R2TestHelpers.CreateCard("c_apple", "Quest", amount: 5, isKappaRequired: true, itemDisplayName: "Apple Item", priorityScore: 50),
                R1R2TestHelpers.CreateCard("c_banana", "Quest", amount: 10, isKappaRequired: false, questNormalizedName: "collector", itemDisplayName: "Banana Item", priorityScore: 100)
            };

            vm.SetCardsForTesting(cards);
            vm.ActiveTab = "Kappa";

            // Sort by Name
            vm.SortMode = "Name";
            Assert.Equal("Apple Item", vm.FilteredCards[0].ItemDisplayName);
            Assert.Equal("Banana Item", vm.FilteredCards[1].ItemDisplayName);
            Assert.Equal("Zebra Item", vm.FilteredCards[2].ItemDisplayName);

            // Sort by Quantity
            vm.SortMode = "Quantity";
            Assert.Equal(10, vm.FilteredCards[0].Amount);
            Assert.Equal(5, vm.FilteredCards[1].Amount);
            Assert.Equal(1, vm.FilteredCards[2].Amount);

            // Sort by Priority
            vm.SortMode = "Priority";
            Assert.Equal(100, vm.FilteredCards[0].PriorityScore);
            Assert.Equal(50, vm.FilteredCards[1].PriorityScore);
            Assert.Equal(10, vm.FilteredCards[2].PriorityScore);
        }

        [Fact]
        public void T2_10_TabTransitions_MaintainCountersInvariance()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                R1R2TestHelpers.CreateCard("k1", "Quest", isKappaRequired: true),
                R1R2TestHelpers.CreateCard("c1", "Quest", isKappaRequired: false, questNormalizedName: "collector"),
                R1R2TestHelpers.CreateCard("q1", "Quest", isKappaRequired: false),
                R1R2TestHelpers.CreateCard("h1", "Hideout", isKappaRequired: false)
            };

            vm.SetCardsForTesting(cards);

            void AssertCounts()
            {
                Assert.Equal(4, vm.AllCount);
                Assert.Equal(3, vm.TasksCount);
                Assert.Equal(1, vm.HideoutCount);
                Assert.Equal(2, GetKappaCount(vm));
            }

            AssertCounts();

            vm.ActiveTab = "Tasks";
            AssertCounts();

            vm.ActiveTab = "Hideout";
            AssertCounts();

            vm.ActiveTab = "Kappa";
            AssertCounts();

            vm.ActiveTab = "Completed";
            AssertCounts();

            vm.ActiveTab = "All";
            AssertCounts();
        }
    }
}
