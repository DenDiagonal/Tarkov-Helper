using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services.DataAudit;
using Xunit;

namespace TarkovHelper.Tests
{
    public class TarkovTrackerExtractorAndNormalizerTests
    {
        [Fact]
        public void GlobalVariableRegistry_ContainsExact27PoolVariables()
        {
            var registry = TarkovTrackerExtractorService.GlobalVariableRegistry;
            Assert.Equal(27, registry.Count);

            // Verify Prapor LL1-4
            Assert.Equal(("Prapor", 1, "54cb50c76803fa8b248b4571"), registry["6a20540cf1b67a977cc5a088"]);
            Assert.Equal(("Prapor", 2, "54cb50c76803fa8b248b4571"), registry["6a2688488bba18e0b0187a04"]);
            Assert.Equal(("Prapor", 3, "54cb50c76803fa8b248b4571"), registry["6a32651a811905ed0cac0973"]);
            Assert.Equal(("Prapor", 4, "54cb50c76803fa8b248b4571"), registry["6a326525789ae12ecb0b2807"]);

            // Verify Therapist LL1-4
            Assert.Equal(("Therapist", 1, "54cb57776803fa99248b456e"), registry["6a4e4ab3ecd1145894d00990"]);
            Assert.Equal(("Therapist", 2, "54cb57776803fa99248b456e"), registry["6a4e4aed3ded7a18126603f6"]);
            Assert.Equal(("Therapist", 3, "54cb57776803fa99248b456e"), registry["6a4e4b28629dc64c4001967c"]);
            Assert.Equal(("Therapist", 4, "54cb57776803fa99248b456e"), registry["6a56925b1c30ba5a77c7c518"]);

            // Verify Skier LL1-4
            Assert.Equal(("Skier", 1, "58330581ace78e27b8b10cee"), registry["6a59f3ba06c8949abad30871"]);
            Assert.Equal(("Skier", 2, "58330581ace78e27b8b10cee"), registry["6a5a111de1f417ac80a163e5"]);
            Assert.Equal(("Skier", 3, "58330581ace78e27b8b10cee"), registry["6a5a115181116e807b55f258"]);
            Assert.Equal(("Skier", 4, "58330581ace78e27b8b10cee"), registry["6a5a1192efde11cc7105b18f"]);

            // Verify Peacekeeper LL1-4
            Assert.Equal(("Peacekeeper", 1, "5935c25fb3acc3127c3d8cd9"), registry["6a5ba40fe5c4eaef5610f232"]);
            Assert.Equal(("Peacekeeper", 2, "5935c25fb3acc3127c3d8cd9"), registry["6a5ba450a7851e16ce0bde44"]);
            Assert.Equal(("Peacekeeper", 3, "5935c25fb3acc3127c3d8cd9"), registry["6a5ba48b8cfd0bddb3d4d2e1"]);
            Assert.Equal(("Peacekeeper", 4, "5935c25fb3acc3127c3d8cd9"), registry["6a5ba4c57cbb93b629051591"]);

            // Verify Mechanic LL1-4
            Assert.Equal(("Mechanic", 1, "5a7c2eca46aef81a7ca2145d"), registry["6a3171c927ca9591bf4db1c4"]);
            Assert.Equal(("Mechanic", 2, "5a7c2eca46aef81a7ca2145d"), registry["6a3c0fefbea2d2ad581c090b"]);
            Assert.Equal(("Mechanic", 3, "5a7c2eca46aef81a7ca2145d"), registry["6a3cf95c6b35530c4a4f532e"]);
            Assert.Equal(("Mechanic", 4, "5a7c2eca46aef81a7ca2145d"), registry["6a3d1c0990e9ffe15463e961"]);

            // Verify Ragman LL1-3
            Assert.Equal(("Ragman", 1, "5ac3b934156ae10c4430e83c"), registry["6a4b339f18db62e03b4f7ded"]);
            Assert.Equal(("Ragman", 2, "5ac3b934156ae10c4430e83c"), registry["6a4b4e6a30dac4b01af220aa"]);
            Assert.Equal(("Ragman", 3, "5ac3b934156ae10c4430e83c"), registry["6a4b9c9a60b56d421cceea18"]);

            // Verify Jaeger LL1-4
            Assert.Equal(("Jaeger", 1, "5c0647fdd443bc2504c2d371"), registry["6a43a01ccc83aceedd35f09c"]);
            Assert.Equal(("Jaeger", 2, "5c0647fdd443bc2504c2d371"), registry["6a43a095bfef0cd74c298963"]);
            Assert.Equal(("Jaeger", 3, "5c0647fdd443bc2504c2d371"), registry["6a43a13633c97d216dfc85de"]);
            Assert.Equal(("Jaeger", 4, "5c0647fdd443bc2504c2d371"), registry["6a43a16dde81644a7951f31b"]);
        }

        [Theory]
        [InlineData("Shooting Cans [PVP ZONE]", "Shooting Cans")]
        [InlineData("Saving the Mole [PVE ZONE]", "Saving the Mole")]
        [InlineData("Ref's Secret [PVP zone]", "Ref's Secret")]
        [InlineData("First in Line", "First in Line")]
        [InlineData("  Weapons Circulation  [PVP ZONE]  ", "Weapons Circulation")]
        public void CleanQuestTitle_StripsPvpPveZoneProperly(string input, string expected)
        {
            var cleaned = DataNormalizerService.CleanQuestTitle(input);
            Assert.Equal(expected, cleaned);
        }

        [Theory]
        [InlineData("https://escapefromtarkov.fandom.com/wiki/First_in_Line", "First in Line")]
        [InlineData("https://escapefromtarkov.fandom.com/wiki/Health_Care_Privacy_-_Part_1", "Health Care Privacy - Part 1")]
        [InlineData("https://escapefromtarkov.fandom.com/wiki/Reserve_(quest)", "Reserve (quest)")]
        [InlineData("https://escapefromtarkov.fandom.com/wiki/Delivery_From_the_Past/", "Delivery From the Past")]
        public void ExtractTitleFromWikiLink_ExtractsCleanTitle(string wikiLink, string expected)
        {
            var title = TarkovTrackerExtractorService.ExtractTitleFromWikiLink(wikiLink);
            Assert.Equal(expected, title);
        }

        [Fact]
        public void ParseTasksCore_ParsesSamplePayloadCorrectly()
        {
            var sampleJson = @"{
                ""data"": {
                    ""tasks"": [
                        {
                            ""id"": ""5a27ba1c86f77461ea5a3c56"",
                            ""name"": ""Weapons Circulation"",
                            ""trader"": { ""id"": ""5935c25fb3acc3127c3d8cd9"", ""name"": ""Peacekeeper"" },
                            ""minPlayerLevel"": 0,
                            ""kappaRequired"": true,
                            ""wikiLink"": ""https://escapefromtarkov.fandom.com/wiki/Weapons_Circulation"",
                            ""normalizedTraderRequirements"": [
                                { ""requirementType"": ""level"", ""compareMethod"": "">="", ""value"": 2, ""trader"": { ""id"": ""5935c25fb3acc3127c3d8cd9"", ""name"": ""Peacekeeper"" } }
                            ],
                            ""otherRequirements"": [
                                {
                                    ""type"": ""globalVariable"",
                                    ""variableId"": ""6a5ba450a7851e16ce0bde44"",
                                    ""value"": 5,
                                    ""taskPool"": {
                                        ""traderId"": ""5935c25fb3acc3127c3d8cd9"",
                                        ""loyaltyLevel"": 2
                                    }
                                }
                            ]
                        }
                    ],
                    ""traders"": [
                        { ""id"": ""5935c25fb3acc3127c3d8cd9"", ""name"": ""Peacekeeper"" }
                    ],
                    ""maps"": []
                }
            }";

            var rawData = TarkovTrackerExtractorService.ParseTasksCore(sampleJson);
            Assert.NotNull(rawData);
            Assert.Single(rawData.Tasks);

            var task = rawData.Tasks[0];
            Assert.Equal("5a27ba1c86f77461ea5a3c56", task.Id);
            Assert.Equal("Weapons Circulation", task.Name);
            Assert.Equal("Peacekeeper", task.Trader?.Name);
            Assert.Single(task.NormalizedTraderRequirements);
            Assert.Equal(2, task.NormalizedTraderRequirements[0].Value);
            Assert.Single(task.OtherRequirements);
            Assert.Equal("globalVariable", task.OtherRequirements[0].Type);
            Assert.Equal(5, task.OtherRequirements[0].Value);
            Assert.Equal(2, task.OtherRequirements[0].TaskPool?.LoyaltyLevel);
        }

        [Fact]
        public void ParseFallbackTasks_Applies27VariableRegistryAndTitleExtraction()
        {
            var fallbackJson = @"{
                ""data"": {
                    ""tasks"": {
                        ""5a27ba1c86f77461ea5a3c56"": {
                            ""id"": ""5a27ba1c86f77461ea5a3c56"",
                            ""name"": ""5a27ba1c86f77461ea5a3c56 name"",
                            ""trader"": ""5935c25fb3acc3127c3d8cd9"",
                            ""wikiLink"": ""https://escapefromtarkov.fandom.com/wiki/Weapons_Circulation"",
                            ""minPlayerLevel"": 0,
                            ""otherRequirements"": [
                                {
                                    ""id"": ""req_1"",
                                    ""type"": ""globalVariable"",
                                    ""variableId"": ""6a5ba450a7851e16ce0bde44"",
                                    ""compareMethod"": "">="",
                                    ""value"": 5
                                }
                            ],
                            ""taskRequirements"": [
                                { ""task"": ""prereq_task_123"", ""status"": [""complete""] }
                            ]
                        }
                    }
                }
            }";

            var rawData = TarkovTrackerExtractorService.ParseFallbackTasks(fallbackJson);
            Assert.NotNull(rawData);
            Assert.Single(rawData.Tasks);

            var task = rawData.Tasks[0];
            Assert.Equal("5a27ba1c86f77461ea5a3c56", task.Id);
            Assert.Equal("Weapons Circulation", task.Name); // Extracted from wikiLink!
            Assert.Equal("Peacekeeper", task.Trader?.Name); // Resolved from TraderIdToName!

            Assert.Single(task.OtherRequirements);
            var poolReq = task.OtherRequirements[0];
            Assert.NotNull(poolReq.TaskPool);
            Assert.Equal(2, poolReq.TaskPool.LoyaltyLevel);
            Assert.Equal("5935c25fb3acc3127c3d8cd9", poolReq.TaskPool.TraderId);

            Assert.Single(task.TaskRequirements);
            Assert.Equal("prereq_task_123", task.TaskRequirements[0].Task?.Id);
        }

        [Fact]
        public void NormalizeTarkovTrackerData_NormalizesTasksAndRequirementsAccurately()
        {
            var sampleTask = new TarkovTrackerTask
            {
                Id = "5a27ba1c86f77461ea5a3c56",
                Name = "Weapons Circulation [PVP ZONE]",
                Trader = new TarkovTrackerTraderRef { Id = "5935c25fb3acc3127c3d8cd9", Name = "Peacekeeper" },
                MinPlayerLevel = 25,
                FactionName = "Any",
                KappaRequired = true,
                WikiLink = "https://escapefromtarkov.fandom.com/wiki/Weapons_Circulation",
                NormalizedTraderRequirements = new List<TarkovTrackerTraderRequirement>
                {
                    new()
                    {
                        RequirementType = "level",
                        CompareMethod = ">=",
                        Value = 2,
                        Trader = new TarkovTrackerTraderRef { Id = "5935c25fb3acc3127c3d8cd9", Name = "Peacekeeper" }
                    }
                },
                OtherRequirements = new List<TarkovTrackerOtherRequirement>
                {
                    new()
                    {
                        Type = "globalVariable",
                        VariableId = "6a5ba450a7851e16ce0bde44",
                        Value = 5,
                        TaskPool = new TarkovTrackerTaskPool
                        {
                            TraderId = "5935c25fb3acc3127c3d8cd9",
                            LoyaltyLevel = 2
                        }
                    }
                },
                TaskRequirements = new List<TarkovTrackerTaskRequirement>
                {
                    new()
                    {
                        Task = new TarkovTrackerTaskRef
                        {
                            Id = "5a03153686f77442d90e2171",
                            Name = "Fishing Spot",
                            WikiLink = "https://escapefromtarkov.fandom.com/wiki/Fishing_Spot"
                        },
                        Status = new List<string> { "complete" }
                    }
                }
            };

            var rawData = new RawTarkovTrackerData
            {
                Source = "TarkovTracker Test",
                Tasks = new List<TarkovTrackerTask> { sampleTask }
            };

            var normalizer = new DataNormalizerService();
            var normalizedData = normalizer.NormalizeTarkovTrackerData(rawData);

            Assert.NotNull(normalizedData);
            Assert.Single(normalizedData.Quests);

            var quest = normalizedData.Quests[0];
            Assert.Equal("Weapons Circulation", quest.Name); // [PVP ZONE] stripped!
            Assert.Equal("5a27ba1c86f77461ea5a3c56", quest.BsgId);
            Assert.Equal("Peacekeeper", quest.Trader);
            Assert.Equal(25, quest.MinLevel);
            Assert.Null(quest.Faction); // "Any" normalized to null
            Assert.True(quest.KappaRequired);

            // Trader requirement
            Assert.Single(normalizedData.QuestTraderRequirements);
            var traderReq = normalizedData.QuestTraderRequirements[0];
            Assert.Equal(quest.Id, traderReq.QuestId);
            Assert.Equal("Peacekeeper", traderReq.TraderName);
            Assert.Equal(2, traderReq.RequiredLevel);
            Assert.Equal(">=", traderReq.CompareMethod);
            Assert.Equal("Level", traderReq.RequirementType);

            // Pool requirement
            Assert.Single(normalizedData.QuestPoolRequirements);
            var poolReq = normalizedData.QuestPoolRequirements[0];
            Assert.Equal(quest.Id, poolReq.QuestId);
            Assert.Equal("Peacekeeper", poolReq.PoolTrader);
            Assert.Equal(2, poolReq.PoolTraderLL);
            Assert.Equal(5, poolReq.RequiredCompletedCount);
            Assert.Contains("Complete at least 5 Peacekeeper LL2 quests", poolReq.Description);

            // Linear prerequisite
            Assert.Single(normalizedData.QuestRequirements);
            var linearReq = normalizedData.QuestRequirements[0];
            Assert.Equal(quest.Id, linearReq.QuestId);
            Assert.Equal("Fishing Spot", linearReq.RequiredQuestName);
            Assert.Equal("Complete", linearReq.RequirementType);
        }

        [Fact]
        public void NormalizeTarkovTrackerData_ScratchCorePayload_NormalizesAllTasksAccurately()
        {
            var scratchPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "scratch", "tasks_core.json");
            if (!File.Exists(scratchPath))
            {
                scratchPath = "scratch/tasks_core.json";
            }

            if (!File.Exists(scratchPath))
            {
                // If run in an environment where scratch is omitted, skip without failure
                return;
            }

            var coreJson = File.ReadAllText(scratchPath);
            var rawData = TarkovTrackerExtractorService.ParseTasksCore(coreJson);

            Assert.Equal(494, rawData.Tasks.Count);

            var normalizer = new DataNormalizerService();
            var normalizedData = normalizer.NormalizeTarkovTrackerData(rawData);

            Assert.Equal(494, normalizedData.Quests.Count);
            // Verify trader requirements count (~303-304)
            Assert.InRange(normalizedData.QuestTraderRequirements.Count, 290, 310);
            // Verify pool requirements count (exactly 168)
            Assert.Equal(168, normalizedData.QuestPoolRequirements.Count);
            // Verify linear prerequisites count
            Assert.InRange(normalizedData.QuestRequirements.Count, 190, 220);

            // Check specific quest: Weapons Circulation
            var weaponsCirc = normalizedData.Quests.FirstOrDefault(q => q.Name == "Weapons Circulation");
            Assert.NotNull(weaponsCirc);
            Assert.Equal("5a27ba1c86f77461ea5a3c56", weaponsCirc.BsgId);
            Assert.Equal("Peacekeeper", weaponsCirc.Trader);
            Assert.Contains(weaponsCirc.TraderRequirements, tr => tr.TraderName == "Peacekeeper" && tr.RequiredLevel == 2);
            Assert.Contains(weaponsCirc.PoolRequirements, pr => pr.PoolTrader == "Peacekeeper" && pr.PoolTraderLL == 2 && pr.RequiredCompletedCount == 5);
        }

        [Fact]
        public void NormalizeTarkovTrackerData_ScratchFallbackPayload_Applies27RegistryAndYields168Pools()
        {
            var scratchPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "scratch", "tasks_fallback.json");
            if (!File.Exists(scratchPath))
            {
                scratchPath = "scratch/tasks_fallback.json";
            }

            if (!File.Exists(scratchPath))
            {
                return;
            }

            var fallbackJson = File.ReadAllText(scratchPath);
            var rawData = TarkovTrackerExtractorService.ParseFallbackTasks(fallbackJson);

            Assert.Equal(503, rawData.Tasks.Count);

            var normalizer = new DataNormalizerService();
            var normalizedData = normalizer.NormalizeTarkovTrackerData(rawData);

            Assert.Equal(503, normalizedData.Quests.Count);
            // Verify that all 168 global variables in fallback got enriched via the 27 registry
            Assert.Equal(168, normalizedData.QuestPoolRequirements.Count);

            var weaponsCirc = normalizedData.Quests.FirstOrDefault(q => q.Name == "Weapons Circulation");
            Assert.NotNull(weaponsCirc);
            Assert.Contains(weaponsCirc.PoolRequirements, pr => pr.PoolTrader == "Peacekeeper" && pr.PoolTraderLL == 2 && pr.RequiredCompletedCount == 5);
        }

        [Fact]
        public void ParseTasksObjectives_ScratchObjectivesPayload_EnrichesTasks()
        {
            var scratchPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "scratch", "tasks_objectives.json");
            if (!File.Exists(scratchPath))
            {
                scratchPath = "scratch/tasks_objectives.json";
            }

            if (!File.Exists(scratchPath))
            {
                return;
            }

            var objJson = File.ReadAllText(scratchPath);
            var objectivesLookup = TarkovTrackerExtractorService.ParseTasksObjectives(objJson);

            Assert.Equal(494, objectivesLookup.Count);
            // Verify First in Line objectives (657315ddab5a49b71f098853)
            Assert.True(objectivesLookup.TryGetValue("657315ddab5a49b71f098853", out var firstInLineObjs));
            Assert.NotNull(firstInLineObjs);
            Assert.NotEmpty(firstInLineObjs);
        }

        [Fact]
        public void ExtractorService_Configuration_Has15SecondTimeoutAndUserAgents()
        {
            using var extractor = new TarkovTrackerExtractorService();
            Assert.Equal(TimeSpan.FromSeconds(15), extractor.RequestTimeout);
            Assert.Equal("TarkovTracker/1.0 (+https://tarkovtracker.org)", TarkovTrackerExtractorService.FallbackUserAgent);
            Assert.Contains("TarkovHelper", TarkovTrackerExtractorService.PrimaryUserAgent);
        }

        [Fact]
        public async Task ExtractorService_WhenPrimaryFails_FallsBackToJsonTarkovDevWithMandatoryUserAgent()
        {
            var fallbackJson = @"{
                ""data"": {
                    ""tasks"": {
                        ""test_task_1"": {
                            ""id"": ""test_task_1"",
                            ""name"": ""Delivery from the Past"",
                            ""trader"": ""54cb50c76803fa8b248b4571"",
                            ""wikiLink"": ""https://escapefromtarkov.fandom.com/wiki/Delivery_From_the_Past"",
                            ""minPlayerLevel"": 5,
                            ""otherRequirements"": [
                                {
                                    ""id"": ""req1"",
                                    ""type"": ""globalVariable"",
                                    ""variableId"": ""6a2688488bba18e0b0187a04"",
                                    ""value"": 5
                                }
                            ]
                        }
                    }
                }
            }";

            string? usedUserAgent = null;

            var mockHandler = new MockHttpMessageHandler(req =>
            {
                if (req.RequestUri?.ToString().Contains("tarkovtracker.org") == true)
                {
                    return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
                }

                if (req.Headers.TryGetValues("User-Agent", out var values))
                {
                    usedUserAgent = string.Join(" ", values);
                }

                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(fallbackJson, System.Text.Encoding.UTF8, "application/json")
                };
            });

            using var httpClient = new HttpClient(mockHandler);
            using var extractor = new TarkovTrackerExtractorService(httpClient);

            var rawData = await extractor.ExtractAsync();

            Assert.NotNull(rawData);
            Assert.Equal("json.tarkov.dev Fallback", rawData.Source);
            Assert.Single(rawData.Tasks);
            Assert.Equal("Prapor", rawData.Tasks[0].Trader?.Name);
            Assert.Equal(2, rawData.Tasks[0].OtherRequirements[0].TaskPool?.LoyaltyLevel);
            Assert.Equal(TarkovTrackerExtractorService.FallbackUserAgent, usedUserAgent);
        }

        private class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

            public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_handler(request));
            }
        }
    }
}
