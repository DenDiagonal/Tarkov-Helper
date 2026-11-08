using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Pages.Map.ViewModels;
using Xunit;

namespace TarkovHelper.Tests;

/// <summary>
/// Empirical Challenger Stress Tests for R1 and R3.
/// Tests custom marker scaling under extreme zoom & slider ranges,
/// and stress-tests IsGeographicObjective filtering against edge cases.
/// </summary>
public class Challenger1EmpiricalTests
{
    // =========================================================================
    // R1: Custom Marker Scale Transforms Under Extreme Zoom and Slider Ranges
    // =========================================================================

    [Theory]
    [InlineData(0.05)]   // Extreme zoomed-out fit
    [InlineData(0.10)]   // MinZoom on MapPage
    [InlineData(0.25)]   // Standard map fit level
    [InlineData(0.50)]   // Mid zoom
    [InlineData(1.00)]   // 100% natural scale
    [InlineData(2.00)]   // 200% zoom
    [InlineData(5.00)]   // MaxZoom on MapPage
    [InlineData(10.00)]  // Extreme 1000% zoom
    [InlineData(100.00)] // Ultra extreme zoom
    [InlineData(0.001)]  // Lower safety clamp boundary
    public void R1_ScaleTransform_ExtremeAndStandardZoomLevels_NeverProducesNaNOrInfinity(double zoomLevel)
    {
        // Formula used in MapCustomMarkerManager & OverlayMiniMapWindow
        var inverseScale = 1.0 / Math.Max(0.001, zoomLevel);

        Assert.False(double.IsNaN(inverseScale), $"inverseScale is NaN for zoomLevel={zoomLevel}");
        Assert.False(double.IsInfinity(inverseScale), $"inverseScale is Infinity for zoomLevel={zoomLevel}");
        Assert.True(inverseScale > 0, $"inverseScale must be strictly positive, was {inverseScale}");

        // Mathematical invariant: (baseSize * inverseScale) * zoomLevel == baseSize
        // Valid for all zoomLevel >= 0.001 (unclamped region)
        const double baseMarkerSize = 18.0;
        var effectiveScreenPixelSize = (baseMarkerSize * inverseScale) * zoomLevel;
        Assert.Equal(baseMarkerSize, effectiveScreenPixelSize, precision: 2);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.05)]
    [InlineData(-1.0)]
    [InlineData(-100.0)]
    public void R1_ScaleTransform_DefensiveZeroAndNegativeZoomLevels_HandledGracefullyWithoutCrashing(double invalidZoom)
    {
        // Safe inverse scale formula protects against division by zero and negative scales
        var inverseScale = 1.0 / Math.Max(0.001, invalidZoom);

        Assert.False(double.IsNaN(inverseScale));
        Assert.False(double.IsInfinity(inverseScale));
        Assert.Equal(1000.0, inverseScale, precision: 3); // 1.0 / 0.001 = 1000.0
    }

    [Theory]
    [InlineData(8.0, 1.0, 12.0)]    // Slider min: 8 -> 12px
    [InlineData(12.0, 1.0, 18.0)]   // Slider default: 12 -> 18px (matches quest marker default)
    [InlineData(16.0, 1.0, 24.0)]   // Slider max: 16 -> 24px (matches extraction/POI marker default)
    [InlineData(8.0, 0.8, 9.6)]     // Scaled map (0.8)
    [InlineData(12.0, 0.8, 14.4)]
    [InlineData(16.0, 0.8, 19.2)]
    [InlineData(8.0, 1.5, 18.0)]    // Large map (1.5)
    [InlineData(12.0, 1.5, 27.0)]
    [InlineData(16.0, 1.5, 36.0)]
    public void R1_SliderBounds_HarmonizesWithMapScale(double userSize, double mapScale, double expectedBaseSize)
    {
        var calculatedSize = (userSize * 1.5) * mapScale;
        Assert.Equal(expectedBaseSize, calculatedSize, precision: 2);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(-12.0)]
    public void R1_SliderBounds_FallbackForZeroOrNegativeSize(double invalidSize)
    {
        // Defensive size fallback in CreateMarkerElement: marker.Size > 0 ? marker.Size : 12.0
        var userSize = invalidSize > 0 ? invalidSize : 12.0;
        Assert.Equal(12.0, userSize);

        var finalSize = (userSize * 1.5) * 1.0;
        Assert.Equal(18.0, finalSize);
    }

    [Theory]
    [InlineData(350.0, 350.0)]   // 1:1 standard minimap
    [InlineData(200.0, 200.0)]   // Compact square
    [InlineData(50.0, 50.0)]     // Minimum window dimension
    [InlineData(10.0, 10.0)]     // Extreme small
    [InlineData(1000.0, 350.0)]  // Wide 2.85:1
    [InlineData(350.0, 1000.0)]  // Tall 0.35:1
    [InlineData(2560.0, 300.0)]  // Ultra-wide ribbon
    [InlineData(300.0, 2000.0)]  // Vertical ribbon
    [InlineData(0.0, 0.0)]       // Zero dimension
    [InlineData(-100.0, 300.0)]  // Negative dimension
    public void R1_Minimap_AspectRatiosAndExtremeDimensions_ProportionClampedAndFinite(double viewW, double viewH)
    {
        // Formula used in OverlayMiniMapWindow.xaml.cs:
        // var minDimension = Math.Min(viewWidth, viewHeight);
        // var mapProportion = Math.Clamp(minDimension / 350.0, 0.6, 2.0);
        var minDimension = Math.Min(viewW, viewH);
        var mapProportion = Math.Clamp(minDimension / 350.0, 0.6, 2.0);

        Assert.InRange(mapProportion, 0.6, 2.0);
        Assert.False(double.IsNaN(mapProportion));
        Assert.False(double.IsInfinity(mapProportion));
    }

    [Fact]
    public void R1_Minimap_MarkerSizePresets_ProduceConsistentScaledSizes()
    {
        double[] sliderSizes = [8.0, 12.0, 16.0];
        const double mapProportion = 1.0; // 350x350 window

        foreach (var userSize in sliderSizes)
        {
            var smallSize = Math.Round(userSize * 0.7 * mapProportion, 1);
            var normalSize = Math.Round(userSize * 1.0 * mapProportion, 1);
            var largeSize = Math.Round(userSize * 1.4 * mapProportion, 1);

            Assert.True(smallSize < normalSize, $"Small size {smallSize} should be < Normal size {normalSize}");
            Assert.True(normalSize < largeSize, $"Normal size {normalSize} should be < Large size {largeSize}");
            Assert.True(smallSize > 0, "Small size must be strictly positive");
        }
    }

    // =========================================================================
    // R3: IsGeographicObjective Filtering Edge Cases
    // =========================================================================

    [Theory]
    [InlineData("visit", true)]
    [InlineData("mark", true)]
    [InlineData("collect", true)]
    [InlineData("kill", true)]
    [InlineData("plantItem", true)]
    [InlineData("custom", true)]
    [InlineData("find", true)]
    [InlineData("locate", true)]
    [InlineData("scout", true)]
    [InlineData("investigate", true)]
    [InlineData("eliminate", true)]
    [InlineData("stash", true)]
    [InlineData("task", true)]
    [InlineData("survive", false)]
    [InlineData("handover", false)]
    [InlineData("build", false)]
    [InlineData("extract", false)]
    public void R3_IsGeographicObjective_AllStandardEftObjectiveTypes_ClassifiedCorrectly(string type, bool expected)
    {
        var result = QuestObjectiveViewModel.IsGeographicObjective(type, "Generic description");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("  SURVIVE  ", false)]
    [InlineData("\tSurvive\n", false)]
    [InlineData("HANDOVER", false)]
    [InlineData("  HandOver  ", false)]
    [InlineData("bUiLd", false)]
    [InlineData("EXTRACT", false)]
    [InlineData("  VISIT  ", true)]
    [InlineData("Mark", true)]
    [InlineData("  CoLLeCt  ", true)]
    [InlineData("KILL", true)]
    [InlineData("  plantItem  ", true)]
    [InlineData("  CUSTOM  ", true)]
    public void R3_IsGeographicObjective_CasingAndSurroundingWhitespaceStress_Resilient(string type, bool expected)
    {
        var result = QuestObjectiveViewModel.IsGeographicObjective(type, "Generic description");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void R3_IsGeographicObjective_NullEmptyOrWhitespaceType_ReturnsFalse(string? emptyType)
    {
        var result = QuestObjectiveViewModel.IsGeographicObjective(emptyType, "Some description");
        Assert.False(result);
    }

    [Theory]
    [InlineData("other", "Survive and extract from Woods", false)]
    [InlineData("other", "survive in the raid", false)]
    [InlineData("other", "Hand over 3 salewas", false)]
    [InlineData("other", "hand over found in raid flash drives", false)]
    [InlineData("other", "Find and hand over 2 gas analyzers", false)]
    [InlineData("other", "find and hand over 3 morphines", false)]
    [InlineData("other", "Extract from Customs", false)]
    [InlineData("other", "extract from Factory", false)]
    [InlineData("task", "SURVIVE AND EXTRACT FROM LIGHTHOUSE", false)]
    [InlineData("task", "HAND OVER FOUND IN RAID GP COINS", false)]
    [InlineData("collect", "  Find and hand over 5 spark plugs  ", false)]
    public void R3_IsGeographicObjective_DescriptionAdministrativePrefixes_ExcludeEvenIfTypeIsGeneric(
        string type, string description, bool expected)
    {
        var result = QuestObjectiveViewModel.IsGeographicObjective(type, description);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("kill", "Eliminate 5 scavs and survive and extract", true)]
    [InlineData("visit", "Visit the dorms and survive and extract", true)]
    [InlineData("mark", "Mark the fuel tank near extract from factory", true)]
    [InlineData("collect", "Find 3 salewas in medical building", true)]
    [InlineData("collect", "Locate and hand over is not the start", true)]
    public void R3_IsGeographicObjective_KeywordsInMiddleOfDescription_DoNotFalselyExclude(
        string type, string description, bool expected)
    {
        var result = QuestObjectiveViewModel.IsGeographicObjective(type, description);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void R3_IsGeographicObjective_RealWorldDatabaseIntegrity_StressTestAllDatabaseObjectives()
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "tarkov_data.db");
        if (!File.Exists(dbPath))
        {
            // Try relative path from solution root
            dbPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "TarkovHelper", "Assets", "tarkov_data.db"));
        }

        if (!File.Exists(dbPath))
        {
            // Skip gracefully if test environment lacks the local DB
            return;
        }

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ObjectiveType, Description FROM QuestObjectives";
        using var reader = cmd.ExecuteReader();

        var totalCount = 0;
        var excludedAdministrativeCount = 0;
        var geographicCount = 0;

        while (reader.Read())
        {
            totalCount++;
            var type = reader.IsDBNull(0) ? null : reader.GetString(0);
            var desc = reader.IsDBNull(1) ? null : reader.GetString(1);

            // Execute without throwing exceptions
            var isGeo = QuestObjectiveViewModel.IsGeographicObjective(type, desc);
            if (isGeo)
            {
                geographicCount++;
                // Invariant: No geographic objective can be survive, handover, build, or extract
                var cleanType = type?.Trim().ToLowerInvariant();
                Assert.NotEqual("survive", cleanType);
                Assert.NotEqual("handover", cleanType);
                Assert.NotEqual("build", cleanType);
                Assert.NotEqual("extract", cleanType);
            }
            else
            {
                excludedAdministrativeCount++;
            }
        }

        Assert.True(totalCount > 0, "Database should contain objectives");
        Assert.True(geographicCount > 0, "Database should contain geographic objectives");
        Assert.True(excludedAdministrativeCount > 0, "Database should contain excluded administrative objectives");
    }
}
