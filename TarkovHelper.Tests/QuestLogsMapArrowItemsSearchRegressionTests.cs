using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Services;
using TarkovHelper.Services.Map;
using TarkovHelper.ViewModels;
using Xunit;

namespace TarkovHelper.Tests;

public class QuestLogsMapArrowItemsSearchRegressionTests
{
    [Fact]
    public async Task SearchText_SearchBolts_Clear_ThenSearchNuts_UpdatesFilteredCardsWithoutDisposedException()
    {
        var cards = new List<NeededItemCardViewModel>
        {
            new()
            {
                NeedKey = "bolts-1",
                ItemId = "bolts-id",
                ItemDisplayName = "Bolts",
                ItemNormalizedName = "bolts",
                SubtitleName = "Bolts",
                SourceType = "Quest",
                SourceName = "Gunsmith - Part 1",
                Amount = 5,
                CurrentCount = 0
            },
            new()
            {
                NeedKey = "nuts-1",
                ItemId = "nuts-id",
                ItemDisplayName = "Screw nuts",
                ItemNormalizedName = "screw-nuts",
                SubtitleName = "Nuts",
                SourceType = "Quest",
                SourceName = "Gunsmith - Part 1",
                Amount = 5,
                CurrentCount = 0
            }
        };

        var vm = new NeededItemsViewModel
        {
            EnableSearchDebounce = true
        };
        vm.SetCardsForTesting(cards);

        // 1. Search "Bolts"
        vm.SearchText = "Bolts";
        await Task.Delay(200);
        Assert.Single(vm.FilteredCards);
        Assert.Equal("bolts-id", vm.FilteredCards[0].ItemId);

        // 2. Clear search (simulates Backspace to empty or Clear button)
        vm.SearchText = "";
        Assert.Equal(2, vm.FilteredCards.Count);

        // 3. Immediately type "Nuts" character by character
        vm.SearchText = "N";
        vm.SearchText = "Nu";
        vm.SearchText = "Nut";
        vm.SearchText = "Nuts";

        // Even when Application.Current is null in headless xUnit or debounced in WPF,
        // no ObjectDisposedException is thrown and FilteredCards updates to Nuts.
        await Task.Delay(200);
        Assert.Single(vm.FilteredCards);
        Assert.Equal("nuts-id", vm.FilteredCards[0].ItemId);
    }

    [Theory]
    [InlineData("Customs", -2.0, 0.0, 0.0, 2.0, 45.0, 45.0)]
    [InlineData("Labs", 0.0, 10.0, 10.0, 0.0, 90.0, 0.0)]
    [InlineData("Factory", 0.0, -10.0, -10.0, 0.0, 90.0, 180.0)]
    public void TryTransformPlayerPosition_ProjectsYawThroughPlayerMarkerTransformOnce(
        string mapKey, double a, double b, double c, double d, double inputAngle, double expectedScreenAngle)
    {
        var transformer = new MapCoordinateTransformer(new[]
        {
            new MapConfig
            {
                Key = mapKey,
                ImageWidth = 4000,
                ImageHeight = 4000,
                PlayerMarkerTransform = new[] { a, b, c, d, 2000.0, 2000.0 }
            }
        });

        bool ok = transformer.TryTransformPlayerPosition(mapKey, 10.0, 0.0, 20.0, inputAngle, out var screenPos);
        Assert.True(ok);
        Assert.NotNull(screenPos);
        Assert.NotNull(screenPos!.Angle);
        Assert.Equal(expectedScreenAngle, screenPos.Angle!.Value % 360.0, 2);
    }

    [Fact]
    public async Task LogSyncService_LiveOpenFileHandleAppend_DetectsCompletedQuestAndPartialJsonSafely()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_LogSyncLive_" + Guid.NewGuid().ToString("N"));
        string subDir = Path.Combine(tempDir, "log_2026.11.20_7-03-23_1.3.0.2.48779");
        Directory.CreateDirectory(subDir);

        string logFile = Path.Combine(subDir, "2026.11.20_7-03-23_1.3.0.2.48779 push-notifications_000.log");
        string nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        long dtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var detected = new TaskCompletionSource<QuestLogEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, QuestLogEvent e)
        {
            if (e.QuestId == "665eec4a4dfc83b0ed0a9dca" && e.EventType == QuestEventType.Completed)
                detected.TrySetResult(e);
        }

        try
        {
            // Keep an open write handle just like EscapeFromTarkov.exe does during an entire session
            using var eftOpenHandle = new FileStream(logFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(eftOpenHandle, new UTF8Encoding(false)) { AutoFlush = true };

            writer.WriteLine($"{nowStr}|0.16.0.0|Info|push-notifications|Initial header line");

            LogSyncService.Instance.QuestEventDetected += Handler;
            LogSyncService.Instance.StartMonitoring(tempDir);

            await Task.Delay(500);

            // Simulate EFT writing a type 12 quest turn-in notification in two chunks (partial JSON first, then closing brace)
            string part1 = $"{nowStr}|0.16.0.0|Info|push-notifications|Got notification | ChatMessageReceived\n" +
                           "{\n" +
                           "  \"type\": \"new_message\",\n" +
                           "  \"dialogId\": \"54cb57776803fa99248b456e\",\n" +
                           "  \"message\": {\n" +
                           "    \"_id\": \"test_msg_1\",\n" +
                           "    \"type\": 12,\n" +
                           $"    \"dt\": {dtUnix},\n" +
                           "    \"templateId\": \"665eec4a4dfc83b0ed0a9dca successMessageText\"";
            string part2 = "\n  }\n}\n";

            writer.Write(part1);
            await Task.Delay(350);
            writer.Write(part2);

            var completedTask = await Task.WhenAny(detected.Task, Task.Delay(5000));
            Assert.Same(detected.Task, completedTask);

            var evt = await detected.Task;
            Assert.Equal("665eec4a4dfc83b0ed0a9dca", evt.QuestId);
            Assert.Equal(QuestEventType.Completed, evt.EventType);
        }
        finally
        {
            LogSyncService.Instance.QuestEventDetected -= Handler;
            LogSyncService.Instance.StopMonitoring();
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
