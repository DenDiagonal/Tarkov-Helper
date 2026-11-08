using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Services;
using TarkovHelper.Services.Map;
using Xunit;

namespace TarkovHelper.Tests
{
    /// <summary>
    /// Comprehensive test suite for modern Escape from Tarkov quest requirements:
    /// - Trader Loyalty Level Gating (with comparison operators and canonical BSG IDs)
    /// - Task Pool Threshold Gating (explicit task list and dynamic trader LL pool counting)
    /// - User Profile Acceptance Scenario: Weapons Circulation (2/5 locked vs 5/5 active)
    /// - Cascading map marker exclusion in QuestObjectiveService
    /// - Reactive event notifications upon player level and progress changes
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class QuestTraderAndPoolRequirementsTests : IDisposable
    {
        private readonly int _originalPlayerLevel;

        public QuestTraderAndPoolRequirementsTests()
        {
            _originalPlayerLevel = SettingsService.Instance.PlayerLevel;
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
            QuestObjectiveDbService.Instance.ClearObjectivesForTesting();
        }

        public void Dispose()
        {
            SettingsService.Instance.PlayerLevel = _originalPlayerLevel;
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
            QuestObjectiveDbService.Instance.ClearObjectivesForTesting();
        }

        #region Helper Factory Methods

        private static TarkovTask CreateTask(
            string id,
            string name,
            string trader = "Prapor",
            int? minLevel = null,
            List<QuestTraderRequirement>? traderRequirements = null,
            List<QuestPoolRequirement>? poolRequirements = null)
        {
            return new TarkovTask
            {
                Ids = new List<string> { id },
                Name = name,
                NormalizedName = id,
                Trader = trader,
                RequiredLevel = minLevel,
                TraderRequirements = traderRequirements ?? new List<QuestTraderRequirement>(),
                PoolRequirements = poolRequirements ?? new List<QuestPoolRequirement>()
            };
        }

        private static TaskObjectiveWithLocation CreateMapObjective(
            string objectiveId,
            string questId,
            string questName,
            string mapName,
            string description,
            double x = 100,
            double y = 200,
            double z = 10)
        {
            return new TaskObjectiveWithLocation
            {
                ObjectiveId = objectiveId,
                QuestId = questId,
                TaskName = questName,
                TaskNormalizedName = questId,
                Description = description,
                Type = "visit",
                Locations = new List<QuestObjectiveLocation>
                {
                    new()
                    {
                        Id = $"{objectiveId}_loc",
                        MapId = mapName,
                        MapName = mapName,
                        MapNormalizedName = mapName.ToLowerInvariant(),
                        X = x,
                        Y = y,
                        Z = z
                    }
                }
            };
        }

        #endregion

        #region 1. Trader Loyalty Level Gating Tests

        [Fact]
        public async Task TraderLL_InsufficientPlayerLevel_EvaluatesLocked()
        {
            var service = new QuestProgressService();

            // Skier LL2 requires level 15 (Skier LL1 is level 1-14)
            var skierQuest = CreateTask(
                "skier_ll2_quest",
                "Beyond the Meat",
                trader: "Skier",
                traderRequirements: new List<QuestTraderRequirement>
                {
                    new()
                    {
                        TraderName = "Skier",
                        RequiredLevel = 2,
                        CompareMethod = ">="
                    }
                });

            await service.InitializeAsync(new List<TarkovTask> { skierQuest });

            // Set player level to 10 (Skier LL is 1 < 2)
            SettingsService.Instance.PlayerLevel = 10;

            var status = service.GetStatus(skierQuest);
            Assert.Equal(QuestStatus.Locked, status);
            Assert.False(service.IsTraderRequirementMet(skierQuest));
        }

        [Fact]
        public async Task TraderLL_SufficientPlayerLevel_EvaluatesActive()
        {
            var service = new QuestProgressService();

            var skierQuest = CreateTask(
                "skier_ll2_quest",
                "Beyond the Meat",
                trader: "Skier",
                traderRequirements: new List<QuestTraderRequirement>
                {
                    new()
                    {
                        TraderName = "Skier",
                        RequiredLevel = 2,
                        CompareMethod = ">="
                    }
                });

            await service.InitializeAsync(new List<TarkovTask> { skierQuest });

            // Set player level to 15 (Skier LL is 2 >= 2)
            SettingsService.Instance.PlayerLevel = 15;

            var status = service.GetStatus(skierQuest);
            Assert.Equal(QuestStatus.Active, status);
            Assert.True(service.IsTraderRequirementMet(skierQuest));
        }

        [Fact]
        public async Task TraderLL_HigherPlayerLevel_EvaluatesActive()
        {
            var service = new QuestProgressService();

            var skierQuest = CreateTask(
                "skier_ll2_quest",
                "Beyond the Meat",
                trader: "Skier",
                traderRequirements: new List<QuestTraderRequirement>
                {
                    new()
                    {
                        TraderName = "Skier",
                        RequiredLevel = 2,
                        CompareMethod = ">="
                    }
                });

            await service.InitializeAsync(new List<TarkovTask> { skierQuest });

            // Set player level to 38 (Skier LL is 4 >= 2)
            SettingsService.Instance.PlayerLevel = 38;

            var status = service.GetStatus(skierQuest);
            Assert.Equal(QuestStatus.Active, status);
            Assert.True(service.IsTraderRequirementMet(skierQuest));
        }

        [Theory]
        [InlineData(">=", 2, 1, false)]
        [InlineData(">=", 2, 2, true)]
        [InlineData(">=", 2, 3, true)]
        [InlineData(">", 2, 2, false)]
        [InlineData(">", 2, 3, true)]
        [InlineData("==", 2, 1, false)]
        [InlineData("==", 2, 2, true)]
        [InlineData("==", 2, 3, false)]
        [InlineData("=", 2, 2, true)]
        [InlineData("!=", 2, 2, false)]
        [InlineData("!=", 2, 3, true)]
        [InlineData("<=", 2, 2, true)]
        [InlineData("<=", 2, 3, false)]
        [InlineData("<", 2, 2, false)]
        [InlineData("<", 2, 1, true)]
        public async Task TraderLL_ComparisonMethods_EvaluatesAccurately(
            string compareMethod,
            int requiredLevel,
            int currentLevelOverride,
            bool expectedMet)
        {
            var service = new QuestProgressService();

            var testQuest = CreateTask(
                "compare_test_quest",
                "Compare Test Quest",
                trader: "Prapor",
                traderRequirements: new List<QuestTraderRequirement>
                {
                    new()
                    {
                        TraderName = "Prapor",
                        RequiredLevel = requiredLevel,
                        CompareMethod = compareMethod
                    }
                });

            await service.InitializeAsync(new List<TarkovTask> { testQuest });

            // Use trader loyalty level override to set exact current level
            service.SetTraderLoyaltyLevelOverride("Prapor", currentLevelOverride);

            bool isMet = service.IsTraderRequirementMet(testQuest);
            Assert.Equal(expectedMet, isMet);

            var status = service.GetStatus(testQuest);
            Assert.Equal(expectedMet ? QuestStatus.Active : QuestStatus.Locked, status);
        }

        [Fact]
        public async Task TraderLL_CanonicalBsgTraderId_ResolvesCorrectly()
        {
            var service = new QuestProgressService();

            // Canonical BSG ID for Skier: 58330581ace78e27b8b10cee
            var bsgSkierQuest = CreateTask(
                "bsg_skier_quest",
                "BSG Skier Quest",
                trader: "Skier",
                traderRequirements: new List<QuestTraderRequirement>
                {
                    new()
                    {
                        TraderId = "58330581ace78e27b8b10cee",
                        TraderName = "58330581ace78e27b8b10cee",
                        RequiredLevel = 3, // Skier LL3 requires level 28
                        CompareMethod = ">="
                    }
                });

            await service.InitializeAsync(new List<TarkovTask> { bsgSkierQuest });

            // Player level 20 -> Skier LL2 -> Insufficient for LL3
            SettingsService.Instance.PlayerLevel = 20;
            Assert.False(service.IsTraderRequirementMet(bsgSkierQuest));
            Assert.Equal(QuestStatus.Locked, service.GetStatus(bsgSkierQuest));

            // Player level 30 -> Skier LL3 -> Meets requirement
            SettingsService.Instance.PlayerLevel = 30;
            Assert.True(service.IsTraderRequirementMet(bsgSkierQuest));
            Assert.Equal(QuestStatus.Active, service.GetStatus(bsgSkierQuest));
        }

        [Fact]
        public async Task TraderLL_TraderOverride_TakesPrecedenceOverPlayerLevel()
        {
            var service = new QuestProgressService();

            var quest = CreateTask(
                "override_test_quest",
                "Override Test",
                trader: "Mechanic",
                traderRequirements: new List<QuestTraderRequirement>
                {
                    new()
                    {
                        TraderName = "Mechanic",
                        RequiredLevel = 3,
                        CompareMethod = ">="
                    }
                });

            await service.InitializeAsync(new List<TarkovTask> { quest });

            // Mechanic LL3 requires level 30 normally. Set level to 10.
            SettingsService.Instance.PlayerLevel = 10;
            Assert.Equal(QuestStatus.Locked, service.GetStatus(quest));

            // Apply direct LL override to 3
            service.SetTraderLoyaltyLevelOverride("Mechanic", 3);
            Assert.Equal(QuestStatus.Active, service.GetStatus(quest));

            // Clear overrides -> reverts to level 10 (Locked)
            service.ClearTraderLoyaltyLevelOverrides();
            Assert.Equal(QuestStatus.Locked, service.GetStatus(quest));
        }

        #endregion

        #region 2. Task Pool Threshold Gating Tests

        [Fact]
        public async Task TaskPool_ExplicitTaskIds_GatedUntilThresholdMet()
        {
            var service = new QuestProgressService();
            var testId = Guid.NewGuid().ToString("N")[..8];

            var poolTask1Id = $"pk_task_1_{testId}";
            var poolTask2Id = $"pk_task_2_{testId}";
            var poolTask3Id = $"pk_task_3_{testId}";
            var poolTask4Id = $"pk_task_4_{testId}";
            var gatedQuestId = $"pk_gated_quest_{testId}";

            var poolTask1 = CreateTask(poolTask1Id, "Peacekeeper Task 1", "Peacekeeper");
            var poolTask2 = CreateTask(poolTask2Id, "Peacekeeper Task 2", "Peacekeeper");
            var poolTask3 = CreateTask(poolTask3Id, "Peacekeeper Task 3", "Peacekeeper");
            var poolTask4 = CreateTask(poolTask4Id, "Peacekeeper Task 4", "Peacekeeper");

            var gatedQuest = CreateTask(
                gatedQuestId,
                "Peacekeeper Capstone Quest",
                trader: "Peacekeeper",
                poolRequirements: new List<QuestPoolRequirement>
                {
                    new()
                    {
                        PoolTrader = "Peacekeeper",
                        RequiredCompletedCount = 3,
                        TaskIds = new List<string> { poolTask1Id, poolTask2Id, poolTask3Id, poolTask4Id }
                    }
                });

            var allTasks = new List<TarkovTask> { poolTask1, poolTask2, poolTask3, poolTask4, gatedQuest };
            await service.InitializeAsync(allTasks);

            // Initially 0 completed -> Locked
            Assert.False(service.IsPoolRequirementMet(gatedQuest));
            Assert.Equal(QuestStatus.Locked, service.GetStatus(gatedQuest));

            // Complete 1 -> still Locked (1 < 3)
            await service.SetStatusAsync(poolTask1, QuestStatus.Done);
            Assert.False(service.IsPoolRequirementMet(gatedQuest));
            Assert.Equal(QuestStatus.Locked, service.GetStatus(gatedQuest));

            // Complete 2 -> still Locked (2 < 3)
            await service.SetStatusAsync(poolTask2, QuestStatus.Done);
            Assert.False(service.IsPoolRequirementMet(gatedQuest));
            Assert.Equal(QuestStatus.Locked, service.GetStatus(gatedQuest));

            // Complete 3 -> threshold met! (3 >= 3)
            await service.SetStatusAsync(poolTask3, QuestStatus.Done);
            Assert.True(service.IsPoolRequirementMet(gatedQuest));
            Assert.Equal(QuestStatus.Active, service.GetStatus(gatedQuest));

            // Complete 4th -> remains Active (4 >= 3)
            await service.SetStatusAsync(poolTask4, QuestStatus.Done);
            Assert.True(service.IsPoolRequirementMet(gatedQuest));
            Assert.Equal(QuestStatus.Active, service.GetStatus(gatedQuest));
        }

        [Fact]
        public async Task TaskPool_DynamicTraderMatching_GatedUntilThresholdMet()
        {
            var service = new QuestProgressService();
            var testId = Guid.NewGuid().ToString("N")[..8];

            var prapor1 = CreateTask($"prapor_1_{testId}", $"Prapor Task 1 {testId}", "Prapor");
            var prapor2 = CreateTask($"prapor_2_{testId}", $"Prapor Task 2 {testId}", "Prapor");
            var prapor3 = CreateTask($"prapor_3_{testId}", $"Prapor Task 3 {testId}", "Prapor");

            var gatedQuest = CreateTask(
                $"prapor_threshold_quest_{testId}",
                $"Prapor Threshold Quest {testId}",
                trader: "Prapor",
                poolRequirements: new List<QuestPoolRequirement>
                {
                    new()
                    {
                        PoolTrader = "Prapor",
                        RequiredCompletedCount = 2,
                        Description = "Complete at least 2 Prapor tasks"
                    }
                });

            var allTasks = new List<TarkovTask> { prapor1, prapor2, prapor3, gatedQuest };
            await service.InitializeAsync(allTasks);

            // 0 completed -> Locked
            Assert.False(service.IsPoolRequirementMet(gatedQuest));
            Assert.Equal(QuestStatus.Locked, service.GetStatus(gatedQuest));

            // Complete 1 -> Locked
            await service.SetStatusAsync(prapor1, QuestStatus.Done);
            Assert.False(service.IsPoolRequirementMet(gatedQuest));
            Assert.Equal(QuestStatus.Locked, service.GetStatus(gatedQuest));

            // Complete 2 -> Active
            await service.SetStatusAsync(prapor2, QuestStatus.Done);
            Assert.True(service.IsPoolRequirementMet(gatedQuest));
            Assert.Equal(QuestStatus.Active, service.GetStatus(gatedQuest));
        }

        [Fact]
        public async Task TaskPool_TargetQuestDoesNotCountItself()
        {
            var service = new QuestProgressService();
            var testId = Guid.NewGuid().ToString("N")[..8];

            var quest = CreateTask(
                $"self_counting_candidate_{testId}",
                $"Self Counting Quest {testId}",
                trader: "Prapor",
                poolRequirements: new List<QuestPoolRequirement>
                {
                    new()
                    {
                        PoolTrader = "Prapor",
                        RequiredCompletedCount = 1
                    }
                });

            await service.InitializeAsync(new List<TarkovTask> { quest });

            // Without other Prapor quests, it cannot satisfy its own requirement
            Assert.False(service.IsPoolRequirementMet(quest));
            Assert.Equal(QuestStatus.Locked, service.GetStatus(quest));
        }

        #endregion

        #region 3. The User Profile Acceptance Scenario: Weapons Circulation

        [Fact]
        public async Task WeaponsCirculation_Scenario1_LockedState_ExcludesMapMarkers()
        {
            var service = new QuestProgressService();
            var testId = Guid.NewGuid().ToString("N")[..8];

            // Set player level 25 (Player meets Peacekeeper LL2, since 25 >= 14)
            SettingsService.Instance.PlayerLevel = 25;
            service.SetTraderLoyaltyLevelOverride("Peacekeeper", 2);

            var pkPool1Id = $"pk_pool_1_{testId}";
            var pkPool2Id = $"pk_pool_2_{testId}";
            var pkPool3Id = $"pk_pool_3_{testId}";
            var pkPool4Id = $"pk_pool_4_{testId}";
            var pkPool5Id = $"pk_pool_5_{testId}";
            var wcId = $"quest_weapons_circulation_{testId}";

            // 5 Peacekeeper LL2 pool tasks
            var pkPool1 = CreateTask(pkPool1Id, $"The Cult - Part 1 {testId}", "Peacekeeper");
            var pkPool2 = CreateTask(pkPool2Id, $"Fishing Gear {testId}", "Peacekeeper");
            var pkPool3 = CreateTask(pkPool3Id, $"Tigavr {testId}", "Peacekeeper");
            var pkPool4 = CreateTask(pkPool4Id, $"Scrap Metal {testId}", "Peacekeeper");
            var pkPool5 = CreateTask(pkPool5Id, $"Eagle Eye {testId}", "Peacekeeper");

            // Independent active quest on Woods for contrast testing
            var shootOutPicnic = CreateTask($"shootout_picnic_{testId}", "Shootout Picnic", "Prapor");

            // Weapons Circulation modeled with real requirements:
            // - Peacekeeper LL >= 2
            // - 5 Peacekeeper pool tasks completed
            var weaponsCirculation = CreateTask(
                wcId,
                "Weapons Circulation",
                trader: "Peacekeeper",
                traderRequirements: new List<QuestTraderRequirement>
                {
                    new()
                    {
                        TraderName = "Peacekeeper",
                        RequiredLevel = 2,
                        CompareMethod = ">="
                    }
                },
                poolRequirements: new List<QuestPoolRequirement>
                {
                    new()
                    {
                        PoolTrader = "Peacekeeper",
                        PoolTraderLL = 2,
                        RequiredCompletedCount = 5,
                        TaskIds = new List<string> { pkPool1Id, pkPool2Id, pkPool3Id, pkPool4Id, pkPool5Id },
                        Description = "Complete at least 5 Peacekeeper LL2 tasks"
                    }
                });

            var allTasks = new List<TarkovTask>
            {
                pkPool1, pkPool2, pkPool3, pkPool4, pkPool5,
                shootOutPicnic,
                weaponsCirculation
            };
            await service.InitializeAsync(allTasks);

            // Objectives for Weapons Circulation (2 on Woods, 1 on Shoreline)
            var wcWoodsObj1 = CreateMapObjective($"wc_woods_1_{testId}", wcId, "Weapons Circulation", "Woods", "Locate first ritual spot on Woods", 150, 250);
            var wcWoodsObj2 = CreateMapObjective($"wc_woods_2_{testId}", wcId, "Weapons Circulation", "Woods", "Locate second ritual spot on Woods", 180, 290);
            var wcShorelineObj = CreateMapObjective($"wc_shoreline_1_{testId}", wcId, "Weapons Circulation", "Shoreline", "Locate ritual spot on Shoreline", 300, 400);

            // Objective for Shootout Picnic (on Woods)
            var picnicWoodsObj = CreateMapObjective($"picnic_woods_1_{testId}", $"shootout_picnic_{testId}", "Shootout Picnic", "Woods", "Eliminate 15 Scavs on Woods", 200, 300);

            var testObjectives = new List<TaskObjectiveWithLocation>
            {
                wcWoodsObj1,
                wcWoodsObj2,
                wcShorelineObj,
                picnicWoodsObj
            };

            // Scenario 1: User has completed only 2 out of 5 Peacekeeper LL2 pool tasks
            await service.SetStatusAsync(pkPool1, QuestStatus.Done);
            await service.SetStatusAsync(pkPool2, QuestStatus.Done);

            // 1. Verify Quest Status is strictly Locked
            var wcStatus = service.GetStatus(weaponsCirculation);
            Assert.Equal(QuestStatus.Locked, wcStatus);
            Assert.True(service.IsTraderRequirementMet(weaponsCirculation)); // LL2 is met
            Assert.False(service.IsPoolRequirementMet(weaponsCirculation));  // 2/5 pool tasks is NOT met

            // 2. Cascading Map Filter on Woods: Must NOT contain Weapons Circulation markers
            var woodsActiveObjectives = QuestObjectiveService.Instance.GetActiveObjectivesForMap("Woods", service, testObjectives);
            Assert.DoesNotContain(woodsActiveObjectives, o => o.QuestId == wcId);
            Assert.DoesNotContain(woodsActiveObjectives, o => o.ObjectiveId == wcWoodsObj1.ObjectiveId);
            Assert.DoesNotContain(woodsActiveObjectives, o => o.ObjectiveId == wcWoodsObj2.ObjectiveId);

            // Shootout Picnic is active, so its objective MUST be present
            Assert.Contains(woodsActiveObjectives, o => o.QuestId == $"shootout_picnic_{testId}");

            // 3. Cascading Map Filter on Shoreline: Must NOT contain Weapons Circulation markers
            var shorelineActiveObjectives = QuestObjectiveService.Instance.GetActiveObjectivesForMap("Shoreline", service, testObjectives);
            Assert.DoesNotContain(shorelineActiveObjectives, o => o.QuestId == wcId);
            Assert.DoesNotContain(shorelineActiveObjectives, o => o.ObjectiveId == wcShorelineObj.ObjectiveId);
            Assert.Empty(shorelineActiveObjectives);
        }

        [Fact]
        public async Task WeaponsCirculation_Scenario2_ActiveTransition_IncludesMapMarkers()
        {
            var service = new QuestProgressService();
            var testId = Guid.NewGuid().ToString("N")[..8];

            SettingsService.Instance.PlayerLevel = 25;
            service.SetTraderLoyaltyLevelOverride("Peacekeeper", 2);

            var pkPool1Id = $"pk_pool_1_{testId}";
            var pkPool2Id = $"pk_pool_2_{testId}";
            var pkPool3Id = $"pk_pool_3_{testId}";
            var pkPool4Id = $"pk_pool_4_{testId}";
            var pkPool5Id = $"pk_pool_5_{testId}";
            var wcId = $"quest_weapons_circulation_{testId}";

            var pkPool1 = CreateTask(pkPool1Id, $"The Cult - Part 1 {testId}", "Peacekeeper");
            var pkPool2 = CreateTask(pkPool2Id, $"Fishing Gear {testId}", "Peacekeeper");
            var pkPool3 = CreateTask(pkPool3Id, $"Tigavr {testId}", "Peacekeeper");
            var pkPool4 = CreateTask(pkPool4Id, $"Scrap Metal {testId}", "Peacekeeper");
            var pkPool5 = CreateTask(pkPool5Id, $"Eagle Eye {testId}", "Peacekeeper");

            var weaponsCirculation = CreateTask(
                wcId,
                "Weapons Circulation",
                trader: "Peacekeeper",
                traderRequirements: new List<QuestTraderRequirement>
                {
                    new()
                    {
                        TraderName = "Peacekeeper",
                        RequiredLevel = 2,
                        CompareMethod = ">="
                    }
                },
                poolRequirements: new List<QuestPoolRequirement>
                {
                    new()
                    {
                        PoolTrader = "Peacekeeper",
                        PoolTraderLL = 2,
                        RequiredCompletedCount = 5,
                        TaskIds = new List<string> { pkPool1Id, pkPool2Id, pkPool3Id, pkPool4Id, pkPool5Id }
                    }
                });

            var allTasks = new List<TarkovTask>
            {
                pkPool1, pkPool2, pkPool3, pkPool4, pkPool5,
                weaponsCirculation
            };
            await service.InitializeAsync(allTasks);

            var wcWoodsObj1 = CreateMapObjective($"wc_woods_1_{testId}", wcId, "Weapons Circulation", "Woods", "Locate first ritual spot on Woods", 150, 250);
            var wcWoodsObj2 = CreateMapObjective($"wc_woods_2_{testId}", wcId, "Weapons Circulation", "Woods", "Locate second ritual spot on Woods", 180, 290);
            var wcShorelineObj = CreateMapObjective($"wc_shoreline_1_{testId}", wcId, "Weapons Circulation", "Shoreline", "Locate ritual spot on Shoreline", 300, 400);

            var testObjectives = new List<TaskObjectiveWithLocation>
            {
                wcWoodsObj1,
                wcWoodsObj2,
                wcShorelineObj
            };

            // Start in Locked State (2/5)
            await service.SetStatusAsync(pkPool1, QuestStatus.Done);
            await service.SetStatusAsync(pkPool2, QuestStatus.Done);
            Assert.Equal(QuestStatus.Locked, service.GetStatus(weaponsCirculation));

            // Complete the remaining 3 tasks, reaching 5/5
            await service.SetStatusAsync(pkPool3, QuestStatus.Done);
            await service.SetStatusAsync(pkPool4, QuestStatus.Done);
            await service.SetStatusAsync(pkPool5, QuestStatus.Done);

            // 1. Verify Quest Status transitions cleanly to Active
            var wcStatus = service.GetStatus(weaponsCirculation);
            Assert.Equal(QuestStatus.Active, wcStatus);
            Assert.True(service.IsPoolRequirementMet(weaponsCirculation));

            // 2. Cascading Map Filter on Woods: Now returns both Woods objectives
            var woodsActiveObjectives = QuestObjectiveService.Instance.GetActiveObjectivesForMap("Woods", service, testObjectives);
            Assert.Equal(2, woodsActiveObjectives.Count);
            Assert.Contains(woodsActiveObjectives, o => o.ObjectiveId == wcWoodsObj1.ObjectiveId);
            Assert.Contains(woodsActiveObjectives, o => o.ObjectiveId == wcWoodsObj2.ObjectiveId);

            // 3. Cascading Map Filter on Shoreline: Now returns Shoreline objective
            var shorelineActiveObjectives = QuestObjectiveService.Instance.GetActiveObjectivesForMap("Shoreline", service, testObjectives);
            Assert.Single(shorelineActiveObjectives);
            Assert.Contains(shorelineActiveObjectives, o => o.ObjectiveId == wcShorelineObj.ObjectiveId);
        }

        [Fact]
        public async Task WeaponsCirculation_EndToEnd_ViaQuestObjectiveDbService()
        {
            var service = new QuestProgressService();
            var testId = Guid.NewGuid().ToString("N")[..8];

            SettingsService.Instance.PlayerLevel = 25;
            service.SetTraderLoyaltyLevelOverride("Peacekeeper", 2);

            var pkPool1Id = $"pk_pool_1_{testId}";
            var pkPool2Id = $"pk_pool_2_{testId}";
            var pkPool3Id = $"pk_pool_3_{testId}";
            var pkPool4Id = $"pk_pool_4_{testId}";
            var pkPool5Id = $"pk_pool_5_{testId}";
            var wcId = $"quest_weapons_circulation_{testId}";

            var pkPool1 = CreateTask(pkPool1Id, $"The Cult - Part 1 {testId}", "Peacekeeper");
            var pkPool2 = CreateTask(pkPool2Id, $"Fishing Gear {testId}", "Peacekeeper");
            var pkPool3 = CreateTask(pkPool3Id, $"Tigavr {testId}", "Peacekeeper");
            var pkPool4 = CreateTask(pkPool4Id, $"Scrap Metal {testId}", "Peacekeeper");
            var pkPool5 = CreateTask(pkPool5Id, $"Eagle Eye {testId}", "Peacekeeper");

            var weaponsCirculation = CreateTask(
                wcId,
                "Weapons Circulation",
                trader: "Peacekeeper",
                traderRequirements: new List<QuestTraderRequirement>
                {
                    new()
                    {
                        TraderName = "Peacekeeper",
                        RequiredLevel = 2,
                        CompareMethod = ">="
                    }
                },
                poolRequirements: new List<QuestPoolRequirement>
                {
                    new()
                    {
                        PoolTrader = "Peacekeeper",
                        PoolTraderLL = 2,
                        RequiredCompletedCount = 5,
                        TaskIds = new List<string> { pkPool1Id, pkPool2Id, pkPool3Id, pkPool4Id, pkPool5Id }
                    }
                });

            var allTasks = new List<TarkovTask>
            {
                pkPool1, pkPool2, pkPool3, pkPool4, pkPool5,
                weaponsCirculation
            };
            await service.InitializeAsync(allTasks);

            // Populate QuestObjectiveDbService directly with test objectives
            var dbObjectives = new List<QuestObjective>
            {
                new()
                {
                    Id = $"db_wc_woods_1_{testId}",
                    QuestId = wcId,
                    QuestName = "Weapons Circulation",
                    MapName = "Woods",
                    Description = "Locate ritual spot on Woods",
                    LocationPointsJson = "[{\"x\":150,\"y\":10,\"z\":250}]"
                },
                new()
                {
                    Id = $"db_wc_shoreline_1_{testId}",
                    QuestId = wcId,
                    QuestName = "Weapons Circulation",
                    MapName = "Shoreline",
                    Description = "Locate ritual spot on Shoreline",
                    LocationPointsJson = "[{\"x\":300,\"y\":10,\"z\":400}]"
                }
            };
            QuestObjectiveDbService.Instance.SetObjectivesForTesting(dbObjectives);

            try
            {
                // Locked state (2/5)
                await service.SetStatusAsync(pkPool1, QuestStatus.Done);
                await service.SetStatusAsync(pkPool2, QuestStatus.Done);

                var woodsBefore = QuestObjectiveService.Instance.GetActiveObjectivesForMap("Woods", service);
                Assert.DoesNotContain(woodsBefore, o => o.QuestId == wcId);

                // Transition to Active (5/5)
                await service.SetStatusAsync(pkPool3, QuestStatus.Done);
                await service.SetStatusAsync(pkPool4, QuestStatus.Done);
                await service.SetStatusAsync(pkPool5, QuestStatus.Done);

                var woodsAfter = QuestObjectiveService.Instance.GetActiveObjectivesForMap("Woods", service);
                Assert.Contains(woodsAfter, o => o.QuestId == wcId);
            }
            finally
            {
                QuestObjectiveDbService.Instance.ClearObjectivesForTesting();
            }
        }

        #endregion

        #region 4. Reactive Notification Tests

        [Fact]
        public void ReactiveNotification_PlayerLevelChanged_FiresProgressChanged()
        {
            var service = QuestProgressService.Instance;
            bool fired = false;
            EventHandler handler = (s, e) => fired = true;

            service.ProgressChanged += handler;
            try
            {
                // Increment player level to trigger event
                SettingsService.Instance.PlayerLevel = SettingsService.Instance.PlayerLevel == 20 ? 21 : 20;
                Assert.True(fired, "Changing SettingsService.PlayerLevel must fire QuestProgressService.ProgressChanged.");
            }
            finally
            {
                service.ProgressChanged -= handler;
            }
        }

        [Fact]
        public void ReactiveNotification_SetTraderLoyaltyLevelOverride_FiresProgressChanged()
        {
            var service = QuestProgressService.Instance;
            bool fired = false;
            EventHandler handler = (s, e) => fired = true;

            service.ProgressChanged += handler;
            try
            {
                service.SetTraderLoyaltyLevelOverride("Skier", 3);
                Assert.True(fired, "SetTraderLoyaltyLevelOverride must fire ProgressChanged.");
            }
            finally
            {
                service.ProgressChanged -= handler;
                service.ClearTraderLoyaltyLevelOverrides();
            }
        }

        [Fact]
        public void ReactiveNotification_ClearTraderLoyaltyLevelOverrides_FiresProgressChanged()
        {
            var service = QuestProgressService.Instance;
            service.SetTraderLoyaltyLevelOverride("Skier", 3);

            bool fired = false;
            EventHandler handler = (s, e) => fired = true;

            service.ProgressChanged += handler;
            try
            {
                service.ClearTraderLoyaltyLevelOverrides();
                Assert.True(fired, "ClearTraderLoyaltyLevelOverrides must fire ProgressChanged.");
            }
            finally
            {
                service.ProgressChanged -= handler;
            }
        }

        [Fact]
        public async Task ReactiveNotification_SetStatusAsync_FiresProgressChanged()
        {
            var service = new QuestProgressService();
            var testId = Guid.NewGuid().ToString("N")[..8];
            var task = CreateTask($"reactive_task_{testId}", "Reactive Task", "Prapor");
            await service.InitializeAsync(new List<TarkovTask> { task });

            bool fired = false;
            EventHandler handler = (s, e) => fired = true;

            service.ProgressChanged += handler;
            try
            {
                await service.SetStatusAsync(task, QuestStatus.Done);
                Assert.True(fired, "SetStatusAsync must fire ProgressChanged.");
            }
            finally
            {
                service.ProgressChanged -= handler;
            }
        }

        [Fact]
        public async Task ReactiveNotification_MapMarkersRefreshOnPlayerLevelChange()
        {
            var service = new QuestProgressService();
            var testId = Guid.NewGuid().ToString("N")[..8];
            var questId = $"skier_map_quest_{testId}";
            var objId = $"skier_customs_obj_{testId}";

            // Skier quest requiring LL2 (level 15)
            var skierQuest = CreateTask(
                questId,
                "Skier Map Quest",
                trader: "Skier",
                traderRequirements: new List<QuestTraderRequirement>
                {
                    new()
                    {
                        TraderName = "Skier",
                        RequiredLevel = 2,
                        CompareMethod = ">="
                    }
                });

            await service.InitializeAsync(new List<TarkovTask> { skierQuest });

            var customsObj = CreateMapObjective(
                objId,
                questId,
                "Skier Map Quest",
                "Customs",
                "Check the dorms for Skier");

            var objectives = new List<TaskObjectiveWithLocation> { customsObj };

            // 1. Initial state: Level 10 (Locked)
            SettingsService.Instance.PlayerLevel = 10;
            var initialMarkers = QuestObjectiveService.Instance.GetActiveObjectivesForMap("Customs", service, objectives);
            Assert.Empty(initialMarkers);

            // 2. Player levels up to 15 (Triggers PlayerLevelChanged -> unlocks LL2)
            SettingsService.Instance.PlayerLevel = 15;

            var updatedMarkers = QuestObjectiveService.Instance.GetActiveObjectivesForMap("Customs", service, objectives);
            Assert.Single(updatedMarkers);
            Assert.Equal(objId, updatedMarkers[0].ObjectiveId);
        }

        #endregion

        #region 5. Defensive Hardening and Resilience Tests

        [Fact]
        public void QuestObjectiveService_NullProgressService_ReturnsEmptyListSafely()
        {
            var result = QuestObjectiveService.Instance.GetActiveObjectivesForMap("Woods", null!);
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void QuestObjectiveService_EmptyMapName_ReturnsEmptyListSafely()
        {
            var service = new QuestProgressService();
            var result = QuestObjectiveService.Instance.GetActiveObjectivesForMap("", service);
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task QuestObjectiveService_ResilientTaskResolution_MatchesByBsgIdAndTaskName()
        {
            var service = new QuestProgressService();

            var taskWithBsgId = new TarkovTask
            {
                Ids = new List<string> { "bsg_id_xyz" },
                Name = "Special Delivery",
                NormalizedName = "special-delivery",
                Trader = "Prapor"
            };

            await service.InitializeAsync(new List<TarkovTask> { taskWithBsgId });

            // Objective referencing only the BSG ID
            var objective = CreateMapObjective(
                "bsg_obj_1",
                "bsg_id_xyz",
                "Special Delivery",
                "Factory",
                "Plant documents in Factory");

            var activeList = QuestObjectiveService.Instance.GetActiveObjectivesForMap(
                "Factory",
                service,
                new List<TaskObjectiveWithLocation> { objective });

            Assert.Single(activeList);
            Assert.Equal("bsg_obj_1", activeList[0].ObjectiveId);
        }

        [Fact]
        public async Task BroadcastPart5_CanonicalObjectives_ContainsAllThreeSteps()
        {
            var questService = QuestDbService.Instance;
            await questService.LoadQuestsAsync();

            var task = questService.GetQuestById("64ee99639878a0569d6ec8c9");
            Assert.NotNull(task);
            Assert.Equal("Broadcast - Part 5", task.Name);
            Assert.NotNull(task.Objectives);
            Assert.Equal(3, task.Objectives.Count);
            Assert.Contains(task.Objectives, o => o.Contains("Priest", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(task.Objectives, o => o.Contains("ritual spot", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(task.Objectives, o => o.Contains("Cultist knife", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task HumanitarianSupplies_Objectives_ContainsRequiredScavKillObjective()
        {
            var questService = QuestDbService.Instance;
            await questService.LoadQuestsAsync();

            var task = questService.GetQuestById("5a27b87686f77460de0252a8");
            Assert.NotNull(task);
            Assert.Equal("Humanitarian Supplies", task.Name);
            Assert.NotNull(task.Objectives);
            Assert.Contains(task.Objectives, o => o.Contains("Scavs", StringComparison.OrdinalIgnoreCase) || o.Contains("uniform", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(task.Objectives, o => o.Contains("first truck", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(task.Objectives, o => o.Contains("second truck", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(task.Objectives, o => o.Contains("MRE", StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
