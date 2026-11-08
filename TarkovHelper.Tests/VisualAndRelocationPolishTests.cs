using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Pages.Map.ViewModels;
using TarkovHelper.Services.Map;
using Xunit;

namespace TarkovHelper.Tests;

public class VisualAndRelocationPolishTests
{
    [Theory]
    [InlineData("visit", "Visit the dorms", true)]
    [InlineData("mark", "Mark the fuel tank with beacon", true)]
    [InlineData("collect", "Find 3 gas analyzers", true)]
    [InlineData("kill", "Eliminate 5 scavs on Customs", true)]
    [InlineData("plantItem", "Plant the gold watch", true)]
    [InlineData("custom", "Investigate the area", true)]
    [InlineData("survive", "Survive and extract from Woods", false)]
    [InlineData("handover", "Hand over 3 Salewa first aid kits", false)]
    [InlineData("build", "Build Shooting Range level 1", false)]
    [InlineData("extract", "Extract from factory", false)]
    [InlineData("other", "Survive in the raid", false)]
    [InlineData("other", "Hand over found in raid graphics card", false)]
    [InlineData(null, "Some objective", false)]
    [InlineData("", "Some objective", false)]
    public void IsGeographicObjective_CorrectlyFiltersAdministrativeObjectives(string? type, string? description, bool expected)
    {
        var result = QuestObjectiveViewModel.IsGeographicObjective(type, description);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void QuestObjectiveViewModel_Properties_MatchExpectedRelocationAndRevertStates()
    {
        var locService = new TarkovHelper.Services.LocalizationService();

        // 1. Geographic objective with coordinates -> Relocate & Revert both visible
        var geoWithCoords = new TaskObjectiveWithLocation
        {
            ObjectiveId = "obj_geo_1",
            TaskName = "Test Task",
            Description = "Find the missing convoy",
            Type = "visit",
            Locations = new List<QuestObjectiveLocation>
            {
                new() { MapName = "woods", X = 100, Y = 0, Z = 200, FloorId = "main" }
            }
        };

        var vm1 = new QuestObjectiveViewModel(geoWithCoords, locService, null, false, "woods");
        Assert.True(vm1.CanRelocate);
        Assert.Equal(Visibility.Visible, vm1.RelocateVisibility);
        Assert.True(vm1.HasLocation);
        Assert.Equal(Visibility.Visible, vm1.RevertVisibility);
        Assert.Equal("📍 Reubicar", vm1.RelocateButtonText);

        // 2. Geographic objective WITHOUT coordinates -> Relocate visible, Revert hidden
        var geoWithoutCoords = new TaskObjectiveWithLocation
        {
            ObjectiveId = "obj_geo_2",
            TaskName = "Test Task",
            Description = "Find the hidden stash",
            Type = "collect",
            Locations = new List<QuestObjectiveLocation>()
        };

        var vm2 = new QuestObjectiveViewModel(geoWithoutCoords, locService, null, false, "woods");
        Assert.True(vm2.CanRelocate);
        Assert.Equal(Visibility.Visible, vm2.RelocateVisibility);
        Assert.False(vm2.HasLocation);
        Assert.Equal(Visibility.Collapsed, vm2.RevertVisibility);
        Assert.Equal("📍 Ubicar", vm2.RelocateButtonText);

        // 3. Administrative objective -> Relocate and Revert both hidden
        var adminObj = new TaskObjectiveWithLocation
        {
            ObjectiveId = "obj_admin_1",
            TaskName = "Test Task",
            Description = "Survive and extract from Customs",
            Type = "survive",
            Locations = new List<QuestObjectiveLocation>()
        };

        var vm3 = new QuestObjectiveViewModel(adminObj, locService, null, false, "customs");
        Assert.False(vm3.CanRelocate);
        Assert.Equal(Visibility.Collapsed, vm3.RelocateVisibility);
        Assert.Equal(Visibility.Collapsed, vm3.RevertVisibility);
    }

    [Theory]
    [InlineData(8.0, 1.0, 12.0)]    // Small (8): 8 * 1.5 = 12px
    [InlineData(12.0, 1.0, 18.0)]   // Normal/Default (12): 12 * 1.5 = 18px (matches Quest default)
    [InlineData(16.0, 1.0, 24.0)]   // Large (16): 16 * 1.5 = 24px (matches Extraction default)
    [InlineData(12.0, 0.8, 14.4)]   // Normal on dense map (0.8 scale)
    [InlineData(12.0, 1.5, 27.0)]   // Normal on expansive map (1.5 scale)
    public void CustomMarkerScale_Formula_HarmonizesWithMapBaseline(double userSize, double mapScale, double expectedSize)
    {
        var calculatedSize = (userSize * 1.5) * mapScale;
        Assert.Equal(expectedSize, calculatedSize, precision: 2);
    }

    [Fact]
    public async Task ClearObjectiveLocationAsync_StrictlyProtectsUserDataDb()
    {
        var service = QuestObjectiveRelocationService.Instance;

        // Passing user_data.db path must throw InvalidOperationException
        var tempUserData = Path.Combine(Path.GetTempPath(), "user_data.db");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ClearObjectiveLocationAsync("any_id", tempUserData));
    }
}
