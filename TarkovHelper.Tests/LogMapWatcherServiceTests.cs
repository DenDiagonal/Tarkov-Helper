using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TarkovHelper.Services.Map;
using Xunit;

namespace TarkovHelper.Tests;

public class LogMapWatcherServiceTests
{
    private readonly LogMapWatcherService _service = LogMapWatcherService.Instance;

    #region Real User Log Extraction Tests

    [Theory]
    [InlineData(
        "2026-10-21 12:18:18.804|1.2.5.0.48146|Info|application|scene preset path:maps/rezerv_base_preset.bundle rcid:Rezerv_Base.scenespreset.asset",
        "Reserve")]
    [InlineData(
        "2026-10-21 12:18:41.674|1.2.5.0.48146|Debug|application|TRACE-NetworkGameCreate profileStatus: 'Profileid: 60dc7d98ab6d900516409b1c, Status: Busy, RaidMode: Online, Ip: 84.17.62.169, Port: 17009, Location: RezervBase, Sid: es-mdr02lgs011_6ad88cf278901e698815fee4_21.10.26_12-59-14, GameMode: deathmatch, shortId: 7ZH7PR'",
        "Reserve")]
    [InlineData(
        "2026-10-21 12:29:59.657|1.2.5.0.48146|Info|application|scene preset path:maps/factory_day_preset.bundle rcid:factory_day.scenespreset.asset",
        "Factory")]
    [InlineData(
        "2026-10-21 12:30:14.160|1.2.5.0.48146|Debug|application|TRACE-NetworkGameCreate profileStatus: 'Profileid: 5ea9b44f3b26ed4f7c69d4eb, Status: Busy, RaidMode: Online, Ip: 79.127.239.76, Port: 17005, Location: factory4_day, Sid: nl-ams04lgs042_6ad88de278901e698815ff94_21.10.26_13-03-14, GameMode: deathmatch, shortId: 7ZH46W'",
        "Factory")]
    [InlineData(
        "2026-10-20 21:04:02.245|1.2.5.0.48146|Debug|application|TRACE-NetworkGameCreate profileStatus: 'Profileid: 5ea9b44f3b26ed4f7c69d4eb, Status: Busy, RaidMode: Online, Ip: 143.244.45.2, Port: 17002, Location: TarkovStreets, Sid: ua-iev02lgs012_6ad7b64278901e69881565a9_20.10.26_21-43-14, GameMode: deathmatch, shortId: 7ZYLTK'",
        "StreetsOfTarkov")]
    [InlineData(
        "2026-10-20 21:27:26.773|1.2.5.0.48146|Info|application|scene preset path:maps/shoreline_preset.bundle rcid:shoreline.scenespreset.asset",
        "Shoreline")]
    [InlineData(
        "2026-10-20 21:28:10.784|1.2.5.0.48146|Debug|application|TRACE-NetworkGameCreate profileStatus: 'Profileid: 5ea9b44f3b26ed4f7c69d4eb, Status: Busy, RaidMode: Online, Ip: 134.119.189.38, Port: 17011, Location: Shoreline, Sid: fr-sbg01lgs064_6ad7c01a78901e6988156ed6_20.10.26_22-25-14, GameMode: deathmatch, shortId: 7Z66NZ'",
        "Shoreline")]
    [InlineData(
        "2026-10-21 19:19:04.748|1.2.5.0.48146|Debug|application|TRACE-NetworkGameCreate profileStatus: 'Profileid: 5ea9b44f3b26ed4f7c69d4eb, Status: Busy, RaidMode: Online, Ip: 169.150.240.221, Port: 17010, Location: Interchange, Sid: ch-zrh01lgs001_6ad8f2aa78901e69881658a3_21.10.26_20-13-14, GameMode: deathmatch, shortId: 7ZSND0'",
        "Interchange")]
    [InlineData(
        "2026-10-20 22:01:04.865|1.2.5.0.48146|Debug|application|TRACE-NetworkGameCreate profileStatus: 'Profileid: 5ea9b44f3b26ed4f7c69d4eb, Status: Busy, RaidMode: Online, Ip: 78.138.127.238, Port: 17000, Location: Interchange, Sid: fr-sbg01lgs059_6ad7c72278901e6988157545_20.10.26_22-55-14, GameMode: deathmatch, shortId: 7Z6S3Y'",
        "Interchange")]
    [InlineData(
        "scene preset path:maps/shopping_mall_preset.bundle rcid:shopping_mall.scenespreset.asset",
        "Interchange")]
    [InlineData(
        "scene preset path:maps/woods_preset.bundle rcid:woods.scenespreset.asset",
        "Woods")]
    [InlineData(
        "Location: Woods, Sid: ...",
        "Woods")]
    [InlineData(
        "scene preset path:maps/customs_preset.bundle rcid:customs.scenespreset.asset",
        "Customs")]
    [InlineData(
        "Location: bigmap, Sid: ...",
        "Customs")]
    [InlineData(
        "scene preset path:maps/lighthouse_preset.bundle rcid:lighthouse.scenespreset.asset",
        "Lighthouse")]
    [InlineData(
        "Location: Lighthouse, Sid: ...",
        "Lighthouse")]
    [InlineData(
        "scene preset path:maps/sandbox_preset.bundle rcid:sandbox.scenespreset.asset",
        "GroundZero")]
    [InlineData(
        "Location: sandbox_high, Sid: ...",
        "GroundZero")]
    [InlineData(
        "scene preset path:maps/laboratory_preset.bundle rcid:laboratory.scenespreset.asset",
        "Labs")]
    [InlineData(
        "Location: laboratory, Sid: ...",
        "Labs")]
    [InlineData(
        "scene preset path:maps/labyrinth_preset.bundle rcid:labyrinth.scenespreset.asset",
        "Labyrinth")]
    [InlineData(
        "scene preset path:maps/terminal_preset.bundle rcid:terminal.scenespreset.asset",
        "Terminal")]
    public void TryExtractMapFromLine_ExtractsCorrectNormalizedMapKey(string logLine, string expectedMapKey)
    {
        var extracted = _service.TryExtractMapFromLine(logLine);
        Assert.Equal(expectedMapKey, extracted);
    }

    #endregion

    #region Normalization Edge Cases

    [Theory]
    [InlineData("rezerv_base", "Reserve")]
    [InlineData("rezerv_base_preset", "Reserve")]
    [InlineData("RezervBase", "Reserve")]
    [InlineData("factory_day", "Factory")]
    [InlineData("factory_day_preset", "Factory")]
    [InlineData("factory4_day", "Factory")]
    [InlineData("factory_night", "Factory")]
    [InlineData("factory4_night", "Factory")]
    [InlineData("city", "StreetsOfTarkov")]
    [InlineData("city_preset", "StreetsOfTarkov")]
    [InlineData("tarkovstreets", "StreetsOfTarkov")]
    [InlineData("shopping_mall", "Interchange")]
    [InlineData("shopping_mall_preset", "Interchange")]
    [InlineData("sandbox", "GroundZero")]
    [InlineData("sandbox_high", "GroundZero")]
    [InlineData("laboratory", "Labs")]
    [InlineData("laboratory_preset", "Labs")]
    [InlineData("bigmap", "Customs")]
    [InlineData("customs_preset", "Customs")]
    public void NormalizeMapName_ResolvesInternalVariants(string rawName, string expectedKey)
    {
        var normalized = _service.NormalizeMapName(rawName);
        Assert.Equal(expectedKey, normalized);
    }

    [Fact]
    public void TryExtractMapFromLine_IrrelevantLines_ReturnNull()
    {
        Assert.Null(_service.TryExtractMapFromLine(""));
        Assert.Null(_service.TryExtractMapFromLine("   "));
        Assert.Null(_service.TryExtractMapFromLine("Game started at 12:00:00"));
        Assert.Null(_service.TryExtractMapFromLine("Player inventory item loaded: 544fb4024bdc2d47078b456b"));
    }

    #endregion

    #region File Processing & Sequential Map Change Flow

    [Fact]
    public void ProcessLogFileChanges_DetectsMapSequence_FiresEvent()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"eft_test_application_{Guid.NewGuid():N}.log");
        try
        {
            string? detectedMap = null;
            EventHandler<MapChangedEventArgs> handler = (s, e) =>
            {
                detectedMap = e.NewMapKey;
            };

            _service.MapChanged += handler;
            try
            {
                // Write Reserve lines
                File.WriteAllLines(tempFile, new[]
                {
                    "2026-10-21 12:18:18.804|1.2.5.0.48146|Info|application|scene preset path:maps/rezerv_base_preset.bundle rcid:Rezerv_Base.scenespreset.asset",
                    "2026-10-21 12:18:41.674|1.2.5.0.48146|Debug|application|TRACE-NetworkGameCreate profileStatus: 'Location: RezervBase, Sid: ...'"
                });

                _service.ProcessLogFileChanges(tempFile);
                Assert.Equal("Reserve", detectedMap);
                Assert.Equal("Reserve", _service.LastDetectedMap);

                // Append Factory lines
                File.AppendAllLines(tempFile, new[]
                {
                    "2026-10-21 12:29:59.657|1.2.5.0.48146|Info|application|scene preset path:maps/factory_day_preset.bundle rcid:factory_day.scenespreset.asset",
                    "2026-10-21 12:30:14.160|1.2.5.0.48146|Debug|application|TRACE-NetworkGameCreate profileStatus: 'Location: factory4_day, Sid: ...'"
                });

                _service.ProcessLogFileChanges(tempFile);
                Assert.Equal("Factory", detectedMap);
                Assert.Equal("Factory", _service.LastDetectedMap);
            }
            finally
            {
                _service.MapChanged -= handler;
            }
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    #endregion

    #region Empirical Real File Verification

    [Fact]
    public void ParseRealUserLogsOnDisk_ReserveAndFactoryExtractedAccurately()
    {
        var realLogPath = @"C:\Battlestate Games\Escape from Tarkov\Logs\log_2026.10.21_12-11-58_1.2.5.0.48146\2026.10.21_12-11-58_1.2.5.0.48146 application_000.log";
        if (!File.Exists(realLogPath))
            return; // Skip on machines without EFT installed

        var detectedMaps = new List<string>();
        foreach (var line in File.ReadLines(realLogPath))
        {
            var map = _service.TryExtractMapFromLine(line);
            if (map != null)
            {
                if (detectedMaps.Count == 0 || detectedMaps[^1] != map)
                {
                    detectedMaps.Add(map);
                }
            }
        }

        // Must have detected Reserve first, then Factory
        Assert.Contains("Reserve", detectedMaps);
        Assert.Contains("Factory", detectedMaps);

        var reserveIdx = detectedMaps.IndexOf("Reserve");
        var factoryIdx = detectedMaps.IndexOf("Factory");
        Assert.True(reserveIdx >= 0 && factoryIdx > reserveIdx, "Reserve must precede Factory in the real log sequence");
    }

    #endregion
}
