using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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

public static class QuestObjectiveDbServiceVerificationExtensions
{
    /// <summary>
    /// Direct extension method enabling QuestObjectiveDbService.Instance.GetObjectives()
    /// as specified in the test mission contract.
    /// </summary>
    public static IReadOnlyList<QuestObjective> GetObjectives(this QuestObjectiveDbService service)
    {
        return service.AllObjectives;
    }
}

/// <summary>
/// Empirical adversarial stress test harness for Block 5 (Interactive Quest Objective Map Relocation Tool).
/// Stress-tests:
/// 1. user_data.db strict protection (InvalidOperationException + SHA256 immutability)
/// 2. High-concurrency rapid placement calls (StartPlacement, CancelPlacement, rapid clicks)
/// 3. Non-existent objective ID handling (Save, Clear, Query)
/// 4. Special characters & malformed JSON injection in objectiveId and FloorId
/// 5. In-memory cache update immediacy (QuestObjectiveDbService.GetObjectives() reflects changes)
/// </summary>
[Collection("DatabaseUpdateServiceCollection")]
public class RelocationAdversarialStressTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testDbPath;
    private readonly string _userDataDbPath;
    private readonly QuestObjectiveRelocationService _service;

    public RelocationAdversarialStressTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_RelocAdv_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        _testDbPath = Path.Combine(_testDir, "tarkov_data.db");
        CreateTestDatabase(_testDbPath);

        _userDataDbPath = Path.Combine(_testDir, "user_data.db");
        CreateUserDataDatabase(_userDataDbPath);

        _service = QuestObjectiveRelocationService.Instance;
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

    #region Database & Map Setup

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
            VALUES ('q_customs_fuel', 'A Fuel Matter', 'Prapor', 'Customs', '2026-01-01T00:00:00Z'),
                   ('q_woods_intro', 'Introduction', 'Mechanic', 'Woods', '2026-01-01T00:00:00Z');

            INSERT INTO QuestObjectives (Id, QuestId, SortOrder, Description, MapName, LocationPoints, IsApproved)
            VALUES ('obj_fuel_1', 'q_customs_fuel', 0, 'Mark first fuel tank', 'Customs', NULL, 0),
                   ('obj_fuel_2', 'q_customs_fuel', 1, 'Mark second fuel tank', 'Customs', NULL, 0),
                   ('obj_woods_camp', 'q_woods_intro', 0, 'Find Jaeger camp', 'Woods', NULL, 0);

            INSERT INTO MapFloorLocations (MapKey, FloorId, MinY, MaxY, Priority)
            VALUES ('Customs', 'main', -1.0, 3.0, 1),
                   ('Customs', 'basement', -8.0, -2.0, 1),
                   ('Customs', 'level2', 4.0, 8.0, 1);
        ";
        cmd.ExecuteNonQuery();
    }

    private static void CreateUserDataDatabase(string path)
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

            CREATE TABLE UserDataVersion (
                Version INTEGER PRIMARY KEY,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE QuestProgress (
                QuestId TEXT PRIMARY KEY,
                Status TEXT NOT NULL,
                CompletedAt TEXT,
                Notes TEXT
            );

            CREATE TABLE HideoutProgress (
                StationId TEXT PRIMARY KEY,
                Level INTEGER NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE CustomNotes (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                Content TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            INSERT INTO UserDataVersion (Version, UpdatedAt) VALUES (1, '2026-01-01T00:00:00Z');
            INSERT INTO QuestProgress (QuestId, Status, CompletedAt, Notes) VALUES ('q_customs_fuel', 'Active', NULL, 'Personal secret note');
            INSERT INTO HideoutProgress (StationId, Level, UpdatedAt) VALUES ('medstation', 2, '2026-01-01T00:00:00Z');
            INSERT INTO CustomNotes (Id, Title, Content, CreatedAt) VALUES ('note_1', 'Spawn plan', 'Head towards dorms fast', '2026-01-01T00:00:00Z');
        ";
        cmd.ExecuteNonQuery();
    }

    private static string ComputeFileSha256(string filePath)
    {
        SqliteConnection.ClearAllPools();
        using var sha256 = SHA256.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    #endregion

    #region 1. user_data.db Protection & SHA256 Immutability

    [Fact]
    public async Task SaveObjectiveLocationAsync_TargetingUserDataDb_DirectAndAdversarialPaths_ThrowsAndPreservesSha256()
    {
        var initialHash = ComputeFileSha256(_userDataDbPath);
        var initialSize = new FileInfo(_userDataDbPath).Length;
        var point = new LocationPoint(100.0, 2.0, 300.0, "main");

        // Adversarial path permutations targeting user_data.db
        var badPaths = new[]
        {
            _userDataDbPath,
            Path.Combine(_testDir, "USER_DATA.DB"),
            Path.Combine(_testDir, "User_Data.Db"),
            Path.Combine(_testDir, ".", "user_data.db"),
            Path.Combine(_testDir, "sub", "..", "user_data.db"),
            _userDataDbPath.ToLowerInvariant(),
            _userDataDbPath.ToUpperInvariant()
        };

        foreach (var badPath in badPaths)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.SaveObjectiveLocationAsync("obj_fuel_1", point, badPath));

            Assert.Contains("user_data.db", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Verify SHA256 immutability & file size
        var finalHash = ComputeFileSha256(_userDataDbPath);
        var finalSize = new FileInfo(_userDataDbPath).Length;
        Assert.Equal(initialHash, finalHash);
        Assert.Equal(initialSize, finalSize);

        // Verify SQLite data inside user_data.db remains 100% intact
        using var connection = new SqliteConnection($"Data Source={_userDataDbPath};Mode=ReadOnly");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM QuestProgress WHERE QuestId = 'q_customs_fuel' AND Status = 'Active'";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ClearObjectiveLocationAsync_TargetingUserDataDb_DirectAndAdversarialPaths_ThrowsAndPreservesSha256()
    {
        var initialHash = ComputeFileSha256(_userDataDbPath);
        var initialSize = new FileInfo(_userDataDbPath).Length;

        var badPaths = new[]
        {
            _userDataDbPath,
            Path.Combine(_testDir, "USER_DATA.DB"),
            Path.Combine(_testDir, "sub", "..", "user_data.db")
        };

        foreach (var badPath in badPaths)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.ClearObjectiveLocationAsync("obj_fuel_1", badPath));

            Assert.Contains("user_data.db", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        var finalHash = ComputeFileSha256(_userDataDbPath);
        var finalSize = new FileInfo(_userDataDbPath).Length;
        Assert.Equal(initialHash, finalHash);
        Assert.Equal(initialSize, finalSize);
    }

    #endregion

    #region 2. Concurrency & Rapid Placement Lifecycle Stress Tests

    [Fact]
    public void ConcurrentRapidPlacementStateTransitions_HighContention_DoesNotDeadlockOrThrow()
    {
        const int threadCount = 40;
        const int iterationsPerThread = 60;
        int stateChangedEventCount = 0;

        EventHandler handler = (s, e) => Interlocked.Increment(ref stateChangedEventCount);
        _service.PlacementStateChanged += handler;

        try
        {
            Parallel.For(0, threadCount, threadIndex =>
            {
                var rnd = new Random(threadIndex * 17);
                for (int i = 0; i < iterationsPerThread; i++)
                {
                    if (rnd.Next(2) == 0)
                    {
                        _service.StartPlacement(
                            $"obj_t{threadIndex}_{i}",
                            $"Desc {threadIndex}_{i}",
                            $"Quest {threadIndex}",
                            rnd.Next(2) == 0 ? "Customs" : "Woods");
                    }
                    else
                    {
                        _service.CancelPlacement();
                    }
                }
            });
        }
        finally
        {
            _service.PlacementStateChanged -= handler;
            _service.CancelPlacement();
        }

        Assert.False(_service.IsPlacementActive);
        Assert.Null(_service.TargetObjectiveId);
        Assert.True(stateChangedEventCount > 0, "PlacementStateChanged events should have fired under contention.");
    }

    [Fact]
    public async Task ConcurrentRapidSaves_MultipleThreads_PersistsValidStateWithoutCorruption()
    {
        // Populate 10 separate objectives
        using (var setupConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadWrite"))
        {
            setupConn.Open();
            using var cmd = setupConn.CreateCommand();
            for (int i = 10; i < 20; i++)
            {
                cmd.CommandText = $"INSERT INTO QuestObjectives (Id, QuestId, SortOrder, Description, MapName) VALUES ('obj_concur_{i}', 'q_customs_fuel', {i}, 'Concur Obj {i}', 'Customs');";
                cmd.ExecuteNonQuery();
            }
        }

        // Run 20 concurrent tasks saving locations rapidly
        var tasks = Enumerable.Range(10, 10).Select(async i =>
        {
            var point = new LocationPoint(100.0 + i, 1.0, 200.0 + i, "main");
            var res = await _service.SaveObjectiveLocationAsync($"obj_concur_{i}", point, _testDbPath);
            Assert.True(res);
        });

        await Task.WhenAll(tasks);

        // Verify SQLite database integrity & correctness for all 10 objectives
        using var verifyConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
        verifyConn.Open();
        using var checkCmd = verifyConn.CreateCommand();
        checkCmd.CommandText = "SELECT Id, LocationPoints, IsApproved FROM QuestObjectives WHERE Id LIKE 'obj_concur_%'";

        using var reader = checkCmd.ExecuteReader();
        int verifiedCount = 0;
        while (reader.Read())
        {
            verifiedCount++;
            var json = reader.GetString(1);
            var isApproved = reader.GetInt32(2);

            Assert.Equal(1, isApproved);
            var list = JsonSerializer.Deserialize<List<LocationPoint>>(json);
            Assert.NotNull(list);
            Assert.Single(list);
            Assert.Equal("main", list[0].FloorId);
        }

        Assert.Equal(10, verifiedCount);
    }

    [Fact]
    public void ConcurrentTryCalculateWorldCoordinates_StressTest_ConsistentAcrossThreads()
    {
        const int iterations = 1000;
        const double canvasX = 2960.88536;
        const double canvasY = 1336.80504;
        const double expectedGameX = -180.44268;
        const double expectedGameZ = -132.34748;

        Parallel.For(0, iterations, _ =>
        {
            var success = _service.TryCalculateWorldCoordinates("Customs", canvasX, canvasY, "level2", out var locPoint);
            Assert.True(success);
            Assert.NotNull(locPoint);
            Assert.Equal(expectedGameX, locPoint.X, 4);
            Assert.Equal(expectedGameZ, locPoint.Z, 4);
            Assert.Equal("level2", locPoint.FloorId);
        });
    }

    #endregion

    #region 3. Non-Existent Objective IDs and Missing Data Handling

    [Fact]
    public async Task SaveObjectiveLocationAsync_NonExistentObjectiveId_ReturnsFalseWithoutThrowingOrModifyingDb()
    {
        var point = new LocationPoint(50.0, 0.0, 50.0, "main");

        // Count objectives before call
        int countBefore;
        using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM QuestObjectives";
            countBefore = Convert.ToInt32(cmd.ExecuteScalar());
        }

        var result = await _service.SaveObjectiveLocationAsync("ghost_objective_does_not_exist_99999", point, _testDbPath);
        Assert.False(result);

        // Count objectives after call: must be strictly identical
        int countAfter;
        using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM QuestObjectives";
            countAfter = Convert.ToInt32(cmd.ExecuteScalar());
        }

        Assert.Equal(countBefore, countAfter);
    }

    [Fact]
    public async Task ClearObjectiveLocationAsync_NonExistentObjectiveId_ReturnsFalseWithoutThrowing()
    {
        var result = await _service.ClearObjectiveLocationAsync("ghost_objective_does_not_exist_99999", _testDbPath);
        Assert.False(result);
    }

    [Fact]
    public void GetObjectivesForQuest_NonExistentQuest_ReturnsEmptyListGracefully()
    {
        var syncResult = _service.GetObjectivesForQuest("TotallyUnknownNonExistentQuest_12345", _testDbPath);
        Assert.NotNull(syncResult);
        Assert.Empty(syncResult);
    }

    [Fact]
    public async Task GetObjectivesForQuestAsync_NonExistentQuest_ReturnsEmptyListGracefully()
    {
        var asyncResult = await _service.GetObjectivesForQuestAsync("TotallyUnknownNonExistentQuest_12345", _testDbPath);
        Assert.NotNull(asyncResult);
        Assert.Empty(asyncResult);
    }

    #endregion

    #region 4. Special Characters, SQL Injection & Malformed JSON Injection

    [Fact]
    public async Task SaveObjectiveLocationAsync_SqlInjectionInObjectiveId_DoesNotInjectOrCorruptSchema()
    {
        var loc = new LocationPoint(10.0, 0.0, 20.0, "main");

        var sqlInjectionPayloads = new[]
        {
            "' OR '1'='1",
            "obj_fuel_1'; DROP TABLE QuestObjectives; --",
            "obj_fuel_1'; DELETE FROM Quests; --",
            "obj' UNION SELECT 1,2,3,4,5; --",
            "obj_test_\"';--/*"
        };

        foreach (var payload in sqlInjectionPayloads)
        {
            // Execute save with injection payload as ID
            var res = await _service.SaveObjectiveLocationAsync(payload, loc, _testDbPath);
            Assert.False(res); // Payload does not match any existing ID
        }

        // Verify tables Quests and QuestObjectives are still completely intact
        using var connection = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Quests";
        var questCount = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(2, questCount);

        cmd.CommandText = "SELECT COUNT(*) FROM QuestObjectives";
        var objCount = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.True(objCount >= 3);
    }

    [Fact]
    public async Task SaveObjectiveLocationAsync_SpecialCharsAndMalformedJsonInFloorId_EscapedAndRoundTrippedCleanly()
    {
        var adversarialFloorIds = new[]
        {
            "\"}], \"injected\": true, [{\"X\": 999", // JSON injection attempt
            "floor\\\"/'\b\f\n\r\t",                   // JSON & control char escapes
            "<script>alert('xss')</script>",           // HTML/script injection
            "floor_地下_階層_🎮_测试",                  // Unicode & emoji
            new string('A', 2048)                      // Ultra-long floor ID
        };

        foreach (var maliciousFloor in adversarialFloorIds)
        {
            var point = new LocationPoint(123.456, 12.34, 789.012, maliciousFloor);

            var saveSuccess = await _service.SaveObjectiveLocationAsync("obj_fuel_1", point, _testDbPath);
            Assert.True(saveSuccess);

            // Read raw JSON from SQLite
            using var connection = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT LocationPoints FROM QuestObjectives WHERE Id = 'obj_fuel_1'";

            var rawJson = (string)cmd.ExecuteScalar()!;
            Assert.NotNull(rawJson);

            // Verify System.Text.Json can deserialize safely
            var deserialized = JsonSerializer.Deserialize<List<LocationPoint>>(rawJson);
            Assert.NotNull(deserialized);
            Assert.Single(deserialized);

            var loaded = deserialized[0];
            Assert.Equal(point.X, loaded.X, 4);
            Assert.Equal(point.Y, loaded.Y, 4);
            Assert.Equal(point.Z, loaded.Z, 4);
            Assert.Equal(maliciousFloor, loaded.FloorId);
        }
    }

    [Fact]
    public async Task QuestObjective_MalformedJsonInDatabase_GracefullyRecoversWithoutCrashing()
    {
        // Corrupt LocationPoints directly in database
        using (var connection = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadWrite"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE QuestObjectives SET LocationPoints = 'NOT_A_VALID_JSON_ARRAY{[(' WHERE Id = 'obj_fuel_1'";
            cmd.ExecuteNonQuery();
        }

        // Query via model: QuestObjective.LocationPointsJson handles corrupted string safely
        var obj = new QuestObjective
        {
            Id = "obj_fuel_1",
            LocationPointsJson = "NOT_A_VALID_JSON_ARRAY{[("
        };

        Assert.False(obj.HasCoordinates);
        Assert.Empty(obj.LocationPoints);

        // Now save valid coordinates over the corrupted database record
        var validPoint = new LocationPoint(50.0, 1.0, 60.0, "main");
        var saveResult = await _service.SaveObjectiveLocationAsync("obj_fuel_1", validPoint, _testDbPath);
        Assert.True(saveResult);

        // Verify repaired state
        using (var connection = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT LocationPoints, IsApproved FROM QuestObjectives WHERE Id = 'obj_fuel_1'";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());

            var repairedJson = reader.GetString(0);
            var isApproved = reader.GetInt32(1);

            Assert.Equal(1, isApproved);
            var points = JsonSerializer.Deserialize<List<LocationPoint>>(repairedJson);
            Assert.NotNull(points);
            Assert.Single(points);
            Assert.Equal(50.0, points[0].X);
        }
    }

    #endregion

    #region 5. In-Memory Cache Synchronization (QuestObjectiveDbService.GetObjectives())

    [Fact]
    public async Task SaveObjectiveLocationAsync_UpdatesQuestObjectiveDbServiceInMemoryImmediately()
    {
        var dbService = QuestObjectiveDbService.Instance;

        // Redirect QuestObjectiveDbService._databasePath to _testDbPath using reflection
        var dbPathField = typeof(QuestObjectiveDbService).GetField("_databasePath", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(dbPathField);

        var originalDbPath = (string)dbPathField.GetValue(dbService)!;
        try
        {
            dbPathField.SetValue(dbService, _testDbPath);

            // Baseline load
            var initialLoaded = await dbService.LoadObjectivesAsync();
            Assert.True(initialLoaded);

            // Initially obj_woods_camp has LocationPoints = NULL, so it is not in GetObjectives()
            var initialList = dbService.GetObjectives();
            Assert.DoesNotContain(initialList, o => o.Id == "obj_woods_camp");

            // Save new coordinates for obj_woods_camp
            var newPoint = new LocationPoint(-613.25378, 0.0, 131.97157, null);
            var saveSuccess = await _service.SaveObjectiveLocationAsync("obj_woods_camp", newPoint, _testDbPath);
            Assert.True(saveSuccess);

            // Verify in-memory cache updated immediately via GetObjectives() without app restart
            var updatedList = dbService.GetObjectives();
            var targetObj = updatedList.FirstOrDefault(o => o.Id == "obj_woods_camp");

            Assert.NotNull(targetObj);
            Assert.True(targetObj.HasCoordinates);
            Assert.Single(targetObj.LocationPoints);
            Assert.Equal(-613.25378, targetObj.LocationPoints[0].X, 4);
            Assert.Equal(131.97157, targetObj.LocationPoints[0].Z, 4);

            // Now clear coordinates
            var clearSuccess = await _service.ClearObjectiveLocationAsync("obj_woods_camp", _testDbPath);
            Assert.True(clearSuccess);

            // Verify in-memory cache reflects clearing immediately
            var clearedList = dbService.GetObjectives();
            Assert.DoesNotContain(clearedList, o => o.Id == "obj_woods_camp");
        }
        finally
        {
            dbPathField.SetValue(dbService, originalDbPath);
        }
    }

    #endregion
}
