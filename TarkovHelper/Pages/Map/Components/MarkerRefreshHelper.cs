using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TarkovHelper.Pages.Map.Components;

/// <summary>
/// Helper for safe refresh of marker containers.
/// Protects elements currently hovered by the user (IsMouseOver == true)
/// from immediate destruction so that active tooltips remain stable during tracking and floor updates.
/// Preserves element Tag properties (e.g. domain models for clicks) and elevates Z-index to prevent
/// newly spawned markers from stealing hit test events mid-hover.
/// </summary>
public static class MarkerRefreshHelper
{
    private static readonly DependencyProperty IsRetainedProperty =
        DependencyProperty.RegisterAttached(
            "IsRetained",
            typeof(bool),
            typeof(MarkerRefreshHelper),
            new PropertyMetadata(false));

    public static bool GetIsRetained(DependencyObject obj) => (bool)obj.GetValue(IsRetainedProperty);
    public static void SetIsRetained(DependencyObject obj, bool value) => obj.SetValue(IsRetainedProperty, value);

    /// <summary>
    /// Safely clears a Canvas container while retaining any element currently under the mouse cursor
    /// or with an active tooltip. When the mouse leaves the retained element and the tooltip is closed,
    /// it is cleanly removed.
    /// </summary>
    public static void SafeClear(Canvas? container, ICollection<FrameworkElement>? trackedList = null)
    {
        if (container == null)
            return;

        trackedList?.Clear();

        if (container.Children.Count == 0)
            return;

        // Find active element: prioritize one with an open tooltip, or first hovered element
        var protectedEl = container.Children
            .OfType<FrameworkElement>()
            .FirstOrDefault(el => el.ToolTip is ToolTip tt && tt.IsOpen)
            ?? container.Children
            .OfType<FrameworkElement>()
            .FirstOrDefault(el => el.IsMouseOver);

        if (protectedEl == null)
        {
            container.Children.Clear();
            return;
        }

        // Remove all children except the protected one
        for (int i = container.Children.Count - 1; i >= 0; i--)
        {
            if (container.Children[i] is FrameworkElement el && el != protectedEl)
            {
                container.Children.RemoveAt(i);
            }
        }

        // Elevate Z-Index so newly added markers in the canvas won't steal hit-testing and trigger premature MouseLeave
        Panel.SetZIndex(protectedEl, 9999);

        if (GetIsRetained(protectedEl))
            return; // Already hooked

        SetIsRetained(protectedEl, true);

        MouseEventHandler? onLeave = null;
        RoutedEventHandler? onClosed = null;

        void Cleanup()
        {
            if (onLeave != null) protectedEl.MouseLeave -= onLeave;
            if (protectedEl.ToolTip is ToolTip tt && onClosed != null) tt.Closed -= onClosed;
            protectedEl.ClearValue(IsRetainedProperty);
            if (container.Children.Contains(protectedEl))
            {
                container.Children.Remove(protectedEl);
            }
        }

        onLeave = (s, e) =>
        {
            if (protectedEl.ToolTip is ToolTip tt && tt.IsOpen)
            {
                return;
            }
            Cleanup();
        };

        if (protectedEl.ToolTip is ToolTip activeTt)
        {
            onClosed = (s, e) =>
            {
                if (!protectedEl.IsMouseOver)
                {
                    Cleanup();
                }
            };
            activeTt.Closed += onClosed;
        }

        protectedEl.MouseLeave += onLeave;
    }
}
