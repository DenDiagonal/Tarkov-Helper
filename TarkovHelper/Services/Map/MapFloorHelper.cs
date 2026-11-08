using System;
using System.Linq;
using System.Windows.Media;
using TarkovHelper.Models.Map;

namespace TarkovHelper.Services.Map;

/// <summary>
/// Helper for map floor identification, elevation calculations, and visual indicators.
/// </summary>
public static class MapFloorHelper
{
    /// <summary>
    /// Checks whether a marker is located on the currently active floor.
    /// If currentFloorId is null or empty, single-floor or no floor selection is active, so all markers are considered on current floor.
    /// If markerFloorId is null or empty, it defaults to "main".
    /// </summary>
    public static bool IsMarkerOnCurrentFloor(string? currentFloorId, string? markerFloorId)
    {
        if (string.IsNullOrEmpty(currentFloorId))
            return true;

        var effectiveMarkerFloorId = string.IsNullOrEmpty(markerFloorId) ? "main" : markerFloorId;
        return string.Equals(currentFloorId, effectiveMarkerFloorId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Computes the relative elevation arrow, floor display label, and theme color for a marker relative to the current floor.
    /// Returns null if on the same floor or if floor ordering cannot be resolved.
    /// </summary>
    public static (string arrow, string floorText, Color color)? GetFloorIndicator(
        MapConfig? config,
        string? currentFloorId,
        string? markerFloorId)
    {
        if (config?.Floors == null || config.Floors.Count == 0 || string.IsNullOrEmpty(currentFloorId))
            return null;

        var currentFloor = config.Floors.FirstOrDefault(f =>
            string.Equals(f.LayerId, currentFloorId, StringComparison.OrdinalIgnoreCase));
        var currentOrder = currentFloor?.Order ?? 0;

        var effectiveMarkerFloorId = string.IsNullOrEmpty(markerFloorId) ? "main" : markerFloorId;
        var markerFloor = config.Floors.FirstOrDefault(f =>
            string.Equals(f.LayerId, effectiveMarkerFloorId, StringComparison.OrdinalIgnoreCase));
        var markerOrder = markerFloor?.Order ?? 0;

        if (currentOrder == markerOrder)
            return null;

        var isAbove = markerOrder > currentOrder;
        var arrow = isAbove ? "↑" : "↓";
        var color = isAbove
            ? Color.FromRgb(100, 181, 246)  // Sky Blue (#64B5F6) for higher floors
            : Color.FromRgb(255, 167, 38);   // Amber (#FFA726) for lower floors

        string floorText = markerOrder < 0 ? "B" : (markerOrder == 0 ? "G" : $"{markerOrder + 1}F");
        return (arrow, floorText, color);
    }

    /// <summary>
    /// Gets the compact floor display code: "B" for basement/bunker, "1" for ground/main, "2" for level 2, "3" for level 3, etc.
    /// </summary>
    public static string GetFloorShortCode(MapFloorConfig? floor, string? displayName = null, string? layerId = null)
    {
        if (floor != null)
        {
            if (floor.Order < 0) return "B";
            if (floor.Order == 0) return "1";
            return (floor.Order + 1).ToString();
        }

        var name = (displayName ?? layerId ?? "").ToLowerInvariant();
        if (name.Contains("basement") || name.Contains("bunker") || name.Contains("underground"))
            return "B";
        if (name.Contains("ground") || name.Contains("main") || name.Contains("level 1") || name.Contains("first"))
            return "1";
        if (name.Contains("level 2") || name.Contains("second") || name.Contains("level2"))
            return "2";
        if (name.Contains("level 3") || name.Contains("third") || name.Contains("level3"))
            return "3";
        if (name.Contains("level 4") || name.Contains("fourth") || name.Contains("level4"))
            return "4";
        if (name.Contains("level 5") || name.Contains("fifth") || name.Contains("level5"))
            return "5";
        if (name.Contains("level 6") || name.Contains("sixth") || name.Contains("level6"))
            return "6";

        return "1";
    }
}
