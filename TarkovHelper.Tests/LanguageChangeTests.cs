using System;
using System.Linq;
using System.Threading.Tasks;
using TarkovHelper.Models;
using TarkovHelper.Services;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests
{
    public class LanguageChangeTests
    {
        [Fact]
        public void LocalizationService_CanSwitchToAllLanguagesWithoutThrowing()
        {
            var loc = LocalizationService.Instance;

            foreach (AppLanguage lang in Enum.GetValues(typeof(AppLanguage)))
            {
                loc.CurrentLanguage = lang;
                Assert.Equal(lang, loc.CurrentLanguage);

                // Check a bunch of core properties
                _ = loc.Welcome;
                _ = loc.Dashboard;
                _ = loc.Storyline;
                _ = loc.Quests;
                _ = loc.NeededItems;
                _ = loc.Hideout;
                _ = loc.Sync;
                _ = loc.Profile;
                _ = loc.Settings;
                _ = loc.Language;
                _ = loc.LanguageSelectionDesc;
                _ = loc.LogFolder;
                _ = loc.LogFolderDesc;
                _ = loc.AutoDetect;
                _ = loc.Browse;
                _ = loc.Reset;
                _ = loc.RestoreDefaults;
                _ = loc.RestoreBackup;
                _ = loc.UpdateApiData;
                _ = loc.CacheManagement;
                _ = loc.CacheDesc;
                _ = loc.ClearCache;
                _ = loc.DeleteAllData;

                // Check trader names
                _ = loc.GetLocalizedTraderName("Prapor");
                _ = loc.GetLocalizedTraderName("Therapist");
                _ = loc.GetLocalizedTraderName("Skier");
                _ = loc.GetLocalizedTraderName("Peacekeeper");
                _ = loc.GetLocalizedTraderName("Mechanic");
                _ = loc.GetLocalizedTraderName("Ragman");
                _ = loc.GetLocalizedTraderName("Jaeger");
                _ = loc.GetLocalizedTraderName("Fence");
                _ = loc.GetLocalizedTraderName("Ref");
                _ = loc.GetLocalizedTraderName("Lightkeeper");
                _ = loc.GetLocalizedTraderName("BTR Driver");
                _ = loc.GetLocalizedTraderName(null);

                // Check map names
                _ = loc.GetLocalizedMapName("customs");
                _ = loc.GetLocalizedMapName("factory");
                _ = loc.GetLocalizedMapName("woods");
                _ = loc.GetLocalizedMapName("shoreline");
                _ = loc.GetLocalizedMapName("reserve");
                _ = loc.GetLocalizedMapName("interchange");
                _ = loc.GetLocalizedMapName("lighthouse");
                _ = loc.GetLocalizedMapName("streetsoftarkov");
                _ = loc.GetLocalizedMapName("groundzero");
                _ = loc.GetLocalizedMapName("the lab");
                _ = loc.GetLocalizedMapName(null);
            }

            // Restore English
            loc.CurrentLanguage = AppLanguage.EN;
        }

        [Fact]
        public void DashboardViewModel_WithKorean_DoesNotThrow()
        {
            var loc = LocalizationService.Instance;
            loc.CurrentLanguage = AppLanguage.KO;

            var vm = new DashboardViewModel();
            _ = vm.TasksCountText;
            _ = vm.HideoutCountText;
            _ = vm.TasksPercentageText;
            _ = vm.HideoutPercentageText;
            _ = vm.KappaPercentageText;

            var mockTasks = new[]
            {
                new TarkovTask { Id = "t1", Name = "Debut", NameKo = "데뷔", Trader = "Prapor", ReqKappa = true },
                new TarkovTask { Id = "t2", Name = "Supplier", NameKo = "공급업자", Trader = "Skier", ReqKappa = false },
                new TarkovTask { Id = "t3", Name = "Gunsmith", NameKo = null, Trader = "Mechanic", ReqKappa = true }
            };

            var ranked = DashboardViewModel.RankUpNextTasks(mockTasks, TaskImpactEngine.Instance, 5);
            Assert.NotEmpty(ranked);
            Assert.Equal("데뷔", ranked.First(r => r.QuestId == "t1").QuestName);

            // Restore English
            loc.CurrentLanguage = AppLanguage.EN;
        }

        [Fact]
        public void RapidCyclicLanguageSwitching_FiresEventsAndDoesNotThrow()
        {
            var loc = LocalizationService.Instance;
            int eventFireCount = 0;
            AppLanguage lastReportedLang = loc.CurrentLanguage;

            void Handler(object? s, AppLanguage l)
            {
                eventFireCount++;
                lastReportedLang = l;
            }

            loc.LanguageChanged += Handler;
            try
            {
                var cycle = new[]
                {
                    AppLanguage.KO,
                    AppLanguage.JA,
                    AppLanguage.ES,
                    AppLanguage.EN,
                    AppLanguage.KO,
                    AppLanguage.ES,
                    AppLanguage.KO
                };

                foreach (var target in cycle)
                {
                    loc.CurrentLanguage = target;
                    Assert.Equal(target, lastReportedLang);
                    Assert.Equal(target, loc.CurrentLanguage);

                    // Korean specific sanity checks
                    if (target == AppLanguage.KO)
                    {
                        Assert.Equal("세관", loc.GetLocalizedMapName("customs"));
                        Assert.Equal("공장", loc.GetLocalizedMapName("factory"));
                        Assert.Equal("프라포", loc.GetLocalizedTraderName("Prapor"));
                    }
                }

                Assert.True(eventFireCount >= cycle.Length);
            }
            finally
            {
                loc.LanguageChanged -= Handler;
                loc.CurrentLanguage = AppLanguage.EN;
            }
        }
    }
}
