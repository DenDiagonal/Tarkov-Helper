using TarkovHelper.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TarkovHelper.Models;
using TarkovHelper.Services;

namespace TarkovHelper.Pages
{
    /// <summary>
    /// Navigation chip view model for Trader and Map filters with reactive badge counters.
    /// </summary>
    public class NavChipItemViewModel : INotifyPropertyChanged
    {
        private int _activeCount;
        private bool _isSelected;
        private string _displayName = string.Empty;

        public string Key { get; set; } = string.Empty;

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string IconOrInitials { get; set; } = string.Empty;

        public int ActiveCount
        {
            get => _activeCount;
            set
            {
                if (_activeCount != value)
                {
                    _activeCount = value;
                    OnPropertyChanged(nameof(ActiveCount));
                    OnPropertyChanged(nameof(BadgeText));
                    OnPropertyChanged(nameof(HasActiveQuests));
                    OnPropertyChanged(nameof(CounterBrush));
                    OnPropertyChanged(nameof(CounterForeground));
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                    OnPropertyChanged(nameof(ChipBackground));
                    OnPropertyChanged(nameof(ChipForeground));
                    OnPropertyChanged(nameof(ChipBorderBrush));
                    OnPropertyChanged(nameof(CounterBrush));
                    OnPropertyChanged(nameof(CounterForeground));
                }
            }
        }

        public bool HasActiveQuests => ActiveCount > 0;
        public string BadgeText => ActiveCount.ToString();

        public Brush CounterBrush => HasActiveQuests
            ? (IsSelected
                ? new SolidColorBrush(Color.FromRgb(54, 44, 22))
                : new SolidColorBrush(Color.FromRgb(36, 36, 46)))
            : new SolidColorBrush(Color.FromRgb(26, 26, 30));

        public Brush CounterForeground => HasActiveQuests
            ? (IsSelected
                ? (Application.Current?.TryFindResource("AccentBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(212, 175, 55)))
                : new SolidColorBrush(Color.FromRgb(180, 180, 192)))
            : new SolidColorBrush(Color.FromRgb(100, 100, 110));

        public Brush ChipBackground => IsSelected
            ? new SolidColorBrush(Color.FromRgb(34, 35, 43))
            : new SolidColorBrush(Color.FromRgb(20, 21, 26));

        public Brush ChipForeground => IsSelected
            ? (Application.Current?.TryFindResource("AccentBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(212, 175, 55)))
            : (Application.Current?.TryFindResource("TextSecondaryBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(180, 180, 190)));

        public Brush ChipBorderBrush => IsSelected
            ? (Application.Current?.TryFindResource("AccentBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(212, 175, 55)))
            : new SolidColorBrush(Color.FromRgb(46, 47, 56));

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Quest list view model for high-density display
    /// </summary>
    public class QuestViewModel : INotifyPropertyChanged
    {
        private QuestStatus _status;
        private string _displayName = string.Empty;
        private string _subtitleName = string.Empty;
        private Visibility _subtitleVisibility = Visibility.Collapsed;
        private string _statusText = string.Empty;
        private Brush _statusBackground = Brushes.Gray;
        private Visibility _completeButtonVisibility = Visibility.Visible;
        private bool _isKappaRequired;
        private bool _hasNeededKeys;
        private bool _hasFirRequirements;
        private bool _hasRequiredItems;
        private bool _isManualOverride;
        private string? _poolProgressText;

        public TarkovTask Task { get; set; } = null!;

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string SubtitleName
        {
            get => _subtitleName;
            set
            {
                if (_subtitleName != value)
                {
                    _subtitleName = value;
                    OnPropertyChanged(nameof(SubtitleName));
                }
            }
        }

        public Visibility SubtitleVisibility
        {
            get => _subtitleVisibility;
            set
            {
                if (_subtitleVisibility != value)
                {
                    _subtitleVisibility = value;
                    OnPropertyChanged(nameof(SubtitleVisibility));
                }
            }
        }

        public string LockToggleIcon
        {
            get
            {
                if (Task == null) return "🔓";
                string taskId = Task.Ids?.FirstOrDefault() ?? Task.NormalizedName ?? string.Empty;
                var qps = QuestProgressService.Instance;
                if (qps.IsQuestForceUnlocked(taskId)) return "🔓";
                if (qps.IsQuestForceLocked(taskId)) return "🔒";
                if (Status == QuestStatus.Done) return "✓";
                return Status == QuestStatus.Active ? "🔓" : "🔒";
            }
        }

        public string LockToggleToolTip
        {
            get
            {
                if (Task == null) return "Status";
                string taskId = Task.Ids?.FirstOrDefault() ?? Task.NormalizedName ?? string.Empty;
                var qps = QuestProgressService.Instance;
                if (qps.IsQuestForceUnlocked(taskId)) return "MANUALLY Unlocked (Forced active).\nClick: reset to automatic.\nRight-click: options.";
                if (qps.IsQuestForceLocked(taskId)) return "MANUALLY Locked (Forced locked).\nClick: reset to automatic.\nRight-click: options.";
                if (Status == QuestStatus.Done) return "Status: Completed.\nRight-click: menu.";
                if (Status == QuestStatus.Active) return "Status: Active (Automatic from requirements).\nClick: force lock.\nRight-click: menu.";
                return $"Status: {Status} (Automatic from requirements).\nClick: force unlock.\nRight-click: menu.";
            }
        }

        public Brush LockToggleBackground
        {
            get
            {
                if (Task == null) return TacticalBrushes.Surface3;
                string taskId = Task.Ids?.FirstOrDefault() ?? Task.NormalizedName ?? string.Empty;
                var qps = QuestProgressService.Instance;
                if (qps.IsQuestForceUnlocked(taskId)) return TacticalBrushes.ActiveBg;
                if (qps.IsQuestForceLocked(taskId)) return TacticalBrushes.LockedBg;
                if (Status == QuestStatus.Done) return TacticalBrushes.DoneBg;
                return Status == QuestStatus.Active ? TacticalBrushes.Surface3 : TacticalBrushes.LockedBg;
            }
        }

        public Brush LockToggleBorder
        {
            get
            {
                if (Task == null) return TacticalBrushes.BorderMedium;
                string taskId = Task.Ids?.FirstOrDefault() ?? Task.NormalizedName ?? string.Empty;
                var qps = QuestProgressService.Instance;
                if (qps.HasQuestManualOverride(taskId)) return TacticalBrushes.BorderGold;
                if (Status == QuestStatus.Done) return TacticalBrushes.DoneBorder;
                return Status == QuestStatus.Active ? TacticalBrushes.BorderMedium : TacticalBrushes.LockedBorder;
            }
        }

        public Brush LockToggleForeground
        {
            get
            {
                if (Task == null) return TacticalBrushes.TextPrimary;
                string taskId = Task.Ids?.FirstOrDefault() ?? Task.NormalizedName ?? string.Empty;
                var qps = QuestProgressService.Instance;
                if (qps.IsQuestForceUnlocked(taskId)) return TacticalBrushes.ActiveFg;
                if (qps.IsQuestForceLocked(taskId)) return TacticalBrushes.LockedFg;
                if (Status == QuestStatus.Done) return TacticalBrushes.DoneFg;
                return Status == QuestStatus.Active ? TacticalBrushes.ActiveFg : TacticalBrushes.LockedFg;
            }
        }

        public Visibility ManualOverrideDotVisibility
        {
            get
            {
                if (Task == null) return Visibility.Collapsed;
                string taskId = Task.Ids?.FirstOrDefault() ?? Task.NormalizedName ?? string.Empty;
                return QuestProgressService.Instance.HasQuestManualOverride(taskId) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public void NotifyLockToggleChanged()
        {
            OnPropertyChanged(nameof(LockToggleIcon));
            OnPropertyChanged(nameof(LockToggleToolTip));
            OnPropertyChanged(nameof(LockToggleBackground));
            OnPropertyChanged(nameof(LockToggleBorder));
            OnPropertyChanged(nameof(LockToggleForeground));
            OnPropertyChanged(nameof(ManualOverrideDotVisibility));
        }

        public string TraderInitial { get; set; } = string.Empty;

        public QuestStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(IsDone));
                    OnPropertyChanged(nameof(StoryToggleText));
                    OnPropertyChanged(nameof(StoryToggleBackground));
                    OnPropertyChanged(nameof(StoryToggleBorder));
                    OnPropertyChanged(nameof(StoryToggleForeground));
                    NotifyLockToggleChanged();
                    SyncCard();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public Brush StatusBackground
        {
            get => _statusBackground;
            set
            {
                if (!Equals(_statusBackground, value))
                {
                    _statusBackground = value;
                    OnPropertyChanged(nameof(StatusBackground));
                }
            }
        }

        public Visibility CompleteButtonVisibility
        {
            get => _completeButtonVisibility;
            set
            {
                if (_completeButtonVisibility != value)
                {
                    _completeButtonVisibility = value;
                    OnPropertyChanged(nameof(CompleteButtonVisibility));
                }
            }
        }

        public bool IsKappaRequired
        {
            get => _isKappaRequired;
            set
            {
                if (_isKappaRequired != value)
                {
                    _isKappaRequired = value;
                    OnPropertyChanged(nameof(IsKappaRequired));
                    OnPropertyChanged(nameof(KappaBadgeVisibility));
                }
            }
        }

        public Visibility KappaBadgeVisibility => IsKappaRequired ? Visibility.Visible : Visibility.Collapsed;

        public bool IsSeasonal => Task?.IsSeasonal ?? false;
        public Visibility SeasonalBadgeVisibility => IsSeasonal ? Visibility.Visible : Visibility.Collapsed;

        // Needed Keys & Mission Items indicators
        public bool HasNeededKeys
        {
            get => _hasNeededKeys;
            set
            {
                if (_hasNeededKeys != value)
                {
                    _hasNeededKeys = value;
                    OnPropertyChanged(nameof(HasNeededKeys));
                    OnPropertyChanged(nameof(KeyBadgeVisibility));
                }
            }
        }

        public Visibility KeyBadgeVisibility => HasNeededKeys ? Visibility.Visible : Visibility.Collapsed;

        public bool HasFirRequirements
        {
            get => _hasFirRequirements;
            set
            {
                if (_hasFirRequirements != value)
                {
                    _hasFirRequirements = value;
                    OnPropertyChanged(nameof(HasFirRequirements));
                    OnPropertyChanged(nameof(FirBadgeVisibility));
                }
            }
        }

        public Visibility FirBadgeVisibility => HasFirRequirements ? Visibility.Visible : Visibility.Collapsed;

        public bool HasRequiredItems
        {
            get => _hasRequiredItems;
            set
            {
                if (_hasRequiredItems != value)
                {
                    _hasRequiredItems = value;
                    OnPropertyChanged(nameof(HasRequiredItems));
                    OnPropertyChanged(nameof(RequiredItemsBadgeVisibility));
                }
            }
        }

        public Visibility RequiredItemsBadgeVisibility => HasRequiredItems ? Visibility.Visible : Visibility.Collapsed;

        // Story Chapters & Manual Override additions
        public bool IsStoryChapter =>
            string.Equals(Task?.Trader, "Story", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Task?.Trader, "Story Chapters", StringComparison.OrdinalIgnoreCase);

        public Visibility StoryBadgeVisibility => IsStoryChapter ? Visibility.Visible : Visibility.Collapsed;
        public Visibility StoryToggleVisibility => IsStoryChapter ? Visibility.Visible : Visibility.Collapsed;

        public bool IsDone
        {
            get => Status == QuestStatus.Done;
            set
            {
                if (value && Status != QuestStatus.Done)
                {
                    Status = QuestStatus.Done;
                }
                else if (!value && Status == QuestStatus.Done)
                {
                    Status = QuestStatus.Active;
                }
            }
        }

        public bool IsManualOverride
        {
            get => _isManualOverride;
            set
            {
                if (_isManualOverride != value)
                {
                    _isManualOverride = value;
                    OnPropertyChanged(nameof(IsManualOverride));
                    OnPropertyChanged(nameof(OverrideBadgeVisibility));
                }
            }
        }

        public Visibility OverrideBadgeVisibility => IsManualOverride ? Visibility.Visible : Visibility.Collapsed;

        public string? PoolProgressText
        {
            get => _poolProgressText;
            set
            {
                if (_poolProgressText != value)
                {
                    _poolProgressText = value;
                    OnPropertyChanged(nameof(PoolProgressText));
                    OnPropertyChanged(nameof(PoolBadgeVisibility));
                }
            }
        }

        public Visibility PoolBadgeVisibility => !string.IsNullOrEmpty(PoolProgressText) ? Visibility.Visible : Visibility.Collapsed;

        // High-Density Card Badges & Actions
        public string RequiredLevelText =>
            Task?.RequiredLevel.HasValue == true && Task.RequiredLevel.Value > 1
                ? $"Lv.{Task.RequiredLevel.Value}"
                : string.Empty;

        public Visibility RequiredLevelVisibility =>
            !string.IsNullOrEmpty(RequiredLevelText) ? Visibility.Visible : Visibility.Collapsed;

        public Brush RequiredLevelBackground
        {
            get
            {
                int playerLevel = SettingsService.Instance.PlayerLevel;
                if (Task?.RequiredLevel.HasValue == true && Task.RequiredLevel.Value > playerLevel)
                {
                    return new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Level locked orange
                }
                return new SolidColorBrush(Color.FromRgb(55, 55, 62));
            }
        }

        public Brush RequiredLevelForeground => Brushes.White;

        public string RequiredLevelToolTip =>
            Task?.RequiredLevel.HasValue == true
                ? $"Nivel requerido: {Task.RequiredLevel.Value}"
                : string.Empty;

        public bool HasMap => Task?.Maps != null && Task.Maps.Count > 0;
        public Visibility MapActionVisibility => HasMap ? Visibility.Visible : Visibility.Collapsed;
        public Visibility WikiActionVisibility => Visibility.Visible;

        public void NotifyMetricsChanged()
        {
            OnPropertyChanged(nameof(RequiredLevelBackground));
            OnPropertyChanged(nameof(RequiredLevelVisibility));
            OnPropertyChanged(nameof(RequiredLevelText));
        }

        public string StoryToggleText
        {
            get
            {
                var lang = LocalizationService.Instance.CurrentLanguage;
                if (IsDone)
                {
                    return lang switch
                    {
                        AppLanguage.ES => "✓ Completado",
                        AppLanguage.KO => "✓ 완료",
                        AppLanguage.JA => "✓ 完了",
                        _ => "✓ Done"
                    };
                }
                return lang switch
                {
                    AppLanguage.ES => "○ Pendiente",
                    AppLanguage.KO => "○ 진행 대기",
                    AppLanguage.JA => "○ 保留中",
                    _ => "○ Pending"
                };
            }
        }

        public Brush StoryToggleBackground =>
            IsDone
                ? new SolidColorBrush(Color.FromRgb(30, 136, 229))
                : new SolidColorBrush(Color.FromRgb(46, 46, 52));

        public Brush StoryToggleBorder =>
            IsDone
                ? new SolidColorBrush(Color.FromRgb(66, 165, 245))
                : new SolidColorBrush(Color.FromRgb(85, 85, 92));

        public Brush StoryToggleForeground =>
            IsDone
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(220, 220, 220));

        private Controls.TaskCardViewModel? _card;
        public Controls.TaskCardViewModel Card
        {
            get
            {
                if (_card == null)
                {
                    _card = new Controls.TaskCardViewModel();
                    _card.LoadFromTask(Task, Status);
                }
                return _card;
            }
        }

        public void SyncCard()
        {
            if (_card != null)
            {
                _card.LoadFromTask(Task, Status);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Required item view model
    /// </summary>
    public class RequiredItemViewModel
    {
        public string DisplayText { get; set; } = string.Empty;
        public bool FoundInRaid { get; set; }
        public Visibility FirVisibility => FoundInRaid ? Visibility.Visible : Visibility.Collapsed;
        public BitmapImage? IconSource { get; set; }
        public string RequirementType { get; set; } = string.Empty;
        public Visibility RequirementTypeVisibility =>
            string.IsNullOrEmpty(RequirementType) ? Visibility.Collapsed : Visibility.Visible;

        // Navigation identifier (use ItemId for cross-tab navigation)
        public string ItemId { get; set; } = string.Empty;

        // Fulfillment status
        public bool IsFulfilled { get; set; }
        public TextDecorationCollection? TextDecorations => IsFulfilled ? System.Windows.TextDecorations.Strikethrough : null;
        public double ItemOpacity => IsFulfilled ? 0.6 : 1.0;
        public Visibility FulfilledVisibility => IsFulfilled ? Visibility.Visible : Visibility.Collapsed;

        // Stash progress & crafting linkage
        public string ItemNormalizedName { get; set; } = string.Empty;
        public string StashProgressText { get; set; } = string.Empty;
        public Visibility StashProgressVisibility => string.IsNullOrEmpty(StashProgressText) ? Visibility.Collapsed : Visibility.Visible;
        public Brush StashProgressBackground => IsFulfilled
            ? new SolidColorBrush(Color.FromRgb(46, 125, 50))
            : new SolidColorBrush(Color.FromRgb(55, 55, 62));
        public Brush StashProgressForeground => IsFulfilled
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(220, 220, 220));
        public string CraftingLinkageText { get; set; } = string.Empty;
        public Visibility CraftingLinkageVisibility => string.IsNullOrEmpty(CraftingLinkageText) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// View model for displaying needed keys in quest details with stash fulfillment indicator
    /// </summary>
    public class NeededKeyViewModel
    {
        public string KeyId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? ShortName { get; set; }
        public string MapOrObjectiveInfo { get; set; } = string.Empty;
        public Visibility MapOrObjectiveVisibility => string.IsNullOrEmpty(MapOrObjectiveInfo) ? Visibility.Collapsed : Visibility.Visible;
        public bool InStash { get; set; }
        public int StashCount { get; set; }

        public BitmapImage? IconSource { get; set; }
        public Visibility IconVisibility => IconSource != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FallbackGlyphVisibility => IconSource == null ? Visibility.Visible : Visibility.Collapsed;

        public string StashStatusText => InStash ? (StashCount > 1 ? $"✓ In Stash (x{StashCount})" : "✓ In Stash") : "Missing";
        public Brush StashStatusBackground => InStash ? new SolidColorBrush(Color.FromRgb(46, 125, 50)) : new SolidColorBrush(Color.FromRgb(66, 66, 66));
        public Brush StashStatusForeground => InStash ? Brushes.White : new SolidColorBrush(Color.FromRgb(220, 220, 220));
    }

    /// <summary>
    /// Prerequisite group view model for displaying OR/AND grouped prerequisites
    /// </summary>
    public class PrerequisiteGroupViewModel
    {
        public int GroupId { get; set; }
        public bool IsOrGroup => GroupId > 0;
        public string GroupLabel => IsOrGroup ? "Choice" : "";
        public Visibility OrLabelVisibility => IsOrGroup ? Visibility.Visible : Visibility.Collapsed;
        public Brush OrGroupBackground => IsOrGroup ? new SolidColorBrush(Color.FromArgb(30, 33, 150, 243)) : Brushes.Transparent;
        public List<PrerequisiteItemViewModel> Items { get; set; } = new();
    }

    /// <summary>
    /// Single prerequisite item view model
    /// </summary>
    public class PrerequisiteItemViewModel
    {
        public TarkovTask? Task { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public Brush StatusBackground { get; set; } = Brushes.Gray;
        public bool IsOrItem { get; set; }
        public string OrSeparator => IsOrItem ? " OR " : "";
        public Visibility OrSeparatorVisibility => IsOrItem ? Visibility.Visible : Visibility.Collapsed;
        public string BulletText => IsOrItem ? "" : "• ";
    }

    /// <summary>
    /// Pool task requirement item view model
    /// </summary>
    public class PoolTaskItemViewModel
    {
        public TarkovTask? Task { get; set; }
        public string TaskId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsDone { get; set; }
        public string StatusIcon => IsDone ? "✓" : "○";
        public Brush StatusBrush => IsDone ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) : new SolidColorBrush(Color.FromRgb(158, 158, 158));
        public Brush TextBrush => IsDone ? new SolidColorBrush(Color.FromRgb(200, 230, 201)) : new SolidColorBrush(Color.FromRgb(220, 220, 220));
        public TextDecorationCollection? TextDecorations => IsDone ? System.Windows.TextDecorations.Strikethrough : null;
    }

    /// <summary>
    /// Recommendation view model for display
    /// </summary>
    public class RecommendationViewModel
    {
        public QuestRecommendation Recommendation { get; set; } = null!;
        public string QuestName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string TypeText { get; set; } = string.Empty;
        public Brush TypeBackground { get; set; } = Brushes.Gray;
        public string TraderInitial { get; set; } = string.Empty;
        public bool IsKappaRequired { get; set; }
        public Visibility KappaBadgeVisibility => IsKappaRequired ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Guide image view model with loading state
    /// </summary>
    public class GuideImageViewModel : INotifyPropertyChanged
    {
        private BitmapImage? _imageSource;
        private bool _isLoading = true;

        public string FileName { get; set; } = string.Empty;
        public string? Caption { get; set; }

        public BitmapImage? ImageSource
        {
            get => _imageSource;
            set
            {
                _imageSource = value;
                OnPropertyChanged(nameof(ImageSource));
                OnPropertyChanged(nameof(ImageVisibility));
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(LoadingVisibility));
                OnPropertyChanged(nameof(ImageVisibility));
            }
        }

        public Visibility CaptionVisibility =>
            string.IsNullOrEmpty(Caption) ? Visibility.Collapsed : Visibility.Visible;

        public Visibility LoadingVisibility =>
            IsLoading ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ImageVisibility =>
            IsLoading ? Visibility.Collapsed : Visibility.Visible;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
