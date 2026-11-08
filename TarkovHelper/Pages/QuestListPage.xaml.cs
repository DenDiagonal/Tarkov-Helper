using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Services;
using TarkovHelper.Services.Map;
using TarkovHelper.ViewModels;
using System.Linq;
using TarkovHelper.Pages.Map;
using TarkovHelper.Pages.Map.Components;

namespace TarkovHelper.Pages
{
    public partial class QuestListPage : UserControl, IQuestMapIntegrationHost
    {
        private readonly LocalizationService _loc = LocalizationService.Instance;
        private readonly QuestProgressService _progressService = QuestProgressService.Instance;
        private readonly ImageCacheService _imageCache = ImageCacheService.Instance;
        private readonly ItemInventoryService _inventoryService = ItemInventoryService.Instance;
        private List<QuestViewModel> _allQuestViewModels = new();
        private List<string> _traders = new();
        private List<string> _maps = new();
        private Dictionary<string, TarkovItem>? _itemLookup;
        private bool _isInitializing = true;
        private bool _isDataLoaded = false;
        private bool _isUnloaded = false;
        private string? _pendingQuestSelection = null;
        private List<GuideImage>? _pendingGuideImages = null;
        private bool _guideImagesLoaded = false;
        private TarkovTask? _currentDetailTask = null;
        private MapPage? _embeddedMapPage;
        private List<QuestViewModel>? _lastFilteredQuests;

        // Navigation Mode & Chips (M2)
        private TasksViewMode _viewMode = TasksViewMode.Traders;
        private NavigationMode _navMode = NavigationMode.Maps;
        private TaskSortMode _sortMode = TaskSortMode.Impact;
        private SortDirection _sortDirection = SortDirection.Descending;
        private string _selectedTarget = string.Empty;
        private Point _lastGraphPanPoint;
        private bool _isGraphPanning = false;
        private double _currentGraphZoom = 1.0;
        private const double GraphMinZoom = 0.20;
        private const double GraphMaxZoom = 2.50;
        private QuestGraphLayoutResult? _activeGraphLayout;
        private string _activeGraphTraderOrMode = "Prapor";
        private QuestGraphNode? _selectedGraphNode;
        private readonly Dictionary<string, TarkovHelper.Controls.QuestGraphNodeControl> _nodeControlMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, System.Windows.Shapes.Path> _edgePathMap = new(StringComparer.OrdinalIgnoreCase);
        public ObservableCollection<NavChipItemViewModel> TraderChips { get; } = new();
        public ObservableCollection<NavChipItemViewModel> MapChips { get; } = new();
        private bool _isUpdatingSecondaryDropdown = false;

        // Status brushes
        private static readonly Brush LockedBrush = new SolidColorBrush(Color.FromRgb(102, 102, 102));
        private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));
        private static readonly Brush DoneBrush = new SolidColorBrush(Color.FromRgb(33, 150, 243));
        private static readonly Brush FailedBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54));
        private static readonly Brush LevelLockedBrush = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange for Level Locked
        private static readonly Brush UnavailableBrush = new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray for Unavailable

        public QuestListPage()
        {
            InitializeComponent();
            AddHandler(TarkovHelper.Controls.TaskCardControl.HideTaskClickedEvent, new RoutedEventHandler(OnTaskCardHideClicked));
            _loc.LanguageChanged += OnLanguageChanged;
            _progressService.ProgressChanged += OnProgressChanged;
            _inventoryService.InventoryChanged += OnInventoryChanged;
            NeededKeysService.Instance.DataChanged += OnNeededKeysDataChanged;
            SettingsService.Instance.HasEodEditionChanged += OnProfileSettingChanged;
            SettingsService.Instance.HasUnheardEditionChanged += OnProfileSettingChanged;
            SettingsService.Instance.PrestigeLevelChanged += OnPrestigeLevelChanged;
            SettingsService.Instance.DspDecodeCountChanged += OnDspDecodeCountChanged;
            SettingsService.Instance.PlayerFactionChanged += OnPlayerFactionChanged;
            SettingsService.Instance.PlayerLevelChanged += OnPlayerLevelChanged;
            QuestDbService.Instance.DataRefreshed += OnDatabaseRefreshed;

            Loaded += QuestListPage_Loaded;
            Unloaded += QuestListPage_Unloaded;
        }

        private void QuestListPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isUnloaded = true;
            // Unsubscribe from events to prevent memory leaks
            _loc.LanguageChanged -= OnLanguageChanged;
            _progressService.ProgressChanged -= OnProgressChanged;
            _inventoryService.InventoryChanged -= OnInventoryChanged;
            NeededKeysService.Instance.DataChanged -= OnNeededKeysDataChanged;
            SettingsService.Instance.HasEodEditionChanged -= OnProfileSettingChanged;
            SettingsService.Instance.HasUnheardEditionChanged -= OnProfileSettingChanged;
            SettingsService.Instance.PrestigeLevelChanged -= OnPrestigeLevelChanged;
            SettingsService.Instance.DspDecodeCountChanged -= OnDspDecodeCountChanged;
            SettingsService.Instance.PlayerFactionChanged -= OnPlayerFactionChanged;
            SettingsService.Instance.PlayerLevelChanged -= OnPlayerLevelChanged;
            QuestDbService.Instance.DataRefreshed -= OnDatabaseRefreshed;
        }

        private void OnPlayerLevelChanged(object? sender, int e)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var vm in _allQuestViewModels)
                {
                    vm.NotifyMetricsChanged();
                }
                RefreshQuestStatuses();
                ApplyFilters();
                UpdateDetailPanel();
                UpdateRecommendations();
            });
        }

        private void OnInventoryChanged(object? sender, EventArgs e) => RefreshDetailInventoryAndKeys();
        private void OnNeededKeysDataChanged() => RefreshDetailInventoryAndKeys();

        private void RefreshDetailInventoryAndKeys()
        {
            Dispatcher.Invoke(() =>
            {
                if (_isUnloaded || _currentDetailTask == null) return;
                LoadNeededKeys(_currentDetailTask);
                if (_currentDetailTask.RequiredItems != null && _currentDetailTask.RequiredItems.Count > 0)
                {
                    LoadRequiredItems(_currentDetailTask.RequiredItems, _currentDetailTask);
                }
                else
                {
                    RequiredItemsList.ItemsSource = null;
                    RequiredItemsSectionWrapper.Visibility = Visibility.Collapsed;
                }
            });
        }

        private async void QuestListPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Re-subscribe events if page was previously unloaded
            if (_isUnloaded)
            {
                _isUnloaded = false;
                _loc.LanguageChanged += OnLanguageChanged;
                _progressService.ProgressChanged += OnProgressChanged;
                _inventoryService.InventoryChanged += OnInventoryChanged;
                NeededKeysService.Instance.DataChanged += OnNeededKeysDataChanged;
                SettingsService.Instance.HasEodEditionChanged += OnProfileSettingChanged;
                SettingsService.Instance.HasUnheardEditionChanged += OnProfileSettingChanged;
                SettingsService.Instance.PrestigeLevelChanged += OnPrestigeLevelChanged;
                SettingsService.Instance.DspDecodeCountChanged += OnDspDecodeCountChanged;
                SettingsService.Instance.PlayerFactionChanged += OnPlayerFactionChanged;
                SettingsService.Instance.PlayerLevelChanged += OnPlayerLevelChanged;
                QuestDbService.Instance.DataRefreshed += OnDatabaseRefreshed;
            }

            NeededKeysService.Instance.LoadTrackerKeysFromResources();

            // Skip if already loaded (prevents re-initialization on tab switching)
            if (_isDataLoaded) return;

            await LoadItemDataAsync();
            if (_isUnloaded) return; // Check if page was unloaded during async operation

            InitializeNavChips();
            LoadQuests();
            PopulateTraderFilter();
            PopulateMapFilter();
            UpdateSecondaryFilterDropdown();
            LoadFactionSelection();
            _isInitializing = false;
            _isDataLoaded = true;
            SwitchNavigationMode(_navMode);
            ApplyFilters();
            UpdateRecommendations();
            TraderPanel.RefreshAllTraders();

            // Process pending selection if any
            if (!string.IsNullOrEmpty(_pendingQuestSelection))
            {
                var pendingName = _pendingQuestSelection;
                _pendingQuestSelection = null;
                SelectQuestInternal(pendingName);
            }
        }

        private async Task LoadItemDataAsync()
        {
            // Load items data from DB for localized names and icons
            var itemDbService = ItemDbService.Instance;
            if (!itemDbService.IsLoaded)
            {
                await itemDbService.LoadItemsAsync();
            }
            _itemLookup = itemDbService.GetItemLookup();
        }

        private void OnLanguageChanged(object? sender, AppLanguage e)
        {
            RefreshQuestDisplayNames();
            UpdateSecondaryFilterDropdown();
            ApplyFilters();
            UpdateDetailPanel();
            TraderPanel.RefreshAllTraders();
        }

        private void OnProgressChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (_viewMode == TasksViewMode.Graph)
                {
                    UpdateGraphNodeStatuses();
                }
                RefreshQuestStatuses();
                ApplyFilters();
                UpdateDetailPanel();
                UpdateRecommendations();
                TraderPanel.RefreshAllTraders();
            });
        }

        private async void OnDatabaseRefreshed(object? sender, EventArgs e)
        {
            // DB 업데이트 후 데이터 다시 로드
            await Dispatcher.InvokeAsync(async () =>
            {
                // Item lookup 새로고침
                _itemLookup = ItemDbService.Instance.GetItemLookup();

                // Quest 데이터 다시 로드
                LoadQuests();
                PopulateTraderFilter();
                PopulateMapFilter();
                UpdateSecondaryFilterDropdown();
                ApplyFilters();
                UpdateDetailPanel();
                UpdateRecommendations();
                UpdateKappaGauge();
            });
        }

        private void OnProfileSettingChanged(object? sender, bool e)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshQuestStatuses();
                ApplyFilters();
                UpdateDetailPanel();
                UpdateRecommendations();
            });
        }

        private void OnPrestigeLevelChanged(object? sender, int e)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshQuestStatuses();
                ApplyFilters();
                UpdateDetailPanel();
                UpdateRecommendations();
            });
        }

        private void OnDspDecodeCountChanged(object? sender, int e)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshQuestStatuses();
                ApplyFilters();
                UpdateDetailPanel();
                UpdateRecommendations();
            });
        }

        private void OnPlayerFactionChanged(object? sender, string? e)
        {
            Dispatcher.Invoke(() =>
            {
                // Update radio button selection to match the new faction
                _isInitializing = true;
                if (e == "bear")
                {
                    RbBear.IsChecked = true;
                    RbUsec.IsChecked = false;
                }
                else if (e == "usec")
                {
                    RbUsec.IsChecked = true;
                    RbBear.IsChecked = false;
                }
                else
                {
                    RbBear.IsChecked = false;
                    RbUsec.IsChecked = false;
                }
                _isInitializing = false;

                RefreshQuestStatuses();
                ApplyFilters();
                UpdateDetailPanel();
                UpdateRecommendations();
            });
        }

        /// <summary>
        /// Public method to refresh the display from external callers
        /// </summary>
        public void RefreshDisplay()
        {
            RefreshQuestStatuses();
            ApplyFilters();
            UpdateDetailPanel();
        }

        /// <summary>
        /// Reload all quest data from QuestProgressService
        /// Call this after data has been refreshed from API
        /// </summary>
        public async Task ReloadDataAsync()
        {
            // Reload map and item data
            await LoadItemDataAsync();

            // Reload quests from the updated progress service
            LoadQuests();

            // Repopulate filters
            PopulateTraderFilter();
            PopulateMapFilter();

            // Refresh display names with current locale
            RefreshQuestDisplayNames();

            // Apply filters to update the list
            ApplyFilters();
        }

        /// <summary>
        /// Select a quest by task instance
        /// </summary>
        public void SelectQuest(TarkovTask task)
        {
            if (task == null) return;
            if (!string.IsNullOrEmpty(task.Id))
            {
                SelectQuest(task.Id);
            }
            else if (!string.IsNullOrEmpty(task.NormalizedName))
            {
                SelectQuest(task.NormalizedName);
            }
        }

        /// <summary>
        /// Select a quest by its normalized name or BSG ID (for cross-tab navigation)
        /// </summary>
        public void SelectQuest(string questNormalizedName)
        {
            // If data is not loaded yet, save for later
            if (!_isDataLoaded)
            {
                _pendingQuestSelection = questNormalizedName;
                return;
            }

            SelectQuestInternal(questNormalizedName);
        }

        /// <summary>
        /// Internal method to select a quest (called when data is ready)
        /// </summary>
        private void SelectQuestInternal(string questNormalizedName)
        {
            // Reset filters to ensure the quest is visible
            ResetFiltersForNavigation();

            // Find the quest view model by Id, BSG ID, normalized name, or legacy alias
            string bsgId = CanonicalQuestMap.ResolveBsgId(questNormalizedName);
            var questVm = _allQuestViewModels.FirstOrDefault(vm =>
                (!string.IsNullOrEmpty(vm.Task.Id) && string.Equals(vm.Task.Id, questNormalizedName, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(bsgId) && string.Equals(vm.Task.Id, bsgId, StringComparison.OrdinalIgnoreCase)) ||
                (vm.Task.Ids != null && vm.Task.Ids.Contains(questNormalizedName, StringComparer.OrdinalIgnoreCase)) ||
                string.Equals(vm.Task.NormalizedName, questNormalizedName, StringComparison.OrdinalIgnoreCase));

            if (questVm == null) return;

            // Disable selection changed event BEFORE ApplyFilters to prevent
            // the detail panel from being hidden when ItemsSource changes
            LstQuests.SelectionChanged -= LstQuests_SelectionChanged;

            // Apply filters to update the list
            ApplyFilters();

            // Use Dispatcher to ensure UI is updated before selection
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // Select the quest in the list
                    LstQuests.SelectedItem = questVm;

                    // Scroll to make it visible
                    LstQuests.ScrollIntoView(questVm);

                    // Force UI update
                    LstQuests.UpdateLayout();

                    // Force update detail panel with the specific quest
                    UpdateDetailPanel(questVm);
                }
                finally
                {
                    // Re-enable selection changed event
                    LstQuests.SelectionChanged += LstQuests_SelectionChanged;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Reset filters for navigation to ensure target item is visible
        /// </summary>
        private void ResetFiltersForNavigation()
        {
            _isInitializing = true;

            // Reset status filter to "All"
            if (RbStatusAll != null) RbStatusAll.IsChecked = true;
            if (CmbStatus != null) CmbStatus.SelectedIndex = 1; // "All"

            // Clear search text
            if (TxtSearch != null) TxtSearch.Text = "";

            // Reset other filters
            if (ChkKappaChip != null) ChkKappaChip.IsChecked = false;
            if (ChkKappaOnly != null) ChkKappaOnly.IsChecked = false;
            if (ChkCurrentLevelChip != null) ChkCurrentLevelChip.IsChecked = false;
            if (ChkItemRequired != null) ChkItemRequired.IsChecked = false;
            if (ChkKeyRequired != null) ChkKeyRequired.IsChecked = false;
            if (TabStoryFilter != null) TabStoryFilter.IsChecked = false;

            // Reset sort
            if (CmbSortBy != null) CmbSortBy.SelectedIndex = 0;
            _sortMode = TaskSortMode.Impact;
            _sortDirection = SortDirection.Descending;
            if (BtnSortDirection != null) BtnSortDirection.Content = "▼";

            // Reset navigation chip to "Todos"
            var activeChips = _navMode == NavigationMode.Traders ? TraderChips : MapChips;
            var todos = activeChips.FirstOrDefault(c => string.IsNullOrEmpty(c.Key));
            if (todos != null)
            {
                foreach (var c in activeChips)
                {
                    c.IsSelected = (c == todos);
                }
                _selectedTarget = string.Empty;
            }

            _isInitializing = false;
        }

        private void LoadQuests()
        {
            var tasks = _progressService.AllTasks;

            _allQuestViewModels = tasks.Select(t => CreateQuestViewModel(t)).ToList();
            _traders = tasks.Select(t => t.Trader).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();
            _maps = tasks.Where(t => t.Maps != null).SelectMany(t => t.Maps!).Distinct().OrderBy(m => m).ToList();
        }

        private bool IsQuestOverridden(TarkovTask? task)
        {
            if (task == null) return false;

            // Story chapters are naturally active by design, not by manual override
            bool isStory = string.Equals(task.Trader, "Story", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(task.Trader, "Story Chapters", StringComparison.OrdinalIgnoreCase);
            if (isStory) return false;

            // A quest is manually overridden if it has QuestStatus.Active but its natural requirements are NOT met
            var currentStatus = _progressService.GetStatus(task);
            if (currentStatus != QuestStatus.Active) return false;

            bool naturalMet = _progressService.ArePrerequisitesMet(task) &&
                              _progressService.IsLevelRequirementMet(task) &&
                              _progressService.IsScavKarmaRequirementMet(task) &&
                              _progressService.IsEditionRequirementMet(task) &&
                              _progressService.IsPrestigeLevelRequirementMet(task) &&
                              _progressService.IsFactionRequirementMet(task) &&
                              _progressService.IsDspRequirementMet(task);

            return !naturalMet;
        }

        private QuestViewModel CreateQuestViewModel(TarkovTask task)
        {
            var status = _progressService.GetStatus(task);
            var (displayName, subtitle, showSubtitle) = GetLocalizedNames(task);
            bool isStory = string.Equals(task.Trader, "Story", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(task.Trader, "Story Chapters", StringComparison.OrdinalIgnoreCase);
            bool isOverridden = !isStory && IsQuestOverridden(task);

            var keys = NeededKeysService.Instance.GetNeededKeysForQuest(task);
            bool hasKeys = keys != null && keys.Count > 0;
            bool hasFir = task.RequiredItems?.Any(ri => ri.FoundInRaid) == true;
            bool hasItems = task.RequiredItems != null && task.RequiredItems.Any(ri =>
            {
                var tarkovItem = GetItemByNormalizedName(ri.ItemNormalizedName, ri.ItemDisplayName);
                bool isKey = (tarkovItem != null && (
                    string.Equals(tarkovItem.Category, "Keys", StringComparison.OrdinalIgnoreCase) ||
                    tarkovItem.Name.EndsWith(" key", StringComparison.OrdinalIgnoreCase) ||
                    tarkovItem.Name.EndsWith(" Keycard", StringComparison.OrdinalIgnoreCase) ||
                    tarkovItem.Name.Contains(" key ", StringComparison.OrdinalIgnoreCase))) ||
                    ri.ItemDisplayName.EndsWith(" key", StringComparison.OrdinalIgnoreCase) ||
                    ri.ItemDisplayName.EndsWith(" Keycard", StringComparison.OrdinalIgnoreCase) ||
                    ri.ItemDisplayName.Contains(" key ", StringComparison.OrdinalIgnoreCase);
                return !isKey;
            });

            return new QuestViewModel
            {
                Task = task,
                DisplayName = displayName,
                SubtitleName = subtitle,
                SubtitleVisibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed,
                TraderInitial = GetTraderInitial(task.Trader),
                Status = status,
                StatusText = GetStatusText(status, task),
                StatusBackground = GetStatusBrush(status),
                CompleteButtonVisibility = (!isStory && (status == QuestStatus.Active || status == QuestStatus.Locked || status == QuestStatus.LevelLocked)
                    && status != QuestStatus.Unavailable) ? Visibility.Visible : Visibility.Collapsed,
                IsKappaRequired = task.ReqKappa,
                IsManualOverride = isOverridden,
                HasNeededKeys = hasKeys,
                HasFirRequirements = hasFir,
                HasRequiredItems = hasItems
            };
        }

        private (string DisplayName, string Subtitle, bool ShowSubtitle) GetLocalizedNames(TarkovTask task)
        {
            var lang = _loc.CurrentLanguage;
            if (lang == AppLanguage.KO && !string.IsNullOrEmpty(task.NameKo))
            {
                return (task.NameKo, task.Name, true);
            }
            if (lang == AppLanguage.JA && !string.IsNullOrEmpty(task.NameJa))
            {
                return (task.NameJa, task.Name, true);
            }

            return (task.Name, string.Empty, false);
        }

        private static string GetTraderInitial(string trader)
        {
            if (string.IsNullOrEmpty(trader)) return "?";
            if (string.Equals(trader, "Story", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trader, "Story Chapters", StringComparison.OrdinalIgnoreCase))
            {
                return "📖";
            }
            return trader.Length >= 2 ? trader[..2].ToUpper() : trader.ToUpper();
        }

        private string GetStatusText(QuestStatus status, TarkovTask? task = null)
        {
            if (status == QuestStatus.Locked && task != null && task.PoolRequirements != null && task.PoolRequirements.Count > 0)
            {
                var (completed, required) = _progressService.GetPoolProgress(task);
                if (required > 0)
                {
                    return $"Locked ({completed}/{required})";
                }
            }

            if (status == QuestStatus.LevelLocked && task != null)
            {
                // Check if it's level-locked or karma-locked
                if (task.RequiredLevel.HasValue && !_progressService.IsLevelRequirementMet(task))
                {
                    return $"Lv.{task.RequiredLevel}";
                }
                if (task.RequiredScavKarma.HasValue && !_progressService.IsScavKarmaRequirementMet(task))
                {
                    return $"Rep {task.RequiredScavKarma:0.#}";
                }
            }

            if (status == QuestStatus.Unavailable && task != null)
            {
                // Show specific reason for unavailability
                if (!_progressService.IsEditionRequirementMet(task))
                {
                    // Show which edition is required
                    var requiredEdition = task.RequiredEdition?.ToLowerInvariant();
                    if (requiredEdition == "eod" || requiredEdition == "edge_of_darkness")
                        return "EOD";
                    if (requiredEdition == "unheard" || requiredEdition == "the_unheard")
                        return "Unheard";
                    // Check for excluded edition
                    var excludedEdition = task.ExcludedEdition?.ToLowerInvariant();
                    if (!string.IsNullOrEmpty(excludedEdition))
                        return "Edition";
                }
                if (!_progressService.IsPrestigeLevelRequirementMet(task))
                {
                    return $"Prestige {task.RequiredPrestigeLevel}";
                }
                // Show faction if quest is for different faction
                if (!_progressService.IsFactionRequirementMet(task))
                {
                    var faction = task.Faction?.ToLowerInvariant();
                    if (faction == "bear")
                        return "BEAR Only";
                    if (faction == "usec")
                        return "USEC Only";
                }
            }

            return status switch
            {
                QuestStatus.Locked => "Locked",
                QuestStatus.Active => "Active",
                QuestStatus.Done => "Done",
                QuestStatus.Failed => "Failed",
                QuestStatus.LevelLocked => "Level Locked",
                QuestStatus.Unavailable => "Unavailable",
                _ => "Unknown"
            };
        }

        private static Brush GetStatusBrush(QuestStatus status)
        {
            return status switch
            {
                QuestStatus.Locked => LockedBrush,
                QuestStatus.Active => ActiveBrush,
                QuestStatus.Done => DoneBrush,
                QuestStatus.Failed => FailedBrush,
                QuestStatus.LevelLocked => LevelLockedBrush,
                QuestStatus.Unavailable => UnavailableBrush,
                _ => Brushes.Gray
            };
        }

        private void RefreshQuestDisplayNames()
        {
            foreach (var vm in _allQuestViewModels)
            {
                var (displayName, subtitle, showSubtitle) = GetLocalizedNames(vm.Task);
                vm.DisplayName = displayName;
                vm.SubtitleName = subtitle;
                vm.SubtitleVisibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RefreshQuestStatuses()
        {
            foreach (var vm in _allQuestViewModels)
            {
                var status = _progressService.GetStatus(vm.Task);
                bool isStory = vm.IsStoryChapter;
                bool isOverridden = !isStory && IsQuestOverridden(vm.Task);

                vm.Status = status;
                vm.StatusText = GetStatusText(status, vm.Task);
                vm.StatusBackground = GetStatusBrush(status);
                vm.IsManualOverride = isOverridden;
                vm.CompleteButtonVisibility = (!isStory && (status == QuestStatus.Active || status == QuestStatus.Locked || status == QuestStatus.LevelLocked)
                    && status != QuestStatus.Unavailable) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void PopulateTraderFilter()
        {
            // Clear existing items except "All Traders"
            while (CmbTrader.Items.Count > 1)
            {
                CmbTrader.Items.RemoveAt(1);
            }

            foreach (var trader in _traders)
            {
                var localizedTrader = _loc.GetLocalizedTraderName(trader);
                CmbTrader.Items.Add(new ComboBoxItem { Content = localizedTrader, Tag = trader });
            }
        }

        private void PopulateMapFilter()
        {
            // Clear existing items except "All Maps"
            while (CmbMap.Items.Count > 1)
            {
                CmbMap.Items.RemoveAt(1);
            }

            foreach (var mapNormalized in _maps)
            {
                // Get localized map name
                var mapName = GetLocalizedMapName(mapNormalized);
                CmbMap.Items.Add(new ComboBoxItem { Content = mapName, Tag = mapNormalized });
            }
        }

        private string GetLocalizedMapName(string normalizedName)
        {
            return _loc.GetLocalizedMapName(normalizedName);
        }

        private void UpdateSecondaryFilterDropdown()
        {
            if (CmbSecondaryFilter == null) return;

            _isUpdatingSecondaryDropdown = true;
            try
            {
                var prevTag = (CmbSecondaryFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
                CmbSecondaryFilter.Items.Clear();

                bool isSpanish = _loc.CurrentLanguage == AppLanguage.ES;

                if (_viewMode == TasksViewMode.Maps)
                {
                    // Primary grouping is Maps -> Secondary filter is Traders
                    CmbSecondaryFilter.ToolTip = isSpanish ? "Filtro secundario (Comerciante)" : "Secondary filter (Trader)";
                    CmbSecondaryFilter.Items.Add(new ComboBoxItem
                    {
                        Content = isSpanish ? "👥 Todos los comerciantes" : "👥 All Traders",
                        Tag = ""
                    });

                    foreach (var trader in _traders)
                    {
                        var localizedTrader = _loc.GetLocalizedTraderName(trader);
                        CmbSecondaryFilter.Items.Add(new ComboBoxItem { Content = localizedTrader, Tag = trader });
                    }
                }
                else
                {
                    // Primary grouping is Traders (or List/Graph) -> Secondary filter is Maps
                    CmbSecondaryFilter.ToolTip = isSpanish ? "Filtro secundario (Mapa)" : "Secondary filter (Map)";
                    CmbSecondaryFilter.Items.Add(new ComboBoxItem
                    {
                        Content = isSpanish ? "🗺️ Todos los mapas" : "🗺️ All Maps",
                        Tag = ""
                    });

                    foreach (var mapNormalized in _maps)
                    {
                        var mapName = GetLocalizedMapName(mapNormalized);
                        CmbSecondaryFilter.Items.Add(new ComboBoxItem { Content = mapName, Tag = mapNormalized });
                    }
                }

                // Restore previous selection if still present, otherwise default to "All" (index 0)
                int selectedIndex = 0;
                if (!string.IsNullOrEmpty(prevTag))
                {
                    for (int i = 0; i < CmbSecondaryFilter.Items.Count; i++)
                    {
                        if (CmbSecondaryFilter.Items[i] is ComboBoxItem item &&
                            string.Equals(item.Tag?.ToString(), prevTag, StringComparison.OrdinalIgnoreCase))
                        {
                            selectedIndex = i;
                            break;
                        }
                    }
                }
                CmbSecondaryFilter.SelectedIndex = selectedIndex;
            }
            finally
            {
                _isUpdatingSecondaryDropdown = false;
            }
        }

        private void SecondaryFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing && !_isUpdatingSecondaryDropdown)
            {
                ApplyFilters();
            }
        }

        private (string? selectedTrader, string? selectedMap) GetSecondaryFilters()
        {
            var secTag = (CmbSecondaryFilter?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(secTag)) secTag = null;

            if (_viewMode == TasksViewMode.Traders)
            {
                string? trader = string.IsNullOrWhiteSpace(_selectedTarget) ? null : _selectedTarget;
                return (trader, secTag);
            }
            else if (_viewMode == TasksViewMode.Maps)
            {
                string? map = string.IsNullOrWhiteSpace(_selectedTarget) ? null : _selectedTarget;
                return (secTag, map);
            }
            else
            {
                return (null, secTag);
            }
        }

        private void InitializeNavChips()
        {
            if (TraderChips.Count == 0)
            {
                TraderChips.Add(new NavChipItemViewModel { Key = "", DisplayName = "Todos", IconOrInitials = "★", IsSelected = true });
                TraderChips.Add(new NavChipItemViewModel { Key = "Prapor", DisplayName = "Prapor", IconOrInitials = "PR" });
                TraderChips.Add(new NavChipItemViewModel { Key = "Therapist", DisplayName = "Therapist", IconOrInitials = "TH" });
                TraderChips.Add(new NavChipItemViewModel { Key = "Skier", DisplayName = "Skier", IconOrInitials = "SK" });
                TraderChips.Add(new NavChipItemViewModel { Key = "Peacekeeper", DisplayName = "Peacekeeper", IconOrInitials = "PK" });
                TraderChips.Add(new NavChipItemViewModel { Key = "Mechanic", DisplayName = "Mechanic", IconOrInitials = "ME" });
                TraderChips.Add(new NavChipItemViewModel { Key = "Ragman", DisplayName = "Ragman", IconOrInitials = "RA" });
                TraderChips.Add(new NavChipItemViewModel { Key = "Jaeger", DisplayName = "Jaeger", IconOrInitials = "JG" });
                TraderChips.Add(new NavChipItemViewModel { Key = "Fence", DisplayName = "Fence", IconOrInitials = "FE" });
                TraderChips.Add(new NavChipItemViewModel { Key = "Ref", DisplayName = "Ref", IconOrInitials = "RF" });
                TraderChips.Add(new NavChipItemViewModel { Key = "Lightkeeper", DisplayName = "Lightkeeper", IconOrInitials = "LK" });
                TraderChips.Add(new NavChipItemViewModel { Key = "BTR Driver", DisplayName = "BTR", IconOrInitials = "BTR" });
                TraderChips.Add(new NavChipItemViewModel { Key = "Kappa", DisplayName = "★ Kappa Path", IconOrInitials = "★" });
            }

            if (MapChips.Count == 0)
            {
                // Ordered alphabetically, with "Todos" as default global chip
                MapChips.Add(new NavChipItemViewModel { Key = "", DisplayName = "Todos", IconOrInitials = "★", IsSelected = false });

                var lastMap = TarkovHelper.Services.Settings.MapSettings.Instance.LastSelectedMap;
                var defaultKey = !string.IsNullOrEmpty(lastMap) ? lastMap.ToLowerInvariant() : "customs";

                var maps = new (string key, string name)[]
                {
                    ("customs", "Customs"),
                    ("factory", "Factory"),
                    ("ground-zero", "Ground Zero"),
                    ("interchange", "Interchange"),
                    ("lighthouse", "Lighthouse"),
                    ("reserve", "Reserve"),
                    ("shoreline", "Shoreline"),
                    ("streets", "Streets of Tarkov"),
                    ("terminal", "Terminal"),
                    ("the-lab", "The Lab"),
                    ("the-labyrinth", "The Labyrinth"),
                    ("woods", "Woods")
                };

                var matchedAny = false;
                foreach (var (k, name) in maps)
                {
                    var isSelected = string.Equals(k, defaultKey, StringComparison.OrdinalIgnoreCase);
                    if (isSelected) matchedAny = true;
                    MapChips.Add(new NavChipItemViewModel { Key = k, DisplayName = name, IsSelected = isSelected });
                }
                if (!matchedAny && MapChips.Count > 0)
                {
                    MapChips[0].IsSelected = true;
                }
            }

            if (IcNavChips != null)
            {
                IcNavChips.ItemsSource = _navMode == NavigationMode.Traders ? TraderChips : MapChips;
            }
        }

        private QuickFilterStatus GetQuickFilterStatus()
        {
            if (RbStatusActive?.IsChecked == true) return QuickFilterStatus.Active;
            if (RbStatusLocked?.IsChecked == true) return QuickFilterStatus.Locked;
            if (RbStatusDone?.IsChecked == true) return QuickFilterStatus.Done;
            if (RbStatusFailed?.IsChecked == true) return QuickFilterStatus.Failed;
            return QuickFilterStatus.All;
        }

        private void Sort_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (CmbSortBy?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                _sortMode = tag switch
                {
                    "Impact" or "impact" => TaskSortMode.Impact,
                    "Default" or "DefaultOrder" or "none" => TaskSortMode.DefaultOrder,
                    "Alphabetical" or "Name" or "alphabetical" => TaskSortMode.Alphabetical,
                    "Level" or "PlayerLevel" or "level" => TaskSortMode.PlayerLevel,
                    "Trader" or "TraderOrder" or "trader" => TaskSortMode.TraderOrder,
                    "XP" or "XpReward" or "xp" => TaskSortMode.XpReward,
                    "Progression" or "progression" => TaskSortMode.Progression,
                    "Teammates" or "teammates" => TaskSortMode.Teammates,
                    _ => TaskSortMode.Impact
                };
                ApplyFilters();
            }
        }

        private void BtnFilters_Click(object sender, RoutedEventArgs e)
        {
            if (FilterPopup != null)
            {
                FilterPopup.IsOpen = !FilterPopup.IsOpen;
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsPopup != null)
            {
                SettingsPopup.IsOpen = !SettingsPopup.IsOpen;
            }
        }

        private void BtnResetFilters_Click(object sender, RoutedEventArgs e)
        {
            if (ChkFilterRegular != null) ChkFilterRegular.IsChecked = true;
            if (ChkKappaChip != null) ChkKappaChip.IsChecked = true;
            if (ChkFilterLightkeeper != null) ChkFilterLightkeeper.IsChecked = true;
            if (ChkKeyRequired != null) ChkKeyRequired.IsChecked = false;
            if (ChkCurrentLevelChip != null) ChkCurrentLevelChip.IsChecked = false;
            if (ChkMapAnyLocation != null) ChkMapAnyLocation.IsChecked = true;
            if (ChkMapHideFinished != null) ChkMapHideFinished.IsChecked = false;
            if (TxtSearch != null) TxtSearch.Text = string.Empty;
            ApplyFilters();
        }

        private void TaskType_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            // Rule: At least one task type must stay selected (TarkovTracker specification)
            if (ChkFilterRegular?.IsChecked != true &&
                ChkKappaChip?.IsChecked != true &&
                ChkFilterLightkeeper?.IsChecked != true)
            {
                if (sender is CheckBox cb)
                {
                    cb.IsChecked = true;
                    return;
                }
            }

            ApplyFilters();
        }

        private void BtnGraphSwitchToTraders_Click(object sender, RoutedEventArgs e)
        {
            if (RbViewTraders != null)
            {
                RbViewTraders.IsChecked = true;
            }
        }

        private void BtnSortDirection_Click(object sender, RoutedEventArgs e)
        {
            _sortDirection = _sortDirection == SortDirection.Descending ? SortDirection.Ascending : SortDirection.Descending;
            if (BtnSortDirection != null)
            {
                BtnSortDirection.Content = _sortDirection == SortDirection.Descending ? "▼" : "▲";
            }
            ApplyFilters();
        }

        private void UpdateStatusCounts(int playerLevel)
        {
            var (secTrader, secMap) = GetSecondaryFilters();

            bool regular = ChkFilterRegular?.IsChecked ?? true;
            bool kappa = ChkKappaChip?.IsChecked ?? (ChkKappaOnly?.IsChecked == true);
            bool lightkeeper = ChkFilterLightkeeper?.IsChecked ?? true;
            bool needsKeys = ChkKeyRequired?.IsChecked == true;
            bool curLevel = ChkCurrentLevelChip?.IsChecked == true;
            bool showAnyLoc = ChkMapAnyLocation?.IsChecked ?? true;
            bool hideFinHere = ChkMapHideFinished?.IsChecked == true;

            bool isKappaOnly = kappa && !regular && !lightkeeper;
            if (BtnKappaQuickToggle != null)
            {
                BtnKappaQuickToggle.Visibility = isKappaOnly ? Visibility.Visible : Visibility.Collapsed;
            }

            var baseCriteria = new QuestFilterCriteria
            {
                ViewMode = _viewMode,
                Mode = _navMode,
                SelectedTarget = _selectedTarget,
                SelectedTrader = secTrader,
                SelectedMap = secMap,
                TaskTypeRegular = regular,
                TaskTypeKappa = kappa,
                TaskTypeLightkeeper = lightkeeper,
                CurrentLevelOnly = curLevel,
                ItemRequiredOnly = ChkItemRequired?.IsChecked == true,
                KeyRequiredOnly = needsKeys,
                NeedsKeys = needsKeys,
                StoryOnly = TabStoryFilter?.IsChecked == true,
                ExcludeStoryChapters = true,
                ShowAnyLocationTasks = showAnyLoc,
                HideFinishedHere = hideFinHere,
                SearchQuery = TxtSearch?.Text?.Trim() ?? string.Empty,
                Faction = RbBear?.IsChecked == true ? "bear" : (RbUsec?.IsChecked == true ? "usec" : null),
                ImpactSortFollowsFilters = ChkSettingImpactFollowsFilters?.IsChecked ?? true
            };

            var counts = QuestFilterEngine.ComputeStatusCounts(_allQuestViewModels, baseCriteria, playerLevel);
            if (TxtStatusCountAll != null) TxtStatusCountAll.Text = counts.All.ToString();
            if (TxtStatusCountActive != null) TxtStatusCountActive.Text = counts.Available.ToString();
            if (TxtStatusCountLocked != null) TxtStatusCountLocked.Text = counts.Locked.ToString();
            if (TxtStatusCountDone != null) TxtStatusCountDone.Text = counts.Completed.ToString();
            if (TxtStatusCountFailed != null) TxtStatusCountFailed.Text = counts.Failed.ToString();
        }

        private void ApplyFilters()
        {
            int playerLevel = SettingsService.Instance.PlayerLevel;
            var (secTrader, secMap) = GetSecondaryFilters();

            bool regular = ChkFilterRegular?.IsChecked ?? true;
            bool kappa = ChkKappaChip?.IsChecked ?? (ChkKappaOnly?.IsChecked == true);
            bool lightkeeper = ChkFilterLightkeeper?.IsChecked ?? true;
            bool needsKeys = ChkKeyRequired?.IsChecked == true;
            bool curLevel = ChkCurrentLevelChip?.IsChecked == true;
            bool showAnyLoc = ChkMapAnyLocation?.IsChecked ?? true;
            bool hideFinHere = ChkMapHideFinished?.IsChecked == true;

            var criteria = new QuestFilterCriteria
            {
                ViewMode = _viewMode,
                Mode = _navMode,
                SelectedTarget = _selectedTarget,
                SelectedTrader = secTrader,
                SelectedMap = secMap,
                StatusFilter = GetQuickFilterStatus(),
                SortBy = _sortMode,
                SortDirection = _sortDirection,
                TaskTypeRegular = regular,
                TaskTypeKappa = kappa,
                TaskTypeLightkeeper = lightkeeper,
                CurrentLevelOnly = curLevel,
                ItemRequiredOnly = ChkItemRequired?.IsChecked == true,
                KeyRequiredOnly = needsKeys,
                NeedsKeys = needsKeys,
                StoryOnly = TabStoryFilter?.IsChecked == true,
                ExcludeStoryChapters = true,
                ShowAnyLocationTasks = showAnyLoc,
                HideFinishedHere = hideFinHere,
                SearchQuery = TxtSearch?.Text?.Trim() ?? string.Empty,
                Faction = RbBear?.IsChecked == true ? "bear" : (RbUsec?.IsChecked == true ? "usec" : null),
                ImpactSortFollowsFilters = ChkSettingImpactFollowsFilters?.IsChecked ?? true
            };

            var filtered = QuestFilterEngine.Filter(_allQuestViewModels, criteria, playerLevel);
            _lastFilteredQuests = filtered;

            var hiddenIds = TarkovHelper.Services.Settings.MapSettings.Instance.HiddenQuests;
            var visibleQuests = new List<QuestViewModel>();
            var hiddenQuests = new List<QuestViewModel>();

            foreach (var q in filtered)
            {
                var qId = q.Task?.NormalizedName ?? q.Task?.Id ?? string.Empty;
                if (!string.IsNullOrEmpty(qId) && hiddenIds.Contains(qId))
                {
                    hiddenQuests.Add(q);
                }
                else
                {
                    visibleQuests.Add(q);
                }
            }

            LstQuests.ItemsSource = visibleQuests;
            
            if (_viewMode == TasksViewMode.List)
            {
                if (_currentDetailTask != null)
                {
                    var match = visibleQuests.FirstOrDefault(v =>
                        (!string.IsNullOrEmpty(v.Task?.Id) && v.Task.Id == _currentDetailTask.Id) ||
                        (!string.IsNullOrEmpty(v.Task?.NormalizedName) && v.Task.NormalizedName == _currentDetailTask.NormalizedName));
                    if (match != null)
                    {
                        LstQuests.SelectedItem = match;
                    }
                    else if (visibleQuests.Count > 0)
                    {
                        LstQuests.SelectedIndex = 0;
                    }
                }
                else if (visibleQuests.Count > 0 && LstQuests.SelectedItem == null)
                {
                    LstQuests.SelectedIndex = 0;
                }
                UpdateDetailPanel();
            }

            if (HiddenTasksBar != null && LstHiddenQuests != null && TxtHiddenTasksTitle != null)
            {
                if (hiddenQuests.Count > 0)
                {
                    HiddenTasksBar.Visibility = Visibility.Visible;
                    TxtHiddenTasksTitle.Text = $"Hidden tasks ({hiddenQuests.Count})";
                    LstHiddenQuests.ItemsSource = hiddenQuests;
                }
                else
                {
                    HiddenTasksBar.Visibility = Visibility.Collapsed;
                    LstHiddenQuests.ItemsSource = null;
                }
            }

            if (_embeddedMapPage != null)
            {
                ApplyQuestFiltersToMapMarkers(criteria);
            }

            // Update live reactive status count badges
            UpdateStatusCounts(playerLevel);

            // Update statistics
            var stats = _progressService.GetStatistics();
            var lockedTotal = stats.Locked + stats.LevelLocked;
            if (TxtStats != null)
            {
                TxtStats.Text = $"Lv. {playerLevel} | Showing {filtered.Count} of {stats.Total} | " +
                               $"Active: {stats.Active} | Locked: {lockedTotal} | Done: {stats.Done} | Failed: {stats.Failed} | Unavailable: {stats.Unavailable}";
            }

            // Update Kappa progress gauge
            UpdateKappaGauge();

            // Update Story badge count
            if (TxtStoryBadge != null)
            {
                var storyQuests = _allQuestViewModels.Where(v => v.IsStoryChapter).ToList();
                var storyDone = storyQuests.Count(v => v.IsDone);
                TxtStoryBadge.Text = $"{storyDone}/{storyQuests.Count}";
            }

            // Update Navigation Chip counters
            RefreshNavigationChipCounters();
        }

        private void RefreshNavigationChipCounters()
        {
            var currentFaction = RbBear?.IsChecked == true ? "bear" : (RbUsec?.IsChecked == true ? "usec" : null);

            var activeQuests = _allQuestViewModels
                .Where(vm => vm.Status == QuestStatus.Active &&
                             (string.IsNullOrEmpty(currentFaction) ||
                              SettingsService.Instance.ShouldIncludeTask(vm.Task?.Faction)))
                .ToList();

            foreach (var chip in TraderChips)
            {
                if (string.IsNullOrEmpty(chip.Key))
                {
                    chip.ActiveCount = activeQuests.Count;
                }
                else
                {
                    chip.ActiveCount = activeQuests.Count(q => QuestFilterEngine.MatchesTrader(q, chip.Key));
                }
            }

            foreach (var chip in MapChips)
            {
                if (string.IsNullOrEmpty(chip.Key))
                {
                    chip.ActiveCount = activeQuests.Count;
                }
                else
                {
                    chip.ActiveCount = activeQuests.Count(q => QuestFilterEngine.MatchesMap(q, chip.Key));
                }
            }
        }

        /// <summary>
        /// Updates the reactive active quest count badges across all navigation chips (traders and maps).
        /// </summary>
        public void UpdateNavChipCounts()
        {
            RefreshNavigationChipCounters();
        }

        private void NavChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is NavChipItemViewModel chip)
            {
                SelectNavChip(chip);
            }
        }

        private void SelectNavChip(NavChipItemViewModel chip)
        {
            var activeList = _navMode == NavigationMode.Traders ? TraderChips : MapChips;
            foreach (var c in activeList)
            {
                c.IsSelected = (c == chip);
            }
            _selectedTarget = chip.Key;

            if (_viewMode == TasksViewMode.Graph)
            {
                _activeGraphTraderOrMode = string.IsNullOrEmpty(chip.Key) ? "Prapor" : chip.Key;
                RefreshGraphView();
                return;
            }

            if (_navMode == NavigationMode.Maps && !string.IsNullOrEmpty(chip.Key))
            {
                EnsureEmbeddedMap();
                _embeddedMapPage?.SelectMap(chip.Key);
                MapTrackerService.Instance.SetCurrentMap(chip.Key);
            }

            ApplyFilters();
        }

        private void NavMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            // 1. [LIST] Mode
            if (RbViewList?.IsChecked == true)
            {
                _viewMode = TasksViewMode.List;
                _navMode = NavigationMode.Traders;
                if (IcNavChips != null) IcNavChips.ItemsSource = null;
                if (NavChipsRow != null) NavChipsRow.Visibility = Visibility.Collapsed;
                if (GraphMainContainer != null) GraphMainContainer.Visibility = Visibility.Collapsed;
                if (LstQuests != null)
                {
                    LstQuests.Visibility = Visibility.Visible;
                    LstQuests.ItemTemplate = TryFindResource("QuestItemTemplate") as DataTemplate;
                    LstQuests.ItemContainerStyle = TryFindResource("QuestListBoxItem") as Style;
                }
                if (RowMapView != null) RowMapView.Height = new GridLength(0);
                if (RowSplitter != null) RowSplitter.Height = new GridLength(0);
                if (RowTaskList != null) RowTaskList.Height = new GridLength(1, GridUnitType.Star);
                if (ColTaskList != null) ColTaskList.Width = new GridLength(1, GridUnitType.Star);
                if (ColSplitter != null) ColSplitter.Width = new GridLength(0);
                if (MapSplitter != null) MapSplitter.Visibility = Visibility.Collapsed;
                if (ColMapView != null) ColMapView.Width = new GridLength(0);
                if (EmbeddedMapContainer != null) EmbeddedMapContainer.Visibility = Visibility.Collapsed;

                
                if (ColListMaster != null) ColListMaster.Width = new GridLength(1.1, GridUnitType.Star);
                if (ColListSplitter != null) ColListSplitter.Width = new GridLength(6, GridUnitType.Pixel);
                if (ColListDetail != null) ColListDetail.Width = new GridLength(1.4, GridUnitType.Star);
                if (ListDetailSplitter != null) ListDetailSplitter.Visibility = Visibility.Visible;
                if (ListDetailContainer != null) ListDetailContainer.Visibility = Visibility.Visible;

                _selectedTarget = string.Empty;
                UpdateSecondaryFilterDropdown();
                ApplyFilters();
                return;
            }

            // 2. [GRAPH] Mode
            if (RbViewGraph?.IsChecked == true)
            {
                _viewMode = TasksViewMode.Graph;
                _navMode = NavigationMode.Traders;
                if (NavChipsRow != null) NavChipsRow.Visibility = Visibility.Visible;
                if (IcNavChips != null) IcNavChips.ItemsSource = TraderChips;

                if (RowMapView != null) RowMapView.Height = new GridLength(0);
                if (RowSplitter != null) RowSplitter.Height = new GridLength(0);
                if (RowTaskList != null) RowTaskList.Height = new GridLength(1, GridUnitType.Star);
                if (ColTaskList != null) ColTaskList.Width = new GridLength(1, GridUnitType.Star);
                if (ColSplitter != null) ColSplitter.Width = new GridLength(0);
                if (MapSplitter != null) MapSplitter.Visibility = Visibility.Collapsed;
                if (ColMapView != null) ColMapView.Width = new GridLength(0);
                if (EmbeddedMapContainer != null) EmbeddedMapContainer.Visibility = Visibility.Collapsed;

                
                if (ColListMaster != null) ColListMaster.Width = new GridLength(1, GridUnitType.Star);
                if (ColListSplitter != null) ColListSplitter.Width = new GridLength(0);
                if (ColListDetail != null) ColListDetail.Width = new GridLength(0);
                if (ListDetailSplitter != null) ListDetailSplitter.Visibility = Visibility.Collapsed;
                if (ListDetailContainer != null) ListDetailContainer.Visibility = Visibility.Collapsed;

                if (LstQuests != null) LstQuests.Visibility = Visibility.Collapsed;
                if (HiddenTasksBar != null) HiddenTasksBar.Visibility = Visibility.Collapsed;
                if (GraphMainContainer != null) GraphMainContainer.Visibility = Visibility.Visible;

                if (string.IsNullOrEmpty(_selectedTarget) || _selectedTarget == "Todos")
                {
                    _activeGraphTraderOrMode = "Prapor";
                    foreach (var c in TraderChips) c.IsSelected = (c.Key == "Prapor");
                    _selectedTarget = "Prapor";
                }
                else
                {
                    _activeGraphTraderOrMode = _selectedTarget;
                }

                RefreshGraphView();
                return;
            }

            // 3. [TRADERS] or [MAPS] Mode
            var newMode = RbViewTraders?.IsChecked == true ? NavigationMode.Traders : NavigationMode.Maps;
            _viewMode = newMode == NavigationMode.Traders ? TasksViewMode.Traders : TasksViewMode.Maps;

            if (NavChipsRow != null) NavChipsRow.Visibility = Visibility.Visible;
            if (GraphMainContainer != null) GraphMainContainer.Visibility = Visibility.Collapsed;
            if (LstQuests != null)
            {
                LstQuests.Visibility = Visibility.Visible;
                LstQuests.ItemTemplate = TryFindResource("QuestCardTemplate") as DataTemplate;
                LstQuests.ItemContainerStyle = TryFindResource("QuestCardListBoxItem") as Style;
            }

            SwitchNavigationMode(newMode);
        }

        private void SwitchNavigationMode(NavigationMode mode)
        {
            _navMode = mode;
            _viewMode = mode == NavigationMode.Maps ? TasksViewMode.Maps : TasksViewMode.Traders;
            if (NavChipsRow != null) NavChipsRow.Visibility = Visibility.Visible;
            if (LstQuests != null)
            {
                LstQuests.ItemTemplate = TryFindResource("QuestCardTemplate") as DataTemplate;
                LstQuests.ItemContainerStyle = TryFindResource("QuestCardListBoxItem") as Style;
            }

            if (mode == NavigationMode.Maps)
            {
                // Vertical layout: Map container at top with ~380px initial height, resizable via MapSplitter
                if (RowMapView != null) RowMapView.Height = new GridLength(380, GridUnitType.Pixel);
                if (RowSplitter != null) RowSplitter.Height = GridLength.Auto;
                if (MapSplitter != null) MapSplitter.Visibility = Visibility.Visible;
                if (RowTaskList != null) RowTaskList.Height = new GridLength(1, GridUnitType.Star);
                if (EmbeddedMapContainer != null) EmbeddedMapContainer.Visibility = Visibility.Visible;

                // Retain column widths for backward compatibility with Challenger1FullSolutionEmpiricalTests
                if (ColTaskList != null) ColTaskList.Width = new GridLength(1, GridUnitType.Star);
                if (ColSplitter != null) ColSplitter.Width = new GridLength(0);
                if (ColMapView != null) ColMapView.Width = new GridLength(0);

                EnsureEmbeddedMap();
                _embeddedMapPage?.SetEmbeddedMode(true);
            }
            else
            {
                // Full-height Task List in List or Traders mode
                if (RowMapView != null) RowMapView.Height = new GridLength(0);
                if (RowSplitter != null) RowSplitter.Height = new GridLength(0);
                if (MapSplitter != null) MapSplitter.Visibility = Visibility.Collapsed;
                if (RowTaskList != null) RowTaskList.Height = new GridLength(1, GridUnitType.Star);
                if (EmbeddedMapContainer != null) EmbeddedMapContainer.Visibility = Visibility.Collapsed;

                if (ColTaskList != null) ColTaskList.Width = new GridLength(1, GridUnitType.Star);
                if (ColSplitter != null) ColSplitter.Width = new GridLength(0);
                if (ColMapView != null) ColMapView.Width = new GridLength(0);
            }

            
            if (ColListMaster != null) ColListMaster.Width = new GridLength(1, GridUnitType.Star);
            if (ColListSplitter != null) ColListSplitter.Width = new GridLength(0);
            if (ColListDetail != null) ColListDetail.Width = new GridLength(0);
            if (ListDetailSplitter != null) ListDetailSplitter.Visibility = Visibility.Collapsed;
            if (ListDetailContainer != null) ListDetailContainer.Visibility = Visibility.Collapsed;

            var activeList = mode == NavigationMode.Traders ? TraderChips : MapChips;
            if (IcNavChips != null)
            {
                IcNavChips.ItemsSource = activeList;
            }

            // Look for matching chip or default to "Todos"
            var matchingChip = activeList.FirstOrDefault(c => string.Equals(c.Key, _selectedTarget, StringComparison.OrdinalIgnoreCase));
            if (matchingChip == null)
            {
                matchingChip = activeList.FirstOrDefault(c => string.IsNullOrEmpty(c.Key)) ?? activeList.FirstOrDefault();
            }

            if (matchingChip != null)
            {
                foreach (var c in activeList)
                {
                    c.IsSelected = (c == matchingChip);
                }
                _selectedTarget = matchingChip.Key;
            }
            else
            {
                _selectedTarget = string.Empty;
            }

            if (mode == NavigationMode.Maps)
            {
                var mapKey = string.IsNullOrEmpty(_selectedTarget) ? "customs" : _selectedTarget;
                EnsureEmbeddedMap();
                _embeddedMapPage?.SelectMap(mapKey);
                MapTrackerService.Instance.SetCurrentMap(mapKey);
            }

            UpdateSecondaryFilterDropdown();
            ApplyFilters();
        }

        private void QuickFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitializing)
            {
                ApplyFilters();
            }
        }

        private void BtnListRowLockToggle_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (sender is FrameworkElement elem && elem.Tag is QuestViewModel vm && vm.Task != null)
            {
                string taskId = vm.Task.Ids?.FirstOrDefault() ?? vm.Task.NormalizedName ?? string.Empty;
                if (string.IsNullOrEmpty(taskId)) return;

                var qps = _progressService;
                bool isForceUnlocked = qps.IsQuestForceUnlocked(taskId);
                bool isForceLocked = qps.IsQuestForceLocked(taskId);
                var currentStatus = qps.GetStatus(vm.Task);

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

                vm.Status = qps.GetStatus(vm.Task);
                vm.NotifyLockToggleChanged();
            }
        }

        private void MenuListRowForceUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is QuestViewModel vm && vm.Task != null)
            {
                string taskId = vm.Task.Ids?.FirstOrDefault() ?? vm.Task.NormalizedName ?? string.Empty;
                _progressService.SetQuestOverride(taskId, QuestStatus.Active);
                vm.Status = _progressService.GetStatus(vm.Task);
                vm.NotifyLockToggleChanged();
            }
        }

        private void MenuListRowForceLock_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is QuestViewModel vm && vm.Task != null)
            {
                string taskId = vm.Task.Ids?.FirstOrDefault() ?? vm.Task.NormalizedName ?? string.Empty;
                _progressService.SetQuestOverride(taskId, QuestStatus.Locked);
                vm.Status = _progressService.GetStatus(vm.Task);
                vm.NotifyLockToggleChanged();
            }
        }

        private void MenuListRowResetOverride_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is QuestViewModel vm && vm.Task != null)
            {
                string taskId = vm.Task.Ids?.FirstOrDefault() ?? vm.Task.NormalizedName ?? string.Empty;
                _progressService.SetQuestOverride(taskId, null);
                vm.Status = _progressService.GetStatus(vm.Task);
                vm.NotifyLockToggleChanged();
            }
        }

        private void ChkQuestDone_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is CheckBox chk && chk.Tag is QuestViewModel vm && vm.Task != null)
            {
                bool isDone = chk.IsChecked == true;
                if (isDone)
                {
                    bool completePrereqs = !vm.IsStoryChapter;
                    _progressService.CompleteQuest(vm.Task, completePrereqs);
                }
                else
                {
                    _progressService.ResetQuest(vm.Task);
                }
            }
        }

        private void BtnCardMap_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is FrameworkElement btn && btn.Tag is QuestViewModel vm && vm.Task != null)
            {
                var targetMap = vm.Task.Maps?.FirstOrDefault(m => !string.Equals(m, "any", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(targetMap))
                {
                    var objs = QuestObjectiveService.Instance.GetObjectivesForTaskById(vm.Task.Id ?? "");
                    targetMap = objs?.FirstOrDefault(o => o.Locations.Count > 0 && !string.IsNullOrEmpty(o.Locations[0].MapName) && !string.Equals(o.Locations[0].MapName, "any", StringComparison.OrdinalIgnoreCase))?.Locations[0].MapName;
                }
                targetMap ??= vm.Task.Maps?.FirstOrDefault() ?? "customs";
                NavigateToTaskOnMap(vm.Task, targetMap);
            }
        }

        #region Embedded Map Integration (M2)

        public void EnsureEmbeddedMap()
        {
            if (_embeddedMapPage == null)
            {
                _embeddedMapPage = new MapPage();
                _embeddedMapPage.SetEmbeddedMode(true);
                _embeddedMapPage.ObjectiveSelected += OnEmbeddedMapObjectiveSelected;
                if (EmbeddedMapContainer != null)
                {
                    EmbeddedMapContainer.Child = _embeddedMapPage;
                }
            }
        }

        private void OnEmbeddedMapObjectiveSelected(object? sender, TaskObjectiveWithLocation objective)
        {
            Dispatcher.BeginInvoke(() =>
            {
                OnMapMarkerSelected(objective);
            });
        }

        public void ShowObjectiveOnMap(string mapKey, TaskObjectiveWithLocation objective, string? floorId = null)
        {
            if (string.IsNullOrEmpty(mapKey) && objective?.Locations != null && objective.Locations.Count > 0)
            {
                mapKey = objective.Locations[0].MapName ?? objective.Locations[0].MapNormalizedName ?? "customs";
            }
            if (string.IsNullOrEmpty(mapKey)) mapKey = "customs";

            // Stay inside Quests tab, activate Maps mode
            if (RbViewMaps?.IsChecked != true)
            {
                RbViewMaps!.IsChecked = true;
            }

            SelectMapChipByKey(mapKey);
            MapTrackerService.Instance.SetCurrentMap(mapKey);
            EnsureEmbeddedMap();

            if (_embeddedMapPage != null)
            {
                var targetFloor = floorId ?? objective?.Locations?.FirstOrDefault()?.FloorId;
                if (!string.IsNullOrEmpty(targetFloor))
                {
                    _embeddedMapPage.SelectFloor(targetFloor);
                    MapTrackerService.Instance.SetCurrentFloor(targetFloor);
                }
                if (objective != null)
                {
                    _embeddedMapPage.PinAndLocateObjective(objective);
                    MapTrackerService.Instance.PinObjective(objective);
                }
            }
        }

        public void OnMapMarkerSelected(TaskObjectiveWithLocation objective)
        {
            if (objective == null || LstQuests == null) return;

            var targetVm = LstQuests.Items.OfType<QuestViewModel>().FirstOrDefault(vm =>
                (!string.IsNullOrEmpty(objective.QuestId) && string.Equals(vm.Task?.Id, objective.QuestId, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(objective.TaskNormalizedName) && string.Equals(vm.Task?.NormalizedName, objective.TaskNormalizedName, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(objective.TaskName) && string.Equals(vm.DisplayName, objective.TaskName, StringComparison.OrdinalIgnoreCase)));

            if (targetVm != null)
            {
                LstQuests.SelectedItem = targetVm;
                LstQuests.ScrollIntoView(targetVm);
            }
        }

        public void ApplyQuestFiltersToMapMarkers(QuestFilterCriteria criteria)
        {
            var hiddenIds = TarkovHelper.Services.Settings.MapSettings.Instance.HiddenQuests;
            var visibleQuests = _lastFilteredQuests?.Where(q =>
            {
                var task = q.Task;
                if (task == null) return false;
                if (!string.IsNullOrEmpty(task.NormalizedName) && hiddenIds.Contains(task.NormalizedName)) return false;
                if (!string.IsNullOrEmpty(task.Id) && hiddenIds.Contains(task.Id)) return false;
                if (task.Ids != null && task.Ids.Any(id => hiddenIds.Contains(id))) return false;
                if (!string.IsNullOrEmpty(task.Name) && hiddenIds.Contains(task.Name)) return false;
                return true;
            }).ToList();

            var matchingIds = visibleQuests?.Where(q => q.Task != null && !string.IsNullOrEmpty(q.Task.Id)).Select(q => q.Task!.Id!).ToList();
            var matchingNames = visibleQuests?.Where(q => q.Task != null && !string.IsNullOrEmpty(q.Task.NormalizedName)).Select(q => q.Task!.NormalizedName!).ToList();
            _embeddedMapPage?.ApplyQuestFiltersToMapMarkers(criteria, matchingIds, matchingNames);
        }

        public void SelectMapChipByKey(string mapKey)
        {
            if (string.IsNullOrEmpty(mapKey)) return;
            var chip = MapChips.FirstOrDefault(c => string.Equals(c.Key, mapKey, StringComparison.OrdinalIgnoreCase));
            if (chip != null)
            {
                SelectNavChip(chip);
            }
            else
            {
                _selectedTarget = mapKey;
                ApplyFilters();
            }
        }

        public void NavigateToTaskOnMap(TarkovTask? task, string? targetMapKey = null)
        {
            if (task == null) return;
            var mapKey = targetMapKey ?? task.Maps?.FirstOrDefault() ?? _selectedTarget;
            if (string.IsNullOrEmpty(mapKey)) mapKey = "customs";

            // Stay inside Quests tab, activate Maps mode
            if (RbViewMaps?.IsChecked != true)
            {
                RbViewMaps!.IsChecked = true;
            }

            SelectMapChipByKey(mapKey);
            MapTrackerService.Instance.SetCurrentMap(mapKey);
            EnsureEmbeddedMap();

            var objectives = !string.IsNullOrEmpty(task.NormalizedName)
                ? QuestObjectiveService.Instance.GetObjectivesForTask(task.NormalizedName)
                : null;
            if (objectives == null || objectives.Count == 0)
            {
                if (!string.IsNullOrEmpty(task.Id))
                {
                    objectives = QuestObjectiveService.Instance.GetObjectivesForTaskById(task.Id);
                }
            }

            TaskObjectiveWithLocation? targetObjective = null;
            if (objectives != null && objectives.Count > 0)
            {
                targetObjective = objectives.FirstOrDefault(o => o.Locations.Any(l =>
                    string.Equals(l.MapName, mapKey, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(l.MapNormalizedName, mapKey, StringComparison.OrdinalIgnoreCase))) ?? objectives.FirstOrDefault();
            }

            if (targetObjective == null)
            {
                targetObjective = new TaskObjectiveWithLocation
                {
                    QuestId = task.Id ?? string.Empty,
                    TaskName = task.Name ?? string.Empty,
                    TaskNormalizedName = task.NormalizedName ?? string.Empty,
                    Description = task.Name ?? string.Empty
                };
            }

            var loc = targetObjective.Locations?.FirstOrDefault();
            if (!string.IsNullOrEmpty(loc?.FloorId))
            {
                _embeddedMapPage?.SelectFloor(loc.FloorId);
                MapTrackerService.Instance.SetCurrentFloor(loc.FloorId);
            }

            _embeddedMapPage?.PinAndLocateObjective(targetObjective);
            MapTrackerService.Instance.PinObjective(targetObjective);
        }

        #endregion

        private void BtnCardWiki_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is FrameworkElement btn && btn.Tag is QuestViewModel vm && vm.Task != null)
            {
                var wikiUrl = NormalizedNameGenerator.GetQuestWikiUrl(vm.Task);
                if (!string.IsNullOrEmpty(wikiUrl))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = wikiUrl,
                            UseShellExecute = true
                        });
                    }
                    catch { /* Ignore errors opening browser */ }
                }
            }
        }

        private void UpdateKappaGauge()
        {
            try
            {
                var graphService = QuestGraphService.Instance;
                var (completed, total, percentage) = graphService.GetCollectorProgress(
                    normalizedName => _progressService.IsQuestCompleted(normalizedName));

                TxtKappaGauge.Text = $"{completed}/{total}";
                KappaGaugeBar.Width = (percentage / 100.0) * 120; // 120 is the gauge width
            }
            catch (Exception ex)
            {
                // QuestGraphService not initialized yet or other error
                System.Diagnostics.Debug.WriteLine($"[QuestListPage] UpdateKappaGauge failed: {ex.Message}");
                TxtKappaGauge.Text = "0/0";
                KappaGaugeBar.Width = 0;
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                if (_viewMode == TasksViewMode.Graph)
                {
                    FilterGraphNodes(TxtSearch?.Text);
                }
                ApplyFilters();
            }
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitializing) ApplyFilters();
        }

        private void TabStoryFilter_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (TabStoryFilter.IsChecked == true && CmbTrader.SelectedIndex != 0)
            {
                _isInitializing = true;
                CmbTrader.SelectedIndex = 0;
                _isInitializing = false;
            }

            ApplyFilters();
        }

        private void CmbTrader_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            var selectedTrader = (CmbTrader.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(selectedTrader) &&
                !string.Equals(selectedTrader, "Story", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(selectedTrader, "Story Chapters", StringComparison.OrdinalIgnoreCase) &&
                TabStoryFilter.IsChecked == true)
            {
                _isInitializing = true;
                TabStoryFilter.IsChecked = false;
                _isInitializing = false;
            }

            ApplyFilters();
        }

        public void FilterByTrader(string traderName)
        {
            TraderPanel_TraderFilterRequested(this, traderName);
        }

        public void FilterByKappa()
        {
            if (RbViewList != null)
            {
                RbViewList.IsChecked = true;
                _viewMode = TasksViewMode.List;
                _navMode = NavigationMode.Traders;
                if (IcNavChips != null) IcNavChips.ItemsSource = null;
                if (NavChipsRow != null) NavChipsRow.Visibility = Visibility.Collapsed;
                if (GraphMainContainer != null) GraphMainContainer.Visibility = Visibility.Collapsed;
                if (LstQuests != null)
                {
                    LstQuests.Visibility = Visibility.Visible;
                    LstQuests.ItemTemplate = TryFindResource("QuestItemTemplate") as DataTemplate;
                    LstQuests.ItemContainerStyle = TryFindResource("QuestListBoxItem") as Style;
                }
                if (RowMapView != null) RowMapView.Height = new GridLength(0);
                if (RowSplitter != null) RowSplitter.Height = new GridLength(0);
                if (RowTaskList != null) RowTaskList.Height = new GridLength(1, GridUnitType.Star);
                if (ColTaskList != null) ColTaskList.Width = new GridLength(1, GridUnitType.Star);
                if (ColSplitter != null) ColSplitter.Width = new GridLength(0);
                if (MapSplitter != null) MapSplitter.Visibility = Visibility.Collapsed;
                if (ColMapView != null) ColMapView.Width = new GridLength(0);
                if (EmbeddedMapContainer != null) EmbeddedMapContainer.Visibility = Visibility.Collapsed;
            }

            if (ChkFilterRegular != null) ChkFilterRegular.IsChecked = false;
            if (ChkFilterLightkeeper != null) ChkFilterLightkeeper.IsChecked = false;
            if (ChkKappaChip != null) ChkKappaChip.IsChecked = true;
            if (ChkKappaOnly != null) ChkKappaOnly.IsChecked = true;
            if (TxtSearch != null) TxtSearch.Text = string.Empty;

            if (RbStatusAll != null)
            {
                RbStatusAll.IsChecked = true;
            }

            ApplyFilters();
        }

        private void BtnKappaQuickToggle_Click(object sender, RoutedEventArgs e)
        {
            if (ChkFilterRegular != null) ChkFilterRegular.IsChecked = true;
            if (ChkKappaChip != null) ChkKappaChip.IsChecked = true;
            if (ChkFilterLightkeeper != null) ChkFilterLightkeeper.IsChecked = true;
            if (ChkKappaOnly != null) ChkKappaOnly.IsChecked = false;
            ApplyFilters();
        }

        private void TraderPanel_TraderFilterRequested(object? sender, string traderName)
        {
            if (string.IsNullOrWhiteSpace(traderName)) return;

            if (_navMode != NavigationMode.Traders && RbViewTraders != null)
            {
                RbViewTraders.IsChecked = true;
                SwitchNavigationMode(NavigationMode.Traders);
            }

            var matching = TraderChips.FirstOrDefault(c =>
                string.Equals(c.Key, traderName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.DisplayName, traderName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(QuestProgressService.ResolveTraderFriendlyName(c.Key), traderName, StringComparison.OrdinalIgnoreCase));

            if (matching != null)
            {
                SelectNavChip(matching);
            }
        }

        private void CmbMap_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing) ApplyFilters();
        }

        private void CmbStatus_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing) ApplyFilters();
        }

        private void Faction_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            // Save faction selection (setter automatically saves and notifies listeners)
            var faction = RbBear.IsChecked == true ? "bear" : (RbUsec.IsChecked == true ? "usec" : null);
            SettingsService.Instance.PlayerFaction = faction;

            ApplyFilters();
        }

        private void LoadFactionSelection()
        {
            var savedFaction = SettingsService.Instance.PlayerFaction;
            if (savedFaction == "bear")
            {
                RbBear.IsChecked = true;
            }
            else if (savedFaction == "usec")
            {
                RbUsec.IsChecked = true;
            }
            // If null, neither is selected (default state)
        }

        private void LstQuests_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDetailPanel();
        }

        private void UpdateDetailPanel(QuestViewModel? overrideVm = null)
        {
            var selectedVm = overrideVm ?? LstQuests.SelectedItem as QuestViewModel;

            if (selectedVm == null)
            {
                _currentDetailTask = null;
                DetailPanel.Visibility = Visibility.Collapsed;
                TxtSelectQuest.Visibility = Visibility.Visible;
                if (TxtSelectQuestWrapper != null) TxtSelectQuestWrapper.Visibility = Visibility.Visible;
                NeededKeysList.ItemsSource = null;
                NeededKeysSectionWrapper.Visibility = Visibility.Collapsed;
                DetailTaskCard?.LoadFromTask(new TarkovTask(), QuestStatus.Unavailable);
                return;
            }

            DetailPanel.Visibility = Visibility.Visible;
            TxtSelectQuest.Visibility = Visibility.Collapsed;
            if (TxtSelectQuestWrapper != null) TxtSelectQuestWrapper.Visibility = Visibility.Collapsed;

            var task = selectedVm.Task;
            _currentDetailTask = task;
            var status = _progressService.GetStatus(task);

            // Load into canonical TaskCardControl
            DetailTaskCard?.LoadFromTask(task, status);

            // Show on Map button
            BtnShowOnMap.Visibility = (task.Maps != null && task.Maps.Count > 0) ? Visibility.Visible : Visibility.Collapsed;

            // Title
            var (displayName, subtitle, showSubtitle) = GetLocalizedNames(task);
            TxtDetailName.Text = displayName;
            TxtDetailSubtitle.Text = subtitle;
            TxtDetailSubtitle.Visibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed;

            // Trader & Status
            TxtDetailTrader.Text = _loc.GetLocalizedTraderName(task.Trader);
            TxtDetailStatus.Text = GetStatusText(status, task);
            DetailStatusBadge.Background = GetStatusBrush(status);

            // Maps
            if (task.Maps != null && task.Maps.Count > 0)
            {
                var mapNames = task.Maps.Select(GetLocalizedMapName);
                TxtDetailMap.Text = string.Join(", ", mapNames);
                MapInfoPanel.Visibility = Visibility.Visible;
            }
            else
            {
                TxtDetailMap.Text = "-";
                MapInfoPanel.Visibility = Visibility.Visible;
            }

            // Kappa Progress Section (for Collector quest)
            UpdateKappaProgressSection(task);

            // Requirements - Level with current level comparison
            bool hasLevelRequirement = task.RequiredLevel.HasValue && task.RequiredLevel.Value > 0;
            bool hasScavKarmaRequirement = task.RequiredScavKarma.HasValue;

            if (hasLevelRequirement)
            {
                var playerLevel = SettingsService.Instance.PlayerLevel;
                var reqLevel = task.RequiredLevel!.Value;
                if (playerLevel >= reqLevel)
                {
                    TxtRequiredLevel.Text = $"Lv.{reqLevel} (Current: {playerLevel})";
                    TxtRequiredLevel.Foreground = (Brush)FindResource("TextPrimaryBrush");
                }
                else
                {
                    TxtRequiredLevel.Text = $"Lv.{reqLevel} (Current: {playerLevel})";
                    TxtRequiredLevel.Foreground = LevelLockedBrush;
                }
                TxtRequiredLevel.Visibility = Visibility.Visible;
            }
            else
            {
                TxtRequiredLevel.Visibility = Visibility.Collapsed;
            }

            // Requirements - Scav Karma (Fence reputation)
            if (hasScavKarmaRequirement)
            {
                var playerScavRep = SettingsService.Instance.ScavRep;
                var reqKarma = task.RequiredScavKarma!.Value;
                var isMet = _progressService.IsScavKarmaRequirementMet(task);
                var comparison = reqKarma < 0 ? "≤" : "≥";
                TxtRequiredScavKarma.Text = $"Scav Karma {comparison} {reqKarma:0.#} (Current: {playerScavRep:0.#})";
                TxtRequiredScavKarma.Foreground = isMet ? (Brush)FindResource("TextPrimaryBrush") : LevelLockedBrush;
                TxtRequiredScavKarma.Visibility = Visibility.Visible;
            }
            else
            {
                TxtRequiredScavKarma.Visibility = Visibility.Collapsed;
            }

            // Pool Requirements Section
            bool hasPoolRequirement = false;
            if (task.PoolRequirements != null && task.PoolRequirements.Count > 0)
            {
                var poolReq = task.PoolRequirements[0];
                var (completed, required) = _progressService.GetPoolProgress(task);

                string traderName = !string.IsNullOrEmpty(poolReq.Trader)
                    ? _loc.GetLocalizedTraderName(poolReq.Trader)
                    : (!string.IsNullOrEmpty(task.Trader) ? _loc.GetLocalizedTraderName(task.Trader) : "Trader");

                string levelStr = poolReq.TraderLevel > 0 ? $" LL{poolReq.TraderLevel}" : "";

                TxtRequiredPool.Text = $"Complete at least {required} {traderName}{levelStr} quests ({completed}/{required})";

                var poolTaskItems = new List<PoolTaskItemViewModel>();
                if (poolReq.TaskIds != null)
                {
                    foreach (var tid in poolReq.TaskIds)
                    {
                        var pTask = _progressService.GetTaskById(tid);
                        bool isDone = _progressService.GetStatus(tid) == QuestStatus.Done;
                        string pName = pTask != null ? GetLocalizedNames(pTask).Item1 : tid;
                        poolTaskItems.Add(new PoolTaskItemViewModel
                        {
                            Task = pTask,
                            TaskId = tid,
                            DisplayName = pName,
                            IsDone = isDone
                        });
                    }
                }
                PoolTasksList.ItemsSource = poolTaskItems;
                PoolRequirementSection.Visibility = Visibility.Visible;
                hasPoolRequirement = true;
            }
            else
            {
                PoolRequirementSection.Visibility = Visibility.Collapsed;
            }

            // Show requirements section if any requirement exists
            RequirementsSectionWrapper.Visibility = (hasLevelRequirement || hasScavKarmaRequirement || hasPoolRequirement)
                ? Visibility.Visible
                : Visibility.Collapsed;

            // Prerequisites - show direct prerequisites with OR/AND grouping
            var prereqGroups = new List<PrerequisiteGroupViewModel>();
            bool hasBrokenPrereqs = false;
            bool isStory = string.Equals(task.Trader, "Story", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(task.Trader, "Story Chapters", StringComparison.OrdinalIgnoreCase);
            bool isOverridden = !isStory && IsQuestOverridden(task);
            bool prereqsMet = _progressService.ArePrerequisitesMet(task);

            if (task.TaskRequirements != null && task.TaskRequirements.Count > 0)
            {
                // Group by GroupId: 0 = AND (each as separate group), >0 = OR (same GroupId = same group)
                var andRequirements = task.TaskRequirements.Where(r => r.GroupId == 0).ToList();
                var orGroups = task.TaskRequirements
                    .Where(r => r.GroupId > 0)
                    .GroupBy(r => r.GroupId)
                    .ToList();

                // Add AND requirements (each as separate entry)
                foreach (var req in andRequirements)
                {
                    var reqTask = !string.IsNullOrEmpty(req.TaskId)
                        ? _progressService.GetTaskById(req.TaskId)
                        : _progressService.GetTask(req.TaskNormalizedName);

                    if (reqTask == null)
                    {
                        hasBrokenPrereqs = true;
                        string missingName = !string.IsNullOrEmpty(req.TaskNormalizedName)
                            ? req.TaskNormalizedName
                            : (!string.IsNullOrEmpty(req.TaskId) ? req.TaskId : "Unknown prerequisite");

                        prereqGroups.Add(new PrerequisiteGroupViewModel
                        {
                            GroupId = 0,
                            Items = new List<PrerequisiteItemViewModel>
                            {
                                new PrerequisiteItemViewModel
                                {
                                    Task = null,
                                    DisplayName = $"{missingName} ⚠️ ({_loc.UnresolvedPrerequisiteWarning})",
                                    StatusText = "Unresolved",
                                    StatusBackground = LevelLockedBrush,
                                    IsOrItem = false
                                }
                            }
                        });
                        continue;
                    }

                    var pStatus = _progressService.GetStatus(reqTask);
                    var (pName, _, _) = GetLocalizedNames(reqTask);

                    prereqGroups.Add(new PrerequisiteGroupViewModel
                    {
                        GroupId = 0,
                        Items = new List<PrerequisiteItemViewModel>
                        {
                            new PrerequisiteItemViewModel
                            {
                                Task = reqTask,
                                DisplayName = pName,
                                StatusText = GetStatusText(pStatus),
                                StatusBackground = GetStatusBrush(pStatus),
                                IsOrItem = false
                            }
                        }
                    });
                }

                // Add OR groups (only show as OR group if 2+ items, otherwise treat as AND)
                foreach (var orGroup in orGroups)
                {
                    var orItems = orGroup.ToList();

                    // If OR group has only 1 item, treat it as a regular AND requirement
                    if (orItems.Count == 1)
                    {
                        var req = orItems[0];
                        var reqTask = !string.IsNullOrEmpty(req.TaskId)
                            ? _progressService.GetTaskById(req.TaskId)
                            : _progressService.GetTask(req.TaskNormalizedName);

                        if (reqTask == null)
                        {
                            hasBrokenPrereqs = true;
                            string missingName = !string.IsNullOrEmpty(req.TaskNormalizedName)
                                ? req.TaskNormalizedName
                                : (!string.IsNullOrEmpty(req.TaskId) ? req.TaskId : "Unknown prerequisite");

                            prereqGroups.Add(new PrerequisiteGroupViewModel
                            {
                                GroupId = 0,
                                Items = new List<PrerequisiteItemViewModel>
                                {
                                    new PrerequisiteItemViewModel
                                    {
                                        Task = null,
                                        DisplayName = $"{missingName} ⚠️ ({_loc.UnresolvedPrerequisiteWarning})",
                                        StatusText = "Unresolved",
                                        StatusBackground = LevelLockedBrush,
                                        IsOrItem = false
                                    }
                                }
                            });
                        }
                        else
                        {
                            var pStatus = _progressService.GetStatus(reqTask);
                            var (pName, _, _) = GetLocalizedNames(reqTask);

                            prereqGroups.Add(new PrerequisiteGroupViewModel
                            {
                                GroupId = 0, // Treat as AND (not OR group)
                                Items = new List<PrerequisiteItemViewModel>
                                {
                                    new PrerequisiteItemViewModel
                                    {
                                        Task = reqTask,
                                        DisplayName = pName,
                                        StatusText = GetStatusText(pStatus),
                                        StatusBackground = GetStatusBrush(pStatus),
                                        IsOrItem = false
                                    }
                                }
                            });
                        }
                        continue;
                    }

                    // OR group with 2+ items - display as OR group
                    var groupVm = new PrerequisiteGroupViewModel
                    {
                        GroupId = orGroup.Key,
                        Items = new List<PrerequisiteItemViewModel>()
                    };

                    bool isFirst = true;
                    foreach (var req in orItems)
                    {
                        var reqTask = !string.IsNullOrEmpty(req.TaskId)
                            ? _progressService.GetTaskById(req.TaskId)
                            : _progressService.GetTask(req.TaskNormalizedName);

                        if (reqTask == null)
                        {
                            hasBrokenPrereqs = true;
                            string missingName = !string.IsNullOrEmpty(req.TaskNormalizedName)
                                ? req.TaskNormalizedName
                                : (!string.IsNullOrEmpty(req.TaskId) ? req.TaskId : "Unknown prerequisite");

                            groupVm.Items.Add(new PrerequisiteItemViewModel
                            {
                                Task = null,
                                DisplayName = $"{missingName} ⚠️ ({_loc.UnresolvedPrerequisiteWarning})",
                                StatusText = "Unresolved",
                                StatusBackground = LevelLockedBrush,
                                IsOrItem = !isFirst
                            });
                        }
                        else
                        {
                            var pStatus = _progressService.GetStatus(reqTask);
                            var (pName, _, _) = GetLocalizedNames(reqTask);

                            groupVm.Items.Add(new PrerequisiteItemViewModel
                            {
                                Task = reqTask,
                                DisplayName = pName,
                                StatusText = GetStatusText(pStatus),
                                StatusBackground = GetStatusBrush(pStatus),
                                IsOrItem = !isFirst  // Show "OR" separator for 2nd item onwards
                            });
                        }
                        isFirst = false;
                    }

                    if (groupVm.Items.Count > 0)
                    {
                        prereqGroups.Add(groupVm);
                    }
                }
            }
            else if (task.Previous != null && task.Previous.Count > 0)
            {
                foreach (var prevName in task.Previous)
                {
                    var prevTask = _progressService.GetTask(prevName);
                    if (prevTask == null)
                    {
                        hasBrokenPrereqs = true;
                        prereqGroups.Add(new PrerequisiteGroupViewModel
                        {
                            GroupId = 0,
                            Items = new List<PrerequisiteItemViewModel>
                            {
                                new PrerequisiteItemViewModel
                                {
                                    Task = null,
                                    DisplayName = $"{prevName} ⚠️ ({_loc.UnresolvedPrerequisiteWarning})",
                                    StatusText = "Unresolved",
                                    StatusBackground = LevelLockedBrush,
                                    IsOrItem = false
                                }
                            }
                        });
                        continue;
                    }

                    var pStatus = _progressService.GetStatus(prevTask);
                    var (pName, _, _) = GetLocalizedNames(prevTask);

                    prereqGroups.Add(new PrerequisiteGroupViewModel
                    {
                        GroupId = 0,
                        Items = new List<PrerequisiteItemViewModel>
                        {
                            new PrerequisiteItemViewModel
                            {
                                Task = prevTask,
                                DisplayName = pName,
                                StatusText = GetStatusText(pStatus),
                                StatusBackground = GetStatusBrush(pStatus),
                                IsOrItem = false
                            }
                        }
                    });
                }
            }

            PrerequisitesList.ItemsSource = prereqGroups;

            // Unresolved / broken prerequisite indicator (Feature 10)
            bool showWarning = hasBrokenPrereqs || (isStory && !prereqsMet && prereqGroups.Count > 0);
            TxtUnresolvedWarning.Text = _loc.UnresolvedPrerequisiteWarning;
            UnresolvedPrereqWarningPanel.Visibility = showWarning ? Visibility.Visible : Visibility.Collapsed;

            // Override active notice (Feature 10)
            TxtOverrideActiveNotice.Text = _loc.OverrideActiveRestore;
            OverrideActiveBadgePanel.Visibility = isOverridden ? Visibility.Visible : Visibility.Collapsed;

            // Manual override button (Feature 10)
            if (isOverridden)
            {
                BtnOverridePrereq.Visibility = Visibility.Visible;
                BtnOverridePrereq.Content = _loc.RemoveManualOverride;
            }
            else if (status != QuestStatus.Done && (!prereqsMet || hasBrokenPrereqs))
            {
                BtnOverridePrereq.Visibility = Visibility.Visible;
                BtnOverridePrereq.Content = _loc.OverridePrerequisite;
            }
            else
            {
                BtnOverridePrereq.Visibility = Visibility.Collapsed;
            }

            PrerequisitesSectionWrapper.Visibility = (prereqGroups.Count > 0 || hasBrokenPrereqs || isOverridden)
                ? Visibility.Visible
                : Visibility.Collapsed;

            // Alternative Quests Section (Mutually Exclusive)
            var alternativeQuests = _progressService.GetAlternativeQuests(task);
            if (alternativeQuests.Count > 0)
            {
                var altVms = alternativeQuests.Select(alt =>
                {
                    var altStatus = _progressService.GetStatus(alt);
                    var (displayName, _, _) = GetLocalizedNames(alt);
                    return new
                    {
                        DisplayName = displayName,
                        TraderName = alt.Trader,
                        StatusText = GetStatusText(altStatus, alt),
                        StatusBackground = GetStatusBrush(altStatus)
                    };
                }).ToList();

                AlternativeQuestsList.ItemsSource = altVms;
                AlternativeQuestsSectionWrapper.Visibility = Visibility.Visible;
            }
            else
            {
                AlternativeQuestsList.ItemsSource = null;
                AlternativeQuestsSectionWrapper.Visibility = Visibility.Collapsed;
            }

            // Objectives Section
            UpdateObjectivesSection(task);

            // Guide Section
            UpdateGuideSection(task);

            // Needed Keys (Loaded first so duplicate keys can be filtered from RequiredItems)
            LoadNeededKeys(task);

            // Required Items
            if (task.RequiredItems != null && task.RequiredItems.Count > 0)
            {
                LoadRequiredItems(task.RequiredItems, task);
            }
            else
            {
                RequiredItemsList.ItemsSource = null;
                RequiredItemsSectionWrapper.Visibility = Visibility.Collapsed;
            }

            // Button states
            BtnComplete.Visibility = status == QuestStatus.Done ? Visibility.Collapsed : Visibility.Visible;
            BtnReset.Visibility = status == QuestStatus.Done || status == QuestStatus.Failed
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CompleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is QuestViewModel vm)
            {
                _progressService.CompleteQuest(vm.Task, true);
            }
        }

        private void BtnToggleChapterCompleted_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is QuestViewModel vm && vm.Task != null)
            {
                var currentStatus = _progressService.GetStatus(vm.Task);
                if (currentStatus == QuestStatus.Done)
                {
                    _progressService.ResetQuest(vm.Task);
                }
                else
                {
                    _progressService.CompleteQuest(vm.Task, completePrerequisites: false);
                }
            }
        }

        private void BtnWiki_Click(object sender, RoutedEventArgs e)
        {
            var selectedVm = LstQuests.SelectedItem as QuestViewModel;
            if (selectedVm?.Task == null) return;

            var wikiUrl = NormalizedNameGenerator.GetQuestWikiUrl(selectedVm.Task);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = wikiUrl,
                    UseShellExecute = true
                });
            }
            catch { /* Ignore errors opening browser */ }
        }

        private void BtnShowOnMap_Click(object sender, RoutedEventArgs e)
        {
            var selectedVm = LstQuests.SelectedItem as QuestViewModel;
            if (selectedVm?.Task != null)
            {
                var targetMap = selectedVm.Task.Maps?.FirstOrDefault() ?? "customs";
                NavigateToTaskOnMap(selectedVm.Task, targetMap);
            }
        }

        private void BtnComplete_Click(object sender, RoutedEventArgs e)
        {
            var selectedVm = LstQuests.SelectedItem as QuestViewModel;
            if (selectedVm != null)
            {
                _progressService.CompleteQuest(selectedVm.Task, true);
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            var selectedVm = LstQuests.SelectedItem as QuestViewModel;
            if (selectedVm != null)
            {
                _progressService.ResetQuest(selectedVm.Task);
            }
        }

        private void BtnOverridePrereq_Click(object sender, RoutedEventArgs e)
        {
            var selectedVm = LstQuests.SelectedItem as QuestViewModel;
            if (selectedVm?.Task == null) return;

            var taskId = selectedVm.Task.Ids?.FirstOrDefault() ?? selectedVm.Task.NormalizedName;
            if (string.IsNullOrEmpty(taskId)) return;

            bool isCurrentlyOverridden = IsQuestOverridden(selectedVm.Task);
            _progressService.SetQuestActiveOverride(taskId, !isCurrentlyOverridden);
        }

        #region Kappa Progress Section

        private void UpdateKappaProgressSection(TarkovTask task)
        {
            // Check if this is the Collector quest
            var isCollector = task.NormalizedName?.Equals("collector", StringComparison.OrdinalIgnoreCase) == true;

            if (!isCollector)
            {
                KappaProgressSection.Visibility = Visibility.Collapsed;
                return;
            }

            KappaProgressSection.Visibility = Visibility.Visible;

            // Get Kappa progress
            var graphService = QuestGraphService.Instance;
            var (completed, total, percentage) = graphService.GetCollectorProgress(
                normalizedName => _progressService.IsQuestCompleted(normalizedName));

            // Update progress text
            TxtKappaProgress.Text = $"Kappa Prerequisites: ({completed}/{total} Done)";
            TxtKappaProgressPercent.Text = $"{percentage}%";

            // Update progress bar width
            KappaProgressBar.Width = (percentage / 100.0) * (KappaProgressBar.Parent as Grid)?.ActualWidth ?? 0;

            // If parent grid not yet rendered, set it after layout
            if (KappaProgressBar.Width == 0 && percentage > 0)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var parentGrid = KappaProgressBar.Parent as Grid;
                    if (parentGrid != null)
                    {
                        KappaProgressBar.Width = (percentage / 100.0) * parentGrid.ActualWidth;
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void BtnShowKappaQuests_Click(object sender, RoutedEventArgs e)
        {
            var graphService = QuestGraphService.Instance;
            var kappaQuests = graphService.GetKappaRequiredQuestsWithStatus(
                normalizedName => _progressService.IsQuestCompleted(normalizedName));

            // Create a popup window to show all Kappa required quests
            var popupWindow = new Window
            {
                Title = "Kappa Required Quests",
                Width = 500,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = (Brush)FindResource("BackgroundDarkBrush")
            };

            var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stackPanel = new StackPanel { Margin = new Thickness(16) };

            // Header
            var (completed, total, percentage) = graphService.GetCollectorProgress(
                normalizedName => _progressService.IsQuestCompleted(normalizedName));
            var headerText = new TextBlock
            {
                Text = $"Kappa Required Quests ({completed}/{total})",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("AccentBrush"),
                Margin = new Thickness(0, 0, 0, 16)
            };
            stackPanel.Children.Add(headerText);

            // Quest list
            foreach (var (quest, isCompleted) in kappaQuests)
            {
                var (displayName, _, _) = GetLocalizedNames(quest);
                var questPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

                // Status indicator
                var statusIndicator = new TextBlock
                {
                    Text = isCompleted ? "✓" : "○",
                    FontSize = 14,
                    Foreground = isCompleted ? DoneBrush : (Brush)FindResource("TextSecondaryBrush"),
                    Width = 24,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Quest name
                var questName = new TextBlock
                {
                    Text = displayName,
                    FontSize = 13,
                    Foreground = isCompleted ? (Brush)FindResource("TextSecondaryBrush") : (Brush)FindResource("TextPrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextDecorations = isCompleted ? TextDecorations.Strikethrough : null
                };

                // Trader
                var traderText = new TextBlock
                {
                    Text = $"  ({quest.Trader})",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };

                questPanel.Children.Add(statusIndicator);
                questPanel.Children.Add(questName);
                questPanel.Children.Add(traderText);
                stackPanel.Children.Add(questPanel);
            }

            scrollViewer.Content = stackPanel;
            popupWindow.Content = scrollViewer;
            popupWindow.ShowDialog();
        }

        #endregion

        #region Objectives Section

        private void UpdateObjectivesSection(TarkovTask task)
        {
            ObjectivesList.Children.Clear();

            if (task.Objectives != null && task.Objectives.Count > 0)
            {
                for (int i = 0; i < task.Objectives.Count; i++)
                {
                    var objective = task.Objectives[i];
                    var isCompleted = task.NormalizedName != null &&
                        ObjectiveProgressService.Instance.IsObjectiveCompleted(task.NormalizedName, i);

                    var objectiveElement = CreateObjectiveElement(task, objective, i, isCompleted);
                    ObjectivesList.Children.Add(objectiveElement);
                }
                ObjectivesSection.Visibility = Visibility.Visible;
            }
            else
            {
                ObjectivesSection.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Create an objective element with checkbox, Optional badge, and rich text
        /// </summary>
        private FrameworkElement CreateObjectiveElement(TarkovTask task, string objective, int objectiveIndex, bool isCompleted)
        {
            // Check for (''Optional'') pattern in wiki markup
            var isOptional = WikiMarkupHelper.IsOptional(objective);

            // Remove the optional marker from text
            var cleanedObjective = WikiMarkupHelper.RemoveOptionalMarker(objective);

            // Main container grid with 2 columns: Checkbox, Text/Badge
            var mainContainer = new Grid
            {
                Margin = new Thickness(0, 3, 0, 3)
            };
            mainContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mainContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Checkbox
            var checkBox = new CheckBox
            {
                IsChecked = isCompleted,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 8, 0),
                Tag = objectiveIndex
            };
            checkBox.Checked += ObjectiveCheckBox_Changed;
            checkBox.Unchecked += ObjectiveCheckBox_Changed;
            Grid.SetColumn(checkBox, 0);
            mainContainer.Children.Add(checkBox);

            // Get resources for text styling
            var fontFamily = (FontFamily)FindResource("MaplestoryFont");
            var fontSize = (double)FindResource("FontSizeXSmall");
            var defaultBrush = (Brush)FindResource("TextPrimaryBrush");
            var accentBrush = (Brush)FindResource("AccentBrush");

            FrameworkElement textElement;

            if (isOptional)
            {
                // Create horizontal layout with Optional badge + text
                var contentContainer = new StackPanel
                {
                    Orientation = Orientation.Horizontal
                };

                // Optional badge
                var badge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(60, 255, 193, 7)), // Amber/yellow with transparency
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };

                var badgeText = new TextBlock
                {
                    Text = "Optional",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 193, 7)) // Amber color
                };

                badge.Child = badgeText;
                contentContainer.Children.Add(badge);

                // Create text block without bullet
                var textBlock = WikiMarkupHelper.CreateRichTextBlockWithoutBullet(
                    cleanedObjective, fontFamily, fontSize, defaultBrush, accentBrush, isCompleted);
                contentContainer.Children.Add(textBlock);

                textElement = contentContainer;
            }
            else
            {
                // Create text block without bullet
                textElement = WikiMarkupHelper.CreateRichTextBlockWithoutBullet(
                    cleanedObjective, fontFamily, fontSize, defaultBrush, accentBrush, isCompleted);
            }

            Grid.SetColumn(textElement, 1);
            mainContainer.Children.Add(textElement);

            return mainContainer;
        }

        private FrameworkElement CreateObjectiveElement(string objective, int objectiveIndex, bool isCompleted)
        {
            return CreateObjectiveElement(_currentDetailTask ?? new TarkovTask(), objective, objectiveIndex, isCompleted);
        }

        private void ObjectiveCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_currentDetailTask?.NormalizedName == null) return;

            var checkBox = sender as CheckBox;
            if (checkBox?.Tag is int objectiveIndex)
            {
                var isCompleted = checkBox.IsChecked ?? false;

                var objectiveId = ObjectiveProgressService.Instance.ResolveObjectiveId(
                    _currentDetailTask.NormalizedName, objectiveIndex, null, _currentDetailTask);

                ObjectiveProgressService.Instance.SetObjectiveCompleted(
                    _currentDetailTask.NormalizedName,
                    objectiveIndex,
                    isCompleted,
                    objectiveId);

                // Update the text style (strikethrough)
                if (checkBox.Parent is FrameworkElement parent)
                {
                    UpdateObjectiveTextStyle(parent, isCompleted);
                }
            }
        }

        internal static void UpdateObjectiveTextStyle(FrameworkElement container, bool isCompleted)
        {
            if (container is TextBlock textBlock)
            {
                textBlock.TextDecorations = isCompleted ? TextDecorations.Strikethrough : null;
                textBlock.Opacity = isCompleted ? 0.6 : 1.0;
            }
            else if (container is Border border)
            {
                border.Opacity = isCompleted ? 0.6 : 1.0;
            }
            else if (container is Panel panel)
            {
                foreach (UIElement child in panel.Children)
                {
                    if (child is FrameworkElement fe && !(child is CheckBox))
                    {
                        UpdateObjectiveTextStyle(fe, isCompleted);
                    }
                }
            }
        }

        #endregion

        #region Guide Section

        private void UpdateGuideSection(TarkovTask task)
        {
            var hasGuideText = !string.IsNullOrEmpty(task.GuideText);
            var hasGuideImages = task.GuideImages != null && task.GuideImages.Count > 0;

            if (!hasGuideText && !hasGuideImages)
            {
                GuideSection.Visibility = Visibility.Collapsed;
                return;
            }

            GuideSection.Visibility = Visibility.Visible;

            // Guide text - show directly in scrollable text block
            if (hasGuideText)
            {
                TxtGuideText.Text = task.GuideText;
                GuideTextSection.Visibility = Visibility.Visible;
            }
            else
            {
                GuideTextSection.Visibility = Visibility.Collapsed;
            }

            // Guide images - lazy load when expander is opened
            if (hasGuideImages)
            {
                _pendingGuideImages = task.GuideImages;
                _guideImagesLoaded = false;
                GuideImagesList.ItemsSource = null; // Clear previous images
                GuideImagesExpander.IsExpanded = false; // Reset to collapsed
                GuideImagesExpander.Visibility = Visibility.Visible;
                TxtGuideImagesHeader.Text = $"Guide Images ({task.GuideImages!.Count})";
            }
            else
            {
                _pendingGuideImages = null;
                GuideImagesExpander.Visibility = Visibility.Collapsed;
                GuideImagesList.ItemsSource = null;
            }
        }

        private void GuideImagesExpander_Expanded(object sender, RoutedEventArgs e)
        {
            // Load images only when expander is opened for the first time
            if (!_guideImagesLoaded && _pendingGuideImages != null)
            {
                _guideImagesLoaded = true;
                _ = LoadGuideImagesAsync(_pendingGuideImages);
            }
        }

        private async Task LoadGuideImagesAsync(List<GuideImage> guideImages)
        {
            // Create ViewModels with loading state first
            var imageVms = new System.Collections.ObjectModel.ObservableCollection<GuideImageViewModel>();

            foreach (var guideImage in guideImages)
            {
                imageVms.Add(new GuideImageViewModel
                {
                    FileName = guideImage.FileName,
                    Caption = guideImage.Caption,
                    IsLoading = true
                });
            }

            // Set ItemsSource immediately to show placeholders
            if (!_isUnloaded)
            {
                Dispatcher.Invoke(() =>
                {
                    if (!_isUnloaded)
                        GuideImagesList.ItemsSource = imageVms;
                });
            }

            // Load images in parallel for better performance
            var loadTasks = imageVms.Select(async (vm, index) =>
            {
                if (_isUnloaded) return;

                var image = await _imageCache.GetWikiImageAsync(vm.FileName);

                // Update on UI thread
                if (!_isUnloaded)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!_isUnloaded)
                        {
                            vm.ImageSource = image;
                            vm.IsLoading = false;
                        }
                    });
                }
            });

            await Task.WhenAll(loadTasks);
        }

        private void GuideImage_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is GuideImageViewModel vm)
            {
                // Open wiki image in browser
                var encodedFileName = Uri.EscapeDataString(vm.FileName.Replace(" ", "_"));
                var url = $"https://escapefromtarkov.fandom.com/wiki/File:{encodedFileName}";

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Ignore errors opening browser
                }
            }
        }

        #endregion

        #region Required Items with Localization

        private void LoadRequiredItems(List<QuestItem> requiredItems, TarkovTask? currentTask = null)
        {
            var itemVms = new List<RequiredItemViewModel>();

            List<NeededKeyInfo>? neededKeys = null;
            if (currentTask != null)
            {
                try
                {
                    neededKeys = NeededKeysService.Instance.GetNeededKeysForQuest(currentTask);
                }
                catch { }
            }

            // Consolidate duplicate required items for the same quest
            var consolidatedItems = new List<QuestItem>();
            foreach (var qi in requiredItems)
            {
                var existing = consolidatedItems.FirstOrDefault(c =>
                    string.Equals(c.ItemNormalizedName, qi.ItemNormalizedName, StringComparison.OrdinalIgnoreCase) &&
                    c.FoundInRaid == qi.FoundInRaid &&
                    c.DogtagMinLevel == qi.DogtagMinLevel);
                if (existing != null)
                {
                    existing.Amount += qi.Amount;
                }
                else
                {
                    consolidatedItems.Add(new QuestItem
                    {
                        ItemNormalizedName = qi.ItemNormalizedName,
                        ItemDisplayName = qi.ItemDisplayName,
                        Amount = qi.Amount,
                        FoundInRaid = qi.FoundInRaid,
                        Requirement = qi.Requirement,
                        DogtagMinLevel = qi.DogtagMinLevel
                    });
                }
            }

            foreach (var item in consolidatedItems)
            {
                if (_isUnloaded) return; // Check if page was unloaded

                // Get item from lookup (for ItemId and localized name)
                var tarkovItem = GetItemByNormalizedName(item.ItemNormalizedName, item.ItemDisplayName);

                // Exclude item if it is a key OR if it is already presented in the Needed Keys section
                bool isKeyItem = (tarkovItem != null && (
                    string.Equals(tarkovItem.Category, "Keys", StringComparison.OrdinalIgnoreCase) ||
                    tarkovItem.Name.EndsWith(" key", StringComparison.OrdinalIgnoreCase) ||
                    tarkovItem.Name.EndsWith(" Keycard", StringComparison.OrdinalIgnoreCase) ||
                    tarkovItem.Name.Contains(" key ", StringComparison.OrdinalIgnoreCase))) ||
                    item.ItemDisplayName.EndsWith(" key", StringComparison.OrdinalIgnoreCase) ||
                    item.ItemDisplayName.EndsWith(" Keycard", StringComparison.OrdinalIgnoreCase) ||
                    item.ItemDisplayName.Contains(" key ", StringComparison.OrdinalIgnoreCase);

                if (isKeyItem)
                {
                    continue;
                }

                if (neededKeys != null && neededKeys.Count > 0)
                {
                    bool isDuplicateKey = neededKeys.Any(k =>
                        (!string.IsNullOrEmpty(k.KeyId) && !string.IsNullOrEmpty(tarkovItem?.Id) && string.Equals(k.KeyId, tarkovItem.Id, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(k.NormalizedName) && string.Equals(k.NormalizedName, item.ItemNormalizedName, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(k.Name) && string.Equals(k.Name, item.ItemDisplayName, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(k.ShortName) && string.Equals(k.ShortName, item.ItemDisplayName, StringComparison.OrdinalIgnoreCase)) ||
                        (k.AlternativeKeys != null && !string.IsNullOrEmpty(tarkovItem?.Id) && k.AlternativeKeys.Contains(tarkovItem.Id, StringComparer.OrdinalIgnoreCase)));

                    if (isDuplicateKey)
                    {
                        continue;
                    }
                }

                // Calculate fulfillment status
                var requiredFir = item.FoundInRaid ? item.Amount : 0;
                var fulfillmentInfo = _inventoryService.GetFulfillmentInfo(
                    item.ItemNormalizedName, item.Amount, requiredFir);
                var isFulfilled = fulfillmentInfo.Status == Models.ItemFulfillmentStatus.Fulfilled;

                string stashText;
                if (item.FoundInRaid)
                {
                    stashText = fulfillmentInfo.IsFirFulfilled
                        ? $"✓ {fulfillmentInfo.OwnedFir}/{item.Amount} FIR"
                        : $"{fulfillmentInfo.OwnedFir}/{item.Amount} FIR";
                }
                else
                {
                    stashText = isFulfilled
                        ? $"✓ {fulfillmentInfo.OwnedTotal}/{item.Amount}"
                        : $"{fulfillmentInfo.OwnedTotal}/{item.Amount}";
                }

                string craftingText = string.Empty;
                try
                {
                    var integrated = IntegratedItemService.Instance.GetItemRequirement(item.ItemNormalizedName);
                    if (integrated != null && integrated.HideoutSources.Count > 0)
                    {
                        var hSource = integrated.HideoutSources[0];
                        craftingText = $"🔨 {hSource.StationName} Lv {hSource.Level}";
                    }
                }
                catch { }

// Item looked up above

                var vm = new RequiredItemViewModel
                {
                    FoundInRaid = item.FoundInRaid,
                    RequirementType = item.Requirement,
                    ItemId = tarkovItem?.Id ?? string.Empty, // Use ItemId for navigation
                    ItemNormalizedName = item.ItemNormalizedName,
                    IsFulfilled = isFulfilled,
                    StashProgressText = stashText,
                    CraftingLinkageText = craftingText
                };

                // Get localized item name (with display name fallback)
                var localizedName = GetLocalizedItemName(item.ItemNormalizedName, item.ItemDisplayName);
                vm.DisplayText = $"{localizedName} x{item.Amount}";

                // Get item icon
                if (!string.IsNullOrEmpty(tarkovItem?.Id))
                {
                    var icon = _imageCache.GetLocalItemIcon(tarkovItem.Id);
                    vm.IconSource = icon;
                }

                itemVms.Add(vm);
            }

            RequiredItemsList.ItemsSource = itemVms;
            RequiredItemsSectionWrapper.Visibility = itemVms.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Handle click on item name to navigate to Items tab
        /// </summary>
        private void ItemName_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is RequiredItemViewModel vm)
            {
                if (string.IsNullOrEmpty(vm.ItemId)) return;

                var mainWindow = Window.GetWindow(this) as MainWindow;
                mainWindow?.NavigateToItem(vm.ItemId);
            }
        }

        #endregion

        #region Needed Keys with Navigation

        private void LoadNeededKeys(TarkovTask task)
        {
            var keys = NeededKeysService.Instance.GetNeededKeysForQuest(task);
            if (keys != null && keys.Count > 0)
            {
                var keyVms = new List<NeededKeyViewModel>();
                foreach (var k in keys)
                {
                    if (_isUnloaded) return;
                    var tarkovItem = GetItemByNormalizedName(k.NormalizedName ?? string.Empty, k.Name);
                    var resolvedKeyId = tarkovItem?.Id ?? k.KeyId;

                    var keyVm = new NeededKeyViewModel
                    {
                        KeyId = resolvedKeyId,
                        Name = k.Name,
                        ShortName = k.ShortName,
                        MapOrObjectiveInfo = !string.IsNullOrEmpty(k.MapName) && k.MapName != "Any Map"
                            ? $"{k.MapName} • {k.ObjectiveDescription}"
                            : (k.ObjectiveDescription ?? string.Empty),
                        InStash = k.InStash,
                        StashCount = k.StashQuantity
                    };

                    if (!string.IsNullOrEmpty(resolvedKeyId))
                    {
                        keyVm.IconSource = _imageCache.GetLocalItemIcon(resolvedKeyId);
                    }

                    keyVms.Add(keyVm);
                }

                NeededKeysList.ItemsSource = keyVms;
                if (TxtNeededKeysCount != null)
                {
                    TxtNeededKeysCount.Text = keyVms.Count.ToString();
                    int inStashCount = keyVms.Count(k => k.InStash);
                    NeededKeysCountBorder.Background = inStashCount == keyVms.Count
                        ? new SolidColorBrush(Color.FromRgb(46, 125, 50))
                        : (inStashCount > 0 ? new SolidColorBrush(Color.FromRgb(255, 152, 0)) : new SolidColorBrush(Color.FromRgb(66, 66, 66)));
                }
                NeededKeysSectionWrapper.Visibility = Visibility.Visible;
            }
            else
            {
                NeededKeysList.ItemsSource = null;
                NeededKeysSectionWrapper.Visibility = Visibility.Collapsed;
            }
        }

        private void NeededKey_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is NeededKeyViewModel vm)
            {
                if (string.IsNullOrEmpty(vm.KeyId)) return;

                var mainWindow = Window.GetWindow(this) as MainWindow;
                mainWindow?.NavigateToItem(vm.KeyId);
            }
        }

        private string GetLocalizedItemName(string normalizedName, string? displayNameFallback = null)
        {
            var item = GetItemByNormalizedName(normalizedName, displayNameFallback);
            if (item == null)
                return !string.IsNullOrEmpty(displayNameFallback) ? displayNameFallback : normalizedName;

            var lang = _loc.CurrentLanguage;
            if (lang == AppLanguage.KO && !string.IsNullOrEmpty(item.NameKo))
                return item.NameKo;
            if (lang == AppLanguage.JA && !string.IsNullOrEmpty(item.NameJa))
                return item.NameJa;

            return item.Name;
        }

        private TarkovItem? GetItemByNormalizedName(string normalizedName, string? displayName = null)
        {
            if (_itemLookup == null)
                return null;

            // Strategy 1: Direct lookup
            if (_itemLookup.TryGetValue(normalizedName, out var item))
                return item;

            // Strategy 2: Try alternatives from display name
            if (!string.IsNullOrEmpty(displayName))
            {
                var displayAlternatives = NormalizedNameGenerator.GenerateAlternatives(displayName);
                foreach (var alt in displayAlternatives)
                {
                    if (_itemLookup.TryGetValue(alt, out item))
                        return item;
                }
            }

            // Strategy 3: Try with alternative names from normalized name (fuzzy match)
            var alternatives = NormalizedNameGenerator.GenerateAlternatives(normalizedName);
            foreach (var alt in alternatives)
            {
                if (_itemLookup.TryGetValue(alt, out item))
                    return item;
            }

            return null;
        }

        #endregion

        #region Nested ScrollViewer Scroll Propagation

        /// <summary>
        /// Handle mouse wheel events on nested ScrollViewers to propagate to parent when at scroll limits
        /// </summary>
        private void NestedScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer) return;

            // Check if the nested ScrollViewer can handle this scroll
            var canScrollDown = scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;
            var canScrollUp = scrollViewer.VerticalOffset > 0;
            var scrollingDown = e.Delta < 0;
            var scrollingUp = e.Delta > 0;

            // If the nested ScrollViewer cannot scroll in the direction of the wheel,
            // or if there's no scrollable content, propagate to parent
            bool shouldPropagate = false;

            if (scrollViewer.ScrollableHeight <= 0)
            {
                // No scrollable content, propagate to parent
                shouldPropagate = true;
            }
            else if (scrollingDown && !canScrollDown)
            {
                // At bottom, trying to scroll down - propagate
                shouldPropagate = true;
            }
            else if (scrollingUp && !canScrollUp)
            {
                // At top, trying to scroll up - propagate
                shouldPropagate = true;
            }

            if (shouldPropagate)
            {
                // Don't handle the event, let it bubble up to parent ScrollViewer
                e.Handled = false;

                // Manually raise the event on the parent ScrollViewer
                var parentScrollViewer = DetailScrollViewer;
                if (parentScrollViewer != null)
                {
                    var newEventArgs = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                    {
                        RoutedEvent = UIElement.MouseWheelEvent,
                        Source = sender
                    };
                    parentScrollViewer.RaiseEvent(newEventArgs);
                    e.Handled = true;
                }
            }
        }

        #endregion

        #region Quest Recommendations

        private void UpdateRecommendations()
        {
            // Panel is hidden per user preference, skip calculation
        }

        private void RecommendationsPanel_RecommendationClicked(object? sender, string questNormalizedName)
        {
            SelectQuestInternal(questNormalizedName);
        }

        #endregion

        #region Prerequisite Quest Navigation

        /// <summary>
        /// Handle click on prerequisite quest name to navigate to that quest
        /// </summary>
        private void PrerequisiteQuest_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is PrerequisiteItemViewModel vm)
            {
                if (vm.Task != null)
                {
                    SelectQuest(vm.Task);
                }
                else if (vm.Task?.NormalizedName != null)
                {
                    SelectQuestInternal(vm.Task.NormalizedName);
                }
            }
        }

        /// <summary>
        /// Handle click on pool task requirement to navigate to that quest
        /// </summary>
        private void PoolTask_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is PoolTaskItemViewModel vm)
            {
                if (vm.Task != null)
                {
                    SelectQuest(vm.Task);
                }
                else if (!string.IsNullOrEmpty(vm.TaskId))
                {
                    SelectQuest(vm.TaskId);
                }
            }
        }

        #endregion
    
        private void OnTaskCardHideClicked(object? sender, RoutedEventArgs e)
        {
            ApplyFilters();
        }

        private void BtnToggleHiddenTasks_Click(object sender, MouseButtonEventArgs e)
        {
            if (LstHiddenQuests != null)
            {
                var isExpanded = LstHiddenQuests.Visibility == Visibility.Visible;
                LstHiddenQuests.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
                if (TxtHiddenTasksChevron != null)
                {
                    TxtHiddenTasksChevron.Text = isExpanded ? "›" : "⌄";
                }
            }
        }

        private void BtnUnhideAll_Click(object sender, RoutedEventArgs e)
        {
            TarkovHelper.Services.Settings.MapSettings.Instance.ClearHiddenQuests();
            ApplyFilters();
        }


        #region Visual Task Progression Graph

        private void RefreshGraphView(bool resetPanZoom = true)
        {
            if (_progressService.AllTasks == null || _progressService.AllTasks.Count == 0) return;
            if (GraphNodesCanvas == null || GraphEdgesCanvas == null || GraphCanvas == null) return;

            var statusFunc = new Func<string, QuestStatus>(norm =>
            {
                var task = _progressService.AllTasks.FirstOrDefault(t =>
                    string.Equals(t.NormalizedName, norm, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.Id, norm, StringComparison.OrdinalIgnoreCase));
                return task != null ? _progressService.GetStatus(task) : QuestStatus.Locked;
            });

            var searchQuery = TxtSearch?.Text?.Trim();
            _activeGraphLayout = QuestGraphLayoutEngine.BuildLayout(_progressService.AllTasks, _activeGraphTraderOrMode, statusFunc, searchQuery);

            if (TxtGraphStatsTitle != null)
            {
                TxtGraphStatsTitle.Text = _activeGraphLayout.ModeTitle.ToUpper();
            }
            if (TxtGraphStatsDetails != null)
            {
                TxtGraphStatsDetails.Text = $"{_activeGraphLayout.TotalQuests} Tasks • {_activeGraphLayout.CompletedQuests} Done • {_activeGraphLayout.AvailableQuests} Ready • {_activeGraphLayout.LockedQuests} Locked";
            }

            GraphEdgesCanvas.Children.Clear();
            GraphNodesCanvas.Children.Clear();
            _nodeControlMap.Clear();
            _edgePathMap.Clear();

            GraphCanvas.Width = Math.Max(_activeGraphLayout.TotalWidth + 200, 3000);
            GraphCanvas.Height = Math.Max(_activeGraphLayout.TotalHeight + 200, 2000);
            GraphEdgesCanvas.Width = GraphCanvas.Width;
            GraphEdgesCanvas.Height = GraphCanvas.Height;
            GraphNodesCanvas.Width = GraphCanvas.Width;
            GraphNodesCanvas.Height = GraphCanvas.Height;

            // Render Edges
            foreach (var edge in _activeGraphLayout.Edges)
            {
                var path = new System.Windows.Shapes.Path
                {
                    Data = edge.Geometry,
                    Stroke = edge.StrokeBrush,
                    StrokeThickness = edge.StrokeThickness,
                    Opacity = edge.Opacity,
                    Tag = edge
                };
                GraphEdgesCanvas.Children.Add(path);
                var key = $"{edge.SourceNode.NormalizedName}->{edge.TargetNode.NormalizedName}";
                _edgePathMap[key] = path;
            }

            // Render Nodes
            foreach (var node in _activeGraphLayout.Nodes)
            {
                var ctrl = new TarkovHelper.Controls.QuestGraphNodeControl
                {
                    DataContext = node,
                    Width = node.Width,
                    Height = node.Height
                };
                ctrl.NodeClicked += OnGraphNodeClicked;
                ctrl.NodeHoverChanged += OnGraphNodeHoverChanged;
                ctrl.CrossTraderClicked += OnGraphCrossTraderClicked;

                Canvas.SetLeft(ctrl, node.X);
                Canvas.SetTop(ctrl, node.Y);

                GraphNodesCanvas.Children.Add(ctrl);
                _nodeControlMap[node.NormalizedName] = ctrl;
            }

            if (resetPanZoom)
            {
                ResetGraphPanAndZoom();
            }
        }

        private void ResetGraphPanAndZoom()
        {
            _currentGraphZoom = 1.0;
            if (GraphScaleTransform != null)
            {
                GraphScaleTransform.ScaleX = 1.0;
                GraphScaleTransform.ScaleY = 1.0;
            }
            if (GraphTranslateTransform != null)
            {
                GraphTranslateTransform.X = 20;
                GraphTranslateTransform.Y = 40;
            }
            if (BtnGraphZoomReset != null) BtnGraphZoomReset.Content = "100%";
        }

        private void BtnGraphFit_Click(object sender, RoutedEventArgs e)
        {
            if (_activeGraphLayout == null || GraphViewport == null) return;
            double vpW = GraphViewport.ActualWidth;
            double vpH = GraphViewport.ActualHeight;
            if (vpW <= 100 || vpH <= 100) return;

            double layoutW = _activeGraphLayout.TotalWidth;
            double layoutH = _activeGraphLayout.TotalHeight;

            double scaleX = (vpW - 60) / layoutW;
            double scaleY = (vpH - 60) / layoutH;
            double fitScale = Math.Clamp(Math.Min(scaleX, scaleY), GraphMinZoom, 1.20);

            _currentGraphZoom = fitScale;
            if (GraphScaleTransform != null)
            {
                GraphScaleTransform.ScaleX = fitScale;
                GraphScaleTransform.ScaleY = fitScale;
            }
            if (GraphTranslateTransform != null)
            {
                GraphTranslateTransform.X = 20;
                GraphTranslateTransform.Y = 20;
            }
            if (BtnGraphZoomReset != null) BtnGraphZoomReset.Content = $"{(int)Math.Round(fitScale * 100)}%";
        }

        private void SetGraphZoom(double newScale, Point? centerOnViewport = null)
        {
            newScale = Math.Clamp(newScale, GraphMinZoom, GraphMaxZoom);
            if (GraphScaleTransform == null || GraphTranslateTransform == null || GraphViewport == null) return;

            double currentScale = GraphScaleTransform.ScaleX;
            if (Math.Abs(currentScale - newScale) < 0.001) return;

            Point center = centerOnViewport ?? new Point(GraphViewport.ActualWidth / 2.0, GraphViewport.ActualHeight / 2.0);
            double factor = newScale / currentScale;

            GraphTranslateTransform.X = center.X - (center.X - GraphTranslateTransform.X) * factor;
            GraphTranslateTransform.Y = center.Y - (center.Y - GraphTranslateTransform.Y) * factor;

            GraphScaleTransform.ScaleX = newScale;
            GraphScaleTransform.ScaleY = newScale;
            _currentGraphZoom = newScale;

            if (BtnGraphZoomReset != null) BtnGraphZoomReset.Content = $"{(int)Math.Round(newScale * 100)}%";
        }

        private void BtnGraphZoomIn_Click(object sender, RoutedEventArgs e)
        {
            SetGraphZoom(_currentGraphZoom * 1.20);
        }

        private void BtnGraphZoomOut_Click(object sender, RoutedEventArgs e)
        {
            SetGraphZoom(_currentGraphZoom * 0.83);
        }

        private void BtnGraphZoomReset_Click(object sender, RoutedEventArgs e)
        {
            SetGraphZoom(1.0);
        }

        private void GraphViewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            Point mousePos = e.GetPosition(GraphViewport);
            double factor = e.Delta > 0 ? 1.15 : 0.87;
            SetGraphZoom(_currentGraphZoom * factor, mousePos);
            e.Handled = true;
        }

        private void GraphViewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed)
            {
                _isGraphPanning = true;
                _lastGraphPanPoint = e.GetPosition(GraphViewport);
                GraphViewport.CaptureMouse();
                GraphViewport.Cursor = Cursors.SizeAll;
            }
        }

        private void GraphViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isGraphPanning && GraphTranslateTransform != null)
            {
                Point currentPoint = e.GetPosition(GraphViewport);
                double dx = currentPoint.X - _lastGraphPanPoint.X;
                double dy = currentPoint.Y - _lastGraphPanPoint.Y;

                GraphTranslateTransform.X += dx;
                GraphTranslateTransform.Y += dy;
                _lastGraphPanPoint = currentPoint;
            }
        }

        private void GraphViewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isGraphPanning)
            {
                _isGraphPanning = false;
                GraphViewport.ReleaseMouseCapture();
                GraphViewport.Cursor = Cursors.Arrow;
            }
        }

        private void GraphViewport_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isGraphPanning)
            {
                _isGraphPanning = false;
                GraphViewport.ReleaseMouseCapture();
                GraphViewport.Cursor = Cursors.Arrow;
            }
        }

        private void OnGraphNodeClicked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is TarkovHelper.Controls.QuestGraphNodeControl ctrl && ctrl.Node != null)
            {
                SelectGraphNode(ctrl.Node);
            }
        }

        private void SelectGraphNode(QuestGraphNode node)
        {
            _selectedGraphNode = node;

            if (_activeGraphLayout != null)
            {
                foreach (var n in _activeGraphLayout.Nodes)
                {
                    n.IsSelected = (n == node);
                }
            }

            if (GraphDetailDrawer != null && GraphDetailTaskCard != null)
            {
                if (TxtGraphDrawerTrader != null) TxtGraphDrawerTrader.Text = node.Trader.ToUpper();
                if (TxtGraphDrawerQuestName != null) TxtGraphDrawerQuestName.Text = node.Name;
                GraphDetailTaskCard.LoadFromTask(node.Task, node.Status);
                GraphDetailDrawer.Visibility = Visibility.Visible;
            }

            HighlightNodeDependencies(node);
        }

        private void BtnCloseGraphDrawer_Click(object sender, RoutedEventArgs e)
        {
            if (GraphDetailDrawer != null)
            {
                GraphDetailDrawer.Visibility = Visibility.Collapsed;
            }
            if (_selectedGraphNode != null)
            {
                _selectedGraphNode.IsSelected = false;
                _selectedGraphNode = null;
            }
            ClearGraphHighlights();
        }

        private void OnGraphNodeHoverChanged(object sender, RoutedEventArgs e)
        {
            if (_selectedGraphNode != null) return;

            if (e.OriginalSource is TarkovHelper.Controls.QuestGraphNodeControl ctrl && ctrl.Node != null)
            {
                HighlightNodeDependencies(ctrl.Node);
            }
            else
            {
                ClearGraphHighlights();
            }
        }

        private void HighlightNodeDependencies(QuestGraphNode targetNode)
        {
            if (_activeGraphLayout == null) return;

            var connectedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { targetNode.NormalizedName };
            var activeEdges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var prev in targetNode.DirectPrerequisites)
            {
                connectedNodes.Add(prev);
                activeEdges.Add($"{prev}->{targetNode.NormalizedName}");
            }
            foreach (var next in targetNode.DirectFollowUps)
            {
                connectedNodes.Add(next);
                activeEdges.Add($"{targetNode.NormalizedName}->{next}");
            }

            foreach (var n in _activeGraphLayout.Nodes)
            {
                bool isConnected = connectedNodes.Contains(n.NormalizedName);
                n.IsHighlighted = isConnected;
                n.IsDimmed = !isConnected;
            }

            foreach (var edge in _activeGraphLayout.Edges)
            {
                var key = $"{edge.SourceNode.NormalizedName}->{edge.TargetNode.NormalizedName}";
                bool isEdgeActive = activeEdges.Contains(key);
                edge.IsHighlighted = isEdgeActive;
                edge.IsDimmed = !isEdgeActive;

                if (_edgePathMap.TryGetValue(key, out var path))
                {
                    path.Stroke = edge.StrokeBrush;
                    path.StrokeThickness = edge.StrokeThickness;
                    path.Opacity = edge.Opacity;
                }
            }
        }

        private void ClearGraphHighlights()
        {
            if (_activeGraphLayout == null) return;

            foreach (var n in _activeGraphLayout.Nodes)
            {
                n.IsHighlighted = false;
                n.IsDimmed = false;
            }

            foreach (var edge in _activeGraphLayout.Edges)
            {
                edge.IsHighlighted = false;
                edge.IsDimmed = false;
                var key = $"{edge.SourceNode.NormalizedName}->{edge.TargetNode.NormalizedName}";
                if (_edgePathMap.TryGetValue(key, out var path))
                {
                    path.Stroke = edge.StrokeBrush;
                    path.StrokeThickness = edge.StrokeThickness;
                    path.Opacity = edge.Opacity;
                }
            }
        }

        private void OnGraphCrossTraderClicked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is CrossTraderPrerequisite req)
            {
                _activeGraphTraderOrMode = req.Trader;
                foreach (var c in TraderChips)
                {
                    c.IsSelected = string.Equals(c.Key, req.Trader, StringComparison.OrdinalIgnoreCase);
                }

                RefreshGraphView(resetPanZoom: false);

                if (_nodeControlMap.TryGetValue(req.NormalizedName, out var ctrl) && ctrl.Node != null)
                {
                    SelectGraphNode(ctrl.Node);
                    CenterOnNode(ctrl.Node);
                }
            }
        }

        private void CenterOnNode(QuestGraphNode node)
        {
            if (GraphViewport == null || GraphTranslateTransform == null || GraphScaleTransform == null) return;
            double vpW = GraphViewport.ActualWidth;
            double vpH = GraphViewport.ActualHeight;

            double targetX = -(node.X * _currentGraphZoom) + (vpW / 2.0) - (node.Width * _currentGraphZoom / 2.0);
            double targetY = -(node.Y * _currentGraphZoom) + (vpH / 2.0) - (node.Height * _currentGraphZoom / 2.0);

            GraphTranslateTransform.X = targetX;
            GraphTranslateTransform.Y = targetY;
        }

        private void UpdateGraphNodeStatuses()
        {
            if (_activeGraphLayout == null || _progressService.AllTasks == null) return;

            foreach (var node in _activeGraphLayout.Nodes)
            {
                node.Status = _progressService.GetStatus(node.Task);
                foreach (var cross in node.CrossTraderPrerequisites)
                {
                    var task = _progressService.AllTasks.FirstOrDefault(t => string.Equals(t.NormalizedName, cross.NormalizedName, StringComparison.OrdinalIgnoreCase));
                    if (task != null)
                    {
                        cross.IsCompleted = _progressService.GetStatus(task) == QuestStatus.Done;
                    }
                }
            }

            _activeGraphLayout.CompletedQuests = _activeGraphLayout.Nodes.Count(n => n.IsDone);
            _activeGraphLayout.AvailableQuests = _activeGraphLayout.Nodes.Count(n => n.IsActive);
            _activeGraphLayout.LockedQuests = _activeGraphLayout.Nodes.Count(n => n.IsLocked);

            if (TxtGraphStatsDetails != null)
            {
                TxtGraphStatsDetails.Text = $"{_activeGraphLayout.TotalQuests} Tasks • {_activeGraphLayout.CompletedQuests} Done • {_activeGraphLayout.AvailableQuests} Ready • {_activeGraphLayout.LockedQuests} Locked";
            }

            foreach (var edge in _activeGraphLayout.Edges)
            {
                var key = $"{edge.SourceNode.NormalizedName}->{edge.TargetNode.NormalizedName}";
                if (_edgePathMap.TryGetValue(key, out var path))
                {
                    path.Stroke = edge.StrokeBrush;
                }
            }

            if (_selectedGraphNode != null && GraphDetailTaskCard != null && GraphDetailDrawer?.Visibility == Visibility.Visible)
            {
                GraphDetailTaskCard.LoadFromTask(_selectedGraphNode.Task, _selectedGraphNode.Status);
            }
        }

        private void FilterGraphNodes(string? query)
        {
            if (_activeGraphLayout == null) return;

            if (string.IsNullOrWhiteSpace(query))
            {
                foreach (var node in _activeGraphLayout.Nodes)
                {
                    node.MatchesSearch = true;
                }
                return;
            }

            var q = query.Trim();
            foreach (var node in _activeGraphLayout.Nodes)
            {
                bool match = node.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                             node.Location.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                             node.Trader.Contains(q, StringComparison.OrdinalIgnoreCase);
                node.MatchesSearch = match;
            }
        }

        #endregion

    }
}