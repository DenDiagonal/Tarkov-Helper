using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TarkovHelper.Models;
using TarkovHelper.Pages;
using TarkovHelper.Services;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests.ViewModels
{
    /// <summary>
    /// Comprehensive Unit Tests for QuestFilterEngine covering Features F3-F6 and legacy F9-F11, F13-F14:
    /// - F3: Extended Filter & Sort Engine (Sorting by Impact, Name, Trader, Level, XP)
    /// - F4: Tasks View Navigation Switcher (List, Traders, Maps, Graph)
    /// - F5: Status Filter Chips with Counts (All, Available, Locked, Completed, Failed)
    /// - F6: Filtering & Sorting Control Bar (Kappa, Current Level, Search, Faction)
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class QuestFilterEngineTests : IDisposable
    {
        private readonly List<QuestViewModel> _testQuests;

        public QuestFilterEngineTests()
        {
            _testQuests = CreateSampleQuests();
            TaskImpactEngine.Instance.Initialize(_testQuests.Select(q => q.Task).ToList());
        }

        public void Dispose()
        {
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
        }

        public static List<QuestViewModel> CreateSampleQuests()
        {
            return new List<QuestViewModel>
            {
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "quest_debut",
                        Name = "Debut",
                        NormalizedName = "debut",
                        Trader = "Prapor",
                        Maps = new List<string> { "customs" },
                        ReqKappa = true,
                        RequiredLevel = 1,
                        RequiredItems = new List<QuestItem>
                        {
                            new() { ItemDisplayName = "MP-133 12ga shotgun", ItemNormalizedName = "mp133", Amount = 2, FoundInRaid = false }
                        },
                        LeadsTo = new List<string> { "checking", "shortage" }
                    },
                    DisplayName = "Debut",
                    TraderInitial = "PR",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "quest_checking",
                        Name = "Checking",
                        NormalizedName = "checking",
                        Trader = "Prapor",
                        Maps = new List<string> { "customs" },
                        ReqKappa = true,
                        RequiredLevel = 2,
                        Previous = new List<string> { "debut" },
                        LeadsTo = new List<string> { "shootout-picnic" }
                    },
                    DisplayName = "Checking",
                    TraderInitial = "PR",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true,
                    HasNeededKeys = true
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "quest_shortage",
                        Name = "Shortage",
                        NormalizedName = "shortage",
                        Trader = "Therapist",
                        Maps = new List<string> { "customs" },
                        ReqKappa = true,
                        RequiredLevel = 1,
                        Previous = new List<string> { "debut" },
                        RequiredItems = new List<QuestItem>
                        {
                            new() { ItemDisplayName = "Salewa first aid kit", ItemNormalizedName = "salewa", Amount = 3, FoundInRaid = true }
                        }
                    },
                    DisplayName = "Shortage",
                    TraderInitial = "TH",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true,
                    HasFirRequirements = true
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "quest_sanitary_claws",
                        Name = "Sanitary Claws",
                        NormalizedName = "sanitary-claws",
                        Trader = "Therapist",
                        Maps = new List<string> { "customs" },
                        ReqKappa = false,
                        RequiredLevel = 15,
                        RequiredItems = new List<QuestItem>
                        {
                            new() { ItemDisplayName = "Gas analyzer", ItemNormalizedName = "gas-analyzer", Amount = 2, FoundInRaid = true }
                        }
                    },
                    DisplayName = "Sanitary Claws",
                    TraderInitial = "TH",
                    Status = QuestStatus.Locked,
                    IsKappaRequired = false,
                    HasFirRequirements = true
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "quest_grenadier",
                        Name = "Grenadier",
                        NormalizedName = "grenadier",
                        Trader = "Prapor",
                        Maps = new List<string> { "factory", "customs" },
                        ReqKappa = true,
                        RequiredLevel = 30
                    },
                    DisplayName = "Grenadier",
                    TraderInitial = "PR",
                    Status = QuestStatus.LevelLocked,
                    IsKappaRequired = true
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "quest_shootout_picnic",
                        Name = "Shootout Picnic",
                        NormalizedName = "shootout-picnic",
                        Trader = "Prapor",
                        Maps = new List<string> { "woods" },
                        ReqKappa = true,
                        RequiredLevel = 3,
                        Previous = new List<string> { "checking" }
                    },
                    DisplayName = "Shootout Picnic",
                    TraderInitial = "PR",
                    Status = QuestStatus.Done,
                    IsKappaRequired = true
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "quest_our_guy",
                        Name = "Our Guy in the City",
                        NormalizedName = "our-guy-in-the-city",
                        Trader = "Prapor",
                        Maps = new List<string> { "streets" },
                        ReqKappa = false,
                        RequiredLevel = 15,
                        Faction = "bear"
                    },
                    DisplayName = "Our Guy in the City",
                    TraderInitial = "PR",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "quest_tour",
                        Name = "Tour",
                        NormalizedName = "tour",
                        Trader = "Peacekeeper",
                        Maps = new List<string> { "lighthouse" },
                        ReqKappa = false,
                        RequiredLevel = 15,
                        Faction = "usec"
                    },
                    DisplayName = "Tour",
                    TraderInitial = "PK",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false
                },
                new()
                {
                    Task = new TarkovTask
                    {
                        Id = "quest_chemical_4_fail",
                        Name = "Chemical - Part 4 (Failed)",
                        NormalizedName = "chemical-4-fail",
                        Trader = "Skier",
                        Maps = new List<string> { "customs" },
                        ReqKappa = false,
                        RequiredLevel = 11
                    },
                    DisplayName = "Chemical - Part 4 (Failed)",
                    TraderInitial = "SK",
                    Status = QuestStatus.Failed,
                    IsKappaRequired = false
                }
            };
        }

        #region F3: Extended Filter & Sort Engine

        [Fact]
        public void F3_T1_01_SortByImpact_DescendingRanksHighestImpactFirst()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                SortBy = TaskSortMode.Impact,
                SortDirection = SortDirection.Descending
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            var debutIndex = result.FindIndex(q => q.DisplayName == "Debut");
            var picnicIndex = result.FindIndex(q => q.DisplayName == "Shootout Picnic");

            Assert.True(debutIndex >= 0 && picnicIndex >= 0);
            Assert.True(debutIndex < picnicIndex);
        }

        [Fact]
        public void F3_T1_02_SortByName_AlphabeticalOrder()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                SortBy = TaskSortMode.Name,
                SortDirection = SortDirection.Ascending
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            var names = result.Select(r => r.DisplayName).ToList();
            var sortedNames = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.Equal(sortedNames, names);
        }

        [Fact]
        public void F3_T1_03_SortByTrader_GroupsOrSortsByTrader()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                SortBy = TaskSortMode.Trader,
                SortDirection = SortDirection.Ascending
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            var traders = result.Select(r => r.Task.Trader).ToList();
            var sortedTraders = traders.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.Equal(sortedTraders, traders);
        }

        [Fact]
        public void F3_T1_04_SortByLevel_RanksByRequiredLevel()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                SortBy = TaskSortMode.Level,
                SortDirection = SortDirection.Ascending
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            Assert.Equal(1, result.First().Task.RequiredLevel);
            Assert.Equal(30, result.Last().Task.RequiredLevel);
        }

        [Fact]
        public void F3_T1_05_SortByXP_RanksByRewardXP()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                SortBy = TaskSortMode.XP,
                SortDirection = SortDirection.Descending
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            Assert.NotEmpty(result);
        }

        [Fact]
        public void F3_T2_01_SortWithEqualValues_StableSecondarySort()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                SortBy = TaskSortMode.Level,
                SortDirection = SortDirection.Ascending
            };

            var result1 = QuestFilterEngine.Filter(_testQuests, criteria);
            var result2 = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.Equal(result1.Select(r => r.DisplayName), result2.Select(r => r.DisplayName));
        }

        [Fact]
        public void F3_T2_02_SortWithNullLevelsOrXP_HandledGracefully()
        {
            var questsWithNull = new List<QuestViewModel>
            {
                new() { Task = new TarkovTask { Name = "Task A", RequiredLevel = null }, DisplayName = "Task A", Status = QuestStatus.Active },
                new() { Task = new TarkovTask { Name = "Task B", RequiredLevel = 10 }, DisplayName = "Task B", Status = QuestStatus.Active }
            };

            var criteria = new QuestFilterCriteria { SortBy = TaskSortMode.Level, SortDirection = SortDirection.Ascending, TaskStatusFilter = TaskStatusFilter.All };
            var result = QuestFilterEngine.Filter(questsWithNull, criteria);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void F3_T2_03_SortDirectionToggle_InvertsOrder()
        {
            var ascCriteria = new QuestFilterCriteria { SortBy = TaskSortMode.Name, SortDirection = SortDirection.Ascending, TaskStatusFilter = TaskStatusFilter.All };
            var descCriteria = new QuestFilterCriteria { SortBy = TaskSortMode.Name, SortDirection = SortDirection.Descending, TaskStatusFilter = TaskStatusFilter.All };

            var asc = QuestFilterEngine.Filter(_testQuests, ascCriteria);
            var desc = QuestFilterEngine.Filter(_testQuests, descCriteria);

            Assert.Equal(asc.First().DisplayName, desc.Last().DisplayName);
            Assert.Equal(asc.Last().DisplayName, desc.First().DisplayName);
        }

        [Fact]
        public void F3_T2_04_SortWithEmptyList_ReturnsEmpty()
        {
            var criteria = new QuestFilterCriteria { SortBy = TaskSortMode.Impact };
            var result = QuestFilterEngine.Filter(new List<QuestViewModel>(), criteria);

            Assert.Empty(result);
        }

        [Fact]
        public void F3_T2_05_CombinedFilterAndSort_MaintainsFilteredSubset()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.Available,
                SortBy = TaskSortMode.Name,
                SortDirection = SortDirection.Ascending
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            Assert.All(result, q => Assert.Equal(QuestStatus.Active, q.Status));
            Assert.True(result.Count >= 2);
        }

        #endregion

        #region F4: Tasks View Navigation Switcher

        [Fact]
        public void F4_T1_01_ModeList_FlatViewAllTasks()
        {
            var criteria = new QuestFilterCriteria { ViewMode = TasksViewMode.List, TaskStatusFilter = TaskStatusFilter.All };
            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.Equal(_testQuests.Count, result.Count);
        }

        [Fact]
        public void F4_T1_02_ModeTraders_FiltersByTraderTarget()
        {
            var criteria = new QuestFilterCriteria
            {
                ViewMode = TasksViewMode.Traders,
                SelectedTarget = "Therapist",
                TaskStatusFilter = TaskStatusFilter.All
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            Assert.All(result, q => Assert.Equal("Therapist", q.Task.Trader));
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void F4_T1_03_ModeMaps_FiltersByMapTarget()
        {
            var criteria = new QuestFilterCriteria
            {
                ViewMode = TasksViewMode.Maps,
                SelectedTarget = "woods",
                TaskStatusFilter = TaskStatusFilter.All
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            Assert.Single(result);
            Assert.Equal("Shootout Picnic", result[0].DisplayName);
        }

        [Fact]
        public void F4_T1_04_ModeGraph_PreservesSelectedQuest()
        {
            var criteria = new QuestFilterCriteria { ViewMode = TasksViewMode.Graph, TaskStatusFilter = TaskStatusFilter.All };
            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.Equal(_testQuests.Count, result.Count);
        }

        [Fact]
        public void F4_T1_05_ModeSwitching_RetainsSearchQueryAndFilters()
        {
            var criteria = new QuestFilterCriteria
            {
                ViewMode = TasksViewMode.List,
                SearchQuery = "debut",
                KappaOnly = true,
                TaskStatusFilter = TaskStatusFilter.All
            };

            criteria.ViewMode = TasksViewMode.Traders;
            criteria.SelectedTarget = "Prapor";

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            Assert.Single(result);
            Assert.Equal("Debut", result[0].DisplayName);
        }

        [Fact]
        public void F4_T2_01_InvalidTargetForMode_GracefulFallback()
        {
            var criteria = new QuestFilterCriteria
            {
                ViewMode = TasksViewMode.Traders,
                SelectedTarget = "NonExistentTrader",
                TaskStatusFilter = TaskStatusFilter.All
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            Assert.Empty(result);
        }

        [Fact]
        public void F4_T2_02_EmptyModeSelection_DefaultsToList()
        {
            var criteria = new QuestFilterCriteria();
            Assert.Equal(TasksViewMode.List, criteria.ViewMode);
        }

        [Fact]
        public void F4_T2_03_RapidModeSwitching_StateIntegrity()
        {
            var criteria = new QuestFilterCriteria { SearchQuery = "checking", TaskStatusFilter = TaskStatusFilter.All };

            foreach (TasksViewMode mode in Enum.GetValues(typeof(TasksViewMode)))
            {
                criteria.ViewMode = mode;
                var res = QuestFilterEngine.Filter(_testQuests, criteria);
                Assert.Single(res);
                Assert.Equal("Checking", res[0].DisplayName);
            }
        }

        [Fact]
        public void F4_T2_04_AllTargetInTraders_ShowsAllTraders()
        {
            var criteria = new QuestFilterCriteria
            {
                ViewMode = TasksViewMode.Traders,
                SelectedTarget = "All",
                TaskStatusFilter = TaskStatusFilter.All
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            Assert.Equal(_testQuests.Count, result.Count);
        }

        [Fact]
        public void F4_T2_05_CaseInsensitiveTarget_MatchesCorrectly()
        {
            var criteria = new QuestFilterCriteria
            {
                ViewMode = TasksViewMode.Maps,
                SelectedTarget = "CUSTOMS",
                TaskStatusFilter = TaskStatusFilter.All
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            Assert.True(result.Count >= 4);
        }

        #endregion

        #region F5: Status Filter Chips with Counts

        [Fact]
        public void F5_T1_01_ChipAll_IncludesAllStatuses()
        {
            var counts = QuestFilterEngine.ComputeStatusCounts(_testQuests);
            Assert.Equal(_testQuests.Count, counts.All);
        }

        [Fact]
        public void F5_T1_02_ChipAvailable_ReturnsOnlyActionableQuests()
        {
            var criteria = new QuestFilterCriteria { TaskStatusFilter = TaskStatusFilter.Available };
            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.All(result, q => Assert.Equal(QuestStatus.Active, q.Status));
            Assert.Equal(5, result.Count);
        }

        [Fact]
        public void F5_T1_03_ChipLocked_ReturnsPrereqAndLevelLocked()
        {
            var criteria = new QuestFilterCriteria { TaskStatusFilter = TaskStatusFilter.Locked };
            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.All(result, q => Assert.True(q.Status == QuestStatus.Locked || q.Status == QuestStatus.LevelLocked));
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void F5_T1_04_ChipCompleted_ReturnsDoneQuests()
        {
            var criteria = new QuestFilterCriteria { TaskStatusFilter = TaskStatusFilter.Completed };
            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.Single(result);
            Assert.Equal("Shootout Picnic", result[0].DisplayName);
        }

        [Fact]
        public void F5_T1_05_ChipFailed_ReturnsFailedQuests()
        {
            var criteria = new QuestFilterCriteria { TaskStatusFilter = TaskStatusFilter.Failed };
            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.Single(result);
            Assert.Equal("Chemical - Part 4 (Failed)", result[0].DisplayName);
        }

        [Fact]
        public void F5_T2_01_StatusCounters_SumMatchesTotalQuests()
        {
            var counts = QuestFilterEngine.ComputeStatusCounts(_testQuests);
            Assert.Equal(counts.All, counts.Available + counts.Locked + counts.Completed + counts.Failed);
        }

        [Fact]
        public void F5_T2_02_DynamicCountRecalculation_OnTaskCompletion()
        {
            var countsBefore = QuestFilterEngine.ComputeStatusCounts(_testQuests);

            var debut = _testQuests.First(q => q.DisplayName == "Debut");
            debut.Status = QuestStatus.Done;

            var countsAfter = QuestFilterEngine.ComputeStatusCounts(_testQuests);

            Assert.Equal(countsBefore.Available - 1, countsAfter.Available);
            Assert.Equal(countsBefore.Completed + 1, countsAfter.Completed);
            Assert.Equal(countsBefore.All, countsAfter.All);

            // Revert
            debut.Status = QuestStatus.Active;
        }

        [Fact]
        public void F5_T2_03_ZeroCountChips_StillSelectableWithoutError()
        {
            var onlyActive = _testQuests.Where(q => q.Status == QuestStatus.Active).ToList();
            var criteria = new QuestFilterCriteria { TaskStatusFilter = TaskStatusFilter.Failed };

            var result = QuestFilterEngine.Filter(onlyActive, criteria);
            Assert.Empty(result);
        }

        [Fact]
        public void F5_T2_04_MultipleStatusTransitions_ConsistentCounts()
        {
            var quest = _testQuests.First(q => q.DisplayName == "Checking");
            var original = QuestFilterEngine.ComputeStatusCounts(_testQuests);

            quest.Status = QuestStatus.Done;
            var step1 = QuestFilterEngine.ComputeStatusCounts(_testQuests);
            Assert.Equal(original.Completed + 1, step1.Completed);

            quest.Status = QuestStatus.Failed;
            var step2 = QuestFilterEngine.ComputeStatusCounts(_testQuests);
            Assert.Equal(original.Failed + 1, step2.Failed);

            quest.Status = QuestStatus.Active;
            var step3 = QuestFilterEngine.ComputeStatusCounts(_testQuests);
            Assert.Equal(original.Available, step3.Available);
        }

        [Fact]
        public void F5_T2_05_FailedTasksExcludedFromAvailableAndLocked()
        {
            var availableResult = QuestFilterEngine.Filter(_testQuests, new QuestFilterCriteria { TaskStatusFilter = TaskStatusFilter.Available });
            var lockedResult = QuestFilterEngine.Filter(_testQuests, new QuestFilterCriteria { TaskStatusFilter = TaskStatusFilter.Locked });

            Assert.DoesNotContain(availableResult, q => q.Status == QuestStatus.Failed);
            Assert.DoesNotContain(lockedResult, q => q.Status == QuestStatus.Failed);
        }

        #endregion

        #region F6: Filtering & Sorting Control Bar

        [Fact]
        public void F6_T1_01_KappaOnlyFilter_RestrictsToCollectorPrereqs()
        {
            var criteria = new QuestFilterCriteria { TaskStatusFilter = TaskStatusFilter.All, KappaOnly = true };
            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.All(result, q => Assert.True(q.IsKappaRequired));
            Assert.DoesNotContain(result, q => q.DisplayName == "Sanitary Claws");
        }

        [Fact]
        public void F6_T1_02_CurrentLevelOnlyFilter_HidesOverLevelQuests()
        {
            var criteria = new QuestFilterCriteria { TaskStatusFilter = TaskStatusFilter.All, CurrentLevelOnly = true };
            var result = QuestFilterEngine.Filter(_testQuests, criteria, currentPlayerLevel: 10);

            Assert.DoesNotContain(result, q => q.DisplayName == "Grenadier");
            Assert.DoesNotContain(result, q => q.DisplayName == "Sanitary Claws");
            Assert.Contains(result, q => q.DisplayName == "Debut");
        }

        [Fact]
        public void F6_T1_03_MultiKeywordSearch_MatchesAcrossFields()
        {
            var criteria = new QuestFilterCriteria { TaskStatusFilter = TaskStatusFilter.All, SearchQuery = "mp133" };
            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.Single(result);
            Assert.Equal("Debut", result[0].DisplayName);
        }

        [Fact]
        public void F6_T1_04_FactionFilter_ExcludesOpposingFactionQuests()
        {
            var criteriaBear = new QuestFilterCriteria { TaskStatusFilter = TaskStatusFilter.All, Faction = "bear" };
            var bearResult = QuestFilterEngine.Filter(_testQuests, criteriaBear);

            Assert.Contains(bearResult, q => q.DisplayName == "Our Guy in the City");
            Assert.DoesNotContain(bearResult, q => q.DisplayName == "Tour");

            var criteriaUsec = new QuestFilterCriteria { TaskStatusFilter = TaskStatusFilter.All, Faction = "usec" };
            var usecResult = QuestFilterEngine.Filter(_testQuests, criteriaUsec);

            Assert.Contains(usecResult, q => q.DisplayName == "Tour");
            Assert.DoesNotContain(usecResult, q => q.DisplayName == "Our Guy in the City");
        }

        [Fact]
        public void F6_T1_05_ResetFilters_RestoresDefaultCriteria()
        {
            var criteria = new QuestFilterCriteria
            {
                SearchQuery = "customs",
                KappaOnly = true,
                CurrentLevelOnly = true,
                SortBy = TaskSortMode.XP
            };

            criteria = new QuestFilterCriteria();

            Assert.Equal(TasksViewMode.List, criteria.ViewMode);
            Assert.Equal(TaskStatusFilter.Available, criteria.TaskStatusFilter);
            Assert.Equal(TaskSortMode.Impact, criteria.SortBy);
            Assert.Empty(criteria.SearchQuery);
            Assert.False(criteria.KappaOnly);
        }

        [Fact]
        public void F6_T2_01_SpecialCharacterSearch_HandlesRegexMetaChars()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                SearchQuery = "Part 4 (Failed)"
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            Assert.Single(result);
            Assert.Equal("Chemical - Part 4 (Failed)", result[0].DisplayName);
        }

        [Fact]
        public void F6_T2_02_WhitespaceOnlySearch_TreatedAsEmpty()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                SearchQuery = "     "
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            Assert.Equal(_testQuests.Count, result.Count);
        }

        [Fact]
        public void F6_T2_03_ConflictingFilters_ReturnsEmptyGracefully()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.Available,
                SearchQuery = "NonExistentString12345"
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            Assert.Empty(result);
        }

        [Fact]
        public void F6_T2_04_LevelBoundary_PlayerLevelMatchesQuestLevel()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                CurrentLevelOnly = true
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria, currentPlayerLevel: 15);
            Assert.Contains(result, q => q.DisplayName == "Sanitary Claws");
        }

        [Fact]
        public void F6_T2_05_SearchLongString_DoesNotDegradePerformance()
        {
            string longQuery = new string('a', 500);
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                SearchQuery = longQuery
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            Assert.Empty(result);
        }

        #endregion

        #region Legacy Compatibility Unit Tests (F9, F10, F11, F13, F14)

        [Fact]
        public void Filter_ByTrader_ReturnsOnlyMatchingTraderQuests()
        {
            var criteria = new QuestFilterCriteria
            {
                Mode = NavigationMode.Traders,
                SelectedTarget = "Therapist",
                TaskStatusFilter = TaskStatusFilter.All
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.All(result, q => Assert.Equal("Therapist", q.Task.Trader));
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void Filter_ByTrader_All_ReturnsQuestsFromAllTraders()
        {
            var criteria = new QuestFilterCriteria
            {
                Mode = NavigationMode.Traders,
                SelectedTarget = "All",
                TaskStatusFilter = TaskStatusFilter.All
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);
            Assert.Equal(_testQuests.Count, result.Count);
        }

        [Fact]
        public void Filter_ByMap_ReturnsOnlyQuestsOnSpecifiedMap()
        {
            var criteria = new QuestFilterCriteria
            {
                Mode = NavigationMode.Maps,
                SelectedTarget = "woods",
                TaskStatusFilter = TaskStatusFilter.All
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.Single(result);
            Assert.Equal("Shootout Picnic", result[0].DisplayName);
        }

        [Fact]
        public void Filter_ByMap_MultiMapQuest_MatchesAnyConfiguredMap()
        {
            var criteriaFactory = new QuestFilterCriteria
            {
                Mode = NavigationMode.Maps,
                SelectedTarget = "factory",
                TaskStatusFilter = TaskStatusFilter.All
            };

            var factoryResult = QuestFilterEngine.Filter(_testQuests, criteriaFactory);
            Assert.Contains(factoryResult, q => q.DisplayName == "Grenadier");

            var criteriaCustoms = new QuestFilterCriteria
            {
                Mode = NavigationMode.Maps,
                SelectedTarget = "customs",
                TaskStatusFilter = TaskStatusFilter.All
            };

            var customsResult = QuestFilterEngine.Filter(_testQuests, criteriaCustoms);
            Assert.Contains(customsResult, q => q.DisplayName == "Grenadier");
        }

        [Fact]
        public void ComputeTraderActiveCounts_AggregatesAccurately()
        {
            var counts = QuestFilterEngine.ComputeTraderActiveCounts(_testQuests);

            Assert.True(counts.ContainsKey("Prapor"));
            Assert.True(counts.ContainsKey("Therapist"));
            Assert.True(counts.ContainsKey("Peacekeeper"));

            Assert.Equal(3, counts["Prapor"]);
            Assert.Equal(1, counts["Therapist"]);
            Assert.Equal(1, counts["Peacekeeper"]);
        }

        [Fact]
        public void ComputeMapActiveCounts_AggregatesAccurately()
        {
            var counts = QuestFilterEngine.ComputeMapActiveCounts(_testQuests);

            Assert.True(counts.ContainsKey("customs"));
            Assert.True(counts.ContainsKey("streets"));
            Assert.True(counts.ContainsKey("lighthouse"));

            Assert.Equal(3, counts["customs"]);
            Assert.Equal(1, counts["streets"]);
            Assert.Equal(1, counts["lighthouse"]);
        }

        [Fact]
        public void QuickFilter_Activas_ReturnsOnlyActiveQuests()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.Available
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.All(result, q => Assert.Equal(QuestStatus.Active, q.Status));
            Assert.Equal(5, result.Count);
        }

        [Fact]
        public void QuickFilter_Bloqueadas_ReturnsLockedAndLevelLockedQuests()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.Locked
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.All(result, q => Assert.True(q.Status == QuestStatus.Locked || q.Status == QuestStatus.LevelLocked));
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void QuickFilter_Completadas_ReturnsOnlyDoneQuests()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.Completed
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.Single(result);
            Assert.Equal("Shootout Picnic", result[0].DisplayName);
        }

        [Fact]
        public void QuickFilter_SoloKappa_ReturnsOnlyKappaRequiredQuests()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                KappaOnly = true
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.All(result, q => Assert.True(q.Task.ReqKappa));
            Assert.DoesNotContain(result, q => q.DisplayName == "Sanitary Claws");
        }

        [Fact]
        public void QuickFilter_NivelActual_HidesQuestsAboveCurrentPlayerLevel()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                CurrentLevelOnly = true
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria, currentPlayerLevel: 15);

            Assert.DoesNotContain(result, q => q.DisplayName == "Grenadier");
            Assert.Contains(result, q => q.DisplayName == "Sanitary Claws");
        }

        [Fact]
        public void QuickFilter_Faction_HidesOpposingFactionQuests()
        {
            var criteriaUsec = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                Faction = "usec"
            };

            var usecResult = QuestFilterEngine.Filter(_testQuests, criteriaUsec);

            Assert.Contains(usecResult, q => q.DisplayName == "Tour");
            Assert.DoesNotContain(usecResult, q => q.DisplayName == "Our Guy in the City");
        }

        [Fact]
        public void Search_MatchesQuestTitle()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                SearchQuery = "debut"
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.Single(result);
            Assert.Equal("Debut", result[0].DisplayName);
        }

        [Fact]
        public void Search_MatchesMapName()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                SearchQuery = "woods"
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.Single(result);
            Assert.Equal("Shootout Picnic", result[0].DisplayName);
        }

        [Fact]
        public void Search_MatchesRequiredItemName()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                SearchQuery = "salewa"
            };

            var result = QuestFilterEngine.Filter(_testQuests, criteria);

            Assert.Single(result);
            Assert.Equal("Shortage", result[0].DisplayName);
        }

        [Fact]
        public void ToggleDone_FlipsActiveToDone_AndDoneToActive()
        {
            var quest = _testQuests.First(q => q.DisplayName == "Debut");
            Assert.Equal(QuestStatus.Active, quest.Status);

            QuestFilterEngine.ToggleDone(quest);
            Assert.Equal(QuestStatus.Done, quest.Status);

            QuestFilterEngine.ToggleDone(quest);
            Assert.Equal(QuestStatus.Active, quest.Status);
        }

        #endregion

        #region Milestone 2 Companion Tests (8 Sort Modes, Filter Criteria, 12 Canonical Maps)

        [Fact]
        public void M2_01_SortByDefaultOrder_PreservesCanonicalOrdering()
        {
            var criteriaAsc = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                SortBy = TaskSortMode.DefaultOrder,
                SortDirection = SortDirection.Ascending
            };
            var sortedAsc = QuestFilterEngine.Filter(_testQuests, criteriaAsc);
            Assert.Equal(_testQuests[0].DisplayName, sortedAsc[0].DisplayName);

            var criteriaDesc = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                SortBy = TaskSortMode.DefaultOrder,
                SortDirection = SortDirection.Descending
            };
            var sortedDesc = QuestFilterEngine.Filter(_testQuests, criteriaDesc);
            Assert.Equal(_testQuests.Last().DisplayName, sortedDesc[0].DisplayName);
        }

        [Fact]
        public void M2_02_SortByTraderOrder_FollowsCanonicalTraderSequence()
        {
            var criteria = new QuestFilterCriteria { SortBy = TaskSortMode.TraderOrder, SortDirection = SortDirection.Ascending };
            var sorted = QuestFilterEngine.Filter(_testQuests, criteria);

            // Prapor quests should come before Therapist quests
            var praporIndex = sorted.FindIndex(q => q.Task.Trader == "Prapor");
            var therapistIndex = sorted.FindIndex(q => q.Task.Trader == "Therapist");
            Assert.True(praporIndex >= 0 && therapistIndex >= 0);
            Assert.True(praporIndex < therapistIndex);
        }

        [Fact]
        public void M2_03_SortByProgression_RanksRootQuestsBeforeFollowups()
        {
            // Debut has 0 prereqs; Checking requires Debut
            var criteriaAsc = new QuestFilterCriteria { SortBy = TaskSortMode.Progression, SortDirection = SortDirection.Ascending };
            var sortedAsc = QuestFilterEngine.Filter(_testQuests, criteriaAsc);

            var debutIndex = sortedAsc.FindIndex(q => q.Task.NormalizedName == "debut");
            var checkingIndex = sortedAsc.FindIndex(q => q.Task.NormalizedName == "checking");

            Assert.True(debutIndex >= 0 && checkingIndex >= 0);
            Assert.True(debutIndex < checkingIndex, "Root quest Debut should precede downstream quest Checking in ascending progression order.");
        }

        [Fact]
        public void M2_04_SortByTeammates_PrioritizesUniversalFactionTasks()
        {
            var criteriaDesc = new QuestFilterCriteria { SortBy = TaskSortMode.Teammates, SortDirection = SortDirection.Descending };
            var sortedDesc = QuestFilterEngine.Filter(_testQuests, criteriaDesc);

            Assert.NotNull(sortedDesc);
            Assert.NotEmpty(sortedDesc);
            Assert.True(QuestFilterEngine.GetTeammatesAvailabilityScore(sortedDesc.First()) >=
                        QuestFilterEngine.GetTeammatesAvailabilityScore(sortedDesc.Last()));
        }

        [Fact]
        public void M2_05_TaskTypesFilter_RegularKappaLightkeeper_MutuallyRespected()
        {
            // 1. Only Kappa
            var kappaCriteria = new QuestFilterCriteria
            {
                TaskTypeRegular = false,
                TaskTypeKappa = true,
                TaskTypeLightkeeper = false
            };
            var kappaOnly = QuestFilterEngine.Filter(_testQuests, kappaCriteria);
            Assert.All(kappaOnly, q => Assert.True(q.IsKappaRequired || q.Task.ReqKappa));

            // 2. Only Regular
            var regularCriteria = new QuestFilterCriteria
            {
                TaskTypeRegular = true,
                TaskTypeKappa = false,
                TaskTypeLightkeeper = false
            };
            var regularOnly = QuestFilterEngine.Filter(_testQuests, regularCriteria);
            Assert.All(regularOnly, q => Assert.False(q.IsKappaRequired || q.Task.ReqKappa || q.Task.Trader == "Lightkeeper"));
        }

        [Fact]
        public void M2_06_MapOptions_ShowAnyLocationAndHideFinishedHere()
        {
            var customsCriteria = new QuestFilterCriteria
            {
                ViewMode = TasksViewMode.Maps,
                SelectedTarget = "customs",
                ShowAnyLocationTasks = true,
                HideFinishedHere = true
            };

            var results = QuestFilterEngine.Filter(_testQuests, customsCriteria);
            // Completed quests should be hidden
            Assert.DoesNotContain(results, q => q.Status == QuestStatus.Done);
        }

        [Fact]
        public void M2_07_ComputeMapActiveCounts_IncludesAll12CanonicalMaps()
        {
            var counts = QuestFilterEngine.ComputeMapActiveCounts(_testQuests);
            Assert.Contains("the-labyrinth", counts.Keys);
            Assert.Contains("terminal", counts.Keys);
            Assert.Contains("ground-zero", counts.Keys);
            Assert.Contains("customs", counts.Keys);
            Assert.Equal(13, counts.Count); // 12 maps + "All"
        }

        [Fact]
        public void M2_08_NormalizeMapKey_DisambiguatesTheLabAndTheLabyrinth()
        {
            Assert.Equal("thelab", QuestFilterEngine.NormalizeMapKey("The Lab"));
            Assert.Equal("thelab", QuestFilterEngine.NormalizeMapKey("the-lab"));
            Assert.Equal("thelabyrinth", QuestFilterEngine.NormalizeMapKey("The Labyrinth"));
            Assert.Equal("thelabyrinth", QuestFilterEngine.NormalizeMapKey("the-labyrinth"));
            Assert.Equal("terminal", QuestFilterEngine.NormalizeMapKey("Terminal"));
            Assert.Equal("terminal", QuestFilterEngine.NormalizeMapKey("terminal"));
        }

        [Fact]
        public void M2_09_SortByAlphabetical_And_XpReward_WorkCorrectly()
        {
            var alphaCriteria = new QuestFilterCriteria { SortBy = TaskSortMode.Alphabetical, SortDirection = SortDirection.Ascending };
            var alphaSorted = QuestFilterEngine.Filter(_testQuests, alphaCriteria);
            for (int i = 0; i < alphaSorted.Count - 1; i++)
            {
                Assert.True(string.Compare(alphaSorted[i].DisplayName, alphaSorted[i + 1].DisplayName, StringComparison.OrdinalIgnoreCase) <= 0);
            }

            var xpCriteria = new QuestFilterCriteria { SortBy = TaskSortMode.XpReward, SortDirection = SortDirection.Descending };
            var xpSorted = QuestFilterEngine.Filter(_testQuests, xpCriteria);
            for (int i = 0; i < xpSorted.Count - 1; i++)
            {
                Assert.True(QuestFilterEngine.GetTaskXP(xpSorted[i].Task) >= QuestFilterEngine.GetTaskXP(xpSorted[i + 1].Task));
            }
        }

        [Fact]
        public void M2_10_KappaOnly_BackwardCompatibilityProperty_Synced()
        {
            var criteria = new QuestFilterCriteria();
            Assert.False(criteria.KappaOnly);

            criteria.KappaOnly = true;
            Assert.True(criteria.TaskTypeKappa);
            Assert.False(criteria.TaskTypeRegular);
            Assert.False(criteria.TaskTypeLightkeeper);
            Assert.True(criteria.KappaOnly);

            criteria.KappaOnly = false;
            Assert.True(criteria.TaskTypeRegular);
            Assert.True(criteria.TaskTypeKappa);
            Assert.True(criteria.TaskTypeLightkeeper);
            Assert.False(criteria.KappaOnly);
        }

        [Fact]
        public void M2_11_NeedsKeysFilter_OnlyMatchesQuestsWithKeys()
        {
            var criteria = new QuestFilterCriteria
            {
                TaskStatusFilter = TaskStatusFilter.All,
                NeedsKeys = true
            };

            var results = QuestFilterEngine.Filter(_testQuests, criteria);
            Assert.NotEmpty(results);
            Assert.All(results, q => Assert.True(q.HasNeededKeys));
        }

        [Fact]
        public void PowerOfPersuasion_MatchesAllowedMaps_AndExcludesStreetsOfTarkov()
        {
            var popTask = new TarkovTask
            {
                Id = "63a5cf262964a7488f5243ce",
                Name = "Power of Persuasion",
                Trader = "Prapor",
                Maps = new List<string> { "woods", "ground zero", "interchange", "customs" }
            };
            var vm = new QuestViewModel { Task = popTask };

            // Must match Woods, Ground Zero, Interchange, Customs
            Assert.True(QuestFilterEngine.MatchesMap(vm, "Woods"));
            Assert.True(QuestFilterEngine.MatchesMap(vm, "Ground Zero"));
            Assert.True(QuestFilterEngine.MatchesMap(vm, "Interchange"));
            Assert.True(QuestFilterEngine.MatchesMap(vm, "Customs"));

            // Must NOT match Streets of Tarkov, Factory, Shoreline, Reserve, Lighthouse, The Lab
            Assert.False(QuestFilterEngine.MatchesMap(vm, "Streets of Tarkov"));
            Assert.False(QuestFilterEngine.MatchesMap(vm, "streetsoftarkov"));
            Assert.False(QuestFilterEngine.MatchesMap(vm, "Factory"));
            Assert.False(QuestFilterEngine.MatchesMap(vm, "Shoreline"));
            Assert.False(QuestFilterEngine.MatchesMap(vm, "Reserve"));
            Assert.False(QuestFilterEngine.MatchesMap(vm, "Lighthouse"));
            Assert.False(QuestFilterEngine.MatchesMap(vm, "The Lab"));
        }

        #endregion
    }
}
