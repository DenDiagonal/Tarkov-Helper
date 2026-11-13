using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using TarkovHelper.Services;
using TarkovHelper.Services.Map;

namespace TarkovHelper.Windows;

public partial class QuestObjectiveRelocationManagerWindow : Window
{
	private readonly QuestObjectiveRelocationService _relocationService = QuestObjectiveRelocationService.Instance;
	private RelocationSearchItem? _selectedSearchItem;
	private CustomizedObjectiveItem? _selectedHistoryItem;

	public QuestObjectiveRelocationManagerWindow()
	{
		InitializeComponent();
		Loaded += OnLoaded;
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		InitializeFilterCombos();
		await DatabaseBootstrapService.Instance.EnsureDatabaseSchemaAsync();
		await RefreshAllAsync();
	}

	private void InitializeFilterCombos()
	{
		string[] itemsSource = new string[11]
		{
			"Todos", "Prapor", "Therapist", "Skier", "Peacekeeper", "Mechanic", "Ragman", "Jaeger", "Fence", "Lightkeeper",
			"Story"
		};
		CmbTraderFilter.ItemsSource = itemsSource;
		CmbTraderFilter.SelectedIndex = 0;
		string[] itemsSource2 = new string[11]
		{
			"Todos", "Customs", "Woods", "Factory", "Interchange", "Reserve", "Shoreline", "Lighthouse", "Streets", "Ground Zero",
			"Labs"
		};
		CmbMapFilter.ItemsSource = itemsSource2;
		CmbMapFilter.SelectedIndex = 0;
	}

	private async Task RefreshAllAsync()
	{
		await PerformSearchAsync();
		await LoadHistoryAsync();
	}

	private async Task PerformSearchAsync()
	{
		try
		{
			TxtStatusMessage.Text = "Searching objectives...";
			string query = TxtSearchQuery.Text?.Trim();
			string trader = CmbTraderFilter.SelectedItem as string;
			string map = CmbMapFilter.SelectedItem as string;
			bool geoOnly = ChkGeographicOnly.IsChecked ?? true;
			bool missingOnly = ChkWithoutCoordsOnly.IsChecked == true;
			List<RelocationSearchItem> results = await _relocationService.SearchObjectivesAsync(query, trader, map, geoOnly, missingOnly);
			GridSearchResults.ItemsSource = results;
			TxtSearchResultCount.Text = $"{results.Count} objectives found";
			TxtStatusMessage.Text = "Search completed";
			_selectedSearchItem = null;
			UpdateSearchSelectionBar();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			TxtStatusMessage.Text = "Search error: " + ex2.Message;
		}
	}

	private async Task LoadHistoryAsync()
	{
		try
		{
			List<CustomizedObjectiveItem> customized = await _relocationService.GetCustomizedObjectivesAsync();
			GridHistoryResults.ItemsSource = customized;
			TxtCustomizedCountHeader.Text = $"User-customized objectives: {customized.Count}";
			_selectedHistoryItem = null;
			UpdateHistorySelectionBar();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			TxtStatusMessage.Text = "Error loading history: " + ex2.Message;
		}
	}

	private void TxtSearchQuery_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			PerformSearchAsync();
		}
	}

	private void FilterChanged(object sender, RoutedEventArgs e)
	{
		if (IsLoaded)
		{
			PerformSearchAsync();
		}
	}

	private void BtnSearch_Click(object sender, RoutedEventArgs e)
	{
		PerformSearchAsync();
	}

	private void GridSearchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		_selectedSearchItem = GridSearchResults.SelectedItem as RelocationSearchItem;
		UpdateSearchSelectionBar();
	}

	private void UpdateSearchSelectionBar()
	{
		if (_selectedSearchItem != null)
		{
			TxtSelectedObjectiveInfo.Text = $"[{_selectedSearchItem.QuestName}] {_selectedSearchItem.Description} ({_selectedSearchItem.StatusDisplay})";
			BtnRelocateSelected.IsEnabled = _selectedSearchItem.CanRelocate;
			BtnRestoreSingle.IsEnabled = _selectedSearchItem.IsCustomized;
		}
		else
		{
			TxtSelectedObjectiveInfo.Text = "Select an objective to relocate or view its coordinates.";
			BtnRelocateSelected.IsEnabled = false;
			BtnRestoreSingle.IsEnabled = false;
		}
	}

	private void BtnRelocateSelected_Click(object sender, RoutedEventArgs e)
	{
		if (_selectedSearchItem != null && _selectedSearchItem.CanRelocate)
		{
			_relocationService.StartPlacement(_selectedSearchItem.ObjectiveId, _selectedSearchItem.Description, _selectedSearchItem.QuestName, _selectedSearchItem.MapName);
			Close();
		}
	}

	private async void BtnRestoreSingle_Click(object sender, RoutedEventArgs e)
	{
		if (_selectedSearchItem != null)
		{
			MessageBoxResult confirmed = MessageBox.Show("Do you want to restore the factory location for '" + _selectedSearchItem.Description + "'?", "Restore Location", MessageBoxButton.YesNo, MessageBoxImage.Question);
			if (confirmed == MessageBoxResult.Yes && await _relocationService.RestoreObjectiveToFactoryAsync(_selectedSearchItem.ObjectiveId))
			{
				TxtStatusMessage.Text = "Objective restored to factory defaults.";
				await RefreshAllAsync();
			}
		}
	}

	private void GridHistoryResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		_selectedHistoryItem = GridHistoryResults.SelectedItem as CustomizedObjectiveItem;
		UpdateHistorySelectionBar();
	}

	private void UpdateHistorySelectionBar()
	{
		if (_selectedHistoryItem != null)
		{
			TxtSelectedHistoryInfo.Text = "[" + _selectedHistoryItem.QuestName + "] " + _selectedHistoryItem.Description;
			BtnHistoryRelocate.IsEnabled = true;
			BtnHistoryRestore.IsEnabled = true;
		}
		else
		{
			TxtSelectedHistoryInfo.Text = "Select an objective from history to restore.";
			BtnHistoryRelocate.IsEnabled = false;
			BtnHistoryRestore.IsEnabled = false;
		}
	}

	private void BtnHistoryRelocate_Click(object sender, RoutedEventArgs e)
	{
		if (_selectedHistoryItem != null)
		{
			_relocationService.StartPlacement(_selectedHistoryItem.ObjectiveId, _selectedHistoryItem.Description, _selectedHistoryItem.QuestName, _selectedHistoryItem.MapName);
			Close();
		}
	}

	private async void BtnHistoryRestore_Click(object sender, RoutedEventArgs e)
	{
		if (_selectedHistoryItem != null)
		{
			MessageBoxResult confirmed = MessageBox.Show("Do you want to restore '" + _selectedHistoryItem.Description + "' to its original factory location?", "Restore Location", MessageBoxButton.YesNo, MessageBoxImage.Question);
			if (confirmed == MessageBoxResult.Yes && await _relocationService.RestoreObjectiveToFactoryAsync(_selectedHistoryItem.ObjectiveId))
			{
				TxtStatusMessage.Text = "Objective restored to factory defaults.";
				await RefreshAllAsync();
			}
		}
	}

	private async void BtnRestoreAll_Click(object sender, RoutedEventArgs e)
	{
		MessageBoxResult confirmed = MessageBox.Show("Are you sure you want to restore ALL customized objectives to their original factory coordinates?\n\nThis will reset all your map objective adjustments.", "Restore All to Default", MessageBoxButton.YesNo, MessageBoxImage.Exclamation);
		if (confirmed == MessageBoxResult.Yes)
		{
			int count = await _relocationService.RestoreAllObjectivesToFactoryAsync();
			MessageBox.Show($"Successfully restored {count} objectives to their original factory coordinates.", "Restore Completed", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			await RefreshAllAsync();
		}
	}

	private void BtnClose_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

}
