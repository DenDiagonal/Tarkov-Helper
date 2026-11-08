using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Pages.Map.Components;
using TarkovHelper.Pages.Map.ViewModels;
using TarkovHelper.Services.Map;
using Xunit;

namespace TarkovHelper.Tests;

[Collection("DatabaseUpdateServiceCollection")]
public class RelocationManagerAndTooltipStabilizationTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testDbPath;
    private readonly QuestObjectiveRelocationService _service;

    public RelocationManagerAndTooltipStabilizationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_RelocMgrTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _testDbPath = Path.Combine(_testDir, "tarkov_data.db");
        CreateTestDatabase(_testDbPath);

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
                ObjectiveType TEXT NOT NULL DEFAULT 'Custom',
                Description TEXT NOT NULL,
                TargetType TEXT,
                MapName TEXT,
                LocationPoints TEXT,
                OriginalLocationPoints TEXT,
                IsCustomized INTEGER NOT NULL DEFAULT 0,
                IsApproved INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT,
                FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE
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

            INSERT INTO Quests (Id, Name, Trader, Location, UpdatedAt)
            VALUES ('q_helping_hand', 'A Helping Hand', 'Prapor', 'Customs', '2026-01-01T00:00:00Z'),
                   ('q_woods_delivery', 'Delivery from the Past', 'Prapor', 'Customs', '2026-01-01T00:00:00Z');

            -- Objective with factory coordinates (like A Helping Hand)
            INSERT INTO QuestObjectives (Id, QuestId, SortOrder, Description, MapName, LocationPoints, OriginalLocationPoints, IsCustomized, IsApproved)
            VALUES ('obj_factory_helping_hand', 'q_helping_hand', 0, 'Find the folder in dorms 214', 'Customs', '[{""x"":123.4,""y"":0.0,""z"":567.8,""floorId"":""main""}]', '[{""x"":123.4,""y"":0.0,""z"":567.8,""floorId"":""main""}]', 0, 1);

            -- Objective without factory coordinates (NULL)
            INSERT INTO QuestObjectives (Id, QuestId, SortOrder, Description, MapName, LocationPoints, OriginalLocationPoints, IsCustomized, IsApproved)
            VALUES ('obj_no_factory_coords', 'q_woods_delivery', 0, 'Obtain the document in customs office', 'Customs', NULL, NULL, 0, 0);

            -- Administrative objective (not geographic)
            INSERT INTO QuestObjectives (Id, QuestId, SortOrder, Description, MapName, LocationPoints, OriginalLocationPoints, IsCustomized, IsApproved)
            VALUES ('obj_admin_survive', 'q_woods_delivery', 1, 'Survive and extract from Customs', 'Customs', NULL, NULL, 0, 0);
        ";
        cmd.ExecuteNonQuery();
    }

    private static void RunInSta(Action action, int timeoutSeconds = 10)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            throw new TimeoutException($"STA test action timed out after {timeoutSeconds}s");
        }
        if (caught != null)
        {
            throw new AggregateException("Exception thrown in STA thread", caught);
        }
    }

    #region 1. Factory Coordinates Preservation & True Factory Restore

    [Fact]
    public async Task RestoreObjectiveToFactory_PreservesNativeCoordinates_DoesNotWipeToNull()
    {
        // 1. Relocate factory objective to new point
        var userPoint = new LocationPoint(999.0, 5.0, 888.0, "main");
        var saveResult = await _service.SaveObjectiveLocationAsync("obj_factory_helping_hand", userPoint, _testDbPath);
        Assert.True(saveResult);

        // Verify it was marked as customized
        using (var verifyConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
        {
            await verifyConn.OpenAsync();
            using var cmd = verifyConn.CreateCommand();
            cmd.CommandText = "SELECT LocationPoints, OriginalLocationPoints, IsCustomized FROM QuestObjectives WHERE Id = 'obj_factory_helping_hand'";
            using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var loc = reader.GetString(0);
            var orig = reader.GetString(1);
            var isCust = reader.GetInt32(2);

            Assert.Contains("999", loc);
            Assert.Contains("123.4", orig);
            Assert.Equal(1, isCust);
        }

        // 2. Perform factory restoration
        var restoreResult = await _service.RestoreObjectiveToFactoryAsync("obj_factory_helping_hand", _testDbPath);
        Assert.True(restoreResult);

        // 3. Verify it faithfully reverted to factory coordinates (NOT NULL) and IsCustomized is 0
        using (var verifyConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
        {
            await verifyConn.OpenAsync();
            using var cmd = verifyConn.CreateCommand();
            cmd.CommandText = "SELECT LocationPoints, OriginalLocationPoints, IsCustomized FROM QuestObjectives WHERE Id = 'obj_factory_helping_hand'";
            using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            Assert.False(reader.IsDBNull(0), "Factory coordinates MUST NOT be NULL after restoration!");
            var loc = reader.GetString(0);
            var orig = reader.GetString(1);
            var isCust = reader.GetInt32(2);

            Assert.Contains("123.4", loc);
            Assert.Equal(orig, loc);
            Assert.Equal(0, isCust);
        }
    }

    [Fact]
    public async Task RestoreObjectiveToFactory_ObjectiveWithoutOriginalCoords_RestoresToNull()
    {
        // 1. Relocate an objective that had no original coordinates
        var userPoint = new LocationPoint(300.0, 0.0, 400.0, "main");
        var saveResult = await _service.SaveObjectiveLocationAsync("obj_no_factory_coords", userPoint, _testDbPath);
        Assert.True(saveResult);

        // 2. Restore to factory
        var restoreResult = await _service.RestoreObjectiveToFactoryAsync("obj_no_factory_coords", _testDbPath);
        Assert.True(restoreResult);

        // 3. Verify restored LocationPoints is NULL because original was NULL
        using (var verifyConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
        {
            await verifyConn.OpenAsync();
            using var cmd = verifyConn.CreateCommand();
            cmd.CommandText = "SELECT LocationPoints, OriginalLocationPoints, IsCustomized FROM QuestObjectives WHERE Id = 'obj_no_factory_coords'";
            using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            Assert.True(reader.IsDBNull(0));
            Assert.True(reader.IsDBNull(1));
            Assert.Equal(0, reader.GetInt32(2));
        }
    }

    [Fact]
    public async Task RestoreAllObjectivesToFactory_RestoresAllCustomizedObjectivesAtOnce()
    {
        // Customize both objectives
        await _service.SaveObjectiveLocationAsync("obj_factory_helping_hand", new LocationPoint(1.0, 0, 2.0, "main"), _testDbPath);
        await _service.SaveObjectiveLocationAsync("obj_no_factory_coords", new LocationPoint(3.0, 0, 4.0, "main"), _testDbPath);

        // Verify 2 are customized
        var customizedBefore = await _service.GetCustomizedObjectivesAsync(_testDbPath);
        Assert.Equal(2, customizedBefore.Count);

        // Restore all
        var restoredCount = await _service.RestoreAllObjectivesToFactoryAsync(_testDbPath);
        Assert.Equal(2, restoredCount);

        // Verify none remain customized
        var customizedAfter = await _service.GetCustomizedObjectivesAsync(_testDbPath);
        Assert.Empty(customizedAfter);
    }

    #endregion

    #region 2. Audit Logging & History Queries

    [Fact]
    public async Task RelocationOperations_WriteAuditLogRecords()
    {
        // 1. Relocate
        await _service.SaveObjectiveLocationAsync("obj_factory_helping_hand", new LocationPoint(500.0, 0, 600.0, "main"), _testDbPath);

        // 2. Restore
        await _service.RestoreObjectiveToFactoryAsync("obj_factory_helping_hand", _testDbPath);

        // 3. Query audit logs
        var logs = await _service.GetRelocationHistoryLogsAsync(50, _testDbPath);
        Assert.True(logs.Count >= 2);

        var restoreLog = logs.FirstOrDefault(l => l.Action == "Restored" && l.ObjectiveId == "obj_factory_helping_hand");
        var relocateLog = logs.FirstOrDefault(l => l.Action == "Relocated" && l.ObjectiveId == "obj_factory_helping_hand");

        Assert.NotNull(restoreLog);
        Assert.NotNull(relocateLog);
        Assert.Equal("A Helping Hand", restoreLog.QuestName);
        Assert.Equal("A Helping Hand", relocateLog.QuestName);
    }

    #endregion

    #region 3. Search and Filtering Queries

    [Fact]
    public async Task SearchObjectivesAsync_FiltersByTextTraderMapAndGeographic()
    {
        // Search by query text
        var searchHelping = await _service.SearchObjectivesAsync("dorms 214", null, null, geographicOnly: false, targetDbPath: _testDbPath);
        Assert.Single(searchHelping);
        Assert.Equal("obj_factory_helping_hand", searchHelping[0].ObjectiveId);

        // Geographic only filters out administrative objectives
        var allGeo = await _service.SearchObjectivesAsync(null, null, "Customs", geographicOnly: true, targetDbPath: _testDbPath);
        Assert.DoesNotContain(allGeo, o => o.ObjectiveId == "obj_admin_survive");
        Assert.Contains(allGeo, o => o.ObjectiveId == "obj_factory_helping_hand");

        // Missing coordinates filter returns only objectives without LocationPoints
        var missingOnly = await _service.SearchObjectivesAsync(null, null, "Customs", geographicOnly: true, missingCoordinatesOnly: true, targetDbPath: _testDbPath);
        Assert.Contains(missingOnly, o => o.ObjectiveId == "obj_no_factory_coords");
        Assert.DoesNotContain(missingOnly, o => o.ObjectiveId == "obj_factory_helping_hand");
    }

    #endregion

    #region 4. Tooltip Normalization & Stabilization Helpers

    [Fact]
    public void MapTooltipHelper_CreateTooltip_ConstructsGoldTitleAndLightDetails()
    {
        RunInSta(() =>
        {
            var tooltip = MapTooltipHelper.CreateTooltip("Customs Extract", "Dorms V-Ex (Requires 5000 Roubles)", " [Floor 1]");
            Assert.NotNull(tooltip);
            Assert.IsType<ToolTip>(tooltip);

            var panel = Assert.IsType<StackPanel>(tooltip.Content);
            Assert.Equal(2, panel.Children.Count);

            var titleBlock = Assert.IsType<TextBlock>(panel.Children[0]);
            Assert.Equal("Customs Extract", titleBlock.Text);
            var titleBrush = Assert.IsType<SolidColorBrush>(titleBlock.Foreground);
            Assert.Equal(Color.FromRgb(255, 215, 0), titleBrush.Color); // #FFD700 Gold

            var detailBlock = Assert.IsType<TextBlock>(panel.Children[1]);
            Assert.Equal("Dorms V-Ex (Requires 5000 Roubles) [Floor 1]", detailBlock.Text);
            var detailBrush = Assert.IsType<SolidColorBrush>(detailBlock.Foreground);
            Assert.Equal(Color.FromRgb(240, 240, 240), detailBrush.Color); // #F0F0F0 Light
        });
    }

    [Fact]
    public void MapTooltipHelper_ConfigureToolTipService_Configures30sDuration()
    {
        RunInSta(() =>
        {
            var element = new Border();
            MapTooltipHelper.ConfigureToolTipService(element);

            Assert.Equal(30000, ToolTipService.GetShowDuration(element));
            Assert.Equal(300, ToolTipService.GetInitialShowDelay(element));
            Assert.Equal(100, ToolTipService.GetBetweenShowDelay(element));
        });
    }

    [Fact]
    public void MarkerRefreshHelper_SafeClear_ClearsContainerWhenNoHoveredElement()
    {
        RunInSta(() =>
        {
            var canvas = new Canvas();
            canvas.Children.Add(new Border());
            canvas.Children.Add(new TextBlock());

            var trackedList = new List<FrameworkElement>();
            MarkerRefreshHelper.SafeClear(canvas, trackedList);

            Assert.Empty(canvas.Children);
            Assert.Empty(trackedList);
        });
    }

    #endregion
}
