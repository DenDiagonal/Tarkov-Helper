using TarkovHelper.Debug;

using TarkovHelper.Models;



namespace TarkovHelper.Services

{

    /// <summary>

    /// Service for managing quest progress state

    /// </summary>

    public class QuestProgressService

    {

        private static QuestProgressService? _instance;

        public static QuestProgressService Instance => _instance ??= new QuestProgressService();



        /// <summary>

        /// 싱글톤 인스턴스를 파괴합니다. 프로필 전환 시 데이터 잔상을 방지하기 위해 사용합니다.

        /// </summary>

        public static void ResetInstance()

        {

            _instance = null;

        }



        private Dictionary<string, QuestStatus> _questProgress = new();

        private Dictionary<string, TarkovTask> _tasksById = new(StringComparer.OrdinalIgnoreCase);

        private List<TarkovTask> _allTasks = new();



        // V2 진행 데이터 (이중 키 저장)

        private QuestProgressDataV2 _progressDataV2 = new();

        private ProfileType _loadedProfile = ProfileType.Pvp;



        /// <summary>

        /// 데이터 소스 (JSON 또는 DB)

        /// </summary>

        public bool IsLoadedFromDb { get; private set; }



        public event EventHandler? ProgressChanged;



        private readonly Dictionary<string, int> _traderLevelOverrides = new(StringComparer.OrdinalIgnoreCase);



        private static readonly Dictionary<string, string[]> TraderAliases = new(StringComparer.OrdinalIgnoreCase)

        {

            ["Prapor"] = new[] { "54cb50c76803fa8b248b4571", "prapor" },

            ["Therapist"] = new[] { "54cb57776803fa99248b456e", "therapist" },

            ["Fence"] = new[] { "579dc571d53a0658a154fbec", "fence" },

            ["Skier"] = new[] { "58330581ace78e27b8b10cee", "skier" },

            ["Peacekeeper"] = new[] { "5935c25fb3acc3114c05bc57", "5935c25fb3acc3127c3d8cd9", "peacekeeper" },

            ["Mechanic"] = new[] { "5a7c2eca46aef81a7ca2145d", "mechanic" },

            ["Ragman"] = new[] { "5ac3b934156ae10c4430e83c", "ragman" },

            ["Jaeger"] = new[] { "5c0647fdd443bc2504c2d371", "jaeger" },

            ["Ref"] = new[] { "656f0f98d542248618000004", "6617beeaa9cfa777ca915b7c", "ref" },

            ["Lightkeeper"] = new[] { "638b0f368b0d6686180150f8", "638f541a29ffd1183d187f57", "lightkeeper" },

            ["BTR Driver"] = new[] { "656f0f98d80a697f855d34b1", "btr-driver", "btr driver", "btr" }

        };



        public QuestProgressService()

        {

            SettingsService.Instance.PlayerLevelChanged += (s, lvl) => NotifyProgressChanged();

        }



        public void NotifyProgressChanged()

        {

            ProgressChanged?.Invoke(this, EventArgs.Empty);

        }



        public void SetTraderLoyaltyLevelOverride(string trader, int level)

        {

            if (string.IsNullOrWhiteSpace(trader)) return;

            level = Math.Clamp(level, 1, 4);

            var friendly = ResolveTraderFriendlyName(trader);

            _traderLevelOverrides[friendly] = level;

            _traderLevelOverrides[trader.Trim()] = level;



            if (TraderAliases.TryGetValue(friendly, out var aliases))

            {

                foreach (var alias in aliases)

                {

                    _traderLevelOverrides[alias] = level;

                }

            }



            try

            {

                _userDataDb.SetSetting($"trader.ll.{friendly.ToLowerInvariant()}", level.ToString(), _loadedProfile);

            }

            catch (Exception ex)

            {

                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to persist trader LL override: {ex.Message}");

            }



            NotifyProgressChanged();

        }



        public int? GetTraderLoyaltyLevelOverride(string trader)

        {

            if (string.IsNullOrWhiteSpace(trader)) return null;

            var friendly = ResolveTraderFriendlyName(trader);

            if (_traderLevelOverrides.TryGetValue(friendly, out var lvl)) return lvl;

            if (_traderLevelOverrides.TryGetValue(trader.Trim(), out var lvl2)) return lvl2;

            return null;

        }



        public void ClearTraderLoyaltyLevelOverrides()

        {

            _traderLevelOverrides.Clear();

            try

            {

                var traders = new[] { "prapor", "therapist", "fence", "skier", "peacekeeper", "mechanic", "ragman", "jaeger", "ref", "lightkeeper", "btr driver" };

                foreach (var t in traders)

                {

                    _userDataDb.DeleteSetting($"trader.ll.{t}", _loadedProfile);

                }

            }

            catch (Exception ex)

            {

                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to clear persisted trader LL overrides: {ex.Message}");

            }



            NotifyProgressChanged();

        }



        /// <summary>

        /// Clears in-memory progress and overrides for isolated unit testing without touching the database.

        /// </summary>

        public void ClearProgressForTesting()

        {

            _questProgress.Clear();

            _traderLevelOverrides.Clear();

        }



        /// <summary>

        /// Sets in-memory task progress for isolated unit testing without touching the database.

        /// </summary>

        public void SetProgressForTesting(string taskKey, QuestStatus status)

        {

            if (string.IsNullOrEmpty(taskKey)) return;

            _questProgress[taskKey] = status;

        }



        public async Task SetStatusAsync(TarkovTask task, QuestStatus status)

        {

            var taskId = task.Ids?.FirstOrDefault();

            var taskKey = taskId ?? task.NormalizedName;

            if (string.IsNullOrEmpty(taskKey)) return;



            _questProgress[taskKey] = status;

            if (!string.IsNullOrEmpty(task.NormalizedName))

            {

                _questProgress[task.NormalizedName] = status;

            }



            try

            {

                await _userDataDb.SaveQuestProgressAsync(taskId ?? taskKey, task.NormalizedName, status);

            }

            catch

            {

                // Headless test or DB-less environment

            }



            NotifyProgressChanged();

        }



        /// <summary>

        /// DB에서 퀘스트 데이터를 로드하고 초기화합니다.

        /// </summary>

        public async Task<bool> InitializeFromDbAsync(ProfileType? profileType = null)

        {

            var dbService = QuestDbService.Instance;



            if (!await dbService.LoadQuestsAsync())

            {

                System.Diagnostics.Debug.WriteLine("[QuestProgressService] Failed to load quests from DB, falling back to JSON");

                return false;

            }



            var tasks = dbService.AllQuests.ToList();

            await InitializeAsync(tasks, profileType);

            IsLoadedFromDb = true;



            // V2 형식으로 진행 데이터 reconcile

            ReconcileProgressWithDb();



            System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Initialized from DB with {tasks.Count} quests");

            return true;

        }



        /// <summary>

        /// DB 업데이트 후 진행 데이터를 reconcile합니다.

        /// ID 또는 NormalizedName으로 매핑하고 누락된 키를 채웁니다.

        /// </summary>

        private void ReconcileProgressWithDb()

        {

            var changed = false;



            // 완료 퀘스트 reconcile

            foreach (var entry in _progressDataV2.CompletedQuests)

            {

                TarkovTask? matched = null;



                // 1차: ID로 매핑

                if (!string.IsNullOrEmpty(entry.Id) && _tasksById.TryGetValue(entry.Id, out matched))

                {

                    // ID 매핑 성공 - NormalizedName 업데이트

                    if (matched.NormalizedName != null && entry.NormalizedName != matched.NormalizedName)

                    {

                        entry.NormalizedName = matched.NormalizedName;

                        changed = true;

                    }

                }

                // 2차: NormalizedName으로 폴백

                else if (!string.IsNullOrEmpty(entry.NormalizedName) &&

                         _tasksById.TryGetValue(entry.NormalizedName, out matched))

                {

                    // NormalizedName 매핑 성공 - ID 업데이트

                    var matchedId = matched.Ids?.FirstOrDefault();

                    if (!string.IsNullOrEmpty(matchedId) && entry.Id != matchedId)

                    {

                        entry.Id = matchedId;

                        changed = true;

                    }

                }

                else

                {

                    // 매핑 실패 - 고아 레코드로 유지 (삭제하지 않음)

                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Orphan completed entry: {entry.Id ?? entry.NormalizedName}");

                }

            }



            // 실패 퀘스트 reconcile

            foreach (var entry in _progressDataV2.FailedQuests)

            {

                TarkovTask? matched = null;



                if (!string.IsNullOrEmpty(entry.Id) && _tasksById.TryGetValue(entry.Id, out matched))

                {

                    if (matched.NormalizedName != null && entry.NormalizedName != matched.NormalizedName)

                    {

                        entry.NormalizedName = matched.NormalizedName;

                        changed = true;

                    }

                }

                else if (!string.IsNullOrEmpty(entry.NormalizedName) &&

                         _tasksById.TryGetValue(entry.NormalizedName, out matched))

                {

                    var matchedId = matched.Ids?.FirstOrDefault();

                    if (!string.IsNullOrEmpty(matchedId) && entry.Id != matchedId)

                    {

                        entry.Id = matchedId;

                        changed = true;

                    }

                }

            }



            if (changed)

            {

                SaveProgress();

            }

        }



        /// <summary>

        /// Initialize service with task data (Asynchronous)

        /// </summary>

        public async Task InitializeAsync(List<TarkovTask> tasks, ProfileType? profileType = null)
        {
            _allTasks = tasks;

            // Single unified dictionary indexed by BSG ID, aliases, and normalized name
            _tasksById = new Dictionary<string, TarkovTask>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in tasks)
            {
                if (!string.IsNullOrEmpty(task.Id))
                {
                    _tasksById[task.Id] = task;
                }

                if (task.Ids != null)
                {
                    foreach (var id in task.Ids)
                    {
                        if (!string.IsNullOrEmpty(id) && !_tasksById.ContainsKey(id))
                        {
                            _tasksById[id] = task;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(task.NormalizedName) && !_tasksById.ContainsKey(task.NormalizedName))
                {
                    _tasksById[task.NormalizedName] = task;
                }
            }

            _loadedProfile = profileType ?? ProfileService.Instance.CurrentProfile;

            await LoadProgressAsync(profileType);

        }



        /// <summary>

        /// Get all tasks

        /// </summary>

        public IReadOnlyList<TarkovTask> AllTasks => _allTasks;



        /// <summary>

        /// Get task by normalized name (deprecated, use GetTaskById instead)

        /// </summary>

        public TarkovTask? GetTask(string normalizedName)
        {
            if (string.IsNullOrEmpty(normalizedName)) return null;
            return _tasksById.TryGetValue(normalizedName, out var task) ? task : null;
        }

        public TarkovTask? GetTaskById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_tasksById.TryGetValue(id, out var task)) return task;
            return _allTasks.FirstOrDefault(t => t.Ids != null && t.Ids.Contains(id, StringComparer.OrdinalIgnoreCase));
        }

        public TarkovTask? GetTaskByBsgId(string bsgId)
        {
            return GetTaskById(bsgId);
        }

        public bool HasAlternativeQuests(TarkovTask task)

        {

            return task.AlternativeQuests != null && task.AlternativeQuests.Count > 0;

        }



        /// <summary>

        /// Get all alternative quest groups (for sync selection UI)

        /// Returns groups of mutually exclusive quests that need user selection

        /// </summary>

        public List<List<TarkovTask>> GetAlternativeQuestGroups()

        {

            var groups = new List<List<TarkovTask>>();

            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);



            foreach (var task in _allTasks)

            {

                if (task.NormalizedName == null) continue;

                if (processed.Contains(task.NormalizedName)) continue;

                if (!HasAlternativeQuests(task)) continue;



                // Build a group of mutually exclusive quests

                var group = new List<TarkovTask> { task };

                processed.Add(task.NormalizedName);



                foreach (var altName in task.AlternativeQuests!)

                {

                    if (processed.Contains(altName)) continue;



                    var altTask = GetTask(altName) ?? GetTaskById(altName);

                    if (altTask != null)

                    {

                        group.Add(altTask);

                        if (altTask.NormalizedName != null)

                            processed.Add(altTask.NormalizedName);

                    }

                }



                if (group.Count > 1)

                {

                    groups.Add(group);

                }

            }



            return groups;

        }



        // Thread-local visited set for GetStatus to prevent circular reference during status check

        [ThreadStatic]

        private static HashSet<string>? _getStatusVisited;



        /// <summary>

        /// Get quest status for a task

        /// </summary>
        public QuestStatus GetStatus(string taskId)
        {
            if (string.IsNullOrEmpty(taskId)) return QuestStatus.Active;
            string bsgId = CanonicalQuestMap.ResolveBsgId(taskId);
            if (_questProgress.TryGetValue(bsgId, out var status)) return status;
            if (_questProgress.TryGetValue(taskId, out status)) return status;
            var task = GetTaskById(bsgId) ?? GetTaskById(taskId);
            if (task?.IsSeasonal == true && ProfileService.Instance.CurrentProfile != ProfileType.Seasonal)
                return QuestStatus.Unavailable;
            if (task != null) return GetStatus(task);
            return QuestStatus.Locked;
        }

        public QuestStatus GetStatus(TarkovTask task)

        {
            // Seasonal quests are unavailable unless in Seasonal profile
            if (task.IsSeasonal && ProfileService.Instance.CurrentProfile != ProfileType.Seasonal)
                return QuestStatus.Unavailable;

            var taskId = task.Ids?.FirstOrDefault();

            var taskKey = taskId ?? task.NormalizedName;



            if (string.IsNullOrEmpty(taskKey)) return QuestStatus.Active;



            // Check if manually set to Done or Failed, or manual override to Active

            // Try by Id first, then by NormalizedName for backwards compatibility

            if (task.Ids != null)

            {

                foreach (var id in task.Ids)

                {

                    if (!string.IsNullOrEmpty(id) && _questProgress.TryGetValue(id, out var statusById))

                    {

                        if (statusById == QuestStatus.Done || statusById == QuestStatus.Failed || statusById == QuestStatus.Locked)

                            return statusById;

                        if (statusById == QuestStatus.Active)

                            return QuestStatus.Active;

                    }

                }

            }

            if (!string.IsNullOrEmpty(task.NormalizedName) && _questProgress.TryGetValue(task.NormalizedName, out var statusByName))

            {

                if (statusByName == QuestStatus.Done || statusByName == QuestStatus.Failed || statusByName == QuestStatus.Locked)

                    return statusByName;

                if (statusByName == QuestStatus.Active)

                    return QuestStatus.Active;

            }



            // Story chapters are decoupled from sequential prerequisites: return QuestStatus.Active (never Locked) unless marked Done

            if (string.Equals(task.Trader, "Story", StringComparison.OrdinalIgnoreCase) ||

                string.Equals(task.Trader, "Story Chapters", StringComparison.OrdinalIgnoreCase))

            {

                return QuestStatus.Active;

            }



            // Circular reference protection for prerequisite checking

            bool isTopLevel = _getStatusVisited == null;

            _getStatusVisited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);



            // If already checking this task (circular reference), treat as Active to break the cycle

            if (!_getStatusVisited.Add(taskKey))

            {

                return QuestStatus.Active;

            }



            try

            {

                // Check edition requirements first (Unavailable takes precedence)

                if (!IsEditionRequirementMet(task))

                    return QuestStatus.Unavailable;



                // Check prestige level requirement (also Unavailable)

                if (!IsPrestigeLevelRequirementMet(task))

                    return QuestStatus.Unavailable;



                // Check faction requirement (Unavailable if player chose different faction)

                if (!IsFactionRequirementMet(task))

                    return QuestStatus.Unavailable;



                // Check DSP Decode Count requirement (Locked, not Unavailable)

                if (!IsDspRequirementMet(task))

                    return QuestStatus.Locked;



                // Check trader gating (TarkovTracker parity: locked trader keeps its quests locked)

                if (!IsTraderUnlocked(task.Trader))

                    return QuestStatus.Locked;

                // Check BTR Driver chain gating (quests from other traders like Prapor/Ragman/Skier that depend on BTR Driver)

                if (RequiresBtrDriver(task) && !IsBtrDriverUnlocked())

                    return QuestStatus.Locked;



                // Check prerequisites

                if (!ArePrerequisitesMet(task))

                    return QuestStatus.Locked;



                // Check trader loyalty level requirements

                if (!IsTraderRequirementMet(task))

                    return QuestStatus.Locked;



                // Check quest pool requirements

                if (!IsPoolRequirementMet(task))

                    return QuestStatus.Locked;



                // Check level requirement

                if (!IsLevelRequirementMet(task))

                    return QuestStatus.LevelLocked;



                // Check Scav Karma requirement

                if (!IsScavKarmaRequirementMet(task))

                    return QuestStatus.LevelLocked;  // Use LevelLocked status for karma-locked quests too



                return QuestStatus.Active;

            }

            finally

            {

                _getStatusVisited.Remove(taskKey);

                if (isTopLevel)

                {

                    _getStatusVisited = null;

                }

            }

        }



        /// <summary>

        /// Check if player level meets quest requirement

        /// </summary>

        public bool IsLevelRequirementMet(TarkovTask task)

        {

            // If no level requirement, always met

            if (!task.RequiredLevel.HasValue || task.RequiredLevel.Value <= 0)

                return true;



            var playerLevel = SettingsService.Instance.PlayerLevel;

            return playerLevel >= task.RequiredLevel.Value;

        }



        /// <summary>

        /// Check if Trader Loyalty Level requirements are met for the quest

        /// </summary>

        public bool IsTraderRequirementMet(TarkovTask task)

        {

            if (task.TraderRequirements == null || task.TraderRequirements.Count == 0)

                return true;



            foreach (var req in task.TraderRequirements)

            {

                var traderName = !string.IsNullOrEmpty(req.TraderName) ? req.TraderName : req.TraderId;

                if (string.IsNullOrEmpty(traderName))

                    continue;



                var friendly = ResolveTraderFriendlyName(traderName);

                if (string.Equals(friendly, "Fence", StringComparison.OrdinalIgnoreCase) ||

                    string.Equals(req.RequirementType, "Reputation", StringComparison.OrdinalIgnoreCase))

                {

                    var playerScavRep = SettingsService.Instance.ScavRep;

                    var compareRep = string.IsNullOrWhiteSpace(req.CompareMethod) ? ">=" : req.CompareMethod.Trim();

                    double targetRep = req.RequiredLevel;



                    bool repSatisfied = compareRep switch

                    {

                        ">=" => playerScavRep >= targetRep,

                        ">" => playerScavRep > targetRep,

                        "<=" => playerScavRep <= targetRep,

                        "<" => playerScavRep < targetRep,

                        "==" or "=" => Math.Abs(playerScavRep - targetRep) < 0.01,

                        "!=" => Math.Abs(playerScavRep - targetRep) >= 0.01,

                        _ => playerScavRep >= targetRep

                    };



                    if (!repSatisfied)

                        return false;



                    continue;

                }



                var currentLL = GetTraderLoyaltyLevel(traderName);

                var compare = string.IsNullOrWhiteSpace(req.CompareMethod) ? ">=" : req.CompareMethod.Trim();



                bool satisfied = compare switch

                {

                    ">=" => currentLL >= req.RequiredLevel,

                    ">" => currentLL > req.RequiredLevel,

                    "<=" => currentLL <= req.RequiredLevel,

                    "<" => currentLL < req.RequiredLevel,

                    "==" or "=" => currentLL == req.RequiredLevel,

                    "!=" => currentLL != req.RequiredLevel,

                    _ => currentLL >= req.RequiredLevel

                };



                if (!satisfied)

                    return false;

            }



            return true;

        }



        /// <summary>

        /// Check if task pool completion threshold requirements are met for the quest

        /// </summary>

        /// <summary>
        /// Get pool completion progress for a quest: (CompletedCount, RequiredCount).
        /// Returns (0, 0) if no pool requirement exists.
        /// </summary>
        public (int CompletedCount, int RequiredCount) GetPoolProgress(TarkovTask task)
        {
            if (task.PoolRequirements == null || task.PoolRequirements.Count == 0)
                return (0, 0);

            var poolReq = task.PoolRequirements[0];
            int completedCount = 0;

            if (poolReq.TaskIds != null && poolReq.TaskIds.Count > 0)
            {
                foreach (var taskId in poolReq.TaskIds)
                {
                    var poolTask = GetTaskById(taskId) ?? GetTaskByBsgId(taskId) ?? GetTask(taskId);
                    if (poolTask != null)
                    {
                        if (poolTask == task) continue;
                        if (poolTask.Ids != null && task.Ids != null && poolTask.Ids.Intersect(task.Ids, StringComparer.OrdinalIgnoreCase).Any()) continue;

                        if (IsQuestDone(poolTask)) completedCount++;
                    }
                    else if (_questProgress.TryGetValue(taskId, out var st) && st == QuestStatus.Done)
                    {
                        completedCount++;
                    }
                }
            }
            else
            {
                foreach (var candidate in _allTasks)
                {
                    if (candidate == task) continue;
                    if (candidate.Ids != null && task.Ids != null && candidate.Ids.Intersect(task.Ids).Any()) continue;

                    if (!string.Equals(candidate.Trader, poolReq.PoolTrader, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (poolReq.PoolTraderLL.HasValue)
                    {
                        int targetLL = poolReq.PoolTraderLL.Value;
                        bool matchesLL = false;

                        if (candidate.TraderRequirements != null && candidate.TraderRequirements.Any(r =>
                            string.Equals(r.TraderName, poolReq.PoolTrader, StringComparison.OrdinalIgnoreCase) &&
                            r.RequiredLevel == targetLL))
                        {
                            matchesLL = true;
                        }
                        else if (candidate.RequiredLevel.HasValue)
                        {
                            if (GetTraderTierFromPlayerLevel(poolReq.PoolTrader, candidate.RequiredLevel.Value) == targetLL)
                            {
                                matchesLL = true;
                            }
                        }
                        else if (targetLL == 1)
                        {
                            matchesLL = true;
                        }

                        if (!matchesLL)
                            continue;
                    }

                    if (IsQuestDone(candidate))
                    {
                        completedCount++;
                    }
                }
            }

            return (completedCount, poolReq.RequiredCompletedCount);
        }

        public bool IsPoolRequirementMet(TarkovTask task)
        {
            if (task.PoolRequirements == null || task.PoolRequirements.Count == 0)
                return true;

            foreach (var poolReq in task.PoolRequirements)
            {
                int completedCount = 0;

                // 1. If explicit TaskIds list is provided (e.g. from tests or taskPool dataset)
                if (poolReq.TaskIds != null && poolReq.TaskIds.Count > 0)
                {
                    foreach (var taskId in poolReq.TaskIds)
                    {
                        var poolTask = GetTaskById(taskId) ?? GetTaskByBsgId(taskId) ?? GetTask(taskId);
                        if (poolTask != null)
                        {
                            if (poolTask == task) continue;
                            if (poolTask.Ids != null && task.Ids != null && poolTask.Ids.Intersect(task.Ids, StringComparer.OrdinalIgnoreCase).Any()) continue;

                            if (IsQuestDone(poolTask)) completedCount++;
                        }
                        else if (_questProgress.TryGetValue(taskId, out var st) && st == QuestStatus.Done)
                        {
                            completedCount++;
                        }
                    }
                }
                else
                {
                    // 2. Query against _allTasks matching PoolTrader and optional PoolTraderLL
                    foreach (var candidate in _allTasks)
                    {
                        // Cannot count quest towards its own unlock
                        if (candidate == task) continue;
                        if (candidate.Ids != null && task.Ids != null && candidate.Ids.Intersect(task.Ids).Any()) continue;

                        if (!string.Equals(candidate.Trader, poolReq.PoolTrader, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (poolReq.PoolTraderLL.HasValue)
                        {
                            int targetLL = poolReq.PoolTraderLL.Value;
                            bool matchesLL = false;

                            if (candidate.TraderRequirements != null && candidate.TraderRequirements.Any(r =>
                                string.Equals(r.TraderName, poolReq.PoolTrader, StringComparison.OrdinalIgnoreCase) &&
                                r.RequiredLevel == targetLL))
                            {
                                matchesLL = true;
                            }
                            else if (candidate.RequiredLevel.HasValue)
                            {
                                if (GetTraderTierFromPlayerLevel(poolReq.PoolTrader, candidate.RequiredLevel.Value) == targetLL)
                                {
                                    matchesLL = true;
                                }
                            }
                            else if (targetLL == 1)
                            {
                                matchesLL = true;
                            }

                            if (!matchesLL)
                                continue;
                        }

                        if (IsQuestDone(candidate))
                        {
                            completedCount++;
                        }
                    }
                }

                if (completedCount < poolReq.RequiredCompletedCount)
                    return false;
            }

            return true;
        }



        public static string ResolveTraderFriendlyName(string idOrName)

        {

            if (string.IsNullOrWhiteSpace(idOrName)) return idOrName;

            var clean = idOrName.Trim();

            return clean.ToLowerInvariant() switch

            {

                "54cb50c76803fa8b248b4571" or "prapor" => "Prapor",

                "54cb57776803fa99248b456e" or "therapist" => "Therapist",

                "579dc571d53a0658a154fbec" or "fence" => "Fence",

                "58330581ace78e27b8b10cee" or "skier" => "Skier",

                "5935c25fb3acc3114c05bc57" or "5935c25fb3acc3127c3d8cd9" or "peacekeeper" => "Peacekeeper",

                "5a7c2eca46aef81a7ca2145d" or "mechanic" => "Mechanic",

                "5ac3b934156ae10c4430e83c" or "ragman" => "Ragman",

                "5c0647fdd443bc2504c2d371" or "jaeger" => "Jaeger",

                "656f0f98d542248618000004" or "6617beeaa9cfa777ca915b7c" or "ref" => "Ref",

                "638b0f368b0d6686180150f8" or "638f541a29ffd1183d187f57" or "lightkeeper" => "Lightkeeper",

                "656f0f98d80a697f855d34b1" or "btr-driver" or "btr driver" or "btr" => "BTR Driver",

                _ => clean

            };

        }



        public int GetTraderLoyaltyLevel(string traderName)

        {

            if (string.IsNullOrWhiteSpace(traderName))

                return 4;



            var key = traderName.Trim();

            var friendly = ResolveTraderFriendlyName(key);



            if (_traderLevelOverrides.TryGetValue(friendly, out var ovrFriendly))

                return ovrFriendly;



            if (_traderLevelOverrides.TryGetValue(key, out var ovr))

                return ovr;



            var playerLevel = SettingsService.Instance.PlayerLevel;



            return friendly.ToLowerInvariant() switch

            {

                "prapor" or "54cb50c76803fa8b248b4571" => playerLevel >= 36 ? 4 : playerLevel >= 26 ? 3 : playerLevel >= 15 ? 2 : 1,

                "therapist" or "54cb57776803fa99248b456e" => playerLevel >= 35 ? 4 : playerLevel >= 24 ? 3 : playerLevel >= 13 ? 2 : 1,

                "skier" or "58330581ace78e27b8b10cee" => playerLevel >= 38 ? 4 : playerLevel >= 28 ? 3 : playerLevel >= 15 ? 2 : 1,

                "peacekeeper" or "5935c25fb3acc3114c05bc57" or "5935c25fb3acc3127c3d8cd9" => IsPeacekeeperUnlocked()

                    ? (playerLevel >= 37 ? 4 : playerLevel >= 23 ? 3 : playerLevel >= 14 ? 2 : 1)

                    : 0,

                "mechanic" or "5a7c2eca46aef81a7ca2145d" => playerLevel >= 40 ? 4 : playerLevel >= 30 ? 3 : playerLevel >= 20 ? 2 : 1,

                "ragman" or "5ac3b934156ae10c4430e83c" => playerLevel >= 42 ? 4 : playerLevel >= 32 ? 3 : playerLevel >= 17 ? 2 : 1,

                "jaeger" or "5c0647fdd443bc2504c2d371" => IsJaegerUnlocked()

                    ? (playerLevel >= 33 ? 4 : playerLevel >= 22 ? 3 : playerLevel >= 15 ? 2 : 1)

                    : 0,

                "fence" or "579dc571d53a0658a154fbec" => 1,

                "lightkeeper" or "638b0f368b0d6686180150f8" or "638f541a29ffd1183d187f57" => IsLightkeeperUnlocked() ? 1 : 0,

                "btr driver" or "btr-driver" or "656f0f98d80a697f855d34b1" => IsBtrDriverUnlocked() ? 1 : 0,

                "ref" or "656f0f98d542248618000004" or "6617beeaa9cfa777ca915b7c" => 1,

                _ => 4

            };

        }



        public static int GetTraderTierFromPlayerLevel(string traderName, int level)

        {

            return traderName.Trim().ToLowerInvariant() switch

            {

                "prapor" or "54cb50c76803fa8b248b4571" => level >= 36 ? 4 : level >= 26 ? 3 : level >= 15 ? 2 : 1,

                "therapist" or "54cb57776803fa99248b456e" => level >= 35 ? 4 : level >= 24 ? 3 : level >= 13 ? 2 : 1,

                "skier" or "58330581ace78e27b8b10cee" => level >= 38 ? 4 : level >= 28 ? 3 : level >= 15 ? 2 : 1,

                "peacekeeper" or "5935c25fb3acc3114c05bc57" or "5935c25fb3acc3127c3d8cd9" => level >= 37 ? 4 : level >= 23 ? 3 : level >= 14 ? 2 : 1,

                "mechanic" or "5a7c2eca46aef81a7ca2145d" => level >= 40 ? 4 : level >= 30 ? 3 : level >= 20 ? 2 : 1,

                "ragman" or "5ac3b934156ae10c4430e83c" => level >= 42 ? 4 : level >= 32 ? 3 : level >= 17 ? 2 : 1,

                "jaeger" or "5c0647fdd443bc2504c2d371" => level >= 33 ? 4 : level >= 22 ? 3 : level >= 15 ? 2 : 1,

                "fence" or "579dc571d53a0658a154fbec" => 1,

                "lightkeeper" or "638b0f368b0d6686180150f8" or "638f541a29ffd1183d187f57" => 1,

                "btr driver" or "btr-driver" or "656f0f98d80a697f855d34b1" => 1,

                "ref" or "656f0f98d542248618000004" or "6617beeaa9cfa777ca915b7c" => 1,

                _ => 1

            };

        }



                public bool IsPeacekeeperUnlocked()
        {
            var quest = GetTaskById("5a27c99a86f7747d2c6bdd8e")
                ?? GetTask("friend-from-the-west---part-2")
                ?? GetTask("friend-from-the-west")
                ?? _allTasks.FirstOrDefault(t => t.Name.Equals("Friend From the West", StringComparison.OrdinalIgnoreCase));

            if (quest == null)
            {
                if (_questProgress.TryGetValue("5a27c99a86f7747d2c6bdd8e", out var s) && s == QuestStatus.Done)
                    return true;
                if (_questProgress.TryGetValue("friend-from-the-west---part-2", out var s2) && s2 == QuestStatus.Done)
                    return true;
                if (_questProgress.TryGetValue("friend-from-the-west", out var s3) && s3 == QuestStatus.Done)
                    return true;
                return _allTasks.Count < 50;
            }

            return IsQuestDone(quest);
        }

        /// <summary>
        /// Check if a task requires BTR Driver to be unlocked
        /// </summary>
        public bool RequiresBtrDriver(TarkovTask task)
        {
            if (string.Equals(task.Trader, "BTR Driver", StringComparison.OrdinalIgnoreCase))
                return true;

            // Direct ID match for known cross-trader BTR chain quests
            if (task.Ids != null && (
                task.Ids.Contains("6740a15566e6a521aa051b15", StringComparer.OrdinalIgnoreCase) || // Forge a Friendship (Prapor)
                task.Ids.Contains("6740b60c60a98cad1b0e0aa0", StringComparer.OrdinalIgnoreCase) || // Another Shipping Delay (Ragman)
                task.Ids.Contains("6745fae369a58fceba10343d", StringComparer.OrdinalIgnoreCase) || // The Higher They Fly (Skier)
                task.Ids.Contains("6744a4717e3818d5bb0680bb", StringComparer.OrdinalIgnoreCase) || // Stabilize Business (Ragman)
                task.Ids.Contains("67460662d0fbbc74ca0f7229", StringComparer.OrdinalIgnoreCase) || // Choose Your Friends Wisely (Skier)
                task.Ids.Contains("159a5419285ef68c058214d1", StringComparer.OrdinalIgnoreCase)    // Cast the Net (Prapor)
            ))
            {
                return true;
            }

            if (string.Equals(task.NormalizedName, "forge-a-friendship", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(task.NormalizedName, "another-shipping-delay", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(task.NormalizedName, "the-higher-they-fly", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(task.NormalizedName, "stabilize-business", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(task.NormalizedName, "choose-your-friends-wisely", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(task.NormalizedName, "cast-the-net", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Check if any prerequisite is a BTR Driver task
            if (task.TaskRequirements != null && task.TaskRequirements.Count > 0)
            {
                foreach (var req in task.TaskRequirements)
                {
                    if (string.IsNullOrEmpty(req.TaskId)) continue;
                    var reqTask = GetTaskById(req.TaskId);
                    if (reqTask != null && string.Equals(reqTask.Trader, "BTR Driver", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            if (task.Previous != null && task.Previous.Count > 0)
            {
                foreach (var prevNorm in task.Previous)
                {
                    var prevTask = GetTask(prevNorm);
                    if (prevTask != null && string.Equals(prevTask.Trader, "BTR Driver", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether a trader is unlocked according to TarkovTracker gating logic.
        /// Quests belonging to a locked trader remain QuestStatus.Locked.
        /// </summary>
        public bool IsTraderUnlocked(string? trader)
        {
            if (string.IsNullOrWhiteSpace(trader))
                return true;

            var normalized = trader.Trim().ToLowerInvariant();
            return normalized switch
            {
                "lightkeeper" or "638b0f368b0d6686180150f8" or "638f541a29ffd1183d187f57" => IsLightkeeperUnlocked(),
                "jaeger" or "5c0647fdd443bc2504c2d371" => IsJaegerUnlocked(),
                "ref" or "6617beeaa9cfa777ca915b7c" => IsRefUnlocked(),
                "peacekeeper" or "5935c25fb3acc3114c05bc57" or "5935c25fb3acc3127c3d8cd9" => IsPeacekeeperUnlocked(),
                "btr driver" or "btr-driver" or "656f0f98d80a697f855d34b1" => IsBtrDriverUnlocked(),
                _ => true
            };
        }

        public bool HasAnyCompletedQuestForTrader(string trader)
        {
            var friendly = ResolveTraderFriendlyName(trader);
            foreach (var task in _allTasks)
            {
                if (ResolveTraderFriendlyName(task.Trader).Equals(friendly, StringComparison.OrdinalIgnoreCase))
                {
                    if (IsQuestDone(task))
                        return true;
                }
            }
            return false;
        }

        public bool IsJaegerUnlocked()
        {
            var quest = GetTaskById("5d2495a886f77425cd51e403")
                ?? GetTask("introduction")
                ?? _allTasks.FirstOrDefault(t => t.Name.Equals("Introduction", StringComparison.OrdinalIgnoreCase));

            if (quest != null && IsQuestDone(quest))
                return true;

            // TarkovTracker parity: If the player has completed ANY quest for Jaeger or has a persisted LL >= 2 override, Jaeger is undeniably unlocked
            if (HasAnyCompletedQuestForTrader("Jaeger") || (_traderLevelOverrides.TryGetValue("Jaeger", out var jLvl) && jLvl >= 2))
                return true;

            if (quest == null)
            {
                if (_questProgress.TryGetValue("5d2495a886f77425cd51e403", out var s) && s == QuestStatus.Done)
                    return true;
                if (_questProgress.TryGetValue("introduction", out var s2) && s2 == QuestStatus.Done)
                    return true;
                return _allTasks.Count < 50;
            }

            return false;
        }

        public bool IsRefUnlocked()
        {
            var quest = GetTaskById("66058cb22cee99303f1ba067")
                ?? GetTask("easy-money---part-1")
                ?? _allTasks.FirstOrDefault(t => t.Name.Equals("Easy Money - Part 1", StringComparison.OrdinalIgnoreCase));

            if (quest != null && IsQuestDone(quest))
                return true;

            if (HasAnyCompletedQuestForTrader("Ref") || (_traderLevelOverrides.TryGetValue("Ref", out var rLvl) && rLvl >= 2))
                return true;

            if (quest == null)
            {
                if (_questProgress.TryGetValue("66058cb22cee99303f1ba067", out var s) && s == QuestStatus.Done)
                    return true;
                if (_questProgress.TryGetValue("easy-money---part-1", out var s2) && s2 == QuestStatus.Done)
                    return true;
                return _allTasks.Count < 50;
            }

            return false;
        }

        public bool IsLightkeeperUnlocked()
        {
            var quest = GetTaskById("625d700cc48e6c62a440fab5")
                ?? GetTask("to-the-light---getting-acquainted")
                ?? GetTask("getting-acquainted")
                ?? _allTasks.FirstOrDefault(t => t.Name.Equals("To the Light - Getting Acquainted", StringComparison.OrdinalIgnoreCase) || t.Name.Equals("Getting Acquainted", StringComparison.OrdinalIgnoreCase));

            if (quest == null)
            {
                if (_questProgress.TryGetValue("625d700cc48e6c62a440fab5", out var s) && s == QuestStatus.Done)
                    return true;
                if (_questProgress.TryGetValue("to-the-light---getting-acquainted", out var s2) && s2 == QuestStatus.Done)
                    return true;
                if (_questProgress.TryGetValue("getting-acquainted", out var s3) && s3 == QuestStatus.Done)
                    return true;
                return _allTasks.Count < 50;
            }

            return IsQuestDone(quest);
        }

        public bool IsBtrDriverUnlocked()
        {
            // 1. Manual user override via UI button takes precedence if explicitly set
            var manualOverride = SettingsService.Instance.IsBtrDriverContacted;
            if (manualOverride.HasValue)
                return manualOverride.Value;

            // 2. Automatically unlocked if "A Helping Hand" is handed over / completed
            var quest = GetTaskById("6752f6d83038f7df520c83e8")
                ?? GetTask("a-helping-hand")
                ?? _allTasks.FirstOrDefault(t => t.Name.Equals("A Helping Hand", StringComparison.OrdinalIgnoreCase));

            if (quest != null)
            {
                if (IsQuestDone(quest))
                    return true;
            }
            else
            {
                if (_questProgress.TryGetValue("6752f6d83038f7df520c83e8", out var s) && s == QuestStatus.Done)
                    return true;
                if (_questProgress.TryGetValue("a-helping-hand", out var s2) && s2 == QuestStatus.Done)
                    return true;
            }

            // 3. Locked by default (sin defecto)
            return false;
        }

        public void SetBtrDriverUnlocked(bool unlocked)
        {
            SettingsService.Instance.IsBtrDriverContacted = unlocked;
                        ProgressChanged?.Invoke(this, EventArgs.Empty);
        }



        private bool IsQuestDone(TarkovTask task)

        {

            if (task.Ids != null)

            {

                foreach (var id in task.Ids)

                {

                    if (!string.IsNullOrEmpty(id) && _questProgress.TryGetValue(id, out var st) && st == QuestStatus.Done)

                        return true;

                }

            }

            if (!string.IsNullOrEmpty(task.NormalizedName) && _questProgress.TryGetValue(task.NormalizedName, out var nSt) && nSt == QuestStatus.Done)

                return true;

            if (!string.IsNullOrEmpty(task.Name) && _questProgress.TryGetValue(task.Name, out var nameSt) && nameSt == QuestStatus.Done)

                return true;

            return false;

        }



        /// <summary>

        /// Check if Scav Karma (Fence reputation) meets quest requirement

        /// </summary>

        public bool IsScavKarmaRequirementMet(TarkovTask task)

        {

            // If no karma requirement, always met

            if (!task.RequiredScavKarma.HasValue)

                return true;



            var playerScavRep = SettingsService.Instance.ScavRep;

            var requiredKarma = task.RequiredScavKarma.Value;



            // Negative requirement means player karma must be <= that value (bad karma quests)

            // Positive requirement means player karma must be >= that value (good karma quests)

            if (requiredKarma < 0)

            {

                return playerScavRep <= requiredKarma;

            }

            else

            {

                return playerScavRep >= requiredKarma;

            }

        }



        /// <summary>

        /// Check if edition requirements are met for the quest

        /// Returns false if quest is unavailable due to edition restrictions

        /// </summary>

        public bool IsEditionRequirementMet(TarkovTask task)

        {

            var settings = SettingsService.Instance;



            // Check required edition (EOD and Unheard are independent)

            if (!string.IsNullOrEmpty(task.RequiredEdition))

            {

                var requiredEdition = task.RequiredEdition.ToLowerInvariant();



                // EOD edition requirement - only EOD checkbox matters

                if (requiredEdition == "eod" || requiredEdition == "edge_of_darkness")

                {

                    if (!settings.HasEodEdition)

                        return false;

                }

                // Unheard edition requirement - only Unheard checkbox matters

                else if (requiredEdition == "unheard" || requiredEdition == "the_unheard")

                {

                    if (!settings.HasUnheardEdition)

                        return false;

                }

            }



            // Check excluded edition

            if (!string.IsNullOrEmpty(task.ExcludedEdition))

            {

                var excludedEdition = task.ExcludedEdition.ToLowerInvariant();



                // Excluded from EOD edition

                if (excludedEdition == "eod" || excludedEdition == "edge_of_darkness")

                {

                    if (settings.HasEodEdition)

                        return false;

                }

                // Excluded from Unheard edition

                else if (excludedEdition == "unheard" || excludedEdition == "the_unheard")

                {

                    if (settings.HasUnheardEdition)

                        return false;

                }

            }



            return true;

        }



        /// <summary>

        /// Check if prestige level requirement is met for the quest

        /// </summary>

        public bool IsPrestigeLevelRequirementMet(TarkovTask task)

        {

            // If no prestige level requirement, always met

            if (!task.RequiredPrestigeLevel.HasValue || task.RequiredPrestigeLevel.Value <= 0)

                return true;



            var playerPrestige = SettingsService.Instance.PrestigeLevel;

            return playerPrestige >= task.RequiredPrestigeLevel.Value;

        }



        /// <summary>

        /// Check if faction requirement is met for the quest

        /// Returns false if player chose a faction and quest is for the opposite faction

        /// </summary>

        public bool IsFactionRequirementMet(TarkovTask task)

        {

            // If quest has no faction requirement, always available

            if (string.IsNullOrEmpty(task.Faction))

                return true;



            // Use SettingsService's existing faction check logic

            return SettingsService.Instance.ShouldIncludeTask(task.Faction);

        }



        /// <summary>

        /// Check if DSP Decode Count requirement is met for the quest.

        /// Uses the RequiredDecodeCount field from the database.

        /// </summary>

        public bool IsDspRequirementMet(TarkovTask task)

        {

            // If no decode count requirement, always met

            if (!task.RequiredDecodeCount.HasValue)

                return true;



            var dspCount = SettingsService.Instance.DspDecodeCount;



            // RequiredDecodeCount specifies the exact DSP decode count needed

            return dspCount == task.RequiredDecodeCount.Value;

        }



        /// <summary>

        /// Check if a quest is completed by its normalized name

        /// Used for Collector quest progress calculation

        /// </summary>

        public bool IsQuestCompleted(string normalizedName)

        {

            var task = GetTask(normalizedName);

            if (task == null) return false;



            return GetStatus(task) == QuestStatus.Done;

        }



        /// <summary>

        /// Check if all prerequisites are met based on taskRequirements or legacy Previous field.

        /// Supports OR groups: GroupId = 0 means AND condition, GroupId > 0 means OR condition within the same group.

        /// </summary>

        public bool ArePrerequisitesMet(TarkovTask task)

        {

            // Use taskRequirements if available (more accurate status conditions with OR group support)

            if (task.TaskRequirements != null && task.TaskRequirements.Count > 0)

            {

                // Group requirements by GroupId

                var andRequirements = task.TaskRequirements.Where(r => r.GroupId == 0).ToList();

                var orGroups = task.TaskRequirements

                    .Where(r => r.GroupId > 0)

                    .GroupBy(r => r.GroupId)

                    .ToList();





                // Check AND requirements (GroupId = 0): ALL must be satisfied

                foreach (var req in andRequirements)

                {

                    // Primary: lookup by TaskId, fallback to TaskNormalizedName for backwards compatibility

                    var reqTask = !string.IsNullOrEmpty(req.TaskId)

                        ? GetTaskById(req.TaskId)

                        : GetTask(req.TaskNormalizedName);



                    if (reqTask == null)

                        continue;



                    var reqStatus = GetStatus(reqTask);

                    var satisfied = IsStatusSatisfied(reqStatus, req.Status);

                    if (!satisfied)

                        return false;

                }



                // Check OR groups (GroupId > 0): ANY ONE in each group must be satisfied

                foreach (var group in orGroups)

                {

                    bool anyInGroupSatisfied = false;



                    foreach (var req in group)

                    {

                        // Primary: lookup by TaskId, fallback to TaskNormalizedName for backwards compatibility

                        var reqTask = !string.IsNullOrEmpty(req.TaskId)

                            ? GetTaskById(req.TaskId)

                            : GetTask(req.TaskNormalizedName);



                        if (reqTask == null)

                            continue;



                        var reqStatus = GetStatus(reqTask);

                        var satisfied = IsStatusSatisfied(reqStatus, req.Status);

                        if (satisfied)

                        {

                            anyInGroupSatisfied = true;

                            break;

                        }

                    }



                    // If no requirement in this OR group is satisfied, prerequisites are not met

                    if (!anyInGroupSatisfied)

                        return false;

                }



                return true;

            }



            // Fallback to legacy Previous field (assumes 'complete' required)

            if (task.Previous == null || task.Previous.Count == 0)

                return true;



            foreach (var prevName in task.Previous)

            {

                var prevTask = GetTask(prevName);

                if (prevTask == null) continue;



                var prevStatus = GetStatus(prevTask);

                if (prevStatus != QuestStatus.Done)

                    return false;

            }



            return true;

        }



        /// <summary>

        /// Check if the current quest status satisfies the required status conditions.

        /// Handles both tarkov.dev API values ("active", "complete", "failed") and

        /// DB/Wiki values ("Start", "Accept", "Complete", "Fail").

        /// </summary>

        private bool IsStatusSatisfied(QuestStatus currentStatus, List<string>? requiredStatuses)

        {

            if (requiredStatuses == null || requiredStatuses.Count == 0)

            {

                // Default: require 'complete'

                return currentStatus == QuestStatus.Done;

            }



            // Check each required status

            foreach (var required in requiredStatuses)

            {

                switch (required.ToLowerInvariant())

                {

                    case "active":

                    case "start":    // DB value: RequirementType = "Start"

                    case "accept":   // DB value: RequirementType = "Accept"

                        // Quest is active, available to accept, or completed
                        if (currentStatus == QuestStatus.Active || currentStatus == QuestStatus.Done)

                            return true;

                        break;



                    case "complete":

                        if (currentStatus == QuestStatus.Done)

                            return true;

                        break;



                    case "failed":

                    case "fail":     // DB value: RequirementType = "Fail"

                        if (currentStatus == QuestStatus.Failed)

                            return true;

                        break;

                }

            }



            return false;

        }



        /// <summary>

        /// Mark quest as completed, optionally completing prerequisites

        /// Also automatically fails alternative quests (mutually exclusive quests)

        /// </summary>

        public void CompleteQuest(TarkovTask task, bool completePrerequisites = true)

        {

            if (string.Equals(task.Trader, "Story", StringComparison.OrdinalIgnoreCase) ||

                string.Equals(task.Trader, "Story Chapters", StringComparison.OrdinalIgnoreCase))

            {

                completePrerequisites = false;

            }



            var taskId = task.Ids?.FirstOrDefault();

            System.Diagnostics.Debug.WriteLine($"[QuestProgressService] CompleteQuest: {taskId} ({task.Name}), prerequisites: {completePrerequisites}");



            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var changedQuests = new List<(string Id, string? NormalizedName, QuestStatus Status)>();



            CompleteQuestInternalOptimized(task, completePrerequisites, visited, changedQuests);



            // Fail alternative quests (mutually exclusive)

            if (task.AlternativeQuests != null && task.AlternativeQuests.Count > 0)

            {

                foreach (var altQuestName in task.AlternativeQuests)

                {

                    // Try to find by NormalizedName (current data format) or by Id

                    var altTask = GetTask(altQuestName) ?? GetTaskById(altQuestName);

                    if (altTask != null)

                    {

                        var altStatus = GetStatus(altTask);

                        // Only fail if not already done or failed

                        if (altStatus != QuestStatus.Done && altStatus != QuestStatus.Failed)

                        {

                            var altId = altTask.Ids?.FirstOrDefault();

                            var altKey = altId ?? altQuestName;

                            _questProgress[altKey] = QuestStatus.Failed;

                            changedQuests.Add((altId ?? altKey, altTask.NormalizedName, QuestStatus.Failed));

                            System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Auto-failed alternative quest: {altKey} ({altTask.Name})");

                        }

                    }

                }

            }



            // Save and notify only once after all recursive completions

            if (changedQuests.Count > 0)

            {

                // PVE/PVP 격리를 위해 로드된 프로필 정보를 명시적으로 전달

                _ = SaveProgressBatchAsync(changedQuests, _loadedProfile);

                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Progress save initiated for {_loadedProfile}");

                ProgressChanged?.Invoke(this, EventArgs.Empty);

            }

            else

            {

                System.Diagnostics.Debug.WriteLine("[QuestProgressService] No changes to save");

            }

        }



        /// <summary>

        /// Optimized internal method to complete quest - collects changes without saving

        /// Skips alternative quests (mutually exclusive) when completing prerequisites

        /// </summary>

        private void CompleteQuestInternalOptimized(TarkovTask task, bool completePrerequisites,

            HashSet<string> visited, List<(string Id, string? NormalizedName, QuestStatus Status)> changedQuests,

            bool skipAlternativeQuests = true)

        {

            if (string.Equals(task.Trader, "Story", StringComparison.OrdinalIgnoreCase) ||

                string.Equals(task.Trader, "Story Chapters", StringComparison.OrdinalIgnoreCase))

            {

                completePrerequisites = false;

            }



            var taskId = task.Ids?.FirstOrDefault();

            var taskKey = taskId ?? task.NormalizedName;



            if (string.IsNullOrEmpty(taskKey)) return;



            // Prevent circular reference - if already visiting this quest, skip

            if (!visited.Add(taskKey)) return;



            // Skip if already done (check by both Id and NormalizedName)

            if (_questProgress.TryGetValue(taskKey, out var currentStatus) && currentStatus == QuestStatus.Done)

                return;

            if (!string.IsNullOrEmpty(task.NormalizedName) &&

                _questProgress.TryGetValue(task.NormalizedName, out var statusByName) && statusByName == QuestStatus.Done)

                return;



            // Complete prerequisites first (recursive) using TaskRequirements

            if (completePrerequisites && task.TaskRequirements != null)

            {

                foreach (var req in task.TaskRequirements)

                {

                    var prevTask = !string.IsNullOrEmpty(req.TaskId)

                        ? GetTaskById(req.TaskId)

                        : GetTask(req.TaskNormalizedName);



                    if (prevTask != null && GetStatus(prevTask) != QuestStatus.Done)

                    {

                        // Skip alternative quests - user must choose which one to complete

                        if (skipAlternativeQuests && HasAlternativeQuests(prevTask))

                        {

                            System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Skipping alternative quest: {prevTask.Name}");

                            continue;

                        }

                        CompleteQuestInternalOptimized(prevTask, true, visited, changedQuests, skipAlternativeQuests);

                    }

                }

            }

            // Fallback to Previous list

            else if (completePrerequisites && task.Previous != null)

            {

                foreach (var prevName in task.Previous)

                {

                    var prevTask = GetTask(prevName);

                    if (prevTask != null && GetStatus(prevTask) != QuestStatus.Done)

                    {

                        // Skip alternative quests - user must choose which one to complete

                        if (skipAlternativeQuests && HasAlternativeQuests(prevTask))

                        {

                            System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Skipping alternative quest: {prevTask.Name}");

                            continue;

                        }

                        CompleteQuestInternalOptimized(prevTask, true, visited, changedQuests, skipAlternativeQuests);

                    }

                }

            }



            _questProgress[taskKey] = QuestStatus.Done;

            changedQuests.Add((taskId ?? taskKey, task.NormalizedName, QuestStatus.Done));

        }



        /// <summary>

        /// Save changed quests in batch (fire-and-forget, doesn't block UI)

        /// </summary>

        private async Task SaveProgressBatchAsync(List<(string Id, string? NormalizedName, QuestStatus Status)> changedQuests, ProfileType? profileType = null)

        {

            try

            {

                var targetProfile = profileType ?? _loadedProfile;

                await _userDataDb.SaveQuestProgressBatchAsync(changedQuests, targetProfile);

                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Batch saved {changedQuests.Count} quest changes for {targetProfile}");

            }

            catch (Exception ex)

            {

                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Batch save failed: {ex.Message}");

            }

        }



        /// <summary>

        /// Complete multiple quests in batch (for log sync - started quest prerequisites)

        /// Single DB transaction, single UI update

        /// Skips alternative quests (mutually exclusive) by default

        /// </summary>

        public void CompleteQuestsBatch(IEnumerable<TarkovTask> tasks, bool skipAlternativeQuests = true)

        {

            var changedQuests = new List<(string Id, string? NormalizedName, QuestStatus Status)>();

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);



            foreach (var task in tasks)

            {

                var taskId = task.Ids?.FirstOrDefault();

                var taskKey = taskId ?? task.NormalizedName;



                if (string.IsNullOrEmpty(taskKey)) continue;

                if (!visited.Add(taskKey)) continue;



                // Skip alternative quests - user must choose which one to complete

                if (skipAlternativeQuests && HasAlternativeQuests(task))

                {

                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Skipping alternative quest in batch: {task.Name}");

                    continue;

                }



                // Skip if already done

                if (_questProgress.TryGetValue(taskKey, out var currentStatus) && currentStatus == QuestStatus.Done)

                    continue;

                if (!string.IsNullOrEmpty(task.NormalizedName) &&

                    _questProgress.TryGetValue(task.NormalizedName, out var statusByName) && statusByName == QuestStatus.Done)

                    continue;



                _questProgress[taskKey] = QuestStatus.Done;

                changedQuests.Add((taskId ?? taskKey, task.NormalizedName, QuestStatus.Done));

            }



            if (changedQuests.Count > 0)

            {

                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Batch completing {changedQuests.Count} quests for {_loadedProfile}");

                _ = SaveProgressBatchAsync(changedQuests, _loadedProfile);

                ProgressChanged?.Invoke(this, EventArgs.Empty);

            }

        }





        /// <summary>

        /// Apply multiple quest changes in batch (for sync operations)

        /// Saves to DB once after all changes are applied

        /// </summary>

        public async Task ApplyQuestChangesBatchAsync(IEnumerable<(TarkovTask Task, QuestStatus Status)> changes)

        {

            var changedItems = new List<(string Id, string? NormalizedName, QuestStatus Status)>();

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);



            foreach (var (task, status) in changes)

            {

                var taskId = task.Ids?.FirstOrDefault();

                var taskKey = taskId ?? task.NormalizedName;



                if (string.IsNullOrEmpty(taskKey)) continue;



                switch (status)

                {

                    case QuestStatus.Done:

                        // Complete without recursive save

                        if (CompleteQuestBatchInternal(task, visited, changedItems))

                        {

                            // Handle alternative quests (mutually exclusive)

                            if (task.AlternativeQuests != null)

                            {

                                foreach (var altQuestName in task.AlternativeQuests)

                                {

                                    var altTask = GetTask(altQuestName) ?? GetTaskById(altQuestName);

                                    if (altTask != null)

                                    {

                                        var altStatus = GetStatus(altTask);

                                        if (altStatus != QuestStatus.Done && altStatus != QuestStatus.Failed)

                                        {

                                            var altId = altTask.Ids?.FirstOrDefault();

                                            var altKey = altId ?? altQuestName;

                                            _questProgress[altKey] = QuestStatus.Failed;

                                            changedItems.Add((altId ?? altKey, altTask.NormalizedName, QuestStatus.Failed));

                                        }

                                    }

                                }

                            }

                        }

                        break;



                    case QuestStatus.Failed:

                        if (!_questProgress.TryGetValue(taskKey, out var currentStatus) || currentStatus != QuestStatus.Failed)

                        {

                            _questProgress[taskKey] = QuestStatus.Failed;

                            changedItems.Add((taskId ?? taskKey, task.NormalizedName, QuestStatus.Failed));

                        }

                        break;

                }

            }



            if (changedItems.Count > 0)

            {

                // Save all changes in one batch transaction

                await _userDataDb.SaveQuestProgressBatchAsync(changedItems);

                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Batch saved {changedItems.Count} quest changes");

                ProgressChanged?.Invoke(this, EventArgs.Empty);

            }

        }



        /// <summary>

        /// Internal batch completion - updates memory state and collects changes without saving

        /// </summary>

        private bool CompleteQuestBatchInternal(TarkovTask task, HashSet<string> visited, List<(string Id, string? NormalizedName, QuestStatus Status)> changedItems)

        {

            var taskId = task.Ids?.FirstOrDefault();

            var taskKey = taskId ?? task.NormalizedName;



            if (string.IsNullOrEmpty(taskKey)) return false;

            if (!visited.Add(taskKey)) return false;



            // Skip if already done

            if (_questProgress.TryGetValue(taskKey, out var currentStatus) && currentStatus == QuestStatus.Done)

                return false;

            if (!string.IsNullOrEmpty(task.NormalizedName) &&

                _questProgress.TryGetValue(task.NormalizedName, out var statusByName) && statusByName == QuestStatus.Done)

                return false;



            _questProgress[taskKey] = QuestStatus.Done;

            changedItems.Add((taskId ?? taskKey, task.NormalizedName, QuestStatus.Done));

            return true;

        }



        /// <summary>

        /// Mark quest as failed

        /// </summary>

        public void FailQuest(TarkovTask task)

        {

            var taskId = task.Ids?.FirstOrDefault();

            var taskKey = taskId ?? task.NormalizedName;



            if (string.IsNullOrEmpty(taskKey)) return;



            _questProgress[taskKey] = QuestStatus.Failed;

            // Fire-and-forget async save - don't block UI

            _ = Task.Run(async () =>

            {

                try

                {

                    await _userDataDb.SaveQuestProgressAsync(taskId ?? taskKey, task.NormalizedName, QuestStatus.Failed);

                }

                catch (Exception ex)

                {

                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to save failed quest: {ex.Message}");

                }

            });

            ProgressChanged?.Invoke(this, EventArgs.Empty);

        }



        /// <summary>

        /// Reset quest to active state

        /// </summary>

        public void ResetQuest(TarkovTask task)

        {

            var taskId = task.Ids?.FirstOrDefault();

            var taskKey = taskId ?? task.NormalizedName;



            if (string.IsNullOrEmpty(taskKey)) return;



            // Remove by both Id and NormalizedName for clean migration

            _questProgress.Remove(taskKey);

            if (!string.IsNullOrEmpty(task.NormalizedName) && task.NormalizedName != taskKey)

            {

                _questProgress.Remove(task.NormalizedName);

            }



            // Fire-and-forget async delete - don't block UI

            _ = Task.Run(async () =>

            {

                try

                {

                    await _userDataDb.DeleteQuestProgressAsync(taskId ?? taskKey);

                    // Also delete by NormalizedName for clean migration

                    if (!string.IsNullOrEmpty(task.NormalizedName) && task.NormalizedName != taskKey)

                    {

                        await _userDataDb.DeleteQuestProgressAsync(task.NormalizedName);

                    }

                }

                catch (Exception ex)

                {

                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to delete quest progress: {ex.Message}");

                }

            });

            ProgressChanged?.Invoke(this, EventArgs.Empty);

        }



        /// <summary>

        /// Sets or clears QuestStatus.Active manual override in _questProgress,

        /// persists to user_data.db via UserDataDbService, and fires ProgressChanged event.

        /// </summary>

        public void SetQuestActiveOverride(string taskId, bool isActive)

        {

            _ = SetQuestActiveOverrideAsync(taskId, isActive);

        }



        /// <summary>

        /// Asynchronously sets or clears QuestStatus.Active manual override in _questProgress,

        /// persists to user_data.db via UserDataDbService, and fires ProgressChanged event.

        /// </summary>

        
        public async Task SetQuestOverrideAsync(string taskId, QuestStatus? overrideStatus)
        {
            if (string.IsNullOrEmpty(taskId)) return;

            var task = GetTaskById(taskId) ?? GetTask(taskId);
            var normName = task?.NormalizedName;

            if (overrideStatus.HasValue)
            {
                var status = overrideStatus.Value;
                _questProgress[taskId] = status;
                if (!string.IsNullOrEmpty(normName) && normName != taskId)
                {
                    _questProgress[normName] = status;
                }

                try
                {
                    await _userDataDb.SaveQuestProgressAsync(taskId, normName, status, _loadedProfile);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to save quest override: {ex.Message}");
                }
            }
            else
            {
                _questProgress.Remove(taskId);
                if (!string.IsNullOrEmpty(normName))
                {
                    _questProgress.Remove(normName);
                }

                try
                {
                    await _userDataDb.DeleteQuestProgressAsync(taskId, _loadedProfile);
                    if (!string.IsNullOrEmpty(normName) && normName != taskId)
                    {
                        await _userDataDb.DeleteQuestProgressAsync(normName, _loadedProfile);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to delete quest override: {ex.Message}");
                }
            }

            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetQuestOverride(string taskId, QuestStatus? overrideStatus)
        {
            _ = SetQuestOverrideAsync(taskId, overrideStatus);
        }

        public bool IsQuestForceUnlocked(string taskId)
        {
            if (string.IsNullOrEmpty(taskId)) return false;
            if (_questProgress.TryGetValue(taskId, out var s) && s == QuestStatus.Active) return true;
            string bsgId = CanonicalQuestMap.ResolveBsgId(taskId);
            if (!string.IsNullOrEmpty(bsgId) && _questProgress.TryGetValue(bsgId, out s) && s == QuestStatus.Active) return true;
            return false;
        }

        public bool IsQuestForceLocked(string taskId)
        {
            if (string.IsNullOrEmpty(taskId)) return false;
            if (_questProgress.TryGetValue(taskId, out var s) && s == QuestStatus.Locked) return true;
            string bsgId = CanonicalQuestMap.ResolveBsgId(taskId);
            if (!string.IsNullOrEmpty(bsgId) && _questProgress.TryGetValue(bsgId, out s) && s == QuestStatus.Locked) return true;
            return false;
        }

        public bool HasQuestManualOverride(string taskId)
        {
            return IsQuestForceUnlocked(taskId) || IsQuestForceLocked(taskId);
        }

        public async Task SetQuestActiveOverrideAsync(string taskId, bool isActive)

        {

            if (string.IsNullOrEmpty(taskId)) return;



            var task = GetTaskById(taskId) ?? GetTask(taskId);

            var normName = task?.NormalizedName;



            if (isActive)

            {

                _questProgress[taskId] = QuestStatus.Active;

                if (!string.IsNullOrEmpty(normName) && normName != taskId)

                {

                    _questProgress[normName] = QuestStatus.Active;

                }



                try

                {

                    await _userDataDb.SaveQuestProgressAsync(taskId, normName, QuestStatus.Active, _loadedProfile);

                }

                catch (Exception ex)

                {

                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to save active override: {ex.Message}");

                }

            }

            else

            {

                _questProgress.Remove(taskId);

                if (!string.IsNullOrEmpty(normName))

                {

                    _questProgress.Remove(normName);

                }



                try

                {

                    await _userDataDb.DeleteQuestProgressAsync(taskId, _loadedProfile);

                    if (!string.IsNullOrEmpty(normName) && normName != taskId)

                    {

                        await _userDataDb.DeleteQuestProgressAsync(normName, _loadedProfile);

                    }

                }

                catch (Exception ex)

                {

                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to clear active override: {ex.Message}");

                }

            }



            ProgressChanged?.Invoke(this, EventArgs.Empty);

        }



        /// <summary>

        /// Reset all quest progress

        /// </summary>

        public void ResetAllProgress()

        {

            _questProgress.Clear();

            ObjectiveProgressService.Instance.ClearAllProgress();



            // DB에서 모든 퀘스트 진행 데이터 삭제

            _ = Task.Run(async () =>

            {

                try

                {

                    var profileType = ProfileService.Instance.CurrentProfile;

                    await _userDataDb.ClearAllQuestProgressAsync(profileType);

                    System.Diagnostics.Debug.WriteLine("[QuestProgressService] All progress cleared from DB");

                }

                catch (Exception ex)

                {

                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Reset failed: {ex.Message}");

                }

            });



            ProgressChanged?.Invoke(this, EventArgs.Empty);

        }



        /// <summary>

        /// Get prerequisite quest chain for a task

        /// </summary>

        public List<TarkovTask> GetPrerequisiteChain(TarkovTask task)

        {

            var chain = new List<TarkovTask>();

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);



            CollectPrerequisites(task, chain, visited);



            return chain;

        }



        private void CollectPrerequisites(TarkovTask task, List<TarkovTask> chain, HashSet<string> visited)

        {

            if (task.Previous == null) return;



            foreach (var prevName in task.Previous)

            {

                if (visited.Contains(prevName)) continue;

                visited.Add(prevName);



                var prevTask = GetTask(prevName);

                if (prevTask != null)

                {

                    CollectPrerequisites(prevTask, chain, visited);

                    chain.Add(prevTask);

                }

            }

        }



        /// <summary>

        /// Get follow-up quests for a task

        /// </summary>

        public List<TarkovTask> GetFollowUpQuests(TarkovTask task)

        {

            var followUps = new List<TarkovTask>();



            if (task.LeadsTo != null)

            {

                foreach (var nextName in task.LeadsTo)

                {

                    var nextTask = GetTask(nextName);

                    if (nextTask != null)

                    {

                        followUps.Add(nextTask);

                    }

                }

            }



            return followUps;

        }



        /// <summary>

        /// Get alternative quests (mutually exclusive) for a task

        /// </summary>

        public List<TarkovTask> GetAlternativeQuests(TarkovTask task)

        {

            var alternatives = new List<TarkovTask>();



            if (task.AlternativeQuests != null)

            {

                foreach (var altName in task.AlternativeQuests)

                {

                    var altTask = GetTask(altName);

                    if (altTask != null)

                    {

                        alternatives.Add(altTask);

                    }

                }

            }



            return alternatives;

        }



        /// <summary>

        /// Get count statistics for quest statuses

        /// </summary>

        public (int Total, int Locked, int Active, int Done, int Failed, int LevelLocked, int Unavailable) GetStatistics()

        {

            int locked = 0, active = 0, done = 0, failed = 0, levelLocked = 0, unavailable = 0;



            foreach (var task in _allTasks)

            {

                var status = GetStatus(task);

                switch (status)

                {

                    case QuestStatus.Locked: locked++; break;

                    case QuestStatus.Active: active++; break;

                    case QuestStatus.Done: done++; break;

                    case QuestStatus.Failed: failed++; break;

                    case QuestStatus.LevelLocked: levelLocked++; break;

                    case QuestStatus.Unavailable: unavailable++; break;

                }

            }



            return (_allTasks.Count, locked, active, done, failed, levelLocked, unavailable);

        }



        #region Persistence



        private readonly UserDataDbService _userDataDb = UserDataDbService.Instance;



        private void SaveProgress()

        {

            // DB에 저장 (비동기로 실행)

            var profileType = ProfileService.Instance.CurrentProfile;

            _ = SaveProgressToDbAsync(profileType);

        }



        private async Task SaveProgressToDbAsync(ProfileType profileType)

        {

            try

            {

                var changedItems = new List<(string Id, string? NormalizedName, QuestStatus Status)>();

                foreach (var kvp in _questProgress)

                {

                    var normalizedName = kvp.Key;

                    var status = kvp.Value;



                    // ID 조회

                    string id = normalizedName;

                    if (_tasksById.TryGetValue(normalizedName, out var task))

                    {

                        id = task.Ids?.FirstOrDefault() ?? normalizedName;

                    }

                    

                    changedItems.Add((id, normalizedName, status));

                }

                

                if (changedItems.Count > 0)

                {

                    await _userDataDb.SaveQuestProgressBatchAsync(changedItems, profileType);

                }

            }

            catch (Exception ex)

            {

                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to save progress to DB: {ex.Message}");

            }

        }



        private void SaveSingleQuestProgress(string normalizedName, QuestStatus status)

        {

            Task.Run(async () =>

            {

                try

                {

                    string id = normalizedName;

                    if (_tasksById.TryGetValue(normalizedName, out var task))

                    {

                        id = task.Ids?.FirstOrDefault() ?? normalizedName;

                    }



                    await _userDataDb.SaveQuestProgressAsync(id, normalizedName, status);

                }

                catch (Exception ex)

                {

                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to save single quest progress: {ex.Message}");

                }

            });

        }



        private void DeleteSingleQuestProgress(string normalizedName)

        {

            Task.Run(async () =>

            {

                try

                {

                    string id = normalizedName;

                    if (_tasksById.TryGetValue(normalizedName, out var task))

                    {

                        id = task.Ids?.FirstOrDefault() ?? normalizedName;

                    }



                    await _userDataDb.DeleteQuestProgressAsync(id);

                }

                catch (Exception ex)

                {

                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to delete quest progress: {ex.Message}");

                }

            });

        }



        /// <summary>

        /// Load progress asynchronously

        /// </summary>

        public async Task LoadProgressAsync(ProfileType? profileType = null)

        {

            await LoadProgressFromDbAsync(profileType);

        }



        private async Task LoadProgressFromDbAsync(ProfileType? profileType = null)

        {

            try

            {

                var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

                _loadedProfile = actualProfileType;

                var dbProgress = await _userDataDb.LoadQuestProgressAsync(actualProfileType);



                _questProgress.Clear();

                foreach (var kvp in dbProgress)

                {

                    _questProgress[kvp.Key] = kvp.Value;

                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Loaded progress: {kvp.Key} = {kvp.Value}");

                }



                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Loaded {_questProgress.Count} quest progress from DB");



                // Restore persisted Trader Loyalty Level overrides

                _traderLevelOverrides.Clear();

                var traders = new[] { "prapor", "therapist", "fence", "skier", "peacekeeper", "mechanic", "ragman", "jaeger", "ref", "lightkeeper", "btr driver" };

                foreach (var trader in traders)

                {

                    var val = _userDataDb.GetSetting($"trader.ll.{trader}", actualProfileType);

                    if (!string.IsNullOrEmpty(val) && int.TryParse(val, out var lvl) && lvl >= 1 && lvl <= 4)

                    {

                        var friendly = ResolveTraderFriendlyName(trader);

                        _traderLevelOverrides[friendly] = lvl;

                        _traderLevelOverrides[trader] = lvl;

                        if (TraderAliases.TryGetValue(friendly, out var aliases))

                        {

                            foreach (var alias in aliases)

                            {

                                _traderLevelOverrides[alias] = lvl;

                            }

                        }

                    }

                }

                // Auto-reconcile trader unlock quests (TarkovTracker parity)
                ReconcileTraderUnlockQuests();

            }

            catch (Exception ex)

            {

                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to load progress from DB: {ex.Message}");

                _questProgress.Clear();

            }

        }



        #endregion

        /// <summary>
        /// Automatically reconciles trader unlock quests if the player has progressed past them.
        /// (e.g. If the player completed Acquaintance or reached Jaeger LL3, Introduction was canonically completed).
        /// </summary>
        private void ReconcileTraderUnlockQuests()
        {
            var unlockMappings = new (string Trader, string QuestId, string QuestNorm)[]
            {
                ("Jaeger", "5d2495a886f77425cd51e403", "introduction"),
                ("Ref", "66058cb22cee99303f1ba067", "easy-money---part-1"),
                ("Lightkeeper", "625d700cc48e6c62a440fab5", "getting-acquainted"),
                ("BTR Driver", "6752f6d83038f7df520c83e8", "a-helping-hand")
            };

            foreach (var (trader, questId, questNorm) in unlockMappings)
            {
                var task = GetTaskById(questId) ?? GetTask(questNorm);
                if (task == null) continue;

                if (!IsQuestDone(task))
                {
                    bool isUnlocked = HasAnyCompletedQuestForTrader(trader) || (_traderLevelOverrides.TryGetValue(trader, out var tLvl) && tLvl >= 2);
                    if (isUnlocked)
                    {
                        _questProgress[questId] = QuestStatus.Done;
                        if (!string.IsNullOrEmpty(task.NormalizedName))
                            _questProgress[task.NormalizedName] = QuestStatus.Done;

                        _ = _userDataDb.SaveQuestProgressAsync(questId, task.NormalizedName, QuestStatus.Done, _loadedProfile);
                    }
                }
            }
        }
    }

}

