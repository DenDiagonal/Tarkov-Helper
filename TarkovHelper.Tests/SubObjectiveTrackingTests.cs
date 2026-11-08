using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Pages;
using TarkovHelper.Pages.Map.Components;
using TarkovHelper.Services;
using TarkovHelper.Services.Map;
using Xunit;

namespace TarkovHelper.Tests
{
    [Collection("DatabaseUpdateServiceCollection")]
    public class SubObjectiveTrackingTests : IDisposable
    {
        private readonly UserDataDbService _userDataDb = UserDataDbService.Instance;
        private readonly ObjectiveProgressService _progressService = ObjectiveProgressService.Instance;
        private readonly List<string> _testQuestsToClean = new();
        private readonly List<string> _testKeysToClean = new();

        public SubObjectiveTrackingTests()
        {
            _progressService.ClearInMemoryForTesting();
        }

        public void Dispose()
        {
            _progressService.ClearInMemoryForTesting();
            QuestObjectiveDbService.Instance.ClearObjectivesForTesting();

            foreach (var q in _testQuestsToClean)
            {
                try
                {
                    _userDataDb.DeleteObjectiveProgressByQuestAsync(q).GetAwaiter().GetResult();
                }
                catch { }
            }

            foreach (var k in _testKeysToClean)
            {
                try
                {
                    _userDataDb.DeleteObjectiveProgressAsync(k).GetAwaiter().GetResult();
                }
                catch { }
            }
        }

        [Fact]
        public async Task SetObjectiveCompleted_WithExplicitId_PersistsDualKeysInDb()
        {
            var questNormName = "test-quest-dual-explicit";
            var objId = "test-obj-id-101";
            _testQuestsToClean.Add(questNormName);
            _testKeysToClean.Add($"id:{objId}");
            _testKeysToClean.Add($"{questNormName}:0");

            // 1. Mark objective 0 as completed with explicit objectiveId
            await _progressService.SetObjectiveCompletedAsync(questNormName, 0, true, objId);

            // 2. Verify in-memory state for both keys
            Assert.True(_progressService.IsObjectiveCompleted(questNormName, 0), "Index-based lookup must be completed");
            Assert.True(_progressService.IsObjectiveCompletedById(objId), "Id-based lookup must be completed");

            // 3. Verify SQLite persistence in ObjectiveProgress table
            var dbEntries = await _userDataDb.LoadObjectiveProgressAsync();
            Assert.True(dbEntries.TryGetValue($"{questNormName}:0", out var idxVal) && idxVal,
                "SQLite DB must contain index-based key");
            Assert.True(dbEntries.TryGetValue($"id:{objId}", out var idVal) && idVal,
                "SQLite DB must contain id-based key");

            // 4. Mark objective 0 as incomplete
            await _progressService.SetObjectiveCompletedAsync(questNormName, 0, false, objId);

            Assert.False(_progressService.IsObjectiveCompleted(questNormName, 0), "Index-based lookup must be false after uncheck");
            Assert.False(_progressService.IsObjectiveCompletedById(objId), "Id-based lookup must be false after uncheck");

            var dbEntriesAfterUncheck = await _userDataDb.LoadObjectiveProgressAsync();
            Assert.False(dbEntriesAfterUncheck.ContainsKey($"{questNormName}:0") && dbEntriesAfterUncheck[$"{questNormName}:0"],
                "Index key must be removed from SQLite DB");
            Assert.False(dbEntriesAfterUncheck.ContainsKey($"id:{objId}") && dbEntriesAfterUncheck[$"id:{objId}"],
                "Id key must be removed from SQLite DB");
        }

        [Fact]
        public async Task SetObjectiveCompleted_WithoutPassingId_ResolvesAndPersistsDualKeys()
        {
            var questNormName = "test-quest-auto-resolve";
            var objId = "test-obj-auto-999";
            _testQuestsToClean.Add(questNormName);
            _testKeysToClean.Add($"id:{objId}");
            _testKeysToClean.Add($"{questNormName}:0");

            var testObjectives = new List<QuestObjective>
            {
                new QuestObjective
                {
                    Id = objId,
                    QuestId = questNormName,
                    QuestName = "Test Auto Resolve Quest",
                    Description = "Find the missing communication device",
                    LocationPointsJson = "[{\"x\":120,\"y\":15,\"z\":250}]"
                }
            };
            QuestObjectiveDbService.Instance.SetObjectivesForTesting(testObjectives);

            // Set completed WITHOUT passing objectiveId (simulates UI checkbox toggle)
            await _progressService.SetObjectiveCompletedAsync(questNormName, 0, true, null);

            // Assert both lookups return true
            Assert.True(_progressService.IsObjectiveCompleted(questNormName, 0), "Index key should be completed");
            Assert.True(_progressService.IsObjectiveCompletedById(objId), "Auto-resolved ID key should be completed");

            // Assert both were saved to SQLite
            var dbEntries = await _userDataDb.LoadObjectiveProgressAsync();
            Assert.True(dbEntries.ContainsKey($"{questNormName}:0"), "Index key must be in DB");
            Assert.True(dbEntries.ContainsKey($"id:{objId}"), "Auto-resolved ID key must be in DB");
        }

        [Fact]
        public async Task SetObjectiveCompletedById_ResolvesParentQuestAndIndex_PersistsDualKeys()
        {
            var questNormName = "test-quest-reverse-sync";
            var objId = "test-obj-rev-888";
            _testQuestsToClean.Add(questNormName);
            _testKeysToClean.Add($"id:{objId}");
            _testKeysToClean.Add($"{questNormName}:0");

            var testObjectives = new List<QuestObjective>
            {
                new QuestObjective
                {
                    Id = objId,
                    QuestId = questNormName,
                    QuestName = "Test Quest Reverse Sync",
                    Description = "Secure the military ledger",
                    LocationPointsJson = "[{\"x\":400,\"y\":5,\"z\":150}]"
                }
            };
            QuestObjectiveDbService.Instance.SetObjectivesForTesting(testObjectives);

            // Set completed by ID alone (simulates map click without quest name/index)
            await _progressService.SetObjectiveCompletedByIdAsync(objId, true);

            // Verify both ID and resolved index report completed
            Assert.True(_progressService.IsObjectiveCompletedById(objId), "ID lookup must be completed");
            Assert.True(_progressService.IsObjectiveCompleted(questNormName, 0), "Index lookup must be completed via reverse sync");

            // Verify both persisted in SQLite
            var dbEntries = await _userDataDb.LoadObjectiveProgressAsync();
            Assert.True(dbEntries.ContainsKey($"id:{objId}"), "ID key must be in DB");
            Assert.True(dbEntries.ContainsKey($"{questNormName}:0"), "Reverse-synced index key must be in DB");
        }

        [Fact]
        public void BidirectionalFallbackLookup_ResolvesEvenWhenInMemCacheHasOnlyOneKey()
        {
            var questNormName = "test-quest-fallback";
            var objId = "test-obj-fallback-777";

            var testObjectives = new List<QuestObjective>
            {
                new QuestObjective
                {
                    Id = objId,
                    QuestId = questNormName,
                    QuestName = "Test Quest Fallback",
                    Description = "Extract with the tactical drive",
                    LocationPointsJson = "[{\"x\":50,\"y\":10,\"z\":50}]"
                }
            };
            QuestObjectiveDbService.Instance.SetObjectivesForTesting(testObjectives);

            // Scenario 1: Memory cache has ONLY the index key (e.g. legacy data or single-key insert)
            _progressService.ClearInMemoryForTesting();
            _progressService.SetObjectiveCompleted(questNormName, 0, true, "different-dummy-id");
            // Now remove the ID key specifically to leave ONLY index key
            var objDictField = typeof(ObjectiveProgressService).GetField("_objectiveProgress", BindingFlags.NonPublic | BindingFlags.Instance);
            var dict = (Dictionary<string, bool>)objDictField!.GetValue(_progressService)!;
            dict.Remove("id:different-dummy-id");
            dict.Remove($"id:{objId}");
            Assert.True(dict.ContainsKey($"{questNormName}:0"));
            Assert.False(dict.ContainsKey($"id:{objId}"));

            // Calling IsObjectiveCompletedById should fall back to checking parent quest:index
            Assert.True(_progressService.IsObjectiveCompletedById(objId),
                "IsObjectiveCompletedById should resolve parent quest and index via fallback when id key is absent in cache");

            // Scenario 2: Memory cache has ONLY the id key
            dict.Clear();
            dict[$"id:{objId}"] = true;
            Assert.False(dict.ContainsKey($"{questNormName}:0"));

            // Calling IsObjectiveCompleted should fall back to checking id key
            Assert.True(_progressService.IsObjectiveCompleted(questNormName, 0),
                "IsObjectiveCompleted should resolve objective ID via fallback when index key is absent in cache");
        }

        [Fact]
        public void SubObjectiveFiltering_HidesMarkerWhenHideCompletedObjectivesIsTrue()
        {
            var questNormName = "test-quest-map-filtering";
            var completedObjId = "map-obj-completed";
            var pendingObjId = "map-obj-pending";

            var testObjectives = new List<QuestObjective>
            {
                new QuestObjective
                {
                    Id = completedObjId,
                    QuestId = questNormName,
                    QuestName = "Map Filtering Task",
                    Description = "Sub objective 1 (Done)",
                    LocationPointsJson = "[{\"x\":10,\"y\":0,\"z\":20}]"
                },
                new QuestObjective
                {
                    Id = pendingObjId,
                    QuestId = questNormName,
                    QuestName = "Map Filtering Task",
                    Description = "Sub objective 2 (Pending)",
                    LocationPointsJson = "[{\"x\":30,\"y\":0,\"z\":40}]"
                }
            };
            QuestObjectiveDbService.Instance.SetObjectivesForTesting(testObjectives);

            // Mark completedObjId as completed
            _progressService.SetObjectiveCompletedById(completedObjId, true, questNormName, 0);
            _progressService.SetObjectiveCompletedById(pendingObjId, false, questNormName, 1);

            // Simulation of MapQuestMarkerManager and OverlayMiniMapWindow filtering loop:
            var objectives = new List<(string Id, string Desc)>
            {
                (completedObjId, "Sub objective 1 (Done)"),
                (pendingObjId, "Sub objective 2 (Pending)")
            };

            // Case A: hideCompletedObjectives = true
            bool hideCompletedObjectives = true;
            var visibleWhenHidden = new List<string>();
            foreach (var obj in objectives)
            {
                var isCompleted = _progressService.IsObjectiveCompletedById(obj.Id);
                if (hideCompletedObjectives && isCompleted)
                    continue;
                visibleWhenHidden.Add(obj.Id);
            }

            Assert.Single(visibleWhenHidden);
            Assert.Contains(pendingObjId, visibleWhenHidden);
            Assert.DoesNotContain(completedObjId, visibleWhenHidden);

            // Case B: hideCompletedObjectives = false
            hideCompletedObjectives = false;
            var visibleWhenNotHidden = new List<string>();
            foreach (var obj in objectives)
            {
                var isCompleted = _progressService.IsObjectiveCompletedById(obj.Id);
                if (hideCompletedObjectives && isCompleted)
                    continue;
                visibleWhenNotHidden.Add(obj.Id);
            }

            Assert.Equal(2, visibleWhenNotHidden.Count);
            Assert.Contains(completedObjId, visibleWhenNotHidden);
            Assert.Contains(pendingObjId, visibleWhenNotHidden);
        }

        private static void RunInSta(Action action, int timeoutSeconds = 10)
        {
            Exception? caught = null;
            var thread = new System.Threading.Thread(() =>
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
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
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

        private static async Task RunInStaAsync(Func<Task> action, int timeoutSeconds = 10)
        {
            var tcs = new TaskCompletionSource<bool>();
            var thread = new System.Threading.Thread(async () =>
            {
                try
                {
                    await action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
            if (completedTask != tcs.Task)
            {
                throw new TimeoutException($"STA test action timed out after {timeoutSeconds}s");
            }
            await tcs.Task;
        }

        [Fact]
        public void QuestListPage_UpdateObjectiveTextStyle_UpdatesGridAndStackPanelVisuals()
        {
            RunInSta(() =>
            {
                // Container 1: Grid container with CheckBox and TextBlock (standard objective)
                var grid = new Grid();
                var chk = new CheckBox();
                var tb = new TextBlock { Text = "Regular Objective" };
                grid.Children.Add(chk);
                grid.Children.Add(tb);

                // Apply completion (strikethrough + opacity 0.6)
                QuestListPage.UpdateObjectiveTextStyle(grid, true);
                Assert.NotNull(tb.TextDecorations);
                Assert.Equal(0.6, tb.Opacity);

                // Remove completion
                QuestListPage.UpdateObjectiveTextStyle(grid, false);
                Assert.Null(tb.TextDecorations);
                Assert.Equal(1.0, tb.Opacity);

                // Container 2: Grid containing an inner StackPanel with Border badge + TextBlock (optional objective)
                var optionalGrid = new Grid();
                var optionalChk = new CheckBox();
                var optionalPanel = new StackPanel();
                var badgeBorder = new Border { Child = new TextBlock { Text = "Optional" } };
                var optionalTb = new TextBlock { Text = "Optional Objective Text" };
                optionalPanel.Children.Add(badgeBorder);
                optionalPanel.Children.Add(optionalTb);
                optionalGrid.Children.Add(optionalChk);
                optionalGrid.Children.Add(optionalPanel);

                // Apply completion
                QuestListPage.UpdateObjectiveTextStyle(optionalGrid, true);
                Assert.NotNull(optionalTb.TextDecorations);
                Assert.Equal(0.6, optionalTb.Opacity);
                Assert.Equal(0.6, badgeBorder.Opacity);

                // Remove completion
                QuestListPage.UpdateObjectiveTextStyle(optionalGrid, false);
                Assert.Null(optionalTb.TextDecorations);
                Assert.Equal(1.0, optionalTb.Opacity);
                Assert.Equal(1.0, badgeBorder.Opacity);
            });
        }

        [Fact]
        public void ClearObjectiveProgress_RemovesBothIndexAndIdKeys()
        {
            var questNormName = "test-quest-clear-both";
            var objId = "test-obj-clear-1";

            var testObjectives = new List<QuestObjective>
            {
                new QuestObjective
                {
                    Id = objId,
                    QuestId = questNormName,
                    QuestName = "Test Quest Clear Both",
                    Description = "Clear test objective",
                    LocationPointsJson = "[{\"x\":1,\"y\":2,\"z\":3}]"
                }
            };
            QuestObjectiveDbService.Instance.SetObjectivesForTesting(testObjectives);

            _progressService.SetObjectiveCompleted(questNormName, 0, true, objId);
            Assert.True(_progressService.IsObjectiveCompleted(questNormName, 0));
            Assert.True(_progressService.IsObjectiveCompletedById(objId));

            _progressService.ClearObjectiveProgress(questNormName);
            Assert.False(_progressService.IsObjectiveCompleted(questNormName, 0));
            Assert.False(_progressService.IsObjectiveCompletedById(objId));
        }

        [Fact]
        public async Task MapQuestMarkerManager_SubObjectiveCompletion_TogglesIndividualMarkersOnCanvas()
        {
            await RunInStaAsync(async () =>
            {
                var questNormName = "test-quest-marker-mgr-empirical";
                var subObj1Id = "empirical-subobj-1";
                var subObj2Id = "empirical-subobj-2";

                _testQuestsToClean.Add(questNormName);
                _testKeysToClean.Add($"id:{subObj1Id}");
                _testKeysToClean.Add($"id:{subObj2Id}");
                _testKeysToClean.Add($"{questNormName}:0");
                _testKeysToClean.Add($"{questNormName}:1");

                // 1. Setup Active task with 2 sub-objectives on Customs
                var task = new TarkovTask
                {
                    Ids = new List<string> { questNormName },
                    NormalizedName = questNormName,
                    Name = "Empirical Multitask Quest",
                    Trader = "Story" // Story trader guarantees QuestStatus.Active
                };
                await QuestProgressService.Instance.InitializeAsync(new List<TarkovTask> { task });

                var testObjectives = new List<QuestObjective>
                {
                    new QuestObjective
                    {
                        Id = subObj1Id,
                        QuestId = questNormName,
                        QuestName = "Empirical Multitask Quest",
                        Description = "Sub Objective 1 - Plant Beacon",
                        MapName = "Customs",
                        LocationPointsJson = "[{\"x\":150,\"y\":10,\"z\":250}]"
                    },
                    new QuestObjective
                    {
                        Id = subObj2Id,
                        QuestId = questNormName,
                        QuestName = "Empirical Multitask Quest",
                        Description = "Sub Objective 2 - Secure Perimeter",
                        MapName = "Customs",
                        LocationPointsJson = "[{\"x\":350,\"y\":10,\"z\":450}]"
                    }
                };
                QuestObjectiveDbService.Instance.SetObjectivesForTesting(testObjectives);

                var markersCanvas = new Canvas();
                var markerManager = new MapQuestMarkerManager(
                    markersCanvas,
                    MapTrackerService.Instance,
                    QuestObjectiveService.Instance,
                    QuestProgressService.Instance,
                    LocalizationService.Instance);

                markerManager.SetCurrentMap("customs");
                markerManager.SetQuestMarkerStyle(QuestMarkerStyle.Default);
                markerManager.SetHideCompletedObjectives(true);

                // Initial load: neither sub-objective is completed -> both markers must be present
                await markerManager.RefreshMarkersAsync();

                var getMarkerObjectiveId = new Func<FrameworkElement, string?>(fe =>
                {
                    if (fe.Tag is TaskObjectiveWithLocation obj) return obj.ObjectiveId;
                    return null;
                });

                var activeIds = markersCanvas.Children.OfType<FrameworkElement>()
                    .Select(getMarkerObjectiveId)
                    .Where(id => id != null)
                    .ToList();

                Assert.Equal(2, activeIds.Count);
                Assert.Contains(subObj1Id, activeIds);
                Assert.Contains(subObj2Id, activeIds);

                // 2. Mark subObj1Id as completed (hideCompletedObjectives = true)
                _progressService.SetObjectiveCompletedById(subObj1Id, true, questNormName, 0);
                await markerManager.RefreshMarkersAsync();

                var activeIdsAfterDone1 = markersCanvas.Children.OfType<FrameworkElement>()
                    .Select(getMarkerObjectiveId)
                    .Where(id => id != null)
                    .ToList();

                Assert.Single(activeIdsAfterDone1);
                Assert.DoesNotContain(subObj1Id, activeIdsAfterDone1);
                Assert.Contains(subObj2Id, activeIdsAfterDone1);

                // 3. Uncheck subObj1Id (mark incomplete) -> marker must be restored immediately
                _progressService.SetObjectiveCompletedById(subObj1Id, false, questNormName, 0);
                await markerManager.RefreshMarkersAsync();

                var activeIdsAfterUncheck = markersCanvas.Children.OfType<FrameworkElement>()
                    .Select(getMarkerObjectiveId)
                    .Where(id => id != null)
                    .ToList();

                Assert.Equal(2, activeIdsAfterUncheck.Count);
                Assert.Contains(subObj1Id, activeIdsAfterUncheck);
                Assert.Contains(subObj2Id, activeIdsAfterUncheck);

                // 4. Disable hideCompletedObjectives: mark subObj1Id completed again, both must remain visible
                markerManager.SetHideCompletedObjectives(false);
                _progressService.SetObjectiveCompletedById(subObj1Id, true, questNormName, 0);
                await markerManager.RefreshMarkersAsync();

                var activeIdsWithHideDisabled = markersCanvas.Children.OfType<FrameworkElement>()
                    .Select(getMarkerObjectiveId)
                    .Where(id => id != null)
                    .ToList();

                Assert.Equal(2, activeIdsWithHideDisabled.Count);
                Assert.Contains(subObj1Id, activeIdsWithHideDisabled);
                Assert.Contains(subObj2Id, activeIdsWithHideDisabled);
            });
        }

        [Fact]
        public async Task MapQuestMarkerManager_IdenticalDescriptionSubObjectives_AreIndependentlyTrackedAndFiltered()
        {
            await RunInStaAsync(async () =>
            {
                var questNormName = "test-quest-identical-desc-subobjs";
                var subObjAId = "identical-desc-subobj-A";
                var subObjBId = "identical-desc-subobj-B";

                _testQuestsToClean.Add(questNormName);
                _testKeysToClean.Add($"id:{subObjAId}");
                _testKeysToClean.Add($"id:{subObjBId}");
                _testKeysToClean.Add($"{questNormName}:0");
                _testKeysToClean.Add($"{questNormName}:1");

                var task = new TarkovTask
                {
                    Ids = new List<string> { questNormName },
                    NormalizedName = questNormName,
                    Name = "Identical Descriptions Quest",
                    Trader = "Story"
                };
                await QuestProgressService.Instance.InitializeAsync(new List<TarkovTask> { task });

                // Both sub-objectives have the exact same description
                var testObjectives = new List<QuestObjective>
                {
                    new QuestObjective
                    {
                        Id = subObjAId,
                        QuestId = questNormName,
                        QuestName = "Identical Descriptions Quest",
                        Description = "Plant MS2000 Marker on the fuel tank",
                        MapName = "Customs",
                        LocationPointsJson = "[{\"x\":100,\"y\":10,\"z\":200}]"
                    },
                    new QuestObjective
                    {
                        Id = subObjBId,
                        QuestId = questNormName,
                        QuestName = "Identical Descriptions Quest",
                        Description = "Plant MS2000 Marker on the fuel tank",
                        MapName = "Customs",
                        LocationPointsJson = "[{\"x\":500,\"y\":10,\"z\":600}]"
                    }
                };
                QuestObjectiveDbService.Instance.SetObjectivesForTesting(testObjectives);

                var markersCanvas = new Canvas();
                var markerManager = new MapQuestMarkerManager(
                    markersCanvas,
                    MapTrackerService.Instance,
                    QuestObjectiveService.Instance,
                    QuestProgressService.Instance,
                    LocalizationService.Instance);

                markerManager.SetCurrentMap("customs");
                markerManager.SetQuestMarkerStyle(QuestMarkerStyle.Default);
                markerManager.SetHideCompletedObjectives(true);

                await markerManager.RefreshMarkersAsync();

                var getMarkerObjectiveId = new Func<FrameworkElement, string?>(fe =>
                {
                    if (fe.Tag is TaskObjectiveWithLocation obj) return obj.ObjectiveId;
                    return null;
                });

                var activeIds = markersCanvas.Children.OfType<FrameworkElement>()
                    .Select(getMarkerObjectiveId)
                    .Where(id => id != null)
                    .ToList();

                Assert.Equal(2, activeIds.Count);
                Assert.Contains(subObjAId, activeIds);
                Assert.Contains(subObjBId, activeIds);

                // Mark SubObj A completed
                _progressService.SetObjectiveCompletedById(subObjAId, true, questNormName, 0);
                await markerManager.RefreshMarkersAsync();

                var activeAfterA = markersCanvas.Children.OfType<FrameworkElement>()
                    .Select(getMarkerObjectiveId)
                    .Where(id => id != null)
                    .ToList();

                Assert.Single(activeAfterA);
                Assert.Contains(subObjBId, activeAfterA);
                Assert.DoesNotContain(subObjAId, activeAfterA);

                // Now uncheck SubObj A and mark SubObj B completed
                _progressService.SetObjectiveCompletedById(subObjAId, false, questNormName, 0);
                _progressService.SetObjectiveCompletedById(subObjBId, true, questNormName, 1);
                await markerManager.RefreshMarkersAsync();

                var activeAfterB = markersCanvas.Children.OfType<FrameworkElement>()
                    .Select(getMarkerObjectiveId)
                    .Where(id => id != null)
                    .ToList();

                Assert.Single(activeAfterB);
                Assert.Contains(subObjAId, activeAfterB);
                Assert.DoesNotContain(subObjBId, activeAfterB);
            });
        }
    }
}
