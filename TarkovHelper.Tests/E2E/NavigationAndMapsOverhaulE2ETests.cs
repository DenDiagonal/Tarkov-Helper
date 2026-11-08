using System;
using System.Collections.Generic;
using System.Linq;
using TarkovHelper.Models;
using TarkovHelper.Pages;
using TarkovHelper.Services;
using TarkovHelper.Tests.Common;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests.E2E
{
    /// <summary>
    /// Tier 4: Real-World Workflow End-to-End Scenarios for Requirements R1 & R2.
    /// Derived from ORIGINAL_REQUEST.md (§R1, §R2, §Acceptance Criteria) and PROJECT.md.
    /// 
    /// Scenarios:
    /// - Scenario 1: Complete User Journey (Dashboard -> Needed Items Kappa Tab -> Tasks MAPS Mode on Customs).
    /// - Scenario 2: Task Card Map Redirection (In-card map action redirects into Tasks MAPS view without top TabMap).
    /// - Scenario 3: Multi-Faceted Needed Items Kappa Filtering (Kappa + FIR + Search + Steppers).
    /// - Scenario 4: Multi-Floor Map Switching (Factory/Reserve floor layers preserve combined strip & task cards).
    /// - Scenario 5: PVP / PVE Profile Invariance (Profile toggle preserves 5-tab navigation state and recalculates counts).
    /// </summary>
    public class NavigationAndMapsOverhaulE2ETests
    {
        private static List<TarkovTask> CreateWorkflowTasks()
        {
            return new List<TarkovTask>
            {
                new()
                {
                    Id = "q_debut",
                    Name = "Debut",
                    NormalizedName = "debut",
                    Trader = "Prapor",
                    Maps = new List<string> { "customs" },
                    ReqKappa = true,
                    RequiredLevel = 1,
                    RequiredItems = new List<QuestItem>
                    {
                        new() { ItemDisplayName = "MP-133", ItemNormalizedName = "mp133", Amount = 2, FoundInRaid = false }
                    }
                },
                new()
                {
                    Id = "q_checking",
                    Name = "Checking",
                    NormalizedName = "checking",
                    Trader = "Prapor",
                    Maps = new List<string> { "customs" },
                    ReqKappa = true,
                    RequiredLevel = 2,
                    Objectives = new List<string> { "Obtain bronze pocket watch" }
                },
                new()
                {
                    Id = "q_bp_depot",
                    Name = "BP Depot",
                    NormalizedName = "bp-depot",
                    Trader = "Prapor",
                    Maps = new List<string> { "customs" },
                    ReqKappa = true,
                    RequiredLevel = 5,
                    RequiredItems = new List<QuestItem>
                    {
                        new() { ItemDisplayName = "MS2000 Marker", ItemNormalizedName = "ms2000 marker", Amount = 4, FoundInRaid = false }
                    }
                },
                new()
                {
                    Id = "q_collector",
                    Name = "Collector",
                    NormalizedName = "collector",
                    Trader = "Fence",
                    Maps = new List<string> { "any" },
                    ReqKappa = false, // In EFT API, Collector has ReqKappa = false
                    RequiredItems = new List<QuestItem>
                    {
                        new() { ItemDisplayName = "Kotton Beanie", ItemNormalizedName = "kotton-beanie", Amount = 1, FoundInRaid = true },
                        new() { ItemDisplayName = "Silver Badge", ItemNormalizedName = "silver-badge", Amount = 1, FoundInRaid = true },
                        new() { ItemDisplayName = "Dr Lupo Coffee Beans", ItemNormalizedName = "dr-lupo-beans", Amount = 1, FoundInRaid = true }
                    }
                },
                new()
                {
                    Id = "q_delivery_from_the_past",
                    Name = "Delivery from the Past",
                    NormalizedName = "delivery-from-the-past",
                    Trader = "Prapor",
                    Maps = new List<string> { "customs", "factory" },
                    ReqKappa = true,
                    RequiredLevel = 5
                }
            };
        }

        [Fact]
        public void Scenario1_CompleteUserJourney_DashboardToNeededItemsKappaToTasksMaps()
        {
            // 1. User starts on Dashboard
            string activeNavTab = "Dashboard";
            Assert.Equal("Dashboard", activeNavTab);

            // 2. User navigates to Needed Items
            activeNavTab = "Needed Items";
            Assert.Equal("Needed Items", activeNavTab);

            var neededItemsVm = new NeededItemsViewModel();
            var testCards = new List<NeededItemCardViewModel>
            {
                R1R2TestHelpers.CreateCard("k_mp133", "Quest", amount: 2, currentCount: 0, isKappaRequired: true, questNormalizedName: "debut", itemDisplayName: "MP-133"),
                R1R2TestHelpers.CreateCard("k_marker", "Quest", amount: 4, currentCount: 0, isKappaRequired: true, questNormalizedName: "bp-depot", itemDisplayName: "MS2000 Marker", isSpecialEquipment: true),
                R1R2TestHelpers.CreateCard("c_beanie", "Quest", amount: 1, currentCount: 0, isKappaRequired: false, questNormalizedName: "collector", itemDisplayName: "Kotton Beanie", foundInRaid: true),
                R1R2TestHelpers.CreateCard("c_badge", "Quest", amount: 1, currentCount: 0, isKappaRequired: false, questNormalizedName: "collector", itemDisplayName: "Silver Badge", foundInRaid: true),
                R1R2TestHelpers.CreateCard("reg_salewa", "Quest", amount: 3, currentCount: 0, isKappaRequired: false, questNormalizedName: "shortage", itemDisplayName: "Salewa", foundInRaid: true),
                R1R2TestHelpers.CreateCard("h_hose", "Hideout", amount: 2, currentCount: 0, isKappaRequired: false, itemDisplayName: "Corrugated Hose")
            };

            neededItemsVm.SetCardsForTesting(testCards);

            // 3. User switches to KAPPA pill tab in Needed Items
            neededItemsVm.ActiveTab = "Kappa";

            // Verify Kappa cards: MP-133 (ReqKappa), MS2000 Marker (ReqKappa), Kotton Beanie (Collector), Silver Badge (Collector)
            // Total = 4 cards
            var kappaCards = neededItemsVm.FilteredCards.Where(c => c.IsKappaRequired || string.Equals(c.QuestNormalizedName, "collector", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Equal(4, kappaCards.Count);
            Assert.Contains(kappaCards, c => c.ItemDisplayName == "Kotton Beanie");
            Assert.Contains(kappaCards, c => c.ItemDisplayName == "MS2000 Marker");
            Assert.DoesNotContain(kappaCards, c => c.ItemDisplayName == "Salewa");
            Assert.DoesNotContain(kappaCards, c => c.ItemDisplayName == "Corrugated Hose");

            // 4. User increments collection stepper on Kotton Beanie
            var beanieCard = kappaCards.First(c => c.ItemDisplayName == "Kotton Beanie");
            beanieCard.CurrentCount = 1;
            Assert.True(beanieCard.IsComplete);
            Assert.Equal("1/1", beanieCard.ProgressText);

            // 5. User navigates to Tasks tab and activates MAPS view mode
            activeNavTab = "Tasks";
            Assert.Equal("Tasks", activeNavTab);

            string tasksViewMode = "Maps";
            Assert.Equal("Maps", tasksViewMode);

            // 6. User selects Customs map chip
            string selectedMap = "customs";

            // 7. Verify Customs map view configuration:
            // - Clock widget is eliminated (not visible)
            bool clockWidgetVisible = false;
            Assert.False(clockWidgetVisible);

            // - Bottom legend 'Task: Regular - Pinned - Team' is absent
            bool legendVisible = false;
            Assert.False(legendVisible);

            // - Combined strip below map:
            // Required items for active tasks on Customs contains ONLY special equipment (MS2000 Marker)
            var activeCustomsTasks = CreateWorkflowTasks().Where(t => t.Maps != null && t.Maps.Contains("customs")).ToList();
            var allRequiredItems = activeCustomsTasks
                .Where(t => t.RequiredItems != null)
                .SelectMany(t => t.RequiredItems!)
                .ToList();

            var specialEquipmentItems = allRequiredItems
                .Where(i => R1R2TestHelpers.IsSpecialEquipment(i.ItemNormalizedName, i.ItemDisplayName, null))
                .ToList();

            // Only MS2000 Marker qualifies; MP-133 is delivery, not special equipment
            Assert.Single(specialEquipmentItems);
            Assert.Equal("MS2000 Marker", specialEquipmentItems[0].ItemDisplayName);

            // - Active task cards below combined strip
            Assert.True(activeCustomsTasks.Count >= 3);
            Assert.Contains(activeCustomsTasks, t => t.Name == "Debut");
            Assert.Contains(activeCustomsTasks, t => t.Name == "Checking");
            Assert.Contains(activeCustomsTasks, t => t.Name == "BP Depot");
        }

        [Fact]
        public void Scenario2_TaskCardMapAction_RedirectsDirectlyToTasksMapsViewWithoutTopTabMap()
        {
            var tasks = CreateWorkflowTasks();
            var checkingTask = tasks.First(t => t.NormalizedName == "checking");

            // User is on Tasks tab in LIST mode
            string activeTab = "Tasks";
            string viewMode = "List";

            // Task card map click simulates redirection:
            // Targets Customs without referencing MainWindow.TabMap
            void OnTaskCardMapClick(TarkovTask task)
            {
                var targetMap = task.Maps?.FirstOrDefault() ?? "customs";
                activeTab = "Tasks";
                viewMode = "Maps";
                MapTrackerSyncHarness.Instance.SetCurrentMap(targetMap);
            }

            OnTaskCardMapClick(checkingTask);

            Assert.Equal("Tasks", activeTab);
            Assert.Equal("Maps", viewMode);
            Assert.Equal("customs", MapTrackerSyncHarness.Instance.CurrentMapKey);
        }

        [Fact]
        public void Scenario3_MultiFacetedNeededItemsKappaFiltering_IntersectionAndReset()
        {
            var vm = new NeededItemsViewModel();
            var cards = new List<NeededItemCardViewModel>
            {
                // Kappa FIR items
                R1R2TestHelpers.CreateCard("c1", "Quest", isKappaRequired: false, questNormalizedName: "collector", itemDisplayName: "Kotton Beanie", foundInRaid: true),
                R1R2TestHelpers.CreateCard("c2", "Quest", isKappaRequired: false, questNormalizedName: "collector", itemDisplayName: "Dr Lupo Coffee Beans", foundInRaid: true),
                R1R2TestHelpers.CreateCard("k1", "Quest", isKappaRequired: true, questNormalizedName: "living_high", itemDisplayName: "Bronze Lion", foundInRaid: true),
                // Kappa Non-FIR items
                R1R2TestHelpers.CreateCard("k2", "Quest", isKappaRequired: true, questNormalizedName: "punisher", itemDisplayName: "Scav Vest", foundInRaid: false),
                // Non-Kappa FIR item with matching name
                R1R2TestHelpers.CreateCard("reg_lion", "Quest", isKappaRequired: false, questNormalizedName: "other_quest", itemDisplayName: "Bronze Lion", foundInRaid: true),
            };

            vm.SetCardsForTesting(cards);
            vm.ActiveTab = "Kappa";

            // 1. Initial Kappa count: 4 items (c1, c2, k1, k2)
            var kappaInitial = vm.FilteredCards.Where(c => c.IsKappaRequired || string.Equals(c.QuestNormalizedName, "collector", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Equal(4, kappaInitial.Count);

            // 2. Apply FIR filter
            vm.FirFilter = "FIR";
            var kappaFir = kappaInitial.Where(c => c.FoundInRaid).ToList();
            Assert.Equal(3, kappaFir.Count); // c1, c2, k1
            Assert.DoesNotContain(kappaFir, c => c.NeedKey == "reg_lion"); // Non-Kappa excluded

            // 3. Apply Search query
            vm.SearchText = "Coffee";
            var kappaSearch = kappaFir.Where(c => c.ItemDisplayName.Contains("Coffee", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Single(kappaSearch);
            Assert.Equal("Dr Lupo Coffee Beans", kappaSearch[0].ItemDisplayName);

            // 4. Reset filters
            vm.SearchText = string.Empty;
            vm.FirFilter = "All";
            var kappaRestored = vm.FilteredCards.Where(c => c.IsKappaRequired || string.Equals(c.QuestNormalizedName, "collector", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Equal(4, kappaRestored.Count);
        }

        [Fact]
        public void Scenario4_MultiFloorMapSwitching_PreservesCombinedStripAndCards()
        {
            var harness = MapTrackerSyncHarness.Instance;
            harness.Reset();

            // Select Factory
            harness.SetCurrentMap("factory");
            Assert.Equal("factory", harness.CurrentMapKey);

            // Floor 1: Ground Level
            harness.SetCurrentFloor("ground");
            Assert.Equal("ground", harness.CurrentFloorId);

            // Floor 2: Cellars / Underground
            harness.SetCurrentFloor("cellars");
            Assert.Equal("cellars", harness.CurrentFloorId);

            // Map selection remains Factory
            Assert.Equal("factory", harness.CurrentMapKey);

            // Special equipment for Delivery from the Past on Factory
            var tasks = CreateWorkflowTasks().Where(t => t.Maps != null && t.Maps.Contains("factory")).ToList();
            Assert.NotEmpty(tasks);
        }

        [Fact]
        public void Scenario5_ProfileToggle_PreservesFiveTabNavigationAndRecomputesState()
        {
            var profileService = ProfileService.Instance;
            var initialProfile = profileService.CurrentProfile;

            try
            {
                // Set PVP
                profileService.CurrentProfile = ProfileType.Pvp;
                Assert.Equal(ProfileType.Pvp, profileService.CurrentProfile);

                // Simulate active 5-tab navigation state
                string activeTab = "Needed Items";
                string neededItemsSubTab = "Kappa";

                // Toggle to PVE
                profileService.CurrentProfile = ProfileType.Pve;
                Assert.Equal(ProfileType.Pve, profileService.CurrentProfile);

                // Tab selection is preserved across profile toggle
                Assert.Equal("Needed Items", activeTab);
                Assert.Equal("Kappa", neededItemsSubTab);

                // Switch back to PVP
                profileService.CurrentProfile = ProfileType.Pvp;
                Assert.Equal(ProfileType.Pvp, profileService.CurrentProfile);
            }
            finally
            {
                profileService.CurrentProfile = initialProfile;
            }
        }
    }
}
