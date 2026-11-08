using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TarkovHelper.Models.DataAudit;

namespace TarkovHelper.Services.DataAudit
{
    /// <summary>
    /// Service for extracting structured task, objective, trader requirement, and task pool data
    /// directly from the precalculated TarkovTracker API with resilient fallback to json.tarkov.dev.
    /// </summary>
    public class TarkovTrackerExtractorService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private bool _disposed;

        #region Endpoints and Headers

        public string TasksCoreUrl { get; set; } = "https://tarkovtracker.org/api/tarkov/tasks-core?lang=en&gameMode=regular";
        public string TasksObjectivesUrl { get; set; } = "https://tarkovtracker.org/api/tarkov/tasks-objectives?lang=en&gameMode=regular";
        public string FallbackTasksUrl { get; set; } = "https://json.tarkov.dev/regular/tasks";

        public const string FallbackUserAgent = "TarkovTracker/1.0 (+https://tarkovtracker.org)";
        public const string PrimaryUserAgent = "TarkovHelper/1.5.7 (+https://github.com/TarkovHelper)";

        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);

        #endregion

        #region 27 EFT Global Variable ID Registry

        /// <summary>
        /// The complete 27 EFT Global Variable ID registry discovered in TarkovTracker / EFT data dumps,
        /// mapping BSG globalVariable IDs directly to the responsible Trader, Loyalty Level tier, and Trader BSG ID.
        /// </summary>
        public static readonly Dictionary<string, (string TraderName, int LoyaltyLevel, string TraderId)> GlobalVariableRegistry = new(StringComparer.OrdinalIgnoreCase)
        {
            // Prapor (54cb50c76803fa8b248b4571)
            { "6a20540cf1b67a977cc5a088", ("Prapor", 1, "54cb50c76803fa8b248b4571") },
            { "6a2688488bba18e0b0187a04", ("Prapor", 2, "54cb50c76803fa8b248b4571") },
            { "6a32651a811905ed0cac0973", ("Prapor", 3, "54cb50c76803fa8b248b4571") },
            { "6a326525789ae12ecb0b2807", ("Prapor", 4, "54cb50c76803fa8b248b4571") },

            // Therapist (54cb57776803fa99248b456e)
            { "6a4e4ab3ecd1145894d00990", ("Therapist", 1, "54cb57776803fa99248b456e") },
            { "6a4e4aed3ded7a18126603f6", ("Therapist", 2, "54cb57776803fa99248b456e") },
            { "6a4e4b28629dc64c4001967c", ("Therapist", 3, "54cb57776803fa99248b456e") },
            { "6a56925b1c30ba5a77c7c518", ("Therapist", 4, "54cb57776803fa99248b456e") },

            // Skier (58330581ace78e27b8b10cee)
            { "6a59f3ba06c8949abad30871", ("Skier", 1, "58330581ace78e27b8b10cee") },
            { "6a5a111de1f417ac80a163e5", ("Skier", 2, "58330581ace78e27b8b10cee") },
            { "6a5a115181116e807b55f258", ("Skier", 3, "58330581ace78e27b8b10cee") },
            { "6a5a1192efde11cc7105b18f", ("Skier", 4, "58330581ace78e27b8b10cee") },

            // Peacekeeper (5935c25fb3acc3127c3d8cd9)
            { "6a5ba40fe5c4eaef5610f232", ("Peacekeeper", 1, "5935c25fb3acc3127c3d8cd9") },
            { "6a5ba450a7851e16ce0bde44", ("Peacekeeper", 2, "5935c25fb3acc3127c3d8cd9") },
            { "6a5ba48b8cfd0bddb3d4d2e1", ("Peacekeeper", 3, "5935c25fb3acc3127c3d8cd9") },
            { "6a5ba4c57cbb93b629051591", ("Peacekeeper", 4, "5935c25fb3acc3127c3d8cd9") },

            // Mechanic (5a7c2eca46aef81a7ca2145d)
            { "6a3171c927ca9591bf4db1c4", ("Mechanic", 1, "5a7c2eca46aef81a7ca2145d") },
            { "6a3c0fefbea2d2ad581c090b", ("Mechanic", 2, "5a7c2eca46aef81a7ca2145d") },
            { "6a3cf95c6b35530c4a4f532e", ("Mechanic", 3, "5a7c2eca46aef81a7ca2145d") },
            { "6a3d1c0990e9ffe15463e961", ("Mechanic", 4, "5a7c2eca46aef81a7ca2145d") },

            // Ragman (5ac3b934156ae10c4430e83c)
            { "6a4b339f18db62e03b4f7ded", ("Ragman", 1, "5ac3b934156ae10c4430e83c") },
            { "6a4b4e6a30dac4b01af220aa", ("Ragman", 2, "5ac3b934156ae10c4430e83c") },
            { "6a4b9c9a60b56d421cceea18", ("Ragman", 3, "5ac3b934156ae10c4430e83c") },

            // Jaeger (5c0647fdd443bc2504c2d371)
            { "6a43a01ccc83aceedd35f09c", ("Jaeger", 1, "5c0647fdd443bc2504c2d371") },
            { "6a43a095bfef0cd74c298963", ("Jaeger", 2, "5c0647fdd443bc2504c2d371") },
            { "6a43a13633c97d216dfc85de", ("Jaeger", 3, "5c0647fdd443bc2504c2d371") },
            { "6a43a16dde81644a7951f31b", ("Jaeger", 4, "5c0647fdd443bc2504c2d371") }
        };

        /// <summary>
        /// Standard BSG Trader ID to English Trader Name lookup.
        /// </summary>
        public static readonly Dictionary<string, string> TraderIdToName = new(StringComparer.OrdinalIgnoreCase)
        {
            { "54cb50c76803fa8b248b4571", "Prapor" },
            { "54cb57776803fa99248b456e", "Therapist" },
            { "579dc571d53a0658a154fbec", "Fence" },
            { "58330581ace78e27b8b10cee", "Skier" },
            { "5935c25fb3acc3127c3d8cd9", "Peacekeeper" },
            { "5a7c2eca46aef81a7ca2145d", "Mechanic" },
            { "5ac3b934156ae10c4430e83c", "Ragman" },
            { "5c0647fdd443bc2504c2d371", "Jaeger" },
            { "638f541a29ffd1183d187f57", "Lightkeeper" },
            { "656f0f98d80a697f855d34b1", "BTR Driver" },
            { "6617beeaa9cfa777ca915b7c", "Ref" },
            { "68fe15910f29ba3fdbba9d54", "Taran" },
            { "68fe15990f29ba3fdbba9d55", "Radio station" },
            { "688246518448b05efd61d461", "Mr. Kerman" },
            { "688246958448b05efd61d462", "Voevoda" },
            { "69e0d6cc77b63940375b9173", "Survivor" }
        };

        #endregion

        #region Constructors & Lifecycle

        public TarkovTrackerExtractorService(HttpClient? httpClient = null)
        {
            if (httpClient != null)
            {
                _httpClient = httpClient;
                _ownsHttpClient = false;
            }
            else
            {
                _httpClient = new HttpClient();
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", PrimaryUserAgent);
                _httpClient.Timeout = RequestTimeout;
                _ownsHttpClient = true;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_ownsHttpClient)
                {
                    _httpClient.Dispose();
                }
                _disposed = true;
            }
        }

        #endregion

        #region Public Extraction Methods

        /// <summary>
        /// Extracts complete TarkovTracker dataset. Attempts primary endpoints (tasks-core and tasks-objectives) first.
        /// If primary fails or times out, falls back to json.tarkov.dev/regular/tasks with mandatory header.
        /// </summary>
        public async Task<RawTarkovTrackerData> ExtractAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            Exception? primaryException = null;

            try
            {
                progress?.Report("Querying primary TarkovTracker API (tasks-core & tasks-objectives)...");
                return await ExtractFromCoreAndObjectivesAsync(progress, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException)
            {
                primaryException = ex;
                progress?.Report($"Primary TarkovTracker endpoint unavailable ({ex.Message}). Initiating fallback to json.tarkov.dev...");
            }

            try
            {
                return await ExtractFromFallbackAsync(progress, cancellationToken);
            }
            catch (Exception fallbackEx)
            {
                throw new AggregateException(
                    "Failed to extract quest data from both TarkovTracker primary endpoints and json.tarkov.dev fallback.",
                    primaryException ?? new InvalidOperationException("Primary extraction failed."),
                    fallbackEx
                );
            }
        }

        /// <summary>
        /// Fetches and parses tasks-core and tasks-objectives from tarkovtracker.org.
        /// </summary>
        public async Task<RawTarkovTrackerData> ExtractFromCoreAndObjectivesAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            using var timeoutCts = new CancellationTokenSource(RequestTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            progress?.Report("Fetching tasks-core payload...");
            using var coreRequest = new HttpRequestMessage(HttpMethod.Get, TasksCoreUrl);
            coreRequest.Headers.TryAddWithoutValidation("User-Agent", PrimaryUserAgent);

            using var coreResponse = await _httpClient.SendAsync(coreRequest, linkedCts.Token);
            coreResponse.EnsureSuccessStatusCode();

            var coreJson = await coreResponse.Content.ReadAsStringAsync(linkedCts.Token);
            var parsedData = ParseTasksCore(coreJson);

            progress?.Report($"Parsed {parsedData.Tasks.Count} tasks from tasks-core. Fetching tasks-objectives payload...");

            try
            {
                using var objTimeoutCts = new CancellationTokenSource(RequestTimeout);
                using var objLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, objTimeoutCts.Token);

                using var objRequest = new HttpRequestMessage(HttpMethod.Get, TasksObjectivesUrl);
                objRequest.Headers.TryAddWithoutValidation("User-Agent", PrimaryUserAgent);

                using var objResponse = await _httpClient.SendAsync(objRequest, objLinkedCts.Token);
                if (objResponse.IsSuccessStatusCode)
                {
                    var objJson = await objResponse.Content.ReadAsStringAsync(objLinkedCts.Token);
                    var objectivesLookup = ParseTasksObjectives(objJson);

                    parsedData.ObjectivesByTaskId = objectivesLookup;

                    // Enrich tasks with detailed objectives
                    foreach (var task in parsedData.Tasks)
                    {
                        if (objectivesLookup.TryGetValue(task.Id, out var detailedObjectives) && detailedObjectives.Count > 0)
                        {
                            task.Objectives = detailedObjectives;
                        }
                    }
                    progress?.Report($"Enriched tasks with {objectivesLookup.Count} task objectives.");
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"Warning: Could not fetch tasks-objectives ({ex.Message}), proceeding with core objectives.");
            }

            return parsedData;
        }

        /// <summary>
        /// Fetches and parses fallback dataset from json.tarkov.dev/regular/tasks with the mandatory User-Agent header.
        /// </summary>
        public async Task<RawTarkovTrackerData> ExtractFromFallbackAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            using var timeoutCts = new CancellationTokenSource(RequestTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            progress?.Report($"Querying fallback endpoint {FallbackTasksUrl} with User-Agent: {FallbackUserAgent}...");

            using var request = new HttpRequestMessage(HttpMethod.Get, FallbackTasksUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", FallbackUserAgent);

            using var response = await _httpClient.SendAsync(request, linkedCts.Token);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(linkedCts.Token);
            var parsedData = ParseFallbackTasks(json);

            progress?.Report($"Successfully extracted {parsedData.Tasks.Count} tasks from json.tarkov.dev fallback.");
            return parsedData;
        }

        #endregion

        #region JSON Parsing and Mapping Methods

        /// <summary>
        /// Parses the JSON from tasks-core into RawTarkovTrackerData.
        /// </summary>
        public static RawTarkovTrackerData ParseTasksCore(string json)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var root = JsonSerializer.Deserialize<TarkovTrackerCoreResponse>(json, options);
            if (root?.Data == null)
            {
                throw new InvalidOperationException("Failed to deserialize TarkovTrackerCoreResponse: 'data' property is null.");
            }

            return new RawTarkovTrackerData
            {
                ExtractedAt = DateTime.UtcNow,
                Source = "TarkovTracker API",
                Tasks = root.Data.Tasks ?? new List<TarkovTrackerTask>(),
                Traders = root.Data.Traders ?? new List<TarkovTrackerTrader>(),
                Maps = root.Data.Maps ?? new List<TarkovTrackerMap>(),
                OverlayMetadata = root.DataOverlay
            };
        }

        /// <summary>
        /// Parses tasks-objectives into a lookup dictionary of BSG Task ID -> List of Objectives.
        /// </summary>
        public static Dictionary<string, List<TarkovTrackerObjective>> ParseTasksObjectives(string json)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var root = JsonSerializer.Deserialize<TarkovTrackerObjectivesResponse>(json, options);
            var result = new Dictionary<string, List<TarkovTrackerObjective>>(StringComparer.OrdinalIgnoreCase);

            if (root?.Data?.Tasks != null)
            {
                foreach (var entry in root.Data.Tasks)
                {
                    if (!string.IsNullOrEmpty(entry.Id))
                    {
                        result[entry.Id] = entry.Objectives ?? new List<TarkovTrackerObjective>();
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Parses the json.tarkov.dev/regular/tasks dictionary payload, applying the 27 Global Variable registry
        /// and trader lookup tables to produce an identical RawTarkovTrackerData representation.
        /// </summary>
        public static RawTarkovTrackerData ParseFallbackTasks(string json)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var dataElem) || !dataElem.TryGetProperty("tasks", out var tasksElem))
            {
                throw new InvalidOperationException("Fallback JSON does not contain 'data.tasks'.");
            }

            var tasks = new List<TarkovTrackerTask>();
            var tasksById = new Dictionary<string, TarkovTrackerTask>(StringComparer.OrdinalIgnoreCase);

            // In fallback, data.tasks is a dictionary/object keyed by task ID
            if (tasksElem.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in tasksElem.EnumerateObject())
                {
                    try
                    {
                        var task = JsonSerializer.Deserialize<TarkovTrackerTask>(prop.Value.GetRawText(), options);
                        if (task != null)
                        {
                            if (string.IsNullOrEmpty(task.Id)) task.Id = prop.Name;
                            tasks.Add(task);
                            tasksById[task.Id] = task;
                        }
                    }
                    catch
                    {
                        // Skip corrupted task entry in fallback
                    }
                }
            }
            else if (tasksElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in tasksElem.EnumerateArray())
                {
                    var task = JsonSerializer.Deserialize<TarkovTrackerTask>(elem.GetRawText(), options);
                    if (task != null)
                    {
                        tasks.Add(task);
                        if (!string.IsNullOrEmpty(task.Id))
                            tasksById[task.Id] = task;
                    }
                }
            }

            // Post-process fallback tasks: clean titles, resolve trader names, map 27 global variable pool thresholds
            foreach (var task in tasks)
            {
                // Clean title from wikiLink if name looks like a localization template (e.g. "657315ddab5a49b71f098853 name")
                if (!string.IsNullOrEmpty(task.Name) && (task.Name.EndsWith(" name", StringComparison.OrdinalIgnoreCase) || task.Name.Length == 24))
                {
                    var titleFromWiki = ExtractTitleFromWikiLink(task.WikiLink);
                    if (!string.IsNullOrEmpty(titleFromWiki))
                    {
                        task.Name = titleFromWiki;
                    }
                    else if (!string.IsNullOrEmpty(task.NormalizedName))
                    {
                        task.Name = FormatNormalizedNameToTitle(task.NormalizedName);
                    }
                }

                // Resolve trader name from TraderIdToName if Trader.Name is empty
                if (task.Trader != null && string.IsNullOrEmpty(task.Trader.Name) && !string.IsNullOrEmpty(task.Trader.Id))
                {
                    if (TraderIdToName.TryGetValue(task.Trader.Id, out var resolvedTraderName))
                    {
                        task.Trader.Name = resolvedTraderName;
                    }
                }

                // Process otherRequirements using the 27 Global Variable registry
                if (task.OtherRequirements != null)
                {
                    foreach (var otherReq in task.OtherRequirements)
                    {
                        if (string.Equals(otherReq.Type, "globalVariable", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrEmpty(otherReq.VariableId))
                        {
                            if (GlobalVariableRegistry.TryGetValue(otherReq.VariableId, out var poolInfo))
                            {
                                otherReq.TaskPool = new TarkovTrackerTaskPool
                                {
                                    TraderId = poolInfo.TraderId,
                                    LoyaltyLevel = poolInfo.LoyaltyLevel,
                                    Verification = "verified",
                                    Coverage = "complete"
                                };
                            }
                        }
                    }
                }

                // Resolve task requirement prerequisite titles if missing
                if (task.TaskRequirements != null)
                {
                    foreach (var req in task.TaskRequirements)
                    {
                        if (req.Task != null && string.IsNullOrEmpty(req.Task.Name) && !string.IsNullOrEmpty(req.Task.Id))
                        {
                            if (tasksById.TryGetValue(req.Task.Id, out var prereqTask))
                            {
                                req.Task.Name = prereqTask.Name;
                                req.Task.WikiLink = prereqTask.WikiLink;
                            }
                        }
                    }
                }
            }

            return new RawTarkovTrackerData
            {
                ExtractedAt = DateTime.UtcNow,
                Source = "json.tarkov.dev Fallback",
                Tasks = tasks
            };
        }

        /// <summary>
        /// Extracts clean English quest title from a fandom Wiki URL.
        /// Example: "https://escapefromtarkov.fandom.com/wiki/First_in_Line" -> "First in Line"
        /// </summary>
        public static string ExtractTitleFromWikiLink(string? wikiLink)
        {
            if (string.IsNullOrWhiteSpace(wikiLink))
                return string.Empty;

            var lastSegment = wikiLink.Trim().TrimEnd('/').Split('/').LastOrDefault();
            if (string.IsNullOrEmpty(lastSegment))
                return string.Empty;

            var unescaped = Uri.UnescapeDataString(lastSegment).Replace('_', ' ').Trim();
            return unescaped;
        }

        /// <summary>
        /// Converts a kebab-case normalized name (e.g. "first-in-line") to Title Case ("First In Line").
        /// </summary>
        public static string FormatNormalizedNameToTitle(string normalizedName)
        {
            if (string.IsNullOrWhiteSpace(normalizedName))
                return string.Empty;

            var words = normalizedName.Split('-', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Select(w =>
                w.Length > 0 ? char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant() : string.Empty));
        }

        #endregion
    }
}
