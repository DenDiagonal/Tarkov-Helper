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
    JA
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
        AppLanguage.KO => "Tarkov Helper에 오신 것을 환영합니다",
        AppLanguage.JA => "Tarkov Helperへようこそ",
        _ => "Welcome to Tarkov Helper"
    };

    public string Cancel => CurrentLanguage switch
    {
        AppLanguage.KO => "취소",
        AppLanguage.JA => "キャンセル",
        _ => "Cancel"
    };

    public string Apply => CurrentLanguage switch
    {
        AppLanguage.KO => "적용",
        AppLanguage.JA => "適用",
        _ => "Apply"
    };

    public string Close => CurrentLanguage switch
    {
        AppLanguage.KO => "닫기",
        AppLanguage.JA => "閉じる",
        _ => "Close"
    };

    public string Settings => CurrentLanguage switch
    {
        AppLanguage.KO => "설정",
        AppLanguage.JA => "設定",
        _ => "Settings"
    };

    public string Browse => CurrentLanguage switch
    {
        AppLanguage.KO => "찾아보기",
        AppLanguage.JA => "参照",
        _ => "Browse"
    };

    public string Open => CurrentLanguage switch
    {
        AppLanguage.KO => "열기",
        AppLanguage.JA => "開く",
        _ => "Open"
    };

    public string Start => CurrentLanguage switch
    {
        AppLanguage.KO => "시작",
        AppLanguage.JA => "開始",
        _ => "Start"
    };

    public string Stop => CurrentLanguage switch
    {
        AppLanguage.KO => "중지",
        AppLanguage.JA => "停止",
        _ => "Stop"
    };

    public string Reset => CurrentLanguage switch
    {
        AppLanguage.KO => "리셋",
        AppLanguage.JA => "リセット",
        _ => "Reset"
    };

    public string ResetAll => CurrentLanguage switch
    {
        AppLanguage.KO => "초기화",
        AppLanguage.JA => "すべてリセット",
        _ => "Reset All"
    };

    public string SelectAll => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 선택",
        AppLanguage.JA => "すべて選択",
        _ => "Select All"
    };

    public string DeselectAll => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 해제",
        AppLanguage.JA => "すべて解除",
        _ => "Deselect All"
    };

    public string ShowAll => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 표시",
        AppLanguage.JA => "すべて表示",
        _ => "Show All"
    };

    public string HideAll => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 숨기기",
        AppLanguage.JA => "すべて非表示",
        _ => "Hide All"
    };

    public string ExpandAll => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 펼치기",
        AppLanguage.JA => "すべて展開",
        _ => "Expand All"
    };

    public string CollapseAll => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 접기",
        AppLanguage.JA => "すべて折りたたむ",
        _ => "Collapse All"
    };

    public string ShowMore => CurrentLanguage switch
    {
        AppLanguage.KO => "더 보기",
        AppLanguage.JA => "もっと見る",
        _ => "Show More"
    };

    public string ShowLess => CurrentLanguage switch
    {
        AppLanguage.KO => "접기",
        AppLanguage.JA => "折りたたむ",
        _ => "Show Less"
    };

    public string FilterAll => CurrentLanguage switch
    {
        AppLanguage.KO => "전체",
        AppLanguage.JA => "すべて",
        _ => "All"
    };

    public string Folder => CurrentLanguage switch
    {
        AppLanguage.KO => "폴더",
        AppLanguage.JA => "フォルダー",
        _ => "Folder"
    };

    public string Waiting => CurrentLanguage switch
    {
        AppLanguage.KO => "대기 중",
        AppLanguage.JA => "待機中",
        _ => "Waiting"
    };

    public string Tracking => CurrentLanguage switch
    {
        AppLanguage.KO => "추적 중",
        AppLanguage.JA => "追跡中",
        _ => "Tracking"
    };

    public string AutoDetect => CurrentLanguage switch
    {
        AppLanguage.KO => "자동 감지",
        AppLanguage.JA => "自動検出",
        _ => "Auto Detect"
    };

    public string Automation => CurrentLanguage switch
    {
        AppLanguage.KO => "자동화",
        AppLanguage.JA => "自動化",
        _ => "Automation"
    };

    public string Layers => CurrentLanguage switch
    {
        AppLanguage.KO => "레이어",
        AppLanguage.JA => "レイヤー",
        _ => "Layers"
    };

    public string Legend => CurrentLanguage switch
    {
        AppLanguage.KO => "범례",
        AppLanguage.JA => "凡例",
        _ => "Legend"
    };

    public string Language => CurrentLanguage switch
    {
        AppLanguage.KO => "언어",
        AppLanguage.JA => "言語",
        _ => "Language"
    };

    // Newly added for MainWindow and Settings
    public string Quests => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트",
        AppLanguage.JA => "クエスト",
        _ => "Quests"
    };

    public string Hideout => CurrentLanguage switch
    {
        AppLanguage.KO => "은신처",
        AppLanguage.JA => "ハイドアウト",
        _ => "Hideout"
    };

    public string Items => CurrentLanguage switch
    {
        AppLanguage.KO => "아이템",
        AppLanguage.JA => "アイテム",
        _ => "Items"
    };

    public string Collector => CurrentLanguage switch
    {
        AppLanguage.KO => "수집가 - 카파",
        AppLanguage.JA => "コレクター - Kappa",
        _ => "Collector - Kappa"
    };

    public string Map => CurrentLanguage switch
    {
        AppLanguage.KO => "지도",
        AppLanguage.JA => "マップ",
        _ => "Map"
    };

    public string Profile => CurrentLanguage switch
    {
        AppLanguage.KO => "프로필",
        _ => "Profile"
    };

    public string PlayerLevel => CurrentLanguage switch
    {
        AppLanguage.KO => "플레이어 레벨",
        _ => "Player Level"
    };

    public string ScavRep => CurrentLanguage switch
    {
        AppLanguage.KO => "스캐브 평판",
        _ => "Scav Karma"
    };

    public string DspDecode => CurrentLanguage switch
    {
        AppLanguage.KO => "DSP 디코드 횟수",
        _ => "DSP Decode Count"
    };

    public string Edition => CurrentLanguage switch
    {
        AppLanguage.KO => "에디션",
        _ => "Edition"
    };

    public string PrestigeLevel => CurrentLanguage switch
    {
        AppLanguage.KO => "프레스티지 레벨",
        _ => "Prestige Level"
    };

    public string LoadingData => CurrentLanguage switch
    {
        AppLanguage.KO => "데이터 로딩 중...",
        _ => "Loading data..."
    };

    public string Initializing => CurrentLanguage switch
    {
        AppLanguage.KO => "초기화 중...",
        _ => "Initializing..."
    };

    public string LogFolder => CurrentLanguage switch
    {
        AppLanguage.KO => "Tarkov 로그 폴더",
        _ => "Tarkov Log Folder"
    };

    public string QuestLogSync => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 로그 동기화",
        _ => "Quest Log Sync"
    };

    public string CacheManagement => CurrentLanguage switch
    {
        AppLanguage.KO => "캐시 관리",
        _ => "Cache Management"
    };

    public string FontSize => CurrentLanguage switch
    {
        AppLanguage.KO => "글꼴 크기",
        _ => "Font Size"
    };

    public string Font => CurrentLanguage switch
    {
        AppLanguage.KO => "글꼴",
        _ => "Font"
    };

    public string FontDesc => CurrentLanguage switch
    {
        AppLanguage.KO => "어플리케이션에 사용할 글꼴을 선택합니다. Fonts 폴더의 폰트 파일(.ttf, .otf)을 자동으로 인식합니다.",
        _ => "Select font to use in the application. Automatically detects fonts (.ttf, .otf) in the Fonts folder."
    };

    public string Unknown => CurrentLanguage switch
    {
        AppLanguage.KO => "알 수 없음",
        _ => "Unknown"
    };

    public string UpdateApiData => CurrentLanguage switch
    {
        AppLanguage.KO => "API 데이터 업데이트",
        _ => "Update API Data"
    };

    public string ApiUpdateCheck => CurrentLanguage switch
    {
        AppLanguage.KO => "업데이트 확인 중...",
        _ => "Checking for updates..."
    };

    public string ApiUpdateSuccess => CurrentLanguage switch
    {
        AppLanguage.KO => "성공적으로 업데이트되었습니다.",
        _ => "Successfully updated."
    };

    public string ApiUpdateFail => CurrentLanguage switch
    {
        AppLanguage.KO => "업데이트 실패: {0}",
        _ => "Update failed: {0}"
    };

    public string ApiUpToDate => CurrentLanguage switch
    {
        AppLanguage.KO => "이미 최신 버전입니다.",
        _ => "Already up to date."
    };

    // Items Page Stats
    public string ItemsShowing => CurrentLanguage switch
    {
        AppLanguage.KO => "아이템 {0}개 표시 중",
        _ => "Showing {0} items"
    };

    // Map Page
    public string MapNoMapImage => CurrentLanguage switch
    {
        AppLanguage.KO => "사용 가능한 지도 이미지가 없습니다",
        _ => "No map image available"
    };

    public string MapAddMapImageHint => CurrentLanguage switch
    {
        AppLanguage.KO => "Assets/Maps/ 폴더에 이미지를 추가하세요",
        _ => "Add image to Assets/Maps/ folder"
    };

    public string MapSetImagePathHint => CurrentLanguage switch
    {
        AppLanguage.KO => "또는 설정에서 경로를 지정하세요",
        _ => "Or specify path in Settings"
    };

    #endregion

    #region Common Name Localization (Trader, Map)

    public string GetLocalizedTraderName(string? englishName)
    {
        if (CurrentLanguage == AppLanguage.KO)
        {
            return englishName?.ToLowerInvariant() switch
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
        return englishName ?? Unknown;
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
