using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;
using TarkovHelper.Services;
using TarkovHelper.Services.Map;
using Xunit;

namespace TarkovHelper.Tests;

public class MapMarkerVerificationTests
{
    [Fact]
    public async Task StreetsOfTarkov_ContainsSewerRiverPmcExtraction()
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "tarkov_data.db");
        Assert.True(File.Exists(dbPath), $"Database not found at {dbPath}");

        await using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        await connection.OpenAsync();

        var sql = "SELECT Name, MarkerType, X, Y, Z FROM MapMarkers WHERE MapKey = 'StreetsOfTarkov' AND Name = 'Sewer River'";
        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "Sewer River must exist in MapMarkers for StreetsOfTarkov");
        var name = reader.GetString(0);
        var markerType = reader.GetString(1);
        var x = reader.GetDouble(2);
        var z = reader.GetDouble(4);

        Assert.Equal("Sewer River", name);
        Assert.Equal("PmcExtraction", markerType);
        Assert.True(x < -200, "X coordinate should be around -251.5");
        Assert.True(z > 100, "Z coordinate should be around 205.3");
    }

    [Fact]
    public async Task StreetsOfTarkov_ContainsPinewoodBasementAndKlimovMall()
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "tarkov_data.db");
        Assert.True(File.Exists(dbPath));

        await using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        await connection.OpenAsync();

        var sql = "SELECT Name, MarkerType FROM MapMarkers WHERE MapKey = 'StreetsOfTarkov' AND Name IN ('Pinewood Basement (Co-Op)', 'Klimov Shopping Mall Exfil')";
        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        var found = new System.Collections.Generic.Dictionary<string, string>();
        while (await reader.ReadAsync())
        {
            found[reader.GetString(0)] = reader.GetString(1);
        }

        Assert.True(found.ContainsKey("Pinewood Basement (Co-Op)"));
        Assert.Equal("SharedExtraction", found["Pinewood Basement (Co-Op)"]);

        Assert.True(found.ContainsKey("Klimov Shopping Mall Exfil"));
        Assert.Equal("ScavExtraction", found["Klimov Shopping Mall Exfil"]);
    }

    [Fact]
    public async Task Customs_ContainsRailroadPassageAndZB013AndPowerLever()
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "tarkov_data.db");
        Assert.True(File.Exists(dbPath));

        await using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        await connection.OpenAsync();

        var sql = "SELECT Name, MarkerType FROM MapMarkers WHERE MapKey = 'Customs' AND Name IN ('Railroad Passage', 'ZB-013', 'Boiler Room Basement (Co-Op)', 'ZB-013 power lever')";
        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        var found = new System.Collections.Generic.Dictionary<string, string>();
        while (await reader.ReadAsync())
        {
            found[reader.GetString(0)] = reader.GetString(1);
        }

        Assert.True(found.ContainsKey("Railroad Passage"));
        Assert.Equal("PmcExtraction", found["Railroad Passage"]);

        Assert.True(found.ContainsKey("ZB-013"));
        Assert.Equal("PmcExtraction", found["ZB-013"]);

        Assert.True(found.ContainsKey("Boiler Room Basement (Co-Op)"));
        Assert.Equal("SharedExtraction", found["Boiler Room Basement (Co-Op)"]);

        Assert.True(found.ContainsKey("ZB-013 power lever"));
        Assert.Equal("Lever", found["ZB-013 power lever"]);
    }

    [Fact]
    public async Task Woods_ContainsTransitToReserveAndScavRoadblocks()
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "tarkov_data.db");
        Assert.True(File.Exists(dbPath));

        await using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        await connection.OpenAsync();

        var sql = "SELECT Name, MarkerType FROM MapMarkers WHERE MapKey = 'Woods' AND Name IN ('Transit to Reserve', 'RUAF Roadblock', 'Friendship Bridge (Co-Op)')";
        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        var found = new System.Collections.Generic.Dictionary<string, string>();
        while (await reader.ReadAsync())
        {
            found[reader.GetString(0)] = reader.GetString(1);
        }

        Assert.True(found.ContainsKey("Transit to Reserve"));
        Assert.Equal("Transit", found["Transit to Reserve"]);

        Assert.True(found.ContainsKey("RUAF Roadblock"));
        Assert.Equal("ScavExtraction", found["RUAF Roadblock"]);

        Assert.True(found.ContainsKey("Friendship Bridge (Co-Op)"));
        Assert.Equal("SharedExtraction", found["Friendship Bridge (Co-Op)"]);
    }

    [Fact]
    public async Task Interchange_BossSpawnsAreNamedKilla()
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "tarkov_data.db");
        Assert.True(File.Exists(dbPath));

        await using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        await connection.OpenAsync();

        var sql = "SELECT COUNT(*) FROM MapMarkers WHERE MapKey = 'Interchange' AND MarkerType = 'BossSpawn' AND Name = 'Killa'";
        await using var cmd = new SqliteCommand(sql, connection);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        Assert.Equal(5, count);

        // Verify no placeholder BOSS_SPAWN names remain
        var checkPlaceholderSql = "SELECT COUNT(*) FROM MapMarkers WHERE MapKey = 'Interchange' AND Name LIKE 'BOSS_SPAWN_%'";
        await using var checkCmd = new SqliteCommand(checkPlaceholderSql, connection);
        var placeholderCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

        Assert.Equal(0, placeholderCount);
    }
}
