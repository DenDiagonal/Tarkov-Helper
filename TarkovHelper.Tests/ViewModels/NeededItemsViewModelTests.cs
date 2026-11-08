using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests.ViewModels
{
    public class NeededItemsViewModelTests
    {
        [Fact]
        public void Card_ProgressAndCompletion_StateUpdatesCorrectly()
        {
            var card = new NeededItemCardViewModel
            {
                NeedKey = "test_item_key",
                Amount = 5,
                CurrentCount = 0
            };

            Assert.Equal("0/5", card.ProgressText);
            Assert.False(card.IsComplete);
            Assert.Equal(1.0, card.CardOpacity);

            card.CurrentCount = 3;
            Assert.Equal("3/5", card.ProgressText);
            Assert.False(card.IsComplete);
            Assert.Equal(1.0, card.CardOpacity);

            card.CurrentCount = 5;
            Assert.Equal("5/5", card.ProgressText);
            Assert.True(card.IsComplete);
            Assert.Equal(0.45, card.CardOpacity);

            // Over-capped still complete
            card.CurrentCount = 6;
            Assert.Equal("6/5", card.ProgressText);
            Assert.True(card.IsComplete);
            Assert.Equal(0.45, card.CardOpacity);
        }

        [Fact]
        public void Card_DogtagDisplay_FormattedProperly()
        {
            var normalCard = new NeededItemCardViewModel
            {
                ItemDisplayName = "Dogtag",
                DogtagMinLevel = null
            };
            Assert.False(normalCard.HasDogtagLevel);
            Assert.Equal(string.Empty, normalCard.DogtagDisplay);
            Assert.Equal(Visibility.Collapsed, normalCard.DogtagVisibility);

            var levelCard = new NeededItemCardViewModel
            {
                ItemDisplayName = "BEAR Dogtag",
                DogtagMinLevel = 15
            };
            Assert.True(levelCard.HasDogtagLevel);
            Assert.Equal("Lv.15+", levelCard.DogtagDisplay);
            Assert.Equal(Visibility.Visible, levelCard.DogtagVisibility);
        }

        [Fact]
        public void Filter_ActiveTabs_FiltersCorrectly()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                new() { NeedKey = "1", SourceType = "Quest", Amount = 2, CurrentCount = 0 },
                new() { NeedKey = "2", SourceType = "Quest", Amount = 1, CurrentCount = 1 }, // Completed
                new() { NeedKey = "3", SourceType = "Hideout", Amount = 3, CurrentCount = 1 },
                new() { NeedKey = "4", SourceType = "Hideout", Amount = 4, CurrentCount = 4 }, // Completed
            };

            vm.SetCardsForTesting(cards);

            // Tab counts
            Assert.Equal(4, vm.AllCount);
            Assert.Equal(2, vm.TasksCount);
            Assert.Equal(2, vm.HideoutCount);
            Assert.Equal(2, vm.CompletedCount);

            // Default tab: All
            Assert.Equal(4, vm.FilteredCards.Count);

            // Tab: Tasks
            vm.ActiveTab = "Tasks";
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.All(vm.FilteredCards, c => Assert.True(c.IsQuest));

            // Tab: Hideout
            vm.ActiveTab = "Hideout";
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.All(vm.FilteredCards, c => Assert.True(c.IsHideout));

            // Tab: Completed
            vm.ActiveTab = "Completed";
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.All(vm.FilteredCards, c => Assert.True(c.IsComplete));
        }

        #region Test Helpers

        private static NeededItemCardViewModel CreateTestCard(
            string needKey = "test_key",
            string sourceType = "Quest",
            int amount = 1,
            int currentCount = 0,
            bool isKappaRequired = false,
            string? questNormalizedName = null,
            bool foundInRaid = false,
            bool isSpecialEquipment = false,
            string itemDisplayName = "Item",
            string sourceName = "Source",
            string? traderName = null,
            int priorityScore = 100)
        {
            return new NeededItemCardViewModel
            {
                NeedKey = needKey,
                SourceType = sourceType,
                Amount = amount,
                CurrentCount = currentCount,
                IsKappaRequired = isKappaRequired,
                QuestNormalizedName = questNormalizedName,
                FoundInRaid = foundInRaid,
                IsSpecialEquipment = isSpecialEquipment,
                ItemDisplayName = itemDisplayName,
                SourceName = sourceName,
                TraderName = traderName,
                PriorityScore = priorityScore
            };
        }

        #endregion

        [Fact]
        public void Filter_TabKappa_FiltersCollectorAndKappaQuests()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                new() { NeedKey = "1", SourceType = "Quest", IsKappaRequired = true, QuestNormalizedName = "the-punisher-part-1", ItemDisplayName = "AKM", Amount = 1 },
                new() { NeedKey = "2", SourceType = "Quest", IsKappaRequired = false, QuestNormalizedName = "collector", ItemDisplayName = "Kotton Beanie", Amount = 1 },
                new() { NeedKey = "3", SourceType = "Quest", IsKappaRequired = false, QuestNormalizedName = "shootout-picnic", ItemDisplayName = "Scav Vest", Amount = 1 },
                new() { NeedKey = "4", SourceType = "Hideout", IsKappaRequired = false, ItemDisplayName = "Corrugated Hose", Amount = 2 },
            };

            vm.SetCardsForTesting(cards);

            // Tab counts check
            Assert.Equal(4, vm.AllCount);
            Assert.Equal(3, vm.TasksCount);
            Assert.Equal(1, vm.HideoutCount);
            Assert.Equal(2, vm.KappaCount);

            // Activate Kappa tab via SelectTab
            vm.SelectTab("Kappa");
            Assert.True(vm.IsTabKappa);
            Assert.False(vm.IsTabAll);
            Assert.False(vm.IsTabTasks);
            Assert.False(vm.IsTabHideout);
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.Contains(vm.FilteredCards, c => c.ItemDisplayName == "AKM");
            Assert.Contains(vm.FilteredCards, c => c.ItemDisplayName == "Kotton Beanie");
            Assert.DoesNotContain(vm.FilteredCards, c => c.ItemDisplayName == "Scav Vest");
            Assert.DoesNotContain(vm.FilteredCards, c => c.ItemDisplayName == "Corrugated Hose");
        }

        [Fact]
        public void TabKappa_TwoWayProperty_UpdatesActiveTabAndCounts()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                new() { NeedKey = "1", SourceType = "Quest", IsKappaRequired = true, ItemDisplayName = "Item 1", Amount = 1 },
                new() { NeedKey = "2", SourceType = "Hideout", IsKappaRequired = false, ItemDisplayName = "Item 2", Amount = 1 }
            };

            vm.SetCardsForTesting(cards);

            // Simulate TwoWay binding trigger
            vm.IsTabKappa = true;
            Assert.Equal("Kappa", vm.ActiveTab);
            Assert.True(vm.IsTabKappa);
            Assert.Single(vm.FilteredCards);
            Assert.Equal("Item 1", vm.FilteredCards[0].ItemDisplayName);

            // Switch to Tasks via TwoWay property
            vm.IsTabTasks = true;
            Assert.Equal("Tasks", vm.ActiveTab);
            Assert.False(vm.IsTabKappa);
            Assert.True(vm.IsTabTasks);
        }

        [Fact]
        public void CalculateCounts_ComputesAccurateCountsDirectly()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                new() { NeedKey = "1", SourceType = "Quest", IsKappaRequired = true, Amount = 2, CurrentCount = 2 }, // Complete
                new() { NeedKey = "2", SourceType = "Quest", IsKappaRequired = false, QuestNormalizedName = "collector", Amount = 1, CurrentCount = 0 },
                new() { NeedKey = "3", SourceType = "Hideout", Amount = 1, CurrentCount = 1 } // Complete
            };

            vm.SetCardsForTesting(cards);
            vm.CalculateCounts();

            Assert.Equal(3, vm.AllCount);
            Assert.Equal(2, vm.TasksCount);
            Assert.Equal(1, vm.HideoutCount);
            Assert.Equal(2, vm.KappaCount);
            Assert.Equal(2, vm.CompletedCount);
        }

        [Fact]
        public void ActiveTab_Kappa_ShowsOnlyKappaRequiredOrCollectorItems()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                // 1. Kappa quest item (ReqKappa = true, regular quest)
                CreateTestCard(needKey: "k1", sourceType: "Quest", isKappaRequired: true, questNormalizedName: "the_punisher_part_4", itemDisplayName: "Scav Vest"),
                // 2. Collector quest item (ReqKappa = false, but quest is "collector")
                CreateTestCard(needKey: "k2", sourceType: "Quest", isKappaRequired: false, questNormalizedName: "collector", itemDisplayName: "Kotton Beanie"),
                // 3. Collector quest item with mixed casing ("Collector")
                CreateTestCard(needKey: "k3", sourceType: "Quest", isKappaRequired: false, questNormalizedName: "Collector", itemDisplayName: "Silver Badge"),
                // 4. Non-Kappa quest item (ReqKappa = false, normal quest)
                CreateTestCard(needKey: "q1", sourceType: "Quest", isKappaRequired: false, questNormalizedName: "checking", itemDisplayName: "Bronze Pocket Watch"),
                // 5. Hideout station item (Hideout, ReqKappa = false)
                CreateTestCard(needKey: "h1", sourceType: "Hideout", isKappaRequired: false, questNormalizedName: null, itemDisplayName: "Car Battery"),
            };

            vm.SetCardsForTesting(cards);

            // Initial state: Default tab is "All"
            Assert.True(vm.IsTabAll);
            Assert.False(vm.IsTabKappa);
            Assert.Equal(5, vm.FilteredCards.Count);

            // Switch to Kappa tab
            vm.ActiveTab = "Kappa";

            // Verify boolean tab indicators
            Assert.True(vm.IsTabKappa);
            Assert.False(vm.IsTabAll);
            Assert.False(vm.IsTabTasks);
            Assert.False(vm.IsTabHideout);
            Assert.False(vm.IsTabCompleted);

            // Filtered cards must contain exactly cards k1, k2, and k3 (3 cards total)
            Assert.Equal(3, vm.FilteredCards.Count);
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "k1" && c.ItemDisplayName == "Scav Vest");
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "k2" && c.ItemDisplayName == "Kotton Beanie");
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "k3" && c.ItemDisplayName == "Silver Badge");

            // Must NOT contain non-Kappa quest or hideout items
            Assert.DoesNotContain(vm.FilteredCards, c => c.NeedKey == "q1");
            Assert.DoesNotContain(vm.FilteredCards, c => c.NeedKey == "h1");
        }

        [Fact]
        public void KappaCount_AccuratelyReflectsKappaCardsCount()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                // 1. Kappa quest item (active)
                CreateTestCard(needKey: "k1", sourceType: "Quest", isKappaRequired: true, questNormalizedName: "punisher_4", amount: 2, currentCount: 0),
                // 2. Kappa quest item (completed)
                CreateTestCard(needKey: "k2", sourceType: "Quest", isKappaRequired: true, questNormalizedName: "grenadier", amount: 1, currentCount: 1),
                // 3. Collector quest item
                CreateTestCard(needKey: "c1", sourceType: "Quest", isKappaRequired: false, questNormalizedName: "collector", amount: 1, currentCount: 0),
                // 4. Non-Kappa quest item (active)
                CreateTestCard(needKey: "q1", sourceType: "Quest", isKappaRequired: false, questNormalizedName: "checking", amount: 1, currentCount: 0),
                // 5. Hideout item
                CreateTestCard(needKey: "h1", sourceType: "Hideout", isKappaRequired: false, amount: 4, currentCount: 2),
            };

            vm.SetCardsForTesting(cards);

            // Verify initial counts across all categories
            Assert.Equal(5, vm.AllCount);
            Assert.Equal(4, vm.TasksCount);
            Assert.Equal(1, vm.HideoutCount);
            Assert.Equal(1, vm.CompletedCount);
            Assert.Equal(3, vm.KappaCount); // k1, k2, c1

            // Update with non-kappa cards only
            var nonKappaCards = new List<NeededItemCardViewModel>
            {
                CreateTestCard(needKey: "q1", sourceType: "Quest", isKappaRequired: false, questNormalizedName: "quest_x"),
                CreateTestCard(needKey: "h1", sourceType: "Hideout", isKappaRequired: false),
            };
            vm.SetCardsForTesting(nonKappaCards);
            Assert.Equal(2, vm.AllCount);
            Assert.Equal(1, vm.TasksCount);
            Assert.Equal(1, vm.HideoutCount);
            Assert.Equal(0, vm.KappaCount);

            // Update with 100% Kappa/Collector cards
            var allKappaCards = new List<NeededItemCardViewModel>
            {
                CreateTestCard(needKey: "k1", sourceType: "Quest", isKappaRequired: true, questNormalizedName: "q1"),
                CreateTestCard(needKey: "k2", sourceType: "Quest", isKappaRequired: true, questNormalizedName: "q2"),
                CreateTestCard(needKey: "c1", sourceType: "Quest", isKappaRequired: false, questNormalizedName: "collector"),
            };
            vm.SetCardsForTesting(allKappaCards);
            Assert.Equal(3, vm.AllCount);
            Assert.Equal(3, vm.KappaCount);
        }

        [Fact]
        public void ActiveTab_Kappa_CombinesWithSearchAndFirFilters()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                // Kappa items
                CreateTestCard(needKey: "k1", isKappaRequired: true, questNormalizedName: "living_high_1", itemDisplayName: "Bronze Lion", foundInRaid: true, sourceName: "Living High is Not a Crime - Part 1"),
                CreateTestCard(needKey: "k2", isKappaRequired: true, questNormalizedName: "living_high_2", itemDisplayName: "Gold Skull Ring", foundInRaid: false, sourceName: "Living High is Not a Crime - Part 2"),
                CreateTestCard(needKey: "c1", isKappaRequired: false, questNormalizedName: "collector", itemDisplayName: "Kotton Beanie", foundInRaid: true, sourceName: "Collector"),
                CreateTestCard(needKey: "c2", isKappaRequired: false, questNormalizedName: "collector", itemDisplayName: "Silver Badge", foundInRaid: false, sourceName: "Collector"),
                // Non-Kappa item that matches search and FIR (to test it remains excluded)
                CreateTestCard(needKey: "nk1", isKappaRequired: false, questNormalizedName: "other_quest", itemDisplayName: "Bronze Lion", foundInRaid: true, sourceName: "Other Quest"),
            };

            vm.SetCardsForTesting(cards);

            // Switch to Kappa tab
            vm.ActiveTab = "Kappa";
            Assert.Equal(4, vm.FilteredCards.Count);

            // 1. FIR Filter: FIR only
            vm.FirFilter = "FIR";
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.All(vm.FilteredCards, c =>
            {
                Assert.True(c.FoundInRaid);
                Assert.True(c.IsKappaRequired || string.Equals(c.QuestNormalizedName, "collector", StringComparison.OrdinalIgnoreCase));
            });
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "k1"); // Bronze Lion (Kappa, FIR)
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "c1"); // Kotton Beanie (Collector, FIR)
            Assert.DoesNotContain(vm.FilteredCards, c => c.NeedKey == "nk1"); // Excluded because not Kappa

            // 2. Search + FIR Filter simultaneously
            vm.SearchText = "Kotton";
            Assert.Single(vm.FilteredCards);
            Assert.Equal("Kotton Beanie", vm.FilteredCards[0].ItemDisplayName);

            // 3. FIR Filter: Non-FIR only
            vm.SearchText = string.Empty;
            vm.FirFilter = "Non-FIR";
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.All(vm.FilteredCards, c =>
            {
                Assert.False(c.FoundInRaid);
                Assert.True(c.IsKappaRequired || string.Equals(c.QuestNormalizedName, "collector", StringComparison.OrdinalIgnoreCase));
            });
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "k2"); // Gold Skull Ring (Non-FIR)
            Assert.Contains(vm.FilteredCards, c => c.NeedKey == "c2"); // Silver Badge (Non-FIR)

            // 4. Search in Non-FIR
            vm.SearchText = "Badge";
            Assert.Single(vm.FilteredCards);
            Assert.Equal("Silver Badge", vm.FilteredCards[0].ItemDisplayName);

            // 5. Reset filters to All and clear search
            vm.FirFilter = "All";
            vm.SearchText = string.Empty;
            Assert.Equal(4, vm.FilteredCards.Count);
        }

        [Fact]
        public void ActiveTab_SwitchingBetweenTabs_PreservesCorrectCounts()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                // Quest items for Kappa:
                // 1. Incomplete Kappa quest item
                CreateTestCard(needKey: "k_inc", sourceType: "Quest", isKappaRequired: true, questNormalizedName: "q_kappa1", amount: 2, currentCount: 0),
                // 2. Completed Kappa quest item
                CreateTestCard(needKey: "k_comp", sourceType: "Quest", isKappaRequired: true, questNormalizedName: "q_kappa2", amount: 1, currentCount: 1),
                // 3. Collector quest item (incomplete)
                CreateTestCard(needKey: "col_inc", sourceType: "Quest", isKappaRequired: false, questNormalizedName: "collector", amount: 1, currentCount: 0),

                // Quest items NOT for Kappa:
                // 4. Incomplete regular quest item
                CreateTestCard(needKey: "reg_inc", sourceType: "Quest", isKappaRequired: false, questNormalizedName: "q_reg1", amount: 3, currentCount: 1),
                // 5. Completed regular quest item
                CreateTestCard(needKey: "reg_comp", sourceType: "Quest", isKappaRequired: false, questNormalizedName: "q_reg2", amount: 2, currentCount: 2),

                // Hideout items:
                // 6. Incomplete hideout item
                CreateTestCard(needKey: "h_inc", sourceType: "Hideout", isKappaRequired: false, amount: 5, currentCount: 2),
                // 7. Completed hideout item
                CreateTestCard(needKey: "h_comp", sourceType: "Hideout", isKappaRequired: false, amount: 1, currentCount: 1),
            };

            vm.SetCardsForTesting(cards);

            // Baseline counts verification:
            // Total = 7
            // Tasks = 5 (k_inc, k_comp, col_inc, reg_inc, reg_comp)
            // Hideout = 2 (h_inc, h_comp)
            // Kappa = 3 (k_inc, k_comp, col_inc)
            // Completed = 3 (k_comp, reg_comp, h_comp)
            void VerifyTabCounts()
            {
                Assert.Equal(7, vm.AllCount);
                Assert.Equal(5, vm.TasksCount);
                Assert.Equal(2, vm.HideoutCount);
                Assert.Equal(3, vm.KappaCount);
                Assert.Equal(3, vm.CompletedCount);
            }

            VerifyTabCounts();

            // 1. Tab: All
            vm.ActiveTab = "All";
            Assert.True(vm.IsTabAll);
            Assert.False(vm.IsTabTasks);
            Assert.False(vm.IsTabHideout);
            Assert.False(vm.IsTabKappa);
            Assert.False(vm.IsTabCompleted);
            Assert.Equal(7, vm.FilteredCards.Count);
            VerifyTabCounts();

            // 2. Tab: Tasks
            vm.ActiveTab = "Tasks";
            Assert.False(vm.IsTabAll);
            Assert.True(vm.IsTabTasks);
            Assert.False(vm.IsTabHideout);
            Assert.False(vm.IsTabKappa);
            Assert.False(vm.IsTabCompleted);
            Assert.Equal(5, vm.FilteredCards.Count);
            Assert.All(vm.FilteredCards, c => Assert.True(c.IsQuest));
            VerifyTabCounts();

            // 3. Tab: Hideout
            vm.ActiveTab = "Hideout";
            Assert.False(vm.IsTabAll);
            Assert.False(vm.IsTabTasks);
            Assert.True(vm.IsTabHideout);
            Assert.False(vm.IsTabKappa);
            Assert.False(vm.IsTabCompleted);
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.All(vm.FilteredCards, c => Assert.True(c.IsHideout));
            VerifyTabCounts();

            // 4. Tab: Kappa
            vm.ActiveTab = "Kappa";
            Assert.False(vm.IsTabAll);
            Assert.False(vm.IsTabTasks);
            Assert.False(vm.IsTabHideout);
            Assert.True(vm.IsTabKappa);
            Assert.False(vm.IsTabCompleted);
            Assert.Equal(3, vm.FilteredCards.Count);
            Assert.All(vm.FilteredCards, c => Assert.True(c.IsKappaRequired || string.Equals(c.QuestNormalizedName, "collector", StringComparison.OrdinalIgnoreCase)));
            VerifyTabCounts();

            // 5. Tab: Completed
            vm.ActiveTab = "Completed";
            Assert.False(vm.IsTabAll);
            Assert.False(vm.IsTabTasks);
            Assert.False(vm.IsTabHideout);
            Assert.False(vm.IsTabKappa);
            Assert.True(vm.IsTabCompleted);
            Assert.Equal(3, vm.FilteredCards.Count);
            Assert.All(vm.FilteredCards, c => Assert.True(c.IsComplete));
            VerifyTabCounts();

            // 6. Return to All
            vm.ActiveTab = "All";
            Assert.True(vm.IsTabAll);
            Assert.Equal(7, vm.FilteredCards.Count);
            VerifyTabCounts();
        }

        [Fact]
        public void Filter_FirStatus_FiltersCardsAccurately()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                new() { NeedKey = "1", FoundInRaid = true, Amount = 1 },
                new() { NeedKey = "2", FoundInRaid = false, Amount = 1 },
                new() { NeedKey = "3", FoundInRaid = true, Amount = 2 },
            };

            vm.SetCardsForTesting(cards);

            // All
            vm.FirFilter = "All";
            Assert.Equal(3, vm.FilteredCards.Count);

            // FIR only
            vm.FirFilter = "FIR";
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.All(vm.FilteredCards, c => Assert.True(c.FoundInRaid));

            // Non-FIR only
            vm.FirFilter = "Non-FIR";
            Assert.Single(vm.FilteredCards);
            Assert.False(vm.FilteredCards[0].FoundInRaid);
        }

        [Fact]
        public void Filter_HideOwned_ExcludesCompletedItems()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                new() { NeedKey = "1", Amount = 5, CurrentCount = 2 },
                new() { NeedKey = "2", Amount = 3, CurrentCount = 3 }, // Owned/complete
                new() { NeedKey = "3", Amount = 1, CurrentCount = 0 },
            };

            vm.SetCardsForTesting(cards);
            Assert.Equal(3, vm.FilteredCards.Count);

            vm.HideOwned = true;
            Assert.Equal(2, vm.FilteredCards.Count);
            Assert.DoesNotContain(vm.FilteredCards, c => c.IsComplete);
        }

        [Fact]
        public void Filter_HideSpecialEquipment_ExcludesPlaceableGear()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                new() { NeedKey = "1", ItemDisplayName = "MS2000 Marker", IsSpecialEquipment = true, Amount = 3 },
                new() { NeedKey = "2", ItemDisplayName = "Car Battery", IsSpecialEquipment = false, Amount = 1 },
                new() { NeedKey = "3", ItemDisplayName = "Wi-Fi Camera", IsSpecialEquipment = true, Amount = 2 },
            };

            vm.SetCardsForTesting(cards);
            Assert.Equal(3, vm.FilteredCards.Count);

            vm.HideSpecialEquipment = true;
            Assert.Single(vm.FilteredCards);
            Assert.Equal("Car Battery", vm.FilteredCards[0].ItemDisplayName);
        }

        [Fact]
        public void Filter_KappaOnly_RestrictsToCollectorQuests()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                new() { NeedKey = "1", SourceType = "Quest", IsKappaRequired = true, ItemDisplayName = "Item A", Amount = 1 },
                new() { NeedKey = "2", SourceType = "Quest", IsKappaRequired = false, ItemDisplayName = "Item B", Amount = 1 },
                new() { NeedKey = "3", SourceType = "Hideout", IsKappaRequired = false, ItemDisplayName = "Hideout Item", Amount = 1 },
            };

            vm.SetCardsForTesting(cards);
            Assert.Equal(3, vm.FilteredCards.Count);

            vm.KappaOnly = true;
            Assert.Single(vm.FilteredCards);
            Assert.Equal("Item A", vm.FilteredCards[0].ItemDisplayName);
        }

        [Fact]
        public void Filter_Search_MatchesNameAndSource()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                new() { NeedKey = "1", ItemDisplayName = "Spark Plug", SourceName = "Car Repair", Amount = 8 },
                new() { NeedKey = "2", ItemDisplayName = "Car Battery", SourceName = "Car Repair", Amount = 4 },
                new() { NeedKey = "3", ItemDisplayName = "Gas Analyzer", SourceName = "Sanitary Standards", Amount = 1 },
            };

            vm.SetCardsForTesting(cards);

            // By item name
            vm.SearchText = "Spark";
            Assert.Single(vm.FilteredCards);
            Assert.Equal("Spark Plug", vm.FilteredCards[0].ItemDisplayName);

            // By quest source name
            vm.SearchText = "Car Repair";
            Assert.Equal(2, vm.FilteredCards.Count);

            // Clear search
            vm.SearchText = string.Empty;
            Assert.Equal(3, vm.FilteredCards.Count);
        }

        [Fact]
        public void Filter_Reset_RestoresDefaults()
        {
            var vm = new NeededItemsViewModel
            {
                FirFilter = "FIR",
                HideOwned = true,
                HideSpecialEquipment = true,
                KappaOnly = true,
                SortMode = "Quantity"
            };

            vm.ResetFilters();

            Assert.Equal("All", vm.FirFilter);
            Assert.False(vm.HideOwned);
            Assert.False(vm.HideSpecialEquipment);
            Assert.False(vm.KappaOnly);
            Assert.Equal("Priority", vm.SortMode);
        }

        [Fact]
        public void Sorting_OrdersCardsCorrectly()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                new() { NeedKey = "1", ItemDisplayName = "Zebra", TraderName = "Prapor", Amount = 10, PriorityScore = 10 },
                new() { NeedKey = "2", ItemDisplayName = "Apple", TraderName = "Therapist", Amount = 2, PriorityScore = 100 },
                new() { NeedKey = "3", ItemDisplayName = "Banana", TraderName = "Jaeger", Amount = 5, PriorityScore = 50 },
            };

            vm.SetCardsForTesting(cards);

            // Sort by Name
            vm.SortMode = "Name";
            Assert.Equal("Apple", vm.FilteredCards[0].ItemDisplayName);
            Assert.Equal("Banana", vm.FilteredCards[1].ItemDisplayName);
            Assert.Equal("Zebra", vm.FilteredCards[2].ItemDisplayName);

            // Sort by Quantity
            vm.SortMode = "Quantity";
            Assert.Equal(10, vm.FilteredCards[0].Amount);
            Assert.Equal(5, vm.FilteredCards[1].Amount);
            Assert.Equal(2, vm.FilteredCards[2].Amount);

            // Sort by Trader
            vm.SortMode = "Trader";
            Assert.Equal("Jaeger", vm.FilteredCards[0].TraderName);
            Assert.Equal("Prapor", vm.FilteredCards[1].TraderName);
            Assert.Equal("Therapist", vm.FilteredCards[2].TraderName);

            // Sort by Priority
            vm.SortMode = "Priority";
            Assert.Equal(100, vm.FilteredCards[0].PriorityScore);
            Assert.Equal(50, vm.FilteredCards[1].PriorityScore);
            Assert.Equal(10, vm.FilteredCards[2].PriorityScore);
        }

        [Fact]
        public void ItemsPage_CanInstantiateAndApplyTemplate_WithoutCrashing()
        {
            Exception? caught = null;
            Application? app = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    // Ensure Application exists for resource lookups
                    try
                    {
                        if (Application.Current == null)
                        {
                            app = new Application();
                        }
                        else
                        {
                            app = Application.Current;
                        }
                    }
                    catch { }

                    var page = new TarkovHelper.Pages.ItemsPage();
                    page.Measure(new Size(1200, 800));
                    page.Arrange(new Rect(0, 0, 1200, 800));
                    page.UpdateLayout();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
                finally
                {
                    try { app?.Dispatcher.InvokeShutdown(); } catch { }
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            try
            {
                typeof(Application).GetField("_appCreated", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.SetValue(null, false);
                typeof(Application).GetField("_current", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.SetValue(null, null);
            }
            catch { }

            Assert.Null(caught);
        }
    }
}
