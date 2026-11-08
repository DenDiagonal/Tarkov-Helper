using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TarkovHelper.Models;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services
{
    public class NeededItemProgressChangedEventArgs : EventArgs
    {
        public string NeedKey { get; }
        public int Count { get; }
        public int MaxCount { get; }
        public bool IsComplete { get; }

        public NeededItemProgressChangedEventArgs(string needKey, int count, int maxCount, bool isComplete)
        {
            NeedKey = needKey;
            Count = count;
            MaxCount = maxCount;
            IsComplete = isComplete;
        }
    }

    /// <summary>
    /// Service managing interactive progress (0/N counts) for needed items required across quests
    /// and hideout modules. Modeled on TarkovTracker's open-source need progress architecture (useNeededItemProgress).
    /// </summary>
    public sealed class NeededItemProgressService
    {
        private static readonly ILogger _log = Log.For<NeededItemProgressService>();
        private static NeededItemProgressService? _instance;
        public static NeededItemProgressService Instance => _instance ??= new NeededItemProgressService();

        public static void ResetInstance()
        {
            _instance = null;
        }

        private readonly UserDataDbService _userDataDb;
        private readonly ProfileService _profileService;
        private readonly object _lock = new();

        private Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
        private ProfileType _loadedProfile = ProfileType.Pvp;
        private bool _isLoaded;

        // Debounce timer for persistent saving
        private System.Timers.Timer? _saveTimer;
        private readonly HashSet<string> _pendingSaves = new(StringComparer.OrdinalIgnoreCase);
        private readonly System.Threading.SemaphoreSlim _saveLock = new(1, 1);
        private Task _activeSaveTask = Task.CompletedTask;

        public bool IsLoaded => _isLoaded;
        public event EventHandler<NeededItemProgressChangedEventArgs>? ProgressChanged;

        private NeededItemProgressService()
        {
            _userDataDb = UserDataDbService.Instance;
            _profileService = ProfileService.Instance;
            _profileService.ProfileChanged += OnProfileChanged;

            InitializeSaveTimer();
        }

        private void InitializeSaveTimer()
        {
            _saveTimer = new System.Timers.Timer(400); // 400ms debounce
            _saveTimer.AutoReset = false;
            _saveTimer.Elapsed += (s, e) => SavePendingItems();
        }

        private void OnProfileChanged(object? sender, ProfileType newProfile)
        {
            FlushPendingSaves();

            lock (_lock)
            {
                _loadedProfile = newProfile;
                _counts.Clear();
                _pendingSaves.Clear();
                _isLoaded = false;
            }
            _ = LoadProgressAsync(newProfile);
        }

        public async Task InitializeAsync()
        {
            var profile = _profileService.CurrentProfile;
            _loadedProfile = profile;
            await LoadProgressAsync(profile);
        }

        public async Task LoadProgressAsync(ProfileType? profile = null)
        {
            var targetProfile = profile ?? _profileService.CurrentProfile;
            lock (_lock)
            {
                if (targetProfile != _profileService.CurrentProfile) return;
                _loadedProfile = targetProfile;
            }
            try
            {
                var storedCounts = await _userDataDb.LoadNeededItemProgressAsync(targetProfile).ConfigureAwait(false);
                lock (_lock)
                {
                    // If active profile has changed during the async load, discard stale result
                    if (targetProfile != _loadedProfile || targetProfile != _profileService.CurrentProfile) return;

                    var merged = new Dictionary<string, int>(storedCounts, StringComparer.OrdinalIgnoreCase);
                    // Preserve any edits made while loading was in flight
                    foreach (var pendingKey in _pendingSaves)
                    {
                        if (_counts.TryGetValue(pendingKey, out var count))
                        {
                            if (count > 0)
                                merged[pendingKey] = count;
                            else
                                merged.Remove(pendingKey);
                        }
                    }
                    _counts = merged;
                    _isLoaded = true;
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to load needed item progress from DB: {ex.Message}");
                lock (_lock)
                {
                    if (targetProfile == _loadedProfile)
                    {
                        _counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        _isLoaded = true;
                    }
                }
            }
        }

        public static string GetQuestItemKey(string questNormalizedName, string itemNormalizedName, int? dogtagMinLevel = null)
        {
            var normQuest = (questNormalizedName ?? "").Trim().ToLowerInvariant();
            var normItem = (itemNormalizedName ?? "").Trim().ToLowerInvariant();
            if (dogtagMinLevel.HasValue && dogtagMinLevel.Value > 0)
            {
                return $"quest:{normQuest}:{normItem}:lv{dogtagMinLevel.Value}";
            }
            return $"quest:{normQuest}:{normItem}";
        }

        public static string GetHideoutItemKey(string stationId, int level, string itemNormalizedName)
        {
            var normStation = (stationId ?? "").Trim().ToLowerInvariant();
            var normItem = (itemNormalizedName ?? "").Trim().ToLowerInvariant();
            return $"hideout:{normStation}:{level}:{normItem}";
        }

        public int GetCount(string needKey)
        {
            if (string.IsNullOrEmpty(needKey)) return 0;

            lock (_lock)
            {
                return _counts.TryGetValue(needKey, out var count) ? count : 0;
            }
        }

        public void SetCount(string needKey, int count, int maxCount)
        {
            if (string.IsNullOrEmpty(needKey)) return;

            var clamped = Math.Clamp(count, 0, Math.Max(0, maxCount));
            bool changed = false;

            lock (_lock)
            {
                _counts.TryGetValue(needKey, out var current);
                if (current != clamped)
                {
                    if (clamped == 0)
                    {
                        _counts.Remove(needKey);
                    }
                    else
                    {
                        _counts[needKey] = clamped;
                    }
                    changed = true;
                    ScheduleSave(needKey);
                }
            }

            if (changed)
            {
                var isComplete = (clamped >= maxCount && maxCount > 0);
                ProgressChanged?.Invoke(this, new NeededItemProgressChangedEventArgs(needKey, clamped, maxCount, isComplete));
            }
        }

        public int Increment(string needKey, int maxCount)
        {
            var cur = GetCount(needKey);
            if (cur < maxCount)
            {
                SetCount(needKey, cur + 1, maxCount);
                return cur + 1;
            }
            return cur;
        }

        public int Decrement(string needKey, int maxCount = int.MaxValue)
        {
            var cur = GetCount(needKey);
            if (cur > 0)
            {
                SetCount(needKey, cur - 1, maxCount);
                return cur - 1;
            }
            return 0;
        }

        public int ToggleComplete(string needKey, int maxCount)
        {
            var cur = GetCount(needKey);
            int target;
            if (cur >= maxCount)
            {
                target = 0;
            }
            else
            {
                target = maxCount;
            }
            SetCount(needKey, target, maxCount);
            return target;
        }

        public bool IsComplete(string needKey, int maxCount)
        {
            if (maxCount <= 0) return true;
            return GetCount(needKey) >= maxCount;
        }

        private void ScheduleSave(string needKey)
        {
            lock (_lock)
            {
                _pendingSaves.Add(needKey);
                _saveTimer?.Stop();
                _saveTimer?.Start();
            }
        }

        private void SavePendingItems()
        {
            List<(string Key, int Count)> itemsToSave;
            ProfileType profile;

            lock (_lock)
            {
                if (_pendingSaves.Count == 0) return;
                profile = _loadedProfile;

                itemsToSave = new List<(string Key, int Count)>();
                foreach (var key in _pendingSaves)
                {
                    _counts.TryGetValue(key, out var count);
                    itemsToSave.Add((key, count));
                }
                _pendingSaves.Clear();

                _activeSaveTask = Task.Run(async () =>
                {
                    await _saveLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        await _userDataDb.SaveNeededItemProgressBatchAsync(itemsToSave, profile).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"Save failed for pending needed items: {ex.Message}");
                    }
                    finally
                    {
                        _saveLock.Release();
                    }
                });
            }
        }

        public void FlushPendingSaves()
        {
            List<(string Key, int Count)> itemsToSave = new();
            ProfileType profile;
            Task priorTask;

            lock (_lock)
            {
                _saveTimer?.Stop();
                profile = _loadedProfile;
                priorTask = _activeSaveTask;

                if (_pendingSaves.Count > 0)
                {
                    foreach (var key in _pendingSaves)
                    {
                        _counts.TryGetValue(key, out var count);
                        itemsToSave.Add((key, count));
                    }
                    _pendingSaves.Clear();
                }
            }

            try
            {
                Task.Run(async () =>
                {
                    try { await priorTask.ConfigureAwait(false); } catch { }

                    if (itemsToSave.Count > 0)
                    {
                        await _saveLock.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            await _userDataDb.SaveNeededItemProgressBatchAsync(itemsToSave, profile).ConfigureAwait(false);
                        }
                        finally
                        {
                            _saveLock.Release();
                        }
                    }
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _log.Error($"Flush failed for needed items: {ex.Message}");
            }
        }

        public async Task FlushPendingSavesAsync()
        {
            List<(string Key, int Count)> itemsToSave = new();
            ProfileType profile;
            Task priorTask;

            lock (_lock)
            {
                _saveTimer?.Stop();
                profile = _loadedProfile;
                priorTask = _activeSaveTask;

                if (_pendingSaves.Count > 0)
                {
                    foreach (var key in _pendingSaves)
                    {
                        _counts.TryGetValue(key, out var count);
                        itemsToSave.Add((key, count));
                    }
                    _pendingSaves.Clear();
                }
            }

            try
            {
                try { await priorTask.ConfigureAwait(false); } catch { }

                if (itemsToSave.Count > 0)
                {
                    await _saveLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        await _userDataDb.SaveNeededItemProgressBatchAsync(itemsToSave, profile).ConfigureAwait(false);
                    }
                    finally
                    {
                        _saveLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"FlushAsync failed for needed items: {ex.Message}");
            }
        }

        public async Task ClearAllProgressAsync(ProfileType? profile = null)
        {
            var targetProfile = profile ?? _profileService.CurrentProfile;
            Task priorTask;
            lock (_lock)
            {
                _saveTimer?.Stop();
                _pendingSaves.Clear();
                priorTask = _activeSaveTask;
                if (targetProfile == _loadedProfile)
                {
                    _counts.Clear();
                }
            }

            try { await priorTask.ConfigureAwait(false); } catch { }
            await _userDataDb.ClearAllNeededItemProgressAsync(targetProfile);
        }

        public void ClearInMemoryForTesting()
        {
            lock (_lock)
            {
                _saveTimer?.Stop();
                _counts.Clear();
                _pendingSaves.Clear();
                _activeSaveTask = Task.CompletedTask;
            }
        }
    }
}
