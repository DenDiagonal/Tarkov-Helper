using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TarkovHelper.Models.DataAudit
{
    public enum ReconciliationSource
    {
        Unchanged,
        BsgServerOfficial,
        CascadeInherited,
        BtrDriverGating,
        CommunityPreserved
    }

    /// <summary>
    /// Audit record for tracking why and how a quest's level or requirements were adjusted.
    /// </summary>
    public class ReconciliationAuditRecord
    {
        public string QuestId { get; set; } = string.Empty;
        public string QuestName { get; set; } = string.Empty;
        public int? OriginalMinLevel { get; set; }
        public int? ReconciledMinLevel { get; set; }
        public ReconciliationSource Source { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? Details { get; set; }

        public override string ToString()
        {
            return $"[{Source}] {QuestName} ({QuestId}): {OriginalMinLevel} -> {ReconciledMinLevel} ({Reason})";
        }
    }

    /// <summary>
    /// Comprehensive report of the dual-source reconciliation process.
    /// </summary>
    public class BsgReconciliationReport
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string SourceUsed { get; set; } = "Unknown";
        public bool IsOfflineFallback { get; set; }
        public string? FailureReason { get; set; }
        public int ServerQuestsLoaded { get; set; }
        public int TotalQuestsAudited { get; set; }
        public int LevelsCorrectedCount { get; set; }
        public int CascadeInheritedCount { get; set; }
        public int SpecialTraderGatedCount { get; set; }
        public List<ReconciliationAuditRecord> Records { get; set; } = new();

        public bool HasCorrections => LevelsCorrectedCount > 0 || CascadeInheritedCount > 0 || SpecialTraderGatedCount > 0;

        public string GenerateSummaryText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"--- INFORME DE CONCILIACIÓN BSG SERVER ({Timestamp:yyyy-MM-dd HH:mm:ss}) ---");
            sb.AppendLine($"Fuente utilizada: {SourceUsed}");
            if (IsOfflineFallback && !string.IsNullOrEmpty(FailureReason))
            {
                sb.AppendLine($"⚠️ Modo contingencia (Caché local): {FailureReason}");
            }
            sb.AppendLine($"Misiones oficiales en servidor BSG: {ServerQuestsLoaded}");
            sb.AppendLine($"Misiones totales auditadas: {TotalQuestsAudited}");
            sb.AppendLine($"Niveles corregidos por servidor BSG: {LevelsCorrectedCount}");
            sb.AppendLine($"Niveles propagados por herencia en cascada: {CascadeInheritedCount}");
            sb.AppendLine($"Misiones de comerciantes puente aseguradas: {SpecialTraderGatedCount}");

            if (Records.Count > 0)
            {
                sb.AppendLine("\nDetalles de ajustes destacados:");
                foreach (var r in Records)
                {
                    if (r.Source != ReconciliationSource.Unchanged)
                    {
                        sb.AppendLine($" * {r}");
                    }
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// BSG official quest condition entity from server quest template.
    /// </summary>
    public class BsgCondition
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("conditionType")]
        public string? ConditionType { get; set; }

        [JsonPropertyName("compareMethod")]
        public string? CompareMethod { get; set; }

        [JsonPropertyName("value")]
        public object? Value { get; set; }

        [JsonPropertyName("target")]
        public object? Target { get; set; }

        [JsonPropertyName("status")]
        public object? Status { get; set; }

        public int? GetNumericValue()
        {
            if (Value == null) return null;
            if (Value is System.Text.Json.JsonElement elem)
            {
                if (elem.ValueKind == System.Text.Json.JsonValueKind.Number && elem.TryGetInt32(out var valInt))
                    return valInt;
                if (elem.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(elem.GetString(), out var parsedInt))
                    return parsedInt;
            }
            if (int.TryParse(Value.ToString(), out var parsed))
                return parsed;

            return null;
        }

        public string? GetTargetString()
        {
            if (Target == null) return null;
            if (Target is System.Text.Json.JsonElement elem)
            {
                if (elem.ValueKind == System.Text.Json.JsonValueKind.String)
                    return elem.GetString();
                if (elem.ValueKind == System.Text.Json.JsonValueKind.Array && elem.GetArrayLength() > 0)
                    return elem[0].GetString();
            }
            return Target.ToString();
        }
    }

    /// <summary>
    /// BSG quest conditions bundle.
    /// </summary>
    public class BsgQuestConditions
    {
        [JsonPropertyName("AvailableForStart")]
        public List<BsgCondition> AvailableForStart { get; set; } = new();

        [JsonPropertyName("AvailableForFinish")]
        public List<BsgCondition> AvailableForFinish { get; set; } = new();

        [JsonPropertyName("Fail")]
        public List<BsgCondition> Fail { get; set; } = new();
    }

    /// <summary>
    /// Official quest model mapping directly to BSG server quest template (quests.json).
    /// </summary>
    public class BsgServerQuest
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("QuestName")]
        public string QuestName { get; set; } = string.Empty;

        [JsonPropertyName("traderId")]
        public string? TraderId { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("side")]
        public string? Side { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("restartable")]
        public bool Restartable { get; set; }

        [JsonPropertyName("instantComplete")]
        public bool InstantComplete { get; set; }

        [JsonPropertyName("conditions")]
        public BsgQuestConditions Conditions { get; set; } = new();

        /// <summary>
        /// Extracts official player level requirement if defined in AvailableForStart.
        /// </summary>
        public int? GetMinPlayerLevel()
        {
            if (Conditions.AvailableForStart == null) return null;

            foreach (var cond in Conditions.AvailableForStart)
            {
                if (string.Equals(cond.ConditionType, "Level", StringComparison.OrdinalIgnoreCase))
                {
                    var val = cond.GetNumericValue();
                    if (val.HasValue && val.Value > 0)
                    {
                        return val.Value;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Extracts direct prerequisite quest IDs required to start this quest.
        /// </summary>
        public List<string> GetPrerequisiteQuestIds()
        {
            var list = new List<string>();
            if (Conditions.AvailableForStart == null) return list;

            foreach (var cond in Conditions.AvailableForStart)
            {
                if (string.Equals(cond.ConditionType, "Quest", StringComparison.OrdinalIgnoreCase))
                {
                    var target = cond.GetTargetString();
                    if (!string.IsNullOrEmpty(target) && !list.Contains(target, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(target);
                    }
                }
            }
            return list;
        }

        /// <summary>
        /// Extracts mutual fail quest IDs (quests that fail if this quest finishes or vice-versa).
        /// </summary>
        public List<string> GetMutualFailQuestIds()
        {
            var list = new List<string>();
            if (Conditions.Fail == null) return list;

            foreach (var cond in Conditions.Fail)
            {
                if (string.Equals(cond.ConditionType, "Quest", StringComparison.OrdinalIgnoreCase))
                {
                    var target = cond.GetTargetString();
                    if (!string.IsNullOrEmpty(target) && !list.Contains(target, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(target);
                    }
                }
            }
            return list;
        }
    }
}
