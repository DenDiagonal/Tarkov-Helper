# TEST_READY: Requirements R1 & R2 E2E & Integration Test Suite
# (5-Tab Navigation, Needed Items Kappa Integration & Tasks MAPS Layout Overhaul)

**Status**: TEST SUITE COMPLETE & READY (Awaiting Implementer Remediation of Production Build Errors)  
**Author**: `teamwork_test_writer_e2e_r3`  
**Date**: 2026-10-29  
**Total Tests Added**: 35 automated tests across 4 tiers  
**Test Code Build Status**: 100% Clean (0 errors in test code)  
**Implementation Code Build Status**: 14 compilation errors in `TarkovHelper` production code (Escalated below)  

---

## 1. Test Suite Summary & Tier Breakdown

The test suite covers the entire feature scope of Requirements R1 and R2 using opaque-box, requirement-driven specifications derived from `ORIGINAL_REQUEST.md` and `PROJECT.md`.

| Tier | Category | Scope / Target | Test Count | Location | Status |
|---|---|---|:---:|---|:---:|
| **Tier 1** | 5-Tab Main Navigation Verification | Dashboard, Storyline (WIP), Tasks, Needed Items, Hideout; absence of TabCollector and TabMap; StorylinePage WIP; navigation contract methods | 10 | `TarkovHelper.Tests\Pages\Navigation5TabTests.cs` | READY |
| **Tier 2** | Needed Items Kappa Filter Integration | `IsTabKappa` property, `KappaCount` counter, filtering `IsKappaRequired || QuestNormalizedName == "collector"`, steppers, FIR, search, HideOwned | 10 | `TarkovHelper.Tests\ViewModels\NeededItemsKappaFilterTests.cs` | READY |
| **Tier 3** | Tasks MAPS Layout Overhaul | Clock elimination, legend elimination, single-line combined items/keys strip (special equipment only, keys without stash badge), 12 canonical maps | 10 | `TarkovHelper.Tests\Pages\MapsLayoutOverhaulTests.cs` | READY |
| **Tier 4** | Real-World Workflows & User Journeys | Full user journey (Dashboard -> Needed Items [Kappa] -> Tasks [MAPS]), task card map redirection, multi-floor inspection, PVP/PVE profile invariance | 5 | `TarkovHelper.Tests\E2E\NavigationAndMapsOverhaulE2ETests.cs` | READY |
| **Total** | **All Tiers Combined** | **Requirements R1 & R2 Complete Suite** | **35** | `TarkovHelper.Tests\` | **READY** |

---

## 2. Test Execution Command

Once the implementing worker applies the 3 production code fixes detailed in Section 5 below, the entire suite executes via:

```powershell
dotnet test TarkovHelper.Tests\TarkovHelper.Tests.csproj --filter "FullyQualifiedName~Navigation5TabTests|FullyQualifiedName~NeededItemsKappaFilterTests|FullyQualifiedName~MapsLayoutOverhaulTests|FullyQualifiedName~NavigationAndMapsOverhaulE2ETests"
```

To run individual tiers:
- **Tier 1 (5-Tab Navigation)**:
  ```powershell
  dotnet test TarkovHelper.Tests\TarkovHelper.Tests.csproj --filter "FullyQualifiedName~Navigation5TabTests"
  ```
- **Tier 2 (Needed Items Kappa Filter Integration)**:
  ```powershell
  dotnet test TarkovHelper.Tests\TarkovHelper.Tests.csproj --filter "FullyQualifiedName~NeededItemsKappaFilterTests"
  ```
- **Tier 3 (Tasks MAPS Layout Overhaul)**:
  ```powershell
  dotnet test TarkovHelper.Tests\TarkovHelper.Tests.csproj --filter "FullyQualifiedName~MapsLayoutOverhaulTests"
  ```
- **Tier 4 (Real-World E2E Workflows)**:
  ```powershell
  dotnet test TarkovHelper.Tests\TarkovHelper.Tests.csproj --filter "FullyQualifiedName~NavigationAndMapsOverhaulE2ETests"
  ```

---

## 3. Feature Coverage Matrix (R1 & R2)

| Feature | Requirement Source | Test Methods |
|---|---|---|
| **F1: 5-Tab Navigation Structure** | ORIGINAL_REQUEST §R1 | `T1_01_TabNavigation_BorderHeight_IsStreamlinedTo28px`<br>`T1_02_TabNavigation_ContainsExactlyFiveTabs`<br>`T1_03_TabNavigation_Sequence_IsDashboardStorylineTasksNeededItemsHideout` |
| **F2: Standalone Collector & Map Tab Elimination** | ORIGINAL_REQUEST §R1 | `T1_04_TabCollector_IsCompletelyAbsentFromTopNavigation`<br>`T1_05_TabMap_IsCompletelyAbsentFromTopNavigation` |
| **F3: Storyline (WIP) Landing Page** | ORIGINAL_REQUEST §R1 | `T1_06_StorylinePage_FileExists_AndIsConfiguredAsWipLandingPage` |
| **F4: Public Navigation Contracts & Aliases** | PROJECT.md §Interface Contracts | `T1_07_MainWindow_PublicNavigationContract_MethodsExist`<br>`T1_08_MainWindow_BackwardCompatibility_PropertiesExist`<br>`T1_09_TaskCardControl_MapClick_EliminatedDirectTabMapReference` |
| **F5: Kappa Tab in ItemsPage** | ORIGINAL_REQUEST §R1 | `T1_10_ItemsPage_ContainsKappaPillTab` |
| **F6: IsTabKappa & KappaCount Contracts** | PROJECT.md §Interface Contracts | `T2_01_Contract_NeededItemsViewModel_DeclaresKappaProperties`<br>`T2_02_IsTabKappa_ReturnsTrueOnlyWhenActiveTabIsKappa`<br>`T2_03_KappaCount_AggregatesBothReqKappaQuestsAndCollectorStreamerItems` |
| **F7: Kappa & Collector Streamer Items Filtering** | ORIGINAL_REQUEST §R1 | `T2_04_ActiveTabKappa_FiltersExclusivelyToKappaAndCollectorItems`<br>`T2_10_TabTransitions_MaintainCountersInvariance` |
| **F8: Interactive Steppers on Kappa Cards** | ORIGINAL_REQUEST §R1 | `T2_05_KappaCards_StepperInteractions_UpdateQuantitiesAndCompletion` |
| **F9: Compound Filters (FIR, Search, HideOwned, Sort)** | ORIGINAL_REQUEST §R1 | `T2_06_ActiveTabKappa_CombinesWithFirFilter`<br>`T2_07_ActiveTabKappa_CombinesWithSearchQuery`<br>`T2_08_ActiveTabKappa_HideOwnedFilter_HidesCompletedItems`<br>`T2_09_ActiveTabKappa_SortingPreserved` |
| **F10: Clock & Legend Elimination from MAPS** | ORIGINAL_REQUEST §R2 | `T3_01_MapPageXaml_TarkovClockWidget_IsAbsentOrCollapsed`<br>`T3_02_MapPageXaml_TopQuestsBarAndBottomLegend_AreAbsent` |
| **F11: Combined Items & Keys Single Strip** | ORIGINAL_REQUEST §R2 | `T3_03_MapPageXaml_ContainsCombinedItemsAndKeysSingleStrip` |
| **F12: Special Equipment Only Filter** | ORIGINAL_REQUEST §R2 | `T3_04_SpecialEquipmentFilter_IncludesOnlyPlaceableRaidGear` |
| **F13: Keys Without Stash Possession Badges** | ORIGINAL_REQUEST §R2 | `T3_05_MapKeysList_SuppressesStashPossessionIndicator` |
| **F14: Canvas, Floor Selector & Active Cards Feed** | ORIGINAL_REQUEST §R2 | `T3_06_MapPageXaml_InteractiveCanvasAndFloorSelector_AreRetained`<br>`T3_09_ActiveTaskCardsList_RenderedBeneathMapContainer`<br>`T3_10_MapSelection_ZeroActiveTasks_HandlesGracefullyWithoutCrash` |
| **F15: 12 Canonical Map Carousel Chips & 4 View Modes** | ORIGINAL_REQUEST §R2 | `T3_07_QuestListPage_MapChips_IncludeAll12CanonicalMaps`<br>`T3_08_QuestListPage_HeaderViewModes_IncludeFourRequiredModes` |
| **F16: E2E User Navigation Lifecycle** | Acceptance Criteria | `Scenario1_CompleteUserJourney_DashboardToNeededItemsKappaToTasksMaps` |
| **F17: E2E Task Card Map Redirection** | Acceptance Criteria | `Scenario2_TaskCardMapAction_RedirectsDirectlyToTasksMapsViewWithoutTopTabMap` |
| **F18: E2E Multi-Faceted Kappa Filter Combinations** | Acceptance Criteria | `Scenario3_MultiFacetedNeededItemsKappaFiltering_IntersectionAndReset` |
| **F19: E2E Multi-Floor Layer Inspection** | Acceptance Criteria | `Scenario4_MultiFloorMapSwitching_PreservesCombinedStripAndCards` |
| **F20: E2E Game Mode Invariance (PVP/PVE)** | Acceptance Criteria | `Scenario5_ProfileToggle_PreservesFiveTabNavigationAndRecomputesState` |

---

## 4. Test Files Added

1. `c:\Games\Proyectos\Tarkov-Helper\TarkovHelper.Tests\Common\R1R2TestHelpers.cs`:
   - Repository-relative path resolution (`ResolveRepoPath`).
   - Namespace-agnostic XAML DOM parser (`LoadXamlDocument`).
   - Canonical 12 maps definition (`Canonical12Maps`).
   - Placeable special equipment detector (`IsSpecialEquipment`).
   - Fluent test card factory (`CreateCard`).
2. `c:\Games\Proyectos\Tarkov-Helper\TarkovHelper.Tests\Pages\Navigation5TabTests.cs`:
   - 10 Tier 1 unit tests validating the 5-tab header structure and navigation contracts.
3. `c:\Games\Proyectos\Tarkov-Helper\TarkovHelper.Tests\ViewModels\NeededItemsKappaFilterTests.cs`:
   - 10 Tier 2 unit tests validating `IsTabKappa`, `KappaCount`, and compound filtering.
4. `c:\Games\Proyectos\Tarkov-Helper\TarkovHelper.Tests\Pages\MapsLayoutOverhaulTests.cs`:
   - 10 Tier 3 unit tests validating map layout clean-up, single combined strip, and special equipment.
5. `c:\Games\Proyectos\Tarkov-Helper\TarkovHelper.Tests\E2E\NavigationAndMapsOverhaulE2ETests.cs`:
   - 5 Tier 4 multi-step E2E integration scenarios.

---

## 5. Escalation: Implementation Bugs in Production Code

During verification of the test suite compilation, 14 compiler errors were detected in the `TarkovHelper` production code caused by an incomplete partial edit by the implementing agent:

### 1. `TarkovHelper\Pages\StorylinePage.xaml.cs`:
- **Line 20**: `mw.NavigateToTasks();` causes `error CS1061: 'MainWindow' does not contain a definition for 'NavigateToTasks'`.
- **Fix Required**: The implementer must add `public void NavigateToTasks()` to `TarkovHelper\MainWindow.xaml.cs`:
  ```csharp
  public void NavigateToTasks()
  {
      TabTasks.IsChecked = true;
      _questListPage ??= new QuestListPage();
      PageContent.Content = _questListPage;
  }
  ```

### 2. `TarkovHelper\Controls\TaskCardControl.xaml.cs`:
- **Line 803**: `mainWin.TabMap.IsChecked = true;` causes `error CS1061: 'MainWindow' does not contain a definition for 'TabMap'`.
- **Fix Required**: `TabMap` was correctly removed from `MainWindow.xaml`, but `TaskCardControl.xaml.cs` still accesses it. The implementer must route map clicks to `mainWin.NavigateToMap(targetMap)` or `questListPage.NavigateToTaskOnMap`.

### 3. `TarkovHelper\MainWindow.xaml.cs`:
- **Lines 783, 788, 795, 796, 878, 882**: `_collectorPage`, `_mapTrackerPage`, `TabCollector`, and `TabMap` were removed from `MainWindow.xaml` and fields, but lingering code in `RefreshCurrentProfileDataAsync()` and `Tab_Checked()` still references them.
- **Fix Required**: Remove obsolete references to `TabCollector` and `TabMap` from `RefreshCurrentProfileDataAsync()` and `Tab_Checked()`.
