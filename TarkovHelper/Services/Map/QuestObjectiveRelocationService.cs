using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Pages.Map.ViewModels;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services.Map;

public sealed class QuestObjectiveRelocationService
{
	private static QuestObjectiveRelocationService? _instance;
	private readonly object _lock = new object();
	private IMapCoordinateTransformer _transformer;
	public static QuestObjectiveRelocationService Instance => _instance ?? (_instance = new QuestObjectiveRelocationService());
	public bool IsPlacementActive { get; private set; }
	public string? TargetObjectiveId { get; private set; }
	public string? TargetObjectiveDescription { get; private set; }
	public string? TargetQuestName { get; private set; }
	public string? TargetMapKey { get; private set; }
	public event EventHandler? PlacementStateChanged;
	private QuestObjectiveRelocationService()
	{
		_transformer = InitializeTransformer();
	}
	private IMapCoordinateTransformer InitializeTransformer()
	{
		try
		{
			if (MapTrackerService.Instance?.Transformer != null && MapTrackerService.Instance.Transformer.GetAllMapKeys().Count > 0)
			{
				return MapTrackerService.Instance.Transformer;
			}
		}
		catch
		{
		}
		return LoadFallbackTransformer();
	}
	private static MapCoordinateTransformer LoadFallbackTransformer()
	{
		string[] array = new string[5]
		{
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Data", "map_configs.json"),
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "TarkovHelper", "Assets", "DB", "Data", "map_configs.json"),
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "TarkovHelper", "Assets", "DB", "Data", "map_configs.json"),
			Path.Combine(Directory.GetCurrentDirectory(), "Assets", "DB", "Data", "map_configs.json"),
			Path.Combine(Directory.GetCurrentDirectory(), "TarkovHelper", "Assets", "DB", "Data", "map_configs.json")
		};
		string[] array2 = array;
		foreach (string path in array2)
		{
			try
			{
				string fullPath = Path.GetFullPath(path);
				if (File.Exists(fullPath))
				{
					string json = File.ReadAllText(fullPath);
					MapConfigList mapConfigList = JsonSerializer.Deserialize<MapConfigList>(json, new JsonSerializerOptions
					{
						PropertyNameCaseInsensitive = true
					});
					if (mapConfigList?.Maps != null && mapConfigList.Maps.Count > 0)
					{
						return new MapCoordinateTransformer(mapConfigList.Maps);
					}
				}
			}
			catch
			{
			}
		}
		return new MapCoordinateTransformer();
	}
	public void SetTransformer(IMapCoordinateTransformer transformer)
	{
		lock (_lock)
		{
			_transformer = transformer ?? throw new ArgumentNullException("transformer");
		}
	}
	internal static void ResetInstanceForTesting(QuestObjectiveRelocationService? testInstance = null)
	{
		_instance = testInstance;
	}
	public void StartPlacement(string objectiveId, string description, string questName, string? mapKey = null)
	{
		if (string.IsNullOrWhiteSpace(objectiveId))
		{
			throw new ArgumentException("Objective ID cannot be null or empty.", "objectiveId");
		}
		TargetObjectiveId = objectiveId;
		TargetObjectiveDescription = description;
		TargetQuestName = questName;
		TargetMapKey = mapKey;
		IsPlacementActive = true;
		this.PlacementStateChanged?.Invoke(this, EventArgs.Empty);
	}
	public void CancelPlacement()
	{
		TargetObjectiveId = null;
		TargetObjectiveDescription = null;
		TargetQuestName = null;
		TargetMapKey = null;
		IsPlacementActive = false;
		this.PlacementStateChanged?.Invoke(this, EventArgs.Empty);
	}
	public bool TryCalculateWorldCoordinates(string mapKey, Point canvasPoint, string? currentFloorId, out LocationPoint? locationPoint)
	{
		return TryCalculateWorldCoordinates(mapKey, canvasPoint.X, canvasPoint.Y, currentFloorId, out locationPoint);
	}
	public bool TryCalculateWorldCoordinates(string mapKey, double canvasX, double canvasY, string? currentFloorId, out LocationPoint? locationPoint)
	{
		locationPoint = null;
		if (string.IsNullOrWhiteSpace(mapKey))
		{
			return false;
		}
		MapConfig mapConfig;
		lock (_lock)
		{
			mapConfig = _transformer.GetMapConfig(mapKey);
		}
		if (mapConfig == null)
		{
			return false;
		}
		double[] array = mapConfig.PlayerMarkerTransform ?? mapConfig.CalibratedTransform;
		double value;
		double value2;
		if (array != null && array.Length >= 6)
		{
			(value, value2) = mapConfig.ScreenToGameForPlayer(canvasX, canvasY);
		}
		else
		{
			if (!_transformer.TryTransformToWorld(mapKey, canvasX, canvasY, out EftPosition worldPosition) || worldPosition == null)
			{
				return false;
			}
			value = worldPosition.X;
			value2 = worldPosition.Z.GetValueOrDefault();
		}
		string text = currentFloorId;
		if (string.IsNullOrEmpty(text) && mapConfig.Floors != null && mapConfig.Floors.Count > 0)
		{
			text = mapConfig.Floors.FirstOrDefault((MapFloorConfig f) => f.IsDefault)?.LayerId ?? mapConfig.Floors.FirstOrDefault()?.LayerId;
		}
		double value3 = 0.0;
		if (!string.IsNullOrEmpty(text))
		{
			value3 = GetFloorMidpoint(mapKey, text);
		}
		locationPoint = new LocationPoint(Math.Round(value, 5), Math.Round(value3, 5), Math.Round(value2, 5), text);
		return true;
	}
	private static double GetFloorMidpoint(string mapKey, string floorId)
	{
		try
		{
			string databasePath = DatabaseUpdateService.Instance.DatabasePath;
			if (string.IsNullOrEmpty(databasePath) || !File.Exists(databasePath))
			{
				return 0.0;
			}
			string connectionString = "Data Source=" + databasePath + ";Mode=ReadOnly";
			using var sqliteConnection = new SqliteConnection(connectionString);
			sqliteConnection.Open();
			using var sqliteCommand2 = sqliteConnection.CreateCommand();
			sqliteCommand2.CommandText = "SELECT MinY, MaxY FROM MapFloorLocations WHERE MapKey = @mapKey AND FloorId = @floorId LIMIT 1";
			sqliteCommand2.Parameters.AddWithValue("@mapKey", mapKey);
			sqliteCommand2.Parameters.AddWithValue("@floorId", floorId);
			using var sqliteDataReader = sqliteCommand2.ExecuteReader();
			if (sqliteDataReader.Read())
			{
				double @double = sqliteDataReader.GetDouble(0);
				double double2 = sqliteDataReader.GetDouble(1);
				return (@double + double2) / 2.0;
			}
		}
		catch
		{
		}
		return 0.0;
	}
	public async Task EnsureDatabaseSchemaAsync(string? targetDbPath = null)
	{
		string dbPath = targetDbPath ?? DatabaseUpdateService.Instance.DatabasePath;
		ValidateTargetDatabasePath(dbPath);
		await DatabaseBootstrapService.Instance.EnsureDatabaseSchemaAsync(dbPath);
	}
	public async Task<bool> SaveObjectiveLocationAsync(string objectiveId, LocationPoint point, string? targetDbPath = null)
	{
		if (string.IsNullOrWhiteSpace(objectiveId))
		{
			throw new ArgumentException("Objective ID cannot be null or empty.", "objectiveId");
		}
		if (point == null)
		{
			throw new ArgumentNullException("point");
		}
		string dbPath = targetDbPath ?? DatabaseUpdateService.Instance.DatabasePath;
		ValidateTargetDatabasePath(dbPath);
		if (!File.Exists(dbPath))
		{
			return false;
		}
		await EnsureDatabaseSchemaAsync(dbPath);
		string json = JsonSerializer.Serialize(new LocationPoint[1] { point }, new JsonSerializerOptions
		{
			WriteIndented = false
		});
		string connectionString = "Data Source=" + dbPath + ";Mode=ReadWrite";
		bool result;
		await using (SqliteConnection connection = new SqliteConnection(connectionString))
		{
			await connection.OpenAsync();
			string currentLoc = null;
			string originalLoc = null;
			string questName = null;
			string objDesc = null;
			string mapName = null;
			string selectSql = "\n            SELECT o.LocationPoints, o.OriginalLocationPoints, o.IsCustomized, o.Description, o.MapName, q.Name\n            FROM QuestObjectives o\n            LEFT JOIN Quests q ON o.QuestId = q.Id\n            WHERE o.Id = @id";
			bool flag2;
			await using (SqliteCommand selCmd = new SqliteCommand(selectSql, connection))
			{
				selCmd.Parameters.AddWithValue("@id", objectiveId);
				bool flag;
				await using (SqliteDataReader reader = await selCmd.ExecuteReaderAsync())
				{
					if (await reader.ReadAsync())
					{
						currentLoc = (reader.IsDBNull(0) ? null : reader.GetString(0));
						originalLoc = (reader.IsDBNull(1) ? null : reader.GetString(1));
						objDesc = (reader.IsDBNull(3) ? null : reader.GetString(3));
						mapName = (reader.IsDBNull(4) ? null : reader.GetString(4));
						questName = (reader.IsDBNull(5) ? null : reader.GetString(5));
						goto IL_0666;
					}
					flag = false;
				}
				flag2 = flag;
			}
			result = flag2;
			goto end_IL_0243;
			IL_08c5:
			bool flag3;
			result = flag3;
			goto end_IL_0243;
			IL_0666:
			if (originalLoc == null && currentLoc != null)
			{
				originalLoc = currentLoc;
			}
			string now = DateTime.UtcNow.ToString("o");
			string updateSql = "\n            UPDATE QuestObjectives\n            SET LocationPoints = @json,\n                OriginalLocationPoints = COALESCE(OriginalLocationPoints, @origLoc),\n                IsCustomized = 1,\n                UpdatedAt = @now,\n                IsApproved = 1\n            WHERE Id = @id";
			await using (SqliteCommand cmd = new SqliteCommand(updateSql, connection))
			{
				cmd.Parameters.AddWithValue("@json", json);
				cmd.Parameters.AddWithValue("@origLoc", ((object)originalLoc) ?? ((object)DBNull.Value));
				cmd.Parameters.AddWithValue("@now", now);
				cmd.Parameters.AddWithValue("@id", objectiveId);
				if (await cmd.ExecuteNonQueryAsync() <= 0)
				{
					flag3 = false;
					goto IL_08c5;
				}
			}
			await LogRelocationActionAsync(connection, objectiveId, questName, objDesc, mapName, currentLoc, json, "Relocated", now);
			await RefreshCachesAsync();
			result = true;
			end_IL_0243:;
		}
		return result;
	}
	public async Task<bool> RestoreObjectiveToFactoryAsync(string objectiveId, string? targetDbPath = null)
	{
		if (string.IsNullOrWhiteSpace(objectiveId))
		{
			throw new ArgumentException("Objective ID cannot be null or empty.", "objectiveId");
		}
		string dbPath = targetDbPath ?? DatabaseUpdateService.Instance.DatabasePath;
		ValidateTargetDatabasePath(dbPath);
		if (!File.Exists(dbPath))
		{
			return false;
		}
		await EnsureDatabaseSchemaAsync(dbPath);
		string connectionString = "Data Source=" + dbPath + ";Mode=ReadWrite";
		bool result;
		await using (SqliteConnection connection = new SqliteConnection(connectionString))
		{
			await connection.OpenAsync();
			string currentLoc = null;
			string originalLoc = null;
			string questName = null;
			string objDesc = null;
			string mapName = null;
			string selectSql = "\n            SELECT o.LocationPoints, o.OriginalLocationPoints, o.Description, o.MapName, q.Name\n            FROM QuestObjectives o\n            LEFT JOIN Quests q ON o.QuestId = q.Id\n            WHERE o.Id = @id";
			bool flag2;
			await using (SqliteCommand selCmd = new SqliteCommand(selectSql, connection))
			{
				selCmd.Parameters.AddWithValue("@id", objectiveId);
				bool flag;
				await using (SqliteDataReader reader = await selCmd.ExecuteReaderAsync())
				{
					if (await reader.ReadAsync())
					{
						currentLoc = (reader.IsDBNull(0) ? null : reader.GetString(0));
						originalLoc = (reader.IsDBNull(1) ? null : reader.GetString(1));
						objDesc = (reader.IsDBNull(2) ? null : reader.GetString(2));
						mapName = (reader.IsDBNull(3) ? null : reader.GetString(3));
						questName = (reader.IsDBNull(4) ? null : reader.GetString(4));
						goto IL_0625;
					}
					flag = false;
				}
				flag2 = flag;
			}
			result = flag2;
			goto end_IL_0202;
			IL_0818:
			bool flag3;
			result = flag3;
			goto end_IL_0202;
			IL_0625:
			string now = DateTime.UtcNow.ToString("o");
			string updateSql = "\n            UPDATE QuestObjectives\n            SET LocationPoints = OriginalLocationPoints,\n                IsCustomized = 0,\n                UpdatedAt = @now\n            WHERE Id = @id";
			await using (SqliteCommand cmd = new SqliteCommand(updateSql, connection))
			{
				cmd.Parameters.AddWithValue("@now", now);
				cmd.Parameters.AddWithValue("@id", objectiveId);
				if (await cmd.ExecuteNonQueryAsync() <= 0)
				{
					flag3 = false;
					goto IL_0818;
				}
			}
			await LogRelocationActionAsync(connection, objectiveId, questName, objDesc, mapName, currentLoc, originalLoc, "Restored", now);
			await RefreshCachesAsync();
			result = true;
			end_IL_0202:;
		}
		return result;
	}
	public async Task<int> RestoreAllObjectivesToFactoryAsync(string? targetDbPath = null)
	{
		string dbPath = targetDbPath ?? DatabaseUpdateService.Instance.DatabasePath;
		ValidateTargetDatabasePath(dbPath);
		if (!File.Exists(dbPath))
		{
			return 0;
		}
		await EnsureDatabaseSchemaAsync(dbPath);
		string connectionString = "Data Source=" + dbPath + ";Mode=ReadWrite";
		int result;
		await using (SqliteConnection connection = new SqliteConnection(connectionString))
		{
			await connection.OpenAsync();
			string now = DateTime.UtcNow.ToString("o");
			string updateSql = "\n            UPDATE QuestObjectives\n            SET LocationPoints = OriginalLocationPoints,\n                IsCustomized = 0,\n                UpdatedAt = @now\n            WHERE IsCustomized = 1";
			int num;
			await using (SqliteCommand cmd = new SqliteCommand(updateSql, connection))
			{
				cmd.Parameters.AddWithValue("@now", now);
				int rowsAffected = await cmd.ExecuteNonQueryAsync();
				if (rowsAffected > 0)
				{
					await LogRelocationActionAsync(connection, "ALL", "ALL", "Restored all customized objectives to factory", null, null, null, "ResetAll", now);
					await RefreshCachesAsync();
				}
				num = rowsAffected;
			}
			result = num;
		}
		return result;
	}
	public async Task<bool> ClearObjectiveLocationAsync(string objectiveId, string? targetDbPath = null)
	{
		if (string.IsNullOrWhiteSpace(objectiveId))
		{
			throw new ArgumentException("Objective ID cannot be null or empty.", "objectiveId");
		}
		string dbPath = targetDbPath ?? DatabaseUpdateService.Instance.DatabasePath;
		ValidateTargetDatabasePath(dbPath);
		if (!File.Exists(dbPath))
		{
			return false;
		}
		await EnsureDatabaseSchemaAsync(dbPath);
		string connectionString = "Data Source=" + dbPath + ";Mode=ReadWrite";
		bool result;
		await using (SqliteConnection connection = new SqliteConnection(connectionString))
		{
			await connection.OpenAsync();
			string currentLoc = null;
			string questName = null;
			string objDesc = null;
			string mapName = null;
			string selectSql = "\n            SELECT o.LocationPoints, o.Description, o.MapName, q.Name\n            FROM QuestObjectives o\n            LEFT JOIN Quests q ON o.QuestId = q.Id\n            WHERE o.Id = @id";
			bool flag2;
			await using (SqliteCommand selCmd = new SqliteCommand(selectSql, connection))
			{
				selCmd.Parameters.AddWithValue("@id", objectiveId);
				bool flag;
				await using (SqliteDataReader reader = await selCmd.ExecuteReaderAsync())
				{
					if (await reader.ReadAsync())
					{
						currentLoc = (reader.IsDBNull(0) ? null : reader.GetString(0));
						objDesc = (reader.IsDBNull(1) ? null : reader.GetString(1));
						mapName = (reader.IsDBNull(2) ? null : reader.GetString(2));
						questName = (reader.IsDBNull(3) ? null : reader.GetString(3));
						goto IL_05fb;
					}
					flag = false;
				}
				flag2 = flag;
			}
			result = flag2;
			goto end_IL_0202;
			IL_07ee:
			bool flag3;
			result = flag3;
			goto end_IL_0202;
			IL_05fb:
			string now = DateTime.UtcNow.ToString("o");
			string updateSql = "\n            UPDATE QuestObjectives\n            SET LocationPoints = NULL,\n                IsCustomized = 0,\n                UpdatedAt = @now\n            WHERE Id = @id";
			await using (SqliteCommand cmd = new SqliteCommand(updateSql, connection))
			{
				cmd.Parameters.AddWithValue("@now", now);
				cmd.Parameters.AddWithValue("@id", objectiveId);
				if (await cmd.ExecuteNonQueryAsync() <= 0)
				{
					flag3 = false;
					goto IL_07ee;
				}
			}
			await LogRelocationActionAsync(connection, objectiveId, questName, objDesc, mapName, currentLoc, null, "Cleared", now);
			await RefreshCachesAsync();
			result = true;
			end_IL_0202:;
		}
		return result;
	}
	public async Task<List<CustomizedObjectiveItem>> GetCustomizedObjectivesAsync(string? targetDbPath = null)
	{
		List<CustomizedObjectiveItem> list = new List<CustomizedObjectiveItem>();
		string dbPath = targetDbPath ?? DatabaseUpdateService.Instance.DatabasePath;
		if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
		{
			return list;
		}
		await EnsureDatabaseSchemaAsync(dbPath);
		string connectionString = "Data Source=" + dbPath + ";Mode=ReadOnly";
		List<CustomizedObjectiveItem> result;
		await using (SqliteConnection connection = new SqliteConnection(connectionString))
		{
			await connection.OpenAsync();
			string sql = "\n            SELECT o.Id, o.QuestId, q.Name, q.Trader, o.MapName, o.Description,\n                   COALESCE(o.ObjectiveType, 'Custom'), o.LocationPoints, o.OriginalLocationPoints, o.UpdatedAt\n            FROM QuestObjectives o\n            LEFT JOIN Quests q ON o.QuestId = q.Id\n            WHERE o.IsCustomized = 1\n            ORDER BY o.UpdatedAt DESC";
			List<CustomizedObjectiveItem> list3;
			await using (SqliteCommand cmd = new SqliteCommand(sql, connection))
			{
				List<CustomizedObjectiveItem> list2;
				await using (SqliteDataReader reader = await cmd.ExecuteReaderAsync())
				{
					while (await reader.ReadAsync())
					{
						CustomizedObjectiveItem item = new CustomizedObjectiveItem
						{
							ObjectiveId = reader.GetString(0),
							QuestId = (reader.IsDBNull(1) ? string.Empty : reader.GetString(1)),
							QuestName = (reader.IsDBNull(2) ? string.Empty : reader.GetString(2)),
							Trader = (reader.IsDBNull(3) ? string.Empty : reader.GetString(3)),
							MapName = (reader.IsDBNull(4) ? string.Empty : reader.GetString(4)),
							Description = (reader.IsDBNull(5) ? string.Empty : reader.GetString(5)),
							Type = (reader.IsDBNull(6) ? "Custom" : reader.GetString(6)),
							CurrentLocationJson = (reader.IsDBNull(7) ? null : reader.GetString(7)),
							OriginalLocationJson = (reader.IsDBNull(8) ? null : reader.GetString(8)),
							LastModified = (reader.IsDBNull(9) ? null : reader.GetString(9))
						};
						item.CurrentCoordinatesDisplay = FormatCoordinatesDisplay(item.CurrentLocationJson);
						item.OriginalCoordinatesDisplay = FormatCoordinatesDisplay(item.OriginalLocationJson);
						list.Add(item);
					}
					list2 = list;
				}
				list3 = list2;
			}
			result = list3;
		}
		return result;
	}
	public async Task<List<RelocationSearchItem>> SearchObjectivesAsync(string? searchText, string? trader, string? mapKey, bool geographicOnly = true, bool missingCoordinatesOnly = false, string? targetDbPath = null)
	{
		List<RelocationSearchItem> list = new List<RelocationSearchItem>();
		string dbPath = targetDbPath ?? DatabaseUpdateService.Instance.DatabasePath;
		if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
		{
			return list;
		}
		await EnsureDatabaseSchemaAsync(dbPath);
		string connectionString = "Data Source=" + dbPath + ";Mode=ReadOnly";
		List<RelocationSearchItem> result;
		await using (SqliteConnection connection = new SqliteConnection(connectionString))
		{
			await connection.OpenAsync();
			string sql = "\n            SELECT o.Id, o.QuestId, q.Name, q.Trader, COALESCE(o.MapName, q.Location), o.Description,\n                   COALESCE(o.ObjectiveType, 'Custom'), o.LocationPoints, o.IsCustomized\n            FROM QuestObjectives o\n            LEFT JOIN Quests q ON o.QuestId = q.Id\n            WHERE 1=1";
			if (!string.IsNullOrWhiteSpace(searchText))
			{
				sql += " AND (q.Name LIKE @search OR o.Description LIKE @search OR q.Trader LIKE @search)";
			}
			if (!string.IsNullOrWhiteSpace(trader) && !trader.Equals("All", StringComparison.OrdinalIgnoreCase) && !trader.Equals("Todos", StringComparison.OrdinalIgnoreCase))
			{
				sql += " AND q.Trader LIKE @trader";
			}
			if (!string.IsNullOrWhiteSpace(mapKey) && !mapKey.Equals("All", StringComparison.OrdinalIgnoreCase) && !mapKey.Equals("Todos", StringComparison.OrdinalIgnoreCase))
			{
				sql += " AND COALESCE(o.MapName, q.Location) LIKE @map";
			}
			if (missingCoordinatesOnly)
			{
				sql += " AND (o.LocationPoints IS NULL OR o.LocationPoints = '')";
			}
			sql += " ORDER BY q.Name, o.SortOrder";
			List<RelocationSearchItem> list3;
			await using (SqliteCommand cmd = new SqliteCommand(sql, connection))
			{
				if (!string.IsNullOrWhiteSpace(searchText))
				{
					cmd.Parameters.AddWithValue("@search", "%" + searchText.Trim() + "%");
				}
				if (!string.IsNullOrWhiteSpace(trader) && !trader.Equals("All", StringComparison.OrdinalIgnoreCase) && !trader.Equals("Todos", StringComparison.OrdinalIgnoreCase))
				{
					cmd.Parameters.AddWithValue("@trader", "%" + trader.Trim() + "%");
				}
				if (!string.IsNullOrWhiteSpace(mapKey) && !mapKey.Equals("All", StringComparison.OrdinalIgnoreCase) && !mapKey.Equals("Todos", StringComparison.OrdinalIgnoreCase))
				{
					cmd.Parameters.AddWithValue("@map", "%" + mapKey.Trim() + "%");
				}
				List<RelocationSearchItem> list2;
				await using (SqliteDataReader reader = await cmd.ExecuteReaderAsync())
				{
					while (await reader.ReadAsync())
					{
						string type = (reader.IsDBNull(6) ? "Custom" : reader.GetString(6));
						string desc = (reader.IsDBNull(5) ? string.Empty : reader.GetString(5));
						bool canRelocate = QuestObjectiveViewModel.IsGeographicObjective(type, desc);
						if (!geographicOnly || canRelocate)
						{
							string locJson = (reader.IsDBNull(7) ? null : reader.GetString(7));
							bool isCustomized = !reader.IsDBNull(8) && reader.GetInt32(8) == 1;
							bool hasLocation = !string.IsNullOrWhiteSpace(locJson);
							string status = (isCustomized ? "Custom" : ((!hasLocation) ? "No Location" : "Default"));
							list.Add(new RelocationSearchItem
							{
								ObjectiveId = reader.GetString(0),
								QuestId = (reader.IsDBNull(1) ? string.Empty : reader.GetString(1)),
								QuestName = (reader.IsDBNull(2) ? string.Empty : reader.GetString(2)),
								Trader = (reader.IsDBNull(3) ? string.Empty : reader.GetString(3)),
								MapName = (reader.IsDBNull(4) ? string.Empty : reader.GetString(4)),
								Description = desc,
								Type = type,
								HasLocation = hasLocation,
								IsCustomized = isCustomized,
								StatusDisplay = status,
								CanRelocate = canRelocate,
								LocationPointsJson = locJson
							});
						}
					}
					list2 = list;
				}
				list3 = list2;
			}
			result = list3;
		}
		return result;
	}
	public async Task<List<RelocationLogItem>> GetRelocationHistoryLogsAsync(int limit = 100, string? targetDbPath = null)
	{
		List<RelocationLogItem> list = new List<RelocationLogItem>();
		string dbPath = targetDbPath ?? DatabaseUpdateService.Instance.DatabasePath;
		if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
		{
			return list;
		}
		await EnsureDatabaseSchemaAsync(dbPath);
		string connectionString = "Data Source=" + dbPath + ";Mode=ReadOnly";
		List<RelocationLogItem> result;
		await using (SqliteConnection connection = new SqliteConnection(connectionString))
		{
			await connection.OpenAsync();
			string sql = "SELECT Id, ObjectiveId, QuestName, ObjectiveDescription, MapName, PreviousLocationPoints, NewLocationPoints, Action, Timestamp FROM QuestObjectiveRelocationLog ORDER BY Id DESC LIMIT @limit";
			List<RelocationLogItem> list3;
			await using (SqliteCommand cmd = new SqliteCommand(sql, connection))
			{
				cmd.Parameters.AddWithValue("@limit", limit);
				List<RelocationLogItem> list2;
				await using (SqliteDataReader reader = await cmd.ExecuteReaderAsync())
				{
					while (await reader.ReadAsync())
					{
						list.Add(new RelocationLogItem
						{
							Id = reader.GetInt64(0),
							ObjectiveId = reader.GetString(1),
							QuestName = (reader.IsDBNull(2) ? null : reader.GetString(2)),
							ObjectiveDescription = (reader.IsDBNull(3) ? null : reader.GetString(3)),
							MapName = (reader.IsDBNull(4) ? null : reader.GetString(4)),
							PreviousLocationPoints = (reader.IsDBNull(5) ? null : reader.GetString(5)),
							NewLocationPoints = (reader.IsDBNull(6) ? null : reader.GetString(6)),
							Action = reader.GetString(7),
							Timestamp = reader.GetString(8)
						});
					}
					list2 = list;
				}
				list3 = list2;
			}
			result = list3;
		}
		return result;
	}
	private static async Task LogRelocationActionAsync(SqliteConnection connection, string objectiveId, string? questName, string? description, string? mapName, string? previousLoc, string? newLoc, string action, string timestamp)
	{
		try
		{
			string logSql = "\n                INSERT INTO QuestObjectiveRelocationLog (\n                    ObjectiveId, QuestName, ObjectiveDescription, MapName,\n                    PreviousLocationPoints, NewLocationPoints, Action, Timestamp\n                ) VALUES (\n                    @objId, @quest, @desc, @map, @prev, @new, @action, @time\n                )";
			await using SqliteCommand cmd = new SqliteCommand(logSql, connection);
			cmd.Parameters.AddWithValue("@objId", objectiveId);
			cmd.Parameters.AddWithValue("@quest", ((object)questName) ?? ((object)DBNull.Value));
			cmd.Parameters.AddWithValue("@desc", ((object)description) ?? ((object)DBNull.Value));
			cmd.Parameters.AddWithValue("@map", ((object)mapName) ?? ((object)DBNull.Value));
			cmd.Parameters.AddWithValue("@prev", ((object)previousLoc) ?? ((object)DBNull.Value));
			cmd.Parameters.AddWithValue("@new", ((object)newLoc) ?? ((object)DBNull.Value));
			cmd.Parameters.AddWithValue("@action", action);
			cmd.Parameters.AddWithValue("@time", timestamp);
			await cmd.ExecuteNonQueryAsync();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			System.Diagnostics.Debug.WriteLine("[QuestObjectiveRelocationService] Audit logging failed: " + ex2.Message);
		}
	}
	private static string FormatCoordinatesDisplay(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return "(Sin coordenadas)";
		}
		try
		{
			List<LocationPoint> list = JsonSerializer.Deserialize<List<LocationPoint>>(json);
			if (list != null && list.Count > 0)
			{
				LocationPoint locationPoint = list[0];
				string value = ((!string.IsNullOrEmpty(locationPoint.FloorId)) ? (" [" + locationPoint.FloorId + "]") : "");
				return $"X: {locationPoint.X:F1}, Z: {locationPoint.Z:F1}{value}";
			}
		}
		catch
		{
		}
		return (json.Length > 30) ? (json.Substring(0, 27) + "...") : json;
	}
	private static async Task RefreshCachesAsync()
	{
		try
		{
			await QuestObjectiveDbService.Instance.RefreshAsync();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			System.Diagnostics.Debug.WriteLine("[QuestObjectiveRelocationService] RefreshAsync error: " + ex2.Message);
		}
		try
		{
			OverlayMiniMapService.Instance?.RefreshMarkers();
		}
		catch (Exception ex)
		{
			Exception ex3 = ex;
			System.Diagnostics.Debug.WriteLine("[QuestObjectiveRelocationService] RefreshMarkers error: " + ex3.Message);
		}
	}
	private static void ValidateTargetDatabasePath(string? dbPath)
	{
		if (string.IsNullOrWhiteSpace(dbPath))
		{
			throw new ArgumentException("Database path cannot be null or empty.", "dbPath");
		}
		string fileName = Path.GetFileName(dbPath);
		if (string.Equals(fileName, "user_data.db", StringComparison.OrdinalIgnoreCase) || dbPath.EndsWith("user_data.db", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Modification of user_data.db is strictly forbidden.");
		}
	}
	public List<ObjectiveLookupItem> GetObjectivesForQuest(string questNameOrId, string? targetDbPath = null)
	{
		List<ObjectiveLookupItem> list = new List<ObjectiveLookupItem>();
		if (string.IsNullOrWhiteSpace(questNameOrId))
		{
			return list;
		}
		string text = targetDbPath ?? DatabaseUpdateService.Instance.DatabasePath;
		if (string.IsNullOrEmpty(text) || !File.Exists(text))
		{
			return list;
		}
		try
		{
			string connectionString = "Data Source=" + text + ";Mode=ReadOnly";
			using var sqliteConnection = new SqliteConnection(connectionString);
			sqliteConnection.Open();
			using var sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = @"
                SELECT o.Id, o.Description, o.MapName, o.LocationPoints, o.SortOrder
                FROM QuestObjectives o
                JOIN Quests q ON o.QuestId = q.Id
                WHERE q.Name = @query OR q.Id = @query
                ORDER BY o.SortOrder";
			sqliteCommand.Parameters.AddWithValue("@query", questNameOrId);
			using var sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				list.Add(new ObjectiveLookupItem
				{
					Id = sqliteDataReader.GetString(0),
					Description = (sqliteDataReader.IsDBNull(1) ? string.Empty : sqliteDataReader.GetString(1)),
					MapName = (sqliteDataReader.IsDBNull(2) ? null : sqliteDataReader.GetString(2)),
					LocationPointsJson = (sqliteDataReader.IsDBNull(3) ? null : sqliteDataReader.GetString(3)),
					SortOrder = ((!sqliteDataReader.IsDBNull(4)) ? sqliteDataReader.GetInt32(4) : 0)
				});
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine("[QuestObjectiveRelocationService] GetObjectivesForQuest error: " + ex.Message);
		}
		return list;
	}
	public async Task<List<ObjectiveLookupItem>> GetObjectivesForQuestAsync(string questNameOrId, string? targetDbPath = null)
	{
		List<ObjectiveLookupItem> result = new List<ObjectiveLookupItem>();
		if (string.IsNullOrWhiteSpace(questNameOrId))
		{
			return result;
		}
		string dbPath = targetDbPath ?? DatabaseUpdateService.Instance.DatabasePath;
		if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
		{
			return result;
		}
		try
		{
			string connectionString = "Data Source=" + dbPath + ";Mode=ReadOnly";
			await using SqliteConnection connection = new SqliteConnection(connectionString);
			await connection.OpenAsync();
			string sql = "\n                SELECT o.Id, o.Description, o.MapName, o.LocationPoints, o.SortOrder\n                FROM QuestObjectives o\n                JOIN Quests q ON o.QuestId = q.Id\n                WHERE q.Name = @query OR q.Id = @query\n                ORDER BY o.SortOrder";
			await using SqliteCommand cmd = new SqliteCommand(sql, connection);
			cmd.Parameters.AddWithValue("@query", questNameOrId);
			await using SqliteDataReader reader = await cmd.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				result.Add(new ObjectiveLookupItem
				{
					Id = reader.GetString(0),
					Description = (reader.IsDBNull(1) ? string.Empty : reader.GetString(1)),
					MapName = (reader.IsDBNull(2) ? null : reader.GetString(2)),
					LocationPointsJson = (reader.IsDBNull(3) ? null : reader.GetString(3)),
					SortOrder = ((!reader.IsDBNull(4)) ? reader.GetInt32(4) : 0)
				});
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine("[QuestObjectiveRelocationService] GetObjectivesForQuestAsync error: " + ex.Message);
		}
		return result;
	}
}
public class ObjectiveLookupItem
{
	public string Id { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string? MapName { get; set; }
	public string? LocationPointsJson { get; set; }
	public bool HasCoordinates => !string.IsNullOrWhiteSpace(LocationPointsJson);
	public int SortOrder { get; set; }
}
public class CustomizedObjectiveItem
{
	public string ObjectiveId { get; set; } = string.Empty;
	public string QuestId { get; set; } = string.Empty;
	public string QuestName { get; set; } = string.Empty;
	public string Trader { get; set; } = string.Empty;
	public string MapName { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string Type { get; set; } = string.Empty;
	public string? CurrentLocationJson { get; set; }
	public string? OriginalLocationJson { get; set; }
	public bool HasOriginalLocation => !string.IsNullOrWhiteSpace(OriginalLocationJson);
	public string CurrentCoordinatesDisplay { get; set; } = string.Empty;
	public string OriginalCoordinatesDisplay { get; set; } = string.Empty;
	public string? LastModified { get; set; }
}
public class RelocationSearchItem
{
	public string ObjectiveId { get; set; } = string.Empty;
	public string QuestId { get; set; } = string.Empty;
	public string QuestName { get; set; } = string.Empty;
	public string Trader { get; set; } = string.Empty;
	public string MapName { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string Type { get; set; } = string.Empty;
	public bool HasLocation { get; set; }
	public bool IsCustomized { get; set; }
	public string StatusDisplay { get; set; } = string.Empty;
	public bool CanRelocate { get; set; }
	public string? LocationPointsJson { get; set; }
}
public class RelocationLogItem
{
	public long Id { get; set; }
	public string ObjectiveId { get; set; } = string.Empty;
	public string? QuestName { get; set; }
	public string? ObjectiveDescription { get; set; }
	public string? MapName { get; set; }
	public string? PreviousLocationPoints { get; set; }
	public string? NewLocationPoints { get; set; }
	public string Action { get; set; } = string.Empty;
	public string Timestamp { get; set; } = string.Empty;
}
