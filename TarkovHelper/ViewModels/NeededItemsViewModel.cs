using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TarkovHelper.Models;
using TarkovHelper.Services;

using System.Collections.Specialized;

namespace TarkovHelper.ViewModels
{
    /// <summary>
    /// ObservableCollection supporting single-notification batch replacement and range additions.
    /// Prevents 997 individual layout invalidation passes when updating large card lists.
    /// </summary>
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotification;

        public void ReplaceAll(IEnumerable<T> items)
        {
            if (items == null) return;
            _suppressNotification = true;
            try
            {
                Items.Clear();
                foreach (var item in items)
                {
                    Items.Add(item);
                }
            }
            finally
            {
                _suppressNotification = false;
            }
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void AddRange(IEnumerable<T> items)
        {
            if (items == null) return;
            var list = items.ToList();
            if (list.Count == 0) return;

            foreach (var item in list)
            {
                Add(item);
            }
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotification)
                base.OnCollectionChanged(e);
        }
    }
    /// <summary>
    /// Contract view model for an individual needed item card in the TarkovTracker-aligned Needed Items grid.
    /// Supports grouping identical items per quest/station, interactive steppers, FIR status, and trader avatars.
    /// </summary>
    public class NeededItemCardViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public string NeedKey { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public string ItemNormalizedName { get; set; } = string.Empty;
        public string ItemDisplayName { get; set; } = string.Empty;
        public string SubtitleName { get; set; } = string.Empty;
        public Visibility SubtitleVisibility => !string.IsNullOrEmpty(SubtitleName) ? Visibility.Visible : Visibility.Collapsed;

        public string SourceType { get; set; } = "Quest"; // "Quest" or "Hideout"
        public bool IsQuest => SourceType == "Quest";
        public bool IsHideout => SourceType == "Hideout";

        public string SourceName { get; set; } = string.Empty;      // Quest Title or Station Name
        public string SourceSubtitle { get; set; } = string.Empty;  // Trader Name or Station Level
        public string? TraderName { get; set; }
        public string? QuestNormalizedName { get; set; }
        public string? StationId { get; set; }
        public int StationLevel { get; set; }

        public bool FoundInRaid { get; set; }
        public Visibility FirVisibility => FoundInRaid ? Visibility.Visible : Visibility.Collapsed;

        public bool IsSpecialEquipment { get; set; } // Placeable task gear: MS2000 Marker, Wi-Fi Camera, Signal Jammer
        public bool IsKappaRequired { get; set; }    // Required for Collector / Kappa container
        public string? Category { get; set; }

        public int? DogtagMinLevel { get; set; }
        public bool HasDogtagLevel => DogtagMinLevel.HasValue && DogtagMinLevel.Value > 0;
        public string DogtagDisplay => HasDogtagLevel ? $"Lv.{DogtagMinLevel}+" : string.Empty;
        public Visibility DogtagVisibility => HasDogtagLevel ? Visibility.Visible : Visibility.Collapsed;

        public int PriorityScore { get; set; }

        private int _amount;
        public int Amount
        {
            get => _amount;
            set
            {
                if (_amount != value)
                {
                    _amount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProgressText));
                    OnPropertyChanged(nameof(IsComplete));
                    OnPropertyChanged(nameof(CardOpacity));
                    OnPropertyChanged(nameof(StatusBrush));
                }
            }
        }

        private int _currentCount;
        public int CurrentCount
        {
            get => _currentCount;
            set
            {
                if (_currentCount != value)
                {
                    _currentCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProgressText));
                    OnPropertyChanged(nameof(IsComplete));
                    OnPropertyChanged(nameof(CardOpacity));
                    OnPropertyChanged(nameof(StatusBrush));
                }
            }
        }

        public string ProgressText => $"{CurrentCount}/{Amount}";
        public bool IsComplete => CurrentCount >= Amount && Amount > 0;
        public double CardOpacity => IsComplete ? 0.45 : 1.0;

        public Brush StatusBrush => IsComplete ? BrushComplete : BrushIncomplete;
        private static readonly SolidColorBrush BrushComplete = new(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly SolidColorBrush BrushIncomplete = new(Color.FromRgb(0xF5, 0x9E, 0x0B));

        private BitmapImage? _iconSource;
        public BitmapImage? IconSource
        {
            get => _iconSource;
            set { _iconSource = value; OnPropertyChanged(); }
        }

        private ImageSource? _traderAvatar;
        public ImageSource? TraderAvatar
        {
            get => _traderAvatar;
            set { _traderAvatar = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTraderAvatar)); }
        }

        public bool HasTraderAvatar => _traderAvatar != null;
        public string TraderInitial => !string.IsNullOrEmpty(TraderName) && TraderName.Length >= 2
            ? TraderName[..2].ToUpperInvariant()
            : (IsHideout ? "HI" : "TR");

        public void Increment()
        {
            if (string.IsNullOrEmpty(NeedKey)) return;
            CurrentCount = NeededItemProgressService.Instance.Increment(NeedKey, Amount);
        }

        public void Decrement()
        {
            if (string.IsNullOrEmpty(NeedKey)) return;
            CurrentCount = NeededItemProgressService.Instance.Decrement(NeedKey, Amount);
        }

        public void ToggleComplete()
        {
            if (string.IsNullOrEmpty(NeedKey)) return;
            CurrentCount = NeededItemProgressService.Instance.ToggleComplete(NeedKey, Amount);
        }
    }

    /// <summary>
    /// View model for the TarkovTracker-aligned Needed Items view (Foto 3 & Foto 6).
    /// Manages card collection, active tab filtering (ALL, TASKS, HIDEOUT, COMPLETED),
    /// multi-faceted filter popup, and responsive search/sort.
    /// </summary>
    public class NeededItemsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private static List<NeededItemCardViewModel>? _prewarmedCardsCache;
        private static readonly object _prewarmLock = new();

        public static void PrewarmCache()
        {
            try
            {
                var vm = new NeededItemsViewModel();
                var built = vm.BuildNeededItemCards();
                lock (_prewarmLock)
                {
                    _prewarmedCardsCache = built;
                }
            }
            catch { }
        }

        public static void InvalidatePrewarmCache()
        {
            lock (_prewarmLock)
            {
                _prewarmedCardsCache = null;
            }
        }

        private readonly List<NeededItemCardViewModel> _allCards = new();
        public BulkObservableCollection<NeededItemCardViewModel> FilteredCards { get; } = new();
        public BulkObservableCollection<NeededItemCardViewModel> DisplayedCards { get; } = new();
        private const int InitialDisplayBatchSize = 48;
        private const int ScrollBatchSize = 48;
        private int _displayedCount;

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        // Top Filter Tabs (ALL, TASKS, HIDEOUT, KAPPA, COMPLETED)
        private string _activeTab = "All"; // "All", "Tasks", "Hideout", "Kappa", "Completed"
        public string ActiveTab
        {
            get => _activeTab;
            set
            {
                if (_activeTab != value)
                {
                    _activeTab = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsTabAll));
                    OnPropertyChanged(nameof(IsTabTasks));
                    OnPropertyChanged(nameof(IsTabHideout));
                    OnPropertyChanged(nameof(IsTabKappa));
                    OnPropertyChanged(nameof(IsTabCompleted));
                    ApplyFilters();
                }
            }
        }

        public bool IsTabAll
        {
            get => _activeTab == "All";
            set { if (value) ActiveTab = "All"; }
        }

        public bool IsTabTasks
        {
            get => _activeTab == "Tasks";
            set { if (value) ActiveTab = "Tasks"; }
        }

        public bool IsTabHideout
        {
            get => _activeTab == "Hideout";
            set { if (value) ActiveTab = "Hideout"; }
        }

        public bool IsTabKappa
        {
            get => _activeTab == "Kappa";
            set { if (value) ActiveTab = "Kappa"; }
        }

        public bool IsTabCompleted
        {
            get => _activeTab == "Completed";
            set { if (value) ActiveTab = "Completed"; }
        }

        /// <summary>
        /// Selects an active tab ("All", "Tasks", "Hideout", "Kappa", "Completed").
        /// </summary>
        public void SelectTab(string tab)
        {
            if (string.IsNullOrWhiteSpace(tab)) return;
            ActiveTab = tab;
        }

        // Tab Counts
        private int _allCount;
        public int AllCount { get => _allCount; private set { _allCount = value; OnPropertyChanged(); } }

        private int _tasksCount;
        public int TasksCount { get => _tasksCount; private set { _tasksCount = value; OnPropertyChanged(); } }

        private int _hideoutCount;
        public int HideoutCount { get => _hideoutCount; private set { _hideoutCount = value; OnPropertyChanged(); } }

        private int _kappaCount;
        public int KappaCount { get => _kappaCount; private set { _kappaCount = value; OnPropertyChanged(); } }

        private int _completedCount;
        public int CompletedCount { get => _completedCount; private set { _completedCount = value; OnPropertyChanged(); } }

        public string FilteredCountText => $"{FilteredCards.Count} items";

        // Search text
        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        // Popup Filters (Foto 6)
        // 1. Found in Raid: "All", "FIR", "Non-FIR"
        private string _firFilter = "All";
        public string FirFilter
        {
            get => _firFilter;
            set
            {
                if (_firFilter != value)
                {
                    _firFilter = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsFirAll));
                    OnPropertyChanged(nameof(IsFirOnly));
                    OnPropertyChanged(nameof(IsFirNon));
                    ApplyFilters();
                }
            }
        }
        public bool IsFirAll => _firFilter == "All";
        public bool IsFirOnly => _firFilter == "FIR";
        public bool IsFirNon => _firFilter == "Non-FIR";

        // 2. Hide: Items I already have
        private bool _hideOwned;
        public bool HideOwned
        {
            get => _hideOwned;
            set
            {
                if (_hideOwned != value)
                {
                    _hideOwned = value;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        // 3. Hide: Non-FIR special equipment (placeable task gear like MS2000, Wi-Fi Camera)
        private bool _hideSpecialEquipment;
        public bool HideSpecialEquipment
        {
            get => _hideSpecialEquipment;
            set
            {
                if (_hideSpecialEquipment != value)
                {
                    _hideSpecialEquipment = value;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        // 4. Tasks: Kappa tasks only
        private bool _kappaOnly;
        public bool KappaOnly
        {
            get => _kappaOnly;
            set
            {
                if (_kappaOnly != value)
                {
                    _kappaOnly = value;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        // Sort: "Priority", "Name", "Trader", "Quantity"
        private string _sortMode = "Priority";
        public string SortMode
        {
            get => _sortMode;
            set
            {
                if (_sortMode != value)
                {
                    _sortMode = value;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        public void ResetFilters()
        {
            _firFilter = "All";
            _hideOwned = false;
            _hideSpecialEquipment = false;
            _kappaOnly = false;
            _sortMode = "Priority";

            OnPropertyChanged(nameof(FirFilter));
            OnPropertyChanged(nameof(IsFirAll));
            OnPropertyChanged(nameof(IsFirOnly));
            OnPropertyChanged(nameof(IsFirNon));
            OnPropertyChanged(nameof(HideOwned));
            OnPropertyChanged(nameof(HideSpecialEquipment));
            OnPropertyChanged(nameof(KappaOnly));
            OnPropertyChanged(nameof(SortMode));

            ApplyFilters();
        }

        internal void SetCardsForTesting(IEnumerable<NeededItemCardViewModel> cards)
        {
            lock (_allCards)
            {
                _allCards.Clear();
                _allCards.AddRange(cards);
            }
            UpdateCounts();
            ApplyFilters();
        }

        public async Task LoadCardsAsync(bool forceRebuild = false)
        {
            IsLoading = true;
            try
            {
                List<NeededItemCardViewModel>? prewarmed = null;
                if (!forceRebuild)
                {
                    lock (_prewarmLock)
                    {
                        if (_prewarmedCardsCache != null && _prewarmedCardsCache.Count > 0)
                        {
                            prewarmed = _prewarmedCardsCache;
                            _prewarmedCardsCache = null;
                        }
                    }
                }

                if (prewarmed != null)
                {
                    lock (_allCards)
                    {
                        _allCards.Clear();
                        _allCards.AddRange(prewarmed);
                    }
                }
                else
                {
                    await Task.Run(() =>
                    {
                        var cards = BuildNeededItemCards();
                        lock (_allCards)
                        {
                            _allCards.Clear();
                            _allCards.AddRange(cards);
                        }
                    });
                }

                UpdateCounts();
                ApplyFilters();
                _ = LoadImagesAsync();
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static readonly HashSet<string> CurrencyKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "roubles", "dollars", "euros", "5449016a4bdc2d6f028b456f", "5696686a4bdc2da3298b456a", "569668774bdc2da2298b4568"
        };
        private static bool IsCurrency(string? name) => !string.IsNullOrEmpty(name) && CurrencyKeys.Contains(name);

        public static readonly HashSet<string> SpecialEquipmentNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "ms2000 marker", "ms2000_marker", "wi-fi camera", "wi-fi_camera", "wifi camera", "signal jammer", "signal_jammer",
            "5991b51486f77447b112d44f", "5b4391a586f7745321235ab2", "5ac78a9b86f7741cca0bbd8d"
        };

        private List<NeededItemCardViewModel> BuildNeededItemCards()
        {
            var result = new List<NeededItemCardViewModel>();
            var itemLookup = ItemDbService.Instance.GetItemLookup();
            var questProgress = QuestProgressService.Instance;
            var hideoutProgress = HideoutProgressService.Instance;
            var neededProgress = NeededItemProgressService.Instance;
            var lang = LocalizationService.Instance.CurrentLanguage;

            // Pre-cache trader avatars once (only ~11 traders exist)
            var traderAvatarMap = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
            var allTraders = TraderDbService.Instance.AllTraders;
            if (allTraders != null)
            {
                foreach (var tr in allTraders)
                {
                    if (string.IsNullOrEmpty(tr?.Name)) continue;
                    var tIcon = ImageCacheService.Instance.GetLocalItemIcon(tr.Id);
                    if (tIcon != null) traderAvatarMap[tr.Name] = tIcon;
                }
            }

            // 1. Quests Required Items
            var allTasks = questProgress.AllTasks;
            if (allTasks != null)
            {
                foreach (var task in allTasks)
                {
                    if (task == null) continue;
                    var status = questProgress.GetStatus(task);
                    if (status == QuestStatus.Done || status == QuestStatus.Failed || status == QuestStatus.Unavailable)
                        continue;

                    if (task.RequiredItems == null || task.RequiredItems.Count == 0)
                        continue;

                    // Group items by itemNormalizedName (and dogtag min level if present) for this task
                    var groupedQuestItems = new Dictionary<string, (int Amount, bool Fir, int? DogtagLevel, QuestItem First)>(StringComparer.OrdinalIgnoreCase);

                    foreach (var qi in task.RequiredItems)
                    {
                        if (qi == null || IsCurrency(qi.ItemNormalizedName)) continue;

                        var groupKey = qi.DogtagMinLevel.HasValue && qi.DogtagMinLevel.Value > 0
                            ? $"{qi.ItemNormalizedName}:dogtag:{qi.DogtagMinLevel.Value}"
                            : qi.ItemNormalizedName;

                        if (groupedQuestItems.TryGetValue(groupKey, out var existing))
                        {
                            groupedQuestItems[groupKey] = (existing.Amount + Math.Max(0, qi.Amount), existing.Fir || qi.FoundInRaid, existing.DogtagLevel, existing.First);
                        }
                        else
                        {
                            groupedQuestItems[groupKey] = (Math.Max(0, qi.Amount), qi.FoundInRaid, qi.DogtagMinLevel, qi);
                        }
                    }

                    foreach (var kvp in groupedQuestItems)
                    {
                        var (amount, fir, dogtagLevel, firstQi) = kvp.Value;
                        if (amount <= 0) continue;

                        var normName = firstQi.ItemNormalizedName;
                        itemLookup.TryGetValue(normName, out var itemInfo);

                        // Exclude pure quest mission items (e.g. golden pocket watch)
                        if (itemInfo != null && string.Equals(itemInfo.Category, "Quest Items", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var taskNormName = !string.IsNullOrWhiteSpace(task.NormalizedName) ? task.NormalizedName : (!string.IsNullOrWhiteSpace(task.Id) ? task.Id : "quest");
                        var needKey = dogtagLevel.HasValue && dogtagLevel.Value > 0
                            ? NeededItemProgressService.GetQuestItemKey(taskNormName, normName, dogtagLevel.Value)
                            : NeededItemProgressService.GetQuestItemKey(taskNormName, normName);

                        var questDisplayName = (lang == AppLanguage.KO && !string.IsNullOrEmpty(task.NameKo)) ? task.NameKo
                            : ((lang == AppLanguage.JA && !string.IsNullOrEmpty(task.NameJa)) ? task.NameJa : task.Name);

                        var itemDisplayName = itemInfo?.Name ?? firstQi.ItemDisplayName ?? normName;
                        var subtitle = (lang == AppLanguage.KO && !string.IsNullOrEmpty(itemInfo?.NameKo)) ? itemInfo.NameKo
                            : ((lang == AppLanguage.JA && !string.IsNullOrEmpty(itemInfo?.NameJa)) ? itemInfo.NameJa : string.Empty);

                        var trader = task.Trader ?? "Prapor";
                        var currentCount = neededProgress.GetCount(needKey);

                        bool isSpecial = (itemInfo != null && string.Equals(itemInfo.Category, "Special equipment", StringComparison.OrdinalIgnoreCase))
                            || SpecialEquipmentNames.Contains(normName) || SpecialEquipmentNames.Contains(itemInfo?.Id ?? "");

                        int priority = 0;
                        if (status == QuestStatus.Active) priority += 1000;
                        if (task.ReqKappa) priority += 400;
                        priority -= Math.Min(50, task.RequiredLevel ?? 0) * 5;

                        var localIcon = !string.IsNullOrEmpty(itemInfo?.Id)
                            ? ImageCacheService.Instance.GetLocalItemIcon(itemInfo.Id)
                            : ImageCacheService.Instance.GetLocalItemIcon(normName);
                        traderAvatarMap.TryGetValue(trader, out var traderAvatar);

                        result.Add(new NeededItemCardViewModel
                        {
                            NeedKey = needKey,
                            ItemId = itemInfo?.Id ?? normName,
                            ItemNormalizedName = normName,
                            ItemDisplayName = itemDisplayName,
                            SubtitleName = subtitle,
                            SourceType = "Quest",
                            SourceName = questDisplayName,
                            SourceSubtitle = trader,
                            TraderName = trader,
                            QuestNormalizedName = taskNormName,
                            FoundInRaid = fir,
                            IsSpecialEquipment = isSpecial,
                            IsKappaRequired = task.ReqKappa,
                            Category = itemInfo?.Category,
                            DogtagMinLevel = dogtagLevel,
                            Amount = amount,
                            CurrentCount = currentCount,
                            PriorityScore = priority,
                            IconSource = localIcon,
                            TraderAvatar = traderAvatar
                        });
                    }
                }
            }

            // 2. Hideout Module Items
            var allModules = hideoutProgress.AllModules;
            if (allModules != null)
            {
                foreach (var module in allModules)
                {
                    if (module?.Levels == null) continue;
                    var currentLevel = hideoutProgress.GetCurrentLevel(module);

                    foreach (var level in module.Levels.Where(l => l != null && l.Level > currentLevel))
                    {
                        if (level.ItemRequirements == null) continue;

                        var groupedHideoutItems = new Dictionary<string, (int Amount, bool Fir, HideoutItemRequirement First)>(StringComparer.OrdinalIgnoreCase);

                        foreach (var req in level.ItemRequirements)
                        {
                            if (req == null || IsCurrency(req.ItemNormalizedName)) continue;
                            if (groupedHideoutItems.TryGetValue(req.ItemNormalizedName, out var existing))
                            {
                                groupedHideoutItems[req.ItemNormalizedName] = (existing.Amount + Math.Max(0, req.Count), existing.Fir || req.FoundInRaid, existing.First);
                            }
                            else
                            {
                                groupedHideoutItems[req.ItemNormalizedName] = (Math.Max(0, req.Count), req.FoundInRaid, req);
                            }
                        }

                        foreach (var kvp in groupedHideoutItems)
                        {
                            var (count, fir, firstReq) = kvp.Value;
                            if (count <= 0) continue;

                            var normName = firstReq.ItemNormalizedName;
                            itemLookup.TryGetValue(normName, out var itemInfo);

                            var stationId = !string.IsNullOrWhiteSpace(module.Id) ? module.Id : module.NormalizedName;
                            var needKey = NeededItemProgressService.GetHideoutItemKey(stationId, level.Level, normName);

                            var itemDisplayName = itemInfo?.Name ?? firstReq.ItemName ?? normName;
                            var subtitle = (lang == AppLanguage.KO && !string.IsNullOrEmpty(itemInfo?.NameKo)) ? itemInfo.NameKo
                                : ((lang == AppLanguage.JA && !string.IsNullOrEmpty(itemInfo?.NameJa)) ? itemInfo.NameJa : string.Empty);

                            var currentCount = neededProgress.GetCount(needKey);
                            bool isSpecial = (itemInfo != null && string.Equals(itemInfo.Category, "Special equipment", StringComparison.OrdinalIgnoreCase))
                                || SpecialEquipmentNames.Contains(normName);

                            int priority = 200 - (level.Level - currentLevel) * 20;

                            var localHideoutIcon = !string.IsNullOrEmpty(itemInfo?.Id)
                                ? ImageCacheService.Instance.GetLocalItemIcon(itemInfo.Id)
                                : ImageCacheService.Instance.GetLocalItemIcon(normName);

                            result.Add(new NeededItemCardViewModel
                            {
                                NeedKey = needKey,
                                ItemId = itemInfo?.Id ?? normName,
                                ItemNormalizedName = normName,
                                ItemDisplayName = itemDisplayName,
                                SubtitleName = subtitle,
                                SourceType = "Hideout",
                                SourceName = module.Name,
                                SourceSubtitle = $"Level {level.Level}",
                                StationId = stationId,
                                StationLevel = level.Level,
                                FoundInRaid = fir,
                                IsSpecialEquipment = isSpecial,
                                IsKappaRequired = false,
                                Category = itemInfo?.Category,
                                Amount = count,
                                CurrentCount = currentCount,
                                PriorityScore = priority,
                                IconSource = localHideoutIcon
                            });
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Recalculates badge counters for All, Tasks, Hideout, Kappa, and Completed tabs.
        /// Kappa count aggregates quest cards where IsKappaRequired is true or QuestNormalizedName is "collector".
        /// </summary>
        public void CalculateCounts()
        {
            lock (_allCards)
            {
                AllCount = _allCards.Count;
                TasksCount = _allCards.Count(c => c.IsQuest);
                HideoutCount = _allCards.Count(c => c.IsHideout);
                KappaCount = _allCards.Count(c => c.IsQuest && (c.IsKappaRequired || string.Equals(c.QuestNormalizedName, "collector", StringComparison.OrdinalIgnoreCase)));
                CompletedCount = _allCards.Count(c => c.IsComplete);
            }
        }

        private void UpdateCounts() => CalculateCounts();

        public void ApplyFilters()
        {
            List<NeededItemCardViewModel> copy;
            lock (_allCards)
            {
                copy = _allCards.ToList();
            }

            var filtered = copy.Where(card =>
            {
                // Tab filter
                if (_activeTab == "Tasks" && !card.IsQuest) return false;
                if (_activeTab == "Hideout" && !card.IsHideout) return false;
                if (_activeTab == "Kappa" && (!card.IsQuest || (!card.IsKappaRequired && !string.Equals(card.QuestNormalizedName, "collector", StringComparison.OrdinalIgnoreCase)))) return false;
                if (_activeTab == "Completed" && !card.IsComplete) return false;

                // Search text filter
                if (!string.IsNullOrWhiteSpace(_searchText))
                {
                    var q = _searchText.Trim().ToLowerInvariant();
                    if (!card.ItemDisplayName.ToLowerInvariant().Contains(q) &&
                        !card.SubtitleName.ToLowerInvariant().Contains(q) &&
                        !card.SourceName.ToLowerInvariant().Contains(q) &&
                        !(card.TraderName?.ToLowerInvariant().Contains(q) ?? false))
                    {
                        return false;
                    }
                }

                // FIR Filter
                if (_firFilter == "FIR" && !card.FoundInRaid) return false;
                if (_firFilter == "Non-FIR" && card.FoundInRaid) return false;

                // Hide: Items I already have
                if (_hideOwned && card.IsComplete) return false;

                // Hide: Non-FIR special equipment
                if (_hideSpecialEquipment && !card.FoundInRaid && card.IsSpecialEquipment) return false;

                // Tasks: Kappa tasks only (from filter popup)
                if (_kappaOnly && (!card.IsQuest || (!card.IsKappaRequired && !string.Equals(card.QuestNormalizedName, "collector", StringComparison.OrdinalIgnoreCase)))) return false;

                return true;
            });

            // Sorting
            IEnumerable<NeededItemCardViewModel> sorted = _sortMode switch
            {
                "Name" => filtered.OrderBy(c => c.ItemDisplayName).ThenByDescending(c => c.PriorityScore),
                "Trader" => filtered.OrderBy(c => !string.IsNullOrEmpty(c.TraderName) ? c.TraderName : c.SourceSubtitle).ThenBy(c => c.SourceName),
                "Quantity" => filtered.OrderByDescending(c => c.Amount).ThenBy(c => c.ItemDisplayName),
                _ => filtered.OrderByDescending(c => c.PriorityScore).ThenBy(c => c.ItemDisplayName) // Default: Priority
            };

            var list = sorted.ToList();

            void UpdateCollections()
            {
                FilteredCards.ReplaceAll(list);
                _displayedCount = Math.Min(InitialDisplayBatchSize, list.Count);
                DisplayedCards.ReplaceAll(list.Take(_displayedCount));
                OnPropertyChanged(nameof(FilteredCountText));
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted && dispatcher.Thread.IsAlive && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(UpdateCollections);
            }
            else
            {
                UpdateCollections();
            }
        }

        public void LoadMoreDisplayedCards()
        {
            if (_displayedCount >= FilteredCards.Count) return;
            var nextBatch = FilteredCards.Skip(_displayedCount).Take(ScrollBatchSize).ToList();
            if (nextBatch.Count > 0)
            {
                _displayedCount += nextBatch.Count;
                DisplayedCards.AddRange(nextBatch);
            }
        }

        private async Task LoadImagesAsync()
        {
            List<NeededItemCardViewModel> cardsToLoad;
            lock (_allCards)
            {
                cardsToLoad = _allCards.Where(c => c.IconSource == null || (c.IsQuest && c.TraderAvatar == null)).ToList();
            }

            if (cardsToLoad.Count == 0) return;

            foreach (var card in cardsToLoad)
            {
                try
                {
                    // 1. Item icon
                    if (card.IconSource == null && !string.IsNullOrEmpty(card.ItemId))
                    {
                        var url = $"https://assets.tarkov.dev/{card.ItemId}.webp";
                        var icon = await ImageCacheService.Instance.GetImageAsync(url, "items");
                        if (icon != null)
                        {
                            RunOnUI(() => card.IconSource = icon);
                        }
                    }

                    // 2. Trader avatar (if quest)
                    if (card.IsQuest && card.TraderAvatar == null && !string.IsNullOrEmpty(card.TraderName))
                    {
                        var trader = TraderDbService.Instance.GetTraderByName(card.TraderName);
                        if (trader != null)
                        {
                            var avatarUrl = trader.ImageLink ?? $"https://assets.tarkov.dev/{trader.Id}.webp";
                            var avatar = await ImageCacheService.Instance.GetImageAsync(avatarUrl, "traders");
                            if (avatar != null)
                            {
                                RunOnUI(() => card.TraderAvatar = avatar);
                            }
                        }
                    }
                }
                catch
                {
                    // Fallback to initials / placeholders
                }
            }
        }

        private static void RunOnUI(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted && dispatcher.Thread.IsAlive && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(action);
            }
            else
            {
                action();
            }
        }
    }
}
