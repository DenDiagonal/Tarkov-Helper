using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Services.Map;
using Xunit;

namespace TarkovHelper.Tests;

[Collection("DatabaseUpdateServiceCollection")]
public class ObjectiveRelocationTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testDbPath;
    private readonly QuestObjectiveRelocationService _service;

    public ObjectiveRelocationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_RelocTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _testDbPath = Path.Combine(_testDir, "tarkov_data.db");
        CreateTestDatabase(_testDbPath);

        _service = QuestObjectiveRelocationService.Instance;

        // Ensure major maps are registered on the service transformer for testing
        SetupTestMapConfigs();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }

    private void SetupTestMapConfigs()
    {
        var testMaps = new List<MapConfig>
        {
            new()
            {
                Key = "Customs",
                DisplayName = "Customs",
                SvgFileName = "Customs.svg",
                ImageWidth = 4400,
                ImageHeight = 3200,
                Aliases = new List<string> { "customs", "CUSTOMS", "bigmap" },
                PlayerMarkerTransform = new double[] { -2.0, 0, 0, 2.0, 2600, 1601.5 },
                Floors = new List<MapFloorConfig>
                {
                    new() { LayerId = "basement", DisplayName = "Basement", Order = -1, IsDefault = false },
                    new() { LayerId = "main", DisplayName = "Ground Floor", Order = 0, IsDefault = true },
                    new() { LayerId = "level2", DisplayName = "Level 2", Order = 1, IsDefault = false }
                }
            },
            new()
            {
                Key = "Woods",
                DisplayName = "Woods",
                SvgFileName = "Woods.svg",
                ImageWidth = 4800,
                ImageHeight = 4800,
                Aliases = new List<string> { "woods", "WOODS" },
                PlayerMarkerTransform = new double[] { -2.0, 0, 0, 2.0, 2200, 2841.5 }
            }
        };

        _service.SetTransformer(new MapCoordinateTransformer(testMaps));
    }

    private static void CreateTestDatabase(string path)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        };

        using var connection = new SqliteConnection(csb.ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            PRAGMA foreign_keys = ON;

            CREATE TABLE Quests (
                Id TEXT PRIMARY KEY,
                BsgId TEXT,
                Name TEXT NOT NULL,
                NameEN TEXT,
                NameKO TEXT,
                NameJA TEXT,
                WikiPageLink TEXT,
                Trader TEXT,
                Location TEXT,
                MinLevel INTEGER,
                MinLevelApproved INTEGER NOT NULL DEFAULT 0,
                MinLevelApprovedAt TEXT,
                MinScavKarma INTEGER,
                MinScavKarmaApproved INTEGER NOT NULL DEFAULT 0,
                MinScavKarmaApprovedAt TEXT,
                UpdatedAt TEXT,
                KappaRequired INTEGER NOT NULL DEFAULT 0,
                Faction TEXT,
                IsApproved INTEGER NOT NULL DEFAULT 0,
                ApprovedAt TEXT,
                RequiredEdition TEXT,
                RequiredEditionApproved INTEGER NOT NULL DEFAULT 0,
                RequiredEditionApprovedAt TEXT,
                ExcludedEdition TEXT,
                ExcludedEditionApproved INTEGER NOT NULL DEFAULT 0,
                ExcludedEditionApprovedAt TEXT,
                RequiredDecodeCount INTEGER,
                RequiredDecodeCountApproved INTEGER NOT NULL DEFAULT 0,
                RequiredDecodeCountApprovedAt TEXT,
                RequiredPrestigeLevel INTEGER,
                RequiredPrestigeLevelApproved INTEGER NOT NULL DEFAULT 0,
                RequiredPrestigeLevelApprovedAt TEXT
            );

            CREATE TABLE QuestObjectives (
                Id TEXT PRIMARY KEY,
                QuestId TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                ObjectiveType TEXT NOT NULL DEFAULT 'Custom',
                Description TEXT NOT NULL,
                TargetType TEXT,
                TargetCount INTEGER,
                ItemId TEXT,
                ItemName TEXT,
                RequiresFIR INTEGER NOT NULL DEFAULT 0,
                MapName TEXT,
                LocationName TEXT,
                LocationPoints TEXT,
                Conditions TEXT,
                ContentHash TEXT,
                IsApproved INTEGER NOT NULL DEFAULT 0,
                ApprovedAt TEXT,
                UpdatedAt TEXT,
                OptionalPoints TEXT,
                DogtagMinLevel INTEGER,
                DogtagFaction TEXT,
                FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE
            );

            CREATE TABLE MapFloorLocations (
                MapKey TEXT NOT NULL,
                FloorId TEXT NOT NULL,
                MinY REAL NOT NULL,
                MaxY REAL NOT NULL,
                MinX REAL,
                MaxX REAL,
                MinZ REAL,
                MaxZ REAL,
                Priority INTEGER NOT NULL DEFAULT 0
            );

            INSERT INTO Quests (Id, Name, Trader, Location, UpdatedAt)
            VALUES ('q_customs_fuel', 'A Fuel Matter', 'Prapor', 'Customs', '2026-01-01T00:00:00Z');

            INSERT INTO QuestObjectives (Id, QuestId, SortOrder, Description, MapName, LocationPoints, IsApproved)
            VALUES ('obj_fuel_1', 'q_customs_fuel', 0, 'Mark first fuel tank', 'Customs', NULL, 0),
                   ('obj_fuel_2', 'q_customs_fuel', 1, 'Mark second fuel tank', 'Customs', NULL, 0);
        ";
        cmd.ExecuteNonQuery();
    }

    #region Placement State Lifecycle Tests

    [Fact]
    public void PlacementState_InitialState_IsInactiveAndNulls()
    {
        _service.CancelPlacement();

        Assert.False(_service.IsPlacementActive);
        Assert.Null(_service.TargetObjectiveId);
        Assert.Null(_service.TargetObjectiveDescription);
        Assert.Null(_service.TargetQuestName);
        Assert.Null(_service.TargetMapKey);
    }

    [Fact]
    public void StartPlacement_SetsPropertiesAndRaisesEvent()
    {
        bool eventFired = false;
        EventHandler handler = (s, e) => eventFired = true;
        _service.PlacementStateChanged += handler;

        try
        {
            _service.StartPlacement("obj_123", "Find the key", "Checking", "customs");

            Assert.True(_service.IsPlacementActive);
            Assert.Equal("obj_123", _service.TargetObjectiveId);
            Assert.Equal("Find the key", _service.TargetObjectiveDescription);
            Assert.Equal("Checking", _service.TargetQuestName);
            Assert.Equal("customs", _service.TargetMapKey);
            Assert.True(eventFired);
        }
        finally
        {
            _service.PlacementStateChanged -= handler;
            _service.CancelPlacement();
        }
    }

    [Fact]
    public void StartPlacement_NullOrEmptyId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _service.StartPlacement("", "Desc", "Quest"));
        Assert.Throws<ArgumentException>(() => _service.StartPlacement("   ", "Desc", "Quest"));
    }

    [Fact]
    public void CancelPlacement_ClearsPropertiesAndRaisesEvent()
    {
        _service.StartPlacement("obj_123", "Find the key", "Checking", "customs");

        bool eventFired = false;
        EventHandler handler = (s, e) => eventFired = true;
        _service.PlacementStateChanged += handler;

        try
        {
            _service.CancelPlacement();

            Assert.False(_service.IsPlacementActive);
            Assert.Null(_service.TargetObjectiveId);
            Assert.Null(_service.TargetObjectiveDescription);
            Assert.Null(_service.TargetQuestName);
            Assert.Null(_service.TargetMapKey);
            Assert.True(eventFired);
        }
        finally
        {
            _service.PlacementStateChanged -= handler;
        }
    }

    #endregion

    #region Coordinate Transformation Accuracy Tests

    [Fact]
    public void TryCalculateWorldCoordinates_Customs_RoundTripMathematicalConsistency()
    {
        // Customs affine transform: X_screen = -2.0 * X_game + 2600, Y_screen = 2.0 * Z_game + 1601.5
        // Test ground-truth game world coordinates
        double expectedGameX = -180.44268;
        double expectedGameZ = -132.34748;

        // Forward transform to canvas screen space
        double screenX = -2.0 * expectedGameX + 2600.0; // 2960.88536
        double screenY = 2.0 * expectedGameZ + 1601.5;  // 1336.80504

        var point = new Point(screenX, screenY);
        var success = _service.TryCalculateWorldCoordinates("Customs", point, null, out var locPoint);

        Assert.True(success);
        Assert.NotNull(locPoint);
        Assert.Equal(expectedGameX, locPoint.X, 4);
        Assert.Equal(expectedGameZ, locPoint.Z, 4);
        Assert.Equal(1.0, locPoint.Y); // Floor midpoint for Customs main (-1.0 + 3.0) / 2 = 1.0
        // Customs default floor in config is "main"
        Assert.Equal("main", locPoint.FloorId);
    }

    [Fact]
    public void TryCalculateWorldCoordinates_Woods_RoundTripMathematicalConsistency()
    {
        // Woods affine transform: X_screen = -2.0 * X_game + 2200, Y_screen = 2.0 * Z_game + 2841.5
        double expectedGameX = -613.25378;
        double expectedGameZ = 131.97157;

        double screenX = -2.0 * expectedGameX + 2200.0; // 3426.50756
        double screenY = 2.0 * expectedGameZ + 2841.5;  // 3105.44314

        var point = new Point(screenX, screenY);
        var success = _service.TryCalculateWorldCoordinates("Woods", point, null, out var locPoint);

        Assert.True(success);
        Assert.NotNull(locPoint);
        Assert.Equal(expectedGameX, locPoint.X, 4);
        Assert.Equal(expectedGameZ, locPoint.Z, 4);
        Assert.Equal(0.0, locPoint.Y);
        // Woods has no floors in config
        Assert.Null(locPoint.FloorId);
    }

    [Fact]
    public void TryCalculateWorldCoordinates_WithExplicitFloorId_PreservesFloorId()
    {
        double screenX = 2600.0; // Origin (0,0)
        double screenY = 1601.5;

        var point = new Point(screenX, screenY);
        var success = _service.TryCalculateWorldCoordinates("Customs", point, "level2", out var locPoint);

        Assert.True(success);
        Assert.NotNull(locPoint);
        Assert.Equal(0.0, locPoint.X, 4);
        Assert.Equal(0.0, locPoint.Z, 4);
        Assert.Equal("level2", locPoint.FloorId);
    }

    [Fact]
    public void TryCalculateWorldCoordinates_UnknownMap_ReturnsFalse()
    {
        var point = new Point(100, 100);
        var success = _service.TryCalculateWorldCoordinates("UnknownNonExistentMap", point, null, out var locPoint);

        Assert.False(success);
        Assert.Null(locPoint);
    }

    #endregion

    #region Security Guard (user_data.db Protection)

    [Fact]
    public async Task SaveObjectiveLocationAsync_TargetingUserDataDb_ThrowsInvalidOperationException()
    {
        var loc = new LocationPoint(10, 0, 20, "main");
        var badPath = Path.Combine(_testDir, "user_data.db");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SaveObjectiveLocationAsync("obj_fuel_1", loc, badPath));
    }

    [Fact]
    public async Task ClearObjectiveLocationAsync_TargetingUserDataDb_ThrowsInvalidOperationException()
    {
        var badPath = Path.Combine(_testDir, "user_data.db");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ClearObjectiveLocationAsync("obj_fuel_1", badPath));
    }

    [Fact]
    public async Task SaveObjectiveLocationAsync_NullOrEmptyId_ThrowsArgumentException()
    {
        var loc = new LocationPoint(10, 0, 20, "main");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SaveObjectiveLocationAsync("", loc, _testDbPath));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SaveObjectiveLocationAsync("   ", loc, _testDbPath));
    }

    [Fact]
    public async Task SaveObjectiveLocationAsync_NullPoint_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.SaveObjectiveLocationAsync("obj_fuel_1", null!, _testDbPath));
    }

    #endregion

    #region SQLite Persistence & Deserialization Tests

    [Fact]
    public async Task SaveObjectiveLocationAsync_UpdatesLocationPoints_AndSetsIsApproved()
    {
        var point = new LocationPoint(-180.44268, 0.0, -132.34748, "main");

        var success = await _service.SaveObjectiveLocationAsync("obj_fuel_1", point, _testDbPath);
        Assert.True(success);

        // Verify SQLite state
        using var connection = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT LocationPoints, IsApproved, UpdatedAt FROM QuestObjectives WHERE Id = 'obj_fuel_1'";

        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());

        var json = reader.GetString(0);
        var isApproved = reader.GetInt32(1);
        var updatedAt = reader.GetString(2);

        Assert.Equal(1, isApproved);
        Assert.False(string.IsNullOrWhiteSpace(updatedAt));

        // Verify valid JSON deserialization into List<LocationPoint>
        var deserialized = JsonSerializer.Deserialize<List<LocationPoint>>(json);
        Assert.NotNull(deserialized);
        Assert.Single(deserialized);

        var first = deserialized[0];
        Assert.Equal(point.X, first.X, 4);
        Assert.Equal(point.Y, first.Y, 4);
        Assert.Equal(point.Z, first.Z, 4);
        Assert.Equal("main", first.FloorId);
    }

    [Fact]
    public async Task ClearObjectiveLocationAsync_SetsLocationPointsToNull()
    {
        // First save a location
        var point = new LocationPoint(50.0, 0.0, 75.0, "main");
        await _service.SaveObjectiveLocationAsync("obj_fuel_2", point, _testDbPath);

        // Now clear it
        var success = await _service.ClearObjectiveLocationAsync("obj_fuel_2", _testDbPath);
        Assert.True(success);

        // Verify SQLite state
        using var connection = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT LocationPoints, UpdatedAt FROM QuestObjectives WHERE Id = 'obj_fuel_2'";

        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());

        Assert.True(reader.IsDBNull(0));
        Assert.False(string.IsNullOrWhiteSpace(reader.GetString(1)));
    }

    [Fact]
    public async Task GetObjectivesForQuest_ReturnsMatchingObjectivesWithCoordinateStatus()
    {
        // Initially both objectives have no coordinates
        var objectivesBefore = _service.GetObjectivesForQuest("A Fuel Matter", _testDbPath);
        Assert.Equal(2, objectivesBefore.Count);
        Assert.False(objectivesBefore[0].HasCoordinates);
        Assert.False(objectivesBefore[1].HasCoordinates);

        // Save location for first objective
        var point = new LocationPoint(-180.44268, 0.0, -132.34748, "main");
        await _service.SaveObjectiveLocationAsync("obj_fuel_1", point, _testDbPath);

        // Query again
        var objectivesAfter = _service.GetObjectivesForQuest("A Fuel Matter", _testDbPath);
        Assert.Equal(2, objectivesAfter.Count);

        var obj1 = objectivesAfter.First(o => o.Id == "obj_fuel_1");
        var obj2 = objectivesAfter.First(o => o.Id == "obj_fuel_2");

        Assert.True(obj1.HasCoordinates);
        Assert.NotNull(obj1.LocationPointsJson);
        Assert.False(obj2.HasCoordinates);
        Assert.Null(obj2.LocationPointsJson);
    }

    #endregion
}
