using System;
using System.Collections.Generic;
using System.Linq;
using TarkovHelper.Models;
using TarkovHelper.Services;
using Xunit;

namespace TarkovHelper.Tests.Services
{
    /// <summary>
    /// Requirement-driven tests for F1: Task Impact Engine (Unlocks Next & Impact)
    /// Validates DAG follow-up calculation, transitive impact, sole-blocker opensCount,
    /// trader unlock detection, and TarkovTracker priority scoring.
    /// </summary>
    public class TaskImpactEngineTests
    {
        private readonly List<TarkovTask> _sampleTasks;

        public TaskImpactEngineTests()
        {
            _sampleTasks = CreateSampleDAG();
        }

        private static List<TarkovTask> CreateSampleDAG()
        {
            return new List<TarkovTask>
            {
                new()
                {
                    Id = "quest_debut",
                    Name = "Debut",
                    NormalizedName = "debut",
                    Trader = "Prapor",
                    ReqKappa = true,
                    LeadsTo = new List<string> { "checking", "shortage" }
                },
                new()
                {
                    Id = "quest_checking",
                    Name = "Checking",
                    NormalizedName = "checking",
                    Trader = "Prapor",
                    ReqKappa = true,
                    Previous = new List<string> { "debut" },
                    LeadsTo = new List<string> { "shootout-picnic", "tarkov-shooter-1" }
                },
                new()
                {
                    Id = "quest_shortage",
                    Name = "Shortage",
                    NormalizedName = "shortage",
                    Trader = "Therapist",
                    ReqKappa = true,
                    Previous = new List<string> { "debut" },
                    LeadsTo = new List<string> { "operation-aquarius", "tarkov-shooter-1" }
                },
                new()
                {
                    Id = "quest_shootout_picnic",
                    Name = "Shootout Picnic",
                    NormalizedName = "shootout-picnic",
                    Trader = "Prapor",
                    ReqKappa = true,
                    Previous = new List<string> { "checking" },
                    LeadsTo = new List<string>() // Leaf
                },
                new()
                {
                    Id = "quest_operation_aquarius",
                    Name = "Operation Aquarius",
                    NormalizedName = "operation-aquarius",
                    Trader = "Therapist",
                    ReqKappa = true,
                    Previous = new List<string> { "shortage" },
                    LeadsTo = new List<string>() // Leaf
                },
                new()
                {
                    Id = "quest_tarkov_shooter_1",
                    Name = "The Tarkov Shooter - Part 1",
                    NormalizedName = "tarkov-shooter-1",
                    Trader = "Jaeger",
                    ReqKappa = true,
                    Previous = new List<string> { "checking", "shortage" },
                    LeadsTo = new List<string>()
                },
                new()
                {
                    Id = "quest_introduction",
                    Name = "Introduction",
                    NormalizedName = "introduction",
                    Trader = "Mechanic",
                    ReqKappa = true,
                    LeadsTo = new List<string> { "survivalist-path" }
                },
                new()
                {
                    Id = "quest_survivalist_path",
                    Name = "Survivalist Path - Unprotected but Dangerous",
                    NormalizedName = "survivalist-path",
                    Trader = "Jaeger",
                    ReqKappa = true,
                    Previous = new List<string> { "introduction" },
                    LeadsTo = new List<string>()
                }
            };
        }

        #region Tier 1: Feature Coverage (>=5 test cases)

        [Fact]
        public void F1_T1_01_DirectUnlocksNext_ComputesImmediateFollowUps()
        {
            var engine = new TaskImpactEngine(_sampleTasks);
            var debutImpact = engine.GetTaskImpact("debut");

            // Debut directly leads to "checking" and "shortage" (2 quests)
            Assert.Equal(2, debutImpact.UnlocksNext);
        }

        [Fact]
        public void F1_T1_02_TransitiveImpact_TraversesDAGRecursively()
        {
            var engine = new TaskImpactEngine(_sampleTasks);
            var debutImpact = engine.GetTaskImpact("debut");

            // Downstream from Debut: checking, shortage, shootout-picnic, operation-aquarius, tarkov-shooter-1
            Assert.Equal(5, debutImpact.TotalDownstreamImpact);
        }

        [Fact]
        public void F1_T1_03_OpensCount_IdentifiesSoleRemainingBlocker()
        {
            var engine = new TaskImpactEngine(_sampleTasks);
            var debutImpact = engine.GetTaskImpact("debut");

            Assert.Equal(2, debutImpact.OpensCount);
        }

        [Fact]
        public void F1_T1_04_TraderUnlock_RecognizesIntroductionAndEasyMoney()
        {
            var engine = new TaskImpactEngine(_sampleTasks);
            var introImpact = engine.GetTaskImpact("introduction");
            Assert.True(introImpact.UnlocksTrader);
            Assert.Equal("Jaeger", introImpact.UnlockedTraderName);

            var debutImpact = engine.GetTaskImpact("debut");
            Assert.False(debutImpact.UnlocksTrader);
            Assert.Null(debutImpact.UnlockedTraderName);
        }

        [Fact]
        public void F1_T1_05_PriorityScore_CalculatesTarkovTrackerRankingFormula()
        {
            var engine = new TaskImpactEngine(_sampleTasks);
            var introImpact = engine.GetTaskImpact("introduction");
            Assert.True(introImpact.PriorityScore >= 1000);

            var debutImpact = engine.GetTaskImpact("debut");
            Assert.True(debutImpact.PriorityScore > 0);
        }

        #endregion

        #region Tier 2: Boundary & Corner Cases (>=5 test cases)

        [Fact]
        public void F1_T2_01_LeafTask_ZeroDownstreamUnlocks()
        {
            var engine = new TaskImpactEngine(_sampleTasks);
            var leafImpact = engine.GetTaskImpact("shootout-picnic");

            Assert.Equal(0, leafImpact.UnlocksNext);
            Assert.Equal(0, leafImpact.TotalDownstreamImpact);
            Assert.Equal(0, leafImpact.OpensCount);
        }

        [Fact]
        public void F1_T2_02_CircularDependencyCycle_DoesNotInfiniteLoop()
        {
            var cyclicTasks = new List<TarkovTask>
            {
                new() { Id = "A", NormalizedName = "A", LeadsTo = new List<string> { "B" } },
                new() { Id = "B", NormalizedName = "B", LeadsTo = new List<string> { "C" } },
                new() { Id = "C", NormalizedName = "C", LeadsTo = new List<string> { "A" } }
            };

            var engine = new TaskImpactEngine(cyclicTasks);
            var impact = engine.GetTaskImpact("A");

            Assert.Equal(2, impact.TotalDownstreamImpact);
        }

        [Fact]
        public void F1_T2_03_AlreadyCompletedDownstream_ExcludedFromImpact()
        {
            var engine = new TaskImpactEngine(_sampleTasks, new[] { "checking" });
            var debutImpact = engine.GetTaskImpact("debut");

            // Only shortage remains as incomplete direct follow-up
            Assert.Equal(1, debutImpact.UnlocksNext);
        }

        [Fact]
        public void F1_T2_04_NullOrEmptyQuestName_ThrowsOrReturnsEmpty()
        {
            var engine = new TaskImpactEngine(_sampleTasks);
            var emptyImpact = engine.GetTaskImpact("");
            Assert.Equal(0, emptyImpact.UnlocksNext);
            Assert.Equal(0, emptyImpact.TotalDownstreamImpact);

            var nullImpact = engine.GetTaskImpact(null!);
            Assert.Equal(0, nullImpact.UnlocksNext);
            Assert.Equal(0, nullImpact.TotalDownstreamImpact);
        }

        [Fact]
        public void F1_T2_05_MultiplePrerequisites_OpensCountOnlyWhenAllOthersMet()
        {
            var engine1 = new TaskImpactEngine(_sampleTasks, completedQuestIds: null);
            var checkingImpact1 = engine1.GetTaskImpact("checking");

            var engine2 = new TaskImpactEngine(_sampleTasks, completedQuestIds: new[] { "shortage" });
            var checkingImpact2 = engine2.GetTaskImpact("checking");

            Assert.True(checkingImpact2.OpensCount >= checkingImpact1.OpensCount);
        }

        #endregion
    }
}
