using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services.DataAudit
{
    /// <summary>
    /// Dual-source reconciliation and level inheritance engine.
    /// Reconciles TarkovTracker normalized data against official BSG server data and enforces cascading level logic.
    /// </summary>
    public class BsgServerReconciliationService
    {
        private static readonly ILogger _log = Log.For<BsgServerReconciliationService>();
        private static BsgServerReconciliationService? _instance;
        public static BsgServerReconciliationService Instance => _instance ??= new BsgServerReconciliationService();

        private static readonly HashSet<string> KnownBtrCrossTraderQuests = new(StringComparer.OrdinalIgnoreCase)
        {
            "6740a15566e6a521aa051b15", // Forge a Friendship (Prapor)
            "6740b60c60a98cad1b0e0aa0", // Another Shipping Delay (Ragman)
            "6745fae369a58fceba10343d", // The Higher They Fly (Skier)
            "6744a4717e3818d5bb0680bb", // Stabilize Business (Ragman)
            "67460662d0fbbc74ca0f7229", // Choose Your Friends Wisely (Skier)
            "159a5419285ef68c058214d1"  // Cast the Net (Prapor)
        };

        /// <summary>
        /// Executes reconciliation on the normalized audit dataset.
        /// </summary>
        public async Task<BsgReconciliationReport> ReconcileAsync(
            NormalizedAuditData data, 
            IProgress<string>? progress = null, 
            CancellationToken ct = default)
        {
            progress?.Report("Iniciando conciliación con servidor BSG...");
            _log.Info("Starting dual-source reconciliation process...");

            var report = new BsgReconciliationReport
            {
                TotalQuestsAudited = data.Quests.Count
            };

            // 1. Load authoritative BSG server templates
            var (bsgQuests, sourceUsed, isOffline, failureReason) = await BsgServerQuestsService.Instance.LoadQuestsAsync(progress, ct);
            report.ServerQuestsLoaded = bsgQuests.Count;
            report.SourceUsed = sourceUsed;
            report.IsOfflineFallback = isOffline;
            report.FailureReason = failureReason;

            // Build fast lookups
            var questsById = new Dictionary<string, NormalizedQuest>(StringComparer.OrdinalIgnoreCase);
            var questsByBsgId = new Dictionary<string, NormalizedQuest>(StringComparer.OrdinalIgnoreCase);

            foreach (var q in data.Quests)
            {
                if (!string.IsNullOrEmpty(q.Id)) questsById[q.Id] = q;
                if (!string.IsNullOrEmpty(q.BsgId)) questsByBsgId[q.BsgId] = q;
            }

            // 2. Step 1: Enforce BSG Server Official Data
            progress?.Report("Applying official BSG server requirements...");
            foreach (var q in data.Quests)
            {
                BsgServerQuest? bsgMatch = null;
                if (!string.IsNullOrEmpty(q.BsgId) && bsgQuests.TryGetValue(q.BsgId, out bsgMatch)) { }
                else if (!string.IsNullOrEmpty(q.Id) && bsgQuests.TryGetValue(q.Id, out bsgMatch)) { }

                if (bsgMatch != null)
                {
                    var officialLvl = bsgMatch.GetMinPlayerLevel();
                    if (officialLvl.HasValue && officialLvl.Value > 0)
                    {
                        if (q.MinLevel != officialLvl.Value)
                        {
                            report.Records.Add(new ReconciliationAuditRecord
                            {
                                QuestId = q.Id,
                                QuestName = q.Name,
                                OriginalMinLevel = q.MinLevel,
                                ReconciledMinLevel = officialLvl.Value,
                                Source = ReconciliationSource.BsgServerOfficial,
                                Reason = "Nivel oficial del servidor BSG",
                                Details = $"BSG Server define nivel mínimo {officialLvl.Value} (anterior: {q.MinLevel ?? 0})"
                            });

                            q.MinLevel = officialLvl.Value;
                            report.LevelsCorrectedCount++;
                        }
                    }
                }
                else
                {
                    // Community or placeholder quest
                    report.Records.Add(new ReconciliationAuditRecord
                    {
                        QuestId = q.Id,
                        QuestName = q.Name,
                        OriginalMinLevel = q.MinLevel,
                        ReconciledMinLevel = q.MinLevel,
                        Source = ReconciliationSource.CommunityPreserved,
                        Reason = "Community quest / event",
                        Details = "No encontrada en la base base de BSG; datos preservados de TarkovTracker"
                    });
                }
            }

            // 3. Step 2: Build Prerequisite Graph
            // Mapping: QuestId -> List of Prerequisite QuestIds
            var prereqsGraph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            // From normalized QuestRequirements
            foreach (var req in data.QuestRequirements)
            {
                if (string.IsNullOrEmpty(req.QuestId) || string.IsNullOrEmpty(req.RequiredQuestId)) continue;
                if (!prereqsGraph.ContainsKey(req.QuestId))
                {
                    prereqsGraph[req.QuestId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                prereqsGraph[req.QuestId].Add(req.RequiredQuestId);
            }

            // Also enrich prerequisites from BSG server conditions
            foreach (var kvp in bsgQuests)
            {
                var bsgQ = kvp.Value;
                var bsgPrereqs = bsgQ.GetPrerequisiteQuestIds();
                if (bsgPrereqs.Count > 0)
                {
                    // Find corresponding normalized quest ID
                    NormalizedQuest? normQ = null;
                    if (questsByBsgId.TryGetValue(bsgQ.Id, out normQ) || questsById.TryGetValue(bsgQ.Id, out normQ))
                    {
                        if (!prereqsGraph.ContainsKey(normQ.Id))
                        {
                            prereqsGraph[normQ.Id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        }

                        foreach (var pBsgId in bsgPrereqs)
                        {
                            NormalizedQuest? normParent = null;
                            if (questsByBsgId.TryGetValue(pBsgId, out normParent) || questsById.TryGetValue(pBsgId, out normParent))
                            {
                                prereqsGraph[normQ.Id].Add(normParent.Id);
                            }
                        }
                    }
                }
            }

            // 4. Step 3: Cascade Level Inheritance (Fixpoint loop)
            progress?.Report("Cascading level requirements...");
            bool changed = true;
            int iteration = 0;
            const int maxIterations = 20;

            while (changed && iteration < maxIterations)
            {
                changed = false;
                iteration++;

                foreach (var q in data.Quests)
                {
                    if (!prereqsGraph.TryGetValue(q.Id, out var parents) || parents.Count == 0)
                        continue;

                    int maxParentLvl = 0;
                    string highestParentName = string.Empty;

                    foreach (var parentId in parents)
                    {
                        if (questsById.TryGetValue(parentId, out var parentQ))
                        {
                            int pLvl = parentQ.MinLevel ?? 0;
                            if (pLvl > maxParentLvl)
                            {
                                maxParentLvl = pLvl;
                                highestParentName = parentQ.Name;
                            }
                        }
                    }

                    if (maxParentLvl > 0 && (q.MinLevel ?? 0) < maxParentLvl)
                    {
                        report.Records.Add(new ReconciliationAuditRecord
                        {
                            QuestId = q.Id,
                            QuestName = q.Name,
                            OriginalMinLevel = q.MinLevel,
                            ReconciledMinLevel = maxParentLvl,
                            Source = ReconciliationSource.CascadeInherited,
                            Reason = "Herencia en cascada",
                            Details = $"Requiere '{highestParentName}' (Nivel {maxParentLvl}), hereda automáticamente nivel {maxParentLvl}"
                        });

                        q.MinLevel = maxParentLvl;
                        report.CascadeInheritedCount++;
                        changed = true;
                    }
                }
            }

            // 5. Step 4: Special Trader Gating (Cross-Trader BTR Quests)
            progress?.Report("Validando cadenas de comerciantes especiales...");
            foreach (var q in data.Quests)
            {
                bool isBtrCrossQuest = KnownBtrCrossTraderQuests.Contains(q.Id) ||
                                       (!string.IsNullOrEmpty(q.BsgId) && KnownBtrCrossTraderQuests.Contains(q.BsgId));

                if (isBtrCrossQuest)
                {
                    if ((q.MinLevel ?? 0) < 20)
                    {
                        report.Records.Add(new ReconciliationAuditRecord
                        {
                            QuestId = q.Id,
                            QuestName = q.Name,
                            OriginalMinLevel = q.MinLevel,
                            ReconciledMinLevel = 20,
                            Source = ReconciliationSource.BtrDriverGating,
                            Reason = "Gating de cadena BTR",
                            Details = "Cross-task linked to BTR chain (requires level 20)"
                        });

                        q.MinLevel = 20;
                        report.SpecialTraderGatedCount++;
                    }
                }
            }

            _log.Info($"Reconciliation complete. Server corrected: {report.LevelsCorrectedCount}, Cascaded: {report.CascadeInheritedCount}, Special gated: {report.SpecialTraderGatedCount}");

            // Write report to Logs/sync_audit.log asynchronously
            _ = Task.Run(() =>
            {
                try
                {
                    var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                    Directory.CreateDirectory(logDir);
                    var logFile = Path.Combine(logDir, "sync_audit.log");
                    System.IO.File.AppendAllText(logFile, report.GenerateSummaryText() + "\n" + new string('-', 80) + "\n");
                }
                catch (Exception ex)
                {
                    _log.Warning($"Failed to write sync_audit.log: {ex.Message}");
                }
            });

            return report;
        }
    }
}
