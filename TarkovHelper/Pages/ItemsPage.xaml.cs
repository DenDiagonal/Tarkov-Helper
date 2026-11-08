using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TarkovHelper.Models;
using TarkovHelper.Services;
using TarkovHelper.ViewModels;

namespace TarkovHelper.Pages
{
    public partial class ItemsPage : UserControl
    {
        public NeededItemsViewModel ViewModel { get; }
        private readonly LocalizationService _loc = LocalizationService.Instance;
        private readonly QuestProgressService _questProgressService = QuestProgressService.Instance;
        private readonly HideoutProgressService _hideoutProgressService = HideoutProgressService.Instance;
        private readonly NeededItemProgressService _neededItemProgressService = NeededItemProgressService.Instance;
        private readonly SettingsService _settingsService = SettingsService.Instance;

        private string? _pendingItemSelection;

        public ItemsPage()
        {
            ViewModel = new NeededItemsViewModel();
            InitializeComponent();
            DataContext = ViewModel;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _loc.LanguageChanged -= OnLanguageChanged;
            _loc.LanguageChanged += OnLanguageChanged;

            _questProgressService.ProgressChanged -= OnProgressChanged;
            _questProgressService.ProgressChanged += OnProgressChanged;

            _hideoutProgressService.ProgressChanged -= OnProgressChanged;
            _hideoutProgressService.ProgressChanged += OnProgressChanged;

            _neededItemProgressService.ProgressChanged -= OnNeededItemProgressChanged;
            _neededItemProgressService.ProgressChanged += OnNeededItemProgressChanged;

            _settingsService.PlayerLevelChanged -= OnPlayerLevelChanged;
            _settingsService.PlayerLevelChanged += OnPlayerLevelChanged;

            _settingsService.PlayerFactionChanged -= OnFactionChanged;
            _settingsService.PlayerFactionChanged += OnFactionChanged;

            _ = InitializeAndLoadAsync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _loc.LanguageChanged -= OnLanguageChanged;
            _questProgressService.ProgressChanged -= OnProgressChanged;
            _hideoutProgressService.ProgressChanged -= OnProgressChanged;
            _neededItemProgressService.ProgressChanged -= OnNeededItemProgressChanged;
            _settingsService.PlayerLevelChanged -= OnPlayerLevelChanged;
            _settingsService.PlayerFactionChanged -= OnFactionChanged;
        }

        private async Task InitializeAndLoadAsync()
        {
            if (ViewModel.FilteredCards.Count == 0)
            {
                await ViewModel.LoadCardsAsync();
            }
            if (!string.IsNullOrEmpty(_pendingItemSelection))
            {
                var id = _pendingItemSelection;
                _pendingItemSelection = null;
                SelectItem(id);
            }
        }

        public async Task ReloadDataAsync()
        {
            await ViewModel.LoadCardsAsync();
        }

        public void SelectItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return;

            if (ViewModel.IsLoading)
            {
                _pendingItemSelection = itemId;
                return;
            }

            // Search for this item by Id or NormalizedName
            var match = ViewModel.FilteredCards.FirstOrDefault(c =>
                string.Equals(c.ItemId, itemId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.ItemNormalizedName, itemId, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                ViewModel.SearchText = match.ItemDisplayName;
            }
            else
            {
                ViewModel.SearchText = itemId;
            }
        }

        private void OnLanguageChanged(object? sender, AppLanguage e)
        {
            Dispatcher.InvokeAsync(async () => await ViewModel.LoadCardsAsync());
        }

        private void CardsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange > 0 && e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 250)
            {
                ViewModel.LoadMoreDisplayedCards();
            }
        }

        private void OnProgressChanged(object? sender, EventArgs e)
        {
            NeededItemsViewModel.InvalidatePrewarmCache();
            Dispatcher.InvokeAsync(async () => await ViewModel.LoadCardsAsync(forceRebuild: true));
        }

        private void OnPlayerLevelChanged(object? sender, int e)
        {
            Dispatcher.InvokeAsync(async () => await ViewModel.LoadCardsAsync());
        }

        private void OnFactionChanged(object? sender, string? e)
        {
            Dispatcher.InvokeAsync(async () => await ViewModel.LoadCardsAsync());
        }

        private void OnNeededItemProgressChanged(object? sender, NeededItemProgressChangedEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var card = ViewModel.FilteredCards.FirstOrDefault(c => string.Equals(c.NeedKey, e.NeedKey, StringComparison.OrdinalIgnoreCase));
                if (card != null && card.CurrentCount != e.Count)
                {
                    card.CurrentCount = e.Count;
                }
            });
        }

        // Tabs
        private void TabAll_Click(object sender, RoutedEventArgs e) => ViewModel.ActiveTab = "All";
        private void TabTasks_Click(object sender, RoutedEventArgs e) => ViewModel.ActiveTab = "Tasks";
        private void TabHideout_Click(object sender, RoutedEventArgs e) => ViewModel.ActiveTab = "Hideout";
        private void TabKappa_Click(object sender, RoutedEventArgs e) => ViewModel.ActiveTab = "Kappa";
        private void TabCompleted_Click(object sender, RoutedEventArgs e) => ViewModel.ActiveTab = "Completed";

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e) => ViewModel.SearchText = string.Empty;

        private void BtnFilters_Click(object sender, RoutedEventArgs e)
        {
            FilterPopup.IsOpen = !FilterPopup.IsOpen;
        }

        private void BtnResetFilters_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ResetFilters();
        }

        private void RadioFir_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb)
            {
                if (rb == RadioFirAll) ViewModel.FirFilter = "All";
                else if (rb == RadioFirOnly) ViewModel.FirFilter = "FIR";
                else if (rb == RadioFirNon) ViewModel.FirFilter = "Non-FIR";
            }
        }

        private void CmbSortMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel != null && CmbSortMode?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                ViewModel.SortMode = tag;
            }
        }

        // Stepper buttons
        private void BtnCardMinus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.DataContext is NeededItemCardViewModel card)
            {
                card.Decrement();
            }
        }

        private void BtnCardPlus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.DataContext is NeededItemCardViewModel card)
            {
                card.Increment();
            }
        }

        private void BtnCardComplete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.DataContext is NeededItemCardViewModel card)
            {
                card.ToggleComplete();
            }
        }

        // Navigation to Task or Hideout
        private void SourcePill_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.DataContext is NeededItemCardViewModel card)
            {
                if (Application.Current.MainWindow is MainWindow mw)
                {
                    if (card.IsQuest && !string.IsNullOrEmpty(card.QuestNormalizedName))
                    {
                        mw.NavigateToQuest(card.QuestNormalizedName);
                    }
                    else if (card.IsHideout)
                    {
                        mw.TabHideout.IsChecked = true;
                    }
                }
            }
        }
    }
}
