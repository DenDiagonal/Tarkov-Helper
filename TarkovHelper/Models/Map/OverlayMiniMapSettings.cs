namespace TarkovHelper.Models.Map;

/// <summary>
/// 오버레이 미니맵 뷰 모드
/// </summary>
public enum MiniMapViewMode
{
    /// <summary>
    /// 고정 뷰 - 전체 맵 표시, 줌으로 조절
    /// </summary>
    Fixed = 0,

    /// <summary>
    /// 플레이어 추적 뷰 - 플레이어가 항상 중앙
    /// </summary>
    PlayerTracking = 1
}

/// <summary>
/// 오버레이 미니맵 설정
/// </summary>
/// <summary>
/// 미니맵 커스텀 마커 크기 프리셋
/// </summary>
public enum MarkerSizePreset
{
    Small = 0,
    Normal = 1,
    Large = 2
}

public sealed class OverlayMiniMapSettings
{
    /// <summary>
    /// 오버레이 활성화 여부
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 오버레이 X 위치 (화면 기준)
    /// </summary>
    public double PositionX { get; set; } = -1; // -1 = 자동 (우측 상단)

    /// <summary>
    /// 오버레이 Y 위치 (화면 기준)
    /// </summary>
    public double PositionY { get; set; } = -1; // -1 = 자동 (우측 상단)

    /// <summary>
    /// 오버레이 너비
    /// </summary>
    public double Width { get; set; } = 300;

    /// <summary>
    /// 오버레이 높이
    /// </summary>
    public double Height { get; set; } = 300;

    /// <summary>
    /// 투명도 (0.1 ~ 1.0)
    /// </summary>
    public double Opacity { get; set; } = 0.8;

    /// <summary>
    /// 줌 레벨 (0.25 ~ 4.0)
    /// </summary>
    public double ZoomLevel { get; set; } = 1.0;

    /// <summary>
    /// 플레이어 마커 크기 배율 (0.5 ~ 3.0)
    /// </summary>
    public double PlayerMarkerSize { get; set; } = 1.0;

    /// <summary>
    /// 뷰 모드 (고정/플레이어 추적)
    /// </summary>
    public MiniMapViewMode ViewMode { get; set; } = MiniMapViewMode.Fixed;

    /// <summary>
    /// Click-through 모드 (마우스 클릭 통과)
    /// </summary>
    public bool ClickThrough { get; set; } = false;

    /// <summary>
    /// 줌 인 단축키 (Virtual Key Code, 기본값: VK_ADD 0x6B)
    /// </summary>
    public int ZoomInKey { get; set; } = 0x6B;

    /// <summary>
    /// 줌 아웃 단축키 (Virtual Key Code, 기본값: VK_SUBTRACT 0x6D)
    /// </summary>
    public int ZoomOutKey { get; set; } = 0x6D;

    /// <summary>
    /// 퀘스트 마커 표시 여부
    /// </summary>
    public bool ShowQuestMarkers { get; set; } = true;

    /// <summary>
    /// 활성 퀘스트만 표시 여부 (기본값: true)
    /// </summary>
    public bool ActiveQuestsOnly { get; set; } = true;

    /// <summary>
    /// 완료된 목표 숨기기 (기본값: true)
    /// </summary>
    public bool HideCompletedObjectives { get; set; } = true;

    /// <summary>
    /// PMC 탈출구 (+ PMC/Scav Co-op) 마커 표시 여부
    /// </summary>
    public bool ShowPmcExtracts { get; set; } = true;

    /// <summary>
    /// Scav 탈출구 (+ Scav/PMC Co-op) 마커 표시 여부
    /// </summary>
    public bool ShowScavExtracts { get; set; } = true;

    /// <summary>
    /// PMC 스폰 마커 표시 여부
    /// </summary>
    public bool ShowPmcSpawns { get; set; } = true;

    /// <summary>
    /// 탈출구 마커 표시 여부 (호환성 유지)
    /// </summary>
    public bool ShowExtractMarkers
    {
        get => ShowPmcExtracts || ShowScavExtracts;
        set
        {
            ShowPmcExtracts = value;
            ShowScavExtracts = value;
        }
    }

    /// <summary>
    /// 커스텀 마커 표시 여부
    /// </summary>
    public bool ShowCustomMarkers { get; set; } = true;

    /// <summary>
    /// 커스텀 마커 크기 배율 (0.4 ~ 3.0, 기본값: 1.0)
    /// </summary>
    public double CustomMarkerScale { get; set; } = 1.0;

    /// <summary>
    /// 커스텀 마커 크기 프리셋 (Small: 작음, Normal: 보통, Large: 큼)
    /// </summary>
    public MarkerSizePreset MarkerPreset { get; set; } = MarkerSizePreset.Normal;


    /// <summary>
    /// 전술 POI 마커 (보스/스폰/레버 등) 표시 여부 (기본값: true)
    /// </summary>
    public bool ShowPoiMarkers { get; set; } = true;

    /// <summary>
    /// 맵 오프셋 X (고정 뷰 모드에서 팬 위치)
    /// </summary>
    public double MapOffsetX { get; set; } = 0;

    /// <summary>
    /// 맵 오프셋 Y (고정 뷰 모드에서 팬 위치)
    /// </summary>
    public double MapOffsetY { get; set; } = 0;

    /// <summary>
    /// 최소 너비
    /// </summary>
    public const double MinWidth = 200;

    /// <summary>
    /// 최대 너비
    /// </summary>
    public const double MaxWidth = 1600;

    /// <summary>
    /// 최소 높이
    /// </summary>
    public const double MinHeight = 200;

    /// <summary>
    /// 최대 높이
    /// </summary>
    public const double MaxHeight = 1600;

    /// <summary>
    /// 최소 투명도
    /// </summary>
    public const double MinOpacity = 0.1;

    /// <summary>
    /// 최대 투명도
    /// </summary>
    public const double MaxOpacity = 1.0;

    /// <summary>
    /// 최소 줌 레벨 (상대적 배율: 0.5x = 50% 축소보기)
    /// </summary>
    public const double MinZoom = 0.50;

    /// <summary>
    /// 최대 줌 레벨 (상대적 배율: 4.0x = 400% 확대)
    /// </summary>
    public const double MaxZoom = 4.00;

    /// <summary>
    /// 줌 단계
    /// </summary>
    public const double ZoomStep = 0.15;

    /// <summary>
    /// 기본 설정으로 초기화
    /// </summary>
    public void ResetToDefaults()
    {
        Enabled = false;
        PositionX = -1;
        PositionY = -1;
        Width = 300;
        Height = 300;
        Opacity = 0.8;
        ZoomLevel = 1.0;
        PlayerMarkerSize = 1.0;
        ViewMode = MiniMapViewMode.Fixed;
        ClickThrough = false;
        ZoomInKey = 0x6B;
        ZoomOutKey = 0x6D;
        ShowQuestMarkers = true;
        ActiveQuestsOnly = true;
        HideCompletedObjectives = true;
        ShowExtractMarkers = true;
        ShowCustomMarkers = true;
        CustomMarkerScale = 1.0;
        ShowPoiMarkers = true;
        MapOffsetX = 0;
        MapOffsetY = 0;
    }

    /// <summary>
    /// 줌 레벨 증가 (비례적 줌)
    /// </summary>
    public void ZoomIn()
    {
        ZoomLevel = Math.Min(MaxZoom, ZoomLevel * 1.15);
    }

    /// <summary>
    /// 줌 레벨 감소 (비례적 줌)
    /// </summary>
    public void ZoomOut()
    {
        ZoomLevel = Math.Max(MinZoom, ZoomLevel / 1.15);
    }

    /// <summary>
    /// 뷰 모드 토글
    /// </summary>
    public void ToggleViewMode()
    {
        ViewMode = ViewMode == MiniMapViewMode.Fixed
            ? MiniMapViewMode.PlayerTracking
            : MiniMapViewMode.Fixed;
    }

    /// <summary>
    /// Click-through 모드 토글
    /// </summary>
    public void ToggleClickThrough()
    {
        ClickThrough = !ClickThrough;
    }

    /// <summary>
    /// 다른 설정에서 값 복사
    /// </summary>
    public void CopyFrom(OverlayMiniMapSettings other)
    {
        Opacity = other.Opacity;
        ZoomLevel = other.ZoomLevel;
        PlayerMarkerSize = other.PlayerMarkerSize;
        ViewMode = other.ViewMode;
        ClickThrough = other.ClickThrough;
        ZoomInKey = other.ZoomInKey;
        ZoomOutKey = other.ZoomOutKey;
        ShowQuestMarkers = other.ShowQuestMarkers;
        ActiveQuestsOnly = other.ActiveQuestsOnly;
        HideCompletedObjectives = other.HideCompletedObjectives;
        ShowExtractMarkers = other.ShowExtractMarkers;
        ShowCustomMarkers = other.ShowCustomMarkers;
        CustomMarkerScale = other.CustomMarkerScale;
        MarkerPreset = other.MarkerPreset;
        ShowPoiMarkers = other.ShowPoiMarkers;
    }

    /// <summary>
    /// 설정 복사본 생성
    /// </summary>
    public OverlayMiniMapSettings Clone()
    {
        return new OverlayMiniMapSettings
        {
            Enabled = Enabled,
            PositionX = PositionX,
            PositionY = PositionY,
            Width = Width,
            Height = Height,
            Opacity = Opacity,
            ZoomLevel = ZoomLevel,
            PlayerMarkerSize = PlayerMarkerSize,
            ViewMode = ViewMode,
            ClickThrough = ClickThrough,
            ZoomInKey = ZoomInKey,
            ZoomOutKey = ZoomOutKey,
            ShowQuestMarkers = ShowQuestMarkers,
            ActiveQuestsOnly = ActiveQuestsOnly,
            HideCompletedObjectives = HideCompletedObjectives,
            ShowExtractMarkers = ShowExtractMarkers,
            ShowCustomMarkers = ShowCustomMarkers,
            CustomMarkerScale = CustomMarkerScale,
            MarkerPreset = MarkerPreset,
            ShowPoiMarkers = ShowPoiMarkers,
            MapOffsetX = MapOffsetX,
            MapOffsetY = MapOffsetY
        };
    }
}
