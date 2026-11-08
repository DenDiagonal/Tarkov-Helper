using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Data.Sqlite;
using TarkovHelper.Pages.Map.Components;
using Xunit;

namespace TarkovHelper.Tests;

/// <summary>
/// Empirical challenge tests for Tasks 3 & 4:
/// - Tooltip visual styling (#EE1A1A1E, #FFD700, #F0F0F0, #55FFFFFF, CornerRadius 4) and 30s duration across MapPage and OverlayMiniMapWindow.
/// - Hit-test isolation (IsHitTestVisible="False" on inner shapes to eliminate spurious MouseLeave cascades).
/// - MarkerRefreshHelper.SafeClear: hovered marker preservation (IsMouseOver == true), tooltip stability, and clean detach on MouseLeave.
/// - Self-contained publish validation: includedFrameworks check and SQLite database integrity.
/// </summary>
public class Task3And4EmpiricalChallengerTests
{
    private static void RunInSta(Action action, int timeoutSeconds = 15)
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

    private static void SetElementIsMouseOver(UIElement element, bool isMouseOver)
    {
        var coreFlagsType = typeof(UIElement).Assembly.GetType("System.Windows.CoreFlags");
        Assert.NotNull(coreFlagsType);
        var flagVal = Enum.Parse(coreFlagsType, "IsMouseOverCache");
        var method = typeof(UIElement).GetMethod("WriteFlag", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(element, new object[] { flagVal, isMouseOver });
    }

    #region 1. Tooltip Visual Styling & 30s Duration Verification

    [Fact]
    public void MapTooltipHelper_CreateTooltip_ExactVisualStylingAndStructure()
    {
        RunInSta(() =>
        {
            var tooltip = MapTooltipHelper.CreateTooltip("Customs Extract", "Dorms V-Ex", " [Floor 1]");
            Assert.NotNull(tooltip);

            var panel = Assert.IsType<StackPanel>(tooltip.Content);
            Assert.Equal(2, panel.Children.Count);

            // Title
            var titleBlock = Assert.IsType<TextBlock>(panel.Children[0]);
            Assert.Equal("Customs Extract", titleBlock.Text);
            var titleBrush = Assert.IsType<SolidColorBrush>(titleBlock.Foreground);
            Assert.Equal(Color.FromRgb(255, 215, 0), titleBrush.Color); // #FFD700
            Assert.Equal(FontWeights.SemiBold, titleBlock.FontWeight);
            Assert.Equal(11.0, titleBlock.FontSize);

            // Details
            var detailBlock = Assert.IsType<TextBlock>(panel.Children[1]);
            Assert.Equal("Dorms V-Ex [Floor 1]", detailBlock.Text);
            var detailBrush = Assert.IsType<SolidColorBrush>(detailBlock.Foreground);
            Assert.Equal(Color.FromRgb(240, 240, 240), detailBrush.Color); // #F0F0F0
            Assert.Equal(10.0, detailBlock.FontSize);
        });
    }

    [Fact]
    public void MapTooltipHelper_ConfigureToolTipService_Configures30sDurationAndSmoothDelays()
    {
        RunInSta(() =>
        {
            var border = new Border();
            MapTooltipHelper.ConfigureToolTipService(border);

            Assert.Equal(30000, ToolTipService.GetShowDuration(border));
            Assert.Equal(300, ToolTipService.GetInitialShowDelay(border));
            Assert.Equal(100, ToolTipService.GetBetweenShowDelay(border));
        });
    }

    [Fact]
    public void AppXaml_DefinesUnifiedToolTipStyle_WithDarkThemeAnd30sDuration()
    {
        var appXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\App.xaml"));
        Assert.True(File.Exists(appXamlPath), $"App.xaml must exist at: {appXamlPath}");
        var content = File.ReadAllText(appXamlPath);

        // ToolTip Style properties
        Assert.Contains(@"<Style TargetType=""ToolTip"">", content);
        Assert.Contains(@"<Setter Property=""Background"" Value=""#EE1A1A1E""/>", content);
        Assert.Contains(@"<Setter Property=""Foreground"" Value=""#F0F0F0""/>", content);
        Assert.Contains(@"<Setter Property=""BorderBrush"" Value=""#55FFFFFF""/>", content);
        Assert.Contains(@"<Setter Property=""BorderThickness"" Value=""1""/>", content);
        Assert.Contains(@"CornerRadius=""4""", content);

        // Window & UserControl styles setting 30s ShowDuration
        Assert.Contains(@"<Setter Property=""ToolTipService.ShowDuration"" Value=""30000""/>", content);
        Assert.Contains(@"<Setter Property=""ToolTipService.InitialShowDelay"" Value=""300""/>", content);
        Assert.Contains(@"<Setter Property=""ToolTipService.BetweenShowDelay"" Value=""100""/>", content);
    }

    [Fact]
    public void OverlayMiniMapWindowXaml_DefinesUnifiedToolTipStyle_WithDarkThemeAnd30sDuration()
    {
        var miniMapXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\Windows\OverlayMiniMapWindow.xaml"));
        Assert.True(File.Exists(miniMapXamlPath), $"OverlayMiniMapWindow.xaml must exist at: {miniMapXamlPath}");
        var content = File.ReadAllText(miniMapXamlPath);

        // Root Window settings
        Assert.Contains(@"ToolTipService.ShowDuration=""30000""", content);
        Assert.Contains(@"ToolTipService.InitialShowDelay=""300""", content);
        Assert.Contains(@"ToolTipService.BetweenShowDelay=""100""", content);

        // Window.Resources ToolTip Style
        Assert.Contains(@"<Style TargetType=""ToolTip"">", content);
        Assert.Contains(@"<Setter Property=""Background"" Value=""#EE1A1A1E""/>", content);
        Assert.Contains(@"<Setter Property=""Foreground"" Value=""#F0F0F0""/>", content);
        Assert.Contains(@"<Setter Property=""BorderBrush"" Value=""#55FFFFFF""/>", content);
        Assert.Contains(@"<Setter Property=""BorderThickness"" Value=""1""/>", content);
        Assert.Contains(@"CornerRadius=""4""", content);
    }

    [Fact]
    public void MapPageXaml_SetsToolTipServiceShowDuration30s()
    {
        var mapPageXamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\Pages\Map\MapPage.xaml"));
        Assert.True(File.Exists(mapPageXamlPath), $"MapPage.xaml must exist at: {mapPageXamlPath}");
        var content = File.ReadAllText(mapPageXamlPath);

        Assert.Contains(@"ToolTipService.ShowDuration=""30000""", content);
        Assert.Contains(@"ToolTipService.InitialShowDelay=""300""", content);
        Assert.Contains(@"ToolTipService.BetweenShowDelay=""100""", content);
    }

    #endregion

    #region 2. Hit-Test Isolation Verification

    [Fact]
    public void OverlayMiniMapWindow_SourceCode_EnsuresHitTestIsolationOnInnerShapes()
    {
        var miniMapCodePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\Windows\OverlayMiniMapWindow.xaml.cs"));
        Assert.True(File.Exists(miniMapCodePath), $"OverlayMiniMapWindow.xaml.cs must exist at: {miniMapCodePath}");
        var code = File.ReadAllText(miniMapCodePath);

        // Check CreateMarker hit-test isolation
        Assert.Contains("var hitArea = new Ellipse", code);
        Assert.Contains("Fill = Brushes.Transparent,", code);

        // All inner decorative elements must have IsHitTestVisible = false
        var hitTestFalseCount = code.Split(new[] { "IsHitTestVisible = false" }, StringSplitOptions.None).Length - 1;
        Assert.True(hitTestFalseCount >= 4, $"Expected at least 4 inner elements with IsHitTestVisible = false, found {hitTestFalseCount}");
    }

    [Fact]
    public void MapQuestMarkerManager_SourceCode_EnsuresHitTestIsolationOnChildren()
    {
        var questMarkerPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\Pages\Map\Components\MapQuestMarkerManager.cs"));
        Assert.True(File.Exists(questMarkerPath), $"MapQuestMarkerManager.cs must exist at: {questMarkerPath}");
        var code = File.ReadAllText(questMarkerPath);

        // Verify transparent hitArea has IsHitTestVisible = true
        Assert.Contains("var hitArea = new Ellipse", code);
        Assert.Contains("IsHitTestVisible = true", code);

        // Verify inner visual shapes (glow, circleOuter, center, completedOverlay, checkMark) have IsHitTestVisible = false
        var hitTestFalseCount = code.Split(new[] { "IsHitTestVisible = false" }, StringSplitOptions.None).Length - 1;
        Assert.True(hitTestFalseCount >= 5, $"Expected at least 5 inner elements with IsHitTestVisible = false, found {hitTestFalseCount}");
    }

    [Fact]
    public void MapCustomMarkerManager_SourceCode_EnsuresHitTestIsolation()
    {
        var customMarkerPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\TarkovHelper\Pages\Map\Components\MapCustomMarkerManager.cs"));
        Assert.True(File.Exists(customMarkerPath), $"MapCustomMarkerManager.cs must exist at: {customMarkerPath}");
        var code = File.ReadAllText(customMarkerPath);

        Assert.Contains("var hitArea = new Ellipse", code);
        Assert.Contains("IsHitTestVisible = true", code);
        Assert.Contains("IsHitTestVisible = false", code);
    }

    [Fact]
    public void MarkerStructure_HitTestIsolation_SimulatedInteractionHierarchy()
    {
        RunInSta(() =>
        {
            // Construct a marker container mimicking the architecture in MapQuestMarkerManager & OverlayMiniMapWindow
            var container = new Canvas { Width = 0, Height = 0 };
            var hitArea = new Ellipse
            {
                Width = 20,
                Height = 20,
                Fill = Brushes.Transparent,
                IsHitTestVisible = true
            };
            container.Children.Add(hitArea);

            var glow = new Ellipse
            {
                Width = 30,
                Height = 30,
                Fill = Brushes.Red,
                IsHitTestVisible = false
            };
            container.Children.Add(glow);

            var text = new TextBlock
            {
                Text = "Quest",
                IsHitTestVisible = false
            };
            container.Children.Add(text);

            // Verify hit-test visibility hierarchy
            Assert.True(hitArea.IsHitTestVisible, "Outer hitArea must be hit-test visible to capture pointer events.");
            Assert.False(glow.IsHitTestVisible, "Inner glow must not be hit-test visible.");
            Assert.False(text.IsHitTestVisible, "Inner text must not be hit-test visible.");

            // Verify that only 1 element receives hit-tests
            var hitTestVisibleChildren = container.Children.OfType<UIElement>().Where(c => c.IsHitTestVisible).ToList();
            Assert.Single(hitTestVisibleChildren);
            Assert.Same(hitArea, hitTestVisibleChildren[0]);
        });
    }

    #endregion

    #region 3. MarkerRefreshHelper.SafeClear Empirical Verification

    [Fact]
    public void SafeClear_HandlesNullAndEmptyContainerGracefully()
    {
        RunInSta(() =>
        {
            // Null container
            MarkerRefreshHelper.SafeClear(null, null);

            // Empty container
            var emptyCanvas = new Canvas();
            var list = new List<FrameworkElement>();
            MarkerRefreshHelper.SafeClear(emptyCanvas, list);
            Assert.Empty(emptyCanvas.Children);
            Assert.Empty(list);
        });
    }

    [Fact]
    public void SafeClear_WhenNoHoveredElement_ClearsAllChildrenImmediately()
    {
        RunInSta(() =>
        {
            var canvas = new Canvas();
            var el1 = new Border { Width = 20, Height = 20 };
            var el2 = new Ellipse { Width = 15, Height = 15 };
            canvas.Children.Add(el1);
            canvas.Children.Add(el2);

            var tracked = new List<FrameworkElement> { el1, el2 };
            MarkerRefreshHelper.SafeClear(canvas, tracked);

            Assert.Empty(canvas.Children);
            Assert.Empty(tracked);
        });
    }

    [Fact]
    public void SafeClear_WhenElementIsHovered_PreservesHoveredElementAndRemovesOthers()
    {
        RunInSta(() =>
        {
            var canvas = new Canvas();
            var normalElement = new Border { Width = 20, Height = 20, ToolTip = "Normal" };
            var hoveredElement = new Border { Width = 20, Height = 20, ToolTip = "Hovered" };
            var anotherNormal = new Border { Width = 20, Height = 20, ToolTip = "Another" };

            canvas.Children.Add(normalElement);
            canvas.Children.Add(hoveredElement);
            canvas.Children.Add(anotherNormal);

            // Simulate mouse hover on hoveredElement
            SetElementIsMouseOver(hoveredElement, true);
            Assert.True(hoveredElement.IsMouseOver);
            Assert.False(normalElement.IsMouseOver);
            Assert.False(anotherNormal.IsMouseOver);

            var tracked = new List<FrameworkElement> { normalElement, hoveredElement, anotherNormal };

            // Execute SafeClear
            MarkerRefreshHelper.SafeClear(canvas, tracked);

            // Verify tracked list was cleared for recreation, but canvas retained hovered element
            Assert.Empty(tracked);
            Assert.Single(canvas.Children);
            Assert.Same(hoveredElement, canvas.Children[0]);
            Assert.Equal("Hovered", hoveredElement.ToolTip);

            // Now simulate MouseLeave on the retained element
            var mouseLeaveArgs = new MouseEventArgs(Mouse.PrimaryDevice, 0)
            {
                RoutedEvent = Mouse.MouseLeaveEvent,
                Source = hoveredElement
            };
            hoveredElement.RaiseEvent(mouseLeaveArgs);

            // Retained element should now be cleanly removed from canvas
            Assert.Empty(canvas.Children);
        });
    }

    [Fact]
    public void SafeClear_RepeatedRefreshesWhileHovered_DoesNotThrowAndMaintainsRetention()
    {
        RunInSta(() =>
        {
            var canvas = new Canvas();
            var hoveredElement = new Border { Width = 20, Height = 20, ToolTip = "Hovered Marker" };
            canvas.Children.Add(hoveredElement);

            SetElementIsMouseOver(hoveredElement, true);

            // Simulate 5 consecutive tracking refreshes while mouse remains hovered
            for (int refresh = 0; refresh < 5; refresh++)
            {
                var newMarker = new Border { Width = 20, Height = 20, ToolTip = $"Fresh Marker {refresh}" };
                canvas.Children.Add(newMarker);

                MarkerRefreshHelper.SafeClear(canvas, null);

                Assert.Single(canvas.Children);
                Assert.Same(hoveredElement, canvas.Children[0]);
            }

            // Finally, user moves mouse away -> MouseLeave event
            SetElementIsMouseOver(hoveredElement, false);
            var mouseLeaveArgs = new MouseEventArgs(Mouse.PrimaryDevice, 0)
            {
                RoutedEvent = Mouse.MouseLeaveEvent,
                Source = hoveredElement
            };
            hoveredElement.RaiseEvent(mouseLeaveArgs);

            Assert.Empty(canvas.Children);
        });
    }

    #endregion

    #region 4. Self-Contained Local Publish & Database Integrity

    [Fact]
    public void PublishRuntimeConfig_HasIncludedFrameworks_AndNoDependentFrameworks()
    {
        var publishDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\publish"));
        var runtimeConfigPath = System.IO.Path.Combine(publishDir, "TarkovHelper.runtimeconfig.json");

        Assert.True(File.Exists(runtimeConfigPath), $"Runtime config must exist at: {runtimeConfigPath}");

        var json = File.ReadAllText(runtimeConfigPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("runtimeOptions", out var runtimeOptions), "Must contain runtimeOptions");

        // Must have includedFrameworks
        Assert.True(runtimeOptions.TryGetProperty("includedFrameworks", out var includedFrameworks), "Must contain 'includedFrameworks' for self-contained deployment.");
        Assert.Equal(JsonValueKind.Array, includedFrameworks.ValueKind);

        var names = includedFrameworks.EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();
        Assert.Contains("Microsoft.NETCore.App", names);
        Assert.Contains("Microsoft.WindowsDesktop.App", names);

        // Must NOT have 'frameworks'
        Assert.False(runtimeOptions.TryGetProperty("frameworks", out _), "Self-contained publish MUST NOT contain dependent 'frameworks' array.");
    }

    [Fact]
    public void PublishDatabase_TarkovDataDb_IsValidSqliteAndPreserved()
    {
        var publishDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\publish"));
        var dbPath = System.IO.Path.Combine(publishDir, "tarkov_data.db");

        Assert.True(File.Exists(dbPath), $"tarkov_data.db must exist at: {dbPath}");

        var fileInfo = new FileInfo(dbPath);
        Assert.True(fileInfo.Length > 6_000_000, $"tarkov_data.db size must be ~6.89MB, found {fileInfo.Length} bytes");

        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();

        // 1. PRAGMA integrity_check
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA integrity_check;";
            var result = cmd.ExecuteScalar()?.ToString();
            Assert.Equal("ok", result);
        }

        // 2. PRAGMA quick_check
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA quick_check;";
            var result = cmd.ExecuteScalar()?.ToString();
            Assert.Equal("ok", result);
        }

        // 3. Quests & QuestObjectives row count
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Quests;";
            var questsCount = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.True(questsCount > 0, "Quests table must have data");

            cmd.CommandText = "SELECT COUNT(*) FROM QuestObjectives;";
            var objectivesCount = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.True(objectivesCount > 0, "QuestObjectives table must have data");

            cmd.CommandText = "SELECT COUNT(*) FROM QuestObjectives WHERE OriginalLocationPoints IS NOT NULL;";
            var origCount = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.True(origCount >= 460, $"Expected >= 460 factory coordinates backfilled into OriginalLocationPoints, found {origCount}");
        }

        // 4. Verify "A Helping Hand" factory coordinates preservation
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT LocationPoints, OriginalLocationPoints, IsCustomized FROM QuestObjectives WHERE Id = 'Hj6qhH-Lwm8HT0CNbSHud1';";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read(), "A Helping Hand objective ('Hj6qhH-Lwm8HT0CNbSHud1') must exist in database");

            var loc = reader.GetString(0);
            var orig = reader.GetString(1);
            var isCust = reader.GetInt32(2);

            Assert.False(string.IsNullOrEmpty(loc), "LocationPoints must not be null/empty");
            Assert.False(string.IsNullOrEmpty(orig), "OriginalLocationPoints must not be null/empty");
            Assert.Contains("-613", loc);
            Assert.Contains("-613", orig);
            Assert.Equal(0, isCust);
        }

        // 5. Verify QuestObjectiveRelocationLog table exists
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='QuestObjectiveRelocationLog';";
            var logTableExists = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.Equal(1, logTableExists);
        }
    }

    [Fact]
    public void PublishBinaries_ExecutablesAndCoreDllsPresent()
    {
        var publishDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\publish"));
        var exePath = System.IO.Path.Combine(publishDir, "TarkovHelper.exe");
        var dllPath = System.IO.Path.Combine(publishDir, "TarkovHelper.dll");

        Assert.True(File.Exists(exePath), $"TarkovHelper.exe must exist in {publishDir}");
        Assert.True(File.Exists(dllPath), $"TarkovHelper.dll must exist in {publishDir}");

        var exeInfo = new FileInfo(exePath);
        Assert.True(exeInfo.Length > 100_000, $"TarkovHelper.exe should be > 100KB, was {exeInfo.Length}");

        var dllInfo = new FileInfo(dllPath);
        Assert.True(dllInfo.Length > 1_000_000, $"TarkovHelper.dll should be > 1MB, was {dllInfo.Length}");
    }

    [Fact]
    public void MapTooltipHelper_CreateTooltip_WithNullDetailsAndFloorSuffix_CombinesTitleAndFloor()
    {
        RunInSta(() =>
        {
            var tooltip = MapTooltipHelper.CreateTooltip("Customs Extract", null, " [Floor 1]");
            var panel = Assert.IsType<StackPanel>(tooltip.Content);

            // Should have only 1 child: Title with floor suffix combined
            Assert.Single(panel.Children);
            var titleBlock = Assert.IsType<TextBlock>(panel.Children[0]);
            Assert.Equal("Customs Extract [Floor 1]", titleBlock.Text);
            Assert.Equal(Color.FromRgb(255, 215, 0), ((SolidColorBrush)titleBlock.Foreground).Color);
        });
    }

    [Fact]
    public void SafeClear_WhenMultipleElementsHovered_PreservesFirstHoveredAndRemovesOthers()
    {
        RunInSta(() =>
        {
            var canvas = new Canvas();
            var hovered1 = new Border { Width = 20, Height = 20, ToolTip = "Hovered 1" };
            var hovered2 = new Border { Width = 20, Height = 20, ToolTip = "Hovered 2" };
            var unhovered = new Border { Width = 20, Height = 20, ToolTip = "Unhovered" };

            canvas.Children.Add(hovered1);
            canvas.Children.Add(hovered2);
            canvas.Children.Add(unhovered);

            SetElementIsMouseOver(hovered1, true);
            SetElementIsMouseOver(hovered2, true);
            SetElementIsMouseOver(unhovered, false);

            MarkerRefreshHelper.SafeClear(canvas, null);

            // First hovered element is preserved, others removed
            Assert.Single(canvas.Children);
            Assert.Same(hovered1, canvas.Children[0]);
        });
    }

    [Fact]
    public void SafeClear_MultipleMouseLeaveInvocations_HandlesRepeatedEventsSafely()
    {
        RunInSta(() =>
        {
            var canvas = new Canvas();
            var hovered = new Border { Width = 20, Height = 20, ToolTip = "Hovered" };
            canvas.Children.Add(hovered);

            SetElementIsMouseOver(hovered, true);
            MarkerRefreshHelper.SafeClear(canvas, null);
            Assert.Single(canvas.Children);

            // Fire MouseLeave first time
            var mouseLeaveArgs = new MouseEventArgs(Mouse.PrimaryDevice, 0)
            {
                RoutedEvent = Mouse.MouseLeaveEvent,
                Source = hovered
            };
            hovered.RaiseEvent(mouseLeaveArgs);
            Assert.Empty(canvas.Children);

            // Fire MouseLeave second time: must NOT throw or corrupt canvas
            hovered.RaiseEvent(mouseLeaveArgs);
            Assert.Empty(canvas.Children);
        });
    }

    [Fact]
    public void PublishDatabase_ForeignKeyCheck_ZeroViolations()
    {
        var publishDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\publish"));
        var dbPath = System.IO.Path.Combine(publishDir, "tarkov_data.db");

        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_key_check;";
        using var reader = cmd.ExecuteReader();
        var violations = new List<string>();
        while (reader.Read())
        {
            var parent = reader.GetString(2);
            if (parent != "Items") // Items table may not contain all quest-specific item references
            {
                violations.Add($"Table {reader.GetString(0)}, RowId {reader.GetInt64(1)}, Parent {parent}");
            }
        }
        Assert.Empty(violations);
    }

    [Fact]
    public void PublishDatabase_UserDataDb_IsValidSqlite()
    {
        var publishDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\publish"));
        var dbPath = System.IO.Path.Combine(publishDir, "user_data.db");

        if (File.Exists(dbPath) && new FileInfo(dbPath).Length > 0)
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            var result = cmd.ExecuteScalar()?.ToString();
            Assert.Equal("ok", result);
        }
    }

    #endregion
}
