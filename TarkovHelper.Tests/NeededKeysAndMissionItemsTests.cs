using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using TarkovHelper.Models;
using TarkovHelper.Models.DataAudit;
using TarkovHelper.Pages;
using TarkovHelper.Services;
using Xunit;

namespace TarkovHelper.Tests
{
    [Collection("DatabaseUpdateServiceCollection")]
    public class NeededKeysAndMissionItemsTests : IDisposable
    {
        public NeededKeysAndMissionItemsTests()
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

        #region Tier 1 & 2: Deserialization & Polymorphism

        [Fact]
        public void TarkovTrackerDataModels_DeserializeTaskWithNeededKeys_PopulatesMapAndKeys()
        {
            var json = @"{
                ""id"": ""task_test_001"",
                ""name"": ""The Key to Success"",
                ""neededKeys"": [
                    {
                        ""map"": { ""id"": ""5b0fc42d86f7744a585f9105"", ""name"": ""The Lab"" },
                        ""keys"": [
                            { ""id"": ""5c1d0f4986f7744bb01837fa"", ""name"": ""TerraGroup Labs keycard (Black)"", ""shortName"": ""Black"" },
                            { ""id"": ""5c1d0c5f86f7744bb2683cf0"", ""name"": ""TerraGroup Labs access keycard"", ""shortName"": ""Access"" }
                        ]
                    }
                ]
            }";

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var task = JsonSerializer.Deserialize<TarkovTrackerTask>(json, options);

            Assert.NotNull(task);
            Assert.Equal("task_test_001", task.Id);
            Assert.NotNull(task.NeededKeys);
            Assert.Single(task.NeededKeys);

            var keyGroup = task.NeededKeys[0];
            Assert.Equal("The Lab", keyGroup.Map?.Name);
            Assert.Equal(2, keyGroup.Keys.Count);
            Assert.Equal("5c1d0f4986f7744bb01837fa", keyGroup.Keys[0].Id);
            Assert.Equal("Black", keyGroup.Keys[0].ShortName);
        }

        [Fact]
        public void TarkovTrackerDataModels_DeserializeObjectiveWithRequiredKeys_2DListAndAlternatives()
        {
            var json = @"{
                ""id"": ""obj_001"",
                ""description"": ""Unlock the marked room in dorms"",
                ""requiredKeys"": [
                    [
                        { ""id"": ""5d80cb1886f77440373c4ef0"", ""name"": ""RB-BK marked key"", ""shortName"": ""RB-BK"" },
                        { ""id"": ""5d80cb5786f7744036212b53"", ""name"": ""RB-VO marked key"", ""shortName"": ""RB-VO"" }
                    ]
                ]
            }";

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var obj = JsonSerializer.Deserialize<TarkovTrackerObjective>(json, options);

            Assert.NotNull(obj);
            Assert.NotNull(obj.RequiredKeys);
            Assert.Single(obj.RequiredKeys);
            Assert.Equal(2, obj.RequiredKeys[0].Count);
            Assert.Equal("5d80cb1886f77440373c4ef0", obj.RequiredKeys[0][0].Id);
            Assert.Equal("RB-VO marked key", obj.RequiredKeys[0][1].Name);
        }

        [Fact]
        public void TarkovTrackerKeyRefConverter_HandlesPolymorphicStringOrObject()
        {
            var jsonStringKey = @"""5a13f46386f7741dd7384b04""";
            var jsonObjectKey = @"{ ""id"": ""5937ee6486f77408994ba448"", ""name"": ""Machinery key"", ""shortName"": ""Machinery"" }";

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var fromString = JsonSerializer.Deserialize<TarkovTrackerKeyRef>(jsonStringKey, options);
            var fromObject = JsonSerializer.Deserialize<TarkovTrackerKeyRef>(jsonObjectKey, options);

            Assert.NotNull(fromString);
            Assert.Equal("5a13f46386f7741dd7384b04", fromString.Id);
            Assert.Null(fromString.Name);

            Assert.NotNull(fromObject);
            Assert.Equal("5937ee6486f77408994ba448", fromObject.Id);
            Assert.Equal("Machinery key", fromObject.Name);
            Assert.Equal("Machinery", fromObject.ShortName);
        }

        #endregion

        #region Tier 1 & 2: Key Classification & Name Resolution

        [Theory]
        [InlineData("Customs office key", null, true)]
        [InlineData("Health Resort west wing room 306 key", null, true)]
        [InlineData("TerraGroup Labs keycard (Black)", null, true)]
        [InlineData("Factory emergency exit key", "Keys", true)]
        [InlineData("Keycard with Blue Stripe", "Keycards", true)]
        [InlineData("Salewa first aid kit", null, false)]
        [InlineData("Gas analyzer", null, false)]
        [InlineData("Secure Flash drive", null, false)]
        [InlineData("Morphine injector", "Medical", false)]
        [InlineData("", null, false)]
        [InlineData(null, null, false)]
        public void NeededKeysService_IsKey_RecognizesKeysAndKeycards_ExcludesNonKeys(string? name, string? category, bool expected)
        {
            var result = NeededKeysService.IsKey(name, category);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("5a13f46386f7741dd7384b04", null, "Health Resort west wing room 306 key")]
        [InlineData("5937ee6486f77408994ba448", null, "Machinery key")]
        [InlineData("593aa4be86f77457f56379f8", null, "Dorm room 303 key")]
        [InlineData("5c1d0f4986f7744bb01837fa", null, "TerraGroup Labs keycard (Black)")]
        [InlineData("unknown_hex_id", "Fallback Name Key", "Fallback Name Key")]
        [InlineData("unknown_hex_id", null, "unknown_hex_id")]
        public void NeededKeysService_ResolveKeyName_UsesCanonicalDictionaryAndFallback(string keyId, string? fallback, string expected)
        {
            var service = NeededKeysService.Instance;
            var resolved = service.ResolveKeyName(keyId, fallback);
            Assert.Equal(expected, resolved);
        }

        #endregion

        #region Tier 2 & 3: Stash Inventory Resolution & Reactivity

        [Fact]
        public void NeededKeysService_IsKeyInStash_ResolvesByDirectIdCanonicalOrNormalized()
        {
            var service = NeededKeysService.Instance;
            var inventory = ItemInventoryService.Instance;

            // Initially empty
            Assert.False(service.IsKeyInStash("593aa4be86f77457f56379f8", out var initialQty));
            Assert.Equal(0, initialQty);

            // Add key by direct ID to inventory
            inventory.SetNonFirQuantity("593aa4be86f77457f56379f8", 2);

            Assert.True(service.IsKeyInStash("593aa4be86f77457f56379f8", out var qty));
            Assert.Equal(2, qty);

            // Add canonical name key
            inventory.SetFirQuantity("machinery-key", 1);
            Assert.True(service.IsKeyInStash("5937ee6486f77408994ba448", out var canonQty));
            Assert.Equal(1, canonQty);
        }

        [Fact]
        public void NeededKeysService_InventoryChanged_TriggersDataChangedEvent()
        {
            var service = NeededKeysService.Instance;
            var inventory = ItemInventoryService.Instance;
            bool eventFired = false;

            service.DataChanged += () => eventFired = true;

            inventory.SetFirQuantity("5448ba0b4bdc2d02308b456c", 1);

            Assert.True(eventFired, "Modifying ItemInventoryService must trigger NeededKeysService.DataChanged");
        }

        #endregion

        #region Tier 2 & 3: Quest Key Resolution & Aggregation

        [Fact]
        public void NeededKeysService_GetNeededKeysForQuest_FromRequiredItems_IdentifiesKeysAndStashState()
        {
            var service = NeededKeysService.Instance;
            var inventory = ItemInventoryService.Instance;

            var quest = new TarkovTask
            {
                Ids = new List<string> { "quest_delivery_past" },
                Name = "Delivery from the Past",
                NormalizedName = "delivery-from-the-past",
                Maps = new List<string> { "Customs" },
                RequiredItems = new List<QuestItem>
                {
                    new()
                    {
                        ItemDisplayName = "Customs office key",
                        ItemNormalizedName = "customs-office-key",
                        Amount = 1,
                        FoundInRaid = false
                    },
                    new()
                    {
                        ItemDisplayName = "Salewa first aid kit",
                        ItemNormalizedName = "salewa-first-aid-kit",
                        Amount = 3,
                        FoundInRaid = true
                    }
                }
            };

            // Test without key in inventory
            var keysWithoutStash = service.GetNeededKeysForQuest(quest);
            Assert.Single(keysWithoutStash);
            Assert.Equal("Customs office key", keysWithoutStash[0].Name);
            Assert.False(keysWithoutStash[0].InStash);
            Assert.Equal(0, keysWithoutStash[0].StashQuantity);

            // Add key to inventory
            inventory.SetNonFirQuantity("customs-office-key", 1);

            var keysWithStash = service.GetNeededKeysForQuest(quest);
            Assert.Single(keysWithStash);
            Assert.True(keysWithStash[0].InStash);
            Assert.Equal(1, keysWithStash[0].StashQuantity);
        }

        [Fact]
        public void NeededKeysService_GetNeededKeysForQuest_FromTrackerTask_IdentifiesNeededKeysAndAlternatives()
        {
            var service = NeededKeysService.Instance;

            var trackerTask = new TarkovTrackerTask
            {
                Id = "tracker_customs_task",
                Name = "Operation Aquarius - Part 1",
                NormalizedName = "operation-aquarius---part-1",
                NeededKeys = new List<TarkovTrackerNeededKeyGroup>
                {
                    new()
                    {
                        Map = new TarkovTrackerMapRef { Name = "Customs" },
                        Keys = new List<TarkovTrackerKeyRef>
                        {
                            new() { Id = "5780cfba2459777de4559329", Name = "Dorm room 220 key", ShortName = "220" }
                        }
                    }
                },
                Objectives = new List<TarkovTrackerObjective>
                {
                    new()
                    {
                        Description = "Find water in room 206",
                        RequiredKeys = new List<List<TarkovTrackerKeyRef>>
                        {
                            new()
                            {
                                new() { Id = "5780cfea2459777de807b5a2", Name = "Dorm room 206 key", ShortName = "206" },
                                new() { Id = "5780cfba2459777de4559329", Name = "Dorm room 220 key", ShortName = "220" }
                            }
                        }
                    }
                }
            };

            service.RegisterTrackerTask(trackerTask);

            var quest = new TarkovTask
            {
                Ids = new List<string> { "tracker_customs_task" },
                Name = "Operation Aquarius - Part 1",
                NormalizedName = "operation-aquarius---part-1"
            };

            var keys = service.GetNeededKeysForQuest(quest);

            Assert.Equal(2, keys.Count);
            Assert.Contains(keys, k => k.Name == "Dorm room 220 key");
            Assert.Contains(keys, k => k.Name == "Dorm room 206 key");

            var room206 = keys.First(k => k.Name == "Dorm room 206 key");
            Assert.Contains("Dorm room 220 key", room206.AlternativeKeys);
        }

        [Fact]
        public void NeededKeysService_GetNeededKeysForActiveQuests_FiltersByActiveStatus()
        {
            var service = NeededKeysService.Instance;
            var progress = QuestProgressService.Instance;

            var activeQuest = new TarkovTask
            {
                Ids = new List<string> { "active_q" },
                Name = "Active Quest",
                NormalizedName = "active-q",
                RequiredItems = new List<QuestItem>
                {
                    new() { ItemDisplayName = "Machinery key", ItemNormalizedName = "machinery-key", Amount = 1 }
                }
            };

            progress.SetProgressForTesting("active-q", QuestStatus.Active);

            var keys = service.GetNeededKeysForQuest(activeQuest);
            Assert.Single(keys);
            Assert.True(keys[0].IsActiveQuest);
            Assert.False(keys[0].IsUpcomingQuest);

            var lockedQuest = new TarkovTask
            {
                Ids = new List<string> { "locked_q" },
                Name = "Locked Quest",
                NormalizedName = "locked-q",
                TraderRequirements = new List<QuestTraderRequirement>
                {
                    new() { TraderName = "Prapor", RequiredLevel = 4, CompareMethod = ">=" }
                },
                RequiredItems = new List<QuestItem>
                {
                    new() { ItemDisplayName = "Machinery key", ItemNormalizedName = "machinery-key", Amount = 1 }
                }
            };

            var lockedKeys = service.GetNeededKeysForQuest(lockedQuest);
            Assert.Single(lockedKeys);
            Assert.False(lockedKeys[0].IsActiveQuest);
            Assert.True(lockedKeys[0].IsUpcomingQuest);
        }

        #endregion

        #region Tier 2 & 3: Future Mission Items & FIR Requirements

        [Fact]
        public void NeededKeysService_GetFutureMissionItems_CalculatesFirQuantitiesAndShortage()
        {
            var inventory = ItemInventoryService.Instance;

            // Player owns 1 FIR Salewa and 2 non-FIR Salewas (Total = 3)
            inventory.SetFirQuantity("salewa-first-aid-kit", 1);
            inventory.SetNonFirQuantity("salewa-first-aid-kit", 2);

            var fulfillment = inventory.GetFulfillmentInfo("salewa-first-aid-kit", requiredTotal: 3, requiredFir: 3);

            Assert.Equal(3, fulfillment.RequiredTotal);
            Assert.Equal(3, fulfillment.RequiredFir);
            Assert.Equal(1, fulfillment.OwnedFir);
            Assert.Equal(2, fulfillment.OwnedNonFir);
            Assert.Equal(3, fulfillment.OwnedTotal);
            Assert.True(fulfillment.IsTotalFulfilled);
            Assert.False(fulfillment.IsFirFulfilled); // Only 1 of 3 is FIR

            // Test FutureMissionItemInfo shortage and fulfillment under mixed inventory:
            // When 3 FIR Salewas required, stash has 1 FIR + 2 Non-FIR:
            var item = new FutureMissionItemInfo
            {
                ItemName = "Salewa first aid kit",
                ItemNormalizedName = "salewa-first-aid-kit",
                RequiredCount = 3,
                RequiresFir = true,
                OwnedFir = fulfillment.OwnedFir,
                OwnedNonFir = fulfillment.OwnedNonFir
            };

            Assert.Equal(1, item.EffectiveOwned);
            Assert.Equal(2, item.Shortage);
            Assert.False(item.IsFulfilled);

            // When 3 FIR Salewas required, stash has 3 FIR:
            inventory.SetFirQuantity("salewa-first-aid-kit", 3);
            var fulfillmentFulfilled = inventory.GetFulfillmentInfo("salewa-first-aid-kit", requiredTotal: 3, requiredFir: 3);
            item.OwnedFir = fulfillmentFulfilled.OwnedFir;
            item.OwnedNonFir = fulfillmentFulfilled.OwnedNonFir;

            Assert.Equal(3, item.EffectiveOwned);
            Assert.Equal(0, item.Shortage);
            Assert.True(item.IsFulfilled);
        }

        [Fact]
        public void NeededKeysService_FutureMissionItemInfo_NonFirRequirement_UsesTotalOwned()
        {
            var item = new FutureMissionItemInfo
            {
                ItemName = "Spark plug",
                ItemNormalizedName = "spark-plug",
                RequiredCount = 5,
                RequiresFir = false,
                OwnedFir = 1,
                OwnedNonFir = 4
            };

            Assert.Equal(5, item.EffectiveOwned);
            Assert.Equal(0, item.Shortage);
            Assert.True(item.IsFulfilled);
        }

        [Fact]
        public void NeededKeysService_IsKey_WordBoundaryValidation()
        {
            Assert.True(NeededKeysService.IsKey("Factory emergency exit key"));
            Assert.True(NeededKeysService.IsKey("TerraGroup Labs keycard (Black)"));
            Assert.True(NeededKeysService.IsKey("Dorm room 314 marked key"));
            Assert.True(NeededKeysService.IsKey("Gingy keychain"));
            Assert.True(NeededKeysService.IsKey("Arbitrary Key", "Keys"));
            Assert.True(NeededKeysService.IsKey("Arbitrary Card", "Keycards"));
            Assert.False(NeededKeysService.IsKey("Salewa first aid kit"));
            Assert.False(NeededKeysService.IsKey("Gas analyzer"));
            Assert.False(NeededKeysService.IsKey("Monkey wrench"));
            Assert.False(NeededKeysService.IsKey("Turnkey solution"));
            Assert.False(NeededKeysService.IsKey(null));
            Assert.False(NeededKeysService.IsKey(""));
        }

        [Fact]
        public void NeededKeysService_GetFutureMissionItems_FirOnlyFilter_FiltersOutNonFirItems()
        {
            var quest = new TarkovTask
            {
                Name = "Test Mission Items",
                NormalizedName = "test-mission-items",
                RequiredItems = new List<QuestItem>
                {
                    new() { ItemDisplayName = "Car battery", ItemNormalizedName = "car-battery", Amount = 4, FoundInRaid = true },
                    new() { ItemDisplayName = "Spark plug", ItemNormalizedName = "spark-plug", Amount = 8, FoundInRaid = false }
                }
            };

            var firItems = quest.RequiredItems.Where(i => i.FoundInRaid).ToList();
            var allItems = quest.RequiredItems.ToList();

            Assert.Single(firItems);
            Assert.Equal("Car battery", firItems[0].ItemDisplayName);
            Assert.Equal(2, allItems.Count);
        }

        #endregion

        #region Tier 4: ViewModels & Visual Properties

        [Fact]
        public void NeededKeyViewModel_StashBadgeTextAndBrushes_CorrectlyReflectOwnership()
        {
            var missingKey = new NeededKeyViewModel
            {
                KeyId = "test_key",
                Name = "Customs office key",
                InStash = false,
                StashCount = 0
            };

            var singleInStash = new NeededKeyViewModel
            {
                KeyId = "test_key",
                Name = "Customs office key",
                InStash = true,
                StashCount = 1
            };

            var multipleInStash = new NeededKeyViewModel
            {
                KeyId = "test_key",
                Name = "Customs office key",
                InStash = true,
                StashCount = 4
            };

            Assert.Equal("Missing", missingKey.StashStatusText);
            Assert.Equal("✓ In Stash", singleInStash.StashStatusText);
            Assert.Equal("✓ In Stash (x4)", multipleInStash.StashStatusText);

            Assert.IsType<SolidColorBrush>(singleInStash.StashStatusBackground);
            Assert.IsType<SolidColorBrush>(missingKey.StashStatusBackground);
        }

        [Fact]
        public void RequiredItemViewModel_FirAndFulfillmentVisuals_ReflectItemState()
        {
            var itemFirFulfilled = new RequiredItemViewModel
            {
                DisplayText = "Salewa first aid kit x3",
                FoundInRaid = true,
                IsFulfilled = true,
                StashProgressText = "3/3 (3 FIR)"
            };

            var itemUnfulfilled = new RequiredItemViewModel
            {
                DisplayText = "Gas analyzer x2",
                FoundInRaid = false,
                IsFulfilled = false,
                StashProgressText = "0/2"
            };

            Assert.Equal(Visibility.Visible, itemFirFulfilled.FirVisibility);
            Assert.Equal(Visibility.Visible, itemFirFulfilled.FulfilledVisibility);
            Assert.Equal(0.6, itemFirFulfilled.ItemOpacity);
            Assert.NotNull(itemFirFulfilled.TextDecorations);

            Assert.Equal(Visibility.Collapsed, itemUnfulfilled.FirVisibility);
            Assert.Equal(Visibility.Collapsed, itemUnfulfilled.FulfilledVisibility);
            Assert.Equal(1.0, itemUnfulfilled.ItemOpacity);
            Assert.Null(itemUnfulfilled.TextDecorations);
        }

        #endregion

        #region Tier 2: Boundary & Concurrency

        [Fact]
        public void NeededKeysService_HandlesNullQuests_NullRequiredItems_Gracefully()
        {
            var service = NeededKeysService.Instance;

            // Null quest
            var nullResult = service.GetNeededKeysForQuest(null!);
            Assert.NotNull(nullResult);
            Assert.Empty(nullResult);

            // Null RequiredItems
            var questWithNulls = new TarkovTask
            {
                Ids = null,
                Name = null!,
                NormalizedName = null,
                RequiredItems = null
            };
            var emptyResult = service.GetNeededKeysForQuest(questWithNulls);
            Assert.NotNull(emptyResult);
            Assert.Empty(emptyResult);

            // Null registration
            service.RegisterTrackerTask(null!);
            service.RegisterTrackerTasks(null!);

            // Null key name resolution
            var resolvedNull = service.ResolveKeyName(null!, null);
            Assert.Equal(string.Empty, resolvedNull);
        }

        [Fact]
        public void NeededKeysService_ThreadSafety_ConcurrentReadsAndRegistrations()
        {
            var service = NeededKeysService.Instance;
            var exceptions = new List<Exception>();

            Parallel.For(0, 50, i =>
            {
                try
                {
                    service.RegisterTrackerTask(new TarkovTrackerTask
                    {
                        Id = $"concurrent_task_{i}",
                        Name = $"Concurrent Task {i}",
                        NeededKeys = new List<TarkovTrackerNeededKeyGroup>
                        {
                            new()
                            {
                                Keys = new List<TarkovTrackerKeyRef>
                                {
                                    new() { Id = $"key_{i}", Name = $"Key {i}" }
                                }
                            }
                        }
                    });

                    var keys = service.GetNeededKeysForQuest(new TarkovTask
                    {
                        Ids = new List<string> { $"concurrent_task_{i}" },
                        Name = $"Concurrent Task {i}"
                    });
                    Assert.NotNull(keys);
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            Assert.Empty(exceptions);
        }

        #endregion
    }
}
