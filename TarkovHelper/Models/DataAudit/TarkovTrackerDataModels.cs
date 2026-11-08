using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TarkovHelper.Models.DataAudit
{
    #region Raw Aggregator Model

    /// <summary>
    /// Encapsulates raw extracted data from TarkovTracker API (tasks-core and tasks-objectives)
    /// or fallback to json.tarkov.dev/regular/tasks.
    /// </summary>
    public class RawTarkovTrackerData
    {
        public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Source identifier: e.g. "TarkovTracker API" or "json.tarkov.dev Fallback"
        /// </summary>
        public string Source { get; set; } = "TarkovTracker API";

        public List<TarkovTrackerTask> Tasks { get; set; } = new();
        public List<TarkovTrackerTrader> Traders { get; set; } = new();
        public List<TarkovTrackerMap> Maps { get; set; } = new();

        /// <summary>
        /// Detailed objectives mapped by BSG Task ID (extracted from tasks-objectives endpoint).
        /// </summary>
        public Dictionary<string, List<TarkovTrackerObjective>> ObjectivesByTaskId { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public TarkovTrackerDataOverlay? OverlayMetadata { get; set; }
    }

    #endregion

    #region API Root Responses

    /// <summary>
    /// Root JSON envelope from https://tarkovtracker.org/api/tarkov/tasks-core?lang=en&gameMode=regular
    /// </summary>
    public class TarkovTrackerCoreResponse
    {
        [JsonPropertyName("data")]
        public TarkovTrackerCoreData? Data { get; set; }

        [JsonPropertyName("dataOverlay")]
        public TarkovTrackerDataOverlay? DataOverlay { get; set; }
    }

    public class TarkovTrackerCoreData
    {
        [JsonPropertyName("tasks")]
        public List<TarkovTrackerTask> Tasks { get; set; } = new();

        [JsonPropertyName("traders")]
        public List<TarkovTrackerTrader> Traders { get; set; } = new();

        [JsonPropertyName("maps")]
        public List<TarkovTrackerMap> Maps { get; set; } = new();
    }

    public class TarkovTrackerDataOverlay
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("version")]
        public JsonElement? Version { get; set; }

        public string? VersionString => Version.HasValue ? Version.Value.ToString() : null;

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }

        [JsonPropertyName("generated")]
        public string? Generated { get; set; }
    }

    /// <summary>
    /// Root JSON envelope from https://tarkovtracker.org/api/tarkov/tasks-objectives?lang=en&gameMode=regular
    /// </summary>
    public class TarkovTrackerObjectivesResponse
    {
        [JsonPropertyName("data")]
        public TarkovTrackerObjectivesData? Data { get; set; }

        [JsonPropertyName("dataOverlay")]
        public TarkovTrackerDataOverlay? DataOverlay { get; set; }
    }

    public class TarkovTrackerObjectivesData
    {
        [JsonPropertyName("tasks")]
        public List<TarkovTrackerTaskObjectivesEntry> Tasks { get; set; } = new();
    }

    public class TarkovTrackerTaskObjectivesEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("objectives")]
        public List<TarkovTrackerObjective> Objectives { get; set; } = new();
    }

    #endregion

    #region Core Entities

    public class TarkovTrackerTrader
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("normalizedName")]
        public string? NormalizedName { get; set; }

        [JsonPropertyName("imageLink")]
        public string? ImageLink { get; set; }
    }

    [JsonConverter(typeof(TarkovTrackerTraderRefConverter))]
    public class TarkovTrackerTraderRef
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("normalizedName")]
        public string? NormalizedName { get; set; }

        [JsonPropertyName("imageLink")]
        public string? ImageLink { get; set; }
    }

    public class TarkovTrackerMap
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("normalizedName")]
        public string? NormalizedName { get; set; }

        [JsonPropertyName("wiki")]
        public string? Wiki { get; set; }
    }

    [JsonConverter(typeof(TarkovTrackerMapRefConverter))]
    public class TarkovTrackerMapRef
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class TarkovTrackerTask
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("normalizedName")]
        public string? NormalizedName { get; set; }

        [JsonPropertyName("trader")]
        public TarkovTrackerTraderRef? Trader { get; set; }

        [JsonPropertyName("map")]
        public TarkovTrackerMapRef? Map { get; set; }

        [JsonPropertyName("maps")]
        public List<TarkovTrackerMapRef>? Maps { get; set; }

        [JsonPropertyName("kappaRequired")]
        public bool KappaRequired { get; set; }

        [JsonPropertyName("lightkeeperRequired")]
        public bool LightkeeperRequired { get; set; }

        [JsonPropertyName("experience")]
        public int? Experience { get; set; }

        [JsonPropertyName("wikiLink")]
        public string? WikiLink { get; set; }

        [JsonPropertyName("minPlayerLevel")]
        public int? MinPlayerLevel { get; set; }

        [JsonPropertyName("factionName")]
        public string? FactionName { get; set; }

        [JsonPropertyName("taskRequirements")]
        public List<TarkovTrackerTaskRequirement> TaskRequirements { get; set; } = new();

        [JsonPropertyName("otherRequirements")]
        public List<TarkovTrackerOtherRequirement> OtherRequirements { get; set; } = new();

        [JsonPropertyName("normalizedTraderRequirements")]
        public List<TarkovTrackerTraderRequirement> NormalizedTraderRequirements { get; set; } = new();

        [JsonPropertyName("traderLevelRequirements")]
        public List<TarkovTrackerTraderRequirement> TraderLevelRequirements { get; set; } = new();

        [JsonPropertyName("objectives")]
        public List<TarkovTrackerObjective> Objectives { get; set; } = new();

        [JsonPropertyName("restartable")]
        public bool Restartable { get; set; }

        [JsonPropertyName("traderTaskGroup")]
        public int? TraderTaskGroup { get; set; }

        [JsonPropertyName("neededKeys")]
        public List<TarkovTrackerNeededKeyGroup>? NeededKeys { get; set; }
    }

    #endregion

    #region Requirement Entities

    public class TarkovTrackerTaskRequirement
    {
        [JsonPropertyName("task")]
        public TarkovTrackerTaskRef? Task { get; set; }

        [JsonPropertyName("status")]
        public List<string>? Status { get; set; }
    }

    [JsonConverter(typeof(TarkovTrackerTaskRefConverter))]
    public class TarkovTrackerTaskRef
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("wikiLink")]
        public string? WikiLink { get; set; }
    }

    public class TarkovTrackerTraderRequirement
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("requirementType")]
        public string? RequirementType { get; set; }

        [JsonPropertyName("compareMethod")]
        public string? CompareMethod { get; set; }

        [JsonPropertyName("value")]
        public double Value { get; set; }

        [JsonPropertyName("level")]
        public int? Level { get; set; }

        [JsonPropertyName("trader")]
        public TarkovTrackerTraderRef? Trader { get; set; }
    }

    public class TarkovTrackerOtherRequirement
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("variableId")]
        public string? VariableId { get; set; }

        [JsonPropertyName("compareMethod")]
        public string? CompareMethod { get; set; }

        [JsonPropertyName("value")]
        public int? Value { get; set; }

        [JsonPropertyName("taskPool")]
        public TarkovTrackerTaskPool? TaskPool { get; set; }

        [JsonPropertyName("counter")]
        public TarkovTrackerCounter? Counter { get; set; }

        [JsonPropertyName("storyChapter")]
        public TarkovTrackerStoryChapterRef? StoryChapter { get; set; }

        [JsonPropertyName("objective")]
        public TarkovTrackerStoryObjectiveRef? Objective { get; set; }
    }

    public class TarkovTrackerTaskPool
    {
        [JsonPropertyName("traderId")]
        public string? TraderId { get; set; }

        [JsonPropertyName("loyaltyLevel")]
        public int? LoyaltyLevel { get; set; }

        [JsonPropertyName("verification")]
        public string? Verification { get; set; }

        [JsonPropertyName("coverage")]
        public string? Coverage { get; set; }

        [JsonPropertyName("derivation")]
        public TarkovTrackerDerivation? Derivation { get; set; }
    }

    public class TarkovTrackerDerivation
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("taskIds")]
        public List<string> TaskIds { get; set; } = new();
    }

    public class TarkovTrackerCounter
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("taskIds")]
        public List<string> TaskIds { get; set; } = new();
    }

    public class TarkovTrackerStoryChapterRef
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class TarkovTrackerStoryObjectiveRef
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    #endregion

    #region Objectives & Geometry Entities

    public class TarkovTrackerObjective
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("count")]
        public int? Count { get; set; }

        [JsonPropertyName("optional")]
        public bool Optional { get; set; }

        [JsonPropertyName("foundInRaid")]
        public bool FoundInRaid { get; set; }

        [JsonPropertyName("items")]
        public List<TarkovTrackerItemRef> Items { get; set; } = new();

        [JsonPropertyName("zones")]
        public List<TarkovTrackerZone> Zones { get; set; } = new();

        [JsonPropertyName("maps")]
        public List<TarkovTrackerMapRef> Maps { get; set; } = new();

        [JsonPropertyName("dogTagLevel")]
        public int? DogTagLevel { get; set; }

        [JsonPropertyName("maxDurability")]
        public double? MaxDurability { get; set; }

        [JsonPropertyName("minDurability")]
        public double? MinDurability { get; set; }

        [JsonPropertyName("requiredKeys")]
        public List<List<TarkovTrackerKeyRef>>? RequiredKeys { get; set; }

        [JsonPropertyName("possibleLocations")]
        public List<TarkovTrackerPossibleLocation> PossibleLocations { get; set; } = new();
    }

    public class TarkovTrackerPossibleLocation
    {
        [JsonPropertyName("map")]
        public TarkovTrackerMapRef? Map { get; set; }

        [JsonPropertyName("positions")]
        public List<TarkovTrackerPosition3D> Positions { get; set; } = new();
    }

    [JsonConverter(typeof(TarkovTrackerItemRefConverter))]
    public class TarkovTrackerItemRef
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    [JsonConverter(typeof(TarkovTrackerKeyRefConverter))]
    public class TarkovTrackerKeyRef
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("shortName")]
        public string? ShortName { get; set; }
    }

    public class TarkovTrackerNeededKeyGroup
    {
        [JsonPropertyName("map")]
        public TarkovTrackerMapRef? Map { get; set; }

        [JsonPropertyName("keys")]
        public List<TarkovTrackerKeyRef> Keys { get; set; } = new();
    }

    public class TarkovTrackerZone
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("map")]
        public TarkovTrackerMapRef? Map { get; set; }

        [JsonPropertyName("position")]
        public TarkovTrackerPosition3D? Position { get; set; }

        [JsonPropertyName("size")]
        public TarkovTrackerPosition3D? Size { get; set; }

        [JsonPropertyName("outline")]
        public List<TarkovTrackerPosition3D> Outline { get; set; } = new();

        [JsonPropertyName("top")]
        public double? Top { get; set; }

        [JsonPropertyName("bottom")]
        public double? Bottom { get; set; }

        [JsonPropertyName("terrainElevation")]
        public double? TerrainElevation { get; set; }
    }

    public class TarkovTrackerPosition3D
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("z")]
        public double Z { get; set; }
    }

    #endregion

    #region Custom JSON Converters for Polymorphic String/Object References

    public class TarkovTrackerTraderRefConverter : JsonConverter<TarkovTrackerTraderRef>
    {
        public override TarkovTrackerTraderRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.String)
            {
                return new TarkovTrackerTraderRef { Id = reader.GetString() ?? string.Empty };
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                var root = doc.RootElement;
                var refObj = new TarkovTrackerTraderRef();
                if (root.TryGetProperty("id", out var idProp)) refObj.Id = idProp.GetString() ?? string.Empty;
                if (root.TryGetProperty("name", out var nameProp)) refObj.Name = nameProp.GetString();
                if (root.TryGetProperty("normalizedName", out var normProp)) refObj.NormalizedName = normProp.GetString();
                if (root.TryGetProperty("imageLink", out var imgProp)) refObj.ImageLink = imgProp.GetString();
                return refObj;
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, TarkovTrackerTraderRef value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            if (value.Name != null) writer.WriteString("name", value.Name);
            if (value.NormalizedName != null) writer.WriteString("normalizedName", value.NormalizedName);
            if (value.ImageLink != null) writer.WriteString("imageLink", value.ImageLink);
            writer.WriteEndObject();
        }
    }

    public class TarkovTrackerTaskRefConverter : JsonConverter<TarkovTrackerTaskRef>
    {
        public override TarkovTrackerTaskRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.String)
            {
                return new TarkovTrackerTaskRef { Id = reader.GetString() ?? string.Empty };
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                var root = doc.RootElement;
                var refObj = new TarkovTrackerTaskRef();
                if (root.TryGetProperty("id", out var idProp)) refObj.Id = idProp.GetString() ?? string.Empty;
                if (root.TryGetProperty("name", out var nameProp)) refObj.Name = nameProp.GetString();
                if (root.TryGetProperty("wikiLink", out var wikiProp)) refObj.WikiLink = wikiProp.GetString();
                return refObj;
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, TarkovTrackerTaskRef value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            if (value.Name != null) writer.WriteString("name", value.Name);
            if (value.WikiLink != null) writer.WriteString("wikiLink", value.WikiLink);
            writer.WriteEndObject();
        }
    }

    public class TarkovTrackerMapRefConverter : JsonConverter<TarkovTrackerMapRef>
    {
        public override TarkovTrackerMapRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.String)
            {
                return new TarkovTrackerMapRef { Id = reader.GetString() ?? string.Empty };
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                var root = doc.RootElement;
                var refObj = new TarkovTrackerMapRef();
                if (root.TryGetProperty("id", out var idProp)) refObj.Id = idProp.GetString() ?? string.Empty;
                if (root.TryGetProperty("name", out var nameProp)) refObj.Name = nameProp.GetString();
                return refObj;
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, TarkovTrackerMapRef value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            if (value.Name != null) writer.WriteString("name", value.Name);
            writer.WriteEndObject();
        }
    }

    public class TarkovTrackerItemRefConverter : JsonConverter<TarkovTrackerItemRef>
    {
        public override TarkovTrackerItemRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.String)
            {
                return new TarkovTrackerItemRef { Id = reader.GetString() ?? string.Empty };
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                var root = doc.RootElement;
                var refObj = new TarkovTrackerItemRef();
                if (root.TryGetProperty("id", out var idProp)) refObj.Id = idProp.GetString() ?? string.Empty;
                if (root.TryGetProperty("name", out var nameProp)) refObj.Name = nameProp.GetString();
                return refObj;
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, TarkovTrackerItemRef value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            if (value.Name != null) writer.WriteString("name", value.Name);
            writer.WriteEndObject();
        }
    }

    public class TarkovTrackerKeyRefConverter : JsonConverter<TarkovTrackerKeyRef>
    {
        public override TarkovTrackerKeyRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.String)
            {
                return new TarkovTrackerKeyRef { Id = reader.GetString() ?? string.Empty };
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                var root = doc.RootElement;
                var refObj = new TarkovTrackerKeyRef();
                if (root.TryGetProperty("id", out var idProp)) refObj.Id = idProp.GetString() ?? string.Empty;
                if (root.TryGetProperty("name", out var nameProp)) refObj.Name = nameProp.GetString();
                if (root.TryGetProperty("shortName", out var shortProp)) refObj.ShortName = shortProp.GetString();
                return refObj;
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, TarkovTrackerKeyRef value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            if (value.Name != null) writer.WriteString("name", value.Name);
            if (value.ShortName != null) writer.WriteString("shortName", value.ShortName);
            writer.WriteEndObject();
        }
    }

    #endregion
}
