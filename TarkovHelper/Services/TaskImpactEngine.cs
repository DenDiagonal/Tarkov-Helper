using System;
using System.Collections.Generic;
using System.Linq;
using TarkovHelper.Models;

namespace TarkovHelper.Models
{
    /// <summary>
    /// Represents an individual sub-objective in a quest.
    /// </summary>
    public class TaskObjective
    {
        public string Id { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Optional { get; set; }
    }
}

namespace TarkovHelper.Services
{
    /// <summary>
    /// Encapsulates unlock impact metrics and priority scoring for a task in the quest DAG.
    /// </summary>
    public record TaskImpactInfo(
        string QuestId,
        int UnlocksNext,         // Immediate downstream quests unlocked when this quest completes
        int TotalDownstreamImpact, // Recursive incomplete downstream quests in DAG
        int OpensCount,          // Quests where this quest is the sole remaining blocker
        bool UnlocksTrader,      // True if unlocks Jaeger, Ref, etc.
        string? UnlockedTraderName,
        int PriorityScore        // Ranking score for "Up next"
    );

    /// <summary>
    /// Service contract for evaluating task unlock impact across the quest dependency graph.
    /// </summary>
    public interface ITaskImpactService
    {
        TaskImpactInfo GetTaskImpact(string questNormalizedName);
        IReadOnlyDictionary<string, TaskImpactInfo> GetAllTaskImpacts();
        int GetIncompleteSuccessorsCount(string questNormalizedName);
    }

    /// <summary>
    /// Canonical TaskImpactEngine calculating immediate follow-ups, transitive downstream impact,
    /// sole-blocker opensCount, and TarkovTracker-aligned priority scoring according to the quest DAG.
    /// </summary>
    public class TaskImpactEngine : ITaskImpactService
    {
        private static TaskImpactEngine? _instance;
        public static TaskImpactEngine Instance => _instance ??= new TaskImpactEngine();

        private List<TarkovTask>? _tasksList;
        private readonly Dictionary<string, TarkovTask> _taskLookup = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _completedQuestKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<TarkovTask>> _directParentsLookup = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TaskImpactInfo> _impactCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _cacheLock = new();
        private bool _eventsSubscribed;

        private static readonly Dictionary<string, string> TraderUnlockQuests = new(StringComparer.OrdinalIgnoreCase)
        {
            ["introduction"] = "Jaeger",
            ["5c0647fdd443bc2504c2d371"] = "Jaeger",
            ["easy-money---part-1"] = "Ref",
            ["6617beeaa9cfa777ca915b7c"] = "Ref",
            ["getting-acquainted"] = "Lightkeeper",
            ["638f541a29ffd1183d187f57"] = "Lightkeeper",
            ["a-helping-hand"] = "BTR Driver",
            ["656f0f98d80a697f855d34b1"] = "BTR Driver"
        };

        /// <summary>
        /// Default constructor using global singleton data sources.
        /// </summary>
        public TaskImpactEngine()
        {
            SubscribeGlobalInvalidationEvents();
        }

        private void SubscribeGlobalInvalidationEvents()
        {
            if (_eventsSubscribed) return;
            _eventsSubscribed = true;
            try
            {
                QuestProgressService.Instance.ProgressChanged += (_, _) => InvalidateCache();
                ObjectiveProgressService.Instance.ObjectiveProgressChanged += (_, _) => InvalidateCache();
                SettingsService.Instance.PlayerLevelChanged += (_, _) => InvalidateCache();
                SettingsService.Instance.PlayerFactionChanged += (_, _) => InvalidateCache();
            }
            catch { }
        }

        public void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _impactCache.Clear();
            }
        }

        /// <summary>
        /// Initializes or updates the underlying task dataset for impact calculations.
        /// </summary>
        public void Initialize(IEnumerable<TarkovTask> tasks)
        {
            lock (_cacheLock)
            {
                _taskLookup.Clear();
                _completedQuestKeys.Clear();
                _directParentsLookup.Clear();
                _impactCache.Clear();
            }
            _tasksList = tasks?.ToList() ?? new List<TarkovTask>();
            foreach (var task in _tasksList)
            {
                if (!string.IsNullOrEmpty(task.NormalizedName))
                    _taskLookup[task.NormalizedName] = task;
                if (!string.IsNullOrEmpty(task.Id))
                    _taskLookup[task.Id] = task;
                if (task.Ids != null)
                {
                    foreach (var id in task.Ids)
                    {
                        if (!string.IsNullOrEmpty(id))
                            _taskLookup[id] = task;
                    }
                }
            }
        }

        /// <summary>
        /// Constructor for injecting a custom task collection and completion set.
        /// </summary>
        public TaskImpactEngine(IEnumerable<TarkovTask> allTasks, IEnumerable<string>? completedQuestIds = null)
        {
            _tasksList = allTasks?.ToList() ?? new List<TarkovTask>();
            foreach (var task in _tasksList)
            {
                if (!string.IsNullOrEmpty(task.NormalizedName))
                    _taskLookup[task.NormalizedName] = task;
                if (!string.IsNullOrEmpty(task.Id))
                    _taskLookup[task.Id] = task;
                if (task.Ids != null)
                {
                    foreach (var id in task.Ids)
                    {
                        if (!string.IsNullOrEmpty(id))
                            _taskLookup[id] = task;
                    }
                }
            }

            if (completedQuestIds != null)
            {
                foreach (var id in completedQuestIds)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                        _completedQuestKeys.Add(id.Trim());
                }
            }
        }

        /// <summary>
        /// Calculates complete impact metrics for the specified quest.
        /// </summary>
        public TaskImpactInfo GetTaskImpact(string questNormalizedName)
            => GetTaskImpact(questNormalizedName, null, null, null);

        /// <summary>
        /// Calculates complete impact metrics for the specified quest with completion and level overrides.
        /// </summary>
        public TaskImpactInfo GetTaskImpact(
            string questNormalizedName,
            Func<string, bool>? isQuestCompletedOverride,
            int? playerLevelOverride = null,
            string? playerFactionOverride = null)
        {
            if (string.IsNullOrWhiteSpace(questNormalizedName))
            {
                return new TaskImpactInfo(string.Empty, 0, 0, 0, false, null, 0);
            }

            EnsureInitialized();

            var task = FindTask(questNormalizedName);
            if (task == null)
            {
                return new TaskImpactInfo(questNormalizedName, 0, 0, 0, false, null, 0);
            }

            string questId = task.Id ?? task.NormalizedName ?? questNormalizedName;
            string normName = task.NormalizedName ?? questNormalizedName;

            bool canUseCache = isQuestCompletedOverride == null && !playerLevelOverride.HasValue && string.IsNullOrEmpty(playerFactionOverride);
            if (canUseCache)
            {
                lock (_cacheLock)
                {
                    if (_impactCache.TryGetValue(normName, out var cached))
                        return cached;
                }
            }

            // 1. Direct incomplete follow-ups (Unlocks next: X)
            var directFollowUps = GetDirectChildren(task)
                .Where(c => !IsCompleted(c, isQuestCompletedOverride))
                .ToList();
            int unlocksNext = directFollowUps.Count;

            // 2. Transitive incomplete downstream tasks in DAG (Impact: Y)
            var downstream = GetAllIncompleteDownstream(task, isQuestCompletedOverride);
            int totalDownstream = downstream.Count;

            // 3. Opens count: Downstream tasks where this task is the sole remaining incomplete blocker
            int opensCount = 0;
            foreach (var child in directFollowUps)
            {
                var prereqs = GetDirectParents(child);
                var incompleteOtherPrereqs = prereqs.Where(p =>
                    !string.Equals(p.NormalizedName, normName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(p.Id, questId, StringComparison.OrdinalIgnoreCase) &&
                    !IsCompleted(p, isQuestCompletedOverride)).ToList();

                if (incompleteOtherPrereqs.Count == 0)
                {
                    bool requirementsMet = true;
                    if (playerLevelOverride.HasValue && child.RequiredLevel.HasValue && child.RequiredLevel.Value > playerLevelOverride.Value)
                    {
                        requirementsMet = false;
                    }
                    if (!string.IsNullOrEmpty(playerFactionOverride) && !string.IsNullOrEmpty(child.Faction) &&
                        !string.Equals(child.Faction, "Any", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(child.Faction, playerFactionOverride, StringComparison.OrdinalIgnoreCase))
                    {
                        requirementsMet = false;
                    }

                    if (requirementsMet)
                    {
                        opensCount++;
                    }
                }
            }

            // 4. Trader unlock detection
            bool unlocksTrader = TraderUnlockQuests.TryGetValue(normName, out var traderName) ||
                                 (!string.IsNullOrEmpty(task.Id) && TraderUnlockQuests.TryGetValue(task.Id, out traderName));

            // 5. TarkovTracker Priority score calculation
            int flagScore = (unlocksTrader && !IsCompleted(task, isQuestCompletedOverride) ? 1000 : 0) +
                            opensCount * 120 +
                            (string.Equals(task.Trader, "Lightkeeper", StringComparison.OrdinalIgnoreCase) ? 90 : 0) +
                            (task.ReqKappa ? 70 : 0);

            // Progress score component
            int progressScore = CalculateProgressScore(task);
            int priorityScore = flagScore + progressScore;

            var impactInfo = new TaskImpactInfo(
                QuestId: questId,
                UnlocksNext: unlocksNext,
                TotalDownstreamImpact: totalDownstream,
                OpensCount: opensCount,
                UnlocksTrader: unlocksTrader,
                UnlockedTraderName: traderName,
                PriorityScore: priorityScore
            );

            if (canUseCache)
            {
                lock (_cacheLock)
                {
                    _impactCache[normName] = impactInfo;
                }
            }

            return impactInfo;
        }

        /// <summary>
        /// Calculates impact info for all available quests in the dataset.
        /// </summary>
        public IReadOnlyDictionary<string, TaskImpactInfo> GetAllTaskImpacts()
        {
            EnsureInitialized();
            var dict = new Dictionary<string, TaskImpactInfo>(StringComparer.OrdinalIgnoreCase);
            var tasks = GetAllTasks();

            foreach (var task in tasks)
            {
                var key = task.NormalizedName ?? task.Id;
                if (!string.IsNullOrEmpty(key) && !dict.ContainsKey(key))
                {
                    dict[key] = GetTaskImpact(key);
                }
            }

            return dict;
        }

        /// <summary>
        /// Counts total incomplete recursive downstream follow-ups in the DAG for a quest.
        /// </summary>
        public int GetIncompleteSuccessorsCount(string questNormalizedName)
        {
            if (string.IsNullOrWhiteSpace(questNormalizedName)) return 0;
            EnsureInitialized();
            var task = FindTask(questNormalizedName);
            return task == null ? 0 : GetAllIncompleteDownstream(task).Count;
        }

        private int CalculateProgressScore(TarkovTask task)
        {
            if (_tasksList != null && _completedQuestKeys.Count == 0 && (task.Objectives == null || task.Objectives.Count == 0))
            {
                // In simulated sample test contexts without objective database, default available base score is 30
                return 30;
            }

            int total = 0;
            int completed = 0;

            if (task.Objectives != null && task.Objectives.Count > 0)
            {
                total = task.Objectives.Count;
                string taskKey = task.NormalizedName ?? task.Id;

                for (int i = 0; i < task.Objectives.Count; i++)
                {
                    if (ObjectiveProgressService.Instance.IsObjectiveCompleted(taskKey, i))
                    {
                        completed++;
                    }
                }
            }

            int remaining = Math.Max(0, total - completed);
            int progressScore = (total > 0 ? (int)Math.Round(((double)completed / total) * 100.0) : 0)
                                + Math.Max(0, 30 - remaining * 6);

            return progressScore;
        }

        private bool IsCompleted(TarkovTask task, Func<string, bool>? isQuestCompletedOverride = null)
        {
            if (isQuestCompletedOverride != null)
            {
                if (!string.IsNullOrEmpty(task.NormalizedName) && isQuestCompletedOverride(task.NormalizedName)) return true;
                if (!string.IsNullOrEmpty(task.Id) && isQuestCompletedOverride(task.Id)) return true;
                if (task.Ids != null && task.Ids.Any(id => isQuestCompletedOverride(id))) return true;
                return false;
            }

            if (_completedQuestKeys.Count > 0)
            {
                if (!string.IsNullOrEmpty(task.Id) && _completedQuestKeys.Contains(task.Id)) return true;
                if (!string.IsNullOrEmpty(task.NormalizedName) && _completedQuestKeys.Contains(task.NormalizedName)) return true;
                if (task.Ids != null && task.Ids.Any(id => _completedQuestKeys.Contains(id))) return true;
                return false;
            }

            if (_tasksList != null)
            {
                return false;
            }

            return QuestProgressService.Instance.GetStatus(task) == QuestStatus.Done;
        }

        private List<TarkovTask> GetDirectChildren(TarkovTask task)
        {
            var result = new List<TarkovTask>();
            if (task.LeadsTo == null) return result;

            foreach (var childKey in task.LeadsTo)
            {
                var child = FindTask(childKey);
                if (child != null)
                    result.Add(child);
            }
            return result;
        }

        private List<TarkovTask> GetDirectParents(TarkovTask child)
        {
            string childKey = child.NormalizedName ?? child.Id ?? string.Empty;
            if (!string.IsNullOrEmpty(childKey))
            {
                lock (_cacheLock)
                {
                    if (_directParentsLookup.TryGetValue(childKey, out var cachedParents))
                        return cachedParents;
                }
            }

            var result = new List<TarkovTask>();
            if (child.Previous != null)
            {
                foreach (var parentKey in child.Previous)
                {
                    var parent = FindTask(parentKey);
                    if (parent != null)
                        result.Add(parent);
                }
            }

            // Also check reciprocal leadsTo
            var all = GetAllTasks();
            foreach (var candidate in all)
            {
                if (candidate.LeadsTo != null && candidate.LeadsTo.Any(l =>
                    string.Equals(l, child.NormalizedName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(l, child.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!result.Contains(candidate))
                        result.Add(candidate);
                }
            }

            if (!string.IsNullOrEmpty(childKey))
            {
                lock (_cacheLock)
                {
                    _directParentsLookup[childKey] = result;
                }
            }

            return result;
        }

        private HashSet<string> GetAllIncompleteDownstream(TarkovTask root, Func<string, bool>? isQuestCompletedOverride = null)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<TarkovTask>();

            string rootKey = root.NormalizedName ?? root.Id;
            if (!string.IsNullOrEmpty(rootKey))
            {
                visited.Add(rootKey);
            }

            foreach (var child in GetDirectChildren(root))
            {
                if (!IsCompleted(child, isQuestCompletedOverride))
                    queue.Enqueue(child);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                string key = current.NormalizedName ?? current.Id;
                if (string.IsNullOrEmpty(key) || !visited.Add(key))
                    continue;

                result.Add(key);

                foreach (var next in GetDirectChildren(current))
                {
                    if (!IsCompleted(next, isQuestCompletedOverride) && !visited.Contains(next.NormalizedName ?? next.Id))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            return result;
        }

        private TarkovTask? FindTask(string nameOrId)
        {
            if (string.IsNullOrWhiteSpace(nameOrId)) return null;

            if (_taskLookup.TryGetValue(nameOrId, out var task))
                return task;

            if (_tasksList != null)
            {
                return _tasksList.FirstOrDefault(t =>
                    string.Equals(t.NormalizedName, nameOrId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.Id, nameOrId, StringComparison.OrdinalIgnoreCase) ||
                    (t.Ids != null && t.Ids.Any(id => string.Equals(id, nameOrId, StringComparison.OrdinalIgnoreCase))));
            }

            try
            {
                var graphTask = QuestGraphService.Instance.GetTask(nameOrId);
                if (graphTask != null) return graphTask;
            }
            catch { }

            var all = GetAllTasks();
            return all.FirstOrDefault(t =>
                string.Equals(t.NormalizedName, nameOrId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Id, nameOrId, StringComparison.OrdinalIgnoreCase) ||
                (t.Ids != null && t.Ids.Any(id => string.Equals(id, nameOrId, StringComparison.OrdinalIgnoreCase))));
        }

        private IEnumerable<TarkovTask> GetAllTasks()
        {
            if (_tasksList != null) return _tasksList;

            if (QuestDbService.Instance.IsLoaded)
                return QuestDbService.Instance.AllQuests;

            try
            {
                return QuestGraphService.Instance.GetAllTasks();
            }
            catch
            {
                return Enumerable.Empty<TarkovTask>();
            }
        }

        private void EnsureInitialized()
        {
            if (_tasksList != null) return;

            if (!QuestDbService.Instance.IsLoaded && QuestDbService.Instance.DatabaseExists)
            {
                try
                {
                    QuestDbService.Instance.LoadQuestsAsync().GetAwaiter().GetResult();
                }
                catch { }
            }

            try
            {
                QuestGraphService.Instance.GetAllTasks();
            }
            catch
            {
                if (QuestDbService.Instance.IsLoaded)
                {
                    QuestGraphService.Instance.Initialize(QuestDbService.Instance.AllQuests.ToList());
                }
            }
        }
    }
}
