using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TarkovHelper.Models;
using TarkovHelper.Services;
using Xunit;

namespace TarkovHelper.Tests
{
    [Collection("DatabaseUpdateServiceCollection")]
    public class ObjectiveProgressEmpiricalChallengeTests : IDisposable
    {
        private readonly UserDataDbService _userDataDb = UserDataDbService.Instance;
        private readonly ObjectiveProgressService _progressService = ObjectiveProgressService.Instance;
        private readonly ProfileType _originalProfile;
        private readonly List<string> _testQuestsToClean = new();
        private readonly List<string> _testKeysToClean = new();

        public ObjectiveProgressEmpiricalChallengeTests()
        {
            _originalProfile = ProfileService.Instance.CurrentProfile;
            _progressService.ClearInMemoryForTesting();
        }

        public void Dispose()
        {
            ProfileService.Instance.CurrentProfile = _originalProfile;
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

        #region 1. Objectives Lacking Location Points (Wiki-Only Objectives)

        [Fact]
        public async Task WikiOnlyObjectives_WithoutLocationPoints_TracksProgressCorrectly()
        {
            var questNormName = "test-quest-wiki-only-no-loc";
            _testQuestsToClean.Add(questNormName);
            _testKeysToClean.Add($"{questNormName}:0");
            _testKeysToClean.Add($"{questNormName}:1");

            // Ensure no objectives exist in QuestObjectiveDbService for this quest
            QuestObjectiveDbService.Instance.ClearObjectivesForTesting();

            var task = new TarkovTask
            {
                Ids = new List<string> { "wiki_task_123" },
                Name = "Test Wiki Only Task",
                NormalizedName = questNormName,
                Objectives = new List<string>
                {
                    "Survive and extract from the raid through any extraction",
                    "Gain PMC experience: 5,000 EXP",
                    "Hand over found in raid military corrugated tubes: 2"
                }
            };

            // Initial state: not completed
            Assert.False(_progressService.IsObjectiveCompleted(questNormName, 0));
            Assert.False(_progressService.IsObjectiveCompleted(questNormName, 1));
            Assert.False(_progressService.IsObjectiveCompleted(questNormName, 2));

            var (completed0, total0) = _progressService.GetObjectiveProgress(task);
            Assert.Equal(0, completed0);
            Assert.Equal(3, total0);

            // Mark objective 0 as completed (no objectiveId)
            await _progressService.SetObjectiveCompletedAsync(questNormName, 0, true, null);

            Assert.True(_progressService.IsObjectiveCompleted(questNormName, 0));
            Assert.False(_progressService.IsObjectiveCompleted(questNormName, 1));
            Assert.False(_progressService.IsObjectiveCompleted(questNormName, 2));

            var (completed1, total1) = _progressService.GetObjectiveProgress(task);
            Assert.Equal(1, completed1);
            Assert.Equal(3, total1);

            // Mark objective 2 as completed
            await _progressService.SetObjectiveCompletedAsync(questNormName, 2, true, null);
            _testKeysToClean.Add($"{questNormName}:2");

            var completedSet = _progressService.GetCompletedObjectives(questNormName);
            Assert.Equal(2, completedSet.Count);
            Assert.Contains(0, completedSet);
            Assert.Contains(2, completedSet);
            Assert.DoesNotContain(1, completedSet);

            var (completed2, total2) = _progressService.GetObjectiveProgress(task);
            Assert.Equal(2, completed2);
            Assert.Equal(3, total2);

            // Verify persistence in DB
            var dbProgress = await _userDataDb.LoadObjectiveProgressAsync();
            Assert.True(dbProgress.TryGetValue($"{questNormName}:0", out var val0) && val0);
            Assert.True(dbProgress.TryGetValue($"{questNormName}:2", out var val2) && val2);
            Assert.False(dbProgress.ContainsKey($"{questNormName}:1"));
        }

        [Fact]
        public async Task ObjectivesWithEmptyOrNullLocationJson_ResolvesObjectiveIdSafely()
        {
            var questNormName = "test-quest-empty-loc-json";
            var objId = "obj-empty-loc-456";
            _testQuestsToClean.Add(questNormName);
            _testKeysToClean.Add($"{questNormName}:0");
            _testKeysToClean.Add($"id:{objId}");

            var testObjectives = new List<QuestObjective>
            {
                new QuestObjective
                {
                    Id = objId,
                    QuestId = questNormName,
                    QuestName = "Test Quest Empty Location",
                    Description = "Locate the hidden flash drive (no map points)",
                    LocationPointsJson = null // Null location coordinates
                }
            };
            QuestObjectiveDbService.Instance.SetObjectivesForTesting(testObjectives);

            // Set completed using index; should resolve objId by index/description
            await _progressService.SetObjectiveCompletedAsync(questNormName, 0, true, null);

            Assert.True(_progressService.IsObjectiveCompleted(questNormName, 0));
            Assert.True(_progressService.IsObjectiveCompletedById(objId));

            // Verify DB saved both
            var dbProgress = await _userDataDb.LoadObjectiveProgressAsync();
            Assert.True(dbProgress.ContainsKey($"{questNormName}:0"));
            Assert.True(dbProgress.ContainsKey($"id:{objId}"));
        }

        #endregion

        #region 2. Special Characters and Long Descriptions

        [Fact]
        public async Task Objectives_WithSpecialCharactersAndMarkup_MatchesAndPersistsCleanly()
        {
            var questNormName = "test-quest-special-chars-markup";
            var objId = "obj-special-789";
            _testQuestsToClean.Add(questNormName);
            _testKeysToClean.Add($"{questNormName}:0");
            _testKeysToClean.Add($"id:{objId}");

            // Complex Tarkov wiki text with wiki links, HTML entities, quotes, Cyrillic, and newlines
            var wikiObjectiveText = "Find & obtain the [[Customs/Dorm room 214 key|Dorm room 214 key]] (Ключ от комнаты 214)\r\n" +
                                   "\"TerraGroup Labs\" encrypted hard drive (FIR: 100%)";

            var dbObjectiveText = "Find & obtain the Dorm room 214 key (Ключ от комнаты 214) \"TerraGroup Labs\" encrypted hard drive (FIR: 100%)";

            var testObjectives = new List<QuestObjective>
            {
                new QuestObjective
                {
                    Id = objId,
                    QuestId = questNormName,
                    QuestName = "Test Quest Special Chars",
                    Description = dbObjectiveText,
                    LocationPointsJson = "[{\"x\":10.5,\"y\":20.5,\"z\":30.5}]"
                }
            };
            QuestObjectiveDbService.Instance.SetObjectivesForTesting(testObjectives);

            var task = new TarkovTask
            {
                Ids = new List<string> { "special_chars_task" },
                Name = "Test Quest Special Chars",
                NormalizedName = questNormName,
                Objectives = new List<string> { wikiObjectiveText }
            };

            // Test ResolveObjectiveId with wiki description containing markup & special characters
            var resolvedId = _progressService.ResolveObjectiveId(questNormName, 0, wikiObjectiveText, task);
            Assert.Equal(objId, resolvedId);

            // Complete objective
            await _progressService.SetObjectiveCompletedAsync(questNormName, 0, true, resolvedId);

            Assert.True(_progressService.IsObjectiveCompleted(questNormName, 0));
            Assert.True(_progressService.IsObjectiveCompletedById(objId));

            // Check DB persistence
            var dbProgress = await _userDataDb.LoadObjectiveProgressAsync();
            Assert.True(dbProgress.ContainsKey($"{questNormName}:0"));
            Assert.True(dbProgress.ContainsKey($"id:{objId}"));
        }

        [Fact]
        public async Task Objectives_WithVeryLongDescription_PersistsWithoutCorruption()
        {
            var questNormName = "test-quest-long-desc";
            var objId = "obj-long-desc-999";
            _testQuestsToClean.Add(questNormName);
            _testKeysToClean.Add($"{questNormName}:0");
            _testKeysToClean.Add($"id:{objId}");

            // Generate a 4000 character description
            var longDesc = "Eliminate targets: " + new string('X', 4000);

            var testObjectives = new List<QuestObjective>
            {
                new QuestObjective
                {
                    Id = objId,
                    QuestId = questNormName,
                    QuestName = "Test Long Desc Task",
                    Description = longDesc,
                    LocationPointsJson = "[{\"x\":0,\"y\":0,\"z\":0}]"
                }
            };
            QuestObjectiveDbService.Instance.SetObjectivesForTesting(testObjectives);

            var task = new TarkovTask
            {
                Ids = new List<string> { "long_desc_task" },
                Name = "Test Long Desc Task",
                NormalizedName = questNormName,
                Objectives = new List<string> { longDesc }
            };

            var resolvedId = _progressService.ResolveObjectiveId(questNormName, 0, longDesc, task);
            Assert.Equal(objId, resolvedId);

            await _progressService.SetObjectiveCompletedAsync(questNormName, 0, true, resolvedId);
            Assert.True(_progressService.IsObjectiveCompleted(questNormName, 0));
            Assert.True(_progressService.IsObjectiveCompletedById(objId));
        }

        [Fact]
        public void Objectives_WithNullOrEmptyValues_DoesNotThrowNullReferenceException()
        {
            // Null description in QuestObjective
            var objNullDesc = new QuestObjective
            {
                Id = "null-desc-obj",
                QuestId = "null-quest",
                QuestName = "Null Quest",
                Description = null!
            };

            QuestObjectiveDbService.Instance.SetObjectivesForTesting(new List<QuestObjective> { objNullDesc });

            // Testing ResolveObjectiveId with null objectiveDesc and null Task
            var id1 = _progressService.ResolveObjectiveId("null-quest", 0, null, null);
            Assert.Equal("null-desc-obj", id1);

            // Testing ResolveQuestAndIndex with null obj
            var res = _progressService.ResolveQuestAndIndex(null!);
            Assert.Null(res.QuestNormalizedName);
            Assert.Equal(-1, res.ObjectiveIndex);

            // Testing IsObjectiveCompletedById with null/empty
            Assert.False(_progressService.IsObjectiveCompletedById(null));
            Assert.False(_progressService.IsObjectiveCompletedById(string.Empty));

            // Testing IsObjectiveCompleted with empty
            Assert.False(_progressService.IsObjectiveCompleted(string.Empty, -1));
        }

        #endregion

        #region 3. Profile Switching (PVP vs PVE Isolation)

        [Fact]
        public async Task ProfileSwitching_UserDataDb_IsolatesObjectiveProgressBetweenPvpAndPve()
        {
            var pvpKey = "pvp-quest-isolated:0";
            var pveKey = "pve-quest-isolated:0";
            _testKeysToClean.Add(pvpKey);
            _testKeysToClean.Add(pveKey);

            // 1. In PVP profile: save pvpKey as completed
            ProfileService.Instance.CurrentProfile = ProfileType.Pvp;
            await _userDataDb.SaveObjectiveProgressAsync(pvpKey, "pvp-quest-isolated", true);

            var pvpEntries = await _userDataDb.LoadObjectiveProgressAsync();
            Assert.True(pvpEntries.TryGetValue(pvpKey, out var pvpVal) && pvpVal, "PVP profile must contain pvpKey");
            Assert.False(pvpEntries.ContainsKey(pveKey), "PVP profile must NOT contain pveKey");

            // 2. Switch to PVE profile: verify pvpKey is NOT in PVE profile
            ProfileService.Instance.CurrentProfile = ProfileType.Pve;
            var pveEntriesBefore = await _userDataDb.LoadObjectiveProgressAsync();
            Assert.False(pveEntriesBefore.ContainsKey(pvpKey), "PVE profile must NOT contain pvpKey from PVP");

            // In PVE: save pveKey as completed
            await _userDataDb.SaveObjectiveProgressAsync(pveKey, "pve-quest-isolated", true);
            var pveEntriesAfter = await _userDataDb.LoadObjectiveProgressAsync();
            Assert.True(pveEntriesAfter.TryGetValue(pveKey, out var pveVal) && pveVal, "PVE profile must contain pveKey");
            Assert.False(pveEntriesAfter.ContainsKey(pvpKey), "PVE profile must still NOT contain pvpKey");

            // 3. Switch back to PVP: verify pveKey is NOT in PVP profile
            ProfileService.Instance.CurrentProfile = ProfileType.Pvp;
            var pvpEntriesReloaded = await _userDataDb.LoadObjectiveProgressAsync();
            Assert.True(pvpEntriesReloaded.TryGetValue(pvpKey, out var pvpVal2) && pvpVal2, "PVP profile must retain pvpKey");
            Assert.False(pvpEntriesReloaded.ContainsKey(pveKey), "PVP profile must NOT contain pveKey");
        }

        [Fact]
        public async Task ProfileSwitching_ObjectiveProgressService_LoadsAndIsolatesWhenReloaded()
        {
            var questName = "cross-profile-quest";
            var pvpObjKey = $"{questName}:0";
            _testQuestsToClean.Add(questName);
            _testKeysToClean.Add(pvpObjKey);

            // 1. In PVP: save in DB and in memory
            ProfileService.Instance.CurrentProfile = ProfileType.Pvp;
            await _progressService.SetObjectiveCompletedAsync(questName, 0, true);
            Assert.True(_progressService.IsObjectiveCompleted(questName, 0));

            // 2. Switch profile to PVE and call LoadObjectiveProgressAsync
            ProfileService.Instance.CurrentProfile = ProfileType.Pve;
            await _progressService.LoadObjectiveProgressAsync();

            // In PVE, this objective must NOT be completed!
            Assert.False(_progressService.IsObjectiveCompleted(questName, 0),
                "After profile switch to PVE and LoadObjectiveProgressAsync, PVP progress must NOT appear");

            // 3. Switch back to PVP and call LoadObjectiveProgressAsync
            ProfileService.Instance.CurrentProfile = ProfileType.Pvp;
            await _progressService.LoadObjectiveProgressAsync();

            // In PVP, this objective must be completed again!
            Assert.True(_progressService.IsObjectiveCompleted(questName, 0),
                "After profile switch back to PVP and LoadObjectiveProgressAsync, PVP progress must be restored");
        }

        [Fact]
        public async Task ProfileSwitching_WithoutReload_ExposesStaleCacheRisk()
        {
            var questName = "stale-cache-quest";
            _testQuestsToClean.Add(questName);
            _testKeysToClean.Add($"{questName}:0");

            // 1. Mark objective in PVP
            ProfileService.Instance.CurrentProfile = ProfileType.Pvp;
            await _progressService.SetObjectiveCompletedAsync(questName, 0, true);

            // 2. Change ProfileService.CurrentProfile to Pve WITHOUT calling LoadObjectiveProgressAsync
            ProfileService.Instance.CurrentProfile = ProfileType.Pve;

            // Notice: The in-memory cache still holds the PVP state unless LoadObjectiveProgressAsync is called!
            // This proves empirically that RefreshCurrentProfileDataAsync MUST invoke LoadObjectiveProgressAsync
            // to ensure UI isolation when switching profiles.
            bool inMemoryLeaked = _progressService.IsObjectiveCompleted(questName, 0);
            Assert.True(inMemoryLeaked, "Without reloading, in-memory dictionary retains prior profile's state");

            // 3. Now explicitly invoke LoadObjectiveProgressAsync
            await _progressService.LoadObjectiveProgressAsync();
            Assert.False(_progressService.IsObjectiveCompleted(questName, 0),
                "Once LoadObjectiveProgressAsync is invoked, PVE reflects true DB state (false)");
        }

        #endregion

        #region 4. Clearing Quest Progress (ClearObjectiveProgress)

        [Fact]
        public async Task ClearObjectiveProgress_ClearsBothIndexAndIdKeys_AndPersistsDeletion()
        {
            var questNormName = "test-quest-clear-deep";
            var objId0 = "clear-obj-id-0";
            var objId1 = "clear-obj-id-1";
            _testQuestsToClean.Add(questNormName);
            _testKeysToClean.Add($"{questNormName}:0");
            _testKeysToClean.Add($"{questNormName}:1");
            _testKeysToClean.Add($"id:{objId0}");
            _testKeysToClean.Add($"id:{objId1}");

            var testObjectives = new List<QuestObjective>
            {
                new QuestObjective
                {
                    Id = objId0,
                    QuestId = questNormName,
                    QuestName = "Test Quest Clear Deep",
                    Description = "Objective 0",
                    LocationPointsJson = "[{\"x\":1,\"y\":2,\"z\":3}]"
                },
                new QuestObjective
                {
                    Id = objId1,
                    QuestId = questNormName,
                    QuestName = "Test Quest Clear Deep",
                    Description = "Objective 1",
                    LocationPointsJson = "[{\"x\":4,\"y\":5,\"z\":6}]"
                }
            };
            QuestObjectiveDbService.Instance.SetObjectivesForTesting(testObjectives);

            // Set both 0 and 1 as completed
            await _progressService.SetObjectiveCompletedAsync(questNormName, 0, true, objId0);
            await _progressService.SetObjectiveCompletedAsync(questNormName, 1, true, objId1);

            Assert.True(_progressService.IsObjectiveCompleted(questNormName, 0));
            Assert.True(_progressService.IsObjectiveCompleted(questNormName, 1));
            Assert.True(_progressService.IsObjectiveCompletedById(objId0));
            Assert.True(_progressService.IsObjectiveCompletedById(objId1));

            // Verify both saved in DB
            var dbBefore = await _userDataDb.LoadObjectiveProgressAsync();
            Assert.True(dbBefore.ContainsKey($"{questNormName}:0"));
            Assert.True(dbBefore.ContainsKey($"{questNormName}:1"));
            Assert.True(dbBefore.ContainsKey($"id:{objId0}"));
            Assert.True(dbBefore.ContainsKey($"id:{objId1}"));

            // Event listener check
            bool eventFired = false;
            string? changedQuest = null;
            _progressService.ObjectiveProgressChanged += (s, e) =>
            {
                eventFired = true;
                changedQuest = e.QuestNormalizedName;
            };

            // Act: ClearObjectiveProgress
            _progressService.ClearObjectiveProgress(questNormName);

            // Verify in-memory state immediately cleared
            Assert.False(_progressService.IsObjectiveCompleted(questNormName, 0));
            Assert.False(_progressService.IsObjectiveCompleted(questNormName, 1));
            Assert.False(_progressService.IsObjectiveCompletedById(objId0));
            Assert.False(_progressService.IsObjectiveCompletedById(objId1));
            Assert.True(eventFired);
            Assert.Equal(questNormName, changedQuest);

            // Give async DB delete small window to complete
            await Task.Delay(200);

            // Verify DB state
            var dbAfter = await _userDataDb.LoadObjectiveProgressAsync();
            Assert.False(dbAfter.ContainsKey($"{questNormName}:0"));
            Assert.False(dbAfter.ContainsKey($"{questNormName}:1"));
            Assert.False(dbAfter.ContainsKey($"id:{objId0}"));
            Assert.False(dbAfter.ContainsKey($"id:{objId1}"));
        }

        [Fact]
        public async Task ClearObjectiveProgress_DoesNotAffectOtherQuestsWithSimilarPrefix()
        {
            var questPrefix = "test-quest-prefix";
            var questA = $"{questPrefix}-alpha";
            var questB = $"{questPrefix}-beta";

            _testQuestsToClean.Add(questA);
            _testQuestsToClean.Add(questB);
            _testKeysToClean.Add($"{questA}:0");
            _testKeysToClean.Add($"{questB}:0");

            await _progressService.SetObjectiveCompletedAsync(questA, 0, true);
            await _progressService.SetObjectiveCompletedAsync(questB, 0, true);

            Assert.True(_progressService.IsObjectiveCompleted(questA, 0));
            Assert.True(_progressService.IsObjectiveCompleted(questB, 0));

            // Act: Clear only questA
            _progressService.ClearObjectiveProgress(questA);

            // Assert: questA cleared, questB untouched
            Assert.False(_progressService.IsObjectiveCompleted(questA, 0));
            Assert.True(_progressService.IsObjectiveCompleted(questB, 0));

            await Task.Delay(200);

            var dbAfter = await _userDataDb.LoadObjectiveProgressAsync();
            Assert.False(dbAfter.ContainsKey($"{questA}:0"));
            Assert.True(dbAfter.ContainsKey($"{questB}:0"));
        }

        #endregion
    }
}
