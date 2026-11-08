using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TarkovHelper.Models;
using TarkovHelper.Services;

namespace TarkovHelper.ViewModels
{
    /// <summary>
    /// Persistent top header ViewModel centralizing character level, PVP/PVE profile,
    /// overall quest completion metrics, Kappa collector progress, and 11 canonical trader loyalty levels.
    /// </summary>
    public class MainHeaderViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly SettingsService _settingsService;
        private readonly ProfileService _profileService;
        private readonly QuestProgressService _questProgressService;
        private readonly QuestGraphService _questGraphService;

        private static readonly (string Name, string Id, string Initials, string? UnlockQuest)[] CanonicalTraders = new[]
        {
            ("Prapor", "54cb50c76803fa8b248b4571", "PR", (string?)null),
            ("Therapist", "54cb57776803fa99248b456e", "TH", null),
            ("Fence", "579dc571d53a0658a154fbec", "FE", null),
            ("Skier", "58330581ace78e27b8b10cee", "SK", null),
            ("Peacekeeper", "5935c25fb3acc3127c3d8cd9", "PK", "Friend from the West - Part 2"),
            ("Mechanic", "5a7c2eca46aef81a7ca2145d", "ME", null),
            ("Ragman", "5ac3b934156ae10c4430e83c", "RA", null),
            ("Jaeger", "5c0647fdd443bc2504c2d371", "JG", "Introduction"),
            ("Ref", "6617beeaa9cfa777ca915b7c", "RF", null),
            ("Lightkeeper", "638f541a29ffd1183d187f57", "LK", "To the Light - Getting Acquainted"),
            ("BTR Driver", "656f0f98d80a697f855d34b1", "BTR", "A Helping Hand")
        };

        public ObservableCollection<TraderLoyaltyItemViewModel> Traders { get; } = new();

        public RelayCommand LevelUpCommand { get; }
        public RelayCommand LevelDownCommand { get; }
        public RelayCommand SetPvpCommand { get; }
        public RelayCommand SetPveCommand { get; }
        public RelayCommand SetSeasonalCommand { get; }

        public MainHeaderViewModel(
            SettingsService? settingsService = null,
            ProfileService? profileService = null,
            QuestProgressService? questProgressService = null,
            QuestGraphService? questGraphService = null)
        {
            _settingsService = settingsService ?? SettingsService.Instance;
            _profileService = profileService ?? ProfileService.Instance;
            _questProgressService = questProgressService ?? QuestProgressService.Instance;
            _questGraphService = questGraphService ?? QuestGraphService.Instance;

            LevelUpCommand = new RelayCommand(LevelUp, () => CanLevelUp);
            LevelDownCommand = new RelayCommand(LevelDown, () => CanLevelDown);
            SetPvpCommand = new RelayCommand(() => CurrentProfile = ProfileType.Pvp);
            SetPveCommand = new RelayCommand(() => CurrentProfile = ProfileType.Pve);
            SetSeasonalCommand = new RelayCommand(() => CurrentProfile = ProfileType.Seasonal);

            InitializeTraders();

            _settingsService.PlayerLevelChanged += OnPlayerLevelChanged;
            _settingsService.ScavRepChanged += OnScavRepChanged;
            _profileService.ProfileChanged += OnProfileChanged;
            _questProgressService.ProgressChanged += OnProgressChanged;

            RefreshMetrics();
            LoadTraderAvatars();
        }

        private void InitializeTraders()
        {
            Traders.Clear();
            foreach (var (name, id, initials, unlock) in CanonicalTraders)
            {
                Traders.Add(new TraderLoyaltyItemViewModel
                {
                    Name = name,
                    TraderId = id,
                    FallbackInitials = initials,
                    UnlockQuest = unlock
                });
            }
        }

        private void LoadTraderAvatars()
        {
            foreach (var item in Traders)
            {
                _ = LoadTraderAvatarAsync(item);
            }
        }

        private async Task LoadTraderAvatarAsync(TraderLoyaltyItemViewModel item)
        {
            try
            {
                var trader = TraderDbService.Instance.GetTraderById(item.TraderId)
                             ?? TraderDbService.Instance.GetTraderByName(item.Name);
                var imageUrl = trader?.ImageLink ?? $"https://assets.tarkov.dev/{item.TraderId}.webp";
                var bitmap = await ImageCacheService.Instance.GetImageAsync(imageUrl, "traders");
                if (bitmap != null)
                {
                    RunOnUI(() =>
                    {
                        item.AvatarImage = bitmap;
                    });
                }
            }
            catch
            {
                // Fallback initials remain visible
            }
        }

        #region Player Level

        public int PlayerLevel
        {
            get => _settingsService.PlayerLevel;
            set => SetPlayerLevel(value);
        }

        public bool CanLevelUp => PlayerLevel < SettingsService.MaxPlayerLevel;
        public bool CanLevelDown => PlayerLevel > SettingsService.MinPlayerLevel;

        public void LevelUp()
        {
            if (CanLevelUp)
            {
                SetPlayerLevel(PlayerLevel + 1);
            }
        }

        public void LevelDown()
        {
            if (CanLevelDown)
            {
                SetPlayerLevel(PlayerLevel - 1);
            }
        }

        public void SetPlayerLevel(int level)
        {
            var clamped = Math.Clamp(level, SettingsService.MinPlayerLevel, SettingsService.MaxPlayerLevel);
            if (_settingsService.PlayerLevel != clamped)
            {
                _settingsService.PlayerLevel = clamped;
            }

            OnPropertyChanged(nameof(PlayerLevel));
            OnPropertyChanged(nameof(CanLevelUp));
            OnPropertyChanged(nameof(CanLevelDown));
            LevelUpCommand.RaiseCanExecuteChanged();
            LevelDownCommand.RaiseCanExecuteChanged();
        }

        #endregion

        #region Game Profile (PVP / PVE)

        public ProfileType CurrentProfile
        {
            get => _profileService.CurrentProfile;
            set
            {
                if (_profileService.CurrentProfile != value)
                {
                    _profileService.CurrentProfile = value;
                    OnPropertyChanged(nameof(CurrentProfile));
                    OnPropertyChanged(nameof(IsPvp));
                    OnPropertyChanged(nameof(IsPve));
                OnPropertyChanged(nameof(IsSeasonal));
                }
            }
        }

        public bool IsPvp
        {
            get => CurrentProfile == ProfileType.Pvp;
            set
            {
                if (value) CurrentProfile = ProfileType.Pvp;
            }
        }

        public bool IsPve
        {
            get => CurrentProfile == ProfileType.Pve;
            set
            {
                if (value) CurrentProfile = ProfileType.Pve;
            }
        }

        public bool IsSeasonal
        {
            get => CurrentProfile == ProfileType.Seasonal;
            set
            {
                if (value) CurrentProfile = ProfileType.Seasonal;
            }
        }

        #endregion

        #region Overall Quest Progress

        private int _overallTotalQuests;
        public int OverallTotalQuests
        {
            get => _overallTotalQuests;
            private set => SetField(ref _overallTotalQuests, value);
        }

        private int _overallCompletedQuests;
        public int OverallCompletedQuests
        {
            get => _overallCompletedQuests;
            private set => SetField(ref _overallCompletedQuests, value);
        }

        private double _overallProgressPercentage;
        public double OverallProgressPercentage
        {
            get => _overallProgressPercentage;
            private set => SetField(ref _overallProgressPercentage, value);
        }

        private string _overallProgressText = "0/0 (0.0%)";
        public string OverallProgressText
        {
            get => _overallProgressText;
            private set => SetField(ref _overallProgressText, value);
        }

        // Aliases for compatibility
        public int OverallTotal => OverallTotalQuests;
        public int OverallCompleted => OverallCompletedQuests;
        public double OverallPercentage
        {
            get => OverallProgressPercentage;
            set { }
        }

        public string OverallProgressTooltip =>
            $"Overall Quest Progress: {OverallCompletedQuests} of {OverallTotalQuests} quests completed ({OverallProgressPercentage:F1}%)";

        #endregion

        #region Kappa Container Progress

        private int _kappaTotalQuests;
        public int KappaTotalQuests
        {
            get => _kappaTotalQuests;
            private set => SetField(ref _kappaTotalQuests, value);
        }

        private int _kappaCompletedQuests;
        public int KappaCompletedQuests
        {
            get => _kappaCompletedQuests;
            private set => SetField(ref _kappaCompletedQuests, value);
        }

        private int _kappaPendingQuests;
        public int KappaPendingQuests
        {
            get => _kappaPendingQuests;
            private set => SetField(ref _kappaPendingQuests, value);
        }

        private double _kappaProgressPercentage;
        public double KappaProgressPercentage
        {
            get => _kappaProgressPercentage;
            private set => SetField(ref _kappaProgressPercentage, value);
        }

        private string _kappaProgressText = "0/0 (0.0%)";
        public string KappaProgressText
        {
            get => _kappaProgressText;
            private set => SetField(ref _kappaProgressText, value);
        }

        private string _kappaPendingText = "0 pending";
        public string KappaPendingText
        {
            get => _kappaPendingText;
            private set => SetField(ref _kappaPendingText, value);
        }

        // Aliases for compatibility
        public int KappaTotal => KappaTotalQuests;
        public int KappaCompleted => KappaCompletedQuests;
        public int KappaPending => KappaPendingQuests;
        public double KappaPercentage
        {
            get => KappaProgressPercentage;
            set { }
        }

        public string KappaProgressTooltip =>
            $"Collector (Kappa Container): {KappaCompletedQuests} of {KappaTotalQuests} completed, {KappaPendingQuests} pending ({KappaProgressPercentage:F1}%)";

        #endregion

        #region Refresh Logic

        public void Refresh() => RefreshMetrics();

        public void RefreshMetrics()
        {
            RunOnUI(() =>
            {
                OnPropertyChanged(nameof(PlayerLevel));
                OnPropertyChanged(nameof(CanLevelUp));
                OnPropertyChanged(nameof(CanLevelDown));
                OnPropertyChanged(nameof(CurrentProfile));
                OnPropertyChanged(nameof(IsPvp));
                OnPropertyChanged(nameof(IsPve));
                OnPropertyChanged(nameof(IsSeasonal));
                LevelUpCommand.RaiseCanExecuteChanged();
                LevelDownCommand.RaiseCanExecuteChanged();

                RefreshOverallProgress();
                RefreshKappaProgress();
                RefreshTraderLoyalty();
            });
        }

        private void RefreshOverallProgress()
        {
            try
            {
                var stats = _questProgressService.GetStatistics();
                OverallTotalQuests = stats.Total;
                OverallCompletedQuests = stats.Done;
                OverallProgressPercentage = stats.Total > 0
                    ? Math.Round((stats.Done * 100.0) / stats.Total, 1)
                    : 0.0;
                OverallProgressText = $"{OverallCompletedQuests}/{OverallTotalQuests} ({OverallProgressPercentage.ToString("F1", CultureInfo.InvariantCulture)}%)";

                OnPropertyChanged(nameof(OverallTotal));
                OnPropertyChanged(nameof(OverallCompleted));
                OnPropertyChanged(nameof(OverallPercentage));
                OnPropertyChanged(nameof(OverallProgressTooltip));
            }
            catch
            {
                OverallTotalQuests = 0;
                OverallCompletedQuests = 0;
                OverallProgressPercentage = 0.0;
                OverallProgressText = "0/0 (0.0%)";
            }
        }

        private void RefreshKappaProgress()
        {
            try
            {
                var (completed, total, pct) = _questGraphService.GetCollectorProgress(
                    normalizedName => _questProgressService.IsQuestCompleted(normalizedName));

                KappaTotalQuests = total;
                KappaCompletedQuests = completed;
                KappaPendingQuests = Math.Max(0, total - completed);
                KappaProgressPercentage = total > 0
                    ? Math.Round((completed * 100.0) / total, 1)
                    : 0.0;
                KappaProgressText = $"{KappaCompletedQuests}/{KappaTotalQuests} ({KappaProgressPercentage.ToString("F1", CultureInfo.InvariantCulture)}%)";
                KappaPendingText = $"{KappaPendingQuests} pending";

                OnPropertyChanged(nameof(KappaTotal));
                OnPropertyChanged(nameof(KappaCompleted));
                OnPropertyChanged(nameof(KappaPending));
                OnPropertyChanged(nameof(KappaPercentage));
                OnPropertyChanged(nameof(KappaProgressTooltip));
            }
            catch
            {
                KappaTotalQuests = 0;
                KappaCompletedQuests = 0;
                KappaPendingQuests = 0;
                KappaProgressPercentage = 0.0;
                KappaProgressText = "0/0 (0.0%)";
                KappaPendingText = "0 pending";
            }
        }

        public void RefreshTraderLoyalty()
        {
            foreach (var item in Traders)
            {
                if (item.IsFence)
                {
                    item.ScavKarma = _settingsService.ScavRep;
                    item.LoyaltyLevel = 1;
                    item.IsUnlocked = true;

                    if (item.ScavKarma >= 6.0)
                    {
                        item.LoyaltyBadge = "👑";
                        item.BadgeBackground = TraderLoyaltyItemViewModel.GoldBadgeBg;
                        item.BadgeForeground = TraderLoyaltyItemViewModel.DarkBadgeFg;
                        item.BadgeBorderBrush = TraderLoyaltyItemViewModel.GoldBadgeBorder;
                    }
                    else
                    {
                        item.LoyaltyBadge = item.ScavKarma.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
                        item.BadgeBackground = item.ScavKarma >= 0
                            ? TraderLoyaltyItemViewModel.PositiveScavBg
                            : TraderLoyaltyItemViewModel.NegativeScavBg;
                        item.BadgeForeground = item.ScavKarma >= 0
                            ? TraderLoyaltyItemViewModel.PositiveScavFg
                            : TraderLoyaltyItemViewModel.NegativeScavFg;
                        item.BadgeBorderBrush = item.ScavKarma >= 0
                            ? TraderLoyaltyItemViewModel.PositiveScavBorder
                            : TraderLoyaltyItemViewModel.NegativeScavBorder;
                    }

                    item.ToolTipText = $"Fence - Scav Karma: {item.ScavKarma.ToString("F2", CultureInfo.InvariantCulture)}";
                }
                else
                {
                    var ll = _questProgressService.GetTraderLoyaltyLevel(item.Name);
                    item.LoyaltyLevel = ll;
                    item.IsUnlocked = ll > 0;

                    if (ll == 0)
                    {
                        item.LoyaltyBadge = "🔒";
                        item.BadgeBackground = TraderLoyaltyItemViewModel.LockedBadgeBg;
                        item.BadgeForeground = TraderLoyaltyItemViewModel.LockedBadgeFg;
                        item.BadgeBorderBrush = TraderLoyaltyItemViewModel.LockedBadgeBorder;
                        item.ToolTipText = item.UnlockQuest != null
                            ? $"{item.Name} - Locked (Requires '{item.UnlockQuest}')"
                            : $"{item.Name} - Locked";
                    }
                    else if (ll >= 4)
                    {
                        item.LoyaltyBadge = "👑";
                        item.BadgeBackground = TraderLoyaltyItemViewModel.GoldBadgeBg;
                        item.BadgeForeground = TraderLoyaltyItemViewModel.DarkBadgeFg;
                        item.BadgeBorderBrush = TraderLoyaltyItemViewModel.GoldBadgeBorder;
                        item.ToolTipText = $"{item.Name} - LL4 (Crown)";
                    }
                    else
                    {
                        item.LoyaltyBadge = $"LL{ll}";
                        item.BadgeBackground = TraderLoyaltyItemViewModel.StandardBadgeBg;
                        item.BadgeForeground = TraderLoyaltyItemViewModel.StandardBadgeFg;
                        item.BadgeBorderBrush = TraderLoyaltyItemViewModel.StandardBadgeBorder;
                        item.ToolTipText = $"{item.Name} - LL{ll}";
                    }
                }
            }
        }

        #endregion

        #region Event Handlers

        private void OnPlayerLevelChanged(object? sender, int newLevel)
        {
            RunOnUI(() =>
            {
                OnPropertyChanged(nameof(PlayerLevel));
                OnPropertyChanged(nameof(CanLevelUp));
                OnPropertyChanged(nameof(CanLevelDown));
                LevelUpCommand.RaiseCanExecuteChanged();
                LevelDownCommand.RaiseCanExecuteChanged();
                RefreshTraderLoyalty();
            });
        }

        private void OnProfileChanged(object? sender, ProfileType profile)
        {
            RunOnUI(() =>
            {
                OnPropertyChanged(nameof(CurrentProfile));
                OnPropertyChanged(nameof(IsPvp));
                OnPropertyChanged(nameof(IsPve));
                OnPropertyChanged(nameof(IsSeasonal));
                RefreshOverallProgress();
                RefreshKappaProgress();
                RefreshTraderLoyalty();
            });
        }

        private void OnProgressChanged(object? sender, EventArgs e)
        {
            RunOnUI(() =>
            {
                RefreshOverallProgress();
                RefreshKappaProgress();
                RefreshTraderLoyalty();
            });
        }

        private void OnScavRepChanged(object? sender, double rep)
        {
            RunOnUI(RefreshTraderLoyalty);
        }

        private static void RunOnUI(Action action)
        {
            var app = Application.Current;
            if (app != null && app.Dispatcher != null && !app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.InvokeAsync(action);
            }
            else
            {
                action();
            }
        }

        #endregion

        #region INotifyPropertyChanged & IDisposable

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public void Dispose()
        {
            _settingsService.PlayerLevelChanged -= OnPlayerLevelChanged;
            _settingsService.ScavRepChanged -= OnScavRepChanged;
            _profileService.ProfileChanged -= OnProfileChanged;
            _questProgressService.ProgressChanged -= OnProgressChanged;
        }

        #endregion
    }

    /// <summary>
    /// Item ViewModel for one trader displayed in the compact header loyalty strip.
    /// </summary>
    public class TraderLoyaltyItemViewModel : INotifyPropertyChanged
    {
        public static readonly SolidColorBrush GoldBadgeBg = Freeze(new SolidColorBrush(Color.FromRgb(0xC5, 0xA8, 0x4A)));
        public static readonly SolidColorBrush DarkBadgeFg = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)));
        public static readonly SolidColorBrush GoldBadgeBorder = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82)));

        public static readonly SolidColorBrush StandardBadgeBg = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)));
        public static readonly SolidColorBrush StandardBadgeFg = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)));
        public static readonly SolidColorBrush StandardBadgeBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x45)));

        public static readonly SolidColorBrush LockedBadgeBg = Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0x22, 0x22)));
        public static readonly SolidColorBrush LockedBadgeFg = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)));
        public static readonly SolidColorBrush LockedBadgeBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x5C, 0x28, 0x28)));

        public static readonly SolidColorBrush PositiveScavBg = Freeze(new SolidColorBrush(Color.FromRgb(0x1B, 0x3D, 0x28)));
        public static readonly SolidColorBrush PositiveScavFg = Freeze(new SolidColorBrush(Color.FromRgb(0x81, 0xC7, 0x84)));
        public static readonly SolidColorBrush PositiveScavBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x5D, 0x3C)));

        public static readonly SolidColorBrush NegativeScavBg = Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0x22, 0x22)));
        public static readonly SolidColorBrush NegativeScavFg = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)));
        public static readonly SolidColorBrush NegativeScavBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x5C, 0x28, 0x28)));

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        public string Name { get; set; } = string.Empty;
        public string TraderId { get; set; } = string.Empty;
        public string FallbackInitials { get; set; } = string.Empty;
        public string? UnlockQuest { get; set; }

        public bool IsFence => string.Equals(Name, "Fence", StringComparison.OrdinalIgnoreCase);

        private int _loyaltyLevel = 1;
        public int LoyaltyLevel
        {
            get => _loyaltyLevel;
            set => SetField(ref _loyaltyLevel, value);
        }

        private bool _isUnlocked = true;
        public bool IsUnlocked
        {
            get => _isUnlocked;
            set => SetField(ref _isUnlocked, value);
        }

        private double _scavKarma;
        public double ScavKarma
        {
            get => _scavKarma;
            set
            {
                if (SetField(ref _scavKarma, value))
                {
                    OnPropertyChanged(nameof(ScavKarmaText));
                }
            }
        }

        public string ScavKarmaText => (_scavKarma > 0 ? "+" : "") + _scavKarma.ToString("F2", CultureInfo.InvariantCulture);

        private string _loyaltyBadge = "LL1";
        public string LoyaltyBadge
        {
            get => _loyaltyBadge;
            set => SetField(ref _loyaltyBadge, value);
        }

        private ImageSource? _avatarImage;
        public ImageSource? AvatarImage
        {
            get => _avatarImage;
            set
            {
                if (SetField(ref _avatarImage, value))
                {
                    OnPropertyChanged(nameof(HasAvatar));
                }
            }
        }

        public bool HasAvatar => _avatarImage != null;

        private string _toolTipText = string.Empty;
        public string ToolTipText
        {
            get => _toolTipText;
            set => SetField(ref _toolTipText, value);
        }

        private Brush _badgeBackground = StandardBadgeBg;
        public Brush BadgeBackground
        {
            get => _badgeBackground;
            set => SetField(ref _badgeBackground, value);
        }

        private Brush _badgeForeground = StandardBadgeFg;
        public Brush BadgeForeground
        {
            get => _badgeForeground;
            set => SetField(ref _badgeForeground, value);
        }

        private Brush _badgeBorderBrush = StandardBadgeBorder;
        public Brush BadgeBorderBrush
        {
            get => _badgeBorderBrush;
            set => SetField(ref _badgeBorderBrush, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    /// <summary>
    /// General purpose ICommand implementation for ViewModels.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _executeWithParam;
        private readonly Func<object?, bool>? _canExecuteWithParam;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            ArgumentNullException.ThrowIfNull(execute);
            _executeWithParam = _ => execute();
            _canExecuteWithParam = canExecute != null ? _ => canExecute() : null;
        }

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            ArgumentNullException.ThrowIfNull(execute);
            _executeWithParam = execute;
            _canExecuteWithParam = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecuteWithParam?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _executeWithParam(parameter);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
