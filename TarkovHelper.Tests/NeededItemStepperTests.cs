using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Media;
using TarkovHelper.Models;
using TarkovHelper.Pages;
using TarkovHelper.Services;
using Xunit;

namespace TarkovHelper.Tests
{
    [Collection("DatabaseUpdateServiceCollection")]
    public class NeededItemStepperTests : IDisposable
    {
        private readonly NeededItemProgressService _progressService;
        private readonly UserDataDbService _dbService;
        private readonly List<string> _testKeysToClean = new();

        public NeededItemStepperTests()
        {
            _progressService = NeededItemProgressService.Instance;
            _dbService = UserDataDbService.Instance;
            _progressService.ClearInMemoryForTesting();
        }

        public void Dispose()
        {
            _progressService.ClearInMemoryForTesting();
            foreach (var key in _testKeysToClean)
            {
                try
                {
                    _dbService.DeleteNeededItemProgressAsync(key, ProfileType.Pvp).GetAwaiter().GetResult();
                    _dbService.DeleteNeededItemProgressAsync(key, ProfileType.Pve).GetAwaiter().GetResult();
                }
                catch { }
            }
        }

        #region 1. Canonical Key Generation & Normalization

        [Fact]
        public void CanonicalKeys_QuestItemKey_FormatsAndNormalizesCorrectly()
        {
            var key1 = NeededItemProgressService.GetQuestItemKey("  Shortage  ", "  Salewa_First_Aid_Kit  ");
            Assert.Equal("quest:shortage:salewa_first_aid_kit", key1);

            var key2 = NeededItemProgressService.GetQuestItemKey("Punisher_Part_6", "BEAR_DOGTAG", 25);
            Assert.Equal("quest:punisher_part_6:bear_dogtag:lv25", key2);

            var key3 = NeededItemProgressService.GetQuestItemKey("Quest", "Item", 0);
            Assert.Equal("quest:quest:item", key3);
        }

        [Fact]
        public void CanonicalKeys_HideoutItemKey_FormatsAndNormalizesCorrectly()
        {
            var key1 = NeededItemProgressService.GetHideoutItemKey("  Medstation  ", 2, "  Salewa  ");
            Assert.Equal("hideout:medstation:2:salewa", key1);

            var key2 = NeededItemProgressService.GetHideoutItemKey("workbench", 3, "wires");
            Assert.Equal("hideout:workbench:3:wires", key2);
        }

        #endregion

        #region 2. Stepper Bounds: Increment, Decrement, Clamp

        [Fact]
        public void Stepper_Increment_CapsAtMaxCount()
        {
            var key = "quest:test_quest:item_inc";
            _testKeysToClean.Add(key);
            int max = 3;

            Assert.Equal(0, _progressService.GetCount(key));

            var c1 = _progressService.Increment(key, max);
            Assert.Equal(1, c1);
            Assert.Equal(1, _progressService.GetCount(key));

            var c2 = _progressService.Increment(key, max);
            Assert.Equal(2, c2);

            var c3 = _progressService.Increment(key, max);
            Assert.Equal(3, c3);
            Assert.True(_progressService.IsComplete(key, max));

            // Should cap at 3
            var c4 = _progressService.Increment(key, max);
            Assert.Equal(3, c4);
            Assert.Equal(3, _progressService.GetCount(key));
        }

        [Fact]
        public void Stepper_Decrement_FloorsAtZero()
        {
            var key = "quest:test_quest:item_dec";
            _testKeysToClean.Add(key);
            int max = 4;

            _progressService.SetCount(key, 2, max);
            Assert.Equal(2, _progressService.GetCount(key));

            var c1 = _progressService.Decrement(key, max);
            Assert.Equal(1, c1);

            var c2 = _progressService.Decrement(key, max);
            Assert.Equal(0, c2);

            // Should floor at 0
            var c3 = _progressService.Decrement(key, max);
            Assert.Equal(0, c3);
            Assert.Equal(0, _progressService.GetCount(key));
        }

        [Fact]
        public void Stepper_ToggleComplete_TogglesBetweenMaxAndZero()
        {
            var key = "quest:test_quest:item_toggle";
            _testKeysToClean.Add(key);
            int max = 5;

            // When at 0, toggle jumps to max
            var t1 = _progressService.ToggleComplete(key, max);
            Assert.Equal(5, t1);
            Assert.True(_progressService.IsComplete(key, max));

            // When at max, toggle resets to 0
            var t2 = _progressService.ToggleComplete(key, max);
            Assert.Equal(0, t2);
            Assert.False(_progressService.IsComplete(key, max));

            // When partial (2/5), toggle jumps to max
            _progressService.SetCount(key, 2, max);
            var t3 = _progressService.ToggleComplete(key, max);
            Assert.Equal(5, t3);
            Assert.True(_progressService.IsComplete(key, max));
        }

        [Fact]
        public void Stepper_ProgressChangedEvent_FiresWithCorrectPayload()
        {
            var key = "quest:test_quest:event_test";
            _testKeysToClean.Add(key);
            int max = 3;

            NeededItemProgressChangedEventArgs? captured = null;
            EventHandler<NeededItemProgressChangedEventArgs> handler = (s, e) => captured = e;

            _progressService.ProgressChanged += handler;
            try
            {
                _progressService.Increment(key, max);
                Assert.NotNull(captured);
                Assert.Equal(key, captured!.NeedKey);
                Assert.Equal(1, captured.Count);
                Assert.Equal(max, captured.MaxCount);
                Assert.False(captured.IsComplete);

                _progressService.ToggleComplete(key, max);
                Assert.NotNull(captured);
                Assert.Equal(3, captured!.Count);
                Assert.True(captured.IsComplete);
            }
            finally
            {
                _progressService.ProgressChanged -= handler;
            }
        }

        #endregion

        #region 3. ViewModels Interactive Stepper & Data Binding

        [Fact]
        public void QuestItemSourceViewModel_StepperOperations_UpdatePropertiesAndNotify()
        {
            var key = "quest:vm_test:salewa";
            _testKeysToClean.Add(key);

            var vm = new QuestItemSourceViewModel
            {
                QuestName = "Shortage",
                TraderName = "Therapist",
                ItemNormalizedName = "salewa",
                NeedKey = key,
                Amount = 3,
                CurrentCount = 0
            };

            var changedProps = new List<string>();
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != null) changedProps.Add(e.PropertyName);
            };

            Assert.Equal("0/3", vm.ProgressText);
            Assert.False(vm.IsComplete);

            vm.Increment();
            Assert.Equal(1, vm.CurrentCount);
            Assert.Equal("1/3", vm.ProgressText);
            Assert.False(vm.IsComplete);
            Assert.Contains(nameof(vm.CurrentCount), changedProps);
            Assert.Contains(nameof(vm.ProgressText), changedProps);
            Assert.Contains(nameof(vm.IsComplete), changedProps);

            vm.ToggleComplete();
            Assert.Equal(3, vm.CurrentCount);
            Assert.Equal("3/3", vm.ProgressText);
            Assert.True(vm.IsComplete);

            vm.Decrement();
            Assert.Equal(2, vm.CurrentCount);
            Assert.Equal("2/3", vm.ProgressText);
            Assert.False(vm.IsComplete);

            vm.ToggleComplete(); // from partial (2) to complete (3)
            Assert.Equal(3, vm.CurrentCount);
            Assert.True(vm.IsComplete);

            vm.ToggleComplete(); // from complete (3) to 0
            Assert.Equal(0, vm.CurrentCount);
            Assert.Equal("0/3", vm.ProgressText);
            Assert.False(vm.IsComplete);
        }

        [Fact]
        public void HideoutItemSourceViewModel_StepperOperations_UpdatePropertiesAndNotify()
        {
            var key = "hideout:medstation:2:salewa";
            _testKeysToClean.Add(key);

            var vm = new HideoutItemSourceViewModel
            {
                ModuleName = "Medstation",
                StationId = "medstation",
                Level = 2,
                ItemNormalizedName = "salewa",
                NeedKey = key,
                Amount = 2,
                CurrentCount = 0
            };

            Assert.Equal("0/2", vm.ProgressText);
            Assert.False(vm.IsComplete);

            vm.Increment();
            Assert.Equal(1, vm.CurrentCount);
            Assert.Equal("1/2", vm.ProgressText);
            Assert.False(vm.IsComplete);

            vm.Increment();
            Assert.Equal(2, vm.CurrentCount);
            Assert.Equal("2/2", vm.ProgressText);
            Assert.True(vm.IsComplete);

            // Increment beyond max should remain capped
            vm.Increment();
            Assert.Equal(2, vm.CurrentCount);

            vm.ToggleComplete();
            Assert.Equal(0, vm.CurrentCount);
            Assert.Equal("0/2", vm.ProgressText);
            Assert.False(vm.IsComplete);
        }

        #endregion

        #region 4. Consolidation Logic Verification

        [Fact]
        public void QuestConsolidation_DuplicateRequirements_SummedIntoSingleCard()
        {
            // Simulate tasks where the same quest requires the same item multiple times
            // e.g. 2 x Salewa + 1 x Salewa in different objectives of "Shortage"
            var task = new TarkovTask
            {
                Ids = new List<string> { "test_consolidation_task" },
                NormalizedName = "test_consolidation_task",
                Name = "Test Consolidation Task",
                Trader = "Therapist",
                RequiredItems = new List<QuestItem>
                {
                    new QuestItem { ItemNormalizedName = "salewa", Amount = 2, FoundInRaid = true },
                    new QuestItem { ItemNormalizedName = "salewa", Amount = 1, FoundInRaid = false }
                }
            };

            // Mimic the grouping algorithm implemented in ItemsDataService.GetQuestSources
            var matchingItems = task.RequiredItems
                .Where(qi => string.Equals(qi.ItemNormalizedName, "salewa", StringComparison.OrdinalIgnoreCase))
                .GroupBy(qi => qi.DogtagMinLevel ?? 0);

            var cards = new List<QuestItemSourceViewModel>();
            foreach (var group in matchingItems)
            {
                var totalAmount = group.Sum(qi => qi.Amount);
                var anyFir = group.Any(qi => qi.FoundInRaid);
                int? dogtagLevel = group.Key > 0 ? group.Key : null;
                var needKey = NeededItemProgressService.GetQuestItemKey(task.NormalizedName, "salewa", dogtagLevel);

                cards.Add(new QuestItemSourceViewModel
                {
                    QuestName = task.Name,
                    TraderName = task.Trader,
                    Amount = totalAmount,
                    FoundInRaid = anyFir,
                    DogtagMinLevel = dogtagLevel,
                    NeedKey = needKey,
                    CurrentCount = _progressService.GetCount(needKey)
                });
            }

            // Must produce exactly 1 consolidated card, not 2
            Assert.Single(cards);
            var card = cards[0];
            Assert.Equal(3, card.Amount); // 2 + 1 = 3
            Assert.True(card.FoundInRaid); // true || false = true
            Assert.Equal("quest:test_consolidation_task:salewa", card.NeedKey);
            Assert.Equal("0/3", card.ProgressText);
        }

        [Fact]
        public void HideoutConsolidation_DuplicateRequirements_SummedIntoSingleCard()
        {
            // Simulate a hideout station module where multiple items of same type are defined for a level
            var module = new HideoutModule
            {
                Id = "medstation",
                Name = "Medstation",
                NormalizedName = "medstation",
                Levels = new List<HideoutLevel>
                {
                    new HideoutLevel
                    {
                        Level = 2,
                        ItemRequirements = new List<HideoutItemRequirement>
                        {
                            new HideoutItemRequirement { ItemNormalizedName = "salewa", Count = 2, FoundInRaid = false },
                            new HideoutItemRequirement { ItemNormalizedName = "salewa", Count = 1, FoundInRaid = true }
                        }
                    }
                }
            };

            var lvl = module.Levels[0];
            // Mimic grouping in ItemsDataService.GetHideoutSources
            var matchingReqs = lvl.ItemRequirements
                .Where(ir => string.Equals(ir.ItemNormalizedName, "salewa", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var cards = new List<HideoutItemSourceViewModel>();
            if (matchingReqs.Count > 0)
            {
                var totalAmount = matchingReqs.Sum(ir => ir.Count);
                var anyFir = matchingReqs.Any(ir => ir.FoundInRaid);
                var needKey = NeededItemProgressService.GetHideoutItemKey(module.NormalizedName, lvl.Level, "salewa");

                cards.Add(new HideoutItemSourceViewModel
                {
                    ModuleName = module.Name,
                    StationId = module.NormalizedName,
                    Level = lvl.Level,
                    ItemNormalizedName = "salewa",
                    NeedKey = needKey,
                    Amount = totalAmount,
                    FoundInRaid = anyFir,
                    CurrentCount = _progressService.GetCount(needKey)
                });
            }

            Assert.Single(cards);
            var card = cards[0];
            Assert.Equal(3, card.Amount);
            Assert.True(card.FoundInRaid);
            Assert.Equal("hideout:medstation:2:salewa", card.NeedKey);
            Assert.Equal("0/3", card.ProgressText);
        }

        #endregion

        #region 5. Persistence & Profile Isolation

        [Fact]
        public async Task Persistence_SaveAndLoadNeededItemProgress_PreservesCountsPerProfile()
        {
            var pvpKey = "quest:test_persist:item_pvp";
            var pveKey = "quest:test_persist:item_pve";
            _testKeysToClean.Add(pvpKey);
            _testKeysToClean.Add(pveKey);

            // Save PVP count
            await _dbService.SaveNeededItemProgressAsync(pvpKey, 4, ProfileType.Pvp);
            // Save PVE count for same key with different value
            await _dbService.SaveNeededItemProgressAsync(pvpKey, 1, ProfileType.Pve);

            // Load PVP
            var pvpCounts = await _dbService.LoadNeededItemProgressAsync(ProfileType.Pvp);
            Assert.True(pvpCounts.TryGetValue(pvpKey, out var pvpVal));
            Assert.Equal(4, pvpVal);

            // Load PVE
            var pveCounts = await _dbService.LoadNeededItemProgressAsync(ProfileType.Pve);
            Assert.True(pveCounts.TryGetValue(pvpKey, out var pveVal));
            Assert.Equal(1, pveVal);

            // Cleanup
            await _dbService.DeleteNeededItemProgressAsync(pvpKey, ProfileType.Pvp);
            await _dbService.DeleteNeededItemProgressAsync(pvpKey, ProfileType.Pve);

            var afterDeletePvp = await _dbService.LoadNeededItemProgressAsync(ProfileType.Pvp);
            Assert.False(afterDeletePvp.ContainsKey(pvpKey));
        }

        #endregion

        #region 6. Architecture & Strict Multiplayer Exclusion (R3)

        [Fact]
        public void Architecture_StrictlyExcludesMultiplayerTeamTracking()
        {
            // Verify NeededItemProgressService contains no squad/team/multiplayer fields or methods
            var serviceType = typeof(NeededItemProgressService);
            var memberNames = serviceType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => m.Name.ToLowerInvariant())
                .ToList();

            Assert.DoesNotContain(memberNames, name => name.Contains("squad"));
            Assert.DoesNotContain(memberNames, name => name.Contains("team"));
            Assert.DoesNotContain(memberNames, name => name.Contains("multiplayer"));
            Assert.DoesNotContain(memberNames, name => name.Contains("peer"));
            Assert.DoesNotContain(memberNames, name => name.Contains("remoteplayer"));

            // Verify UserDataDbService contains no multiplayer needed item progress tables
            var dbType = typeof(UserDataDbService);
            var dbMemberNames = dbType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => m.Name.ToLowerInvariant())
                .ToList();

            Assert.DoesNotContain(dbMemberNames, name => name.Contains("squadneeded"));
            Assert.DoesNotContain(dbMemberNames, name => name.Contains("teamneeded"));
        }

        #endregion

        #region 7. Adversarial Edge Case & Robustness Tests

        [Fact]
        public async Task FlushPendingSaves_FlushesDebouncedCountsToSqliteBeforeTimerExpires()
        {
            var key = "quest:adversarial:debounce_flush";
            _testKeysToClean.Add(key);

            // Set count, which enters _pendingSaves with 400ms debounce
            _progressService.SetCount(key, 3, 5);
            Assert.Equal(3, _progressService.GetCount(key));

            // Immediately flush synchronously (without waiting 400ms)
            _progressService.FlushPendingSaves();

            // Verify SQLite DB has the count persisted
            var pvpCounts = await _dbService.LoadNeededItemProgressAsync(ProfileType.Pvp);
            Assert.True(pvpCounts.TryGetValue(key, out var storedCount));
            Assert.Equal(3, storedCount);

            // Now decrement to 0 and flush
            _progressService.SetCount(key, 0, 5);
            _progressService.FlushPendingSaves();

            // Should be removed from DB
            var afterZeroCounts = await _dbService.LoadNeededItemProgressAsync(ProfileType.Pvp);
            Assert.False(afterZeroCounts.ContainsKey(key));
        }

        [Fact]
        public async Task ProfileChange_FlushesPendingSavesForPreviousProfile()
        {
            var pvpKey = "quest:adversarial:profile_switch_pvp";
            _testKeysToClean.Add(pvpKey);

            // Set count on PVP
            ProfileService.Instance.CurrentProfile = ProfileType.Pvp;
            await _progressService.LoadProgressAsync(ProfileType.Pvp);
            _progressService.SetCount(pvpKey, 2, 5);

            // Immediately switch profile to PVE without waiting for 400ms debounce
            ProfileService.Instance.CurrentProfile = ProfileType.Pve;

            // Wait a brief moment for async load to complete
            await Task.Delay(50);

            // Verify PVP count was NOT lost and is saved in DB
            var pvpCounts = await _dbService.LoadNeededItemProgressAsync(ProfileType.Pvp);
            Assert.True(pvpCounts.TryGetValue(pvpKey, out var pvpStored));
            Assert.Equal(2, pvpStored);

            // Reset back to PVP for subsequent tests
            ProfileService.Instance.CurrentProfile = ProfileType.Pvp;
            await _progressService.LoadProgressAsync(ProfileType.Pvp);
        }

        [Fact]
        public void QuestConsolidation_DogtagLevelZero_ConsolidatesWithNullLevel()
        {
            var task = new TarkovTask
            {
                Ids = new List<string> { "dogtag_task_0" },
                NormalizedName = "dogtag_task_0",
                Name = "Dogtag Task 0",
                Trader = "Prapor",
                RequiredItems = new List<QuestItem>
                {
                    new QuestItem { ItemNormalizedName = "bear_dogtag", Amount = 2, DogtagMinLevel = null },
                    new QuestItem { ItemNormalizedName = "bear_dogtag", Amount = 3, DogtagMinLevel = 0 }
                }
            };

            var matchingItems = task.RequiredItems
                .Where(qi => string.Equals(qi.ItemNormalizedName, "bear_dogtag", StringComparison.OrdinalIgnoreCase));

            var grouped = new Dictionary<string, QuestItemSourceViewModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var qi in matchingItems)
            {
                var taskNormName = !string.IsNullOrWhiteSpace(task.NormalizedName) ? task.NormalizedName : task.Name;
                var hasMinLevel = qi.DogtagMinLevel.HasValue && qi.DogtagMinLevel.Value > 0;
                var groupKey = hasMinLevel ? $"{taskNormName}:dogtag:{qi.DogtagMinLevel!.Value}" : taskNormName;

                if (grouped.TryGetValue(groupKey, out var existing))
                {
                    existing.Amount += qi.Amount;
                }
                else
                {
                    grouped[groupKey] = new QuestItemSourceViewModel
                    {
                        QuestName = task.Name,
                        Amount = qi.Amount,
                        NeedKey = NeededItemProgressService.GetQuestItemKey(taskNormName, "bear_dogtag", hasMinLevel ? qi.DogtagMinLevel : null)
                    };
                }
            }

            // Both null and 0 dogtag min level should merge into 1 card
            Assert.Single(grouped);
            var card = grouped.Values.First();
            Assert.Equal(5, card.Amount); // 2 + 3 = 5
            Assert.Equal("quest:dogtag_task_0:bear_dogtag", card.NeedKey);
        }

        [Fact]
        public void QuestConsolidation_NullNormalizedName_FallsBackToIdOrNameWithoutColliding()
        {
            var taskA = new TarkovTask
            {
                Ids = new List<string> { "wiki_task_a" },
                NormalizedName = null,
                Name = "Wiki Quest Alpha",
                Trader = "Mechanic",
                RequiredItems = new List<QuestItem>
                {
                    new QuestItem { ItemNormalizedName = "salewa", Amount = 1 }
                }
            };

            var taskB = new TarkovTask
            {
                Ids = new List<string> { "wiki_task_b" },
                NormalizedName = "",
                Name = "Wiki Quest Beta",
                Trader = "Mechanic",
                RequiredItems = new List<QuestItem>
                {
                    new QuestItem { ItemNormalizedName = "salewa", Amount = 2 }
                }
            };

            var tasks = new[] { taskA, taskB };
            var grouped = new Dictionary<string, QuestItemSourceViewModel>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in tasks)
            {
                foreach (var qi in task.RequiredItems!)
                {
                    var taskNormName = !string.IsNullOrWhiteSpace(task.NormalizedName)
                        ? task.NormalizedName
                        : (!string.IsNullOrWhiteSpace(task.Id) ? task.Id : task.Name);
                    var hasMinLevel = qi.DogtagMinLevel.HasValue && qi.DogtagMinLevel.Value > 0;
                    var groupKey = hasMinLevel ? $"{taskNormName}:dogtag:{qi.DogtagMinLevel!.Value}" : taskNormName;

                    if (grouped.TryGetValue(groupKey, out var existing))
                    {
                        existing.Amount += qi.Amount;
                    }
                    else
                    {
                        grouped[groupKey] = new QuestItemSourceViewModel
                        {
                            QuestName = task.Name,
                            Amount = qi.Amount,
                            NeedKey = NeededItemProgressService.GetQuestItemKey(taskNormName, "salewa")
                        };
                    }
                }
            }

            // Must produce 2 separate cards, NOT merge into a single empty-key card
            Assert.Equal(2, grouped.Count);
            Assert.True(grouped.ContainsKey("wiki_task_a"));
            Assert.True(grouped.ContainsKey("wiki_task_b"));
            Assert.Equal(1, grouped["wiki_task_a"].Amount);
            Assert.Equal(2, grouped["wiki_task_b"].Amount);
        }

        [Fact]
        public async Task BatchSave_SavesAndDeletesInSingleTransaction()
        {
            var key1 = "quest:batch:test1";
            var key2 = "quest:batch:test2";
            var key3 = "quest:batch:test3";
            _testKeysToClean.Add(key1);
            _testKeysToClean.Add(key2);
            _testKeysToClean.Add(key3);

            // Pre-seed key3 in DB
            await _dbService.SaveNeededItemProgressAsync(key3, 10, ProfileType.Pvp);

            // Execute batch: insert key1 (3), insert key2 (5), delete key3 (0)
            var batch = new List<(string Id, int Count)>
            {
                (key1, 3),
                (key2, 5),
                (key3, 0)
            };

            await _dbService.SaveNeededItemProgressBatchAsync(batch, ProfileType.Pvp);

            var counts = await _dbService.LoadNeededItemProgressAsync(ProfileType.Pvp);
            Assert.Equal(3, counts[key1]);
            Assert.Equal(5, counts[key2]);
            Assert.False(counts.ContainsKey(key3));
        }

        [Fact]
        public void Stepper_BoundaryAndIllegalValues_HandleGracefully()
        {
            var key = "quest:boundary:test";
            _testKeysToClean.Add(key);

            // Max count 0: should remain 0
            _progressService.SetCount(key, 5, 0);
            Assert.Equal(0, _progressService.GetCount(key));
            Assert.True(_progressService.IsComplete(key, 0));

            // Increment with 0 max
            var c1 = _progressService.Increment(key, 0);
            Assert.Equal(0, c1);

            // Decrement below 0
            var c2 = _progressService.Decrement(key, 0);
            Assert.Equal(0, c2);

            // Toggle complete with 0 max
            var t1 = _progressService.ToggleComplete(key, 0);
            Assert.Equal(0, t1);

            // Negative count passed to SetCount: clamped to 0
            _progressService.SetCount(key, -10, 5);
            Assert.Equal(0, _progressService.GetCount(key));

            // Count exceeding max: clamped to max
            _progressService.SetCount(key, 100, 5);
            Assert.Equal(5, _progressService.GetCount(key));
        }

        [Fact]
        public void FlushPendingSaves_OnSynchronizationContext_DoesNotDeadlock()
        {
            var key = "quest:adversarial:sync_ctx_deadlock";
            _testKeysToClean.Add(key);
            _progressService.SetCount(key, 2, 5);

            var finished = false;
            var testThread = new System.Threading.Thread(() =>
            {
                var prevCtx = System.Threading.SynchronizationContext.Current;
                try
                {
                    var blockingContext = new SingleThreadTestSyncContext();
                    System.Threading.SynchronizationContext.SetSynchronizationContext(blockingContext);

                    _progressService.FlushPendingSaves();
                    finished = true;
                }
                finally
                {
                    System.Threading.SynchronizationContext.SetSynchronizationContext(prevCtx);
                }
            });

            testThread.Start();
            bool joined = testThread.Join(5000);
            Assert.True(joined, "FlushPendingSaves deadlocked on thread with SynchronizationContext!");
            Assert.True(finished);
        }

        [Fact]
        public async Task LoadProgressAsync_StaleProfileCompletion_DoesNotOverwriteCurrentProfile()
        {
            var pvpKey = "quest:stale:pvp";
            var pveKey = "quest:stale:pve";
            _testKeysToClean.Add(pvpKey);
            _testKeysToClean.Add(pveKey);

            await _dbService.SaveNeededItemProgressAsync(pvpKey, 5, ProfileType.Pvp);
            await _dbService.SaveNeededItemProgressAsync(pveKey, 2, ProfileType.Pve);

            // Switch to PVE
            ProfileService.Instance.CurrentProfile = ProfileType.Pve;
            await _progressService.LoadProgressAsync(ProfileType.Pve);
            Assert.Equal(2, _progressService.GetCount(pveKey));

            // Now initiate switch back to PVP
            ProfileService.Instance.CurrentProfile = ProfileType.Pvp;
            // Simulate slow PVE load arriving AFTER switch to PVP
            await _progressService.LoadProgressAsync(ProfileType.Pve);

            // Active counts must NOT have been overwritten with PVE data
            Assert.Equal(0, _progressService.GetCount(pveKey));

            // Load PVP properly
            await _progressService.LoadProgressAsync(ProfileType.Pvp);
            Assert.Equal(5, _progressService.GetCount(pvpKey));
        }

        [Fact]
        public async Task LoadProgressAsync_InFlightUserModifications_PreservedAgainstStaleDbValues()
        {
            var key = "quest:inflight:edit";
            _testKeysToClean.Add(key);

            // Database has count 1
            await _dbService.SaveNeededItemProgressAsync(key, 1, ProfileType.Pvp);

            // User sets count to 4 in memory before LoadProgressAsync merges
            _progressService.SetCount(key, 4, 10);

            // Call LoadProgressAsync (simulating load completing while edit is in flight)
            await _progressService.LoadProgressAsync(ProfileType.Pvp);

            // The in-flight edit (4) must be preserved, not overwritten by DB's 1
            Assert.Equal(4, _progressService.GetCount(key));
        }

        [Fact]
        public async Task BatchSave_EmptyAndNullCollections_HandledGracefully()
        {
            // Null collection should not throw
            await _dbService.SaveNeededItemProgressBatchAsync(null!, ProfileType.Pvp);

            // Empty collection should not throw
            await _dbService.SaveNeededItemProgressBatchAsync(Enumerable.Empty<(string, int)>(), ProfileType.Pvp);
        }

        [Fact]
        public void Localization_JapaneseNames_UsedWhenLanguageIsJA()
        {
            var origLang = LocalizationService.Instance.CurrentLanguage;
            try
            {
                LocalizationService.Instance.CurrentLanguage = AppLanguage.JA;

                var task = new TarkovTask
                {
                    Ids = new List<string> { "ja_task" },
                    NormalizedName = "ja_task",
                    Name = "Delivery from the Past",
                    NameKo = "과거로부터의 배달",
                    NameJa = "過去からの配達",
                    Trader = "Prapor",
                    RequiredItems = new List<QuestItem>
                    {
                        new QuestItem { ItemNormalizedName = "customs_doc", Amount = 1 }
                    }
                };

                var lang = LocalizationService.Instance.CurrentLanguage;
                var questName = (lang == AppLanguage.KO && !string.IsNullOrEmpty(task.NameKo))
                    ? task.NameKo
                    : (lang == AppLanguage.JA && !string.IsNullOrEmpty(task.NameJa)
                        ? task.NameJa
                        : task.Name);

                Assert.Equal("過去からの配達", questName);
            }
            finally
            {
                LocalizationService.Instance.CurrentLanguage = origLang;
            }
        }

        [Fact]
        public async Task DirectSources_RealProgressServiceInvocation_ConsolidatesAndClamps()
        {
            var itemNorm = "direct_test_salewa";
            var key = NeededItemProgressService.GetQuestItemKey("direct_task", itemNorm);
            _testKeysToClean.Add(key);

            // Set DB progress to 10 (which is > required Amount of 3)
            await _dbService.SaveNeededItemProgressAsync(key, 10, ProfileType.Pvp);
            await _progressService.LoadProgressAsync(ProfileType.Pvp);

            var task = new TarkovTask
            {
                Ids = new List<string> { "direct_task" },
                NormalizedName = "direct_task",
                Name = "Direct Consolidation Quest",
                Trader = "Therapist",
                RequiredItems = new List<QuestItem>
                {
                    new QuestItem { ItemNormalizedName = itemNorm, Amount = 1, FoundInRaid = true },
                    new QuestItem { ItemNormalizedName = itemNorm, Amount = 2, FoundInRaid = false }
                }
            };

            // Initialize QuestProgressService with this task
            await QuestProgressService.Instance.InitializeAsync(new List<TarkovTask> { task }, ProfileType.Pvp);

            // Call real ItemsDataService.Instance.GetQuestSources
            var sources = ItemsDataService.Instance.GetQuestSources(itemNorm);

            Assert.Single(sources);
            var vm = sources[0];
            Assert.Equal(3, vm.Amount); // Consolidated 1 + 2
            Assert.True(vm.FoundInRaid); // Consolidated FIR
            Assert.Equal(3, vm.CurrentCount); // Clamped at Amount (3), not 10!
            Assert.Equal("3/3", vm.ProgressText);
            Assert.True(vm.IsComplete);
        }

        [Fact]
        public async Task InFlightSaveTask_AwaitedOnFlushPendingSaves_PreventsDataLoss()
        {
            var key = "quest:adversarial:inflight_flush";
            _testKeysToClean.Add(key);

            // Set count in service
            _progressService.SetCount(key, 4, 10);

            // Trigger SavePendingItems via reflection so _activeSaveTask is in flight and _pendingSaves is drained
            var method = typeof(NeededItemProgressService).GetMethod("SavePendingItems", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method!.Invoke(_progressService, null);

            // Now immediately flush synchronously while _pendingSaves is 0 but _activeSaveTask is in flight
            _progressService.FlushPendingSaves();

            // Verify count is written and persisted in DB
            var counts = await _dbService.LoadNeededItemProgressAsync(ProfileType.Pvp);
            Assert.True(counts.TryGetValue(key, out var storedCount));
            Assert.Equal(4, storedCount);
        }

        [Fact]
        public async Task ClearAllProgressAsync_AwaitsInFlightSave_PreventsResurrection()
        {
            var key = "quest:adversarial:clear_resurrect";
            _testKeysToClean.Add(key);

            _progressService.SetCount(key, 3, 5);

            // Trigger background save
            var method = typeof(NeededItemProgressService).GetMethod("SavePendingItems", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method!.Invoke(_progressService, null);

            // Immediately clear all progress
            await _progressService.ClearAllProgressAsync(ProfileType.Pvp);

            // Assert DB is truly empty for this profile, not resurrected by the background save
            var counts = await _dbService.LoadNeededItemProgressAsync(ProfileType.Pvp);
            Assert.False(counts.ContainsKey(key));
        }

        [Fact]
        public async Task GetHideoutSources_NullAndIncompleteData_HandlesGracefullyWithoutThrowing()
        {
            // Null string should return empty list
            var emptyResult = ItemsDataService.Instance.GetHideoutSources(null!);
            Assert.Empty(emptyResult);

            var whitespaceResult = ItemsDataService.Instance.GetHideoutSources("   ");
            Assert.Empty(whitespaceResult);

            // Test with a module having null levels and a level with null item requirements
            var brokenModule = new HideoutModule
            {
                Id = "broken_module",
                Name = "Broken Module",
                Levels = null!
            };

            var brokenModule2 = new HideoutModule
            {
                Id = "broken_module_2",
                Name = "Broken Module 2",
                Levels = new List<HideoutLevel>
                {
                    null!,
                    new HideoutLevel
                    {
                        Level = 2,
                        ItemRequirements = null!
                    }
                }
            };

            await HideoutProgressService.Instance.InitializeAsync(new List<HideoutModule> { brokenModule, brokenModule2 });

            var result = ItemsDataService.Instance.GetHideoutSources("some_item");
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetHideoutSources_RealHideoutProgressServiceInvocation_ConsolidatesAndClamps()
        {
            var itemNorm = "hideout_test_screws";
            var key = NeededItemProgressService.GetHideoutItemKey("workbench_test", 2, itemNorm);
            _testKeysToClean.Add(key);

            // Pre-seed DB count with 99 (exceeding requirement of 5)
            await _dbService.SaveNeededItemProgressAsync(key, 99, ProfileType.Pvp);
            await _progressService.LoadProgressAsync(ProfileType.Pvp);

            var module = new HideoutModule
            {
                Id = "workbench_test",
                Name = "Workbench",
                NormalizedName = "workbench_test",
                Levels = new List<HideoutLevel>
                {
                    new HideoutLevel
                    {
                        Level = 2,
                        ItemRequirements = new List<HideoutItemRequirement>
                        {
                            new HideoutItemRequirement { ItemNormalizedName = itemNorm, Count = 2, FoundInRaid = false },
                            new HideoutItemRequirement { ItemNormalizedName = itemNorm, Count = 3, FoundInRaid = true }
                        }
                    }
                }
            };

            await HideoutProgressService.Instance.InitializeAsync(new List<HideoutModule> { module });

            var sources = ItemsDataService.Instance.GetHideoutSources(itemNorm);
            Assert.Single(sources);
            var card = sources[0];
            Assert.Equal("Workbench", card.ModuleName);
            Assert.Equal(2, card.Level);
            Assert.Equal(5, card.Amount); // Consolidated 2 + 3
            Assert.True(card.FoundInRaid); // true || false = true
            Assert.Equal(5, card.CurrentCount); // Clamped at 5, not 99
            Assert.Equal("5/5", card.ProgressText);
            Assert.True(card.IsComplete);
        }

        [Fact]
        public async Task GetQuestSources_NullAndIncompleteData_HandlesGracefullyWithoutThrowing()
        {
            var emptyResult = ItemsDataService.Instance.GetQuestSources(null!);
            Assert.Empty(emptyResult);

            var whitespaceResult = ItemsDataService.Instance.GetQuestSources("   ");
            Assert.Empty(whitespaceResult);

            var brokenTask = new TarkovTask
            {
                Ids = new List<string> { "broken_task" },
                Name = null!,
                Trader = null!,
                RequiredItems = new List<QuestItem>
                {
                    null!,
                    new QuestItem { ItemNormalizedName = null!, Amount = -5 }
                }
            };

            await QuestProgressService.Instance.InitializeAsync(new List<TarkovTask> { brokenTask }, ProfileType.Pvp);

            var result = ItemsDataService.Instance.GetQuestSources("any_item");
            Assert.Empty(result);
        }

        #endregion
    }

    internal class SingleThreadTestSyncContext : System.Threading.SynchronizationContext
    {
        public override void Post(System.Threading.SendOrPostCallback d, object? state)
        {
        }

        public override void Send(System.Threading.SendOrPostCallback d, object? state)
        {
            d(state);
        }
    }
}
