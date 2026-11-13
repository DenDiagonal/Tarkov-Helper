using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using TarkovHelper.Models;
using TarkovHelper.Services;

namespace TarkovHelper.ViewModels
{
    /// <summary>
    /// Contract summary for top stats overview row (F17).
    /// </summary>
    public record TopStatsSummary(
        double TasksCompletionPercentage,
        int CompletedKappaTasks,
        int TotalKappaTasks,
        double HideoutItemsPercentage
    );

    /// <summary>
    /// Contract item for ranked "Up next" / relevant task card (F18).
    /// </summary>
    public record UpNextTaskItem(
        string QuestId,
        string QuestName,
        string Trader,
        string? Map,
        int UnlocksNext,
        int RemainingObjectives,
        string ReasonText,      // e.g. "Unlocks next: 3 | 1 left" or "Unlocks Jaeger · 1 left"
        int PriorityScore
    ) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string BadgeType { get; init; } = "ready"; // unlock-trader, opens, lightkeeper, kappa, almost-done, ready
        public string BadgeText { get; init; } = "";
        public bool IsKappaRequired { get; init; }
        public bool IsLightkeeperRequired { get; init; }

        private ImageSource? _avatarSource;
        public ImageSource? AvatarSource
        {
            get => _avatarSource;
            set
            {
                if (_avatarSource != value)
                {
                    _avatarSource = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FallbackVisibility));
                    OnPropertyChanged(nameof(AvatarVisibility));
                }
            }
        }
        public Visibility FallbackVisibility => AvatarSource != null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility AvatarVisibility => AvatarSource != null ? Visibility.Visible : Visibility.Collapsed;
        public string TraderInitial => !string.IsNullOrEmpty(Trader) && Trader.Length >= 2 ? Trader[..2].ToUpperInvariant() : "TR";
    }

    /// <summary>
    /// Contract view model for trader progress card in the 11-trader grid (F19).
    /// </summary>
    public record TraderProgressCardViewModel(
        string TraderId,
        string DisplayName,
        string AvatarUrl,
        int CurrentLoyaltyLevel, // 1..4
        double CurrentReputation,
        double NextLevelRequiredRep,
        int CompletedTasks,
        int TotalTasks,
        bool IsLocked,
        string? UnlockRequirementText
    ) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private ImageSource? _avatarSource;
        public ImageSource? AvatarSource
        {
            get => _avatarSource;
            set
            {
                if (_avatarSource != value)
                {
                    _avatarSource = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FallbackVisibility));
                    OnPropertyChanged(nameof(AvatarVisibility));
                }
            }
        }

        public Visibility FallbackVisibility => AvatarSource != null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility AvatarVisibility => AvatarSource != null ? Visibility.Visible : Visibility.Collapsed;

        public double ProgressPercentage
        {
            get => TotalTasks > 0 ? (double)CompletedTasks * 100.0 / TotalTasks : 0.0;
            set { }
        }
        public string ProgressText => $"{CompletedTasks}/{TotalTasks}";
        public string FallbackInitials { get; init; } = "";
        public bool IsFence => string.Equals(TraderId, "fence", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(DisplayName, "Fence", StringComparison.OrdinalIgnoreCase);
        public string RepDisplayText => IsFence
            ? CurrentReputation.ToString("F2", CultureInfo.InvariantCulture)
            : (CurrentReputation >= 0 ? "+" : "") + CurrentReputation.ToString("F2", CultureInfo.InvariantCulture);
        public string NextRepText => IsFence
            ? $"Karma: {CurrentReputation:F2} / 6.00"
            : (CurrentLoyaltyLevel >= 4 ? "MAX LL" : $"Next LL: {CurrentReputation:F2} / {NextLevelRequiredRep:F2}");
        public double NextRepProgressPercentage
        {
            get
            {
                if (CurrentLoyaltyLevel >= 4) return 100.0;
                if (NextLevelRequiredRep <= 0) return 0.0;
                return Math.Clamp(CurrentReputation / NextLevelRequiredRep * 100.0, 0.0, 100.0);
            }
            set { }
        }
        public bool HasLoyaltyLevels => !IsFence &&
                               !string.Equals(TraderId, "lightkeeper", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(DisplayName, "Lightkeeper", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(TraderId, "btr-driver", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(TraderId, "btr", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(DisplayName, "BTR Driver", StringComparison.OrdinalIgnoreCase);
        public bool HasNoLoyaltyLevels => !IsFence && !HasLoyaltyLevels;
        public bool IsCrown => HasLoyaltyLevels && CurrentLoyaltyLevel >= 4;

        public bool IsBtrDriver => string.Equals(TraderId, "btr", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(TraderId, "btr-driver", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(DisplayName, "BTR Driver", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(TraderId, "656f0f98d80a697f855d34b1", StringComparison.OrdinalIgnoreCase);

        public bool IsSpecialTraderWithoutToggle => HasNoLoyaltyLevels && !IsBtrDriver;

        public string BtrToggleIcon => IsLocked ? "🔓" : "✓";
        public string BtrToggleText => IsLocked ? "Unlock BTR" : "✓ Active (Pause)";
        public string BtrToggleToolTip => IsLocked
            ? "Click to manually unlock BTR Driver (or complete 'A Helping Hand')"
            : "BTR Driver active. Click to lock or pause.";

        private static readonly Brush BtrLockedBg = new SolidColorBrush(Color.FromArgb(0x55, 0x5E, 0x2A, 0x2A));
        private static readonly Brush BtrLockedBorder = new SolidColorBrush(Color.FromRgb(0x8E, 0x3A, 0x3A));
        private static readonly Brush BtrLockedFg = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80));

        private static readonly Brush BtrUnlockedBg = new SolidColorBrush(Color.FromArgb(0x40, 0x4C, 0xAF, 0x50));
        private static readonly Brush BtrUnlockedBorder = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly Brush BtrUnlockedFg = new SolidColorBrush(Color.FromRgb(0x81, 0xC7, 0x84));

        static TraderProgressCardViewModel()
        {
            BtrLockedBg.Freeze();
            BtrLockedBorder.Freeze();
            BtrLockedFg.Freeze();
            BtrUnlockedBg.Freeze();
            BtrUnlockedBorder.Freeze();
            BtrUnlockedFg.Freeze();
        }

        public Brush BtrToggleBackground => IsLocked ? BtrLockedBg : BtrUnlockedBg;
        public Brush BtrToggleBorderBrush => IsLocked ? BtrLockedBorder : BtrUnlockedBorder;
        public Brush BtrToggleForeground => IsLocked ? BtrLockedFg : BtrUnlockedFg;
    }

    /// <summary>
    /// Primary view model for the TarkovTracker-aligned dedicated Dashboard view (F16-F19).
    /// Orchestrates top stats metrics, impact-ranked Up Next queue, and 11-trader progression grid.
    /// </summary>
    public class DashboardViewModel : INotifyPropertyChanged
    {
        private TopStatsSummary _topStats = new(0, 0, 0, 0);
        private List<UpNextTaskItem> _upNextTasks = new();
        private List<TraderProgressCardViewModel> _traderCards = new();
        private bool _isLoading;

        private int _completedTasksCount;
        private int _totalTasksCount;
        private int _completedHideoutItems;
        private int _totalHideoutItems;

        public event PropertyChangedEventHandler? PropertyChanged;

        public TopStatsSummary TopStats
        {
            get => _topStats;
            set
            {
                _topStats = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TasksPercentageText));
                OnPropertyChanged(nameof(KappaText));
                OnPropertyChanged(nameof(KappaPercentageText));
                OnPropertyChanged(nameof(HideoutPercentageText));
                OnPropertyChanged(nameof(IsKappaReady));
            }
        }

        public List<UpNextTaskItem> UpNextTasks
        {
            get => _upNextTasks;
            set { _upNextTasks = value; OnPropertyChanged(); }
        }

        public List<TraderProgressCardViewModel> TraderCards
        {
            get => _traderCards;
            set { _traderCards = value; OnPropertyChanged(); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public int CompletedTasksCount
        {
            get => _completedTasksCount;
            set { _completedTasksCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(TasksCountText)); }
        }

        public int TotalTasksCount
        {
            get => _totalTasksCount;
            set { _totalTasksCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(TasksCountText)); }
        }

        public int CompletedHideoutItems
        {
            get => _completedHideoutItems;
            set { _completedHideoutItems = value; OnPropertyChanged(); OnPropertyChanged(nameof(HideoutCountText)); }
        }

        public int TotalHideoutItems
        {
            get => _totalHideoutItems;
            set { _totalHideoutItems = value; OnPropertyChanged(); OnPropertyChanged(nameof(HideoutCountText)); }
        }

        public string TasksPercentageText => $"{TopStats.TasksCompletionPercentage:F0}%";
        public string TasksCountText => $"{CompletedTasksCount}/{TotalTasksCount} tasks";
        public string KappaText => $"Kappa {TopStats.CompletedKappaTasks}/{TopStats.TotalKappaTasks}";
        public string KappaPercentageText => TopStats.TotalKappaTasks > 0
            ? $"{(int)Math.Floor((double)TopStats.CompletedKappaTasks * 100 / TopStats.TotalKappaTasks)}%"
            : "0%";
        public string HideoutPercentageText => $"{TopStats.HideoutItemsPercentage:F0}%";
        public string HideoutCountText => $"{CompletedHideoutItems}/{TotalHideoutItems} barter items";
        public bool IsKappaReady => TopStats.CompletedKappaTasks == TopStats.TotalKappaTasks && TopStats.TotalKappaTasks > 0;

        private static readonly HashSet<string> CurrencyKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "roubles", "dollars", "euros", "5449016a4bdc2d6f028b456f", "5696686a4bdc2da3298b456a", "569668774bdc2da2298b4568"
        };

        private static bool IsCurrency(string? name) => !string.IsNullOrEmpty(name) && CurrencyKeys.Contains(name);

        private static readonly (string Id, string Name, string Key, string FallbackInitials, string? UnlockQuest)[] TraderSpecs = new[]
        {
            ("54cb50c76803fa8b248b4571", "Prapor", "prapor", "PR", (string?)null),
            ("54cb57776803fa99248b456e", "Therapist", "therapist", "TH", null),
            ("579dc571d53a0658a154fbec", "Fence", "fence", "FE", null),
            ("58330581ace78e27b8b10cee", "Skier", "skier", "SK", null),
            ("5935c25fb3acc3127c3d8cd9", "Peacekeeper", "peacekeeper", "PK", "Requires Friend from the West - Part 2"),
            ("5a7c2eca46aef81a7ca2145d", "Mechanic", "mechanic", "ME", null),
            ("5ac3b934156ae10c4430e83c", "Ragman", "ragman", "RA", null),
            ("5c0647fdd443bc2504c2d371", "Jaeger", "jaeger", "JG", "Requires Introduction"),
            ("6617beeaa9cfa777ca915b7c", "Ref", "ref", "RF", "Requires Easy Money - Part 1"),
            ("638f541a29ffd1183d187f57", "Lightkeeper", "lightkeeper", "LK", "Requires Getting Acquainted"),
            ("656f0f98d80a697f855d34b1", "BTR Driver", "btr", "BTR", "Requires A Helping Hand")
        };

        public DashboardViewModel()
        {
        }

        /// <summary>
        /// Calculates top stats summary following TarkovTracker's mathematical floor rules (F17).
        /// </summary>
        public static TopStatsSummary CalculateTopStats(
            int completedTasks, int totalTasks,
            int completedKappa, int totalKappa,
            int completedHideoutItems, int totalHideoutItems)
        {
            double taskPct = totalTasks > 0 ? Math.Floor((double)completedTasks * 100.0 / totalTasks) : 0.0;
            double hideoutPct = totalHideoutItems > 0 ? Math.Floor((double)completedHideoutItems * 100.0 / totalHideoutItems) : 0.0;

            return new TopStatsSummary(taskPct, completedKappa, totalKappa, hideoutPct);
        }

        /// <summary>
        /// Ranks available tasks by unlock impact, sole-blockers (opensCount), and TarkovTracker priority formula (F18).
        /// </summary>
        public static List<UpNextTaskItem> RankUpNextTasks(
            IEnumerable<TarkovTask> availableTasks,
            ITaskImpactService impactEngine,
            int maxCount = 10)
        {
            var items = new List<UpNextTaskItem>();

            foreach (var task in availableTasks)
            {
                var impact = impactEngine.GetTaskImpact(task.NormalizedName ?? task.Id ?? string.Empty);
                int remaining = task.Objectives?.Count ?? 1;

                string reasonText;
                string badgeType;
                string badgeText;

                if (impact.UnlocksTrader)
                {
                    reasonText = $"Unlocks {impact.UnlockedTraderName ?? "Trader"} · {remaining} left";
                    badgeType = "unlock-trader";
                    badgeText = $"Unlocks {impact.UnlockedTraderName ?? "Trader"}";
                }
                else if (impact.OpensCount > 0)
                {
                    reasonText = $"Unlocks next: {impact.OpensCount} | {remaining} left";
                    badgeType = "opens";
                    badgeText = $"Opens {impact.OpensCount}";
                }
                else if (remaining == 0)
                {
                    reasonText = "Ready to turn in";
                    badgeType = "ready";
                    badgeText = "Ready";
                }
                else if (remaining == 1)
                {
                    reasonText = "Almost done · 1 left";
                    badgeType = "almost-done";
                    badgeText = "Almost Done";
                }
                else
                {
                    reasonText = $"In progress · {remaining} left";
                    badgeType = "ready";
                    badgeText = $"{remaining} left";
                }

                if (string.Equals(task.Trader, "Lightkeeper", StringComparison.OrdinalIgnoreCase))
                {
                    badgeType = "lightkeeper";
                    badgeText = "Lightkeeper";
                }
                else if (task.ReqKappa && badgeType != "unlock-trader" && badgeType != "opens")
                {
                    badgeType = "kappa";
                    badgeText = "Kappa";
                }

                ImageSource? avatarSource = null;
                if (!string.IsNullOrEmpty(task.Trader))
                {
                    var traderObj = TraderDbService.Instance.GetTraderByName(task.Trader);
                    if (traderObj != null)
                    {
                        avatarSource = ImageCacheService.Instance.GetLocalItemIcon(traderObj.Id);
                    }
                }

                items.Add(new UpNextTaskItem(
                    task.Id ?? task.NormalizedName ?? string.Empty,
                    task.Name ?? task.NormalizedName ?? string.Empty,
                    task.Trader ?? "Unknown",
                    task.Maps?.FirstOrDefault() ?? "Any",
                    impact.UnlocksNext,
                    remaining,
                    reasonText,
                    impact.PriorityScore
                )
                {
                    BadgeType = badgeType,
                    BadgeText = badgeText,
                    IsKappaRequired = task.ReqKappa,
                    IsLightkeeperRequired = string.Equals(task.Trader, "Lightkeeper", StringComparison.OrdinalIgnoreCase),
                    AvatarSource = avatarSource
                });
            }

            return items
                .OrderByDescending(i => i.PriorityScore)
                .ThenBy(i => i.QuestName, StringComparer.OrdinalIgnoreCase)
                .Take(maxCount)
                .ToList();
        }

        /// <summary>
        /// Loads live profile data and computes all dashboard sections asynchronously.
        /// </summary>
        public async Task LoadLiveDashboardDataAsync()
        {
            if (IsLoading) return;
            IsLoading = true;

            try
            {
                await Task.Run(() =>
                {
                    var progress = QuestProgressService.Instance;
                    var allTasks = progress.AllTasks;
                    if (allTasks.Count == 0 && QuestDbService.Instance.IsLoaded)
                    {
                        allTasks = QuestDbService.Instance.AllQuests.ToList();
                    }

                    // 1. Top stats calculation
                    int totalTasks = allTasks.Count;
                    int completedTasks = allTasks.Count(t => progress.GetStatus(t) == QuestStatus.Done);
                    int totalKappa = allTasks.Count(t => t.ReqKappa);
                    int completedKappa = allTasks.Count(t => t.ReqKappa && progress.GetStatus(t) == QuestStatus.Done);

                    // Hideout barter item progression
                    int completedHideout = 0;
                    int totalHideout = 0;
                    var hideoutModules = HideoutProgressService.Instance.AllModules;
                    if (hideoutModules != null && hideoutModules.Count > 0)
                    {
                        foreach (var module in hideoutModules)
                        {
                            var currentLevel = HideoutProgressService.Instance.GetCurrentLevel(module);
                            if (module.Levels == null) continue;
                            foreach (var level in module.Levels)
                            {
                                if (level.ItemRequirements == null) continue;
                                foreach (var req in level.ItemRequirements)
                                {
                                    if (IsCurrency(req.ItemNormalizedName)) continue;
                                    int required = req.Count;
                                    totalHideout += required;
                                    if (currentLevel >= level.Level)
                                    {
                                        completedHideout += required;
                                    }
                                    else
                                    {
                                        int collected = NeededItemProgressService.Instance.GetCount(req.ItemId ?? req.ItemNormalizedName);
                                        completedHideout += Math.Min(required, collected);
                                    }
                                }
                            }
                        }
                    }

                    CompletedTasksCount = completedTasks;
                    TotalTasksCount = totalTasks;
                    CompletedHideoutItems = completedHideout;
                    TotalHideoutItems = totalHideout;

                    TopStats = CalculateTopStats(completedTasks, totalTasks, completedKappa, totalKappa, completedHideout, totalHideout);

                    // 2. Up Next Tasks
                    var availableTasks = allTasks.Where(t => progress.GetStatus(t) == QuestStatus.Active).ToList();
                    if (availableTasks.Count == 0 && allTasks.Count > 0)
                    {
                        availableTasks = allTasks.Where(t => (t.Previous == null || t.Previous.Count == 0) && progress.GetStatus(t) != QuestStatus.Done).ToList();
                    }

                    UpNextTasks = RankUpNextTasks(availableTasks, TaskImpactEngine.Instance, 6);
                    _ = LoadUpNextAvatarsAsync(UpNextTasks);

                    // 3. Trader Progress Cards for all 11 traders
                    var cards = BuildTraderCards(progress, allTasks);
                    TraderCards = cards;
                    _ = LoadTraderAvatarsAsync(cards);
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static async Task LoadUpNextAvatarsAsync(List<UpNextTaskItem> items)
        {
            foreach (var item in items)
            {
                try
                {
                    if (item.AvatarSource == null && !string.IsNullOrEmpty(item.Trader))
                    {
                        var traderObj = TraderDbService.Instance.GetTraderByName(item.Trader);
                        if (traderObj != null)
                        {
                            var img = ImageCacheService.Instance.GetLocalItemIcon(traderObj.Id);
                            if (img == null && !string.IsNullOrEmpty(traderObj.ImageLink))
                            {
                                img = await ImageCacheService.Instance.GetImageAsync(traderObj.ImageLink, "traders");
                            }
                            if (img != null)
                            {
                                if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                                {
                                    Application.Current.Dispatcher.Invoke(() => item.AvatarSource = img);
                                }
                                else
                                {
                                    item.AvatarSource = img;
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private static async Task LoadTraderAvatarsAsync(List<TraderProgressCardViewModel> cards)
        {
            foreach (var card in cards)
            {
                try
                {
                    if (!string.IsNullOrEmpty(card.AvatarUrl))
                    {
                        var bitmap = await ImageCacheService.Instance.GetImageAsync(card.AvatarUrl, "traders");
                        if (bitmap != null)
                        {
                            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                            {
                                Application.Current.Dispatcher.Invoke(() => card.AvatarSource = bitmap);
                            }
                            else
                            {
                                card.AvatarSource = bitmap;
                            }
                        }
                    }
                }
                catch
                {
                    // Fallback initials remain visible
                }
            }
        }

        private static List<TraderProgressCardViewModel> BuildTraderCards(QuestProgressService progress, IEnumerable<TarkovTask> allTasks)
        {
            var taskList = allTasks.ToList();
            var cards = new List<TraderProgressCardViewModel>();

            foreach (var spec in TraderSpecs)
            {
                bool isLocked = false;
                string? unlockText = null;

                if (spec.Name == "Jaeger")
                {
                    isLocked = !progress.IsJaegerUnlocked();
                    unlockText = isLocked ? "Requires Introduction" : null;
                }
                else if (spec.Name == "Peacekeeper")
                {
                    isLocked = !progress.IsPeacekeeperUnlocked();
                    unlockText = isLocked ? "Requires Friend from the West - Part 2" : null;
                }
                else if (spec.Name == "Ref")
                {
                    isLocked = !progress.IsRefUnlocked();
                    unlockText = isLocked ? "Requires Easy Money - Part 1" : null;
                }
                else if (spec.Name == "Lightkeeper")
                {
                    isLocked = !progress.IsLightkeeperUnlocked();
                    unlockText = isLocked ? "Requires Getting Acquainted" : null;
                }
                else if (spec.Name == "BTR Driver")
                {
                    isLocked = !progress.IsBtrDriverUnlocked();
                    unlockText = isLocked ? "Requires A Helping Hand" : null;
                }

                int loyaltyLevel = progress.GetTraderLoyaltyLevel(spec.Name);
                if (loyaltyLevel < 1) loyaltyLevel = 1;
                if (loyaltyLevel > 4) loyaltyLevel = 4;

                double currentRep = 0.0;
                double nextRequiredRep = 0.0;

                if (string.Equals(spec.Key, "fence", StringComparison.OrdinalIgnoreCase))
                {
                    currentRep = SettingsService.Instance.ScavRep;
                    nextRequiredRep = 6.00;
                }

                // Tasks count for trader
                var traderTasks = taskList.Where(t =>
                    string.Equals(t.Trader, spec.Name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(QuestProgressService.ResolveTraderFriendlyName(t.Trader ?? string.Empty), spec.Name, StringComparison.OrdinalIgnoreCase)
                ).ToList();

                int total = traderTasks.Count;
                int completed = traderTasks.Count(t => progress.GetStatus(t) == QuestStatus.Done);

                string avatarUrl = $"https://assets.tarkov.dev/{spec.Id}.webp";

                cards.Add(new TraderProgressCardViewModel(
                    spec.Key,
                    spec.Name,
                    avatarUrl,
                    loyaltyLevel,
                    currentRep,
                    nextRequiredRep,
                    completed,
                    total,
                    isLocked,
                    unlockText
                )
                {
                    FallbackInitials = spec.FallbackInitials
                });
            }

            return cards;
        }

        public void SetTraderLoyaltyLevel(string traderName, int level)
        {
            QuestProgressService.Instance.SetTraderLoyaltyLevelOverride(traderName, level);
            _ = LoadLiveDashboardDataAsync();
        }

        public void AdjustScavKarma(double delta)
        {
            var current = SettingsService.Instance.ScavRep;
            var updated = Math.Round(Math.Clamp(current + delta, SettingsService.MinScavRep, SettingsService.MaxScavRep), 2);
            SettingsService.Instance.ScavRep = updated;
            _ = LoadLiveDashboardDataAsync();
        }

        public void ToggleBtrDriver()
        {
            bool current = QuestProgressService.Instance.IsBtrDriverUnlocked();
            QuestProgressService.Instance.SetBtrDriverUnlocked(!current);
            _ = LoadLiveDashboardDataAsync();
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
