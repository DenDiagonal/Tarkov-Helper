using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovHelper.Debug;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services;

/// <summary>
/// 사용자 데이터 SQLite DB (user_data.db) 저장 및 로드 서비스.
/// 퀘스트 진행, 목표 완료, 하이드아웃 진행, 아이템 인벤토리 등을 관리합니다.
/// </summary>
public sealed class UserDataDbService
{
    private static readonly ILogger _log = Log.For<UserDataDbService>();
    private static readonly Lazy<UserDataDbService> _lazyInstance =
        new(() => new UserDataDbService(), LazyThreadSafetyMode.ExecutionAndPublication);
    public static UserDataDbService Instance => _lazyInstance.Value;

    private readonly string _databasePath;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private volatile bool _isInitialized;

    public bool IsInitialized => _isInitialized;
    public string DatabasePath => _databasePath;

    public event Action<string>? MigrationProgress;
    public event Action? CustomMarkersChanged;

    private const string SchemaDdlSql = @"
        CREATE TABLE IF NOT EXISTS QuestProgress (
            ProfileType INTEGER NOT NULL DEFAULT 0,
            Id TEXT NOT NULL CHECK(length(Id) > 0),
            NormalizedName TEXT,
            Status TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL,
            PRIMARY KEY (ProfileType, Id)
        );

        CREATE TABLE IF NOT EXISTS ObjectiveProgress (
            ProfileType INTEGER NOT NULL DEFAULT 0,
            Id TEXT NOT NULL CHECK(length(Id) > 0),
            QuestId TEXT,
            IsCompleted INTEGER NOT NULL DEFAULT 0,
            UpdatedAt TEXT NOT NULL,
            PRIMARY KEY (ProfileType, Id)
        );

        CREATE TABLE IF NOT EXISTS ItemInventory (
            ProfileType INTEGER NOT NULL DEFAULT 0,
            ItemNormalizedName TEXT NOT NULL CHECK(length(ItemNormalizedName) > 0),
            FirQuantity INTEGER NOT NULL DEFAULT 0,
            NonFirQuantity INTEGER NOT NULL DEFAULT 0,
            UpdatedAt TEXT NOT NULL,
            PRIMARY KEY (ProfileType, ItemNormalizedName)
        );

        CREATE TABLE IF NOT EXISTS HideoutProgress (
            ProfileType INTEGER NOT NULL DEFAULT 0,
            StationId TEXT NOT NULL CHECK(length(StationId) > 0),
            Level INTEGER NOT NULL DEFAULT 0,
            UpdatedAt TEXT NOT NULL,
            PRIMARY KEY (ProfileType, StationId)
        );

        CREATE TABLE IF NOT EXISTS UserSettings (
            ProfileType INTEGER NOT NULL DEFAULT 0,
            Key TEXT NOT NULL CHECK(length(Key) > 0),
            Value TEXT NOT NULL,
            PRIMARY KEY (ProfileType, Key)
        );

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
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            ExtractedName TEXT,
            ExtractionType TEXT,
            ExitStatus TEXT,
            Survived INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS CustomMapMarkers (
            ProfileType INTEGER NOT NULL DEFAULT 0,
            Id TEXT NOT NULL CHECK(length(Id) > 0),
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
            PRIMARY KEY (ProfileType, Id)
        );

        CREATE TABLE IF NOT EXISTS NeededItemProgress (
            ProfileType INTEGER NOT NULL DEFAULT 0,
            Id TEXT NOT NULL CHECK(length(Id) > 0),
            Count INTEGER NOT NULL DEFAULT 0,
            UpdatedAt TEXT NOT NULL,
            PRIMARY KEY (ProfileType, Id)
        );

        CREATE INDEX IF NOT EXISTS idx_quest_progress_profile_norm ON QuestProgress(ProfileType, NormalizedName);
        CREATE INDEX IF NOT EXISTS idx_objective_progress_profile_quest ON ObjectiveProgress(ProfileType, QuestId);
        CREATE INDEX IF NOT EXISTS idx_custom_markers_profile_map ON CustomMapMarkers(ProfileType, MapKey);

        CREATE INDEX IF NOT EXISTS idx_raid_history_type_map_start ON RaidHistory(RaidType, MapKey, StartTime DESC);
        CREATE INDEX IF NOT EXISTS idx_raid_history_type_start ON RaidHistory(RaidType, StartTime DESC);
        CREATE INDEX IF NOT EXISTS idx_raid_history_map_start ON RaidHistory(MapKey, StartTime DESC);
        CREATE INDEX IF NOT EXISTS idx_raid_history_session ON RaidHistory(SessionId);
    ";

    private UserDataDbService()
    {
        _databasePath = Path.Combine(AppEnv.ConfigPath, "user_data.db");
    }

    private string GetConnectionString(bool readOnly = false)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 15,
            Pooling = true,
            Cache = SqliteCacheMode.Private
        };
        return builder.ConnectionString;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(bool readOnly = false, CancellationToken ct = default)
    {
        var connection = new SqliteConnection(GetConnectionString(readOnly));
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var pragmaCmd = connection.CreateCommand();
            pragmaCmd.CommandText = readOnly
                ? "PRAGMA busy_timeout = 5000;"
                : "PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;";
            await pragmaCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task EnableWalModeAsync(SqliteConnection connection, CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA wal_autocheckpoint = 1000;
            PRAGMA synchronous = NORMAL;
            PRAGMA temp_store = MEMORY;";
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public void ClearOwnPoolOnly()
    {
        try
        {
            using var rwConn = new SqliteConnection(GetConnectionString(readOnly: false));
            SqliteConnection.ClearPool(rwConn);
            using var roConn = new SqliteConnection(GetConnectionString(readOnly: true));
            SqliteConnection.ClearPool(roConn);
        }
        catch { }
    }

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
        _log.Debug($"[Migration] {message}");
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_isInitialized) return;

        await _initGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_isInitialized) return;
            await InitializeCoreAsync(ct).ConfigureAwait(false);
            _isInitialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public Task EnsureDatabaseSchemaAsync() => InitializeAsync();

    public void EnsureInitialized()
    {
        if (_isInitialized) return;

        _initGate.Wait();
        try
        {
            if (_isInitialized) return;
            Task.Run(() => InitializeCoreAsync(CancellationToken.None)).GetAwaiter().GetResult();
            _isInitialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private async Task InitializeCoreAsync(CancellationToken ct)
    {
        var dbExistedBeforeOpen = File.Exists(_databasePath) && new FileInfo(_databasePath).Length > 0;
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

        await using var connection = await OpenConnectionAsync(readOnly: false, ct).ConfigureAwait(false);
        await EnableWalModeAsync(connection, ct).ConfigureAwait(false);

        // Solo realizar backup en arranque si la BD ya existía con datos y no hay backup reciente (<12h)
        if (dbExistedBeforeOpen)
        {
            var bakPath = _databasePath + ".bak";
            var needsBackup = !File.Exists(bakPath) ||
                (DateTime.UtcNow - File.GetLastWriteTimeUtc(bakPath)) > TimeSpan.FromHours(12);
            if (needsBackup)
            {
                await BackupDatabaseSafeOnlineAsync(connection).ConfigureAwait(false);
            }
        }

        await MigrateToProfileSystemTransactionalAsync(connection, ct).ConfigureAwait(false);

        await using (var cmd = new SqliteCommand(SchemaDdlSql, connection))
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await MigrateLegacyBase64QuestProgressAsync(connection).ConfigureAwait(false);
        CustomMarkersChanged?.Invoke();
    }

    private async Task BackupDatabaseSafeOnlineAsync(SqliteConnection sourceConnection)
    {
        if (!File.Exists(_databasePath)) return;

        try
        {
            var tempBackupPath = _databasePath + ".bak.tmp";
            var finalBackupPath = _databasePath + ".bak";

            if (File.Exists(tempBackupPath))
            {
                try { File.Delete(tempBackupPath); } catch { }
            }

            var destBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = tempBackupPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            };

            using (var destination = new SqliteConnection(destBuilder.ConnectionString))
            {
                await destination.OpenAsync().ConfigureAwait(false);
                sourceConnection.BackupDatabase(destination);
            }

            File.Move(tempBackupPath, finalBackupPath, overwrite: true);
            _log.Debug($"Consistent ACID database backup created: {finalBackupPath}");
        }
        catch (Exception ex)
        {
            _log.Warning($"Online SQLite backup warning: {ex.Message}");
        }
    }

    private static async Task ExecuteNonQueryInTxAsync(SqliteConnection connection, SqliteTransaction tx, string sql, CancellationToken ct)
    {
        await using var cmd = new SqliteCommand(sql, connection, tx);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task MigrateToProfileSystemTransactionalAsync(SqliteConnection connection, CancellationToken ct = default)
    {
        (string Table, string TargetCols, string Projection)[] targets =
        [
            ("QuestProgress", "(ProfileType, Id, NormalizedName, Status, UpdatedAt)", "0, COALESCE(Id, ''), NormalizedName, Status, UpdatedAt"),
            ("ObjectiveProgress", "(ProfileType, Id, QuestId, IsCompleted, UpdatedAt)", "0, COALESCE(Id, ''), QuestId, IsCompleted, UpdatedAt"),
            ("ItemInventory", "(ProfileType, ItemNormalizedName, FirQuantity, NonFirQuantity, UpdatedAt)", "0, COALESCE(ItemNormalizedName, ''), FirQuantity, NonFirQuantity, UpdatedAt"),
            ("HideoutProgress", "(ProfileType, StationId, Level, UpdatedAt)", "0, COALESCE(StationId, ''), Level, UpdatedAt"),
            ("UserSettings", "(ProfileType, Key, Value)", "0, COALESCE(Key, ''), Value"),
            ("CustomMapMarkers", "(ProfileType, Id, MapKey, Name, X, Y, Z, FloorId, Color, Size, Opacity, CreatedAt)", "0, COALESCE(Id, ''), MapKey, Name, X, Y, Z, FloorId, Color, Size, Opacity, CreatedAt"),
            ("NeededItemProgress", "(ProfileType, Id, Count, UpdatedAt)", "0, COALESCE(Id, ''), Count, UpdatedAt")
        ];

        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var (table, targetCols, projection) in targets)
            {
                await using var checkCmd = new SqliteCommand(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;", connection, tx);
                checkCmd.Parameters.Add("@name", SqliteType.Text).Value = table;
                if (Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct).ConfigureAwait(false)) == 0)
                    continue;

                // Si la columna ProfileType ya existe en la tabla, el esquema ya es multi-perfil y no requiere migración.
                await using var colCheckCmd = new SqliteCommand(
                    $"SELECT COUNT(*) FROM pragma_table_info(\"{table}\") WHERE name = 'ProfileType';", connection, tx);
                var hasProfileType = Convert.ToInt32(await colCheckCmd.ExecuteScalarAsync(ct).ConfigureAwait(false)) > 0;
                if (hasProfileType)
                    continue;

                _log.Info($"Migrating legacy single-profile {table} to multi-profile schema in atomic transaction...");

                await ExecuteNonQueryInTxAsync(connection, tx, $"ALTER TABLE \"{table}\" RENAME TO \"{table}_old\";", ct);
                await RecreateTableWithNewPkInTxAsync(table, connection, tx, ct);
                await ExecuteNonQueryInTxAsync(connection, tx, $"INSERT INTO \"{table}\" {targetCols} SELECT {projection} FROM \"{table}_old\";", ct);
                await ExecuteNonQueryInTxAsync(connection, tx, $"DROP TABLE \"{table}_old\";", ct);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            _log.Warning($"Transactional schema migration rollback: {ex.Message}");
        }
    }

    private static async Task RecreateTableWithNewPkInTxAsync(string tableName, SqliteConnection connection, SqliteTransaction tx, CancellationToken ct)
    {
        string sql = tableName switch
        {
            "QuestProgress" => @"
                CREATE TABLE QuestProgress (
                    ProfileType INTEGER NOT NULL DEFAULT 0,
                    Id TEXT NOT NULL CHECK(length(Id) > 0),
                    NormalizedName TEXT,
                    Status TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    PRIMARY KEY (ProfileType, Id))",
            "ObjectiveProgress" => @"
                CREATE TABLE ObjectiveProgress (
                    ProfileType INTEGER NOT NULL DEFAULT 0,
                    Id TEXT NOT NULL CHECK(length(Id) > 0),
                    QuestId TEXT,
                    IsCompleted INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT NOT NULL,
                    PRIMARY KEY (ProfileType, Id))",
            "ItemInventory" => @"
                CREATE TABLE ItemInventory (
                    ProfileType INTEGER NOT NULL DEFAULT 0,
                    ItemNormalizedName TEXT NOT NULL CHECK(length(ItemNormalizedName) > 0),
                    FirQuantity INTEGER NOT NULL DEFAULT 0,
                    NonFirQuantity INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT NOT NULL,
                    PRIMARY KEY (ProfileType, ItemNormalizedName))",
            "HideoutProgress" => @"
                CREATE TABLE HideoutProgress (
                    ProfileType INTEGER NOT NULL DEFAULT 0,
                    StationId TEXT NOT NULL CHECK(length(StationId) > 0),
                    Level INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT NOT NULL,
                    PRIMARY KEY (ProfileType, StationId))",
            "UserSettings" => @"
                CREATE TABLE UserSettings (
                    ProfileType INTEGER NOT NULL DEFAULT 0,
                    Key TEXT NOT NULL CHECK(length(Key) > 0),
                    Value TEXT NOT NULL,
                    PRIMARY KEY (ProfileType, Key))",
            "CustomMapMarkers" => @"
                CREATE TABLE CustomMapMarkers (
                    ProfileType INTEGER NOT NULL DEFAULT 0,
                    Id TEXT NOT NULL CHECK(length(Id) > 0),
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
                    PRIMARY KEY (ProfileType, Id))",
            "NeededItemProgress" => @"
                CREATE TABLE NeededItemProgress (
                    ProfileType INTEGER NOT NULL DEFAULT 0,
                    Id TEXT NOT NULL CHECK(length(Id) > 0),
                    Count INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT NOT NULL,
                    PRIMARY KEY (ProfileType, Id))",
            _ => throw new ArgumentException($"Unknown table: {tableName}")
        };

        await ExecuteNonQueryInTxAsync(connection, tx, sql, ct);
    }

    #region Quest Progress

    public async Task<Dictionary<string, QuestStatus>> LoadQuestProgressAsync(ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;
        var result = new Dictionary<string, QuestStatus>(StringComparer.OrdinalIgnoreCase);

        await using var connection = await OpenConnectionAsync(readOnly: true).ConfigureAwait(false);
        const string sql = "SELECT Id, NormalizedName, Status FROM QuestProgress WHERE ProfileType = @profileType";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            var normalizedName = reader.IsDBNull(1) ? null : reader.GetString(1);
            var statusStr = reader.GetString(2);

            if (Enum.TryParse<QuestStatus>(statusStr, out var status))
            {
                if (!string.IsNullOrEmpty(id)) result[id] = status;
                if (!string.IsNullOrEmpty(normalizedName)) result[normalizedName] = status;
            }
        }

        return result;
    }

    public async Task MigrateLegacyBase64QuestProgressAsync(SqliteConnection connection)
    {
        try
        {
            const string checkSql = "SELECT COUNT(*) FROM QuestProgress WHERE Id LIKE 'aHR0%'";
            await using (var checkCmd = new SqliteCommand(checkSql, connection))
            {
                var count = Convert.ToInt64(await checkCmd.ExecuteScalarAsync().ConfigureAwait(false));
                if (count == 0) return;
            }

            _log.Info("Migrating legacy Base64 quest IDs in QuestProgress to canonical BSG IDs...");
            const string selectSql = "SELECT Id, ProfileType, NormalizedName, Status, UpdatedAt FROM QuestProgress WHERE Id LIKE 'aHR0%'";
            var itemsToMigrate = new List<(string OldId, int ProfileType, string? NormalizedName, string Status, string UpdatedAt)>();

            await using (var selectCmd = new SqliteCommand(selectSql, connection))
            await using (var reader = await selectCmd.ExecuteReaderAsync().ConfigureAwait(false))
            {
                while (await reader.ReadAsync().ConfigureAwait(false))
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

            await using var tx = await connection.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                foreach (var item in itemsToMigrate)
                {
                    var newBsgId = CanonicalQuestMap.ResolveBsgId(item.OldId);
                    if (string.Equals(newBsgId, item.OldId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    const string checkExistingSql = "SELECT rowid FROM QuestProgress WHERE Id = @newId AND ProfileType = @profileType";
                    long existingRowId = 0;
                    await using (var chkCmd = new SqliteCommand(checkExistingSql, connection, (SqliteTransaction)tx))
                    {
                        chkCmd.Parameters.Add("@newId", SqliteType.Text).Value = newBsgId;
                        chkCmd.Parameters.Add("@profileType", SqliteType.Integer).Value = item.ProfileType;
                        var rId = await chkCmd.ExecuteScalarAsync().ConfigureAwait(false);
                        if (rId != null && rId != DBNull.Value) existingRowId = Convert.ToInt64(rId);
                    }

                    if (existingRowId > 0)
                    {
                        const string updateExistingSql = "UPDATE QuestProgress SET Status = @status, UpdatedAt = @updatedAt WHERE rowid = @rowid";
                        await using (var updateCmd = new SqliteCommand(updateExistingSql, connection, (SqliteTransaction)tx))
                        {
                            updateCmd.Parameters.Add("@status", SqliteType.Text).Value = item.Status;
                            updateCmd.Parameters.Add("@updatedAt", SqliteType.Text).Value = item.UpdatedAt;
                            updateCmd.Parameters.Add("@rowid", SqliteType.Integer).Value = existingRowId;
                            await updateCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }

                        const string deleteOldSql = "DELETE FROM QuestProgress WHERE Id = @oldId AND ProfileType = @profileType";
                        await using (var deleteCmd = new SqliteCommand(deleteOldSql, connection, (SqliteTransaction)tx))
                        {
                            deleteCmd.Parameters.Add("@oldId", SqliteType.Text).Value = item.OldId;
                            deleteCmd.Parameters.Add("@profileType", SqliteType.Integer).Value = item.ProfileType;
                            await deleteCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        const string updateInPlaceSql = "UPDATE QuestProgress SET Id = @newId WHERE Id = @oldId AND ProfileType = @profileType";
                        await using (var updateCmd = new SqliteCommand(updateInPlaceSql, connection, (SqliteTransaction)tx))
                        {
                            updateCmd.Parameters.Add("@newId", SqliteType.Text).Value = newBsgId;
                            updateCmd.Parameters.Add("@oldId", SqliteType.Text).Value = item.OldId;
                            updateCmd.Parameters.Add("@profileType", SqliteType.Integer).Value = item.ProfileType;
                            await updateCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }
                    }
                }

                await tx.CommitAsync().ConfigureAwait(false);
                _log.Info($"Successfully migrated {itemsToMigrate.Count} legacy Base64 quest IDs in user_data.db.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                _log.Error($"Failed to migrate legacy Base64 quest IDs: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Error in MigrateLegacyBase64QuestProgressAsync: {ex.Message}");
        }
    }

    public async Task SaveQuestProgressAsync(string id, string? normalizedName, QuestStatus status, ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        id = CanonicalQuestMap.ResolveBsgId(id);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);
        const string sql = @"
            INSERT INTO QuestProgress (ProfileType, Id, NormalizedName, Status, UpdatedAt)
            VALUES (@profileType, @id, @normalizedName, @status, @updatedAt)
            ON CONFLICT(ProfileType, Id) DO UPDATE SET
                NormalizedName = excluded.NormalizedName,
                Status = excluded.Status,
                UpdatedAt = excluded.UpdatedAt;";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        cmd.Parameters.Add("@id", SqliteType.Text).Value = id;
        cmd.Parameters.Add("@normalizedName", SqliteType.Text).Value = normalizedName ?? (object)DBNull.Value;
        cmd.Parameters.Add("@status", SqliteType.Text).Value = status.ToString();
        cmd.Parameters.Add("@updatedAt", SqliteType.Text).Value = DateTime.UtcNow.ToString("o");

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task SaveQuestProgressBatchAsync(IEnumerable<(string Id, string? NormalizedName, QuestStatus Status)> progressItems, ProfileType? profileType = null)
    {
        if (progressItems == null) return;
        var list = progressItems as IList<(string Id, string? NormalizedName, QuestStatus Status)> ?? progressItems.ToList();
        if (list.Count == 0) return;

        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        try
        {
            const string sql = @"
                INSERT INTO QuestProgress (ProfileType, Id, NormalizedName, Status, UpdatedAt)
                VALUES (@profileType, @id, @normalizedName, @status, @updatedAt)
                ON CONFLICT(ProfileType, Id) DO UPDATE SET
                    NormalizedName = excluded.NormalizedName,
                    Status = excluded.Status,
                    UpdatedAt = excluded.UpdatedAt;";

            await using var cmd = new SqliteCommand(sql, connection, (SqliteTransaction)transaction);
            var pProfile = cmd.Parameters.Add("@profileType", SqliteType.Integer);
            var pId = cmd.Parameters.Add("@id", SqliteType.Text);
            var pNorm = cmd.Parameters.Add("@normalizedName", SqliteType.Text);
            var pStatus = cmd.Parameters.Add("@status", SqliteType.Text);
            var pUpd = cmd.Parameters.Add("@updatedAt", SqliteType.Text);

            pProfile.Value = (int)actualProfileType;
            pUpd.Value = DateTime.UtcNow.ToString("o");
            await cmd.PrepareAsync().ConfigureAwait(false);

            foreach (var item in list)
            {
                if (string.IsNullOrWhiteSpace(item.Id)) continue;
                pId.Value = CanonicalQuestMap.ResolveBsgId(item.Id);
                pNorm.Value = item.NormalizedName ?? (object)DBNull.Value;
                pStatus.Value = item.Status.ToString();
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task DeleteQuestProgressAsync(string id, ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        id = CanonicalQuestMap.ResolveBsgId(id);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);
        const string sql = "DELETE FROM QuestProgress WHERE ProfileType = @profileType AND (Id = @id OR NormalizedName = @id)";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        cmd.Parameters.Add("@id", SqliteType.Text).Value = id;

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task ClearAllQuestProgressAsync(ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("DELETE FROM QuestProgress WHERE ProfileType = @profileType", connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    #endregion

    #region Objective Progress

    public async Task<Dictionary<string, bool>> LoadObjectiveProgressAsync(ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfile = profileType ?? ProfileService.Instance.CurrentProfile;
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        await using var connection = await OpenConnectionAsync(readOnly: true).ConfigureAwait(false);
        const string sql = "SELECT Id, IsCompleted FROM ObjectiveProgress WHERE ProfileType = @profileType";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfile;
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            var isCompleted = reader.GetInt32(1) == 1;
            result[id] = isCompleted;
        }

        return result;
    }

    public async Task SaveObjectiveProgressBatchAsync(IEnumerable<(string Key, string? QuestId, bool IsCompleted)> items, ProfileType? profileType = null)
    {
        if (items == null) return;
        var list = items as IList<(string Key, string? QuestId, bool IsCompleted)> ?? items.ToList();
        if (list.Count == 0) return;

        await InitializeAsync().ConfigureAwait(false);
        var actualProfile = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync().ConfigureAwait(false);

        try
        {
            const string delSql = "DELETE FROM ObjectiveProgress WHERE ProfileType = @profileType AND Id = @id;";
            await using var delCmd = new SqliteCommand(delSql, connection, (SqliteTransaction)tx);
            var dProfile = delCmd.Parameters.Add("@profileType", SqliteType.Integer);
            var dId = delCmd.Parameters.Add("@id", SqliteType.Text);
            dProfile.Value = (int)actualProfile;
            await delCmd.PrepareAsync().ConfigureAwait(false);

            const string upsertSql = @"
                INSERT INTO ObjectiveProgress (ProfileType, Id, QuestId, IsCompleted, UpdatedAt)
                VALUES (@profileType, @id, @questId, 1, @updatedAt)
                ON CONFLICT(ProfileType, Id) DO UPDATE SET
                    QuestId = excluded.QuestId,
                    IsCompleted = 1,
                    UpdatedAt = excluded.UpdatedAt;";
            await using var upsertCmd = new SqliteCommand(upsertSql, connection, (SqliteTransaction)tx);
            var uProfile = upsertCmd.Parameters.Add("@profileType", SqliteType.Integer);
            var uId = upsertCmd.Parameters.Add("@id", SqliteType.Text);
            var uQuest = upsertCmd.Parameters.Add("@questId", SqliteType.Text);
            var uUpd = upsertCmd.Parameters.Add("@updatedAt", SqliteType.Text);
            uProfile.Value = (int)actualProfile;
            uUpd.Value = DateTime.UtcNow.ToString("o");
            await upsertCmd.PrepareAsync().ConfigureAwait(false);

            foreach (var (key, questId, isCompleted) in list)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;

                if (!isCompleted)
                {
                    dId.Value = key;
                    await delCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                else
                {
                    uId.Value = key;
                    uQuest.Value = questId ?? (object)DBNull.Value;
                    await upsertCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
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

    public async Task SaveObjectiveProgressAsync(string id, string? questId, bool isCompleted, ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);
        const string sql = @"
            INSERT INTO ObjectiveProgress (ProfileType, Id, QuestId, IsCompleted, UpdatedAt)
            VALUES (@profileType, @id, @questId, @isCompleted, @updatedAt)
            ON CONFLICT(ProfileType, Id) DO UPDATE SET
                QuestId = excluded.QuestId,
                IsCompleted = excluded.IsCompleted,
                UpdatedAt = excluded.UpdatedAt;";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        cmd.Parameters.Add("@id", SqliteType.Text).Value = id;
        cmd.Parameters.Add("@questId", SqliteType.Text).Value = questId ?? (object)DBNull.Value;
        cmd.Parameters.Add("@isCompleted", SqliteType.Integer).Value = isCompleted ? 1 : 0;
        cmd.Parameters.Add("@updatedAt", SqliteType.Text).Value = DateTime.UtcNow.ToString("o");

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task DeleteObjectiveProgressAsync(string id, ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);
        const string sql = "DELETE FROM ObjectiveProgress WHERE ProfileType = @profileType AND Id = @id";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        cmd.Parameters.Add("@id", SqliteType.Text).Value = id;

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static string EscapeSqliteLikeLiteral(string input)
    {
        return input
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
    }

    public async Task DeleteObjectiveProgressByQuestAsync(string questId, ProfileType? profileType = null)
    {
        if (string.IsNullOrWhiteSpace(questId)) return;

        await InitializeAsync().ConfigureAwait(false);
        var actualProfile = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);
        const string sql = @"
            DELETE FROM ObjectiveProgress
            WHERE ProfileType = @profileType
              AND (QuestId = @questId OR Id LIKE @pattern ESCAPE '\');";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfile;
        cmd.Parameters.Add("@questId", SqliteType.Text).Value = questId;
        cmd.Parameters.Add("@pattern", SqliteType.Text).Value = $"{EscapeSqliteLikeLiteral(questId)}:%";

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task ClearAllObjectiveProgressAsync(ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("DELETE FROM ObjectiveProgress WHERE ProfileType = @profileType", connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    #endregion

    #region Hideout Progress

    public async Task<Dictionary<string, int>> LoadHideoutProgressAsync(ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        await using var connection = await OpenConnectionAsync(readOnly: true).ConfigureAwait(false);
        const string sql = "SELECT StationId, Level FROM HideoutProgress WHERE ProfileType = @profileType";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var stationId = reader.GetString(0);
            var level = reader.GetInt32(1);
            result[stationId] = level;
        }

        return result;
    }

    public async Task SaveHideoutProgressAsync(string stationId, int level, ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);

        if (level == 0)
        {
            const string deleteSql = "DELETE FROM HideoutProgress WHERE ProfileType = @profileType AND StationId = @stationId";
            await using var deleteCmd = new SqliteCommand(deleteSql, connection);
            deleteCmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
            deleteCmd.Parameters.Add("@stationId", SqliteType.Text).Value = stationId;
            await deleteCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            return;
        }

        const string sql = @"
            INSERT INTO HideoutProgress (ProfileType, StationId, Level, UpdatedAt)
            VALUES (@profileType, @stationId, @level, @updatedAt)
            ON CONFLICT(ProfileType, StationId) DO UPDATE SET
                Level = excluded.Level,
                UpdatedAt = excluded.UpdatedAt;";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        cmd.Parameters.Add("@stationId", SqliteType.Text).Value = stationId;
        cmd.Parameters.Add("@level", SqliteType.Integer).Value = level;
        cmd.Parameters.Add("@updatedAt", SqliteType.Text).Value = DateTime.UtcNow.ToString("o");

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task ClearAllHideoutProgressAsync(ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("DELETE FROM HideoutProgress WHERE ProfileType = @profileType", connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    #endregion

    #region Item Inventory

    public async Task<Dictionary<string, (int FirQuantity, int NonFirQuantity)>> LoadItemInventoryAsync(ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;
        var result = new Dictionary<string, (int FirQuantity, int NonFirQuantity)>(StringComparer.OrdinalIgnoreCase);

        await using var connection = await OpenConnectionAsync(readOnly: true).ConfigureAwait(false);
        const string sql = "SELECT ItemNormalizedName, FirQuantity, NonFirQuantity FROM ItemInventory WHERE ProfileType = @profileType";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var itemName = reader.GetString(0);
            var firQty = reader.GetInt32(1);
            var nonFirQty = reader.GetInt32(2);
            result[itemName] = (firQty, nonFirQty);
        }

        return result;
    }

    public async Task SaveItemInventoryAsync(string itemNormalizedName, int firQuantity, int nonFirQuantity, ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);

        if (firQuantity == 0 && nonFirQuantity == 0)
        {
            const string deleteSql = "DELETE FROM ItemInventory WHERE ProfileType = @profileType AND ItemNormalizedName = @itemName";
            await using var deleteCmd = new SqliteCommand(deleteSql, connection);
            deleteCmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
            deleteCmd.Parameters.Add("@itemName", SqliteType.Text).Value = itemNormalizedName;
            await deleteCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            return;
        }

        const string sql = @"
            INSERT INTO ItemInventory (ProfileType, ItemNormalizedName, FirQuantity, NonFirQuantity, UpdatedAt)
            VALUES (@profileType, @itemName, @firQty, @nonFirQty, @updatedAt)
            ON CONFLICT(ProfileType, ItemNormalizedName) DO UPDATE SET
                FirQuantity = excluded.FirQuantity,
                NonFirQuantity = excluded.NonFirQuantity,
                UpdatedAt = excluded.UpdatedAt;";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        cmd.Parameters.Add("@itemName", SqliteType.Text).Value = itemNormalizedName;
        cmd.Parameters.Add("@firQty", SqliteType.Integer).Value = firQuantity;
        cmd.Parameters.Add("@nonFirQty", SqliteType.Integer).Value = nonFirQuantity;
        cmd.Parameters.Add("@updatedAt", SqliteType.Text).Value = DateTime.UtcNow.ToString("o");

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task SaveItemInventoryBatchAsync(
        IEnumerable<(string ItemNormalizedName, int FirQuantity, int NonFirQuantity)> items,
        ProfileType? profileType = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var list = items as IList<(string ItemNormalizedName, int FirQuantity, int NonFirQuantity)> ?? items.ToList();
        if (list.Count == 0) return;

        await InitializeAsync(ct).ConfigureAwait(false);
        int profile = (int)(profileType ?? ProfileService.Instance.CurrentProfile);
        string nowIso = DateTime.UtcNow.ToString("o");

        await using var connection = await OpenConnectionAsync(readOnly: false, ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            await using var upsertCmd = connection.CreateCommand();
            upsertCmd.Transaction = tx;
            upsertCmd.CommandText = @"
                INSERT INTO ItemInventory (ProfileType, ItemNormalizedName, FirQuantity, NonFirQuantity, UpdatedAt)
                VALUES (@profile, @name, @fir, @nonFir, @updatedAt)
                ON CONFLICT(ProfileType, ItemNormalizedName) DO UPDATE SET
                    FirQuantity = excluded.FirQuantity,
                    NonFirQuantity = excluded.NonFirQuantity,
                    UpdatedAt = excluded.UpdatedAt;";

            var pProf = upsertCmd.Parameters.Add("@profile", SqliteType.Integer);
            var pName = upsertCmd.Parameters.Add("@name", SqliteType.Text);
            var pFir = upsertCmd.Parameters.Add("@fir", SqliteType.Integer);
            var pNonFir = upsertCmd.Parameters.Add("@nonFir", SqliteType.Integer);
            var pUpd = upsertCmd.Parameters.Add("@updatedAt", SqliteType.Text);
            pProf.Value = profile;
            pUpd.Value = nowIso;
            await upsertCmd.PrepareAsync(ct).ConfigureAwait(false);

            await using var deleteCmd = connection.CreateCommand();
            deleteCmd.Transaction = tx;
            deleteCmd.CommandText = "DELETE FROM ItemInventory WHERE ProfileType = @profile AND ItemNormalizedName = @name;";
            var dProf = deleteCmd.Parameters.Add("@profile", SqliteType.Integer);
            var dName = deleteCmd.Parameters.Add("@name", SqliteType.Text);
            dProf.Value = profile;
            await deleteCmd.PrepareAsync(ct).ConfigureAwait(false);

            foreach (var (name, fir, nonFir) in list)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (fir <= 0 && nonFir <= 0)
                {
                    dName.Value = name;
                    await deleteCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    pName.Value = name;
                    pFir.Value = Math.Max(0, fir);
                    pNonFir.Value = Math.Max(0, nonFir);
                    await upsertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task ClearAllItemInventoryAsync(ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("DELETE FROM ItemInventory WHERE ProfileType = @profileType", connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    #endregion

    #region Needed Item Progress

    public async Task<Dictionary<string, int>> LoadNeededItemProgressAsync(ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        await using var connection = await OpenConnectionAsync(readOnly: true).ConfigureAwait(false);
        const string sql = "SELECT Id, Count FROM NeededItemProgress WHERE ProfileType = @profileType";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            var count = reader.GetInt32(1);
            result[id] = count;
        }

        return result;
    }

    public async Task SaveNeededItemProgressAsync(string id, int count, ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);

        if (count <= 0)
        {
            const string deleteSql = "DELETE FROM NeededItemProgress WHERE ProfileType = @profileType AND Id = @id";
            await using var deleteCmd = new SqliteCommand(deleteSql, connection);
            deleteCmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
            deleteCmd.Parameters.Add("@id", SqliteType.Text).Value = id;
            await deleteCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            return;
        }

        const string sql = @"
            INSERT INTO NeededItemProgress (ProfileType, Id, Count, UpdatedAt)
            VALUES (@profileType, @id, @count, @updatedAt)
            ON CONFLICT(ProfileType, Id) DO UPDATE SET
                Count = excluded.Count,
                UpdatedAt = excluded.UpdatedAt;";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        cmd.Parameters.Add("@id", SqliteType.Text).Value = id;
        cmd.Parameters.Add("@count", SqliteType.Integer).Value = count;
        cmd.Parameters.Add("@updatedAt", SqliteType.Text).Value = DateTime.UtcNow.ToString("o");

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task SaveNeededItemProgressBatchAsync(IEnumerable<(string Id, int Count)> items, ProfileType? profileType = null)
    {
        if (items == null) return;
        var itemsList = items as IList<(string Id, int Count)> ?? items.ToList();
        if (itemsList.Count == 0) return;

        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync().ConfigureAwait(false);

        try
        {
            const string delSql = "DELETE FROM NeededItemProgress WHERE ProfileType = @profileType AND Id = @id;";
            await using var delCmd = new SqliteCommand(delSql, connection, (SqliteTransaction)tx);
            var dProfile = delCmd.Parameters.Add("@profileType", SqliteType.Integer);
            var dId = delCmd.Parameters.Add("@id", SqliteType.Text);
            dProfile.Value = (int)actualProfileType;
            await delCmd.PrepareAsync().ConfigureAwait(false);

            const string upsertSql = @"
                INSERT INTO NeededItemProgress (ProfileType, Id, Count, UpdatedAt)
                VALUES (@profileType, @id, @count, @updatedAt)
                ON CONFLICT(ProfileType, Id) DO UPDATE SET
                    Count = excluded.Count,
                    UpdatedAt = excluded.UpdatedAt;";
            await using var upsertCmd = new SqliteCommand(upsertSql, connection, (SqliteTransaction)tx);
            var uProfile = upsertCmd.Parameters.Add("@profileType", SqliteType.Integer);
            var uId = upsertCmd.Parameters.Add("@id", SqliteType.Text);
            var uCount = upsertCmd.Parameters.Add("@count", SqliteType.Integer);
            var uUpd = upsertCmd.Parameters.Add("@updatedAt", SqliteType.Text);
            uProfile.Value = (int)actualProfileType;
            uUpd.Value = DateTime.UtcNow.ToString("o");
            await upsertCmd.PrepareAsync().ConfigureAwait(false);

            foreach (var (id, count) in itemsList)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;

                if (count <= 0)
                {
                    dId.Value = id;
                    await delCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                else
                {
                    uId.Value = id;
                    uCount.Value = count;
                    await upsertCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
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

    public async Task DeleteNeededItemProgressAsync(string id, ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);
        const string sql = "DELETE FROM NeededItemProgress WHERE ProfileType = @profileType AND Id = @id";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        cmd.Parameters.Add("@id", SqliteType.Text).Value = id;

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task ClearAllNeededItemProgressAsync(ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);
        await using var cmd = new SqliteCommand("DELETE FROM NeededItemProgress WHERE ProfileType = @profileType", connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    #endregion

    #region JSON Migration

    public async Task<bool> MigrateFromJsonAsync()
    {
        if (!NeedsMigration()) return false;

        ReportProgress("Starting data migration...");
        var migrated = false;

        ReportProgress("Migrating quest progress data...");
        migrated |= await MigrateQuestProgressJsonAsync().ConfigureAwait(false);

        ReportProgress("Migrating objective progress data...");
        migrated |= await MigrateObjectiveProgressJsonAsync().ConfigureAwait(false);

        ReportProgress("Migrating hideout progress data...");
        migrated |= await MigrateHideoutProgressJsonAsync().ConfigureAwait(false);

        ReportProgress("Migrating item inventory data...");
        migrated |= await MigrateItemInventoryJsonAsync().ConfigureAwait(false);

        if (migrated)
        {
            ReportProgress("Data migration completed!");
        }

        return migrated;
    }

    private async Task<bool> MigrateQuestProgressJsonAsync()
    {
        var v2Path = Path.Combine(AppEnv.ConfigPath, "quest_progress_v2.json");
        var v1Path = Path.Combine(AppEnv.ConfigPath, "quest_progress.json");

        if (File.Exists(v2Path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(v2Path).ConfigureAwait(false);
                var v2Data = JsonSerializer.Deserialize<QuestProgressDataV2>(json);

                if (v2Data != null)
                {
                    var items = new List<(string Id, string? NormalizedName, QuestStatus Status)>();
                    foreach (var entry in v2Data.CompletedQuests.Where(e => e.IsValid))
                    {
                        items.Add((entry.Id ?? entry.NormalizedName!, entry.NormalizedName, QuestStatus.Done));
                    }
                    foreach (var entry in v2Data.FailedQuests.Where(e => e.IsValid))
                    {
                        items.Add((entry.Id ?? entry.NormalizedName!, entry.NormalizedName, QuestStatus.Failed));
                    }

                    await SaveQuestProgressBatchAsync(items).ConfigureAwait(false);

                    SafeArchiveFile(v2Path);
                    if (File.Exists(v1Path)) SafeArchiveFile(v1Path);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _log.Error($"V2 migration failed: {ex.Message}");
            }
        }
        else if (File.Exists(v1Path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(v1Path).ConfigureAwait(false);
                var v1Data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                if (v1Data != null)
                {
                    var items = new List<(string Id, string? NormalizedName, QuestStatus Status)>();
                    foreach (var kvp in v1Data)
                    {
                        if (Enum.TryParse<QuestStatus>(kvp.Value, out var status))
                        {
                            items.Add((kvp.Key, kvp.Key, status));
                        }
                    }

                    await SaveQuestProgressBatchAsync(items).ConfigureAwait(false);
                    SafeArchiveFile(v1Path);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _log.Error($"V1 migration failed: {ex.Message}");
            }
        }

        return false;
    }

    private async Task<bool> MigrateObjectiveProgressJsonAsync()
    {
        var filePath = Path.Combine(AppEnv.ConfigPath, "objective_progress.json");
        if (!File.Exists(filePath)) return false;

        try
        {
            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);

            if (data != null)
            {
                var items = new List<(string Key, string? QuestId, bool IsCompleted)>();
                foreach (var kvp in data)
                {
                    string? questId = null;
                    if (kvp.Key.Contains(':'))
                    {
                        var parts = kvp.Key.Split(':');
                        if (parts[0] != "id") questId = parts[0];
                    }
                    items.Add((kvp.Key, questId, kvp.Value));
                }

                await SaveObjectiveProgressBatchAsync(items).ConfigureAwait(false);
                SafeArchiveFile(filePath);
                return true;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Objective migration failed: {ex.Message}");
        }

        return false;
    }

    private async Task<bool> MigrateHideoutProgressJsonAsync()
    {
        var filePath = Path.Combine(AppEnv.ConfigPath, "hideout_progress.json");
        if (!File.Exists(filePath)) return false;

        try
        {
            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            Dictionary<string, int>? modules = null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("modules", out var modulesElement))
                {
                    modules = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in modulesElement.EnumerateObject())
                    {
                        if (prop.Value.TryGetInt32(out var level)) modules[prop.Name] = level;
                    }
                }
            }
            catch
            {
                modules = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            }

            if (modules != null && modules.Count > 0)
            {
                foreach (var kvp in modules)
                {
                    await SaveHideoutProgressAsync(kvp.Key, kvp.Value).ConfigureAwait(false);
                }
                SafeArchiveFile(filePath);
                return true;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Hideout migration failed: {ex.Message}");
        }

        return false;
    }

    private async Task<bool> MigrateItemInventoryJsonAsync()
    {
        var filePath = Path.Combine(AppEnv.ConfigPath, "item_inventory.json");
        if (!File.Exists(filePath)) return false;

        try
        {
            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var data = JsonSerializer.Deserialize<ItemInventoryData>(json, options);

            if (data != null && data.Items.Count > 0)
            {
                var list = data.Items.Select(kvp => (kvp.Key, kvp.Value.FirQuantity, kvp.Value.NonFirQuantity));
                await SaveItemInventoryBatchAsync(list).ConfigureAwait(false);
                SafeArchiveFile(filePath);
                return true;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"ItemInventory migration failed: {ex.Message}");
        }

        return false;
    }

    private static void SafeArchiveFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var backup = filePath + ".migrated.bak";
                File.Move(filePath, backup, overwrite: true);
            }
        }
        catch { }
    }

    #endregion

    #region User Settings (Safe & Unified)

    public async Task<string?> GetSettingAsync(string key, ProfileType? profileType = null, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var actualProfileType = profileType.HasValue ? (int)profileType.Value : 99;

        await using var connection = await OpenConnectionAsync(readOnly: true, ct).ConfigureAwait(false);
        const string sql = "SELECT Value FROM UserSettings WHERE ProfileType = @profileType AND Key = @key LIMIT 1;";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = actualProfileType;
        cmd.Parameters.Add("@key", SqliteType.Text).Value = key;

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result?.ToString();
    }

    public async Task SetSettingAsync(string key, string value, ProfileType? profileType = null, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var actualProfileType = profileType.HasValue ? (int)profileType.Value : 99;

        await using var connection = await OpenConnectionAsync(readOnly: false, ct).ConfigureAwait(false);
        const string sql = @"
            INSERT INTO UserSettings (ProfileType, Key, Value)
            VALUES (@profileType, @key, @value)
            ON CONFLICT(ProfileType, Key) DO UPDATE SET Value = excluded.Value;";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = actualProfileType;
        cmd.Parameters.Add("@key", SqliteType.Text).Value = key;
        cmd.Parameters.Add("@value", SqliteType.Text).Value = value;

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteSettingAsync(string key, ProfileType? profileType = null, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var actualProfileType = profileType.HasValue ? (int)profileType.Value : 99;

        await using var connection = await OpenConnectionAsync(readOnly: false, ct).ConfigureAwait(false);
        const string sql = "DELETE FROM UserSettings WHERE ProfileType = @profileType AND Key = @key;";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = actualProfileType;
        cmd.Parameters.Add("@key", SqliteType.Text).Value = key;

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public string? GetSetting(string key, ProfileType? profileType = null)
    {
        EnsureInitialized();
        var actualProfileType = profileType.HasValue ? (int)profileType.Value : 99;

        try
        {
            using var connection = new SqliteConnection(GetConnectionString(readOnly: true));
            connection.Open();
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA busy_timeout = 5000;";
                pragma.ExecuteNonQuery();
            }
            const string sql = "SELECT Value FROM UserSettings WHERE ProfileType = @profileType AND Key = @key LIMIT 1;";
            using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = actualProfileType;
            cmd.Parameters.Add("@key", SqliteType.Text).Value = key;
            var result = cmd.ExecuteScalar();
            return result?.ToString();
        }
        catch (Exception ex)
        {
            _log.Warning($"GetSetting error: {key}, {ex.Message}");
            return null;
        }
    }

    public Dictionary<string, string> GetAllSettings(ProfileType? profileType = null)
    {
        EnsureInitialized();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var actualProfileType = profileType.HasValue ? (int)profileType.Value : (int)ProfileService.Instance.CurrentProfile;

        try
        {
            using var connection = new SqliteConnection(GetConnectionString(readOnly: true));
            connection.Open();
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA busy_timeout = 5000;";
                pragma.ExecuteNonQuery();
            }
            const string sql = @"
                SELECT Key, Value, ProfileType FROM UserSettings
                WHERE ProfileType IN (@currentProfile, 99)
                ORDER BY CASE WHEN ProfileType = @currentProfile THEN 1 ELSE 0 END ASC;";
            using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.Add("@currentProfile", SqliteType.Integer).Value = actualProfileType;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var k = reader.IsDBNull(0) ? null : reader.GetString(0);
                var v = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                if (!string.IsNullOrEmpty(k)) result[k] = v;
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"GetAllSettings error: {ex.Message}");
        }

        return result;
    }

    public void SetSetting(string key, string value, ProfileType? profileType = null)
    {
        EnsureInitialized();
        var actualProfileType = profileType.HasValue ? (int)profileType.Value : 99;

        try
        {
            using var connection = new SqliteConnection(GetConnectionString(readOnly: false));
            connection.Open();
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA busy_timeout = 5000;";
                pragma.ExecuteNonQuery();
            }
            const string sql = @"
                INSERT INTO UserSettings (ProfileType, Key, Value)
                VALUES (@profileType, @key, @value)
                ON CONFLICT(ProfileType, Key) DO UPDATE SET Value = excluded.Value;";

            using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = actualProfileType;
            cmd.Parameters.Add("@key", SqliteType.Text).Value = key;
            cmd.Parameters.Add("@value", SqliteType.Text).Value = value;
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _log.Warning($"SetSetting error: {key}, {ex.Message}");
        }
    }

    public void DeleteSetting(string key, ProfileType? profileType = null)
    {
        EnsureInitialized();
        var actualProfileType = profileType.HasValue ? (int)profileType.Value : 99;

        try
        {
            using var connection = new SqliteConnection(GetConnectionString(readOnly: false));
            connection.Open();
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA busy_timeout = 5000;";
                pragma.ExecuteNonQuery();
            }
            const string sql = "DELETE FROM UserSettings WHERE ProfileType = @profileType AND Key = @key;";
            using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = actualProfileType;
            cmd.Parameters.Add("@key", SqliteType.Text).Value = key;
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _log.Warning($"DeleteSetting error: {key}, {ex.Message}");
        }
    }

    #endregion

    #region Raid History (Safe & Non-blocking)

    public async Task SaveRaidHistoryAsync(Models.EftRaidInfo raid, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(readOnly: false, ct).ConfigureAwait(false);
        const string sql = @"
            INSERT INTO RaidHistory (
                RaidId, SessionId, ShortId, ProfileId, RaidType, GameMode,
                MapName, MapKey, ServerIp, ServerPort, IsParty, PartyLeaderAccountId,
                StartTime, EndTime, DurationSeconds, Rtt, PacketLoss, PacketsSent, PacketsReceived
            ) VALUES (
                @raidId, @sessionId, @shortId, @profileId, @raidType, @gameMode,
                @mapName, @mapKey, @serverIp, @serverPort, @isParty, @partyLeaderId,
                @startTime, @endTime, @durationSeconds, @rtt, @packetLoss, @packetsSent, @packetsReceived
            );";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@raidId", SqliteType.Text).Value = raid.RaidId ?? (object)DBNull.Value;
        cmd.Parameters.Add("@sessionId", SqliteType.Text).Value = raid.SessionId ?? (object)DBNull.Value;
        cmd.Parameters.Add("@shortId", SqliteType.Text).Value = raid.ShortId ?? (object)DBNull.Value;
        cmd.Parameters.Add("@profileId", SqliteType.Text).Value = raid.ProfileId ?? (object)DBNull.Value;
        cmd.Parameters.Add("@raidType", SqliteType.Integer).Value = (int)raid.RaidType;
        cmd.Parameters.Add("@gameMode", SqliteType.Integer).Value = (int)raid.GameMode;
        cmd.Parameters.Add("@mapName", SqliteType.Text).Value = raid.MapName ?? (object)DBNull.Value;
        cmd.Parameters.Add("@mapKey", SqliteType.Text).Value = raid.MapKey ?? (object)DBNull.Value;
        cmd.Parameters.Add("@serverIp", SqliteType.Text).Value = raid.ServerIp ?? (object)DBNull.Value;
        cmd.Parameters.Add("@serverPort", SqliteType.Integer).Value = raid.ServerPort;
        cmd.Parameters.Add("@isParty", SqliteType.Integer).Value = raid.IsParty ? 1 : 0;
        cmd.Parameters.Add("@partyLeaderId", SqliteType.Text).Value = raid.PartyLeaderAccountId ?? (object)DBNull.Value;
        cmd.Parameters.Add("@startTime", SqliteType.Text).Value = raid.StartTime?.ToString("o") ?? (object)DBNull.Value;
        cmd.Parameters.Add("@endTime", SqliteType.Text).Value = raid.EndTime?.ToString("o") ?? (object)DBNull.Value;
        cmd.Parameters.Add("@durationSeconds", SqliteType.Integer).Value = raid.Duration.HasValue ? (int)raid.Duration.Value.TotalSeconds : (object)DBNull.Value;
        cmd.Parameters.Add("@rtt", SqliteType.Real).Value = raid.Rtt.HasValue ? raid.Rtt.Value : DBNull.Value;
        cmd.Parameters.Add("@packetLoss", SqliteType.Real).Value = raid.PacketLoss.HasValue ? raid.PacketLoss.Value : DBNull.Value;
        cmd.Parameters.Add("@packetsSent", SqliteType.Integer).Value = raid.PacketsSent.HasValue ? raid.PacketsSent.Value : DBNull.Value;
        cmd.Parameters.Add("@packetsReceived", SqliteType.Integer).Value = raid.PacketsReceived.HasValue ? raid.PacketsReceived.Value : DBNull.Value;

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<Models.EftRaidInfo>> GetRaidHistoryAsync(int limit = 100, Models.RaidType? raidType = null, string? mapKey = null, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var result = new List<Models.EftRaidInfo>(Math.Min(limit, 128));

        await using var connection = await OpenConnectionAsync(readOnly: true, ct).ConfigureAwait(false);

        var sql = @"
            SELECT RaidId, SessionId, ShortId, ProfileId, RaidType, GameMode,
                   MapName, MapKey, ServerIp, ServerPort, IsParty, PartyLeaderAccountId,
                   StartTime, EndTime, DurationSeconds, Rtt, PacketLoss, PacketsSent, PacketsReceived
            FROM RaidHistory
            WHERE (@raidType IS NULL OR RaidType = @raidType)
              AND (@mapKey IS NULL OR MapKey = @mapKey)
            ORDER BY StartTime DESC
            LIMIT @limit;";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@raidType", SqliteType.Integer).Value = raidType.HasValue ? (int)raidType.Value : DBNull.Value;
        cmd.Parameters.Add("@mapKey", SqliteType.Text).Value = !string.IsNullOrEmpty(mapKey) ? mapKey : DBNull.Value;
        cmd.Parameters.Add("@limit", SqliteType.Integer).Value = Math.Clamp(limit, 1, 5000);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new Models.EftRaidInfo
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
                ServerPort = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                IsParty = reader.GetInt32(10) == 1,
                PartyLeaderAccountId = reader.IsDBNull(11) ? null : reader.GetString(11),
                StartTime = TryParseIsoDateTime(reader, 12),
                EndTime = TryParseIsoDateTime(reader, 13),
                Rtt = reader.IsDBNull(15) ? null : reader.GetDouble(15),
                PacketLoss = reader.IsDBNull(16) ? null : reader.GetDouble(16),
                PacketsSent = reader.IsDBNull(17) ? null : reader.GetInt64(17),
                PacketsReceived = reader.IsDBNull(18) ? null : reader.GetInt64(18)
            });
        }

        return result;
    }

    private static DateTime? TryParseIsoDateTime(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        return DateTime.TryParse(
            reader.GetString(ordinal),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var dt) ? dt : null;
    }

    #endregion

    #region Custom Map Markers (Safe & Non-blocking)

    public async Task<List<CustomMapMarker>> LoadCustomMarkersAsync(string mapKey, ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;
        var result = new List<CustomMapMarker>();

        await using var connection = await OpenConnectionAsync(readOnly: true).ConfigureAwait(false);
        const string sql = "SELECT Id, MapKey, Name, X, Y, Z, FloorId, Color, Size, Opacity, CreatedAt FROM CustomMapMarkers WHERE ProfileType = @profileType AND MapKey = @mapKey";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        cmd.Parameters.Add("@mapKey", SqliteType.Text).Value = mapKey;

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
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
                Size = reader.IsDBNull(8) ? 24.0 : reader.GetDouble(8),
                Opacity = reader.IsDBNull(9) ? 1.0 : reader.GetDouble(9),
                CreatedAt = !reader.IsDBNull(10) && DateTime.TryParse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                    ? dt
                    : DateTime.UtcNow
            });
        }
        return result;
    }

    public async Task SaveCustomMarkerAsync(CustomMapMarker marker, ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);
        const string sql = @"
            INSERT INTO CustomMapMarkers (ProfileType, Id, MapKey, Name, X, Y, Z, FloorId, Color, Size, Opacity, CreatedAt)
            VALUES (@profileType, @id, @mapKey, @name, @x, @y, @z, @floorId, @color, @size, @opacity, @createdAt)
            ON CONFLICT(ProfileType, Id) DO UPDATE SET
                MapKey = excluded.MapKey,
                Name = excluded.Name,
                X = excluded.X,
                Y = excluded.Y,
                Z = excluded.Z,
                FloorId = excluded.FloorId,
                Color = excluded.Color,
                Size = excluded.Size,
                Opacity = excluded.Opacity,
                CreatedAt = excluded.CreatedAt;";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        cmd.Parameters.Add("@id", SqliteType.Text).Value = marker.Id;
        cmd.Parameters.Add("@mapKey", SqliteType.Text).Value = marker.MapKey;
        cmd.Parameters.Add("@name", SqliteType.Text).Value = marker.Name ?? (object)DBNull.Value;
        cmd.Parameters.Add("@x", SqliteType.Real).Value = marker.X;
        cmd.Parameters.Add("@y", SqliteType.Real).Value = marker.Y;
        cmd.Parameters.Add("@z", SqliteType.Real).Value = marker.Z;
        cmd.Parameters.Add("@floorId", SqliteType.Text).Value = marker.FloorId ?? (object)DBNull.Value;
        cmd.Parameters.Add("@color", SqliteType.Text).Value = marker.Color ?? (object)DBNull.Value;
        cmd.Parameters.Add("@size", SqliteType.Real).Value = marker.Size;
        cmd.Parameters.Add("@opacity", SqliteType.Real).Value = marker.Opacity;
        cmd.Parameters.Add("@createdAt", SqliteType.Text).Value = marker.CreatedAt.ToString("o");

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        CustomMarkersChanged?.Invoke();
    }

    public async Task DeleteCustomMarkerAsync(string id, ProfileType? profileType = null)
    {
        await InitializeAsync().ConfigureAwait(false);
        var actualProfileType = profileType ?? ProfileService.Instance.CurrentProfile;

        await using var connection = await OpenConnectionAsync(readOnly: false).ConfigureAwait(false);
        const string sql = "DELETE FROM CustomMapMarkers WHERE ProfileType = @profileType AND Id = @id";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.Add("@profileType", SqliteType.Integer).Value = (int)actualProfileType;
        cmd.Parameters.Add("@id", SqliteType.Text).Value = id;

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        CustomMarkersChanged?.Invoke();
    }

    #endregion
}
