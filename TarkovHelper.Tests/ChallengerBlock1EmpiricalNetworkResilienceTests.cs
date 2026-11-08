using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services.DataAudit;
using Xunit;

namespace TarkovHelper.Tests
{
    /// <summary>
    /// Empirical challenger tests for Bloque 1: Network Resilience, Simulated Failures (500, Timeout),
    /// Fallback execution, 27 Global Variable Registry verification, and ground-truth count assertions.
    /// </summary>
    public class ChallengerBlock1EmpiricalNetworkResilienceTests
    {
        private const string SampleFallbackJson = @"{
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
                                ""id"": ""req1"",
                                ""type"": ""globalVariable"",
                                ""variableId"": ""6a5ba450a7851e16ce0bde44"",
                                ""value"": 5
                            }
                        ]
                    }
                }
            }
        }";

        #region 1. Simulated Primary Failure (500 Error, 502/503/504, Timeout, Corrupted JSON)

        [Fact]
        public async Task SimulatedPrimaryFailure_Http500_TriggersFallbackAndYieldsValidData()
        {
            var progressReports = new System.Collections.Concurrent.ConcurrentBag<string>();
            var progress = new Progress<string>(msg => progressReports.Add(msg));

            var handler = new MockDelegatingHandler((req, ct) =>
            {
                if (req.RequestUri != null && req.RequestUri.ToString().Contains("tarkovtracker.org"))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("{\"error\":\"Internal Server Error\"}", Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SampleFallbackJson, Encoding.UTF8, "application/json")
                });
            });

            using var httpClient = new HttpClient(handler);
            using var extractor = new TarkovTrackerExtractorService(httpClient);

            var result = await extractor.ExtractAsync(progress, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("json.tarkov.dev Fallback", result.Source);
            Assert.Single(result.Tasks);
            Assert.Equal("Weapons Circulation", result.Tasks[0].Name);
            Assert.Equal("Peacekeeper", result.Tasks[0].Trader?.Name);

            await Task.Delay(20);
            Assert.Contains(progressReports, p => p.Contains("Initiating fallback"));
        }

        [Theory]
        [InlineData(HttpStatusCode.BadGateway)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.GatewayTimeout)]
        public async Task SimulatedPrimaryFailure_ServerErrors_TriggerFallback(HttpStatusCode statusCode)
        {
            var handler = new MockDelegatingHandler((req, ct) =>
            {
                if (req.RequestUri != null && req.RequestUri.ToString().Contains("tarkovtracker.org"))
                {
                    return Task.FromResult(new HttpResponseMessage(statusCode));
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SampleFallbackJson, Encoding.UTF8, "application/json")
                });
            });

            using var httpClient = new HttpClient(handler);
            using var extractor = new TarkovTrackerExtractorService(httpClient);

            var result = await extractor.ExtractAsync();

            Assert.NotNull(result);
            Assert.Equal("json.tarkov.dev Fallback", result.Source);
            Assert.NotEmpty(result.Tasks);
        }

        [Fact]
        public async Task SimulatedPrimaryFailure_Timeout_TriggersFallbackSuccessfully()
        {
            var progressReports = new System.Collections.Concurrent.ConcurrentBag<string>();
            var progress = new Progress<string>(msg => progressReports.Add(msg));

            var handler = new MockDelegatingHandler((req, ct) =>
            {
                if (req.RequestUri != null && req.RequestUri.ToString().Contains("tarkovtracker.org"))
                {
                    // Throw TaskCanceledException simulating HttpClient timeout
                    throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SampleFallbackJson, Encoding.UTF8, "application/json")
                });
            });

            using var httpClient = new HttpClient(handler);
            using var extractor = new TarkovTrackerExtractorService(httpClient);

            var result = await extractor.ExtractAsync(progress, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("json.tarkov.dev Fallback", result.Source);
            Assert.Single(result.Tasks);
            
            // Allow progress event to flush if needed
            await Task.Delay(20);
            Assert.Contains(progressReports, p => p.Contains("Initiating fallback"));
        }

        [Fact]
        public async Task SimulatedPrimaryFailure_CorruptedJson_TriggersFallbackSuccessfully()
        {
            var handler = new MockDelegatingHandler((req, ct) =>
            {
                if (req.RequestUri != null && req.RequestUri.ToString().Contains("tarkovtracker.org"))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{ broken json ... invalid <<<", Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SampleFallbackJson, Encoding.UTF8, "application/json")
                });
            });

            using var httpClient = new HttpClient(handler);
            using var extractor = new TarkovTrackerExtractorService(httpClient);

            var result = await extractor.ExtractAsync();

            Assert.NotNull(result);
            Assert.Equal("json.tarkov.dev Fallback", result.Source);
            Assert.Single(result.Tasks);
        }

        [Fact]
        public async Task SimulatedFailure_BothPrimaryAndFallbackFail_ThrowsAggregateException()
        {
            var handler = new MockDelegatingHandler((req, ct) =>
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            });

            using var httpClient = new HttpClient(handler);
            using var extractor = new TarkovTrackerExtractorService(httpClient);

            var ex = await Assert.ThrowsAsync<AggregateException>(() => extractor.ExtractAsync());
            Assert.Contains("Failed to extract quest data from both", ex.Message);
            Assert.True(ex.InnerExceptions.Count >= 2);
        }

        [Fact]
        public async Task SimulatedFailure_PrimaryObjectives500_DegradesGracefullyWithCoreData()
        {
            var coreJson = @"{
                ""data"": {
                    ""tasks"": [
                        {
                            ""id"": ""5a27ba1c86f77461ea5a3c56"",
                            ""name"": ""Weapons Circulation"",
                            ""trader"": { ""id"": ""5935c25fb3acc3127c3d8cd9"", ""name"": ""Peacekeeper"" },
                            ""objectives"": [
                                { ""id"": ""core_obj_1"", ""description"": ""Find in raid 2 M4A1 rifles"" }
                            ]
                        }
                    ],
                    ""traders"": [],
                    ""maps"": []
                }
            }";

            var handler = new MockDelegatingHandler((req, ct) =>
            {
                var uri = req.RequestUri?.ToString() ?? "";
                if (uri.Contains("tasks-core"))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(coreJson, Encoding.UTF8, "application/json")
                    });
                }
                if (uri.Contains("tasks-objectives"))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

            using var httpClient = new HttpClient(handler);
            using var extractor = new TarkovTrackerExtractorService(httpClient);

            var result = await extractor.ExtractFromCoreAndObjectivesAsync();

            Assert.NotNull(result);
            Assert.Single(result.Tasks);
            Assert.Equal("Weapons Circulation", result.Tasks[0].Name);
            // Core objectives are preserved cleanly
            Assert.Single(result.Tasks[0].Objectives);
            Assert.Equal("Find in raid 2 M4A1 rifles", result.Tasks[0].Objectives[0].Description);
        }

        [Fact]
        public async Task SimulatedFailure_PrimaryObjectivesException_ReportsWarningAndDegradesGracefully()
        {
            var coreJson = @"{
                ""data"": {
                    ""tasks"": [
                        {
                            ""id"": ""5a27ba1c86f77461ea5a3c56"",
                            ""name"": ""Weapons Circulation"",
                            ""trader"": { ""id"": ""5935c25fb3acc3127c3d8cd9"", ""name"": ""Peacekeeper"" },
                            ""objectives"": [
                                { ""id"": ""core_obj_1"", ""description"": ""Find in raid 2 M4A1 rifles"" }
                            ]
                        }
                    ],
                    ""traders"": [],
                    ""maps"": []
                }
            }";

            var progressReports = new System.Collections.Concurrent.ConcurrentBag<string>();
            var progress = new Progress<string>(msg => progressReports.Add(msg));

            var handler = new MockDelegatingHandler((req, ct) =>
            {
                var uri = req.RequestUri?.ToString() ?? "";
                if (uri.Contains("tasks-core"))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(coreJson, Encoding.UTF8, "application/json")
                    });
                }
                if (uri.Contains("tasks-objectives"))
                {
                    throw new HttpRequestException("Connection forcibly closed by remote host.");
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

            using var httpClient = new HttpClient(handler);
            using var extractor = new TarkovTrackerExtractorService(httpClient);

            var result = await extractor.ExtractFromCoreAndObjectivesAsync(progress);

            Assert.NotNull(result);
            Assert.Single(result.Tasks);
            Assert.Equal("Weapons Circulation", result.Tasks[0].Name);
            Assert.Single(result.Tasks[0].Objectives);

            await Task.Delay(20);
            Assert.Contains(progressReports, p => p.Contains("Warning: Could not fetch tasks-objectives"));
        }

        [Fact]
        public async Task Fallback_StrictlySendsRequiredUserAgentHeader()
        {
            string? capturedUserAgent = null;

            var handler = new MockDelegatingHandler((req, ct) =>
            {
                if (req.Headers.TryGetValues("User-Agent", out var values))
                {
                    capturedUserAgent = string.Join(" ", values);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SampleFallbackJson, Encoding.UTF8, "application/json")
                });
            });

            using var httpClient = new HttpClient(handler);
            using var extractor = new TarkovTrackerExtractorService(httpClient);

            await extractor.ExtractFromFallbackAsync();

            Assert.Equal("TarkovTracker/1.0 (+https://tarkovtracker.org)", capturedUserAgent);
        }

        #endregion

        #region 2. Verification of tasks_fallback.json -> 168 Pool Requirements via 27 Variable Registry

        [Fact]
        public void Verify_TasksFallbackJson_Yields168PoolRequirementsThrough27VariableRegistry()
        {
            var scratchPath = FindScratchFile("tasks_fallback.json");
            Assert.True(File.Exists(scratchPath), $"Scratch file not found: {scratchPath}");

            var fallbackJson = File.ReadAllText(scratchPath);
            var rawData = TarkovTrackerExtractorService.ParseFallbackTasks(fallbackJson);

            Assert.NotNull(rawData);
            Assert.Equal(503, rawData.Tasks.Count);

            // Verify in raw tasks that all otherRequirements with type globalVariable got enriched
            int rawPoolCount = 0;
            var usedVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in rawData.Tasks)
            {
                if (task.OtherRequirements != null)
                {
                    foreach (var o in task.OtherRequirements)
                    {
                        if (string.Equals(o.Type, "globalVariable", StringComparison.OrdinalIgnoreCase))
                        {
                            rawPoolCount++;
                            Assert.NotNull(o.VariableId);
                            usedVariables.Add(o.VariableId);

                            // Must be enriched by the 27 variable registry
                            Assert.NotNull(o.TaskPool);
                            Assert.False(string.IsNullOrEmpty(o.TaskPool.TraderId), $"TraderId was empty for variableId {o.VariableId}");
                            Assert.NotNull(o.TaskPool.LoyaltyLevel);
                            Assert.InRange(o.TaskPool.LoyaltyLevel.Value, 1, 4);
                        }
                    }
                }
            }

            Assert.Equal(168, rawPoolCount);
            Assert.Equal(27, usedVariables.Count);

            // Now normalize and verify NormalizedAuditData
            var normalizer = new DataNormalizerService();
            var normalized = normalizer.NormalizeTarkovTrackerData(rawData);

            Assert.Equal(503, normalized.Quests.Count);
            Assert.Equal(168, normalized.QuestPoolRequirements.Count);

            // Verify every pool requirement is fully formed
            foreach (var pr in normalized.QuestPoolRequirements)
            {
                Assert.False(string.IsNullOrWhiteSpace(pr.Id), "PoolRequirement Id is empty");
                Assert.False(string.IsNullOrWhiteSpace(pr.QuestId), "PoolRequirement QuestId is empty");
                Assert.False(string.IsNullOrWhiteSpace(pr.PoolTrader), "PoolRequirement PoolTrader is empty");
                Assert.True(pr.PoolTraderLL.HasValue && pr.PoolTraderLL.Value >= 1 && pr.PoolTraderLL.Value <= 4,
                    $"PoolTraderLL invalid: {pr.PoolTraderLL}");
                Assert.True(pr.RequiredCompletedCount > 0, $"RequiredCompletedCount must be > 0: {pr.RequiredCompletedCount}");
                Assert.Contains($"Complete at least {pr.RequiredCompletedCount} {pr.PoolTrader} LL{pr.PoolTraderLL}", pr.Description);
            }

            // Check Weapons Circulation specifically in fallback
            var wc = normalized.Quests.FirstOrDefault(q => q.Name == "Weapons Circulation");
            Assert.NotNull(wc);
            Assert.Equal("Peacekeeper", wc.Trader);
            Assert.Single(wc.PoolRequirements);
            Assert.Equal("Peacekeeper", wc.PoolRequirements[0].PoolTrader);
            Assert.Equal(2, wc.PoolRequirements[0].PoolTraderLL);
            Assert.Equal(5, wc.PoolRequirements[0].RequiredCompletedCount);
        }

        #endregion

        #region 3. Verification of tasks_core.json -> 303/304 Trader Requirements and 168 Pool Requirements

        [Fact]
        public void Verify_TasksCoreJson_Yields303Or304TraderRequirementsAnd168PoolRequirements()
        {
            var scratchPath = FindScratchFile("tasks_core.json");
            Assert.True(File.Exists(scratchPath), $"Scratch file not found: {scratchPath}");

            var coreJson = File.ReadAllText(scratchPath);
            var rawData = TarkovTrackerExtractorService.ParseTasksCore(coreJson);

            Assert.NotNull(rawData);
            Assert.Equal(494, rawData.Tasks.Count);

            var normalizer = new DataNormalizerService();
            var normalized = normalizer.NormalizeTarkovTrackerData(rawData);

            Assert.Equal(494, normalized.Quests.Count);

            // In EFT: 304 total trader requirements (293 level requirements + 11 reputation requirements)
            Assert.True(normalized.QuestTraderRequirements.Count >= 303 && normalized.QuestTraderRequirements.Count <= 304,
                $"Expected 303 or 304 trader requirements, got: {normalized.QuestTraderRequirements.Count}");

            // Exactly 168 task pool requirements
            Assert.Equal(168, normalized.QuestPoolRequirements.Count);

            // Verify distribution of trader requirements
            var traderLevelReqs = normalized.QuestTraderRequirements.Where(r => r.RequirementType == "Level").ToList();
            var traderRepReqs = normalized.QuestTraderRequirements.Where(r => r.RequirementType == "Reputation").ToList();

            Assert.Equal(293, traderLevelReqs.Count);
            Assert.Equal(11, traderRepReqs.Count);

            // Verify Weapons Circulation requirements in core
            var wc = normalized.Quests.FirstOrDefault(q => q.Name == "Weapons Circulation");
            Assert.NotNull(wc);
            Assert.Equal("Peacekeeper", wc.Trader);

            // Trader requirement: Peacekeeper LL2
            Assert.Single(wc.TraderRequirements);
            Assert.Equal("Peacekeeper", wc.TraderRequirements[0].TraderName);
            Assert.Equal(2, wc.TraderRequirements[0].RequiredLevel);

            // Pool requirement: Peacekeeper LL2, 5 completed
            Assert.Single(wc.PoolRequirements);
            Assert.Equal("Peacekeeper", wc.PoolRequirements[0].PoolTrader);
            Assert.Equal(2, wc.PoolRequirements[0].PoolTraderLL);
            Assert.Equal(5, wc.PoolRequirements[0].RequiredCompletedCount);
        }

        #endregion

        #region 4. Stress Tests & Edge Case Mining

        [Fact]
        public void GlobalVariableRegistry_HasExact27UniqueEntries_WithValidTradersAndLLs()
        {
            var registry = TarkovTrackerExtractorService.GlobalVariableRegistry;
            Assert.Equal(27, registry.Count);

            var validTraders = new HashSet<string> { "Prapor", "Therapist", "Skier", "Peacekeeper", "Mechanic", "Ragman", "Jaeger" };

            foreach (var kvp in registry)
            {
                var varId = kvp.Key;
                var info = kvp.Value;

                Assert.Equal(24, varId.Length);
                Assert.Contains(info.TraderName, validTraders);
                Assert.InRange(info.LoyaltyLevel, 1, 4);
                Assert.Equal(24, info.TraderId.Length);
                Assert.True(TarkovTrackerExtractorService.TraderIdToName.ContainsKey(info.TraderId),
                    $"TraderId {info.TraderId} not in TraderIdToName lookup");
            }
        }

        [Fact]
        public void ParseFallbackTasks_UnknownVariableId_DoesNotCrash()
        {
            var fallbackWithUnknown = @"{
                ""data"": {
                    ""tasks"": {
                        ""unknown_pool_task"": {
                            ""id"": ""unknown_pool_task"",
                            ""name"": ""Test Quest"",
                            ""trader"": ""54cb50c76803fa8b248b4571"",
                            ""otherRequirements"": [
                                {
                                    ""type"": ""globalVariable"",
                                    ""variableId"": ""000000000000000000000000"",
                                    ""value"": 3
                                }
                            ]
                        }
                    }
                }
            }";

            var rawData = TarkovTrackerExtractorService.ParseFallbackTasks(fallbackWithUnknown);
            Assert.NotNull(rawData);
            Assert.Single(rawData.Tasks);

            var normalizer = new DataNormalizerService();
            var normalized = normalizer.NormalizeTarkovTrackerData(rawData);

            Assert.Single(normalized.Quests);
            // The unknown variableId will still map using task's trader fallback without crashing
            Assert.Single(normalized.QuestPoolRequirements);
            Assert.Equal("Prapor", normalized.QuestPoolRequirements[0].PoolTrader);
            Assert.Null(normalized.QuestPoolRequirements[0].PoolTraderLL);
            Assert.Equal(3, normalized.QuestPoolRequirements[0].RequiredCompletedCount);
        }

        #endregion

        #region Helper Methods

        private static string FindScratchFile(string fileName)
        {
            var candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "scratch", fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "scratch", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "scratch", fileName),
                Path.Combine(@"c:\Games\Proyectos\Tarkov-Helper", "scratch", fileName)
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                    return Path.GetFullPath(path);
            }

            return Path.Combine("scratch", fileName);
        }

        private class MockDelegatingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

            public MockDelegatingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _handler(request, cancellationToken);
            }
        }

        #endregion
    }
}
