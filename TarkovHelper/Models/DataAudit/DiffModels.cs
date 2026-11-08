using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TarkovHelper.Models.DataAudit
{
    /// <summary>
    /// Type of difference detected between remote data and local database.
    /// </summary>
    public enum DiffChangeType
    {
        Unchanged = 0,
        Added = 1,
        Modified = 2,
        Removed = 3
    }

    /// <summary>
    /// Category of the audited entity.
    /// </summary>
    public enum DiffCategory
    {
        Quest,
        QuestRequirement,
        QuestItem,
        QuestTraderRequirement,
        QuestPoolRequirement,
        HideoutStation,
        HideoutLevel,
        HideoutItem
    }

    /// <summary>
    /// Represents an individual property change (Old Value vs New Value).
    /// </summary>
    public class PropertyDiff
    {
        public string PropertyName { get; set; } = string.Empty;
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        public PropertyDiff() { }

        public PropertyDiff(string propertyName, string? oldValue, string? newValue)
        {
            PropertyName = propertyName;
            OldValue = oldValue;
            NewValue = newValue;
        }

        public override string ToString() => $"{PropertyName}: '{OldValue}' -> '{NewValue}'";
    }

    /// <summary>
    /// Common contract for all diff items.
    /// </summary>
    public interface IDiffItem
    {
        DiffCategory Category { get; }
        DiffChangeType ChangeType { get; set; }
        string EntityId { get; }
        string DisplayName { get; }
        string Description { get; }
        List<PropertyDiff> PropertyChanges { get; }
    }

    /// <summary>
    /// Diff representation for a Quest (MinLevel, Faction, Trader, Prerequisites, etc.).
    /// </summary>
    public class QuestDiff : IDiffItem
    {
        public DiffCategory Category => DiffCategory.Quest;
        public DiffChangeType ChangeType { get; set; } = DiffChangeType.Unchanged;

        public string QuestId { get; set; } = string.Empty;
        public string? BsgId { get; set; }
        public string QuestName { get; set; } = string.Empty;

        public string EntityId => QuestId;
        public string DisplayName => QuestName;

        public string? OldTrader { get; set; }
        public string? NewTrader { get; set; }

        public int? OldMinLevel { get; set; }
        public int? NewMinLevel { get; set; }

        public string? OldFaction { get; set; }
        public string? NewFaction { get; set; }

        public int? OldMinScavKarma { get; set; }
        public int? NewMinScavKarma { get; set; }

        public bool? OldKappaRequired { get; set; }
        public bool? NewKappaRequired { get; set; }

        public string? OldRequiredEdition { get; set; }
        public string? NewRequiredEdition { get; set; }

        public string? OldExcludedEdition { get; set; }
        public string? NewExcludedEdition { get; set; }

        public int? OldRequiredPrestigeLevel { get; set; }
        public int? NewRequiredPrestigeLevel { get; set; }

        public int? OldRequiredDecodeCount { get; set; }
        public int? NewRequiredDecodeCount { get; set; }

        public int? OldRequiredTraderLoyaltyLevel { get; set; }
        public int? NewRequiredTraderLoyaltyLevel { get; set; }

        public List<string> AddedPrerequisiteQuestIds { get; set; } = new();
        public List<string> RemovedPrerequisiteQuestIds { get; set; } = new();
        public List<string> AddedPrerequisiteNames { get; set; } = new();
        public List<string> RemovedPrerequisiteNames { get; set; } = new();

        public NormalizedQuest? RemoteQuest { get; set; }
        public List<NormalizedQuestTraderRequirement> TraderRequirements { get; set; } = new();
        public List<NormalizedQuestPoolRequirement> PoolRequirements { get; set; } = new();
        public List<PropertyDiff> PropertyChanges { get; set; } = new();

        public string Description
        {
            get
            {
                if (ChangeType == DiffChangeType.Added)
                    return $"New quest '{QuestName}' (Trader: {NewTrader ?? "N/A"}, MinLevel: {NewMinLevel?.ToString() ?? "None"})";
                if (ChangeType == DiffChangeType.Removed)
                    return $"Quest '{QuestName}' removed from remote source";
                if (PropertyChanges.Count > 0 || AddedPrerequisiteQuestIds.Count > 0 || RemovedPrerequisiteQuestIds.Count > 0)
                {
                    var sb = new StringBuilder();
                    if (PropertyChanges.Count > 0)
                        sb.Append(string.Join(", ", PropertyChanges.Select(p => p.ToString())));
                    if (AddedPrerequisiteQuestIds.Count > 0)
                    {
                        if (sb.Length > 0) sb.Append("; ");
                        sb.Append($"Added prereqs: +{AddedPrerequisiteQuestIds.Count}");
                    }
                    if (RemovedPrerequisiteQuestIds.Count > 0)
                    {
                        if (sb.Length > 0) sb.Append("; ");
                        sb.Append($"Removed prereqs: -{RemovedPrerequisiteQuestIds.Count}");
                    }
                    return sb.ToString();
                }
                return "No changes";
            }
        }
    }

    /// <summary>
    /// Diff representation for a Quest prerequisite requirement.
    /// </summary>
    public class QuestRequirementDiff : IDiffItem
    {
        public DiffCategory Category => DiffCategory.QuestRequirement;
        public DiffChangeType ChangeType { get; set; } = DiffChangeType.Unchanged;

        public string RequirementId { get; set; } = string.Empty;
        public string QuestId { get; set; } = string.Empty;
        public string QuestName { get; set; } = string.Empty;
        public string RequiredQuestId { get; set; } = string.Empty;
        public string RequiredQuestName { get; set; } = string.Empty;

        public string EntityId => RequirementId;
        public string DisplayName => $"{QuestName} ➔ {RequiredQuestName}";

        public string? OldRequirementType { get; set; }
        public string? NewRequirementType { get; set; }

        public int? OldDelayMinutes { get; set; }
        public int? NewDelayMinutes { get; set; }

        public int? OldGroupId { get; set; }
        public int? NewGroupId { get; set; }

        public NormalizedQuestRequirement? RemoteRequirement { get; set; }
        public List<PropertyDiff> PropertyChanges { get; set; } = new();

        public string Description
        {
            get
            {
                if (ChangeType == DiffChangeType.Added)
                    return $"Added requirement: requires '{RequiredQuestName}' ({NewRequirementType ?? "Complete"})";
                if (ChangeType == DiffChangeType.Removed)
                    return $"Removed requirement on '{RequiredQuestName}'";
                if (PropertyChanges.Count > 0)
                    return string.Join(", ", PropertyChanges.Select(p => p.ToString()));
                return "No changes";
            }
        }
    }

    /// <summary>
    /// Diff representation for a Quest required item (quantity, FIR flag, etc.).
    /// </summary>
    public class QuestItemDiff : IDiffItem
    {
        public DiffCategory Category => DiffCategory.QuestItem;
        public DiffChangeType ChangeType { get; set; } = DiffChangeType.Unchanged;

        public string RequirementId { get; set; } = string.Empty;
        public string QuestId { get; set; } = string.Empty;
        public string QuestName { get; set; } = string.Empty;
        public string? ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;

        public string EntityId => RequirementId;
        public string DisplayName => $"{QuestName}: {ItemName}";

        public int? OldQuantity { get; set; }
        public int? NewQuantity { get; set; }

        public int? OldCount { get => OldQuantity; set => OldQuantity = value; }
        public int? NewCount { get => NewQuantity; set => NewQuantity = value; }

        public bool? OldRequiresFIR { get; set; }
        public bool? NewRequiresFIR { get; set; }

        public string? OldRequirementType { get; set; }
        public string? NewRequirementType { get; set; }

        public NormalizedQuestRequiredItem? RemoteItem { get; set; }
        public List<PropertyDiff> PropertyChanges { get; set; } = new();

        public string Description
        {
            get
            {
                if (ChangeType == DiffChangeType.Added)
                    return $"New required item '{ItemName}' x{NewQuantity}{(NewRequiresFIR == true ? " (FIR)" : "")}";
                if (ChangeType == DiffChangeType.Removed)
                    return $"Removed item requirement '{ItemName}'";
                if (PropertyChanges.Count > 0)
                    return string.Join(", ", PropertyChanges.Select(p => p.ToString()));
                return "No changes";
            }
        }
    }

    /// <summary>
    /// Diff representation for a Hideout Station.
    /// </summary>
    public class HideoutStationDiff : IDiffItem
    {
        public DiffCategory Category => DiffCategory.HideoutStation;
        public DiffChangeType ChangeType { get; set; } = DiffChangeType.Unchanged;

        public string StationId { get; set; } = string.Empty;
        public string StationName { get; set; } = string.Empty;

        public string EntityId => StationId;
        public string DisplayName => StationName;

        public int? OldMaxLevel { get; set; }
        public int? NewMaxLevel { get; set; }

        public NormalizedHideoutStation? RemoteStation { get; set; }
        public List<PropertyDiff> PropertyChanges { get; set; } = new();

        public string Description
        {
            get
            {
                if (ChangeType == DiffChangeType.Added)
                    return $"New hideout station '{StationName}' (MaxLevel: {NewMaxLevel})";
                if (ChangeType == DiffChangeType.Removed)
                    return $"Removed hideout station '{StationName}'";
                if (PropertyChanges.Count > 0)
                    return string.Join(", ", PropertyChanges.Select(p => p.ToString()));
                return "No changes";
            }
        }
    }

    /// <summary>
    /// Diff representation for a Hideout Station Level (e.g. construction time).
    /// </summary>
    public class HideoutLevelDiff : IDiffItem
    {
        public DiffCategory Category => DiffCategory.HideoutLevel;
        public DiffChangeType ChangeType { get; set; } = DiffChangeType.Unchanged;

        public string LevelId { get; set; } = string.Empty;
        public string StationId { get; set; } = string.Empty;
        public string StationName { get; set; } = string.Empty;
        public int Level { get; set; }

        public string EntityId => LevelId;
        public string DisplayName => $"{StationName} Lv.{Level}";

        public int? OldConstructionTime { get; set; }
        public int? NewConstructionTime { get; set; }

        public NormalizedHideoutLevel? RemoteLevel { get; set; }
        public List<PropertyDiff> PropertyChanges { get; set; } = new();

        public string Description
        {
            get
            {
                if (ChangeType == DiffChangeType.Added)
                    return $"New station level '{StationName}' Lv.{Level} (Construction: {NewConstructionTime}s)";
                if (ChangeType == DiffChangeType.Removed)
                    return $"Removed station level '{StationName}' Lv.{Level}";
                if (PropertyChanges.Count > 0)
                    return string.Join(", ", PropertyChanges.Select(p => p.ToString()));
                return "No changes";
            }
        }
    }

    /// <summary>
    /// Diff representation for a Hideout Item Requirement.
    /// </summary>
    public class HideoutItemDiff : IDiffItem
    {
        public DiffCategory Category => DiffCategory.HideoutItem;
        public DiffChangeType ChangeType { get; set; } = DiffChangeType.Unchanged;

        public string RequirementId { get; set; } = string.Empty;
        public string StationId { get; set; } = string.Empty;
        public string StationName { get; set; } = string.Empty;
        public int Level { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;

        public string EntityId => RequirementId;
        public string DisplayName => $"{StationName} Lv.{Level}: {ItemName}";

        public int? OldQuantity { get; set; }
        public int? NewQuantity { get; set; }

        public int? OldCount { get => OldQuantity; set => OldQuantity = value; }
        public int? NewCount { get => NewQuantity; set => NewQuantity = value; }

        public bool? OldFoundInRaid { get; set; }
        public bool? NewFoundInRaid { get; set; }

        public NormalizedHideoutItemRequirement? RemoteItem { get; set; }
        public List<PropertyDiff> PropertyChanges { get; set; } = new();

        public string Description
        {
            get
            {
                if (ChangeType == DiffChangeType.Added)
                    return $"Added item requirement: {ItemName} x{NewQuantity}{(NewFoundInRaid == true ? " (FIR)" : "")}";
                if (ChangeType == DiffChangeType.Removed)
                    return $"Removed item requirement: {ItemName}";
                if (PropertyChanges.Count > 0)
                    return string.Join(", ", PropertyChanges.Select(p => p.ToString()));
                return "No changes";
            }
        }
    }

    /// <summary>
    /// Diff representation for a Quest Trader Loyalty Level requirement.
    /// </summary>
    public class QuestTraderRequirementDiff : IDiffItem
    {
        public DiffCategory Category => DiffCategory.QuestTraderRequirement;
        public DiffChangeType ChangeType { get; set; } = DiffChangeType.Unchanged;

        public string RequirementId { get; set; } = string.Empty;
        public string QuestId { get; set; } = string.Empty;
        public string QuestName { get; set; } = string.Empty;
        public string TraderName { get; set; } = string.Empty;
        public string? TraderId { get; set; }

        public string EntityId => RequirementId;
        public string DisplayName => $"{QuestName}: {TraderName} LL{NewRequiredLevel ?? OldRequiredLevel}";

        public int? OldRequiredLevel { get; set; }
        public int? NewRequiredLevel { get; set; }
        public string? OldCompareMethod { get; set; }
        public string? NewCompareMethod { get; set; }

        public NormalizedQuestTraderRequirement? RemoteRequirement { get; set; }
        public List<PropertyDiff> PropertyChanges { get; set; } = new();

        public string Description
        {
            get
            {
                if (ChangeType == DiffChangeType.Added)
                    return $"Added trader requirement: {TraderName} {NewCompareMethod ?? ">="} {NewRequiredLevel}";
                if (ChangeType == DiffChangeType.Removed)
                    return $"Removed trader requirement for {TraderName}";
                if (PropertyChanges.Count > 0)
                    return string.Join(", ", PropertyChanges.Select(p => p.ToString()));
                return "No changes";
            }
        }
    }

    /// <summary>
    /// Diff representation for a Quest Task Pool threshold requirement.
    /// </summary>
    public class QuestPoolRequirementDiff : IDiffItem
    {
        public DiffCategory Category => DiffCategory.QuestPoolRequirement;
        public DiffChangeType ChangeType { get; set; } = DiffChangeType.Unchanged;

        public string RequirementId { get; set; } = string.Empty;
        public string QuestId { get; set; } = string.Empty;
        public string QuestName { get; set; } = string.Empty;
        public string PoolTrader { get; set; } = string.Empty;
        public int? PoolTraderLL { get; set; }

        public string EntityId => RequirementId;
        public string DisplayName => $"{QuestName}: Pool {PoolTrader} LL{PoolTraderLL} ({NewRequiredCompletedCount ?? OldRequiredCompletedCount} tasks)";

        public int? OldRequiredCompletedCount { get; set; }
        public int? NewRequiredCompletedCount { get; set; }
        public string? OldDescription { get; set; }
        public string? NewDescription { get; set; }

        public NormalizedQuestPoolRequirement? RemoteRequirement { get; set; }
        public List<PropertyDiff> PropertyChanges { get; set; } = new();

        public string Description
        {
            get
            {
                if (ChangeType == DiffChangeType.Added)
                    return $"Added pool requirement: Complete at least {NewRequiredCompletedCount} {PoolTrader} LL{PoolTraderLL} tasks";
                if (ChangeType == DiffChangeType.Removed)
                    return $"Removed pool requirement for {PoolTrader}";
                if (PropertyChanges.Count > 0)
                    return string.Join(", ", PropertyChanges.Select(p => p.ToString()));
                return "No changes";
            }
        }
    }

    /// <summary>
    /// Aggregate report containing all categorized diff items and overall summary metrics.
    /// </summary>
    public class DataDiffReport
    {
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public string DatabasePath { get; set; } = string.Empty;

        /// <summary>
        /// Optional reconciliation report detailing BSG official server overrides and cascade inferences.
        /// </summary>
        public BsgReconciliationReport? ReconciliationReport { get; set; }

        public List<QuestDiff> QuestDiffs { get; set; } = new();
        public List<QuestRequirementDiff> QuestRequirementDiffs { get; set; } = new();
        public List<QuestItemDiff> QuestItemDiffs { get; set; } = new();
        public List<QuestTraderRequirementDiff> QuestTraderRequirementDiffs { get; set; } = new();
        public List<QuestPoolRequirementDiff> QuestPoolRequirementDiffs { get; set; } = new();
        public List<HideoutStationDiff> HideoutStationDiffs { get; set; } = new();
        public List<HideoutLevelDiff> HideoutLevelDiffs { get; set; } = new();
        public List<HideoutItemDiff> HideoutItemDiffs { get; set; } = new();

        public int TotalChanges => AddedCount + ModifiedCount + RemovedCount;

        public int AddedCount =>
            QuestDiffs.Count(d => d.ChangeType == DiffChangeType.Added) +
            QuestRequirementDiffs.Count(d => d.ChangeType == DiffChangeType.Added) +
            QuestItemDiffs.Count(d => d.ChangeType == DiffChangeType.Added) +
            QuestTraderRequirementDiffs.Count(d => d.ChangeType == DiffChangeType.Added) +
            QuestPoolRequirementDiffs.Count(d => d.ChangeType == DiffChangeType.Added) +
            HideoutStationDiffs.Count(d => d.ChangeType == DiffChangeType.Added) +
            HideoutLevelDiffs.Count(d => d.ChangeType == DiffChangeType.Added) +
            HideoutItemDiffs.Count(d => d.ChangeType == DiffChangeType.Added);

        public int ModifiedCount =>
            QuestDiffs.Count(d => d.ChangeType == DiffChangeType.Modified) +
            QuestRequirementDiffs.Count(d => d.ChangeType == DiffChangeType.Modified) +
            QuestItemDiffs.Count(d => d.ChangeType == DiffChangeType.Modified) +
            QuestTraderRequirementDiffs.Count(d => d.ChangeType == DiffChangeType.Modified) +
            QuestPoolRequirementDiffs.Count(d => d.ChangeType == DiffChangeType.Modified) +
            HideoutStationDiffs.Count(d => d.ChangeType == DiffChangeType.Modified) +
            HideoutLevelDiffs.Count(d => d.ChangeType == DiffChangeType.Modified) +
            HideoutItemDiffs.Count(d => d.ChangeType == DiffChangeType.Modified);

        public int RemovedCount =>
            QuestDiffs.Count(d => d.ChangeType == DiffChangeType.Removed) +
            QuestRequirementDiffs.Count(d => d.ChangeType == DiffChangeType.Removed) +
            QuestItemDiffs.Count(d => d.ChangeType == DiffChangeType.Removed) +
            QuestTraderRequirementDiffs.Count(d => d.ChangeType == DiffChangeType.Removed) +
            QuestPoolRequirementDiffs.Count(d => d.ChangeType == DiffChangeType.Removed) +
            HideoutStationDiffs.Count(d => d.ChangeType == DiffChangeType.Removed) +
            HideoutLevelDiffs.Count(d => d.ChangeType == DiffChangeType.Removed) +
            HideoutItemDiffs.Count(d => d.ChangeType == DiffChangeType.Removed);

        public int UnchangedCount =>
            QuestDiffs.Count(d => d.ChangeType == DiffChangeType.Unchanged) +
            QuestRequirementDiffs.Count(d => d.ChangeType == DiffChangeType.Unchanged) +
            QuestItemDiffs.Count(d => d.ChangeType == DiffChangeType.Unchanged) +
            QuestTraderRequirementDiffs.Count(d => d.ChangeType == DiffChangeType.Unchanged) +
            QuestPoolRequirementDiffs.Count(d => d.ChangeType == DiffChangeType.Unchanged) +
            HideoutStationDiffs.Count(d => d.ChangeType == DiffChangeType.Unchanged) +
            HideoutLevelDiffs.Count(d => d.ChangeType == DiffChangeType.Unchanged) +
            HideoutItemDiffs.Count(d => d.ChangeType == DiffChangeType.Unchanged);

        public bool HasChanges => TotalChanges > 0;

        /// <summary>
        /// Returns an enumeration of all categorized diff items regardless of change type.
        /// </summary>
        public IEnumerable<IDiffItem> AllDiffs()
        {
            foreach (var d in QuestDiffs) yield return d;
            foreach (var d in QuestRequirementDiffs) yield return d;
            foreach (var d in QuestItemDiffs) yield return d;
            foreach (var d in QuestTraderRequirementDiffs) yield return d;
            foreach (var d in QuestPoolRequirementDiffs) yield return d;
            foreach (var d in HideoutStationDiffs) yield return d;
            foreach (var d in HideoutLevelDiffs) yield return d;
            foreach (var d in HideoutItemDiffs) yield return d;
        }

        /// <summary>
        /// Returns an enumeration of changed diff items (Added, Modified, Removed).
        /// </summary>
        public IEnumerable<IDiffItem> AllChanges()
        {
            return AllDiffs().Where(d => d.ChangeType != DiffChangeType.Unchanged);
        }

        /// <summary>
        /// Returns a formatted human-readable summary of the diff report.
        /// </summary>
        public string GetSummaryText()
        {
            return $"Data Diff Summary: {TotalChanges} total changes " +
                   $"({AddedCount} added, {ModifiedCount} modified, {RemovedCount} removed, {UnchangedCount} unchanged).";
        }
    }
}
