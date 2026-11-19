using TarkovHelper.Models;

namespace TarkovHelper.Services;

/// <summary>
/// Map-related localization strings for LocalizationService.
/// Includes: Map Tracker, Quest Drawer, Map Area, Legend, Settings, etc.
/// </summary>
public partial class LocalizationService
{
    #region Map Tracker Page

    public string MapPositionTracker => CurrentLanguage switch {
        AppLanguage.KO => "맵 위치 트래커",
        AppLanguage.JA => "マップ位置トラッカー",
        AppLanguage.ES => "Rastreador de Posición en Mapa",
        _ => "Map Position Tracker"
    };

    public string MapLabel => CurrentLanguage switch {
        AppLanguage.KO => "맵:",
        AppLanguage.JA => "マップ:",
        AppLanguage.ES => "Mapa:",
        _ => "Map:"
    };

    public string QuestMarkers => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트 마커",
        AppLanguage.JA => "クエストマーカー",
        AppLanguage.ES => "Marcadores de Misión",
        _ => "Quest Markers"
    };

    public string Extracts => CurrentLanguage switch {
        AppLanguage.KO => "탈출구",
        AppLanguage.JA => "脱出口",
        AppLanguage.ES => "Extracciones",
        _ => "Extracts"
    };

    public string ClearTrail => CurrentLanguage switch {
        AppLanguage.KO => "경로 지우기",
        AppLanguage.JA => "軌跡クリア",
        AppLanguage.ES => "Borrar Rastro",
        _ => "Clear Trail"
    };

    public string FullScreen => CurrentLanguage switch {
        AppLanguage.KO => "전체 화면",
        AppLanguage.JA => "全画面",
        AppLanguage.ES => "Pantalla Completa",
        _ => "Full Screen"
    };

    public string ExitFullScreen => CurrentLanguage switch {
        AppLanguage.KO => "전체 화면 종료",
        AppLanguage.JA => "全画面終了",
        AppLanguage.ES => "Salir de Pantalla Completa",
        _ => "Exit Full Screen"
    };

    public string StartTracking => CurrentLanguage switch {
        AppLanguage.KO => "추적 시작",
        AppLanguage.JA => "追跡開始",
        AppLanguage.ES => "Iniciar Rastreo",
        _ => "Start Tracking"
    };

    public string StopTracking => CurrentLanguage switch {
        AppLanguage.KO => "추적 중지",
        AppLanguage.JA => "追跡停止",
        AppLanguage.ES => "Detener Rastreo",
        _ => "Stop Tracking"
    };

    public string StatusWaiting => CurrentLanguage switch {
        AppLanguage.KO => "대기 중",
        AppLanguage.JA => "待機中",
        AppLanguage.ES => "En espera",
        _ => "Waiting"
    };

    public string StatusTracking => CurrentLanguage switch {
        AppLanguage.KO => "추적 중",
        AppLanguage.JA => "追跡中",
        AppLanguage.ES => "Rastreando",
        _ => "Tracking"
    };

    public string PositionLabel => CurrentLanguage switch {
        AppLanguage.KO => "위치:",
        AppLanguage.JA => "位置:",
        AppLanguage.ES => "Posición:",
        _ => "Position:"
    };

    public string LastUpdateLabel => CurrentLanguage switch {
        AppLanguage.KO => "마지막 업데이트:",
        AppLanguage.JA => "最終更新:",
        AppLanguage.ES => "Última actualización:",
        _ => "Last update:"
    };

    public string QuestObjectives => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트 목표",
        AppLanguage.JA => "クエスト目標",
        AppLanguage.ES => "Objetivos de Misión",
        _ => "Quest Objectives"
    };

    public string ProgressOnThisMap => CurrentLanguage switch {
        AppLanguage.KO => "이 맵 진행률",
        AppLanguage.JA => "このマップの進捗",
        AppLanguage.ES => "Progreso en este mapa",
        _ => "Progress on this map"
    };

    public string FilterIncomplete => CurrentLanguage switch {
        AppLanguage.KO => "미완료",
        AppLanguage.JA => "未完了",
        AppLanguage.ES => "Incompletas",
        _ => "Incomplete"
    };

    public string FilterCompleted => CurrentLanguage switch {
        AppLanguage.KO => "완료",
        AppLanguage.JA => "完了",
        AppLanguage.ES => "Completadas",
        _ => "Completed"
    };

    public string FilterAllTypes => CurrentLanguage switch {
        AppLanguage.KO => "전체 타입",
        AppLanguage.JA => "全タイプ",
        AppLanguage.ES => "Todos los Tipos",
        _ => "All Types"
    };

    public string FilterVisit => CurrentLanguage switch {
        AppLanguage.KO => "방문",
        AppLanguage.JA => "訪問",
        AppLanguage.ES => "Visitar",
        _ => "Visit"
    };

    public string FilterMark => CurrentLanguage switch {
        AppLanguage.KO => "마킹",
        AppLanguage.JA => "マーキング",
        AppLanguage.ES => "Marcar",
        _ => "Mark"
    };

    public string FilterPlant => CurrentLanguage switch {
        AppLanguage.KO => "설치",
        AppLanguage.JA => "設置",
        AppLanguage.ES => "Colocar",
        _ => "Plant"
    };

    public string FilterExtract => CurrentLanguage switch {
        AppLanguage.KO => "탈출",
        AppLanguage.JA => "脱出",
        AppLanguage.ES => "Extraer",
        _ => "Extract"
    };

    public string FilterFind => CurrentLanguage switch {
        AppLanguage.KO => "찾기",
        AppLanguage.JA => "発見",
        AppLanguage.ES => "Encontrar",
        _ => "Find"
    };

    public string ThisMapOnly => CurrentLanguage switch {
        AppLanguage.KO => "이 맵만",
        AppLanguage.JA => "このマップのみ",
        AppLanguage.ES => "Este Mapa",
        _ => "This Map"
    };

    public string GroupByQuest => CurrentLanguage switch {
        AppLanguage.KO => "그룹화",
        AppLanguage.JA => "グループ化",
        AppLanguage.ES => "Agrupar",
        _ => "Group"
    };

    public string ScreenshotFolder => CurrentLanguage switch {
        AppLanguage.KO => "스크린샷 폴더",
        AppLanguage.JA => "スクリーンショットフォルダ",
        AppLanguage.ES => "Carpeta de Capturas",
        _ => "Screenshot Folder"
    };

    public string MarkerSettings => CurrentLanguage switch {
        AppLanguage.KO => "마커 설정",
        AppLanguage.JA => "マーカー設定",
        AppLanguage.ES => "Ajustes de Marcadores",
        _ => "Marker Settings"
    };

    public string HideCompletedObjectives => CurrentLanguage switch {
        AppLanguage.KO => "완료된 목표 숨기기",
        AppLanguage.JA => "完了した目標を隠す",
        AppLanguage.ES => "Ocultar Objetivos Completados",
        _ => "Hide Completed Objectives"
    };

    public string QuestStyle => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트 스타일:",
        AppLanguage.JA => "クエストスタイル:",
        AppLanguage.ES => "Estilo de Misión:",
        _ => "Quest Style:"
    };

    public string QuestNameSize => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트명:",
        AppLanguage.JA => "クエスト名:",
        AppLanguage.ES => "Nombre de Misión:",
        _ => "Quest Name:"
    };

    public string QuestMarkerSize => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트 마커:",
        AppLanguage.JA => "クエストマーカー:",
        AppLanguage.ES => "Tamaño de Marcador:",
        _ => "Quest Marker:"
    };

    public string PlayerMarkerSize => CurrentLanguage switch {
        AppLanguage.KO => "플레이어 마커:",
        AppLanguage.JA => "プレイヤーマーカー:",
        AppLanguage.ES => "Marcador de Jugador:",
        _ => "Player Marker:"
    };

    public string ExtractSettings => CurrentLanguage switch {
        AppLanguage.KO => "탈출구 설정",
        AppLanguage.JA => "脱出口設定",
        AppLanguage.ES => "Ajustes de Extracción",
        _ => "Extract Settings"
    };

    public string PmcExtracts => CurrentLanguage switch {
        AppLanguage.KO => "PMC 탈출구",
        AppLanguage.JA => "PMC脱出口",
        AppLanguage.ES => "Extracciones PMC",
        _ => "PMC Extracts"
    };

    public string ScavExtracts => CurrentLanguage switch {
        AppLanguage.KO => "Scav 탈출구",
        AppLanguage.JA => "Scav脱出口",
        AppLanguage.ES => "Extracciones Scav",
        _ => "Scav Extracts"
    };

    public string ExtractNameSize => CurrentLanguage switch {
        AppLanguage.KO => "이름 크기:",
        AppLanguage.JA => "名前サイズ:",
        AppLanguage.ES => "Tamaño de Extracción:",
        _ => "Name Size:"
    };

    public string MarkerColors => CurrentLanguage switch {
        AppLanguage.KO => "마커 색상",
        AppLanguage.JA => "マーカー色",
        AppLanguage.ES => "Colores de Marcadores",
        _ => "Marker Colors"
    };

    public string ResetColors => CurrentLanguage switch {
        AppLanguage.KO => "색상 초기화",
        AppLanguage.JA => "色をリセット",
        AppLanguage.ES => "Restablecer Colores",
        _ => "Reset Colors"
    };

    public string NoMapImage => CurrentLanguage switch {
        AppLanguage.KO => "맵 이미지가 없습니다",
        AppLanguage.JA => "マップ画像がありません",
        AppLanguage.ES => "Sin imagen de mapa",
        _ => "No map image available"
    };

    public string AddMapImageHint => CurrentLanguage switch {
        AppLanguage.KO => "Assets/Maps/ 폴더에 맵 이미지를 추가하세요",
        AppLanguage.JA => "Assets/Maps/フォルダにマップ画像を追加してください",
        AppLanguage.ES => "Añade una imagen a Assets/Maps/",
        _ => "Add map image to Assets/Maps/ folder"
    };

    public string SetImagePathHint => CurrentLanguage switch {
        AppLanguage.KO => "또는 설정에서 이미지 경로를 지정하세요",
        AppLanguage.JA => "または設定で画像パスを指定してください",
        AppLanguage.ES => "O define la ruta en Ajustes",
        _ => "Or specify image path in settings"
    };

    public string ResetView => CurrentLanguage switch {
        AppLanguage.KO => "초기화",
        AppLanguage.JA => "リセット",
        AppLanguage.ES => "Restablecer Vista",
        _ => "Reset"
    };

    public string StyleIconOnly => CurrentLanguage switch {
        AppLanguage.KO => "아이콘만",
        AppLanguage.JA => "アイコンのみ",
        AppLanguage.ES => "Solo Icono",
        _ => "Icon Only"
    };

    public string StyleGreenCircle => CurrentLanguage switch {
        AppLanguage.KO => "녹색 원",
        AppLanguage.JA => "緑の丸",
        AppLanguage.ES => "Círculo Verde",
        _ => "Green Circle"
    };

    public string StyleIconWithName => CurrentLanguage switch {
        AppLanguage.KO => "아이콘 + 이름",
        AppLanguage.JA => "アイコン+名前",
        AppLanguage.ES => "Icono con Nombre",
        _ => "Icon + Name"
    };

    public string StyleCircleWithName => CurrentLanguage switch {
        AppLanguage.KO => "원 + 이름",
        AppLanguage.JA => "丸+名前",
        AppLanguage.ES => "Círculo con Nombre",
        _ => "Circle + Name"
    };

    #endregion

    #region Map Page - Quest Drawer

    public string Quest => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트",
        AppLanguage.JA => "クエスト",
        AppLanguage.ES => "Misión",
        _ => "Quest"
    };

    public string QuestPanelTooltip => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트 패널 열기/닫기 (Q)",
        AppLanguage.JA => "クエストパネル開閉 (Q)",
        AppLanguage.ES => "Panel de Misiones (Q)",
        _ => "Open/Close Quest Panel (Q)"
    };

    public string ShortcutHelp => CurrentLanguage switch {
        AppLanguage.KO => "단축키 도움말",
        AppLanguage.JA => "ショートカットヘルプ",
        AppLanguage.ES => "Atajos de Teclado",
        _ => "Shortcut Help"
    };

    public string DisplayOptions => CurrentLanguage switch {
        AppLanguage.KO => "표시 옵션",
        AppLanguage.JA => "表示オプション",
        AppLanguage.ES => "Opciones de Visualización",
        _ => "Display Options"
    };

    public string CloseWithShortcut => CurrentLanguage switch {
        AppLanguage.KO => "닫기 (Q)",
        AppLanguage.JA => "閉じる (Q)",
        AppLanguage.ES => "Cerrar (Esc)",
        _ => "Close (Q)"
    };

    public string SearchPlaceholder => CurrentLanguage switch {
        AppLanguage.KO => "🔍 검색...",
        AppLanguage.JA => "🔍 検索...",
        AppLanguage.ES => "Buscar...",
        _ => "🔍 Search..."
    };

    public string Incomplete => CurrentLanguage switch {
        AppLanguage.KO => "미완료",
        AppLanguage.JA => "未完了",
        AppLanguage.ES => "Incompleto",
        _ => "Incomplete"
    };

    public string CurrentMap => CurrentLanguage switch {
        AppLanguage.KO => "현재 맵",
        AppLanguage.JA => "現在のマップ",
        AppLanguage.ES => "Mapa Actual",
        _ => "Current Map"
    };

    public string SortByName => CurrentLanguage switch {
        AppLanguage.KO => "이름",
        AppLanguage.JA => "名前",
        AppLanguage.ES => "Ordenar por Nombre",
        _ => "Name"
    };

    public string SortByProgress => CurrentLanguage switch {
        AppLanguage.KO => "진행률",
        AppLanguage.JA => "進捗",
        AppLanguage.ES => "Ordenar por Progreso",
        _ => "Progress"
    };

    public string SortByCount => CurrentLanguage switch {
        AppLanguage.KO => "개수",
        AppLanguage.JA => "個数",
        AppLanguage.ES => "Ordenar por Cantidad",
        _ => "Count"
    };

    public string NoQuestsToDisplay => CurrentLanguage switch {
        AppLanguage.KO => "표시할 퀘스트 없음",
        AppLanguage.JA => "表示するクエストがありません",
        AppLanguage.ES => "No hay misiones para mostrar",
        _ => "No quests to display"
    };

    public string TryAdjustingFilters => CurrentLanguage switch {
        AppLanguage.KO => "필터를 조정해 보세요",
        AppLanguage.JA => "フィルターを調整してください",
        AppLanguage.ES => "Intenta cambiar los filtros",
        _ => "Try adjusting filters"
    };

    public string MarkAllComplete => CurrentLanguage switch {
        AppLanguage.KO => "모두 완료",
        AppLanguage.JA => "すべて完了",
        AppLanguage.ES => "Marcar Todo como Completado",
        _ => "Complete All"
    };

    public string MarkAllIncomplete => CurrentLanguage switch {
        AppLanguage.KO => "모두 미완료",
        AppLanguage.JA => "すべて未完了",
        AppLanguage.ES => "Marcar Todo como Incompleto",
        _ => "Mark All Incomplete"
    };

    public string HideFromMap => CurrentLanguage switch {
        AppLanguage.KO => "맵에서 숨기기",
        AppLanguage.JA => "マップから隠す",
        AppLanguage.ES => "Ocultar del Mapa",
        _ => "Hide from Map"
    };

    public string ShowHideOnMap => CurrentLanguage switch {
        AppLanguage.KO => "맵에 표시/숨김",
        AppLanguage.JA => "マップに表示/非表示",
        AppLanguage.ES => "Mostrar/Ocultar en el Mapa",
        _ => "Show/Hide on Map"
    };

    public string ViewOnMap => CurrentLanguage switch {
        AppLanguage.KO => "맵에서 보기",
        AppLanguage.JA => "マップで表示",
        AppLanguage.ES => "Ver en el Mapa",
        _ => "View on Map"
    };

    // Keyboard Hints
    public string OpenClose => CurrentLanguage switch {
        AppLanguage.KO => "열기/닫기",
        AppLanguage.JA => "開閉",
        AppLanguage.ES => "Abrir/Cerrar",
        _ => "Open/Close"
    };

    public string Move => CurrentLanguage switch {
        AppLanguage.KO => "이동",
        AppLanguage.JA => "移動",
        AppLanguage.ES => "Mover",
        _ => "Move"
    };

    public string Select => CurrentLanguage switch {
        AppLanguage.KO => "선택",
        AppLanguage.JA => "選択",
        AppLanguage.ES => "Seleccionar",
        _ => "Select"
    };

    public string GoToMap => CurrentLanguage switch {
        AppLanguage.KO => "맵이동",
        AppLanguage.JA => "マップ移動",
        AppLanguage.ES => "Ir al Mapa",
        _ => "Go to Map"
    };

    public string ToggleComplete => CurrentLanguage switch {
        AppLanguage.KO => "완료토글",
        AppLanguage.JA => "完了切替",
        AppLanguage.ES => "Alternar Completado",
        _ => "Toggle Complete"
    };

    public string Click => CurrentLanguage switch {
        AppLanguage.KO => "클릭",
        AppLanguage.JA => "クリック",
        AppLanguage.ES => "Clic",
        _ => "Click"
    };

    public string RightClick => CurrentLanguage switch {
        AppLanguage.KO => "우클릭",
        AppLanguage.JA => "右クリック",
        AppLanguage.ES => "Clic Derecho",
        _ => "Right-click"
    };

    #endregion

    #region Map Page - Map Area

    public string Scroll => CurrentLanguage switch {
        AppLanguage.KO => "스크롤",
        AppLanguage.JA => "スクロール",
        AppLanguage.ES => "Rueda",
        _ => "Scroll"
    };

    public string Zoom => CurrentLanguage switch {
        AppLanguage.KO => "줌",
        AppLanguage.JA => "ズーム",
        AppLanguage.ES => "Zoom",
        _ => "Zoom"
    };

    public string Drag => CurrentLanguage switch {
        AppLanguage.KO => "드래그",
        AppLanguage.JA => "ドラッグ",
        AppLanguage.ES => "Arrastrar",
        _ => "Drag"
    };

    public string LoadingMap => CurrentLanguage switch {
        AppLanguage.KO => "맵 로딩 중...",
        AppLanguage.JA => "マップ読み込み中...",
        AppLanguage.ES => "Cargando mapa...",
        _ => "Loading map..."
    };

    public string ZoomInTooltip => CurrentLanguage switch {
        AppLanguage.KO => "확대 (Scroll Up)",
        AppLanguage.JA => "拡大 (Scroll Up)",
        AppLanguage.ES => "Acercar (+)",
        _ => "Zoom In (Scroll Up)"
    };

    public string ZoomOutTooltip => CurrentLanguage switch {
        AppLanguage.KO => "축소 (Scroll Down)",
        AppLanguage.JA => "縮小 (Scroll Down)",
        AppLanguage.ES => "Alejar (-)",
        _ => "Zoom Out (Scroll Down)"
    };

    public string ResetViewTooltip => CurrentLanguage switch {
        AppLanguage.KO => "뷰 초기화 (R)",
        AppLanguage.JA => "ビューリセット (R)",
        AppLanguage.ES => "Restablecer Vista (R)",
        _ => "Reset View (R)"
    };

    #endregion

    #region Map Page - Legend

    public string MapLegend => CurrentLanguage switch {
        AppLanguage.KO => "맵 범례",
        AppLanguage.JA => "マップ凡例",
        AppLanguage.ES => "Leyenda del Mapa",
        _ => "Map Legend"
    };

    public string Extract => CurrentLanguage switch {
        AppLanguage.KO => "탈출구",
        AppLanguage.JA => "脱出口",
        AppLanguage.ES => "Extracción",
        _ => "Extract"
    };

    public string TransitPoint => CurrentLanguage switch {
        AppLanguage.KO => "환승 지점",
        AppLanguage.JA => "乗り換え地点",
        AppLanguage.ES => "Punto de Tránsito",
        _ => "Transit Point"
    };

    public string QuestObjective => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트 목표",
        AppLanguage.JA => "クエスト目標",
        AppLanguage.ES => "Objetivo de Misión",
        _ => "Quest Objective"
    };

    public string QuestType => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트 타입",
        AppLanguage.JA => "クエストタイプ",
        AppLanguage.ES => "Tipo de Misión",
        _ => "Quest Type"
    };

    public string Visit => CurrentLanguage switch {
        AppLanguage.KO => "방문",
        AppLanguage.JA => "訪問",
        AppLanguage.ES => "Visitar",
        _ => "Visit"
    };

    public string Mark => CurrentLanguage switch {
        AppLanguage.KO => "마킹",
        AppLanguage.JA => "マーキング",
        AppLanguage.ES => "Marcar",
        _ => "Mark"
    };

    public string PlantItem => CurrentLanguage switch {
        AppLanguage.KO => "아이템 설치",
        AppLanguage.JA => "アイテム設置",
        AppLanguage.ES => "Colocar Objeto",
        _ => "Plant Item"
    };

    public string Kill => CurrentLanguage switch {
        AppLanguage.KO => "처치",
        AppLanguage.JA => "撃破",
        AppLanguage.ES => "Eliminar",
        _ => "Kill"
    };

    #endregion

    #region Map Page - Quest Filter

    public string QuestTypeFilter => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트 타입 필터",
        AppLanguage.JA => "クエストタイプフィルター",
        AppLanguage.ES => "Filtro de Tipo",
        _ => "Quest Type Filter"
    };

    public string VisitType => CurrentLanguage switch {
        AppLanguage.KO => "방문 (Visit)",
        AppLanguage.JA => "訪問 (Visit)",
        AppLanguage.ES => "Visita",
        _ => "Visit"
    };

    public string MarkType => CurrentLanguage switch {
        AppLanguage.KO => "마킹 (Mark)",
        AppLanguage.JA => "マーキング (Mark)",
        AppLanguage.ES => "Marcado",
        _ => "Mark"
    };

    public string PlantType => CurrentLanguage switch {
        AppLanguage.KO => "아이템 설치 (Plant)",
        AppLanguage.JA => "アイテム設置 (Plant)",
        AppLanguage.ES => "Colocación",
        _ => "Plant Item"
    };

    public string ExtractType => CurrentLanguage switch {
        AppLanguage.KO => "탈출 (Extract)",
        AppLanguage.JA => "脱出 (Extract)",
        AppLanguage.ES => "Extracción",
        _ => "Extract"
    };

    public string FindType => CurrentLanguage switch {
        AppLanguage.KO => "아이템 찾기 (Find)",
        AppLanguage.JA => "アイテム発見 (Find)",
        AppLanguage.ES => "Búsqueda",
        _ => "Find Item"
    };

    public string KillType => CurrentLanguage switch {
        AppLanguage.KO => "처치 (Kill)",
        AppLanguage.JA => "撃破 (Kill)",
        AppLanguage.ES => "Eliminación",
        _ => "Kill"
    };

    public string OtherType => CurrentLanguage switch {
        AppLanguage.KO => "기타 (Other)",
        AppLanguage.JA => "その他 (Other)",
        AppLanguage.ES => "Otro",
        _ => "Other"
    };

    #endregion

    #region Map Page - Minimap

    public string Minimap => CurrentLanguage switch {
        AppLanguage.KO => "미니맵",
        AppLanguage.JA => "ミニマップ",
        AppLanguage.ES => "Minimapa",
        _ => "Minimap"
    };

    #endregion

    #region Map Page - Settings

    public string SettingsTitle => CurrentLanguage switch {
        AppLanguage.KO => "⚙ 설정",
        AppLanguage.JA => "⚙ 設定",
        AppLanguage.ES => "Ajustes del Mapa",
        _ => "⚙ Settings"
    };

    public string SettingsTooltip => CurrentLanguage switch {
        AppLanguage.KO => "설정 (레이어, 마커 크기, 트래커)",
        AppLanguage.JA => "設定 (レイヤー、マーカーサイズ、トラッカー)",
        AppLanguage.ES => "Ajustes",
        _ => "Settings (Layers, Marker Size, Tracker)"
    };

    public string TabDisplay => CurrentLanguage switch {
        AppLanguage.KO => "표시",
        AppLanguage.JA => "表示",
        AppLanguage.ES => "Visualización",
        _ => "Display"
    };

    public string TabMarker => CurrentLanguage switch {
        AppLanguage.KO => "마커",
        AppLanguage.JA => "マーカー",
        AppLanguage.ES => "Marcadores",
        _ => "Marker"
    };

    public string TabTracker => CurrentLanguage switch {
        AppLanguage.KO => "트래커",
        AppLanguage.JA => "トラッカー",
        AppLanguage.ES => "Rastreador",
        _ => "Tracker"
    };

    public string TabShortcuts => CurrentLanguage switch {
        AppLanguage.KO => "단축키",
        AppLanguage.JA => "ショートカット",
        AppLanguage.ES => "Atajos",
        _ => "Shortcuts"
    };

    // Display Tab
    public string Trail => CurrentLanguage switch {
        AppLanguage.KO => "이동 경로",
        AppLanguage.JA => "移動経路",
        AppLanguage.ES => "Rastro",
        _ => "Trail"
    };

    public string ShowMinimap => CurrentLanguage switch {
        AppLanguage.KO => "미니맵 표시",
        AppLanguage.JA => "ミニマップ表示",
        AppLanguage.ES => "Mostrar Minimapa",
        _ => "Show Minimap"
    };

    public string MinimapSize => CurrentLanguage switch {
        AppLanguage.KO => "미니맵 크기",
        AppLanguage.JA => "ミニマップサイズ",
        AppLanguage.ES => "Tamaño del Minimapa",
        _ => "Minimap Size"
    };

    public string QuestFilter => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트 필터",
        AppLanguage.JA => "クエストフィルター",
        AppLanguage.ES => "Filtro de Misiones",
        _ => "Quest Filter"
    };

    // Marker Tab
    public string MarkerSize => CurrentLanguage switch {
        AppLanguage.KO => "마커 크기",
        AppLanguage.JA => "マーカーサイズ",
        AppLanguage.ES => "Tamaño de Marcador",
        _ => "Marker Size"
    };

    public string MarkerOpacity => CurrentLanguage switch {
        AppLanguage.KO => "마커 투명도",
        AppLanguage.JA => "マーカー透明度",
        AppLanguage.ES => "Opacidad de Marcador",
        _ => "Marker Opacity"
    };

    public string QuestDisplay => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트 표시",
        AppLanguage.JA => "クエスト表示",
        AppLanguage.ES => "Muestra de Misión",
        _ => "Quest Display"
    };

    public string AutoHideCompleted => CurrentLanguage switch {
        AppLanguage.KO => "완료 퀘스트 자동 숨김",
        AppLanguage.JA => "完了クエストを自動非表示",
        AppLanguage.ES => "Ocultar Automáticamente Completadas",
        _ => "Auto-hide Completed Quests"
    };

    public string FadeCompleted => CurrentLanguage switch {
        AppLanguage.KO => "완료 퀘스트 흐리게",
        AppLanguage.JA => "完了クエストを薄く表示",
        AppLanguage.ES => "Atenuar Completadas",
        _ => "Fade Completed Quests"
    };

    public string ShowMarkerLabels => CurrentLanguage switch {
        AppLanguage.KO => "마커 라벨 표시",
        AppLanguage.JA => "マーカーラベル表示",
        AppLanguage.ES => "Mostrar Etiquetas de Marcador",
        _ => "Show Marker Labels"
    };

    // Tracker Tab
    public string TrackerStatus => CurrentLanguage switch {
        AppLanguage.KO => "트래커 상태",
        AppLanguage.JA => "トラッカー状態",
        AppLanguage.ES => "Estado del Rastreador",
        _ => "Tracker Status"
    };

    public string NoFolderSelected => CurrentLanguage switch {
        AppLanguage.KO => "폴더 미선택",
        AppLanguage.JA => "フォルダ未選択",
        AppLanguage.ES => "Ninguna carpeta seleccionada",
        _ => "No folder selected"
    };

    public string SelectScreenshotFolder => CurrentLanguage switch {
        AppLanguage.KO => "스크린샷 폴더 선택",
        AppLanguage.JA => "スクリーンショットフォルダ選択",
        AppLanguage.ES => "Seleccionar Carpeta de Capturas",
        _ => "Select Screenshot Folder"
    };

    public string OpenFolder => CurrentLanguage switch {
        AppLanguage.KO => "폴더 열기",
        AppLanguage.JA => "フォルダを開く",
        AppLanguage.ES => "Abrir Carpeta",
        _ => "Open Folder"
    };

    public string StartStopTracking => CurrentLanguage switch {
        AppLanguage.KO => "트래킹 시작/중지",
        AppLanguage.JA => "トラッキング開始/停止",
        AppLanguage.ES => "Iniciar/Detener Rastreo",
        _ => "Start/Stop Tracking"
    };

    public string ClearPath => CurrentLanguage switch {
        AppLanguage.KO => "경로 초기화",
        AppLanguage.JA => "経路クリア",
        AppLanguage.ES => "Borrar Trayectoria",
        _ => "Clear Path"
    };

    public string PathSettings => CurrentLanguage switch {
        AppLanguage.KO => "경로 설정",
        AppLanguage.JA => "経路設定",
        AppLanguage.ES => "Ajustes de Trayectoria",
        _ => "Path Settings"
    };

    public string PathColor => CurrentLanguage switch {
        AppLanguage.KO => "경로 색상",
        AppLanguage.JA => "経路色",
        AppLanguage.ES => "Color de Trayectoria",
        _ => "Path Color"
    };

    public string PathThickness => CurrentLanguage switch {
        AppLanguage.KO => "경로 두께",
        AppLanguage.JA => "経路太さ",
        AppLanguage.ES => "Grosor de Trayectoria",
        _ => "Path Thickness"
    };

    public string AutoTrackOnMapLoad => CurrentLanguage switch {
        AppLanguage.KO => "맵 로드시 자동 추적",
        AppLanguage.JA => "マップ読み込み時に自動追跡",
        AppLanguage.ES => "Rastreo Automático al Cargar Mapa",
        _ => "Auto Track on Map Load"
    };

    // Shortcuts Tab
    public string MapControls => CurrentLanguage switch {
        AppLanguage.KO => "맵 조작",
        AppLanguage.JA => "マップ操作",
        AppLanguage.ES => "Controles de Mapa",
        _ => "Map Controls"
    };

    public string ZoomInOut => CurrentLanguage switch {
        AppLanguage.KO => "확대/축소",
        AppLanguage.JA => "拡大/縮小",
        AppLanguage.ES => "Acercar / Alejar",
        _ => "Zoom In/Out"
    };

    public string PanMap => CurrentLanguage switch {
        AppLanguage.KO => "맵 이동",
        AppLanguage.JA => "マップ移動",
        AppLanguage.ES => "Desplazar Mapa",
        _ => "Pan Map"
    };

    public string LayerToggle => CurrentLanguage switch {
        AppLanguage.KO => "레이어 토글",
        AppLanguage.JA => "レイヤー切替",
        AppLanguage.ES => "Alternar Capas",
        _ => "Layer Toggle"
    };

    public string ShowHideExtracts => CurrentLanguage switch {
        AppLanguage.KO => "탈출구 표시/숨김",
        AppLanguage.JA => "脱出口表示/非表示",
        AppLanguage.ES => "Mostrar/Ocultar Extracciones",
        _ => "Show/Hide Extracts"
    };

    public string ShowHideTransit => CurrentLanguage switch {
        AppLanguage.KO => "환승 표시/숨김",
        AppLanguage.JA => "乗り換え表示/非表示",
        AppLanguage.ES => "Mostrar/Ocultar Tránsitos",
        _ => "Show/Hide Transit"
    };

    public string ShowHideQuests => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트 표시/숨김",
        AppLanguage.JA => "クエスト表示/非表示",
        AppLanguage.ES => "Mostrar/Ocultar Misiones",
        _ => "Show/Hide Quests"
    };

    public string Panel => CurrentLanguage switch {
        AppLanguage.KO => "패널",
        AppLanguage.JA => "パネル",
        AppLanguage.ES => "Panel",
        _ => "Panel"
    };

    public string QuestPanel => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트 패널",
        AppLanguage.JA => "クエストパネル",
        AppLanguage.ES => "Panel de Misiones",
        _ => "Quest Panel"
    };

    public string FloorChange => CurrentLanguage switch {
        AppLanguage.KO => "층 변경 (다층맵)",
        AppLanguage.JA => "階層変更 (多層マップ)",
        AppLanguage.ES => "Cambio de Planta",
        _ => "Floor Change (Multi-floor)"
    };

    // Footer
    public string ResetAllSettings => CurrentLanguage switch {
        AppLanguage.KO => "모든 설정 초기화",
        AppLanguage.JA => "すべての設定をリセット",
        AppLanguage.ES => "Restablecer Todos los Ajustes",
        _ => "Reset All Settings"
    };

    #endregion

    #region Map Page - Status Bar

    public string SelectMap => CurrentLanguage switch {
        AppLanguage.KO => "맵 선택",
        AppLanguage.JA => "マップ選択",
        AppLanguage.ES => "Seleccionar Mapa",
        _ => "Select Map"
    };

    public string CopyCoordinates => CurrentLanguage switch {
        AppLanguage.KO => "좌표 복사",
        AppLanguage.JA => "座標コピー",
        AppLanguage.ES => "Copiar Coordenadas",
        _ => "Copy Coordinates"
    };

    #endregion

    #region MapTrackerPage

    // Sidebar section headers
    public string MapTrackerLayers => CurrentLanguage switch {
        AppLanguage.KO => "레이어",
        AppLanguage.JA => "レイヤー",
        AppLanguage.ES => "Capas",
        _ => "LAYERS"
    };

    public string MapTrackerPointsOfInterest => CurrentLanguage switch {
        AppLanguage.KO => "관심 지점",
        AppLanguage.JA => "注目ポイント",
        AppLanguage.ES => "Puntos de Interés",
        _ => "Points of Interest"
    };

    public string MapTrackerEnemies => CurrentLanguage switch {
        AppLanguage.KO => "적",
        AppLanguage.JA => "敵",
        AppLanguage.ES => "Enemigos",
        _ => "Enemies"
    };

    public string MapTrackerInteractables => CurrentLanguage switch {
        AppLanguage.KO => "상호작용",
        AppLanguage.JA => "インタラクト",
        AppLanguage.ES => "Interactuables",
        _ => "Interactables"
    };

    public string MapTrackerQuests => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트",
        AppLanguage.JA => "クエスト",
        AppLanguage.ES => "Misiones",
        _ => "Quests"
    };

    public string MapTrackerQuickActions => CurrentLanguage switch {
        AppLanguage.KO => "빠른 실행",
        AppLanguage.JA => "クイックアクション",
        AppLanguage.ES => "Acciones Rápidas",
        _ => "QUICK ACTIONS"
    };

    public string MapTrackerShortcuts => CurrentLanguage switch {
        AppLanguage.KO => "단축키",
        AppLanguage.JA => "ショートカット",
        AppLanguage.ES => "Atajos",
        _ => "SHORTCUTS"
    };

    // Layer names
    public string MapTrackerExtractions => CurrentLanguage switch {
        AppLanguage.KO => "탈출구",
        AppLanguage.JA => "脱出口",
        AppLanguage.ES => "Extracciones",
        _ => "Extractions"
    };

    public string MapTrackerTransits => CurrentLanguage switch {
        AppLanguage.KO => "환승",
        AppLanguage.JA => "乗り換え",
        AppLanguage.ES => "Puntos de Tránsito",
        _ => "Transits"
    };

    public string MapTrackerSpawns => CurrentLanguage switch {
        AppLanguage.KO => "스폰",
        AppLanguage.JA => "スポーン",
        AppLanguage.ES => "Apariciones (Spawns)",
        _ => "Spawns"
    };

    public string MapTrackerBosses => CurrentLanguage switch {
        AppLanguage.KO => "보스",
        AppLanguage.JA => "ボス",
        AppLanguage.ES => "Jefes (Bosses)",
        _ => "Bosses"
    };

    public string MapTrackerLevers => CurrentLanguage switch {
        AppLanguage.KO => "레버",
        AppLanguage.JA => "レバー",
        AppLanguage.ES => "Palancas / Interruptores",
        _ => "Levers"
    };

    public string MapTrackerKeys => CurrentLanguage switch {
        AppLanguage.KO => "열쇠",
        AppLanguage.JA => "鍵",
        AppLanguage.ES => "Llaves",
        _ => "Keys"
    };

    public string MapTrackerQuestObjectives => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트 목표",
        AppLanguage.JA => "クエスト目標",
        AppLanguage.ES => "Objetivos de Misión",
        _ => "Quest Objectives"
    };

    // Quick actions
    public string MapTrackerShowAllLayers => CurrentLanguage switch {
        AppLanguage.KO => "모든 레이어 표시",
        AppLanguage.JA => "すべてのレイヤーを表示",
        AppLanguage.ES => "Mostrar Todas las Capas",
        _ => "Show All Layers"
    };

    public string MapTrackerHideAllLayers => CurrentLanguage switch {
        AppLanguage.KO => "모든 레이어 숨기기",
        AppLanguage.JA => "すべてのレイヤーを非表示",
        AppLanguage.ES => "Ocultar Todas las Capas",
        _ => "Hide All Layers"
    };

    // Status bar
    public string MapTrackerMarkersLabel => CurrentLanguage switch {
        AppLanguage.KO => "마커:",
        AppLanguage.JA => "マーカー:",
        AppLanguage.ES => "Marcadores",
        _ => "Markers:"
    };

    public string MapTrackerQuestsLabel => CurrentLanguage switch {
        AppLanguage.KO => "퀘스트:",
        AppLanguage.JA => "クエスト:",
        AppLanguage.ES => "Misiones",
        _ => "Quests:"
    };

    public string MapTrackerCursorLabel => CurrentLanguage switch {
        AppLanguage.KO => "커서:",
        AppLanguage.JA => "カーソル:",
        AppLanguage.ES => "Cursor:",
        _ => "Cursor:"
    };

    public string MapTrackerSelectMapMessage => CurrentLanguage switch {
        AppLanguage.KO => "위 드롭다운에서 맵을 선택하세요",
        AppLanguage.JA => "上のドロップダウンからマップを選択してください",
        AppLanguage.ES => "Por favor, selecciona un mapa para comenzar el rastreo.",
        _ => "Select a map from the dropdown above"
    };

    public string MapTrackerLoadingMap => CurrentLanguage switch {
        AppLanguage.KO => "맵 로딩 중...",
        AppLanguage.JA => "マップを読み込み中...",
        AppLanguage.ES => "Cargando mapa...",
        _ => "Loading map..."
    };

    // Top bar
    public string MapTrackerMapLabel => CurrentLanguage switch {
        AppLanguage.KO => "맵:",
        AppLanguage.JA => "マップ:",
        AppLanguage.ES => "Mapa:",
        _ => "Map:"
    };

    public string MapTrackerFloorLabel => CurrentLanguage switch {
        AppLanguage.KO => "층:",
        AppLanguage.JA => "フロア:",
        AppLanguage.ES => "Planta:",
        _ => "Floor:"
    };

    public string MapTrackerPlayerLabel => CurrentLanguage switch {
        AppLanguage.KO => "플레이어:",
        AppLanguage.JA => "プレイヤー:",
        AppLanguage.ES => "Jugador:",
        _ => "Player:"
    };

    public string MapTrackerAutoFloor => CurrentLanguage switch {
        AppLanguage.KO => "자동",
        AppLanguage.JA => "自動",
        AppLanguage.ES => "Autocambio de Planta",
        _ => "Auto"
    };

    public string MapTrackerAll => CurrentLanguage switch {
        AppLanguage.KO => "전체",
        AppLanguage.JA => "全て",
        AppLanguage.ES => "Todos",
        _ => "All"
    };

    public string MapTrackerNone => CurrentLanguage switch {
        AppLanguage.KO => "없음",
        AppLanguage.JA => "なし",
        AppLanguage.ES => "Ninguno",
        _ => "None"
    };

    public string MapTrackerStart => CurrentLanguage switch {
        AppLanguage.KO => "시작",
        AppLanguage.JA => "開始",
        AppLanguage.ES => "Iniciar",
        _ => "Start"
    };

    public string MapTrackerStop => CurrentLanguage switch {
        AppLanguage.KO => "중지",
        AppLanguage.JA => "停止",
        AppLanguage.ES => "Detener",
        _ => "Stop"
    };

    public string MapTrackerReady => CurrentLanguage switch {
        AppLanguage.KO => "준비 완료",
        AppLanguage.JA => "準備完了",
        AppLanguage.ES => "Listo",
        _ => "Ready"
    };

    public string MapTrackerKeyboardShortcuts => CurrentLanguage switch {
        AppLanguage.KO => "키보드 단축키",
        AppLanguage.JA => "キーボードショートカット",
        AppLanguage.ES => "Atajos de Teclado",
        _ => "Keyboard Shortcuts"
    };

    // Marker type localization method
    public string GetMarkerTypeName(MarkerType type) => type switch
    {
        MarkerType.PmcExtraction => CurrentLanguage switch
        {
            AppLanguage.KO => "PMC 탈출구",
            AppLanguage.JA => "PMC脱出口",
            _ => "PMC Extraction"
        },
        MarkerType.ScavExtraction => CurrentLanguage switch
        {
            AppLanguage.KO => "스캐브 탈출구",
            AppLanguage.JA => "Scav脱出口",
            _ => "Scav Extraction"
        },
        MarkerType.SharedExtraction => CurrentLanguage switch
        {
            AppLanguage.KO => "공유 탈출구",
            AppLanguage.JA => "共有脱出口",
            _ => "Shared Extraction"
        },
        MarkerType.Transit => CurrentLanguage switch
        {
            AppLanguage.KO => "환승 지점",
            AppLanguage.JA => "乗り換え地点",
            _ => "Transit Point"
        },
        MarkerType.PmcSpawn => CurrentLanguage switch
        {
            AppLanguage.KO => "PMC 스폰",
            AppLanguage.JA => "PMCスポーン",
            _ => "PMC Spawn"
        },
        MarkerType.ScavSpawn => CurrentLanguage switch
        {
            AppLanguage.KO => "스캐브 스폰",
            AppLanguage.JA => "Scavスポーン",
            _ => "Scav Spawn"
        },
        MarkerType.BossSpawn => CurrentLanguage switch
        {
            AppLanguage.KO => "보스 스폰",
            AppLanguage.JA => "ボススポーン",
            _ => "Boss Spawn"
        },
        MarkerType.RaiderSpawn => CurrentLanguage switch
        {
            AppLanguage.KO => "레이더 스폰",
            AppLanguage.JA => "レイダースポーン",
            _ => "Raider Spawn"
        },
        MarkerType.Lever => CurrentLanguage switch
        {
            AppLanguage.KO => "레버/스위치",
            AppLanguage.JA => "レバー/スイッチ",
            _ => "Lever/Switch"
        },
        MarkerType.Keys => CurrentLanguage switch
        {
            AppLanguage.KO => "열쇠 위치",
            AppLanguage.JA => "鍵の場所",
            _ => "Key Location"
        },
        _ => "Unknown"
    };

    #endregion

    #region Custom Markers & Fixed View

    public string FixedView => CurrentLanguage switch {
        AppLanguage.KO => "고정 뷰",
        AppLanguage.ES => "Vista Fija",
        _ => "Fixed View"
    };

    public string CustomMarkers => CurrentLanguage switch {
        AppLanguage.KO => "사용자 마커",
        AppLanguage.ES => "Marcadores Personalizados",
        _ => "Custom Markers"
    };

    public string AddCustomMarker => CurrentLanguage switch {
        AppLanguage.KO => "마커 추가",
        AppLanguage.ES => "Añadir Marcador",
        _ => "Add Custom Marker"
    };

    public string DeleteCustomMarker => CurrentLanguage switch {
        AppLanguage.KO => "마커 삭제",
        AppLanguage.ES => "Eliminar Marcador",
        _ => "Delete Custom Marker"
    };

    public string MarkerName => CurrentLanguage switch {
        AppLanguage.KO => "마커 이름",
        AppLanguage.ES => "Nombre del Marcador",
        _ => "Marker Name"
    };

    public string EnterMarkerName => CurrentLanguage switch {
        AppLanguage.KO => "마커 이름을 입력하세요",
        AppLanguage.ES => "Introduce el nombre del marcador",
        _ => "Enter marker name"
    };

    public string CustomMarkerColor => CurrentLanguage switch {
        AppLanguage.KO => "마커 색상",
        AppLanguage.ES => "Color del Marcador",
        _ => "Marker Color"
    };

    #endregion
}
