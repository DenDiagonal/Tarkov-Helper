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
			TxtStatusMessage.Text = "Buscando objetivos...";
			string query = TxtSearchQuery.Text?.Trim();
			string trader = CmbTraderFilter.SelectedItem as string;
			string map = CmbMapFilter.SelectedItem as string;
			bool geoOnly = ChkGeographicOnly.IsChecked ?? true;
			bool missingOnly = ChkWithoutCoordsOnly.IsChecked == true;
			List<RelocationSearchItem> results = await _relocationService.SearchObjectivesAsync(query, trader, map, geoOnly, missingOnly);
			GridSearchResults.ItemsSource = results;
			TxtSearchResultCount.Text = $"{results.Count} objetivos encontrados";
			TxtStatusMessage.Text = "Búsqueda completada";
			_selectedSearchItem = null;
			UpdateSearchSelectionBar();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			TxtStatusMessage.Text = "Error en búsqueda: " + ex2.Message;
		}
	}

	private async Task LoadHistoryAsync()
	{
		try
		{
			List<CustomizedObjectiveItem> customized = await _relocationService.GetCustomizedObjectivesAsync();
			GridHistoryResults.ItemsSource = customized;
			TxtCustomizedCountHeader.Text = $"Objetivos personalizados por el usuario: {customized.Count}";
			_selectedHistoryItem = null;
			UpdateHistorySelectionBar();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			TxtStatusMessage.Text = "Error cargando historial: " + ex2.Message;
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
			TxtSelectedObjectiveInfo.Text = "Selecciona un objetivo para reubicarlo o consultar sus coordenadas.";
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
			MessageBoxResult confirmed = MessageBox.Show("¿Deseas restaurar la ubicación de fábrica de '" + _selectedSearchItem.Description + "'?", "Restaurar Ubicación", MessageBoxButton.YesNo, MessageBoxImage.Question);
			if (confirmed == MessageBoxResult.Yes && await _relocationService.RestoreObjectiveToFactoryAsync(_selectedSearchItem.ObjectiveId))
			{
				TxtStatusMessage.Text = "Objetivo restaurado a valores de fábrica.";
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
			TxtSelectedHistoryInfo.Text = "Selecciona un objetivo del historial para restaurarlo.";
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
			MessageBoxResult confirmed = MessageBox.Show("¿Deseas restaurar '" + _selectedHistoryItem.Description + "' a su valor original de fábrica?", "Restaurar Ubicación", MessageBoxButton.YesNo, MessageBoxImage.Question);
			if (confirmed == MessageBoxResult.Yes && await _relocationService.RestoreObjectiveToFactoryAsync(_selectedHistoryItem.ObjectiveId))
			{
				TxtStatusMessage.Text = "Objetivo restaurado a valores de fábrica.";
				await RefreshAllAsync();
			}
		}
	}

	private async void BtnRestoreAll_Click(object sender, RoutedEventArgs e)
	{
		MessageBoxResult confirmed = MessageBox.Show("¿Estás seguro de que deseas restaurar TODOS los objetivos personalizados a sus coordenadas originales de fábrica?\n\nEsta acción restablecerá todas tus modificaciones de objetivos en el mapa.", "Restaurar Todo a Fábrica", MessageBoxButton.YesNo, MessageBoxImage.Exclamation);
		if (confirmed == MessageBoxResult.Yes)
		{
			int count = await _relocationService.RestoreAllObjectivesToFactoryAsync();
			MessageBox.Show($"Se restauraron exitosamente {count} objetivos a sus valores originales de fábrica.", "Restauración Completada", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			await RefreshAllAsync();
		}
	}

	private void BtnClose_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

}
