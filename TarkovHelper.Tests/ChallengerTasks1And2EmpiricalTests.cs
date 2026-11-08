using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Pages.Map.ViewModels;
using TarkovHelper.Services;
using TarkovHelper.Services.Map;
using Xunit;

namespace TarkovHelper.Tests;

/// <summary>
/// Empirical challenge test harness by challenger_proj6_1.
/// Adversarially stress-tests Tasks 1 & 2:
/// - True factory restoration & zero wipe to NULL for factory coordinates (including "A Helping Hand" Hj6qhH-Lwm8HT0CNbSHud1).
/// - Lifecycle of user relocation followed by RestoreObjectiveToFactoryAsync and RestoreAllObjectivesToFactoryAsync.
/// - Objectives originally with NULL coordinates.
/// - Relocation Manager search filtering, history queries, and audit logging.
/// - Concurrency, bootstrap idempotency, and security protections against writing to user_data.db.
/// - Drawer card ContextMenu and right-click cancel verification.
/// - Local publish self-contained runtimeconfig validation.
/// </summary>
[Collection("DatabaseUpdateServiceCollection")]
public class ChallengerTasks1And2EmpiricalTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testDbPath;
    private readonly string _realDbPath;
    private readonly QuestObjectiveRelocationService _service;

    public ChallengerTasks1And2EmpiricalTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_Chal1_2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        _testDbPath = Path.Combine(_testDir, "tarkov_data.db");
        _realDbPath = FindRealProductionDatabase();

        // If real production database exists, copy it to create our test database; otherwise synthesize
        if (File.Exists(_realDbPath))
        {
            File.Copy(_realDbPath, _testDbPath, overwrite: true);
        }
        else
        {
            CreateSynthesizedDatabase(_testDbPath);
        }

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

    private static string FindRealProductionDatabase()
    {
        var possibleLocations = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "tarkov_data.db"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "TarkovHelper", "Assets", "tarkov_data.db"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "tarkov_data.db"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "publish", "tarkov_data.db"),
            Path.Combine(Directory.GetCurrentDirectory(), "tarkov_data.db"),
            Path.Combine(Directory.GetCurrentDirectory(), "publish", "tarkov_data.db"),
            Path.Combine(Directory.GetCurrentDirectory(), "TarkovHelper", "Assets", "tarkov_data.db")
        };

        foreach (var loc in possibleLocations)
        {
            try
            {
                var full = Path.GetFullPath(loc);
                if (File.Exists(full) && new FileInfo(full).Length > 1_000_000)
                {
                    return full;
                }
            }
            catch { }
        }

        return string.Empty;
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
                PlayerMarkerTransform = new double[] { -2.0, 0, 0, 2.0, 2600, 1601.5 }
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

    private static void CreateSynthesizedDatabase(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path};Mode=ReadWriteCreate");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
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
                ObjectiveType TEXT NOT NULL DEFAULT 'Custom',
                Description TEXT NOT NULL,
                TargetType TEXT,
                MapName TEXT,
                LocationPoints TEXT,
                OriginalLocationPoints TEXT,
                IsCustomized INTEGER NOT NULL DEFAULT 0,
                IsApproved INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT
            );

            CREATE TABLE QuestObjectiveRelocationLog (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ObjectiveId TEXT NOT NULL,
                QuestName TEXT,
                ObjectiveDescription TEXT,
                MapName TEXT,
                PreviousLocationPoints TEXT,
                NewLocationPoints TEXT,
                Action TEXT NOT NULL,
                Timestamp TEXT NOT NULL
            );

            INSERT INTO Quests (Id, Name, Trader, Location)
            VALUES ('q_helping_hand', 'A Helping Hand', 'Prapor', 'Woods');

            INSERT INTO QuestObjectives (Id, QuestId, SortOrder, Description, MapName, LocationPoints, OriginalLocationPoints, IsCustomized, IsApproved)
            VALUES ('Hj6qhH-Lwm8HT0CNbSHud1', 'q_helping_hand', 0, 'Find the folder in dorms 214', 'Woods',
                    '[{""X"":-613.2537800616469,""Y"":0,""Z"":131.97157464649376,""FloorId"":null}]',
                    '[{""X"":-613.2537800616469,""Y"":0,""Z"":131.97157464649376,""FloorId"":null}]', 0, 1);
        ";
        cmd.ExecuteNonQuery();
    }

    #region 1. Production Database Inspection & A Helping Hand Verification

    [Fact]
    public async Task ProductionDatabase_ContainsHelpingHandWithPreservedFactoryCoordinates()
    {
        Assert.True(File.Exists(_realDbPath), $"Production tarkov_data.db must exist, found at: '{_realDbPath}'");

        // Ensure schema bootstrap runs cleanly
        await DatabaseBootstrapService.Instance.EnsureDatabaseSchemaAsync(_realDbPath);

        using var conn = new SqliteConnection($"Data Source={_realDbPath};Mode=ReadOnly");
        await conn.OpenAsync();

        // 1. Verify Helping Hand factory coordinates
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, LocationPoints, OriginalLocationPoints, IsCustomized
            FROM QuestObjectives
            WHERE Id = 'Hj6qhH-Lwm8HT0CNbSHud1'";

        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Objective 'Hj6qhH-Lwm8HT0CNbSHud1' (A Helping Hand) must exist in tarkov_data.db!");

        var id = reader.GetString(0);
        var loc = reader.GetString(1);
        var orig = reader.GetString(2);
        var isCustomized = reader.GetInt32(3);

        Assert.Equal("Hj6qhH-Lwm8HT0CNbSHud1", id);
        Assert.False(string.IsNullOrWhiteSpace(loc), "LocationPoints must not be null or empty!");
        Assert.False(string.IsNullOrWhiteSpace(orig), "OriginalLocationPoints must not be null or empty!");
        Assert.Contains("-613.25", loc);
        Assert.Contains("131.97", loc);
        Assert.Equal(orig, loc);
        Assert.Equal(0, isCustomized);

        // 2. Verify total factory coordinates backfilled is at least 461
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM QuestObjectives WHERE OriginalLocationPoints IS NOT NULL AND OriginalLocationPoints != ''";
        var origCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        Assert.True(origCount >= 461, $"Expected at least 461 factory coordinates in OriginalLocationPoints, but found {origCount}");
    }

    #endregion

    #region 2. True Factory Restoration & Never Wiped to NULL

    [Fact]
    public async Task TrueFactoryRestore_AHelpingHand_RelocateAndRestore_PreservesFactoryCoordinates_NeverNull()
    {
        await DatabaseBootstrapService.Instance.EnsureDatabaseSchemaAsync(_testDbPath);

        const string helpingHandId = "Hj6qhH-Lwm8HT0CNbSHud1";

        // Query initial factory coordinates
        string initialFactoryCoords;
        using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT OriginalLocationPoints FROM QuestObjectives WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", helpingHandId);
            initialFactoryCoords = (string)(await cmd.ExecuteScalarAsync() ?? string.Empty);
            Assert.False(string.IsNullOrEmpty(initialFactoryCoords));
        }

        // Step 1: User relocates "A Helping Hand" to new coordinates
        var userPoint = new LocationPoint(1234.5, 12.0, -876.5, "floor_custom");
        var relocated = await _service.SaveObjectiveLocationAsync(helpingHandId, userPoint, _testDbPath);
        Assert.True(relocated);

        // Step 2: Verify database state after relocation
        using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT LocationPoints, OriginalLocationPoints, IsCustomized FROM QuestObjectives WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", helpingHandId);
            using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            var currentLoc = reader.GetString(0);
            var origLoc = reader.GetString(1);
            var isCustomized = reader.GetInt32(2);

            Assert.Contains("1234.5", currentLoc);
            Assert.Equal(initialFactoryCoords, origLoc); // Original preserved!
            Assert.Equal(1, isCustomized); // Marked as customized
        }

        // Step 3: Perform True Factory Restoration
        var restored = await _service.RestoreObjectiveToFactoryAsync(helpingHandId, _testDbPath);
        Assert.True(restored);

        // Step 4: EMPIRICAL PROOF: Must NOT be NULL, must equal initial factory coords, IsCustomized == 0
        using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT LocationPoints, OriginalLocationPoints, IsCustomized FROM QuestObjectives WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", helpingHandId);
            using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            Assert.False(reader.IsDBNull(0), "CRITICAL FAILURE: Factory objective was wiped to NULL upon restoration!");
            var finalLoc = reader.GetString(0);
            var finalOrig = reader.GetString(1);
            var finalCustomized = reader.GetInt32(2);

            Assert.Equal(initialFactoryCoords, finalLoc);
            Assert.Equal(initialFactoryCoords, finalOrig);
            Assert.Equal(0, finalCustomized);
        }
    }

    [Fact]
    public async Task TrueFactoryRestore_RepeatedCycles_MaintainsOriginalCoordinatesIndefinitely()
    {
        await DatabaseBootstrapService.Instance.EnsureDatabaseSchemaAsync(_testDbPath);
        const string helpingHandId = "Hj6qhH-Lwm8HT0CNbSHud1";

        string initialFactoryCoords;
        using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT OriginalLocationPoints FROM QuestObjectives WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", helpingHandId);
            initialFactoryCoords = (string)(await cmd.ExecuteScalarAsync() ?? string.Empty);
        }

        // Run 5 cycles of user relocate followed by restore
        for (int i = 1; i <= 5; i++)
        {
            var userPt = new LocationPoint(i * 100.0, i * 2.0, i * -50.0, "floor_" + i);
            var saveOk = await _service.SaveObjectiveLocationAsync(helpingHandId, userPt, _testDbPath);
            Assert.True(saveOk);

            var restoreOk = await _service.RestoreObjectiveToFactoryAsync(helpingHandId, _testDbPath);
            Assert.True(restoreOk);

            using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT LocationPoints, OriginalLocationPoints, IsCustomized FROM QuestObjectives WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", helpingHandId);
            using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            Assert.Equal(initialFactoryCoords, reader.GetString(0));
            Assert.Equal(initialFactoryCoords, reader.GetString(1));
            Assert.Equal(0, reader.GetInt32(2));
        }

        // Verify audit log has 10 entries for this objective (5 Relocated, 5 Restored)
        var logs = await _service.GetRelocationHistoryLogsAsync(100, _testDbPath);
        var objLogs = logs.Where(l => l.ObjectiveId == helpingHandId).ToList();
        Assert.True(objLogs.Count >= 10);
    }

    [Fact]
    public async Task TrueFactoryRestore_ObjectiveOriginallyWithNullCoordinates_RestoresCleanlyToNull()
    {
        await DatabaseBootstrapService.Instance.EnsureDatabaseSchemaAsync(_testDbPath);

        // Find or create an objective that originally had NULL coordinates
        string nullObjId;
        using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadWrite"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id FROM QuestObjectives WHERE OriginalLocationPoints IS NULL OR OriginalLocationPoints = '' LIMIT 1";
            var existingId = await cmd.ExecuteScalarAsync() as string;

            if (string.IsNullOrEmpty(existingId))
            {
                nullObjId = "obj_synth_null_" + Guid.NewGuid().ToString("N");
                using var insCmd = conn.CreateCommand();
                insCmd.CommandText = @"
                    INSERT INTO QuestObjectives (Id, QuestId, SortOrder, Description, MapName, LocationPoints, OriginalLocationPoints, IsCustomized)
                    VALUES (@id, 'q_synth', 0, 'Synthesized objective without coordinates', 'Customs', NULL, NULL, 0)";
                insCmd.Parameters.AddWithValue("@id", nullObjId);
                await insCmd.ExecuteNonQueryAsync();
            }
            else
            {
                nullObjId = existingId;
            }
        }

        // Relocate objective
        var pt = new LocationPoint(450.0, 0.0, 750.0, null);
        var saveResult = await _service.SaveObjectiveLocationAsync(nullObjId, pt, _testDbPath);
        Assert.True(saveResult);

        // Verify customized
        using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT LocationPoints, OriginalLocationPoints, IsCustomized FROM QuestObjectives WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", nullObjId);
            using var r = await cmd.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.False(r.IsDBNull(0));
            Assert.True(r.IsDBNull(1));
            Assert.Equal(1, r.GetInt32(2));
        }

        // Restore to factory
        var restoreResult = await _service.RestoreObjectiveToFactoryAsync(nullObjId, _testDbPath);
        Assert.True(restoreResult);

        // Verify restored to NULL without throwing
        using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT LocationPoints, OriginalLocationPoints, IsCustomized FROM QuestObjectives WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", nullObjId);
            using var r = await cmd.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.True(r.IsDBNull(0), "Objective that originally had NULL coordinates must restore to NULL!");
            Assert.True(r.IsDBNull(1));
            Assert.Equal(0, r.GetInt32(2));
        }
    }

    #endregion

    #region 3. Bulk Factory Restoration & History Queries

    [Fact]
    public async Task BulkFactoryRestore_RestoresAllCustomizedObjectivesSimultaneously()
    {
        await DatabaseBootstrapService.Instance.EnsureDatabaseSchemaAsync(_testDbPath);

        // Select 2 factory objectives and 1 null objective
        var targets = new List<string>();
        using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id FROM QuestObjectives WHERE OriginalLocationPoints IS NOT NULL LIMIT 2";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                targets.Add(r.GetString(0));
        }

        // Insert 1 synthetic null objective
        var synthNullId = "synth_null_bulk_" + Guid.NewGuid().ToString("N");
        using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadWrite"))
        {
            await conn.OpenAsync();
            using var insCmd = conn.CreateCommand();
            insCmd.CommandText = @"
                INSERT OR IGNORE INTO Quests (Id, Name, Trader) VALUES ('q_bulk', 'Bulk Quest', 'Prapor');
                INSERT INTO QuestObjectives (Id, QuestId, Description, MapName, LocationPoints, OriginalLocationPoints, IsCustomized) VALUES (@id, 'q_bulk', 'Bulk null', 'Woods', NULL, NULL, 0);";
            insCmd.Parameters.AddWithValue("@id", synthNullId);
            await insCmd.ExecuteNonQueryAsync();
        }
        targets.Add(synthNullId);

        // Customize all 3 targets
        foreach (var tId in targets)
        {
            await _service.SaveObjectiveLocationAsync(tId, new LocationPoint(888.0, 0, 999.0, null), _testDbPath);
        }

        // Verify history lists all customized objectives
        var historyList = await _service.GetCustomizedObjectivesAsync(_testDbPath);
        Assert.True(historyList.Count >= 3);
        foreach (var tId in targets)
        {
            Assert.Contains(historyList, h => h.ObjectiveId == tId);
        }

        // Execute bulk restoration
        var restoredCount = await _service.RestoreAllObjectivesToFactoryAsync(_testDbPath);
        Assert.True(restoredCount >= 3);

        // Verify history is now completely empty
        var postHistory = await _service.GetCustomizedObjectivesAsync(_testDbPath);
        Assert.Empty(postHistory);

        // Verify factory objectives restored to factory coords (not null) and null restored to null
        using (var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
        {
            await conn.OpenAsync();
            foreach (var tId in targets.Take(2))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT LocationPoints, OriginalLocationPoints, IsCustomized FROM QuestObjectives WHERE Id = @id";
                cmd.Parameters.AddWithValue("@id", tId);
                using var r = await cmd.ExecuteReaderAsync();
                Assert.True(await r.ReadAsync());
                Assert.False(r.IsDBNull(0));
                Assert.Equal(r.GetString(1), r.GetString(0));
                Assert.Equal(0, r.GetInt32(2));
            }

            // Synth null objective
            using var nullCmd = conn.CreateCommand();
            nullCmd.CommandText = "SELECT LocationPoints, IsCustomized FROM QuestObjectives WHERE Id = @id";
            nullCmd.Parameters.AddWithValue("@id", synthNullId);
            using var nr = await nullCmd.ExecuteReaderAsync();
            Assert.True(await nr.ReadAsync());
            Assert.True(nr.IsDBNull(0));
            Assert.Equal(0, nr.GetInt32(1));
        }
    }

    #endregion

    #region 4. Search Filtering & Audit Logging

    [Fact]
    public async Task RelocationManager_SearchFiltering_StressTest()
    {
        await DatabaseBootstrapService.Instance.EnsureDatabaseSchemaAsync(_testDbPath);

        // 1. Text Search
        var textResults = await _service.SearchObjectivesAsync("Helping", null, null, geographicOnly: false, targetDbPath: _testDbPath);
        Assert.Contains(textResults, r => r.ObjectiveId == "Hj6qhH-Lwm8HT0CNbSHud1" || r.QuestName.Contains("Helping"));

        // 2. Trader filter: Prapor
        var praporResults = await _service.SearchObjectivesAsync(null, "Prapor", null, geographicOnly: false, targetDbPath: _testDbPath);
        Assert.NotEmpty(praporResults);
        Assert.All(praporResults, r => Assert.Equal("Prapor", r.Trader));

        // 3. Map filter: Woods
        var woodsResults = await _service.SearchObjectivesAsync(null, null, "Woods", geographicOnly: false, targetDbPath: _testDbPath);
        Assert.NotEmpty(woodsResults);
        Assert.All(woodsResults, r => Assert.Contains("Woods", r.MapName, StringComparison.OrdinalIgnoreCase));

        // 4. Missing coordinates filter
        var missingResults = await _service.SearchObjectivesAsync(null, null, null, geographicOnly: true, missingCoordinatesOnly: true, targetDbPath: _testDbPath);
        Assert.All(missingResults, r => Assert.False(r.HasLocation));
    }

    [Fact]
    public async Task AuditLogging_AccuratelyTracksPreviousAndNewCoordinates()
    {
        await DatabaseBootstrapService.Instance.EnsureDatabaseSchemaAsync(_testDbPath);
        const string objId = "Hj6qhH-Lwm8HT0CNbSHud1";

        // Relocate
        var newPt = new LocationPoint(777.7, 0, 888.8, "f1");
        await _service.SaveObjectiveLocationAsync(objId, newPt, _testDbPath);

        // Restore
        await _service.RestoreObjectiveToFactoryAsync(objId, _testDbPath);

        // Inspect audit logs
        var logs = await _service.GetRelocationHistoryLogsAsync(50, _testDbPath);
        var recentLogs = logs.Where(l => l.ObjectiveId == objId).Take(2).ToList();

        Assert.Equal(2, recentLogs.Count);
        Assert.Equal("Restored", recentLogs[0].Action);
        Assert.Equal("Relocated", recentLogs[1].Action);

        Assert.Contains("777.7", recentLogs[0].PreviousLocationPoints);
        Assert.Contains("777.7", recentLogs[1].NewLocationPoints);
        Assert.False(string.IsNullOrEmpty(recentLogs[0].Timestamp));
    }

    #endregion

    #region 5. Adversarial & Edge Case Tests

    [Fact]
    public async Task Security_StrictlyForbidsTargetingUserDataDb()
    {
        var userDataPath = Path.Combine(_testDir, "user_data.db");
        File.WriteAllText(userDataPath, "dummy");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SaveObjectiveLocationAsync("test_id", new LocationPoint(0, 0, 0, null), userDataPath));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RestoreObjectiveToFactoryAsync("test_id", userDataPath));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ClearObjectiveLocationAsync("test_id", userDataPath));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseBootstrapService.Instance.EnsureDatabaseSchemaAsync(userDataPath));
    }

    [Fact]
    public async Task BootstrapIdempotency_DoesNotOverwriteCustomizedRecordsOnRepeatedRuns()
    {
        await DatabaseBootstrapService.Instance.EnsureDatabaseSchemaAsync(_testDbPath);
        const string objId = "Hj6qhH-Lwm8HT0CNbSHud1";

        // Customize the objective
        await _service.SaveObjectiveLocationAsync(objId, new LocationPoint(999, 0, 999, null), _testDbPath);

        // Run bootstrap 3 times repeatedly
        for (int i = 0; i < 3; i++)
        {
            await DatabaseBootstrapService.Instance.EnsureDatabaseSchemaAsync(_testDbPath);
        }

        // Verify it was NOT overwritten back to factory by bootstrap
        using var conn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT LocationPoints, OriginalLocationPoints, IsCustomized FROM QuestObjectives WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", objId);
        using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());

        Assert.Contains("999", r.GetString(0)); // Still customized!
        Assert.Contains("-613.25", r.GetString(1)); // Original still preserved!
        Assert.Equal(1, r.GetInt32(2)); // IsCustomized still 1!
    }

    [Fact]
    public async Task ErrorHandling_NonExistentObjectiveId_ReturnsFalseWithoutThrowing()
    {
        await DatabaseBootstrapService.Instance.EnsureDatabaseSchemaAsync(_testDbPath);

        var saveResult = await _service.SaveObjectiveLocationAsync("non_existent_id_12345", new LocationPoint(0, 0, 0, null), _testDbPath);
        Assert.False(saveResult);

        var restoreResult = await _service.RestoreObjectiveToFactoryAsync("non_existent_id_12345", _testDbPath);
        Assert.False(restoreResult);
    }

    [Fact]
    public async Task Validation_EmptyObjectiveId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SaveObjectiveLocationAsync("", new LocationPoint(0, 0, 0, null), _testDbPath));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.RestoreObjectiveToFactoryAsync("   ", _testDbPath));
    }

    #endregion

    #region 6. UI XAML Inspection: Drawer Card Cleanup & ContextMenu

    [Fact]
    public void MapPageXaml_QuestObjectiveTemplate_HasNoInvasiveButtons_AndHasContextMenu()
    {
        var mapPageXamlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "TarkovHelper", "Pages", "Map", "MapPage.xaml");
        if (!File.Exists(mapPageXamlPath))
        {
            mapPageXamlPath = Path.Combine(Directory.GetCurrentDirectory(), "TarkovHelper", "Pages", "Map", "MapPage.xaml");
        }
        Assert.True(File.Exists(mapPageXamlPath), $"MapPage.xaml not found at: {mapPageXamlPath}");

        var xamlText = File.ReadAllText(mapPageXamlPath);

        // 1. Assert invasive buttons [📍 Reubicar] and [❌] are completely absent from the template
        Assert.DoesNotContain("BtnRelocateObjective", xamlText);
        Assert.DoesNotContain("BtnRevertObjective", xamlText);

        // 2. Assert Border.ContextMenu is present in QuestObjectiveTemplate
        Assert.Contains("<Border.ContextMenu>", xamlText);
        Assert.Contains("MenuCardRelocate_Click", xamlText);
        Assert.Contains("MenuCardRevert_Click", xamlText);
        Assert.Contains("MenuCardCenter_Click", xamlText);
        Assert.Contains("MenuCardOpenManager_Click", xamlText);

        // 3. Assert toolbar contains dedicated button for Relocation Manager
        Assert.Contains("BtnOpenRelocationManager", xamlText);
    }

    [Fact]
    public void PlacementCancellation_RightClickAndCancelState_ResetsCleanly()
    {
        // Start placement
        _service.StartPlacement("test_obj_id", "Test Description", "Test Quest", "Woods");
        Assert.True(_service.IsPlacementActive);
        Assert.Equal("test_obj_id", _service.TargetObjectiveId);
        Assert.Equal("Woods", _service.TargetMapKey);

        // Cancel placement (simulating right-click preview handler or cancel button)
        _service.CancelPlacement();
        Assert.False(_service.IsPlacementActive);
        Assert.Null(_service.TargetObjectiveId);
        Assert.Null(_service.TargetMapKey);
    }

    [Fact]
    public void QuestObjectiveViewModel_MaintainsBackwardCompatibilityProperties()
    {
        var obj = new TaskObjectiveWithLocation
        {
            ObjectiveId = "test_vm_obj",
            QuestId = "q_test",
            TaskName = "Test Quest",
            Type = "Visit",
            Description = "Visit the water treatment plant",
            Locations = new List<QuestObjectiveLocation> { new() { Id = "loc_1", X = 100, Y = 0, Z = 200 } }
        };

        var vm = new QuestObjectiveViewModel(obj, LocalizationService.Instance, null, false, "Lighthouse");

        Assert.True(vm.CanRelocate);
        Assert.True(vm.HasLocation);
        Assert.Equal("📍 Reubicar", vm.RelocateButtonText);
        Assert.Equal(Visibility.Visible, vm.RevertVisibility);

        // Also test non-geographic objective
        var nonGeoObj = new TaskObjectiveWithLocation
        {
            ObjectiveId = "test_non_geo",
            QuestId = "q_test",
            TaskName = "Test Quest",
            Type = "Survive",
            Description = "Survive and extract from Lighthouse",
            Locations = new List<QuestObjectiveLocation>()
        };

        var nonGeoVm = new QuestObjectiveViewModel(nonGeoObj, LocalizationService.Instance, null, false, "Lighthouse");
        Assert.False(nonGeoVm.CanRelocate);
        Assert.False(nonGeoVm.HasLocation);
        Assert.Equal(Visibility.Collapsed, nonGeoVm.RelocateVisibility);
        Assert.Equal(Visibility.Collapsed, nonGeoVm.RevertVisibility);
    }

    #endregion

    #region 7. Publish & RuntimeConfig Inspection

    [Fact]
    public void PublishDirectory_RuntimeConfigContainsIncludedFrameworks_AndPreservesDatabase()
    {
        var publishDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "publish");
        if (!Directory.Exists(publishDir))
        {
            publishDir = Path.Combine(Directory.GetCurrentDirectory(), "publish");
        }

        Assert.True(Directory.Exists(publishDir), $"Publish directory not found at: {publishDir}");

        var runtimeConfigFile = Path.Combine(publishDir, "TarkovHelper.runtimeconfig.json");
        Assert.True(File.Exists(runtimeConfigFile), "TarkovHelper.runtimeconfig.json must exist in publish/");

        var json = File.ReadAllText(runtimeConfigFile);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var runtimeOptions = root.GetProperty("runtimeOptions");
        Assert.True(runtimeOptions.TryGetProperty("includedFrameworks", out var includedFrameworks),
            "Publish configuration MUST contain 'includedFrameworks' (self-contained mode)!");
        Assert.Equal(JsonValueKind.Array, includedFrameworks.ValueKind);

        Assert.False(runtimeOptions.TryGetProperty("frameworks", out _),
            "Publish configuration MUST NOT contain 'frameworks' (would cause Windows host error)!");

        // Check database preservation
        var publishDb = Path.Combine(publishDir, "tarkov_data.db");
        Assert.True(File.Exists(publishDb), "publish/tarkov_data.db must exist and be preserved!");
        var dbInfo = new FileInfo(publishDb);
        Assert.True(dbInfo.Length > 1_000_000, $"publish/tarkov_data.db must not be empty (was {dbInfo.Length} bytes)");
    }

    #endregion
}
