using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using TarkovHelper.Models;
using TarkovHelper.Pages;
using TarkovHelper.Services;
using Xunit;

namespace TarkovHelper.Tests.Services
{
    [Collection("DatabaseUpdateServiceCollection")]
    public class StoryChaptersBlock3Tests
    {
        [Fact]
        public void Test_LocalizationService_StoryTrader_AllLanguages()
        {
            var loc = LocalizationService.Instance;
            var initialLang = loc.CurrentLanguage;

            try
            {
                // Spanish (ES)
                loc.CurrentLanguage = AppLanguage.ES;
                Assert.Equal("Capítulos de Historia", loc.GetLocalizedTraderName("Story"));
                Assert.Equal("Capítulos de Historia", loc.GetLocalizedTraderName("Story Chapters"));
                Assert.Equal("Capítulos de Historia", loc.GetLocalizedTraderName("story"));
                Assert.Equal("Historia", loc.GetLocalizedTraderShortName("Story"));
                Assert.Equal("Historia", loc.StoryTabTitle);
                Assert.Equal("Ignorar prerequisito / Marcar disponible", loc.OverridePrerequisite);
                Assert.Equal("Anulación activa (Restaurar)", loc.OverrideActiveRestore);
                Assert.Equal("Quitar anulación manual", loc.RemoveManualOverride);
                Assert.Equal("Alternar completado (1 clic)", loc.ToggleCompletedTooltip);
                Assert.False(string.IsNullOrEmpty(loc.UnresolvedPrerequisiteWarning));

                // English (EN)
                loc.CurrentLanguage = AppLanguage.EN;
                Assert.Equal("Story Chapters", loc.GetLocalizedTraderName("Story"));
                Assert.Equal("Story Chapters", loc.GetLocalizedTraderName("Story Chapters"));
                Assert.Equal("Story", loc.GetLocalizedTraderShortName("Story"));
                Assert.Equal("Story", loc.StoryTabTitle);
                Assert.Equal("Ignore prerequisite / Mark available", loc.OverridePrerequisite);
                Assert.Equal("Override Active (Restore)", loc.OverrideActiveRestore);
                Assert.Equal("Override Active (Re-lock)", loc.RemoveManualOverride);
                Assert.Equal("Toggle completed (1-click)", loc.ToggleCompletedTooltip);

                // Korean (KO)
                loc.CurrentLanguage = AppLanguage.KO;
                Assert.Equal("스토리 챕터", loc.GetLocalizedTraderName("Story"));
                Assert.Equal("스토리", loc.GetLocalizedTraderShortName("Story"));
                Assert.Equal("스토리", loc.StoryTabTitle);
                Assert.Equal("선행 조건 무시 / 활성화", loc.OverridePrerequisite);

                // Japanese (JA)
                loc.CurrentLanguage = AppLanguage.JA;
                Assert.Equal("ストーリーチャプター", loc.GetLocalizedTraderName("Story"));
                Assert.Equal("ストーリー", loc.GetLocalizedTraderShortName("Story"));
                Assert.Equal("ストーリー", loc.StoryTabTitle);
                Assert.Equal("前提条件を無視 / 有効化", loc.OverridePrerequisite);
            }
            finally
            {
                loc.CurrentLanguage = initialLang;
            }
        }

        [Fact]
        public void Test_QuestViewModel_StoryAndOverrideProperties()
        {
            var initialLang = LocalizationService.Instance.CurrentLanguage;
            LocalizationService.Instance.CurrentLanguage = AppLanguage.EN;

            try
            {
                var storyTask = new TarkovTask
                {
                    Ids = new List<string> { "story_ch1_test" },
                    Name = "Chapter 1: The Beginning",
                    NormalizedName = "chapter_1_the_beginning",
                    Trader = "Story"
                };

                var vm = new QuestViewModel
                {
                    Task = storyTask,
                    DisplayName = storyTask.Name,
                    Status = QuestStatus.Active,
                    IsManualOverride = false
                };

                // Story detection
                Assert.True(vm.IsStoryChapter);
                Assert.Equal(Visibility.Visible, vm.StoryBadgeVisibility);
                Assert.Equal(Visibility.Visible, vm.StoryToggleVisibility);
                Assert.False(vm.IsDone);
                Assert.Equal("○ Pending", vm.StoryToggleText);
                Assert.Equal(Visibility.Collapsed, vm.OverrideBadgeVisibility);

                // Toggle to Done
                vm.Status = QuestStatus.Done;
                Assert.True(vm.IsDone);
                Assert.Equal("✓ Done", vm.StoryToggleText);

                // Manual override toggling
                vm.IsManualOverride = true;
                Assert.Equal(Visibility.Visible, vm.OverrideBadgeVisibility);

                vm.IsManualOverride = false;
                Assert.Equal(Visibility.Collapsed, vm.OverrideBadgeVisibility);
            }
            finally
            {
                LocalizationService.Instance.CurrentLanguage = initialLang;
            }
        }

        [Fact]
        public void Test_QuestViewModel_NonStoryQuest_HidesStoryBadgeAndToggle()
        {
            var regularTask = new TarkovTask
            {
                Ids = new List<string> { "debut_test" },
                Name = "Debut",
                NormalizedName = "debut",
                Trader = "Prapor"
            };

            var vm = new QuestViewModel
            {
                Task = regularTask,
                DisplayName = regularTask.Name,
                Status = QuestStatus.Active,
                IsManualOverride = false
            };

            Assert.False(vm.IsStoryChapter);
            Assert.Equal(Visibility.Collapsed, vm.StoryBadgeVisibility);
            Assert.Equal(Visibility.Collapsed, vm.StoryToggleVisibility);
        }

        [Fact]
        public void Test_StoryChapter_DecoupledToggleBehavior()
        {
            var service = QuestProgressService.Instance;

            var ch1 = new TarkovTask
            {
                Ids = new List<string> { "story_ch1_isolated" },
                Name = "Story Chapter 1",
                NormalizedName = "story_ch1_isolated",
                Trader = "Story"
            };

            var ch2 = new TarkovTask
            {
                Ids = new List<string> { "story_ch2_isolated" },
                Name = "Story Chapter 2",
                NormalizedName = "story_ch2_isolated",
                Trader = "Story",
                TaskRequirements = new List<TaskRequirement>
                {
                    new TaskRequirement
                    {
                        TaskId = "story_ch1_isolated",
                        TaskNormalizedName = "story_ch1_isolated",
                        Status = new List<string> { "complete" }
                    }
                }
            };

            // Decoupled story chapters are Active even when prereqs are not completed
            var statusBefore = service.GetStatus(ch2);
            Assert.Equal(QuestStatus.Active, statusBefore);

            // 1-Click Toggle: Complete Chapter 2 without completing Chapter 1
            service.CompleteQuest(ch2, completePrerequisites: false);
            Assert.Equal(QuestStatus.Done, service.GetStatus(ch2));
            Assert.NotEqual(QuestStatus.Done, service.GetStatus(ch1));

            // Reset Chapter 2
            service.ResetQuest(ch2);
            Assert.Equal(QuestStatus.Active, service.GetStatus(ch2));
        }
    }
}
