using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TarkovHelper.Models;
using TarkovHelper.Services;
using Xunit;

namespace TarkovHelper.Tests
{
    [Collection("DatabaseUpdateServiceCollection")]
    public class ChallengerM2EmpiricalTests : IDisposable
    {
        private readonly int _originalPlayerLevel;
        private readonly ProfileType _originalProfile;

        public ChallengerM2EmpiricalTests()
        {
            _originalPlayerLevel = SettingsService.Instance.PlayerLevel;
            _originalProfile = ProfileService.Instance.CurrentProfile;
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
        }

        public void Dispose()
        {
            SettingsService.Instance.PlayerLevel = _originalPlayerLevel;
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
            QuestProgressService.Instance.LoadProgressAsync().GetAwaiter().GetResult();
        }

        [Fact]
        public async Task EmpiricalVerification_PeacekeeperLL2_UnlocksWeaponsCirculation_AndLL1_LocksIt()
        {
            var service = QuestProgressService.Instance;
            SettingsService.Instance.PlayerLevel = 10; // Natural Peacekeeper LL is 1 at lvl 10

            var weaponsCirculation = new TarkovTask
            {
                Ids = new List<string> { "wc_empirical_test_id" },
                Name = "Weapons Circulation",
                NormalizedName = "weapons-circulation",
                Trader = "Peacekeeper",
                RequiredLevel = 10,
                TraderRequirements = new List<QuestTraderRequirement>
                {
                    new()
                    {
                        TraderName = "Peacekeeper",
                        TraderId = "5935c25fb3acc3127c3d8cd9",
                        RequiredLevel = 2,
                        CompareMethod = ">="
                    }
                }
            };

            await service.InitializeAsync(new List<TarkovTask> { weaponsCirculation });

            // 1. Initial State: LL is 1 (< 2), so task must be Locked
            var statusBefore = service.GetStatus(weaponsCirculation);
            Assert.Equal(QuestStatus.Locked, statusBefore);
            Assert.False(service.IsTraderRequirementMet(weaponsCirculation));

            // Track ProgressChanged notification
            int progressEventCount = 0;
            EventHandler handler = (s, e) => progressEventCount++;
            service.ProgressChanged += handler;

            try
            {
                // 2. Set Peacekeeper LL override to 2
                service.SetTraderLoyaltyLevelOverride("Peacekeeper", 2);

                // Verify event fired immediately
                Assert.True(progressEventCount > 0, "SetTraderLoyaltyLevelOverride must trigger ProgressChanged immediately.");
                Assert.Equal(2, service.GetTraderLoyaltyLevel("Peacekeeper"));
                Assert.True(service.IsTraderRequirementMet(weaponsCirculation));

                // Verify evaluated status changes immediately to Active
                var statusAfterLL2 = service.GetStatus(weaponsCirculation);
                Assert.Equal(QuestStatus.Active, statusAfterLL2);

                // 3. Set Peacekeeper LL override back to 1
                int eventCountBeforeLock = progressEventCount;
                service.SetTraderLoyaltyLevelOverride("Peacekeeper", 1);

                Assert.True(progressEventCount > eventCountBeforeLock, "Setting back to LL1 must trigger ProgressChanged.");
                Assert.Equal(1, service.GetTraderLoyaltyLevel("Peacekeeper"));
                Assert.False(service.IsTraderRequirementMet(weaponsCirculation));

                // Verify evaluated status returns immediately to Locked
                var statusAfterLL1 = service.GetStatus(weaponsCirculation);
                Assert.Equal(QuestStatus.Locked, statusAfterLL1);
            }
            finally
            {
                service.ProgressChanged -= handler;
                service.ClearTraderLoyaltyLevelOverrides();
            }
        }

        [Fact]
        public async Task EmpiricalVerification_PersistenceInUserDataDb_AcrossReload()
        {
            var service = QuestProgressService.Instance;
            var currentProfile = ProfileService.Instance.CurrentProfile;

            // 1. Set overrides
            service.SetTraderLoyaltyLevelOverride("Peacekeeper", 2);
            service.SetTraderLoyaltyLevelOverride("Mechanic", 4);

            // 2. Check direct database value in UserSettings table
            var pkDbValue = UserDataDbService.Instance.GetSetting("trader.ll.peacekeeper", currentProfile);
            var mechDbValue = UserDataDbService.Instance.GetSetting("trader.ll.mechanic", currentProfile);

            Assert.Equal("2", pkDbValue);
            Assert.Equal("4", mechDbValue);

            // 3. Simulate reload
            service.ClearProgressForTesting();
            Assert.Equal(0, service.GetTraderLoyaltyLevelOverride("Peacekeeper") ?? 0);

            await service.LoadProgressAsync();

            // 4. Verify reloaded from DB
            Assert.Equal(2, service.GetTraderLoyaltyLevel("Peacekeeper"));
            Assert.Equal(4, service.GetTraderLoyaltyLevel("Mechanic"));

            // 5. Clean up and verify deleted from DB
            service.ClearTraderLoyaltyLevelOverrides();
            Assert.Null(UserDataDbService.Instance.GetSetting("trader.ll.peacekeeper", currentProfile));
            Assert.Null(UserDataDbService.Instance.GetSetting("trader.ll.mechanic", currentProfile));
        }

        [Fact]
        public async Task EmpiricalVerification_WeaponsCirculation_RealRequirementsGating()
        {
            var service = QuestProgressService.Instance;
            SettingsService.Instance.PlayerLevel = 25; // Player level satisfies MinLevel (14)

            var prereqTask = new TarkovTask
            {
                Ids = new List<string> { "prereq_the_cult_id" },
                Name = "The Cult",
                NormalizedName = "the-cult",
                Trader = "Peacekeeper"
            };

            var poolTasks = new List<TarkovTask>();
            var poolTaskIds = new List<string>();
            for (int i = 1; i <= 5; i++)
            {
                var ptId = $"pk_pool_task_{i}";
                poolTaskIds.Add(ptId);
                poolTasks.Add(new TarkovTask
                {
                    Ids = new List<string> { ptId },
                    Name = $"PK Pool Task {i}",
                    NormalizedName = $"pk-pool-task-{i}",
                    Trader = "Peacekeeper"
                });
            }

            var weaponsCirculation = new TarkovTask
            {
                Ids = new List<string> { "wc_full_test_id" },
                Name = "Weapons Circulation",
                NormalizedName = "weapons-circulation",
                Trader = "Peacekeeper",
                RequiredLevel = 14,
                Previous = new List<string> { "the-cult" },
                TraderRequirements = new List<QuestTraderRequirement>
                {
                    new()
                    {
                        TraderName = "Peacekeeper",
                        TraderId = "5935c25fb3acc3127c3d8cd9",
                        RequiredLevel = 2,
                        CompareMethod = ">="
                    }
                },
                PoolRequirements = new List<QuestPoolRequirement>
                {
                    new()
                    {
                        PoolTrader = "Peacekeeper",
                        PoolTraderLL = 2,
                        RequiredCompletedCount = 5,
                        TaskIds = poolTaskIds,
                        Description = "Complete at least 5 Peacekeeper LL2 quests"
                    }
                }
            };

            var allTasks = new List<TarkovTask> { prereqTask, weaponsCirculation };
            allTasks.AddRange(poolTasks);

            await service.InitializeAsync(allTasks);
            service.ClearProgressForTesting();

            // Prerequisite 'The Cult' completed
            service.SetProgressForTesting("the-cult", QuestStatus.Done);

            // 5 pool tasks completed
            foreach (var pt in poolTasks)
            {
                if (pt.NormalizedName != null)
                {
                    service.SetProgressForTesting(pt.NormalizedName, QuestStatus.Done);
                }
            }

            // At this point, Prereqs are met, Pool is met, PlayerLevel is met.
            // ONLY Peacekeeper LL is the gating factor!
            // When PK LL is 1:
            service.SetTraderLoyaltyLevelOverride("Peacekeeper", 1);
            Assert.False(service.IsTraderRequirementMet(weaponsCirculation));
            Assert.Equal(QuestStatus.Locked, service.GetStatus(weaponsCirculation));

            // When PK LL is overridden to 2:
            service.SetTraderLoyaltyLevelOverride("Peacekeeper", 2);
            Assert.True(service.IsTraderRequirementMet(weaponsCirculation));
            Assert.Equal(QuestStatus.Active, service.GetStatus(weaponsCirculation));

            // When PK LL is overridden to 1 again:
            service.SetTraderLoyaltyLevelOverride("Peacekeeper", 1);
            Assert.False(service.IsTraderRequirementMet(weaponsCirculation));
            Assert.Equal(QuestStatus.Locked, service.GetStatus(weaponsCirculation));
        }

        [Fact]
        public void EmpiricalVerification_CanonicalId_And_AliasResolution()
        {
            var service = QuestProgressService.Instance;
            service.ClearTraderLoyaltyLevelOverrides();

            // 1. Setting by canonical ID resolves when queried by friendly name or canonical ID
            service.SetTraderLoyaltyLevelOverride("5935c25fb3acc3127c3d8cd9", 2);
            Assert.Equal(2, service.GetTraderLoyaltyLevel("Peacekeeper"));
            Assert.Equal(2, service.GetTraderLoyaltyLevel("5935c25fb3acc3127c3d8cd9"));
            Assert.Equal(2, service.GetTraderLoyaltyLevel("peacekeeper"));

            // 2. Setting by friendly name resolves when queried by canonical ID if cleared or uniform
            service.ClearTraderLoyaltyLevelOverrides();
            service.SetTraderLoyaltyLevelOverride("peacekeeper", 3);
            Assert.Equal(3, service.GetTraderLoyaltyLevel("Peacekeeper"));
            Assert.Equal(3, service.GetTraderLoyaltyLevel("5935c25fb3acc3127c3d8cd9"));

            // 3. Synchronized Aliases:
            // When SetTraderLoyaltyLevelOverride is called with canonical ID and later updated with friendly name,
            // canonical BSG IDs and friendly name aliases are kept strictly synchronized.
            service.SetTraderLoyaltyLevelOverride("5935c25fb3acc3127c3d8cd9", 2);
            service.SetTraderLoyaltyLevelOverride("Peacekeeper", 4);
            // Friendly query returns updated 4
            Assert.Equal(4, service.GetTraderLoyaltyLevel("Peacekeeper"));
            // Raw ID query also returns updated 4 because aliases are synchronized
            Assert.Equal(4, service.GetTraderLoyaltyLevel("5935c25fb3acc3127c3d8cd9"));
        }

        [Fact]
        public void EmpiricalVerification_EdgeCases_NullAndUnknownTraders()
        {
            var service = QuestProgressService.Instance;

            // Null or whitespace trader should not throw
            service.SetTraderLoyaltyLevelOverride(null!, 2);
            service.SetTraderLoyaltyLevelOverride("   ", 2);
            Assert.Null(service.GetTraderLoyaltyLevelOverride(null!));
            Assert.Null(service.GetTraderLoyaltyLevelOverride("   "));

            // Unknown trader defaults to LL 4
            Assert.Equal(4, service.GetTraderLoyaltyLevel("UnknownTraderXYZ"));
        }
    }
}
