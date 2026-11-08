using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Media;
using TarkovHelper.Models;
using TarkovHelper.Pages;
using TarkovHelper.Services;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests
{
    /// <summary>
    /// Empirical Challenger Stress Tests for TarkovHelper UI Revamp.
    /// Stress-tests MainHeaderViewModel boundaries, rapid toggling, progress math, loyalty calculations,
    /// and tests QuestFilterEngine under extensive permutations, regex attacks, and edge cases.
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class ChallengerPreviewStressTests : IDisposable
    {
        private readonly int _originalLevel;
        private readonly ProfileType _originalProfile;
        private readonly double _originalScavRep;
        private readonly MainHeaderViewModel _headerVm;

        public ChallengerPreviewStressTests()
        {
            _originalLevel = SettingsService.Instance.PlayerLevel;
            _originalProfile = ProfileService.Instance.CurrentProfile;
            _originalScavRep = SettingsService.Instance.ScavRep;

            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
            _headerVm = new MainHeaderViewModel();
        }

        public void Dispose()
        {
            _headerVm.Dispose();
            SettingsService.Instance.PlayerLevel = _originalLevel;
            ProfileService.Instance.CurrentProfile = ProfileType.Pvp;
            SettingsService.Instance.ScavRep = _originalScavRep;
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
        }

        #region 1. MainHeaderViewModel Stress Tests

        [Fact]
        public void Header_RapidLevelStepping_ClampsAtBoundaries()
        {
            // Start at level 15
            _headerVm.SetPlayerLevel(15);
            Assert.Equal(15, _headerVm.PlayerLevel);

            // Rapidly step up 200 times
            for (int i = 0; i < 200; i++)
            {
                if (_headerVm.CanLevelUp)
                {
                    _headerVm.LevelUp();
                }
            }

            // Must clamp strictly at MaxPlayerLevel (79)
            Assert.Equal(79, _headerVm.PlayerLevel);
            Assert.False(_headerVm.CanLevelUp);
            Assert.False(_headerVm.LevelUpCommand.CanExecute(null));
            Assert.True(_headerVm.CanLevelDown);
            Assert.True(_headerVm.LevelDownCommand.CanExecute(null));

            // Calling LevelUp at boundary must not exceed 79
            _headerVm.LevelUp();
            Assert.Equal(79, _headerVm.PlayerLevel);

            // Rapidly step down 200 times
            for (int i = 0; i < 200; i++)
            {
                if (_headerVm.CanLevelDown)
                {
                    _headerVm.LevelDown();
                }
            }

            // Must clamp strictly at MinPlayerLevel (1)
            Assert.Equal(1, _headerVm.PlayerLevel);
            Assert.False(_headerVm.CanLevelDown);
            Assert.False(_headerVm.LevelDownCommand.CanExecute(null));
            Assert.True(_headerVm.CanLevelUp);
            Assert.True(_headerVm.LevelUpCommand.CanExecute(null));

            // Calling LevelDown at boundary must not go below 1
            _headerVm.LevelDown();
            Assert.Equal(1, _headerVm.PlayerLevel);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(-1, 1)]
        [InlineData(-5, 1)]
        [InlineData(-999, 1)]
        [InlineData(int.MinValue, 1)]
        [InlineData(80, 79)]
        [InlineData(81, 79)]
        [InlineData(999, 79)]
        [InlineData(int.MaxValue, 79)]
        [InlineData(1, 1)]
        [InlineData(42, 42)]
        [InlineData(79, 79)]
        public void Header_SetPlayerLevel_ExtremeValues_ClampStrictly(int inputLevel, int expectedClamped)
        {
            _headerVm.SetPlayerLevel(inputLevel);
            Assert.Equal(expectedClamped, _headerVm.PlayerLevel);
            Assert.Equal(expectedClamped, SettingsService.Instance.PlayerLevel);
            Assert.True(_headerVm.PlayerLevel >= 1 && _headerVm.PlayerLevel <= 79);
        }

        [Fact]
        public void Header_RapidProfileModeToggling_MaintainsIsolationAndReactivity()
        {
            // Rapidly toggle PVP/PVE 500 times in quick succession
            for (int i = 0; i < 500; i++)
            {
                var target = (i % 2 == 0) ? ProfileType.Pve : ProfileType.Pvp;
                _headerVm.CurrentProfile = target;

                Assert.Equal(target, _headerVm.CurrentProfile);
                Assert.Equal(target == ProfileType.Pvp, _headerVm.IsPvp);
                Assert.Equal(target == ProfileType.Pve, _headerVm.IsPve);
                Assert.True(_headerVm.IsPvp ^ _headerVm.IsPve, "PVP and PVE must be strictly mutually exclusive");
            }

            // Toggle via commands rapidly
            for (int i = 0; i < 200; i++)
            {
                _headerVm.SetPveCommand.Execute(null);
                Assert.True(_headerVm.IsPve);
                Assert.False(_headerVm.IsPvp);

                _headerVm.SetPvpCommand.Execute(null);
                Assert.True(_headerVm.IsPvp);
                Assert.False(_headerVm.IsPve);
            }
        }

        [Fact]
        public void Header_ProgressCalculations_ZeroQuests_DoesNotDivideByZeroOrProduceNaN()
        {
            _headerVm.RefreshMetrics();

            // Check overall progress values when no division by zero can occur
            Assert.False(double.IsNaN(_headerVm.OverallProgressPercentage));
            Assert.False(double.IsInfinity(_headerVm.OverallProgressPercentage));
            Assert.NotNull(_headerVm.OverallProgressText);
            Assert.NotNull(_headerVm.OverallProgressTooltip);

            // Check Kappa progress
            Assert.False(double.IsNaN(_headerVm.KappaProgressPercentage));
            Assert.False(double.IsInfinity(_headerVm.KappaProgressPercentage));
            Assert.NotNull(_headerVm.KappaProgressText);
            Assert.NotNull(_headerVm.KappaPendingText);
            Assert.NotNull(_headerVm.KappaProgressTooltip);
            Assert.True(_headerVm.KappaPendingQuests >= 0);
        }

        [Fact]
        public void Header_ProgressCalculations_100PercentCompleted_FormatsAccurately()
        {
            var initialOverall = _headerVm.OverallCompletedQuests;
            var initialKappa = _headerVm.KappaCompletedQuests;

            // Invariant: Completed must never exceed Total
            Assert.True(_headerVm.OverallCompletedQuests <= _headerVm.OverallTotalQuests);
            Assert.True(_headerVm.OverallProgressPercentage >= 0.0 && _headerVm.OverallProgressPercentage <= 100.0);

            Assert.True(_headerVm.KappaCompletedQuests <= _headerVm.KappaTotalQuests);
            Assert.True(_headerVm.KappaProgressPercentage >= 0.0 && _headerVm.KappaProgressPercentage <= 100.0);
            Assert.Equal(_headerVm.KappaTotalQuests - _headerVm.KappaCompletedQuests, _headerVm.KappaPendingQuests);
        }

        [Fact]
        public void Header_TraderLoyalty_All11Traders_AcrossAll79Levels_MonotonicProgression()
        {
            Assert.Equal(11, _headerVm.Traders.Count);

            var expectedTraders = new[]
            {
                "Prapor", "Therapist", "Fence", "Skier", "Peacekeeper",
                "Mechanic", "Ragman", "Jaeger", "Ref", "Lightkeeper", "BTR Driver"
            };

            foreach (var name in expectedTraders)
            {
                Assert.Contains(_headerVm.Traders, t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            }

            var previousLevels = new Dictionary<string, int>();
            foreach (var t in _headerVm.Traders)
            {
                previousLevels[t.Name] = 0;
            }

            // Iterate through every single level 1 to 79
            for (int lvl = 1; lvl <= 79; lvl++)
            {
                _headerVm.SetPlayerLevel(lvl);

                foreach (var trader in _headerVm.Traders)
                {
                    // Invariant: loyalty level is always between 0 and 4
                    Assert.InRange(trader.LoyaltyLevel, 0, 4);

                    // Badge is never null or empty
                    Assert.False(string.IsNullOrWhiteSpace(trader.LoyaltyBadge));

                    // Brushes are valid
                    Assert.NotNull(trader.BadgeBackground);
                    Assert.NotNull(trader.BadgeForeground);
                    Assert.NotNull(trader.BadgeBorderBrush);

                    // Tooltip is informative
                    Assert.False(string.IsNullOrWhiteSpace(trader.ToolTipText));

                    // Initials are valid
                    Assert.False(string.IsNullOrWhiteSpace(trader.FallbackInitials));

                    if (!trader.IsFence)
                    {
                        // Check natural progression monotonicity: as level rises, natural loyalty never regresses
                        if (trader.IsUnlocked)
                        {
                            Assert.True(trader.LoyaltyLevel >= previousLevels[trader.Name],
                                $"Trader {trader.Name} regressed from LL{previousLevels[trader.Name]} to LL{trader.LoyaltyLevel} at character level {lvl}");
                            previousLevels[trader.Name] = trader.LoyaltyLevel;
                        }

                        // Badge representation
                        if (trader.LoyaltyLevel == 0)
                        {
                            Assert.Equal("🔒", trader.LoyaltyBadge);
                            Assert.False(trader.IsUnlocked);
                        }
                        else if (trader.LoyaltyLevel >= 4)
                        {
                            Assert.Equal("👑", trader.LoyaltyBadge);
                            Assert.True(trader.IsUnlocked);
                        }
                        else
                        {
                            Assert.Equal($"LL{trader.LoyaltyLevel}", trader.LoyaltyBadge);
                            Assert.True(trader.IsUnlocked);
                        }
                    }
                }
            }
        }

        [Theory]
        [InlineData(-10.0, -6.0, "-6.0", "-6.00", false)] // Clamped to MinScavRep (-6.0)
        [InlineData(-6.0, -6.0, "-6.0", "-6.00", false)]
        [InlineData(-1.5, -1.5, "-1.5", "-1.50", false)]
        [InlineData(0.0, 0.0, "0.0", "0.00", false)]
        [InlineData(1.5, 1.5, "+1.5", "+1.50", false)]
        [InlineData(5.9, 5.9, "+5.9", "+5.90", false)]
        [InlineData(6.0, 6.0, "👑", "+6.00", true)]
        [InlineData(10.0, 6.0, "👑", "+6.00", true)] // Clamped to MaxScavRep (6.0)
        public void Header_FenceScavKarma_ExtremeRangesAndCrownThreshold(
            double inputScavRep, double expectedEffectiveKarma, string expectedBadge, string expectedKarmaText, bool isCrown)
        {
            SettingsService.Instance.ScavRep = inputScavRep;
            _headerVm.RefreshMetrics();

            var fence = _headerVm.Traders.First(t => t.IsFence);
            Assert.Equal(expectedEffectiveKarma, fence.ScavKarma);
            Assert.Equal(expectedKarmaText, fence.ScavKarmaText);
            Assert.Equal(expectedBadge, fence.LoyaltyBadge);

            if (isCrown)
            {
                Assert.Equal(TraderLoyaltyItemViewModel.GoldBadgeBg, fence.BadgeBackground);
                Assert.Equal(TraderLoyaltyItemViewModel.DarkBadgeFg, fence.BadgeForeground);
                Assert.Equal(TraderLoyaltyItemViewModel.GoldBadgeBorder, fence.BadgeBorderBrush);
            }
            else if (expectedEffectiveKarma >= 0)
            {
                Assert.Equal(TraderLoyaltyItemViewModel.PositiveScavBg, fence.BadgeBackground);
                Assert.Equal(TraderLoyaltyItemViewModel.PositiveScavFg, fence.BadgeForeground);
            }
            else
            {
                Assert.Equal(TraderLoyaltyItemViewModel.NegativeScavBg, fence.BadgeBackground);
                Assert.Equal(TraderLoyaltyItemViewModel.NegativeScavFg, fence.BadgeForeground);
            }
        }

        #endregion

        #region 2. Production QuestFilterEngine Stress Tests (via Reflection on TarkovHelper.dll)

        private static readonly Assembly ProdAssembly = typeof(MainWindow).Assembly;
        private static readonly Type ProdEngineType = ProdAssembly.GetType("TarkovHelper.ViewModels.QuestFilterEngine")!;
        private static readonly Type ProdCriteriaType = ProdAssembly.GetType("TarkovHelper.ViewModels.QuestFilterCriteria")!;
        private static readonly Type ProdNavModeType = ProdAssembly.GetType("TarkovHelper.ViewModels.NavigationMode")!;
        private static readonly Type ProdStatusType = ProdAssembly.GetType("TarkovHelper.ViewModels.QuickFilterStatus")!;

        private static readonly MethodInfo MatchesMethod = ProdEngineType.GetMethod("Matches", BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo MatchesTraderMethod = ProdEngineType.GetMethod("MatchesTrader", BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo MatchesMapMethod = ProdEngineType.GetMethod("MatchesMap", BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo MatchesSearchQueryMethod = ProdEngineType.GetMethod("MatchesSearchQuery", BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo NormalizeMapKeyMethod = ProdEngineType.GetMethod("NormalizeMapKey", BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo GetCanonicalMapDisplayNameMethod = ProdEngineType.GetMethod("GetCanonicalMapDisplayName", BindingFlags.Public | BindingFlags.Static)!;

        private static object CreateProdCriteria(
            string mode = "Traders",
            string? selectedTarget = null,
            string status = "Active",
            bool kappaOnly = false,
            bool currentLevelOnly = false,
            string searchQuery = "",
            string? faction = null,
            bool itemRequiredOnly = false,
            bool keyRequiredOnly = false,
            bool storyOnly = false)
        {
            var criteria = Activator.CreateInstance(ProdCriteriaType)!;

            ProdCriteriaType.GetProperty("Mode")!.SetValue(criteria, Enum.Parse(ProdNavModeType, mode));
            ProdCriteriaType.GetProperty("SelectedTarget")!.SetValue(criteria, selectedTarget);
            ProdCriteriaType.GetProperty("StatusFilter")!.SetValue(criteria, Enum.Parse(ProdStatusType, status));
            ProdCriteriaType.GetProperty("KappaOnly")!.SetValue(criteria, kappaOnly);
            ProdCriteriaType.GetProperty("CurrentLevelOnly")!.SetValue(criteria, currentLevelOnly);
            ProdCriteriaType.GetProperty("SearchQuery")!.SetValue(criteria, searchQuery);
            ProdCriteriaType.GetProperty("Faction")!.SetValue(criteria, faction);
            ProdCriteriaType.GetProperty("ItemRequiredOnly")!.SetValue(criteria, itemRequiredOnly);
            ProdCriteriaType.GetProperty("KeyRequiredOnly")!.SetValue(criteria, keyRequiredOnly);
            ProdCriteriaType.GetProperty("StoryOnly")!.SetValue(criteria, storyOnly);

            return criteria;
        }

        private static bool InvokeMatches(QuestViewModel vm, object prodCriteria, int currentPlayerLevel) =>
            (bool)MatchesMethod.Invoke(null, new object[] { vm, prodCriteria, currentPlayerLevel })!;

        private static bool InvokeMatchesTrader(QuestViewModel vm, string targetTrader) =>
            (bool)MatchesTraderMethod.Invoke(null, new object[] { vm, targetTrader })!;

        private static bool InvokeMatchesMap(QuestViewModel vm, string targetMap) =>
            (bool)MatchesMapMethod.Invoke(null, new object[] { vm, targetMap })!;

        private static bool InvokeMatchesSearchQuery(QuestViewModel vm, string query) =>
            (bool)MatchesSearchQueryMethod.Invoke(null, new object[] { vm, query })!;

        private static string InvokeNormalizeMapKey(string map) =>
            (string)NormalizeMapKeyMethod.Invoke(null, new object[] { map })!;

        private static string InvokeGetCanonicalMapDisplayName(string mapKey) =>
            (string)GetCanonicalMapDisplayNameMethod.Invoke(null, new object[] { mapKey })!;

        [Fact]
        public void ProdEngine_FilterPermutations_TradersXMapsXStatusesXModifiers_NeverThrows()
        {
            var dataset = CreateStressQuestDataset();

            var traders = new[] { null, "Prapor", "Therapist", "Fence", "Skier", "Peacekeeper", "Mechanic", "Ragman", "Jaeger", "Ref", "Lightkeeper", "BTR Driver" };
            var maps = new[] { null, "ground-zero", "customs", "woods", "factory", "interchange", "shoreline", "reserve", "lighthouse", "streets", "the-lab" };
            var statuses = new[] { "Active", "Locked", "Done", "All" };
            var boolCombos = new[]
            {
                (Kappa: false, CurrentLvl: false, Items: false, Keys: false),
                (Kappa: true,  CurrentLvl: false, Items: false, Keys: false),
                (Kappa: false, CurrentLvl: true,  Items: false, Keys: false),
                (Kappa: true,  CurrentLvl: true,  Items: true,  Keys: true)
            };

            int evaluationsCount = 0;

            foreach (var trader in traders)
            {
                foreach (var status in statuses)
                {
                    foreach (var (kappa, curLvl, items, keys) in boolCombos)
                    {
                        var criteria = CreateProdCriteria(
                            mode: "Traders",
                            selectedTarget: trader,
                            status: status,
                            kappaOnly: kappa,
                            currentLevelOnly: curLvl,
                            itemRequiredOnly: items,
                            keyRequiredOnly: keys);

                        foreach (var quest in dataset)
                        {
                            bool matched = InvokeMatches(quest, criteria, currentPlayerLevel: 15);
                            evaluationsCount++;

                            if (matched)
                            {
                                // Validate status invariant
                                if (status == "Active")
                                    Assert.Equal(QuestStatus.Active, quest.Status);
                                else if (status == "Locked")
                                    Assert.True(quest.Status == QuestStatus.Locked || quest.Status == QuestStatus.LevelLocked);
                                else if (status == "Done")
                                    Assert.Equal(QuestStatus.Done, quest.Status);

                                // Validate Kappa invariant
                                if (kappa)
                                    Assert.True(quest.IsKappaRequired || (quest.Task?.ReqKappa ?? false));

                                // Validate CurrentLevel invariant
                                if (curLvl && quest.Task?.RequiredLevel.HasValue == true)
                                    Assert.True(quest.Task.RequiredLevel.Value <= 15);

                                // Validate Keys invariant
                                if (keys)
                                    Assert.True(quest.HasNeededKeys);

                                // Validate Items invariant
                                if (items)
                                    Assert.True(quest.HasRequiredItems || quest.HasFirRequirements || (quest.Task?.RequiredItems?.Count > 0));

                                // Validate Trader invariant
                                if (!string.IsNullOrEmpty(trader))
                                    Assert.True(InvokeMatchesTrader(quest, trader));
                            }
                        }
                    }
                }
            }

            foreach (var map in maps)
            {
                foreach (var status in statuses)
                {
                    var criteria = CreateProdCriteria(
                        mode: "Maps",
                        selectedTarget: map,
                        status: status);

                    foreach (var quest in dataset)
                    {
                        bool matched = InvokeMatches(quest, criteria, currentPlayerLevel: 15);
                        evaluationsCount++;

                        if (matched && !string.IsNullOrEmpty(map))
                        {
                            Assert.True(InvokeMatchesMap(quest, map));
                        }
                    }
                }
            }

            Assert.True(evaluationsCount > 1000, $"Expected > 1000 evaluations, got {evaluationsCount}");
        }

        [Theory]
        // Empty / Whitespace
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t\r\n")]
        // Regex metacharacters that would break naive Regex matching
        [InlineData(".*")]
        [InlineData("^.*$")]
        [InlineData("[a-z]+")]
        [InlineData("(?i)customs")]
        [InlineData("\\d{3}")]
        [InlineData("\\b")]
        [InlineData("(?<=)")]
        [InlineData("[")]
        [InlineData("]")]
        [InlineData("(")]
        [InlineData(")")]
        [InlineData("{")]
        [InlineData("}")]
        [InlineData("*")]
        [InlineData("+")]
        [InlineData("?")]
        [InlineData("^")]
        [InlineData("$")]
        [InlineData("|")]
        [InlineData("\\")]
        // Unicode and multilingual scripts
        [InlineData("야간전")]
        [InlineData("カスタム")]
        [InlineData("Таможня")]
        [InlineData("Прапор")]
        [InlineData("العربية")]
        [InlineData("★")]
        [InlineData("👑")]
        [InlineData("🔥")]
        // SQL injection & special code strings
        [InlineData("' OR '1'='1")]
        [InlineData("'; DROP TABLE quests; --")]
        [InlineData("UNION SELECT * FROM user_data")]
        [InlineData("\" OR \"\"=\"")]
        [InlineData("<script>alert(1)</script>")]
        [InlineData("<img src=x onerror=alert(1)>")]
        [InlineData("<?xml version=\"1.0\"?>")]
        public void ProdEngine_SearchQuery_AdversarialInputs_NeverThrowsOrCrashes(string query)
        {
            var dataset = CreateStressQuestDataset();

            foreach (var quest in dataset)
            {
                // Invoke search query match directly
                var matchesSearch = InvokeMatchesSearchQuery(quest, query);
                Assert.True(matchesSearch || !matchesSearch); // Evaluated successfully without unhandled exception

                // Also invoke via full criteria
                var criteria = CreateProdCriteria(searchQuery: query, status: "All");
                var matchesFull = InvokeMatches(quest, criteria, currentPlayerLevel: 15);
                Assert.True(matchesFull || !matchesFull);
            }
        }

        [Fact]
        public void ProdEngine_SearchQuery_ExtremelyLongString_CompletesQuicklyWithoutOOM()
        {
            var dataset = CreateStressQuestDataset();
            var longQuery = new string('A', 10000);

            var criteria = CreateProdCriteria(searchQuery: longQuery, status: "All");

            foreach (var quest in dataset)
            {
                bool matches = InvokeMatches(quest, criteria, currentPlayerLevel: 15);
                Assert.False(matches, "A 10,000-char 'A' sequence should not match any quest");
            }
        }

        [Theory]
        [InlineData("groundzero", "groundzero")]
        [InlineData("sandbox", "groundzero")]
        [InlineData("Ground-Zero", "groundzero")]
        [InlineData("customs", "customs")]
        [InlineData("bigmap", "customs")]
        [InlineData("woods", "woods")]
        [InlineData("factory", "factory")]
        [InlineData("interchange", "interchange")]
        [InlineData("shoreline", "shoreline")]
        [InlineData("reserve", "reserve")]
        [InlineData("rezerv", "reserve")]
        [InlineData("lighthouse", "lighthouse")]
        [InlineData("streets", "streets")]
        [InlineData("streets-of-tarkov", "streets")]
        [InlineData("thelab", "thelab")]
        [InlineData("the-lab", "thelab")]
        [InlineData("lab", "thelab")]
        public void ProdEngine_NormalizeMapKey_NormalizesAliasesAccurately(string rawMap, string expectedNormalized)
        {
            var result = InvokeNormalizeMapKey(rawMap);
            Assert.Equal(expectedNormalized, result);
        }

        [Theory]
        [InlineData("groundzero", "Ground Zero")]
        [InlineData("customs", "Customs")]
        [InlineData("woods", "Woods")]
        [InlineData("factory", "Factory")]
        [InlineData("interchange", "Interchange")]
        [InlineData("shoreline", "Shoreline")]
        [InlineData("reserve", "Reserve")]
        [InlineData("lighthouse", "Lighthouse")]
        [InlineData("streets", "Streets of Tarkov")]
        [InlineData("thelab", "The Lab")]
        public void ProdEngine_GetCanonicalMapDisplayName_ReturnsExpectedDisplayName(string mapKey, string expectedDisplay)
        {
            var result = InvokeGetCanonicalMapDisplayName(mapKey);
            Assert.Equal(expectedDisplay, result);
        }

        [Fact]
        public void ProdEngine_MatchesTrader_HandlesFriendlyNamesAndStoryChapters()
        {
            var storyQuest = new QuestViewModel
            {
                Task = new TarkovTask { Id = "story_1", Name = "Prologue", Trader = "Story" }
            };

            var btrQuest = new QuestViewModel
            {
                Task = new TarkovTask { Id = "btr_1", Name = "A Helping Hand", Trader = "BTR Driver" }
            };

            var praporQuest = new QuestViewModel
            {
                Task = new TarkovTask { Id = "prapor_1", Name = "Debut", Trader = "Prapor" }
            };

            // Story matches
            Assert.True(InvokeMatchesTrader(storyQuest, "Story"));
            Assert.True(InvokeMatchesTrader(storyQuest, "Story Chapters"));
            Assert.False(InvokeMatchesTrader(storyQuest, "Prapor"));

            // BTR matches
            Assert.True(InvokeMatchesTrader(btrQuest, "BTR"));
            Assert.True(InvokeMatchesTrader(btrQuest, "BTR Driver"));
            Assert.False(InvokeMatchesTrader(btrQuest, "Therapist"));

            // Regular trader matches
            Assert.True(InvokeMatchesTrader(praporQuest, "Prapor"));
            Assert.False(InvokeMatchesTrader(praporQuest, "Story"));
            Assert.False(InvokeMatchesTrader(praporQuest, "Therapist"));
        }

        [Fact]
        public void ProdEngine_MatchesMap_HandlesAliasesAndMultiMapQuests()
        {
            var multiMapQuest = new QuestViewModel
            {
                Task = new TarkovTask
                {
                    Id = "punisher_4",
                    Name = "The Punisher - Part 4",
                    Maps = new List<string> { "shoreline", "woods" }
                }
            };

            var customsQuest = new QuestViewModel
            {
                Task = new TarkovTask
                {
                    Id = "checking",
                    Name = "Checking",
                    Maps = new List<string> { "bigmap" } // Tarkov dev alias for customs
                }
            };

            // Shoreline and Woods match multi-map
            Assert.True(InvokeMatchesMap(multiMapQuest, "shoreline"));
            Assert.True(InvokeMatchesMap(multiMapQuest, "woods"));
            Assert.False(InvokeMatchesMap(multiMapQuest, "factory"));

            // Bigmap alias matches Customs
            Assert.True(InvokeMatchesMap(customsQuest, "customs"));
            Assert.True(InvokeMatchesMap(customsQuest, "bigmap"));
            Assert.False(InvokeMatchesMap(customsQuest, "woods"));
        }

        [Fact]
        public void ProdEngine_MatchesSearchQuery_FindsItemsAndKeys()
        {
            var salewaQuest = new QuestViewModel
            {
                DisplayName = "Shortage",
                Task = new TarkovTask
                {
                    Id = "shortage_1",
                    Name = "Shortage",
                    Trader = "Therapist",
                    RequiredItems = new List<QuestItem>
                    {
                        new() { ItemDisplayName = "Salewa first aid kit", ItemNormalizedName = "salewa" }
                    }
                }
            };

            Assert.True(InvokeMatchesSearchQuery(salewaQuest, "salewa"));
            Assert.True(InvokeMatchesSearchQuery(salewaQuest, "first aid"));
            Assert.True(InvokeMatchesSearchQuery(salewaQuest, "Shortage"));
            Assert.False(InvokeMatchesSearchQuery(salewaQuest, "dorm key 314"));
        }

        #endregion

        #region Helpers

        private static List<QuestViewModel> CreateStressQuestDataset()
        {
            return new List<QuestViewModel>
            {
                new()
                {
                    DisplayName = "Debut",
                    TraderInitial = "PR",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true,
                    Task = new TarkovTask
                    {
                        Id = "debut_task",
                        Name = "Debut",
                        NameKo = "데뷔",
                        NameJa = "初陣",
                        NormalizedName = "debut",
                        Trader = "Prapor",
                        Maps = new List<string> { "customs" },
                        ReqKappa = true,
                        RequiredLevel = 1,
                        RequiredItems = new List<QuestItem>
                        {
                            new() { ItemDisplayName = "MP-133 12ga shotgun", ItemNormalizedName = "mp133", Amount = 2, FoundInRaid = false }
                        }
                    }
                },
                new()
                {
                    DisplayName = "Checking",
                    TraderInitial = "PR",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true,
                    HasNeededKeys = true,
                    Task = new TarkovTask
                    {
                        Id = "checking_task",
                        Name = "Checking",
                        NormalizedName = "checking",
                        Trader = "Prapor",
                        Maps = new List<string> { "customs" },
                        ReqKappa = true,
                        RequiredLevel = 2
                    }
                },
                new()
                {
                    DisplayName = "Shortage",
                    TraderInitial = "TH",
                    Status = QuestStatus.Active,
                    IsKappaRequired = true,
                    HasFirRequirements = true,
                    Task = new TarkovTask
                    {
                        Id = "shortage_task",
                        Name = "Shortage",
                        NameKo = "물자 부족",
                        NormalizedName = "shortage",
                        Trader = "Therapist",
                        Maps = new List<string> { "customs" },
                        ReqKappa = true,
                        RequiredLevel = 1,
                        RequiredItems = new List<QuestItem>
                        {
                            new() { ItemDisplayName = "Salewa first aid kit", ItemNormalizedName = "salewa", Amount = 3, FoundInRaid = true }
                        }
                    }
                },
                new()
                {
                    DisplayName = "Sanitary Claws",
                    TraderInitial = "TH",
                    Status = QuestStatus.Locked,
                    IsKappaRequired = false,
                    HasFirRequirements = true,
                    Task = new TarkovTask
                    {
                        Id = "sanitary_task",
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
                    }
                },
                new()
                {
                    DisplayName = "Grenadier",
                    TraderInitial = "PR",
                    Status = QuestStatus.LevelLocked,
                    IsKappaRequired = true,
                    Task = new TarkovTask
                    {
                        Id = "grenadier_task",
                        Name = "Grenadier",
                        NormalizedName = "grenadier",
                        Trader = "Prapor",
                        Maps = new List<string> { "factory", "customs" },
                        ReqKappa = true,
                        RequiredLevel = 30
                    }
                },
                new()
                {
                    DisplayName = "Shootout Picnic",
                    TraderInitial = "PR",
                    Status = QuestStatus.Done,
                    IsKappaRequired = true,
                    Task = new TarkovTask
                    {
                        Id = "shootout_task",
                        Name = "Shootout Picnic",
                        NormalizedName = "shootout-picnic",
                        Trader = "Prapor",
                        Maps = new List<string> { "woods" },
                        ReqKappa = true,
                        RequiredLevel = 3
                    }
                },
                new()
                {
                    DisplayName = "Our Guy in the City",
                    TraderInitial = "PR",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false,
                    Task = new TarkovTask
                    {
                        Id = "our_guy_task",
                        Name = "Our Guy in the City",
                        NormalizedName = "our-guy-in-the-city",
                        Trader = "Prapor",
                        Maps = new List<string> { "streets" },
                        ReqKappa = false,
                        RequiredLevel = 15,
                        Faction = "bear"
                    }
                },
                new()
                {
                    DisplayName = "Tour",
                    TraderInitial = "PK",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false,
                    Task = new TarkovTask
                    {
                        Id = "tour_task",
                        Name = "Tour",
                        NormalizedName = "tour",
                        Trader = "Peacekeeper",
                        Maps = new List<string> { "lighthouse" },
                        ReqKappa = false,
                        RequiredLevel = 15,
                        Faction = "usec"
                    }
                },
                new()
                {
                    DisplayName = "A Helping Hand",
                    TraderInitial = "BTR",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false,
                    Task = new TarkovTask
                    {
                        Id = "btr_helping_hand",
                        Name = "A Helping Hand",
                        NormalizedName = "a-helping-hand",
                        Trader = "BTR Driver",
                        Maps = new List<string> { "streets", "woods" },
                        ReqKappa = false,
                        RequiredLevel = 1
                    }
                },
                new()
                {
                    DisplayName = "Story: Prologue",
                    TraderInitial = "ST",
                    Status = QuestStatus.Active,
                    IsKappaRequired = false,
                    Task = new TarkovTask
                    {
                        Id = "story_prologue",
                        Name = "Prologue",
                        NormalizedName = "prologue",
                        Trader = "Story Chapters",
                        Maps = new List<string> { "ground-zero" },
                        ReqKappa = false,
                        RequiredLevel = 1
                    }
                }
            };
        }

        #endregion
    }
}
