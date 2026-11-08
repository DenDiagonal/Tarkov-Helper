using System;
using System.Collections.Generic;
using System.Linq;
using TarkovHelper.Models;
using TarkovHelper.Pages;
using TarkovHelper.Services;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests.ViewModels
{
    /// <summary>
    /// Empirical Challenger M2-1 Adversarial Stress Test Suite.
    /// Stress-tests edge cases, permutations, boundary conditions, and canonical disambiguation
    /// for Milestone 2 (Tasks Header, 4 View Modes, Filter Bar & 12 Map Chips).
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class ChallengerM2_1AdversarialTests : IDisposable
    {
        private readonly List<QuestViewModel> _sampleQuests;

        public ChallengerM2_1AdversarialTests()
        {
            _sampleQuests = CreateAdversarialQuests();
            TaskImpactEngine.Instance.Initialize(_sampleQuests.Select(q => q.Task).ToList());
        }

        public void Dispose()
        {
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
        }

        private static List<QuestViewModel> CreateAdversarialQuests()
        {
            return new List<QuestViewModel>
            {
                // 0: Prapor Debut (Root, Level 1, Regular+Kappa, Customs)
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_debut",
                        Name = "Debut",
                        NormalizedName = "debut",
                        Trader = "Prapor",
                        Maps = new List<string> { "customs" },
                        ReqKappa = true,
                        RequiredLevel = 1,
                        LeadsTo = new List<string> { "checking" }
                    },
                    DisplayName = "Debut",
                    TraderInitial = "PR",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true
                },
                // 1: Prapor Checking (Followup to Debut, Level 2, Needs Keys, Regular+Kappa, Customs)
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_checking",
                        Name = "Checking",
                        NormalizedName = "checking",
                        Trader = "Prapor",
                        Maps = new List<string> { "customs" },
                        ReqKappa = true,
                        RequiredLevel = 2,
                        Previous = new List<string> { "debut" }
                    },
                    DisplayName = "Checking",
                    TraderInitial = "PR",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true,
                    HasNeededKeys = true
                },
                // 2: Therapist Shortage (Level 1, Regular+Kappa, Customs, FIR items)
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_shortage",
                        Name = "Shortage",
                        NormalizedName = "shortage",
                        Trader = "Therapist",
                        Maps = new List<string> { "customs" },
                        ReqKappa = true,
                        RequiredLevel = 1,
                        Previous = new List<string> { "debut" }
                    },
                    DisplayName = "Shortage",
                    TraderInitial = "TH",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true,
                    HasFirRequirements = true
                },
                // 3: Fence Collector (Kappa Collector endgame quest, Level 45, Any Map)
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_collector",
                        Name = "Collector",
                        NormalizedName = "collector",
                        Trader = "Fence",
                        Maps = new List<string> { "any" },
                        ReqKappa = false,
                        RequiredLevel = 45
                    },
                    DisplayName = "Collector",
                    TraderInitial = "FE",
                    Status = QuestStatus.Locked,
                    IsKappaRequired = false
                },
                // 4: Skier What's on the Flash Drive? (Level 8, Regular+Kappa, Customs)
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_flashdrive",
                        Name = "What's on the Flash Drive?",
                        NormalizedName = "whats-on-the-flash-drive",
                        Trader = "Skier",
                        Maps = new List<string> { "customs" },
                        ReqKappa = true,
                        RequiredLevel = 8
                    },
                    DisplayName = "What's on the Flash Drive?",
                    TraderInitial = "SK",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true
                },
                // 5: Peacekeeper Fishing Gear (Level 12, Regular+Kappa, Shoreline, USEC-favored)
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_fishing_gear",
                        Name = "Fishing Gear",
                        NormalizedName = "fishing-gear",
                        Trader = "Peacekeeper",
                        Maps = new List<string> { "shoreline" },
                        ReqKappa = true,
                        RequiredLevel = 12
                    },
                    DisplayName = "Fishing Gear",
                    TraderInitial = "PK",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true
                },
                // 6: Mechanic Gunsmith Pt 1 (Level 2, Regular+Kappa, Factory)
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_gunsmith_1",
                        Name = "Gunsmith - Part 1",
                        NormalizedName = "gunsmith-part-1",
                        Trader = "Mechanic",
                        Maps = new List<string> { "factory" },
                        ReqKappa = true,
                        RequiredLevel = 2
                    },
                    DisplayName = "Gunsmith - Part 1",
                    TraderInitial = "ME",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true
                },
                // 7: Ragman Only Business (Level 15, Regular, Interchange)
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_only_business",
                        Name = "Only Business",
                        NormalizedName = "only-business",
                        Trader = "Ragman",
                        Maps = new List<string> { "interchange" },
                        ReqKappa = false,
                        RequiredLevel = 15
                    },
                    DisplayName = "Only Business",
                    TraderInitial = "RA",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                // 8: Jaeger Survivalist Path (Level 2, Regular+Kappa, Woods)
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_survivalist",
                        Name = "Survivalist Path - Unprotected",
                        NormalizedName = "the-survivalist-path-unprotected-but-dangerous",
                        Trader = "Jaeger",
                        Maps = new List<string> { "woods" },
                        ReqKappa = true,
                        RequiredLevel = 2
                    },
                    DisplayName = "Survivalist Path - Unprotected",
                    TraderInitial = "JG",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true
                },
                // 9: Ref Easy Money (Level 10, Regular Arena quest, Ground Zero)
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_ref_easy_money",
                        Name = "Easy Money - Part 1",
                        NormalizedName = "easy-money-part-1",
                        Trader = "Ref",
                        Maps = new List<string> { "ground-zero" },
                        ReqKappa = false,
                        RequiredLevel = 10
                    },
                    DisplayName = "Easy Money - Part 1",
                    TraderInitial = "RF",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                // 10: Lightkeeper Getting Acquainted (Endgame, Lightkeeper, Lighthouse)
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_getting_acquainted",
                        Name = "Getting Acquainted",
                        NormalizedName = "getting-acquainted",
                        Trader = "Lightkeeper",
                        Maps = new List<string> { "lighthouse" },
                        ReqKappa = false,
                        RequiredLevel = 35
                    },
                    DisplayName = "Getting Acquainted",
                    TraderInitial = "LK",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                // 11: BTR Driver Delivery (BTR Trader, Streets of Tarkov)
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_btr_delivery",
                        Name = "BTR Express Delivery",
                        NormalizedName = "btr-express-delivery",
                        Trader = "BTR Driver",
                        Maps = new List<string> { "streets" },
                        ReqKappa = false,
                        RequiredLevel = 20
                    },
                    DisplayName = "BTR Express Delivery",
                    TraderInitial = "BTR",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                // 12: The Lab quest (TerraGroup Employee, The Lab)
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_lab_terragroup",
                        Name = "TerraGroup Employee",
                        NormalizedName = "terragroup-employee",
                        Trader = "Therapist",
                        Maps = new List<string> { "the-lab" },
                        ReqKappa = true,
                        RequiredLevel = 35
                    },
                    DisplayName = "TerraGroup Employee",
                    TraderInitial = "TH",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true
                },
                // 13: The Labyrinth quest (Labyrinth Explorer, The Labyrinth)
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_labyrinth_explorer",
                        Name = "Labyrinth Explorer",
                        NormalizedName = "labyrinth-explorer",
                        Trader = "Peacekeeper",
                        Maps = new List<string> { "the-labyrinth" },
                        ReqKappa = false,
                        RequiredLevel = 40
                    },
                    DisplayName = "Labyrinth Explorer",
                    TraderInitial = "PK",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                // 14: Terminal quest (Terminal Recon, Terminal)
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_terminal_recon",
                        Name = "Terminal Reconnaissance",
                        NormalizedName = "terminal-reconnaissance",
                        Trader = "Prapor",
                        Maps = new List<string> { "terminal" },
                        ReqKappa = false,
                        RequiredLevel = 50
                    },
                    DisplayName = "Terminal Reconnaissance",
                    TraderInitial = "PR",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                // 15: Global / Any Location Completed Quest
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_global_done",
                        Name = "Global Finished Task",
                        NormalizedName = "global-finished-task",
                        Trader = "Prapor",
                        Maps = new List<string> { "any" },
                        ReqKappa = false,
                        RequiredLevel = 5
                    },
                    DisplayName = "Global Finished Task",
                    TraderInitial = "PR",
                    Status = QuestStatus.Done,
                    IsKappaRequired = false
                },
                // 16: Customs Completed Quest
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_customs_done",
                        Name = "Customs Completed Task",
                        NormalizedName = "customs-completed-task",
                        Trader = "Prapor",
                        Maps = new List<string> { "customs" },
                        ReqKappa = true,
                        RequiredLevel = 1
                    },
                    DisplayName = "Customs Completed Task",
                    TraderInitial = "PR",
                    Status = QuestStatus.Done,
                    IsKappaRequired = true
                },
                // 17: BEAR Exclusive Quest
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_bear_exclusive",
                        Name = "Our Guy in the North",
                        NormalizedName = "our-guy-in-the-north",
                        Trader = "Prapor",
                        Faction = "bear",
                        Maps = new List<string> { "reserve" },
                        ReqKappa = false,
                        RequiredLevel = 15
                    },
                    DisplayName = "Our Guy in the North",
                    TraderInitial = "PR",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                // 18: USEC Exclusive Quest
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_usec_exclusive",
                        Name = "Special Orders",
                        NormalizedName = "special-orders",
                        Trader = "Peacekeeper",
                        Faction = "usec",
                        Maps = new List<string> { "lighthouse" },
                        ReqKappa = false,
                        RequiredLevel = 15
                    },
                    DisplayName = "Special Orders",
                    TraderInitial = "PK",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                // 19: Failed Quest
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "q_failed_task",
                        Name = "An Out of Date Task",
                        NormalizedName = "an-out-of-date-task",
                        Trader = "Skier",
                        Maps = new List<string> { "customs" },
                        ReqKappa = false,
                        RequiredLevel = 10
                    },
                    DisplayName = "An Out of Date Task",
                    TraderInitial = "SK",
                    Status = QuestStatus.Failed,
                    IsKappaRequired = false
                }
            };
        }

        #region 1. All 8 Sort Modes Empirical Stress Tests

        [Theory]
        [InlineData(TaskSortMode.DefaultOrder)]
        [InlineData(TaskSortMode.Impact)]
        [InlineData(TaskSortMode.Alphabetical)]
        [InlineData(TaskSortMode.PlayerLevel)]
        [InlineData(TaskSortMode.TraderOrder)]
        [InlineData(TaskSortMode.Trader)]
        [InlineData(TaskSortMode.XpReward)]
        [InlineData(TaskSortMode.Progression)]
        [InlineData(TaskSortMode.Teammates)]
        public void Challenge_SortQuests_All8Modes_ExecuteWithoutException_AscendingAndDescending(TaskSortMode mode)
        {
            var sortedAsc = QuestFilterEngine.SortQuests(_sampleQuests, mode, SortDirection.Ascending);
            Assert.NotNull(sortedAsc);
            Assert.Equal(_sampleQuests.Count, sortedAsc.Count);

            var sortedDesc = QuestFilterEngine.SortQuests(_sampleQuests, mode, SortDirection.Descending);
            Assert.NotNull(sortedDesc);
            Assert.Equal(_sampleQuests.Count, sortedDesc.Count);
        }

        [Fact]
        public void Challenge_SortQuests_DefaultOrder_StrictPreservationAndInversion()
        {
            var asc = QuestFilterEngine.SortQuests(_sampleQuests, TaskSortMode.DefaultOrder, SortDirection.Ascending);
            for (int i = 0; i < _sampleQuests.Count; i++)
            {
                Assert.Same(_sampleQuests[i], asc[i]);
            }

            var desc = QuestFilterEngine.SortQuests(_sampleQuests, TaskSortMode.DefaultOrder, SortDirection.Descending);
            for (int i = 0; i < _sampleQuests.Count; i++)
            {
                Assert.Same(_sampleQuests[_sampleQuests.Count - 1 - i], desc[i]);
            }
        }

        [Fact]
        public void Challenge_SortQuests_TraderOrder_StrictCanonicalSequence()
        {
            var asc = QuestFilterEngine.SortQuests(_sampleQuests, TaskSortMode.TraderOrder, SortDirection.Ascending);

            // Canonical sequence: Prapor(1), Therapist(2), Fence(3), Skier(4), Peacekeeper(5), Mechanic(6), Ragman(7), Jaeger(8), Ref(9), Lightkeeper(10), BTR(11)
            int prevRank = -1;
            foreach (var q in asc)
            {
                int currentRank = QuestFilterEngine.GetTraderRank(q.Task?.Trader);
                Assert.True(currentRank >= prevRank,
                    $"Trader {q.Task?.Trader} (rank {currentRank}) appeared after rank {prevRank}");
                prevRank = currentRank;
            }

            var desc = QuestFilterEngine.SortQuests(_sampleQuests, TaskSortMode.TraderOrder, SortDirection.Descending);
            int prevDescRank = 999;
            foreach (var q in desc)
            {
                int currentRank = QuestFilterEngine.GetTraderRank(q.Task?.Trader);
                Assert.True(currentRank <= prevDescRank,
                    $"Trader {q.Task?.Trader} (rank {currentRank}) appeared after rank {prevDescRank} in descending sort");
                prevDescRank = currentRank;
            }
        }

        [Fact]
        public void Challenge_SortQuests_Trader_LegacyAlphabeticalSorting()
        {
            var asc = QuestFilterEngine.SortQuests(_sampleQuests, TaskSortMode.Trader, SortDirection.Ascending);
            for (int i = 0; i < asc.Count - 1; i++)
            {
                string traderA = asc[i].Task?.Trader ?? string.Empty;
                string traderB = asc[i + 1].Task?.Trader ?? string.Empty;
                Assert.True(string.Compare(traderA, traderB, StringComparison.OrdinalIgnoreCase) <= 0,
                    $"Alphabetical Trader sort failed: {traderA} precedes {traderB}");
            }
        }

        [Fact]
        public void Challenge_SortQuests_PlayerLevel_AscendingAndDescending()
        {
            var asc = QuestFilterEngine.SortQuests(_sampleQuests, TaskSortMode.PlayerLevel, SortDirection.Ascending);
            for (int i = 0; i < asc.Count - 1; i++)
            {
                int lvlA = asc[i].Task?.RequiredLevel ?? 0;
                int lvlB = asc[i + 1].Task?.RequiredLevel ?? 0;
                Assert.True(lvlA <= lvlB, $"Level asc failed: {lvlA} > {lvlB}");
            }

            var desc = QuestFilterEngine.SortQuests(_sampleQuests, TaskSortMode.PlayerLevel, SortDirection.Descending);
            for (int i = 0; i < desc.Count - 1; i++)
            {
                int lvlA = desc[i].Task?.RequiredLevel ?? 0;
                int lvlB = desc[i + 1].Task?.RequiredLevel ?? 0;
                Assert.True(lvlA >= lvlB, $"Level desc failed: {lvlA} < {lvlB}");
            }
        }

        [Fact]
        public void Challenge_SortQuests_Alphabetical_AscendingAndDescending()
        {
            var asc = QuestFilterEngine.SortQuests(_sampleQuests, TaskSortMode.Alphabetical, SortDirection.Ascending);
            for (int i = 0; i < asc.Count - 1; i++)
            {
                string nameA = asc[i].DisplayName ?? string.Empty;
                string nameB = asc[i + 1].DisplayName ?? string.Empty;
                Assert.True(string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase) <= 0);
            }

            var desc = QuestFilterEngine.SortQuests(_sampleQuests, TaskSortMode.Alphabetical, SortDirection.Descending);
            for (int i = 0; i < desc.Count - 1; i++)
            {
                string nameA = desc[i].DisplayName ?? string.Empty;
                string nameB = desc[i + 1].DisplayName ?? string.Empty;
                Assert.True(string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        [Fact]
        public void Challenge_SortQuests_Teammates_UniversalFactionPrioritizedOverFactionExclusive()
        {
            var desc = QuestFilterEngine.SortQuests(_sampleQuests, TaskSortMode.Teammates, SortDirection.Descending);

            var universalTask = desc.First(q => string.IsNullOrEmpty(q.Task?.Faction));
            var bearTask = desc.First(q => q.Task?.Faction == "bear");

            int universalScore = QuestFilterEngine.GetTeammatesAvailabilityScore(universalTask);
            int bearScore = QuestFilterEngine.GetTeammatesAvailabilityScore(bearTask);

            Assert.True(universalScore > bearScore,
                $"Universal faction score {universalScore} must exceed faction exclusive score {bearScore}");
        }

        [Fact]
        public void Challenge_SortQuests_EmptyAndSingleItemList_Resilience()
        {
            var emptyList = new List<QuestViewModel>();
            foreach (TaskSortMode mode in Enum.GetValues<TaskSortMode>())
            {
                var resAsc = QuestFilterEngine.SortQuests(emptyList, mode, SortDirection.Ascending);
                Assert.Empty(resAsc);
                var resDesc = QuestFilterEngine.SortQuests(emptyList, mode, SortDirection.Descending);
                Assert.Empty(resDesc);
            }

            var singleList = new List<QuestViewModel> { _sampleQuests[0] };
            foreach (TaskSortMode mode in Enum.GetValues<TaskSortMode>())
            {
                var resAsc = QuestFilterEngine.SortQuests(singleList, mode, SortDirection.Ascending);
                Assert.Single(resAsc);
                var resDesc = QuestFilterEngine.SortQuests(singleList, mode, SortDirection.Descending);
                Assert.Single(resDesc);
            }
        }

        #endregion

        #region 2. Filter Criteria Permutations Empirical Stress Tests

        [Fact]
        public void Challenge_FilterCriteria_TaskType_8Permutations_StrictAdherence()
        {
            // Permutation 1: Regular=T, Kappa=F, Lightkeeper=F
            var crit1 = new QuestFilterCriteria { TaskTypeRegular = true, TaskTypeKappa = false, TaskTypeLightkeeper = false };
            var res1 = QuestFilterEngine.Filter(_sampleQuests, crit1);
            Assert.All(res1, q =>
            {
                bool isLk = string.Equals(q.Task?.Trader, "Lightkeeper", StringComparison.OrdinalIgnoreCase);
                bool isKap = q.IsKappaRequired || (q.Task?.ReqKappa ?? false);
                Assert.False(isLk || isKap, $"Found non-regular quest: {q.DisplayName}");
            });

            // Permutation 2: Regular=F, Kappa=T, Lightkeeper=F
            var crit2 = new QuestFilterCriteria { TaskTypeRegular = false, TaskTypeKappa = true, TaskTypeLightkeeper = false };
            var res2 = QuestFilterEngine.Filter(_sampleQuests, crit2);
            Assert.All(res2, q =>
            {
                bool isKap = q.IsKappaRequired || (q.Task?.ReqKappa ?? false);
                Assert.True(isKap, $"Found non-Kappa quest: {q.DisplayName}");
            });

            // Permutation 3: Regular=F, Kappa=F, Lightkeeper=T
            var crit3 = new QuestFilterCriteria { TaskTypeRegular = false, TaskTypeKappa = false, TaskTypeLightkeeper = true };
            var res3 = QuestFilterEngine.Filter(_sampleQuests, crit3);
            Assert.All(res3, q =>
            {
                bool isLk = string.Equals(q.Task?.Trader, "Lightkeeper", StringComparison.OrdinalIgnoreCase);
                Assert.True(isLk, $"Found non-Lightkeeper quest: {q.DisplayName}");
            });

            // Permutation 4: Regular=T, Kappa=T, Lightkeeper=F
            var crit4 = new QuestFilterCriteria { TaskTypeRegular = true, TaskTypeKappa = true, TaskTypeLightkeeper = false };
            var res4 = QuestFilterEngine.Filter(_sampleQuests, crit4);
            Assert.DoesNotContain(res4, q => string.Equals(q.Task?.Trader, "Lightkeeper", StringComparison.OrdinalIgnoreCase));

            // Permutation 5: Regular=T, Kappa=F, Lightkeeper=T
            var crit5 = new QuestFilterCriteria { TaskTypeRegular = true, TaskTypeKappa = false, TaskTypeLightkeeper = true };
            var res5 = QuestFilterEngine.Filter(_sampleQuests, crit5);
            Assert.DoesNotContain(res5, q => q.IsKappaRequired || (q.Task?.ReqKappa ?? false));

            // Permutation 6: Regular=F, Kappa=T, Lightkeeper=T
            var crit6 = new QuestFilterCriteria { TaskTypeRegular = false, TaskTypeKappa = true, TaskTypeLightkeeper = true };
            var res6 = QuestFilterEngine.Filter(_sampleQuests, crit6);
            Assert.All(res6, q =>
            {
                bool isLk = string.Equals(q.Task?.Trader, "Lightkeeper", StringComparison.OrdinalIgnoreCase);
                bool isKap = q.IsKappaRequired || (q.Task?.ReqKappa ?? false);
                Assert.True(isLk || isKap);
            });

            // Permutation 7: Regular=T, Kappa=T, Lightkeeper=T
            var crit7 = new QuestFilterCriteria { TaskTypeRegular = true, TaskTypeKappa = true, TaskTypeLightkeeper = true };
            var res7 = QuestFilterEngine.Filter(_sampleQuests, crit7);
            Assert.NotEmpty(res7);

            // Permutation 8: Regular=F, Kappa=F, Lightkeeper=F (Empty result)
            var crit8 = new QuestFilterCriteria { TaskTypeRegular = false, TaskTypeKappa = false, TaskTypeLightkeeper = false };
            var res8 = QuestFilterEngine.Filter(_sampleQuests, crit8);
            Assert.Empty(res8);
        }

        [Fact]
        public void Challenge_FilterCriteria_NeedsKeys_MatchesOnlyKeyRequiringQuests()
        {
            var crit = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                NeedsKeys = true
            };
            var res = QuestFilterEngine.Filter(_sampleQuests, crit);
            Assert.NotEmpty(res);
            Assert.All(res, q => Assert.True(q.HasNeededKeys));
            Assert.Contains(res, q => q.DisplayName == "Checking");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(15)]
        [InlineData(35)]
        public void Challenge_FilterCriteria_CurrentLevelOnly_FiltersHigherLevelQuests(int playerLevel)
        {
            var crit = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                CurrentLevelOnly = true
            };
            var res = QuestFilterEngine.Filter(_sampleQuests, crit, currentPlayerLevel: playerLevel);
            Assert.All(res, q =>
            {
                if (q.Task?.RequiredLevel.HasValue == true)
                {
                    Assert.True(q.Task.RequiredLevel.Value <= playerLevel,
                        $"Task {q.DisplayName} requiring level {q.Task.RequiredLevel.Value} exceeded player level {playerLevel}");
                }
            });
        }

        [Fact]
        public void Challenge_FilterCriteria_ShowAnyLocationTasks_IncludesGlobalQuestsOnMap()
        {
            var customsWithAny = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                ViewMode = TasksViewMode.Maps,
                SelectedTarget = "customs",
                ShowAnyLocationTasks = true
            };
            var resWithAny = QuestFilterEngine.Filter(_sampleQuests, customsWithAny);
            Assert.Contains(resWithAny, q => q.DisplayName == "Collector"); // Maps = ["any"]

            var customsWithoutAny = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                ViewMode = TasksViewMode.Maps,
                SelectedTarget = "customs",
                ShowAnyLocationTasks = false
            };
            var resWithoutAny = QuestFilterEngine.Filter(_sampleQuests, customsWithoutAny);
            Assert.DoesNotContain(resWithoutAny, q => q.DisplayName == "Collector");
        }

        [Fact]
        public void Challenge_FilterCriteria_HideFinishedHere_ExcludesCompletedTasksOnMap()
        {
            var customsKeepFinished = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                ViewMode = TasksViewMode.Maps,
                SelectedTarget = "customs",
                HideFinishedHere = false
            };
            var resKeep = QuestFilterEngine.Filter(_sampleQuests, customsKeepFinished);
            Assert.Contains(resKeep, q => q.DisplayName == "Customs Completed Task");

            var customsHideFinished = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                ViewMode = TasksViewMode.Maps,
                SelectedTarget = "customs",
                HideFinishedHere = true
            };
            var resHide = QuestFilterEngine.Filter(_sampleQuests, customsHideFinished);
            Assert.DoesNotContain(resHide, q => q.DisplayName == "Customs Completed Task");
        }

        #endregion

        #region 3. Canonical Map Disambiguation Empirical Tests

        [Theory]
        [InlineData("the-lab", "thelab")]
        [InlineData("thelab", "thelab")]
        [InlineData("The Lab", "thelab")]
        [InlineData("Labs", "thelab")]
        [InlineData("Laboratory", "thelab")]
        [InlineData("the-labyrinth", "thelabyrinth")]
        [InlineData("thelabyrinth", "thelabyrinth")]
        [InlineData("The Labyrinth", "thelabyrinth")]
        [InlineData("Labyrinth", "thelabyrinth")]
        [InlineData("terminal", "terminal")]
        [InlineData("Terminal", "terminal")]
        [InlineData("ground-zero", "groundzero")]
        [InlineData("Ground Zero", "groundzero")]
        [InlineData("sandbox", "groundzero")]
        [InlineData("customs", "customs")]
        [InlineData("bigmap", "customs")]
        [InlineData("streets", "streets")]
        [InlineData("Streets of Tarkov", "streets")]
        [InlineData("reserve", "reserve")]
        [InlineData("rezerv", "reserve")]
        public void Challenge_NormalizeMapKey_StrictDisambiguation(string input, string expected)
        {
            Assert.Equal(expected, QuestFilterEngine.NormalizeMapKey(input));
        }

        [Fact]
        public void Challenge_MatchesMap_StrictIsolation_TheLab_Vs_TheLabyrinth()
        {
            var labQuest = _sampleQuests.First(q => q.DisplayName == "TerraGroup Employee");
            var labyrinthQuest = _sampleQuests.First(q => q.DisplayName == "Labyrinth Explorer");
            var terminalQuest = _sampleQuests.First(q => q.DisplayName == "Terminal Reconnaissance");

            // Lab matches
            Assert.True(QuestFilterEngine.MatchesMap(labQuest, "the-lab"));
            Assert.True(QuestFilterEngine.MatchesMap(labQuest, "The Lab"));
            Assert.True(QuestFilterEngine.MatchesMap(labQuest, "Labs"));
            // Lab MUST NOT match Labyrinth or Terminal
            Assert.False(QuestFilterEngine.MatchesMap(labQuest, "the-labyrinth"));
            Assert.False(QuestFilterEngine.MatchesMap(labQuest, "The Labyrinth"));
            Assert.False(QuestFilterEngine.MatchesMap(labQuest, "terminal"));

            // Labyrinth matches
            Assert.True(QuestFilterEngine.MatchesMap(labyrinthQuest, "the-labyrinth"));
            Assert.True(QuestFilterEngine.MatchesMap(labyrinthQuest, "The Labyrinth"));
            Assert.True(QuestFilterEngine.MatchesMap(labyrinthQuest, "Labyrinth"));
            // Labyrinth MUST NOT match Lab or Terminal
            Assert.False(QuestFilterEngine.MatchesMap(labyrinthQuest, "the-lab"));
            Assert.False(QuestFilterEngine.MatchesMap(labyrinthQuest, "The Lab"));
            Assert.False(QuestFilterEngine.MatchesMap(labyrinthQuest, "terminal"));

            // Terminal matches
            Assert.True(QuestFilterEngine.MatchesMap(terminalQuest, "terminal"));
            Assert.True(QuestFilterEngine.MatchesMap(terminalQuest, "Terminal"));
            // Terminal MUST NOT match Lab or Labyrinth
            Assert.False(QuestFilterEngine.MatchesMap(terminalQuest, "the-lab"));
            Assert.False(QuestFilterEngine.MatchesMap(terminalQuest, "the-labyrinth"));
        }

        [Fact]
        public void Challenge_GetCanonicalMapDisplayName_All12CanonicalMaps()
        {
            var expectedNames = new Dictionary<string, string>
            {
                ["ground-zero"] = "Ground Zero",
                ["customs"] = "Customs",
                ["woods"] = "Woods",
                ["factory"] = "Factory",
                ["shoreline"] = "Shoreline",
                ["interchange"] = "Interchange",
                ["reserve"] = "Reserve",
                ["lighthouse"] = "Lighthouse",
                ["streets"] = "Streets of Tarkov",
                ["the-lab"] = "The Lab",
                ["the-labyrinth"] = "The Labyrinth",
                ["terminal"] = "Terminal"
            };

            foreach (var kvp in expectedNames)
            {
                Assert.Equal(kvp.Value, QuestFilterEngine.GetCanonicalMapDisplayName(kvp.Key));
            }
        }

        #endregion

        #region 4. Dynamic Map Active Count & Status Count Robustness Stress Tests

        [Fact]
        public void Challenge_ComputeMapActiveCounts_ResilienceWithNullAndEmptyInput()
        {
            var countsNull = QuestFilterEngine.ComputeMapActiveCounts(null);
            Assert.NotNull(countsNull);
            Assert.Equal(13, countsNull.Count); // 12 maps + All
            Assert.All(countsNull.Values, c => Assert.Equal(0, c));

            var countsEmpty = QuestFilterEngine.ComputeMapActiveCounts(new List<QuestViewModel>());
            Assert.NotNull(countsEmpty);
            Assert.Equal(13, countsEmpty.Count);
            Assert.All(countsEmpty.Values, c => Assert.Equal(0, c));
        }

        [Fact]
        public void Challenge_ComputeMapActiveCounts_AccurateCounts_And_LabVsLabyrinthSeparation()
        {
            var counts = QuestFilterEngine.ComputeMapActiveCounts(_sampleQuests);

            Assert.Equal(1, counts["the-lab"]);
            Assert.Equal(1, counts["the-labyrinth"]);
            Assert.Equal(1, counts["terminal"]);
            Assert.Equal(1, counts["ground-zero"]);
            Assert.Equal(1, counts["woods"]);
            Assert.Equal(1, counts["factory"]);
            Assert.Equal(1, counts["shoreline"]);
            Assert.Equal(1, counts["interchange"]);
            Assert.Equal(1, counts["reserve"]);
            Assert.Equal(2, counts["lighthouse"]); // Fishing Gear + Lightkeeper Getting Acquainted (+ USEC exclusive)
            Assert.True(counts["customs"] >= 3);   // Debut, Checking, Shortage, Flash Drive
        }

        [Fact]
        public void Challenge_ComputeMapActiveCounts_FactionFilter_Enforced()
        {
            var countsBear = QuestFilterEngine.ComputeMapActiveCounts(_sampleQuests, "bear");
            var countsUsec = QuestFilterEngine.ComputeMapActiveCounts(_sampleQuests, "usec");

            // BEAR exclusive quest is on Reserve
            Assert.Equal(1, countsBear["reserve"]);
            Assert.Equal(0, countsUsec["reserve"]); // USEC cannot take BEAR-exclusive quest
        }

        [Fact]
        public void Challenge_ComputeStatusCounts_AccuracyAcrossAll5Statuses()
        {
            var summary = QuestFilterEngine.ComputeStatusCounts(_sampleQuests);

            // Total quests in sample
            Assert.Equal(_sampleQuests.Count, summary.All);
            // Available (Active)
            Assert.Equal(_sampleQuests.Count(q => q.Status == QuestStatus.Active), summary.Available);
            // Locked (Locked + LevelLocked)
            Assert.Equal(_sampleQuests.Count(q => q.Status == QuestStatus.Locked || q.Status == QuestStatus.LevelLocked), summary.Locked);
            // Completed (Done)
            Assert.Equal(_sampleQuests.Count(q => q.Status == QuestStatus.Done), summary.Completed);
            // Failed
            Assert.Equal(_sampleQuests.Count(q => q.Status == QuestStatus.Failed), summary.Failed);
        }

        [Fact]
        public void Challenge_ComputeStatusCounts_WithBaseCriteria_Faceting()
        {
            var praporCriteria = new QuestFilterCriteria
            {
                SelectedTrader = "Prapor"
            };

            var summaryPrapor = QuestFilterEngine.ComputeStatusCounts(_sampleQuests, praporCriteria);
            Assert.True(summaryPrapor.All > 0);
            Assert.True(summaryPrapor.Available > 0);
            Assert.Equal(2, summaryPrapor.Completed); // q_global_done and q_customs_done
        }

        #endregion

        #region 5. Edge Case Stress Tests (Adversarial Inputs)

        [Fact]
        public void Challenge_Matches_WithNullQuestOrNullTask_ReturnsFalseSafely()
        {
            var criteria = new QuestFilterCriteria();
            Assert.False(QuestFilterEngine.Matches(null!, criteria));

            var questWithNullTask = new QuestViewModel { Task = null! };
            Assert.False(QuestFilterEngine.Matches(questWithNullTask, criteria));
        }

        [Fact]
        public void Challenge_MatchesSearchQuery_StressNullProperties_ReturnsFalseSafely()
        {
            var vm = new QuestViewModel
            {
                Task = new TarkovTask
                {
                    Ids = null,
                    Name = string.Empty,
                    NormalizedName = string.Empty,
                    Maps = null,
                    RequiredItems = null
                },
                DisplayName = null
            };

            // Query that doesn't match
            Assert.False(QuestFilterEngine.MatchesSearchQuery(vm, "nonexistent"));

            // Empty query returns true
            Assert.True(QuestFilterEngine.MatchesSearchQuery(vm, ""));
            Assert.True(QuestFilterEngine.MatchesSearchQuery(vm, "   "));
        }

        [Fact]
        public void Challenge_Finding_NullItemInIdsOrMaps_EmpiricalDemonstration()
        {
            // Empirical reproduction of edge case in MatchesSearchQuery:
            // When Ids contains a null element, id.Contains(q) without null check throws NullReferenceException.
            var vmWithNullId = new QuestViewModel
            {
                Task = new TarkovTask
                {
                    Ids = new List<string> { null! }
                }
            };
            Assert.Throws<NullReferenceException>(() => QuestFilterEngine.MatchesSearchQuery(vmWithNullId, "test"));

            // Empirical reproduction of edge case in ComputeMapActiveCounts:
            // When Maps contains a null element, m.Replace("-", "") throws NullReferenceException.
            var vmWithNullMap = new QuestViewModel
            {
                Status = QuestStatus.Active,
                Task = new TarkovTask
                {
                    Maps = new List<string> { null! }
                }
            };
            Assert.Throws<NullReferenceException>(() => QuestFilterEngine.ComputeMapActiveCounts(new[] { vmWithNullMap }));
        }

        [Fact]
        public void Challenge_TaskSortMode_And_TaskStatusFilter_EnumBidirectionalMappings()
        {
            var criteria = new QuestFilterCriteria();

            // TaskStatusFilter <-> QuickFilterStatus
            criteria.TaskStatusFilter = TaskStatusFilter.Available;
            Assert.Equal(QuickFilterStatus.Active, criteria.StatusFilter);

            criteria.TaskStatusFilter = TaskStatusFilter.Locked;
            Assert.Equal(QuickFilterStatus.Locked, criteria.StatusFilter);

            criteria.TaskStatusFilter = TaskStatusFilter.Completed;
            Assert.Equal(QuickFilterStatus.Done, criteria.StatusFilter);

            criteria.TaskStatusFilter = TaskStatusFilter.Failed;
            Assert.Equal(QuickFilterStatus.Failed, criteria.StatusFilter);

            criteria.TaskStatusFilter = TaskStatusFilter.All;
            Assert.Equal(QuickFilterStatus.All, criteria.StatusFilter);

            // Backward compatibility aliases
            Assert.Equal(TaskSortMode.Alphabetical, TaskSortMode.Name);
            Assert.Equal(TaskSortMode.PlayerLevel, TaskSortMode.Level);
            Assert.Equal(TaskSortMode.XpReward, TaskSortMode.XP);
        }

        #endregion
    }
}
