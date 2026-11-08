using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TarkovHelper.Models;
using TarkovHelper.Services;
using Xunit;

namespace TarkovHelper.Tests
{
    [Collection("DatabaseUpdateServiceCollection")]
    public class TraderUnlockPrerequisiteTests : IDisposable
    {
        private readonly ProfileType _originalProfile;

        public TraderUnlockPrerequisiteTests()
        {
            _originalProfile = ProfileService.Instance.CurrentProfile;
        }

        public void Dispose()
        {
            QuestProgressService.Instance.ClearProgressForTesting();
            QuestProgressService.Instance.ClearTraderLoyaltyLevelOverrides();
        }

        [Fact]
        public async Task QuestGraphService_JaegerQuests_HaveIntroductionAsPrerequisite()
        {
            await QuestDbService.Instance.LoadQuestsAsync();
            var allQuests = QuestDbService.Instance.AllQuests.ToList();
            QuestGraphService.Instance.Initialize(allQuests);

            var acq = allQuests.FirstOrDefault(q => q.Name == "Acquaintance");
            Assert.NotNull(acq);

            var prereqs = QuestGraphService.Instance.GetAllPrerequisites(acq.NormalizedName ?? "acquaintance");
            Assert.Contains(prereqs, p => p.Name == "Introduction" || p.NormalizedName == "introduction");
        }

        [Fact]
        public async Task QuestGraphService_Introduction_LeadsTo_JaegerStarterQuests()
        {
            await QuestDbService.Instance.LoadQuestsAsync();
            var allQuests = QuestDbService.Instance.AllQuests.ToList();
            QuestGraphService.Instance.Initialize(allQuests);

            var intro = allQuests.FirstOrDefault(q => q.Name == "Introduction");
            Assert.NotNull(intro);
            Assert.NotNull(intro.LeadsTo);
            Assert.Contains(intro.LeadsTo, l => l.Equals("acquaintance", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task IsJaegerUnlocked_WhenAcquaintanceIsCompleted_ReturnsTrue()
        {
            await QuestDbService.Instance.LoadQuestsAsync();
            var allQuests = QuestDbService.Instance.AllQuests.ToList();
            var service = QuestProgressService.Instance;
            await service.InitializeAsync(allQuests);
            service.ClearProgressForTesting();

            var acq = allQuests.FirstOrDefault(q => q.Name == "Acquaintance");
            Assert.NotNull(acq);

            // Mark Acquaintance as Done without marking Introduction
            await service.SetStatusAsync(acq, QuestStatus.Done);

            Assert.True(service.IsJaegerUnlocked(), "Jaeger must be unlocked when player has completed Acquaintance!");
        }

        [Fact]
        public async Task LogSync_CompletingAcquaintance_AutoCompletesIntroductionAsPrerequisite()
        {
            await QuestDbService.Instance.LoadQuestsAsync();
            var allQuests = QuestDbService.Instance.AllQuests.ToList();
            var service = QuestProgressService.Instance;
            await service.InitializeAsync(allQuests);
            QuestGraphService.Instance.Initialize(allQuests);
            service.ClearProgressForTesting();

            var logSync = LogSyncService.Instance;

            // Create temporary push-notifications log file with Acquaintance completed event (QuestStatus = 2)
            var tempDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_TraderUnlockTest_" + Guid.NewGuid().ToString("N"));
            var subDir = Path.Combine(tempDir, "log_2026.10.28_00-00-00_test");
            Directory.CreateDirectory(subDir);
            var logPath = Path.Combine(subDir, "2026.10.28_push-notifications_000.log");

            // Event line for Acquaintance completed:
            // Acquaintance ID: 5d24b81486f77439c92d6ba8
            var logJson = "{\n  \"type\": \"new_message\",\n  \"dialogId\": \"5c0647fdd443bc2504c2d371\",\n  \"message\": {\n    \"type\": 12,\n    \"templateId\": \"5d24b81486f77439c92d6ba8 description\",\n    \"dt\": 1730000000\n  }\n}\n";
            await File.WriteAllTextAsync(logPath, logJson);

            try
            {
                var syncResult = await logSync.SyncFromLogsAsync(tempDir);
                Assert.NotNull(syncResult);
                Assert.Contains(syncResult.QuestsToComplete, q => q.NormalizedName == "acquaintance");
                Assert.Contains(syncResult.QuestsToComplete, q => q.NormalizedName == "introduction" && q.IsPrerequisite);

                // Apply changes
                await logSync.ApplyQuestChangesAsync(syncResult.QuestsToComplete);

                var introTask = service.GetTask("introduction");
                Assert.NotNull(introTask);
                Assert.Equal(QuestStatus.Done, service.GetStatus(introTask));
                Assert.True(service.IsJaegerUnlocked());
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}
