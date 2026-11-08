namespace TarkovHelper.Models;

/// <summary>
/// Map marker types
/// </summary>
public enum MarkerType
{
    PmcSpawn,
    ScavSpawn,
    PmcExtraction,
    ScavExtraction,
    SharedExtraction,
    Transit,
    BossSpawn,
    RaiderSpawn,
    Lever,
    Keys,
    // DB에 존재하는 추가 타입
    CultistSpawn,
    SniperScavSpawn,
    RogueSpawn
}

/// <summary>
/// Map marker model for locations on the map
/// </summary>
public class MapMarker
{
    /// <summary>
    /// Unique marker ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Marker name (English)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Marker name (Korean)
    /// </summary>
    public string? NameKo { get; set; }

    /// <summary>
    /// Marker name (Japanese)
    /// </summary>
    public string? NameJa { get; set; }

    /// <summary>
    /// Marker type
    /// </summary>
    public MarkerType Type { get; set; }

    /// <summary>
    /// Map key this marker belongs to
    /// </summary>
    public string MapKey { get; set; } = string.Empty;

    /// <summary>
    /// Game X coordinate
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Game Y coordinate (height)
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Game Z coordinate
    /// </summary>
    public double Z { get; set; }

    /// <summary>
    /// Floor ID for multi-floor maps
    /// </summary>
    public string? FloorId { get; set; }

    /// <summary>
    /// Get icon filename for this marker type
    /// </summary>
    public string GetIconFileName()
    {
        return Type switch
        {
            MarkerType.PmcSpawn => "PMC Spawn.webp",
            MarkerType.ScavSpawn => "SCAV Spawn.webp",
            MarkerType.PmcExtraction => "PMC Extraction.webp",
            MarkerType.ScavExtraction => "SCAV Extraction.webp",
            MarkerType.SharedExtraction => "PMC Extraction.webp",
            MarkerType.Transit => "Transit.webp",
            MarkerType.BossSpawn => "BOSS Spawn.webp",
            MarkerType.RaiderSpawn => "Raider Spawn.webp",
            MarkerType.Lever => "Lever.webp",
            MarkerType.Keys => "Keys.webp",
            MarkerType.CultistSpawn => "Cultist.svg",
            MarkerType.SniperScavSpawn => "SniperScav.svg",
            MarkerType.RogueSpawn => "Rogue.svg",
            _ => "PMC Spawn.webp"
        };
    }

    /// <summary>
    /// Get SVG icon filename for this marker type (used in Map Markers overlay).
    /// Returns null if no SVG icon available.
    /// </summary>
    public static string? GetSvgIconFileName(MarkerType type)
    {
        return type switch
        {
            MarkerType.PmcSpawn => "PMC Spawn.svg",
            MarkerType.SniperScavSpawn => "SniperScav.svg",
            MarkerType.RogueSpawn => "Rogue.svg",
            MarkerType.CultistSpawn => "Cultist.svg",
            MarkerType.Lever => "Lever.svg",
            MarkerType.BossSpawn => "Boss.svg",
            _ => null
        };
    }

    /// <summary>
    /// Check if this is an extraction marker
    /// </summary>
    public bool IsExtraction => Type is MarkerType.PmcExtraction or MarkerType.ScavExtraction or MarkerType.SharedExtraction;

    /// <summary>
    /// Check if this is a spawn marker
    /// </summary>
    public bool IsSpawn => Type is MarkerType.PmcSpawn or MarkerType.ScavSpawn or MarkerType.BossSpawn or MarkerType.RaiderSpawn;

    /// <summary>
    /// Parse MarkerType from string
    /// </summary>
    public static MarkerType ParseType(string typeStr)
    {
        return typeStr switch
        {
            "PmcSpawn" => MarkerType.PmcSpawn,
            "ScavSpawn" => MarkerType.ScavSpawn,
            "PmcExtraction" => MarkerType.PmcExtraction,
            "ScavExtraction" => MarkerType.ScavExtraction,
            "SharedExtraction" => MarkerType.SharedExtraction,
            "Transit" => MarkerType.Transit,
            "BossSpawn" => MarkerType.BossSpawn,
            "RaiderSpawn" => MarkerType.RaiderSpawn,
            "Lever" => MarkerType.Lever,
            "Keys" => MarkerType.Keys,
            "CultistSpawn" => MarkerType.CultistSpawn,
            "SniperScavSpawn" => MarkerType.SniperScavSpawn,
            "RogueSpawn" => MarkerType.RogueSpawn,
            _ => MarkerType.PmcSpawn
        };
    }

    /// <summary>
    /// Get marker color as RGB tuple for the given marker type
    /// </summary>
    public static (byte R, byte G, byte B) GetMarkerColor(MarkerType type)
    {
        return type switch
        {
            MarkerType.PmcSpawn => (0x42, 0xA5, 0xF5),         // Light Blue / Sky Blue (#42A5F5)
            MarkerType.ScavSpawn => (0xFF, 0xB3, 0x00),        // Amber (#FFB300)
            MarkerType.PmcExtraction => (0x00, 0xE6, 0x76),    // Vibrant Emerald Green (#00E676)
            MarkerType.ScavExtraction => (0xFF, 0x98, 0x00),   // Orange (#FF9800)
            MarkerType.SharedExtraction => (0x00, 0xE5, 0xFF), // Cyan (#00E5FF)
            MarkerType.Transit => (0xAB, 0x47, 0xBC),          // Purple (#AB47BC)
            MarkerType.BossSpawn => (0xF4, 0x43, 0x36),        // Red (#F44336)
            MarkerType.RaiderSpawn => (0xD8, 0x1B, 0x60),      // Pink/Magenta (#D81B60)
            MarkerType.Lever => (0x00, 0xE5, 0xFF),            // Cyan (#00E5FF)
            MarkerType.Keys => (0xFF, 0xEB, 0x3B),             // Yellow (#FFEB3B)
            MarkerType.CultistSpawn => (0x7B, 0x1F, 0xA2),     // Deep Purple (#7B1FA2)
            MarkerType.SniperScavSpawn => (0x9E, 0x9E, 0x9E),   // Gray (#9E9E9E)
            MarkerType.RogueSpawn => (0xE5, 0x39, 0x35),       // Crimson Red (#E53935)
            _ => (0x9E, 0x9E, 0x9E)                            // Grey
        };
    }
}
