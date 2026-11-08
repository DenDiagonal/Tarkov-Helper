# E2E Test Infrastructure: Requirements R1 & R2
# (5-Tab Main Navigation, Needed Items Kappa Integration & Tasks MAPS Layout Overhaul)

## 1. Test Philosophy & Design Principles
- **Requirement-Driven & Opaque-Box**: All test cases are derived strictly from `ORIGINAL_REQUEST.md` (Requirements R1 and R2) and `PROJECT.md` specifications, treating internal WPF visual tree and implementation mechanics as black boxes.
- **TarkovTracker Architectural Parity**: Alignment with upstream open-source TarkovTracker behaviors (4 view modes: `[LIST]`, `[TRADERS]`, `[MAPS]`, `[GRAPH WIP]`; 12 canonical map selector chips; compact combined items/keys strip; removal of redundant clock and legends).
- **Progressive Testability & Isolation**: Tests are structured across 4 distinct tiers, ensuring independent validation from unit contracts up to full real-world multi-step user workflows.
- **Culture Invariance**: Date, number, and string formatting tests enforce invariant culture to prevent false positives across multi-lingual user environments.

---

## 2. Feature Inventory (Requirements R1 & R2)

| # | Feature | Requirement Source | Tier 1 (Unit/Contract) | Tier 2 (Boundary/Filter) | Tier 3 (Cross-Module) | Tier 4 (Workflow) |
|---|---------|-------------------|:----------------------:|:------------------------:|:---------------------:|:-----------------:|
| F1 | 5-Tab Top Navigation Structure | ORIGINAL_REQUEST §R1 | ✓ | ✓ | ✓ | ✓ |
| F2 | Standalone Collector & Map Tab Elimination | ORIGINAL_REQUEST §R1 | ✓ | ✓ | ✓ | ✓ |
| F3 | Storyline (WIP) Landing Page Integration | ORIGINAL_REQUEST §R1 | ✓ | ✓ | ✓ | ✓ |
| F4 | Needed Items Kappa Integration (`IsTabKappa`) | ORIGINAL_REQUEST §R1 | ✓ | ✓ | ✓ | ✓ |
| F5 | Kappa Count Reactive Badge (`KappaCount`) | ORIGINAL_REQUEST §R1 | ✓ | ✓ | ✓ | ✓ |
| F6 | Kappa & Collector Item Filtering Logic | ORIGINAL_REQUEST §R1 | ✓ | ✓ | ✓ | ✓ |
| F7 | Tasks Header 4 View Modes (`LIST`, `TRADERS`, `MAPS`, `GRAPH WIP`) | ORIGINAL_REQUEST §R2 | ✓ | ✓ | ✓ | ✓ |
| F8 | Status Filter Bar & [FILTERS] Popup Parity | ORIGINAL_REQUEST §R2 | ✓ | ✓ | ✓ | ✓ |
| F9 | 12 Canonical Map Carousel Chips with Counters | ORIGINAL_REQUEST §R2 | ✓ | ✓ | ✓ | ✓ |
| F10 | MAPS Clock Widget Elimination (`TarkovClockWidget`) | ORIGINAL_REQUEST §R2 | ✓ | ✓ | ✓ | ✓ |
| F11 | MAPS Header & Bottom Legend Removal | ORIGINAL_REQUEST §R2 | ✓ | ✓ | ✓ | ✓ |
| F12 | Single-Line Combined Items & Keys Strip | ORIGINAL_REQUEST §R2 | ✓ | ✓ | ✓ | ✓ |
| F13 | Special Raid Equipment Only Filter (`SpecialEquipmentNames`) | ORIGINAL_REQUEST §R2 | ✓ | ✓ | ✓ | ✓ |
| F14 | Required Keys Without Stash Indicator | ORIGINAL_REQUEST §R2 | ✓ | ✓ | ✓ | ✓ |
| F15 | Active Task Cards Feed Below Map Strip | ORIGINAL_REQUEST §R2 | ✓ | ✓ | ✓ | ✓ |
| F16 | Interactive Map Canvas & Multi-Floor Switching | ORIGINAL_REQUEST §R2 | ✓ | ✓ | ✓ | ✓ |

---

## 3. 4-Tier Test Architecture

### Tier 1: 5-Tab Main Navigation Verification
Location: `TarkovHelper.Tests\E2E\NavigationAndMapsOverhaulE2ETests.cs` & `TarkovHelper.Tests\Pages\Navigation5TabTests.cs`
- **T1.1**: Verifies top navigation in `MainWindow` contains exactly 5 canonical tabs in required sequence:
  1. `TabDashboard` ("Dashboard")
  2. `TabStoryline` ("Storyline (WIP)")
  3. `TabTasks` ("Tasks", representing Quests/Misiones)
  4. `TabNeededItems` ("Needed Items", formerly "Items")
  5. `TabHideout` ("Hideout")
- **T1.2**: Verifies `TabCollector` ("Collector - Kappa") is completely absent from top bar navigation.
- **T1.3**: Verifies `TabMap` ("Map") is completely absent from top bar navigation.
- **T1.4**: Verifies selecting `TabStoryline` instantiates and displays `StorylinePage` with WIP banner.
- **T1.5**: Verifies selecting `TabTasks` displays `QuestListPage`.
- **T1.6**: Verifies selecting `TabNeededItems` displays `ItemsPage`.
- **T1.7**: Verifies selecting `TabHideout` displays `HideoutPage`.
- **T1.8**: Verifies cross-module map navigation request (`NavigateToMap(targetMap)`) smoothly redirects into Tasks MAPS view without requiring a standalone top-level Map tab.
- **T1.9**: Verifies backward-compatible aliases (`TabQuests`, `TabItems`) resolve safely without breaking legacy callers.

### Tier 2: Needed Items Kappa Filter Integration
Location: `TarkovHelper.Tests\ViewModels\NeededItemsViewModelTests.cs` & `TarkovHelper.Tests\E2E\NavigationAndMapsOverhaulE2ETests.cs`
- **T2.1**: Verifies `IsTabKappa` property in `NeededItemsViewModel`:
  - Returns `true` when `ActiveTab == "Kappa"`.
  - Returns `false` when `ActiveTab` is `All`, `Tasks`, `Hideout`, or `Completed`.
- **T2.2**: Verifies `KappaCount` counter calculation:
  - Dynamically calculates count of cards where `IsKappaRequired == true` OR `QuestNormalizedName == "collector"`.
  - Excludes non-Kappa quests and hideout module requirements from `KappaCount`.
- **T2.3**: Verifies filtering logic when `ActiveTab == "Kappa"`:
  - Retains quests flagged `IsKappaRequired == true`.
  - Retains Fence's "Collector" streamer items (`QuestNormalizedName == "collector"`).
  - Strictly filters out regular non-Kappa quests.
  - Strictly filters out all hideout station upgrade items.
- **T2.4**: Verifies stepper and progress interactions on Kappa tab:
  - Increment `[+]` updates `CurrentCount` and reflects in `ProgressText`.
  - Decrement `[-]` respects 0 lower bound.
  - Marking complete `[✓]` toggles completion status.
  - Toggling `HideOwned` filter removes completed Kappa cards while preserving incomplete ones.
- **T2.5**: Verifies combination with secondary filters:
  - Text search (e.g., "beanie", "salewa") operates within the Kappa item subset.
  - FIR filter (`IsFirOnly` / `IsFirNon`) accurately partitions Kappa items.
  - Sorting modes (`Priority`, `Name`, `Trader`, `Quantity`) reorder Kappa items accurately.

### Tier 3: MAPS Layout Verification
Location: `TarkovHelper.Tests\Pages\MapPageTests.cs` & `TarkovHelper.Tests\E2E\NavigationAndMapsOverhaulE2ETests.cs`
- **T3.1**: Verifies `TarkovClockWidget` is completely absent or collapsed (`Visibility == Collapsed` / 0px footprint) in MAPS view container.
- **T3.2**: Verifies bottom legend (`Task: Regular - Pinned - Team`) is completely removed from the map view.
- **T3.3**: Verifies single-line combined strip beneath the interactive map:
  - Left section: "Required items for active tasks".
    - Shows ONLY special raid equipment (MS2000 Marker, Wi-Fi Camera, Signal Jammer) matching `SpecialEquipmentNames` / category `Special equipment`.
    - Strictly excludes general barter and delivery items (Salewas, Gas Analyzers, Spark Plugs, Car Batteries, etc.).
  - Right section: "Required keys for active tasks".
    - Displays required keys for active quests on that map.
    - Strictly suppresses stash possession status (`InStash` indicator badges removed; neutral key chips).
- **T3.4**: Verifies interactive SVG map canvas and multi-floor level selector remain intact and interactive (Ground Level, Bunkers, Floors 1-3).
- **T3.5**: Verifies active task cards feed is rendered directly beneath the combined items/keys strip with objective checklists and action toggles.
- **T3.6**: Verifies 12 canonical map selector carousel chips (Ground Zero, Customs, Woods, Factory, Shoreline, Interchange, Reserve, Lighthouse, Streets of Tarkov, The Lab, The Labyrinth, Terminal) with reactive active quest counters.

### Tier 4: Real-World Workflow Scenarios
Location: `TarkovHelper.Tests\E2E\NavigationAndMapsOverhaulE2ETests.cs`
- **Scenario 1**: Full Navigation Lifecycle (Dashboard -> Needed Items [Kappa] -> Tasks [MAPS]):
  - User starts on Dashboard.
  - Switches to Needed Items tab, activates KAPPA pill tab, verifies streamer items and Kappa quest items are shown with valid `KappaCount`.
  - Increments progress on a Kappa item using stepper.
  - Switches to Tasks tab, activates MAPS view mode, selects Customs map.
  - Verifies Customs map displays SVG canvas, no clock widget, no bottom legend, and single combined strip showing only MS2000 markers and needed keys without stash badge.
- **Scenario 2**: In-Card Map Redirection Workflow:
  - User navigates to Tasks tab (LIST or TRADERS view).
  - Finds an active task requiring a map visit (e.g. "Checking" on Customs).
  - Clicks "Map" button on task card.
  - Verifies application smoothly activates Tasks MAPS view, selects Customs, centers map, and displays required keys and special items without requiring top-level TabMap.
- **Scenario 3**: Multi-Floor Inspection & Raid Preparation:
  - User selects Factory or Reserve map.
  - Switches between Ground Level and Bunker floors.
  - Verifies floor selector operates without disturbing the single combined strip or active task cards below.
- **Scenario 4**: Needed Items Kappa Multi-Facet Filtering:
  - User switches to Needed Items Kappa tab.
  - Toggles FIR-only filter and enters search term.
  - Verifies exact intersection of Kappa + FIR + Search query.
  - Clears filters and verifies return to full Kappa set.
- **Scenario 5**: PVP/PVE Profile Switch Invariance:
  - User switches profile between PVP and PVE while on 5-tab navigation.
  - Verifies active tab is preserved, Kappa counts re-evaluated, and map task counters updated.

---

## 4. Test Execution Guide

To execute all tests in the R1 & R2 test suite:
```powershell
dotnet test TarkovHelper.Tests\TarkovHelper.Tests.csproj --filter "FullyQualifiedName~NavigationAndMapsOverhaulE2ETests|FullyQualifiedName~Navigation5TabTests|FullyQualifiedName~NeededItemsViewModelTests|FullyQualifiedName~MapPageTests"
```

To run individual tiers:
- **Tier 1 (5-Tab Navigation)**:
  `dotnet test TarkovHelper.Tests\TarkovHelper.Tests.csproj --filter "FullyQualifiedName~Navigation5TabTests"`
- **Tier 2 (Needed Items Kappa Integration)**:
  `dotnet test TarkovHelper.Tests\TarkovHelper.Tests.csproj --filter "FullyQualifiedName~NeededItemsViewModelTests"`
- **Tier 3 (MAPS Layout Overhaul)**:
  `dotnet test TarkovHelper.Tests\TarkovHelper.Tests.csproj --filter "FullyQualifiedName~MapPageTests"`
- **Tier 4 (Real-World E2E Scenarios)**:
  `dotnet test TarkovHelper.Tests\TarkovHelper.Tests.csproj --filter "FullyQualifiedName~NavigationAndMapsOverhaulE2ETests"`
