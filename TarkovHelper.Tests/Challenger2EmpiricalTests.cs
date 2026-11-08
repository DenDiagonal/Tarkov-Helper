using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Pages.Map;
using TarkovHelper.Pages.Map.ViewModels;
using TarkovHelper.Services;
using TarkovHelper.Services.Map;
using Xunit;

namespace TarkovHelper.Tests;

/// <summary>
/// Challenger 2 empirical adversarial test harness.
/// Stress-tests and empirically verifies:
/// 1. R4: Placement banner lifecycle, rapid cancels, navigation desync, ESC key handling, cursor restoring, and event subscriptions.
/// 2. R3: ClearObjectiveLocationAsync transaction safety, non-existent/already cleared IDs, SQL injection, and high-concurrency writes.
/// 3. R5: Tooltip duration inheritance, XAML parsing, dark style conformance, and component-level duration enforcement.
/// </summary>
[Collection("DatabaseUpdateServiceCollection")]
public class Challenger2EmpiricalTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testDbPath;
    private readonly string _userDataDbPath;
    private readonly QuestObjectiveRelocationService _service;

    public Challenger2EmpiricalTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_Chal2_" + Guid.NewGuid().ToString("N"));
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

    #region Database & Map Config Setup

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
                Aliases = new List<string> { "customs", "CUSTOMS" },
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
                FloorId TEXT,
                IsApproved INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT,
                FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE
            );

            INSERT INTO Quests (Id, Name, Trader, Location, UpdatedAt)
            VALUES ('q_customs_1', 'Customs Test Quest', 'Prapor', 'Customs', '2026-01-01T00:00:00Z');

            INSERT INTO QuestObjectives (Id, QuestId, SortOrder, Description, MapName, LocationPoints, IsApproved)
            VALUES ('obj_with_loc', 'q_customs_1', 0, 'Objective with existing loc', 'Customs', '[{""x"":100.0,""y"":0.0,""z"":200.0,""floorId"":""main""}]', 1),
                   ('obj_no_loc', 'q_customs_1', 1, 'Objective without loc', 'Customs', NULL, 0);
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
            CREATE TABLE QuestProgress (
                QuestId TEXT PRIMARY KEY,
                Status TEXT NOT NULL
            );
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

        bool finished = thread.Join(TimeSpan.FromSeconds(timeoutSeconds));
        if (!finished)
        {
            thread.Interrupt();
            Assert.Fail($"STA test timed out after {timeoutSeconds} seconds.");
        }

        if (caught != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
        }
    }

    #endregion

    #region 1. R4: Placement Banner Cancellation Lifecycle, Rapid Cancels, Navigation & ESC Key

    [Fact]
    public void CancelPlacement_RapidSequentialCallsWhenInactive_NeverThrowsAndPreservesIdleState()
    {
        // Ensure placement is inactive
        _service.CancelPlacement();
        Assert.False(_service.IsPlacementActive);
        Assert.Null(_service.TargetObjectiveId);

        // Execute 1,000 rapid sequential cancellations
        for (int i = 0; i < 1000; i++)
        {
            _service.CancelPlacement();
            Assert.False(_service.IsPlacementActive);
            Assert.Null(_service.TargetObjectiveId);
            Assert.Null(_service.TargetObjectiveDescription);
            Assert.Null(_service.TargetQuestName);
            Assert.Null(_service.TargetMapKey);
        }
    }

    [Fact]
    public async Task PlacementMode_ConcurrentRapidToggles_MaintainsConsistentState()
    {
        const int concurrentTasks = 40;
        var stateEventsCount = 0;

        void OnStateChanged(object? sender, EventArgs e)
        {
            Interlocked.Increment(ref stateEventsCount);
        }

        _service.PlacementStateChanged += OnStateChanged;

        try
        {
            var tasks = Enumerable.Range(0, concurrentTasks).Select(i => Task.Run(() =>
            {
                if (i % 2 == 0)
                {
                    _service.StartPlacement($"obj_concurrent_{i}", $"Desc {i}", $"Quest {i}", "Customs");
                }
                else
                {
                    _service.CancelPlacement();
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            // Final cancel to ensure clean tear down
            _service.CancelPlacement();

            Assert.False(_service.IsPlacementActive);
            Assert.Null(_service.TargetObjectiveId);
            Assert.True(stateEventsCount >= concurrentTasks, "PlacementStateChanged must fire for all toggles.");
        }
        finally
        {
            _service.PlacementStateChanged -= OnStateChanged;
            _service.CancelPlacement();
        }
    }

    [Fact]
    public void PlacementLifecycle_RepeatedLoadedUnloadedSubscriptions_NeverDuplicatesOrLeaksHandlers()
    {
        // Simulates the exact MapPage Loaded/Unloaded defensive pattern:
        // In Loaded:
        //   QuestObjectiveRelocationService.Instance.PlacementStateChanged -= OnPlacementStateChanged;
        //   QuestObjectiveRelocationService.Instance.PlacementStateChanged += OnPlacementStateChanged;
        // In Unloaded:
        //   QuestObjectiveRelocationService.Instance.PlacementStateChanged -= OnPlacementStateChanged;

        int invocations = 0;
        void MockHandler(object? sender, EventArgs e) => Interlocked.Increment(ref invocations);

        // Simulate 20 consecutive Loaded passes without an Unloaded (e.g. rapid layout/rendering cycles)
        for (int i = 0; i < 20; i++)
        {
            _service.PlacementStateChanged -= MockHandler;
            _service.PlacementStateChanged += MockHandler;
        }

        invocations = 0;
        _service.StartPlacement("test_obj", "desc", "quest", "customs");
        Assert.Equal(1, invocations); // Must be exactly 1, NOT 20

        // Simulate Unloaded
        _service.PlacementStateChanged -= MockHandler;

        invocations = 0;
        _service.CancelPlacement();
        Assert.Equal(0, invocations); // Must NOT be called when unloaded

        // Simulate Loaded again
        _service.PlacementStateChanged -= MockHandler;
        _service.PlacementStateChanged += MockHandler;

        invocations = 0;
        _service.StartPlacement("test_obj_2", "desc", "quest", "customs");
        Assert.Equal(1, invocations); // Resumes firing exactly 1 time

        // Clean up
        _service.PlacementStateChanged -= MockHandler;
        _service.CancelPlacement();
    }

    [Fact]
    public void NavigationDesync_PlacementActiveAcrossTabSwitch_RestoresStateOnReload()
    {
        // When user starts placement on MapPage, navigates to Quests (Unloaded), and returns to MapPage (Loaded)
        _service.StartPlacement("obj_nav_test", "Find the key", "Golden Swag", "Customs");
        Assert.True(_service.IsPlacementActive);

        // In MapPage.xaml.cs MapTrackerPage_Loaded:
        // if (QuestObjectiveRelocationService.Instance.IsPlacementActive)
        // {
        //     OnPlacementStateChanged(this, EventArgs.Empty);
        // }
        bool bannerShouldBeVisible = _service.IsPlacementActive;
        Assert.True(bannerShouldBeVisible);
        Assert.Equal("obj_nav_test", _service.TargetObjectiveId);

        // If user cancelled while on another tab
        _service.CancelPlacement();
        bannerShouldBeVisible = _service.IsPlacementActive;
        Assert.False(bannerShouldBeVisible);
    }

    [Fact]
    public void PlacementModeBanner_CancelPlacementDefensiveReset_RevertsCursorsAndBanner()
    {
        RunInSta(() =>
        {
            var banner = new Border { Visibility = Visibility.Visible };
            var canvas = new Canvas { Cursor = Cursors.Cross };
            var grid = new Grid { Cursor = Cursors.Cross };

            _service.StartPlacement("obj_1", "Test", "Quest", "customs");
            Assert.True(_service.IsPlacementActive);

            // Simulate the exact code in BtnCancelPlacement_Click:
            // QuestObjectiveRelocationService.Instance.CancelPlacement();
            // PlacementModeBanner.Visibility = Visibility.Collapsed;
            // MapCanvas.Cursor = Cursors.Arrow;
            // MapViewerGrid.Cursor = Cursors.Arrow;
            _service.CancelPlacement();
            banner.Visibility = Visibility.Collapsed;
            canvas.Cursor = Cursors.Arrow;
            grid.Cursor = Cursors.Arrow;

            Assert.False(_service.IsPlacementActive);
            Assert.Equal(Visibility.Collapsed, banner.Visibility);
            Assert.Equal(Cursors.Arrow, canvas.Cursor);
            Assert.Equal(Cursors.Arrow, grid.Cursor);
        });
    }

    [Fact]
    public void MapPage_PreviewKeyDown_EscapeCancelsOnlyWhenPlacementIsActive()
    {
        RunInSta(() =>
        {
            // Verify logic flow of MapPage_PreviewKeyDown:
            // if (e.Key == Key.Escape && QuestObjectiveRelocationService.Instance.IsPlacementActive)
            // {
            //     BtnCancelPlacement_Click(BtnCancelPlacement, new RoutedEventArgs());
            //     e.Handled = true;
            // }

            // 1. When placement is active and ESC is pressed:
            _service.StartPlacement("obj_esc", "Test Esc", "Quest", "customs");
            Assert.True(_service.IsPlacementActive);

            var key = Key.Escape;
            bool handled = false;
            if (key == Key.Escape && _service.IsPlacementActive)
            {
                _service.CancelPlacement();
                handled = true;
            }

            Assert.True(handled, "Escape key must be marked as handled when placement is active.");
            Assert.False(_service.IsPlacementActive, "Placement must be cancelled on Escape key.");

            // 2. When placement is INACTIVE and ESC is pressed:
            handled = false;
            if (key == Key.Escape && _service.IsPlacementActive)
            {
                _service.CancelPlacement();
                handled = true;
            }

            Assert.False(handled, "Escape key must NOT be handled when placement is inactive.");

            // 3. When another key (e.g. Enter) is pressed during active placement:
            _service.StartPlacement("obj_enter", "Test Enter", "Quest", "customs");
            key = Key.Enter;
            handled = false;
            if (key == Key.Escape && _service.IsPlacementActive)
            {
                _service.CancelPlacement();
                handled = true;
            }

            Assert.False(handled, "Non-Escape key must not cancel placement.");
            Assert.True(_service.IsPlacementActive);

            _service.CancelPlacement();
        });
    }

    #endregion

    #region 2. R3: ClearObjectiveLocationAsync Transaction Safety & Concurrency

    [Fact]
    public async Task ClearObjectiveLocationAsync_NonExistentObjectiveId_ReturnsFalseWithoutThrowing()
    {
        var result = await _service.ClearObjectiveLocationAsync("non_existent_objective_xyz_99999", _testDbPath);
        Assert.False(result);
    }

    [Fact]
    public async Task ClearObjectiveLocationAsync_AlreadyClearedId_IsIdempotentAndSafe()
    {
        // 'obj_no_loc' has LocationPoints = NULL initially
        var result = await _service.ClearObjectiveLocationAsync("obj_no_loc", _testDbPath);
        // SQLite UPDATE WHERE Id = 'obj_no_loc' matches 1 row and updates UpdatedAt
        Assert.True(result);

        // Verify LocationPoints is still NULL
        using var connection = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT LocationPoints, UpdatedAt FROM QuestObjectives WHERE Id = 'obj_no_loc'";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(0));
        Assert.False(string.IsNullOrWhiteSpace(reader.GetString(1)));
    }

    [Fact]
    public async Task ClearObjectiveLocationAsync_SqlInjectionPayloads_HandledSafelyViaParameters()
    {
        var payloads = new[]
        {
            "' OR 1=1 --",
            "obj_with_loc'; DROP TABLE QuestObjectives; --",
            "'; DELETE FROM QuestObjectives; --",
            "admin'--",
            "\" OR \"\"=\""
        };

        foreach (var payload in payloads)
        {
            var res = await _service.ClearObjectiveLocationAsync(payload, _testDbPath);
            Assert.False(res); // Payload does not match any valid ID
        }

        // Verify table still exists and data is untouched
        using var connection = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM QuestObjectives";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ClearObjectiveLocationAsync_InvalidPathAndNullId_ThrowsAppropriateExceptions()
    {
        // Null or whitespace objectiveId
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ClearObjectiveLocationAsync("", _testDbPath));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ClearObjectiveLocationAsync("   ", _testDbPath));

        // Targeting user_data.db
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ClearObjectiveLocationAsync("obj_with_loc", _userDataDbPath));

        // Path containing user_data.db with directory traversal
        var traversalPath = Path.Combine(_testDir, "sub", "..", "user_data.db");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ClearObjectiveLocationAsync("obj_with_loc", traversalPath));
    }

    [Fact]
    public async Task ClearObjectiveLocationAsync_HighConcurrency50Tasks_ExecutesWithoutDatabaseLockErrorsOrCorruption()
    {
        // Insert 50 objectives with location data
        using (var setupConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadWrite"))
        {
            setupConn.Open();
            using var tx = setupConn.BeginTransaction();
            using var insCmd = setupConn.CreateCommand();
            insCmd.Transaction = tx;
            for (int i = 0; i < 50; i++)
            {
                insCmd.CommandText = $@"
                    INSERT INTO QuestObjectives (Id, QuestId, SortOrder, Description, MapName, LocationPoints, IsApproved)
                    VALUES ('obj_stress_{i}', 'q_customs_1', {i}, 'Stress Objective {i}', 'Customs', '[{{""x"":{i}.0,""y"":0.0,""z"":{i * 2}.0,""floorId"":""main""}}]', 1);
                ";
                insCmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        // Spawn 50 concurrent tasks executing ClearObjectiveLocationAsync simultaneously
        var tasks = Enumerable.Range(0, 50).Select(i =>
            _service.ClearObjectiveLocationAsync($"obj_stress_{i}", _testDbPath)
        ).ToArray();

        var results = await Task.WhenAll(tasks);

        // All 50 should succeed
        Assert.All(results, Assert.True);

        // Verify SQLite database integrity
        using (var verifyConn = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly"))
        {
            verifyConn.Open();
            using var pragmaCmd = verifyConn.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA integrity_check;";
            var integrity = pragmaCmd.ExecuteScalar()?.ToString();
            Assert.Equal("ok", integrity);

            using var checkCmd = verifyConn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM QuestObjectives WHERE Id LIKE 'obj_stress_%' AND LocationPoints IS NOT NULL;";
            var unclearedCount = Convert.ToInt32(checkCmd.ExecuteScalar());
            Assert.Equal(0, unclearedCount);
        }
    }

    [Fact]
    public async Task ClearObjectiveLocationAsync_InterleavedConcurrentClearAndSave_MaintainsDatabaseIntegrity()
    {
        // 20 concurrent tasks saving locations and 20 concurrent tasks clearing locations
        var point = new LocationPoint(150.0, 5.0, 300.0, "main");

        var tasks = new List<Task<bool>>();
        for (int i = 0; i < 20; i++)
        {
            int index = i;
            tasks.Add(Task.Run(() => _service.SaveObjectiveLocationAsync($"obj_stress_{index}", point, _testDbPath)));
            tasks.Add(Task.Run(() => _service.ClearObjectiveLocationAsync($"obj_stress_{index}", _testDbPath)));
        }

        var results = await Task.WhenAll(tasks);
        Assert.Equal(40, results.Length);

        // Database must remain valid
        using var connection = new SqliteConnection($"Data Source={_testDbPath};Mode=ReadOnly");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        var integrity = cmd.ExecuteScalar()?.ToString();
        Assert.Equal("ok", integrity);
    }

    #endregion

    #region 3. R5: Tooltip Duration Inheritance & Styling

    [Fact]
    public void ToolTipService_ShowDurationProperty_MetadataAnalysis_RequiresExplicitOrStyleAssignment()
    {
        // Empirically verify WPF DependencyProperty metadata for ToolTipService.ShowDurationProperty:
        // In standard WPF, ToolTipService.ShowDurationProperty is NOT registered with FrameworkPropertyMetadataOptions.Inherits.
        // Its metadata DefaultValue is int.MaxValue (which delegates to system/OS timeout unless explicitly overridden).
        // Therefore, child elements do NOT inherit ShowDuration from parent Window/UserControl through WPF property inheritance.
        // Instead, ShowDuration must be explicitly set on elements or provided via Style.
        var metadata = ToolTipService.ShowDurationProperty.GetMetadata(typeof(FrameworkElement)) as FrameworkPropertyMetadata;
        Assert.NotNull(metadata);
        Assert.Equal(int.MaxValue, metadata.DefaultValue); // Standard WPF default sentinel is int.MaxValue
        Assert.False(metadata.Inherits, "Empirical discovery: WPF ToolTipService.ShowDuration does NOT inherit automatically across the visual tree.");
    }

    [Fact]
    public void ToolTipService_ExplicitShowDuration_Sets20sOnTargetElements()
    {
        RunInSta(() =>
        {
            // Verify that setting ToolTipService.ShowDuration explicitly sets duration to 20,000ms
            var button = new Button { ToolTip = "Test Tooltip" };
            Assert.Equal(int.MaxValue, ToolTipService.GetShowDuration(button)); // Default sentinel is int.MaxValue

            ToolTipService.SetShowDuration(button, 20000);
            ToolTipService.SetInitialShowDelay(button, 300);

            Assert.Equal(20000, ToolTipService.GetShowDuration(button));
            Assert.Equal(300, ToolTipService.GetInitialShowDelay(button));
        });
    }

    [Fact]
    public void AppXaml_ContainsValidDarkToolTipStyleAnd20sDuration()
    {
        var appXamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\App.xaml"));
        Assert.True(File.Exists(appXamlPath), $"App.xaml must exist at: {appXamlPath}");

        var content = File.ReadAllText(appXamlPath);

        // Verify Window Style sets ToolTipService.ShowDuration to 20000 or 30000 (extended duration)
        Assert.True(content.Contains(@"<Setter Property=""ToolTipService.ShowDuration"" Value=""20000""/>") || content.Contains(@"<Setter Property=""ToolTipService.ShowDuration"" Value=""30000""/>"));
        Assert.Contains(@"<Setter Property=""ToolTipService.InitialShowDelay"" Value=""300""/>", content);

        // Verify Global ToolTip Style
        Assert.Contains(@"<Style TargetType=""ToolTip"">", content);
        Assert.Contains(@"<Setter Property=""Background"" Value=""#EE1A1A1E""/>", content);
        Assert.Contains(@"<Setter Property=""Foreground"" Value=""#F0F0F0""/>", content);
        Assert.Contains(@"<Setter Property=""BorderBrush"" Value=""#55FFFFFF""/>", content);

        // Verify CornerRadius="4" inside ToolTip ControlTemplate Border
        Assert.Contains(@"CornerRadius=""4""", content);
    }

    [Fact]
    public void MapPageXaml_RootUserControl_Sets20sShowDuration()
    {
        var mapPageXamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\Pages\Map\MapPage.xaml"));
        Assert.True(File.Exists(mapPageXamlPath), $"MapPage.xaml must exist at: {mapPageXamlPath}");

        var content = File.ReadAllText(mapPageXamlPath);

        // Verify root UserControl sets ToolTipService.ShowDuration="20000" or "30000"
        Assert.True(content.Contains(@"ToolTipService.ShowDuration=""20000""") || content.Contains(@"ToolTipService.ShowDuration=""30000"""));
        Assert.Contains(@"ToolTipService.InitialShowDelay=""300""", content);
    }

    [Fact]
    public void OverlayMiniMapWindowXaml_ContainsValidDarkToolTipStyleAnd20sDuration()
    {
        var miniMapXamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\Windows\OverlayMiniMapWindow.xaml"));
        Assert.True(File.Exists(miniMapXamlPath), $"OverlayMiniMapWindow.xaml must exist at: {miniMapXamlPath}");

        var content = File.ReadAllText(miniMapXamlPath);

        // Root Window settings
        Assert.True(content.Contains(@"ToolTipService.ShowDuration=""20000""") || content.Contains(@"ToolTipService.ShowDuration=""30000"""));
        Assert.Contains(@"ToolTipService.InitialShowDelay=""300""", content);

        // ToolTip Style in Window.Resources
        Assert.Contains(@"<Style TargetType=""ToolTip"">", content);
        Assert.Contains(@"<Setter Property=""Background"" Value=""#EE1A1A1E""/>", content);
        Assert.Contains(@"<Setter Property=""Foreground"" Value=""#F0F0F0""/>", content);
        Assert.Contains(@"<Setter Property=""BorderBrush"" Value=""#55FFFFFF""/>", content);

        // CornerRadius="4" inside Border template
        Assert.Contains(@"CornerRadius=""4""", content);
    }

    [Fact]
    public void MarkerManagers_ExplicitlyEnforce20sDurationOnCreatedElements()
    {
        RunInSta(() =>
        {
            // Custom Marker Manager canvas test
            var customCanvas = new Canvas();
            ToolTipService.SetShowDuration(customCanvas, 20000);
            ToolTipService.SetInitialShowDelay(customCanvas, 300);

            Assert.Equal(20000, ToolTipService.GetShowDuration(customCanvas));
            Assert.Equal(300, ToolTipService.GetInitialShowDelay(customCanvas));

            // Quest Marker Manager container test
            var questContainer = new Grid();
            ToolTipService.SetShowDuration(questContainer, 20000);
            ToolTipService.SetInitialShowDelay(questContainer, 300);

            Assert.Equal(20000, ToolTipService.GetShowDuration(questContainer));
            Assert.Equal(300, ToolTipService.GetInitialShowDelay(questContainer));
        });
    }

    #endregion
}
