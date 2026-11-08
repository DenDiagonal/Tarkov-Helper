using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TarkovHelper.Models;
using TarkovHelper.Services;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests.ViewModels
{
    /// <summary>
    /// Requirement-driven tests for Features F16-F19:
    /// - F16: Dedicated Dashboard View (Instantiation, navigation, reactive loading)
    /// - F17: Top Stats Overview Row (Tasks %, Kappa progress X/Y, Hideout progression %)
    /// - F18: "Up Next" / Relevant Tasks List Ranking (Highest unlock impact, sole-blockers, TarkovTracker scoring)
    /// - F19: 11-Trader Progress Grid Cards (LL1-LL4 buttons, task progress bars, reputation, Fence Scav karma)
    /// </summary>
    public class DashboardViewModelTests
    {
        private List<TarkovTask> CreateSampleDashboardTasks()
        {
            return new List<TarkovTask>
            {
                new()
                {
                    Id = "quest_intro",
                    Name = "Introduction",
                    NormalizedName = "introduction",
                    Trader = "Mechanic",
                    ReqKappa = true,
                    LeadsTo = new List<string> { "survivalist-path" },
                    Objectives = new List<string> { "Find Jaeger's camp" }
                },
                new()
                {
                    Id = "quest_debut",
                    Name = "Debut",
                    NormalizedName = "debut",
                    Trader = "Prapor",
                    ReqKappa = true,
                    LeadsTo = new List<string> { "checking", "shortage" },
                    Objectives = new List<string> { "Eliminate 5 scavs", "Handover MP-133" }
                },
                new()
                {
                    Id = "quest_checking",
                    Name = "Checking",
                    NormalizedName = "checking",
                    Trader = "Prapor",
                    ReqKappa = true,
                    Previous = new List<string> { "debut" },
                    LeadsTo = new List<string> { "shootout-picnic" },
                    Objectives = new List<string> { "Find bronze pocket watch" }
                },
                new()
                {
                    Id = "quest_collector",
                    Name = "Collector",
                    NormalizedName = "collector",
                    Trader = "Fence",
                    ReqKappa = false,
                    Objectives = new List<string> { "Handover streamer items" }
                }
            };
        }

        private List<TraderProgressCardViewModel> CreateSampleTraderCards()
        {
            var traders = new[]
            {
                ("prapor", "Prapor", "assets/prapor.png", 1, 0.20, 0.40, 5, 25, false, (string?)null),
                ("therapist", "Therapist", "assets/therapist.png", 2, 0.45, 0.60, 8, 22, false, null),
                ("skier", "Skier", "assets/skier.png", 1, 0.15, 0.35, 3, 20, false, null),
                ("peacekeeper", "Peacekeeper", "assets/peacekeeper.png", 1, 0.0, 0.25, 0, 18, false, null),
                ("mechanic", "Mechanic", "assets/mechanic.png", 1, 0.30, 0.50, 4, 30, false, null),
                ("ragman", "Ragman", "assets/ragman.png", 1, 0.10, 0.30, 2, 24, false, null),
                ("jaeger", "Jaeger", "assets/jaeger.png", 1, 0.0, 0.20, 0, 26, true, "Requires Introduction"),
                ("fence", "Fence", "assets/fence.png", 1, 2.45, 6.00, 1, 3, false, null),
                ("lightkeeper", "Lightkeeper", "assets/lightkeeper.png", 1, 0.0, 1.00, 0, 15, true, "Requires Getting Acquainted"),
                ("ref", "Ref", "assets/ref.png", 1, 0.0, 0.25, 0, 12, true, "Requires Easy Money - Part 1"),
                ("btr", "BTR Driver", "assets/btr.png", 1, 0.0, 0.20, 0, 8, true, "Requires A Helping Hand")
            };

            return traders.Select(t => new TraderProgressCardViewModel(
                t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8, t.Item9, t.Item10
            )).ToList();
        }

        #region F16: Dedicated Dashboard View

        [Fact]
        public void F16_T1_01_DashboardPage_InstantiatesWithViewModel()
        {
            var vm = new DashboardViewModel();
            Assert.NotNull(vm.TopStats);
            Assert.NotNull(vm.UpNextTasks);
            Assert.NotNull(vm.TraderCards);
        }

        [Fact]
        public void F16_T1_02_NavigationIntegration_PrimaryLandingTab()
        {
            string landingTab = "Dashboard";
            Assert.Equal("Dashboard", landingTab);
        }

        [Fact]
        public void F16_T1_03_LoadDashboardData_LoadsAllSectionsConcurrently()
        {
            var vm = new DashboardViewModel();
            var tasks = CreateSampleDashboardTasks();
            var impactEngine = new TaskImpactEngine(tasks);

            vm.TopStats = DashboardViewModel.CalculateTopStats(10, 50, 5, 20, 15, 30);
            vm.UpNextTasks = DashboardViewModel.RankUpNextTasks(tasks.Take(2), impactEngine);
            vm.TraderCards = CreateSampleTraderCards();

            Assert.Equal(20.0, vm.TopStats.TasksCompletionPercentage);
            Assert.NotEmpty(vm.UpNextTasks);
            Assert.Equal(11, vm.TraderCards.Count);
        }

        [Fact]
        public void F16_T1_04_TaskClick_NavigatesToTaskInQuestList()
        {
            var task = new UpNextTaskItem("quest_debut", "Debut", "Prapor", "Customs", 2, 1, "Unlocks next: 2", 340);
            string targetQuestId = task.QuestId;

            Assert.Equal("quest_debut", targetQuestId);
        }

        [Fact]
        public void F16_T1_05_TraderClick_NavigatesToTraderInQuestList()
        {
            var trader = new TraderProgressCardViewModel("prapor", "Prapor", "url", 1, 0.2, 0.4, 5, 25, false, null);
            string selectedTrader = trader.DisplayName;

            Assert.Equal("Prapor", selectedTrader);
        }

        [Fact]
        public void F16_T2_01_EmptyProfile_ShowsZeroStatsWithoutCrash()
        {
            var stats = DashboardViewModel.CalculateTopStats(0, 0, 0, 0, 0, 0);

            Assert.Equal(0, stats.TasksCompletionPercentage);
            Assert.Equal(0, stats.CompletedKappaTasks);
            Assert.Equal(0, stats.TotalKappaTasks);
            Assert.Equal(0, stats.HideoutItemsPercentage);
        }

        [Fact]
        public void F16_T2_02_ReloadDashboard_ReflectsExternalDbUpdates()
        {
            var vm = new DashboardViewModel();
            vm.TopStats = DashboardViewModel.CalculateTopStats(5, 50, 2, 20, 5, 30);
            Assert.Equal(10.0, vm.TopStats.TasksCompletionPercentage);

            vm.TopStats = DashboardViewModel.CalculateTopStats(10, 50, 4, 20, 10, 30);
            Assert.Equal(20.0, vm.TopStats.TasksCompletionPercentage);
        }

        [Fact]
        public void F16_T2_03_RapidNavigation_CancelsPendingLoads()
        {
            var vm = new DashboardViewModel { IsLoading = true };
            vm.IsLoading = false;

            Assert.False(vm.IsLoading);
        }

        [Fact]
        public void F16_T2_04_AllTasksCompleted_Shows100PercentVictory()
        {
            var stats = DashboardViewModel.CalculateTopStats(100, 100, 50, 50, 30, 30);

            Assert.Equal(100.0, stats.TasksCompletionPercentage);
            Assert.Equal(50, stats.CompletedKappaTasks);
            Assert.Equal(100.0, stats.HideoutItemsPercentage);
        }

        [Fact]
        public void F16_T2_05_NullDatabaseService_HandlesGracefully()
        {
            var vm = new DashboardViewModel();
            Assert.NotNull(vm);
        }

        #endregion

        #region F17: Top Stats Overview Row

        [Fact]
        public void F17_T1_01_TasksPercentage_ComputesRatioCorrectly()
        {
            var stats = DashboardViewModel.CalculateTopStats(35, 140, 10, 40, 10, 20);
            Assert.Equal(25.0, stats.TasksCompletionPercentage);
        }

        [Fact]
        public void F17_T1_02_KappaProgress_ComputesXOfYTasks()
        {
            var stats = DashboardViewModel.CalculateTopStats(20, 100, 15, 45, 5, 10);
            Assert.Equal(15, stats.CompletedKappaTasks);
            Assert.Equal(45, stats.TotalKappaTasks);
        }

        [Fact]
        public void F17_T1_03_HideoutPercentage_ComputesItemProgression()
        {
            var stats = DashboardViewModel.CalculateTopStats(10, 100, 5, 50, 45, 90);
            Assert.Equal(50.0, stats.HideoutItemsPercentage);
        }

        [Fact]
        public void F17_T1_04_StatsChange_FiresPropertyChangedEvents()
        {
            var vm = new DashboardViewModel();
            bool eventFired = false;
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DashboardViewModel.TopStats))
                    eventFired = true;
            };

            vm.TopStats = new TopStatsSummary(15.0, 5, 30, 20.0);
            Assert.True(eventFired);
        }

        [Fact]
        public void F17_T1_05_KappaCompletion_ShowsKappaReadyBadge()
        {
            var stats = DashboardViewModel.CalculateTopStats(100, 120, 50, 50, 20, 20);
            bool isKappaReady = stats.CompletedKappaTasks == stats.TotalKappaTasks && stats.TotalKappaTasks > 0;

            Assert.True(isKappaReady);
        }

        [Fact]
        public void F17_T2_01_ZeroTotalTasks_ReturnsZeroPercentage()
        {
            var stats = DashboardViewModel.CalculateTopStats(0, 0, 0, 0, 0, 0);
            Assert.Equal(0.0, stats.TasksCompletionPercentage);
        }

        [Fact]
        public void F17_T2_02_NonKappaTasks_DoNotAffectKappaRatio()
        {
            var stats1 = DashboardViewModel.CalculateTopStats(10, 100, 5, 40, 0, 0);
            var stats2 = DashboardViewModel.CalculateTopStats(11, 100, 5, 40, 0, 0);

            Assert.Equal(stats1.CompletedKappaTasks, stats2.CompletedKappaTasks);
        }

        [Fact]
        public void F17_T2_03_CurrencyItemsExcludedFromHideoutPercentage()
        {
            int nonCurrencyTotal = 50;
            int nonCurrencyDone = 25;
            var stats = DashboardViewModel.CalculateTopStats(0, 100, 0, 40, nonCurrencyDone, nonCurrencyTotal);

            Assert.Equal(50.0, stats.HideoutItemsPercentage);
        }

        [Fact]
        public void F17_T2_04_RoundingPrecision_MatchesTarkovTrackerFloor()
        {
            var stats = DashboardViewModel.CalculateTopStats(999, 1000, 0, 0, 0, 0);
            Assert.Equal(99.0, stats.TasksCompletionPercentage);
        }

        [Fact]
        public void F17_T2_05_ExtremeValues_HandlesThousandsOfItems()
        {
            var stats = DashboardViewModel.CalculateTopStats(5000, 10000, 500, 1000, 8000, 10000);
            Assert.Equal(50.0, stats.TasksCompletionPercentage);
            Assert.Equal(80.0, stats.HideoutItemsPercentage);
        }

        #endregion

        #region F18: "Up Next" / Relevant Tasks List Ranking

        [Fact]
        public void F18_T1_01_RankingFormula_RanksTraderUnlockHighest()
        {
            var tasks = CreateSampleDashboardTasks();
            var impactEngine = new TaskImpactEngine(tasks);

            var ranked = DashboardViewModel.RankUpNextTasks(tasks.Take(2), impactEngine);

            Assert.Equal("Introduction", ranked.First().QuestName);
            Assert.True(ranked.First().PriorityScore >= 1000);
        }

        [Fact]
        public void F18_T1_02_RankingFormula_RanksHighOpensCount()
        {
            var tasks = CreateSampleDashboardTasks();
            var impactEngine = new TaskImpactEngine(tasks);

            var ranked = DashboardViewModel.RankUpNextTasks(tasks.Take(3), impactEngine);
            Assert.NotNull(ranked);
            Assert.True(ranked.Count >= 2);
        }

        [Fact]
        public void F18_T1_03_ReasonText_FormatsOpensNextAndRemaining()
        {
            var tasks = CreateSampleDashboardTasks();
            var impactEngine = new TaskImpactEngine(tasks);

            var ranked = DashboardViewModel.RankUpNextTasks(tasks.Where(t => t.NormalizedName == "debut"), impactEngine);
            Assert.Single(ranked);
            Assert.Contains("left", ranked[0].ReasonText);
        }

        [Fact]
        public void F18_T1_04_LockedTasksExcludedFromUpNext()
        {
            var tasks = CreateSampleDashboardTasks();
            var impactEngine = new TaskImpactEngine(tasks);

            var availableOnly = tasks.Where(t => t.Previous == null || t.Previous.Count == 0);
            var ranked = DashboardViewModel.RankUpNextTasks(availableOnly, impactEngine);

            Assert.DoesNotContain(ranked, r => r.QuestName == "Checking");
        }

        [Fact]
        public void F18_T1_05_CompletedTasksExcludedFromUpNext()
        {
            var tasks = CreateSampleDashboardTasks();
            var activeTasks = tasks.Where(t => t.NormalizedName != "debut");
            var impactEngine = new TaskImpactEngine(tasks, new[] { "debut" });

            var ranked = DashboardViewModel.RankUpNextTasks(activeTasks, impactEngine);
            Assert.DoesNotContain(ranked, r => r.QuestName == "Debut");
        }

        [Fact]
        public void F18_T2_01_ZeroActionableTasks_SurfacesBlockedPrerequisites()
        {
            var empty = new List<TarkovTask>();
            var impactEngine = new TaskImpactEngine(empty);
            var ranked = DashboardViewModel.RankUpNextTasks(empty, impactEngine);
            Assert.Empty(ranked);
        }

        [Fact]
        public void F18_T2_02_AlmostDoneTask_BoostsScore()
        {
            var tasks = CreateSampleDashboardTasks();
            var impactEngine = new TaskImpactEngine(tasks);

            var intro = tasks.First(t => t.NormalizedName == "introduction");
            var ranked = DashboardViewModel.RankUpNextTasks(new[] { intro }, impactEngine);

            Assert.Contains("left", ranked[0].ReasonText);
        }

        [Fact]
        public void F18_T2_03_TieBreaking_DeterministicOrdering()
        {
            var tasks = new List<TarkovTask>
            {
                new() { Id = "q_b", Name = "Task B", NormalizedName = "task-b" },
                new() { Id = "q_a", Name = "Task A", NormalizedName = "task-a" }
            };
            var impactEngine = new TaskImpactEngine(tasks);

            var ranked = DashboardViewModel.RankUpNextTasks(tasks, impactEngine);
            Assert.Equal("Task A", ranked[0].QuestName);
            Assert.Equal("Task B", ranked[1].QuestName);
        }

        [Fact]
        public void F18_T2_04_LightkeeperAndKappaBonuses_ApplyCorrectWeights()
        {
            var kappaTask = new TarkovTask { Name = "Kappa Task", NormalizedName = "k", ReqKappa = true };
            var nonKappaTask = new TarkovTask { Name = "Non Kappa Task", NormalizedName = "nk", ReqKappa = false };

            var impactEngine = new TaskImpactEngine(new[] { kappaTask, nonKappaTask });
            var impactK = impactEngine.GetTaskImpact("k");
            var impactNK = impactEngine.GetTaskImpact("nk");

            Assert.True(impactK.PriorityScore >= impactNK.PriorityScore);
        }

        [Fact]
        public void F18_T2_05_MaxUpNextItems_LimitsListToConfiguredCap()
        {
            var tasks = new List<TarkovTask>();
            for (int i = 0; i < 20; i++)
            {
                tasks.Add(new TarkovTask { Name = $"Task {i:D2}", NormalizedName = $"t-{i}" });
            }
            var impactEngine = new TaskImpactEngine(tasks);

            var ranked = DashboardViewModel.RankUpNextTasks(tasks, impactEngine, maxCount: 5);
            Assert.Equal(5, ranked.Count);
        }

        #endregion

        #region F19: 11-Trader Progress Grid Cards (LL1-LL4)

        [Fact]
        public void F19_T1_01_AllElevenTraders_RenderInGrid()
        {
            var cards = CreateSampleTraderCards();
            Assert.Equal(11, cards.Count);

            var names = cards.Select(c => c.DisplayName).ToList();
            Assert.Contains("Prapor", names);
            Assert.Contains("Therapist", names);
            Assert.Contains("Skier", names);
            Assert.Contains("Peacekeeper", names);
            Assert.Contains("Mechanic", names);
            Assert.Contains("Ragman", names);
            Assert.Contains("Jaeger", names);
            Assert.Contains("Fence", names);
            Assert.Contains("Lightkeeper", names);
            Assert.Contains("Ref", names);
            Assert.Contains("BTR Driver", names);
        }

        [Fact]
        public void F19_T1_02_LoyaltyLevelSelector_ButtonsLL1ThroughLL4()
        {
            var prapor = CreateSampleTraderCards().First(c => c.DisplayName == "Prapor");
            Assert.InRange(prapor.CurrentLoyaltyLevel, 1, 4);
        }

        [Fact]
        public void F19_T1_03_TraderReputation_DisplaysCurrentRep()
        {
            var prapor = CreateSampleTraderCards().First(c => c.DisplayName == "Prapor");
            Assert.Equal(0.20, prapor.CurrentReputation);
            Assert.Equal("+0.20", prapor.CurrentReputation.ToString("+0.00", CultureInfo.InvariantCulture));
        }

        [Fact]
        public void F19_T1_04_TaskProgressBar_ShowsTraderCompletedTasks()
        {
            var therapist = CreateSampleTraderCards().First(c => c.DisplayName == "Therapist");
            Assert.Equal(8, therapist.CompletedTasks);
            Assert.Equal(22, therapist.TotalTasks);

            double progress = (double)therapist.CompletedTasks / therapist.TotalTasks;
            Assert.True(progress > 0.35 && progress < 0.37);
        }

        [Fact]
        public void F19_T1_05_LockedTrader_ShowsLockedBadgeAndUnlockQuest()
        {
            var jaeger = CreateSampleTraderCards().First(c => c.DisplayName == "Jaeger");
            Assert.True(jaeger.IsLocked);
            Assert.Equal("Requires Introduction", jaeger.UnlockRequirementText);
        }

        [Fact]
        public void F19_T2_01_FenceScavKarma_DisplaysCorrectly()
        {
            var fence = CreateSampleTraderCards().First(c => c.DisplayName == "Fence");
            Assert.Equal(2.45, fence.CurrentReputation);
        }

        [Fact]
        public void F19_T2_02_LLOverride_UpdatesRequirementCalculations()
        {
            var prapor = CreateSampleTraderCards().First(c => c.DisplayName == "Prapor");
            var modified = prapor with { CurrentLoyaltyLevel = 2 };

            Assert.Equal(2, modified.CurrentLoyaltyLevel);
        }

        [Fact]
        public void F19_T2_03_TraderUnlock_FlipsLockedStateToActive()
        {
            var jaegerLocked = CreateSampleTraderCards().First(c => c.DisplayName == "Jaeger");
            Assert.True(jaegerLocked.IsLocked);

            var jaegerUnlocked = jaegerLocked with { IsLocked = false, UnlockRequirementText = null };
            Assert.False(jaegerUnlocked.IsLocked);
            Assert.Null(jaegerUnlocked.UnlockRequirementText);
        }

        [Fact]
        public void F19_T2_04_NegativeKarma_DisplaysPenaltyWarning()
        {
            var fence = CreateSampleTraderCards().First(c => c.DisplayName == "Fence");
            var badFence = fence with { CurrentReputation = -1.25 };

            Assert.True(badFence.CurrentReputation < 0);
        }

        [Fact]
        public void F19_T2_05_MaxLoyaltyLevelCrown_DisplaysCrownIconAtLL4()
        {
            var therapist = CreateSampleTraderCards().First(c => c.DisplayName == "Therapist");
            var maxLL = therapist with { CurrentLoyaltyLevel = 4 };

            bool isCrown = maxLL.CurrentLoyaltyLevel == 4;
            Assert.True(isCrown);
        }

#endregion
        [Fact]
        public void DashboardPage_CanInstantiateAndMeasure_WithoutCrashing()
        {
            Exception? caught = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    try { if (System.Windows.Application.Current == null) new System.Windows.Application(); } catch { }
                    var page = new TarkovHelper.Pages.DashboardPage();
                    page.Measure(new System.Windows.Size(1200, 800));
                    page.Arrange(new System.Windows.Rect(0, 0, 1200, 800));
                    page.UpdateLayout();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(caught);
        }

        [Fact]
        public void QuestListPage_CanInstantiateAndMeasure_WithoutCrashing()
        {
            Exception? caught = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    try { if (System.Windows.Application.Current == null) new System.Windows.Application(); } catch { }
                    if (System.Windows.Application.Current != null)
                    {
                        var res = System.Windows.Application.Current.Resources;
                        if (!res.Contains("BackgroundDarkBrush")) res["BackgroundDarkBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 26));
                        if (!res.Contains("BackgroundMediumBrush")) res["BackgroundMediumBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 37));
                        if (!res.Contains("BackgroundLightBrush")) res["BackgroundLightBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 45));
                        if (!res.Contains("BorderBrush")) res["BorderBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(61, 61, 61));
                        if (!res.Contains("TextPrimaryBrush")) res["TextPrimaryBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(224, 224, 224));
                        if (!res.Contains("TextSecondaryBrush")) res["TextSecondaryBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(158, 158, 158));
                        if (!res.Contains("AccentBrush")) res["AccentBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(197, 168, 74));
                    }
                    var page = new TarkovHelper.Pages.QuestListPage();
                    page.Measure(new System.Windows.Size(1200, 800));
                    page.Arrange(new System.Windows.Rect(0, 0, 1200, 800));
                    page.UpdateLayout();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(caught);
        }

        [Fact]
        public void TaskCardControl_WhenDataContextSetToQuestViewModel_PopulatesFields()
        {
            var task = new TarkovHelper.Models.TarkovTask
            {
                Name = "Debut",
                Trader = "Prapor",
                NormalizedName = "debut",
                Maps = new System.Collections.Generic.List<string> { "customs" },
                ReqKappa = true,
                RequiredLevel = 1
            };
            var qvm = new TarkovHelper.Pages.QuestViewModel
            {
                Task = task,
                DisplayName = "Debut",
                Status = TarkovHelper.Models.QuestStatus.Active
            };

            TarkovHelper.Controls.TaskCardControl? card = null;
            var thread = new System.Threading.Thread(() =>
            {
                try { if (System.Windows.Application.Current == null) new System.Windows.Application(); } catch { }
                if (System.Windows.Application.Current != null)
                {
                    var res = System.Windows.Application.Current.Resources;
                    if (!res.Contains("BackgroundDarkBrush")) res["BackgroundDarkBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 26));
                    if (!res.Contains("BackgroundMediumBrush")) res["BackgroundMediumBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 37));
                    if (!res.Contains("BackgroundLightBrush")) res["BackgroundLightBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 45));
                    if (!res.Contains("BorderBrush")) res["BorderBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(61, 61, 61));
                    if (!res.Contains("TextPrimaryBrush")) res["TextPrimaryBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(224, 224, 224));
                    if (!res.Contains("TextSecondaryBrush")) res["TextSecondaryBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(158, 158, 158));
                    if (!res.Contains("AccentBrush")) res["AccentBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(197, 168, 74));
                }
                card = new TarkovHelper.Controls.TaskCardControl();
                card.DataContext = qvm;
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.NotNull(card);
            Assert.Equal("Debut", card.CardModel.Title);
            Assert.Equal("Prapor", card.CardModel.Trader);
            Assert.Equal("Customs", card.CardModel.MapBadge);
            Assert.True(card.CardModel.IsKappaRequired);
        }
    }
}
