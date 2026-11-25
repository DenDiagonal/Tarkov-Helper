using TarkovHelper.Debug;
using TarkovHelper.Models;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Service for managing hideout construction progress state
    /// </summary>
    public class HideoutProgressService
    {
        private static readonly ILogger _log = Log.For<HideoutProgressService>();
        private static HideoutProgressService? _instance;
        private static readonly object _instanceLock = new();

        public static HideoutProgressService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        _instance ??= new HideoutProgressService();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// 싱글톤 인스턴스를 파괴하여 은신처 진행 데이터를 초기화합니다.
        /// </summary>
        public static void ResetInstance()
        {
            HideoutProgressService? old;
            lock (_instanceLock)
            {
                old = _instance;
                _instance = null;
            }
            if (old != null)
            {
                try { old.FlushPendingSavesAsync().GetAwaiter().GetResult(); } catch { }
            }
        }
        private ProfileType _loadedProfile = ProfileType.Pvp;
        private readonly object _lock = new();
        private Task _activeSaveTask = Task.CompletedTask;

        private readonly UserDataDbService _userDataDb = UserDataDbService.Instance;

        // Currency items should count by reference count, not total amount
        private static readonly HashSet<string> CurrencyItems = new(StringComparer.OrdinalIgnoreCase)
        {
            "roubles", "dollars", "euros"
        };

        private static bool IsCurrency(string normalizedName) => CurrencyItems.Contains(normalizedName);

        private HideoutProgress _progress = new();
        private Dictionary<string, HideoutModule> _modulesByNormalizedName = new();
        private List<HideoutModule> _allModules = new();

        public event EventHandler? ProgressChanged;

        /// <summary>
        /// Initialize service with hideout module data (Asynchronous)
        /// </summary>
        public async Task InitializeAsync(List<HideoutModule> modules)
        {
            _allModules = modules;
            _loadedProfile = ProfileService.Instance.CurrentProfile;

            // Build dictionary by normalized name
            _modulesByNormalizedName = new Dictionary<string, HideoutModule>(StringComparer.OrdinalIgnoreCase);
            foreach (var module in modules.Where(m => !string.IsNullOrEmpty(m.NormalizedName)))
            {
                if (!_modulesByNormalizedName.ContainsKey(module.NormalizedName))
                {
                    _modulesByNormalizedName[module.NormalizedName] = module;
                }
            }

            await LoadProgressAsync();
        }

        public void Initialize(List<HideoutModule> modules)
        {
            _allModules = modules;
            lock (_lock)
            {
                _loadedProfile = ProfileService.Instance.CurrentProfile;
            }
            _modulesByNormalizedName = new Dictionary<string, HideoutModule>(StringComparer.OrdinalIgnoreCase);
            foreach (var module in modules.Where(m => !string.IsNullOrEmpty(m.NormalizedName)))
            {
                if (!_modulesByNormalizedName.ContainsKey(module.NormalizedName))
                {
                    _modulesByNormalizedName[module.NormalizedName] = module;
                }
            }
            _ = LoadProgressFromDbAsync();
        }

        /// <summary>
        /// Get all modules
        /// </summary>
        public IReadOnlyList<HideoutModule> AllModules => _allModules;

        /// <summary>
        /// Get module by normalized name
        /// </summary>
        public HideoutModule? GetModule(string normalizedName)
        {
            return _modulesByNormalizedName.TryGetValue(normalizedName, out var module) ? module : null;
        }

        /// <summary>
        /// Get current level for a module (0 = not built)
        /// </summary>
        public int GetCurrentLevel(HideoutModule module)
        {
            if (string.IsNullOrEmpty(module.NormalizedName))
                return 0;

            lock (_lock)
            {
                return _progress.Modules.TryGetValue(module.NormalizedName, out var level) ? level : 0;
            }
        }

        /// <summary>
        /// Get current level for a module by normalized name
        /// </summary>
        public int GetCurrentLevel(string normalizedName)
        {
            lock (_lock)
            {
                return _progress.Modules.TryGetValue(normalizedName, out var level) ? level : 0;
            }
        }

        /// <summary>
        /// Set current level for a module
        /// </summary>
        public void SetLevel(HideoutModule module, int level)
        {
            if (string.IsNullOrEmpty(module.NormalizedName))
                return;

            // Clamp level between 0 and max level
            level = Math.Max(0, Math.Min(level, module.MaxLevel));
            ProfileType targetProfile;

            lock (_lock)
            {
                targetProfile = _loadedProfile;
                if (level == 0)
                {
                    _progress.Modules.Remove(module.NormalizedName);
                }
                else
                {
                    _progress.Modules[module.NormalizedName] = level;
                }

                _progress.LastUpdated = DateTime.UtcNow;
            }

            SaveSingleModule(module.NormalizedName, level, targetProfile);
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SaveSingleModule(string normalizedName, int level, ProfileType targetProfile)
        {
            _activeSaveTask = Task.Run(async () =>
            {
                try
                {
                    await _userDataDb.SaveHideoutProgressAsync(normalizedName, level, targetProfile).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Error($"Save failed: {ex.Message}");
                }
            });
        }

        public async Task FlushPendingSavesAsync()
        {
            try
            {
                await _activeSaveTask.ConfigureAwait(false);
            }
            catch { }
        }

        /// <summary>
        /// Increment level for a module
        /// </summary>
        public void IncrementLevel(HideoutModule module)
        {
            var currentLevel = GetCurrentLevel(module);
            if (currentLevel < module.MaxLevel)
            {
                SetLevel(module, currentLevel + 1);
            }
        }

        /// <summary>
        /// Decrement level for a module
        /// </summary>
        public void DecrementLevel(HideoutModule module)
        {
            var currentLevel = GetCurrentLevel(module);
            if (currentLevel > 0)
            {
                SetLevel(module, currentLevel - 1);
            }
        }

        /// <summary>
        /// Get next level requirements for a module
        /// </summary>
        public HideoutLevel? GetNextLevel(HideoutModule module)
        {
            var currentLevel = GetCurrentLevel(module);
            return module.Levels.FirstOrDefault(l => l.Level == currentLevel + 1);
        }

        /// <summary>
        /// Get total remaining item requirements for a module (all levels after current)
        /// </summary>
        public Dictionary<string, (HideoutItemRequirement Item, int TotalCount, int FIRCount)> GetRemainingItemRequirements(HideoutModule module)
        {
            var currentLevel = GetCurrentLevel(module);
            var result = new Dictionary<string, (HideoutItemRequirement Item, int TotalCount, int FIRCount)>(StringComparer.OrdinalIgnoreCase);

            foreach (var level in module.Levels.Where(l => l.Level > currentLevel))
            {
                foreach (var itemReq in level.ItemRequirements)
                {
                    if (result.TryGetValue(itemReq.ItemNormalizedName, out var existing))
                    {
                        var newFirCount = existing.FIRCount + (itemReq.FoundInRaid ? itemReq.Count : 0);
                        result[itemReq.ItemNormalizedName] = (existing.Item, existing.TotalCount + itemReq.Count, newFirCount);
                    }
                    else
                    {
                        var firCount = itemReq.FoundInRaid ? itemReq.Count : 0;
                        result[itemReq.ItemNormalizedName] = (itemReq, itemReq.Count, firCount);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get total item requirements for all incomplete hideout modules
        /// </summary>
        public Dictionary<string, HideoutItemAggregate> GetAllRemainingItemRequirements()
        {
            var result = new Dictionary<string, HideoutItemAggregate>(StringComparer.OrdinalIgnoreCase);

            foreach (var module in _allModules)
            {
                var currentLevel = GetCurrentLevel(module);

                foreach (var level in module.Levels.Where(l => l.Level > currentLevel))
                {
                    foreach (var itemReq in level.ItemRequirements)
                    {
                        // For currency items, count by reference (1 per hideout level) instead of total amount
                        var countToAdd = IsCurrency(itemReq.ItemNormalizedName) ? 1 : itemReq.Count;
                        var firCountToAdd = itemReq.FoundInRaid ? countToAdd : 0;

                        if (result.TryGetValue(itemReq.ItemNormalizedName, out var existing))
                        {
                            existing.HideoutCount += countToAdd;
                            existing.TotalCount += countToAdd;
                            // Track FIR count separately
                            if (itemReq.FoundInRaid)
                            {
                                existing.HideoutFIRCount += countToAdd;
                                existing.TotalFIRCount += countToAdd;
                                existing.FoundInRaid = true;
                            }
                        }
                        else
                        {
                            result[itemReq.ItemNormalizedName] = new HideoutItemAggregate
                            {
                                ItemId = itemReq.ItemId,
                                ItemName = itemReq.ItemName,
                                ItemNameKo = itemReq.ItemNameKo,
                                ItemNameJa = itemReq.ItemNameJa,
                                ItemNormalizedName = itemReq.ItemNormalizedName,
                                IconLink = itemReq.IconLink,
                                HideoutCount = countToAdd,
                                HideoutFIRCount = firCountToAdd,
                                QuestCount = 0,
                                QuestFIRCount = 0,
                                TotalCount = countToAdd,
                                TotalFIRCount = firCountToAdd,
                                FoundInRaid = itemReq.FoundInRaid
                            };
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Check if all prerequisites are met for building a specific level
        /// </summary>
        public bool ArePrerequisitesMet(HideoutModule module, int targetLevel)
        {
            var level = module.Levels.FirstOrDefault(l => l.Level == targetLevel);
            if (level == null)
                return false;

            // Check station level requirements
            foreach (var stationReq in level.StationLevelRequirements)
            {
                var requiredStationLevel = GetCurrentLevel(stationReq.StationId);
                if (requiredStationLevel < stationReq.Level)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Get construction statistics
        /// </summary>
        public HideoutStatistics GetStatistics()
        {
            var stats = new HideoutStatistics();

            foreach (var module in _allModules)
            {
                var currentLevel = GetCurrentLevel(module);
                var maxLevel = module.MaxLevel;

                stats.TotalModules++;
                stats.TotalLevels += maxLevel;
                stats.CompletedLevels += currentLevel;

                if (currentLevel == 0)
                    stats.NotStarted++;
                else if (currentLevel >= maxLevel)
                    stats.FullyCompleted++;
                else
                    stats.InProgress++;
            }

            return stats;
        }

        /// <summary>
        /// Reset all hideout progress
        /// </summary>
        public void ResetAllProgress()
        {
            ProfileType targetProfile;
            lock (_lock)
            {
                targetProfile = _loadedProfile;
                _progress = new HideoutProgress();
            }
            _activeSaveTask = Task.Run(async () =>
            {
                try
                {
                    await _userDataDb.ClearAllHideoutProgressAsync(targetProfile).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Error($"Reset failed: {ex.Message}");
                }
            });
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 강제로 진행도를 다시 로드 (프로필 전환 시 사용)
        /// </summary>
        public async Task ReloadProgressAsync()
        {
            var profile = ProfileService.Instance.CurrentProfile;
            lock (_lock)
            {
                _loadedProfile = profile;
            }
            await LoadProgressFromDbAsync();
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        #region Persistence

        private void SaveProgress()
        {
            _ = SaveProgressToDbAsync();
        }

        private async Task SaveProgressToDbAsync()
        {
            try
            {
                List<KeyValuePair<string, int>> snapshot;
                ProfileType targetProfile;
                lock (_lock)
                {
                    targetProfile = _loadedProfile;
                    snapshot = _progress.Modules.ToList();
                }
                foreach (var kvp in snapshot)
                {
                    await _userDataDb.SaveHideoutProgressAsync(kvp.Key, kvp.Value, targetProfile).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Save failed: {ex.Message}");
            }
        }

        public async Task LoadProgressAsync()
        {
            await LoadProgressFromDbAsync();
        }

        private void LoadProgress()
        {
            _ = LoadProgressFromDbAsync();
        }

        private async Task LoadProgressFromDbAsync()
        {
            ProfileType targetProfile;
            lock (_lock)
            {
                targetProfile = _loadedProfile;
            }
            try
            {
                var modules = await _userDataDb.LoadHideoutProgressAsync(targetProfile).ConfigureAwait(false);
                lock (_lock)
                {
                    if (targetProfile != _loadedProfile) return;
                    _progress = new HideoutProgress
                    {
                        Modules = new Dictionary<string, int>(modules, StringComparer.OrdinalIgnoreCase),
                        LastUpdated = DateTime.UtcNow
                    };
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Load failed: {ex.Message}");
                lock (_lock)
                {
                    if (targetProfile == _loadedProfile)
                        _progress = new HideoutProgress();
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Aggregated item requirement from hideout
    /// </summary>
    public class HideoutItemAggregate
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string? ItemNameKo { get; set; }
        public string? ItemNameJa { get; set; }
        public string ItemNormalizedName { get; set; } = string.Empty;
        public string? IconLink { get; set; }
        public int QuestCount { get; set; }
        public int QuestFIRCount { get; set; }
        public int HideoutCount { get; set; }
        public int HideoutFIRCount { get; set; }
        public int TotalCount { get; set; }
        public int TotalFIRCount { get; set; }
        public bool FoundInRaid { get; set; }
    }

    /// <summary>
    /// Hideout construction statistics
    /// </summary>
    public class HideoutStatistics
    {
        public int TotalModules { get; set; }
        public int NotStarted { get; set; }
        public int InProgress { get; set; }
        public int FullyCompleted { get; set; }
        public int TotalLevels { get; set; }
        public int CompletedLevels { get; set; }
    }
}
