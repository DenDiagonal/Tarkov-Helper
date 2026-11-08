using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using TarkovHelper.Models;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services.DataAudit;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Service for identifying and tracking needed keys and future mission items for active and upcoming quests.
    /// Integrates TarkovTracker key requirements, local database item requirements, and stash inventory status.
    /// </summary>
    public sealed class NeededKeysService
    {
        private static readonly ILogger _log = Log.For<NeededKeysService>();
        private static NeededKeysService? _instance;
        public static NeededKeysService Instance => _instance ??= new NeededKeysService();

        /// <summary>
        /// Resets the singleton instance (useful for unit testing).
        /// </summary>
        public static void ResetInstance()
        {
            _instance = null;
        }

        private readonly ItemInventoryService _inventory;
        private readonly ItemDbService _itemDb;
        private readonly QuestDbService _questDb;
        private readonly QuestProgressService _questProgress;
        private readonly IntegratedItemService _integratedItems;

        private readonly Dictionary<string, TarkovTrackerTask> _trackerTasksById = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TarkovTrackerTask> _trackerTasksByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private bool _trackerResourcesLoaded;

        /// <summary>
        /// Triggered when inventory or quest progress changes affecting needed keys.
        /// </summary>
        public event Action? DataChanged;

        /// <summary>
        /// Canonical mapping of known BSG item IDs to English item names for common quest keys.
        /// </summary>
        public static readonly Dictionary<string, string> CanonicalKeyNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["5448ba0b4bdc2d02308b456c"] = "Factory emergency exit key",
            ["5780cf7f2459777de4559322"] = "Dorm room 314 marked key",
            ["5780cf912459777de4559325"] = "Dorm room 114 key",
            ["5780cf942459777df90dcb72"] = "Dorm room 214 key",
            ["5780cfa52459777dfb276eb1"] = "Dorm room 220 key",
            ["5780cfba2459777de4559329"] = "Dorm room 220 key",
            ["5780cfea2459777de807b5a2"] = "Dorm room 206 key",
            ["5780d0532459777a5108b9a2"] = "Tarcone Director's office key",
            ["5780d0802459777a5108b9a6"] = "Dorm room 204 key",
            ["5780d0b02459777a5108b9aa"] = "Cabin key",
            ["5913611c86f77479e0084092"] = "Trailer park portable cabin key",
            ["5937ee6486f77408994ba448"] = "Machinery key",
            ["5937fd0086f7742bf33fc198"] = "Unknown key",
            ["5938144586f77473c2087145"] = "Portable bunkhouse key",
            ["5938188786f77474f723e87f"] = "Secure Folder 0031 (Quest Item)",
            ["5938504186f7740991483f30"] = "Dorm room 203 key",
            ["5938603e86f77435642354f4"] = "Dorm room 206 key",
            ["59387a4986f77401cc236e62"] = "Dorm room 114 key",
            ["5939627786f77405c754020a"] = "Trailer park cabin key",
            ["593962ca86f774068014d9af"] = "Unknown key",
            ["593aa4be86f77457f56379f8"] = "Dorm room 303 key",
            ["5a0eb6ac86f7743124037a28"] = "Cottage back door key",
            ["5a0ee34586f774023b6ee092"] = "Health Resort west wing room 220 key",
            ["5a0eee1486f77402aa773226"] = "Health Resort east wing room 328 key",
            ["5a13ef0686f7746e5a411744"] = "Health Resort west wing room 219 key",
            ["5a13f46386f7741dd7384b04"] = "Health Resort west wing room 306 key",
            ["5a145d4786f7744cbb6f4a12"] = "Health Resort east wing room 306 key",
            ["5a145d7b86f7744cbb6f4a13"] = "Health Resort east wing room 308 key",
            ["5ad5cfbd86f7742c825d6104"] = "OLI logistics department office key",
            ["5ad5d7d286f77450166e0a89"] = "Kiba Arms outer door key",
            ["5addaffe86f77470b455f900"] = "Kiba Arms inner grate door key",
            ["5c1d0c5f86f7744bb2683cf0"] = "TerraGroup Labs keycard (Blue)",
            ["5c1d0d6d86f7744bbe05ec82"] = "TerraGroup Labs keycard (Red)",
            ["5c1d0dc586f7744baf2e7b79"] = "TerraGroup Labs keycard (Green)",
            ["5c1d0efb86f7744baf2e7b7b"] = "TerraGroup Labs keycard (Red)",
            ["5c1d0f4986f7744bb01837fa"] = "TerraGroup Labs keycard (Black)",
            ["5c1d0f6286f7744bb01837fc"] = "TerraGroup Labs keycard (Violet)",
            ["5c1d0f8d86f7744bbe05ec86"] = "TerraGroup Labs keycard (Yellow)",
            ["5c1e2d1f86f77431e9280bee"] = "TerraGroup Labs weapon testing area key",
            ["5d80c60f86f77440373c4ee9"] = "RB-KPRL key",
            ["5d80c68686f7744036212b43"] = "RB-RLS key",
            ["5d80c6c586f77440351beef1"] = "RB-OB key",
            ["5d80c76e86f7744036212b45"] = "RB-MP21 key",
            ["5d80c7a586f77440373c4eea"] = "RB-MP22 key",
            ["5d80c84186f77440373c4eeb"] = "RB-PSP1 key",
            ["5d80c88586f7744036212b47"] = "RB-PSP2 key",
            ["5d80c8be86f7744036212b49"] = "RB-PS81 key",
            ["5d80c8fe86f77440373c4eec"] = "RB-PS82 key",
            ["5d80c95086f7744036212b4b"] = "RB-ORB1 key",
            ["5d80c98f86f77440373c4eed"] = "RB-ORB2 key",
            ["5d80c9d686f77440373c4eee"] = "RB-ORB3 key",
            ["5d80ca1986f7744036212b4d"] = "RB-OB key",
            ["5d80ca5b86f7744036212b4f"] = "RB-AO key",
            ["5d80ca9f86f77440373c4eef"] = "RB-AM key",
            ["5d80cad986f7744036212b51"] = "RB-AK key",
            ["5d80cb1886f77440373c4ef0"] = "RB-BK marked key",
            ["5d80cb5786f7744036212b53"] = "RB-VO marked key",
            ["5d80cb9586f77440373c4ef1"] = "RB-PKPM marked key",
            ["5d80ccac86f77470841ff452"] = "RB-ORB1 key",
            ["5d80ccdd86f77474f7575e02"] = "RB-ORB2 key",
            ["5d80cd1a86f77402aa362f42"] = "RB-ORB3 key",
            ["5d947d3886f774447b415893"] = "RB-SMP key",
            ["5d947d4e86f774447b415895"] = "RB-KSM key",
            ["5d9f1fa686f774726974a992"] = "RB-ST key",
            ["5e42c81886f7742a01529f3f"] = "Object 11SR keycard",
            ["5e42c81886f7742a01529f57"] = "Object #11SR keycard",
            ["5e42c83786f7742a021fdf3c"] = "Object #21WS keycard",
            ["5efde6b4f5448336730dbd61"] = "Keycard with a blue marking",
            ["5eff09cd30a7dc22fd1ddfed"] = "Health Resort office key with a blue tape",
            ["62987c658081af308d7558c6"] = "Radar station commandant room key",
            ["62987da96188c076bc0d8c51"] = "Operating room key",
            ["6398fd8ad3de3849057f5128"] = "Backup hideout key",
            ["63a397d3af870e651d58e65b"] = "Car dealership closed section key",
            ["63a399193901f439517cafb6"] = "Car dealership director's office room key",
            ["63a39c7964283b5e9c56b280"] = "Concordia security room key",
            ["63a39e1d234195315d4020bd"] = "Primorsky 46-48 skybridge key",
            ["63a39e7c3901f439517cafb8"] = "TerraGroup meeting room key",
            ["63a39f08cd6db0635c197600"] = "Pinewood hotel room 215 key",
            ["63a39f6e64283b5e9c56b289"] = "Iron gate key",
            ["63a3a93f8a56922e82001f5d"] = "Abandoned factory marked key",
            ["64ccc1fe088064307e14a6f7"] = "Beluga restaurant director key",
            ["64ccc206793ca11c8f450a38"] = "TerraGroup meeting room key",
            ["64ccc246ff54fb38131acf29"] = "X-ray room key",
            ["64ccc25f95763a1ae376e447"] = "Mysterious room marked key",
            ["64d4b23dc1b37504b41ac2b6"] = "Rusted bloody key",
            ["6582dbe43a2e5248357dbe9a"] = "\"Negotiation\" room key",
            ["6582dbf0b8d7830efc45016f"] = "Relaxation room key",
            ["6582dc5740562727a654ebb1"] = "Real estate agency office room key",
            ["664d4b0103ef2c61246afb56"] = "Old champion room key",
            ["66acd6702b17692df20144c0"] = "TerraGroup storage room keycard",
            ["6761a6f90575f25e020816a4"] = "Company director's room key",
            ["679bab714e9ca6b3d80586b4"] = "Corpse room key",
        };

        private NeededKeysService()
        {
            _inventory = ItemInventoryService.Instance;
            _itemDb = ItemDbService.Instance;
            _questDb = QuestDbService.Instance;
            _questProgress = QuestProgressService.Instance;
            _integratedItems = IntegratedItemService.Instance;

            // Subscribe to inventory & quest updates
            _inventory.InventoryChanged += OnInventoryOrProgressChanged;
            _questProgress.ProgressChanged += OnInventoryOrProgressChanged;

            // Load any cached task tracker keys from scratch/ or disk
            LoadTrackerKeysFromResources();
        }

        private void OnInventoryOrProgressChanged(object? sender, EventArgs e)
        {
            DataChanged?.Invoke();
        }

        /// <summary>
        /// Registers a TarkovTracker task into the service's key registry.
        /// </summary>
        public void RegisterTrackerTask(TarkovTrackerTask task)
        {
            if (task == null) return;
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(task.Id))
                    _trackerTasksById[task.Id] = task;

                if (!string.IsNullOrEmpty(task.NormalizedName))
                    _trackerTasksByName[task.NormalizedName] = task;

                if (!string.IsNullOrEmpty(task.Name))
                    _trackerTasksByName[task.Name] = task;
            }
        }

        /// <summary>
        /// Registers multiple TarkovTracker tasks into the service's key registry.
        /// </summary>
        public void RegisterTrackerTasks(IEnumerable<TarkovTrackerTask> tasks)
        {
            if (tasks == null) return;
            foreach (var task in tasks)
            {
                RegisterTrackerTask(task);
            }
        }

        /// <summary>
        /// Loads tracker task definitions from scratch json files if available.
        /// </summary>
        public void LoadTrackerKeysFromResources(bool forceReload = false)
        {
            lock (_lock)
            {
                if (_trackerResourcesLoaded && !forceReload && _trackerTasksById.Count > 0)
                    return;
            }
            try
            {
                string[] coreCandidates = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Data", "tasks_core.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scratch", "tasks_core.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "scratch", "tasks_core.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Assets", "DB", "Data", "tasks_core.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "scratch", "tasks_core.json")
                };
                var corePath = coreCandidates.FirstOrDefault(File.Exists) ?? coreCandidates[0];

                string[] objCandidates = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Data", "tasks_objectives.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scratch", "tasks_objectives.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "scratch", "tasks_objectives.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Assets", "DB", "Data", "tasks_objectives.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "scratch", "tasks_objectives.json")
                };
                var objPath = objCandidates.FirstOrDefault(File.Exists) ?? objCandidates[0];

                if (File.Exists(corePath))
                {
                    var coreJson = File.ReadAllText(corePath);
                    var coreData = TarkovTrackerExtractorService.ParseTasksCore(coreJson);
                    RegisterTrackerTasks(coreData.Tasks);
                    lock (_lock) { _trackerResourcesLoaded = true; }

                    if (File.Exists(objPath))
                    {
                        var objJson = File.ReadAllText(objPath);
                        var objectivesLookup = TarkovTrackerExtractorService.ParseTasksObjectives(objJson);
                        foreach (var task in coreData.Tasks)
                        {
                            if (objectivesLookup.TryGetValue(task.Id, out var objs))
                            {
                                task.Objectives = objs;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Failed to load scratch tracker task resources: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves human-readable item name from key ID or fallback name.
        /// </summary>
        public string ResolveKeyName(string keyId, string? fallbackName = null)
        {
            if (string.IsNullOrWhiteSpace(keyId))
                return fallbackName ?? string.Empty;

            // 1. Check canonical dictionary
            if (CanonicalKeyNames.TryGetValue(keyId, out var canonicalName))
                return canonicalName;

            // 2. Check ItemDb lookup
            if (_itemDb.IsLoaded)
            {
                var lookup = _itemDb.GetItemLookup();
                if (lookup.TryGetValue(keyId, out var item))
                    return item.Name;
            }

            return fallbackName ?? keyId;
        }

        private static readonly Regex KeyWordRegex = new(@"\b(key|keycard|keychain)s?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Determines whether an item or quest requirement represents a key or keycard.
        /// </summary>
        public static bool IsKey(string? name, string? category = null)
        {
            if (string.IsNullOrEmpty(name)) return false;

            if (!string.IsNullOrEmpty(category) &&
                (string.Equals(category, "Keys", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(category, "Keycards", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return KeyWordRegex.IsMatch(name);
        }

        /// <summary>
        /// Determines whether a given key is currently present in the player's stash inventory.
        /// </summary>
        public bool IsKeyInStash(string keyIdOrName, out int quantity)
        {
            quantity = 0;
            if (string.IsNullOrWhiteSpace(keyIdOrName)) return false;

            // Check normalized name
            var normalized = NormalizedNameGenerator.Generate(keyIdOrName);
            if (!string.IsNullOrEmpty(normalized))
            {
                quantity = _inventory.GetTotalQuantity(normalized);
                if (quantity > 0) return true;
            }

            // Check direct id or name
            quantity = _inventory.GetTotalQuantity(keyIdOrName);
            if (quantity > 0) return true;

            // If it's a known canonical key, check its English name and normalized name
            if (CanonicalKeyNames.TryGetValue(keyIdOrName, out var englishName))
            {
                var canonNorm = NormalizedNameGenerator.Generate(englishName);
                quantity = _inventory.GetTotalQuantity(canonNorm);
                if (quantity > 0) return true;

                quantity = _inventory.GetTotalQuantity(englishName);
                if (quantity > 0) return true;
            }

            // Check ItemDb item
            if (_itemDb.IsLoaded)
            {
                var lookup = _itemDb.GetItemLookup();
                if (lookup.TryGetValue(keyIdOrName, out var item))
                {
                    if (!string.IsNullOrEmpty(item.NormalizedName))
                    {
                        quantity = _inventory.GetTotalQuantity(item.NormalizedName);
                        if (quantity > 0) return true;
                    }

                    if (!string.IsNullOrEmpty(item.Id))
                    {
                        quantity = _inventory.GetTotalQuantity(item.Id);
                        if (quantity > 0) return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Gets all needed keys for a specific quest, checking both QuestRequiredItems and TarkovTracker objectives.
        /// </summary>
        public List<NeededKeyInfo> GetNeededKeysForQuest(TarkovTask quest)
        {
            if (quest == null) return new List<NeededKeyInfo>();

            var result = new List<NeededKeyInfo>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var questStatus = _questProgress.GetStatus(quest);
            var isActive = questStatus == QuestStatus.Active;
            var isUpcoming = questStatus == QuestStatus.Locked || questStatus == QuestStatus.LevelLocked || questStatus == QuestStatus.Unavailable;

            var defaultMap = quest.Maps?.FirstOrDefault() ?? "Any Map";

            // 1. Gather keys from quest.RequiredItems (from SQLite / database)
            if (quest.RequiredItems != null)
            {
                foreach (var reqItem in quest.RequiredItems)
                {
                    if (reqItem == null) continue;
                    var itemName = reqItem.ItemDisplayName ?? reqItem.ItemNormalizedName;
                    if (!IsKey(itemName)) continue;

                    var keyId = reqItem.ItemNormalizedName ?? string.Empty;
                    var displayName = ResolveKeyName(keyId, itemName);

                    if (!string.IsNullOrEmpty(displayName) && seenKeys.Add(displayName))
                    {
                        IsKeyInStash(displayName, out int stashCount);
                        if (stashCount == 0 && !string.IsNullOrEmpty(keyId))
                            IsKeyInStash(keyId, out stashCount);

                        result.Add(new NeededKeyInfo
                        {
                            KeyId = keyId,
                            Name = displayName,
                            ShortName = ExtractShortName(displayName),
                            NormalizedName = NormalizedNameGenerator.Generate(displayName),
                            MapName = defaultMap,
                            QuestId = quest.Ids?.FirstOrDefault(),
                            QuestName = quest.Name ?? string.Empty,
                            QuestNormalizedName = quest.NormalizedName,
                            ObjectiveDescription = $"Required for {quest.Name}",
                            InStash = stashCount > 0,
                            StashQuantity = stashCount,
                            IsActiveQuest = isActive,
                            IsUpcomingQuest = isUpcoming
                        });
                    }
                }
            }

            // 2. Gather keys from TarkovTracker Task registry (if registered)
            TarkovTrackerTask? trackerTask = null;
            lock (_lock)
            {
                if (quest.Ids != null)
                {
                    foreach (var id in quest.Ids)
                    {
                        if (_trackerTasksById.TryGetValue(id, out trackerTask))
                            break;
                    }
                }

                if (trackerTask == null && !string.IsNullOrEmpty(quest.NormalizedName))
                {
                    _trackerTasksByName.TryGetValue(quest.NormalizedName, out trackerTask);
                }

                if (trackerTask == null && !string.IsNullOrEmpty(quest.Name))
                {
                    _trackerTasksByName.TryGetValue(quest.Name, out trackerTask);
                }
            }

            if (trackerTask != null)
            {
                // Process trackerTask.NeededKeys
                if (trackerTask.NeededKeys != null)
                {
                    foreach (var group in trackerTask.NeededKeys)
                    {
                        var mapName = group.Map?.Name ?? defaultMap;
                        foreach (var keyRef in group.Keys)
                        {
                            var resolvedName = ResolveKeyName(keyRef.Id, keyRef.Name);
                            if (seenKeys.Add(resolvedName))
                            {
                                IsKeyInStash(resolvedName, out int count);
                                if (count == 0 && !string.IsNullOrEmpty(keyRef.Id))
                                    IsKeyInStash(keyRef.Id, out count);

                                result.Add(new NeededKeyInfo
                                {
                                    KeyId = keyRef.Id,
                                    Name = resolvedName,
                                    ShortName = keyRef.ShortName ?? ExtractShortName(resolvedName),
                                    NormalizedName = NormalizedNameGenerator.Generate(resolvedName),
                                    MapName = mapName,
                                    QuestId = quest.Ids?.FirstOrDefault(),
                                    QuestName = quest.Name ?? string.Empty,
                                    QuestNormalizedName = quest.NormalizedName,
                                    ObjectiveDescription = $"Access on {mapName}",
                                    InStash = count > 0,
                                    StashQuantity = count,
                                    IsActiveQuest = isActive,
                                    IsUpcomingQuest = isUpcoming
                                });
                            }
                        }
                    }
                }

                // Process trackerTask.Objectives.RequiredKeys
                if (trackerTask.Objectives != null)
                {
                    foreach (var obj in trackerTask.Objectives)
                    {
                        if (obj.RequiredKeys == null || obj.RequiredKeys.Count == 0) continue;

                        var mapName = obj.Maps?.FirstOrDefault()?.Name ?? defaultMap;

                        foreach (var keyGroup in obj.RequiredKeys)
                        {
                            var alternatives = keyGroup.Select(k => ResolveKeyName(k.Id, k.Name)).ToList();

                            foreach (var keyRef in keyGroup)
                            {
                                var resolvedName = ResolveKeyName(keyRef.Id, keyRef.Name);
                                if (seenKeys.Add(resolvedName))
                                {
                                    IsKeyInStash(resolvedName, out int count);
                                    if (count == 0 && !string.IsNullOrEmpty(keyRef.Id))
                                        IsKeyInStash(keyRef.Id, out count);

                                    result.Add(new NeededKeyInfo
                                    {
                                        KeyId = keyRef.Id,
                                        Name = resolvedName,
                                        ShortName = keyRef.ShortName ?? ExtractShortName(resolvedName),
                                        NormalizedName = NormalizedNameGenerator.Generate(resolvedName),
                                        MapName = mapName,
                                        QuestId = quest.Ids?.FirstOrDefault(),
                                        QuestName = quest.Name ?? string.Empty,
                                        QuestNormalizedName = quest.NormalizedName,
                                        ObjectiveDescription = obj.Description ?? $"Objective on {mapName}",
                                        InStash = count > 0,
                                        StashQuantity = count,
                                        IsActiveQuest = isActive,
                                        IsUpcomingQuest = isUpcoming,
                                        AlternativeKeys = alternatives.Where(a => !string.Equals(a, resolvedName, StringComparison.OrdinalIgnoreCase)).ToList()
                                    });
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Gets all needed keys for currently Active quests.
        /// </summary>
        public List<NeededKeyInfo> GetNeededKeysForActiveQuests()
        {
            var result = new List<NeededKeyInfo>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var quest in _questDb.AllQuests)
            {
                if (_questProgress.GetStatus(quest) != QuestStatus.Active)
                    continue;

                var keys = GetNeededKeysForQuest(quest);
                foreach (var k in keys)
                {
                    if (seenKeys.Add($"{k.Name}:{k.QuestName}"))
                    {
                        result.Add(k);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Gets needed keys for upcoming available quests.
        /// </summary>
        public List<NeededKeyInfo> GetNeededKeysForUpcomingQuests(int maxDepth = 2)
        {
            var result = new List<NeededKeyInfo>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var quest in _questDb.AllQuests)
            {
                var status = _questProgress.GetStatus(quest);
                if (status == QuestStatus.Done || status == QuestStatus.Active)
                    continue;

                var keys = GetNeededKeysForQuest(quest);
                foreach (var k in keys)
                {
                    if (seenKeys.Add($"{k.Name}:{k.QuestName}"))
                    {
                        result.Add(k);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Gets all needed keys across all uncompleted quests.
        /// </summary>
        public List<NeededKeyInfo> GetAllNeededKeys()
        {
            var result = new List<NeededKeyInfo>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var quest in _questDb.AllQuests)
            {
                if (_questProgress.IsQuestCompleted(quest.NormalizedName ?? ""))
                    continue;

                var keys = GetNeededKeysForQuest(quest);
                foreach (var k in keys)
                {
                    if (seenKeys.Add($"{k.Name}:{k.QuestName}"))
                    {
                        result.Add(k);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Gathers future mission items across uncompleted quests, cross-referenced with stash inventory
        /// and Hideout station crafting requirements.
        /// </summary>
        public List<FutureMissionItemInfo> GetFutureMissionItems(bool firOnly = false)
        {
            var items = new List<FutureMissionItemInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var quest in _questDb.AllQuests)
            {
                if (_questProgress.IsQuestCompleted(quest.NormalizedName ?? ""))
                    continue;

                var isQuestActive = _questProgress.GetStatus(quest) == QuestStatus.Active;
                if (quest.RequiredItems == null) continue;

                foreach (var reqItem in quest.RequiredItems)
                {
                    if (reqItem == null) continue;
                    if (firOnly && !reqItem.FoundInRaid)
                        continue;

                    // Skip keys if desired, or keep non-key mission items
                    var name = reqItem.ItemDisplayName ?? reqItem.ItemNormalizedName;
                    var uniqueKey = $"{name}:{quest.Name}:{reqItem.Requirement}";
                    if (!seen.Add(uniqueKey)) continue;

                    var norm = reqItem.ItemNormalizedName;
                    var fulfillment = _inventory.GetFulfillmentInfo(norm, reqItem.Amount, reqItem.FoundInRaid ? reqItem.Amount : 0);

                    // Check crafting / hideout linkage
                    string? stationName = null;
                    int? stationLevel = null;
                    try
                    {
                        var integrated = _integratedItems.GetItemRequirement(norm);
                        if (integrated != null && integrated.HideoutSources.Count > 0)
                        {
                            var hSource = integrated.HideoutSources[0];
                            stationName = hSource.StationName;
                            stationLevel = hSource.Level;
                        }
                    }
                    catch
                    {
                        // Ignore lookup failure
                    }

                    items.Add(new FutureMissionItemInfo
                    {
                        ItemId = norm,
                        ItemNormalizedName = norm,
                        ItemName = name,
                        RequiredCount = reqItem.Amount,
                        RequiresFir = reqItem.FoundInRaid,
                        OwnedFir = fulfillment.OwnedFir,
                        OwnedNonFir = fulfillment.OwnedNonFir,
                        RequirementType = reqItem.Requirement,
                        QuestName = quest.Name,
                        QuestNormalizedName = quest.NormalizedName ?? string.Empty,
                        IsQuestActive = isQuestActive,
                        HideoutStation = stationName,
                        HideoutStationLevel = stationLevel
                    });
                }
            }

            return items;
        }

        private static string ExtractShortName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return string.Empty;
            var shortName = fullName.Replace("keycard", "", StringComparison.OrdinalIgnoreCase)
                                    .Replace("key", "", StringComparison.OrdinalIgnoreCase)
                                    .Trim();
            return string.IsNullOrEmpty(shortName) ? fullName : shortName;
        }
    }

    /// <summary>
    /// Information model for a needed key for a quest.
    /// </summary>
    public class NeededKeyInfo
    {
        public string KeyId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? ShortName { get; set; }
        public string? NormalizedName { get; set; }
        public string? MapName { get; set; }
        public string? QuestId { get; set; }
        public string QuestName { get; set; } = string.Empty;
        public string? QuestNormalizedName { get; set; }
        public string? ObjectiveDescription { get; set; }
        public bool InStash { get; set; }
        public int StashQuantity { get; set; }
        public bool IsActiveQuest { get; set; }
        public bool IsUpcomingQuest { get; set; }
        public List<string> AlternativeKeys { get; set; } = new();
    }

    /// <summary>
    /// Information model for future mission items required across quests.
    /// </summary>
    public class FutureMissionItemInfo
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemNormalizedName { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public int RequiredCount { get; set; }
        public bool RequiresFir { get; set; }
        public int OwnedFir { get; set; }
        public int OwnedNonFir { get; set; }
        public int TotalOwned => OwnedFir + OwnedNonFir;
        public int EffectiveOwned => RequiresFir ? OwnedFir : TotalOwned;
        public int Shortage => Math.Max(0, RequiredCount - EffectiveOwned);
        public bool IsFulfilled => EffectiveOwned >= RequiredCount;
        public string RequirementType { get; set; } = "Required";
        public string QuestName { get; set; } = string.Empty;
        public string QuestNormalizedName { get; set; } = string.Empty;
        public bool IsQuestActive { get; set; }
        public string? HideoutStation { get; set; }
        public int? HideoutStationLevel { get; set; }
        public bool HasCraftingLinkage => !string.IsNullOrEmpty(HideoutStation);
    }
}
