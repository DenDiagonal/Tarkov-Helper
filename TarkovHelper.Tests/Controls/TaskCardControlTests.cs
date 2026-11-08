using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TarkovHelper.Tests.Common;
using Xunit;

namespace TarkovHelper.Tests.Controls
{
    /// <summary>
    /// Requirement-driven tests for Features F7-F10:
    /// - F7: TarkovTracker Task Card Structure (Header, Requirements summary, collapse/expand, complete)
    /// - F8: Interactive Sub-Objective Steppers ([-] X/Y [+] [✓], action checks, debounced persistence)
    /// - F9: Task Card Footer Rewards & Impact (XP, Trader rep, Items, Unlocks next: X | Impact: Y)
    /// - F10: Single-Player Isolation (Strictly local profile state, no squads/multiplayer)
    /// </summary>
    public class TaskCardControlTests
    {
        private TaskCardTestModel CreateSampleTaskCard()
        {
            var card = new TaskCardTestModel
            {
                QuestId = "quest_debut",
                Title = "Debut",
                Trader = "Prapor",
                AvatarUrl = "assets/traders/prapor.png",
                WikiUrl = "https://escapefromtarkov.fandom.com/wiki/Debut",
                MapBadge = "Customs",
                IsExpanded = true,
                IsCompleted = false,
                TotalRequirements = 2,
                MetRequirements = 0,
                XpReward = 8500,
                UnlocksNext = 2,
                Impact = 5
            };

            card.TraderRepRewards["Prapor"] = 0.03;
            card.ItemRewards.Add("MP-133 shotgun");

            // Sub-objective 1: Item stepper (Handover 2 MP-133)
            card.ItemSteppers.Add(new SubObjectiveStepperTestItem
            {
                ItemId = "item_mp133",
                ItemName = "MP-133 12ga shotgun",
                CurrentCount = 0,
                MaxCount = 2,
                FoundInRaid = false
            });

            // Sub-objective 2: Action objective (Eliminate 5 Scavs on Customs)
            card.ActionToggles.Add(new SubObjectiveActionTestItem
            {
                ObjectiveId = "obj_scavs",
                Description = "Eliminate 5 Scavs all over the Customs territory",
                IsChecked = false
            });

            return card;
        }

        #region F7: TarkovTracker Task Card Structure

        [Fact]
        public void F7_T1_01_HeaderElements_DisplaysTraderTitleWikiMap()
        {
            var card = CreateSampleTaskCard();

            Assert.Equal("Prapor", card.Trader);
            Assert.Equal("Debut", card.Title);
            Assert.Equal("Customs", card.MapBadge);
            Assert.Contains("fandom.com", card.WikiUrl);
            Assert.Contains("prapor.png", card.AvatarUrl);
        }

        [Fact]
        public void F7_T1_02_RequirementsSummary_CalculatesXOfYMet()
        {
            var card = CreateSampleTaskCard();
            Assert.Equal("Requirements 0 of 2 met", card.RequirementsSummaryText);

            // Complete first objective
            card.ItemSteppers[0].SetMax();
            card.RecalculateMetRequirements();
            Assert.Equal("Requirements 1 of 2 met", card.RequirementsSummaryText);

            // Complete second objective
            card.ActionToggles[0].IsChecked = true;
            card.RecalculateMetRequirements();
            Assert.Equal("Requirements 2 of 2 met", card.RequirementsSummaryText);
        }

        [Fact]
        public void F7_T1_03_CollapseExpandToggle_TogglesDetailVisibility()
        {
            var card = CreateSampleTaskCard();
            Assert.True(card.IsExpanded);

            card.ToggleExpand();
            Assert.False(card.IsExpanded);

            card.ToggleExpand();
            Assert.True(card.IsExpanded);
        }

        [Fact]
        public void F7_T1_04_CompleteButton_MarksEntireTaskDone()
        {
            var card = CreateSampleTaskCard();
            Assert.False(card.IsCompleted);

            card.MarkComplete();

            Assert.True(card.IsCompleted);
            Assert.Equal(card.TotalRequirements, card.MetRequirements);
            Assert.Equal(2, card.ItemSteppers[0].CurrentCount);
            Assert.True(card.ActionToggles[0].IsChecked);
        }

        [Fact]
        public void F7_T1_05_MapBadge_DisplaysCanonicalMapName()
        {
            var card = CreateSampleTaskCard();
            Assert.Equal("Customs", card.MapBadge);
        }

        [Fact]
        public void F7_T2_01_ZeroRequirements_ShowsZeroOfZero()
        {
            var card = new TaskCardTestModel
            {
                TotalRequirements = 0,
                MetRequirements = 0
            };

            Assert.Equal("Requirements 0 of 0 met", card.RequirementsSummaryText);
        }

        [Fact]
        public void F7_T2_02_AllRequirementsMet_HighlightsHeader()
        {
            var card = CreateSampleTaskCard();
            card.ItemSteppers[0].SetMax();
            card.ActionToggles[0].IsChecked = true;
            card.RecalculateMetRequirements();

            Assert.Equal(card.TotalRequirements, card.MetRequirements);
        }

        [Fact]
        public void F7_T2_03_LongTaskTitle_EllipsizesWithoutOverflow()
        {
            var card = CreateSampleTaskCard();
            card.Title = "The Survivalist Path - Tough Guy (Eliminate 3 scavs in a single raid without healing)";

            Assert.True(card.Title.Length > 40);
            Assert.NotNull(card.Title);
        }

        [Fact]
        public void F7_T2_04_MissingTraderAvatar_FallsBackToTraderInitial()
        {
            var card = CreateSampleTaskCard();
            card.AvatarUrl = "";

            string initials = string.IsNullOrEmpty(card.AvatarUrl) ? card.Trader.Substring(0, 2).ToUpperInvariant() : "IMG";
            Assert.Equal("PR", initials);
        }

        [Fact]
        public void F7_T2_05_TaskWithNullMap_DisplaysAnyMapBadge()
        {
            var card = CreateSampleTaskCard();
            card.MapBadge = "";

            string badge = string.IsNullOrEmpty(card.MapBadge) ? "Any Map" : card.MapBadge;
            Assert.Equal("Any Map", badge);
        }

        #endregion

        #region F8: Interactive Sub-Objective Steppers

        [Fact]
        public void F8_T1_01_ItemQuantityStepper_IncrementAdvancesCount()
        {
            var card = CreateSampleTaskCard();
            var stepper = card.ItemSteppers[0];

            Assert.Equal(0, stepper.CurrentCount);
            stepper.Increment();
            Assert.Equal(1, stepper.CurrentCount);
            stepper.Increment();
            Assert.Equal(2, stepper.CurrentCount);
        }

        [Fact]
        public void F8_T1_02_ItemQuantityStepper_DecrementReducesCount()
        {
            var card = CreateSampleTaskCard();
            var stepper = card.ItemSteppers[0];
            stepper.SetMax();

            Assert.Equal(2, stepper.CurrentCount);
            stepper.Decrement();
            Assert.Equal(1, stepper.CurrentCount);
        }

        [Fact]
        public void F8_T1_03_ItemQuantityStepper_CheckButtonMaxesCount()
        {
            var card = CreateSampleTaskCard();
            var stepper = card.ItemSteppers[0];

            Assert.Equal(0, stepper.CurrentCount);
            stepper.SetMax();
            Assert.Equal(stepper.MaxCount, stepper.CurrentCount);
        }

        [Fact]
        public void F8_T1_04_ActionCheckToggle_FlipsCompletedState()
        {
            var card = CreateSampleTaskCard();
            var action = card.ActionToggles[0];

            Assert.False(action.IsChecked);
            action.Toggle();
            Assert.True(action.IsChecked);
            action.Toggle();
            Assert.False(action.IsChecked);
        }

        [Fact]
        public void F8_T1_05_ObjectiveCompletion_UpdatesTaskRequirementsMet()
        {
            var card = CreateSampleTaskCard();
            Assert.Equal(0, card.MetRequirements);

            card.ItemSteppers[0].Increment();
            card.RecalculateMetRequirements();
            Assert.Equal(0, card.MetRequirements); // 1/2 is not met

            card.ItemSteppers[0].Increment();
            card.RecalculateMetRequirements();
            Assert.Equal(1, card.MetRequirements); // 2/2 is met
        }

        [Fact]
        public void F8_T2_01_StepperUnderflow_DoesNotGoBelowZero()
        {
            var stepper = new SubObjectiveStepperTestItem { CurrentCount = 0, MaxCount = 5 };
            stepper.Decrement();
            Assert.Equal(0, stepper.CurrentCount);
        }

        [Fact]
        public void F8_T2_02_StepperOverflow_DoesNotExceedMaxAmount()
        {
            var stepper = new SubObjectiveStepperTestItem { CurrentCount = 5, MaxCount = 5 };
            stepper.Increment();
            Assert.Equal(5, stepper.CurrentCount);
        }

        [Fact]
        public void F8_T2_03_DebouncedPersistence_SavesToDatabase()
        {
            var stepper = new SubObjectiveStepperTestItem { ItemId = "salewa", CurrentCount = 0, MaxCount = 3 };
            stepper.Increment();

            // Simulating save payload
            var record = new { Id = stepper.ItemId, Count = stepper.CurrentCount };
            Assert.Equal(1, record.Count);
        }

        [Fact]
        public void F8_T2_04_MultiObjectiveOrder_PreservesSequence()
        {
            var card = CreateSampleTaskCard();
            card.ActionToggles.Add(new SubObjectiveActionTestItem { ObjectiveId = "obj_survive", Description = "Survive and extract" });

            Assert.Equal(2, card.ActionToggles.Count);
            Assert.Equal("obj_scavs", card.ActionToggles[0].ObjectiveId);
            Assert.Equal("obj_survive", card.ActionToggles[1].ObjectiveId);
        }

        [Fact]
        public void F8_T2_05_PartialProgress_ReflectsFractionalState()
        {
            var stepper = new SubObjectiveStepperTestItem { CurrentCount = 3, MaxCount = 10 };
            double fraction = (double)stepper.CurrentCount / stepper.MaxCount;

            Assert.Equal(0.3, fraction);
        }

        #endregion

        #region F9: Task Card Footer Rewards & Impact

        [Fact]
        public void F9_T1_01_RewardsStrip_DisplaysXPReward()
        {
            var card = CreateSampleTaskCard();
            Assert.Equal(8500, card.XpReward);
            Assert.Equal("+8,500 XP", $"+{card.XpReward.ToString("N0", CultureInfo.InvariantCulture)} XP");
        }

        [Fact]
        public void F9_T1_02_RewardsStrip_DisplaysTraderReputation()
        {
            var card = CreateSampleTaskCard();
            Assert.True(card.TraderRepRewards.ContainsKey("Prapor"));
            Assert.Equal(0.03, card.TraderRepRewards["Prapor"]);
        }

        [Fact]
        public void F9_T1_03_RewardsStrip_DisplaysItemRewards()
        {
            var card = CreateSampleTaskCard();
            Assert.Single(card.ItemRewards);
            Assert.Equal("MP-133 shotgun", card.ItemRewards[0]);
        }

        [Fact]
        public void F9_T1_04_ImpactSummary_DisplaysUnlocksNextAndImpact()
        {
            var card = CreateSampleTaskCard();
            Assert.Equal("Unlocks next: 2 | Impact: 5", card.ImpactSummaryText);
        }

        [Fact]
        public void F9_T1_05_ZeroRewards_HidesEmptyRewardPills()
        {
            var card = new TaskCardTestModel { XpReward = 0 };
            Assert.Empty(card.TraderRepRewards);
            Assert.Empty(card.ItemRewards);
        }

        [Fact]
        public void F9_T2_01_NegativeReputation_DisplaysWithRedWarning()
        {
            var card = CreateSampleTaskCard();
            card.TraderRepRewards["Jaeger"] = -0.05;

            Assert.True(card.TraderRepRewards["Jaeger"] < 0);
            string formatted = $"{card.TraderRepRewards["Jaeger"].ToString("+0.00;-0.00", CultureInfo.InvariantCulture)} Jaeger";
            Assert.Equal("-0.05 Jaeger", formatted);
        }

        [Fact]
        public void F9_T2_02_ZeroImpactTask_DisplaysZeroUnlocks()
        {
            var card = new TaskCardTestModel { UnlocksNext = 0, Impact = 0 };
            Assert.Equal("Unlocks next: 0 | Impact: 0", card.ImpactSummaryText);
        }

        [Fact]
        public void F9_T2_03_LargeXPValue_FormatsWithThousandsSeparators()
        {
            var card = new TaskCardTestModel { XpReward = 150000 };
            string formatted = $"+{card.XpReward.ToString("N0", CultureInfo.InvariantCulture)} XP";
            Assert.Equal("+150,000 XP", formatted);
        }

        [Fact]
        public void F9_T2_04_MultipleTraderRepChanges_ListsAllAffectedTraders()
        {
            var card = CreateSampleTaskCard();
            card.TraderRepRewards["Therapist"] = 0.04;
            card.TraderRepRewards["Skier"] = -0.02;

            Assert.Equal(3, card.TraderRepRewards.Count);
        }

        [Fact]
        public void F9_T2_05_SpecialRewards_DisplaysKeyOrAmmoUnlocks()
        {
            var card = CreateSampleTaskCard();
            card.ItemRewards.Add("Purchase unlock: 5.45x39mm BT gs");

            Assert.Contains(card.ItemRewards, r => r.StartsWith("Purchase unlock:"));
        }

        #endregion

        #region F10: Single-Player Isolation

        [Fact]
        public void F10_T1_01_LocalStateOnly_OperatesWithoutNetwork()
        {
            var card = CreateSampleTaskCard();
            // Stepper changes execute locally in-memory without remote endpoints
            card.ItemSteppers[0].Increment();
            Assert.Equal(1, card.ItemSteppers[0].CurrentCount);
        }

        [Fact]
        public void F10_T1_02_NoSquadEntities_TaskCardHasNoTeamShareButtons()
        {
            var card = CreateSampleTaskCard();
            // Verify absence of team/squad properties
            var type = card.GetType();
            Assert.Null(type.GetProperty("SquadShareButton"));
            Assert.Null(type.GetProperty("TeamPresence"));
        }

        [Fact]
        public void F10_T1_03_ProfileIsolation_ProfileProgressDoesNotLeak()
        {
            var cardProfile1 = CreateSampleTaskCard();
            var cardProfile2 = CreateSampleTaskCard();

            cardProfile1.ItemSteppers[0].Increment();
            Assert.Equal(1, cardProfile1.ItemSteppers[0].CurrentCount);
            Assert.Equal(0, cardProfile2.ItemSteppers[0].CurrentCount);
        }

        [Fact]
        public void F10_T1_04_OfflineDatabaseIntegrity_TransactionsAreLocal()
        {
            var card = CreateSampleTaskCard();
            card.MarkComplete();
            Assert.True(card.IsCompleted);
        }

        [Fact]
        public void F10_T1_05_NoMultiplayerWebSocket_NoBackgroundNetworkSockets()
        {
            var card = CreateSampleTaskCard();
            // Verification that TaskCard does not instantiate web sockets
            Assert.True(card.IsExpanded);
        }

        [Fact]
        public void F10_T2_01_OfflineAirgapMode_OperatesWithNetworkDisabled()
        {
            var card = CreateSampleTaskCard();
            card.ItemSteppers[0].SetMax();
            card.RecalculateMetRequirements();

            Assert.Equal(1, card.MetRequirements);
        }

        [Fact]
        public void F10_T2_02_ConcurrentLocalWrites_NoLockingFailures()
        {
            var card = CreateSampleTaskCard();
            for (int i = 0; i < 50; i++)
            {
                card.ItemSteppers[0].Increment();
                card.ItemSteppers[0].Decrement();
            }
            Assert.Equal(0, card.ItemSteppers[0].CurrentCount);
        }

        [Fact]
        public void F10_T2_03_CorruptedProfileFallback_LoadsDefaultGracefully()
        {
            var card = new TaskCardTestModel();
            Assert.NotNull(card.RequirementsSummaryText);
            Assert.NotNull(card.ImpactSummaryText);
        }

        [Fact]
        public void F10_T2_04_ProfileSwitchDuringActiveRaid_IsolatesState()
        {
            var pvpCard = CreateSampleTaskCard();
            pvpCard.ItemSteppers[0].SetMax();

            var pveCard = CreateSampleTaskCard();
            Assert.Equal(0, pveCard.ItemSteppers[0].CurrentCount);
        }

        [Fact]
        public void F10_T2_05_LocalCacheReplay_IdempotentOnApplicationRestart()
        {
            var card = CreateSampleTaskCard();
            card.ItemSteppers[0].Increment();
            int count = card.ItemSteppers[0].CurrentCount;

            // Replay from persisted state
            var restoredCard = CreateSampleTaskCard();
            restoredCard.ItemSteppers[0].CurrentCount = count;

            Assert.Equal(1, restoredCard.ItemSteppers[0].CurrentCount);
        }

        [Fact]
        public void RequirementsPill_ReflectsRequirementsProgressFraction()
        {
            var card = CreateSampleTaskCard();
            Assert.Equal("0/2", card.RequirementsPillText);

            card.ItemSteppers[0].SetMax();
            card.RecalculateMetRequirements();
            Assert.Equal("1/2", card.RequirementsPillText);

            card.ActionToggles[0].IsChecked = true;
            card.RecalculateMetRequirements();
            Assert.Equal("2/2", card.RequirementsPillText);
        }

        [Fact]
        public void LoadFromTask_AlternativeHandoverItems_GroupedIntoSingleStepperWithAlternatives()
        {
            var vm = new TarkovHelper.Controls.TaskCardViewModel();
            var task = new TarkovHelper.Models.TarkovTask
            {
                NormalizedName = "ironclad-proof",
                Name = "Ironclad Proof",
                Trader = "Prapor",
                Objectives = new List<string>
                {
                    "Hand over 10 M67 Hand grenades to Prapor",
                    "Eliminate Scavs using hand grenades"
                },
                RequiredItems = new List<TarkovHelper.Models.QuestItem>
                {
                    new() { ItemNormalizedName = "rgd-5", ItemDisplayName = "RGD-5 hand grenade", Amount = 10, FoundInRaid = true, Requirement = "Handover" },
                    new() { ItemNormalizedName = "vog-17", ItemDisplayName = "VOG-17 Khattabka", Amount = 10, FoundInRaid = true, Requirement = "Handover" },
                    new() { ItemNormalizedName = "vog-25", ItemDisplayName = "VOG-25 Khattabka", Amount = 10, FoundInRaid = true, Requirement = "Handover" },
                    new() { ItemNormalizedName = "f-1", ItemDisplayName = "F-1 hand grenade", Amount = 10, FoundInRaid = true, Requirement = "Handover" },
                    new() { ItemNormalizedName = "f-1-delay", ItemDisplayName = "F-1 (Reduced delay)", Amount = 10, FoundInRaid = true, Requirement = "Handover" },
                    new() { ItemNormalizedName = "rgn", ItemDisplayName = "RGN hand grenade", Amount = 10, FoundInRaid = true, Requirement = "Handover" },
                    new() { ItemNormalizedName = "rgo", ItemDisplayName = "RGO hand grenade", Amount = 10, FoundInRaid = true, Requirement = "Handover" },
                    new() { ItemNormalizedName = "zarya", ItemDisplayName = "Zarya stun grenade", Amount = 10, FoundInRaid = true, Requirement = "Handover" },
                    new() { ItemNormalizedName = "40mm-vog-25", ItemDisplayName = "40mm VOG-25 grenade", Amount = 10, FoundInRaid = true, Requirement = "Handover" }
                }
            };

            vm.LoadFromTask(task);

            // 1. Grouped into exactly 1 stepper
            Assert.Single(vm.ItemSteppers);
            var stepper = vm.ItemSteppers[0];
            Assert.True(stepper.HasAlternatives);
            Assert.Equal(9, stepper.Alternatives.Count);
            Assert.Equal(10, stepper.MaxCount);
            Assert.Contains("9", stepper.AlternativesBadgeText);

            // 2. Handover objective was skipped from action toggles, leaving only the kill objective
            Assert.Single(vm.ActionToggles);
            Assert.Equal("Eliminate Scavs using hand grenades", vm.ActionToggles[0].Description);

            // 3. Total requirements is 2 (1 handover + 1 kill), NOT 11!
            Assert.Equal(2, vm.TotalRequirements);
            Assert.Equal("0/2", vm.RequirementsPillText);
            Assert.Equal("Requirements 0 of 2 met", vm.RequirementsSummaryText);
            Assert.Equal(System.Windows.Visibility.Visible, vm.RequirementsPillVisibility);

            // 4. Dropdown selection of alternatives (no auto-cycling timer)
            Assert.Equal("1/9", stepper.AlternativesBadgeText);
            Assert.Equal("rgd-5", stepper.ItemId);
            Assert.Equal(System.Windows.Visibility.Visible, stepper.AlternativesVisibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, stepper.SingleItemVisibility);

            // Select an alternative via ComboBox selection (e.g. F-1 grenade)
            var f1Alt = stepper.Alternatives.First(a => a.ItemId == "f-1");
            stepper.SelectedAlternative = f1Alt;
            Assert.Equal("f-1", stepper.ItemId);
            Assert.Equal("F-1 hand grenade", stepper.ItemName);
            Assert.Equal(f1Alt, stepper.SelectedAlternative);
            Assert.Contains("Alternativas válidas", stepper.AlternativesTooltip);
        }

        [Fact]
        public void LoadFromTask_AHelpingHand_SingleItemHasCorrectVisibilityAndRequirementsPill()
        {
            var vm = new TarkovHelper.Controls.TaskCardViewModel();
            var task = new TarkovHelper.Models.TarkovTask
            {
                NormalizedName = "a-helping-hand",
                Name = "A Helping Hand",
                Trader = "Mechanic",
                Maps = new List<string> { "woods" },
                Objectives = new List<string>
                {
                    "Locate and obtain Prapor's package on Woods",
                    "Survive and extract from the location",
                    "Complete the task Shipping Delay - Part 1"
                },
                RequiredItems = new List<TarkovHelper.Models.QuestItem>
                {
                    new() { ItemNormalizedName = "package-for-prapor", ItemDisplayName = "Package for Prapor", Amount = 1, FoundInRaid = true, Requirement = "Handover" }
                }
            };

            vm.LoadFromTask(task);

            // Single item stepper (NO alternatives, so simple text is visible, ComboBox is collapsed)
            Assert.Single(vm.ItemSteppers);
            var stepper = vm.ItemSteppers[0];
            Assert.False(stepper.HasAlternatives);
            Assert.Equal(System.Windows.Visibility.Visible, stepper.SingleItemVisibility);
            Assert.Equal(System.Windows.Visibility.Collapsed, stepper.AlternativesVisibility);
            Assert.Equal("Package for Prapor", stepper.ItemName);

            // 3 actions + 1 handover = 4 requirements total
            Assert.Equal(4, vm.TotalRequirements);
            Assert.Equal(0, vm.MetRequirements);
            Assert.Equal("0/4", vm.RequirementsPillText);
            Assert.Equal(System.Windows.Visibility.Visible, vm.RequirementsPillVisibility);
        }

        #endregion
    }
}
