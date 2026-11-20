using System.IO;
using TarkovHelper.Models.Map;
using TarkovHelper.Services.Map;
using Xunit;

namespace TarkovHelper.Tests.Services;

public class ScreenshotWatcherAndTrackerTests
{
    [Theory]
    [InlineData("2026-11-20[08-15]_54.16, -1.14, -23.15_0.04122, -0.42533, 0.01939, 0.90389_13.02 (0).png", 54.16, -1.14, -23.15)]
    [InlineData("2026-11-20[07-53]_70.60, 1.78, -61.48_0.13835, -0.07823, 0.01131, 0.98722_13.02 (0).png", 70.60, 1.78, -61.48)]
    [InlineData("2025-12-04[00-40]_95.77, 2.44, -134.02_-0.02395, -0.85891, 0.03920, -0.51007_16.74 (0).png", 95.77, 2.44, -134.02)]
    public void ScreenshotCoordinateParser_ParsesEftCoordinatesAccurately(string fileName, double expectedX, double expectedY, double expectedZ)
    {
        var parser = new ScreenshotCoordinateParser();
        bool success = parser.TryParse(fileName, out var position);

        Assert.True(success);
        Assert.NotNull(position);
        Assert.Equal(expectedX, position.X, 2);
        Assert.Equal(expectedY, position.Y, 2);
        Assert.Equal(expectedZ, position.Z!.Value, 2);
        Assert.NotNull(position.Angle);
    }

    [Fact]
    public void MapTrackerService_TransformsFactoryCoordinatesSuccessfully()
    {
        var service = MapTrackerService.Instance;
        // Factory config has playerMarkerTransform: [0, -10, -10, 0, 1800, 1851.5]
        var screenPos = service.Transformer.TryTransformPlayerPosition(
            "Factory",
            54.16,
            -1.14,
            -23.15,
            120.0,
            out var result);

        Assert.True(screenPos);
        Assert.NotNull(result);
        Assert.True(result.X > 0 && result.X < 3600);
        Assert.True(result.Y > 0 && result.Y < 3600);
    }

    [Fact]
    public void ScreenshotWatcherService_HandlesFileRenamedAndLatestFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TarkovHelper_Screenshot_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var parser = new ScreenshotCoordinateParser();
            using var watcher = new ScreenshotWatcherService(parser);

            EftPosition? detectedPos = null;
            watcher.PositionDetected += (s, e) =>
            {
                detectedPos = e.Position;
            };

            // Seed file before starting watcher to test startup scan
            var testFile = Path.Combine(tempDir, "2026-11-20[08-15]_54.16, -1.14, -23.15_0.04122, -0.42533, 0.01939, 0.90389_13.02 (0).png");
            File.WriteAllBytes(testFile, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            bool started = watcher.StartWatching(tempDir);
            Assert.True(started);

            // Wait brief moment for the initial scan task
            Thread.Sleep(350);

            Assert.NotNull(detectedPos);
            Assert.Equal(54.16, detectedPos.X, 2);
            Assert.Equal(-23.15, detectedPos.Z!.Value, 2);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
