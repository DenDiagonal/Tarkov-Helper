using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TarkovHelper.Models.DataAudit
{
    /// <summary>
    /// Raw quest extracted from MediaWiki wikitext and infobox templates.
    /// </summary>
    public class RawWikiQuest
    {
        /// <summary>
        /// Page title on MediaWiki (e.g., "Debut", "Postman Pat - Part 1").
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Full canonical URL of the quest wiki page.
        /// </summary>
        public string? WikiUrl { get; set; }

        /// <summary>
        /// MediaWiki revision ID (for incremental updates and caching).
        /// </summary>
        public long? RevisionId { get; set; }

        /// <summary>
        /// Full raw wikitext content of the page.
        /// </summary>
        public string? Wikitext { get; set; }

        /// <summary>
        /// Trader name as parsed from infobox (e.g. "Prapor", "Therapist").
        /// </summary>
        public string? Trader { get; set; }

        /// <summary>
        /// Location/map parsed from infobox (e.g. "Customs", "Any").
        /// </summary>
        public string? Location { get; set; }

        /// <summary>
        /// Minimum player level required to accept the quest.
        /// </summary>
        public int? MinPlayerLevel { get; set; }

        /// <summary>
        /// Minimum Scav Karma / Fence reputation required.
        /// </summary>
        public int? MinScavKarma { get; set; }

        /// <summary>
        /// Faction restriction ("Bear", "Usec", or null for all).
        /// </summary>
        public string? Faction { get; set; }

        /// <summary>
        /// Whether the quest is required for the Collector / Kappa container.
        /// </summary>
        public bool KappaRequired { get; set; }

        /// <summary>
        /// Whether this entry is an EFT Story Chapter (Category:Story chapters).
        /// </summary>
        public bool IsStoryChapter { get; set; } = false;

        /// <summary>
        /// Required game edition (e.g. "EOD", "Unheard").
        /// </summary>
        public string? RequiredEdition { get; set; }

        /// <summary>
        /// Excluded game edition (e.g. "Unheard").
        /// </summary>
        public string? ExcludedEdition { get; set; }

        /// <summary>
        /// Required prestige level (0-5).
        /// </summary>
        public int? RequiredPrestigeLevel { get; set; }

        /// <summary>
        /// Required DSP transmitter decode count.
        /// </summary>
        public int? RequiredDecodeCount { get; set; }

        /// <summary>
        /// Prerequisite quest conditions extracted from infobox |previous and ==Requirements==.
        /// </summary>
        public List<RawWikiQuestRequirement> Prerequisites { get; set; } = new();

        /// <summary>
        /// Required items parsed from "Related Quest Items" table.
        /// </summary>
        public List<RawWikiRequiredItem> RequiredItems { get; set; } = new();

        /// <summary>
        /// Objectives parsed from ==Objectives== section.
        /// </summary>
        public List<RawWikiObjective> Objectives { get; set; } = new();

        /// <summary>
        /// Mutually exclusive / alternative quests extracted from |related.
        /// </summary>
        public List<string> AlternativeQuests { get; set; } = new();

        /// <summary>
        /// Trader loyalty level requirements parsed from wikitext infobox (|LL requirement = ...).
        /// </summary>
        public List<RawWikiQuestTraderRequirement> TraderRequirements { get; set; } = new();

        /// <summary>
        /// Task pool threshold requirements parsed from wikitext infobox (|LL quests = ...).
        /// </summary>
        public List<RawWikiQuestPoolRequirement> PoolRequirements { get; set; } = new();
    }

    /// <summary>
    /// Raw trader loyalty level requirement from wikitext.
    /// </summary>
    public class RawWikiQuestTraderRequirement
    {
        public string TraderName { get; set; } = string.Empty;
        public int RequiredLevel { get; set; }
        public string CompareMethod { get; set; } = ">=";
        public string RequirementType { get; set; } = "Level";
    }

    /// <summary>
    /// Raw task pool requirement from wikitext.
    /// </summary>
    public class RawWikiQuestPoolRequirement
    {
        public string PoolTrader { get; set; } = string.Empty;
        public int? PoolTraderLL { get; set; }
        public int RequiredCompletedCount { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>
    /// Raw prerequisite quest requirement extracted from wikitext.
    /// </summary>
    public class RawWikiQuestRequirement
    {
        /// <summary>
        /// Title of the required quest (e.g. "Checking").
        /// </summary>
        public string QuestTitle { get; set; } = string.Empty;

        /// <summary>
        /// Condition type: "Complete", "Accept", or "Fail".
        /// </summary>
        public string RequirementType { get; set; } = "Complete";

        /// <summary>
        /// Delay time in minutes before quest unlocks (e.g. 1440 for 24hr).
        /// </summary>
        public int? DelayMinutes { get; set; }

        /// <summary>
        /// Group ID for OR prerequisites. GroupId = 0 is AND; GroupId > 0 groups alternatives.
        /// </summary>
        public int GroupId { get; set; }
    }

    /// <summary>
    /// Raw item requirement extracted from "Related Quest Items" table.
    /// </summary>
    public class RawWikiRequiredItem
    {
        /// <summary>
        /// 24-character hex BSG item ID if available via template, else null.
        /// </summary>
        public string? ItemId { get; set; }

        /// <summary>
        /// Clean display name of the item.
        /// </summary>
        public string ItemName { get; set; } = string.Empty;

        /// <summary>
        /// Required quantity.
        /// </summary>
        public int Count { get; set; } = 1;

        /// <summary>
        /// Whether the item must be Found in Raid (FIR).
        /// </summary>
        public bool RequiresFIR { get; set; }

        /// <summary>
        /// Requirement type: "Required", "Handover", "Optional".
        /// </summary>
        public string RequirementType { get; set; } = "Required";

        /// <summary>
        /// Minimum dogtag level (for dogtag items).
        /// </summary>
        public int? DogtagMinLevel { get; set; }

        /// <summary>
        /// Dogtag faction restriction ("BEAR" or "USEC").
        /// </summary>
        public string? DogtagFaction { get; set; }

        /// <summary>
        /// Order index inside the table.
        /// </summary>
        public int SortOrder { get; set; }
    }

    /// <summary>
    /// Raw objective extracted from wikitext ==Objectives== section.
    /// </summary>
    public class RawWikiObjective
    {
        public string Description { get; set; } = string.Empty;
        public string? MapName { get; set; }
        public bool RequiresFIR { get; set; }
        public string? TargetType { get; set; }
        public int? TargetCount { get; set; }
        public string? ObjectiveType { get; set; }
        public int SortOrder { get; set; }
    }

    /// <summary>
    /// Community data payload aggregating community quests, hideout, and items.
    /// </summary>
    public class CommunityDataPayload
    {
        /// <summary>
        /// Source identifier (e.g., "tarkov.dev GraphQL", "TarkovTracker GitHub CDN").
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp when data was retrieved.
        /// </summary>
        public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Community quests list.
        /// </summary>
        public List<CommunityQuest> Quests { get; set; } = new();

        /// <summary>
        /// Community hideout stations list.
        /// </summary>
        public List<CommunityHideoutStation> HideoutStations { get; set; } = new();

        /// <summary>
        /// Item lookup by BSG item GUID.
        /// </summary>
        public Dictionary<string, CommunityItemInfo> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Community quest transfer object.
    /// </summary>
    public class CommunityQuest
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("tarkovDataId")]
        public int? TarkovDataId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("normalizedName")]
        public string? NormalizedName { get; set; }

        [JsonPropertyName("wikiLink")]
        public string? WikiLink { get; set; }

        [JsonPropertyName("trader")]
        public string? Trader { get; set; }

        [JsonPropertyName("minPlayerLevel")]
        public int? MinPlayerLevel { get; set; }

        [JsonPropertyName("faction")]
        public string? Faction { get; set; }

        [JsonPropertyName("nameKo")]
        public string? NameKo { get; set; }

        [JsonPropertyName("nameJa")]
        public string? NameJa { get; set; }

        public List<CommunityQuestRequirement> Requirements { get; set; } = new();
        public List<CommunityQuestItemRequirement> ItemRequirements { get; set; } = new();
        public List<CommunityQuestTraderRequirement> TraderRequirements { get; set; } = new();
        public List<CommunityQuestPoolRequirement> PoolRequirements { get; set; } = new();
    }

    public class CommunityQuestTraderRequirement
    {
        public string TraderId { get; set; } = string.Empty;
        public string TraderName { get; set; } = string.Empty;
        public string RequirementType { get; set; } = "level";
        public string CompareMethod { get; set; } = ">=";
        public int Value { get; set; }
    }

    public class CommunityQuestPoolRequirement
    {
        public string PoolTrader { get; set; } = string.Empty;
        public int? PoolTraderLL { get; set; }
        public int RequiredCompletedCount { get; set; }
        public string? Description { get; set; }
    }

    public class CommunityQuestRequirement
    {
        public string? RequiredQuestId { get; set; }
        public string? RequiredQuestName { get; set; }
        public string Status { get; set; } = "complete";
        public int GroupId { get; set; }
    }

    public class CommunityQuestItemRequirement
    {
        public string? ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int Count { get; set; } = 1;
        public bool FoundInRaid { get; set; }
        public string RequirementType { get; set; } = "Required";
    }

    /// <summary>
    /// Community hideout station transfer object.
    /// </summary>
    public class CommunityHideoutStation
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? NormalizedName { get; set; }
        public string? NameKo { get; set; }
        public string? NameJa { get; set; }
        public string? ImageLink { get; set; }
        public List<CommunityHideoutLevel> Levels { get; set; } = new();
    }

    public class CommunityHideoutLevel
    {
        public int Level { get; set; }
        public int ConstructionTime { get; set; }
        public List<CommunityHideoutItemRequirement> ItemRequirements { get; set; } = new();
        public List<CommunityHideoutStationRequirement> StationRequirements { get; set; } = new();
        public List<CommunityHideoutTraderRequirement> TraderRequirements { get; set; } = new();
        public List<CommunityHideoutSkillRequirement> SkillRequirements { get; set; } = new();
    }

    public class CommunityHideoutItemRequirement
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string? ItemNameKo { get; set; }
        public string? ItemNameJa { get; set; }
        public string? IconLink { get; set; }
        public int Count { get; set; }
        public bool FoundInRaid { get; set; }
    }

    public class CommunityHideoutStationRequirement
    {
        public string StationId { get; set; } = string.Empty;
        public string StationName { get; set; } = string.Empty;
        public int Level { get; set; }
    }

    public class CommunityHideoutTraderRequirement
    {
        public string TraderId { get; set; } = string.Empty;
        public string TraderName { get; set; } = string.Empty;
        public int Level { get; set; }
    }

    public class CommunityHideoutSkillRequirement
    {
        public string SkillName { get; set; } = string.Empty;
        public int Level { get; set; }
    }

    public class CommunityItemInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? ShortName { get; set; }
        public string? NormalizedName { get; set; }
        public string? IconLink { get; set; }
    }
}
