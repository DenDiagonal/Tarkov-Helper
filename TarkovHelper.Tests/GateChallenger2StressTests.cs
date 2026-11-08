using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Controls;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Pages;
using TarkovHelper.Pages.Map.ViewModels;
using TarkovHelper.Services;
using TarkovHelper.Services.Map;
using TarkovHelper.Tests.Common;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests
{
    /// <summary>
    /// Empirical adversarial stress test harness executed by Gate Challenger 2.
    /// Focuses on:
    /// 1. Interactive sub-objective steppers: rapid clicking, debounce persistence, SQLite concurrency, clamp bounds.
    /// 2. Status filter chips & live counters: immediate UI updates on quest completion and sub-objective changes.
    /// 3. Maps View and Minimap Overlay synchronization: rapid 10-map switching, SharpVectors cleanup, pinned objective beacon lifecycle.
    /// 4. SQLite database integrity: PRAGMA integrity_check, quick_check, foreign_key_check.
    /// </summary>
    [Collection("DatabaseUpdateServiceCollection")]
    public class GateChallenger2StressTests : IDisposable
    {
        private readonly NeededItemProgressService _neededItemService;
        private readonly UserDataDbService _userDataDb;
        private readonly List<string> _cleanUpKeys = new();

        public GateChallenger2StressTests()
        {
            _neededItemService = NeededItemProgressService.Instance;
            _userDataDb = UserDataDbService.Instance;
            _neededItemService.ClearInMemoryForTesting();
        }

        public void Dispose()
        {
            _neededItemService.ClearInMemoryForTesting();
            foreach (var key in _cleanUpKeys)
            {
                try
                {
                    _userDataDb.DeleteNeededItemProgressAsync(key, ProfileType.Pvp).GetAwaiter().GetResult();
                    _userDataDb.DeleteNeededItemProgressAsync(key, ProfileType.Pve).GetAwaiter().GetResult();
                }
                catch { }
            }
        }

        #region Objective 1: Interactive Sub-Objective Steppers & Concurrency

        [Fact]
        public async Task Objective1_Stepper_RapidBurst_DebouncedSavePreservesCountInSqlite()
        {
            var key = "quest:gate2_burst:salewa";
            _cleanUpKeys.Add(key);

            // Simulate rapid user clicks: 0 -> 1 -> 2 -> 3 -> 4 -> 5 within a few milliseconds
            for (int i = 1; i <= 5; i++)
            {
                _neededItemService.SetCount(key, i, 5);
                Thread.Sleep(5); // Rapid burst < 400ms debounce
            }

            Assert.Equal(5, _neededItemService.GetCount(key));

            // Synchronously flush pending debounced saves
            _neededItemService.FlushPendingSaves();

            // Verify in SQLite database
            var pvpCounts = await _userDataDb.LoadNeededItemProgressAsync(ProfileType.Pvp);
            Assert.True(pvpCounts.TryGetValue(key, out var storedCount), "Key was not found in SQLite database after flush!");
            Assert.Equal(5, storedCount);
        }

        [Fact]
        public async Task Objective1_Stepper_ConcurrentHammer_NoDeadlockAndPreservesState()
        {
            const int threadCount = 10;
            const int opsPerThread = 20;
            var keys = Enumerable.Range(0, 5).Select(i => $"quest:gate2_concurrency:item_{i}").ToList();
            foreach (var k in keys) _cleanUpKeys.Add(k);

            var tasks = new List<Task>();
            using var barrier = new Barrier(threadCount);

            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                tasks.Add(Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    for (int op = 0; op < opsPerThread; op++)
                    {
                        var targetKey = keys[(threadId + op) % keys.Count];
                        if (op % 3 == 0)
                            _neededItemService.Increment(targetKey, 10);
                        else if (op % 3 == 1)
                            _neededItemService.Decrement(targetKey, 10);
                        else
                            _neededItemService.SetCount(targetKey, (op % 5) + 1, 10);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Flush all pending debounced saves
            await _neededItemService.FlushPendingSavesAsync();

            // Verify SQLite can be read without lock or corruption exceptions
            var dbCounts = await _userDataDb.LoadNeededItemProgressAsync(ProfileType.Pvp);
            foreach (var k in keys)
            {
                int inMemory = _neededItemService.GetCount(k);
                if (inMemory > 0)
                {
                    Assert.True(dbCounts.ContainsKey(k), $"DB missing key {k}");
                    Assert.Equal(inMemory, dbCounts[k]);
                }
                else
                {
                    Assert.False(dbCounts.ContainsKey(k), $"DB should have deleted zeroed key {k}");
                }
            }
        }

        [Fact]
        public void Objective1_Stepper_StrictClampBounds_NeverExceedMaxOrDropBelowZero()
        {
            var key = "quest:gate2_clamp:gasmask";
            _cleanUpKeys.Add(key);
            const int max = 4;

            // 1. Negative inputs
            _neededItemService.SetCount(key, -100, max);
            Assert.Equal(0, _neededItemService.GetCount(key));

            _neededItemService.SetCount(key, int.MinValue, max);
            Assert.Equal(0, _neededItemService.GetCount(key));

            // 2. Decrement below zero
            var d1 = _neededItemService.Decrement(key, max);
            Assert.Equal(0, d1);
            Assert.Equal(0, _neededItemService.GetCount(key));

            // 3. Overflow inputs
            _neededItemService.SetCount(key, 999, max);
            Assert.Equal(max, _neededItemService.GetCount(key));

            _neededItemService.SetCount(key, int.MaxValue, max);
            Assert.Equal(max, _neededItemService.GetCount(key));

            // 4. Increment at max
            var inc = _neededItemService.Increment(key, max);
            Assert.Equal(max, inc);
            Assert.Equal(max, _neededItemService.GetCount(key));

            // 5. Zero maxCount edge case
            var zeroMaxKey = "quest:gate2_clamp:zeromax";
            _cleanUpKeys.Add(zeroMaxKey);
            _neededItemService.SetCount(zeroMaxKey, 10, 0);
            Assert.Equal(0, _neededItemService.GetCount(zeroMaxKey));
            Assert.True(_neededItemService.IsComplete(zeroMaxKey, 0));
        }

        [Fact]
        public async Task Objective1_Stepper_ZeroCountDeletesRecordFromSqlite()
        {
            var key = "quest:gate2_zero:morphine";
            _cleanUpKeys.Add(key);

            // Step 1: save count = 3
            _neededItemService.SetCount(key, 3, 5);
            _neededItemService.FlushPendingSaves();

            var before = await _userDataDb.LoadNeededItemProgressAsync(ProfileType.Pvp);
            Assert.True(before.ContainsKey(key));
            Assert.Equal(3, before[key]);

            // Step 2: decrement to 0 and flush
            _neededItemService.SetCount(key, 0, 5);
            _neededItemService.FlushPendingSaves();

            var after = await _userDataDb.LoadNeededItemProgressAsync(ProfileType.Pvp);
            Assert.False(after.ContainsKey(key), "Row with count 0 should be deleted from SQLite!");
        }

        #endregion

        #region Objective 2: Status Filter Chips & Live Counters

        [Fact]
        public void Objective2_StatusFilterChips_CompletingQuestImmediatelyUpdatesLiveBadges()
        {
            // Set up a mock quest chain:
            // Quest A: Available, unlocks Quest B
            // Quest B: Locked, requires Quest A
            var taskA = new TarkovTask
            {
                Id = "task_a",
                NormalizedName = "task_a",
                Name = "Task Alpha",
                Trader = "Prapor",
                LeadsTo = new List<string> { "task_b" }
            };

            var taskB = new TarkovTask
            {
                Id = "task_b",
                NormalizedName = "task_b",
                Name = "Task Beta",
                Trader = "Prapor"
            };

            var vmA = new QuestViewModel
            {
                Task = taskA,
                DisplayName = "Task Alpha",
                Status = QuestStatus.Active
            };

            var vmB = new QuestViewModel
            {
                Task = taskB,
                DisplayName = "Task Beta",
                Status = QuestStatus.Locked
            };

            var allVms = new List<QuestViewModel> { vmA, vmB };
            var criteria = new QuestFilterCriteria { ViewMode = TasksViewMode.List };

            // Initial counts: 1 Available, 1 Locked, 0 Completed
            var initialCounts = QuestFilterEngine.ComputeStatusCounts(allVms, criteria, 1);
            Assert.Equal(2, initialCounts.All);
            Assert.Equal(1, initialCounts.Available);
            Assert.Equal(1, initialCounts.Locked);
            Assert.Equal(0, initialCounts.Completed);

            // Simulate user completing Quest A
            vmA.Status = QuestStatus.Done;
            vmB.Status = QuestStatus.Active; // Unlocked!

            // Compute counts immediately
            var updatedCounts = QuestFilterEngine.ComputeStatusCounts(allVms, criteria, 1);
            Assert.Equal(2, updatedCounts.All);
            Assert.Equal(1, updatedCounts.Available); // Task Beta is now available!
            Assert.Equal(0, updatedCounts.Locked);    // Task Beta no longer locked
            Assert.Equal(1, updatedCounts.Completed); // Task Alpha is completed
        }

        [Fact]
        public void Objective2_TaskCard_SubObjectiveInteractiveUpdates_RequirementsSummarySync()
        {
            ObjectiveProgressService.Instance.ClearInMemoryForTesting();
            NeededItemProgressService.Instance.ClearInMemoryForTesting();
            var task = new TarkovTask
            {
                Id = "multi_obj_task",
                NormalizedName = "multi_obj_task",
                Name = "Multi Objective Task",
                Trader = "Therapist",
                RequiredItems = new List<QuestItem>
                {
                    new() { ItemNormalizedName = "salewa", Amount = 3, FoundInRaid = true }
                },
                Objectives = new List<string>
                {
                    "Locate the ambulance in Customs"
                }
            };

            var cardVm = new TaskCardViewModel();
            cardVm.LoadFromTask(task);

            Assert.Equal(2, cardVm.TotalRequirements);
            Assert.Equal(0, cardVm.MetRequirements);
            Assert.Equal("Requirements 0 of 2 met", cardVm.RequirementsSummaryText);

            // 1. Interactive stepper increment to max (3/3)
            var stepper = cardVm.ItemSteppers.First();
            stepper.Increment();
            stepper.Increment();
            stepper.Increment();
            Assert.Equal(3, stepper.CurrentCount);
            Assert.Equal(1, cardVm.MetRequirements);
            Assert.Equal("Requirements 1 of 2 met", cardVm.RequirementsSummaryText);

            // 2. Interactive action check toggle
            var action = cardVm.ActionToggles.First();
            action.Toggle();
            Assert.True(action.IsChecked);
            Assert.Equal(2, cardVm.MetRequirements);
            Assert.Equal("Requirements 2 of 2 met", cardVm.RequirementsSummaryText);

            // 3. Mark complete button
            cardVm.MarkComplete();
            Assert.True(cardVm.IsCompleted);
            Assert.Equal(2, cardVm.MetRequirements);
        }

        #endregion

        #region Objective 3: Maps View and Minimap Overlay Synchronization

        [Fact]
        public void Objective3_MapSwitching_RapidSwitchAcrossAll10CanonicalMaps_SyncsSynchronously()
        {
            var canonicalMaps = new[]
            {
                "ground-zero",
                "customs",
                "woods",
                "factory",
                "interchange",
                "shoreline",
                "reserve",
                "lighthouse",
                "streets",
                "the-lab"
            };

            var tracker = MapTrackerService.Instance;
            string? lastReportedMap = null;
            tracker.MapChanged += m => lastReportedMap = m;

            // Rapidly switch across all 10 maps 10 times (100 switches)
            for (int round = 0; round < 10; round++)
            {
                foreach (var map in canonicalMaps)
                {
                    var expected = tracker.ResolveMapKey(map) ?? map;
                    tracker.SetCurrentMap(map, force: true);
                    Assert.Equal(expected, tracker.CurrentMapKey);
                    Assert.Equal(expected, lastReportedMap);

                    // Check that the map config exists for this canonical key
                    var config = tracker.GetMapConfig(map);
                    Assert.NotNull(config);
                    Assert.False(string.IsNullOrEmpty(config!.DisplayName));
                }
            }
        }

        [Fact]
        public void Objective3_PinnedObjectiveBeaconLifecycle_PinSwitchFloorsUnpin_Lockstep()
        {
            var tracker = MapTrackerService.Instance;
            TaskObjectiveWithLocation? minimapPinned = null;
            string? minimapFloor = null;

            Action<TaskObjectiveWithLocation?> pinnedHandler = o => minimapPinned = o;
            Action<string?> floorHandler = f => minimapFloor = f;

            tracker.PinnedObjectiveChanged += pinnedHandler;
            tracker.FloorChanged += floorHandler;

            try
            {
                // 1. Pin objective
                var objective = new TaskObjectiveWithLocation
                {
                    QuestId = "checking",
                    ObjectiveId = "find_bronze_pocket_watch",
                    Locations = new List<QuestObjectiveLocation>
                    {
                        new() { X = 150.0, Y = 250.0, MapNormalizedName = "customs", FloorId = "ground" }
                    }
                };

                tracker.PinObjective(objective);
                Assert.NotNull(tracker.PinnedObjective);
                Assert.NotNull(minimapPinned);
                Assert.Equal("find_bronze_pocket_watch", minimapPinned!.ObjectiveId);

                // 2. Switch floor
                tracker.SetCurrentFloor("floor_2");
                Assert.Equal("floor_2", tracker.CurrentFloorId);
                Assert.Equal("floor_2", minimapFloor);

                // 3. Unpin objective
                tracker.PinObjective(null);
                Assert.Null(tracker.PinnedObjective);
                Assert.Null(minimapPinned);

                // 4. Stress cycle: 50 rapid pin/unpin toggles
                for (int i = 0; i < 50; i++)
                {
                    tracker.PinObjective(objective);
                    Assert.Equal("checking", tracker.PinnedObjective?.QuestId);
                    tracker.PinObjective(null);
                    Assert.Null(tracker.PinnedObjective);
                }
            }
            finally
            {
                tracker.PinnedObjectiveChanged -= pinnedHandler;
                tracker.FloorChanged -= floorHandler;
            }
        }

        [Fact]
        public void Objective3_SvgStylePreprocessor_RapidFloorFilterProcessing_DoesNotCorruptOrThrow()
        {
            var preprocessor = new SvgStylePreprocessor();

            // Sample SVG content with floor group layers
            string sampleSvg = @"<?xml version=""1.0"" encoding=""utf-8""?>
<svg version=""1.1"" xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 1000 1000"">
  <g id=""floor_ground"" class=""floor"">
    <rect x=""10"" y=""10"" width=""100"" height=""100"" fill=""blue""/>
  </g>
  <g id=""floor_2"" class=""floor"">
    <rect x=""50"" y=""50"" width=""100"" height=""100"" fill=""red""/>
  </g>
</svg>";

            var tempInputPath = Path.Combine(Path.GetTempPath(), $"gate2_test_{Guid.NewGuid():N}.svg");
            File.WriteAllText(tempInputPath, sampleSvg);

            try
            {
                // Rapidly cycle floor visibility 20 times
                for (int i = 0; i < 20; i++)
                {
                    var visibleFloor = (i % 2 == 0) ? "floor_ground" : "floor_2";
                    var processed = preprocessor.ProcessSvgFile(
                        tempInputPath,
                        visibleFloorIds: new[] { visibleFloor },
                        allFloorIds: new[] { "floor_ground", "floor_2" },
                        backgroundFloorId: null,
                        backgroundOpacity: 0.3
                    );

                    Assert.False(string.IsNullOrEmpty(processed));
                    Assert.Contains("<svg", processed);
                }
            }
            finally
            {
                if (File.Exists(tempInputPath)) File.Delete(tempInputPath);
            }
        }

        #endregion

        #region Objective 4: SQLite Database Integrity

        [Fact]
        public void Objective4_DatabaseIntegrity_AllSqliteDatabasesPassIntegrityAndQuickCheck()
        {
            // Collect all .db files in project and publish directories
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var workspaceDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));

            var candidatePaths = new[]
            {
                Path.Combine(workspaceDir, "publish", "tarkov_data.db"),
                Path.Combine(workspaceDir, "publish", "user_data.db"),
                Path.Combine(workspaceDir, "tarkov_data.db"),
                Path.Combine(workspaceDir, "TarkovHelper", "tarkov_data.db"),
                Path.Combine(workspaceDir, "TarkovHelper", "Assets", "tarkov_data.db")
            };

            int checkedDbs = 0;

            foreach (var dbPath in candidatePaths)
            {
                if (!File.Exists(dbPath)) continue;

                checkedDbs++;
                var connStr = $"Data Source={dbPath};Mode=ReadOnly;";
                using var conn = new SqliteConnection(connStr);
                conn.Open();

                // 1. PRAGMA integrity_check
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA integrity_check;";
                    var res = cmd.ExecuteScalar()?.ToString();
                    Assert.Equal("ok", res);
                }

                // 2. PRAGMA quick_check
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA quick_check;";
                    var res = cmd.ExecuteScalar()?.ToString();
                    Assert.Equal("ok", res);
                }

                // 3. PRAGMA foreign_key_check
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA foreign_key_check;";
                    using var reader = cmd.ExecuteReader();
                    bool hasFkViolations = reader.Read();
                    Assert.False(hasFkViolations, $"Foreign key check failed for {dbPath}");
                }
            }

            Assert.True(checkedDbs > 0, "At least one target SQLite database should exist and be verified!");
        }

        [Fact]
        public async Task Objective4_DatabaseIntegrity_UserDataDbConcurrentStress_RemainsUncorrupted()
        {
            var testKeys = Enumerable.Range(0, 10).Select(i => $"quest:gate2_db_stress:key_{i}").ToList();
            foreach (var k in testKeys) _cleanUpKeys.Add(k);

            // Run 15 concurrent batch writes
            var writeTasks = Enumerable.Range(0, 15).Select(idx => Task.Run(async () =>
            {
                var batch = testKeys.Select((k, i) => (k, (idx + i) % 5 + 1)).ToList();
                await _userDataDb.SaveNeededItemProgressBatchAsync(batch, ProfileType.Pvp);
            }));

            // Run 15 concurrent reads
            var readTasks = Enumerable.Range(0, 15).Select(_ => Task.Run(async () =>
            {
                await _userDataDb.LoadNeededItemProgressAsync(ProfileType.Pvp);
            }));

            await Task.WhenAll(writeTasks.Concat(readTasks));

            // Verify integrity of user_data.db after stress
            var dbPath = _userDataDb.DatabasePath;
            if (File.Exists(dbPath))
            {
                using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA integrity_check;";
                var res = cmd.ExecuteScalar()?.ToString();
                Assert.Equal("ok", res);
            }
        }

        #endregion
    }
}
