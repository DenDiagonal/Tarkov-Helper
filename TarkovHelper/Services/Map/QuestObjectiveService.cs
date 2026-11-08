using TarkovHelper.Models;
using TarkovHelper.Models.Map;

namespace TarkovHelper.Services.Map;

/// <summary>
/// Legacy Map용 퀘스트 목표 서비스.
/// QuestObjectiveDbService의 데이터를 Legacy_Map.TaskObjectiveWithLocation으로 변환합니다.
/// </summary>
public sealed class QuestObjectiveService
{
    private static QuestObjectiveService? _instance;
    public static QuestObjectiveService Instance => _instance ??= new QuestObjectiveService();

    private readonly QuestObjectiveDbService _objectiveService;

    private QuestObjectiveService()
    {
        _objectiveService = QuestObjectiveDbService.Instance;
    }

    /// <summary>
    /// 데이터가 로드되었는지 여부
    /// </summary>
    public bool IsLoaded => _objectiveService.IsLoaded;

    /// <summary>
    /// 모든 목표 목록 (속성)
    /// </summary>
    public IReadOnlyList<TaskObjectiveWithLocation> AllObjectives => GetAllObjectives();

    /// <summary>
    /// 퀘스트 목표 데이터를 로드합니다.
    /// </summary>
    public async Task<bool> LoadAsync()
    {
        if (!_objectiveService.IsLoaded)
        {
            return await _objectiveService.LoadObjectivesAsync();
        }
        return true;
    }

    /// <summary>
    /// 데이터 로드를 보장하고 상태 콜백을 제공합니다.
    /// </summary>
    public async Task EnsureLoadedAsync(Action<string>? statusCallback = null)
    {
        if (IsLoaded)
        {
            statusCallback?.Invoke("Quest objectives already loaded");
            return;
        }

        statusCallback?.Invoke("Loading quest objectives...");
        await LoadAsync();
        statusCallback?.Invoke($"Loaded {_objectiveService.AllObjectives.Count} quest objectives");
    }

    /// <summary>
    /// 특정 맵의 퀘스트 목표를 반환합니다.
    /// </summary>
    /// <param name="mapKey">맵 키</param>
    /// <param name="mapConfig">맵 설정 (별칭 매칭용)</param>
    /// <returns>퀘스트 목표 목록</returns>
    public List<TaskObjectiveWithLocation> GetObjectivesForMap(string mapKey, MapConfig mapConfig)
    {
        var objectives = _objectiveService.GetObjectivesForMap(mapKey, mapConfig);
        return objectives.Select(o => ConvertToTaskObjective(o, mapConfig)).ToList();
    }

    /// <summary>
    /// 모든 퀘스트 목표를 반환합니다.
    /// </summary>
    public List<TaskObjectiveWithLocation> GetAllObjectives()
    {
        return _objectiveService.AllObjectives
            .Select(o => ConvertToTaskObjective(o, null))
            .ToList();
    }

    /// <summary>
    /// 맵별로 그룹화된 퀘스트 목표를 반환합니다.
    /// </summary>
    public Dictionary<string, List<TaskObjectiveWithLocation>> GetObjectivesGroupedByMap()
    {
        var result = new Dictionary<string, List<TaskObjectiveWithLocation>>(StringComparer.OrdinalIgnoreCase);

        foreach (var objective in _objectiveService.AllObjectives)
        {
            var mapName = objective.EffectiveMapName ?? "Unknown";
            var converted = ConvertToTaskObjective(objective, null);

            if (!result.TryGetValue(mapName, out var list))
            {
                list = new List<TaskObjectiveWithLocation>();
                result[mapName] = list;
            }
            list.Add(converted);
        }

        return result;
    }

    /// <summary>
    /// 특정 맵의 활성(미완료) 퀘스트 목표를 반환합니다.
    /// QuestProgressService에서 QuestStatus.Active인 퀘스트의 목표만 필터링하여 반환하며,
    /// Locked (Trader LL 또는 Task Pool 미충족), LevelLocked, Unavailable 등은 엄격히 제외됩니다.
    /// </summary>
    /// <param name="mapName">맵 이름</param>
    /// <param name="progressService">퀘스트 진행 서비스</param>
    /// <param name="sourceObjectives">선택적 소스 목표 컬렉션 (null인 경우 GetAllObjectives() 사용)</param>
    /// <returns>활성 퀘스트 목표 목록</returns>
    public List<TaskObjectiveWithLocation> GetActiveObjectivesForMap(
        string mapName,
        QuestProgressService progressService,
        IEnumerable<TaskObjectiveWithLocation>? sourceObjectives = null)
    {
        var activeObjectives = new List<TaskObjectiveWithLocation>();
        if (progressService == null || string.IsNullOrWhiteSpace(mapName))
            return activeObjectives;

        var allObjectives = sourceObjectives ?? GetAllObjectives();

        foreach (var obj in allObjectives)
        {
            // 맵 필터링
            bool isOnMap = false;
            foreach (var loc in obj.Locations)
            {
                if (string.Equals(loc.MapName, mapName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(loc.MapNormalizedName, mapName, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(loc.MapName) && loc.MapName.Split(new[] { ',', '/', ';' }, StringSplitOptions.TrimEntries).Any(p => string.Equals(p, mapName, StringComparison.OrdinalIgnoreCase))))
                {
                    isOnMap = true;
                    break;
                }
            }

            if (!isOnMap) continue;

            // 퀘스트 활성 상태 확인 (ID 기반 조회 우선, NormalizedName, BsgId, TaskName은 fallback)
            var task = (!string.IsNullOrEmpty(obj.QuestId) ? progressService.GetTaskById(obj.QuestId) : null)
                ?? (!string.IsNullOrEmpty(obj.TaskNormalizedName) ? progressService.GetTask(obj.TaskNormalizedName) : null)
                ?? (!string.IsNullOrEmpty(obj.QuestId) ? progressService.GetTaskByBsgId(obj.QuestId) : null);

            if (task == null && !string.IsNullOrEmpty(obj.TaskName))
            {
                var norm = NormalizeQuestName(obj.TaskName);
                task = progressService.GetTask(norm)
                    ?? progressService.AllTasks.FirstOrDefault(t => string.Equals(t.Name, obj.TaskName, StringComparison.OrdinalIgnoreCase));
            }

            if (task != null)
            {
                var status = progressService.GetStatus(task);
                // Trader Loyalty Level 또는 Task Pool 요구치 미충족으로 인한 Locked 상태는 자동으로 완전히 제외됨
                if (status == QuestStatus.Active)
                {
                    // 목표 완료 상태 확인
                    obj.IsCompleted = ObjectiveProgressService.Instance.IsObjectiveCompletedById(obj.ObjectiveId);
                    activeObjectives.Add(obj);
                }
            }
        }

        return activeObjectives;
    }

    /// <summary>
    /// 특정 퀘스트의 모든 목표를 반환합니다. (NormalizedName 기반 - deprecated)
    /// </summary>
    /// <param name="taskNormalizedName">정규화된 퀘스트 이름</param>
    /// <returns>해당 퀘스트의 목표 목록</returns>
    public List<TaskObjectiveWithLocation> GetObjectivesForTask(string taskNormalizedName)
    {
        return GetAllObjectives()
            .Where(o => string.Equals(o.TaskNormalizedName, taskNormalizedName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 특정 퀘스트의 모든 목표를 QuestId로 반환합니다. (권장)
    /// </summary>
    /// <param name="questId">퀘스트 ID</param>
    /// <returns>해당 퀘스트의 목표 목록</returns>
    public List<TaskObjectiveWithLocation> GetObjectivesForTaskById(string questId)
    {
        return GetAllObjectives()
            .Where(o => string.Equals(o.QuestId, questId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// QuestObjective를 TaskObjectiveWithLocation으로 변환합니다.
    /// </summary>
    private static TaskObjectiveWithLocation ConvertToTaskObjective(QuestObjective obj, MapConfig? mapConfig)
    {
        // QuestDbService에서 정확한 NormalizedName 조회
        var quest = QuestDbService.Instance.GetQuestById(obj.QuestId);
        var taskNormalizedName = quest?.NormalizedName ?? NormalizeQuestName(obj.QuestName);
        var resolved = ObjectiveProgressService.Instance.ResolveQuestAndIndex(obj);

        var result = new TaskObjectiveWithLocation
        {
            ObjectiveId = obj.Id,
            ObjectiveIndex = resolved.ObjectiveIndex,
            Description = obj.Description,
            DescriptionKo = null, // DB에 한국어 설명이 없으면 null
            Type = ConvertObjectiveType(obj.ObjectiveType),
            QuestId = obj.QuestId,
            TaskNormalizedName = taskNormalizedName,
            TaskName = obj.QuestName,
            TaskNameKo = obj.QuestNameKo,
            Locations = new List<QuestObjectiveLocation>()
        };

        // LocationPoints를 QuestObjectiveLocation으로 변환
        var mapName = obj.EffectiveMapName ?? "Unknown";

        // HandOver 및 Deliver 목표는 레이드 종료 후 상인에게 제출하는 목표이므로
        // 인게임 맵 마커(Locations)를 생성하지 않습니다. (TarkovDev / Tarkov Tracker 표준)
        bool isHandOver = obj.ObjectiveType == QuestObjectiveType.HandOver ||
            (!string.IsNullOrEmpty(obj.Description) &&
             (obj.Description.StartsWith("Hand over", StringComparison.OrdinalIgnoreCase) ||
              obj.Description.StartsWith("Deliver", StringComparison.OrdinalIgnoreCase)));

        if (!isHandOver && obj.LocationPoints.Count > 0)
        {
            var pointsByMap = obj.LocationPoints
                .GroupBy(p => !string.IsNullOrEmpty(p.MapName) ? p.MapName : mapName);

            foreach (var group in pointsByMap)
            {
                var ptMap = group.Key;
                var pts = group.ToList();
                var firstPoint = pts[0];
                var location = new QuestObjectiveLocation
                {
                    Id = $"{obj.Id}_{ptMap}_loc",
                    MapId = ptMap,
                    MapName = ptMap,
                    MapNormalizedName = NormalizeMapName(ptMap),
                    X = firstPoint.X,
                    Y = firstPoint.Z, // Game Z → Location Y (수평면)
                    Z = firstPoint.Y, // Game Y → Location Z (높이)
                    FloorId = firstPoint.FloorId // 층 정보 복사
                };

                // 여러 포인트가 있으면 Outline으로 변환 (단, 대각선 길이가 2m 이상인 실제 구역만 Area로 처리)
                if (pts.Count > 2)
                {
                    var minX = pts.Min(p => p.X);
                    var maxX = pts.Max(p => p.X);
                    var minZ = pts.Min(p => p.Z);
                    var maxZ = pts.Max(p => p.Z);
                    var dx = maxX - minX;
                    var dz = maxZ - minZ;
                    var span = Math.Sqrt(dx * dx + dz * dz);

                    if (span >= 2.0)
                    {
                        location.Outline = pts
                            .Select(p => new OutlinePoint { X = p.X, Y = p.Z })
                            .ToList();
                    }
                }

                result.Locations.Add(location);
            }
        }

        // OptionalPoints도 별도 위치로 추가 (HandOver가 아닐 때만)
        if (!isHandOver)
        {
            foreach (var point in obj.OptionalPoints)
            {
                var optMap = !string.IsNullOrEmpty(point.MapName) ? point.MapName : mapName;
                var optLoc = new QuestObjectiveLocation
                {
                    Id = $"{obj.Id}_opt_{result.Locations.Count}",
                    MapId = optMap,
                    MapName = optMap,
                    MapNormalizedName = NormalizeMapName(optMap),
                    X = point.X,
                    Y = point.Z,
                    Z = point.Y,
                    FloorId = point.FloorId // 층 정보 복사
                };
                result.Locations.Add(optLoc);
            }
        }

        return result;
    }

    /// <summary>
    /// QuestObjectiveType을 문자열로 변환합니다.
    /// </summary>
    private static string ConvertObjectiveType(QuestObjectiveType type)
    {
        return type switch
        {
            QuestObjectiveType.Kill => "kill",
            QuestObjectiveType.Collect => "collect",
            QuestObjectiveType.HandOver => "handover",
            QuestObjectiveType.Visit => "visit",
            QuestObjectiveType.Mark => "mark",
            QuestObjectiveType.Stash => "stash",
            QuestObjectiveType.Survive => "survive",
            QuestObjectiveType.Build => "build",
            QuestObjectiveType.Task => "task",
            _ => "custom"
        };
    }

    /// <summary>
    /// 퀘스트 이름을 정규화합니다.
    /// </summary>
    private static string NormalizeQuestName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        return name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("'", "")
            .Replace("\"", "");
    }

    /// <summary>
    /// 맵 이름을 정규화합니다.
    /// </summary>
    private static string NormalizeMapName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        return name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("streets of tarkov", "streets")
            .Replace("ground zero", "ground-zero")
            .Replace("the lab", "labs");
    }
}
