using System.Windows.Input;
using System.Windows.Threading;
using System;

using System.Collections.Generic;

using System.Collections.ObjectModel;

using System.ComponentModel;

using System.Diagnostics;

using System.Globalization;

using System.Linq;

using System.Runtime.CompilerServices;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Media;

using TarkovHelper.Common;

using TarkovHelper.Models;

using TarkovHelper.Pages;

using TarkovHelper.Services;

using TarkovHelper.Services.Map;

using TarkovHelper.ViewModels;

namespace TarkovHelper.Controls

{

    /// <summary>

    /// Interactive sub-objective quantity stepper item ([-] X/Y [+] [✓]).

    /// </summary>

    public class AlternativeItemOption

    {

        public string ItemId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string NormalizedName { get; set; } = string.Empty;

        public override string ToString() => DisplayName;

    }

    public class SubObjectiveStepperItem : INotifyPropertyChanged
    {
        private int _currentCount;
        private int _maxCount;
        private int _currentAlternativeIndex;

        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public bool FoundInRaid { get; set; }
        public string NeedKey { get; set; } = string.Empty;
        public Action? OnChanged { get; set; }

        public ObservableCollection<AlternativeItemOption> Alternatives { get; } = new();
        public AlternativeItemOption? SelectedAlternative
        {
            get => Alternatives.Count > _currentAlternativeIndex && _currentAlternativeIndex >= 0 ? Alternatives[_currentAlternativeIndex] : null;
            set
            {
                if (value != null)
                {
                    int idx = Alternatives.IndexOf(value);
                    if (idx >= 0) _currentAlternativeIndex = idx;
                    ItemId = value.NormalizedName;
                    ItemName = value.DisplayName;
                    OnPropertyChanged(nameof(SelectedAlternative));
                    OnPropertyChanged(nameof(ItemName));
                    OnPropertyChanged(nameof(ItemId));
                    OnPropertyChanged(nameof(AlternativesBadgeText));
                    OnPropertyChanged(nameof(AlternativesTooltip));
                }
            }
        }


        public bool HasAlternatives => Alternatives.Count > 1;
        public Visibility AlternativesVisibility => HasAlternatives ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SingleItemVisibility => HasAlternatives ? Visibility.Collapsed : Visibility.Visible;

        public string AlternativesBadgeText => HasAlternatives 
            ? $"{_currentAlternativeIndex + 1}/{Alternatives.Count}" 
            : string.Empty;

        public string AlternativesTooltip
        {
            get
            {
                if (Alternatives.Count <= 1) return ItemName;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Valid alternatives ({_currentAlternativeIndex + 1} of {Alternatives.Count}) - Select from dropdown:");
                for (int i = 0; i < Alternatives.Count; i++)
                {
                    string marker = i == _currentAlternativeIndex ? " ▶ " : "   • ";
                    sb.AppendLine($"{marker}{Alternatives[i].DisplayName}");
                }
                return sb.ToString().TrimEnd();
            }
        }

        public void AdvanceNextAlternative()
        {
            if (Alternatives.Count <= 1) return;
            _currentAlternativeIndex = (_currentAlternativeIndex + 1) % Alternatives.Count;
            var alt = Alternatives[_currentAlternativeIndex];
            ItemId = alt.NormalizedName;
            ItemName = alt.DisplayName;
            OnPropertyChanged(nameof(ItemName));
            OnPropertyChanged(nameof(ItemId));
            OnPropertyChanged(nameof(AlternativesBadgeText));
            OnPropertyChanged(nameof(AlternativesTooltip));
        }

        public int CurrentCount
        {
            get => _currentCount;
            set
            {
                if (_currentCount != value)
                {
                    _currentCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProgressFraction));
                    OnPropertyChanged(nameof(ProgressForeground));
                    OnPropertyChanged(nameof(MaxCheckBackground));
                    OnPropertyChanged(nameof(MaxCheckForeground));
                    OnPropertyChanged(nameof(RowBackground));
                    OnPropertyChanged(nameof(RowBorderBrush));
                    OnPropertyChanged(nameof(ItemNameForeground));
                }
            }
        }

        public int MaxCount
        {
            get => _maxCount;
            set
            {
                if (_maxCount != value)
                {
                    _maxCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProgressFraction));
                    OnPropertyChanged(nameof(ProgressForeground));
                    OnPropertyChanged(nameof(RowBackground));
                    OnPropertyChanged(nameof(RowBorderBrush));
                    OnPropertyChanged(nameof(ItemNameForeground));
                }
            }
        }

        public double ProgressFraction => MaxCount > 0 ? (double)CurrentCount / MaxCount : 0.0;
        public Visibility FirVisibility => FoundInRaid ? Visibility.Visible : Visibility.Collapsed;

        public Brush ProgressForeground => CurrentCount >= MaxCount
            ? TacticalBrushes.TacticalSuccess
            : (CurrentCount > 0
                ? TacticalBrushes.TacticalWarning
                : TacticalBrushes.TextSecondary);

        public Brush MaxCheckBackground => CurrentCount >= MaxCount
            ? TacticalBrushes.ActiveBg
            : TacticalBrushes.Surface4;

        public Brush MaxCheckForeground => CurrentCount >= MaxCount
            ? TacticalBrushes.ActiveFg
            : TacticalBrushes.TextSecondary;

        public Brush RowBackground => CurrentCount >= MaxCount
            ? new SolidColorBrush(Color.FromArgb(28, 74, 222, 128))
            : Brushes.Transparent;

        public Brush RowBorderBrush => CurrentCount >= MaxCount
            ? new SolidColorBrush(Color.FromArgb(70, 74, 222, 128))
            : new SolidColorBrush(Color.FromArgb(255, 24, 27, 34));

        public Brush ItemNameForeground => CurrentCount >= MaxCount
            ? TacticalBrushes.ActiveFg
            : TacticalBrushes.TextPrimary;

        public void Increment()
        {
            if (CurrentCount < MaxCount)
            {
                CurrentCount++;
                if (!string.IsNullOrEmpty(NeedKey))
                {
                    NeededItemProgressService.Instance.Increment(NeedKey, MaxCount);
                }
                OnChanged?.Invoke();
            }
        }

        public void Decrement()
        {
            if (CurrentCount > 0)
            {
                CurrentCount--;
                if (!string.IsNullOrEmpty(NeedKey))
                {
                    NeededItemProgressService.Instance.Decrement(NeedKey);
                }
                OnChanged?.Invoke();
            }
        }

        public void SetMax()
        {
            CurrentCount = MaxCount;
            if (!string.IsNullOrEmpty(NeedKey))
            {
                NeededItemProgressService.Instance.SetCount(NeedKey, MaxCount, MaxCount);
            }
            OnChanged?.Invoke();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class SubObjectiveActionItem : INotifyPropertyChanged

    {

        private bool _isChecked;

        public string ObjectiveId { get; set; } = string.Empty;

        public string QuestNormalizedName { get; set; } = string.Empty;

        public int ObjectiveIndex { get; set; }

        public string Description { get; set; } = string.Empty;

        public Action? OnChanged { get; set; }

        public bool IsChecked

        {

            get => _isChecked;

            set

            {

                if (_isChecked != value)

                {

                    _isChecked = value;

                    OnPropertyChanged();

                    OnPropertyChanged(nameof(ToggleGlyph));

                    OnPropertyChanged(nameof(ToggleBackground));

                    OnPropertyChanged(nameof(ToggleForeground));

                    OnPropertyChanged(nameof(ToggleBorder));

                    OnPropertyChanged(nameof(TextDecorations));

                    OnPropertyChanged(nameof(TextOpacity));

                }

            }

        }

        public string ToggleGlyph => IsChecked ? "✓" : "○";

        public Brush ToggleBackground => IsChecked

            ? TacticalBrushes.ActiveBg

            : TacticalBrushes.Surface4;

        public Brush ToggleForeground => IsChecked

            ? TacticalBrushes.ActiveFg

            : TacticalBrushes.TextSecondary;

        public Brush ToggleBorder => IsChecked

            ? TacticalBrushes.ActiveBorder

            : TacticalBrushes.BorderMedium;

        public TextDecorationCollection? TextDecorations => IsChecked ? System.Windows.TextDecorations.Strikethrough : null;

        public double TextOpacity => IsChecked ? 0.6 : 1.0;

        public void Toggle()

        {

            IsChecked = !IsChecked;

            if (!string.IsNullOrEmpty(QuestNormalizedName))

            {

                ObjectiveProgressService.Instance.SetObjectiveCompleted(QuestNormalizedName, ObjectiveIndex, IsChecked, ObjectiveId);

            }

            OnChanged?.Invoke();

        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    }

    /// <summary>

    /// Formatted trader reputation reward representation.

    /// </summary>

    public class TraderRepRewardItem

    {

        public string Text { get; set; } = string.Empty;

        public Brush BackgroundBrush { get; set; } = new SolidColorBrush(Color.FromRgb(30, 40, 50));

        public Brush BorderBrush { get; set; } = new SolidColorBrush(Color.FromRgb(50, 70, 90));

        public Brush TextBrush { get; set; } = new SolidColorBrush(Color.FromRgb(144, 202, 249));

    }

    /// <summary>

    /// Visual item card model for the expandable rewards drawer (TarkovTracker parity).

    /// </summary>

    public class RewardItemCardViewModel

    {

        public string ItemId { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string ShortName { get; set; } = string.Empty;

        public int Count { get; set; } = 1;

        public ImageSource? IconSource { get; set; }

        public string FormattedCount => $"x{Count.ToString("N0", CultureInfo.InvariantCulture)}";

        public Visibility CountVisibility => Count > 1 ? Visibility.Visible : Visibility.Collapsed;

    }

    /// <summary>

    /// ViewModel for TarkovTracker task card structure (Features F7-F10).

    /// </summary>

    public class TaskCardViewModel : INotifyPropertyChanged

    {

        private string _questId = string.Empty;

        private string _title = string.Empty;

        private string _trader = string.Empty;

        private string _avatarUrl = string.Empty;

        private string _wikiUrl = string.Empty;

        private string _mapBadge = string.Empty;

        private bool _isExpanded = true;

        private bool _isCompleted;

        private int _totalRequirements;

        private int _metRequirements;

        private int _xpReward;

        private int _unlocksNext;

        private int _impact;

        private TarkovTask? _task;

        private bool _isHidden;

        public bool IsHidden

        {

            get => _isHidden;

            set

            {

                if (_isHidden != value)

                {

                    _isHidden = value;

                    OnPropertyChanged();

                    OnPropertyChanged(nameof(HideButtonIcon));

                    OnPropertyChanged(nameof(HideButtonTooltip));

                }

            }

        }

        public string HideButtonIcon => IsHidden ? "👁" : "⃠";

        public string HideButtonTooltip => IsHidden ? "Unhide task" : "Hide task";

        public TarkovTask? Task

        {

            get => _task;

            set => _task = value;

        }

        public string QuestId

        {

            get => _questId;

            set { _questId = value; OnPropertyChanged(); }

        }

        public string Title

        {

            get => _title;

            set { _title = value; OnPropertyChanged(); }

        }

        public string Trader

        {

            get => _trader;

            set

            {

                _trader = value;

                OnPropertyChanged();

                OnPropertyChanged(nameof(TraderInitial));

            }

        }

        private ImageSource? _avatarSource;

        public ImageSource? AvatarSource

        {

            get => _avatarSource;

            set

            {

                _avatarSource = value;

                OnPropertyChanged();

                OnPropertyChanged(nameof(AvatarVisibility));

                OnPropertyChanged(nameof(FallbackVisibility));

            }

        }

        public string AvatarUrl

        {

            get => _avatarUrl;

            set

            {

                _avatarUrl = value;

                OnPropertyChanged();

                OnPropertyChanged(nameof(AvatarVisibility));

            }

        }

        public Visibility AvatarVisibility => _avatarSource != null ? Visibility.Visible : (!string.IsNullOrEmpty(AvatarUrl) ? Visibility.Visible : Visibility.Collapsed);

        public Visibility FallbackVisibility => _avatarSource != null ? Visibility.Collapsed : Visibility.Visible;

        private string _requiredLevelText = string.Empty;

        public string RequiredLevelText

        {

            get => _requiredLevelText;

            set { _requiredLevelText = value; OnPropertyChanged(); OnPropertyChanged(nameof(RequiredLevelVisibility)); }

        }

        public Visibility RequiredLevelVisibility => !string.IsNullOrEmpty(_requiredLevelText) ? Visibility.Visible : Visibility.Collapsed;

        public Brush RequiredLevelBackground

        {

            get

            {

                int playerLevel = SettingsService.Instance.PlayerLevel;

                if (Task?.RequiredLevel.HasValue == true && Task.RequiredLevel.Value > playerLevel)

                {

                    return TacticalBrushes.LevelLockedBg;

                }

                return TacticalBrushes.Surface3;

            }

        }

        public Brush RequiredLevelBorder

        {

            get

            {

                int playerLevel = SettingsService.Instance.PlayerLevel;

                if (Task?.RequiredLevel.HasValue == true && Task.RequiredLevel.Value > playerLevel)

                {

                    return TacticalBrushes.LevelLockedBorder;

                }

                return TacticalBrushes.BorderSubtle;

            }

        }

        public Brush RequiredLevelForeground

        {

            get

            {

                int playerLevel = SettingsService.Instance.PlayerLevel;

                if (Task?.RequiredLevel.HasValue == true && Task.RequiredLevel.Value > playerLevel)

                {

                    return TacticalBrushes.LevelLockedFg;

                }

                return TacticalBrushes.TextSecondary;

            }

        }

        private bool _isKappaRequired;

        public bool IsKappaRequired

        {

            get => _isKappaRequired;

            set { _isKappaRequired = value; OnPropertyChanged(); OnPropertyChanged(nameof(KappaBadgeVisibility)); }

        }

        public Visibility KappaBadgeVisibility => _isKappaRequired ? Visibility.Visible : Visibility.Collapsed;

        private bool _isSeasonal;
        public bool IsSeasonal
        {
            get => _isSeasonal;
            set { _isSeasonal = value; OnPropertyChanged(); OnPropertyChanged(nameof(SeasonalBadgeVisibility)); }
        }
        public Visibility SeasonalBadgeVisibility => _isSeasonal ? Visibility.Visible : Visibility.Collapsed;

        private bool _hasFir;

        public bool HasFir

        {

            get => _hasFir;

            set { _hasFir = value; OnPropertyChanged(); OnPropertyChanged(nameof(FirBadgeVisibility)); }

        }

        public Visibility FirBadgeVisibility => _hasFir ? Visibility.Visible : Visibility.Collapsed;

        private string _statusText = string.Empty;

        public string StatusText

        {

            get => _statusText;

            set { _statusText = value; OnPropertyChanged(); }

        }

        private Brush _statusBackground = TacticalBrushes.LockedBg;

        public Brush StatusBackground

        {

            get => _statusBackground;

            set { _statusBackground = value; OnPropertyChanged(); }

        }

        private Brush _statusBorder = TacticalBrushes.LockedBorder;

        public Brush StatusBorder

        {

            get => _statusBorder;

            set { _statusBorder = value; OnPropertyChanged(); }

        }

        private Brush _statusForeground = TacticalBrushes.LockedFg;

        public Brush StatusForeground

        {

            get => _statusForeground;

            set { _statusForeground = value; OnPropertyChanged(); }

        }

        public bool HasMap => Task?.Maps != null && Task.Maps.Count > 0;

        public Visibility MapActionVisibility => HasMap ? Visibility.Visible : Visibility.Collapsed;

        public bool IsStoryChapter =>

            string.Equals(Task?.Trader, "Story", StringComparison.OrdinalIgnoreCase) ||

            string.Equals(Task?.Trader, "Story Chapters", StringComparison.OrdinalIgnoreCase);

        public Visibility StoryBadgeVisibility => IsStoryChapter ? Visibility.Visible : Visibility.Collapsed;

        public string GuideText => Task?.GuideText ?? string.Empty;

        public bool HasGuideText => !string.IsNullOrWhiteSpace(Task?.GuideText);

        public Visibility GuideExpanderVisibility => HasGuideText ? Visibility.Visible : Visibility.Collapsed;

        public ObservableCollection<TarkovHelper.Pages.NeededKeyViewModel> NeededKeys { get; } = new();

        public bool HasNeededKeys => NeededKeys.Count > 0;

        public Visibility NeededKeysVisibility => HasNeededKeys ? Visibility.Visible : Visibility.Collapsed;

        private string _lockToggleIcon = "🔓";
        public string LockToggleIcon
        {
            get => _lockToggleIcon;
            set { _lockToggleIcon = value; OnPropertyChanged(); }
        }

        private string _lockToggleToolTip = "Task status";
        public string LockToggleToolTip
        {
            get => _lockToggleToolTip;
            set { _lockToggleToolTip = value; OnPropertyChanged(); }
        }

        private Brush _lockToggleBackground = TacticalBrushes.Surface3;
        public Brush LockToggleBackground
        {
            get => _lockToggleBackground;
            set { _lockToggleBackground = value; OnPropertyChanged(); }
        }

        private Brush _lockToggleBorder = TacticalBrushes.BorderMedium;
        public Brush LockToggleBorder
        {
            get => _lockToggleBorder;
            set { _lockToggleBorder = value; OnPropertyChanged(); }
        }

        private Brush _lockToggleForeground = TacticalBrushes.TextPrimary;
        public Brush LockToggleForeground
        {
            get => _lockToggleForeground;
            set { _lockToggleForeground = value; OnPropertyChanged(); }
        }

        private Visibility _manualOverrideDotVisibility = Visibility.Collapsed;
        public Visibility ManualOverrideDotVisibility
        {
            get => _manualOverrideDotVisibility;
            set { _manualOverrideDotVisibility = value; OnPropertyChanged(); }
        }

        public void RefreshLockToggleState()
        {
            if (Task == null) return;
            string taskId = Task.Ids?.FirstOrDefault() ?? Task.NormalizedName ?? string.Empty;
            var qps = QuestProgressService.Instance;
            bool isForceUnlocked = qps.IsQuestForceUnlocked(taskId);
            bool isForceLocked = qps.IsQuestForceLocked(taskId);
            var currentStatus = qps.GetStatus(Task);

            ManualOverrideDotVisibility = (isForceUnlocked || isForceLocked) ? Visibility.Visible : Visibility.Collapsed;

            if (isForceUnlocked)
            {
                LockToggleIcon = "🔓";
                LockToggleToolTip = "MANUALLY Unlocked (Forced active).\nClick: reset to automatic.\nRight-click: options.";
                LockToggleBackground = TacticalBrushes.ActiveBg;
                LockToggleBorder = TacticalBrushes.BorderGold;
                LockToggleForeground = TacticalBrushes.ActiveFg;
            }
            else if (isForceLocked)
            {
                LockToggleIcon = "🔒";
                LockToggleToolTip = "MANUALLY Locked (Forced locked).\nClick: reset to automatic.\nRight-click: options.";
                LockToggleBackground = TacticalBrushes.LockedBg;
                LockToggleBorder = TacticalBrushes.BorderGold;
                LockToggleForeground = TacticalBrushes.LockedFg;
            }
            else if (currentStatus == QuestStatus.Active)
            {
                LockToggleIcon = "🔓";
                LockToggleToolTip = "Status: Active (Automatic from requirements).\nClick: force lock.\nRight-click: options.";
                LockToggleBackground = TacticalBrushes.Surface3;
                LockToggleBorder = TacticalBrushes.BorderMedium;
                LockToggleForeground = TacticalBrushes.ActiveFg;
            }
            else if (currentStatus == QuestStatus.Done)
            {
                LockToggleIcon = "✓";
                LockToggleToolTip = "Status: Completed.\nRight-click: status options.";
                LockToggleBackground = TacticalBrushes.DoneBg;
                LockToggleBorder = TacticalBrushes.DoneBorder;
                LockToggleForeground = TacticalBrushes.DoneFg;
            }
            else
            {
                LockToggleIcon = "🔒";
                LockToggleToolTip = $"Status: {currentStatus} (Automatic from requirements).\nClick: force unlock.\nRight-click: options.";
                LockToggleBackground = TacticalBrushes.LockedBg;
                LockToggleBorder = TacticalBrushes.LockedBorder;
                LockToggleForeground = TacticalBrushes.LockedFg;
            }
        }

        public string TraderInitial =>

            !string.IsNullOrEmpty(Trader) && Trader.Length >= 2

                ? Trader.Substring(0, 2).ToUpperInvariant()

                : "TR";

        public string WikiUrl

        {

            get => _wikiUrl;

            set

            {

                _wikiUrl = value;

                OnPropertyChanged();

                OnPropertyChanged(nameof(WikiVisibility));

            }

        }

        public Visibility WikiVisibility => !string.IsNullOrWhiteSpace(_wikiUrl) ? Visibility.Visible : Visibility.Collapsed;

        public string MapBadge

        {

            get => string.IsNullOrEmpty(_mapBadge) ? "Any Map" : _mapBadge;

            set { _mapBadge = value; OnPropertyChanged(); }

        }

        private string? _mapBadgeToolTip;
        public string MapBadgeToolTip
        {
            get => string.IsNullOrEmpty(_mapBadgeToolTip) ? MapBadge : _mapBadgeToolTip;
            set { _mapBadgeToolTip = value; OnPropertyChanged(); }
        }

        public bool IsExpanded

        {

            get => _isExpanded;

            set

            {

                _isExpanded = value;

                OnPropertyChanged();

                OnPropertyChanged(nameof(ExpandedVisibility));

                OnPropertyChanged(nameof(ChevronGlyph));

            }

        }

        public Visibility ExpandedVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;

        public string ChevronGlyph => IsExpanded ? "▲" : "▼";

        public bool IsCompleted

        {

            get => _isCompleted;

            set

            {

                _isCompleted = value;

                OnPropertyChanged();

                OnPropertyChanged(nameof(CompleteButtonBackground));

                OnPropertyChanged(nameof(CompleteButtonBorder));

                OnPropertyChanged(nameof(CompleteButtonForeground));

                OnPropertyChanged(nameof(CompleteButtonText));

            }

        }

        public Brush CompleteButtonBackground => IsCompleted

            ? TacticalBrushes.ActiveBg

            : TacticalBrushes.Surface3;

        public Brush CompleteButtonBorder => IsCompleted

            ? TacticalBrushes.ActiveBorder

            : TacticalBrushes.BorderMedium;

        public Brush CompleteButtonForeground => IsCompleted

            ? TacticalBrushes.ActiveFg

            : TacticalBrushes.TextSecondary;

        public string CompleteButtonText => IsCompleted ? "Completed" : "Complete";

        public int TotalRequirements
        {
            get => _totalRequirements;
            set
            {
                _totalRequirements = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RequirementsSummaryText));
                OnPropertyChanged(nameof(RequirementsSummaryColor));
                OnPropertyChanged(nameof(RequirementsPillText));
                OnPropertyChanged(nameof(RequirementsPillVisibility));
                OnPropertyChanged(nameof(RequirementsPillBackground));
                OnPropertyChanged(nameof(RequirementsPillBorder));
                OnPropertyChanged(nameof(RequirementsPillForeground));
            }
        }

        public int MetRequirements
        {
            get => _metRequirements;
            set
            {
                _metRequirements = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RequirementsSummaryText));
                OnPropertyChanged(nameof(RequirementsSummaryColor));
                OnPropertyChanged(nameof(RequirementsPillText));
                OnPropertyChanged(nameof(RequirementsPillVisibility));
                OnPropertyChanged(nameof(RequirementsPillBackground));
                OnPropertyChanged(nameof(RequirementsPillBorder));
                OnPropertyChanged(nameof(RequirementsPillForeground));
            }
        }

        public string RequirementsSummaryText =>
            $"Requirements {MetRequirements} of {TotalRequirements} met";

        public Brush RequirementsSummaryColor =>
            TotalRequirements > 0 && MetRequirements >= TotalRequirements
                ? TacticalBrushes.TacticalSuccess
                : (MetRequirements > 0
                    ? TacticalBrushes.TacticalWarning
                    : TacticalBrushes.TextSecondary);

        public string RequirementsPillText => $"{MetRequirements}/{TotalRequirements}";

        public Visibility RequirementsPillVisibility => TotalRequirements > 0 ? Visibility.Visible : Visibility.Collapsed;

        public Brush RequirementsPillBackground
        {
            get
            {
                if (TotalRequirements > 0 && MetRequirements >= TotalRequirements)
                    return new SolidColorBrush(Color.FromArgb(45, 46, 125, 78));
                if (MetRequirements > 0)
                    return new SolidColorBrush(Color.FromArgb(45, 197, 168, 74));
                return new SolidColorBrush(Color.FromArgb(255, 26, 30, 38));
            }
        }

        public Brush RequirementsPillBorder
        {
            get
            {
                if (TotalRequirements > 0 && MetRequirements >= TotalRequirements)
                    return new SolidColorBrush(Color.FromArgb(255, 46, 125, 78));
                if (MetRequirements > 0)
                    return new SolidColorBrush(Color.FromArgb(255, 197, 168, 74));
                return new SolidColorBrush(Color.FromArgb(255, 54, 62, 78));
            }
        }

        public Brush RequirementsPillForeground
        {
            get
            {
                if (TotalRequirements > 0 && MetRequirements >= TotalRequirements)
                    return new SolidColorBrush(Color.FromArgb(255, 78, 202, 120));
                if (MetRequirements > 0)
                    return new SolidColorBrush(Color.FromArgb(255, 229, 184, 105));
                return new SolidColorBrush(Color.FromArgb(255, 215, 220, 230));
            }
        }

        public ObservableCollection<SubObjectiveStepperItem> ItemSteppers { get; } = new();

        public ObservableCollection<SubObjectiveActionItem> ActionToggles { get; } = new();

        public Visibility ItemSteppersVisibility => ItemSteppers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public int XpReward

        {

            get => _xpReward;

            set

            {

                _xpReward = value;

                OnPropertyChanged();

                OnPropertyChanged(nameof(FormattedXpReward));

                OnPropertyChanged(nameof(XpRewardVisibility));

                OnPropertyChanged(nameof(RewardsStripVisibility));

                OnPropertyChanged(nameof(RewardsFooterVisibility));

            }

        }

        public string FormattedXpReward => $"+{XpReward.ToString("N0", CultureInfo.InvariantCulture)} XP";

        public Visibility XpRewardVisibility => XpReward > 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility RewardsStripVisibility =>

            (XpReward > 0 || FormattedRepRewards.Count > 0 || ItemRewards.Count > 0)

                ? Visibility.Visible

                : Visibility.Collapsed;

        public Visibility RewardsFooterVisibility =>

            (RewardsStripVisibility == Visibility.Visible || UnlocksNext > 0 || Impact > 0)

                ? Visibility.Visible

                : Visibility.Collapsed;

        public Dictionary<string, double> TraderRepRewards { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<TraderRepRewardItem> FormattedRepRewards { get; } = new();

        public ObservableCollection<string> ItemRewards { get; } = new();

        public ObservableCollection<RewardItemCardViewModel> DetailedItemRewards { get; } = new();

        public ObservableCollection<string> PreviousTaskNames { get; } = new();

        public ObservableCollection<string> NextTaskNames { get; } = new();

        public string ItemRewardsCountText => DetailedItemRewards.Count > 0

            ? $"{DetailedItemRewards.Count} items"

            : (ItemRewards.Count > 0 ? $"{ItemRewards.Count} items" : string.Empty);

        public Visibility ItemRewardsPillVisibility =>

            (DetailedItemRewards.Count > 0 || ItemRewards.Count > 0) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility NoPreviousTasksVisibility => PreviousTaskNames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility NoRewardItemsVisibility => DetailedItemRewards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility NoNextTasksVisibility => NextTaskNames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private bool _isRewardsDrawerOpen;

        public bool IsRewardsDrawerOpen

        {

            get => _isRewardsDrawerOpen;

            set

            {

                if (_isRewardsDrawerOpen != value)

                {

                    _isRewardsDrawerOpen = value;

                    OnPropertyChanged();

                    OnPropertyChanged(nameof(RewardsDrawerVisibility));

                    OnPropertyChanged(nameof(RewardsDrawerChevron));

                }

            }

        }

        public Visibility RewardsDrawerVisibility => IsRewardsDrawerOpen ? Visibility.Visible : Visibility.Collapsed;

        public string RewardsDrawerChevron => IsRewardsDrawerOpen ? "▴" : "▾";

        public void ToggleRewardsDrawer() => IsRewardsDrawerOpen = !IsRewardsDrawerOpen;

        public int UnlocksNext

        {

            get => _unlocksNext;

            set

            {

                _unlocksNext = value;

                OnPropertyChanged();

                OnPropertyChanged(nameof(ImpactSummaryText));

                OnPropertyChanged(nameof(RewardsFooterVisibility));

            }

        }

        public int Impact

        {

            get => _impact;

            set

            {

                _impact = value;

                OnPropertyChanged();

                OnPropertyChanged(nameof(ImpactSummaryText));

                OnPropertyChanged(nameof(RewardsFooterVisibility));

            }

        }

        public string ImpactSummaryText => $"Unlocks next: {UnlocksNext} | Impact: {Impact}";

        public void ToggleExpand() => IsExpanded = !IsExpanded;

        public void MarkComplete()

        {

            if (_task == null) return;

            if (IsCompleted)

            {

                IsCompleted = false;

                QuestProgressService.Instance.ResetQuest(_task);

                RecalculateMetRequirements();

            }

            else

            {

                IsCompleted = true;

                foreach (var s in ItemSteppers) s.SetMax();

                foreach (var a in ActionToggles) a.IsChecked = true;

                MetRequirements = TotalRequirements;

                QuestProgressService.Instance.CompleteQuest(_task, true);

            }

        }

        public void RecalculateMetRequirements()
        {
            int met = 0;
            met += ItemSteppers.Count(s => s.CurrentCount >= s.MaxCount);
            met += ActionToggles.Count(a => a.IsChecked);
            MetRequirements = met;
            OnPropertyChanged(nameof(RequirementsPillText));
            OnPropertyChanged(nameof(RequirementsPillVisibility));
            OnPropertyChanged(nameof(RequirementsPillBackground));
            OnPropertyChanged(nameof(RequirementsPillBorder));
            OnPropertyChanged(nameof(RequirementsPillForeground));
        }

        public void RefreshRewardsVisibility()

        {

            OnPropertyChanged(nameof(RewardsStripVisibility));

            OnPropertyChanged(nameof(RewardsFooterVisibility));

            OnPropertyChanged(nameof(ItemRewardsCountText));

            OnPropertyChanged(nameof(ItemRewardsPillVisibility));

            OnPropertyChanged(nameof(NoPreviousTasksVisibility));

            OnPropertyChanged(nameof(NoRewardItemsVisibility));

            OnPropertyChanged(nameof(NoNextTasksVisibility));

        }

        public void UpdateRepRewardItems()

        {

            FormattedRepRewards.Clear();

            foreach (var kvp in TraderRepRewards)

            {

                bool isNegative = kvp.Value < 0;

                string formatted = $"{kvp.Value.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)} {kvp.Key}";

                FormattedRepRewards.Add(new TraderRepRewardItem

                {

                    Text = formatted,

                    BackgroundBrush = isNegative ? TacticalBrushes.FailedBg : TacticalBrushes.ActiveBg,

                    BorderBrush = isNegative ? TacticalBrushes.FailedBorder : TacticalBrushes.ActiveBorder,

                    TextBrush = isNegative ? TacticalBrushes.FailedFg : TacticalBrushes.ActiveFg

                });

            }

            RefreshRewardsVisibility();

        }

        public void LoadFromTask(TarkovTask task, QuestStatus status = QuestStatus.Active)

        {

            _task = task;

            QuestId = task.Id ?? task.NormalizedName ?? string.Empty;

                        var lang = LocalizationService.Instance.CurrentLanguage;

            if (lang == AppLanguage.KO && !string.IsNullOrEmpty(task.NameKo))

                Title = task.NameKo;

            else if (lang == AppLanguage.JA && !string.IsNullOrEmpty(task.NameJa))

                Title = task.NameJa;

            else

                Title = task.Name ?? task.NormalizedName ?? string.Empty;

            Trader = task.Trader ?? "Unknown";

            AvatarUrl = string.Empty;

            WikiUrl = task.WikiPageLink ?? (string.IsNullOrEmpty(task.NormalizedName) ? string.Empty : $"https://escapefromtarkov.fandom.com/wiki/{task.NormalizedName}");

            if (task.Maps == null || task.Maps.Count == 0)
            {
                MapBadge = "Any Map";
                MapBadgeToolTip = "Any Map";
            }
            else if (task.Maps.Count == 1)
            {
                var displayName = QuestFilterEngine.GetCanonicalMapDisplayName(task.Maps[0]);
                MapBadge = displayName;
                MapBadgeToolTip = displayName;
            }
            else if (task.Maps.Count == 2)
            {
                var names = task.Maps.Select(QuestFilterEngine.GetCanonicalMapDisplayName).ToList();
                var combined = string.Join(", ", names);
                MapBadge = combined;
                MapBadgeToolTip = combined;
            }
            else if (task.Maps.Count >= 7)
            {
                MapBadge = "Any Map";
                MapBadgeToolTip = string.Join(", ", task.Maps.Select(QuestFilterEngine.GetCanonicalMapDisplayName));
            }
            else
            {
                var names = task.Maps.Select(QuestFilterEngine.GetCanonicalMapDisplayName).ToList();
                MapBadge = $"Multi-Map ({task.Maps.Count})";
                MapBadgeToolTip = string.Join(", ", names);
            }

            IsCompleted = status == QuestStatus.Done || QuestProgressService.Instance.IsQuestCompleted(task.NormalizedName ?? string.Empty);

            var hiddenSet = TarkovHelper.Services.Settings.MapSettings.Instance.HiddenQuests;

            IsHidden = (!string.IsNullOrEmpty(task.NormalizedName) && hiddenSet.Contains(task.NormalizedName))

                || (!string.IsNullOrEmpty(task.Id) && hiddenSet.Contains(task.Id))

                || (task.Ids != null && task.Ids.Any(id => hiddenSet.Contains(id)))

                || (!string.IsNullOrEmpty(task.Name) && hiddenSet.Contains(task.Name));

            IsKappaRequired = task.ReqKappa;
            IsSeasonal = task.IsSeasonal;

            HasFir = task.RequiredItems?.Any(ri => ri.FoundInRaid) == true;

            RequiredLevelText = task.RequiredLevel.HasValue && task.RequiredLevel.Value > 1 ? $"Lv.{task.RequiredLevel.Value}" : string.Empty;

            StatusText = status.ToString();

            StatusBackground = status switch

            {

                QuestStatus.Active => TacticalBrushes.ActiveBg,

                QuestStatus.Done => TacticalBrushes.DoneBg,

                QuestStatus.Locked => TacticalBrushes.LockedBg,

                QuestStatus.LevelLocked => TacticalBrushes.LevelLockedBg,

                QuestStatus.Failed => TacticalBrushes.FailedBg,

                _ => TacticalBrushes.LockedBg

            };

            StatusBorder = status switch

            {

                QuestStatus.Active => TacticalBrushes.ActiveBorder,

                QuestStatus.Done => TacticalBrushes.DoneBorder,

                QuestStatus.Locked => TacticalBrushes.LockedBorder,

                QuestStatus.LevelLocked => TacticalBrushes.LevelLockedBorder,

                QuestStatus.Failed => TacticalBrushes.FailedBorder,

                _ => TacticalBrushes.LockedBorder

            };

            StatusForeground = status switch

            {

                QuestStatus.Active => TacticalBrushes.ActiveFg,

                QuestStatus.Done => TacticalBrushes.DoneFg,

                QuestStatus.Locked => TacticalBrushes.LockedFg,

                QuestStatus.LevelLocked => TacticalBrushes.LevelLockedFg,

                QuestStatus.Failed => TacticalBrushes.FailedFg,

                _ => TacticalBrushes.LockedFg

            };

            RefreshLockToggleState();

            // Trader avatar loading from local cache or web service

            if (!string.IsNullOrEmpty(Trader))

            {

                var traderObj = TraderDbService.Instance.GetTraderByName(Trader);

                if (traderObj != null)

                {

                    var img = ImageCacheService.Instance.GetLocalItemIcon(traderObj.Id);

                    if (img != null)

                    {

                        AvatarSource = img;

                    }

                    else

                    {

                        var imageUrl = traderObj.ImageLink ?? $"https://assets.tarkov.dev/{traderObj.Id}.webp";

                        _ = LoadTraderAvatarAsync(imageUrl);

                    }

                }

            }

            // Keys loading

            NeededKeys.Clear();

            var keys = NeededKeysService.Instance.GetNeededKeysForQuest(task);

            if (keys != null)

            {

                foreach (var k in keys)

                {

                    NeededKeys.Add(new TarkovHelper.Pages.NeededKeyViewModel

                    {

                        KeyId = k.KeyId,

                        Name = k.Name,

                        ShortName = k.ShortName,

                        InStash = k.InStash,

                        StashCount = k.StashQuantity,

                        IconSource = !string.IsNullOrEmpty(k.KeyId) ? ImageCacheService.Instance.GetLocalItemIcon(k.KeyId) : null

                    });

                }

            }

            OnPropertyChanged(nameof(HasNeededKeys));

            OnPropertyChanged(nameof(NeededKeysVisibility));

            OnPropertyChanged(nameof(GuideText));

            OnPropertyChanged(nameof(HasGuideText));

            OnPropertyChanged(nameof(GuideExpanderVisibility));

            OnPropertyChanged(nameof(HasMap));

            OnPropertyChanged(nameof(MapActionVisibility));

            OnPropertyChanged(nameof(StoryBadgeVisibility));

            // Calculate DAG impact

            var impact = TaskImpactEngine.Instance.GetTaskImpact(task.NormalizedName ?? task.Id ?? string.Empty);

            UnlocksNext = impact.UnlocksNext;

            Impact = impact.TotalDownstreamImpact;

            // Clear and populate ItemSteppers (grouping alternative turn-ins such as Ironclad Proof)
            ItemSteppers.Clear();
            if (task.RequiredItems != null && task.RequiredItems.Count > 0)
            {
                var handoverItems = task.RequiredItems
                    .Where(ri => string.IsNullOrEmpty(ri.Requirement) || string.Equals(ri.Requirement, "Handover", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                int handoverObjCount = task.Objectives != null
                    ? task.Objectives.Count(obj => IsHandoverObjective(obj, task.RequiredItems))
                    : 0;

                var groups = handoverItems.GroupBy(ri => (ri.Amount, ri.FoundInRaid)).ToList();

                foreach (var group in groups)
                {
                    var itemsInGroup = group.ToList();

                    if (itemsInGroup.Count > 1 && handoverItems.Count > Math.Max(1, handoverObjCount))
                    {
                        var primaryItem = itemsInGroup[0];
                        string groupNeedKey = NeededItemProgressService.GetQuestItemKey(task.NormalizedName ?? string.Empty, "alt_" + primaryItem.ItemNormalizedName, primaryItem.DogtagMinLevel);

                        int currentCount = 0;
                        if (IsCompleted)
                        {
                            currentCount = primaryItem.Amount;
                        }
                        else
                        {
                            int groupProg = NeededItemProgressService.Instance.GetCount(groupNeedKey);
                            if (groupProg > 0)
                            {
                                currentCount = groupProg;
                            }
                            else
                            {
                                int sum = itemsInGroup.Sum(it => NeededItemProgressService.Instance.GetCount(NeededItemProgressService.GetQuestItemKey(task.NormalizedName ?? string.Empty, it.ItemNormalizedName, it.DogtagMinLevel)));
                                currentCount = Math.Min(primaryItem.Amount, sum);
                            }
                        }

                        var stepper = new SubObjectiveStepperItem
                        {
                            ItemId = primaryItem.ItemNormalizedName,
                            ItemName = primaryItem.ItemDisplayName ?? primaryItem.ItemNormalizedName,
                            CurrentCount = currentCount,
                            MaxCount = primaryItem.Amount,
                            FoundInRaid = primaryItem.FoundInRaid,
                            NeedKey = groupNeedKey,
                            OnChanged = RecalculateMetRequirements
                        };

                        foreach (var alt in itemsInGroup)
                        {
                            stepper.Alternatives.Add(new AlternativeItemOption
                            {
                                ItemId = alt.ItemNormalizedName,
                                NormalizedName = alt.ItemNormalizedName,
                                DisplayName = alt.ItemDisplayName ?? alt.ItemNormalizedName
                            });
                        }
                        stepper.SelectedAlternative = stepper.Alternatives.FirstOrDefault();
                        // Dropped auto-cycling timer in favor of ComboBox dropdown

                        ItemSteppers.Add(stepper);
                    }
                    else
                    {
                        foreach (var ri in itemsInGroup)
                        {
                            string needKey = NeededItemProgressService.GetQuestItemKey(task.NormalizedName ?? string.Empty, ri.ItemNormalizedName, ri.DogtagMinLevel);
                            int currentCount = IsCompleted ? ri.Amount : NeededItemProgressService.Instance.GetCount(needKey);
                            var stepper = new SubObjectiveStepperItem
                            {
                                ItemId = ri.ItemNormalizedName,
                                ItemName = ri.ItemDisplayName ?? ri.ItemNormalizedName,
                                CurrentCount = currentCount,
                                MaxCount = ri.Amount,
                                FoundInRaid = ri.FoundInRaid,
                                NeedKey = needKey,
                                OnChanged = RecalculateMetRequirements
                            };
                            ItemSteppers.Add(stepper);
                        }
                    }
                }
            }

            OnPropertyChanged(nameof(ItemSteppersVisibility));

            // Clear and populate ActionToggles (skipping handover objectives already represented as item steppers)

            ActionToggles.Clear();

            if (task.Objectives != null && task.Objectives.Count > 0)

            {

                for (int i = 0; i < task.Objectives.Count; i++)

                {

                    string objText = task.Objectives[i];

                    if (IsHandoverObjective(objText, task.RequiredItems))

                    {

                        continue;

                    }

                    string cleaned = WikiMarkupHelper.RemoveOptionalMarker(objText);

                    string objId = ObjectiveProgressService.Instance.ResolveObjectiveId(task.NormalizedName ?? string.Empty, i, null, task) ?? string.Empty;

                    bool isObjCompleted = IsCompleted || ObjectiveProgressService.Instance.IsObjectiveCompleted(task.NormalizedName ?? string.Empty, i);

                    var action = new SubObjectiveActionItem

                    {

                        ObjectiveId = objId,

                        QuestNormalizedName = task.NormalizedName ?? string.Empty,

                        ObjectiveIndex = i,

                        Description = cleaned,

                        IsChecked = isObjCompleted,

                        OnChanged = RecalculateMetRequirements

                    };

                    ActionToggles.Add(action);

                }

            }

            TotalRequirements = ItemSteppers.Count + ActionToggles.Count;
            RecalculateMetRequirements();
            OnPropertyChanged(nameof(RequirementsPillText));
            OnPropertyChanged(nameof(RequirementsPillVisibility));
            OnPropertyChanged(nameof(RequirementsPillBackground));
            OnPropertyChanged(nameof(RequirementsPillBorder));
            OnPropertyChanged(nameof(RequirementsPillForeground));

            // Populate Previous & Next task names for the expandable drawer

            PreviousTaskNames.Clear();

            if (task.Previous != null)

            {

                foreach (var prevNorm in task.Previous)

                {

                    if (string.IsNullOrWhiteSpace(prevNorm)) continue;

                    var prevTask = QuestProgressService.Instance.GetTask(prevNorm);

                    PreviousTaskNames.Add($"← {prevTask?.Name ?? prevNorm}");

                }

            }

            NextTaskNames.Clear();

            if (task.LeadsTo != null)

            {

                foreach (var nextNorm in task.LeadsTo)

                {

                    if (string.IsNullOrWhiteSpace(nextNorm)) continue;

                    var nextTask = QuestProgressService.Instance.GetTask(nextNorm);

                    NextTaskNames.Add($"→ {nextTask?.Name ?? nextNorm}");

                }

            }

            // Sourced XP & Rewards

            var rewards = QuestFilterEngine.GetTaskRewards(task);

            if (rewards != null && (rewards.Experience > 0 || rewards.TraderReputation.Count > 0 || rewards.ItemRewards.Count > 0 || rewards.DetailedItems.Count > 0))

            {

                XpReward = rewards.Experience;

                TraderRepRewards.Clear();

                foreach (var rep in rewards.TraderReputation)

                {

                    TraderRepRewards[rep.Key] = rep.Value;

                }

                ItemRewards.Clear();

                foreach (var item in rewards.ItemRewards)

                {

                    ItemRewards.Add(item);

                }

                DetailedItemRewards.Clear();

                foreach (var det in rewards.DetailedItems)

                {

                    var icon = !string.IsNullOrEmpty(det.ItemId) ? ImageCacheService.Instance.GetLocalItemIcon(det.ItemId) : null;

                    if (icon == null && !string.IsNullOrEmpty(det.BaseItemId))

                    {

                        icon = ImageCacheService.Instance.GetLocalItemIcon(det.BaseItemId);

                    }

                    DetailedItemRewards.Add(new RewardItemCardViewModel

                    {

                        ItemId = det.ItemId,

                        FullName = det.Name,

                        ShortName = det.ShortName,

                        Count = det.Count,

                        IconSource = icon

                    });

                }

            }

            else

            {

                XpReward = 0;

                TraderRepRewards.Clear();

                ItemRewards.Clear();

                DetailedItemRewards.Clear();

            }

            IsRewardsDrawerOpen = false;

            UpdateRepRewardItems();

        }

        public static bool IsHandoverObjective(string? objText, List<QuestItem>? requiredItems)
        {
            if (string.IsNullOrWhiteSpace(objText) || requiredItems == null || requiredItems.Count == 0)
                return false;

            string lower = objText.Trim().ToLowerInvariant();

            bool hasHandoverKeyword = lower.StartsWith("hand over")
                                   || lower.StartsWith("handover")
                                   || lower.StartsWith("turn in")
                                   || lower.StartsWith("give ")
                                   || lower.StartsWith("deliver ")
                                   || lower.StartsWith("surrender ")
                                   || lower.StartsWith("entregar");

            if (!hasHandoverKeyword)
                return false;

            if (lower.StartsWith("hand over the item:") || lower.StartsWith("hand over the found in raid item:"))
                return true;

            string cleanObj = CleanForMatch(lower);
            foreach (var ri in requiredItems)
            {
                string name = ri.ItemDisplayName ?? ri.ItemNormalizedName ?? string.Empty;
                if (!string.IsNullOrEmpty(name) && cleanObj.Contains(CleanForMatch(name)))
                    return true;

                if (!string.IsNullOrEmpty(ri.ItemNormalizedName))
                {
                    string norm = ri.ItemNormalizedName.Replace("-", " ").Replace("_", " ");
                    if (cleanObj.Contains(CleanForMatch(norm)))
                        return true;
                }
            }

            // Fallback for grouped alternatives (e.g. Ironclad Proof "Hand over 10 M67 Hand grenades to Prapor")
            if (cleanObj.Contains("grenade") && requiredItems.Any(ri => (ri.ItemDisplayName ?? ri.ItemNormalizedName ?? "").ToLowerInvariant().Contains("grenade")))
                return true;

            if (requiredItems.Any(ri => ri.Amount > 1 && cleanObj.Contains(ri.Amount.ToString()) && (string.IsNullOrEmpty(ri.Requirement) || string.Equals(ri.Requirement, "Handover", StringComparison.OrdinalIgnoreCase))))
                return true;

            return false;
        }

        private static string CleanForMatch(string text)

        {

            return text.Replace("-", " ")

                       .Replace("_", " ")

                       .Replace("'", "")

                       .Replace("\"", "")

                       .Replace(".", "")

                       .Replace(",", "")

                       .Trim()

                       .ToLowerInvariant();

        }

        private async Task LoadTraderAvatarAsync(string imageUrl)

        {

            try

            {

                var bmp = await ImageCacheService.Instance.GetImageAsync(imageUrl, "traders");

                if (bmp != null)

                {

                    if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())

                    {

                        Application.Current.Dispatcher.Invoke(() => AvatarSource = bmp);

                    }

                    else

                    {

                        AvatarSource = bmp;

                    }

                }

            }

            catch { }

        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    }

    /// <summary>

    /// Interaction logic for TaskCardControl.xaml.

    /// </summary>

    public partial class TaskCardControl : UserControl, INotifyPropertyChanged

    {

        private TaskCardViewModel _cardModel = new();

        public TaskCardViewModel CardModel

        {

            get => _cardModel;

            set

            {

                if (_cardModel != value)

                {

                    _cardModel = value;

                    OnPropertyChanged();

                    OnPropertyChanged(nameof(ViewModel));

                    UpdateRewardsFooterVisibility();

                }

            }

        }

        public TaskCardViewModel ViewModel => CardModel;

        public string RequirementsPillText => CardModel.RequirementsPillText;

        public Visibility RequirementsPillVisibility => CardModel.RequirementsPillVisibility;

        public Brush RequirementsPillBackground => CardModel.RequirementsPillBackground;

        public Brush RequirementsPillBorder => CardModel.RequirementsPillBorder;

        public Brush RequirementsPillForeground => CardModel.RequirementsPillForeground;

        public string RequirementsSummaryText => CardModel.RequirementsSummaryText;

        public int TotalRequirements => CardModel.TotalRequirements;

        public int MetRequirements => CardModel.MetRequirements;

        private void BtnLockToggle_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (CardModel.Task == null) return;

            string taskId = CardModel.Task.Ids?.FirstOrDefault() ?? CardModel.Task.NormalizedName ?? string.Empty;
            if (string.IsNullOrEmpty(taskId)) return;

            var qps = QuestProgressService.Instance;
            bool isForceUnlocked = qps.IsQuestForceUnlocked(taskId);
            bool isForceLocked = qps.IsQuestForceLocked(taskId);
            var currentStatus = qps.GetStatus(CardModel.Task);

            if (isForceUnlocked || isForceLocked)
            {
                qps.SetQuestOverride(taskId, null);
            }
            else if (currentStatus == QuestStatus.Active)
            {
                qps.SetQuestOverride(taskId, QuestStatus.Locked);
            }
            else
            {
                qps.SetQuestOverride(taskId, QuestStatus.Active);
            }

            CardModel.RefreshLockToggleState();
        }

        private void MenuForceUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (CardModel.Task == null) return;
            string taskId = CardModel.Task.Ids?.FirstOrDefault() ?? CardModel.Task.NormalizedName ?? string.Empty;
            QuestProgressService.Instance.SetQuestOverride(taskId, QuestStatus.Active);
            CardModel.RefreshLockToggleState();
        }

        private void MenuForceLock_Click(object sender, RoutedEventArgs e)
        {
            if (CardModel.Task == null) return;
            string taskId = CardModel.Task.Ids?.FirstOrDefault() ?? CardModel.Task.NormalizedName ?? string.Empty;
            QuestProgressService.Instance.SetQuestOverride(taskId, QuestStatus.Locked);
            CardModel.RefreshLockToggleState();
        }

        private void MenuResetOverride_Click(object sender, RoutedEventArgs e)
        {
            if (CardModel.Task == null) return;
            string taskId = CardModel.Task.Ids?.FirstOrDefault() ?? CardModel.Task.NormalizedName ?? string.Empty;
            QuestProgressService.Instance.SetQuestOverride(taskId, null);
            CardModel.RefreshLockToggleState();
        }

        public TaskCardControl()

        {

            InitializeComponent();

            DataContextChanged += OnDataContextChanged;

            Loaded += (s, e) => UpdateRewardsFooterVisibility();

        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)

        {

            if (e.NewValue is TarkovHelper.Pages.QuestViewModel qvm)

            {

                if (qvm.Task != null)

                {

                    CardModel.LoadFromTask(qvm.Task, qvm.Status);

                }

            }

            else if (e.NewValue is TaskCardViewModel cardVm)

            {

                CardModel = cardVm;

            }

            else if (e.NewValue is TarkovTask task)

            {

                CardModel.LoadFromTask(task, QuestProgressService.Instance.GetStatus(task));

            }

            UpdateRewardsFooterVisibility();

        }

        public void LoadFromTask(TarkovTask task, QuestStatus status = QuestStatus.Active)

        {

            CardModel.LoadFromTask(task, status);

            UpdateRewardsFooterVisibility();

        }

        public void UpdateRewardsFooterVisibility()

        {

            try

            {

                CardModel?.RefreshRewardsVisibility();

                if (RewardsFooterBorder != null)

                {

                    RewardsFooterBorder.Visibility = CardModel?.RewardsFooterVisibility ?? Visibility.Collapsed;

                }

                if (RewardsWrapPanel != null)

                {

                    RewardsWrapPanel.Visibility = CardModel?.RewardsStripVisibility ?? Visibility.Collapsed;

                }

            }

            catch { }

        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject

        {

            while (child != null)

            {

                if (child is T parent) return parent;

                child = VisualTreeHelper.GetParent(child);

            }

            return null;

        }

        private void BtnMap_Click(object sender, RoutedEventArgs e)
        {
            var targetMap = CardModel.Task?.Maps?.FirstOrDefault(m => !string.Equals(m, "any", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(targetMap))
            {
                var objs = TarkovHelper.Services.Map.QuestObjectiveService.Instance.GetObjectivesForTaskById(CardModel.Task?.Id ?? "");
                targetMap = objs?.FirstOrDefault(o => o.Locations.Count > 0 && !string.IsNullOrEmpty(o.Locations[0].MapName) && !string.Equals(o.Locations[0].MapName, "any", StringComparison.OrdinalIgnoreCase))?.Locations[0].MapName;
            }
            if (string.IsNullOrEmpty(targetMap))
            {
                targetMap = CardModel.Task?.Maps?.FirstOrDefault() ?? "customs";
            }

            var questListPage = FindVisualParent<QuestListPage>(this);
            if (questListPage != null)
            {
                questListPage.NavigateToTaskOnMap(CardModel.Task, targetMap);
                return;
            }

            if (Application.Current?.MainWindow is MainWindow mainWin)
            {
                mainWin.NavigateToTaskOnMap(CardModel.Task, targetMap);
            }
        }

        private void BtnWiki_Click(object sender, RoutedEventArgs e)

        {

            if (!string.IsNullOrEmpty(CardModel.WikiUrl))

            {

                try

                {

                    Process.Start(new ProcessStartInfo

                    {

                        FileName = CardModel.WikiUrl,

                        UseShellExecute = true

                    });

                }

                catch { }

            }

        }

        private void BtnComplete_Click(object sender, RoutedEventArgs e)

        {

            CardModel.MarkComplete();

        }

        private void BtnToggleExpand_Click(object sender, RoutedEventArgs e)

        {

            CardModel.ToggleExpand();

        }

        private void BtnToggleRewardsDrawer_Click(object sender, RoutedEventArgs e)

        {

            CardModel.ToggleRewardsDrawer();

        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

                private void BtnCycleAlternative_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SubObjectiveStepperItem item)
            {
                item.AdvanceNextAlternative();
            }
        }

        private void BtnStepperDecrement_Click(object sender, RoutedEventArgs e)

        {

            if (sender is Button btn && btn.Tag is SubObjectiveStepperItem item)

            {

                item.Decrement();

            }

        }

        private void BtnStepperIncrement_Click(object sender, RoutedEventArgs e)

        {

            if (sender is Button btn && btn.Tag is SubObjectiveStepperItem item)

            {

                item.Increment();

            }

        }

        private void AlternativeComboBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ComboBox cb && !cb.IsDropDownOpen)
            {
                e.Handled = true;
                var parent = VisualTreeHelper.GetParent(cb) as UIElement ?? cb.Parent as UIElement;
                parent?.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                });
            }
        }

        private void BtnStepperMax_Click(object sender, RoutedEventArgs e)

        {

            if (sender is Button btn && btn.Tag is SubObjectiveStepperItem item)

            {

                item.SetMax();

            }

        }

        private void BtnActionToggle_Click(object sender, RoutedEventArgs e)

        {

            if (sender is Button btn && btn.Tag is SubObjectiveActionItem item)

            {

                item.Toggle();

            }

        }

        public static readonly RoutedEvent HideTaskClickedEvent = EventManager.RegisterRoutedEvent(

            nameof(HideTaskClicked), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(TaskCardControl));

        public event RoutedEventHandler HideTaskClicked

        {

            add => AddHandler(HideTaskClickedEvent, value);

            remove => RemoveHandler(HideTaskClickedEvent, value);

        }

        private void BtnHide_Click(object sender, RoutedEventArgs e)

        {

            var task = CardModel.Task;

            if (task == null && DataContext is TarkovHelper.Pages.QuestViewModel qvm)

            {

                task = qvm.Task;

            }

            if (task != null)

            {

                var mapSettings = TarkovHelper.Services.Settings.MapSettings.Instance;

                var hidden = mapSettings.HiddenQuests;

                bool isCurrentlyHidden = (!string.IsNullOrEmpty(task.NormalizedName) && hidden.Contains(task.NormalizedName))

                    || (!string.IsNullOrEmpty(task.Id) && hidden.Contains(task.Id))

                    || (task.Ids != null && task.Ids.Any(id => hidden.Contains(id)))

                    || (!string.IsNullOrEmpty(task.Name) && hidden.Contains(task.Name));

                if (isCurrentlyHidden)

                {

                    if (!string.IsNullOrEmpty(task.NormalizedName)) mapSettings.RemoveHiddenQuest(task.NormalizedName);

                    if (!string.IsNullOrEmpty(task.Id)) mapSettings.RemoveHiddenQuest(task.Id);

                    if (task.Ids != null) { foreach (var id in task.Ids) mapSettings.RemoveHiddenQuest(id); }

                    if (!string.IsNullOrEmpty(task.Name)) mapSettings.RemoveHiddenQuest(task.Name);

                    CardModel.IsHidden = false;

                }

                else

                {

                    if (!string.IsNullOrEmpty(task.NormalizedName)) mapSettings.AddHiddenQuest(task.NormalizedName);

                    if (!string.IsNullOrEmpty(task.Id)) mapSettings.AddHiddenQuest(task.Id);

                    if (task.Ids != null) { foreach (var id in task.Ids) mapSettings.AddHiddenQuest(id); }

                    if (!string.IsNullOrEmpty(task.Name)) mapSettings.AddHiddenQuest(task.Name);

                    CardModel.IsHidden = true;

                }

                RaiseEvent(new RoutedEventArgs(HideTaskClickedEvent, this));

            }

        }

    }

}