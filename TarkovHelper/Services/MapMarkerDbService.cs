using System.IO;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services;

/// <summary>
/// SQLite DB에서 맵 마커 데이터를 로드하는 서비스.
/// tarkov_data.db의 MapMarkers 테이블 사용.
/// </summary>
public sealed class MapMarkerDbService
{
    private static readonly ILogger _log = Log.For<MapMarkerDbService>();
    private static readonly Lazy<MapMarkerDbService> _lazyInstance =
        new(() => new MapMarkerDbService(), LazyThreadSafetyMode.ExecutionAndPublication);
    public static MapMarkerDbService Instance => _lazyInstance.Value;

    private readonly string _databasePath;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private List<MapMarker> _allMarkers = new();
    private Dictionary<string, List<MapMarker>> _markersByMap = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLoaded;

    public bool IsLoaded => Volatile.Read(ref _isLoaded);
    public int MarkerCount => Volatile.Read(ref _allMarkers).Count;

    /// <summary>
    /// 데이터가 새로고침되었을 때 발생하는 이벤트.
    /// UI 페이지들은 이 이벤트를 구독하여 화면을 갱신해야 함.
    /// </summary>
    public event EventHandler? DataRefreshed;

    private MapMarkerDbService()
    {
        _databasePath = DatabaseUpdateService.Instance.DatabasePath;

        // 데이터베이스 업데이트 이벤트 구독
        DatabaseUpdateService.Instance.DatabaseUpdated += OnDatabaseUpdated;
    }

    /// <summary>
    /// 데이터베이스 업데이트 시 데이터 리로드
    /// </summary>
    private async void OnDatabaseUpdated(object? sender, EventArgs e)
    {
        if (DatabaseUpdateService.Instance.IsInternalHotReloadInProgress)
            return;

        try
        {
            _log.Info("Database updated, reloading data...");
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error($"Error reloading marker data after DB update: {ex.Message}");
        }
    }

    /// <summary>
    /// DB가 존재하는지 확인
    /// </summary>
    public bool DatabaseExists => File.Exists(_databasePath);

    /// <summary>
    /// 모든 마커 반환
    /// </summary>
    public IReadOnlyList<MapMarker> AllMarkers => Volatile.Read(ref _allMarkers);

    public IReadOnlyList<MapMarker> GetMarkersForMap(string mapKey)
    {
        if (Volatile.Read(ref _markersByMap).TryGetValue(mapKey, out var markers))
            return markers;
        return Array.Empty<MapMarker>();
    }

    public async Task<bool> LoadMarkersAsync()
    {
        if (!DatabaseExists)
        {
            _log.Warning($"Database not found: {_databasePath}");
            return false;
        }

        await _reloadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var connectionString = $"Data Source={_databasePath};Mode=ReadOnly";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            if (!await TableExistsAsync(connection, "MapMarkers").ConfigureAwait(false))
            {
                _log.Warning("MapMarkers table not found");
                return false;
            }

            var markers = await LoadAllMarkersAsync(connection).ConfigureAwait(false);
            var newMarkersByMap = new Dictionary<string, List<MapMarker>>(StringComparer.OrdinalIgnoreCase);

            foreach (var marker in markers)
            {
                if (!newMarkersByMap.TryGetValue(marker.MapKey, out var mapMarkers))
                {
                    mapMarkers = new List<MapMarker>();
                    newMarkersByMap[marker.MapKey] = mapMarkers;
                }
                mapMarkers.Add(marker);
            }

            Interlocked.Exchange(ref _allMarkers, markers);
            Interlocked.Exchange(ref _markersByMap, newMarkersByMap);
            Volatile.Write(ref _isLoaded, true);
            _log.Info($"Loaded {markers.Count} markers from DB");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"Error loading markers: {ex.Message}");
            return false;
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        var sql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@name", tableName);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return count > 0;
    }

    /// <summary>
    /// 모든 마커 로드
    /// </summary>
    private async Task<List<MapMarker>> LoadAllMarkersAsync(SqliteConnection connection)
    {
        var markers = new List<MapMarker>();

        // Check if NameJa column exists
        var hasNameJa = await ColumnExistsAsync(connection, "MapMarkers", "NameJa");

        var sql = hasNameJa
            ? "SELECT Id, Name, NameKo, NameJa, MarkerType, MapKey, X, Y, Z, FloorId FROM MapMarkers"
            : "SELECT Id, Name, NameKo, MarkerType, MapKey, X, Y, Z, FloorId FROM MapMarkers";
        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (hasNameJa)
            {
                var markerTypeStr = reader.GetString(4);
                var markerType = MapMarker.ParseType(markerTypeStr);

                var marker = new MapMarker
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    NameKo = reader.IsDBNull(2) ? null : reader.GetString(2),
                    NameJa = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Type = markerType,
                    MapKey = reader.GetString(5),
                    X = reader.GetDouble(6),
                    Y = reader.GetDouble(7),
                    Z = reader.GetDouble(8),
                    FloorId = reader.IsDBNull(9) ? null : reader.GetString(9)
                };
                markers.Add(marker);
            }
            else
            {
                var markerTypeStr = reader.GetString(3);
                var markerType = MapMarker.ParseType(markerTypeStr);

                var marker = new MapMarker
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    NameKo = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Type = markerType,
                    MapKey = reader.GetString(4),
                    X = reader.GetDouble(5),
                    Y = reader.GetDouble(6),
                    Z = reader.GetDouble(7),
                    FloorId = reader.IsDBNull(8) ? null : reader.GetString(8)
                };
                markers.Add(marker);
            }
        }

        return markers;
    }

    private async Task<bool> ColumnExistsAsync(SqliteConnection connection, string tableName, string columnName)
    {
        var sql = $"PRAGMA table_info({tableName})";
        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1); // Column name is at index 1
            if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 타입별로 마커 필터링
    /// </summary>
    public IEnumerable<MapMarker> GetMarkersForMapByType(string mapKey, MarkerType type)
    {
        return GetMarkersForMap(mapKey).Where(m => m.Type == type);
    }

    /// <summary>
    /// 탈출구 마커만 필터링
    /// </summary>
    public IEnumerable<MapMarker> GetExtractionsForMap(string mapKey)
    {
        return GetMarkersForMap(mapKey).Where(m => m.IsExtraction);
    }

    /// <summary>
    /// 스폰 마커만 필터링
    /// </summary>
    public IEnumerable<MapMarker> GetSpawnsForMap(string mapKey)
    {
        return GetMarkersForMap(mapKey).Where(m => m.IsSpawn);
    }

    /// <summary>
    /// Transit 마커만 필터링
    /// </summary>
    public IEnumerable<MapMarker> GetTransitsForMap(string mapKey)
    {
        return GetMarkersForMap(mapKey).Where(m => m.Type == MarkerType.Transit);
    }

    /// <summary>
    /// 데이터 새로고침 (기존 데이터를 유지하면서 새 데이터로 atomic swap)
    /// </summary>
    public async Task RefreshAsync()
    {
        _log.Debug("Refreshing marker data...");
        // 기존 데이터를 클리어하지 않음 - LoadMarkersAsync()에서 atomic swap으로 교체
        await LoadMarkersAsync();

        // 데이터 새로고침 완료 이벤트 발생
        OnDataRefreshed();
    }

    /// <summary>
    /// 데이터 새로고침 이벤트 발생
    /// </summary>
    private void OnDataRefreshed()
    {
        var handler = DataRefreshed;
        if (handler == null) return;

        foreach (EventHandler listener in handler.GetInvocationList())
        {
            try
            {
                listener(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _log.Warning($"Error in MapMarkerDbService.DataRefreshed listener: {ex.Message}");
            }
        }
    }
}
