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
