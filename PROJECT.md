# Project: TarkovHelper - Visual Modernization, UI Sharpness & Tactical Hierarchy Overhaul
# Scope: Requirements R1, R2, R3, R4 (Display Rendering, Tactical Dark Palette, TaskCard Hierarchy, Map Zoom Fix)

## Architecture
TarkovHelper is a desktop application written in WPF and .NET 8 (`net8.0-windows`). This overhaul modernizes the visual styling and eliminates the rough, blurry, or low-resolution appearance in WPF. It introduces a 5-level tactical surface hierarchy inspired by Escape from Tarkov and TarkovTracker, refines the visual hierarchy of `TaskCardControl` while preserving 100% of data and controls, and fixes visual inconsistencies in the Tasks - Maps view (notably the clipped "Res" zoom reset button).

### Component Architecture & Module Boundaries
1. **Rendering & Composition Pipeline (`App.xaml`, `MainWindow.xaml`):**
   - Configures `TextOptions.TextFormattingMode="Display"`, `TextOptions.TextRenderingMode="ClearType"`, `RenderOptions.ClearTypeHint="Enabled"`, `SnapsToDevicePixels="True"`, and `UseLayoutRounding="True"`.
   - Propagated via default styles in `App.xaml` and window/page roots to eliminate font blurriness and fuzzy borders.

2. **Tactical Dark Palette & Surface Elevation System (`App.xaml`, `TacticalBrushes.cs`):**
   - 5-level surface elevation:
     - Surface 0 (`#0D0E11`): Deep app canvas
     - Surface 1 (`#131519`): Navigation and toolbars
     - Surface 2 (`#1A1D23`): Cards and major panels
     - Surface 3 (`#21252D`): Insets, objective boxes, item rows
     - Surface 4 (`#292E38`): Interactive controls, active chips, hover states
   - Tactical Tinted Badge System (15-20% container tint + 1px subtle border + high contrast text) replacing garish solid Material neons.
   - 100% backward-compatible brush aliasing for legacy keys (`BackgroundDarkBrush`, `BorderBrush`, etc.).

3. **Tasks - Maps & Interactive Map Controls (`MapPage.xaml`, `MapPage.xaml.cs`, `QuestListPage.xaml`):**
   - Fixes the zoom reset button bug where localized text overwrote `Content="⟳"` in a 28x28px button, clipping to `"Res"`. Redirects localized text to `ToolTip` and preserves the vector glyph.
   - Applies pixel-snapped rendering to the map canvas, floor selector, and requirements strip.

4. **TaskCardControl Visual Hierarchy Polish (`TaskCardControl.xaml`, `TaskCardControl.xaml.cs`):**
   - Eliminates restrictive `MaxWidth="320"` on title, allowing clean layout on wide feeds.
   - Refactors status and attribute badges (Active, FIR, Any Map, Kappa, Story, Level) into a responsive, tactical `WrapPanel`.
   - Direct XAML bindings for `RewardsFooterVisibility` and `RewardsStripVisibility`.
   - Normalizes fractional font sizes (9.5, 10.5, 11.5) to crisp integers (10, 11, 12).
   - Preserves all 24+ components, bindings, interaction steppers, and commands without data loss.

---

## Feature Inventory

| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| F1 | Global WPF Text Rendering & Sharpness Pipeline | Set Display mode, ClearType, SnapsToDevicePixels, UseLayoutRounding in App.xaml and MainWindow.xaml | M1 | ORIGINAL_REQUEST §R1 |
| F2 | 5-Level Tactical Surface Palette & Frozen Brushes | Implement EFT/TarkovTracker dark palette, elevation surfaces, tinted badges, and TacticalBrushes.cs | M1 | ORIGINAL_REQUEST §R2 |
| F3 | Interactive Map Zoom Control & Adjacent Controls Bug Fix | Fix "Res" button clipping in MapPage.xaml.cs, preserve ⟳ glyph, set localized tooltip | M2 | ORIGINAL_REQUEST §R4 |
| F4 | Tasks - Maps View & Quest List Styling Alignment | Apply tactical surface styling, borders, and sharpness to QuestListPage and embedded map | M2 | ORIGINAL_REQUEST §R2, §R4 |
| F5 | TaskCardControl Typography & Title Truncation Fix | Remove MaxWidth="320", normalize font sizes to whole integers, apply ClearType and snapping | M3 | ORIGINAL_REQUEST §R1, §R3 |
| F6 | TaskCardControl Tactical Badges & Elevated Objectives | WrapPanel for badges, tactical dark tinted badges, elevated objective boxes with aligned controls | M3 | ORIGINAL_REQUEST §R2, §R3 |
| F7 | TaskCardControl Rewards Footer Clean Binding & Stepper Polish | Clean XAML bindings for RewardsFooterVisibility, polish stepper buttons [-] X/Y [+] [✓] | M3 | ORIGINAL_REQUEST §R3 |
| F8 | Solution Build, Unit Test Suite & Publish Integrity Verification | Clean dotnet build, all unit tests pass, publish\tarkov_data.db preserved, self-contained publish check | M4 | ORIGINAL_REQUEST §Verification |

---

## Milestones

| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Foundation: WPF Crisp Rendering & Tactical Dark Palette | F1, F2 | None | DONE |
| M2 | Tasks - Maps View & Map Zoom Fix | F3, F4 | M1 | DONE |
| M3 | TaskCardControl Visual Hierarchy & Layout Polish | F5, F6, F7 | M1 | DONE |
| M4 | Integration, Gate Verification & Forensic Integrity Audit | F8 | M1, M2, M3 | DONE |

---

## Interface Contracts

### 1. Tactical Brushes Contract (`TarkovHelper\Common\TacticalBrushes.cs`)
```csharp
namespace TarkovHelper.Common
{
    public static class TacticalBrushes
    {
        // Surfaces
        public static readonly SolidColorBrush Surface0; // #0D0E11
        public static readonly SolidColorBrush Surface1; // #131519
        public static readonly SolidColorBrush Surface2; // #1A1D23
        public static readonly SolidColorBrush Surface3; // #21252D
        public static readonly SolidColorBrush Surface4; // #292E38

        // Borders
        public static readonly SolidColorBrush BorderSubtle;   // #242832
        public static readonly SolidColorBrush BorderMedium;   // #323846
        public static readonly SolidColorBrush BorderHighlight;// #4A5368

        // Status Tinted Badges (Background + Border + Foreground)
        public static readonly SolidColorBrush ActiveBg;
        public static readonly SolidColorBrush ActiveBorder;
        public static readonly SolidColorBrush ActiveFg;
        // Completed, Locked, LevelLocked, Failed, FIR, Kappa, Story...
    }
}
```

### 2. MapPage Reset Button Contract
```csharp
// In MapPage.xaml.cs: ApplyLocalization()
BtnResetView.Content = "⟳";
BtnResetView.ToolTip = _loc.ResetViewTooltip;
```

### 3. TaskCardControl Contracts
```csharp
// In TaskCardControl.xaml & .cs
// Preserves all existing properties: CardModel, ItemSteppers, ActionToggles,
// RequirementsSummaryText, ImpactSummaryText, RewardsFooterVisibility, etc.
```

---

## Code Layout
- `TarkovHelper\App.xaml` & `TarkovHelper\MainWindow.xaml` (Owned by M1)
- `TarkovHelper\Common\TacticalBrushes.cs` (Owned by M1)
- `TarkovHelper\Pages\Map\MapPage.xaml` & `MapPage.xaml.cs` (Owned by M2)
- `TarkovHelper\Pages\QuestListPage.xaml` & `QuestListPage.xaml.cs` (Owned by M2)
- `TarkovHelper\Controls\TaskCardControl.xaml` & `TaskCardControl.xaml.cs` (Owned by M3)
- `TarkovHelper.Tests\` (Owned by M4)
