using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TarkovHelper.Models;
using TarkovHelper.Services;
using Xunit;
using Xunit.Abstractions;

namespace TarkovHelper.Tests
{
    [Collection("DatabaseUpdateServiceCollection")]
    public class WeaponsCirculationDiagnosticTest
    {
        private readonly ITestOutputHelper _output;

        public WeaponsCirculationDiagnosticTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task DiagnoseWeaponsCirculationStatusWithRealData()
        {
            // 1. Load quests from TarkovHelper/Assets/tarkov_data.db
            await QuestDbService.Instance.LoadQuestsAsync();
            var allQuests = QuestDbService.Instance.AllQuests.ToList();
            var wc = allQuests.FirstOrDefault(q => q.Name == "Weapons Circulation");
            Assert.NotNull(wc);

            _output.WriteLine($"WC Id: {wc.Ids?.FirstOrDefault()}, Trader: {wc.Trader}, ReqLevel: {wc.RequiredLevel}");
            _output.WriteLine($"WC PoolReqs count: {wc.PoolRequirements?.Count}");
            if (wc.PoolRequirements != null)
            {
                foreach (var pr in wc.PoolRequirements)
                {
                    _output.WriteLine($"  PoolReq: Trader={pr.PoolTrader}, LL={pr.PoolTraderLL}, Count={pr.RequiredCompletedCount}, TaskIds count={pr.TaskIds?.Count}");
                }
            }

            // 2. Initialize QuestProgressService
            var service = QuestProgressService.Instance;
            await service.InitializeAsync(allQuests);

            // 3. Test with exactly Fishing Gear and The Cult marked as Done (2/5 parity)
            service.ClearProgressForTesting();
            var fishingGear = allQuests.FirstOrDefault(q => q.Name == "Fishing Gear");
            var theCult = allQuests.FirstOrDefault(q => q.Name == "The Cult");
            Assert.NotNull(fishingGear);
            Assert.NotNull(theCult);

            await service.SetStatusAsync(fishingGear, QuestStatus.Done);
            await service.SetStatusAsync(theCult, QuestStatus.Done);

            var status = service.GetStatus(wc);
            var poolMet = service.IsPoolRequirementMet(wc);
            var poolProgress = service.GetPoolProgress(wc);

            _output.WriteLine($"WC Status: {status}");
            _output.WriteLine($"WC IsPoolRequirementMet: {poolMet}");
            _output.WriteLine($"WC PoolProgress: {poolProgress.CompletedCount}/{poolProgress.RequiredCount}");

            // Assert status is Locked and progress is strictly 2/5
            Assert.Equal(QuestStatus.Locked, status);
            Assert.False(poolMet);
            Assert.Equal(2, poolProgress.CompletedCount);
            Assert.Equal(5, poolProgress.RequiredCount);

            // 4. Test with real user_data.db if available
            var realUserDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TarkovHelper", "Config", "user_data.db"
            );
            if (File.Exists(realUserDataPath))
            {
                var testConfigDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_UnitTests", "Config");
                Directory.CreateDirectory(testConfigDir);
                File.Copy(realUserDataPath, Path.Combine(testConfigDir, "user_data.db"), true);

                await service.LoadProgressAsync(ProfileType.Pvp);
                var realProgress = service.GetPoolProgress(wc);
                _output.WriteLine($"Real user_data.db WC Progress: {realProgress.CompletedCount}/{realProgress.RequiredCount}");
                Assert.True(realProgress.CompletedCount >= 0);
                Assert.Equal(5, realProgress.RequiredCount);
                var expectedStatus = realProgress.CompletedCount >= realProgress.RequiredCount ? QuestStatus.Active : QuestStatus.Locked;
                Assert.Equal(expectedStatus, service.GetStatus(wc));
            }
        }
    }
}
