using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services;

/// <summary>
/// Service responsible for verifying and applying schema upgrades and migration backfills to tarkov_data.db.
/// Ensures support for true factory restoration, audit logging, and customization flags.
/// </summary>
public sealed class DatabaseBootstrapService
{
    private static readonly ILogger _log = Log.For<DatabaseBootstrapService>();
    private static DatabaseBootstrapService? _instance;
    public static DatabaseBootstrapService Instance => _instance ??= new DatabaseBootstrapService();

    private readonly object _lock = new();

    private DatabaseBootstrapService() { }

    /// <summary>
    /// Ensures that QuestObjectives has OriginalLocationPoints and IsCustomized columns,
    /// preserves initial factory coordinates, and creates the QuestObjectiveRelocationLog audit table.
    /// Strictly protects against modifying user_data.db.
    /// </summary>
    public async Task EnsureDatabaseSchemaAsync(string? targetDbPath = null)
    {
        var dbPath = targetDbPath ?? DatabaseUpdateService.Instance.DatabasePath;
        if (string.IsNullOrWhiteSpace(dbPath))
            return;

        ValidateTargetDatabasePath(dbPath);

        if (!File.Exists(dbPath))
            return;

        try
        {
            var connectionString = $"Data Source={dbPath};Mode=ReadWrite";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // 1. Ensure QuestObjectives schema migrations if table exists
            if (await TableExistsAsync(connection, "QuestObjectives"))
            {
                // 2. Add OriginalLocationPoints column if missing
                if (!await ColumnExistsAsync(connection, "QuestObjectives", "OriginalLocationPoints"))
                {
                    _log.Info("Adding column OriginalLocationPoints to QuestObjectives...");
                    var alterSql = "ALTER TABLE QuestObjectives ADD COLUMN OriginalLocationPoints TEXT;";
                    await using var cmd = new SqliteCommand(alterSql, connection);
                    await cmd.ExecuteNonQueryAsync();
                }

                // 3. Add IsCustomized column if missing
                if (!await ColumnExistsAsync(connection, "QuestObjectives", "IsCustomized"))
                {
                    _log.Info("Adding column IsCustomized to QuestObjectives...");
                    var alterSql = "ALTER TABLE QuestObjectives ADD COLUMN IsCustomized INTEGER NOT NULL DEFAULT 0;";
                    await using var cmd = new SqliteCommand(alterSql, connection);
                    await cmd.ExecuteNonQueryAsync();
                }

                // 4. Backfill OriginalLocationPoints for existing native records that have LocationPoints
                // but have not yet been marked as customized
                var backfillSql = @"
                    UPDATE QuestObjectives
                    SET OriginalLocationPoints = LocationPoints
                    WHERE OriginalLocationPoints IS NULL
                      AND LocationPoints IS NOT NULL
                      AND LocationPoints != ''
                      AND IsCustomized = 0;";
                await using (var cmd = new SqliteCommand(backfillSql, connection))
                {
                    var rowsBackfilled = await cmd.ExecuteNonQueryAsync();
                    if (rowsBackfilled > 0)
                    {
                        _log.Info($"Backfilled {rowsBackfilled} original factory coordinates in QuestObjectives.");
                    }
                }

                // 5. Create QuestObjectiveRelocationLog audit table
                var createLogSql = @"
                    CREATE TABLE IF NOT EXISTS QuestObjectiveRelocationLog (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ObjectiveId TEXT NOT NULL,
                        QuestName TEXT,
                        ObjectiveDescription TEXT,
                        MapName TEXT,
                        PreviousLocationPoints TEXT,
                        NewLocationPoints TEXT,
                        Action TEXT NOT NULL,
                        Timestamp TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_reloclog_objid ON QuestObjectiveRelocationLog(ObjectiveId);";
                await using (var cmd = new SqliteCommand(createLogSql, connection))
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // 6. Create QuestTraderRequirements table & indices
            var createTraderReqSql = @"
                CREATE TABLE IF NOT EXISTS QuestTraderRequirements (
                    Id TEXT PRIMARY KEY,
                    QuestId TEXT NOT NULL,
                    TraderId TEXT,
                    TraderName TEXT NOT NULL,
                    RequirementType TEXT NOT NULL DEFAULT 'Level',
                    CompareMethod TEXT NOT NULL DEFAULT '>=',
                    RequiredLevel INTEGER NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    IsApproved INTEGER NOT NULL DEFAULT 1,
                    ApprovedAt TEXT,
                    UpdatedAt TEXT,
                    FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS idx_quest_trader_req_quest_id ON QuestTraderRequirements(QuestId);
                CREATE INDEX IF NOT EXISTS idx_quest_trader_req_trader ON QuestTraderRequirements(TraderName, RequiredLevel);";
            await using (var cmd = new SqliteCommand(createTraderReqSql, connection))
            {
                await cmd.ExecuteNonQueryAsync();
            }

            // 7. Create QuestPoolRequirements table & indices
            var createPoolReqSql = @"
                CREATE TABLE IF NOT EXISTS QuestPoolRequirements (
                    Id TEXT PRIMARY KEY,
                    QuestId TEXT NOT NULL,
                    PoolTrader TEXT NOT NULL,
                    PoolTraderLL INTEGER,
                    RequiredCompletedCount INTEGER NOT NULL,
                    Description TEXT,
                    IsApproved INTEGER NOT NULL DEFAULT 1,
                    ApprovedAt TEXT,
                    UpdatedAt TEXT,
                    TaskIds TEXT,
                    FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS idx_quest_pool_req_quest_id ON QuestPoolRequirements(QuestId);
                CREATE INDEX IF NOT EXISTS idx_quest_pool_req_trader ON QuestPoolRequirements(PoolTrader, PoolTraderLL);";
            await using (var cmd = new SqliteCommand(createPoolReqSql, connection))
            {
                await cmd.ExecuteNonQueryAsync();
            }

            if (!await ColumnExistsAsync(connection, "QuestPoolRequirements", "TaskIds"))
            {
                await using var alterCmd = new SqliteCommand("ALTER TABLE QuestPoolRequirements ADD COLUMN TaskIds TEXT;", connection);
                await alterCmd.ExecuteNonQueryAsync();
            }

            // 8. Clean up any duplicate seed records from QuestTraderRequirements
            var cleanupSql = @"
                DELETE FROM QuestTraderRequirements 
                WHERE Id = 'seed_wc_pk_ll2' 
                  AND EXISTS (SELECT 1 FROM QuestTraderRequirements WHERE QuestId = '5a27ba1c86f77461ea5a3c56' AND Id != 'seed_wc_pk_ll2');";
            await using (var cmd = new SqliteCommand(cleanupSql, connection))
            {
                await cmd.ExecuteNonQueryAsync();
            }

            // 9. Seed missing baseline extractions into MapMarkers
            if (await TableExistsAsync(connection, "MapMarkers"))
            {
                var seedMarkersSql = @"
                    INSERT OR IGNORE INTO MapMarkers (Id, Name, NameKo, MarkerType, MapKey, X, Y, Z, FloorId, CreatedAt, UpdatedAt) VALUES
                    ('173a0ca8-adcd-499d-ba60-55d1eaaf04f3', 'Sewer River', '하수구 강', 'PmcExtraction', 'StreetsOfTarkov', -251.5554, 0.0, 205.2907, NULL, datetime('now'), datetime('now')),
                    ('afbdea0f-0337-4674-965f-d3f21a7401de', 'Pinewood Basement (Co-Op)', '파인우드 지하실 (협동)', 'SharedExtraction', 'StreetsOfTarkov', -109.3777, 0.0, 61.4218, NULL, datetime('now'), datetime('now')),
                    ('174ef47d-067b-41b2-9505-ddcf1c55968b', 'Klimov Shopping Mall Exfil', '클리모프 쇼핑몰 탈출구', 'ScavExtraction', 'StreetsOfTarkov', -164.2437, 0.0, -16.8312, NULL, datetime('now'), datetime('now')),
                    ('4186a794-8244-47ad-a50c-2575fca90fa1', 'ZB-013', 'ZB-013', 'PmcExtraction', 'Customs', 199.5158, 0.0, -153.4935, NULL, datetime('now'), datetime('now')),
                    ('db385dfe-4642-4ae7-b5f2-68584fb407ef', 'Railroad Passage', '철도 통로', 'PmcExtraction', 'Customs', 195.9057, 0.0, -258.3163, NULL, datetime('now'), datetime('now')),
                    ('7a6f865b-0b5a-4acc-992b-2d09fe525f36', 'Boiler Room Basement (Co-Op)', '보일러실 지하실 (협동)', 'SharedExtraction', 'Customs', 108.9011, 0.0, -55.7044, NULL, datetime('now'), datetime('now')),
                    ('98f01be9-c11f-47fa-bb32-d104bc6216ee', 'Friendship Bridge (Co-Op)', '우정의 다리 (협동)', 'SharedExtraction', 'Woods', 100.5138, 0.0, -839.6506, NULL, datetime('now'), datetime('now')),
                    ('e5ca211d-a6be-4302-bcb8-107720363d85', 'Transit to Reserve', '리저브로 이동', 'Transit', 'Woods', 235.6407, 0.0, 339.099, NULL, datetime('now'), datetime('now')),
                    ('033d79de-f911-4a67-94f6-2d830d94545c', 'RUAF Roadblock', 'RUAF 바리케이드', 'ScavExtraction', 'Woods', -130.0398, 0.0, 409.6407, NULL, datetime('now'), datetime('now')),
                    ('1ce29b9c-a7bc-478a-9e7a-941cb5733284', 'UN Roadblock', 'UN 바리케이드', 'ScavExtraction', 'Woods', -515.2278, 0.0, 287.7618, NULL, datetime('now'), datetime('now')),
                    ('23010346-2695-4636-8a82-7a0cffe7c307', 'Smuggler''s Path (Co-op)', '밀수업자의 길 (협동)', 'SharedExtraction', 'Shoreline', -708.6718, 0.0, -246.7079, NULL, datetime('now'), datetime('now')),
                    ('9305c5ce-b3a8-42d0-ba6e-a72daa853f51', 'Scav Checkpoint (Co-Op)', '스캐브 검문소 (협동)', 'SharedExtraction', 'GroundZero', 28.0084, 0.0, -82.9392, NULL, datetime('now'), datetime('now')),

                    ('19d67562-b9b4-4be0-a393-37609ee364f9', 'ZB-013 power lever', 'ZB-013 전원 레버', 'Lever', 'Customs', 351.9056, 1.0, -39.4678, 'main', datetime('now'), datetime('now'));

                    DELETE FROM MapMarkers WHERE Id = '135def91-174f-4451-9696-b85d4039fa76';

                    UPDATE MapMarkers SET Name = 'Killa', NameKo = '킬라' WHERE MapKey = 'Interchange' AND MarkerType = 'BossSpawn' AND Name LIKE 'BOSS_SPAWN_%';";
                await using var markerCmd = new SqliteCommand(seedMarkersSql, connection);
                await markerCmd.ExecuteNonQueryAsync();
            }

            if (await TableExistsAsync(connection, "QuestObjectives"))
            {
                var seedObjectivesSql = @"
                    DELETE FROM QuestObjectives WHERE Id IN ('64ee99639878a0569d6ec8c9_survive', 'sLnZaRMj1J0xfB_Xb4izGB');
                    DELETE FROM QuestObjectives WHERE Id = 'lsz-izpRbqejXNnpl3IVtF' OR (QuestId = '5a27b87686f77460de0252a8' AND ObjectiveType = 'Kill');";
                await using var objCmd = new SqliteCommand(seedObjectivesSql, connection);
                await objCmd.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Error ensuring database schema in {dbPath}: {ex.Message}", ex);
        }
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        var sql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@name", tableName);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string tableName, string columnName)
    {
        var sql = $"PRAGMA table_info({tableName})";
        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1);
            if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void ValidateTargetDatabasePath(string dbPath)
    {
        var fileName = Path.GetFileName(dbPath);
        if (string.Equals(fileName, "user_data.db", StringComparison.OrdinalIgnoreCase) ||
            dbPath.EndsWith("user_data.db", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DatabaseBootstrapService must never modify user_data.db.");
        }
    }
}
