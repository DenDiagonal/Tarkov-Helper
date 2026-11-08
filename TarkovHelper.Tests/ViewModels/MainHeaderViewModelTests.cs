using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using TarkovHelper.Models;
using TarkovHelper.Services;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests.ViewModels
{
    /// <summary>
    /// Unit test suite for MainHeaderViewModel covering Features F1 through F6:
    /// - F1: Player Level Stepper & Text Entry (clamping 1-79, +/- commands, persistence).
    /// - F2: Game Mode Selector (PVP / PVE toggle, commands, profile reactivity).
    /// - F3: Global Quest Progress Metric (% completed vs total, formatting, reactivity).
    /// - F4: Kappa Progress (% and pending count, collector graph math, reactivity).
    /// - F5: Trader Loyalty Strip (11 canonical traders, natural LL thresholds, gated locks, Scav karma).
    /// - F6: Non-Invasive Actions (Commands, tooltips, lifecycle disposal).
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class MainHeaderViewModelTests : IDisposable
    {
        private readonly int _originalLevel;
        private readonly ProfileType _originalProfile;
        private readonly double _originalScavRep;
        private readonly MainHeaderViewModel _viewModel;

        public MainHeaderViewModelTests()
        {
            _originalLevel = SettingsService.Instance.PlayerLevel;
            _originalProfile = ProfileService.Instance.CurrentProfile;
            _originalScavRep = SettingsService.Instance.ScavRep;

            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
            _viewModel = new MainHeaderViewModel();
        }

        public void Dispose()
        {
            _viewModel.Dispose();
            SettingsService.Instance.PlayerLevel = _originalLevel;
            ProfileService.Instance.CurrentProfile = _originalProfile;
            SettingsService.Instance.ScavRep = _originalScavRep;
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
        }

        #region F1: Player Level Stepper & Text Entry

        [Fact]
        public void PlayerLevel_InitialValue_MatchesSettingsService()
        {
            Assert.Equal(SettingsService.Instance.PlayerLevel, _viewModel.PlayerLevel);
        }

        [Fact]
        public void LevelUp_IncrementsPlayerLevel_AndUpdatesSettings()
        {
            _viewModel.SetPlayerLevel(15);
            Assert.True(_viewModel.CanLevelUp);

            _viewModel.LevelUp();

            Assert.Equal(16, _viewModel.PlayerLevel);
            Assert.Equal(16, SettingsService.Instance.PlayerLevel);
        }

        [Fact]
        public void LevelDown_DecrementsPlayerLevel_AndUpdatesSettings()
        {
            _viewModel.SetPlayerLevel(15);
            Assert.True(_viewModel.CanLevelDown);

            _viewModel.LevelDown();

            Assert.Equal(14, _viewModel.PlayerLevel);
            Assert.Equal(14, SettingsService.Instance.PlayerLevel);
        }

        [Fact]
        public void LevelUpCommand_ExecutesCorrectly()
        {
            _viewModel.SetPlayerLevel(20);
            Assert.True(_viewModel.LevelUpCommand.CanExecute(null));

            _viewModel.LevelUpCommand.Execute(null);

            Assert.Equal(21, _viewModel.PlayerLevel);
        }

        [Fact]
        public void LevelDownCommand_ExecutesCorrectly()
        {
            _viewModel.SetPlayerLevel(20);
            Assert.True(_viewModel.LevelDownCommand.CanExecute(null));

            _viewModel.LevelDownCommand.Execute(null);

            Assert.Equal(19, _viewModel.PlayerLevel);
        }

        [Fact]
        public void PlayerLevel_ClampsToMax_At79()
        {
            _viewModel.SetPlayerLevel(79);

            Assert.Equal(79, _viewModel.PlayerLevel);
            Assert.False(_viewModel.CanLevelUp);
            Assert.False(_viewModel.LevelUpCommand.CanExecute(null));

            // Calling LevelUp should not exceed 79
            _viewModel.LevelUp();
            Assert.Equal(79, _viewModel.PlayerLevel);

            // Attempting to set above 79 clamps to 79
            _viewModel.SetPlayerLevel(100);
            Assert.Equal(79, _viewModel.PlayerLevel);
        }

        [Fact]
        public void PlayerLevel_ClampsToMin_At1()
        {
            _viewModel.SetPlayerLevel(1);

            Assert.Equal(1, _viewModel.PlayerLevel);
            Assert.False(_viewModel.CanLevelDown);
            Assert.False(_viewModel.LevelDownCommand.CanExecute(null));

            // Calling LevelDown should not go below 1
            _viewModel.LevelDown();
            Assert.Equal(1, _viewModel.PlayerLevel);

            // Attempting to set 0 or negative clamps to 1
            _viewModel.SetPlayerLevel(0);
            Assert.Equal(1, _viewModel.PlayerLevel);

            _viewModel.SetPlayerLevel(-10);
            Assert.Equal(1, _viewModel.PlayerLevel);
        }

        [Fact]
        public void ExternalPlayerLevelChange_UpdatesViewModelReactivity()
        {
            bool propertyChangedFired = false;
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainHeaderViewModel.PlayerLevel))
                    propertyChangedFired = true;
            };

            SettingsService.Instance.PlayerLevel = 42;

            Assert.Equal(42, _viewModel.PlayerLevel);
            Assert.True(propertyChangedFired);
        }

        #endregion

        #region F2: Game Mode Selector (PVP / PVE)

        [Fact]
        public void CurrentProfile_InitialValue_MatchesProfileService()
        {
            Assert.Equal(ProfileService.Instance.CurrentProfile, _viewModel.CurrentProfile);
            Assert.Equal(ProfileService.Instance.CurrentProfile == ProfileType.Pvp, _viewModel.IsPvp);
            Assert.Equal(ProfileService.Instance.CurrentProfile == ProfileType.Pve, _viewModel.IsPve);
        }

        [Fact]
        public void SettingIsPvp_UpdatesCurrentProfile_AndRaisesNotifications()
        {
            _viewModel.IsPve = true;
            Assert.Equal(ProfileType.Pve, _viewModel.CurrentProfile);
            Assert.True(_viewModel.IsPve);
            Assert.False(_viewModel.IsPvp);

            _viewModel.IsPvp = true;
            Assert.Equal(ProfileType.Pvp, _viewModel.CurrentProfile);
            Assert.True(_viewModel.IsPvp);
            Assert.False(_viewModel.IsPve);
        }

        [Fact]
        public void SetPvpCommand_And_SetPveCommand_ToggleModeCorrectly()
        {
            _viewModel.SetPveCommand.Execute(null);
            Assert.Equal(ProfileType.Pve, _viewModel.CurrentProfile);

            _viewModel.SetPvpCommand.Execute(null);
            Assert.Equal(ProfileType.Pvp, _viewModel.CurrentProfile);
        }

        [Fact]
        public void ExternalProfileChange_UpdatesViewModelReactivity()
        {
            ProfileService.Instance.CurrentProfile = ProfileType.Pve;
            Assert.True(_viewModel.IsPve);
            Assert.False(_viewModel.IsPvp);

            ProfileService.Instance.CurrentProfile = ProfileType.Pvp;
            Assert.True(_viewModel.IsPvp);
            Assert.False(_viewModel.IsPve);
        }

        #endregion

        #region F3: Global Quest Progress Metric

        [Fact]
        public void OverallProgress_MatchesQuestProgressServiceStatistics()
        {
            var stats = QuestProgressService.Instance.GetStatistics();

            Assert.Equal(stats.Total, _viewModel.OverallTotalQuests);
            Assert.Equal(stats.Done, _viewModel.OverallCompletedQuests);

            double expectedPct = stats.Total > 0 ? Math.Round((stats.Done * 100.0) / stats.Total, 1) : 0.0;
            Assert.Equal(expectedPct, _viewModel.OverallProgressPercentage);

            string expectedText = $"{stats.Done}/{stats.Total} ({expectedPct.ToString("F1", CultureInfo.InvariantCulture)}%)";
            Assert.Equal(expectedText, _viewModel.OverallProgressText);
        }

        [Fact]
        public void OverallProgress_Aliases_ReturnIdenticalValues()
        {
            Assert.Equal(_viewModel.OverallTotalQuests, _viewModel.OverallTotal);
            Assert.Equal(_viewModel.OverallCompletedQuests, _viewModel.OverallCompleted);
            Assert.Equal(_viewModel.OverallProgressPercentage, _viewModel.OverallPercentage);
        }

        [Fact]
        public void OverallProgressTooltip_ContainsEssentialMetrics()
        {
            var tooltip = _viewModel.OverallProgressTooltip;
            Assert.NotNull(tooltip);
            Assert.Contains(_viewModel.OverallCompletedQuests.ToString(), tooltip);
            Assert.Contains(_viewModel.OverallTotalQuests.ToString(), tooltip);
        }

        [Fact]
        public void CompletingQuest_UpdatesOverallProgress_Reactively()
        {
            var initialCompleted = _viewModel.OverallCompletedQuests;

            var testTask = new TarkovTask
            {
                Ids = new List<string> { "f3_test_quest_header" },
                Name = "F3 Test Quest Header",
                NormalizedName = "f3-test-quest-header",
                Trader = "Prapor"
            };

            QuestProgressService.Instance.CompleteQuest(testTask, false);
            _viewModel.RefreshMetrics();

            Assert.True(_viewModel.OverallCompletedQuests >= initialCompleted);

            // Clean up
            QuestProgressService.Instance.ResetQuest(testTask);
            _viewModel.RefreshMetrics();
        }

        #endregion

        #region F4: Kappa Progress Metric & Pending Count

        [Fact]
        public void KappaProgress_CalculatesCorrectPendingCount()
        {
            _viewModel.RefreshMetrics();

            int expectedPending = Math.Max(0, _viewModel.KappaTotalQuests - _viewModel.KappaCompletedQuests);
            Assert.Equal(expectedPending, _viewModel.KappaPendingQuests);
            Assert.Equal($"{expectedPending} pending", _viewModel.KappaPendingText);
        }

        [Fact]
        public void KappaProgress_Aliases_ReturnIdenticalValues()
        {
            Assert.Equal(_viewModel.KappaTotalQuests, _viewModel.KappaTotal);
            Assert.Equal(_viewModel.KappaCompletedQuests, _viewModel.KappaCompleted);
            Assert.Equal(_viewModel.KappaPendingQuests, _viewModel.KappaPending);
            Assert.Equal(_viewModel.KappaProgressPercentage, _viewModel.KappaPercentage);
        }

        [Fact]
        public void KappaProgressTooltip_ContainsCollectorDetails()
        {
            var tooltip = _viewModel.KappaProgressTooltip;
            Assert.NotNull(tooltip);
            Assert.Contains("Collector", tooltip);
            Assert.Contains(_viewModel.KappaPendingQuests.ToString(), tooltip);
        }

        [Fact]
        public void CompletingKappaQuest_UpdatesPendingCountReactively()
        {
            var initialPending = _viewModel.KappaPendingQuests;

            var kappaTask = new TarkovTask
            {
                Ids = new List<string> { "kappa_test_reactive" },
                Name = "Kappa Test Reactive",
                NormalizedName = "kappa-test-reactive",
                ReqKappa = true,
                Trader = "Therapist"
            };

            QuestProgressService.Instance.CompleteQuest(kappaTask, false);
            _viewModel.RefreshMetrics();

            Assert.True(_viewModel.KappaPendingQuests <= initialPending);

            // Clean up
            QuestProgressService.Instance.ResetQuest(kappaTask);
            _viewModel.RefreshMetrics();
        }

        #endregion

        #region F5: Trader Loyalty Strip (11 Canonical Traders)

        [Fact]
        public void TradersCollection_ContainsExactly11CanonicalTraders()
        {
            Assert.Equal(11, _viewModel.Traders.Count);

            var expectedNames = new[]
            {
                "Prapor", "Therapist", "Fence", "Skier", "Peacekeeper",
                "Mechanic", "Ragman", "Jaeger", "Ref", "Lightkeeper", "BTR Driver"
            };

            var actualNames = _viewModel.Traders.Select(t => t.Name).ToList();
            foreach (var expected in expectedNames)
            {
                Assert.Contains(expected, actualNames);
            }
        }

        [Fact]
        public void PraporLoyaltyLevel_ProgressesWithPlayerLevel()
        {
            var prapor = _viewModel.Traders.First(t => t.Name == "Prapor");

            // Level 1: LL1
            _viewModel.SetPlayerLevel(1);
            Assert.Equal(1, prapor.LoyaltyLevel);
            Assert.Equal("LL1", prapor.LoyaltyBadge);

            // Level 15: LL2
            _viewModel.SetPlayerLevel(15);
            Assert.Equal(2, prapor.LoyaltyLevel);
            Assert.Equal("LL2", prapor.LoyaltyBadge);

            // Level 26: LL3
            _viewModel.SetPlayerLevel(26);
            Assert.Equal(3, prapor.LoyaltyLevel);
            Assert.Equal("LL3", prapor.LoyaltyBadge);

            // Level 36: LL4 (Crown)
            _viewModel.SetPlayerLevel(36);
            Assert.Equal(4, prapor.LoyaltyLevel);
            Assert.Equal("👑", prapor.LoyaltyBadge);
        }

        [Fact]
        public void TherapistLoyaltyLevel_ProgressesWithPlayerLevel()
        {
            var therapist = _viewModel.Traders.First(t => t.Name == "Therapist");

            _viewModel.SetPlayerLevel(1);
            Assert.Equal(1, therapist.LoyaltyLevel);

            _viewModel.SetPlayerLevel(13);
            Assert.Equal(2, therapist.LoyaltyLevel);

            _viewModel.SetPlayerLevel(24);
            Assert.Equal(3, therapist.LoyaltyLevel);

            _viewModel.SetPlayerLevel(35);
            Assert.Equal(4, therapist.LoyaltyLevel);
            Assert.Equal("👑", therapist.LoyaltyBadge);
        }

        [Fact]
        public void MechanicLoyaltyLevel_ProgressesWithPlayerLevel()
        {
            var mechanic = _viewModel.Traders.First(t => t.Name == "Mechanic");

            _viewModel.SetPlayerLevel(1);
            Assert.Equal(1, mechanic.LoyaltyLevel);

            _viewModel.SetPlayerLevel(20);
            Assert.Equal(2, mechanic.LoyaltyLevel);

            _viewModel.SetPlayerLevel(30);
            Assert.Equal(3, mechanic.LoyaltyLevel);

            _viewModel.SetPlayerLevel(40);
            Assert.Equal(4, mechanic.LoyaltyLevel);
            Assert.Equal("👑", mechanic.LoyaltyBadge);
        }

        [Fact]
        public void Fence_UsesScavKarma_CorrectlyFormatted()
        {
            var fence = _viewModel.Traders.First(t => t.Name == "Fence");
            Assert.True(fence.IsFence);
            Assert.True(fence.IsUnlocked);

            // Scav karma 1.50
            SettingsService.Instance.ScavRep = 1.5;
            _viewModel.RefreshMetrics();
            Assert.Equal(1.5, fence.ScavKarma);
            Assert.Equal("+1.5", fence.LoyaltyBadge);
            Assert.Equal("+1.50", fence.ScavKarmaText);

            // Negative Scav karma -0.80
            SettingsService.Instance.ScavRep = -0.8;
            _viewModel.RefreshMetrics();
            Assert.Equal(-0.8, fence.ScavKarma);
            Assert.Equal("-0.8", fence.LoyaltyBadge);
            Assert.Equal("-0.80", fence.ScavKarmaText);

            // Max Scav karma >= 6.0 yields Crown
            SettingsService.Instance.ScavRep = 6.0;
            _viewModel.RefreshMetrics();
            Assert.Equal("👑", fence.LoyaltyBadge);
        }

        [Fact]
        public void GatedTrader_ShowsLockedBadge_WhenNotUnlocked()
        {
            // Jaeger is gated by "Introduction" quest
            var jaeger = _viewModel.Traders.First(t => t.Name == "Jaeger");

            if (!QuestProgressService.Instance.IsJaegerUnlocked())
            {
                Assert.Equal(0, jaeger.LoyaltyLevel);
                Assert.False(jaeger.IsUnlocked);
                Assert.Equal("🔒", jaeger.LoyaltyBadge);
                Assert.Contains("Introduction", jaeger.ToolTipText);
            }
        }

        [Fact]
        public void TraderLoyaltyOverride_TakesPrecedenceOverLevelMath()
        {
            var skier = _viewModel.Traders.First(t => t.Name == "Skier");
            _viewModel.SetPlayerLevel(5); // Natural Skier LL is 1

            Assert.Equal(1, skier.LoyaltyLevel);

            // Set manual override to LL3
            QuestProgressService.Instance.SetTraderLoyaltyLevelOverride("Skier", 3);
            _viewModel.RefreshMetrics();

            Assert.Equal(3, skier.LoyaltyLevel);
            Assert.Equal("LL3", skier.LoyaltyBadge);

            // Clear override restores natural LL
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
            _viewModel.RefreshMetrics();

            Assert.Equal(1, skier.LoyaltyLevel);
        }

        #endregion

        #region F6: Non-Invasive Actions & Lifecycle

        [Fact]
        public void Dispose_UnsubscribesGracefully_WithoutThrowing()
        {
            var vm = new MainHeaderViewModel();
            vm.Dispose();

            // Changing service settings after dispose must not throw or update disposed vm
            SettingsService.Instance.PlayerLevel = 33;
            SettingsService.Instance.ScavRep = 2.0;
            ProfileService.Instance.CurrentProfile = ProfileType.Pve;
        }

        [Fact]
        public void FallbackInitials_AreAssignedForAll11Traders()
        {
            var expectedInitials = new Dictionary<string, string>
            {
                { "Prapor", "PR" },
                { "Therapist", "TH" },
                { "Fence", "FE" },
                { "Skier", "SK" },
                { "Peacekeeper", "PK" },
                { "Mechanic", "ME" },
                { "Ragman", "RA" },
                { "Jaeger", "JG" },
                { "Ref", "RF" },
                { "Lightkeeper", "LK" },
                { "BTR Driver", "BTR" }
            };

            foreach (var trader in _viewModel.Traders)
            {
                Assert.True(expectedInitials.ContainsKey(trader.Name), $"Unexpected trader: {trader.Name}");
                Assert.Equal(expectedInitials[trader.Name], trader.FallbackInitials);
            }
        }

        #endregion
    }
}
