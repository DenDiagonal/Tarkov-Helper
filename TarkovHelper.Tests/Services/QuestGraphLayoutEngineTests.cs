using System;
using System.Collections.Generic;
using System.Linq;
using TarkovHelper.Models;
using TarkovHelper.Services;
using Xunit;

namespace TarkovHelper.Tests.Services
{
    public class QuestGraphLayoutEngineTests
    {
        [Fact]
        public void BuildLayout_ForSpecificTrader_OnlyIncludesTraderTasks()
        {
            var tasks = new List<TarkovTask>
            {
                new TarkovTask { Id = "1", NormalizedName = "debut", Name = "Debut", Trader = "Prapor", Previous = new() },
                new TarkovTask { Id = "2", NormalizedName = "checkin", Name = "Checking", Trader = "Prapor", Previous = new() { "debut" } },
                new TarkovTask { Id = "3", NormalizedName = "salewa", Name = "Shortage", Trader = "Therapist", Previous = new() }
            };

            var result = QuestGraphLayoutEngine.BuildLayout(tasks, "Prapor", _ => QuestStatus.Active);

            Assert.Equal(2, result.TotalQuests);
            Assert.Contains(result.Nodes, n => n.NormalizedName == "debut");
            Assert.Contains(result.Nodes, n => n.NormalizedName == "checkin");
            Assert.DoesNotContain(result.Nodes, n => n.NormalizedName == "salewa");
        }

        [Fact]
        public void BuildLayout_CalculatesTopologicalDepth_ParentsHaveLowerDepthThanChildren()
        {
            var tasks = new List<TarkovTask>
            {
                new TarkovTask { Id = "1", NormalizedName = "q1", Name = "Quest 1", Trader = "Prapor", Previous = new() },
                new TarkovTask { Id = "2", NormalizedName = "q2", Name = "Quest 2", Trader = "Prapor", Previous = new() { "q1" } },
                new TarkovTask { Id = "3", NormalizedName = "q3", Name = "Quest 3", Trader = "Prapor", Previous = new() { "q2" } }
            };

            var result = QuestGraphLayoutEngine.BuildLayout(tasks, "Prapor", _ => QuestStatus.Active);

            var node1 = result.Nodes.First(n => n.NormalizedName == "q1");
            var node2 = result.Nodes.First(n => n.NormalizedName == "q2");
            var node3 = result.Nodes.First(n => n.NormalizedName == "q3");

            Assert.Equal(0, node1.Depth);
            Assert.Equal(1, node2.Depth);
            Assert.Equal(2, node3.Depth);

            Assert.True(node1.X < node2.X);
            Assert.True(node2.X < node3.X);
        }

        [Fact]
        public void BuildLayout_DetectsCrossTraderPrerequisites()
        {
            var tasks = new List<TarkovTask>
            {
                new TarkovTask { Id = "1", NormalizedName = "intro", Name = "Introduction", Trader = "Mechanic", Previous = new() },
                new TarkovTask { Id = "2", NormalizedName = "survivalist", Name = "Survivalist Path", Trader = "Jaeger", Previous = new() { "intro" } }
            };

            var result = QuestGraphLayoutEngine.BuildLayout(tasks, "Jaeger", _ => QuestStatus.Active);

            var jaegerNode = result.Nodes.First(n => n.NormalizedName == "survivalist");
            Assert.True(jaegerNode.HasCrossTraderPrereqs);
            Assert.Single(jaegerNode.CrossTraderPrerequisites);
            Assert.Equal("Mechanic", jaegerNode.CrossTraderPrerequisites[0].Trader);
            Assert.Equal("intro", jaegerNode.CrossTraderPrerequisites[0].NormalizedName);
        }

        [Fact]
        public void BuildLayout_GeneratesSmoothBezierCurvesAndArrowheads()
        {
            var tasks = new List<TarkovTask>
            {
                new TarkovTask { Id = "1", NormalizedName = "q1", Name = "Quest 1", Trader = "Prapor", Previous = new() },
                new TarkovTask { Id = "2", NormalizedName = "q2", Name = "Quest 2", Trader = "Prapor", Previous = new() { "q1" } }
            };

            var result = QuestGraphLayoutEngine.BuildLayout(tasks, "Prapor", _ => QuestStatus.Active);

            Assert.Single(result.Edges);
            var edge = result.Edges[0];
            Assert.Equal("q1", edge.SourceNode.NormalizedName);
            Assert.Equal("q2", edge.TargetNode.NormalizedName);
            Assert.NotNull(edge.Geometry);
        }

        [Fact]
        public void BuildLayout_SearchQueryFadesNonMatchingNodes()
        {
            var tasks = new List<TarkovTask>
            {
                new TarkovTask { Id = "1", NormalizedName = "q1", Name = "Delivery from the Past", Trader = "Prapor", Maps = new() { "Customs" } },
                new TarkovTask { Id = "2", NormalizedName = "q2", Name = "Bad Rep Evidence", Trader = "Prapor", Maps = new() { "Customs" } }
            };

            var result = QuestGraphLayoutEngine.BuildLayout(tasks, "Prapor", _ => QuestStatus.Active, searchQuery: "Delivery");

            var matched = result.Nodes.First(n => n.NormalizedName == "q1");
            var unmatched = result.Nodes.First(n => n.NormalizedName == "q2");

            Assert.True(matched.MatchesSearch);
            Assert.False(unmatched.MatchesSearch);
            Assert.True(matched.CardOpacity > unmatched.CardOpacity);
        }

        [Fact]
        public void BuildLayout_ForKappaMode_IncludesReqKappaTasks()
        {
            var tasks = new List<TarkovTask>
            {
                new TarkovTask { Id = "1", NormalizedName = "q1", Name = "Kappa Task 1", Trader = "Prapor", ReqKappa = true },
                new TarkovTask { Id = "2", NormalizedName = "q2", Name = "Non Kappa Task", Trader = "Prapor", ReqKappa = false }
            };

            var result = QuestGraphLayoutEngine.BuildLayout(tasks, "Kappa", _ => QuestStatus.Active);

            Assert.Contains(result.Nodes, n => n.NormalizedName == "q1");
            Assert.DoesNotContain(result.Nodes, n => n.NormalizedName == "q2");
        }
    }
}
