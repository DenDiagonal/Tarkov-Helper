# Architectural Analysis & Plan: Needed Items Stepper Grouping

## 1. Overview
This document specifies the architectural design, data structures, and UI layout for consolidating duplicate needed items into a single card featuring an interactive progress stepper in TarkovHelper. The implementation directly aligns with TarkovTracker's open-source schema while strictly excluding multiplayer team tracking.

---

## 2. TarkovTracker Open-Source Alignment

### 2.1 Schema & Key Conventions
In TarkovTracker, item requirements across quests and hideout stations are addressed using canonical string keys:
- **Quest Item Need Key**:
  `quest:{questNormalizedName}:{itemNormalizedName}`
  *(For dogtag level requirements: `quest:{questNormalizedName}:{itemNormalizedName}:lv{minLevel}`)*
- **Hideout Item Need Key**:
  `hideout:{stationId}:{stationLevel}:{itemNormalizedName}`

### 2.2 Compatibility Model
| TarkovTracker Field | TarkovHelper Model | Representation | Description |
|---|---|---|---|
| `needKey` / `id` | `NeededItemProgress.Id` / `NeedKey` | `TEXT` PK | Canonical requirement identifier |
| `profile` | `NeededItemProgress.ProfileType` | `INTEGER` PK (0=PVP, 1=PVE) | Isolated persistence across profiles |
| `count` | `NeededItemProgress.Count` | `INTEGER` | User progress count (0 to N) |
| `max` / `countRequired` | `Amount` | `int` | Total required item quantity (N) |
| `complete` | `IsComplete` | `bool` (`Count >= Amount`) | Visual completion status |

---

## 3. Consolidation Logic

### 3.1 Quest Item Consolidation
- Multiple objectives requiring the same item within the same quest (e.g., 2 Salewas to hand over + 1 Salewa to find in raid) are consolidated into a single card.
- **Grouping Rule**:
  `groupKey = hasMinLevel ? $"{taskNormName}:dogtag:{DogtagMinLevel}" : taskNormName`
- **Total Amount**: `Sum(objective.Amount)`
- **FIR Requirement**: `Any(objective.FoundInRaid)` (FIR flag is marked if any objective demands FIR)
- **Dogtag Levels**: Segmented if `DogtagMinLevel > 0`, preserving Tarkov requirements semantics.

### 3.2 Hideout Item Consolidation
- Multiple requirements for the same item within the same station level are consolidated into a single card.
- **Grouping Rule**:
  `groupKey = $"{stationId}:{level.Level}"`
- **Total Amount**: `Sum(requirement.Count)`
- **FIR Requirement**: `Any(requirement.FoundInRaid)`

---

## 4. UI Layout & Stepper Controls

### 4.1 Layout Structure (WPF DataTemplate)
```
+-------------------------------------------------------------------------+
| [Quest Name or Hideout Station]                       [-]  0/N  [+] [✓] |
| Trader / Level                                        [FIR] [Lv.X+]     |
+-------------------------------------------------------------------------+
```

### 4.2 Interactive Stepper Behavior
- **[-] Decrement Button**: Decrements count by 1, floor at 0.
- **Progress Counter `[count]/[N]`**:
  - Displays dynamic text (e.g., `0/3`, `2/3`, `3/3`).
  - Colored amber when incomplete (`#F59E0B`), switches to green (`#4CAF50`) when complete (`count >= N`).
- **[+] Increment Button**: Increments count by 1, capped at `N`.
- **[✓] Toggle Complete Button**:
  - If current count is `< N`, immediately sets count to `N` (complete).
  - If current count is `== N`, resets count to `0`.

---

## 5. Persistence & Concurrency

### 5.1 SQLite Schema (`user_data.db`)
```sql
CREATE TABLE IF NOT EXISTS NeededItemProgress (
    Id TEXT,
    ProfileType INTEGER NOT NULL DEFAULT 0,
    Count INTEGER NOT NULL DEFAULT 0,
    UpdatedAt TEXT NOT NULL,
    PRIMARY KEY (Id, ProfileType)
);
CREATE INDEX IF NOT EXISTS idx_needed_item_progress_profile ON NeededItemProgress(ProfileType);
```

### 5.2 Debouncing, Serialization & Flushes
- Updates are debounced with a 400ms timer to minimize SQLite disk writes during rapid clicks.
- Atomic batch writes via `SaveNeededItemProgressBatchAsync`. Zero counts are deleted to minimize table bloat.
- **Write Serialization**:
  All asynchronous database writes are serialized through a dedicated `SemaphoreSlim(1, 1)` lock, guaranteeing sequential disk writes without SQLite database locking conflicts or write-order inversions.
- **In-Flight Task Tracking**:
  Active background saves are tracked via `_activeSaveTask`. `FlushPendingSaves()`, `FlushPendingSavesAsync()`, and `ClearAllProgressAsync()` await any in-flight background task to prevent data loss on application exit or accidental data resurrection after clearing.
- **UI Thread Deadlock Prevention**:
  `FlushPendingSaves()` executes via `Task.Run(...).GetAwaiter().GetResult()` and uses `ConfigureAwait(false)` in all data access operations, preventing sync-over-async deadlocks on WPF's `DispatcherSynchronizationContext`.
- **Exit & Profile Switch Flush**:
  `FlushPendingSaves()` is called synchronously on `App.OnExit` and `ProfileService.ProfileChanged` before switching profiles or terminating the process.
- **Race Condition Prevention**:
  `LoadProgressAsync` verifies `targetProfile == _loadedProfile` upon async completion, discarding stale loads from rapidly toggled profiles, and preserves any in-flight user edits in `_pendingSaves`.

---

## 6. Strict Exclusion of Multiplayer Features (R3)
- No team, squad, peer-to-peer, WebSocket, or remote player tracking schemas or UI components exist or are permitted in this module.
- All state management is strictly local and profile-bound (PVP / PVE).
