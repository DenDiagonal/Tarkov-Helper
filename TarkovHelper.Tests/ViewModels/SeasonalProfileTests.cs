using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TarkovHelper.Models;
using TarkovHelper.Pages;
using TarkovHelper.Services;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests.ViewModels
{
    [Collection("DatabaseUpdateServiceCollection")]
    public class SeasonalProfileTests : IDisposable
    {
        private readonly ProfileType _originalProfile;

        public SeasonalProfileTests()
        {
            _originalProfile = ProfileService.Instance.CurrentProfile;
        }

        public void Dispose()
        {
            ProfileService.Instance.CurrentProfile = _originalProfile;
        }

        [Fact]
        public void ProfileService_GetProfileName_ReturnsCorrectStrings()
        {
            Assert.Equal("PVP", ProfileService.Instance.GetProfileName(ProfileType.Pvp));
            Assert.Equal("PVE", ProfileService.Instance.GetProfileName(ProfileType.Pve));
            Assert.Equal("SEASON", ProfileService.Instance.GetProfileName(ProfileType.Seasonal));
        }

        [Fact]
        public void QuestFilterEngine_ExcludesSeasonalQuests_InPvpAndPveModes()
        {
            var regularTask = new TarkovTask
            {
                Name = "Debut",
                Trader = "Prapor",
                IsSeasonal = false
            };
            var regularVm = new QuestViewModel
            {
                Task = regularTask,
                Status = QuestStatus.Active
            };

            var seasonalTask = new TarkovTask
            {
                Name = "Break the Chain",
                Trader = "Mechanic",
                IsSeasonal = true
            };
            var seasonalVm = new QuestViewModel
            {
                Task = seasonalTask,
                Status = QuestStatus.Active
            };

            var criteria = new QuestFilterCriteria
            {
                StatusFilter = QuickFilterStatus.Active,
                TaskTypeRegular = true
            };

            // In PVP mode: regular is matched, seasonal is hidden
            ProfileService.Instance.CurrentProfile = ProfileType.Pvp;
            Assert.True(QuestFilterEngine.Matches(regularVm, criteria));
            Assert.False(QuestFilterEngine.Matches(seasonalVm, criteria));

            // In PVE mode: regular is matched, seasonal is hidden
            ProfileService.Instance.CurrentProfile = ProfileType.Pve;
            Assert.True(QuestFilterEngine.Matches(regularVm, criteria));
            Assert.False(QuestFilterEngine.Matches(seasonalVm, criteria));

            // In Seasonal mode: both regular and seasonal are matched
            ProfileService.Instance.CurrentProfile = ProfileType.Seasonal;
            Assert.True(QuestFilterEngine.Matches(regularVm, criteria));
            Assert.True(QuestFilterEngine.Matches(seasonalVm, criteria));
        }

        [Fact]
        public void QuestProgressService_SeasonalQuestStatus_UnavailableInPvp_ActiveInSeasonal()
        {
            var seasonalTask = new TarkovTask
            {
                Name = "Break the Chain",
                Trader = "Mechanic",
                IsSeasonal = true
            };

            // In PVP mode
            ProfileService.Instance.CurrentProfile = ProfileType.Pvp;
            var statusPvp = QuestProgressService.Instance.GetStatus(seasonalTask);
            Assert.Equal(QuestStatus.Unavailable, statusPvp);

            // In PVE mode
            ProfileService.Instance.CurrentProfile = ProfileType.Pve;
            var statusPve = QuestProgressService.Instance.GetStatus(seasonalTask);
            Assert.Equal(QuestStatus.Unavailable, statusPve);

            // In Seasonal mode
            ProfileService.Instance.CurrentProfile = ProfileType.Seasonal;
            var statusSeasonal = QuestProgressService.Instance.GetStatus(seasonalTask);
            Assert.Equal(QuestStatus.Active, statusSeasonal);
        }

        [Fact]
        public void QuestViewModel_SeasonalBadgeVisibility_Correct()
        {
            var seasonalTask = new TarkovTask { Name = "Break the Chain", IsSeasonal = true };
            var regularTask = new TarkovTask { Name = "Debut", IsSeasonal = false };

            var seasonalVm = new QuestViewModel { Task = seasonalTask };
            var regularVm = new QuestViewModel { Task = regularTask };

            Assert.True(seasonalVm.IsSeasonal);
            Assert.Equal(System.Windows.Visibility.Visible, seasonalVm.SeasonalBadgeVisibility);

            Assert.False(regularVm.IsSeasonal);
            Assert.Equal(System.Windows.Visibility.Collapsed, regularVm.SeasonalBadgeVisibility);
        }

        [Fact]
        public async Task DatabaseUpdateService_CheckTarkovTrackerUpdates_SucceedsWithoutNullCrash()
        {
            var diffReport = await DatabaseUpdateService.Instance.CheckTarkovTrackerUpdatesAsync(null);
            Assert.NotNull(diffReport);
        }
    }
}
