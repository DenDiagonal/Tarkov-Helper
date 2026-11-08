using System;

using System.Collections.Generic;

using System.Collections.ObjectModel;

using System.ComponentModel;

using System.Globalization;

using System.Linq;

using System.Runtime.CompilerServices;

using System.Threading.Tasks;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using System.Windows.Media;

using System.Windows.Media.Imaging;

using TarkovHelper.Models;

using TarkovHelper.Services;



namespace TarkovHelper.Pages.Components

{

    public partial class TraderProgressPanel : UserControl

    {

        public event EventHandler<string>? TraderFilterRequested;



        private readonly ObservableCollection<TraderCardViewModel> _traderCards = new();

        private bool _isInitialized;



        private static readonly SolidColorBrush ActiveBgBrush = new(Color.FromRgb(0xC5, 0xA8, 0x4A)); // Accent Gold

        private static readonly SolidColorBrush ActiveFgBrush = new(Colors.White);

        private static readonly SolidColorBrush ActiveBorderBrush = new(Color.FromRgb(0xE5, 0xC8, 0x6A));



        private static readonly SolidColorBrush InactiveBgBrush = new(Color.FromRgb(0x2A, 0x2A, 0x30));

        private static readonly SolidColorBrush InactiveFgBrush = new(Color.FromRgb(0x9E, 0x9E, 0x9E));

        private static readonly SolidColorBrush InactiveBorderBrush = new(Color.FromRgb(0x3D, 0x3D, 0x45));



        private static readonly SolidColorBrush LockedBgBrush = new(Color.FromRgb(0x4A, 0x22, 0x22));

        private static readonly SolidColorBrush LockedFgBrush = new(Color.FromRgb(0xFF, 0x8A, 0x80));

        private static readonly SolidColorBrush UnlockedBgBrush = new(Color.FromRgb(0x1B, 0x3D, 0x28));

        private static readonly SolidColorBrush UnlockedFgBrush = new(Color.FromRgb(0x81, 0xC7, 0x84));



        static TraderProgressPanel()

        {

            ActiveBgBrush.Freeze();

            ActiveFgBrush.Freeze();

            ActiveBorderBrush.Freeze();

            InactiveBgBrush.Freeze();

            InactiveFgBrush.Freeze();

            InactiveBorderBrush.Freeze();

            LockedBgBrush.Freeze();

            LockedFgBrush.Freeze();

            UnlockedBgBrush.Freeze();

            UnlockedFgBrush.Freeze();

        }



        private static readonly TraderDef[] TraderDefinitions = new[]

        {

            new TraderDef("Prapor", "54cb50c76803fa8b248b4571", "PR", null, null),

            new TraderDef("Therapist", "54cb57776803fa99248b456e", "TH", null, null),

            new TraderDef("Fence", "579dc571d53a0658a154fbec", "FE", null, null),

            new TraderDef("Skier", "58330581ace78e27b8b10cee", "SK", null, null),

            new TraderDef("Peacekeeper", "5935c25fb3acc3127c3d8cd9", "PK", "Friend from the West - Part 2", q => q.IsPeacekeeperUnlocked()),

            new TraderDef("Mechanic", "5a7c2eca46aef81a7ca2145d", "ME", null, null),

            new TraderDef("Ragman", "5ac3b934156ae10c4430e83c", "RA", null, null),

            new TraderDef("Jaeger", "5c0647fdd443bc2504c2d371", "JG", "Introduction", q => q.IsJaegerUnlocked()),

            new TraderDef("Ref", "6617beeaa9cfa777ca915b7c", "RF", null, null),

            new TraderDef("Lightkeeper", "638f541a29ffd1183d187f57", "LK", "To the Light - Getting Acquainted", q => q.IsLightkeeperUnlocked()),

            new TraderDef("BTR Driver", "656f0f98d80a697f855d34b1", "BTR", "A Helping Hand", q => q.IsBtrDriverUnlocked())

        };



        public TraderProgressPanel()

        {

            InitializeComponent();

            TradersItemsControl.ItemsSource = _traderCards;



            Loaded += OnLoaded;

            Unloaded += OnUnloaded;

        }



        private void OnLoaded(object sender, RoutedEventArgs e)

        {

            QuestProgressService.Instance.ProgressChanged -= OnProgressChanged;

            QuestProgressService.Instance.ProgressChanged += OnProgressChanged;

            SettingsService.Instance.PlayerLevelChanged -= OnPlayerLevelChanged;

            SettingsService.Instance.PlayerLevelChanged += OnPlayerLevelChanged;

            SettingsService.Instance.ScavRepChanged -= OnScavRepChanged;

            SettingsService.Instance.ScavRepChanged += OnScavRepChanged;



            InitializeCardsOnce();

            RefreshAllTraders();

        }



        private void OnUnloaded(object sender, RoutedEventArgs e)

        {

            QuestProgressService.Instance.ProgressChanged -= OnProgressChanged;

            SettingsService.Instance.PlayerLevelChanged -= OnPlayerLevelChanged;

            SettingsService.Instance.ScavRepChanged -= OnScavRepChanged;

        }



        private void OnProgressChanged(object? sender, EventArgs e)

        {

            Dispatcher.InvokeAsync(RefreshAllTraders);

        }



        private void OnPlayerLevelChanged(object? sender, int newLevel)

        {

            Dispatcher.InvokeAsync(RefreshAllTraders);

        }



        private void InitializeCardsOnce()

        {

            if (_isInitialized) return;

            _isInitialized = true;



            _traderCards.Clear();

            foreach (var def in TraderDefinitions)

            {

                var card = new TraderCardViewModel

                {

                    TraderName = def.Name,

                    TraderId = def.Id,

                    FallbackInitials = def.FallbackInitials,

                    UnlockQuestName = def.UnlockQuestName,

                    HasUnlockRequirement = def.UnlockQuestName != null,

                    UnlockEvaluator = def.UnlockEvaluator

                };



                _traderCards.Add(card);



                // Asynchronously load trader image

                _ = LoadTraderAvatarAsync(card, def.Id);

            }

        }



        private async Task LoadTraderAvatarAsync(TraderCardViewModel card, string traderId)

        {

            try

            {

                var trader = TraderDbService.Instance.GetTraderById(traderId)

                             ?? TraderDbService.Instance.GetTraderByName(card.TraderName);



                var imageUrl = trader?.ImageLink ?? $"https://assets.tarkov.dev/{traderId}.webp";



                var bitmap = await ImageCacheService.Instance.GetImageAsync(imageUrl, "traders");

                if (bitmap != null)

                {

                    card.AvatarSource = bitmap;

                    card.AvatarVisibility = Visibility.Visible;

                    card.FallbackVisibility = Visibility.Collapsed;

                }

                else

                {

                    card.AvatarVisibility = Visibility.Collapsed;

                    card.FallbackVisibility = Visibility.Visible;

                }

            }

            catch

            {

                card.AvatarVisibility = Visibility.Collapsed;

                card.FallbackVisibility = Visibility.Visible;

            }

        }



        public void RefreshAllTraders()

        {

            if (!_isInitialized)

            {

                InitializeCardsOnce();

            }



            var progress = QuestProgressService.Instance;

            var loc = LocalizationService.Instance;

            var allTasks = progress.AllTasks;

            var playerFaction = SettingsService.Instance.PlayerFaction;



            int overallCompleted = 0;

            int overallTotal = 0;



            foreach (var card in _traderCards)

            {

                // Localized Name

                card.DisplayName = loc.GetLocalizedTraderName(card.TraderName);



                // Task counting

                var traderTasks = allTasks.Where(t =>

                    string.Equals(t.Trader, card.TraderName, StringComparison.OrdinalIgnoreCase) ||

                    string.Equals(QuestProgressService.ResolveTraderFriendlyName(t.Trader ?? string.Empty), card.TraderName, StringComparison.OrdinalIgnoreCase)

                ).ToList();



                var factionTasks = traderTasks.Where(t =>

                    string.IsNullOrEmpty(t.Faction) ||

                    t.Faction.Equals("Any", StringComparison.OrdinalIgnoreCase) ||

                    string.IsNullOrEmpty(playerFaction) ||

                    t.Faction.Equals(playerFaction, StringComparison.OrdinalIgnoreCase)

                ).ToList();



                var activeSet = factionTasks.Count > 0 ? factionTasks : traderTasks;

                int totalCount = activeSet.Count;

                int completedCount = activeSet.Count(t => progress.GetStatus(t) == QuestStatus.Done);



                card.TotalQuests = totalCount;

                card.CompletedQuests = completedCount;



                double pct = totalCount > 0 ? (completedCount / (double)totalCount) * 100.0 : 0.0;

                card.ProgressPercentage = pct;

                card.ProgressText = $"{completedCount}/{totalCount} ({pct.ToString("F1", CultureInfo.InvariantCulture)}%)";

                // Max interior width of progress bar is ~195

                card.ProgressBarWidth = Math.Max(0, Math.Min(195, (pct / 100.0) * 195.0));



                overallCompleted += completedCount;

                overallTotal += totalCount;



                // Unlock evaluation
                if (card.IsBtrDriver)
                {
                    bool isBtrActive = progress.IsBtrDriverUnlocked();
                    card.IsUnlocked = isBtrActive;
                    card.UnlockBadgeVisibility = Visibility.Visible;
                    if (isBtrActive)
                    {
                        card.UnlockBadgeText = "✓ Desbloqueado";
                        card.UnlockBadgeBackground = UnlockedBgBrush;
                        card.UnlockBadgeForeground = UnlockedFgBrush;
                        card.UnlockBadgeToolTip = "El Conductor del BTR está desbloqueado (Woods, Streets, Lighthouse)";

                        card.BtrToggleIcon = "✓";
                        card.BtrToggleText = "Desbloqueado (Clic p/ bloquear)";
                        card.BtrToggleBackground = UnlockedBgBrush;
                        card.BtrToggleBorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                        card.BtrToggleForeground = UnlockedFgBrush;
                    }
                    else
                    {
                        card.UnlockBadgeText = "🔒 A Helping Hand";
                        card.UnlockBadgeBackground = LockedBgBrush;
                        card.UnlockBadgeForeground = LockedFgBrush;
                        card.UnlockBadgeToolTip = "Desbloquea al entregar 'A Helping Hand' o haz clic abajo para desbloquear manualmente.";

                        card.BtrToggleIcon = "🔓";
                        card.BtrToggleText = "Desbloquear BTR (En partida)";
                        card.BtrToggleBackground = LockedBgBrush;
                        card.BtrToggleBorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                        card.BtrToggleForeground = LockedFgBrush;
                    }
                }
                else if (card.HasUnlockRequirement && card.UnlockEvaluator != null)
                {
                    bool isUnlocked = card.UnlockEvaluator(progress);
                    card.IsUnlocked = isUnlocked;
                    card.UnlockBadgeVisibility = Visibility.Visible;

                    if (isUnlocked)
                    {
                        card.UnlockBadgeText = "✓ Unlocked";
                        card.UnlockBadgeBackground = UnlockedBgBrush;
                        card.UnlockBadgeForeground = UnlockedFgBrush;
                        card.UnlockBadgeToolTip = $"Task '{card.UnlockQuestName}' is completed. Trader unlocked!";
                    }
                    else
                    {
                        card.UnlockBadgeText = $"🔒 {card.UnlockQuestName}";
                        card.UnlockBadgeBackground = LockedBgBrush;
                        card.UnlockBadgeForeground = LockedFgBrush;
                        card.UnlockBadgeToolTip = $"Unlocks after completing task '{card.UnlockQuestName}'";
                    }
                }
                else
                {
                    card.IsUnlocked = true;
                    card.UnlockBadgeVisibility = Visibility.Collapsed;
                }



                // Loyalty Level or Scav Karma evaluation

                if (card.IsFence)

                {

                    card.ScavKarma = SettingsService.Instance.ScavRep;

                }

                else

                {

                    int currentLL = progress.GetTraderLoyaltyLevel(card.TraderName);

                    card.SetLoyaltyLevel(currentLL);

                }

            }



            double overallPct = overallTotal > 0 ? (overallCompleted / (double)overallTotal) * 100.0 : 0.0;

            TxtOverallProgress.Text = $"{overallCompleted}/{overallTotal} ({overallPct.ToString("F1", CultureInfo.InvariantCulture)}%)";

        }



        private void OnScavRepChanged(object? sender, double newRep)

        {

            Dispatcher.InvokeAsync(RefreshAllTraders);

        }



        private void BtnLoyaltyLevel_Click(object sender, RoutedEventArgs e)

        {

            if (sender is Button btn && btn.DataContext is TraderCardViewModel card && btn.Tag is string tagStr && int.TryParse(tagStr, out int level))

            {

                // Set the override in QuestProgressService (which persists in user_data.db and triggers ProgressChanged)

                QuestProgressService.Instance.SetTraderLoyaltyLevelOverride(card.TraderName, level);

                card.SetLoyaltyLevel(level);

            }

        }



        private void BtnScavKarmaMinus_Click(object sender, RoutedEventArgs e)

        {

            var current = SettingsService.Instance.ScavRep;

            var updated = Math.Round(Math.Clamp(current - 0.10, SettingsService.MinScavRep, SettingsService.MaxScavRep), 1);

            SettingsService.Instance.ScavRep = updated;

            RefreshAllTraders();

        }



        private void BtnScavKarmaPlus_Click(object sender, RoutedEventArgs e)

        {

            var current = SettingsService.Instance.ScavRep;

            var updated = Math.Round(Math.Clamp(current + 0.10, SettingsService.MinScavRep, SettingsService.MaxScavRep), 1);

            SettingsService.Instance.ScavRep = updated;

            RefreshAllTraders();

        }



        private void TraderHeader_Click(object sender, MouseButtonEventArgs e)

        {

            if (sender is FrameworkElement elem && elem.DataContext is TraderCardViewModel card)

            {

                TraderFilterRequested?.Invoke(this, card.TraderName);

            }

        }



        private void BtnBtrToggle_Click(object sender, MouseButtonEventArgs e)
        {
            bool current = QuestProgressService.Instance.IsBtrDriverUnlocked();
            QuestProgressService.Instance.SetBtrDriverUnlocked(!current);
            RefreshAllTraders();
        }

        private record TraderDef(

            string Name,

            string Id,

            string FallbackInitials,

            string? UnlockQuestName,

            Func<QuestProgressService, bool>? UnlockEvaluator);

    }



    public class TraderCardViewModel : INotifyPropertyChanged

    {

        private string _traderName = string.Empty;

        private string _displayName = string.Empty;

        private ImageSource? _avatarSource;

        private Visibility _avatarVisibility = Visibility.Collapsed;

        private Visibility _fallbackVisibility = Visibility.Visible;

        private string _fallbackInitials = string.Empty;

        private string _progressText = "0/0 (0.0%)";

        private double _progressBarWidth;

        private bool _isUnlocked = true;

        private string _unlockBadgeText = string.Empty;

        private Brush _unlockBadgeBackground = Brushes.Transparent;

        private Brush _unlockBadgeForeground = Brushes.White;

        private Visibility _unlockBadgeVisibility = Visibility.Collapsed;

        private string _unlockBadgeToolTip = string.Empty;



        private Brush _ll1Bg = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));

        private Brush _ll1Fg = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));

        private FontWeight _ll1Weight = FontWeights.Normal;

        private Brush _ll1Border = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x45));



        private Brush _ll2Bg = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));

        private Brush _ll2Fg = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));

        private FontWeight _ll2Weight = FontWeights.Normal;

        private Brush _ll2Border = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x45));



        private Brush _ll3Bg = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));

        private Brush _ll3Fg = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));

        private FontWeight _ll3Weight = FontWeights.Normal;

        private Brush _ll3Border = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x45));



        private Brush _ll4Bg = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));

        private Brush _ll4Fg = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));

        private FontWeight _ll4Weight = FontWeights.Normal;

        private Brush _ll4Border = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x45));



        private static readonly SolidColorBrush ActiveBg = new(Color.FromRgb(0xC5, 0xA8, 0x4A));

        private static readonly SolidColorBrush ActiveFg = new(Colors.White);

        private static readonly SolidColorBrush ActiveBorder = new(Color.FromRgb(0xE5, 0xC8, 0x6A));



        private static readonly SolidColorBrush InactiveBg = new(Color.FromRgb(0x2A, 0x2A, 0x30));

        private static readonly SolidColorBrush InactiveFg = new(Color.FromRgb(0x9E, 0x9E, 0x9E));

        private static readonly SolidColorBrush InactiveBorder = new(Color.FromRgb(0x3D, 0x3D, 0x45));



        static TraderCardViewModel()

        {

            ActiveBg.Freeze();

            ActiveFg.Freeze();

            ActiveBorder.Freeze();

            InactiveBg.Freeze();

            InactiveFg.Freeze();

            InactiveBorder.Freeze();

        }



        public string TraderName

        {

            get => _traderName;

            set => SetField(ref _traderName, value);

        }



        public string TraderId { get; set; } = string.Empty;



        public string DisplayName

        {

            get => _displayName;

            set => SetField(ref _displayName, value);

        }



        public ImageSource? AvatarSource

        {

            get => _avatarSource;

            set => SetField(ref _avatarSource, value);

        }



        public Visibility AvatarVisibility

        {

            get => _avatarVisibility;

            set => SetField(ref _avatarVisibility, value);

        }



        public Visibility FallbackVisibility

        {

            get => _fallbackVisibility;

            set => SetField(ref _fallbackVisibility, value);

        }



        public string FallbackInitials

        {

            get => _fallbackInitials;

            set => SetField(ref _fallbackInitials, value);

        }



        public int TotalQuests { get; set; }

        public int CompletedQuests { get; set; }

        public double ProgressPercentage { get; set; }



        public string ProgressText

        {

            get => _progressText;

            set => SetField(ref _progressText, value);

        }



        public double ProgressBarWidth

        {

            get => _progressBarWidth;

            set => SetField(ref _progressBarWidth, value);

        }



        public bool HasUnlockRequirement { get; set; }

        public string? UnlockQuestName { get; set; }

        public Func<QuestProgressService, bool>? UnlockEvaluator { get; set; }



        public bool IsUnlocked

        {

            get => _isUnlocked;

            set => SetField(ref _isUnlocked, value);

        }



        public string UnlockBadgeText

        {

            get => _unlockBadgeText;

            set => SetField(ref _unlockBadgeText, value);

        }



        public Brush UnlockBadgeBackground

        {

            get => _unlockBadgeBackground;

            set => SetField(ref _unlockBadgeBackground, value);

        }



        public Brush UnlockBadgeForeground

        {

            get => _unlockBadgeForeground;

            set => SetField(ref _unlockBadgeForeground, value);

        }



        public Visibility UnlockBadgeVisibility

        {

            get => _unlockBadgeVisibility;

            set => SetField(ref _unlockBadgeVisibility, value);

        }



        public string UnlockBadgeToolTip

        {

            get => _unlockBadgeToolTip;

            set => SetField(ref _unlockBadgeToolTip, value);

        }



        public Brush LL1Background { get => _ll1Bg; private set => SetField(ref _ll1Bg, value); }

        public Brush LL1Foreground { get => _ll1Fg; private set => SetField(ref _ll1Fg, value); }

        public FontWeight LL1FontWeight { get => _ll1Weight; private set => SetField(ref _ll1Weight, value); }

        public Brush LL1BorderBrush { get => _ll1Border; private set => SetField(ref _ll1Border, value); }



        public Brush LL2Background { get => _ll2Bg; private set => SetField(ref _ll2Bg, value); }

        public Brush LL2Foreground { get => _ll2Fg; private set => SetField(ref _ll2Fg, value); }

        public FontWeight LL2FontWeight { get => _ll2Weight; private set => SetField(ref _ll2Weight, value); }

        public Brush LL2BorderBrush { get => _ll2Border; private set => SetField(ref _ll2Border, value); }



        public Brush LL3Background { get => _ll3Bg; private set => SetField(ref _ll3Bg, value); }

        public Brush LL3Foreground { get => _ll3Fg; private set => SetField(ref _ll3Fg, value); }

        public FontWeight LL3FontWeight { get => _ll3Weight; private set => SetField(ref _ll3Weight, value); }

        public Brush LL3BorderBrush { get => _ll3Border; private set => SetField(ref _ll3Border, value); }



        public Brush LL4Background { get => _ll4Bg; private set => SetField(ref _ll4Bg, value); }

        public Brush LL4Foreground { get => _ll4Fg; private set => SetField(ref _ll4Fg, value); }

        public FontWeight LL4FontWeight { get => _ll4Weight; private set => SetField(ref _ll4Weight, value); }

        public Brush LL4BorderBrush { get => _ll4Border; private set => SetField(ref _ll4Border, value); }



        public bool IsFence => string.Equals(TraderName, "Fence", StringComparison.OrdinalIgnoreCase);
        public bool IsBtrDriver => string.Equals(TraderName, "BTR Driver", StringComparison.OrdinalIgnoreCase);
        public bool IsLightkeeper => string.Equals(TraderName, "Lightkeeper", StringComparison.OrdinalIgnoreCase);

        public Visibility LLVisibility => (IsFence || IsBtrDriver || IsLightkeeper) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ScavKarmaVisibility => IsFence ? Visibility.Visible : Visibility.Collapsed;
        public Visibility BtrToggleVisibility => IsBtrDriver ? Visibility.Visible : Visibility.Collapsed;

        private string _btrToggleIcon = "✓";
        public string BtrToggleIcon
        {
            get => _btrToggleIcon;
            set => SetField(ref _btrToggleIcon, value);
        }

        private string _btrToggleText = "Disponible (Clic p/ pausar)";
        public string BtrToggleText
        {
            get => _btrToggleText;
            set => SetField(ref _btrToggleText, value);
        }

        private Brush _btrToggleBackground = Brushes.Transparent;
        public Brush BtrToggleBackground
        {
            get => _btrToggleBackground;
            set => SetField(ref _btrToggleBackground, value);
        }

        private Brush _btrToggleBorderBrush = Brushes.Transparent;
        public Brush BtrToggleBorderBrush
        {
            get => _btrToggleBorderBrush;
            set => SetField(ref _btrToggleBorderBrush, value);
        }

        private Brush _btrToggleForeground = Brushes.White;
        public Brush BtrToggleForeground
        {
            get => _btrToggleForeground;
            set => SetField(ref _btrToggleForeground, value);
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



        public void SetLoyaltyLevel(int level)

        {

            LL1Background = level == 1 ? ActiveBg : InactiveBg;

            LL1Foreground = level == 1 ? ActiveFg : InactiveFg;

            LL1FontWeight = level == 1 ? FontWeights.Bold : FontWeights.Normal;

            LL1BorderBrush = level == 1 ? ActiveBorder : InactiveBorder;



            LL2Background = level == 2 ? ActiveBg : InactiveBg;

            LL2Foreground = level == 2 ? ActiveFg : InactiveFg;

            LL2FontWeight = level == 2 ? FontWeights.Bold : FontWeights.Normal;

            LL2BorderBrush = level == 2 ? ActiveBorder : InactiveBorder;



            LL3Background = level == 3 ? ActiveBg : InactiveBg;

            LL3Foreground = level == 3 ? ActiveFg : InactiveFg;

            LL3FontWeight = level == 3 ? FontWeights.Bold : FontWeights.Normal;

            LL3BorderBrush = level == 3 ? ActiveBorder : InactiveBorder;



            LL4Background = level == 4 ? ActiveBg : InactiveBg;

            LL4Foreground = level == 4 ? ActiveFg : InactiveFg;

            LL4FontWeight = level == 4 ? FontWeights.Bold : FontWeights.Normal;

            LL4BorderBrush = level == 4 ? ActiveBorder : InactiveBorder;

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

}

