using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services.DataAudit
{
    /// <summary>
    /// Compares normalized remote data (Wiki and community sources) against local SQLite database tables
    /// (Quests, QuestRequirements, QuestRequiredItems, HideoutStations, HideoutLevels, HideoutItemRequirements)
    /// and generates a classified DataDiffReport.
    /// </summary>
    public class DatabaseDiffEngine
    {
        private static readonly ILogger _log = Log.For<DatabaseDiffEngine>();
        private static DatabaseDiffEngine? _instance;
        public static DatabaseDiffEngine Instance => _instance ??= new DatabaseDiffEngine();

        #region Internal Local Record DTOs

        private sealed class LocalQuest
        {
            public string Id { get; set; } = string.Empty;
            public string? BsgId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? NameEN { get; set; }
            public string? NameKO { get; set; }
            public string? NameJA { get; set; }
            public string? WikiPageLink { get; set; }
            public string? Trader { get; set; }
            public string? Location { get; set; }
            public int? MinLevel { get; set; }
            public int? MinScavKarma { get; set; }
            public bool KappaRequired { get; set; }
            public string? Faction { get; set; }
            public string? RequiredEdition { get; set; }
            public string? ExcludedEdition { get; set; }
            public int? RequiredDecodeCount { get; set; }
            public int? RequiredPrestigeLevel { get; set; }
            public string NormalizedName { get; set; } = string.Empty;
        }

        private sealed class LocalQuestRequirement
        {
            public string Id { get; set; } = string.Empty;
            public string QuestId { get; set; } = string.Empty;
            public string RequiredQuestId { get; set; } = string.Empty;
            public string RequirementType { get; set; } = "Complete";
            public int? DelayMinutes { get; set; }
            public int GroupId { get; set; }
        }

        private sealed class LocalQuestRequiredItem
        {
            public string Id { get; set; } = string.Empty;
            public string QuestId { get; set; } = string.Empty;
            public string? ItemId { get; set; }
            public string ItemName { get; set; } = string.Empty;
            public int Count { get; set; } = 1;
            public bool RequiresFIR { get; set; }
            public string RequirementType { get; set; } = "Required";
            public int SortOrder { get; set; }
            public int? DogtagMinLevel { get; set; }
            public string? DogtagFaction { get; set; }
        }

        private sealed class LocalHideoutStation
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string? NormalizedName { get; set; }
            public int MaxLevel { get; set; }
        }

        private sealed class LocalHideoutLevel
        {
            public string Id { get; set; } = string.Empty;
            public string StationId { get; set; } = string.Empty;
            public int Level { get; set; }
            public int ConstructionTime { get; set; }
        }

        private sealed class LocalHideoutItemRequirement
        {
            public string Id { get; set; } = string.Empty;
            public string StationId { get; set; } = string.Empty;
            public int Level { get; set; }
            public string ItemId { get; set; } = string.Empty;
            public string ItemName { get; set; } = string.Empty;
            public int Count { get; set; } = 1;
            public bool FoundInRaid { get; set; }
        }

        private sealed class LocalQuestTraderRequirement
        {
            public string Id { get; set; } = string.Empty;
            public string QuestId { get; set; } = string.Empty;
            public string? TraderId { get; set; }
            public string TraderName { get; set; } = string.Empty;
            public string RequirementType { get; set; } = "Level";
            public string CompareMethod { get; set; } = ">=";
            public int RequiredLevel { get; set; }
            public int SortOrder { get; set; }
        }

        private sealed class LocalQuestPoolRequirement
        {
            public string Id { get; set; } = string.Empty;
            public string QuestId { get; set; } = string.Empty;
            public string PoolTrader { get; set; } = string.Empty;
            public int? PoolTraderLL { get; set; }
            public int RequiredCompletedCount { get; set; }
            public string? Description { get; set; }
        }

        #endregion

        /// <summary>
        /// Computes a classified difference report between remote normalized data and local SQLite database tables.
        /// </summary>
        public async Task<DataDiffReport> ComputeDiffAsync(
            NormalizedAuditData remoteData,
            string? dbPath,
            bool includeUnchanged,
            bool detectRemovedQuests,
            CancellationToken ct)
            => await ComputeDiffAsync(remoteData, dbPath, includeUnchanged, detectRemovedQuests, false, ct);

        /// <summary>
        /// Computes a classified difference report between remote normalized data and local SQLite database tables.
        /// </summary>
        /// <param name="remoteData">Normalized remote data bundle.</param>
        /// <param name="dbPath">Optional path to SQLite database; defaults to Assets/tarkov_data.db.</param>
        /// <param name="includeUnchanged">Whether to include unchanged items in the report (default false).</param>
        /// <param name="detectRemovedQuests">Whether to flag quests in DB that are missing from remote as Removed (default false).</param>
        /// <param name="pruneBrokenPrerequisites">Whether to prune unresolvable/phantom prerequisites from diff (default false).</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task<DataDiffReport> ComputeDiffAsync(
            NormalizedAuditData remoteData,
            string? dbPath = null,
            bool includeUnchanged = false,
            bool detectRemovedQuests = false,
            bool pruneBrokenPrerequisites = false,
            CancellationToken ct = default)
        {
            if (remoteData == null)
                throw new ArgumentNullException(nameof(remoteData));

            var resolvedDbPath = DatabaseBackupManager.ResolveDatabasePath(dbPath);
            if (!File.Exists(resolvedDbPath))
            {
                throw new FileNotFoundException($"Target database not found: {resolvedDbPath}", resolvedDbPath);
            }

            _log.Info($"Computing diff against database: {resolvedDbPath}...");

            var report = new DataDiffReport
            {
                GeneratedAt = DateTime.UtcNow,
                DatabasePath = resolvedDbPath
            };

            var connectionString = $"Data Source={resolvedDbPath};Mode=ReadOnly";
            await using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync(ct);

            // 1. Load local tables
            var localQuests = await LoadLocalQuestsAsync(conn, ct);
            var localQuestReqs = await LoadLocalQuestRequirementsAsync(conn, ct);
            var localQuestItems = await LoadLocalQuestRequiredItemsAsync(conn, ct);
            var localQuestTraderReqs = await LoadLocalQuestTraderRequirementsAsync(conn, ct);
            var localQuestPoolReqs = await LoadLocalQuestPoolRequirementsAsync(conn, ct);
            var localStations = await LoadLocalHideoutStationsAsync(conn, ct);
            var localHideoutLevels = await LoadLocalHideoutLevelsAsync(conn, ct);
            var localHideoutItemReqs = await LoadLocalHideoutItemRequirementsAsync(conn, ct);

            _log.Debug($"Loaded local tables: {localQuests.Count} quests, {localQuestReqs.Count} quest requirements, " +
                      $"{localQuestItems.Count} quest items, {localQuestTraderReqs.Count} trader requirements, {localQuestPoolReqs.Count} pool requirements, " +
                      $"{localStations.Count} stations, {localHideoutLevels.Count} hideout levels, {localHideoutItemReqs.Count} hideout item requirements.");

            // Build lookup indices for local entities
            var localQuestsById = new Dictionary<string, LocalQuest>(StringComparer.OrdinalIgnoreCase);
            var localQuestsByBsgId = new Dictionary<string, LocalQuest>(StringComparer.OrdinalIgnoreCase);
            var localQuestsByName = new Dictionary<string, List<LocalQuest>>(StringComparer.OrdinalIgnoreCase);
            var localQuestsByNormalizedName = new Dictionary<string, List<LocalQuest>>(StringComparer.OrdinalIgnoreCase);

            foreach (var q in localQuests)
            {
                if (!string.IsNullOrEmpty(q.Id)) localQuestsById[q.Id] = q;
                if (!string.IsNullOrEmpty(q.BsgId)) localQuestsByBsgId[q.BsgId] = q;
                if (!string.IsNullOrEmpty(q.Name))
                {
                    if (!localQuestsByName.TryGetValue(q.Name, out var list))
                    {
                        list = new List<LocalQuest>();
                        localQuestsByName[q.Name] = list;
                    }
                    list.Add(q);
                }
                if (!string.IsNullOrEmpty(q.NormalizedName))
                {
                    if (!localQuestsByNormalizedName.TryGetValue(q.NormalizedName, out var nList))
                    {
                        nList = new List<LocalQuest>();
                        localQuestsByNormalizedName[q.NormalizedName] = nList;
                    }
                    nList.Add(q);
                }
            }

            foreach (var kvp in CanonicalQuestMap.LegacyToBsg)
            {
                if (localQuestsById.TryGetValue(kvp.Value, out var targetQuest))
                {
                    localQuestsById.TryAdd(kvp.Key, targetQuest);
                }
            }

            var localReqsByQuestId = localQuestReqs
                .GroupBy(r => r.QuestId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var localItemsByQuestId = localQuestItems
                .GroupBy(i => i.QuestId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var localTraderReqsByQuestId = localQuestTraderReqs
                .GroupBy(r => r.QuestId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var localPoolReqsByQuestId = localQuestPoolReqs
                .GroupBy(r => r.QuestId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var localStationsById = new Dictionary<string, LocalHideoutStation>(StringComparer.OrdinalIgnoreCase);
            var localStationsByName = new Dictionary<string, LocalHideoutStation>(StringComparer.OrdinalIgnoreCase);
            foreach (var st in localStations)
            {
                if (!string.IsNullOrEmpty(st.Id)) localStationsById[st.Id] = st;
                if (!string.IsNullOrEmpty(st.Name)) localStationsByName[st.Name] = st;
                if (!string.IsNullOrEmpty(st.NormalizedName)) localStationsByName[st.NormalizedName] = st;
            }

            var localLevelsByStationAndLevel = new Dictionary<string, LocalHideoutLevel>(StringComparer.OrdinalIgnoreCase);
            foreach (var hl in localHideoutLevels)
            {
                var key = $"{hl.StationId}:{hl.Level}";
                localLevelsByStationAndLevel[key] = hl;
            }

            var localHideoutItemsByStationAndLevel = localHideoutItemReqs
                .GroupBy(hi => $"{hi.StationId}:{hi.Level}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // Mapping from remote QuestId to local canonical QuestId
            var remoteToLocalQuestId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var matchedLocalQuestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Remote prerequisites index by QuestId
            var remoteReqsByQuestId = (remoteData.QuestRequirements ?? new())
                .GroupBy(r => r.QuestId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // Pre-pass: Match all remote quests to local quests first to fully populate remoteToLocalQuestId
            // regardless of quest declaration order in remote data.
            var remoteToLocalQuest = new Dictionary<string, LocalQuest>(StringComparer.OrdinalIgnoreCase);
            if (remoteData.Quests != null)
            {
                foreach (var rq in remoteData.Quests)
                {
                    LocalQuest? lq = null;
                    if (!string.IsNullOrEmpty(rq.Id) && localQuestsById.TryGetValue(rq.Id, out var foundById))
                    {
                        lq = foundById;
                    }
                    else if (!string.IsNullOrEmpty(rq.BsgId) && localQuestsByBsgId.TryGetValue(rq.BsgId, out var foundByBsg))
                    {
                        lq = foundByBsg;
                    }
                    else if (!string.IsNullOrEmpty(rq.Name) && localQuestsByName.TryGetValue(rq.Name, out var candidatesByName))
                    {
                        lq = candidatesByName.FirstOrDefault(c => !matchedLocalQuestIds.Contains(c.Id) &&
                             (string.IsNullOrEmpty(c.Faction) || string.IsNullOrEmpty(rq.Faction) || string.Equals(c.Faction, rq.Faction, StringComparison.OrdinalIgnoreCase)));
                    }
                    else if (!string.IsNullOrEmpty(rq.NormalizedName) && localQuestsByNormalizedName.TryGetValue(rq.NormalizedName, out var candidatesByNorm))
                    {
                        lq = candidatesByNorm.FirstOrDefault(c => !matchedLocalQuestIds.Contains(c.Id) &&
                             (string.IsNullOrEmpty(c.Faction) || string.IsNullOrEmpty(rq.Faction) || string.Equals(c.Faction, rq.Faction, StringComparison.OrdinalIgnoreCase)));
                    }

                    if (lq != null)
                    {
                        remoteToLocalQuest[rq.Id] = lq;
                        remoteToLocalQuestId[rq.Id] = lq.Id;
                        matchedLocalQuestIds.Add(lq.Id);
                    }
                    else
                    {
                        remoteToLocalQuestId[rq.Id] = rq.Id;
                    }
                }
            }

            // ==========================================
            // A. DIFF QUESTS
            // ==========================================
            if (remoteData.Quests != null)
            {
                foreach (var rq in remoteData.Quests)
                {
                    ct.ThrowIfCancellationRequested();

                    var lq = remoteToLocalQuest.GetValueOrDefault(rq.Id);

                    if (lq == null)
                    {
                        // Added Quest

                        var diff = new QuestDiff
                        {
                            ChangeType = DiffChangeType.Added,
                            QuestId = rq.Id,
                            BsgId = rq.BsgId,
                            QuestName = rq.Name,
                            NewTrader = rq.Trader,
                            NewMinLevel = rq.MinLevel,
                            NewFaction = rq.Faction,
                            NewMinScavKarma = rq.MinScavKarma,
                            NewKappaRequired = rq.KappaRequired,
                            NewRequiredEdition = rq.RequiredEdition,
                            NewExcludedEdition = rq.ExcludedEdition,
                            NewRequiredPrestigeLevel = rq.RequiredPrestigeLevel,
                            NewRequiredDecodeCount = rq.RequiredDecodeCount,
                            RemoteQuest = rq
                        };

                        if (rq.TraderRequirements != null && rq.TraderRequirements.Count > 0)
                            diff.TraderRequirements.AddRange(rq.TraderRequirements);
                        if (rq.PoolRequirements != null && rq.PoolRequirements.Count > 0)
                            diff.PoolRequirements.AddRange(rq.PoolRequirements);

                        diff.PropertyChanges.Add(new PropertyDiff("Quest", null, rq.Name));
                        if (rq.MinLevel.HasValue)
                            diff.PropertyChanges.Add(new PropertyDiff("MinLevel", null, rq.MinLevel.Value.ToString()));
                        if (!string.IsNullOrEmpty(rq.Trader))
                            diff.PropertyChanges.Add(new PropertyDiff("Trader", null, rq.Trader));
                        if (!string.IsNullOrEmpty(rq.Faction))
                            diff.PropertyChanges.Add(new PropertyDiff("Faction", null, rq.Faction));

                        report.QuestDiffs.Add(diff);
                    }
                    else
                    {
                        // Matched Quest -> compare properties & prerequisites

                        var diff = new QuestDiff
                        {
                            QuestId = lq.Id,
                            BsgId = rq.BsgId ?? lq.BsgId,
                            QuestName = lq.Name,
                            OldTrader = lq.Trader,
                            NewTrader = rq.Trader,
                            OldMinLevel = lq.MinLevel,
                            NewMinLevel = rq.MinLevel,
                            OldFaction = lq.Faction,
                            NewFaction = rq.Faction,
                            OldMinScavKarma = lq.MinScavKarma,
                            NewMinScavKarma = rq.MinScavKarma,
                            OldKappaRequired = lq.KappaRequired,
                            NewKappaRequired = rq.KappaRequired,
                            OldRequiredEdition = lq.RequiredEdition,
                            NewRequiredEdition = rq.RequiredEdition,
                            OldExcludedEdition = lq.ExcludedEdition,
                            NewExcludedEdition = rq.ExcludedEdition,
                            OldRequiredPrestigeLevel = lq.RequiredPrestigeLevel,
                            NewRequiredPrestigeLevel = rq.RequiredPrestigeLevel,
                            OldRequiredDecodeCount = lq.RequiredDecodeCount,
                            NewRequiredDecodeCount = rq.RequiredDecodeCount,
                            RemoteQuest = rq
                        };

                        if (rq.TraderRequirements != null && rq.TraderRequirements.Count > 0)
                            diff.TraderRequirements.AddRange(rq.TraderRequirements);
                        if (rq.PoolRequirements != null && rq.PoolRequirements.Count > 0)
                            diff.PoolRequirements.AddRange(rq.PoolRequirements);

                        bool isModified = false;

                        // MinLevel:
                        // 1. If remote has no level data (null, e.g. from TarkovTracker where minPlayerLevel is 0/unspecified):
                        //    - If local is 0 or null, both mean "no requirement" (0 == ninguno). No diff.
                        //    - If local has an established level (> 0), unpopulated remote data must not wipe local level.
                        // 2. If remote explicitly provides a level (rq.MinLevel.HasValue):
                        //    - Compare local level (treating null as 0) against remote value.
                        if (rq.MinLevel.HasValue)
                        {
                            int lMin = lq.MinLevel.GetValueOrDefault(0);
                            int rMin = rq.MinLevel.Value;
                            if (lMin != rMin)
                            {
                                diff.PropertyChanges.Add(new PropertyDiff("MinLevel", lq.MinLevel?.ToString(), rq.MinLevel.Value.ToString()));
                                isModified = true;
                            }
                        }

                        // Faction
                        if (!StringsEqual(lq.Faction, rq.Faction) && !string.IsNullOrEmpty(rq.Faction))
                        {
                            diff.PropertyChanges.Add(new PropertyDiff("Faction", lq.Faction, rq.Faction));
                            isModified = true;
                        }

                        // Trader
                        if (!StringsEqual(lq.Trader, rq.Trader) && !string.IsNullOrEmpty(rq.Trader))
                        {
                            diff.PropertyChanges.Add(new PropertyDiff("Trader", lq.Trader, rq.Trader));
                            isModified = true;
                        }

                        // MinScavKarma
                        if (lq.MinScavKarma != rq.MinScavKarma && rq.MinScavKarma.HasValue)
                        {
                            diff.PropertyChanges.Add(new PropertyDiff("MinScavKarma", lq.MinScavKarma?.ToString(), rq.MinScavKarma.Value.ToString()));
                            isModified = true;
                        }

                        // KappaRequired
                        if (lq.KappaRequired != rq.KappaRequired)
                        {
                            diff.PropertyChanges.Add(new PropertyDiff("KappaRequired", lq.KappaRequired.ToString(), rq.KappaRequired.ToString()));
                            isModified = true;
                        }

                        // Editions & Prestige
                        if (!StringsEqual(lq.RequiredEdition, rq.RequiredEdition) && rq.RequiredEdition != null)
                        {
                            diff.PropertyChanges.Add(new PropertyDiff("RequiredEdition", lq.RequiredEdition, rq.RequiredEdition));
                            isModified = true;
                        }
                        if (!StringsEqual(lq.ExcludedEdition, rq.ExcludedEdition) && rq.ExcludedEdition != null)
                        {
                            diff.PropertyChanges.Add(new PropertyDiff("ExcludedEdition", lq.ExcludedEdition, rq.ExcludedEdition));
                            isModified = true;
                        }
                        if (lq.RequiredPrestigeLevel != rq.RequiredPrestigeLevel && rq.RequiredPrestigeLevel.HasValue)
                        {
                            diff.PropertyChanges.Add(new PropertyDiff("RequiredPrestigeLevel", lq.RequiredPrestigeLevel?.ToString(), rq.RequiredPrestigeLevel.Value.ToString()));
                            isModified = true;
                        }
                        if (lq.RequiredDecodeCount != rq.RequiredDecodeCount && rq.RequiredDecodeCount.HasValue)
                        {
                            diff.PropertyChanges.Add(new PropertyDiff("RequiredDecodeCount", lq.RequiredDecodeCount?.ToString(), rq.RequiredDecodeCount.Value.ToString()));
                            isModified = true;
                        }

                        // Compare prerequisites for this quest
                        var localPrereqs = localReqsByQuestId.GetValueOrDefault(lq.Id) ?? new List<LocalQuestRequirement>();
                        var localPrereqIds = new HashSet<string>(localPrereqs.Select(r => r.RequiredQuestId), StringComparer.OrdinalIgnoreCase);

                        var remotePrereqs = remoteReqsByQuestId.GetValueOrDefault(rq.Id) ?? new List<NormalizedQuestRequirement>();
                        var remotePrereqIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        var allRemoteQuestIds = new HashSet<string>(remoteData.Quests.Select(q => q.Id), StringComparer.OrdinalIgnoreCase);
                        foreach (var rp in remotePrereqs)
                        {
                            // Map remote required quest ID to local ID if matched
                            var resolvedReqId = rp.RequiredQuestId;
                            if (localQuestsById.TryGetValue(resolvedReqId, out var foundReqMatch))
                            {
                                remotePrereqIds.Add(foundReqMatch.Id);
                            }
                            else if (remoteToLocalQuestId.TryGetValue(resolvedReqId, out var mappedId) && localQuestsById.TryGetValue(mappedId, out var foundMapped))
                            {
                                remotePrereqIds.Add(foundMapped.Id);
                            }
                            else if (allRemoteQuestIds.Contains(resolvedReqId))
                            {
                                remotePrereqIds.Add(resolvedReqId);
                            }
                            else if (!string.IsNullOrEmpty(rp.RequiredQuestName) && localQuestsByName.TryGetValue(rp.RequiredQuestName, out var foundReqByName) && foundReqByName.Count > 0)
                            {
                                remotePrereqIds.Add(foundReqByName.First().Id);
                            }
                            else if (pruneBrokenPrerequisites)
                            {
                                _log.Warning($"DiffQuests: Skipping phantom prerequisite '{rp.RequiredQuestName}' ({resolvedReqId}) for quest '{lq.Name}' - quest does not exist.");
                            }
                            else
                            {
                                remotePrereqIds.Add(resolvedReqId);
                            }
                        }

                        var addedPrereqs = remotePrereqIds.Where(id => !localPrereqIds.Contains(id)).ToList();
                        var removedPrereqs = localPrereqIds.Where(id => !remotePrereqIds.Contains(id)).ToList();

                        if (remotePrereqs.Count > 0 || localPrereqs.Count > 0)
                        {
                            if (addedPrereqs.Count > 0)
                            {
                                diff.AddedPrerequisiteQuestIds.AddRange(addedPrereqs);
                                isModified = true;
                            }
                            if (removedPrereqs.Count > 0 && remotePrereqs.Count > 0)
                            {
                                diff.RemovedPrerequisiteQuestIds.AddRange(removedPrereqs);
                                isModified = true;
                            }
                        }

                        if (isModified)
                        {
                            diff.ChangeType = DiffChangeType.Modified;
                            report.QuestDiffs.Add(diff);
                        }
                        else if (includeUnchanged)
                        {
                            diff.ChangeType = DiffChangeType.Unchanged;
                            report.QuestDiffs.Add(diff);
                        }
                    }
                }

                // Removed Quests (if requested)
                if (detectRemovedQuests)
                {
                    foreach (var lq in localQuests)
                    {
                        if (!matchedLocalQuestIds.Contains(lq.Id))
                        {
                            var diff = new QuestDiff
                            {
                                ChangeType = DiffChangeType.Removed,
                                QuestId = lq.Id,
                                BsgId = lq.BsgId,
                                QuestName = lq.Name,
                                OldTrader = lq.Trader,
                                OldMinLevel = lq.MinLevel,
                                OldFaction = lq.Faction,
                                OldMinScavKarma = lq.MinScavKarma,
                                OldKappaRequired = lq.KappaRequired
                            };
                            diff.PropertyChanges.Add(new PropertyDiff("Quest", lq.Name, null));
                            report.QuestDiffs.Add(diff);
                        }
                    }
                }
            }

            // ==========================================
            // B. DIFF QUEST REQUIREMENTS (Detailed)
            // ==========================================
            if (remoteData.QuestRequirements != null && remoteData.QuestRequirements.Count > 0)
            {
                var processedLocalReqIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var allValidQuestIds = new HashSet<string>(localQuestsById.Keys, StringComparer.OrdinalIgnoreCase);

                foreach (var qd in report.QuestDiffs)
                {
                    if (qd.ChangeType == DiffChangeType.Added && !string.IsNullOrWhiteSpace(qd.QuestId))
                        allValidQuestIds.Add(qd.QuestId);
                }

                foreach (var rReq in remoteData.QuestRequirements)
                {
                    ct.ThrowIfCancellationRequested();

                    var targetQuestId = remoteToLocalQuestId.GetValueOrDefault(rReq.QuestId, rReq.QuestId);
                    if (localQuestsById.TryGetValue(targetQuestId, out var foundLocalTarget))
                        targetQuestId = foundLocalTarget.Id;

                    var targetRequiredQuestId = rReq.RequiredQuestId;
                    if (localQuestsById.TryGetValue(targetRequiredQuestId, out var foundReqById))
                    {
                        targetRequiredQuestId = foundReqById.Id;
                    }
                    else if (remoteToLocalQuestId.TryGetValue(targetRequiredQuestId, out var mappedReqId) && localQuestsById.TryGetValue(mappedReqId, out var foundMappedReq))
                    {
                        targetRequiredQuestId = foundMappedReq.Id;
                    }
                    else if (allValidQuestIds.Contains(targetRequiredQuestId))
                    {
                        // Target required quest ID is already a valid canonical ID
                    }
                    else if (!string.IsNullOrEmpty(rReq.RequiredQuestName) && localQuestsByName.TryGetValue(rReq.RequiredQuestName, out var foundReqByName) && foundReqByName.Count > 0)
                    {
                        targetRequiredQuestId = foundReqByName.First().Id;
                    }

                    if (localQuestsById.TryGetValue(targetRequiredQuestId, out var foundReqMapped))
                        targetRequiredQuestId = foundReqMapped.Id;

                    var localReqsForQuest = localReqsByQuestId.GetValueOrDefault(targetQuestId);
                    var matchedLocalReq = localReqsForQuest?.FirstOrDefault(lr =>
                        string.Equals(lr.RequiredQuestId, targetRequiredQuestId, StringComparison.OrdinalIgnoreCase));

                    if (pruneBrokenPrerequisites && (!allValidQuestIds.Contains(targetQuestId) || !allValidQuestIds.Contains(targetRequiredQuestId)))
                    {
                        _log.Warning($"DiffQuestRequirements: Skipping requirement pointing to non-existent quest (QuestId='{targetQuestId}', RequiredQuestId='{targetRequiredQuestId}')");
                        continue;
                    }

                    if (matchedLocalReq == null)
                    {
                        var reqDiff = new QuestRequirementDiff
                        {
                            ChangeType = DiffChangeType.Added,
                            RequirementId = rReq.Id,
                            QuestId = targetQuestId,
                            QuestName = rReq.QuestName,
                            RequiredQuestId = targetRequiredQuestId,
                            RequiredQuestName = rReq.RequiredQuestName,
                            NewRequirementType = rReq.RequirementType,
                            NewDelayMinutes = rReq.DelayMinutes,
                            NewGroupId = rReq.GroupId,
                            RemoteRequirement = rReq
                        };
                        reqDiff.PropertyChanges.Add(new PropertyDiff("RequirementType", null, rReq.RequirementType));
                        report.QuestRequirementDiffs.Add(reqDiff);
                    }
                    else
                    {
                        processedLocalReqIds.Add(matchedLocalReq.Id);

                        bool reqModified = false;
                        var normLocalReqType = DataNormalizerService.NormalizeRequirementType(matchedLocalReq.RequirementType);
                        var normRemoteReqType = DataNormalizerService.NormalizeRequirementType(rReq.RequirementType);

                        var reqDiff = new QuestRequirementDiff
                        {
                            RequirementId = matchedLocalReq.Id,
                            QuestId = targetQuestId,
                            QuestName = rReq.QuestName,
                            RequiredQuestId = targetRequiredQuestId,
                            RequiredQuestName = rReq.RequiredQuestName,
                            OldRequirementType = normLocalReqType,
                            NewRequirementType = normRemoteReqType,
                            OldDelayMinutes = matchedLocalReq.DelayMinutes,
                            NewDelayMinutes = rReq.DelayMinutes,
                            OldGroupId = matchedLocalReq.GroupId,
                            NewGroupId = rReq.GroupId,
                            RemoteRequirement = rReq
                        };

                        if (!StringsEqual(normLocalReqType, normRemoteReqType))
                        {
                            reqDiff.PropertyChanges.Add(new PropertyDiff("RequirementType", normLocalReqType, normRemoteReqType));
                            reqModified = true;
                        }
                        if (matchedLocalReq.DelayMinutes != rReq.DelayMinutes && rReq.DelayMinutes.HasValue)
                        {
                            reqDiff.PropertyChanges.Add(new PropertyDiff("DelayMinutes", matchedLocalReq.DelayMinutes?.ToString(), rReq.DelayMinutes.Value.ToString()));
                            reqModified = true;
                        }
                        if (matchedLocalReq.GroupId != rReq.GroupId)
                        {
                            reqDiff.PropertyChanges.Add(new PropertyDiff("GroupId", matchedLocalReq.GroupId.ToString(), rReq.GroupId.ToString()));
                            reqModified = true;
                        }

                        if (reqModified)
                        {
                            reqDiff.ChangeType = DiffChangeType.Modified;
                            report.QuestRequirementDiffs.Add(reqDiff);
                        }
                        else if (includeUnchanged)
                        {
                            reqDiff.ChangeType = DiffChangeType.Unchanged;
                            report.QuestRequirementDiffs.Add(reqDiff);
                        }
                    }
                }

                // Check removed requirements for audited quests
                foreach (var (questId, remoteReqList) in remoteReqsByQuestId)
                {
                    var resolvedQuestId = remoteToLocalQuestId.GetValueOrDefault(questId, questId);
                    if (localReqsByQuestId.TryGetValue(resolvedQuestId, out var localReqs))
                    {
                        foreach (var lr in localReqs)
                        {
                            if (!processedLocalReqIds.Contains(lr.Id))
                            {
                                var reqDiff = new QuestRequirementDiff
                                {
                                    ChangeType = DiffChangeType.Removed,
                                    RequirementId = lr.Id,
                                    QuestId = lr.QuestId,
                                    RequiredQuestId = lr.RequiredQuestId,
                                    OldRequirementType = lr.RequirementType,
                                    OldDelayMinutes = lr.DelayMinutes,
                                    OldGroupId = lr.GroupId
                                };
                                reqDiff.PropertyChanges.Add(new PropertyDiff("Requirement", $"{lr.RequiredQuestId} ({lr.RequirementType})", null));
                                report.QuestRequirementDiffs.Add(reqDiff);
                            }
                        }
                    }
                }
            }

            // ==========================================
            // C. DIFF QUEST REQUIRED ITEMS
            // ==========================================
            if (remoteData.QuestRequiredItems != null && remoteData.QuestRequiredItems.Count > 0)
            {
                var processedLocalItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var allValidQuestIdsForItems = new HashSet<string>(localQuestsById.Keys, StringComparer.OrdinalIgnoreCase);
                foreach (var qd in report.QuestDiffs)
                {
                    if (qd.ChangeType == DiffChangeType.Added && !string.IsNullOrWhiteSpace(qd.QuestId))
                        allValidQuestIdsForItems.Add(qd.QuestId);
                }
                var remoteItemsByQuestId = remoteData.QuestRequiredItems
                    .GroupBy(i => i.QuestId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                foreach (var rItem in remoteData.QuestRequiredItems)
                {
                    ct.ThrowIfCancellationRequested();

                    var targetQuestId = remoteToLocalQuestId.GetValueOrDefault(rItem.QuestId, CanonicalQuestMap.ResolveBsgId(rItem.QuestId));
                    if (string.IsNullOrEmpty(targetQuestId)) targetQuestId = rItem.QuestId;
                    if (!allValidQuestIdsForItems.Contains(targetQuestId))
                    {
                        _log.Warning($"DiffQuestItems: Skipping required item for non-existent quest '{targetQuestId}'");
                        continue;
                    }

                    // Guard against corrupt manifests in Reserve Expert (historical TarkovTracker scraping bug)
                    if (string.Equals(targetQuestId, "608974af4b05530f55550c21", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(rItem.ItemName) &&
                        rItem.ItemName.Contains("manifest", StringComparison.OrdinalIgnoreCase))
                    {
                        _log.Warning($"DiffQuestItems: Skipping corrupt cargo manifest '{rItem.ItemName}' for Reserve Expert ({targetQuestId})");
                        continue;
                    }

                    var localItemsForQuest = localItemsByQuestId.GetValueOrDefault(targetQuestId);

                    LocalQuestRequiredItem? matchedLocalItem = null;
                    if (localItemsForQuest != null)
                    {
                        if (!string.IsNullOrEmpty(rItem.ItemId))
                        {
                            matchedLocalItem = localItemsForQuest.FirstOrDefault(li =>
                                string.Equals(li.ItemId, rItem.ItemId, StringComparison.OrdinalIgnoreCase));
                        }

                        if (matchedLocalItem == null && !string.IsNullOrEmpty(rItem.ItemName))
                        {
                            matchedLocalItem = localItemsForQuest.FirstOrDefault(li =>
                                StringsEqual(li.ItemName, rItem.ItemName));
                        }
                    }

                    if (matchedLocalItem == null)
                    {
                        var itemDiff = new QuestItemDiff
                        {
                            ChangeType = DiffChangeType.Added,
                            RequirementId = rItem.Id,
                            QuestId = targetQuestId,
                            QuestName = rItem.QuestName,
                            ItemId = rItem.ItemId,
                            ItemName = rItem.ItemName,
                            NewQuantity = rItem.Count,
                            NewRequiresFIR = rItem.RequiresFIR,
                            NewRequirementType = rItem.RequirementType,
                            RemoteItem = rItem
                        };
                        itemDiff.PropertyChanges.Add(new PropertyDiff("Count", null, rItem.Count.ToString()));
                        itemDiff.PropertyChanges.Add(new PropertyDiff("RequiresFIR", null, rItem.RequiresFIR.ToString()));
                        report.QuestItemDiffs.Add(itemDiff);
                    }
                    else
                    {
                        processedLocalItemIds.Add(matchedLocalItem.Id);

                        bool itemModified = false;
                        var itemDiff = new QuestItemDiff
                        {
                            RequirementId = matchedLocalItem.Id,
                            QuestId = targetQuestId,
                            QuestName = rItem.QuestName,
                            ItemId = rItem.ItemId ?? matchedLocalItem.ItemId,
                            ItemName = matchedLocalItem.ItemName,
                            OldQuantity = matchedLocalItem.Count,
                            NewQuantity = rItem.Count,
                            OldRequiresFIR = matchedLocalItem.RequiresFIR,
                            NewRequiresFIR = rItem.RequiresFIR,
                            OldRequirementType = matchedLocalItem.RequirementType,
                            NewRequirementType = rItem.RequirementType,
                            RemoteItem = rItem
                        };

                        if (matchedLocalItem.Count != rItem.Count)
                        {
                            itemDiff.PropertyChanges.Add(new PropertyDiff("Count", matchedLocalItem.Count.ToString(), rItem.Count.ToString()));
                            itemModified = true;
                        }

                        if (matchedLocalItem.RequiresFIR != rItem.RequiresFIR)
                        {
                            itemDiff.PropertyChanges.Add(new PropertyDiff("RequiresFIR", matchedLocalItem.RequiresFIR.ToString(), rItem.RequiresFIR.ToString()));
                            itemModified = true;
                        }

                        if (!StringsEqual(matchedLocalItem.RequirementType, rItem.RequirementType))
                        {
                            itemDiff.PropertyChanges.Add(new PropertyDiff("RequirementType", matchedLocalItem.RequirementType, rItem.RequirementType));
                            itemModified = true;
                        }

                        if (itemModified)
                        {
                            itemDiff.ChangeType = DiffChangeType.Modified;
                            report.QuestItemDiffs.Add(itemDiff);
                        }
                        else if (includeUnchanged)
                        {
                            itemDiff.ChangeType = DiffChangeType.Unchanged;
                            report.QuestItemDiffs.Add(itemDiff);
                        }
                    }
                }

                // Check removed quest required items for audited quests
                foreach (var (questId, _) in remoteItemsByQuestId)
                {
                    var resolvedQuestId = remoteToLocalQuestId.GetValueOrDefault(questId, questId);
                    if (localItemsByQuestId.TryGetValue(resolvedQuestId, out var localItems))
                    {
                        foreach (var li in localItems)
                        {
                            if (!processedLocalItemIds.Contains(li.Id))
                            {
                                var itemDiff = new QuestItemDiff
                                {
                                    ChangeType = DiffChangeType.Removed,
                                    RequirementId = li.Id,
                                    QuestId = li.QuestId,
                                    ItemId = li.ItemId,
                                    ItemName = li.ItemName,
                                    OldQuantity = li.Count,
                                    OldRequiresFIR = li.RequiresFIR,
                                    OldRequirementType = li.RequirementType
                                };
                                itemDiff.PropertyChanges.Add(new PropertyDiff("Item", $"{li.ItemName} x{li.Count}", null));
                                report.QuestItemDiffs.Add(itemDiff);
                            }
                        }
                    }
                }
            }

            // ==========================================
            // D. DIFF HIDEOUT STATIONS
            // ==========================================
            if (remoteData.HideoutStations != null)
            {
                foreach (var rStation in remoteData.HideoutStations)
                {
                    ct.ThrowIfCancellationRequested();

                    LocalHideoutStation? lStation = null;
                    if (!string.IsNullOrEmpty(rStation.Id) && localStationsById.TryGetValue(rStation.Id, out var foundById))
                        lStation = foundById;
                    else if (!string.IsNullOrEmpty(rStation.Name) && localStationsByName.TryGetValue(rStation.Name, out var foundByName))
                        lStation = foundByName;
                    else if (!string.IsNullOrEmpty(rStation.NormalizedName) && localStationsByName.TryGetValue(rStation.NormalizedName, out var foundByNorm))
                        lStation = foundByNorm;

                    if (lStation == null)
                    {
                        var stationDiff = new HideoutStationDiff
                        {
                            ChangeType = DiffChangeType.Added,
                            StationId = rStation.Id,
                            StationName = rStation.Name,
                            NewMaxLevel = rStation.MaxLevel,
                            RemoteStation = rStation
                        };
                        stationDiff.PropertyChanges.Add(new PropertyDiff("MaxLevel", null, rStation.MaxLevel.ToString()));
                        report.HideoutStationDiffs.Add(stationDiff);
                    }
                    else
                    {
                        bool modified = false;
                        var stationDiff = new HideoutStationDiff
                        {
                            StationId = lStation.Id,
                            StationName = lStation.Name,
                            OldMaxLevel = lStation.MaxLevel,
                            NewMaxLevel = rStation.MaxLevel,
                            RemoteStation = rStation
                        };

                        if (lStation.MaxLevel != rStation.MaxLevel && rStation.MaxLevel > 0)
                        {
                            stationDiff.PropertyChanges.Add(new PropertyDiff("MaxLevel", lStation.MaxLevel.ToString(), rStation.MaxLevel.ToString()));
                            modified = true;
                        }

                        if (modified)
                        {
                            stationDiff.ChangeType = DiffChangeType.Modified;
                            report.HideoutStationDiffs.Add(stationDiff);
                        }
                        else if (includeUnchanged)
                        {
                            stationDiff.ChangeType = DiffChangeType.Unchanged;
                            report.HideoutStationDiffs.Add(stationDiff);
                        }
                    }
                }
            }

            // ==========================================
            // E. DIFF HIDEOUT LEVELS
            // ==========================================
            if (remoteData.HideoutLevels != null)
            {
                foreach (var rLevel in remoteData.HideoutLevels)
                {
                    ct.ThrowIfCancellationRequested();

                    var stationId = rLevel.StationId;
                    if (localStationsByName.TryGetValue(rLevel.StationName, out var foundStation))
                        stationId = foundStation.Id;

                    var levelKey = $"{stationId}:{rLevel.Level}";
                    if (!localLevelsByStationAndLevel.TryGetValue(levelKey, out var lLevel))
                    {
                        var levelDiff = new HideoutLevelDiff
                        {
                            ChangeType = DiffChangeType.Added,
                            LevelId = rLevel.Id,
                            StationId = stationId,
                            StationName = rLevel.StationName,
                            Level = rLevel.Level,
                            NewConstructionTime = rLevel.ConstructionTime,
                            RemoteLevel = rLevel
                        };
                        levelDiff.PropertyChanges.Add(new PropertyDiff("ConstructionTime", null, rLevel.ConstructionTime.ToString()));
                        report.HideoutLevelDiffs.Add(levelDiff);
                    }
                    else
                    {
                        bool modified = false;
                        var levelDiff = new HideoutLevelDiff
                        {
                            LevelId = lLevel.Id,
                            StationId = stationId,
                            StationName = rLevel.StationName,
                            Level = lLevel.Level,
                            OldConstructionTime = lLevel.ConstructionTime,
                            NewConstructionTime = rLevel.ConstructionTime,
                            RemoteLevel = rLevel
                        };

                        if (lLevel.ConstructionTime != rLevel.ConstructionTime)
                        {
                            levelDiff.PropertyChanges.Add(new PropertyDiff("ConstructionTime", lLevel.ConstructionTime.ToString(), rLevel.ConstructionTime.ToString()));
                            modified = true;
                        }

                        if (modified)
                        {
                            levelDiff.ChangeType = DiffChangeType.Modified;
                            report.HideoutLevelDiffs.Add(levelDiff);
                        }
                        else if (includeUnchanged)
                        {
                            levelDiff.ChangeType = DiffChangeType.Unchanged;
                            report.HideoutLevelDiffs.Add(levelDiff);
                        }
                    }
                }
            }

            // ==========================================
            // F. DIFF HIDEOUT ITEM REQUIREMENTS
            // ==========================================
            if (remoteData.HideoutItemRequirements != null)
            {
                var processedHideoutItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var remoteHideoutItemsByStationAndLevel = remoteData.HideoutItemRequirements
                    .GroupBy(hi => $"{hi.StationId}:{hi.Level}", StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                foreach (var rItem in remoteData.HideoutItemRequirements)
                {
                    ct.ThrowIfCancellationRequested();

                    var stationId = rItem.StationId;
                    if (localStationsByName.TryGetValue(rItem.StationName, out var foundStation))
                        stationId = foundStation.Id;

                    var levelKey = $"{stationId}:{rItem.Level}";
                    var localItems = localHideoutItemsByStationAndLevel.GetValueOrDefault(levelKey);

                    LocalHideoutItemRequirement? matchedLocalItem = null;
                    if (localItems != null)
                    {
                        if (!string.IsNullOrEmpty(rItem.ItemId))
                        {
                            matchedLocalItem = localItems.FirstOrDefault(li =>
                                string.Equals(li.ItemId, rItem.ItemId, StringComparison.OrdinalIgnoreCase));
                        }

                        if (matchedLocalItem == null && !string.IsNullOrEmpty(rItem.ItemName))
                        {
                            matchedLocalItem = localItems.FirstOrDefault(li =>
                                StringsEqual(li.ItemName, rItem.ItemName));
                        }
                    }

                    if (matchedLocalItem == null)
                    {
                        var itemDiff = new HideoutItemDiff
                        {
                            ChangeType = DiffChangeType.Added,
                            RequirementId = rItem.Id,
                            StationId = stationId,
                            StationName = rItem.StationName,
                            Level = rItem.Level,
                            ItemId = rItem.ItemId,
                            ItemName = rItem.ItemName,
                            NewQuantity = rItem.Count,
                            NewFoundInRaid = rItem.FoundInRaid,
                            RemoteItem = rItem
                        };
                        itemDiff.PropertyChanges.Add(new PropertyDiff("Count", null, rItem.Count.ToString()));
                        itemDiff.PropertyChanges.Add(new PropertyDiff("FoundInRaid", null, rItem.FoundInRaid.ToString()));
                        report.HideoutItemDiffs.Add(itemDiff);
                    }
                    else
                    {
                        processedHideoutItemIds.Add(matchedLocalItem.Id);

                        bool modified = false;
                        var itemDiff = new HideoutItemDiff
                        {
                            RequirementId = matchedLocalItem.Id,
                            StationId = stationId,
                            StationName = rItem.StationName,
                            Level = rItem.Level,
                            ItemId = matchedLocalItem.ItemId,
                            ItemName = matchedLocalItem.ItemName,
                            OldQuantity = matchedLocalItem.Count,
                            NewQuantity = rItem.Count,
                            OldFoundInRaid = matchedLocalItem.FoundInRaid,
                            NewFoundInRaid = rItem.FoundInRaid,
                            RemoteItem = rItem
                        };

                        if (matchedLocalItem.Count != rItem.Count)
                        {
                            itemDiff.PropertyChanges.Add(new PropertyDiff("Count", matchedLocalItem.Count.ToString(), rItem.Count.ToString()));
                            modified = true;
                        }

                        if (matchedLocalItem.FoundInRaid != rItem.FoundInRaid)
                        {
                            itemDiff.PropertyChanges.Add(new PropertyDiff("FoundInRaid", matchedLocalItem.FoundInRaid.ToString(), rItem.FoundInRaid.ToString()));
                            modified = true;
                        }

                        if (modified)
                        {
                            itemDiff.ChangeType = DiffChangeType.Modified;
                            report.HideoutItemDiffs.Add(itemDiff);
                        }
                        else if (includeUnchanged)
                        {
                            itemDiff.ChangeType = DiffChangeType.Unchanged;
                            report.HideoutItemDiffs.Add(itemDiff);
                        }
                    }
                }

                // Check removed hideout items for audited station/levels
                foreach (var (key, _) in remoteHideoutItemsByStationAndLevel)
                {
                    if (localHideoutItemsByStationAndLevel.TryGetValue(key, out var localItems))
                    {
                        foreach (var li in localItems)
                        {
                            if (!processedHideoutItemIds.Contains(li.Id))
                            {
                                var itemDiff = new HideoutItemDiff
                                {
                                    ChangeType = DiffChangeType.Removed,
                                    RequirementId = li.Id,
                                    StationId = li.StationId,
                                    StationName = li.StationId,
                                    Level = li.Level,
                                    ItemId = li.ItemId,
                                    ItemName = li.ItemName,
                                    OldQuantity = li.Count,
                                    OldFoundInRaid = li.FoundInRaid
                                };
                                itemDiff.PropertyChanges.Add(new PropertyDiff("Item", $"{li.ItemName} x{li.Count}", null));
                                report.HideoutItemDiffs.Add(itemDiff);
                            }
                        }
                    }
                }
            }

            // ==========================================
            // G. DIFF QUEST TRADER REQUIREMENTS
            // ==========================================
            if (remoteData.QuestTraderRequirements != null && remoteData.QuestTraderRequirements.Count > 0)
            {
                var processedLocalTraderReqIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenTargetTraderReqs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var remoteTraderReqsByQuestId = remoteData.QuestTraderRequirements
                    .GroupBy(r => r.QuestId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                foreach (var rReq in remoteData.QuestTraderRequirements)
                {
                    ct.ThrowIfCancellationRequested();

                    var targetQuestId = remoteToLocalQuestId.GetValueOrDefault(rReq.QuestId, CanonicalQuestMap.ResolveBsgId(rReq.QuestId));
                    if (string.IsNullOrEmpty(targetQuestId)) targetQuestId = rReq.QuestId;
                    if (localQuestsById.TryGetValue(targetQuestId, out var foundLocalTarget))
                        targetQuestId = foundLocalTarget.Id;

                    var trDedupeKey = $"{targetQuestId}_{rReq.TraderName}_{rReq.RequirementType}_{rReq.RequiredLevel}_{rReq.CompareMethod}";
                    if (!seenTargetTraderReqs.Add(trDedupeKey))
                        continue;
                    var localTraderReqsForQuest = localTraderReqsByQuestId.GetValueOrDefault(targetQuestId);

                    var matchedLocalTraderReq = localTraderReqsForQuest?.FirstOrDefault(ltr =>
                        (StringsEqual(ltr.TraderName, rReq.TraderName) || (!string.IsNullOrEmpty(ltr.TraderId) && string.Equals(ltr.TraderId, rReq.TraderId, StringComparison.OrdinalIgnoreCase))) &&
                        StringsEqual(ltr.RequirementType, rReq.RequirementType));

                    if (matchedLocalTraderReq == null)
                    {
                        var trDiff = new QuestTraderRequirementDiff
                        {
                            ChangeType = DiffChangeType.Added,
                            RequirementId = rReq.Id,
                            QuestId = targetQuestId,
                            QuestName = rReq.QuestName,
                            TraderName = rReq.TraderName,
                            TraderId = rReq.TraderId,
                            NewRequiredLevel = rReq.RequiredLevel,
                            NewCompareMethod = rReq.CompareMethod,
                            RemoteRequirement = rReq
                        };
                        trDiff.PropertyChanges.Add(new PropertyDiff("RequiredLevel", null, rReq.RequiredLevel.ToString()));
                        trDiff.PropertyChanges.Add(new PropertyDiff("CompareMethod", null, rReq.CompareMethod));
                        report.QuestTraderRequirementDiffs.Add(trDiff);
                    }
                    else
                    {
                        processedLocalTraderReqIds.Add(matchedLocalTraderReq.Id);
                        if (localTraderReqsForQuest != null)
                        {
                            foreach (var extraDupe in localTraderReqsForQuest)
                            {
                                if ((StringsEqual(extraDupe.TraderName, rReq.TraderName) || (!string.IsNullOrEmpty(extraDupe.TraderId) && string.Equals(extraDupe.TraderId, rReq.TraderId, StringComparison.OrdinalIgnoreCase))) &&
                                    StringsEqual(extraDupe.RequirementType, rReq.RequirementType) &&
                                    extraDupe.RequiredLevel == rReq.RequiredLevel)
                                {
                                    processedLocalTraderReqIds.Add(extraDupe.Id);
                                }
                            }
                        }

                        bool modified = false;
                        var trDiff = new QuestTraderRequirementDiff
                        {
                            RequirementId = matchedLocalTraderReq.Id,
                            QuestId = targetQuestId,
                            QuestName = rReq.QuestName,
                            TraderName = rReq.TraderName,
                            TraderId = rReq.TraderId ?? matchedLocalTraderReq.TraderId,
                            OldRequiredLevel = matchedLocalTraderReq.RequiredLevel,
                            NewRequiredLevel = rReq.RequiredLevel,
                            OldCompareMethod = matchedLocalTraderReq.CompareMethod,
                            NewCompareMethod = rReq.CompareMethod,
                            RemoteRequirement = rReq
                        };

                        if (matchedLocalTraderReq.RequiredLevel != rReq.RequiredLevel)
                        {
                            trDiff.PropertyChanges.Add(new PropertyDiff("RequiredLevel", matchedLocalTraderReq.RequiredLevel.ToString(), rReq.RequiredLevel.ToString()));
                            modified = true;
                        }
                        if (!StringsEqual(matchedLocalTraderReq.CompareMethod, rReq.CompareMethod))
                        {
                            trDiff.PropertyChanges.Add(new PropertyDiff("CompareMethod", matchedLocalTraderReq.CompareMethod, rReq.CompareMethod));
                            modified = true;
                        }

                        if (modified)
                        {
                            trDiff.ChangeType = DiffChangeType.Modified;
                            report.QuestTraderRequirementDiffs.Add(trDiff);
                        }
                        else if (includeUnchanged)
                        {
                            trDiff.ChangeType = DiffChangeType.Unchanged;
                            report.QuestTraderRequirementDiffs.Add(trDiff);
                        }
                    }
                }

                // Check removed trader requirements for audited quests
                foreach (var (questId, _) in remoteTraderReqsByQuestId)
                {
                    var resolvedQuestId = remoteToLocalQuestId.GetValueOrDefault(questId, CanonicalQuestMap.ResolveBsgId(questId));
                    if (string.IsNullOrEmpty(resolvedQuestId)) resolvedQuestId = questId;
                    if (localQuestsById.TryGetValue(resolvedQuestId, out var foundLocalTarget))
                        resolvedQuestId = foundLocalTarget.Id;

                    if (localTraderReqsByQuestId.TryGetValue(resolvedQuestId, out var localReqs))
                    {
                        var localQuestObj = localQuestsById.GetValueOrDefault(resolvedQuestId);
                        var questName = localQuestObj?.Name ?? resolvedQuestId;

                        foreach (var ltr in localReqs)
                        {
                            if (!processedLocalTraderReqIds.Contains(ltr.Id))
                            {
                                var trDiff = new QuestTraderRequirementDiff
                                {
                                    ChangeType = DiffChangeType.Removed,
                                    RequirementId = ltr.Id,
                                    QuestId = ltr.QuestId,
                                    QuestName = questName,
                                    TraderName = ltr.TraderName,
                                    TraderId = ltr.TraderId,
                                    OldRequiredLevel = ltr.RequiredLevel,
                                    OldCompareMethod = ltr.CompareMethod
                                };
                                trDiff.PropertyChanges.Add(new PropertyDiff("TraderRequirement", $"{ltr.TraderName} {ltr.CompareMethod} {ltr.RequiredLevel}", null));
                                report.QuestTraderRequirementDiffs.Add(trDiff);
                            }
                        }
                    }
                }
            }

            // ==========================================
            // H. DIFF QUEST POOL REQUIREMENTS
            // ==========================================
            if (remoteData.QuestPoolRequirements != null && remoteData.QuestPoolRequirements.Count > 0)
            {
                var processedLocalPoolReqIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenTargetPoolReqs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var remotePoolReqsByQuestId = remoteData.QuestPoolRequirements
                    .GroupBy(r => r.QuestId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                foreach (var rPool in remoteData.QuestPoolRequirements)
                {
                    ct.ThrowIfCancellationRequested();

                    var targetQuestId = remoteToLocalQuestId.GetValueOrDefault(rPool.QuestId, CanonicalQuestMap.ResolveBsgId(rPool.QuestId));
                    if (string.IsNullOrEmpty(targetQuestId)) targetQuestId = rPool.QuestId;
                    if (localQuestsById.TryGetValue(targetQuestId, out var foundLocalTarget))
                        targetQuestId = foundLocalTarget.Id;

                    var poolDedupeKey = $"{targetQuestId}_{rPool.PoolTrader}_{rPool.PoolTraderLL}_{rPool.RequiredCompletedCount}";
                    if (!seenTargetPoolReqs.Add(poolDedupeKey))
                        continue;
                    var localPoolReqsForQuest = localPoolReqsByQuestId.GetValueOrDefault(targetQuestId);

                    var matchedLocalPoolReq = localPoolReqsForQuest?.FirstOrDefault(lpr =>
                        StringsEqual(lpr.PoolTrader, rPool.PoolTrader) && lpr.PoolTraderLL == rPool.PoolTraderLL);

                    if (matchedLocalPoolReq == null)
                    {
                        var poolDiff = new QuestPoolRequirementDiff
                        {
                            ChangeType = DiffChangeType.Added,
                            RequirementId = rPool.Id,
                            QuestId = targetQuestId,
                            QuestName = rPool.QuestName,
                            PoolTrader = rPool.PoolTrader,
                            PoolTraderLL = rPool.PoolTraderLL,
                            NewRequiredCompletedCount = rPool.RequiredCompletedCount,
                            NewDescription = rPool.Description,
                            RemoteRequirement = rPool
                        };
                        poolDiff.PropertyChanges.Add(new PropertyDiff("RequiredCompletedCount", null, rPool.RequiredCompletedCount.ToString()));
                        report.QuestPoolRequirementDiffs.Add(poolDiff);
                    }
                    else
                    {
                        processedLocalPoolReqIds.Add(matchedLocalPoolReq.Id);

                        bool modified = false;
                        var poolDiff = new QuestPoolRequirementDiff
                        {
                            RequirementId = matchedLocalPoolReq.Id,
                            QuestId = targetQuestId,
                            QuestName = rPool.QuestName,
                            PoolTrader = rPool.PoolTrader,
                            PoolTraderLL = rPool.PoolTraderLL,
                            OldRequiredCompletedCount = matchedLocalPoolReq.RequiredCompletedCount,
                            NewRequiredCompletedCount = rPool.RequiredCompletedCount,
                            OldDescription = matchedLocalPoolReq.Description,
                            NewDescription = rPool.Description,
                            RemoteRequirement = rPool
                        };

                        if (matchedLocalPoolReq.RequiredCompletedCount != rPool.RequiredCompletedCount)
                        {
                            poolDiff.PropertyChanges.Add(new PropertyDiff("RequiredCompletedCount", matchedLocalPoolReq.RequiredCompletedCount.ToString(), rPool.RequiredCompletedCount.ToString()));
                            modified = true;
                        }
                        if (!StringsEqual(matchedLocalPoolReq.Description, rPool.Description))
                        {
                            poolDiff.PropertyChanges.Add(new PropertyDiff("Description", matchedLocalPoolReq.Description, rPool.Description));
                            modified = true;
                        }

                        if (modified)
                        {
                            poolDiff.ChangeType = DiffChangeType.Modified;
                            report.QuestPoolRequirementDiffs.Add(poolDiff);
                        }
                        else if (includeUnchanged)
                        {
                            poolDiff.ChangeType = DiffChangeType.Unchanged;
                            report.QuestPoolRequirementDiffs.Add(poolDiff);
                        }
                    }
                }

                // Check removed pool requirements for audited quests
                foreach (var (questId, _) in remotePoolReqsByQuestId)
                {
                    var resolvedQuestId = remoteToLocalQuestId.GetValueOrDefault(questId, questId);
                    if (localPoolReqsByQuestId.TryGetValue(resolvedQuestId, out var localReqs))
                    {
                        foreach (var lpr in localReqs)
                        {
                            if (!processedLocalPoolReqIds.Contains(lpr.Id))
                            {
                                var poolDiff = new QuestPoolRequirementDiff
                                {
                                    ChangeType = DiffChangeType.Removed,
                                    RequirementId = lpr.Id,
                                    QuestId = lpr.QuestId,
                                    PoolTrader = lpr.PoolTrader,
                                    PoolTraderLL = lpr.PoolTraderLL,
                                    OldRequiredCompletedCount = lpr.RequiredCompletedCount,
                                    OldDescription = lpr.Description
                                };
                                poolDiff.PropertyChanges.Add(new PropertyDiff("PoolRequirement", $"{lpr.PoolTrader} LL{lpr.PoolTraderLL} ({lpr.RequiredCompletedCount} tasks)", null));
                                report.QuestPoolRequirementDiffs.Add(poolDiff);
                            }
                        }
                    }
                }
            }

            _log.Info($"Diff computation completed: {report.GetSummaryText()}");
            return report;
        }

        #region Database Load Helpers

        private static async Task<List<LocalQuest>> LoadLocalQuestsAsync(SqliteConnection conn, CancellationToken ct)
        {
            var list = new List<LocalQuest>();
            const string sql = @"
                SELECT Id, BsgId, Name, NameEN, NameKO, NameJA, WikiPageLink, Trader, Location,
                       MinLevel, MinScavKarma, KappaRequired, Faction, RequiredEdition,
                       ExcludedEdition, RequiredDecodeCount, RequiredPrestigeLevel
                FROM Quests;";

            await using var cmd = new SqliteCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var name = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                list.Add(new LocalQuest
                {
                    Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    BsgId = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Name = name,
                    NameEN = reader.IsDBNull(3) ? null : reader.GetString(3),
                    NameKO = reader.IsDBNull(4) ? null : reader.GetString(4),
                    NameJA = reader.IsDBNull(5) ? null : reader.GetString(5),
                    WikiPageLink = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Trader = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Location = reader.IsDBNull(8) ? null : reader.GetString(8),
                    MinLevel = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    MinScavKarma = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    KappaRequired = !reader.IsDBNull(11) && reader.GetInt32(11) != 0,
                    Faction = reader.IsDBNull(12) ? null : reader.GetString(12),
                    RequiredEdition = reader.IsDBNull(13) ? null : reader.GetString(13),
                    ExcludedEdition = reader.IsDBNull(14) ? null : reader.GetString(14),
                    RequiredDecodeCount = reader.IsDBNull(15) ? null : reader.GetInt32(15),
                    RequiredPrestigeLevel = reader.IsDBNull(16) ? null : reader.GetInt32(16),
                    NormalizedName = name.ToLowerInvariant().Replace(" ", "-")
                });
            }
            return list;
        }

        private static async Task<List<LocalQuestRequirement>> LoadLocalQuestRequirementsAsync(SqliteConnection conn, CancellationToken ct)
        {
            var list = new List<LocalQuestRequirement>();
            const string sql = @"
                SELECT Id, QuestId, RequiredQuestId, RequirementType, DelayMinutes, GroupId
                FROM QuestRequirements;";

            await using var cmd = new SqliteCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var qId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var reqQId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var reqId = reader.IsDBNull(0) ? $"{qId}_{reqQId}" : reader.GetString(0);
                list.Add(new LocalQuestRequirement
                {
                    Id = reqId,
                    QuestId = qId,
                    RequiredQuestId = reqQId,
                    RequirementType = reader.IsDBNull(3) ? "Complete" : reader.GetString(3),
                    DelayMinutes = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    GroupId = reader.IsDBNull(5) ? 0 : reader.GetInt32(5)
                });
            }
            return list;
        }

        private static async Task<List<LocalQuestRequiredItem>> LoadLocalQuestRequiredItemsAsync(SqliteConnection conn, CancellationToken ct)
        {
            var list = new List<LocalQuestRequiredItem>();
            const string sql = @"
                SELECT Id, QuestId, ItemId, ItemName, Count, RequiresFIR, RequirementType, SortOrder, DogtagMinLevel, DogtagFaction
                FROM QuestRequiredItems;";

            await using var cmd = new SqliteCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var qId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var itId = reader.IsDBNull(2) ? null : reader.GetString(2);
                var reqItemId = reader.IsDBNull(0) ? (!string.IsNullOrEmpty(qId) && !string.IsNullOrEmpty(itId) ? $"{qId}_{itId}" : Guid.NewGuid().ToString("N")) : reader.GetString(0);
                list.Add(new LocalQuestRequiredItem
                {
                    Id = reqItemId,
                    QuestId = qId,
                    ItemId = itId,
                    ItemName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Count = reader.IsDBNull(4) ? 1 : reader.GetInt32(4),
                    RequiresFIR = !reader.IsDBNull(5) && reader.GetInt32(5) != 0,
                    RequirementType = reader.IsDBNull(6) ? "Required" : reader.GetString(6),
                    SortOrder = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                    DogtagMinLevel = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    DogtagFaction = reader.IsDBNull(9) ? null : reader.GetString(9)
                });
            }
            return list;
        }

        private static async Task<List<LocalHideoutStation>> LoadLocalHideoutStationsAsync(SqliteConnection conn, CancellationToken ct)
        {
            var list = new List<LocalHideoutStation>();
            const string sql = @"
                SELECT Id, Name, NormalizedName, MaxLevel
                FROM HideoutStations;";

            await using var cmd = new SqliteCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new LocalHideoutStation
                {
                    Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    NormalizedName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    MaxLevel = reader.IsDBNull(3) ? 0 : reader.GetInt32(3)
                });
            }
            return list;
        }

        private static async Task<List<LocalHideoutLevel>> LoadLocalHideoutLevelsAsync(SqliteConnection conn, CancellationToken ct)
        {
            var list = new List<LocalHideoutLevel>();
            const string sql = @"
                SELECT Id, StationId, Level, ConstructionTime
                FROM HideoutLevels;";

            await using var cmd = new SqliteCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new LocalHideoutLevel
                {
                    Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    StationId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Level = reader.GetInt32(2),
                    ConstructionTime = reader.IsDBNull(3) ? 0 : reader.GetInt32(3)
                });
            }
            return list;
        }

        private static async Task<List<LocalHideoutItemRequirement>> LoadLocalHideoutItemRequirementsAsync(SqliteConnection conn, CancellationToken ct)
        {
            var list = new List<LocalHideoutItemRequirement>();
            const string sql = @"
                SELECT Id, StationId, Level, ItemId, ItemName, Count, FoundInRaid
                FROM HideoutItemRequirements;";

            await using var cmd = new SqliteCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new LocalHideoutItemRequirement
                {
                    Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    StationId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Level = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    ItemId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    ItemName = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Count = reader.IsDBNull(5) ? 1 : reader.GetInt32(5),
                    FoundInRaid = !reader.IsDBNull(6) && reader.GetInt32(6) != 0
                });
            }
            return list;
        }

        private static async Task<bool> TableExistsAsync(SqliteConnection conn, string tableName, CancellationToken ct)
        {
            const string sql = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
            await using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@name", tableName);
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
            return count > 0;
        }

        private static async Task<List<LocalQuestTraderRequirement>> LoadLocalQuestTraderRequirementsAsync(SqliteConnection conn, CancellationToken ct)
        {
            var list = new List<LocalQuestTraderRequirement>();
            if (!await TableExistsAsync(conn, "QuestTraderRequirements", ct))
                return list;

            const string sql = @"
                SELECT Id, QuestId, TraderId, TraderName, RequirementType, CompareMethod, RequiredLevel, SortOrder
                FROM QuestTraderRequirements;";

            await using var cmd = new SqliteCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new LocalQuestTraderRequirement
                {
                    Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    QuestId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    TraderId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    TraderName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    RequirementType = reader.IsDBNull(4) ? "Level" : reader.GetString(4),
                    CompareMethod = reader.IsDBNull(5) ? ">=" : reader.GetString(5),
                    RequiredLevel = reader.GetInt32(6),
                    SortOrder = reader.IsDBNull(7) ? 0 : reader.GetInt32(7)
                });
            }
            return list;
        }

        private static async Task<List<LocalQuestPoolRequirement>> LoadLocalQuestPoolRequirementsAsync(SqliteConnection conn, CancellationToken ct)
        {
            var list = new List<LocalQuestPoolRequirement>();
            if (!await TableExistsAsync(conn, "QuestPoolRequirements", ct))
                return list;

            const string sql = @"
                SELECT Id, QuestId, PoolTrader, PoolTraderLL, RequiredCompletedCount, Description
                FROM QuestPoolRequirements;";

            await using var cmd = new SqliteCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new LocalQuestPoolRequirement
                {
                    Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    QuestId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    PoolTrader = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    PoolTraderLL = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    RequiredCompletedCount = reader.GetInt32(4),
                    Description = reader.IsDBNull(5) ? null : reader.GetString(5)
                });
            }
            return list;
        }

        private static bool StringsEqual(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b))
                return true;
            return string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
