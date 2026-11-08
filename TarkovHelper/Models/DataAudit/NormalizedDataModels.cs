using System;
using System.Collections.Generic;
using System.Linq;
using TarkovHelper.Models;

namespace TarkovHelper.Models.DataAudit
{
    /// <summary>
    /// Root bundle of normalized data ready for diff comparison and atomic DB application.
    /// </summary>
    public class NormalizedAuditData
    {
        public DateTime NormalizedAt { get; set; } = DateTime.UtcNow;
        public string SourceSummary { get; set; } = string.Empty;

        public List<NormalizedQuest> Quests { get; set; } = new();
        public List<NormalizedQuestRequirement> QuestRequirements { get; set; } = new();
        public List<NormalizedQuestRequiredItem> QuestRequiredItems { get; set; } = new();
        public List<NormalizedQuestTraderRequirement> QuestTraderRequirements { get; set; } = new();
        public List<NormalizedQuestPoolRequirement> QuestPoolRequirements { get; set; } = new();
        public List<NormalizedHideoutStation> HideoutStations { get; set; } = new();
        public List<NormalizedHideoutLevel> HideoutLevels { get; set; } = new();
        public List<NormalizedHideoutItemRequirement> HideoutItemRequirements { get; set; } = new();
    }

    /// <summary>
    /// Normalized quest entity mapping 1:1 with SQLite `Quests` table and in-memory `TarkovTask`.
    /// </summary>
    public class NormalizedQuest
    {
        /// <summary>
        /// Base64-encoded canonical Wiki page URL. Primary key of Quests table.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 24-character hexadecimal MongoDB ObjectId string from BSG/tarkov.dev.
        /// </summary>
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

        public string? NormalizedName { get; set; }
        public string? RequiredEdition { get; set; }
        public string? ExcludedEdition { get; set; }
        public int? RequiredPrestigeLevel { get; set; }
        public int? RequiredDecodeCount { get; set; }

        public List<string>? Maps { get; set; }
        public List<string>? Objectives { get; set; }

        public List<NormalizedQuestTraderRequirement> TraderRequirements { get; set; } = new();
        public List<NormalizedQuestPoolRequirement> PoolRequirements { get; set; } = new();

        /// <summary>
        /// Converts normalized entity to in-memory TarkovTask domain model.
        /// </summary>
        public TarkovTask ToTarkovTask()
        {
            var task = new TarkovTask
            {
                Ids = new List<string> { Id },
                Name = Name,
                NameKo = NameKO,
                NameJa = NameJA,
                Trader = Trader ?? string.Empty,
                Maps = Maps,
                NormalizedName = NormalizedName,
                RequiredLevel = MinLevel,
                RequiredScavKarma = MinScavKarma.HasValue ? (double)MinScavKarma.Value : null,
                ReqKappa = KappaRequired,
                Faction = Faction,
                RequiredEdition = RequiredEdition,
                ExcludedEdition = ExcludedEdition,
                RequiredPrestigeLevel = RequiredPrestigeLevel,
                RequiredDecodeCount = RequiredDecodeCount,
                WikiPageLink = WikiPageLink,
                Objectives = Objectives != null ? new List<string>(Objectives) : null,
                TraderRequirements = TraderRequirements.Select(tr => tr.ToQuestTraderRequirement()).ToList(),
                PoolRequirements = PoolRequirements.Select(pr => pr.ToQuestPoolRequirement()).ToList()
            };

            if (!string.IsNullOrEmpty(BsgId) && !task.Ids.Contains(BsgId, StringComparer.OrdinalIgnoreCase))
            {
                task.Ids.Add(BsgId);
            }

            return task;
        }
    }

    /// <summary>
    /// Normalized prerequisite requirement mapping 1:1 with SQLite `QuestRequirements` table.
    /// </summary>
    public class NormalizedQuestRequirement
    {
        /// <summary>
        /// Deterministic ID or hash: "{QuestId}_{RequiredQuestId}" or SHA256 base64 hash.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Base64 ID of the child quest.
        /// </summary>
        public string QuestId { get; set; } = string.Empty;

        public string QuestName { get; set; } = string.Empty;

        /// <summary>
        /// Base64 ID of the prerequisite quest.
        /// </summary>
        public string RequiredQuestId { get; set; } = string.Empty;

        public string RequiredQuestName { get; set; } = string.Empty;

        public string RequirementType { get; set; } = "Complete";
        public int? DelayMinutes { get; set; }
        public int GroupId { get; set; }

        public TaskRequirement ToTaskRequirement(string? requiredNormalizedName = null)
        {
            return new TaskRequirement
            {
                TaskId = RequiredQuestId,
                TaskNormalizedName = requiredNormalizedName ?? RequiredQuestName,
                Status = new List<string> { RequirementType.ToLowerInvariant() },
                GroupId = GroupId
            };
        }
    }

    /// <summary>
    /// Normalized quest item requirement mapping 1:1 with SQLite `QuestRequiredItems` table.
    /// </summary>
    public class NormalizedQuestRequiredItem
    {
        public string Id { get; set; } = string.Empty;
        public string QuestId { get; set; } = string.Empty;
        public string QuestName { get; set; } = string.Empty;
        public string? ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int Count { get; set; } = 1;
        public bool RequiresFIR { get; set; }
        public string RequirementType { get; set; } = "Required";
        public int SortOrder { get; set; }
        public int? DogtagMinLevel { get; set; }
        public string? DogtagFaction { get; set; }

        public QuestItem ToQuestItem()
        {
            return new QuestItem
            {
                ItemNormalizedName = ItemId ?? ItemName,
                ItemDisplayName = ItemName,
                Amount = Count,
                Requirement = RequirementType,
                FoundInRaid = RequiresFIR,
                DogtagMinLevel = DogtagMinLevel
            };
        }
    }

    /// <summary>
    /// Normalized hideout station mapping 1:1 with SQLite `HideoutStations` table.
    /// </summary>
    public class NormalizedHideoutStation
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? NameKO { get; set; }
        public string? NameJA { get; set; }
        public string? NormalizedName { get; set; }
        public string? ImageLink { get; set; }
        public int MaxLevel { get; set; }

        public HideoutModule ToHideoutModule()
        {
            return new HideoutModule
            {
                Id = Id,
                Name = Name,
                NameKo = NameKO,
                NameJa = NameJA,
                NormalizedName = NormalizedName ?? Name.ToLowerInvariant().Replace(" ", "-"),
                ImageLink = ImageLink
            };
        }
    }

    /// <summary>
    /// Normalized hideout station level mapping 1:1 with SQLite `HideoutLevels` table.
    /// </summary>
    public class NormalizedHideoutLevel
    {
        public string Id { get; set; } = string.Empty;
        public string StationId { get; set; } = string.Empty;
        public string StationName { get; set; } = string.Empty;
        public int Level { get; set; }
        public int ConstructionTime { get; set; }

        public HideoutLevel ToHideoutLevel()
        {
            return new HideoutLevel
            {
                Level = Level,
                ConstructionTime = ConstructionTime
            };
        }
    }

    /// <summary>
    /// Normalized hideout item requirement mapping 1:1 with SQLite `HideoutItemRequirements` table.
    /// </summary>
    public class NormalizedHideoutItemRequirement
    {
        public string Id { get; set; } = string.Empty;
        public string StationId { get; set; } = string.Empty;
        public string StationName { get; set; } = string.Empty;
        public int Level { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string? ItemNameKO { get; set; }
        public string? ItemNameJA { get; set; }
        public string? IconLink { get; set; }
        public int Count { get; set; } = 1;
        public bool FoundInRaid { get; set; }
        public int SortOrder { get; set; }

        public HideoutItemRequirement ToHideoutItemRequirement()
        {
            return new HideoutItemRequirement
            {
                ItemId = ItemId,
                ItemName = ItemName,
                ItemNameKo = ItemNameKO,
                ItemNameJa = ItemNameJA,
                ItemNormalizedName = ItemId,
                IconLink = IconLink,
                Count = Count,
                FoundInRaid = FoundInRaid
            };
        }
    }

    /// <summary>
    /// Normalized quest trader loyalty level requirement.
    /// </summary>
    public class NormalizedQuestTraderRequirement
    {
        public string Id { get; set; } = string.Empty;
        public string QuestId { get; set; } = string.Empty;
        public string QuestName { get; set; } = string.Empty;
        public string? TraderId { get; set; }
        public string TraderName { get; set; } = string.Empty;
        public string RequirementType { get; set; } = "Level";
        public string CompareMethod { get; set; } = ">=";
        public int RequiredLevel { get; set; }
        public int SortOrder { get; set; }

        public QuestTraderRequirement ToQuestTraderRequirement()
        {
            return new QuestTraderRequirement
            {
                Id = Id,
                QuestId = QuestId,
                TraderId = TraderId,
                TraderName = TraderName,
                RequirementType = RequirementType,
                CompareMethod = CompareMethod,
                RequiredLevel = RequiredLevel,
                SortOrder = SortOrder
            };
        }
    }

    /// <summary>
    /// Normalized quest pool milestone requirement.
    /// </summary>
    public class NormalizedQuestPoolRequirement
    {
        public string Id { get; set; } = string.Empty;
        public string QuestId { get; set; } = string.Empty;
        public string QuestName { get; set; } = string.Empty;
        public string PoolTrader { get; set; } = string.Empty;
        public int? PoolTraderLL { get; set; }
        public int RequiredCompletedCount { get; set; }
        public string? Description { get; set; }

        public QuestPoolRequirement ToQuestPoolRequirement()
        {
            return new QuestPoolRequirement
            {
                Id = Id,
                QuestId = QuestId,
                PoolTrader = PoolTrader,
                PoolTraderLL = PoolTraderLL,
                RequiredCompletedCount = RequiredCompletedCount,
                Description = Description
            };
        }
    }
}
