using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TarkovHelper.Pages.Map.Components;

/// <summary>
/// Helper for building unified high-contrast dark tooltips with gold header
/// and standardized ToolTipService configurations (30s duration, isolated placement).
/// </summary>
public static class MapTooltipHelper
{
    private static readonly SolidColorBrush GoldTitleBrush = new(Color.FromRgb(255, 215, 0));
    private static readonly SolidColorBrush LightTextBrush = new(Color.FromRgb(240, 240, 240));

    static MapTooltipHelper()
    {
        GoldTitleBrush.Freeze();
        LightTextBrush.Freeze();
    }

    /// <summary>
    /// Creates a formatted ToolTip with gold title (#FFD700) and readable light details (#F0F0F0).
    /// Uses PlacementMode.Top with a safe physical exclusion zone (-14px) to prevent popup HWND cursor collision.
    /// </summary>
    public static ToolTip CreateTooltip(string title, string? details = null, string? floorSuffix = null, double maxWidth = 300, UIElement? placementTarget = null)
    {
        var panel = new StackPanel
        {
            IsHitTestVisible = false
        };

        var displayTitle = string.IsNullOrEmpty(details) && !string.IsNullOrEmpty(floorSuffix)
            ? $"{title}{floorSuffix}"
            : title;

        var titleBlock = new TextBlock
        {
            Text = displayTitle,
            Foreground = GoldTitleBrush,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = maxWidth,
            IsHitTestVisible = false
        };
        panel.Children.Add(titleBlock);

        if (!string.IsNullOrEmpty(details))
        {
            var displayDetails = !string.IsNullOrEmpty(floorSuffix) ? $"{details}{floorSuffix}" : details;
            var detailBlock = new TextBlock
            {
                Text = displayDetails,
                Foreground = LightTextBrush,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = maxWidth,
                Margin = new Thickness(0, 2, 0, 0),
                IsHitTestVisible = false
            };
            panel.Children.Add(detailBlock);
        }

        var tooltip = new ToolTip
        {
            Content = panel,
            Placement = PlacementMode.Top,
            VerticalOffset = -14,
            IsHitTestVisible = false,
            Focusable = false
        };

        if (placementTarget != null)
        {
            tooltip.PlacementTarget = placementTarget;
        }

        return tooltip;
    }

    /// <summary>
    /// Configures 30 seconds show duration and responsive display delays on a visual element.
    /// Positions tooltip via PlacementMode.Top with -14px offset to eliminate cursor collision and flicker loops.
    /// </summary>
    public static void ConfigureToolTipService(DependencyObject target)
    {
        ToolTipService.SetShowDuration(target, 30000);
        ToolTipService.SetInitialShowDelay(target, 300);
        ToolTipService.SetBetweenShowDelay(target, 100);
        ToolTipService.SetPlacement(target, PlacementMode.Top);
        ToolTipService.SetVerticalOffset(target, -14);
        ToolTipService.SetShowOnDisabled(target, true);
    }
}
