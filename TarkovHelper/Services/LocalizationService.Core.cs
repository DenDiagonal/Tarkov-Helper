using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace TarkovHelper.Services;

/// <summary>
/// Supported languages
/// </summary>
public enum AppLanguage
{
    EN,
    KO,
    JA,
    ES
}

/// <summary>
/// Core functionality for LocalizationService - settings persistence and common UI strings.
/// This is a partial class; other parts contain domain-specific strings.
/// </summary>
public partial class LocalizationService : INotifyPropertyChanged
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    private readonly UserDataDbService _userDataDb = UserDataDbService.Instance;
    private const string KeyLanguage = "app.language";

    private AppLanguage _currentLanguage = AppLanguage.EN;

    public LocalizationService()
    {
        LoadSettings();
    }

    public AppLanguage CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                OnPropertyChanged(nameof(CurrentLanguage));
                LanguageChanged?.Invoke(this, value);
                SaveSettings();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<AppLanguage>? LanguageChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #region Settings Persistence

    private void SaveSettings()
    {
        try
        {
            _userDataDb.SetSetting(KeyLanguage, _currentLanguage.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save language setting: {ex.Message}");
        }
    }

    private void LoadSettings()
    {
        try
        {
            var savedLang = _userDataDb.GetSetting(KeyLanguage);
            if (!string.IsNullOrEmpty(savedLang) && Enum.TryParse<AppLanguage>(savedLang, out var lang))
            {
                _currentLanguage = lang;
            }
            else
            {
                _currentLanguage = AppLanguage.EN;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load language setting: {ex.Message}");
            _currentLanguage = AppLanguage.EN;
        }
    }

    #endregion

    #region Common UI Strings

    public string Welcome => CurrentLanguage switch
    {
        AppLanguage.ES => "Bienvenido a Tarkov Helper",
        AppLanguage.KO => "Tarkov Helper에 오신 것을 환영합니다",
        AppLanguage.JA => "Tarkov Helperへようこそ",
        _ => "Welcome to Tarkov Helper"
    };

    public string Cancel => CurrentLanguage switch
    {
        AppLanguage.ES => "Cancelar",
        AppLanguage.KO => "취소",
        AppLanguage.JA => "キャンセル",
        _ => "Cancel"
    };

    public string Apply => CurrentLanguage switch
    {
        AppLanguage.ES => "Aplicar",
        AppLanguage.KO => "적용",
        AppLanguage.JA => "適用",
        _ => "Apply"
    };

    public string Close => CurrentLanguage switch
    {
        AppLanguage.ES => "Cerrar",
        AppLanguage.KO => "닫기",
        AppLanguage.JA => "閉じる",
        _ => "Close"
    };

    public string Settings => CurrentLanguage switch
    {
        AppLanguage.ES => "Ajustes",
        AppLanguage.KO => "설정",
        AppLanguage.JA => "設定",
        _ => "Settings"
    };

    public string Browse => CurrentLanguage switch
    {
        AppLanguage.ES => "Examinar...",
        AppLanguage.KO => "찾아보기",
        AppLanguage.JA => "参照",
        _ => "Browse"
    };

    public string Open => CurrentLanguage switch
    {
        AppLanguage.ES => "Abrir",
        AppLanguage.KO => "열기",
        AppLanguage.JA => "開く",
        _ => "Open"
    };

    public string Start => CurrentLanguage switch
    {
        AppLanguage.ES => "Iniciar",
        AppLanguage.KO => "시작",
        AppLanguage.JA => "開始",
        _ => "Start"
    };

    public string Stop => CurrentLanguage switch
    {
        AppLanguage.ES => "Detener",
        AppLanguage.KO => "중지",
        AppLanguage.JA => "停止",
        _ => "Stop"
    };

    public string Reset => CurrentLanguage switch
    {
        AppLanguage.ES => "Restablecer",
        AppLanguage.KO => "리셋",
        AppLanguage.JA => "リセット",
        _ => "Reset"
    };

    public string ResetAll => CurrentLanguage switch
    {
        AppLanguage.ES => "Restablecer Todo",
        AppLanguage.KO => "초기화",
        AppLanguage.JA => "すべてリセット",
        _ => "Reset All"
    };

    public string SelectAll => CurrentLanguage switch
    {
        AppLanguage.ES => "Seleccionar Todo",
        AppLanguage.KO => "전체 선택",
        AppLanguage.JA => "すべて選択",
        _ => "Select All"
    };

    public string DeselectAll => CurrentLanguage switch
    {
        AppLanguage.ES => "Deseleccionar Todo",
        AppLanguage.KO => "전체 해제",
        AppLanguage.JA => "すべて解除",
        _ => "Deselect All"
    };

    public string ShowAll => CurrentLanguage switch
    {
        AppLanguage.ES => "Mostrar Todo",
        AppLanguage.KO => "전체 표시",
        AppLanguage.JA => "すべて表示",
        _ => "Show All"
    };

    public string HideAll => CurrentLanguage switch
    {
        AppLanguage.ES => "Ocultar Todo",
        AppLanguage.KO => "전체 숨기기",
        AppLanguage.JA => "すべて非表示",
        _ => "Hide All"
    };

    public string ExpandAll => CurrentLanguage switch
    {
        AppLanguage.ES => "Expandir Todo",
        AppLanguage.KO => "전체 펼치기",
        AppLanguage.JA => "すべて展開",
        _ => "Expand All"
    };

    public string CollapseAll => CurrentLanguage switch
    {
        AppLanguage.ES => "Contraer Todo",
        AppLanguage.KO => "전체 접기",
        AppLanguage.JA => "すべて折りたたむ",
        _ => "Collapse All"
    };

    public string ShowMore => CurrentLanguage switch
    {
        AppLanguage.ES => "Mostrar Más",
        AppLanguage.KO => "더 보기",
        AppLanguage.JA => "もっと見る",
        _ => "Show More"
    };

    public string ShowLess => CurrentLanguage switch
    {
        AppLanguage.ES => "Mostrar Menos",
        AppLanguage.KO => "접기",
        AppLanguage.JA => "折りたたむ",
        _ => "Show Less"
    };

    public string FilterAll => CurrentLanguage switch
    {
        AppLanguage.ES => "Todos",
        AppLanguage.KO => "전체",
        AppLanguage.JA => "すべて",
        _ => "All"
    };

    public string Folder => CurrentLanguage switch
    {
        AppLanguage.ES => "Carpeta",
        AppLanguage.KO => "폴더",
        AppLanguage.JA => "フォルダー",
        _ => "Folder"
    };

    public string Waiting => CurrentLanguage switch
    {
        AppLanguage.ES => "En espera",
        AppLanguage.KO => "대기 중",
        AppLanguage.JA => "待機中",
        _ => "Waiting"
    };

    public string Tracking => CurrentLanguage switch
    {
        AppLanguage.ES => "Rastreando",
        AppLanguage.KO => "추적 중",
        AppLanguage.JA => "追跡中",
        _ => "Tracking"
    };

    public string AutoDetect => CurrentLanguage switch
    {
        AppLanguage.ES => "Autodetectar",
        AppLanguage.KO => "자동 감지",
        AppLanguage.JA => "自動検出",
        _ => "Auto Detect"
    };

    public string Automation => CurrentLanguage switch
    {
        AppLanguage.ES => "Automatización",
        AppLanguage.KO => "자동화",
        AppLanguage.JA => "自動化",
        _ => "Automation"
    };

    public string Layers => CurrentLanguage switch
    {
        AppLanguage.ES => "Capas",
        AppLanguage.KO => "레이어",
        AppLanguage.JA => "レイヤー",
        _ => "Layers"
    };

    public string Legend => CurrentLanguage switch
    {
        AppLanguage.ES => "Leyenda",
        AppLanguage.KO => "범례",
        AppLanguage.JA => "凡例",
        _ => "Legend"
    };

    public string Language => CurrentLanguage switch
    {
        AppLanguage.ES => "Idioma",
        AppLanguage.KO => "언어",
        AppLanguage.JA => "言語",
        _ => "Language"
    };

    // Main Navigation Tabs
    public string Dashboard => CurrentLanguage switch
    {
        AppLanguage.ES => "Panel Principal",
        AppLanguage.KO => "대시보드",
        AppLanguage.JA => "ダッシュボード",
        _ => "Dashboard"
    };

    public string Storyline => CurrentLanguage switch
    {
        AppLanguage.ES => "Historia",
        AppLanguage.KO => "스토리라인",
        AppLanguage.JA => "ストーリーライン",
        _ => "Storyline"
    };

    public string Quests => CurrentLanguage switch
    {
        AppLanguage.ES => "Misiones",
        AppLanguage.KO => "퀘스트",
        AppLanguage.JA => "クエスト",
        _ => "Quests"
    };

    public string NeededItems => CurrentLanguage switch
    {
        AppLanguage.ES => "Objetos Necesarios",
        AppLanguage.KO => "필요 아이템",
        AppLanguage.JA => "必要なアイテム",
        _ => "Needed Items"
    };

    public string Hideout => CurrentLanguage switch
    {
        AppLanguage.ES => "Refugio",
        AppLanguage.KO => "은신처",
        AppLanguage.JA => "ハイドアウト",
        _ => "Hideout"
    };

    public string Items => CurrentLanguage switch
    {
        AppLanguage.ES => "Objetos",
        AppLanguage.KO => "아이템",
        AppLanguage.JA => "アイテム",
        _ => "Items"
    };

    public string Collector => CurrentLanguage switch
    {
        AppLanguage.ES => "Coleccionista - Kappa",
        AppLanguage.KO => "수집가 - 카파",
        AppLanguage.JA => "コレクター - Kappa",
        _ => "Collector - Kappa"
    };

    public string Map => CurrentLanguage switch
    {
        AppLanguage.ES => "Mapa",
        AppLanguage.KO => "지도",
        AppLanguage.JA => "マップ",
        _ => "Map"
    };

    public string Profile => CurrentLanguage switch
    {
        AppLanguage.ES => "Perfil",
        AppLanguage.KO => "프로필",
        _ => "Profile"
    };

    public string Sync => CurrentLanguage switch
    {
        AppLanguage.ES => "Sincronizar",
        AppLanguage.KO => "동기화",
        AppLanguage.JA => "同期",
        _ => "Sync"
    };

    public string SyncQuests => CurrentLanguage switch
    {
        AppLanguage.ES => "Sincronizar Misiones",
        AppLanguage.KO => "퀘스트 동기화",
        AppLanguage.JA => "クエスト同期",
        _ => "Sync Quests"
    };

    public string StartMonitoring => CurrentLanguage switch
    {
        AppLanguage.ES => "Iniciar Monitorización",
        AppLanguage.KO => "모니터링 시작",
        AppLanguage.JA => "監視開始",
        _ => "Start Monitoring"
    };

    public string StopMonitoring => CurrentLanguage switch
    {
        AppLanguage.ES => "Detener Monitorización",
        AppLanguage.KO => "모니터링 중지",
        AppLanguage.JA => "監視停止",
        _ => "Stop Monitoring"
    };

    public string Monitoring => CurrentLanguage switch
    {
        AppLanguage.ES => "Monitorizando",
        AppLanguage.KO => "모니터링 중",
        AppLanguage.JA => "監視中",
        _ => "Monitoring"
    };

    public string NotMonitoring => CurrentLanguage switch
    {
        AppLanguage.ES => "Sin monitorizar",
        AppLanguage.KO => "모니터링 안 함",
        AppLanguage.JA => "未監視",
        _ => "Not monitoring"
    };

    public string ValidPath => CurrentLanguage switch
    {
        AppLanguage.ES => "Ruta válida",
        AppLanguage.KO => "유효한 경로",
        AppLanguage.JA => "有効なパス",
        _ => "Valid path"
    };

    public string InvalidPath => CurrentLanguage switch
    {
        AppLanguage.ES => "Ruta no válida",
        AppLanguage.KO => "유효하지 않은 경로",
        AppLanguage.JA => "無効なパス",
        _ => "Invalid path"
    };

    public string ClearCache => CurrentLanguage switch
    {
        AppLanguage.ES => "Limpiar Caché",
        AppLanguage.KO => "캐시 삭제",
        AppLanguage.JA => "キャッシュクリア",
        _ => "Clear Cache"
    };

    public string DeleteAllData => CurrentLanguage switch
    {
        AppLanguage.ES => "Borrar Todos los Datos",
        AppLanguage.KO => "모든 데이터 삭제",
        AppLanguage.JA => "全データ削除",
        _ => "Delete All Data"
    };

    public string RestoreBackup => CurrentLanguage switch
    {
        AppLanguage.ES => "Restaurar Copia",
        AppLanguage.KO => "백업 복원",
        AppLanguage.JA => "バックアップ復元",
        _ => "Restore Backup"
    };

    public string RestoreDefaults => CurrentLanguage switch
    {
        AppLanguage.ES => "Restablecer Valores",
        AppLanguage.KO => "기본값 복원",
        AppLanguage.JA => "デフォルトに戻す",
        _ => "Restore Defaults"
    };

    public string EnterInProgressQuests => CurrentLanguage switch
    {
        AppLanguage.ES => "Introducir Misiones en Progreso",
        AppLanguage.KO => "진행 중 퀘스트 입력",
        AppLanguage.JA => "進行中クエスト入力",
        _ => "Enter In-Progress Quests"
    };

    public string LogFolderDesc => CurrentLanguage switch
    {
        AppLanguage.ES => "Ruta de la carpeta Logs de Tarkov para rastrear automáticamente misiones completadas.",
        AppLanguage.KO => "완료된 퀘스트를 자동으로 추적하기 위해 Tarkov Logs 폴더 경로를 설정합니다.",
        AppLanguage.JA => "完了したクエストを自動追跡するためのTarkov Logsフォルダパスを設定します。",
        _ => "Set the Tarkov Logs folder path to automatically track completed quests."
    };

    public string QuestSyncDesc => CurrentLanguage switch
    {
        AppLanguage.ES => "Sincroniza el progreso de las misiones leyendo los archivos de registro de EFT.",
        AppLanguage.KO => "게임 로그 파일에서 퀘스트 진행 상황을 동기화합니다.",
        AppLanguage.JA => "ゲームログファイルからクエスト進行状況を同期します。",
        _ => "Sync quest progress from game log files. Analyzes Tarkov logs to update completed quests."
    };

    public string CacheDesc => CurrentLanguage switch
    {
        AppLanguage.ES => "Limpia la caché de páginas wiki y datos descargados. Usar si hay datos corruptos o para volver a descargar todo.",
        AppLanguage.KO => "위키 페이지 캐시와 다운로드된 데이터를 정리합니다.",
        AppLanguage.JA => "Wikiページのキャッシュとダウンロードされたデータをクリアします。",
        _ => "Clear wiki page cache and downloaded data. Use if data is corrupted or to re-download everything."
    };

    public string DataUpdateDesc => CurrentLanguage switch
    {
        AppLanguage.ES => "Actualiza manualmente los requisitos más recientes de misiones y comerciantes desde la API de Tarkov Tracker.",
        AppLanguage.KO => "Tarkov Tracker API에서 최신 퀘스트 및 상인 요구 사항을 수동으로 업데이트합니다.",
        AppLanguage.JA => "Tarkov Tracker APIから最新のクエストおよびトレーダー要件を手動で更新します。",
        _ => "Manually update latest quest and trader requirements from Tarkov Tracker API."
    };

    public string LanguageSelectionDesc => CurrentLanguage switch
    {
        AppLanguage.ES => "Selecciona el idioma de la interfaz de la aplicación.",
        AppLanguage.KO => "애플리케이션 인터페이스 언어를 선택합니다.",
        AppLanguage.JA => "アプリケーションの表示言語を選択します。",
        _ => "Select application display language."
    };

    public string PlayerLevel => CurrentLanguage switch
    {
        AppLanguage.ES => "Nivel de Jugador",
        AppLanguage.KO => "플레이어 레벨",
        AppLanguage.JA => "プレイヤーレベル",
        _ => "Player Level"
    };

    public string ScavRep => CurrentLanguage switch
    {
        AppLanguage.ES => "Reputación Scav",
        AppLanguage.KO => "스캐브 평판",
        AppLanguage.JA => "Scav評判",
        _ => "Scav Karma"
    };

    public string DspDecode => CurrentLanguage switch
    {
        AppLanguage.ES => "Decodificaciones DSP",
        AppLanguage.KO => "DSP 디코드 횟수",
        AppLanguage.JA => "DSPデコード回数",
        _ => "DSP Decode Count"
    };

    public string Edition => CurrentLanguage switch
    {
        AppLanguage.ES => "Edición",
        AppLanguage.KO => "에디션",
        AppLanguage.JA => "エディション",
        _ => "Edition"
    };

    public string PrestigeLevel => CurrentLanguage switch
    {
        AppLanguage.ES => "Nivel de Prestigio",
        AppLanguage.KO => "프레스티지 레벨",
        AppLanguage.JA => "プレステージレベル",
        _ => "Prestige Level"
    };

    public string LoadingData => CurrentLanguage switch
    {
        AppLanguage.ES => "Cargando datos...",
        AppLanguage.KO => "데이터 로딩 중...",
        AppLanguage.JA => "データ読み込み中...",
        _ => "Loading data..."
    };

    public string Initializing => CurrentLanguage switch
    {
        AppLanguage.ES => "Inicializando...",
        AppLanguage.KO => "초기화 중...",
        AppLanguage.JA => "初期化中...",
        _ => "Initializing..."
    };

    public string LogFolder => CurrentLanguage switch
    {
        AppLanguage.ES => "Carpeta de Registros de Tarkov",
        AppLanguage.KO => "Tarkov 로그 폴더",
        AppLanguage.JA => "Tarkovログフォルダ",
        _ => "Tarkov Log Folder"
    };

    public string QuestLogSync => CurrentLanguage switch
    {
        AppLanguage.ES => "Sincronización de Misiones",
        AppLanguage.KO => "퀘스트 로그 동기화",
        AppLanguage.JA => "クエストログ同期",
        _ => "Quest Log Sync"
    };

    public string CacheManagement => CurrentLanguage switch
    {
        AppLanguage.ES => "Gestión de Caché",
        AppLanguage.KO => "캐시 관리",
        AppLanguage.JA => "キャッシュ管理",
        _ => "Cache Management"
    };

    public string FontSize => CurrentLanguage switch
    {
        AppLanguage.ES => "Tamaño de Fuente",
        AppLanguage.KO => "글꼴 크기",
        AppLanguage.JA => "フォントサイズ",
        _ => "Font Size"
    };

    public string Font => CurrentLanguage switch
    {
        AppLanguage.ES => "Fuente",
        AppLanguage.KO => "글꼴",
        AppLanguage.JA => "フォント",
        _ => "Font"
    };

    public string FontDesc => CurrentLanguage switch
    {
        AppLanguage.ES => "Cambiar la fuente de la aplicación. Reconoce automáticamente fuentes en la carpeta Fonts.",
        AppLanguage.KO => "어플리케이션에 사용할 글꼴을 선택합니다. Fonts 폴더의 폰트 파일(.ttf, .otf)을 자동으로 인식합니다.",
        AppLanguage.JA => "アプリケーションで使用するフォントを選択します。Fontsフォルダ内のフォントを自動検出します。",
        _ => "Select font to use in the application. Automatically detects fonts (.ttf, .otf) in the Fonts folder."
    };

    public string Unknown => CurrentLanguage switch
    {
        AppLanguage.ES => "Desconocido",
        AppLanguage.KO => "알 수 없음",
        AppLanguage.JA => "不明",
        _ => "Unknown"
    };

    public string UpdateApiData => CurrentLanguage switch
    {
        AppLanguage.ES => "Actualizar Datos de la API",
        AppLanguage.KO => "API 데이터 업데이트",
        AppLanguage.JA => "APIデータ更新",
        _ => "Update API Data"
    };

    public string ApiUpdateCheck => CurrentLanguage switch
    {
        AppLanguage.ES => "Buscando actualizaciones...",
        AppLanguage.KO => "업데이트 확인 중...",
        AppLanguage.JA => "更新を確認中...",
        _ => "Checking for updates..."
    };

    public string ApiUpdateSuccess => CurrentLanguage switch
    {
        AppLanguage.ES => "Actualizado correctamente.",
        AppLanguage.KO => "성공적으로 업데이트되었습니다.",
        AppLanguage.JA => "正常に更新されました。",
        _ => "Successfully updated."
    };

    public string ApiUpdateFail => CurrentLanguage switch
    {
        AppLanguage.ES => "Fallo al actualizar: {0}",
        AppLanguage.KO => "업데이트 실패: {0}",
        AppLanguage.JA => "更新失敗: {0}",
        _ => "Update failed: {0}"
    };

    public string ApiUpToDate => CurrentLanguage switch
    {
        AppLanguage.ES => "Ya está actualizado a la última versión.",
        AppLanguage.KO => "이미 최신 버전입니다.",
        AppLanguage.JA => "すでに最新バージョンです。",
        _ => "Already up to date."
    };

    // Items Page Stats
    public string ItemsShowing => CurrentLanguage switch
    {
        AppLanguage.ES => "Mostrando {0} objetos",
        AppLanguage.KO => "아이템 {0}개 표시 중",
        AppLanguage.JA => "{0}個のアイテムを表示中",
        _ => "Showing {0} items"
    };

    // Map Page
    public string MapNoMapImage => CurrentLanguage switch
    {
        AppLanguage.ES => "No hay imagen de mapa disponible",
        AppLanguage.KO => "사용 가능한 지도 이미지가 없습니다",
        AppLanguage.JA => "利用可能なマップ画像がありません",
        _ => "No map image available"
    };

    public string MapAddMapImageHint => CurrentLanguage switch
    {
        AppLanguage.ES => "Añade una imagen a la carpeta Assets/Maps/",
        AppLanguage.KO => "Assets/Maps/ 폴더에 이미지를 추가하세요",
        AppLanguage.JA => "Assets/Maps/フォルダに画像を追加してください",
        _ => "Add image to Assets/Maps/ folder"
    };

    public string MapSetImagePathHint => CurrentLanguage switch
    {
        AppLanguage.ES => "O especifica la ruta en Ajustes",
        AppLanguage.KO => "또는 설정에서 경로를 지정하세요",
        AppLanguage.JA => "または設定でパスを指定してください",
        _ => "Or specify path in Settings"
    };

    // Story Chapters & Prerequisites Override (Feature 7)
    public string StoryTabTitle => CurrentLanguage switch
    {
        AppLanguage.ES => "Historia",
        AppLanguage.KO => "스토리",
        AppLanguage.JA => "ストーリー",
        _ => "Story"
    };

    public string StoryTraderName => CurrentLanguage switch
    {
        AppLanguage.ES => "Capítulos de Historia",
        AppLanguage.KO => "스토리 챕터",
        AppLanguage.JA => "ストーリーチャプター",
        _ => "Story Chapters"
    };

    public string OverridePrerequisite => CurrentLanguage switch
    {
        AppLanguage.ES => "Ignorar prerequisito / Marcar disponible",
        AppLanguage.KO => "선행 조건 무시 / 활성화",
        AppLanguage.JA => "前提条件を無視 / 有効化",
        _ => "Ignore prerequisite / Mark available"
    };

    public string OverrideActiveRestore => CurrentLanguage switch
    {
        AppLanguage.ES => "Anulación activa (Restaurar)",
        AppLanguage.KO => "수동 활성화 됨 (복원)",
        AppLanguage.JA => "手動オーバーライド有効 (復元)",
        _ => "Override Active (Restore)"
    };

    public string RemoveManualOverride => CurrentLanguage switch
    {
        AppLanguage.ES => "Quitar anulación manual",
        AppLanguage.KO => "수동 활성화 해제",
        AppLanguage.JA => "手動オーバーライド解除",
        _ => "Override Active (Re-lock)"
    };

    public string ToggleCompletedTooltip => CurrentLanguage switch
    {
        AppLanguage.ES => "Alternar completado (1 clic)",
        AppLanguage.KO => "완료 토글 (1 클릭)",
        AppLanguage.JA => "完了を切り替え (1クリック)",
        _ => "Toggle completed (1-click)"
    };

    public string UnresolvedPrerequisiteWarning => CurrentLanguage switch
    {
        AppLanguage.ES => "Prerequisito no resuelto",
        AppLanguage.KO => "미해결 선행 조건",
        AppLanguage.JA => "未解決の前提条件",
        _ => "Unresolved prerequisite"
    };

    #endregion

    #region Common Name Localization (Trader, Map)

    public string GetLocalizedTraderName(string? englishName)
    {
        var lower = englishName?.ToLowerInvariant();
        if (lower == "story" || lower == "story chapters")
        {
            return CurrentLanguage switch
            {
                AppLanguage.ES => "Capítulos de Historia",
                AppLanguage.KO => "스토리 챕터",
                AppLanguage.JA => "ストーリーチャプター",
                _ => "Story Chapters"
            };
        }

        if (CurrentLanguage == AppLanguage.KO)
        {
            return lower switch
            {
                "prapor" => "프라포",
                "therapist" => "테라피스트",
                "skier" => "스키어",
                "peacekeeper" => "피스키퍼",
                "mechanic" => "메카닉",
                "ragman" => "래그맨",
                "jaeger" => "예거",
                "fence" => "펜스",
                "lightkeeper" => "등대지기",
                "ref" => "레프",
                "arena" => "아레나",
                "the labyrinth" => "미궁",
                "btr driver" => "BTR 운전수",
                _ => englishName ?? Unknown
            };
        }

        if (CurrentLanguage == AppLanguage.JA)
        {
            return lower switch
            {
                "prapor" => "プラポール",
                "therapist" => "セラピスト",
                "skier" => "スキーヤー",
                "peacekeeper" => "ピースキーパー",
                "mechanic" => "メカニック",
                "ragman" => "ラグマン",
                "jaeger" => "イェーガー",
                "fence" => "フェンス",
                "lightkeeper" => "ライトキーパー",
                "ref" => "レフ",
                "arena" => "アリーナ",
                "the labyrinth" => "迷宮",
                "btr driver" => "BTRドライバー",
                _ => englishName ?? Unknown
            };
        }

        if (CurrentLanguage == AppLanguage.ES)
        {
            return lower switch
            {
                "prapor" => "Prapor",
                "therapist" => "Therapist",
                "skier" => "Skier",
                "peacekeeper" => "Peacekeeper",
                "mechanic" => "Mechanic",
                "ragman" => "Ragman",
                "jaeger" => "Jaeger",
                "fence" => "Fence",
                "lightkeeper" => "Lightkeeper",
                "ref" => "Ref",
                "arena" => "Arena",
                "the labyrinth" => "El Laberinto",
                "btr driver" => "Conductor BTR",
                _ => englishName ?? Unknown
            };
        }

        return lower switch
        {
            "story" or "story chapters" => "Story Chapters",
            _ => englishName ?? Unknown
        };
    }

    public string GetLocalizedTraderShortName(string? englishName)
    {
        var lower = englishName?.ToLowerInvariant();
        if (lower == "story" || lower == "story chapters")
        {
            return CurrentLanguage switch
            {
                AppLanguage.ES => "Historia",
                AppLanguage.KO => "스토리",
                AppLanguage.JA => "ストーリー",
                _ => "Story"
            };
        }
        return GetLocalizedTraderName(englishName);
    }

    public string GetLocalizedMapName(string? englishName)
    {
        if (CurrentLanguage == AppLanguage.KO)
        {
            return englishName?.ToLowerInvariant().Replace("-", " ") switch
            {
                "customs" => "세관",
                "factory" => "공장",
                "interchange" => "인터체인지",
                "reserve" => "리저브",
                "shoreline" => "해안선",
                "woods" => "삼림",
                "lighthouse" => "등대",
                "streets" or "streets of tarkov" or "streetsoftarkov" => "타르코프 시내",
                "ground zero" or "groundzero" => "그라운드 제로",
                "the lab" or "the labs" or "labs" => "연구소",
                "the labyrinth" or "labyrinth" => "미궁",
                "terminal" => "터미널",
                "arena" => "아레나",
                _ => englishName ?? Unknown
            };
        }

        if (CurrentLanguage == AppLanguage.ES)
        {
            return englishName?.ToLowerInvariant().Replace("-", " ") switch
            {
                "customs" => "Aduana (Customs)",
                "factory" => "Fábrica (Factory)",
                "interchange" => "Intercambiador (Interchange)",
                "reserve" => "Reserva (Reserve)",
                "shoreline" => "Costa (Shoreline)",
                "woods" => "Bosque (Woods)",
                "lighthouse" => "Faro (Lighthouse)",
                "streets" or "streets of tarkov" or "streetsoftarkov" => "Calles de Tarkov (Streets)",
                "ground zero" or "groundzero" => "Zona Cero (Ground Zero)",
                "the lab" or "the labs" or "labs" => "El Laboratorio (The Lab)",
                "the labyrinth" or "labyrinth" => "El Laberinto",
                "terminal" => "Terminal",
                "arena" => "Arena",
                _ => englishName ?? Unknown
            };
        }
        return englishName?.ToLowerInvariant().Replace("-", " ") switch
        {
            "customs" => "Customs",
            "factory" => "Factory",
            "interchange" => "Interchange",
            "reserve" => "Reserve",
            "shoreline" => "Shoreline",
            "woods" => "Woods",
            "lighthouse" => "Lighthouse",
            "streets" or "streets of tarkov" or "streetsoftarkov" => "Streets of Tarkov",
            "ground zero" or "groundzero" => "Ground Zero",
            "the lab" or "the labs" or "labs" => "The Lab",
            "the labyrinth" or "labyrinth" => "The Labyrinth",
            "terminal" => "Terminal",
            "arena" => "Arena",
            _ => englishName ?? Unknown
        };
    }

    #endregion
}
