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
using TarkovHelper.Services;
using TarkovHelper.Services.Map;
using Xunit;

namespace TarkovHelper.Tests;

/// <summary>
/// Empirical adversarial stress tests for QuestObjectiveRelocationService:
/// - Round-trip mathematical accuracy across all 8 supported maps (Customs, Woods, Interchange, Shoreline, Factory, Reserve, Lighthouse, Streets).
/// - Inversion with extreme and boundary coordinates (canvas corners, negative pixels, limits).
/// - Multi-floor elevation calculations and unrecognized floor IDs.
/// - Unknown map key handling and SQL injection resilience without throwing NullReferenceException.
/// - Security guard against user_data.db mutation and persistence robustness.
/// - Multithreaded concurrency stress testing.
/// </summary>
[Collection("DatabaseUpdateServiceCollection")]
public class ObjectiveRelocationAdversarialTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testDbPath;
    private readonly QuestObjectiveRelocationService _service;
    private readonly List<MapConfig> _productionMapConfigs;

    public ObjectiveRelocationAdversarialTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_RelocAdvTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _testDbPath = Path.Combine(_testDir, "tarkov_data.db");
        CreateTestDatabase(_testDbPath);

        _service = QuestObjectiveRelocationService.Instance;

        // Load all production map configs from map_configs.json
        _productionMapConfigs = LoadProductionMapConfigs();
        _service.SetTransformer(new MapCoordinateTransformer(_productionMapConfigs));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }

        // Restore transformer with full production map list
        _service.SetTransformer(new MapCoordinateTransformer(_productionMapConfigs));
    }

    private static List<MapConfig> LoadProductionMapConfigs()
    {
        var possiblePaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Data", "map_configs.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "TarkovHelper", "Assets", "DB", "Data", "map_configs.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "TarkovHelper", "Assets", "DB", "Data", "map_configs.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "DB", "Data", "map_configs.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "TarkovHelper", "Assets", "DB", "Data", "map_configs.json")
        };

        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                var json = File.ReadAllText(fullPath);
                var configList = JsonSerializer.Deserialize<MapConfigList>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (configList?.Maps != null && configList.Maps.Count > 0)
                {
                    return configList.Maps;
                }
            }
        }

        throw new FileNotFoundException("map_configs.json could not be located in any expected path.");
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
                Name TEXT NOT NULL,
                Trader TEXT,
                Location TEXT,
                UpdatedAt TEXT
            );

            CREATE TABLE QuestObjectives (
                Id TEXT PRIMARY KEY,
                QuestId TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                Description TEXT NOT NULL,
                MapName TEXT,
                LocationPoints TEXT,
                IsApproved INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT,
                FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE
            );

            CREATE TABLE MapFloorLocations (
                Id TEXT PRIMARY KEY,
                MapKey TEXT NOT NULL,
                FloorId TEXT NOT NULL,
                RegionName TEXT NOT NULL,
                MinY REAL NOT NULL,
                MaxY REAL NOT NULL,
                MinX REAL,
                MaxX REAL,
                MinZ REAL,
                MaxZ REAL,
                Priority INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            INSERT INTO Quests (Id, Name, Trader, Location, UpdatedAt)
            VALUES ('q_test_1', 'Test Quest Alpha', 'Prapor', 'Customs', '2026-01-01T00:00:00Z');

            INSERT INTO QuestObjectives (Id, QuestId, SortOrder, Description, MapName, LocationPoints, IsApproved)
            VALUES ('obj_adv_1', 'q_test_1', 0, 'Objective 1', 'Customs', NULL, 0),
                   ('obj_adv_2', 'q_test_1', 1, 'Objective 2', 'Woods', NULL, 0);

            -- Floor elevations for multi-floor testing
            INSERT INTO MapFloorLocations (Id, MapKey, FloorId, RegionName, MinY, MaxY, CreatedAt, UpdatedAt)
            VALUES ('fl_c_1', 'Customs', 'main', 'Ground Floor', -1.0, 3.0, '2026-01-01', '2026-01-01'),
                   ('fl_c_2', 'Customs', 'level2', 'Second Floor', 3.0, 6.0, '2026-01-01', '2026-01-01'),
                   ('fl_c_3', 'Customs', 'level3', 'Third Floor', 6.0, 10.0, '2026-01-01', '2026-01-01'),
                   ('fl_s_1', 'Shoreline', 'basement', 'Basement', -10.0, -4.5, '2026-01-01', '2026-01-01'),
                   ('fl_s_2', 'Shoreline', 'main', 'Ground Floor', -4.5, -2.0, '2026-01-01', '2026-01-01'),
                   ('fl_s_3', 'Shoreline', 'level2', 'Second Floor', -2.0, 1.0, '2026-01-01', '2026-01-01'),
                   ('fl_s_4', 'Shoreline', 'level3', 'Third Floor', 1.0, 10.0, '2026-01-01', '2026-01-01'),
                   ('fl_i_1', 'Interchange', 'main', 'Ground Floor', 22.0, 23.5, '2026-01-01', '2026-01-01'),
                   ('fl_i_2', 'Interchange', 'level2', 'Second Floor', 23.5, 32.0, '2026-01-01', '2026-01-01'),
                   ('fl_i_3', 'Interchange', 'level3', 'Third Floor', 32.0, 50.0, '2026-01-01', '2026-01-01'),
                   ('fl_r_1', 'Reserve', 'bunker', 'Bunker', -20.0, -7.0, '2026-01-01', '2026-01-01'),
                   ('fl_r_2', 'Reserve', 'main', 'Ground Floor', -5.7, -3.5, '2026-01-01', '2026-01-01'),
                   ('fl_r_3', 'Reserve', 'level4', 'Level 4', 2.0, 7.0, '2026-01-01', '2026-01-01');
        ";
        cmd.ExecuteNonQuery();
    }

    #region Requirement 1: Round-Trip Accuracy Across All 8 Supported Maps

    [Theory]
    [InlineData("Customs")]
    [InlineData("Woods")]
    [InlineData("Interchange")]
    [InlineData("Shoreline")]
    [InlineData("Factory")]
    [InlineData("Reserve")]
    [InlineData("Lighthouse")]
    [InlineData("StreetsOfTarkov")]
    [InlineData("streets")]
    public void RoundTripAccuracy_ForwardToInverse_MatchesGameWorldCoordinates(string mapKey)
    {
        var config = _productionMapConfigs.FirstOrDefault(m => m.MatchesMapName(mapKey));
        Assert.NotNull(config);

        // Test coordinates spread across quadrants in EFT game world units
        var testGameCoords = new[]
        {
            (X: 0.0, Z: 0.0),
            (X: 125.456, Z: 89.123),
            (X: -180.789, Z: -130.456),
            (X: 320.125, Z: -210.875),
            (X: -95.625, Z: 245.375)
        };

        foreach (var (expectedX, expectedZ) in testGameCoords)
        {
            // 1. Forward transform: Game -> Screen
            var (screenX, screenY) = config.GameToScreenForPlayer(expectedX, expectedZ);

            // 2. Inverse transform via Relocation Service: Screen -> Game
            var point = new Point(screenX, screenY);
            var success = _service.TryCalculateWorldCoordinates(mapKey, point, null, out var locPoint);

            Assert.True(success, $"TryCalculateWorldCoordinates failed for map '{mapKey}' at screen ({screenX}, {screenY}).");
            Assert.NotNull(locPoint);

            // Assert round-trip precision within 0.0001 game world units
            Assert.Equal(expectedX, locPoint.X, 4);
            Assert.Equal(expectedZ, locPoint.Z, 4);
        }
    }

    [Theory]
    [InlineData("Customs")]
    [InlineData("Woods")]
    [InlineData("Interchange")]
    [InlineData("Shoreline")]
    [InlineData("Factory")]
    [InlineData("Reserve")]
    [InlineData("Lighthouse")]
    [InlineData("StreetsOfTarkov")]
    public void RoundTripAccuracy_InverseToForward_MatchesCanvasScreenCoordinates(string mapKey)
    {
        var config = _productionMapConfigs.FirstOrDefault(m => m.MatchesMapName(mapKey));
        Assert.NotNull(config);

        // Test canvas pixel coordinates across different regions
        var testCanvasPoints = new[]
        {
            (X: config.ImageWidth / 2.0, Y: config.ImageHeight / 2.0),
            (X: config.ImageWidth * 0.25, Y: config.ImageHeight * 0.25),
            (X: config.ImageWidth * 0.75, Y: config.ImageHeight * 0.25),
            (X: config.ImageWidth * 0.25, Y: config.ImageHeight * 0.75),
            (X: config.ImageWidth * 0.75, Y: config.ImageHeight * 0.75)
        };

        foreach (var (expectedCanvasX, expectedCanvasY) in testCanvasPoints)
        {
            // 1. Inverse transform: Screen -> Game
            var success = _service.TryCalculateWorldCoordinates(mapKey, expectedCanvasX, expectedCanvasY, null, out var locPoint);
            Assert.True(success, $"TryCalculateWorldCoordinates failed for map '{mapKey}' at canvas ({expectedCanvasX}, {expectedCanvasY}).");
            Assert.NotNull(locPoint);

            // 2. Forward transform: Game -> Screen
            var (actualCanvasX, actualCanvasY) = config.GameToScreenForPlayer(locPoint.X, locPoint.Z);

            // Assert round-trip canvas precision within 0.001 screen pixels
            Assert.Equal(expectedCanvasX, actualCanvasX, 3);
            Assert.Equal(expectedCanvasY, actualCanvasY, 3);
        }
    }

    [Fact]
    public void Factory_RotatedAffineMatrix_InvertsAccurately()
    {
        // Factory has a rotated transform with zeros on the diagonal: [0, -10, -10, 0, 1800, 1851.5]
        var config = _productionMapConfigs.First(m => m.Key == "Factory");
        Assert.Equal(0, config.PlayerMarkerTransform![0]);
        Assert.Equal(-10, config.PlayerMarkerTransform[1]);
        Assert.Equal(-10, config.PlayerMarkerTransform[2]);
        Assert.Equal(0, config.PlayerMarkerTransform[3]);

        double testGameX = 35.5;
        double testGameZ = -22.4;

        var (screenX, screenY) = config.GameToScreenForPlayer(testGameX, testGameZ);
        var success = _service.TryCalculateWorldCoordinates("Factory", screenX, screenY, null, out var locPoint);

        Assert.True(success);
        Assert.NotNull(locPoint);
        Assert.Equal(testGameX, locPoint.X, 4);
        Assert.Equal(testGameZ, locPoint.Z, 4);
    }

    [Fact]
    public void Reserve_ShearedRotatedAffineMatrix_InvertsAccurately()
    {
        // Reserve transform: [-1.932, 0.518, 0.517, 1.932, 1600.02, 1520.08]
        var config = _productionMapConfigs.First(m => m.Key == "Reserve");
        Assert.Equal(-1.932, config.PlayerMarkerTransform![0]);
        Assert.Equal(0.518, config.PlayerMarkerTransform[1]);

        double testGameX = -112.35;
        double testGameZ = 48.72;

        var (screenX, screenY) = config.GameToScreenForPlayer(testGameX, testGameZ);
        var success = _service.TryCalculateWorldCoordinates("Reserve", screenX, screenY, null, out var locPoint);

        Assert.True(success);
        Assert.NotNull(locPoint);
        Assert.Equal(testGameX, locPoint.X, 4);
        Assert.Equal(testGameZ, locPoint.Z, 4);
    }

    #endregion

    #region Requirement 2: Extreme & Boundary Coordinates

    [Theory]
    [InlineData("Customs")]
    [InlineData("Woods")]
    [InlineData("Interchange")]
    [InlineData("Shoreline")]
    [InlineData("Factory")]
    [InlineData("Reserve")]
    [InlineData("Lighthouse")]
    [InlineData("StreetsOfTarkov")]
    public void BoundaryCoordinates_CanvasCorners_ComputeFiniteCoordinatesAndRoundTrip(string mapKey)
    {
        var config = _productionMapConfigs.First(m => m.MatchesMapName(mapKey));

        var cornerPoints = new (double X, double Y)[]
        {
            (0.0, 0.0),                                       // Top-left
            (config.ImageWidth, 0.0),                          // Top-right
            (0.0, config.ImageHeight),                         // Bottom-left
            (config.ImageWidth, config.ImageHeight)            // Bottom-right
        };

        foreach (var (cx, cy) in cornerPoints)
        {
            var success = _service.TryCalculateWorldCoordinates(mapKey, cx, cy, null, out var locPoint);

            Assert.True(success, $"Failed for {mapKey} corner ({cx}, {cy})");
            Assert.NotNull(locPoint);
            Assert.False(double.IsNaN(locPoint.X));
            Assert.False(double.IsInfinity(locPoint.X));
            Assert.False(double.IsNaN(locPoint.Z));
            Assert.False(double.IsInfinity(locPoint.Z));

            // Round-trip back to canvas
            var (reconstructedX, reconstructedY) = config.GameToScreenForPlayer(locPoint.X, locPoint.Z);
            Assert.Equal(cx, reconstructedX, 3);
            Assert.Equal(cy, reconstructedY, 3);
        }
    }

    [Theory]
    [InlineData(-100.0, -100.0)]
    [InlineData(-5000.0, -2500.0)]
    [InlineData(50000.0, 50000.0)]
    [InlineData(-1e6, -1e6)]
    [InlineData(1e6, 1e6)]
    public void BoundaryCoordinates_NegativeAndFarBeyondCanvas_ProduceFiniteRoundTripCoordinates(double canvasX, double canvasY)
    {
        var config = _productionMapConfigs.First(m => m.Key == "Customs");

        var success = _service.TryCalculateWorldCoordinates("Customs", canvasX, canvasY, null, out var locPoint);

        Assert.True(success);
        Assert.NotNull(locPoint);
        Assert.False(double.IsNaN(locPoint.X));
        Assert.False(double.IsInfinity(locPoint.X));
        Assert.False(double.IsNaN(locPoint.Z));
        Assert.False(double.IsInfinity(locPoint.Z));

        var (reconX, reconY) = config.GameToScreenForPlayer(locPoint.X, locPoint.Z);
        Assert.Equal(canvasX, reconX, 2);
        Assert.Equal(canvasY, reconY, 2);
    }

    [Fact]
    public void SubpixelCoordinates_PreservesPrecisionWithinRoundingLimits()
    {
        double subpixelX = 2600.12345678;
        double subpixelY = 1601.87654321;

        var success = _service.TryCalculateWorldCoordinates("Customs", subpixelX, subpixelY, null, out var locPoint);

        Assert.True(success);
        Assert.NotNull(locPoint);

        // Service rounds to 5 decimal places
        Assert.Equal(locPoint.X, Math.Round(locPoint.X, 5));
        Assert.Equal(locPoint.Z, Math.Round(locPoint.Z, 5));

        // Forward check
        var config = _productionMapConfigs.First(m => m.Key == "Customs");
        var (reconX, reconY) = config.GameToScreenForPlayer(locPoint.X, locPoint.Z);
        Assert.Equal(subpixelX, reconX, 3);
        Assert.Equal(subpixelY, reconY, 3);
    }

    #endregion

    #region Requirement 3: Multi-Floor Elevation & Unrecognized Floor IDs

    [Fact]
    public void MultiFloor_Customs_CalculatesCorrectElevationsAcrossAllFloors()
    {
        // Customs has: main (-1.0 to 3.0, mid=1.0), level2 (3.0 to 6.0, mid=4.5), level3 (6.0 to 10.0, mid=8.0)
        _service.TryCalculateWorldCoordinates("Customs", 2600, 1601.5, "main", out var locMain);
        _service.TryCalculateWorldCoordinates("Customs", 2600, 1601.5, "level2", out var locLvl2);
        _service.TryCalculateWorldCoordinates("Customs", 2600, 1601.5, "level3", out var locLvl3);

        Assert.NotNull(locMain);
        Assert.NotNull(locLvl2);
        Assert.NotNull(locLvl3);

        Assert.Equal(1.0, locMain.Y);
        Assert.Equal("main", locMain.FloorId);

        Assert.Equal(4.5, locLvl2.Y);
        Assert.Equal("level2", locLvl2.FloorId);

        Assert.Equal(8.0, locLvl3.Y);
        Assert.Equal("level3", locLvl3.FloorId);
    }

    [Fact]
    public void MultiFloor_Interchange_CalculatesCorrectElevations()
    {
        // Interchange: main (22.0 to 23.5, mid=22.75), level2 (23.5 to 32.0, mid=27.75), level3 (32.0 to 50.0, mid=41.0)
        _service.TryCalculateWorldCoordinates("Interchange", 2166, 2004, "main", out var locMain);
        _service.TryCalculateWorldCoordinates("Interchange", 2166, 2004, "level2", out var locLvl2);
        _service.TryCalculateWorldCoordinates("Interchange", 2166, 2004, "level3", out var locLvl3);

        Assert.NotNull(locMain);
        Assert.NotNull(locLvl2);
        Assert.NotNull(locLvl3);

        Assert.Equal(22.75, locMain.Y);
        Assert.Equal(27.75, locLvl2.Y);
        Assert.Equal(41.0, locLvl3.Y);
    }

    [Fact]
    public void MultiFloor_Shoreline_CalculatesCorrectNegativeAndPositiveElevations()
    {
        // Shoreline: basement (-10 to -4.5, mid=-7.25), main (-4.5 to -2.0, mid=-3.25), level2 (-2.0 to 1.0, mid=-0.5), level3 (1.0 to 10.0, mid=5.5)
        _service.TryCalculateWorldCoordinates("Shoreline", 1570, 1451.5, "basement", out var locBase);
        _service.TryCalculateWorldCoordinates("Shoreline", 1570, 1451.5, "main", out var locMain);
        _service.TryCalculateWorldCoordinates("Shoreline", 1570, 1451.5, "level2", out var locLvl2);
        _service.TryCalculateWorldCoordinates("Shoreline", 1570, 1451.5, "level3", out var locLvl3);

        Assert.NotNull(locBase);
        Assert.NotNull(locMain);
        Assert.NotNull(locLvl2);
        Assert.NotNull(locLvl3);

        Assert.Equal(-7.25, locBase.Y);
        Assert.Equal(-3.25, locMain.Y);
        Assert.Equal(-0.5, locLvl2.Y);
        Assert.Equal(5.5, locLvl3.Y);
    }

    [Fact]
    public void MultiFloor_NullFloor_ResolvesDefaultFloorFromConfig()
    {
        // Customs default floor in config is "main"
        var successCustoms = _service.TryCalculateWorldCoordinates("Customs", 2600, 1601.5, null, out var locCustoms);
        Assert.True(successCustoms);
        Assert.NotNull(locCustoms);
        Assert.Equal("main", locCustoms.FloorId);
        Assert.Equal(1.0, locCustoms.Y);

        // Interchange default floor in config is "main"
        var successInterchange = _service.TryCalculateWorldCoordinates("Interchange", 2166, 2004, null, out var locInterchange);
        Assert.True(successInterchange);
        Assert.NotNull(locInterchange);
        Assert.Equal("main", locInterchange.FloorId);
        Assert.Equal(22.75, locInterchange.Y);
    }

    [Fact]
    public void MultiFloor_UnrecognizedFloorId_GracefullyPreservesFloorAndDefaultsElevationToZero()
    {
        // An unknown floor ID on Customs should NOT throw, must preserve the FloorId string, and set Y to 0.0
        var success = _service.TryCalculateWorldCoordinates("Customs", 2600, 1601.5, "secret_bunker_floor_99", out var locPoint);

        Assert.True(success);
        Assert.NotNull(locPoint);
        Assert.Equal("secret_bunker_floor_99", locPoint.FloorId);
        Assert.Equal(0.0, locPoint.Y);
    }

    [Fact]
    public void MultiFloor_CaseInsensitiveMapKey_FloorElevationBehavior()
    {
        // When using exact PascalCase "Customs"
        var successPascal = _service.TryCalculateWorldCoordinates("Customs", 2600, 1601.5, "main", out var locPascal);
        Assert.True(successPascal);
        Assert.NotNull(locPascal);
        Assert.Equal(1.0, locPascal.Y);

        // When using lowercase "customs" or alias "bigmap"
        var successLower = _service.TryCalculateWorldCoordinates("customs", 2600, 1601.5, "main", out var locLower);
        Assert.True(successLower);
        Assert.NotNull(locLower);
        // Does SQLite binary match find 'Customs' when queried with 'customs'?
        // locLower.Y will be 0.0 due to binary case sensitivity in SQLite table query
        Assert.Equal(0.0, locLower.Y);
    }

    [Fact]
    public void MultiFloor_FloorIdWithSpecialCharactersAndSqlSyntax_DoesNotThrow()
    {
        string adversarialFloorId = "main' OR 1=1; DROP TABLE Quests;--";
        var success = _service.TryCalculateWorldCoordinates("Customs", 2600, 1601.5, adversarialFloorId, out var locPoint);

        Assert.True(success);
        Assert.NotNull(locPoint);
        Assert.Equal(adversarialFloorId, locPoint.FloorId);
        Assert.Equal(0.0, locPoint.Y);
    }

    [Fact]
    public void SingleFloorMap_WoodsAndLighthouse_DefaultsToZeroElevationAndNullFloor()
    {
        var successWoods = _service.TryCalculateWorldCoordinates("Woods", 2200, 2841.5, null, out var locWoods);
        Assert.True(successWoods);
        Assert.NotNull(locWoods);
        Assert.Null(locWoods.FloorId);
        Assert.Equal(0.0, locWoods.Y);

        var successLighthouse = _service.TryCalculateWorldCoordinates("Lighthouse", 1550, 2051.5, null, out var locLighthouse);
        Assert.True(successLighthouse);
        Assert.NotNull(locLighthouse);
        Assert.Null(locLighthouse.FloorId);
        Assert.Equal(0.0, locLighthouse.Y);
    }

    #endregion

    #region Requirement 4: Unknown Map Key Handling & Graceful Resilience

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NonExistentMap_12345")]
    [InlineData("UnknownZone")]
    [InlineData("Customs'; DROP TABLE Quests;--")]
    public void UnknownMapKey_ReturnsFalseWithoutThrowingNullReferenceException(string? badMapKey)
    {
        var canvasPoint = new Point(100, 100);

        // Point overload
        var success1 = _service.TryCalculateWorldCoordinates(badMapKey!, canvasPoint, null, out var locPoint1);
        Assert.False(success1);
        Assert.Null(locPoint1);

        // double overload
        var success2 = _service.TryCalculateWorldCoordinates(badMapKey!, 100.0, 100.0, null, out var locPoint2);
        Assert.False(success2);
        Assert.Null(locPoint2);
    }

    [Theory]
    [InlineData("customs")]
    [InlineData("CUSTOMS")]
    [InlineData("bigmap")]
    [InlineData("woods")]
    [InlineData("WOODS")]
    [InlineData("interchange")]
    [InlineData("INTERCHANGE")]
    [InlineData("reserve")]
    [InlineData("RESERVE")]
    [InlineData("RezervBase")]
    [InlineData("lighthouse")]
    [InlineData("LIGHTHOUSE")]
    [InlineData("streets")]
    [InlineData("STREETS")]
    [InlineData("TarkovStreets")]
    [InlineData("streets-of-tarkov")]
    [InlineData("Streets of Tarkov")]
    [InlineData("factory")]
    [InlineData("FACTORY")]
    [InlineData("factory4_day")]
    [InlineData("factory4_night")]
    [InlineData("shoreline")]
    [InlineData("SHORELINE")]
    public void SupportedMapKeysAndAliases_ResolveCaseInsensitively(string mapAlias)
    {
        var success = _service.TryCalculateWorldCoordinates(mapAlias, 1000, 1000, null, out var locPoint);
        Assert.True(success, $"Failed to resolve map alias '{mapAlias}'");
        Assert.NotNull(locPoint);
    }

    #endregion

    #region Security Guard & Persistence Stress Testing

    [Theory]
    [InlineData("user_data.db")]
    [InlineData("USER_DATA.DB")]
    [InlineData("User_Data.DB")]
    [InlineData("C:\\Games\\Tarkov\\user_data.db")]
    [InlineData("/var/data/user_data.db")]
    [InlineData(".\\Assets\\user_data.db")]
    public async Task Persistence_AnyUserDataDbPath_ThrowsInvalidOperationException(string forbiddenPath)
    {
        var point = new LocationPoint(10, 0, 20, "main");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SaveObjectiveLocationAsync("obj_adv_1", point, forbiddenPath));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ClearObjectiveLocationAsync("obj_adv_1", forbiddenPath));
    }

    [Fact]
    public async Task Persistence_NonExistentDatabaseFile_ReturnsFalseWithoutThrowing()
    {
        var nonExistentPath = Path.Combine(_testDir, "does_not_exist.db");
        var point = new LocationPoint(10, 0, 20, "main");

        var successSave = await _service.SaveObjectiveLocationAsync("obj_adv_1", point, nonExistentPath);
        Assert.False(successSave);

        var successClear = await _service.ClearObjectiveLocationAsync("obj_adv_1", nonExistentPath);
        Assert.False(successClear);
    }

    [Fact]
    public async Task Persistence_ExtremeCoordinates_PersistsAndDeserializesPrecisely()
    {
        var extremePoint = new LocationPoint(-987654.32109, 543.21098, 876543.21098, "level3");

        var saveSuccess = await _service.SaveObjectiveLocationAsync("obj_adv_1", extremePoint, _testDbPath);
        Assert.True(saveSuccess);

        var objectives = await _service.GetObjectivesForQuestAsync("Test Quest Alpha", _testDbPath);
        Assert.NotEmpty(objectives);

        var obj1 = objectives.First(o => o.Id == "obj_adv_1");
        Assert.True(obj1.HasCoordinates);
        Assert.NotNull(obj1.LocationPointsJson);

        var deserialized = JsonSerializer.Deserialize<List<LocationPoint>>(obj1.LocationPointsJson!);
        Assert.NotNull(deserialized);
        Assert.Single(deserialized);

        var retrieved = deserialized[0];
        Assert.Equal(extremePoint.X, retrieved.X, 5);
        Assert.Equal(extremePoint.Y, retrieved.Y, 5);
        Assert.Equal(extremePoint.Z, retrieved.Z, 5);
        Assert.Equal("level3", retrieved.FloorId);
    }

    [Fact]
    public async Task Persistence_SqlInjectionInObjectiveId_DoesNotCorruptDatabase()
    {
        string injectionId = "obj_adv_1'; DROP TABLE Quests;--";
        var point = new LocationPoint(1, 2, 3, "main");

        // Should safely execute as a parameterized query affecting 0 rows
        var result = await _service.SaveObjectiveLocationAsync(injectionId, point, _testDbPath);
        Assert.False(result);

        // Verify Quests table is completely intact
        using var connection = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Quests";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(1, count);
    }

    #endregion

    #region Concurrency Stress Testing

    [Fact]
    public void Concurrency_SimultaneousTransformationsAcrossThreads_ThreadSafeAndConsistent()
    {
        var maps = new[] { "Customs", "Woods", "Interchange", "Shoreline", "Factory", "Reserve", "Lighthouse", "StreetsOfTarkov" };
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 100, i =>
        {
            try
            {
                var map = maps[i % maps.Length];
                var canvasX = 1000.0 + (i * 10.5);
                var canvasY = 800.0 + (i * 8.2);

                var success = _service.TryCalculateWorldCoordinates(map, canvasX, canvasY, null, out var locPoint);
                if (!success || locPoint == null)
                {
                    exceptions.Add(new Exception($"TryCalculateWorldCoordinates returned false for map {map} at iteration {i}"));
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    #endregion
}
