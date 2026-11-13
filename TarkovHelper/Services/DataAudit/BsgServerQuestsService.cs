using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services.DataAudit
{
    /// <summary>
    /// Service for fetching, caching, and parsing the authoritative BSG server quest templates (quests.json).
    /// Provides zero-friction fallback to local embedded cache if remote connectivity fails.
    /// </summary>
    public class BsgServerQuestsService : IDisposable
    {
        private static readonly ILogger _log = Log.For<BsgServerQuestsService>();
        private static BsgServerQuestsService? _instance;
        public static BsgServerQuestsService Instance => _instance ??= new BsgServerQuestsService();

        public const string RemoteUrl = "https://raw.githubusercontent.com/sp-tarkov/server/master/project/assets/database/templates/quests.json";
        public const string UserAgent = "TarkovHelper/1.5.7 (+https://github.com/TarkovHelper)";

        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private bool _disposed;

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(12);

        public BsgServerQuestsService(HttpClient? httpClient = null)
        {
            if (httpClient != null)
            {
                _httpClient = httpClient;
                _ownsHttpClient = false;
            }
            else
            {
                _httpClient = new HttpClient();
                _ownsHttpClient = true;
            }
        }

        private static string GetLocalCachePath()
        {
            // Primary: AppData writable cache
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appDataDir = Path.Combine(appData, "TarkovHelper", "Data");
            var appDataPath = Path.Combine(appDataDir, "bsg_quests_cache.json");

            if (File.Exists(appDataPath))
            {
                return appDataPath;
            }

            // Fallback: Bundled Assets cache in BaseDirectory
            var bundledPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Data", "bsg_quests_cache.json");
            if (File.Exists(bundledPath))
            {
                return bundledPath;
            }

            // Fallback for development / project structure
            var devPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "DB", "Data", "bsg_quests_cache.json");
            return devPath;
        }

        /// <summary>
        /// Loads authoritative BSG server quests. Attempts remote update check first; falls back cleanly to local cache.
        /// </summary>
        public async Task<(Dictionary<string, BsgServerQuest> Quests, string SourceUsed, bool IsOffline, string? FailureReason)> LoadQuestsAsync(
            IProgress<string>? progress = null, 
            CancellationToken ct = default)
        {
            progress?.Report("Checking BSG quest server...");
            _log.Info("Starting BSG server quests retrieval...");

            string? failureReason = null;
            string sourceUsed = "Remote GitHub (Live)";
            bool isOffline = false;

            // 1. Try remote fetch
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(Timeout);

                using var request = new HttpRequestMessage(HttpMethod.Get, RemoteUrl);
                request.Headers.Add("User-Agent", UserAgent);

                // Optional ETag check
                var etag = SettingsService.Instance.GetValue("bsg_server.etag", string.Empty);
                if (!string.IsNullOrEmpty(etag))
                {
                    request.Headers.IfNoneMatch.TryParseAdd(etag);
                }

                progress?.Report("Downloading official BSG quest dump...");
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
                {
                    _log.Info("BSG server quests cache is up-to-date (HTTP 304). Using cached data.");
                    sourceUsed = "Local Cache (Verified 304 Up-to-date)";
                    var cachedQuests = await LoadFromCacheFileAsync(GetLocalCachePath(), ct);
                    if (cachedQuests != null && cachedQuests.Count > 0)
                    {
                        return (cachedQuests, sourceUsed, false, null);
                    }
                }
                else if (response.IsSuccessStatusCode)
                {
                    var newEtag = response.Headers.ETag?.Tag;
                    if (!string.IsNullOrEmpty(newEtag))
                    {
                        SettingsService.Instance.SetValue("bsg_server.etag", newEtag);
                    }

                    progress?.Report("Parsing official quest data...");
                    await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                    var quests = await JsonSerializer.DeserializeAsync<Dictionary<string, BsgServerQuest>>(stream, cancellationToken: cts.Token);

                    if (quests != null && quests.Count > 0)
                    {
                        _log.Info($"Successfully loaded {quests.Count} quests from remote BSG server repository.");

                        // Persist to AppData writable cache asynchronously in background
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                                var dir = Path.Combine(appData, "TarkovHelper", "Data");
                                Directory.CreateDirectory(dir);
                                var dest = Path.Combine(dir, "bsg_quests_cache.json");
                                await using var writeStream = File.Create(dest);
                                await JsonSerializer.SerializeAsync(writeStream, quests);
                            }
                            catch (Exception ex)
                            {
                                _log.Warning($"Failed to save local AppData cache for BSG server quests: {ex.Message}");
                            }
                        }, CancellationToken.None);

                        return (quests, sourceUsed, false, null);
                    }
                }
                else
                {
                    failureReason = $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) al conectar con GitHub";
                    _log.Warning($"Remote BSG server quests fetch returned non-success: {failureReason}");
                }
            }
            catch (OperationCanceledException)
            {
                failureReason = "Tiempo de espera agotado al conectar con GitHub (Timeout > 12s)";
                _log.Warning(failureReason);
            }
            catch (Exception ex)
            {
                failureReason = $"Error de conexión con GitHub: {ex.Message}";
                _log.Warning(failureReason);
            }

            // 2. Resilient fallback to local cache
            isOffline = true;
            sourceUsed = "Local Backup Cache";
            progress?.Report("Using local copy of official BSG quests...");
            _log.Info($"Falling back to local cache due to: {failureReason}");

            var fallbackPath = GetLocalCachePath();
            if (File.Exists(fallbackPath))
            {
                var cached = await LoadFromCacheFileAsync(fallbackPath, ct);
                if (cached != null && cached.Count > 0)
                {
                    _log.Info($"Loaded {cached.Count} quests from local fallback cache ({fallbackPath}).");
                    return (cached, sourceUsed, isOffline, failureReason);
                }
            }

            _log.Error("Both remote fetch and local fallback cache failed for BSG server quests.");
            return (new Dictionary<string, BsgServerQuest>(), "None", true, failureReason ?? "Caché local no encontrada");
        }

        private static async Task<Dictionary<string, BsgServerQuest>?> LoadFromCacheFileAsync(string filePath, CancellationToken ct)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                await using var stream = File.OpenRead(filePath);
                return await JsonSerializer.DeserializeAsync<Dictionary<string, BsgServerQuest>>(stream, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to parse local cache file '{filePath}': {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }
    }
}
