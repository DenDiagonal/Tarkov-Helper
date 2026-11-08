using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TarkovHelper.Debug;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services;

/// <summary>
/// ?�용???�이?��? SQLite DB (user_data.db)???�??로드?�는 ?�비??
/// ?�스??진행, 목표 ?�료, ?�이?�아??진행, ?�이???�벤?�리 ?�을 관리합?�다.
/// </summary>
public sealed class UserDataDbService
{
    private static readonly ILogger _log = Log.For<UserDataDbService>();
    private static readonly object _dbLock = new object();
    private static readonly System.Threading.SemaphoreSlim _dbSemaphore = new System.Threading.SemaphoreSlim(1, 1);
    private static UserDataDbService? _instance;
    public static UserDataDbService Instance => _instance ??= new UserDataDbService();

    private string GetConnectionString(bool readOnly = false) { var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _databasePath, Mode = readOnly ? Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly : Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 30, Pooling = true, Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Shared }; return builder.ConnectionString; }


    private readonly string _databasePath;
    private bool _isInitialized;

    public bool IsInitialized => _isInitialized;
    public string DatabasePath => _databasePath;

    /// <summary>
    /// 마이그레?�션 진행 ?�황 ?�벤??
    /// </summary>
    public event Action<string>? MigrationProgress;

    /// <summary>
    /// 커스텀 마커 변경 이벤트 (추가/수정/삭제)
    /// </summary>
    public event Action? CustomMarkersChanged;

    /// <summary>
    /// 마이그레?�션???�요?��? ?�인
    /// </summary>
    public bool NeedsMigration()
    {
        var v2Path = Path.Combine(AppEnv.ConfigPath, "quest_progress_v2.json");
        var v1Path = Path.Combine(AppEnv.ConfigPath, "quest_progress.json");
        var objPath = Path.Combine(AppEnv.ConfigPath, "objective_progress.json");
        var hideoutPath = Path.Combine(AppEnv.ConfigPath, "hideout_progress.json");
        var inventoryPath = Path.Combine(AppEnv.ConfigPath, "item_inventory.json");

        return File.Exists(v2Path) || File.Exists(v1Path) || File.Exists(objPath) ||
               File.Exists(hideoutPath) || File.Exists(inventoryPath);
    }

    private void ReportProgress(string message)
    {
        MigrationProgress?.Invoke(message);
        System.Diagnostics.Debug.WriteLine($"[UserDataDbService] {message}");
    }

    private UserDataDbService()
    {
        _databasePath = Path.Combine(AppEnv.ConfigPath, "user_data.db");
    }

    /// <summary>
    /// DB 초기??(?�이�??�성)
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            // 백업 먼�? ?�행
            await BackupDatabaseAsync();

            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

            var connectionString = GetConnectionString();
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            await CreateTablesAsync(connection);

            _isInitialized = true;
            System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Initialized: {_databasePath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Initialization failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// ?�기??DB 초기??(???�작 ?�계 ?�는 GetSetting ?�출 ???�용)
    /// </summary>
    public void EnsureInitialized()
    {
        if (_isInitialized) return;

        lock (_dbLock)
        {
            if (_isInitialized) return;

            try
            {
                // ?�전 ?�션???�긴 ?�들 ?�리
                SqliteConnection.ClearAllPools();
                
                Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

                var connectionString = GetConnectionString();

                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();

                    // Perform synchronous migration to ProfileType system if needed
                    MigrateToProfileSystem(connection);

                    var createTablesSql = @"
                    CREATE TABLE IF NOT EXISTS QuestProgress (
                        Id TEXT,
                        ProfileType INTEGER NOT NULL DEFAULT 0,
                        NormalizedName TEXT,
                        Status TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL,
                        PRIMARY KEY (Id, ProfileType)
                    );
                    CREATE TABLE IF NOT EXISTS ObjectiveProgress (
                        Id TEXT,
                        ProfileType INTEGER NOT NULL DEFAULT 0,
                        QuestId TEXT,
                        IsCompleted INTEGER NOT NULL DEFAULT 0,
                        UpdatedAt TEXT NOT NULL,
                        PRIMARY KEY (Id, ProfileType)
                    );
                    CREATE TABLE IF NOT EXISTS ItemInventory (
                        ItemNormalizedName TEXT,
                        ProfileType INTEGER NOT NULL DEFAULT 0,
                        FirQuantity INTEGER NOT NULL DEFAULT 0,
                        NonFirQuantity INTEGER NOT NULL DEFAULT 0,
                        UpdatedAt TEXT NOT NULL,
                        PRIMARY KEY (ItemNormalizedName, ProfileType)
                    );
                    CREATE TABLE IF NOT EXISTS HideoutProgress (
                        StationId TEXT,
                        ProfileType INTEGER NOT NULL DEFAULT 0,
                        Level INTEGER NOT NULL DEFAULT 0,
                        UpdatedAt TEXT NOT NULL,
                        PRIMARY KEY (StationId, ProfileType)
                    );
                    CREATE TABLE IF NOT EXISTS UserSettings (
                        Key TEXT,
                        ProfileType INTEGER NOT NULL DEFAULT 0,
                        Value TEXT NOT NULL,
                        PRIMARY KEY (Key, ProfileType)
                    );
                    CREATE TABLE IF NOT EXISTS CustomMapMarkers (
                        Id TEXT,
                        ProfileType INTEGER NOT NULL DEFAULT 0,
                        MapKey TEXT NOT NULL,
                        Name TEXT,
                        X REAL NOT NULL,
                        Y REAL NOT NULL,
                        Z REAL NOT NULL,
                        FloorId TEXT,
                        Color TEXT,
                        Size REAL NOT NULL DEFAULT 24.0,
                        Opacity REAL NOT NULL DEFAULT 1.0,
                        CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        PRIMARY KEY (Id, ProfileType)
                    );
                    CREATE TABLE IF NOT EXISTS NeededItemProgress (
                        Id TEXT,
                        ProfileType INTEGER NOT NULL DEFAULT 0,
                        Count INTEGER NOT NULL DEFAULT 0,
                        UpdatedAt TEXT NOT NULL,
                        PRIMARY KEY (Id, ProfileType)
                    );
                    CREATE INDEX IF NOT EXISTS idx_quest_progress_normalized ON QuestProgress(NormalizedName);
                    CREATE INDEX IF NOT EXISTS idx_needed_item_progress_profile ON NeededItemProgress(ProfileType);
                    ";

                    using (var cmd = new SqliteCommand(createTablesSql, connection))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }

                _isInitialized = true;
                _log.Info("UserDataDbService (Synchronous) initialized successfully.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Sync Initialization failed: {ex.Message}");
            }
        }
    }

    private void MigrateToProfileSystem(SqliteConnection connection)
    {
        // Check if DB exists and has QuestProgress table
        var checkTableSql = "SELECT name FROM sqlite_master WHERE type='table' AND name='QuestProgress'";
        using var checkCmd = new SqliteCommand(checkTableSql, connection);
        var tableName = checkCmd.ExecuteScalar() as string;
        if (string.IsNullOrEmpty(tableName)) return;

        // Check if ProfileType column exists
        var checkColumnSql = "PRAGMA table_info(QuestProgress)";
        using var columnCmd = new SqliteCommand(checkColumnSql, connection);
        using var reader = columnCmd.ExecuteReader();
        bool hasProfileType = false;
        while (reader.Read())
        {
            if (reader.GetString(1) == "ProfileType")
            {
                hasProfileType = true;
                break;
            }
        }

        if (!hasProfileType)
        {
            System.Diagnostics.Debug.WriteLine("[UserDataDbService] Starting Schema Migration to ProfileType System (Sync)");
            // Note: Perform heavy migration inside a transaction if needed.
            // Simplified for EnsureInitialized.
        }
    }

    private async Task CreateTablesAsync(SqliteConnection connection)
    {
        // First, check for schema migration to ProfileType system
        await MigrateToProfileSystemAsync(connection);

        var createTablesSql = @"
            -- ?�스??진행 ?�태
            CREATE TABLE IF NOT EXISTS QuestProgress (
                Id TEXT,
                ProfileType INTEGER NOT NULL DEFAULT 0,
                NormalizedName TEXT,
                Status TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                PRIMARY KEY (Id, ProfileType)
            );

            -- ?�스??목표 진행 ?�태
            CREATE TABLE IF NOT EXISTS ObjectiveProgress (
                Id TEXT,
                ProfileType INTEGER NOT NULL DEFAULT 0,
                QuestId TEXT,
                IsCompleted INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL,
                PRIMARY KEY (Id, ProfileType)
            );

            -- ?�이???�벤?�리
            CREATE TABLE IF NOT EXISTS ItemInventory (
                ItemNormalizedName TEXT,
                ProfileType INTEGER NOT NULL DEFAULT 0,
                FirQuantity INTEGER NOT NULL DEFAULT 0,
                NonFirQuantity INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL,
                PRIMARY KEY (ItemNormalizedName, ProfileType)
            );

            -- ?�이?�아??진행
            CREATE TABLE IF NOT EXISTS HideoutProgress (
                StationId TEXT,
                ProfileType INTEGER NOT NULL DEFAULT 0,
                Level INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL,
                PRIMARY KEY (StationId, ProfileType)
            );

            -- ?�용???�정 (?��? ?�정?� ?�로?�별�?관�?
            CREATE TABLE IF NOT EXISTS UserSettings (
                Key TEXT,
                ProfileType INTEGER NOT NULL DEFAULT 0,
                Value TEXT NOT NULL,
                PRIMARY KEY (Key, ProfileType)
            );

            -- ?�이???�스?�리
            CREATE TABLE IF NOT EXISTS RaidHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RaidId TEXT,
                SessionId TEXT,
                ShortId TEXT,
                ProfileId TEXT,
                RaidType INTEGER NOT NULL DEFAULT 0,
                GameMode INTEGER NOT NULL DEFAULT 0,
                MapName TEXT,
                MapKey TEXT,
                ServerIp TEXT,
                ServerPort INTEGER,
                IsParty INTEGER NOT NULL DEFAULT 0,
                PartyLeaderAccountId TEXT,
                StartTime TEXT,
                EndTime TEXT,
                DurationSeconds INTEGER,
                Rtt REAL,
                PacketLoss REAL,
                PacketsSent INTEGER,
                PacketsReceived INTEGER,
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            -- 커스?� �?마커
            CREATE TABLE IF NOT EXISTS CustomMapMarkers (
                Id TEXT,
                ProfileType INTEGER NOT NULL DEFAULT 0,
                MapKey TEXT NOT NULL,
                Name TEXT,
                X REAL NOT NULL,
                Y REAL NOT NULL,
                Z REAL NOT NULL,
                FloorId TEXT,
                Color TEXT,
                Size REAL NOT NULL DEFAULT 24.0,
                Opacity REAL NOT NULL DEFAULT 1.0,
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (Id, ProfileType)
            );

            -- Needed Item Progress
            CREATE TABLE IF NOT EXISTS NeededItemProgress (
                Id TEXT,
                ProfileType INTEGER NOT NULL DEFAULT 0,
                Count INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL,
                PRIMARY KEY (Id, ProfileType)
            );

            -- ?�덱??
            CREATE INDEX IF NOT EXISTS idx_quest_progress_normalized ON QuestProgress(NormalizedName);
            CREATE INDEX IF NOT EXISTS idx_objective_progress_quest ON ObjectiveProgress(QuestId);
            CREATE INDEX IF NOT EXISTS idx_raid_history_start_time ON RaidHistory(StartTime);
            CREATE INDEX IF NOT EXISTS idx_raid_history_map_key ON RaidHistory(MapKey);
            CREATE INDEX IF NOT EXISTS idx_raid_history_raid_type ON RaidHistory(RaidType);
            CREATE INDEX IF NOT EXISTS idx_custom_markers_map_key ON CustomMapMarkers(MapKey);
            CREATE INDEX IF NOT EXISTS idx_needed_item_progress_profile ON NeededItemProgress(ProfileType);
        ";

        await using var cmd = new SqliteCommand(createTablesSql, connection);
        await cmd.ExecuteNonQueryAsync();
        CustomMarkersChanged?.Invoke();
    }

    /// <summary>
    /// 기존 ?�일 ?�로???�스?�에??PVP/PVE ?�합 ?�로???�스?�으�?마이그레?�션?�니??
    /// SQLite??ALTER TABLE�?PK�?변경할 ???�으므�??�이�??�생??방식???�용?�니??
    /// </summary>
    private async Task MigrateToProfileSystemAsync(SqliteConnection connection)
    {
        var tablesToMigrate = new Dictionary<string, string>
        {
            { "QuestProgress", "Id, 0 as ProfileType, NormalizedName, Status, UpdatedAt" },
            { "ObjectiveProgress", "Id, 0 as ProfileType, QuestId, IsCompleted, UpdatedAt" },
            { "ItemInventory", "ItemNormalizedName, 0 as ProfileType, FirQuantity, NonFirQuantity, UpdatedAt" },
            { "HideoutProgress", "StationId, 0 as ProfileType, Level, UpdatedAt" },
            { "UserSettings", "Key, 0 as ProfileType, Value" },
            { "CustomMapMarkers", "Id, 0 as ProfileType, MapKey, Name, X, Y, Z, FloorId, Color, Size, Opacity, CreatedAt" }
        };

        foreach (var entry in tablesToMigrate)
        {
            var tableName = entry.Key;
            var columns = entry.Value;

            try
            {
                // ?�이�?존재 ?��? ?�인
                var checkTableSql = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'";
                await using var checkTableCmd = new SqliteCommand(checkTableSql, connection);
                if (Convert.ToInt32(await checkTableCmd.ExecuteScalarAsync()) == 0) continue;

                // ?��? 고유 PK(ProfileType ?�함)가 ?�정?�어 ?�는지 ?�인
                var checkPkSql = $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE pk > 0 AND name = 'ProfileType'";
                await using var checkPkCmd = new SqliteCommand(checkPkSql, connection);
                var hasProfileTypeInPk = Convert.ToInt32(await checkPkCmd.ExecuteScalarAsync()) > 0;

                if (!hasProfileTypeInPk)
                {
                    System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Reconstructing {tableName} for PVE/PVP PK change...");

                    // 1. 기존 ?�이�??�름 변�?
                    var renameSql = $"ALTER TABLE {tableName} RENAME TO {tableName}_old";
                    await using (var cmd = new SqliteCommand(renameSql, connection)) await cmd.ExecuteNonQueryAsync();

                    // 2. ???�이�??�성 (CreateTablesAsync가 ?�중???�출?��?�??�기?�는 직접 명령 ?�행 ?�??
                    // CreateTablesAsync?�서 ?�용??SQL�??�일??구조???�이블을 미리 ?�성)
                    await RecreateTableWithNewPkAsync(tableName, connection);

                    // 3. ?�이??복사 (기존 ?�이?�는 PVP??0?�로 매핑)
                    // ?�드 목록??ProfileType 컬럼???�함?�어 ?�는지 ?�인 ???�이??부?�넣�?
                    var insertSql = $@"
                        INSERT INTO {tableName} 
                        SELECT * FROM (
                            SELECT {columns} FROM {tableName}_old
                        )";
                    
                    try {
                        await using (var cmd = new SqliteCommand(insertSql, connection)) await cmd.ExecuteNonQueryAsync();
                        // 4. 기존 ?�이�???��
                        var dropSql = $"DROP TABLE {tableName}_old";
                        await using (var cmd = new SqliteCommand(dropSql, connection)) await cmd.ExecuteNonQueryAsync();
                        System.Diagnostics.Debug.WriteLine($"[UserDataDbService] {tableName} reconstruction success");
                    } catch (Exception ex) {
                        System.Diagnostics.Debug.WriteLine($"[UserDataDbService] {tableName} data copy failed: {ex.Message}. Rolling back rename...");
                        var rollbackSql = $"ALTER TABLE {tableName}_old RENAME TO {tableName}";
                        await using (var cmd = new SqliteCommand(rollbackSql, connection)) await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Migration failed for {tableName}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// ?�이블별 ???�키마로 ?�생??
    /// </summary>
    private async Task RecreateTableWithNewPkAsync(string tableName, SqliteConnection connection)
    {
        string sql = tableName switch
        {
            "QuestProgress" => @"
                CREATE TABLE QuestProgress (
                    Id TEXT, ProfileType INTEGER NOT NULL DEFAULT 0, NormalizedName TEXT, Status TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
                    PRIMARY KEY (Id, ProfileType))",
            "ObjectiveProgress" => @"
                CREATE TABLE ObjectiveProgress (
                    Id TEXT, ProfileType INTEGER NOT NULL DEFAULT 0, QuestId TEXT, IsCompleted INTEGER NOT NULL DEFAULT 0, UpdatedAt TEXT NOT NULL,
                    PRIMARY KEY (Id, ProfileType))",
            "ItemInventory" => @"
                CREATE TABLE ItemInventory (
                    ItemNormalizedName TEXT, ProfileType INTEGER NOT NULL DEFAULT 0, FirQuantity INTEGER NOT NULL DEFAULT 0, NonFirQuantity INTEGER NOT NULL DEFAULT 0, UpdatedAt TEXT NOT NULL,
                    PRIMARY KEY (ItemNormalizedName, ProfileType))",
            "HideoutProgress" => @"
                CREATE TABLE HideoutProgress (
                    StationId TEXT, ProfileType INTEGER NOT NULL DEFAULT 0, Level INTEGER NOT NULL DEFAULT 0, UpdatedAt TEXT NOT NULL,
                    PRIMARY KEY (StationId, ProfileType))",
            "UserSettings" => @"
                CREATE TABLE UserSettings (
                    Key TEXT, ProfileType INTEGER NOT NULL DEFAULT 0, Value TEXT NOT NULL,
                    PRIMARY KEY (Key, ProfileType))",
            "CustomMapMarkers" => @"
                CREATE TABLE CustomMapMarkers (
                    Id TEXT, ProfileType INTEGER NOT NULL DEFAULT 0, MapKey TEXT NOT NULL, Name TEXT, X REAL NOT NULL, Y REAL NOT NULL, Z REAL NOT NULL, FloorId TEXT, Color TEXT, Size REAL NOT NULL DEFAULT 24.0, Opacity REAL NOT NULL DEFAULT 1.0, CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (Id, ProfileType))",
            _ => throw new ArgumentException($"Unknown table: {tableName}")
        };

        if (!string.IsNullOrEmpty(sql))
        {
            await using var cmd = new SqliteCommand(sql, connection);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task BackupDatabaseAsync()
    {
        try
        {
            if (File.Exists(_databasePath))
            {
                var backupPath = _databasePath + ".bak";
                File.Copy(_databasePath, backupPath, true);
                System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Database backup created: {backupPath}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Backup failed: {ex.Message}");
        }
    }



    #region Quest Progress

    /// <summary>
    /// 모든 ?�스??진행 ?�태 로드
    /// </summary>
    public async Task<Dictionary<string, QuestStatus>> LoadQuestProgressAsync(ProfileType? profileType = null)
    {
        await InitializeAsync();
        
        // ProfileService.Instance�?직접 ?�출?��? ?�고 ?�자�?받�? 값을 ?�용?�거??기본값을 ?�용?�니??
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        var result = new Dictionary<string, QuestStatus>(StringComparer.OrdinalIgnoreCase);

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // Migrate any historical Base64 IDs in QuestProgress to canonical BSG IDs
        await MigrateLegacyBase64QuestProgressAsync(connection);

        var sql = "SELECT Id, NormalizedName, Status FROM QuestProgress WHERE ProfileType = @profileType";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var id = reader.GetString(0);
            var normalizedName = reader.IsDBNull(1) ? null : reader.GetString(1);
            var statusStr = reader.GetString(2);

            if (Enum.TryParse<QuestStatus>(statusStr, out var status))
            {
                if (!string.IsNullOrEmpty(id))
                    result[id] = status;
                if (!string.IsNullOrEmpty(normalizedName))
                    result[normalizedName] = status;
            }
        }

        return result;
    }

    /// <summary>
    /// Migrates any historical Base64 Wiki URLs in QuestProgress to canonical BSG IDs.
    /// Runs seamlessly in a single atomic transaction without modifying statuses or timestamps.
    /// </summary>
    public async Task MigrateLegacyBase64QuestProgressAsync(SqliteConnection connection)
    {
        try
        {
            var checkSql = "SELECT COUNT(*) FROM QuestProgress WHERE Id LIKE 'aHR0%'";
            await using (var checkCmd = new SqliteCommand(checkSql, connection))
            {
                var count = Convert.ToInt64(await checkCmd.ExecuteScalarAsync());
                if (count == 0) return;
            }

            _log.Info("Migrating legacy Base64 quest IDs in QuestProgress to canonical BSG IDs...");
            var selectSql = "SELECT Id, ProfileType, NormalizedName, Status, UpdatedAt FROM QuestProgress WHERE Id LIKE 'aHR0%'";
            var itemsToMigrate = new List<(string OldId, int ProfileType, string? NormalizedName, string Status, string UpdatedAt)>();

            await using (var selectCmd = new SqliteCommand(selectSql, connection))
            await using (var reader = await selectCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    itemsToMigrate.Add((
                        reader.GetString(0),
                        reader.GetInt32(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.GetString(3),
                        reader.IsDBNull(4) ? DateTime.UtcNow.ToString("o") : reader.GetString(4)
                    ));
                }
            }

            await using var tx = await connection.BeginTransactionAsync();
            try
            {
                foreach (var item in itemsToMigrate)
                {
                    var newBsgId = CanonicalQuestMap.ResolveBsgId(item.OldId);
                    if (string.Equals(newBsgId, item.OldId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var checkExistingSql = "SELECT rowid FROM QuestProgress WHERE Id = @newId AND ProfileType = @profileType";
                    long existingRowId = 0;
                    await using (var checkExistCmd = new SqliteCommand(checkExistingSql, connection, (SqliteTransaction)tx))
                    {
                        checkExistCmd.Parameters.AddWithValue("@newId", newBsgId);
                        checkExistCmd.Parameters.AddWithValue("@profileType", item.ProfileType);
                        var obj = await checkExistCmd.ExecuteScalarAsync();
                        if (obj != null && obj != DBNull.Value)
                        {
                            existingRowId = Convert.ToInt64(obj);
                        }
                    }

                    if (existingRowId > 0)
                    {
                        var updateExistingSql = "UPDATE QuestProgress SET Status = @status, UpdatedAt = @updatedAt WHERE rowid = @rowid";
                        await using (var updateCmd = new SqliteCommand(updateExistingSql, connection, (SqliteTransaction)tx))
                        {
                            updateCmd.Parameters.AddWithValue("@status", item.Status);
                            updateCmd.Parameters.AddWithValue("@updatedAt", item.UpdatedAt);
                            updateCmd.Parameters.AddWithValue("@rowid", existingRowId);
                            await updateCmd.ExecuteNonQueryAsync();
                        }

                        var deleteOldSql = "DELETE FROM QuestProgress WHERE Id = @oldId AND ProfileType = @profileType";
                        await using (var deleteCmd = new SqliteCommand(deleteOldSql, connection, (SqliteTransaction)tx))
                        {
                            deleteCmd.Parameters.AddWithValue("@oldId", item.OldId);
                            deleteCmd.Parameters.AddWithValue("@profileType", item.ProfileType);
                            await deleteCmd.ExecuteNonQueryAsync();
                        }
                    }
                    else
                    {
                        var updateInPlaceSql = "UPDATE QuestProgress SET Id = @newId WHERE Id = @oldId AND ProfileType = @profileType";
                        await using (var updateCmd = new SqliteCommand(updateInPlaceSql, connection, (SqliteTransaction)tx))
                        {
                            updateCmd.Parameters.AddWithValue("@newId", newBsgId);
                            updateCmd.Parameters.AddWithValue("@oldId", item.OldId);
                            updateCmd.Parameters.AddWithValue("@profileType", item.ProfileType);
                            await updateCmd.ExecuteNonQueryAsync();
                        }
                    }
                }

                await tx.CommitAsync();
                _log.Info($"Successfully migrated {itemsToMigrate.Count} legacy Base64 quest IDs in user_data.db.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _log.Error($"Failed to migrate legacy Base64 quest IDs: {ex.Message}");
                throw;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Error in MigrateLegacyBase64QuestProgressAsync: {ex.Message}");
        }
    }


    /// <summary>
    /// ?�스??진행 ?�태 ?�??
    /// </summary>
    public async Task SaveQuestProgressAsync(string id, string? normalizedName, QuestStatus status, ProfileType? profileType = null)
    {
        await InitializeAsync();
        id = CanonicalQuestMap.ResolveBsgId(id);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            INSERT INTO QuestProgress (Id, ProfileType, NormalizedName, Status, UpdatedAt)
            VALUES (@id, @profileType, @normalizedName, @status, @updatedAt)
            ON CONFLICT(Id, ProfileType) DO UPDATE SET
                NormalizedName = @normalizedName,
                Status = @status,
                UpdatedAt = @updatedAt";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
        cmd.Parameters.AddWithValue("@normalizedName", normalizedName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@status", status.ToString());
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// ?�러 ?�스??진행 ?�태�?배치�??�??(?�랜??�� ?�용)
    /// </summary>
    public async Task SaveQuestProgressBatchAsync(IEnumerable<(string Id, string? NormalizedName, QuestStatus Status)> progressItems, ProfileType? profileType = null)
    {
        await InitializeAsync();
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var sql = @"
                INSERT INTO QuestProgress (Id, ProfileType, NormalizedName, Status, UpdatedAt)
                VALUES (@id, @profileType, @normalizedName, @status, @updatedAt)
                ON CONFLICT(Id, ProfileType) DO UPDATE SET
                    NormalizedName = @normalizedName,
                    Status = @status,
                    UpdatedAt = @updatedAt";

            var updatedAt = DateTime.UtcNow.ToString("o");

            foreach (var item in progressItems)
            {
                await using var cmd = new SqliteCommand(sql, connection, (SqliteTransaction)transaction);
                cmd.Parameters.AddWithValue("@id", item.Id);
                cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
                cmd.Parameters.AddWithValue("@normalizedName", item.NormalizedName ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@status", item.Status.ToString());
                cmd.Parameters.AddWithValue("@updatedAt", updatedAt);
                await cmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// ?�스??진행 ?�태 ??�� (리셋)
    /// </summary>
    public async Task DeleteQuestProgressAsync(string id, ProfileType? profileType = null)
    {
        await InitializeAsync();
        id = CanonicalQuestMap.ResolveBsgId(id);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM QuestProgress WHERE (Id = @id OR NormalizedName = @id) AND ProfileType = @profileType";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 모든 ?�스??진행 ?�태 ??��
    /// </summary>
    public async Task ClearAllQuestProgressAsync(ProfileType? profileType = null)
    {
        await InitializeAsync();
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = new SqliteCommand("DELETE FROM QuestProgress WHERE ProfileType = @profileType", connection);
        cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
        await cmd.ExecuteNonQueryAsync();
        CustomMarkersChanged?.Invoke();
    }

    #endregion

    #region Objective Progress

    /// <summary>
    /// 모든 목표 진행 ?�태 로드
    /// </summary>
    public async Task<Dictionary<string, bool>> LoadObjectiveProgressAsync(ProfileType? profileType = null)
    {
        await InitializeAsync();
        var actualProfile = profileType ?? ProfileService.Instance.CurrentProfile;

        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "SELECT Id, IsCompleted FROM ObjectiveProgress WHERE ProfileType = @profileType";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@profileType", (int)actualProfile);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var id = reader.GetString(0);
            var isCompleted = reader.GetInt32(1) == 1;
            result[id] = isCompleted;
        }

        return result;
    }

    /// <summary>
    /// 목표 진행 ?�태 ?�??
    /// </summary>
    public async Task SaveObjectiveProgressBatchAsync(IEnumerable<(string Key, string? QuestId, bool IsCompleted)> items, ProfileType? profileType = null)
    {
        if (items == null) return;
        var list = items as IList<(string Key, string? QuestId, bool IsCompleted)> ?? items.ToList();
        if (list.Count == 0) return;

        await InitializeAsync().ConfigureAwait(false);
        var actualProfile = profileType ?? ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync().ConfigureAwait(false);

        try
        {
            foreach (var (key, questId, isCompleted) in list)
            {
                if (!isCompleted)
                {
                    var delSql = "DELETE FROM ObjectiveProgress WHERE Id = @id AND ProfileType = @profileType";
                    await using var delCmd = new SqliteCommand(delSql, connection, (SqliteTransaction)tx);
                    delCmd.Parameters.AddWithValue("@id", key);
                    delCmd.Parameters.AddWithValue("@profileType", (int)actualProfile);
                    await delCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                else
                {
                    var sql = @"
                        INSERT INTO ObjectiveProgress (Id, ProfileType, QuestId, IsCompleted, UpdatedAt)
                        VALUES (@id, @profileType, @questId, 1, @updatedAt)
                        ON CONFLICT(Id, ProfileType) DO UPDATE SET
                            QuestId = @questId,
                            IsCompleted = 1,
                            UpdatedAt = @updatedAt";
                    await using var cmd = new SqliteCommand(sql, connection, (SqliteTransaction)tx);
                    cmd.Parameters.AddWithValue("@id", key);
                    cmd.Parameters.AddWithValue("@profileType", (int)actualProfile);
                    cmd.Parameters.AddWithValue("@questId", questId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
            await tx.CommitAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync().ConfigureAwait(false);
            _log.Error($"Failed to batch save objective progress: {ex.Message}");
            throw;
        }
    }

    public async Task SaveObjectiveProgressAsync(string id, string? questId, bool isCompleted)
    {
        await InitializeAsync();
        var profileType = ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            INSERT INTO ObjectiveProgress (Id, ProfileType, QuestId, IsCompleted, UpdatedAt)
            VALUES (@id, @profileType, @questId, @isCompleted, @updatedAt)
            ON CONFLICT(Id, ProfileType) DO UPDATE SET
                QuestId = @questId,
                IsCompleted = @isCompleted,
                UpdatedAt = @updatedAt";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@profileType", (int)profileType);
        cmd.Parameters.AddWithValue("@questId", questId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@isCompleted", isCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 목표 진행 ?�태 ??��
    /// </summary>
    public async Task DeleteObjectiveProgressAsync(string id)
    {
        await InitializeAsync();
        var profileType = ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM ObjectiveProgress WHERE Id = @id AND ProfileType = @profileType";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@profileType", (int)profileType);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// ?�스?�의 모든 목표 진행 ?�태 ??��
    /// </summary>
    public async Task DeleteObjectiveProgressByQuestAsync(string questId)
    {
        await InitializeAsync();
        var profileType = ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM ObjectiveProgress WHERE (QuestId = @questId OR Id LIKE @pattern) AND ProfileType = @profileType";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@questId", questId);
        cmd.Parameters.AddWithValue("@pattern", $"{questId}:%");
        cmd.Parameters.AddWithValue("@profileType", (int)profileType);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 모든 목표 진행 ?�태 ??��
    /// </summary>
    public async Task ClearAllObjectiveProgressAsync()
    {
        await InitializeAsync();
        var profileType = ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = new SqliteCommand("DELETE FROM ObjectiveProgress WHERE ProfileType = @profileType", connection);
        cmd.Parameters.AddWithValue("@profileType", (int)profileType);
        await cmd.ExecuteNonQueryAsync();
        CustomMarkersChanged?.Invoke();
    }

    #endregion

    #region Hideout Progress

    /// <summary>
    /// 모든 ?�이?�아??진행 ?�태 로드
    /// </summary>
    public async Task<Dictionary<string, int>> LoadHideoutProgressAsync(ProfileType? profileType = null)
    {
        await InitializeAsync();
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "SELECT StationId, Level FROM HideoutProgress WHERE ProfileType = @profileType";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var stationId = reader.GetString(0);
            var level = reader.GetInt32(1);
            result[stationId] = level;
        }

        return result;
    }

    /// <summary>
    /// ?�이?�아??진행 ?�태 ?�??
    /// </summary>
    public async Task SaveHideoutProgressAsync(string stationId, int level, ProfileType? profileType = null)
    {
        await InitializeAsync();
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // ?�벨??0?�면 ??��
        if (level == 0)
        {
            var deleteSql = "DELETE FROM HideoutProgress WHERE StationId = @stationId AND ProfileType = @profileType";
            await using var deleteCmd = new SqliteCommand(deleteSql, connection);
            deleteCmd.Parameters.AddWithValue("@stationId", stationId);
            deleteCmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
            await deleteCmd.ExecuteNonQueryAsync();
            return;
        }

        var sql = @"
            INSERT INTO HideoutProgress (StationId, ProfileType, Level, UpdatedAt)
            VALUES (@stationId, @profileType, @level, @updatedAt)
            ON CONFLICT(StationId, ProfileType) DO UPDATE SET
                Level = @level,
                UpdatedAt = @updatedAt";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@stationId", stationId);
        cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
        cmd.Parameters.AddWithValue("@level", level);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 모든 ?�이?�아??진행 ?�태 ??��
    /// </summary>
    public async Task ClearAllHideoutProgressAsync(ProfileType? profileType = null)
    {
        await InitializeAsync();
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = new SqliteCommand("DELETE FROM HideoutProgress WHERE ProfileType = @profileType", connection);
        cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
        await cmd.ExecuteNonQueryAsync();
        CustomMarkersChanged?.Invoke();
    }

    #endregion

    #region Item Inventory

    /// <summary>
    /// 모든 ?�이???�벤?�리 로드
    /// </summary>
    public async Task<Dictionary<string, (int FirQuantity, int NonFirQuantity)>> LoadItemInventoryAsync(ProfileType? profileType = null)
    {
        await InitializeAsync();
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        var result = new Dictionary<string, (int FirQuantity, int NonFirQuantity)>(StringComparer.OrdinalIgnoreCase);

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "SELECT ItemNormalizedName, FirQuantity, NonFirQuantity FROM ItemInventory WHERE ProfileType = @profileType";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var itemName = reader.GetString(0);
            var firQty = reader.GetInt32(1);
            var nonFirQty = reader.GetInt32(2);
            result[itemName] = (firQty, nonFirQty);
        }

        return result;
    }

    /// <summary>
    /// ?�이???�벤?�리 ?�??
    /// </summary>
    public async Task SaveItemInventoryAsync(string itemNormalizedName, int firQuantity, int nonFirQuantity, ProfileType? profileType = null)
    {
        await InitializeAsync();
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // ????0?�면 ??��
        if (firQuantity == 0 && nonFirQuantity == 0)
        {
            var deleteSql = "DELETE FROM ItemInventory WHERE ItemNormalizedName = @itemName AND ProfileType = @profileType";
            await using var deleteCmd = new SqliteCommand(deleteSql, connection);
            deleteCmd.Parameters.AddWithValue("@itemName", itemNormalizedName);
            deleteCmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
            await deleteCmd.ExecuteNonQueryAsync();
            return;
        }

        var sql = @"
            INSERT INTO ItemInventory (ItemNormalizedName, ProfileType, FirQuantity, NonFirQuantity, UpdatedAt)
            VALUES (@itemName, @profileType, @firQty, @nonFirQty, @updatedAt)
            ON CONFLICT(ItemNormalizedName, ProfileType) DO UPDATE SET
                FirQuantity = @firQty,
                NonFirQuantity = @nonFirQty,
                UpdatedAt = @updatedAt";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@itemName", itemNormalizedName);
        cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
        cmd.Parameters.AddWithValue("@firQty", firQuantity);
        cmd.Parameters.AddWithValue("@nonFirQty", nonFirQuantity);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 모든 ?�이???�벤?�리 ??��
    /// </summary>
    public async Task ClearAllItemInventoryAsync(ProfileType? profileType = null)
    {
        await InitializeAsync();
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = new SqliteCommand("DELETE FROM ItemInventory WHERE ProfileType = @profileType", connection);
        cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
        await cmd.ExecuteNonQueryAsync();
        CustomMarkersChanged?.Invoke();
    }

    #endregion

    #region Needed Item Progress

    /// <summary>
    /// Load needed item progress counts for profile
    /// </summary>
    public async Task<Dictionary<string, int>> LoadNeededItemProgressAsync(ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var sql = "SELECT Id, Count FROM NeededItemProgress WHERE ProfileType = @profileType";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            var count = reader.GetInt32(1);
            result[id] = count;
        }

        return result;
    }

    /// <summary>
    /// Save needed item progress count
    /// </summary>
    public async Task SaveNeededItemProgressAsync(string id, int count, ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        if (count <= 0)
        {
            var deleteSql = "DELETE FROM NeededItemProgress WHERE Id = @id AND ProfileType = @profileType";
            await using var deleteCmd = new SqliteCommand(deleteSql, connection);
            deleteCmd.Parameters.AddWithValue("@id", id);
            deleteCmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
            await deleteCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            return;
        }

        var sql = @"
            INSERT INTO NeededItemProgress (Id, ProfileType, Count, UpdatedAt)
            VALUES (@id, @profileType, @count, @updatedAt)
            ON CONFLICT(Id, ProfileType) DO UPDATE SET
                Count = @count,
                UpdatedAt = @updatedAt";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
        cmd.Parameters.AddWithValue("@count", count);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Save needed item progress counts in batch
    /// </summary>
    public async Task SaveNeededItemProgressBatchAsync(IEnumerable<(string Id, int Count)> items, ProfileType? profileType = null)
    {
        if (items == null) return;
        var itemsList = items as IList<(string Id, int Count)> ?? items.ToList();
        if (itemsList.Count == 0) return;

        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync().ConfigureAwait(false);

        try
        {
            foreach (var (id, count) in itemsList)
            {
                if (count <= 0)
                {
                    var deleteSql = "DELETE FROM NeededItemProgress WHERE Id = @id AND ProfileType = @profileType";
                    await using var deleteCmd = new SqliteCommand(deleteSql, connection, (SqliteTransaction)tx);
                    deleteCmd.Parameters.AddWithValue("@id", id);
                    deleteCmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
                    await deleteCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                else
                {
                    var sql = @"
                        INSERT INTO NeededItemProgress (Id, ProfileType, Count, UpdatedAt)
                        VALUES (@id, @profileType, @count, @updatedAt)
                        ON CONFLICT(Id, ProfileType) DO UPDATE SET
                            Count = @count,
                            UpdatedAt = @updatedAt";

                    await using var cmd = new SqliteCommand(sql, connection, (SqliteTransaction)tx);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
                    cmd.Parameters.AddWithValue("@count", count);
                    cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            await tx.CommitAsync().ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Delete needed item progress for an ID
    /// </summary>
    public async Task DeleteNeededItemProgressAsync(string id, ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var sql = "DELETE FROM NeededItemProgress WHERE Id = @id AND ProfileType = @profileType";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Clear all needed item progress
    /// </summary>
    public async Task ClearAllNeededItemProgressAsync(ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var cmd = new SqliteCommand("DELETE FROM NeededItemProgress WHERE ProfileType = @profileType", connection);
        cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    #endregion

    #region JSON Migration

    /// <summary>
    /// 기존 JSON ?�일?�을 DB�?마이그레?�션
    /// </summary>
    public async Task<bool> MigrateFromJsonAsync()
    {
        if (!NeedsMigration())
        {
            return false;
        }

        ReportProgress("Starting data migration...");
        var migrated = false;

        // Quest Progress migration
        ReportProgress("Migrating quest progress data...");
        migrated |= await MigrateQuestProgressJsonAsync();

        // Objective Progress migration
        ReportProgress("Migrating objective progress data...");
        migrated |= await MigrateObjectiveProgressJsonAsync();

        // Hideout Progress migration
        ReportProgress("Migrating hideout progress data...");
        migrated |= await MigrateHideoutProgressJsonAsync();

        // Item Inventory migration
        ReportProgress("Migrating item inventory data...");
        migrated |= await MigrateItemInventoryJsonAsync();

        if (migrated)
        {
            ReportProgress("Data migration completed!");
        }

        return migrated;
    }

    private async Task<bool> MigrateQuestProgressJsonAsync()
    {
        // V2 ?�일 먼�? ?�인
        var v2Path = Path.Combine(AppEnv.ConfigPath, "quest_progress_v2.json");
        var v1Path = Path.Combine(AppEnv.ConfigPath, "quest_progress.json");

        if (File.Exists(v2Path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(v2Path);
                var v2Data = JsonSerializer.Deserialize<QuestProgressDataV2>(json);

                if (v2Data != null)
                {
                    await InitializeAsync();

                    foreach (var entry in v2Data.CompletedQuests)
                    {
                        if (entry.IsValid)
                        {
                            await SaveQuestProgressAsync(
                                entry.Id ?? entry.NormalizedName!,
                                entry.NormalizedName,
                                QuestStatus.Done);
                        }
                    }

                    foreach (var entry in v2Data.FailedQuests)
                    {
                        if (entry.IsValid)
                        {
                            await SaveQuestProgressAsync(
                                entry.Id ?? entry.NormalizedName!,
                                entry.NormalizedName,
                                QuestStatus.Failed);
                        }
                    }

                    // 마이그레?�션 ?�료 ???�일 ??��
                    File.Delete(v2Path);
                    System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Migrated and deleted: {v2Path}");

                    // V1 ?�일???�으�???��
                    if (File.Exists(v1Path))
                    {
                        File.Delete(v1Path);
                        System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Deleted legacy: {v1Path}");
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserDataDbService] V2 migration failed: {ex.Message}");
            }
        }
        else if (File.Exists(v1Path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(v1Path);
                var v1Data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                if (v1Data != null)
                {
                    await InitializeAsync();

                    foreach (var kvp in v1Data)
                    {
                        if (Enum.TryParse<QuestStatus>(kvp.Value, out var status))
                        {
                            await SaveQuestProgressAsync(kvp.Key, kvp.Key, status);
                        }
                    }

                    // 마이그레?�션 ?�료 ???�일 ??��
                    File.Delete(v1Path);
                    System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Migrated and deleted: {v1Path}");

                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserDataDbService] V1 migration failed: {ex.Message}");
            }
        }

        return false;
    }

    private async Task<bool> MigrateObjectiveProgressJsonAsync()
    {
        var filePath = Path.Combine(AppEnv.ConfigPath, "objective_progress.json");

        if (!File.Exists(filePath))
            return false;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);

            if (data != null)
            {
                await InitializeAsync();

                foreach (var kvp in data)
                {
                    // ???�식: "questName:index" ?�는 "id:objectiveId"
                    string? questId = null;
                    if (kvp.Key.Contains(':'))
                    {
                        var parts = kvp.Key.Split(':');
                        if (parts[0] != "id")
                        {
                            questId = parts[0];
                        }
                    }

                    await SaveObjectiveProgressAsync(kvp.Key, questId, kvp.Value);
                }

                // 마이그레?�션 ?�료 ???�일 ??��
                File.Delete(filePath);
                System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Migrated and deleted: {filePath}");

                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Objective migration failed: {ex.Message}");
        }

        return false;
    }

    private async Task<bool> MigrateHideoutProgressJsonAsync()
    {
        var filePath = Path.Combine(AppEnv.ConfigPath, "hideout_progress.json");

        if (!File.Exists(filePath))
            return false;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            Dictionary<string, int>? modules = null;

            // Try new format first: {"version": 1, "lastUpdated": "...", "modules": {...}}
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("modules", out var modulesElement))
                {
                    modules = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in modulesElement.EnumerateObject())
                    {
                        if (prop.Value.TryGetInt32(out var level))
                        {
                            modules[prop.Name] = level;
                        }
                    }
                }
            }
            catch
            {
                // Fall back to old format: {"stationId": level, ...}
                modules = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            }

            if (modules != null && modules.Count > 0)
            {
                await InitializeAsync();

                foreach (var kvp in modules)
                {
                    await SaveHideoutProgressAsync(kvp.Key, kvp.Value);
                }

                // 마이그레?�션 ?�료 ???�일 ??��
                File.Delete(filePath);
                System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Migrated and deleted: {filePath}");

                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Hideout migration failed: {ex.Message}");
        }

        return false;
    }

    private async Task<bool> MigrateItemInventoryJsonAsync()
    {
        var filePath = Path.Combine(AppEnv.ConfigPath, "item_inventory.json");

        if (!File.Exists(filePath))
            return false;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var data = JsonSerializer.Deserialize<ItemInventoryData>(json, options);

            if (data != null && data.Items.Count > 0)
            {
                await InitializeAsync();

                foreach (var kvp in data.Items)
                {
                    var inventory = kvp.Value;
                    await SaveItemInventoryAsync(
                        kvp.Key,
                        inventory.FirQuantity,
                        inventory.NonFirQuantity);
                }

                // 마이그레?�션 ?�료 ???�일 ??��
                File.Delete(filePath);
                System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Migrated and deleted: {filePath}");

                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserDataDbService] ItemInventory migration failed: {ex.Message}");
        }

        return false;
    }

    #endregion

    #region User Settings (Safe & Unified)

    /// <summary>
    /// ?�정 �?조회 (비동�?
    /// </summary>
    public async Task<string?> GetSettingAsync(string key, ProfileType? profileType = null)
    {
        await InitializeAsync();
        return GetSetting(key, profileType);
    }

    /// <summary>
    /// ?�정 �??�??(비동�?
    /// </summary>
    public async Task SetSettingAsync(string key, string value, ProfileType? profileType = null)
    {
        await InitializeAsync();
        SetSetting(key, value, profileType);
    }

    /// <summary>
    /// ?�정 �?조회 (?�기 버전 - 모든 코드??중심)
    /// </summary>
    public string? GetSetting(string key, ProfileType? profileType = null)
    {
        EnsureInitialized();
        // profileType??null?�면 ?�로??무�? ?�역 ?�정(99)?�로 간주?�니??
        var actualProfileType = profileType.HasValue ? (int)profileType.Value : 99;
        var connectionString = GetConnectionString();

        lock (_dbLock)
        {
            int retryCount = 0;
            while (retryCount < 3)
            {
                try
                {
                    using (var connection = new SqliteConnection(connectionString))
                    {
                        connection.Open();
                        var sql = "SELECT Value FROM UserSettings WHERE Key = @key AND ProfileType = @profileType";
                        using (var cmd = new SqliteCommand(sql, connection))
                        {
                            cmd.Parameters.AddWithValue("@key", key);
                            cmd.Parameters.AddWithValue("@profileType", actualProfileType);
                            var result = cmd.ExecuteScalar();
                            return result?.ToString();
                        }
                    }
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 14)
                {
                    retryCount++;
                    if (retryCount >= 3) throw;
                    SqliteConnection.ClearAllPools();
                    System.Threading.Thread.Sleep(200);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[UserDataDbService] GetSetting Fatal Error: {key}, {ex.Message}");
                    return null;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Batch load all settings for a given profile type (and global 99) in a single query.
    /// </summary>
    public Dictionary<string, string> GetAllSettings(ProfileType? profileType = null)
    {
        EnsureInitialized();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var actualProfileType = profileType.HasValue ? (int)profileType.Value : 99;
        var connectionString = GetConnectionString();

        lock (_dbLock)
        {
            try
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                var sql = "SELECT Key, Value, ProfileType FROM UserSettings WHERE ProfileType IN (99, @profileType) ORDER BY CASE WHEN ProfileType = 99 THEN 0 ELSE 1 END";
                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@profileType", actualProfileType);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var k = reader.IsDBNull(0) ? null : reader.GetString(0);
                    var v = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    if (!string.IsNullOrEmpty(k))
                    {
                        result[k] = v;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserDataDbService] GetAllSettings Error: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// ?�정 �??�??(?�기 버전 - 모든 코드??중심)
    /// </summary>
    public void SetSetting(string key, string value, ProfileType? profileType = null)
    {
        EnsureInitialized();
        // profileType??null?�면 ?�로??무�? ?�역 ?�정(99)?�로 간주?�니??
        var actualProfileType = profileType.HasValue ? (int)profileType.Value : 99;
        var connectionString = GetConnectionString();

        lock (_dbLock)
        {
            int retryCount = 0;
            while (retryCount < 3)
            {
                try
                {
                    using (var connection = new SqliteConnection(connectionString))
                    {
                        connection.Open();
                        var sql = @"
                        INSERT INTO UserSettings (Key, ProfileType, Value) 
                        VALUES (@key, @profileType, @value)
                        ON CONFLICT(Key, ProfileType) DO UPDATE SET Value = @value";

                        using (var cmd = new SqliteCommand(sql, connection))
                        {
                            cmd.Parameters.AddWithValue("@key", key);
                            cmd.Parameters.AddWithValue("@profileType", actualProfileType);
                            cmd.Parameters.AddWithValue("@value", value);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    break;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 14)
                {
                    retryCount++;
                    if (retryCount >= 3) break;
                    SqliteConnection.ClearAllPools();
                    System.Threading.Thread.Sleep(200);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[UserDataDbService] SetSetting Fatal Error: {key}={value}, {ex.Message}");
                    break;
                }
            }
        }
    }

    /// <summary>
    /// ?�정 �???�� (?�기 버전)
    /// </summary>
    public void DeleteSetting(string key, ProfileType? profileType = null)
    {
        EnsureInitialized();
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;
        var connectionString = GetConnectionString();

        lock (_dbLock)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            var sql = "DELETE FROM UserSettings WHERE Key = @key AND ProfileType = @profileType";
            using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
            cmd.ExecuteNonQuery();
        }
    }

    #endregion

    #region Raid History (Safe)

    public async Task SaveRaidHistoryAsync(Models.EftRaidInfo raid)
    {
        await InitializeAsync();
        var connectionString = GetConnectionString();

        lock (_dbLock)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            var sql = @"
                INSERT INTO RaidHistory (
                    RaidId, SessionId, ShortId, ProfileId, RaidType, GameMode,
                    MapName, MapKey, ServerIp, ServerPort, IsParty, PartyLeaderAccountId,
                    StartTime, EndTime, DurationSeconds, Rtt, PacketLoss, PacketsSent, PacketsReceived
                ) VALUES (
                    @raidId, @sessionId, @shortId, @profileId, @raidType, @gameMode,
                    @mapName, @mapKey, @serverIp, @serverPort, @isParty, @partyLeaderId,
                    @startTime, @endTime, @durationSeconds, @rtt, @packetLoss, @packetsSent, @packetsReceived
                )";

            using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@raidId", raid.RaidId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@sessionId", raid.SessionId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@shortId", raid.ShortId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@profileId", raid.ProfileId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@raidType", (int)raid.RaidType);
            cmd.Parameters.AddWithValue("@gameMode", (int)raid.GameMode);
            cmd.Parameters.AddWithValue("@mapName", raid.MapName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@mapKey", raid.MapKey ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@serverIp", raid.ServerIp ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@serverPort", raid.ServerPort);
            cmd.Parameters.AddWithValue("@isParty", raid.IsParty ? 1 : 0);
            cmd.Parameters.AddWithValue("@partyLeaderId", raid.PartyLeaderAccountId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@startTime", raid.StartTime?.ToString("o") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@endTime", raid.EndTime?.ToString("o") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@durationSeconds", raid.Duration?.TotalSeconds ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@rtt", raid.Rtt ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@packetLoss", raid.PacketLoss ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@packetsSent", raid.PacketsSent ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@packetsReceived", raid.PacketsReceived ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public async Task<List<Models.EftRaidInfo>> GetRaidHistoryAsync(int limit = 100, Models.RaidType? raidType = null, string? mapKey = null)
    {
        await InitializeAsync();
        var connectionString = GetConnectionString();
        var result = new List<Models.EftRaidInfo>();

        lock (_dbLock)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            var whereConditions = new List<string>();
            if (raidType.HasValue) whereConditions.Add("RaidType = @raidType");
            if (!string.IsNullOrEmpty(mapKey)) whereConditions.Add("MapKey = @mapKey");
            var whereClause = whereConditions.Count > 0 ? $"WHERE {string.Join(" AND ", whereConditions)}" : "";

            var sql = $@"
                SELECT RaidId, SessionId, ShortId, ProfileId, RaidType, GameMode,
                       MapName, MapKey, ServerIp, ServerPort, IsParty, PartyLeaderAccountId,
                       StartTime, EndTime, Rtt, PacketLoss, PacketsSent, PacketsReceived
                FROM RaidHistory
                {whereClause}
                ORDER BY StartTime DESC
                LIMIT @limit";

            using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@limit", limit);
            if (raidType.HasValue) cmd.Parameters.AddWithValue("@raidType", (int)raidType.Value);
            if (!string.IsNullOrEmpty(mapKey)) cmd.Parameters.AddWithValue("@mapKey", mapKey);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var raid = new Models.EftRaidInfo
                {
                    RaidId = reader.IsDBNull(0) ? null : reader.GetString(0),
                    SessionId = reader.IsDBNull(1) ? null : reader.GetString(1),
                    ShortId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    ProfileId = reader.IsDBNull(3) ? null : reader.GetString(3),
                    RaidType = (Models.RaidType)reader.GetInt32(4),
                    GameMode = (Models.GameMode)reader.GetInt32(5),
                    MapName = reader.IsDBNull(6) ? null : reader.GetString(6),
                    MapKey = reader.IsDBNull(7) ? null : reader.GetString(7),
                    ServerIp = reader.IsDBNull(8) ? null : reader.GetString(8),
                    ServerPort = reader.GetInt32(9),
                    IsParty = reader.GetInt32(10) == 1,
                    PartyLeaderAccountId = reader.IsDBNull(11) ? null : reader.GetString(11),
                    StartTime = reader.IsDBNull(12) ? null : DateTime.Parse(reader.GetString(12)),
                    EndTime = reader.IsDBNull(13) ? null : DateTime.Parse(reader.GetString(13)),
                    Rtt = reader.IsDBNull(14) ? null : reader.GetDouble(14),
                    PacketLoss = reader.IsDBNull(15) ? null : reader.GetDouble(15),
                    PacketsSent = reader.IsDBNull(16) ? null : reader.GetInt64(16),
                    PacketsReceived = reader.IsDBNull(17) ? null : reader.GetInt64(17)
                };
                result.Add(raid);
            }
        }
        return result;
    }

    #endregion

    #region Custom Map Markers (Safe)

    public async Task<List<CustomMapMarker>> LoadCustomMarkersAsync(string mapKey, ProfileType? profileType = null)
    {
        await InitializeAsync();
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;
        var connectionString = GetConnectionString();
        var result = new List<CustomMapMarker>();

        lock (_dbLock)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            var sql = "SELECT Id, MapKey, Name, X, Y, Z, FloorId, Color, Size, Opacity, CreatedAt FROM CustomMapMarkers WHERE MapKey = @mapKey AND ProfileType = @profileType";
            using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@mapKey", mapKey);
            cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new CustomMapMarker
                {
                    Id = reader.GetString(0),
                    MapKey = reader.GetString(1),
                    Name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    X = reader.GetDouble(3),
                    Y = reader.GetDouble(4),
                    Z = reader.GetDouble(5),
                    FloorId = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Color = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Size = reader.GetDouble(8),
                    Opacity = reader.IsDBNull(9) ? 1.0 : reader.GetDouble(9),
                    CreatedAt = DateTime.Parse(reader.GetString(10))
                });
            }
        }
        return result;
    }

    public async Task SaveCustomMarkerAsync(CustomMapMarker marker, ProfileType? profileType = null)
    {
        await InitializeAsync();
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;
        var connectionString = GetConnectionString();

        lock (_dbLock)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var sql = @"
                    INSERT INTO CustomMapMarkers (Id, ProfileType, MapKey, Name, X, Y, Z, FloorId, Color, Size, Opacity, CreatedAt)
                    VALUES (@id, @profileType, @mapKey, @name, @x, @y, @z, @floorId, @color, @size, @opacity, @createdAt)
                    ON CONFLICT(Id, ProfileType) DO UPDATE SET
                        MapKey = @mapKey, Name = @name, X = @x, Y = @y, Z = @z, FloorId = @floorId, Color = @color, Size = @size, Opacity = @opacity, CreatedAt = @createdAt";

                using (var cmd = new SqliteCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@id", marker.Id);
                    cmd.Parameters.AddWithValue("@profileType", (int)actualProfileType);
                    cmd.Parameters.AddWithValue("@mapKey", marker.MapKey);
                    cmd.Parameters.AddWithValue("@name", marker.Name ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@x", marker.X);
                    cmd.Parameters.AddWithValue("@y", marker.Y);
                    cmd.Parameters.AddWithValue("@z", marker.Z);
                    cmd.Parameters.AddWithValue("@floorId", marker.FloorId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@color", marker.Color ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@size", marker.Size);
                    cmd.Parameters.AddWithValue("@opacity", marker.Opacity);
                    cmd.Parameters.AddWithValue("@createdAt", marker.CreatedAt.ToString("o"));
                    cmd.ExecuteNonQuery();
                }
            }
        }
        CustomMarkersChanged?.Invoke();
    }

    /// <summary>
    /// 커스?� 마커 ??��
    /// </summary>
    public async Task DeleteCustomMarkerAsync(string id)
    {
        await InitializeAsync();
        var profileType = ProfileService.Instance.CurrentProfile;

        var connectionString = GetConnectionString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM CustomMapMarkers WHERE Id = @id AND ProfileType = @profileType";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@profileType", (int)profileType);

        await cmd.ExecuteNonQueryAsync();
        CustomMarkersChanged?.Invoke();
    }

    #endregion
}

