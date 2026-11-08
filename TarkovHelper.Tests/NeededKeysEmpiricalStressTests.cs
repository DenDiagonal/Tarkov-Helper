using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TarkovHelper.Models;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Services;
using TarkovHelper.Services.DataAudit;
using Xunit;

namespace TarkovHelper.Tests
{
    [Collection("DatabaseUpdateServiceCollection")]
    public class NeededKeysEmpiricalStressTests : IDisposable
    {
        public NeededKeysEmpiricalStressTests()
        {
            NeededKeysService.ResetInstance();
            ItemInventoryService.ResetInstance();
            QuestProgressService.Instance.ClearProgressForTesting();
        }

        public void Dispose()
        {
            NeededKeysService.ResetInstance();
            ItemInventoryService.ResetInstance();
            QuestProgressService.Instance.ClearProgressForTesting();
            QuestProgressService.Instance.LoadProgressAsync().GetAwaiter().GetResult();
        }

        [Fact]
        public void CanonicalKeyNames_All92Keys_ResolveToAuthoritativeEnglishNames_NeverFallbackToHex()
        {
            var service = NeededKeysService.Instance;
            var canonicalDict = NeededKeysService.CanonicalKeyNames;

            // Verify total dictionary count is 92
            Assert.Equal(92, canonicalDict.Count);

            foreach (var kvp in canonicalDict)
            {
                var hexId = kvp.Key;
                var englishName = kvp.Value;

                // BSG Item IDs are 24-character hex strings
                Assert.Equal(24, hexId.Length);
                Assert.False(string.IsNullOrWhiteSpace(englishName), $"Key {hexId} must have non-empty name");

                // 1. Lowercase resolution
                var resolvedLower = service.ResolveKeyName(hexId.ToLowerInvariant());
                Assert.Equal(englishName, resolvedLower);

                // 2. Uppercase resolution
                var resolvedUpper = service.ResolveKeyName(hexId.ToUpperInvariant());
                Assert.Equal(englishName, resolvedUpper);

                // 3. Fallback override test: canonical must ALWAYS override user-provided fallback
                var resolvedWithFallback = service.ResolveKeyName(hexId, "Generic Fallback Key");
                Assert.Equal(englishName, resolvedWithFallback);

                // 4. Verification that output is NEVER the raw hex ID
                Assert.NotEqual(hexId, resolvedLower);
            }
        }

        [Fact]
        public void TrackerDataset_AllKeyReferencesInScratch_ResolveToAuthoritativeNames()
        {
            var service = NeededKeysService.Instance;

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);
            string? scratchDir = null;
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "scratch");
                if (Directory.Exists(candidate))
                {
                    scratchDir = candidate;
                    break;
                }
                dir = dir.Parent;
            }

            Assert.NotNull(scratchDir);
            var corePath = Path.Combine(scratchDir!, "tasks_core.json");
            var objPath = Path.Combine(scratchDir!, "tasks_objectives.json");

            if (!File.Exists(corePath) || !File.Exists(objPath))
            {
                Assert.Fail($"Scratch tracker files missing at: {corePath} or {objPath}");
                return;
            }

            var coreJson = File.ReadAllText(corePath);
            var coreData = TarkovTrackerExtractorService.ParseTasksCore(coreJson);

            var objJson = File.ReadAllText(objPath);
            var objData = TarkovTrackerExtractorService.ParseTasksObjectives(objJson);

            var referencedKeyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in coreData.Tasks)
            {
                if (task.NeededKeys != null)
                {
                    foreach (var group in task.NeededKeys)
                    {
                        foreach (var keyRef in group.Keys)
                        {
                            if (!string.IsNullOrWhiteSpace(keyRef.Id))
                                referencedKeyIds.Add(keyRef.Id);
                        }
                    }
                }
            }

            foreach (var objectives in objData.Values)
            {
                foreach (var obj in objectives)
                {
                    if (obj.RequiredKeys != null)
                    {
                        foreach (var group in obj.RequiredKeys)
                        {
                            foreach (var keyRef in group)
                            {
                                if (!string.IsNullOrWhiteSpace(keyRef.Id))
                                    referencedKeyIds.Add(keyRef.Id);
                            }
                        }
                    }
                }
            }

            // Empirical assertion: at least 50 distinct key IDs exist in the tracker dataset
            Assert.True(referencedKeyIds.Count >= 50, $"Expected >= 50 referenced keys, found {referencedKeyIds.Count}");

            var unmappedHexKeys = new List<string>();

            foreach (var keyId in referencedKeyIds)
            {
                var resolved = service.ResolveKeyName(keyId);
                if (string.Equals(resolved, keyId, StringComparison.OrdinalIgnoreCase))
                {
                    unmappedHexKeys.Add(keyId);
                }
            }

            // Zero keys from TarkovTracker should fail to resolve to a human-readable English name
            Assert.Empty(unmappedHexKeys);
        }

        [Fact]
        public void StashReactivity_InventoryChangeEvents_TriggerLiveUpdatesAndReflectExactQuantities()
        {
            var service = NeededKeysService.Instance;
            var inventory = ItemInventoryService.Instance;

            int eventCount = 0;
            service.DataChanged += () => eventCount++;

            const string testKeyId = "5780cf7f2459777de4559322"; // Dorm room 314 marked key
            const string normalizedKeyName = "dorm-room-314-marked-key";

            // 1. Initially missing
            Assert.False(service.IsKeyInStash(testKeyId, out var initialQty));
            Assert.Equal(0, initialQty);

            // 2. Set FIR quantity -> should trigger DataChanged and update count
            inventory.SetFirQuantity(normalizedKeyName, 2);
            Assert.True(eventCount >= 1, "Setting FIR quantity must fire DataChanged");
            Assert.True(service.IsKeyInStash(testKeyId, out var firQty));
            Assert.Equal(2, firQty);

            // 3. Set Non-FIR quantity -> should trigger DataChanged and update count
            int previousEventCount = eventCount;
            inventory.SetNonFirQuantity(normalizedKeyName, 3);
            Assert.True(eventCount > previousEventCount, "Setting Non-FIR quantity must fire DataChanged");
            Assert.True(service.IsKeyInStash(testKeyId, out var totalQty));
            Assert.Equal(5, totalQty); // 2 FIR + 3 Non-FIR

            // 4. Query by canonical English name directly
            Assert.True(service.IsKeyInStash("Dorm room 314 marked key", out var englishNameQty));
            Assert.Equal(5, englishNameQty);

            // 5. Query with case variation (UPPERCASE)
            Assert.True(service.IsKeyInStash(testKeyId.ToUpperInvariant(), out var upperQty));
            Assert.Equal(5, upperQty);

            // 6. Reset inventory to zero
            previousEventCount = eventCount;
            inventory.SetFirQuantity(normalizedKeyName, 0);
            inventory.SetNonFirQuantity(normalizedKeyName, 0);
            Assert.True(eventCount > previousEventCount, "Resetting quantity must fire DataChanged");
            Assert.False(service.IsKeyInStash(testKeyId, out var zeroQty));
            Assert.Equal(0, zeroQty);
        }

        [Fact]
        public void StashResolution_BidirCrossResolution_SupportsDirectIdEnglishNameAndNormalized()
        {
            var service = NeededKeysService.Instance;
            var inventory = ItemInventoryService.Instance;

            const string dorm220Hex = "5780cfba2459777de4559329";
            const string dorm220English = "Dorm room 220 key";
            const string dorm220Norm = "dorm-room-220-key";

            // Store by normalized name (standard TarkovHelper storage pattern)
            inventory.SetNonFirQuantity(dorm220Norm, 4);

            // Resolving by hex ID must find the 4 items via CanonicalKeyNames -> Normalized
            Assert.True(service.IsKeyInStash(dorm220Hex, out var qtyFromHex));
            Assert.Equal(4, qtyFromHex);

            // Resolving by English name must find the 4 items via NormalizedNameGenerator
            Assert.True(service.IsKeyInStash(dorm220English, out var qtyFromEnglish));
            Assert.Equal(4, qtyFromEnglish);

            // Resolving by normalized name directly
            Assert.True(service.IsKeyInStash(dorm220Norm, out var qtyFromNorm));
            Assert.Equal(4, qtyFromNorm);

            // Storing under hex ID directly
            const string machineryHex = "5937ee6486f77408994ba448";
            inventory.SetFirQuantity(machineryHex, 1);

            // Resolving by hex ID directly succeeds
            Assert.True(service.IsKeyInStash(machineryHex, out var machineryQty));
            Assert.Equal(1, machineryQty);

            // Resolving by English name when stored ONLY under hex ID:
            // Empirical finding: CanonicalKeyNames is a forward mapping (Hex -> Name).
            // Direct query by English name does not reverse-map to hex ID when inventory only contains hex.
            bool foundByNameWhenStoredAsHex = service.IsKeyInStash("Machinery key", out _);
            Assert.False(foundByNameWhenStoredAsHex, "English name lookup does not reverse-map to hex ID without reverse dictionary or ItemDb");
        }

        [Theory]
        [InlineData("Factory emergency exit key", true)]
        [InlineData("TerraGroup Labs keycard (Black)", true)]
        [InlineData("Dorm room 314 marked key", true)]
        [InlineData("Salewa first aid kit", false)]
        [InlineData("Gas analyzer", false)]
        [InlineData("Not A Key Item", true)] // Contains " Key" substring -> matches IsKey
        [InlineData("Gingy keychain", true)] // Contains " key" substring -> matches IsKey
        public void IsKey_SubstringMatching_DocumentsEdgeCases(string itemName, bool expectedIsKey)
        {
            Assert.Equal(expectedIsKey, NeededKeysService.IsKey(itemName));
        }

        [Fact]
        public void GetNeededKeysForQuest_NullSafetyAndBoundaryAssault_NeverThrows()
        {
            var service = NeededKeysService.Instance;

            // 1. Completely null quest
            var nullQuestResult = service.GetNeededKeysForQuest(null!);
            Assert.NotNull(nullQuestResult);
            Assert.Empty(nullQuestResult);

            // 2. Quest with all null collections and properties
            var emptyQuest = new TarkovTask
            {
                Ids = null,
                Name = null!,
                NormalizedName = null,
                Maps = null,
                RequiredItems = null
            };
            var emptyResult = service.GetNeededKeysForQuest(emptyQuest);
            Assert.NotNull(emptyResult);
            Assert.Empty(emptyResult);

            // 3. Quest with null elements in RequiredItems list
            var questWithNullElements = new TarkovTask
            {
                Ids = new List<string> { "q_null_elems" },
                Name = "Quest with Null Elements",
                NormalizedName = "quest-with-null-elements",
                RequiredItems = new List<QuestItem>
                {
                    null!,
                    new() { ItemDisplayName = null, ItemNormalizedName = null!, Amount = 1 },
                    new() { ItemDisplayName = "Salewa first aid kit", ItemNormalizedName = "salewa-first-aid-kit", Amount = 2 },
                    new() { ItemDisplayName = "Factory emergency exit key", ItemNormalizedName = "factory-emergency-exit-key", Amount = 1 },
                    null!
                }
            };
            var nullElemsResult = service.GetNeededKeysForQuest(questWithNullElements);
            Assert.NotNull(nullElemsResult);
            Assert.Single(nullElemsResult);
            Assert.Equal("Factory emergency exit key", nullElemsResult[0].Name);

            // 4. Tracker task with null collections and nested null items
            var malformedTrackerTask = new TarkovTrackerTask
            {
                Id = "malformed_tracker_task",
                Name = "Malformed Task",
                NormalizedName = "malformed-task",
                NeededKeys = new List<TarkovTrackerNeededKeyGroup>
                {
                    new()
                    {
                        Map = null,
                        Keys = new List<TarkovTrackerKeyRef>
                        {
                            new() { Id = "", Name = null, ShortName = null },
                            new() { Id = "5937ee6486f77408994ba448", Name = "Machinery key", ShortName = "Machinery" }
                        }
                    }
                },
                Objectives = new List<TarkovTrackerObjective>
                {
                    new()
                    {
                        Description = null,
                        Maps = null!,
                        RequiredKeys = new List<List<TarkovTrackerKeyRef>>
                        {
                            new()
                            {
                                new() { Id = "5937ee6486f77408994ba448", Name = "Machinery key", ShortName = "Machinery" } // Duplicate key
                            }
                        }
                    }
                }
            };

            service.RegisterTrackerTask(malformedTrackerTask);

            var questForTracker = new TarkovTask
            {
                Ids = new List<string> { "malformed_tracker_task" },
                Name = "Malformed Task",
                NormalizedName = "malformed-task"
            };

            var trackerResult = service.GetNeededKeysForQuest(questForTracker);
            Assert.NotNull(trackerResult);
            // Empirical finding: empty key references ({ Id = "", Name = null }) are added as blank NeededKeyInfo
            Assert.Equal(2, trackerResult.Count);
            Assert.Contains(trackerResult, k => k.Name == "Machinery key");
            Assert.Contains(trackerResult, k => string.IsNullOrEmpty(k.KeyId) && string.IsNullOrEmpty(k.Name));
        }

        [Fact]
        public void FutureMissionItems_FulfillmentAndShortageCalculations_AccurateAndNonNegative()
        {
            var inventory = ItemInventoryService.Instance;

            // Scenario 1: Shortage when owned < required
            inventory.SetFirQuantity("test-fir-item", 2);
            inventory.SetNonFirQuantity("test-fir-item", 1);

            var fulfillmentUnder = inventory.GetFulfillmentInfo("test-fir-item", requiredTotal: 5, requiredFir: 4);
            Assert.Equal(5, fulfillmentUnder.RequiredTotal);
            Assert.Equal(4, fulfillmentUnder.RequiredFir);
            Assert.Equal(2, fulfillmentUnder.OwnedFir);
            Assert.Equal(1, fulfillmentUnder.OwnedNonFir);
            Assert.Equal(3, fulfillmentUnder.OwnedTotal);
            Assert.False(fulfillmentUnder.IsTotalFulfilled);
            Assert.False(fulfillmentUnder.IsFirFulfilled);

            var itemInfoUnder = new FutureMissionItemInfo
            {
                RequiredCount = 5,
                OwnedFir = fulfillmentUnder.OwnedFir,
                OwnedNonFir = fulfillmentUnder.OwnedNonFir
            };
            Assert.Equal(2, itemInfoUnder.Shortage);
            Assert.False(itemInfoUnder.IsFulfilled);

            // Scenario 2: Over-fulfilled -> Shortage must clamp to 0, never negative
            inventory.SetFirQuantity("test-fir-item", 8);
            inventory.SetNonFirQuantity("test-fir-item", 2);

            var fulfillmentOver = inventory.GetFulfillmentInfo("test-fir-item", requiredTotal: 5, requiredFir: 4);
            Assert.True(fulfillmentOver.IsTotalFulfilled);
            Assert.True(fulfillmentOver.IsFirFulfilled);

            var itemInfoOver = new FutureMissionItemInfo
            {
                RequiredCount = 5,
                OwnedFir = fulfillmentOver.OwnedFir,
                OwnedNonFir = fulfillmentOver.OwnedNonFir
            };
            Assert.Equal(0, itemInfoOver.Shortage);
            Assert.True(itemInfoOver.IsFulfilled);
        }

        [Fact]
        public void HighConcurrency_StressTest_ReadWriteReactivityUnder100Threads()
        {
            var service = NeededKeysService.Instance;
            var inventory = ItemInventoryService.Instance;
            var exceptions = new ConcurrentBag<Exception>();

            var trackerKeys = NeededKeysService.CanonicalKeyNames.Keys.Take(20).ToList();

            Parallel.For(0, 100, i =>
            {
                try
                {
                    var hexId = trackerKeys[i % trackerKeys.Count];

                    // 1. Concurrent task registration
                    service.RegisterTrackerTask(new TarkovTrackerTask
                    {
                        Id = $"concurrent_task_{i}",
                        Name = $"Concurrent Task {i}",
                        NormalizedName = $"concurrent-task-{i}",
                        NeededKeys = new List<TarkovTrackerNeededKeyGroup>
                        {
                            new()
                            {
                                Keys = new List<TarkovTrackerKeyRef>
                                {
                                    new() { Id = hexId }
                                }
                            }
                        }
                    });

                    // 2. Concurrent inventory mutation
                    inventory.SetFirQuantity(hexId, i % 5);
                    inventory.SetNonFirQuantity(hexId, (i + 1) % 5);

                    // 3. Concurrent name resolution
                    var resolved = service.ResolveKeyName(hexId);
                    Assert.False(string.IsNullOrEmpty(resolved));

                    // 4. Concurrent stash check
                    service.IsKeyInStash(hexId, out var qty);
                    Assert.True(qty >= 0);

                    // 5. Concurrent quest key resolution
                    var needed = service.GetNeededKeysForQuest(new TarkovTask
                    {
                        Ids = new List<string> { $"concurrent_task_{i}" },
                        Name = $"Concurrent Task {i}",
                        NormalizedName = $"concurrent-task-{i}"
                    });
                    Assert.NotNull(needed);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert.Empty(exceptions);
        }
    }
}
