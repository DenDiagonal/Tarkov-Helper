using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TarkovHelper.Models;
using TarkovHelper.Pages.Components;
using TarkovHelper.Services;
using Xunit;

namespace TarkovHelper.Tests
{
    [Collection("DatabaseUpdateServiceCollection")]
    public class TraderProgressTests : IDisposable
    {
        private readonly int _originalPlayerLevel;
        private readonly ProfileType _originalProfile;

        public TraderProgressTests()
        {
            _originalPlayerLevel = SettingsService.Instance.PlayerLevel;
            _originalProfile = ProfileService.Instance.CurrentProfile;
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
        }

        public void Dispose()
        {
            SettingsService.Instance.PlayerLevel = _originalPlayerLevel;
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
            QuestProgressService.Instance.LoadProgressAsync().GetAwaiter().GetResult();
        }

        [Fact]
        public async Task LoyaltyLevelOverride_PersistsInUserSettings_AndReloadsAcrossProgressLoad()
        {
            var service = QuestProgressService.Instance;

            // Set override for Peacekeeper to LL3 and Jaeger to LL2
            service.SetTraderLoyaltyLevelOverride("Peacekeeper", 3);
            service.SetTraderLoyaltyLevelOverride("Jaeger", 2);

            Assert.Equal(3, service.GetTraderLoyaltyLevel("Peacekeeper"));
            Assert.Equal(2, service.GetTraderLoyaltyLevel("Jaeger"));

            // Verify persistence in UserSettings table
            var pkSaved = UserDataDbService.Instance.GetSetting("trader.ll.peacekeeper", ProfileService.Instance.CurrentProfile);
            var jgSaved = UserDataDbService.Instance.GetSetting("trader.ll.jaeger", ProfileService.Instance.CurrentProfile);

            Assert.Equal("3", pkSaved);
            Assert.Equal("2", jgSaved);

            // Re-load progress and verify restored
            await service.LoadProgressAsync();

            Assert.Equal(3, service.GetTraderLoyaltyLevel("Peacekeeper"));
            Assert.Equal(2, service.GetTraderLoyaltyLevel("Jaeger"));

            // Clear overrides and verify cleared in DB
            service.ClearTraderLoyaltyLevelOverrides();
            var pkCleared = UserDataDbService.Instance.GetSetting("trader.ll.peacekeeper", ProfileService.Instance.CurrentProfile);
            Assert.Null(pkCleared);
        }

        [Fact]
        public async Task LoyaltyLevelOverride_TriggersProgressChanged_AndUnlocksWeaponsCirculation()
        {
            var service = QuestProgressService.Instance;
            SettingsService.Instance.PlayerLevel = 10; // Peacekeeper natural LL is 1 at level 10

            var weaponsCirculation = new TarkovTask
            {
                Ids = new List<string> { "weapons_circulation_test" },
                Name = "Weapons Circulation",
                NormalizedName = "weapons-circulation",
                Trader = "Peacekeeper",
                RequiredLevel = 10,
                TraderRequirements = new List<QuestTraderRequirement>
                {
                    new()
                    {
                        TraderName = "Peacekeeper",
                        TraderId = "5935c25fb3acc3127c3d8cd9",
                        RequiredLevel = 2,
                        CompareMethod = ">="
                    }
                }
            };

            await service.InitializeAsync(new List<TarkovTask> { weaponsCirculation });

            // Initially, Peacekeeper natural LL is 1 (< 2), so Weapons Circulation must be Locked
            var initialStatus = service.GetStatus(weaponsCirculation);
            Assert.Equal(QuestStatus.Locked, initialStatus);

            // Track ProgressChanged notification
            bool progressChangedFired = false;
            EventHandler handler = (s, e) => progressChangedFired = true;
            service.ProgressChanged += handler;

            try
            {
                // User clicks LL2 on Peacekeeper card
                service.SetTraderLoyaltyLevelOverride("Peacekeeper", 2);

                Assert.True(progressChangedFired, "SetTraderLoyaltyLevelOverride must notify ProgressChanged");
                Assert.Equal(2, service.GetTraderLoyaltyLevel("Peacekeeper"));

                // Weapons Circulation should now be Active!
                var updatedStatus = service.GetStatus(weaponsCirculation);
                Assert.Equal(QuestStatus.Active, updatedStatus);

                // Changing to LL1 locks it again
                service.SetTraderLoyaltyLevelOverride("Peacekeeper", 1);
                Assert.Equal(QuestStatus.Locked, service.GetStatus(weaponsCirculation));
            }
            finally
            {
                service.ProgressChanged -= handler;
                service.ClearTraderLoyaltyLevelOverrides();
            }
        }

        [Fact]
        public async Task TraderUnlockEvaluators_EvaluatePrerequisitesCorrectly()
        {
            var service = QuestProgressService.Instance;

            var friendOfTheWest2 = new TarkovTask
            {
                Ids = new List<string> { "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9GcmllbmRfRnJvbV90aGVfV2VzdF8tX1BhcnRfMg==" },
                Name = "Friend from the West - Part 2",
                NormalizedName = "friend-from-the-west---part-2",
                Trader = "Skier"
            };

            var introTask = new TarkovTask
            {
                Ids = new List<string> { "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9JbnRyb2R1Y3Rpb24=" },
                Name = "Introduction",
                NormalizedName = "introduction",
                Trader = "Mechanic"
            };

            var lightkeeperTask = new TarkovTask
            {
                Ids = new List<string> { "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9HZXR0aW5nX0FjcXVhaW50ZWQ=" },
                Name = "To the Light - Getting Acquainted",
                NormalizedName = "to-the-light---getting-acquainted",
                Trader = "Mechanic"
            };

            var btrTask = new TarkovTask
            {
                Ids = new List<string> { "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9BX0hlbHBpbmdfSGFuZA==" },
                Name = "A Helping Hand",
                NormalizedName = "a-helping-hand",
                Trader = "Mechanic"
            };

            // Build task list of > 50 dummy tasks so count < 50 fallback doesn't trigger
            var tasks = new List<TarkovTask> { friendOfTheWest2, introTask, lightkeeperTask, btrTask };
            for (int i = 0; i < 60; i++)
            {
                tasks.Add(new TarkovTask
                {
                    Ids = new List<string> { $"dummy_task_{i}" },
                    Name = $"Dummy Task {i}",
                    NormalizedName = $"dummy-task-{i}",
                    Trader = "Prapor"
                });
            }

            await service.InitializeAsync(tasks);

            // Isolate in-memory progress for clean testing of initial locked state
            service.ClearProgressForTesting();

            // Initially locked
            Assert.False(service.IsPeacekeeperUnlocked());
            Assert.False(service.IsJaegerUnlocked());
            Assert.False(service.IsLightkeeperUnlocked());
            Assert.False(service.IsBtrDriverUnlocked());

            // Mark Friend from the West - Part 2 as Done
            service.SetProgressForTesting("friend-from-the-west---part-2", QuestStatus.Done);
            Assert.True(service.IsPeacekeeperUnlocked(), "Peacekeeper must unlock when Friend from the West - Part 2 is done");

            // Mark Introduction as Done
            service.SetProgressForTesting("introduction", QuestStatus.Done);
            Assert.True(service.IsJaegerUnlocked(), "Jaeger must unlock when Introduction is done");

            // Mark Lightkeeper quest as Done
            service.SetProgressForTesting("to-the-light---getting-acquainted", QuestStatus.Done);
            Assert.True(service.IsLightkeeperUnlocked(), "Lightkeeper must unlock when Getting Acquainted is done");

            // Mark BTR quest as Done
            service.SetProgressForTesting("a-helping-hand", QuestStatus.Done);
            Assert.True(service.IsBtrDriverUnlocked(), "BTR Driver must unlock when A Helping Hand is done");

            // Restore DB progress
            await service.LoadProgressAsync();
        }

        [Theory]
        [InlineData("54cb50c76803fa8b248b4571", "Prapor")]
        [InlineData("54cb57776803fa99248b456e", "Therapist")]
        [InlineData("579dc571d53a0658a154fbec", "Fence")]
        [InlineData("58330581ace78e27b8b10cee", "Skier")]
        [InlineData("5935c25fb3acc3127c3d8cd9", "Peacekeeper")]
        [InlineData("5a7c2eca46aef81a7ca2145d", "Mechanic")]
        [InlineData("5ac3b934156ae10c4430e83c", "Ragman")]
        [InlineData("5c0647fdd443bc2504c2d371", "Jaeger")]
        [InlineData("6617beeaa9cfa777ca915b7c", "Ref")]
        [InlineData("638f541a29ffd1183d187f57", "Lightkeeper")]
        [InlineData("656f0f98d80a697f855d34b1", "BTR Driver")]
        [InlineData("btr-driver", "BTR Driver")]
        public void ResolveTraderFriendlyName_ResolvesKnownIdsAndAliases(string input, string expected)
        {
            var resolved = QuestProgressService.ResolveTraderFriendlyName(input);
            Assert.Equal(expected, resolved);
        }

        [Fact]
        public void SetTraderLoyaltyLevelOverride_WorksWithCanonicalIdOrName()
        {
            var service = QuestProgressService.Instance;

            // Set using canonical ID
            service.SetTraderLoyaltyLevelOverride("5935c25fb3acc3127c3d8cd9", 4);
            Assert.Equal(4, service.GetTraderLoyaltyLevel("Peacekeeper"));
            Assert.Equal(4, service.GetTraderLoyaltyLevel("5935c25fb3acc3127c3d8cd9"));

            // Set using lowercase name
            service.SetTraderLoyaltyLevelOverride("mechanic", 3);
            Assert.Equal(3, service.GetTraderLoyaltyLevel("Mechanic"));
            Assert.Equal(3, service.GetTraderLoyaltyLevel("5a7c2eca46aef81a7ca2145d"));
        }

        private static void RunInSta(Action action, int timeoutSeconds = 15)
        {
            Exception? caught = null;
            var thread = new Thread(() =>
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
            thread.SetApartmentState(ApartmentState.STA);
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

        [Fact]
        public async Task TraderUnlockEvaluators_BidirectionalToggle_TransitionsBetweenDoneAndLocked_AcrossAllFourTraders()
        {
            var service = QuestProgressService.Instance;

            var friendOfTheWest2 = new TarkovTask
            {
                Ids = new List<string> { "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9GcmllbmRfRnJvbV90aGVfV2VzdF8tX1BhcnRfMg==" },
                Name = "Friend from the West - Part 2",
                NormalizedName = "friend-from-the-west---part-2",
                Trader = "Skier"
            };

            var introTask = new TarkovTask
            {
                Ids = new List<string> { "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9JbnRyb2R1Y3Rpb24=" },
                Name = "Introduction",
                NormalizedName = "introduction",
                Trader = "Mechanic"
            };

            var lightkeeperTask = new TarkovTask
            {
                Ids = new List<string> { "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9HZXR0aW5nX0FjcXVhaW50ZWQ=" },
                Name = "To the Light - Getting Acquainted",
                NormalizedName = "to-the-light---getting-acquainted",
                Trader = "Mechanic"
            };

            var btrTask = new TarkovTask
            {
                Ids = new List<string> { "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9BX0hlbHBpbmdfSGFuZA==" },
                Name = "A Helping Hand",
                NormalizedName = "a-helping-hand",
                Trader = "Mechanic"
            };

            var tasks = new List<TarkovTask> { friendOfTheWest2, introTask, lightkeeperTask, btrTask };
            for (int i = 0; i < 60; i++)
            {
                tasks.Add(new TarkovTask
                {
                    Ids = new List<string> { $"dummy_pk_task_{i}" },
                    Name = $"Dummy PK Task {i}",
                    NormalizedName = $"dummy-pk-task-{i}",
                    Trader = "Prapor"
                });
            }

            await service.InitializeAsync(tasks);
            service.ClearProgressForTesting();

            // 1. Initial State: All 4 unlock-gated traders must be LOCKED
            Assert.False(service.IsPeacekeeperUnlocked(), "Peacekeeper must be locked when Friend from the West - Part 2 is not completed");
            Assert.False(service.IsJaegerUnlocked(), "Jaeger must be locked when Introduction is not completed");
            Assert.False(service.IsLightkeeperUnlocked(), "Lightkeeper must be locked when Getting Acquainted is not completed");
            Assert.False(service.IsBtrDriverUnlocked(), "BTR Driver must be locked when A Helping Hand is not completed");

            // 2. Mark as Done -> All must transition to UNLOCKED
            service.SetProgressForTesting("friend-from-the-west---part-2", QuestStatus.Done);
            service.SetProgressForTesting("introduction", QuestStatus.Done);
            service.SetProgressForTesting("to-the-light---getting-acquainted", QuestStatus.Done);
            service.SetProgressForTesting("a-helping-hand", QuestStatus.Done);

            Assert.True(service.IsPeacekeeperUnlocked(), "Peacekeeper must be unlocked when quest is Done");
            Assert.True(service.IsJaegerUnlocked(), "Jaeger must be unlocked when quest is Done");
            Assert.True(service.IsLightkeeperUnlocked(), "Lightkeeper must be unlocked when quest is Done");
            Assert.True(service.IsBtrDriverUnlocked(), "BTR Driver must be unlocked when quest is Done");

            // 3. Mark as Locked -> All must transition back to LOCKED (bidirectional toggle test)
            service.SetProgressForTesting("friend-from-the-west---part-2", QuestStatus.Locked);
            service.SetProgressForTesting("introduction", QuestStatus.Locked);
            service.SetProgressForTesting("to-the-light---getting-acquainted", QuestStatus.Locked);
            service.SetProgressForTesting("a-helping-hand", QuestStatus.Locked);

            Assert.False(service.IsPeacekeeperUnlocked(), "Peacekeeper must be locked when quest is marked Locked");
            Assert.False(service.IsJaegerUnlocked(), "Jaeger must be locked when quest is marked Locked");
            Assert.False(service.IsLightkeeperUnlocked(), "Lightkeeper must be locked when quest is marked Locked");
            Assert.False(service.IsBtrDriverUnlocked(), "BTR Driver must be locked when quest is marked Locked");

            // 4. Mark as Active -> Still LOCKED (only Done unlocks)
            service.SetProgressForTesting("friend-from-the-west---part-2", QuestStatus.Active);
            service.SetProgressForTesting("introduction", QuestStatus.Active);
            service.SetProgressForTesting("to-the-light---getting-acquainted", QuestStatus.Active);
            service.SetProgressForTesting("a-helping-hand", QuestStatus.Active);

            Assert.False(service.IsPeacekeeperUnlocked(), "Active quest must NOT unlock Peacekeeper");
            Assert.False(service.IsJaegerUnlocked(), "Active quest must NOT unlock Jaeger");
            Assert.False(service.IsLightkeeperUnlocked(), "Active quest must NOT unlock Lightkeeper");
            Assert.False(service.IsBtrDriverUnlocked(), "Active quest must NOT unlock BTR Driver");

            // 5. Mark as Failed / LevelLocked / Unavailable -> Still LOCKED
            service.SetProgressForTesting("friend-from-the-west---part-2", QuestStatus.Failed);
            service.SetProgressForTesting("introduction", QuestStatus.LevelLocked);
            service.SetProgressForTesting("to-the-light---getting-acquainted", QuestStatus.Unavailable);
            service.SetProgressForTesting("a-helping-hand", QuestStatus.Failed);

            Assert.False(service.IsPeacekeeperUnlocked());
            Assert.False(service.IsJaegerUnlocked());
            Assert.False(service.IsLightkeeperUnlocked());
            Assert.False(service.IsBtrDriverUnlocked());

            // 6. Flip back to Done -> UNLOCKED again
            service.SetProgressForTesting("friend-from-the-west---part-2", QuestStatus.Done);
            service.SetProgressForTesting("introduction", QuestStatus.Done);
            service.SetProgressForTesting("to-the-light---getting-acquainted", QuestStatus.Done);
            service.SetProgressForTesting("a-helping-hand", QuestStatus.Done);

            Assert.True(service.IsPeacekeeperUnlocked());
            Assert.True(service.IsJaegerUnlocked());
            Assert.True(service.IsLightkeeperUnlocked());
            Assert.True(service.IsBtrDriverUnlocked());

            // Clean up and restore DB progress
            await service.LoadProgressAsync();
        }

        [Fact]
        public async Task TraderUnlockEvaluators_AlternativeKeysAndAliases_EvaluateConsistently()
        {
            var service = QuestProgressService.Instance;

            // Tasks defined with various naming conventions across EFT datasets
            var friendWestPart1 = new TarkovTask
            {
                Ids = new List<string> { "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9GcmllbmRfRnJvbV90aGVfV2VzdF8tX1BhcnRfMQ==" },
                Name = "Friend From the West",
                NormalizedName = "friend-from-the-west",
                Trader = "Skier"
            };

            var introTask = new TarkovTask
            {
                Ids = new List<string> { "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9JbnRyb2R1Y3Rpb24=" },
                Name = "Introduction",
                NormalizedName = "introduction",
                Trader = "Mechanic"
            };

            var lightkeeperShort = new TarkovTask
            {
                Ids = new List<string> { "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9HZXR0aW5nX0FjcXVhaW50ZWQ=" },
                Name = "Getting Acquainted",
                NormalizedName = "getting-acquainted",
                Trader = "Mechanic"
            };

            var btrTask = new TarkovTask
            {
                Ids = new List<string> { "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9BX0hlbHBpbmdfSGFuZA==" },
                Name = "A Helping Hand",
                NormalizedName = "a-helping-hand",
                Trader = "Mechanic"
            };

            var tasks = new List<TarkovTask> { friendWestPart1, introTask, lightkeeperShort, btrTask };
            for (int i = 0; i < 60; i++)
            {
                tasks.Add(new TarkovTask
                {
                    Ids = new List<string> { $"dummy_alias_task_{i}" },
                    Name = $"Dummy Alias Task {i}",
                    NormalizedName = $"dummy-alias-task-{i}",
                    Trader = "Prapor"
                });
            }

            await service.InitializeAsync(tasks);
            service.ClearProgressForTesting();

            // Test Peacekeeper unlocked via Part 1 / combined "friend-from-the-west"
            service.SetProgressForTesting("friend-from-the-west", QuestStatus.Done);
            Assert.True(service.IsPeacekeeperUnlocked(), "Peacekeeper must unlock via friend-from-the-west normalized name");
            service.SetProgressForTesting("friend-from-the-west", QuestStatus.Locked);
            Assert.False(service.IsPeacekeeperUnlocked());

            // Test Peacekeeper unlocked via Part 1 Base64 ID
            service.SetProgressForTesting("aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9GcmllbmRfRnJvbV90aGVfV2VzdF8tX1BhcnRfMQ==", QuestStatus.Done);
            Assert.True(service.IsPeacekeeperUnlocked(), "Peacekeeper must unlock via Part 1 base64 ID");
            service.SetProgressForTesting("aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9GcmllbmRfRnJvbV90aGVfV2VzdF8tX1BhcnRfMQ==", QuestStatus.Locked);
            Assert.False(service.IsPeacekeeperUnlocked());

            // Test Lightkeeper unlocked via short "getting-acquainted"
            service.SetProgressForTesting("getting-acquainted", QuestStatus.Done);
            Assert.True(service.IsLightkeeperUnlocked(), "Lightkeeper must unlock via getting-acquainted");
            service.SetProgressForTesting("getting-acquainted", QuestStatus.Locked);
            Assert.False(service.IsLightkeeperUnlocked());

            // Test BTR Driver unlocked via Base64 ID
            service.SetProgressForTesting("aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9BX0hlbHBpbmdfSGFuZA==", QuestStatus.Done);
            Assert.True(service.IsBtrDriverUnlocked(), "BTR Driver must unlock via base64 ID");
            service.SetProgressForTesting("aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9BX0hlbHBpbmdfSGFuZA==", QuestStatus.Locked);
            Assert.False(service.IsBtrDriverUnlocked());

            await service.LoadProgressAsync();
        }

        [Fact]
        public async Task TraderTaskCounting_All11Traders_AccuratelyCalculated()
        {
            var service = QuestProgressService.Instance;
            var expectedTraders = new[]
            {
                "Prapor", "Therapist", "Fence", "Skier", "Peacekeeper",
                "Mechanic", "Ragman", "Jaeger", "Ref", "Lightkeeper", "BTR Driver"
            };

            var traderIds = new Dictionary<string, string>
            {
                ["Prapor"] = "54cb50c76803fa8b248b4571",
                ["Therapist"] = "54cb57776803fa99248b456e",
                ["Fence"] = "579dc571d53a0658a154fbec",
                ["Skier"] = "58330581ace78e27b8b10cee",
                ["Peacekeeper"] = "5935c25fb3acc3127c3d8cd9",
                ["Mechanic"] = "5a7c2eca46aef81a7ca2145d",
                ["Ragman"] = "5ac3b934156ae10c4430e83c",
                ["Jaeger"] = "5c0647fdd443bc2504c2d371",
                ["Ref"] = "6617beeaa9cfa777ca915b7c",
                ["Lightkeeper"] = "638f541a29ffd1183d187f57",
                ["BTR Driver"] = "656f0f98d80a697f855d34b1"
            };

            // Build 4 tasks per trader = 44 tasks total:
            // - Task 1: Name = $"{trader} Done Task 1", Trader = trader, Status = Done
            // - Task 2: Name = $"{trader} Done Task 2", Trader = traderId, Status = Done
            // - Task 3: Name = $"{trader} Active Task", Trader = trader.ToLowerInvariant(), Status = Active
            // - Task 4: Name = $"{trader} Locked Task", Trader = trader, Status = Locked
            // Expected for each trader: Total = 4, Done = 2 (50.0%)
            var allTasks = new List<TarkovTask>();
            var doneKeys = new List<string>();
            var activeKeys = new List<string>();
            var lockedKeys = new List<string>();

            foreach (var trader in expectedTraders)
            {
                var id = traderIds[trader];
                var t1 = new TarkovTask
                {
                    Ids = new List<string> { $"{trader.ToLowerInvariant()}_task_1" },
                    Name = $"{trader} Done Task 1",
                    NormalizedName = $"{trader.ToLowerInvariant()}-done-task-1",
                    Trader = trader
                };
                var t2 = new TarkovTask
                {
                    Ids = new List<string> { $"{trader.ToLowerInvariant()}_task_2" },
                    Name = $"{trader} Done Task 2",
                    NormalizedName = $"{trader.ToLowerInvariant()}-done-task-2",
                    Trader = id // Identified by Trader ID
                };
                var t3 = new TarkovTask
                {
                    Ids = new List<string> { $"{trader.ToLowerInvariant()}_task_3" },
                    Name = $"{trader} Active Task",
                    NormalizedName = $"{trader.ToLowerInvariant()}-active-task",
                    Trader = trader.ToLowerInvariant() // Lowercase alias
                };
                var t4 = new TarkovTask
                {
                    Ids = new List<string> { $"{trader.ToLowerInvariant()}_task_4" },
                    Name = $"{trader} Locked Task",
                    NormalizedName = $"{trader.ToLowerInvariant()}-locked-task",
                    Trader = trader
                };

                allTasks.Add(t1);
                allTasks.Add(t2);
                allTasks.Add(t3);
                allTasks.Add(t4);

                doneKeys.Add(t1.NormalizedName);
                doneKeys.Add(t2.NormalizedName);
                activeKeys.Add(t3.NormalizedName);
                lockedKeys.Add(t4.NormalizedName);
            }

            await service.InitializeAsync(allTasks);
            service.ClearProgressForTesting();

            foreach (var k in doneKeys) service.SetProgressForTesting(k, QuestStatus.Done);
            foreach (var k in activeKeys) service.SetProgressForTesting(k, QuestStatus.Active);
            foreach (var k in lockedKeys) service.SetProgressForTesting(k, QuestStatus.Locked);

            RunInSta(() =>
            {
                var panel = new TraderProgressPanel();
                panel.RefreshAllTraders();

                var itemsControl = panel.FindName("TradersItemsControl") as ItemsControl;
                Assert.NotNull(itemsControl);
                var cards = (itemsControl.ItemsSource as IEnumerable<TraderCardViewModel>)?.ToList();
                Assert.NotNull(cards);
                Assert.Equal(11, cards.Count);

                int aggregateTotal = 0;
                int aggregateCompleted = 0;

                foreach (var expectedTrader in expectedTraders)
                {
                    var card = cards.FirstOrDefault(c => string.Equals(c.TraderName, expectedTrader, StringComparison.OrdinalIgnoreCase));
                    Assert.NotNull(card);

                    Assert.Equal(4, card.TotalQuests);
                    Assert.Equal(2, card.CompletedQuests);
                    Assert.Equal(50.0, card.ProgressPercentage, 1);
                    Assert.Equal($"2/4 ({(50.0).ToString("F1", CultureInfo.InvariantCulture)}%)", card.ProgressText);

                    // ProgressBarWidth = (50 / 100) * 195 = 97.5
                    Assert.Equal(97.5, card.ProgressBarWidth, 1);

                    aggregateTotal += card.TotalQuests;
                    aggregateCompleted += card.CompletedQuests;
                }

                Assert.Equal(44, aggregateTotal);
                Assert.Equal(22, aggregateCompleted);

                var txtOverall = panel.FindName("TxtOverallProgress") as TextBlock;
                Assert.NotNull(txtOverall);
                Assert.Equal($"22/44 ({(50.0).ToString("F1", CultureInfo.InvariantCulture)}%)", txtOverall.Text);
            });

            await service.LoadProgressAsync();
        }

        [Fact]
        public async Task TraderTaskCounting_FactionFiltering_ExcludesOpposingFactionQuests()
        {
            var service = QuestProgressService.Instance;
            var originalFaction = SettingsService.Instance.PlayerFaction;

            try
            {
                var tasks = new List<TarkovTask>
                {
                    new()
                    {
                        Ids = new List<string> { "prapor_usec_task" },
                        Name = "Prapor USEC Only",
                        NormalizedName = "prapor-usec-only",
                        Trader = "Prapor",
                        Faction = "USEC"
                    },
                    new()
                    {
                        Ids = new List<string> { "prapor_bear_task" },
                        Name = "Prapor BEAR Only",
                        NormalizedName = "prapor-bear-only",
                        Trader = "Prapor",
                        Faction = "BEAR"
                    },
                    new()
                    {
                        Ids = new List<string> { "prapor_any_task" },
                        Name = "Prapor Shared Any",
                        NormalizedName = "prapor-shared-any",
                        Trader = "Prapor",
                        Faction = "Any"
                    },
                    new()
                    {
                        Ids = new List<string> { "prapor_null_task" },
                        Name = "Prapor General",
                        NormalizedName = "prapor-general",
                        Trader = "Prapor",
                        Faction = null
                    }
                };

                await service.InitializeAsync(tasks);
                service.ClearProgressForTesting();

                // Mark USEC and BEAR tasks Done
                service.SetProgressForTesting("prapor-usec-only", QuestStatus.Done);
                service.SetProgressForTesting("prapor-bear-only", QuestStatus.Done);

                // 1. When Player is USEC: BEAR task is excluded!
                SettingsService.Instance.PlayerFaction = "USEC";

                RunInSta(() =>
                {
                    var panel = new TraderProgressPanel();
                    panel.RefreshAllTraders();

                    var itemsControl = panel.FindName("TradersItemsControl") as ItemsControl;
                    var cards = (itemsControl?.ItemsSource as IEnumerable<TraderCardViewModel>)?.ToList();
                    var praporCard = cards?.FirstOrDefault(c => c.TraderName == "Prapor");
                    Assert.NotNull(praporCard);

                    // 3 eligible tasks: USEC (Done), Any (Not done), General (Not done)
                    Assert.Equal(3, praporCard.TotalQuests);
                    Assert.Equal(1, praporCard.CompletedQuests);
                    Assert.Equal($"1/3 ({(100.0 / 3.0).ToString("F1", CultureInfo.InvariantCulture)}%)", praporCard.ProgressText);
                });

                // 2. When Player is BEAR: USEC task is excluded!
                SettingsService.Instance.PlayerFaction = "BEAR";

                RunInSta(() =>
                {
                    var panel = new TraderProgressPanel();
                    panel.RefreshAllTraders();

                    var itemsControl = panel.FindName("TradersItemsControl") as ItemsControl;
                    var cards = (itemsControl?.ItemsSource as IEnumerable<TraderCardViewModel>)?.ToList();
                    var praporCard = cards?.FirstOrDefault(c => c.TraderName == "Prapor");
                    Assert.NotNull(praporCard);

                    // 3 eligible tasks: BEAR (Done), Any (Not done), General (Not done)
                    Assert.Equal(3, praporCard.TotalQuests);
                    Assert.Equal(1, praporCard.CompletedQuests);
                    Assert.Equal($"1/3 ({(100.0 / 3.0).ToString("F1", CultureInfo.InvariantCulture)}%)", praporCard.ProgressText);
                });
            }
            finally
            {
                SettingsService.Instance.PlayerFaction = originalFaction;
                await service.LoadProgressAsync();
            }
        }

        [Fact]
        public async Task TraderTaskCounting_EdgeCases_ZeroTasks_AndAllCompleted_NoDivisionByZero()
        {
            var service = QuestProgressService.Instance;

            // Initialize with empty tasks list
            await service.InitializeAsync(new List<TarkovTask>());
            service.ClearProgressForTesting();

            RunInSta(() =>
            {
                var panel = new TraderProgressPanel();
                panel.RefreshAllTraders();

                var itemsControl = panel.FindName("TradersItemsControl") as ItemsControl;
                var cards = (itemsControl?.ItemsSource as IEnumerable<TraderCardViewModel>)?.ToList();
                Assert.NotNull(cards);
                Assert.Equal(11, cards.Count);

                foreach (var card in cards)
                {
                    Assert.Equal(0, card.TotalQuests);
                    Assert.Equal(0, card.CompletedQuests);
                    Assert.Equal(0.0, card.ProgressPercentage);
                    Assert.Equal($"0/0 ({(0.0).ToString("F1", CultureInfo.InvariantCulture)}%)", card.ProgressText);
                    Assert.Equal(0.0, card.ProgressBarWidth);
                }

                var txtOverall = panel.FindName("TxtOverallProgress") as TextBlock;
                Assert.NotNull(txtOverall);
                Assert.Equal($"0/0 ({(0.0).ToString("F1", CultureInfo.InvariantCulture)}%)", txtOverall.Text);
            });

            // Initialize with all completed tasks for Prapor
            var praporTasks = new List<TarkovTask>();
            for (int i = 0; i < 10; i++)
            {
                praporTasks.Add(new TarkovTask
                {
                    Ids = new List<string> { $"all_done_task_{i}" },
                    Name = $"All Done Task {i}",
                    NormalizedName = $"all-done-task-{i}",
                    Trader = "Prapor"
                });
            }

            await service.InitializeAsync(praporTasks);
            foreach (var t in praporTasks) service.SetProgressForTesting(t.NormalizedName ?? t.Name, QuestStatus.Done);

            RunInSta(() =>
            {
                var panel = new TraderProgressPanel();
                panel.RefreshAllTraders();

                var itemsControl = panel.FindName("TradersItemsControl") as ItemsControl;
                var cards = (itemsControl?.ItemsSource as IEnumerable<TraderCardViewModel>)?.ToList();
                var prapor = cards?.FirstOrDefault(c => c.TraderName == "Prapor");
                Assert.NotNull(prapor);

                Assert.Equal(10, prapor.TotalQuests);
                Assert.Equal(10, prapor.CompletedQuests);
                Assert.Equal(100.0, prapor.ProgressPercentage);
                Assert.Equal($"10/10 ({(100.0).ToString("F1", CultureInfo.InvariantCulture)}%)", prapor.ProgressText);
                Assert.Equal(195.0, prapor.ProgressBarWidth);
            });

            await service.LoadProgressAsync();
        }

        [Fact]
        public async Task TraderProgressPanel_WpfUI_UnlockBadges_ReflectDynamicUnlockState()
        {
            var service = QuestProgressService.Instance;

            var friendOfTheWest2 = new TarkovTask
            {
                Ids = new List<string> { "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9GcmllbmRfRnJvbV90aGVfV2VzdF8tX1BhcnRfMg==" },
                Name = "Friend from the West - Part 2",
                NormalizedName = "friend-from-the-west---part-2",
                Trader = "Skier"
            };

            var introTask = new TarkovTask
            {
                Ids = new List<string> { "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9JbnRyb2R1Y3Rpb24=" },
                Name = "Introduction",
                NormalizedName = "introduction",
                Trader = "Mechanic"
            };

            var lightkeeperTask = new TarkovTask
            {
                Ids = new List<string> { "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9HZXR0aW5nX0FjcXVhaW50ZWQ=" },
                Name = "To the Light - Getting Acquainted",
                NormalizedName = "to-the-light---getting-acquainted",
                Trader = "Mechanic"
            };

            var btrTask = new TarkovTask
            {
                Ids = new List<string> { "aHR0cHM6Ly9lc2NhcGVmcm9tdGFya292LmZhbmRvbS5jb20vd2lraS9BX0hlbHBpbmdfSGFuZA==" },
                Name = "A Helping Hand",
                NormalizedName = "a-helping-hand",
                Trader = "Mechanic"
            };

            var tasks = new List<TarkovTask> { friendOfTheWest2, introTask, lightkeeperTask, btrTask };
            for (int i = 0; i < 60; i++)
            {
                tasks.Add(new TarkovTask
                {
                    Ids = new List<string> { $"dummy_ui_task_{i}" },
                    Name = $"Dummy UI Task {i}",
                    NormalizedName = $"dummy-ui-task-{i}",
                    Trader = "Prapor"
                });
            }

            await service.InitializeAsync(tasks);
            service.ClearProgressForTesting();

            RunInSta(() =>
            {
                var panel = new TraderProgressPanel();
                panel.RefreshAllTraders();

                var itemsControl = panel.FindName("TradersItemsControl") as ItemsControl;
                var cards = (itemsControl?.ItemsSource as IEnumerable<TraderCardViewModel>)?.ToList();
                Assert.NotNull(cards);
                Assert.Equal(11, cards.Count);

                // Check always-unlocked traders: Prapor, Therapist, Fence, Skier, Mechanic, Ragman, Ref
                var alwaysUnlockedNames = new[] { "Prapor", "Therapist", "Fence", "Skier", "Mechanic", "Ragman", "Ref" };
                foreach (var name in alwaysUnlockedNames)
                {
                    var card = cards.First(c => c.TraderName == name);
                    Assert.True(card.IsUnlocked);
                    Assert.False(card.HasUnlockRequirement);
                    Assert.Equal(Visibility.Collapsed, card.UnlockBadgeVisibility);
                }

                // Check 4 unlock-gated traders: Initially all LOCKED
                var pk = cards.First(c => c.TraderName == "Peacekeeper");
                Assert.False(pk.IsUnlocked);
                Assert.True(pk.HasUnlockRequirement);
                Assert.Equal(Visibility.Visible, pk.UnlockBadgeVisibility);
                Assert.Equal("🔒 Friend from the West - Part 2", pk.UnlockBadgeText);

                var jg = cards.First(c => c.TraderName == "Jaeger");
                Assert.False(jg.IsUnlocked);
                Assert.True(jg.HasUnlockRequirement);
                Assert.Equal(Visibility.Visible, jg.UnlockBadgeVisibility);
                Assert.Equal("🔒 Introduction", jg.UnlockBadgeText);

                var lk = cards.First(c => c.TraderName == "Lightkeeper");
                Assert.False(lk.IsUnlocked);
                Assert.True(lk.HasUnlockRequirement);
                Assert.Equal(Visibility.Visible, lk.UnlockBadgeVisibility);
                Assert.Equal("🔒 To the Light - Getting Acquainted", lk.UnlockBadgeText);

                var btr = cards.First(c => c.TraderName == "BTR Driver");
                Assert.False(btr.IsUnlocked);
                Assert.True(btr.HasUnlockRequirement);
                Assert.Equal(Visibility.Visible, btr.UnlockBadgeVisibility);
                Assert.Equal("🔒 A Helping Hand", btr.UnlockBadgeText);

                // Mark Introduction Done -> Jaeger should transition to Unlocked
                service.SetProgressForTesting("introduction", QuestStatus.Done);
                panel.RefreshAllTraders();

                Assert.True(jg.IsUnlocked);
                Assert.Equal("✓ Unlocked", jg.UnlockBadgeText);

                // Mark Introduction Locked again -> Jaeger reverts to Locked
                service.SetProgressForTesting("introduction", QuestStatus.Locked);
                panel.RefreshAllTraders();

                Assert.False(jg.IsUnlocked);
                Assert.Equal("🔒 Introduction", jg.UnlockBadgeText);
            });

            await service.LoadProgressAsync();
        }

        [Fact]
        public async Task QuestGating_LightkeeperQuestsLockedUntilTraderUnlocked()
        {
            var service = QuestProgressService.Instance;
            var tasks = new List<TarkovTask>();

            for (int i = 0; i < 55; i++)
            {
                tasks.Add(new TarkovTask
                {
                    Id = $"prapor_task_{i}",
                    Ids = new List<string> { $"prapor_task_{i}" },
                    Name = $"Prapor Task {i}",
                    Trader = "Prapor"
                });
            }

            var gettingAcquainted = new TarkovTask
            {
                Id = "625d700cc48e6c62a440fab5",
                Ids = new List<string> { "625d700cc48e6c62a440fab5" },
                Name = "Getting Acquainted",
                NormalizedName = "getting-acquainted",
                Trader = "Mechanic"
            };
            tasks.Add(gettingAcquainted);

            var infoSource = new TarkovTask
            {
                Id = "63966faeea19ac7ed845db2c",
                Ids = new List<string> { "63966faeea19ac7ed845db2c" },
                Name = "Information Source",
                NormalizedName = "information-source",
                Trader = "Lightkeeper",
                RequiredLevel = 1
            };
            tasks.Add(infoSource);

            await service.InitializeAsync(tasks);
            service.ClearProgressForTesting();

            // 1. Initially, Getting Acquainted is not done -> Lightkeeper is locked
            Assert.False(service.IsLightkeeperUnlocked());
            Assert.Equal(QuestStatus.Locked, service.GetStatus(infoSource));

            // 2. Complete Getting Acquainted -> Lightkeeper unlocked -> Information Source becomes Active
            service.SetProgressForTesting("625d700cc48e6c62a440fab5", QuestStatus.Done);
            Assert.True(service.IsLightkeeperUnlocked());
            Assert.Equal(QuestStatus.Active, service.GetStatus(infoSource));

            // 3. Mark Getting Acquainted as Locked again -> Information Source returns to Locked
            service.SetProgressForTesting("625d700cc48e6c62a440fab5", QuestStatus.Locked);
            Assert.False(service.IsLightkeeperUnlocked());
            Assert.Equal(QuestStatus.Locked, service.GetStatus(infoSource));

            await service.LoadProgressAsync();
        }
    }
}
