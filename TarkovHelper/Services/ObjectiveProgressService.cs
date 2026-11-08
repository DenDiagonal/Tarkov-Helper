using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TarkovHelper.Models;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Service for managing quest objective completion progress
    /// </summary>
    public class ObjectiveProgressService
    {
        private static ObjectiveProgressService? _instance;
        public static ObjectiveProgressService Instance => _instance ??= new ObjectiveProgressService();

        private readonly UserDataDbService _userDataDb = UserDataDbService.Instance;

        // Objective progress: key = "questNormalizedName:objectiveIndex" or "id:objectiveId", value = completed
        private Dictionary<string, bool> _objectiveProgress = new();

        public event EventHandler<ObjectiveProgressChangedEventArgs>? ObjectiveProgressChanged;

        private ObjectiveProgressService()
        {
            ProfileService.Instance.ProfileChanged += (s, newProfile) => _ = LoadObjectiveProgressAsync(newProfile);
        }

        #region Objective Status

        /// <summary>
        /// Get objective completion status by quest name and index
        /// </summary>
        public bool IsObjectiveCompleted(string questNormalizedName, int objectiveIndex)
        {
            var key = $"{questNormalizedName}:{objectiveIndex}";
            if (_objectiveProgress.TryGetValue(key, out var completed) && completed)
                return true;

            // Fallback: check if the corresponding objective has id: completed
            var objectiveId = ResolveObjectiveId(questNormalizedName, objectiveIndex);
            if (!string.IsNullOrEmpty(objectiveId))
            {
                var idKey = $"id:{objectiveId}";
                if (_objectiveProgress.TryGetValue(idKey, out var idCompleted) && idCompleted)
                {
                    _objectiveProgress[key] = true;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Get objective completion status by objective ID
        /// </summary>
        public bool IsObjectiveCompletedById(string? objectiveId)
        {
            if (string.IsNullOrEmpty(objectiveId))
                return false;

            var key = $"id:{objectiveId}";
            if (_objectiveProgress.TryGetValue(key, out var completed) && completed)
                return true;

            // Fallback: look up objective in QuestObjectiveDbService.Instance.AllObjectives
            // and check parent quest index key
            var obj = QuestObjectiveDbService.Instance.AllObjectives
                .FirstOrDefault(o => string.Equals(o.Id, objectiveId, StringComparison.OrdinalIgnoreCase));

            if (obj != null)
            {
                var (questNormName, objIndex) = ResolveQuestAndIndex(obj);
                if (!string.IsNullOrEmpty(questNormName) && objIndex >= 0)
                {
                    var indexKey = $"{questNormName}:{objIndex}";
                    if (_objectiveProgress.TryGetValue(indexKey, out var indexCompleted) && indexCompleted)
                    {
                        _objectiveProgress[key] = true;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Set objective completion status (index based - for Quests tab).
        /// Synchronizes and persists both index key "{questNormalizedName}:{objectiveIndex}"
        /// and ID key "id:{objectiveId}".
        /// </summary>
        public void SetObjectiveCompleted(string questNormalizedName, int objectiveIndex, bool completed, string? objectiveId = null)
        {
            if (string.IsNullOrEmpty(objectiveId))
            {
                objectiveId = ResolveObjectiveId(questNormalizedName, objectiveIndex);
            }

            var indexKey = $"{questNormalizedName}:{objectiveIndex}";
            var keysToSave = new List<(string Key, string? QuestId, bool IsCompleted)>();

            if (completed)
            {
                _objectiveProgress[indexKey] = true;
                keysToSave.Add((indexKey, questNormalizedName, true));

                if (!string.IsNullOrEmpty(objectiveId))
                {
                    var idKey = $"id:{objectiveId}";
                    _objectiveProgress[idKey] = true;
                    keysToSave.Add((idKey, questNormalizedName, true));
                }
            }
            else
            {
                _objectiveProgress.Remove(indexKey);
                keysToSave.Add((indexKey, questNormalizedName, false));

                if (!string.IsNullOrEmpty(objectiveId))
                {
                    var idKey = $"id:{objectiveId}";
                    _objectiveProgress.Remove(idKey);
                    keysToSave.Add((idKey, questNormalizedName, false));
                }
            }

            // Fire-and-forget async save - don't block UI
            _ = SaveObjectiveProgressBatchAsync(keysToSave);
            ObjectiveProgressChanged?.Invoke(this, new ObjectiveProgressChangedEventArgs(questNormalizedName, objectiveId, objectiveIndex, completed));
        }

        /// <summary>
        /// Set objective completion status asynchronously (with awaited database persistence).
        /// </summary>
        public async Task SetObjectiveCompletedAsync(string questNormalizedName, int objectiveIndex, bool completed, string? objectiveId = null)
        {
            if (string.IsNullOrEmpty(objectiveId))
            {
                objectiveId = ResolveObjectiveId(questNormalizedName, objectiveIndex);
            }

            var indexKey = $"{questNormalizedName}:{objectiveIndex}";
            var keysToSave = new List<(string Key, string? QuestId, bool IsCompleted)>();

            if (completed)
            {
                _objectiveProgress[indexKey] = true;
                keysToSave.Add((indexKey, questNormalizedName, true));

                if (!string.IsNullOrEmpty(objectiveId))
                {
                    var idKey = $"id:{objectiveId}";
                    _objectiveProgress[idKey] = true;
                    keysToSave.Add((idKey, questNormalizedName, true));
                }
            }
            else
            {
                _objectiveProgress.Remove(indexKey);
                keysToSave.Add((indexKey, questNormalizedName, false));

                if (!string.IsNullOrEmpty(objectiveId))
                {
                    var idKey = $"id:{objectiveId}";
                    _objectiveProgress.Remove(idKey);
                    keysToSave.Add((idKey, questNormalizedName, false));
                }
            }

            await SaveObjectiveProgressBatchAsync(keysToSave);
            ObjectiveProgressChanged?.Invoke(this, new ObjectiveProgressChangedEventArgs(questNormalizedName, objectiveId, objectiveIndex, completed));
        }

        /// <summary>
        /// Set objective completion status by objective ID (for Map Tracker).
        /// Synchronizes and persists both ID key "id:{objectiveId}"
        /// and index key "{questNormalizedName}:{objectiveIndex}".
        /// </summary>
        public void SetObjectiveCompletedById(string objectiveId, bool completed, string? questNormalizedName = null, int objectiveIndex = -1)
        {
            if (string.IsNullOrEmpty(questNormalizedName) || objectiveIndex < 0)
            {
                var obj = QuestObjectiveDbService.Instance.AllObjectives
                    .FirstOrDefault(o => string.Equals(o.Id, objectiveId, StringComparison.OrdinalIgnoreCase));

                if (obj != null)
                {
                    var resolved = ResolveQuestAndIndex(obj);
                    if (string.IsNullOrEmpty(questNormalizedName))
                        questNormalizedName = resolved.QuestNormalizedName;
                    if (objectiveIndex < 0)
                        objectiveIndex = resolved.ObjectiveIndex;
                }
            }

            var idKey = $"id:{objectiveId}";
            var keysToSave = new List<(string Key, string? QuestId, bool IsCompleted)>();

            if (completed)
            {
                _objectiveProgress[idKey] = true;
                keysToSave.Add((idKey, questNormalizedName, true));

                if (!string.IsNullOrEmpty(questNormalizedName) && objectiveIndex >= 0)
                {
                    var indexKey = $"{questNormalizedName}:{objectiveIndex}";
                    _objectiveProgress[indexKey] = true;
                    keysToSave.Add((indexKey, questNormalizedName, true));
                }
            }
            else
            {
                _objectiveProgress.Remove(idKey);
                keysToSave.Add((idKey, questNormalizedName, false));

                if (!string.IsNullOrEmpty(questNormalizedName) && objectiveIndex >= 0)
                {
                    var indexKey = $"{questNormalizedName}:{objectiveIndex}";
                    _objectiveProgress.Remove(indexKey);
                    keysToSave.Add((indexKey, questNormalizedName, false));
                }
            }

            // Fire-and-forget async save - don't block UI
            _ = SaveObjectiveProgressBatchAsync(keysToSave);
            ObjectiveProgressChanged?.Invoke(this, new ObjectiveProgressChangedEventArgs(questNormalizedName, objectiveId, objectiveIndex, completed));
        }

        /// <summary>
        /// Set objective completion status by ID asynchronously (with awaited database persistence).
        /// </summary>
        public async Task SetObjectiveCompletedByIdAsync(string objectiveId, bool completed, string? questNormalizedName = null, int objectiveIndex = -1)
        {
            if (string.IsNullOrEmpty(questNormalizedName) || objectiveIndex < 0)
            {
                var obj = QuestObjectiveDbService.Instance.AllObjectives
                    .FirstOrDefault(o => string.Equals(o.Id, objectiveId, StringComparison.OrdinalIgnoreCase));

                if (obj != null)
                {
                    var resolved = ResolveQuestAndIndex(obj);
                    if (string.IsNullOrEmpty(questNormalizedName))
                        questNormalizedName = resolved.QuestNormalizedName;
                    if (objectiveIndex < 0)
                        objectiveIndex = resolved.ObjectiveIndex;
                }
            }

            var idKey = $"id:{objectiveId}";
            var keysToSave = new List<(string Key, string? QuestId, bool IsCompleted)>();

            if (completed)
            {
                _objectiveProgress[idKey] = true;
                keysToSave.Add((idKey, questNormalizedName, true));

                if (!string.IsNullOrEmpty(questNormalizedName) && objectiveIndex >= 0)
                {
                    var indexKey = $"{questNormalizedName}:{objectiveIndex}";
                    _objectiveProgress[indexKey] = true;
                    keysToSave.Add((indexKey, questNormalizedName, true));
                }
            }
            else
            {
                _objectiveProgress.Remove(idKey);
                keysToSave.Add((idKey, questNormalizedName, false));

                if (!string.IsNullOrEmpty(questNormalizedName) && objectiveIndex >= 0)
                {
                    var indexKey = $"{questNormalizedName}:{objectiveIndex}";
                    _objectiveProgress.Remove(indexKey);
                    keysToSave.Add((indexKey, questNormalizedName, false));
                }
            }

            await SaveObjectiveProgressBatchAsync(keysToSave);
            ObjectiveProgressChanged?.Invoke(this, new ObjectiveProgressChangedEventArgs(questNormalizedName, objectiveId, objectiveIndex, completed));
        }

        /// <summary>
        /// Resolve objectiveId given quest normalized name and objective index.
        /// </summary>
        public string? ResolveObjectiveId(string questNormalizedName, int objectiveIndex, string? objectiveDesc = null, TarkovTask? task = null)
        {
            if (string.IsNullOrEmpty(questNormalizedName) || objectiveIndex < 0)
                return null;

            task ??= QuestDbService.Instance.GetQuestByNormalizedName(questNormalizedName)
                   ?? QuestDbService.Instance.GetQuestById(questNormalizedName);

            if (string.IsNullOrEmpty(objectiveDesc) && task?.Objectives != null && objectiveIndex < task.Objectives.Count)
            {
                objectiveDesc = task.Objectives[objectiveIndex];
            }

            var allObjectives = QuestObjectiveDbService.Instance.AllObjectives;
            if (allObjectives.Count == 0)
                return null;

            // Filter objectives belonging to this quest
            var questObjectives = allObjectives.Where(o => MatchesQuest(o, questNormalizedName, task)).ToList();
            if (questObjectives.Count == 0)
                return null;

            // 1. Try match by description
            if (!string.IsNullOrEmpty(objectiveDesc))
            {
                var cleanedDesc = CleanDescription(objectiveDesc);
                var descMatch = questObjectives.FirstOrDefault(o =>
                    string.Equals(CleanDescription(o.Description), cleanedDesc, StringComparison.OrdinalIgnoreCase) ||
                    (cleanedDesc.Length > 5 && CleanDescription(o.Description).Contains(cleanedDesc, StringComparison.OrdinalIgnoreCase)) ||
                    (o.Description.Length > 5 && cleanedDesc.Contains(CleanDescription(o.Description), StringComparison.OrdinalIgnoreCase)));

                if (descMatch != null)
                    return descMatch.Id;
            }

            // 2. Try match by index within quest's objectives
            if (objectiveIndex < questObjectives.Count)
            {
                return questObjectives[objectiveIndex].Id;
            }

            return null;
        }

        /// <summary>
        /// Resolve parent quest normalized name and objective index given a QuestObjective.
        /// </summary>
        public (string? QuestNormalizedName, int ObjectiveIndex) ResolveQuestAndIndex(QuestObjective obj)
        {
            if (obj == null)
                return (null, -1);

            var task = QuestDbService.Instance.GetQuestById(obj.QuestId)
                    ?? QuestDbService.Instance.GetQuestByNormalizedName(obj.QuestId);

            if (task == null && !string.IsNullOrEmpty(obj.QuestName))
            {
                task = QuestDbService.Instance.AllQuests
                    .FirstOrDefault(q => string.Equals(q.Name, obj.QuestName, StringComparison.OrdinalIgnoreCase));
            }

            var questNormalizedName = task?.NormalizedName
                ?? (!string.IsNullOrEmpty(obj.QuestName) ? NormalizeQuestName(obj.QuestName) : obj.QuestId);

            int objectiveIndex = -1;

            // 1. If parent task has objectives, try finding index by description
            if (task?.Objectives != null && task.Objectives.Count > 0 && !string.IsNullOrEmpty(obj.Description))
            {
                var cleanedDesc = CleanDescription(obj.Description);
                objectiveIndex = task.Objectives.FindIndex(d =>
                    string.Equals(CleanDescription(d), cleanedDesc, StringComparison.OrdinalIgnoreCase) ||
                    (cleanedDesc.Length > 5 && CleanDescription(d).Contains(cleanedDesc, StringComparison.OrdinalIgnoreCase)) ||
                    (d.Length > 5 && cleanedDesc.Contains(CleanDescription(d), StringComparison.OrdinalIgnoreCase)));
            }

            // 2. If index not found or task has no objectives, look among AllObjectives for this quest
            if (objectiveIndex < 0)
            {
                var questObjectives = QuestObjectiveDbService.Instance.AllObjectives
                    .Where(o => MatchesQuest(o, questNormalizedName, task))
                    .ToList();

                objectiveIndex = questObjectives.FindIndex(o => string.Equals(o.Id, obj.Id, StringComparison.OrdinalIgnoreCase));
            }

            return (questNormalizedName, objectiveIndex);
        }

        private static bool MatchesQuest(QuestObjective obj, string questNormalizedName, TarkovTask? task)
        {
            if (string.Equals(obj.QuestId, questNormalizedName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (task != null)
            {
                if (task.Ids != null && task.Ids.Any(id => string.Equals(id, obj.QuestId, StringComparison.OrdinalIgnoreCase)))
                    return true;

                if (!string.IsNullOrEmpty(task.NormalizedName) &&
                    string.Equals(task.NormalizedName, obj.QuestId, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (!string.IsNullOrEmpty(task.Name) && !string.IsNullOrEmpty(obj.QuestName) &&
                    string.Equals(task.Name, obj.QuestName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (!string.IsNullOrEmpty(obj.QuestName))
            {
                var norm = NormalizeQuestName(obj.QuestName);
                if (string.Equals(norm, questNormalizedName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string CleanDescription(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var cleaned = WikiMarkupHelper.RemoveOptionalMarker(text).Trim();
            return cleaned.Replace("\r", "").Replace("\n", "").Trim();
        }

        private static string NormalizeQuestName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            return name.ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("'", "")
                .Replace("\"", "")
                .Replace(".", "");
        }

        /// <summary>
        /// Get all completed objective indices for a quest
        /// </summary>
        public HashSet<int> GetCompletedObjectives(string questNormalizedName)
        {
            var result = new HashSet<int>();
            var prefix = $"{questNormalizedName}:";

            foreach (var kvp in _objectiveProgress)
            {
                if (kvp.Key.StartsWith(prefix) && kvp.Value)
                {
                    var indexStr = kvp.Key.Substring(prefix.Length);
                    if (int.TryParse(indexStr, out var index))
                    {
                        result.Add(index);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get objective completion count for a quest
        /// </summary>
        public (int Completed, int Total) GetObjectiveProgress(TarkovTask task)
        {
            if (task.NormalizedName == null || task.Objectives == null)
                return (0, 0);

            var completedSet = GetCompletedObjectives(task.NormalizedName);
            return (completedSet.Count, task.Objectives.Count);
        }

        /// <summary>
        /// Clear all objective progress for a quest
        /// </summary>
        public void ClearObjectiveProgress(string questNormalizedName)
        {
            var prefix = $"{questNormalizedName}:";
            var keysToRemove = _objectiveProgress.Keys.Where(k => k.StartsWith(prefix)).ToList();

            var quest = QuestDbService.Instance.GetQuestByNormalizedName(questNormalizedName);
            var questObjectives = QuestObjectiveDbService.Instance.AllObjectives
                .Where(o => MatchesQuest(o, questNormalizedName, quest))
                .ToList();

            foreach (var obj in questObjectives)
            {
                var idKey = $"id:{obj.Id}";
                if (_objectiveProgress.ContainsKey(idKey))
                {
                    keysToRemove.Add(idKey);
                }
            }

            foreach (var key in keysToRemove)
            {
                _objectiveProgress.Remove(key);
            }

            if (keysToRemove.Count > 0)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await _userDataDb.DeleteObjectiveProgressByQuestAsync(questNormalizedName);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ObjectiveProgressService] Delete failed: {ex.Message}");
                    }
                });

                ObjectiveProgressChanged?.Invoke(this, new ObjectiveProgressChangedEventArgs(questNormalizedName, null, -1, false));
            }
        }

        /// <summary>
        /// Clear in-memory progress for testing purposes only.
        /// </summary>
        internal void ClearInMemoryForTesting()
        {
            _objectiveProgress.Clear();
        }

        /// <summary>
        /// Clear all objective progress across all quests
        /// </summary>
        public void ClearAllProgress()
        {
            _objectiveProgress.Clear();
            Task.Run(async () =>
            {
                try
                {
                    await _userDataDb.ClearAllObjectiveProgressAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ObjectiveProgressService] ClearAll failed: {ex.Message}");
                }
            });
        }

        #endregion

        #region Persistence

        public void FlushPendingSaves()
        {
            SaveObjectiveProgressSync();
        }

        public void SaveObjectiveProgress()
        {
            SaveObjectiveProgressSync();
        }

        public void SaveObjectiveProgressSync()
        {
            try
            {
                Task.Run(async () =>
                {
                    await SaveObjectiveProgressToDbAsync().ConfigureAwait(false);
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ObjectiveProgressService] Sync save failed: {ex.Message}");
            }
        }

        private async Task SaveObjectiveProgressToDbAsync()
        {
            try
            {
                var list = new List<(string Key, string? QuestId, bool IsCompleted)>();
                foreach (var kvp in _objectiveProgress)
                {
                    string? questId = null;
                    if (kvp.Key.Contains(':'))
                    {
                        var parts = kvp.Key.Split(':');
                        if (parts[0] != "id")
                        {
                            questId = parts[0];
                        }
                        else if (parts.Length > 1)
                        {
                            var obj = QuestObjectiveDbService.Instance.AllObjectives
                                .FirstOrDefault(o => string.Equals(o.Id, parts[1], StringComparison.OrdinalIgnoreCase));
                            if (obj != null)
                            {
                                questId = ResolveQuestAndIndex(obj).QuestNormalizedName;
                            }
                        }
                    }
                    list.Add((kvp.Key, questId, kvp.Value));
                }

                if (list.Count > 0)
                {
                    await _userDataDb.SaveObjectiveProgressBatchAsync(list).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ObjectiveProgressService] Save failed: {ex.Message}");
            }
        }

        public async Task SaveObjectiveProgressBatchAsync(List<(string Key, string? QuestId, bool IsCompleted)> items)
        {
            try
            {
                await _userDataDb.SaveObjectiveProgressBatchAsync(items);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ObjectiveProgressService] Batch save failed: {ex.Message}");
            }
        }

        public async Task LoadObjectiveProgressAsync(ProfileType? profile = null)
        {
            await LoadObjectiveProgressFromDbAsync(profile);
        }

        private void LoadObjectiveProgress()
        {
            _ = LoadObjectiveProgressFromDbAsync();
        }

        private async Task LoadObjectiveProgressFromDbAsync(ProfileType? profile = null)
        {
            try
            {
                var targetProfile = profile ?? ProfileService.Instance.CurrentProfile;
                var dbProgress = await _userDataDb.LoadObjectiveProgressAsync(targetProfile);
                _objectiveProgress.Clear();
                foreach (var kvp in dbProgress)
                {
                    _objectiveProgress[kvp.Key] = kvp.Value;
                }
                System.Diagnostics.Debug.WriteLine($"[ObjectiveProgressService] Loaded {_objectiveProgress.Count} objective progress from DB for {targetProfile}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ObjectiveProgressService] Load failed: {ex.Message}");
                _objectiveProgress.Clear();
            }
        }

        #endregion
    }

    /// <summary>
    /// Event args for objective progress changes
    /// </summary>
    public class ObjectiveProgressChangedEventArgs : EventArgs
    {
        public string? QuestNormalizedName { get; }
        public string? ObjectiveId { get; }
        public int ObjectiveIndex { get; }
        public bool IsCompleted { get; }

        public ObjectiveProgressChangedEventArgs(string questNormalizedNameOrId, int objectiveIndex, bool isCompleted)
        {
            if (questNormalizedNameOrId.StartsWith("id:"))
            {
                ObjectiveId = questNormalizedNameOrId.Substring(3);
                QuestNormalizedName = null;
            }
            else
            {
                QuestNormalizedName = questNormalizedNameOrId;
                ObjectiveId = null;
            }
            ObjectiveIndex = objectiveIndex;
            IsCompleted = isCompleted;
        }

        public ObjectiveProgressChangedEventArgs(string? questNormalizedName, string? objectiveId, int objectiveIndex, bool isCompleted)
        {
            QuestNormalizedName = questNormalizedName;
            ObjectiveId = objectiveId;
            ObjectiveIndex = objectiveIndex;
            IsCompleted = isCompleted;
        }
    }
}
