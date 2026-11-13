using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Pages.Map.Components;
using TarkovHelper.Pages.Map.ViewModels;
using TarkovHelper.Services;
using TarkovHelper.Services.Logging;
using TarkovHelper.Services.Map;
using TarkovHelper.Services.Settings;
using TarkovHelper.ViewModels;
using TarkovHelper.Windows;

namespace TarkovHelper.Pages.Map;

public partial class MapPage : System.Windows.Controls.UserControl
{
	private class AreaMarkerTag
	{
		public TaskObjectiveWithLocation? Objective { get; set; }

		public bool IsArea { get; set; }
	}

	private static readonly ILogger _log = Log.For<MapPage>();

	private readonly MapTrackerService? _trackerService;

	private readonly QuestObjectiveService _objectiveService = QuestObjectiveService.Instance;

	private readonly QuestProgressService _progressService = QuestProgressService.Instance;

	private readonly LocalizationService _loc = LocalizationService.Instance;

	private readonly OverlayMiniMapService _overlayService = OverlayMiniMapService.Instance;

	private string? _currentMapKey;

	private double _zoomLevel = 1.0;

	private const double MinZoom = 0.1;

	private const double MaxZoom = 5.0;

	private bool _isDragging;

	private System.Windows.Point _dragStartPoint;

	private double _dragStartTranslateX;

	private double _dragStartTranslateY;

	private static readonly double[] ZoomPresets = new double[9] { 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0 };

	private List<TaskObjectiveWithLocation> _currentMapObjectives = new List<TaskObjectiveWithLocation>();

	private bool _showQuestMarkers = true;

	private QuestMarkerStyle _questMarkerStyle;

	private double _questNameTextSize = 12.0;

	private TaskObjectiveWithLocation? _selectedObjective;

	private readonly ExtractService _extractService = ExtractService.Instance;

	private bool _showExtractMarkers = true;

	private bool _showPmcExtracts = true;

	private bool _showScavExtracts = true;

	private bool _showTransitExtracts = true;

	private double _extractNameTextSize = 10.0;

	private bool _hideCompletedObjectives = true;

	private bool _showPmcSpawnsMarker = true;

	private bool _showSniperScavsMarker = true;

	private bool _showRoguesMarker = true;

	private bool _showCultistsMarker = true;

	private bool _showLeversMarkerOverlay = true;

	private bool _showBossesMarker = true;

	private bool _isMapMarkersPanelCollapsed;

	private bool _isCustomMarkersPanelCollapsed;

	private readonly EftRaidEventService _raidEventService = EftRaidEventService.Instance;

	private readonly Dictionary<string, double> _mapZoomLevelCache = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

	private readonly MapCalibrationService _calibrationService = MapCalibrationService.Instance;

	private bool _isCalibrationMode;

	private FrameworkElement? _draggingExtractMarker;

	private System.Windows.Point _extractDragStartPoint;

	private double _extractMarkerOriginalLeft;

	private double _extractMarkerOriginalTop;

	private MapExtract? _draggingExtract;

	private string? _currentFloorId;

	private MapQuestMarkerManager? _questMarkerManager;

	private MapExtractMarkerManager? _extractMarkerManager;

	private MapCalibrationController? _calibrationController;

	private MapMarkersManager? _mapMarkersManager;

	private MapCustomMarkerManager? _customMarkerManager;

	private System.Windows.Point _lastRightClickPosition;

	private CancellationTokenSource? _loadingCts;

	private bool _isInitializing;

	private bool _isSwitchingMap;

	private bool _isApplyingMapConfig;

	private readonly ObservableCollection<MapPillItem> _mapPills = new ObservableCollection<MapPillItem>();

	private readonly ObservableCollection<MapActiveItemStripViewModel> _requiredItems = new ObservableCollection<MapActiveItemStripViewModel>();

	private readonly ObservableCollection<MapActiveKeyStripViewModel> _requiredKeys = new ObservableCollection<MapActiveKeyStripViewModel>();

	private DispatcherTimer? _clockTimer;

	private bool _isEmbedded;



























































































































































	public bool IsEmbedded => _isEmbedded;

	public event EventHandler<TaskObjectiveWithLocation>? ObjectiveSelected;

	public MapPage()
	{
		try
		{
			InitializeComponent();
			_trackerService = MapTrackerService.Instance;
			_trackerService.PositionUpdated += OnPositionUpdated;
			_trackerService.ErrorOccurred += OnErrorOccurred;
			_trackerService.StatusMessage += OnStatusMessage;
			_trackerService.WatchingStateChanged += OnWatchingStateChanged;
			_trackerService.FloorChanged += OnTrackerFloorChanged;
			_trackerService.MapChanged += OnTrackerMapChanged;
			_trackerService.PinnedObjectiveChanged += OnTrackerPinnedObjectiveChanged;
			_loc.LanguageChanged += OnLanguageChanged;
			base.Loaded += MapTrackerPage_Loaded;
			base.Unloaded += MapTrackerPage_Unloaded;
			InitializeZoomComboBox();
			InitializeMapPills();
			InitializeClockWidget();
		}
		catch (Exception ex)
		{
			System.Windows.MessageBox.Show((_loc.CurrentLanguage == AppLanguage.KO) ? ("지도 추적 페이지 초기화 오류: " + ex.Message) : ("Map tracker initialization error: " + ex.Message), (_loc.CurrentLanguage == AppLanguage.KO) ? "오류" : "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private void InitializeZoomComboBox()
	{
		CmbZoomLevel.Items.Clear();
		double[] zoomPresets = ZoomPresets;
		foreach (double num in zoomPresets)
		{
			CmbZoomLevel.Items.Add($"{num * 100.0:F0}%");
		}
		CmbZoomLevel.Text = "100%";
	}

	private void InitializeComponents()
	{
		if (_trackerService == null)
		{
			return;
		}
		_questMarkerManager = new MapQuestMarkerManager(QuestMarkersContainer, _trackerService, _objectiveService, _progressService, _loc);
		_questMarkerManager.ObjectiveSelected += OnObjectiveSelectedFromManager;
		_questMarkerManager.FloorChangeRequested += OnFloorChangeRequestedFromManager;
		_questMarkerManager.StatusUpdated += delegate(string msg)
		{
			base.Dispatcher.Invoke(() => TxtStatus.Text = msg);
		};
		_extractMarkerManager = new MapExtractMarkerManager(ExtractMarkersContainer, _trackerService, _extractService, _loc);
		_extractMarkerManager.CalibrationMarkerSetup += OnCalibrationMarkerSetup;
		_calibrationController = new MapCalibrationController(ExtractMarkersContainer, _trackerService, _calibrationService);
		_calibrationController.StatusUpdated += delegate(string msg)
		{
			base.Dispatcher.Invoke(() => TxtStatus.Text = msg);
		};
		_calibrationController.CalibrationCompleted += OnCalibrationCompleted;
		_mapMarkersManager = new MapMarkersManager(MapMarkersContainer, _trackerService, MapMarkerDbService.Instance, _loc);
		_customMarkerManager = new MapCustomMarkerManager(CustomMarkersContainer, _trackerService, _trackerService.Transformer, _loc);
		_customMarkerManager.StatusUpdated += delegate(string msg)
		{
			base.Dispatcher.Invoke(() => TxtStatus.Text = msg);
		};
		LstCustomMarkers.ItemsSource = _customMarkerManager.Markers;
	}

	private void UpdateCustomMarkersParam()
	{
		_customMarkerManager?.SetParameters(_currentMapKey, _currentFloorId, _zoomLevel);
	}

	private void OnObjectiveSelectedFromManager(object? sender, TaskObjectiveWithLocation objective)
	{
		_selectedObjective = objective;
		ShowQuestDrawer(objective);
		this.ObjectiveSelected?.Invoke(this, objective);
	}

	private void OnFloorChangeRequestedFromManager(object? sender, TaskObjectiveWithLocation objective)
	{
		SelectFloorForObjective(objective);
	}

	private void OnCalibrationMarkerSetup(FrameworkElement marker, MapExtract extract)
	{
		_calibrationController?.SetupMarkerForCalibration(marker, extract);
	}

	private async void OnCalibrationCompleted(object? sender, EventArgs e)
	{
		try
		{
			CancellationToken newCancellationToken = GetNewCancellationToken();
			await RefreshExtractMarkers(newCancellationToken);
		}
		catch (OperationCanceledException)
		{
		}
	}

	private CancellationToken GetNewCancellationToken()
	{
		if ((_isInitializing || _isSwitchingMap || _isApplyingMapConfig) && _loadingCts != null && !_loadingCts.IsCancellationRequested)
		{
			return _loadingCts.Token;
		}
		_loadingCts?.Cancel();
		_loadingCts?.Dispose();
		_loadingCts = new CancellationTokenSource();
		return _loadingCts.Token;
	}

	private async void MapTrackerPage_Loaded(object sender, RoutedEventArgs e)
	{
		_ = 9;
		try
		{
			_loadingCts?.Cancel();
			_loadingCts?.Dispose();
			_loadingCts = new CancellationTokenSource();
			CancellationToken ct = _loadingCts.Token;
			_isInitializing = true;
			_trackerService?.ClearTrail();
			TrailPath.Points.Clear();
			LoadSettings();
			InitializeComponents();
			PopulateMapComboBox();
			StartRaidEventMonitoring();
			RestoreMapState();
			UpdateUI();
			await Task.Delay(50, ct);
			if (!string.IsNullOrEmpty(_currentMapKey))
			{
				await LoadMapImageAsync(_currentMapKey, centerView: true, ct);
			}
			await LoadQuestObjectivesAsync(ct);
			await Task.Delay(20, ct);
			await LoadExtractsAsync(ct);
			await Task.Delay(20, ct);
			await LoadMapMarkersAsync(ct);
			await Task.Delay(20, ct);
			await FloorDetectionService.Instance.LoadFloorRangesAsync().WaitAsync(ct);
			OpenQuestDrawer();
			_progressService.ProgressChanged += OnQuestProgressChanged;
			MapSettings.Instance.HiddenQuestsChanged += OnHiddenQuestsChanged;
			ObjectiveProgressService.Instance.ObjectiveProgressChanged += OnObjectiveProgressChanged;
			GlobalKeyboardHookService.Instance.FloorKeyPressed += OnFloorKeyPressed;
			GlobalKeyboardHookService.Instance.IsEnabled = true;
			await InitializeOverlayServiceAsync().WaitAsync(ct);
			_overlayService.OverlayVisibilityChanged += OnOverlayVisibilityChanged;
			UpdateMinimapButtonState(_overlayService.IsOverlayVisible);
			StartAutoTracking();
			MapMarkerDbService.Instance.DataRefreshed -= OnDatabaseRefreshed;
			MapMarkerDbService.Instance.DataRefreshed += OnDatabaseRefreshed;
			QuestObjectiveDbService.Instance.DataRefreshed -= OnDatabaseRefreshed;
			QuestObjectiveDbService.Instance.DataRefreshed += OnDatabaseRefreshed;
			QuestObjectiveRelocationService.Instance.PlacementStateChanged -= OnPlacementStateChanged;
			QuestObjectiveRelocationService.Instance.PlacementStateChanged += OnPlacementStateChanged;
			base.PreviewKeyDown -= MapPage_PreviewKeyDown;
			base.PreviewKeyDown += MapPage_PreviewKeyDown;
			if (QuestObjectiveRelocationService.Instance.IsPlacementActive)
			{
				OnPlacementStateChanged(this, EventArgs.Empty);
			}
			UpdateMapPillTaskCounts();
			if (!string.IsNullOrEmpty(_currentMapKey))
			{
				UpdateMapPillSelection(_currentMapKey);
				UpdateRequiredStripsForCurrentMap();
			}
		}
		catch (Exception ex)
		{
			if (ex is OperationCanceledException || ex is TaskCanceledException || (_loadingCts?.IsCancellationRequested ?? false) || ex.Message.Contains("canceled", StringComparison.OrdinalIgnoreCase))
			{
				_log.Debug("Map loading task was cancelled, ignoring exception.");
				return;
			}
			_log.Error("Critical error loading map page", ex);
			System.Windows.MessageBox.Show((_loc.CurrentLanguage == AppLanguage.KO) ? ("지도 추적 페이지 로드 오류: " + ex.Message) : ("Map tracker loading error: " + ex.Message), (_loc.CurrentLanguage == AppLanguage.KO) ? "오류" : "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		finally
		{
			_isInitializing = false;
		}
	}

	private void MapTrackerPage_Unloaded(object sender, RoutedEventArgs e)
	{
		_clockTimer?.Stop();
		_loadingCts?.Cancel();
		SaveMapState();
		base.PreviewKeyDown -= MapPage_PreviewKeyDown;
		_progressService.ProgressChanged -= OnQuestProgressChanged;
		MapSettings.Instance.HiddenQuestsChanged -= OnHiddenQuestsChanged;
		ObjectiveProgressService.Instance.ObjectiveProgressChanged -= OnObjectiveProgressChanged;
		MapMarkerDbService.Instance.DataRefreshed -= OnDatabaseRefreshed;
		QuestObjectiveDbService.Instance.DataRefreshed -= OnDatabaseRefreshed;
		QuestObjectiveRelocationService.Instance.PlacementStateChanged -= OnPlacementStateChanged;
		if (_trackerService != null)
		{
			_trackerService.FloorChanged -= OnTrackerFloorChanged;
			_trackerService.MapChanged -= OnTrackerMapChanged;
			_trackerService.PinnedObjectiveChanged -= OnTrackerPinnedObjectiveChanged;
		}
		GlobalKeyboardHookService.Instance.FloorKeyPressed -= OnFloorKeyPressed;
		GlobalKeyboardHookService.Instance.IsEnabled = false;
		_overlayService.OverlayVisibilityChanged -= OnOverlayVisibilityChanged;
		_overlayService.HideOverlay();
		StopAutoTracking();
		StopRaidEventMonitoring();
	}

	private void SaveMapState()
	{
		SettingsService instance = SettingsService.Instance;
		instance.MapLastSelectedMap = _currentMapKey;
		instance.MapLastZoomLevel = _zoomLevel;
		instance.MapLastTranslateX = MapTranslate.X;
		instance.MapLastTranslateY = MapTranslate.Y;
	}

	private async void OnDatabaseRefreshed(object? sender, EventArgs e)
	{
		_ = 2;
		try
		{
			CancellationToken ct = GetNewCancellationToken();
			await LoadQuestObjectivesAsync(ct);
			await LoadExtractsAsync(ct);
			await RefreshQuestMarkers(ct);
			RefreshQuestDrawer();
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void RestoreMapState()
	{
		string mapLastSelectedMap = SettingsService.Instance.MapLastSelectedMap;
		if (!string.IsNullOrEmpty(_currentMapKey) || string.IsNullOrEmpty(mapLastSelectedMap))
		{
			return;
		}
		for (int i = 0; i < CmbMapSelect.Items.Count; i++)
		{
			if (CmbMapSelect.Items[i] is ComboBoxItem comboBoxItem && string.Equals(comboBoxItem.Tag as string, mapLastSelectedMap, StringComparison.OrdinalIgnoreCase))
			{
				CmbMapSelect.SelectedIndex = i;
				break;
			}
		}
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			SetZoom(1.0);
			CenterMapInView();
		}, DispatcherPriority.Loaded);
	}

	private void OnLanguageChanged(object? sender, AppLanguage e)
	{
		UpdateLocalizedText();
	}

	private void UpdateLocalizedText()
	{
		TxtPageTitle.Text = _loc.MapPositionTracker;
		TxtMapLabel.Text = _loc.MapLabel;
		ChkShowQuestMarkers.Content = _loc.QuestMarkers;
		ChkShowExtractMarkers.Content = _loc.Extracts;
		ChkFixedView.Content = _loc.FixedView;
		BtnClearTrail.Content = _loc.ClearTrail;
		BtnFullScreen.Content = _loc.FullScreen;
		BtnExitFullScreen.Content = _loc.ExitFullScreen;
		BtnMinimap.Content = _loc.Minimap;
		BtnSettings.Content = _loc.Settings;
		MenuAddCustomMarker.Header = _loc.AddCustomMarker;
		bool flag = _trackerService?.IsWatching ?? false;
		BtnToggleTracking.Content = (flag ? _loc.StopTracking : _loc.StartTracking);
		if (!flag)
		{
			TxtStatus.Text = _loc.StatusWaiting;
		}
		TxtPositionLabel.Text = _loc.PositionLabel;
		TxtLastUpdateLabel.Text = _loc.LastUpdateLabel;
		TxtSettingsTitle.Text = _loc.Settings;
		TxtScreenshotFolderLabel.Text = _loc.ScreenshotFolder;
		BtnAutoDetect.Content = _loc.AutoDetect;
		BtnBrowseFolder.Content = _loc.Browse;
		TxtMarkerSettingsLabel.Text = _loc.MarkerSettings;
		ChkHideCompletedObjectives.Content = _loc.HideCompletedObjectives;
		TxtQuestStyleLabel.Text = _loc.QuestStyle;
		TxtQuestNameSizeLabel.Text = _loc.QuestNameSize;
		TxtQuestMarkerSizeLabel.Text = _loc.QuestMarkerSize;
		TxtPlayerMarkerSizeLabel.Text = _loc.PlayerMarkerSize;
		TxtExtractSettingsLabel.Text = _loc.ExtractSettings;
		if (TxtPmcExtractsLabel != null)
		{
			TxtPmcExtractsLabel.Text = _loc.PmcExtracts;
		}
		if (TxtScavExtractsLabel != null)
		{
			TxtScavExtractsLabel.Text = _loc.ScavExtracts;
		}
		ChkShowPmcExtracts.Content = null;
		ChkShowScavExtracts.Content = null;
		TxtExtractNameSizeLabel.Text = _loc.ExtractNameSize;
		TxtMarkerOpacityLabel.Text = _loc.MarkerOpacity;
		TxtMarkerColorsLabel.Text = _loc.MarkerColors;
		TxtColorVisit.Text = _loc.FilterVisit;
		TxtColorMark.Text = _loc.FilterMark;
		TxtColorPlant.Text = _loc.FilterPlant;
		TxtColorExtract.Text = _loc.FilterExtract;
		TxtColorFind.Text = _loc.FilterFind;
		BtnResetColors.Content = _loc.ResetColors;
		CmbStyleIconOnly.Content = _loc.StyleIconOnly;
		CmbStyleGreenCircle.Content = _loc.StyleGreenCircle;
		CmbStyleIconWithName.Content = _loc.StyleIconWithName;
		CmbStyleCircleWithName.Content = _loc.StyleCircleWithName;
		TxtNoMapImage.Text = _loc.NoMapImage;
		TxtAddMapImageHint.Text = _loc.AddMapImageHint;
		TxtSetImagePathHint.Text = _loc.SetImagePathHint;
		BtnZoomIn.ToolTip = _loc.ZoomInTooltip;
		BtnZoomOut.ToolTip = _loc.ZoomOutTooltip;
		BtnResetView.ToolTip = _loc.ResetViewTooltip;
		string text = (CmbMapSelect.SelectedItem as ComboBoxItem)?.Tag as string;
		PopulateMapComboBox();
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		for (int i = 0; i < CmbMapSelect.Items.Count; i++)
		{
			if (CmbMapSelect.Items[i] is ComboBoxItem comboBoxItem && string.Equals(comboBoxItem.Tag as string, text, StringComparison.OrdinalIgnoreCase))
			{
				CmbMapSelect.SelectedIndex = i;
				break;
			}
		}
	}

	private void LoadSettings()
	{
		SettingsService instance = SettingsService.Instance;
		string text = instance.MapScreenshotPath;
		if (string.IsNullOrEmpty(text))
		{
			text = MapTrackerSettings.TryDetectScreenshotFolder() ?? (Environment.GetFolderPath(Environment.SpecialFolder.Personal) + "\\Escape from Tarkov\\Screenshots");
		}
		TxtScreenshotFolder.Text = text;
		SliderMarkerSize.Value = instance.MapQuestMarkerSize;
		SliderPlayerMarkerSize.Value = instance.MapPlayerMarkerSize;
		UpdatePlayerMarkerSize(instance.MapPlayerMarkerSize);
		_showPmcExtracts = instance.MapShowPmcExtracts;
		_showScavExtracts = instance.MapShowScavExtracts;
		_showTransitExtracts = instance.MapShowTransits;
		_extractNameTextSize = instance.MapExtractNameSize;
		_showExtractMarkers = instance.MapShowExtracts;
		_showQuestMarkers = instance.MapShowQuests;
		_questNameTextSize = instance.MapQuestNameSize;
		_questMarkerStyle = (QuestMarkerStyle)instance.MapQuestMarkerStyle;
		if (_questMarkerStyle == QuestMarkerStyle.DefaultWithName)
		{
			_questMarkerStyle = QuestMarkerStyle.Default;
			SettingsService.Instance.MapQuestMarkerStyle = (int)_questMarkerStyle;
		}
		else if (_questMarkerStyle == QuestMarkerStyle.GreenCircleWithName)
		{
			_questMarkerStyle = QuestMarkerStyle.GreenCircle;
			SettingsService.Instance.MapQuestMarkerStyle = (int)_questMarkerStyle;
		}
		_hideCompletedObjectives = instance.MapHideCompletedObjectives;
		ChkShowPmcExtracts.IsChecked = _showPmcExtracts;
		ChkShowScavExtracts.IsChecked = _showScavExtracts;
		ChkShowTransitExtracts.IsChecked = _showTransitExtracts;
		SliderExtractTextSize.Value = _extractNameTextSize;
		ChkShowExtractMarkers.IsChecked = _showExtractMarkers;
		ChkShowQuestMarkers.IsChecked = _showQuestMarkers;
		CmbQuestMarkerStyle.SelectedIndex = (int)_questMarkerStyle;
		if (ChkSettingTaskTitles != null)
		{
			ChkSettingTaskTitles.IsChecked = _questMarkerStyle == QuestMarkerStyle.DefaultWithName || _questMarkerStyle == QuestMarkerStyle.GreenCircleWithName;
		}
		SliderQuestNameTextSize.Value = _questNameTextSize;
		ChkHideCompletedObjectives.IsChecked = _hideCompletedObjectives;
		SliderMarkerOpacity.Value = MapSettings.Instance.MarkerOpacity;
		if (ExtractMarkersContainer != null)
		{
			ExtractMarkersContainer.Visibility = ((!_showExtractMarkers) ? Visibility.Collapsed : Visibility.Visible);
		}
		if (QuestMarkersContainer != null)
		{
			QuestMarkersContainer.Visibility = ((!_showQuestMarkers) ? Visibility.Collapsed : Visibility.Visible);
		}
		UpdateMarkerColorUI();
		LoadMapMarkersSettings();
		_isCustomMarkersPanelCollapsed = instance.IsCustomMarkersPanelCollapsed;
		UpdateCustomMarkersPanelUI(_isCustomMarkersPanelCollapsed);
		if (ChkFixedView != null)
		{
			ChkFixedView.IsChecked = !MapSettings.Instance.AutoCenterEnabled;
		}
	}

	private void LoadMapMarkersSettings()
	{
		MapSettings instance = MapSettings.Instance;
		_showPmcSpawnsMarker = instance.ShowPmcSpawns;
		_showSniperScavsMarker = instance.ShowSniperScavs;
		_showRoguesMarker = instance.ShowRogues;
		_showCultistsMarker = instance.ShowCultists;
		_showLeversMarkerOverlay = instance.ShowLevers;
		_showBossesMarker = instance.ShowBosses;
		ChkShowPmcSpawns.IsChecked = _showPmcSpawnsMarker;
		ChkShowSniperScavs.IsChecked = _showSniperScavsMarker;
		ChkShowRogues.IsChecked = _showRoguesMarker;
		ChkShowCultists.IsChecked = _showCultistsMarker;
		ChkShowLeversMarker.IsChecked = _showLeversMarkerOverlay;
		ChkShowBosses.IsChecked = _showBossesMarker;
		LoadMapMarkerIcons();
	}

	private void LoadMapMarkerIcons()
	{
		try
		{
			string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Icons", "Markers");
			LoadSvgIcon(IconPmcSpawn, System.IO.Path.Combine(path, "PMC Spawn.svg"));
			LoadSvgIcon(IconSniperScav, System.IO.Path.Combine(path, "SniperScav.svg"));
			LoadSvgIcon(IconRogue, System.IO.Path.Combine(path, "Rogue.svg"));
			LoadSvgIcon(IconCultist, System.IO.Path.Combine(path, "Cultist.svg"));
			LoadSvgIcon(IconLever, System.IO.Path.Combine(path, "Lever.svg"));
			LoadSvgIcon(IconBoss, System.IO.Path.Combine(path, "Boss.svg"));
		}
		catch (Exception ex)
		{
			_log.Warning("Failed to load map marker icons: " + ex.Message);
		}
	}

	private void LoadSvgIcon(SvgViewbox svgViewbox, string path)
	{
		if (svgViewbox == null)
		{
			return;
		}
		try
		{
			if (File.Exists(path))
			{
				svgViewbox.Source = new Uri(path);
			}
		}
		catch (Exception ex)
		{
			_log.Warning("Failed to load SVG icon " + path + ": " + ex.Message);
		}
	}

	private void PopulateMapComboBox()
	{
		if (_trackerService == null)
		{
			return;
		}
		CmbMapSelect.Items.Clear();
		foreach (ComboBoxItem item in (from mapKey in _trackerService.GetAllMapKeys()
			select new ComboBoxItem
			{
				Content = _loc.GetLocalizedMapName(mapKey),
				Tag = mapKey
			}).OrderBy<ComboBoxItem, string>((ComboBoxItem item) => item.Content?.ToString(), StringComparer.CurrentCultureIgnoreCase).ToList())
		{
			CmbMapSelect.Items.Add(item);
		}
		if (CmbMapSelect.Items.Count > 0 && CmbMapSelect.SelectedIndex < 0)
		{
			CmbMapSelect.SelectedIndex = 0;
		}
	}

	private void UpdateUI()
	{
		bool flag = _trackerService?.IsWatching ?? false;
		BtnToggleTracking.Content = (flag ? _loc.StopTracking : _loc.StartTracking);
		System.Windows.Media.Brush brush = (TryFindResource("SuccessBrush") as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.Green;
		System.Windows.Media.Brush brush2 = (TryFindResource("TextSecondaryBrush") as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.Gray;
		StatusIndicator.Fill = (flag ? brush : brush2);
		TxtStatus.Text = (flag ? _loc.StatusTracking : _loc.StatusWaiting);
		UpdateLocalizedText();
	}

	private void OnPositionUpdated(object? sender, ScreenPosition position)
	{
		base.Dispatcher.Invoke(delegate
		{
			UpdateMarkerPosition(position);
			UpdateTrailPath();
			UpdateCoordinatesDisplay(position);
			TryAutoSwitchFloor(position);
			if (MapSettings.Instance.AutoCenterEnabled)
			{
				CenterOnPosition(position);
			}
		});
	}

	private void CenterOnPosition(ScreenPosition position)
	{
		double actualWidth = MapViewerGrid.ActualWidth;
		double actualHeight = MapViewerGrid.ActualHeight;
		if (!(actualWidth <= 0.0) && !(actualHeight <= 0.0))
		{
			MapTranslate.X = actualWidth / 2.0 - position.X * _zoomLevel;
			MapTranslate.Y = actualHeight / 2.0 - position.Y * _zoomLevel;
		}
	}

	private void TryAutoSwitchFloor(ScreenPosition position)
	{
		if (string.IsNullOrEmpty(_currentMapKey) || CmbFloorSelect.Visibility != 0)
		{
			return;
		}
		EftPosition originalPosition = position.OriginalPosition;
		if (originalPosition == null)
		{
			return;
		}
		string text = FloorDetectionService.Instance.DetectFloor(_currentMapKey, originalPosition.X, originalPosition.Y, originalPosition.Z.GetValueOrDefault());
		if (string.IsNullOrEmpty(text))
		{
			text = "main";
		}
		if (string.Equals(_currentFloorId, text, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		for (int i = 0; i < CmbFloorSelect.Items.Count; i++)
		{
			if (CmbFloorSelect.Items[i] is ComboBoxItem comboBoxItem && string.Equals(comboBoxItem.Tag as string, text, StringComparison.OrdinalIgnoreCase))
			{
				CmbFloorSelect.SelectedIndex = i;
				break;
			}
		}
	}

	private void OnTrackerFloorChanged(string? floorId)
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			if (!string.IsNullOrEmpty(floorId) && !string.Equals(_currentFloorId, floorId, StringComparison.OrdinalIgnoreCase))
			{
				for (int i = 0; i < CmbFloorSelect.Items.Count; i++)
				{
					if (CmbFloorSelect.Items[i] is ComboBoxItem comboBoxItem && string.Equals(comboBoxItem.Tag as string, floorId, StringComparison.OrdinalIgnoreCase))
					{
						if (CmbFloorSelect.SelectedIndex != i)
						{
							CmbFloorSelect.SelectedIndex = i;
						}
						break;
					}
				}
			}
		});
	}

	private void OnTrackerMapChanged(string mapKey)
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			if (!string.IsNullOrEmpty(mapKey))
			{
				string b = _trackerService?.Transformer.ResolveMapKey(mapKey) ?? mapKey;
				if (!string.Equals(_currentMapKey, b, StringComparison.OrdinalIgnoreCase))
				{
					for (int i = 0; i < CmbMapSelect.Items.Count; i++)
					{
						if (CmbMapSelect.Items[i] is ComboBoxItem comboBoxItem && (string.Equals(comboBoxItem.Tag as string, b, StringComparison.OrdinalIgnoreCase) || string.Equals(comboBoxItem.Tag as string, mapKey, StringComparison.OrdinalIgnoreCase)))
						{
							if (CmbMapSelect.SelectedIndex != i)
							{
								CmbMapSelect.SelectedIndex = i;
							}
							break;
						}
					}
				}
			}
		});
	}

	private void OnTrackerPinnedObjectiveChanged(TaskObjectiveWithLocation? objective)
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			_selectedObjective = objective;
			_questMarkerManager?.SetSelectedObjective(objective);
			if (objective != null)
			{
				SelectFloorForObjective(objective);
				CenterOnObjective(objective);
				if (_zoomLevel < 1.0)
				{
					SetZoom(1.25);
				}
			}
		});
	}

	public void SelectFloor(string floorId)
	{
		if (string.IsNullOrEmpty(floorId) || CmbFloorSelect == null)
		{
			return;
		}
		base.Dispatcher.Invoke(delegate
		{
			for (int i = 0; i < CmbFloorSelect.Items.Count; i++)
			{
				if (CmbFloorSelect.Items[i] is ComboBoxItem comboBoxItem && string.Equals(comboBoxItem.Tag as string, floorId, StringComparison.OrdinalIgnoreCase))
				{
					if (CmbFloorSelect.SelectedIndex != i)
					{
						CmbFloorSelect.SelectedIndex = i;
					}
					break;
				}
			}
		});
	}

	public void ApplyQuestFiltersToMapMarkers(QuestFilterCriteria criteria, IEnumerable<string>? allowedQuestIds = null, IEnumerable<string>? allowedQuestNames = null)
	{
		_questMarkerManager?.SetFilterCriteria(criteria, allowedQuestIds, allowedQuestNames);
		_questMarkerManager?.RefreshMarkersAsync();
	}

	public void SetEmbeddedMode(bool isEmbedded)
	{
		_isEmbedded = isEmbedded;
		base.Dispatcher.Invoke(delegate
		{
			if (TxtPageTitle != null)
			{
				TxtPageTitle.Visibility = (isEmbedded ? Visibility.Collapsed : Visibility.Visible);
			}
			if (TxtMapLabel != null)
			{
				TxtMapLabel.Visibility = (isEmbedded ? Visibility.Collapsed : Visibility.Visible);
			}
			if (CmbMapSelect != null)
			{
				CmbMapSelect.Visibility = (isEmbedded ? Visibility.Collapsed : Visibility.Visible);
			}
			if (isEmbedded && CmbMapSelect != null && CmbMapSelect.Items.Count == 0)
			{
				PopulateMapComboBox();
			}
			if (BtnClearTrail != null)
			{
				BtnClearTrail.Visibility = (isEmbedded ? Visibility.Collapsed : Visibility.Visible);
			}
			if (BtnFullScreen != null)
			{
				BtnFullScreen.Visibility = (isEmbedded ? Visibility.Collapsed : Visibility.Visible);
			}
			if (BtnOpenRelocationManager != null)
			{
				BtnOpenRelocationManager.Visibility = Visibility.Collapsed;
			}
			if (BtnSettings != null)
			{
				BtnSettings.Visibility = (isEmbedded ? Visibility.Collapsed : Visibility.Visible);
			}
			if (BtnToggleTracking != null)
			{
				BtnToggleTracking.Visibility = (isEmbedded ? Visibility.Collapsed : Visibility.Visible);
			}
			if (ChkFixedView != null)
			{
				ChkFixedView.Visibility = (isEmbedded ? Visibility.Collapsed : Visibility.Visible);
			}
			if (base.Content is Grid grid)
			{
				foreach (UIElement child in grid.Children)
				{
					if (child == MapRequirementsStripsPanel)
					{
						child.Visibility = Visibility.Visible;
					}
					else
					{
						int row = Grid.GetRow(child);
						if (row == 0 || row == 1 || row == 2)
						{
							child.Visibility = (isEmbedded ? Visibility.Collapsed : Visibility.Visible);
						}
					}
				}
			}
			if (isEmbedded)
			{
				_clockTimer?.Stop();
			}
		});
	}

	public static string ResolveCanonicalMapKey(string? mapNameOrKey)
	{
		if (string.IsNullOrWhiteSpace(mapNameOrKey))
		{
			return "Customs";
		}
		string text = mapNameOrKey.Trim().ToLowerInvariant().Replace("-", "")
			.Replace("_", "")
			.Replace(" ", "");
		if (text.Contains("groundzero") || text.Contains("sandbox"))
		{
			return "GroundZero";
		}
		if (text.Contains("customs") || text.Contains("bigmap"))
		{
			return "Customs";
		}
		if (text.Contains("woods"))
		{
			return "Woods";
		}
		if (text.Contains("factory"))
		{
			return "Factory";
		}
		if (text.Contains("interchange") || text.Contains("shopping"))
		{
			return "Interchange";
		}
		if (text.Contains("shoreline"))
		{
			return "Shoreline";
		}
		if (text.Contains("reserve") || text.Contains("rezerv"))
		{
			return "Reserve";
		}
		if (text.Contains("lighthouse"))
		{
			return "Lighthouse";
		}
		if (text.Contains("street"))
		{
			return "StreetsOfTarkov";
		}
		if (text.Contains("labyrinth"))
		{
			return "Labyrinth";
		}
		if (text.Contains("terminal"))
		{
			return "Terminal";
		}
		if (text.Contains("lab"))
		{
			return "Labs";
		}
		return mapNameOrKey;
	}

	public void SelectMap(string mapKey)
	{
		if (string.IsNullOrWhiteSpace(mapKey))
		{
			return;
		}
		string text = ResolveCanonicalMapKey(mapKey);
		_trackerService?.SetCurrentMap(text);
		if (!base.Dispatcher.CheckAccess())
		{
			base.Dispatcher.Invoke(delegate
			{
				SelectMap(mapKey);
			});
			return;
		}
		if (!string.IsNullOrEmpty(_currentMapKey) && !string.Equals(_currentMapKey, text, StringComparison.OrdinalIgnoreCase))
		{
			SaveCurrentPerMapMarkerConfig();
		}
		_isSwitchingMap = true;
		try
		{
			if (CmbMapSelect.Items.Count == 0)
			{
				PopulateMapComboBox();
			}
			int num = -1;
			for (int i = 0; i < CmbMapSelect.Items.Count; i++)
			{
				if (CmbMapSelect.Items[i] is ComboBoxItem comboBoxItem)
				{
					string text2 = comboBoxItem.Tag as string;
					string a = comboBoxItem.Content as string;
					if (string.Equals(text2, text, StringComparison.OrdinalIgnoreCase) || string.Equals(text2, mapKey, StringComparison.OrdinalIgnoreCase) || string.Equals(ResolveCanonicalMapKey(text2), text, StringComparison.OrdinalIgnoreCase) || string.Equals(a, mapKey, StringComparison.OrdinalIgnoreCase))
					{
						num = i;
						break;
					}
				}
			}
			CmbMapSelect.SelectionChanged -= CmbMapSelect_SelectionChanged;
			if (num >= 0)
			{
				CmbMapSelect.SelectedIndex = num;
			}
			CmbMapSelect.SelectionChanged += CmbMapSelect_SelectionChanged;
			_loadingCts?.Cancel();
			_loadingCts?.Dispose();
			_loadingCts = new CancellationTokenSource();
			CancellationToken token = _loadingCts.Token;
			_currentMapKey = text;
			LoadAndApplyPerMapConfig(text);
			UpdateFloorComboBox(text);
			bool centerView = MapSettings.Instance.GetPerMapConfig(text).ZoomLevel < 0.1;
			LoadMapPipelineAsync(text, centerView, token);
		}
		finally
		{
			_isSwitchingMap = false;
		}
	}

	private async Task LoadMapPipelineAsync(string canonicalKey, bool centerView, CancellationToken ct)
	{
		_ = 3;
		try
		{
			await LoadMapImageAsync(canonicalKey, centerView, ct);
			LoadCurrentMapSettings();
			int size = _trackerService?.Settings.PlayerMarkerSize ?? 16;
			UpdatePlayerMarkerSize(size);
			await RefreshExtractMarkers(ct);
			await RefreshMapMarkers(ct);
			await RefreshQuestMarkers(ct);
			UpdateMapPillSelection(canonicalKey);
			UpdateRequiredStripsForCurrentMap();
		}
		catch (OperationCanceledException)
		{
			_log.Info("Map pipeline cancelled for " + canonicalKey);
		}
		catch (Exception exception)
		{
			_log.Error("Error loading map pipeline for " + canonicalKey, exception);
		}
	}

	private void OnPlacementStateChanged(object? sender, EventArgs e)
	{
		base.Dispatcher.Invoke(delegate
		{
			QuestObjectiveRelocationService instance = QuestObjectiveRelocationService.Instance;
			if (instance.IsPlacementActive)
			{
				PlacementModeBanner.Visibility = Visibility.Visible;
				TxtPlacementTargetName.Text = instance.TargetObjectiveDescription ?? instance.TargetQuestName ?? string.Empty;
				MapCanvas.Cursor = System.Windows.Input.Cursors.Cross;
				MapViewerGrid.Cursor = System.Windows.Input.Cursors.Cross;
				if (!string.IsNullOrEmpty(instance.TargetMapKey) && !string.Equals(_currentMapKey, instance.TargetMapKey, StringComparison.OrdinalIgnoreCase))
				{
					SelectMap(instance.TargetMapKey);
				}
			}
			else
			{
				PlacementModeBanner.Visibility = Visibility.Collapsed;
				MapCanvas.Cursor = System.Windows.Input.Cursors.Arrow;
				MapViewerGrid.Cursor = System.Windows.Input.Cursors.Arrow;
			}
		});
	}

	private void BtnCancelPlacement_Click(object sender, RoutedEventArgs e)
	{
		QuestObjectiveRelocationService.Instance.CancelPlacement();
		PlacementModeBanner.Visibility = Visibility.Collapsed;
		MapCanvas.Cursor = System.Windows.Input.Cursors.Arrow;
		MapViewerGrid.Cursor = System.Windows.Input.Cursors.Arrow;
	}

	private void MapPage_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key == Key.Escape && QuestObjectiveRelocationService.Instance.IsPlacementActive)
		{
			BtnCancelPlacement_Click(BtnCancelPlacement, new RoutedEventArgs());
			e.Handled = true;
		}
	}

	private async void HandlePlacementClick(System.Windows.Point canvasPos)
	{
		QuestObjectiveRelocationService service = QuestObjectiveRelocationService.Instance;
		if (!service.IsPlacementActive)
		{
			return;
		}
		string targetObjectiveId = service.TargetObjectiveId;
		string targetDesc = service.TargetObjectiveDescription ?? service.TargetQuestName ?? "Objective";
		if (string.IsNullOrEmpty(_currentMapKey) || string.IsNullOrEmpty(targetObjectiveId))
		{
			service.CancelPlacement();
			return;
		}
		try
		{
			if (service.TryCalculateWorldCoordinates(_currentMapKey, canvasPos, _currentFloorId, out LocationPoint locPoint) && locPoint != null)
			{
				if (await service.SaveObjectiveLocationAsync(targetObjectiveId, locPoint))
				{
					service.CancelPlacement();
					TxtStatus.Text = ((_loc.CurrentLanguage == AppLanguage.KO) ? $"목표 위치가 설정되었습니다: {targetDesc} (X={locPoint.X:F1}, Z={locPoint.Z:F1})" : $"Objective location saved: {targetDesc} (X={locPoint.X:F1}, Z={locPoint.Z:F1})");
				}
				else
				{
					TxtStatus.Text = ((_loc.CurrentLanguage == AppLanguage.KO) ? "목표 위치 저장 실패" : "Failed to save objective location");
				}
			}
			else
			{
				TxtStatus.Text = ((_loc.CurrentLanguage == AppLanguage.KO) ? "좌표 변환 실패" : "Coordinate calculation failed for current map");
			}
		}
		catch (Exception ex)
		{
			_log.Error("Error handling placement click", ex);
			TxtStatus.Text = "Error: " + ex.Message;
		}
	}

	private void OnErrorOccurred(object? sender, string message)
	{
		base.Dispatcher.Invoke(delegate
		{
			TxtStatus.Text = ((_loc.CurrentLanguage == AppLanguage.KO) ? ("오류: " + message) : ("Error: " + message));
			TxtStatus.Foreground = (TryFindResource("WarningBrush") as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.Orange;
		});
	}

	private void OnStatusMessage(object? sender, string message)
	{
		base.Dispatcher.Invoke(delegate
		{
			TxtStatus.Text = message;
			TxtStatus.Foreground = (TryFindResource("TextSecondaryBrush") as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.Gray;
		});
	}

	private void OnWatchingStateChanged(object? sender, bool isWatching)
	{
		base.Dispatcher.Invoke(UpdateUI);
	}

	private void StartAutoTracking()
	{
		if (_trackerService != null && !_trackerService.IsWatching && !string.IsNullOrEmpty(_trackerService.Settings.ScreenshotFolderPath))
		{
			_trackerService.StartTracking();
		}
	}

	private void StopAutoTracking()
	{
		if (_trackerService != null && _trackerService.IsWatching)
		{
			_trackerService.StopTracking();
		}
	}

	private void StartRaidEventMonitoring()
	{
		_raidEventService.RaidEvent += OnRaidEvent;
		string logFolderPath = SettingsService.Instance.LogFolderPath;
		_raidEventService.StartMonitoring(logFolderPath);
	}

	private void StopRaidEventMonitoring()
	{
		_raidEventService.RaidEvent -= OnRaidEvent;
		_raidEventService.StopMonitoring();
	}

	private void OnRaidEvent(object? sender, EftRaidEventArgs e)
	{
		base.Dispatcher.Invoke(delegate
		{
			switch (e.EventType)
			{
			case EftRaidEventType.RaidStarted:
				HandleRaidStarted(e);
				break;
			case EftRaidEventType.Disconnected:
			case EftRaidEventType.RaidEnded:
			case EftRaidEventType.NetworkTimeout:
			case EftRaidEventType.NetworkError:
				HandleRaidEnded(e);
				break;
			}
		});
	}

	private void HandleRaidStarted(EftRaidEventArgs e)
	{
		string text = e.RaidInfo?.MapKey;
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		RaidType raidType = e.RaidInfo?.RaidType ?? RaidType.Unknown;
		ApplyRaidTypeExtracts(raidType);
		if (string.Equals(_currentMapKey, text, StringComparison.OrdinalIgnoreCase))
		{
			ClearTrailAndMarkers();
			string value = ((raidType == RaidType.PMC) ? "PMC" : "SCAV");
			TxtStatus.Text = ((_loc.CurrentLanguage == AppLanguage.KO) ? $"레이드 시작: {text} ({value})" : $"Raid started: {text} ({value})");
			return;
		}
		for (int i = 0; i < CmbMapSelect.Items.Count; i++)
		{
			if (CmbMapSelect.Items[i] is ComboBoxItem comboBoxItem && string.Equals(comboBoxItem.Tag as string, text, StringComparison.OrdinalIgnoreCase))
			{
				if (!string.IsNullOrEmpty(_currentMapKey))
				{
					SaveMapZoomLevel(_currentMapKey, _zoomLevel);
				}
				ClearTrailAndMarkers();
				CmbMapSelect.SelectedIndex = i;
				double zoom = LoadMapZoomLevel(text);
				SetZoom(zoom);
				CenterMapInView();
				string value2 = ((raidType == RaidType.PMC) ? "PMC" : "SCAV");
				TxtStatus.Text = ((_loc.CurrentLanguage == AppLanguage.KO) ? $"레이드 시작: {text} ({value2})" : $"Raid started: {text} ({value2})");
				break;
			}
		}
	}

	private void ApplyRaidTypeExtracts(RaidType raidType)
	{
		bool flag;
		bool flag2;
		switch (raidType)
		{
		case RaidType.PMC:
			flag = true;
			flag2 = false;
			break;
		case RaidType.Scav:
			flag = false;
			flag2 = true;
			break;
		default:
			flag = true;
			flag2 = true;
			break;
		}
		_showPmcExtracts = flag;
		_showScavExtracts = flag2;
		ChkShowPmcExtracts.Checked -= ChkExtractFilter_Changed;
		ChkShowPmcExtracts.Unchecked -= ChkExtractFilter_Changed;
		ChkShowScavExtracts.Checked -= ChkExtractFilter_Changed;
		ChkShowScavExtracts.Unchecked -= ChkExtractFilter_Changed;
		ChkShowPmcExtracts.IsChecked = flag;
		ChkShowScavExtracts.IsChecked = flag2;
		ChkShowPmcExtracts.Checked += ChkExtractFilter_Changed;
		ChkShowPmcExtracts.Unchecked += ChkExtractFilter_Changed;
		ChkShowScavExtracts.Checked += ChkExtractFilter_Changed;
		ChkShowScavExtracts.Unchecked += ChkExtractFilter_Changed;
		if (_extractMarkerManager != null)
		{
			_extractMarkerManager.SetShowPmcExtracts(flag);
			_extractMarkerManager.SetShowScavExtracts(flag2);
		}
		RefreshExtractMarkers();
	}

	private void HandleRaidEnded(EftRaidEventArgs e)
	{
		ClearTrailAndMarkers();
		string text = e.RaidInfo?.MapKey ?? _currentMapKey;
		TimeSpan? timeSpan = e.RaidInfo?.Duration;
		string text2 = ((!timeSpan.HasValue) ? "" : ((_loc.CurrentLanguage == AppLanguage.KO) ? $" ({timeSpan.Value.Minutes}분 {timeSpan.Value.Seconds}초)" : $" ({timeSpan.Value.Minutes}m {timeSpan.Value.Seconds}s)"));
		TxtStatus.Text = ((_loc.CurrentLanguage == AppLanguage.KO) ? ("레이드 종료: " + text + text2) : ("Raid ended: " + text + text2));
	}

	private void ClearTrailAndMarkers()
	{
		_trackerService?.ClearTrail();
		TrailPath.Points.Clear();
		PlayerMarker.Visibility = Visibility.Collapsed;
		PlayerDot.Visibility = Visibility.Collapsed;
		TxtCoordinates.Text = "--";
		TxtLastUpdateTime.Text = "--";
	}

	private void SaveMapZoomLevel(string mapKey, double zoomLevel)
	{
		_mapZoomLevelCache[mapKey] = zoomLevel;
		Task.Run(async delegate
		{
			try
			{
				string key = "map.zoomLevel." + mapKey;
				await UserDataDbService.Instance.SetSettingAsync(key, zoomLevel.ToString("F2"), null);
			}
			catch
			{
			}
		});
	}

	private double LoadMapZoomLevel(string mapKey)
	{
		if (_mapZoomLevelCache.TryGetValue(mapKey, out var value))
		{
			return value;
		}
		try
		{
			string key = "map.zoomLevel." + mapKey;
			string setting = UserDataDbService.Instance.GetSetting(key, null);
			if (!string.IsNullOrEmpty(setting) && double.TryParse(setting, out var result))
			{
				_mapZoomLevelCache[mapKey] = result;
				return result;
			}
		}
		catch
		{
		}
		return 1.0;
	}

	private void BtnToggleTracking_Click(object sender, RoutedEventArgs e)
	{
		if (_trackerService != null)
		{
			if (_trackerService.IsWatching)
			{
				_trackerService.StopTracking();
			}
			else
			{
				_trackerService.StartTracking();
			}
		}
	}

	private void ChkFixedView_Changed(object sender, RoutedEventArgs e)
	{
		if (!_isInitializing && ChkFixedView != null)
		{
			bool valueOrDefault = ChkFixedView.IsChecked == true;
			MapSettings.Instance.AutoCenterEnabled = !valueOrDefault;
			if (_trackerService != null)
			{
				_trackerService.Settings.AutoCenterOnPosition = !valueOrDefault;
			}
		}
	}

	private void BtnClearTrail_Click(object sender, RoutedEventArgs e)
	{
		_trackerService?.ClearTrail();
		TrailPath.Points.Clear();
		PlayerMarker.Visibility = Visibility.Collapsed;
		PlayerDot.Visibility = Visibility.Collapsed;
		TxtCoordinates.Text = "--";
		TxtLastUpdateTime.Text = "--";
	}

	private async Task InitializeOverlayServiceAsync()
	{
		try
		{
			await _overlayService.InitializeAsync();
			_log.Info("Overlay MiniMap service initialized");
		}
		catch (Exception exception)
		{
			_log.Error("Failed to initialize Overlay MiniMap service", exception);
		}
	}

	private void BtnSettings_Click(object sender, RoutedEventArgs e)
	{
		ToggleSettingsPanel(show: true);
	}

	private void BtnMinimap_Click(object sender, RoutedEventArgs e)
	{
		_overlayService.ToggleOverlay();
	}

	private void BtnMinimapSettings_Click(object sender, RoutedEventArgs e)
	{
		_overlayService.ShowSettingsWindow();
	}

	private void OnOverlayVisibilityChanged(bool isVisible)
	{
		base.Dispatcher.Invoke(delegate
		{
			UpdateMinimapButtonState(isVisible);
		});
	}

	private void UpdateMinimapButtonState(bool isVisible)
	{
		if (BtnMinimap != null)
		{
			if (isVisible)
			{
				BtnMinimap.Background = (TryFindResource("AccentBrush") as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.RoyalBlue;
				BtnMinimap.Foreground = System.Windows.Media.Brushes.White;
			}
			else
			{
				BtnMinimap.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
				BtnMinimap.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
			}
		}
	}

	private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
	{
		ToggleSettingsPanel(show: false);
	}

	private void ToggleSettingsPanel(bool show)
	{
		if (show)
		{
			SettingsColumn.Width = new GridLength(320.0);
			SettingsPanel.Visibility = Visibility.Visible;
			LoadCurrentMapSettings();
		}
		else
		{
			SettingsColumn.Width = new GridLength(0.0);
			SettingsPanel.Visibility = Visibility.Collapsed;
		}
	}

	private async void CmbMapSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_isSwitchingMap || !(CmbMapSelect.SelectedItem is ComboBoxItem { Tag: var tag }) || !(tag is string mapKey))
		{
			return;
		}
		try
		{
			CancellationToken ct = GetNewCancellationToken();
			_currentMapKey = mapKey;
			_trackerService?.SetCurrentMap(mapKey);
			LoadAndApplyPerMapConfig(mapKey);
			_trackerService?.ClearTrail();
			TrailPath.Points.Clear();
			PlayerMarker.Visibility = Visibility.Collapsed;
			PlayerDot.Visibility = Visibility.Collapsed;
			UpdateFloorComboBox(mapKey);
			await LoadMapImageAsync(mapKey, centerView: true, ct);
			LoadCurrentMapSettings();
			int size = _trackerService?.Settings.PlayerMarkerSize ?? 16;
			UpdatePlayerMarkerSize(size);
			if (_objectiveService.IsLoaded)
			{
				await RefreshQuestMarkers(ct);
			}
			if (_extractService.IsLoaded)
			{
				await RefreshExtractMarkers(ct);
			}
			if (MapMarkerDbService.Instance.IsLoaded)
			{
				await RefreshMapMarkers(ct);
			}
			UpdateCustomMarkersParam();
			if (_customMarkerManager != null)
			{
				await _customMarkerManager.RefreshMarkersAsync(ct);
			}
			UpdateMapPillSelection(mapKey);
			UpdateRequiredStripsForCurrentMap();
		}
		catch (OperationCanceledException)
		{
			_log.Info("CmbMapSelect_SelectionChanged cancelled");
		}
		catch (Exception exception)
		{
			_log.Error("Error in CmbMapSelect_SelectionChanged", exception);
		}
	}

	private void OnFloorKeyPressed(int floorIndex)
	{
		if (CmbFloorSelect.Visibility == Visibility.Visible && floorIndex >= 0 && floorIndex < CmbFloorSelect.Items.Count)
		{
			CmbFloorSelect.SelectedIndex = floorIndex;
		}
	}

	private async void CmbFloorSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!(CmbFloorSelect.SelectedItem is ComboBoxItem { Tag: string tag }) || !(_currentFloorId != tag))
		{
			return;
		}
		try
		{
			CancellationToken ct = GetNewCancellationToken();
			_currentFloorId = tag;
			_trackerService?.SetCurrentFloor(tag);
			SyncPillFloorSelector();
			if (!string.IsNullOrEmpty(_currentMapKey))
			{
				await LoadMapImageAsync(_currentMapKey, centerView: false, ct);
				await RefreshExtractMarkers(ct);
				await RefreshQuestMarkers(ct);
				await RefreshMapMarkers(ct);
				UpdateCustomMarkersParam();
				_customMarkerManager?.UpdateMarkerDisplay();
			}
		}
		catch (OperationCanceledException)
		{
			_log.Info("CmbFloorSelect_SelectionChanged cancelled");
		}
		catch (Exception exception)
		{
			_log.Error("Error in CmbFloorSelect_SelectionChanged", exception);
		}
	}

	private void UpdateFloorComboBox(string mapKey)
	{
		List<MapFloorConfig> list = (_trackerService?.GetMapConfig(mapKey))?.Floors;
		CmbFloorSelect.SelectionChanged -= CmbFloorSelect_SelectionChanged;
		try
		{
			CmbFloorSelect.Items.Clear();
			_currentFloorId = null;
			if (list == null || list.Count == 0)
			{
				TxtFloorLabel.Visibility = Visibility.Collapsed;
				CmbFloorSelect.Visibility = Visibility.Collapsed;
				return;
			}
			TxtFloorLabel.Visibility = Visibility.Visible;
			CmbFloorSelect.Visibility = Visibility.Visible;
			List<MapFloorConfig> list2 = list.OrderBy((MapFloorConfig f) => f.Order).ToList();
			int num = 0;
			for (int i = 0; i < list2.Count; i++)
			{
				MapFloorConfig mapFloorConfig = list2[i];
				CmbFloorSelect.Items.Add(new ComboBoxItem
				{
					Content = mapFloorConfig.DisplayName,
					Tag = mapFloorConfig.LayerId
				});
				if (mapFloorConfig.IsDefault)
				{
					num = i;
				}
			}
			if (CmbFloorSelect.Items.Count > 0)
			{
				CmbFloorSelect.SelectedIndex = num;
				_currentFloorId = list2[num].LayerId;
			}
		}
		finally
		{
			CmbFloorSelect.SelectionChanged += CmbFloorSelect_SelectionChanged;
			SyncPillFloorSelector();
		}
	}

	private void BtnAutoDetect_Click(object sender, RoutedEventArgs e)
	{
		string text = MapTrackerSettings.TryDetectScreenshotFolder();
		if (!string.IsNullOrEmpty(text))
		{
			TxtScreenshotFolder.Text = text;
			_trackerService?.ChangeScreenshotFolder(text);
			SettingsService.Instance.MapScreenshotPath = text;
			System.Windows.MessageBox.Show((_loc.CurrentLanguage == AppLanguage.KO) ? ("스크린샷 폴더를 찾았습니다:\n" + text) : ("Screenshot folder found:\n" + text), (_loc.CurrentLanguage == AppLanguage.KO) ? "자동 탐지 성공" : "Auto Detect Success", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		List<string> possibleScreenshotPaths = MapTrackerSettings.GetPossibleScreenshotPaths();
		if (possibleScreenshotPaths.Count > 0)
		{
			string text2 = string.Join("\n", possibleScreenshotPaths);
			System.Windows.MessageBox.Show((_loc.CurrentLanguage == AppLanguage.KO) ? ("스크린샷 폴더를 자동으로 찾지 못했습니다.\n\n발견된 EFT 폴더:\n" + text2 + "\n\n수동으로 선택해주세요.") : ("Could not automatically find screenshot folder.\n\nFound EFT folder:\n" + text2 + "\n\nPlease select manually."), "Auto Detect Failed", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		else
		{
			System.Windows.MessageBox.Show((_loc.CurrentLanguage == AppLanguage.KO) ? "EFT 스크린샷 폴더를 찾을 수 없습니다.\n수동으로 폴더를 선택해주세요." : "Could not find EFT screenshot folder.\nPlease select folder manually.", "Auto Detect Failed", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = "Select Screenshot Folder",
			InitialDirectory = TxtScreenshotFolder.Text
		};
		if (openFolderDialog.ShowDialog() == true)
		{
			TxtScreenshotFolder.Text = openFolderDialog.FolderName;
			_trackerService?.ChangeScreenshotFolder(openFolderDialog.FolderName);
			SettingsService.Instance.MapScreenshotPath = openFolderDialog.FolderName;
		}
	}

	private void SliderMarkerSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (TxtMarkerSize != null)
		{
			int num = (int)e.NewValue;
			TxtMarkerSize.Text = num.ToString();
			UpdateMarkerSize(num);
			SettingsService.Instance.MapQuestMarkerSize = num;
		}
	}

	private void SliderPlayerMarkerSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (TxtPlayerMarkerSize != null)
		{
			int num = (int)e.NewValue;
			TxtPlayerMarkerSize.Text = num.ToString();
			UpdatePlayerMarkerSize(num);
			SettingsService.Instance.MapPlayerMarkerSize = num;
		}
	}

	private void ChkSettingTaskTitles_Click(object sender, RoutedEventArgs e)
	{
		bool valueOrDefault = ChkSettingTaskTitles.IsChecked == true;
		_questMarkerStyle = (valueOrDefault ? QuestMarkerStyle.DefaultWithName : QuestMarkerStyle.Default);
		SettingsService.Instance.MapQuestMarkerStyle = (int)_questMarkerStyle;
		if (CmbQuestMarkerStyle != null)
		{
			CmbQuestMarkerStyle.SelectedIndex = (int)_questMarkerStyle;
		}
		_questMarkerManager.SetQuestMarkerStyle(_questMarkerStyle);
		RefreshQuestMarkers();
	}

	private void CmbQuestMarkerStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_isInitializing && CmbQuestMarkerStyle != null && CmbQuestMarkerStyle.SelectedIndex >= 0)
		{
			_questMarkerStyle = (QuestMarkerStyle)CmbQuestMarkerStyle.SelectedIndex;
			SettingsService.Instance.MapQuestMarkerStyle = (int)_questMarkerStyle;
			RefreshQuestMarkers();
		}
	}

	private void SliderMarkerOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (TxtMarkerOpacity != null)
		{
			int value = (int)e.NewValue;
			TxtMarkerOpacity.Text = $"{value}%";
			MapSettings.Instance.MarkerOpacity = e.NewValue;
			_customMarkerManager?.UpdateMarkerDisplay();
		}
	}

	private void SliderQuestNameTextSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (TxtQuestNameTextSize != null)
		{
			int num = (int)e.NewValue;
			TxtQuestNameTextSize.Text = num.ToString();
			_questNameTextSize = num;
			SettingsService.Instance.MapQuestNameSize = num;
			RefreshQuestMarkers();
		}
	}

	private void BtnFullScreen_Click(object sender, RoutedEventArgs e)
	{
		EnterFullScreen();
	}

	private void BtnExitFullScreen_Click(object sender, RoutedEventArgs e)
	{
		ExitFullScreen();
	}

	private void EnterFullScreen()
	{
		if (Window.GetWindow(this) is MainWindow mainWindow)
		{
			mainWindow.SetFullScreenMode(fullScreen: true);
			BtnExitFullScreen.Visibility = Visibility.Visible;
		}
	}

	private void ExitFullScreen()
	{
		if (Window.GetWindow(this) is MainWindow mainWindow)
		{
			mainWindow.SetFullScreenMode(fullScreen: false);
			BtnExitFullScreen.Visibility = Visibility.Collapsed;
		}
	}

	private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
	{
		double num = ZoomPresets.FirstOrDefault((double p) => p > _zoomLevel);
		double newZoom = ((num > 0.0) ? num : (_zoomLevel * 1.25));
		ZoomToMouse(newZoom);
	}

	private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
	{
		double num = ZoomPresets.LastOrDefault((double p) => p < _zoomLevel);
		double newZoom = ((num > 0.0) ? num : (_zoomLevel * 0.8));
		ZoomToMouse(newZoom);
	}

	private void ZoomToMouse(double newZoom)
	{
		double zoomLevel = _zoomLevel;
		newZoom = Math.Clamp(newZoom, 0.1, 5.0);
		if (!(Math.Abs(newZoom - zoomLevel) < 0.001))
		{
			System.Windows.Point position = Mouse.GetPosition(MapViewerGrid);
			double num = (position.X - MapTranslate.X) / zoomLevel;
			double num2 = (position.Y - MapTranslate.Y) / zoomLevel;
			MapTranslate.X = position.X - num * newZoom;
			MapTranslate.Y = position.Y - num2 * newZoom;
			SetZoom(newZoom);
		}
	}

	private void CmbZoomLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (CmbZoomLevel.SelectedItem is string zoomText)
		{
			ParseAndSetZoom(zoomText);
		}
	}

	private void CmbZoomLevel_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			ParseAndSetZoom(CmbZoomLevel.Text);
			e.Handled = true;
		}
	}

	private void ParseAndSetZoom(string zoomText)
	{
		if (double.TryParse(zoomText.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			SetZoom(result / 100.0);
		}
	}

	private void BtnResetView_Click(object sender, RoutedEventArgs e)
	{
		SetZoom(1.0);
		CenterMapInView();
	}

	private void MapViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (NoMapPanel.Visibility != 0)
		{
			if (QuestObjectiveRelocationService.Instance.IsPlacementActive)
			{
				System.Windows.Point position = e.GetPosition(MapCanvas);
				HandlePlacementClick(position);
				e.Handled = true;
				return;
			}
			_isDragging = true;
			_dragStartPoint = e.GetPosition(MapViewerGrid);
			_dragStartTranslateX = MapTranslate.X;
			_dragStartTranslateY = MapTranslate.Y;
			MapViewerGrid.CaptureMouse();
			MapCanvas.Cursor = System.Windows.Input.Cursors.ScrollAll;
		}
	}

	private void MapViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (_isDragging)
		{
			_isDragging = false;
			MapViewerGrid.ReleaseMouseCapture();
			MapCanvas.Cursor = (QuestObjectiveRelocationService.Instance.IsPlacementActive ? System.Windows.Input.Cursors.Cross : System.Windows.Input.Cursors.Arrow);
		}
	}

	private void MapViewer_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (_isDragging)
		{
			System.Windows.Point position = e.GetPosition(MapViewerGrid);
			double num = position.X - _dragStartPoint.X;
			double num2 = position.Y - _dragStartPoint.Y;
			MapTranslate.X = _dragStartTranslateX + num;
			MapTranslate.Y = _dragStartTranslateY + num2;
		}
	}

	private void MapViewer_MouseWheel(object sender, MouseWheelEventArgs e)
	{
		System.Windows.Point position = e.GetPosition(MapViewerGrid);
		double zoomLevel = _zoomLevel;
		double num = ((e.Delta > 0) ? 1.15 : 0.87);
		double num2 = Math.Clamp(_zoomLevel * num, 0.1, 5.0);
		if (!(Math.Abs(num2 - zoomLevel) < 0.001))
		{
			double num3 = (position.X - MapTranslate.X) / zoomLevel;
			double num4 = (position.Y - MapTranslate.Y) / zoomLevel;
			MapTranslate.X = position.X - num3 * num2;
			MapTranslate.Y = position.Y - num4 * num2;
			SetZoom(num2);
			e.Handled = true;
		}
	}

	private async Task LoadMapImageAsync(string mapKey, bool centerView = true, CancellationToken ct = default(CancellationToken))
	{
		ct.ThrowIfCancellationRequested();
		MapConfig config = _trackerService?.GetMapConfig(mapKey);
		if (config == null)
		{
			ShowNoMapPanel(show: true);
			return;
		}
		string imagePath = config.ImagePath;
		if (string.IsNullOrEmpty(imagePath) && !string.IsNullOrEmpty(config.SvgFileName))
		{
			imagePath = System.IO.Path.Combine("Assets", "DB", "Maps", config.SvgFileName);
		}
		if (!string.IsNullOrEmpty(imagePath) && !System.IO.Path.IsPathRooted(imagePath))
		{
			imagePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, imagePath);
		}
		if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
		{
			ShowNoMapPanel(show: true);
			return;
		}
		try
		{
			if (System.IO.Path.GetExtension(imagePath).ToLowerInvariant() == ".svg")
			{
				MapImage.Visibility = Visibility.Collapsed;
				MapSvg.Visibility = Visibility.Visible;
				MapSvg.Source = null;
				IEnumerable<string> visibleFloors = null;
				IEnumerable<string> allFloors = null;
				string backgroundFloorId = null;
				double backgroundOpacity = 0.3;
				if (config.Floors != null && config.Floors.Count > 0 && !string.IsNullOrEmpty(_currentFloorId))
				{
					allFloors = config.Floors.Select((MapFloorConfig f) => f.LayerId);
					visibleFloors = new string[1] { _currentFloorId };
					MapFloorConfig mapFloorConfig = config.Floors.FirstOrDefault((MapFloorConfig f) => f.IsDefault);
					MapFloorConfig mapFloorConfig2 = config.Floors.FirstOrDefault((MapFloorConfig f) => string.Equals(f.LayerId, _currentFloorId, StringComparison.OrdinalIgnoreCase));
					if (mapFloorConfig != null && !string.Equals(_currentFloorId, mapFloorConfig.LayerId, StringComparison.OrdinalIgnoreCase))
					{
						backgroundFloorId = mapFloorConfig.LayerId;
						if (mapFloorConfig2 != null && mapFloorConfig2.Order < 0)
						{
							backgroundOpacity = 0.15;
						}
					}
				}
				if (visibleFloors != null)
				{
					string uriString = await Task.Run(delegate
					{
						string contents = new SvgStylePreprocessor().ProcessSvgFile(imagePath, visibleFloors, allFloors, backgroundFloorId, backgroundOpacity);
						string text = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"legacy_map_{Guid.NewGuid()}.svg");
						File.WriteAllText(text, contents);
						return text;
					}, ct);
					ct.ThrowIfCancellationRequested();
					MapSvg.Source = new Uri(uriString, UriKind.Absolute);
				}
				else
				{
					MapSvg.Source = new Uri(imagePath, UriKind.Absolute);
				}
				MapSvg.Width = config.ImageWidth;
				MapSvg.Height = config.ImageHeight;
				MapCanvas.Width = config.ImageWidth;
				MapCanvas.Height = config.ImageHeight;
				Canvas.SetLeft(MapSvg, 0.0);
				Canvas.SetTop(MapSvg, 0.0);
			}
			else
			{
				MapSvg.Visibility = Visibility.Collapsed;
				MapImage.Visibility = Visibility.Visible;
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.UriSource = new Uri(imagePath, UriKind.Absolute);
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.EndInit();
				MapImage.Source = bitmapImage;
				MapCanvas.Width = bitmapImage.PixelWidth;
				MapCanvas.Height = bitmapImage.PixelHeight;
				Canvas.SetLeft(MapImage, 0.0);
				Canvas.SetTop(MapImage, 0.0);
			}
			ShowNoMapPanel(show: false);
			if (centerView)
			{
				CenterMapInView();
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			_log.Error("Error in LoadMapImageAsync for " + mapKey, exception);
			ShowNoMapPanel(show: true);
		}
	}

	private void CenterMapInView()
	{
		FitAndCenterMapInView();
	}

	private void FitAndCenterMapInView(bool forceFit = false)
	{
		double actualWidth = MapViewerGrid.ActualWidth;
		double actualHeight = MapViewerGrid.ActualHeight;
		if (actualWidth <= 0.0 || actualHeight <= 0.0)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				FitAndCenterMapInView(forceFit);
			}, DispatcherPriority.Loaded);
			return;
		}
		Rect mapPlayableBounds = OverlayMiniMapWindow.GetMapPlayableBounds(string.IsNullOrEmpty(_currentMapKey) ? null : _trackerService?.GetMapConfig(_currentMapKey));
		if (forceFit || _zoomLevel < 0.1 || (_zoomLevel >= 0.99 && _zoomLevel <= 1.01))
		{
			double val = actualWidth * 0.95 / mapPlayableBounds.Width;
			double val2 = actualHeight * 0.95 / mapPlayableBounds.Height;
			double value = Math.Min(val, val2);
			value = Math.Clamp(value, 0.1, 5.0);
			SetZoom(value);
		}
		double num = mapPlayableBounds.X + mapPlayableBounds.Width / 2.0;
		double num2 = mapPlayableBounds.Y + mapPlayableBounds.Height / 2.0;
		MapTranslate.X = actualWidth / 2.0 - num * _zoomLevel;
		MapTranslate.Y = actualHeight / 2.0 - num2 * _zoomLevel;
	}

	private void ShowNoMapPanel(bool show)
	{
		NoMapPanel.Visibility = ((!show) ? Visibility.Collapsed : Visibility.Visible);
		if (show)
		{
			MapImage.Visibility = Visibility.Collapsed;
			MapSvg.Visibility = Visibility.Collapsed;
		}
	}

	private void LoadCurrentMapSettings()
	{
	}

	private void UpdateMarkerPosition(ScreenPosition position)
	{
		if (!string.Equals(_currentMapKey, position.MapKey, StringComparison.OrdinalIgnoreCase))
		{
			for (int i = 0; i < CmbMapSelect.Items.Count; i++)
			{
				if (CmbMapSelect.Items[i] is ComboBoxItem comboBoxItem && string.Equals(comboBoxItem.Tag as string, position.MapKey, StringComparison.OrdinalIgnoreCase))
				{
					CmbMapSelect.SelectedIndex = i;
					break;
				}
			}
		}
		MapTrackerService? trackerService = _trackerService;
		if ((trackerService == null || trackerService.Settings.ShowDirection) && position.Angle.HasValue)
		{
			PlayerMarker.Visibility = Visibility.Visible;
			PlayerDot.Visibility = Visibility.Collapsed;
			MarkerTranslation.X = position.X;
			MarkerTranslation.Y = position.Y;
			double num = position.Angle.GetValueOrDefault();
			if (string.Equals(_currentMapKey, "Labs", StringComparison.OrdinalIgnoreCase))
			{
				num -= 90.0;
			}
			else if (string.Equals(_currentMapKey, "Factory", StringComparison.OrdinalIgnoreCase))
			{
				num += 90.0;
			}
			MarkerRotation.Angle = num;
		}
		else
		{
			PlayerMarker.Visibility = Visibility.Collapsed;
			PlayerDot.Visibility = Visibility.Visible;
			DotTranslation.X = position.X;
			DotTranslation.Y = position.Y;
		}
	}

	private void UpdateTrailPath()
	{
		if (_trackerService == null || !_trackerService.Settings.ShowTrail)
		{
			return;
		}
		TrailPath.Points.Clear();
		foreach (ScreenPosition trailPosition in _trackerService.TrailPositions)
		{
			TrailPath.Points.Add(new System.Windows.Point(trailPosition.X, trailPosition.Y));
		}
	}

	private void UpdateCoordinatesDisplay(ScreenPosition position)
	{
		EftPosition originalPosition = position.OriginalPosition;
		if (originalPosition != null)
		{
			string value = (originalPosition.Angle.HasValue ? $", Angle: {originalPosition.Angle:F1}°" : "");
			TxtCoordinates.Text = $"Map: {originalPosition.MapName}, X: {originalPosition.X:F2}, Y: {originalPosition.Y:F2}{value}";
		}
		else
		{
			TxtCoordinates.Text = $"X: {position.X:F0}, Y: {position.Y:F0}";
		}
		TxtLastUpdateTime.Text = DateTime.Now.ToString("HH:mm:ss");
	}

	private async void UpdateMarkerSize(int size)
	{
		if (_trackerService != null)
		{
			_trackerService.Settings.MarkerSize = size;
			_trackerService.SaveSettings();
			try
			{
				CancellationToken newCancellationToken = GetNewCancellationToken();
				await RefreshQuestMarkers(newCancellationToken);
			}
			catch (OperationCanceledException)
			{
			}
		}
	}

	private void UpdatePlayerMarkerSize(int size)
	{
		double num = (double)size / 16.0;
		double num2 = (_trackerService?.GetMapConfig(_currentMapKey ?? ""))?.MarkerScale ?? 1.0;
		double num3 = num * num2;
		MarkerScale.ScaleX = num3;
		MarkerScale.ScaleY = num3;
		DotScale.ScaleX = num3;
		DotScale.ScaleY = num3;
		if (_trackerService != null)
		{
			_trackerService.Settings.PlayerMarkerSize = size;
			_trackerService.SaveSettings();
		}
	}

	private void UpdateMarkerVisibility()
	{
		if (_trackerService != null)
		{
			ScreenPosition currentPosition = _trackerService.CurrentPosition;
			if (currentPosition == null)
			{
				PlayerMarker.Visibility = Visibility.Collapsed;
				PlayerDot.Visibility = Visibility.Collapsed;
			}
			else
			{
				bool flag = _trackerService.Settings.ShowDirection && currentPosition.Angle.HasValue;
				PlayerMarker.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
				PlayerDot.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
			}
		}
	}

	private void SetZoom(double zoom)
	{
		_zoomLevel = Math.Clamp(zoom, 0.1, 5.0);
		MapScale.ScaleX = _zoomLevel;
		MapScale.ScaleY = _zoomLevel;
		CmbZoomLevel.SelectionChanged -= CmbZoomLevel_SelectionChanged;
		CmbZoomLevel.Text = $"{_zoomLevel * 100.0:F0}%";
		CmbZoomLevel.SelectionChanged += CmbZoomLevel_SelectionChanged;
		UpdateMarkerScales();
	}

	private void UpdateMarkerScales()
	{
		double inverseScale = 1.0 / _zoomLevel;
		if (_questMarkerManager != null)
		{
			_questMarkerManager.SetZoomLevel(_zoomLevel);
			_questMarkerManager.UpdateMarkerScales();
		}
		if (_extractMarkerManager != null)
		{
			_extractMarkerManager.SetZoomLevel(_zoomLevel);
			_extractMarkerManager.UpdateMarkerScales();
		}
		if (_customMarkerManager != null)
		{
			_customMarkerManager.SetZoomLevel(_zoomLevel);
			_customMarkerManager.UpdateMarkerScales();
		}
		if (_mapMarkersManager != null)
		{
			_mapMarkersManager.SetZoomLevel(_zoomLevel);
			_mapMarkersManager.UpdateMarkerScales();
		}
		UpdatePlayerMarkerScale(inverseScale);
		UpdateTrailStrokeThickness(inverseScale);
	}

	private void UpdateTrailStrokeThickness(double inverseScale)
	{
		TrailPath.StrokeThickness = 2.0 * inverseScale;
	}

	private void UpdatePlayerMarkerScale(double inverseScale)
	{
		if (MarkerScale != null)
		{
			MarkerScale.ScaleX = inverseScale;
			MarkerScale.ScaleY = inverseScale;
		}
		if (DotScale != null)
		{
			DotScale.ScaleX = inverseScale;
			DotScale.ScaleY = inverseScale;
		}
	}

	private BitmapSource? ConvertSvgToPngWithPreprocessing(string svgPath, int width, int height)
	{
		return ConvertSvgToPngWithPreprocessing(svgPath, width, height, null, null);
	}

	private BitmapSource? ConvertSvgToPngWithPreprocessing(string svgPath, int width, int height, IEnumerable<string>? visibleFloors, IEnumerable<string>? allFloors, string? backgroundFloorId = null, double backgroundOpacity = 0.3)
	{
		try
		{
			string svgContent = new SvgStylePreprocessor().ProcessSvgFile(svgPath, visibleFloors, allFloors, backgroundFloorId, backgroundOpacity);
			return RenderSvgContent(svgContent, width, height);
		}
		catch
		{
			return null;
		}
	}

	private BitmapSource? RenderSvgContent(string svgContent, int width, int height)
	{
		try
		{
			WpfDrawingSettings settings = new WpfDrawingSettings
			{
				IncludeRuntime = true,
				TextAsGeometry = false,
				OptimizePath = true,
				CultureInfo = CultureInfo.InvariantCulture,
				EnsureViewboxSize = false,
				EnsureViewboxPosition = false,
				IgnoreRootViewbox = false
			};
			DrawingGroup drawingGroup;
			using (MemoryStream svgStream = new MemoryStream(Encoding.UTF8.GetBytes(svgContent)))
			{
				drawingGroup = new FileSvgReader(settings).Read(svgStream);
			}
			if (drawingGroup == null)
			{
				return null;
			}
			Rect bounds = drawingGroup.Bounds;
			double scaleX = (double)width / bounds.Width;
			double scaleY = (double)height / bounds.Height;
			DrawingVisual drawingVisual = new DrawingVisual();
			using (DrawingContext drawingContext = drawingVisual.RenderOpen())
			{
				TransformGroup transformGroup = new TransformGroup();
				transformGroup.Children.Add(new TranslateTransform(0.0 - bounds.X, 0.0 - bounds.Y));
				transformGroup.Children.Add(new ScaleTransform(scaleX, scaleY));
				drawingContext.PushTransform(transformGroup);
				drawingContext.DrawDrawing(drawingGroup);
				drawingContext.Pop();
			}
			RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(width, height, 96.0, 96.0, PixelFormats.Pbgra32);
			renderTargetBitmap.Render(drawingVisual);
			renderTargetBitmap.Freeze();
			return renderTargetBitmap;
		}
		catch
		{
			return null;
		}
	}

	private async Task LoadQuestObjectivesAsync(CancellationToken ct = default(CancellationToken))
	{
		_ = 1;
		try
		{
			ct.ThrowIfCancellationRequested();
			TxtStatus.Text = "Loading quest objectives...";
			await _objectiveService.EnsureLoadedAsync(delegate(string msg)
			{
				base.Dispatcher.Invoke(() => TxtStatus.Text = msg);
			}).WaitAsync(ct);
			int count = _objectiveService.AllObjectives.Count;
			TxtStatus.Text = $"Loaded {count} quest objectives";
			if (!string.IsNullOrEmpty(_currentMapKey))
			{
				await RefreshQuestMarkers(ct);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TxtStatus.Text = "Error loading objectives: " + ex2.Message;
		}
	}

	private async void OnHiddenQuestsChanged()
	{
		try
		{
			CancellationToken newCancellationToken = GetNewCancellationToken();
			await RefreshQuestMarkers(newCancellationToken);
			UpdateMapPillTaskCounts();
			UpdateRequiredStripsForCurrentMap();
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async void OnQuestProgressChanged(object? sender, EventArgs e)
	{
		try
		{
			CancellationToken newCancellationToken = GetNewCancellationToken();
			await RefreshQuestMarkers(newCancellationToken);
			UpdateMapPillTaskCounts();
			UpdateRequiredStripsForCurrentMap();
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async void OnObjectiveProgressChanged(object? sender, ObjectiveProgressChangedEventArgs e)
	{
		try
		{
			CancellationToken newCancellationToken = GetNewCancellationToken();
			await RefreshQuestMarkers(newCancellationToken);
			UpdateMapPillTaskCounts();
			UpdateRequiredStripsForCurrentMap();
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void ChkShowQuestMarkers_Changed(object sender, RoutedEventArgs e)
	{
		_showQuestMarkers = ChkShowQuestMarkers?.IsChecked ?? true;
		if (QuestMarkersContainer != null)
		{
			QuestMarkersContainer.Visibility = ((!_showQuestMarkers) ? Visibility.Collapsed : Visibility.Visible);
		}
		SettingsService.Instance.MapShowQuests = _showQuestMarkers;
		OverlayMiniMapService.Instance.RefreshMap();
	}

	private bool IsLocationOnCurrentMap(QuestObjectiveLocation location, MapConfig config)
	{
		if (string.IsNullOrEmpty(_currentMapKey))
		{
			return false;
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { _currentMapKey };
		if (config.Aliases != null)
		{
			foreach (string alias in config.Aliases)
			{
				hashSet.Add(alias);
			}
		}
		if (!string.IsNullOrEmpty(config.DisplayName))
		{
			hashSet.Add(config.DisplayName);
		}
		if (!string.IsNullOrEmpty(location.MapName) && hashSet.Contains(location.MapName))
		{
			return true;
		}
		if (!string.IsNullOrEmpty(location.MapNormalizedName) && hashSet.Contains(location.MapNormalizedName))
		{
			return true;
		}
		return false;
	}

	private async Task RefreshQuestMarkers(CancellationToken ct = default(CancellationToken))
	{
		if (_questMarkerManager != null)
		{
			_questMarkerManager.SetCurrentMap(_currentMapKey);
			_questMarkerManager.SetCurrentFloor(_currentFloorId);
			_questMarkerManager.SetZoomLevel(_zoomLevel);
			_questMarkerManager.SetShowQuestMarkers(_showQuestMarkers);
			_questMarkerManager.SetQuestMarkerStyle(_questMarkerStyle);
			_questMarkerManager.SetQuestNameTextSize(_questNameTextSize);
			_questMarkerManager.SetHideCompletedObjectives(_hideCompletedObjectives);
			await _questMarkerManager.RefreshMarkersAsync(ct);
			_currentMapObjectives = _questMarkerManager.GetCurrentMapObjectives();
		}
	}

	private static TaskObjectiveWithLocation? GetObjectiveFromTag(object? tag)
	{
		if (!(tag is TaskObjectiveWithLocation result))
		{
			if (tag is AreaMarkerTag areaMarkerTag)
			{
				return areaMarkerTag.Objective;
			}
			return null;
		}
		return result;
	}

	private static (double X, double Y) GetMarkerPosition(Canvas markerCanvas)
	{
		if (markerCanvas.Tag is AreaMarkerTag)
		{
			foreach (object child in markerCanvas.Children)
			{
				if (child is Canvas canvas && canvas.Tag is TaskObjectiveWithLocation)
				{
					return (X: Canvas.GetLeft(canvas), Y: Canvas.GetTop(canvas));
				}
			}
		}
		return (X: Canvas.GetLeft(markerCanvas), Y: Canvas.GetTop(markerCanvas));
	}

	private static void HideMarkerText(Canvas markerCanvas)
	{
		SetMarkerTextVisibility(markerCanvas, Visibility.Collapsed);
	}

	private static void ShowMarkerText(Canvas markerCanvas)
	{
		SetMarkerTextVisibility(markerCanvas, Visibility.Visible);
	}

	private static void SetMarkerTextVisibility(Canvas markerCanvas, Visibility visibility)
	{
		Canvas canvas = markerCanvas;
		if (markerCanvas.Tag is AreaMarkerTag)
		{
			foreach (object child in markerCanvas.Children)
			{
				if (child is Canvas canvas2 && canvas2.Tag is TaskObjectiveWithLocation)
				{
					canvas = canvas2;
					break;
				}
			}
		}
		foreach (object child2 in canvas.Children)
		{
			if (child2 is StackPanel stackPanel)
			{
				stackPanel.Visibility = visibility;
			}
			else if (child2 is Border border && border.Tag is TaskObjectiveWithLocation)
			{
				border.Visibility = visibility;
			}
		}
	}

	private void SelectFloorForObjective(TaskObjectiveWithLocation objective)
	{
		if (CmbFloorSelect.Visibility != 0)
		{
			return;
		}
		QuestObjectiveLocation questObjectiveLocation = objective.Locations.FirstOrDefault((QuestObjectiveLocation l) => !string.IsNullOrEmpty(_currentMapKey) && (l.MapName.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase) || (l.MapNormalizedName?.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase) ?? false)));
		if (questObjectiveLocation == null)
		{
			questObjectiveLocation = objective.Locations.FirstOrDefault();
		}
		if (questObjectiveLocation == null || string.IsNullOrEmpty(questObjectiveLocation.FloorId) || string.Equals(_currentFloorId, questObjectiveLocation.FloorId, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		for (int i = 0; i < CmbFloorSelect.Items.Count; i++)
		{
			if (CmbFloorSelect.Items[i] is ComboBoxItem comboBoxItem && string.Equals(comboBoxItem.Tag as string, questObjectiveLocation.FloorId, StringComparison.OrdinalIgnoreCase))
			{
				CmbFloorSelect.SelectedIndex = i;
				break;
			}
		}
	}

	private void ShowQuestDrawer(TaskObjectiveWithLocation? selectedObjective = null)
	{
		if (selectedObjective != null)
		{
			TaskObjectiveWithLocation taskObjectiveWithLocation = _currentMapObjectives.FirstOrDefault((TaskObjectiveWithLocation o) => o.ObjectiveId == selectedObjective.ObjectiveId);
			_selectedObjective = taskObjectiveWithLocation ?? selectedObjective;
		}
		else
		{
			_selectedObjective = null;
		}
		_questMarkerManager?.SetSelectedObjective(_selectedObjective);
		if (_selectedObjective != null)
		{
			CenterOnObjective(_selectedObjective);
		}
	}

	private void OpenQuestDrawer()
	{
	}

	private void CloseQuestDrawer()
	{
		_selectedObjective = null;
		_questMarkerManager?.SetSelectedObjective(null);
	}

	private void QuestObjectiveItem_Click(object sender, MouseButtonEventArgs e)
	{
		if (!(sender is FrameworkElement { DataContext: var dataContext }))
		{
			return;
		}
		QuestObjectiveViewModel vm = dataContext as QuestObjectiveViewModel;
		if (vm != null)
		{
			_selectedObjective = _currentMapObjectives.FirstOrDefault((TaskObjectiveWithLocation o) => o.ObjectiveId == vm.Objective.ObjectiveId) ?? vm.Objective;
			SelectFloorForObjective(_selectedObjective);
			_questMarkerManager?.SetSelectedObjective(_selectedObjective);
			RefreshQuestDrawer();
			CenterOnObjective(_selectedObjective);
		}
	}

	private QuestObjectiveViewModel? GetObjectiveVmFromSender(object sender)
	{
		if (sender is FrameworkElement frameworkElement)
		{
			if (frameworkElement.DataContext is QuestObjectiveViewModel result)
			{
				return result;
			}
			if (frameworkElement.Tag is QuestObjectiveViewModel result2)
			{
				return result2;
			}
			if (frameworkElement is MenuItem menuItem)
			{
				if (menuItem.DataContext is QuestObjectiveViewModel result3)
				{
					return result3;
				}
				if (menuItem.Tag is QuestObjectiveViewModel result4)
				{
					return result4;
				}
				if (menuItem.Parent is ContextMenu { PlacementTarget: FrameworkElement { DataContext: QuestObjectiveViewModel dataContext } })
				{
					return dataContext;
				}
			}
		}
		return null;
	}

	private void MenuCardRelocate_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		QuestObjectiveViewModel objectiveVmFromSender = GetObjectiveVmFromSender(sender);
		if (objectiveVmFromSender != null && objectiveVmFromSender.CanRelocate)
		{
			QuestObjectiveRelocationService.Instance.StartPlacement(objectiveVmFromSender.ObjectiveId, objectiveVmFromSender.Description, objectiveVmFromSender.QuestName, _currentMapKey);
		}
	}

	private async void MenuCardRevert_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		QuestObjectiveViewModel objectiveVmFromSender = GetObjectiveVmFromSender(sender);
		if (objectiveVmFromSender != null)
		{
			QuestObjectiveRelocationService instance = QuestObjectiveRelocationService.Instance;
			if (instance.IsPlacementActive && instance.TargetObjectiveId == objectiveVmFromSender.ObjectiveId)
			{
				instance.CancelPlacement();
				PlacementModeBanner.Visibility = Visibility.Collapsed;
				MapCanvas.Cursor = System.Windows.Input.Cursors.Arrow;
				MapViewerGrid.Cursor = System.Windows.Input.Cursors.Arrow;
			}
			if (System.Windows.MessageBox.Show("Do you want to restore the factory location for this objective (" + objectiveVmFromSender.Description + ")?", "Restore Location", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
			{
				await instance.RestoreObjectiveToFactoryAsync(objectiveVmFromSender.ObjectiveId);
			}
		}
	}

	private void MenuCardCenter_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		QuestObjectiveViewModel vm = GetObjectiveVmFromSender(sender);
		if (vm != null)
		{
			_selectedObjective = _currentMapObjectives.FirstOrDefault((TaskObjectiveWithLocation o) => o.ObjectiveId == vm.Objective.ObjectiveId) ?? vm.Objective;
			SelectFloorForObjective(_selectedObjective);
			_questMarkerManager?.SetSelectedObjective(_selectedObjective);
			RefreshQuestDrawer();
			CenterOnObjective(_selectedObjective);
		}
	}

	private void MenuCardOpenManager_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		OpenObjectiveRelocationManager();
	}

	private void BtnOpenRelocationManager_Click(object sender, RoutedEventArgs e)
	{
		OpenObjectiveRelocationManager();
	}

	private void InitializeMapPills()
	{
		if (_mapPills.Count == 0)
		{
			_mapPills.Add(new MapPillItem
			{
				MapKey = "ground-zero",
				DisplayName = "Ground Zero"
			});
			_mapPills.Add(new MapPillItem
			{
				MapKey = "customs",
				DisplayName = "Customs"
			});
			_mapPills.Add(new MapPillItem
			{
				MapKey = "woods",
				DisplayName = "Woods"
			});
			_mapPills.Add(new MapPillItem
			{
				MapKey = "factory",
				DisplayName = "Factory"
			});
			_mapPills.Add(new MapPillItem
			{
				MapKey = "interchange",
				DisplayName = "Interchange"
			});
			_mapPills.Add(new MapPillItem
			{
				MapKey = "shoreline",
				DisplayName = "Shoreline"
			});
			_mapPills.Add(new MapPillItem
			{
				MapKey = "reserve",
				DisplayName = "Reserve"
			});
			_mapPills.Add(new MapPillItem
			{
				MapKey = "lighthouse",
				DisplayName = "Lighthouse"
			});
			_mapPills.Add(new MapPillItem
			{
				MapKey = "streets",
				DisplayName = "Streets of Tarkov"
			});
			_mapPills.Add(new MapPillItem
			{
				MapKey = "the-lab",
				DisplayName = "The Lab"
			});
		}
		MapSelectorPillsList.ItemsSource = _mapPills;
		RequiredItemsList.ItemsSource = _requiredItems;
		RequiredKeysList.ItemsSource = _requiredKeys;
		UpdateMapPillTaskCounts();
		if (!string.IsNullOrEmpty(_currentMapKey))
		{
			UpdateMapPillSelection(_currentMapKey);
			UpdateRequiredStripsForCurrentMap();
		}
	}

	private void InitializeClockWidget()
	{
		_clockTimer?.Stop();
		_clockTimer = null;
	}

	private void UpdateClockWidget()
	{
	}

	private void MapPill_Click(object sender, RoutedEventArgs e)
	{
		string text = (sender as FrameworkElement)?.Tag as string;
		if (text != null || ((sender as FrameworkElement)?.DataContext is MapPillItem mapPillItem && !string.IsNullOrEmpty(text = mapPillItem.MapKey)))
		{
			SelectMap(text);
		}
	}

	private void UpdateMapPillSelection(string? mapKey)
	{
		if (string.IsNullOrEmpty(mapKey))
		{
			return;
		}
		string b = _trackerService?.ResolveMapKey(mapKey) ?? mapKey;
		foreach (MapPillItem mapPill in _mapPills)
		{
			mapPill.IsSelected = string.Equals(mapPill.MapKey, mapKey, StringComparison.OrdinalIgnoreCase) || string.Equals(mapPill.MapKey, b, StringComparison.OrdinalIgnoreCase) || string.Equals(_trackerService?.ResolveMapKey(mapPill.MapKey), b, StringComparison.OrdinalIgnoreCase);
		}
		SyncPillFloorSelector();
	}

	private void SyncPillFloorSelector()
	{
		if (CmbFloorSelect.Visibility == Visibility.Visible && CmbFloorSelect.Items.Count > 0)
		{
			TxtPillFloorLabel.Visibility = Visibility.Visible;
			CmbPillFloorSelect.Visibility = Visibility.Visible;
			if (OverlayFloorSelectorPanel != null)
			{
				OverlayFloorSelectorPanel.Visibility = Visibility.Collapsed;
			}
			UpdateDiscreetFloorControl();
			CmbPillFloorSelect.SelectionChanged -= CmbPillFloorSelect_SelectionChanged;
			CmbPillFloorSelect.Items.Clear();
			if (CmbOverlayFloorSelect != null)
			{
				CmbOverlayFloorSelect.SelectionChanged -= CmbOverlayFloorSelect_SelectionChanged;
				CmbOverlayFloorSelect.Items.Clear();
			}
			foreach (object item in (IEnumerable)CmbFloorSelect.Items)
			{
				if (item is ComboBoxItem comboBoxItem)
				{
					CmbPillFloorSelect.Items.Add(new ComboBoxItem
					{
						Content = comboBoxItem.Content,
						Tag = comboBoxItem.Tag
					});
					if (CmbOverlayFloorSelect != null)
					{
						CmbOverlayFloorSelect.Items.Add(new ComboBoxItem
						{
							Content = comboBoxItem.Content,
							Tag = comboBoxItem.Tag
						});
					}
				}
			}
			CmbPillFloorSelect.SelectedIndex = CmbFloorSelect.SelectedIndex;
			CmbPillFloorSelect.SelectionChanged += CmbPillFloorSelect_SelectionChanged;
			if (CmbOverlayFloorSelect != null)
			{
				CmbOverlayFloorSelect.SelectedIndex = CmbFloorSelect.SelectedIndex;
				CmbOverlayFloorSelect.SelectionChanged += CmbOverlayFloorSelect_SelectionChanged;
			}
		}
		else
		{
			TxtPillFloorLabel.Visibility = Visibility.Collapsed;
			CmbPillFloorSelect.Visibility = Visibility.Collapsed;
			if (OverlayFloorSelectorPanel != null)
			{
				OverlayFloorSelectorPanel.Visibility = Visibility.Collapsed;
			}
		}
	}

	private void CmbPillFloorSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (CmbPillFloorSelect.SelectedIndex >= 0 && CmbPillFloorSelect.SelectedIndex < CmbFloorSelect.Items.Count && CmbFloorSelect.SelectedIndex != CmbPillFloorSelect.SelectedIndex)
		{
			CmbFloorSelect.SelectedIndex = CmbPillFloorSelect.SelectedIndex;
		}
	}

	private void CmbOverlayFloorSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (CmbOverlayFloorSelect != null && CmbOverlayFloorSelect.SelectedIndex >= 0 && CmbOverlayFloorSelect.SelectedIndex < CmbFloorSelect.Items.Count && CmbFloorSelect.SelectedIndex != CmbOverlayFloorSelect.SelectedIndex)
		{
			CmbFloorSelect.SelectedIndex = CmbOverlayFloorSelect.SelectedIndex;
		}
	}

	private void UpdateDiscreetFloorControl()
	{
		if (DiscreetFloorControl == null || TxtFloorCode == null)
		{
			return;
		}
		if (CmbFloorSelect != null && CmbFloorSelect.Visibility == Visibility.Visible && CmbFloorSelect.Items.Count > 1)
		{
			DiscreetFloorControl.Visibility = Visibility.Visible;
			int selectedIndex = CmbFloorSelect.SelectedIndex;
			if (selectedIndex >= 0 && selectedIndex < CmbFloorSelect.Items.Count)
			{
				ComboBoxItem comboBoxItem = CmbFloorSelect.Items[selectedIndex] as ComboBoxItem;
				string text = comboBoxItem?.Content?.ToString() ?? "";
				string layerId = comboBoxItem?.Tag?.ToString() ?? "";
				string floorShortCode = MapFloorHelper.GetFloorShortCode(_trackerService.GetMapConfig(_currentMapKey ?? "")?.Floors?.FirstOrDefault((MapFloorConfig f) => string.Equals(f.LayerId, layerId, StringComparison.OrdinalIgnoreCase)), text, layerId);
				TxtFloorCode.Text = floorShortCode;
				TxtFloorCode.Tag = $"Planta: {text} ({floorShortCode})";
				if (BtnFloorDown != null)
				{
					BtnFloorDown.IsEnabled = selectedIndex > 0;
					BtnFloorDown.Opacity = ((selectedIndex > 0) ? 1.0 : 0.4);
				}
				if (BtnFloorUp != null)
				{
					BtnFloorUp.IsEnabled = selectedIndex < CmbFloorSelect.Items.Count - 1;
					BtnFloorUp.Opacity = ((selectedIndex < CmbFloorSelect.Items.Count - 1) ? 1.0 : 0.4);
				}
			}
		}
		else
		{
			DiscreetFloorControl.Visibility = Visibility.Collapsed;
		}
	}

	private void BtnFloorUp_Click(object sender, RoutedEventArgs e)
	{
		if (CmbFloorSelect != null && CmbFloorSelect.SelectedIndex < CmbFloorSelect.Items.Count - 1)
		{
			CmbFloorSelect.SelectedIndex++;
			UpdateDiscreetFloorControl();
		}
	}

	private void BtnFloorDown_Click(object sender, RoutedEventArgs e)
	{
		if (CmbFloorSelect != null && CmbFloorSelect.SelectedIndex > 0)
		{
			CmbFloorSelect.SelectedIndex--;
			UpdateDiscreetFloorControl();
		}
	}

	private void UpdateMapPillTaskCounts()
	{
		IReadOnlyList<TarkovTask> allQuests = QuestDbService.Instance.AllQuests;
		if (allQuests == null || allQuests.Count == 0)
		{
			return;
		}
		List<TarkovTask> source = allQuests.Where((TarkovTask q) => _progressService.GetStatus(q) == QuestStatus.Active).ToList();
		foreach (MapPillItem pill in _mapPills)
		{
			int activeTaskCount = source.Count((TarkovTask q) => IsQuestOnMap(q, pill.MapKey, pill.DisplayName));
			pill.ActiveTaskCount = activeTaskCount;
		}
	}

	private bool IsQuestOnMap(TarkovTask quest, string mapKey, string displayName)
	{
		if (quest.Maps == null || quest.Maps.Count == 0)
		{
			return false;
		}
		string b = _trackerService?.ResolveMapKey(mapKey) ?? mapKey;
		foreach (string map in quest.Maps)
		{
			if (string.Equals(map, mapKey, StringComparison.OrdinalIgnoreCase) || string.Equals(map, displayName, StringComparison.OrdinalIgnoreCase) || string.Equals(map, b, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			string text = _trackerService?.ResolveMapKey(map);
			if (!string.IsNullOrEmpty(text) && (string.Equals(text, b, StringComparison.OrdinalIgnoreCase) || string.Equals(text, mapKey, StringComparison.OrdinalIgnoreCase)))
			{
				return true;
			}
		}
		return false;
	}

	private void UpdateRequiredStripsForCurrentMap()
	{
		if (string.IsNullOrEmpty(_currentMapKey))
		{
			return;
		}
		IReadOnlyList<TarkovTask> allQuests = QuestDbService.Instance.AllQuests;
		if (allQuests == null || allQuests.Count == 0)
		{
			return;
		}
		List<TarkovTask> source = allQuests.Where((TarkovTask q) => _progressService.GetStatus(q) == QuestStatus.Active).ToList();
		string displayName = _mapPills.FirstOrDefault((MapPillItem p) => string.Equals(p.MapKey, _currentMapKey, StringComparison.OrdinalIgnoreCase) || string.Equals(_trackerService?.ResolveMapKey(p.MapKey), _currentMapKey, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? _currentMapKey;
		HashSet<string> hiddenQuests = MapSettings.Instance.HiddenQuests;
		List<TarkovTask> list = source.Where((TarkovTask q) => IsQuestOnMap(q, _currentMapKey, displayName) && !IsQuestHidden(q)).ToList();
		Dictionary<string, (string, bool, int, List<string>)> dictionary = new Dictionary<string, (string, bool, int, List<string>)>(StringComparer.OrdinalIgnoreCase);
		foreach (TarkovTask item3 in list)
		{
			if (item3.RequiredItems == null)
			{
				continue;
			}
			foreach (QuestItem requiredItem in item3.RequiredItems)
			{
				string text = ((!string.IsNullOrEmpty(requiredItem.ItemNormalizedName)) ? requiredItem.ItemNormalizedName : (requiredItem.ItemDisplayName ?? ""));
				if (string.IsNullOrWhiteSpace(text))
				{
					continue;
				}
				string norm = (requiredItem.ItemNormalizedName ?? requiredItem.ItemDisplayName ?? "").Trim();
				bool flag = NeededItemsViewModel.SpecialEquipmentNames.Contains(norm) || NeededItemsViewModel.SpecialEquipmentNames.Any((string s) => norm.Contains(s, StringComparison.OrdinalIgnoreCase));
				if (!flag)
				{
					Dictionary<string, TarkovItem> itemLookup = ItemDbService.Instance.GetItemLookup();
					if (itemLookup != null && itemLookup.TryGetValue(norm, out var value) && string.Equals(value.Category, "Special equipment", StringComparison.OrdinalIgnoreCase))
					{
						flag = true;
					}
				}
				if (!flag)
				{
					continue;
				}
				string item = requiredItem.ItemDisplayName ?? requiredItem.ItemNormalizedName ?? string.Empty;
				if (dictionary.TryGetValue(text, out var value2))
				{
					List<string> item2 = value2.Item4;
					if (!item2.Contains(item3.Name))
					{
						item2.Add(item3.Name);
					}
					dictionary[text] = (value2.Item1, value2.Item2 || requiredItem.FoundInRaid, value2.Item3 + requiredItem.Amount, item2);
				}
				else
				{
					dictionary[text] = (item, requiredItem.FoundInRaid, requiredItem.Amount, new List<string> { item3.Name });
				}
			}
		}
		_requiredItems.Clear();
		foreach (KeyValuePair<string, (string, bool, int, List<string>)> item4 in dictionary)
		{
			string key = item4.Key;
			(string, bool, int, List<string>) value3 = item4.Value;
			int num = 0;
			foreach (string qName in value3.Item4)
			{
				TarkovTask tarkovTask = list.FirstOrDefault((TarkovTask x) => x.Name == qName);
				if (tarkovTask != null)
				{
					string questItemKey = NeededItemProgressService.GetQuestItemKey(tarkovTask.NormalizedName ?? tarkovTask.Id, key, null);
					num += NeededItemProgressService.Instance.GetCount(questItemKey);
				}
			}
			_requiredItems.Add(new MapActiveItemStripViewModel
			{
				ItemNormalizedName = key,
				ItemName = value3.Item1,
				FoundInRaid = value3.Item2,
				CurrentCount = Math.Min(num, value3.Item3),
				NeededCount = value3.Item3,
				QuestName = string.Join(", ", value3.Item4),
				NeedKey = key
			});
		}
		TxtNoRequiredItems.Visibility = ((_requiredItems.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		RequiredItemsList.Visibility = ((_requiredItems.Count <= 0) ? Visibility.Collapsed : Visibility.Visible);
		List<MapActiveKeyStripViewModel> list2 = new List<MapActiveKeyStripViewModel>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (TarkovTask item5 in list)
		{
			foreach (NeededKeyInfo item6 in NeededKeysService.Instance.GetNeededKeysForQuest(item5))
			{
				if (hashSet.Add(item6.Name))
				{
					list2.Add(new MapActiveKeyStripViewModel
					{
						KeyName = item6.Name,
						ShortName = ((!string.IsNullOrEmpty(item6.ShortName)) ? item6.ShortName : item6.Name),
						InStash = item6.InStash,
						QuestName = item6.QuestName,
						TargetRoom = (item6.ObjectiveDescription ?? string.Empty)
					});
				}
			}
		}
		_requiredKeys.Clear();
		foreach (MapActiveKeyStripViewModel item7 in list2)
		{
			_requiredKeys.Add(item7);
		}
		TxtNoRequiredKeys.Visibility = ((_requiredKeys.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		RequiredKeysList.Visibility = ((_requiredKeys.Count <= 0) ? Visibility.Collapsed : Visibility.Visible);
		bool IsQuestHidden(TarkovTask quest)
		{
			if (hiddenQuests == null || hiddenQuests.Count == 0)
			{
				return false;
			}
			if (!string.IsNullOrEmpty(quest.Id) && hiddenQuests.Contains(quest.Id))
			{
				return true;
			}
			if (quest.Ids != null && quest.Ids.Any((string id) => hiddenQuests.Contains(id)))
			{
				return true;
			}
			if (!string.IsNullOrEmpty(quest.NormalizedName) && hiddenQuests.Contains(quest.NormalizedName))
			{
				return true;
			}
			if (!string.IsNullOrEmpty(quest.Name) && hiddenQuests.Contains(quest.Name))
			{
				return true;
			}
			return false;
		}
	}

	private void BtnStripItemMinus_Click(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.Tag is MapActiveItemStripViewModel { CurrentCount: >0 } mapActiveItemStripViewModel)
		{
			mapActiveItemStripViewModel.CurrentCount--;
			UpdateNeededItemProgress(mapActiveItemStripViewModel);
		}
	}

	private void BtnStripItemPlus_Click(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.Tag is MapActiveItemStripViewModel mapActiveItemStripViewModel && mapActiveItemStripViewModel.CurrentCount < mapActiveItemStripViewModel.NeededCount)
		{
			mapActiveItemStripViewModel.CurrentCount++;
			UpdateNeededItemProgress(mapActiveItemStripViewModel);
		}
	}

	private void BtnStripItemDone_Click(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.Tag is MapActiveItemStripViewModel mapActiveItemStripViewModel)
		{
			mapActiveItemStripViewModel.CurrentCount = mapActiveItemStripViewModel.NeededCount;
			UpdateNeededItemProgress(mapActiveItemStripViewModel);
		}
	}

	private void UpdateNeededItemProgress(MapActiveItemStripViewModel item)
	{
		List<TarkovTask> list = QuestDbService.Instance.AllQuests.Where((TarkovTask q) => _progressService.GetStatus(q) == QuestStatus.Active && q.RequiredItems != null && q.RequiredItems.Any((QuestItem r) => string.Equals(r.ItemNormalizedName, item.ItemNormalizedName, StringComparison.OrdinalIgnoreCase) || string.Equals(r.ItemDisplayName, item.ItemName, StringComparison.OrdinalIgnoreCase))).ToList();
		int num = item.CurrentCount;
		foreach (TarkovTask item2 in list)
		{
			QuestItem questItem = item2.RequiredItems.First((QuestItem r) => string.Equals(r.ItemNormalizedName, item.ItemNormalizedName, StringComparison.OrdinalIgnoreCase) || string.Equals(r.ItemDisplayName, item.ItemName, StringComparison.OrdinalIgnoreCase));
			string questItemKey = NeededItemProgressService.GetQuestItemKey(item2.NormalizedName ?? item2.Id, questItem.ItemNormalizedName, null);
			int num2 = Math.Min(num, questItem.Amount);
			NeededItemProgressService.Instance.SetCount(questItemKey, num2, questItem.Amount);
			num -= num2;
		}
	}

	private void BtnLocateObjective_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		QuestObjectiveViewModel objectiveVmFromSender = GetObjectiveVmFromSender(sender);
		if (objectiveVmFromSender != null && objectiveVmFromSender.HasLocation)
		{
			PinAndLocateObjective(objectiveVmFromSender.Objective);
		}
	}

	public void PinAndLocateObjective(TaskObjectiveWithLocation obj)
	{
		_selectedObjective = _currentMapObjectives.FirstOrDefault((TaskObjectiveWithLocation o) => o.ObjectiveId == obj.ObjectiveId) ?? obj;
		SelectFloorForObjective(_selectedObjective);
		_questMarkerManager?.SetSelectedObjective(_selectedObjective);
		_trackerService?.PinObjective(_selectedObjective);
		RefreshQuestDrawer();
		CenterOnObjective(_selectedObjective);
		if (_zoomLevel < 1.0)
		{
			SetZoom(1.25);
		}
	}

	public void OpenObjectiveRelocationManager()
	{
		try
		{
			Window window = Window.GetWindow(this);
			QuestObjectiveRelocationManagerWindow questObjectiveRelocationManagerWindow = new QuestObjectiveRelocationManagerWindow();
			if (window != null && window.IsLoaded)
			{
				questObjectiveRelocationManagerWindow.Owner = window;
			}
			questObjectiveRelocationManagerWindow.ShowDialog();
		}
		catch (Exception ex)
		{
			_log.Error("[MapPage] Error opening relocation manager: " + ex.Message, ex);
			System.Windows.MessageBox.Show("No se pudo abrir el gestor de reubicaciones: " + ex.Message, "Gestor de Reubicación", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void BtnDrawerRelocateObjective_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		if (sender is System.Windows.Controls.Button { Tag: QuestObjectiveViewModel tag })
		{
			QuestObjectiveRelocationService.Instance.StartPlacement(tag.ObjectiveId, tag.Description, tag.QuestName, _currentMapKey);
		}
	}

	private async void BtnDrawerRevertObjective_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		if (sender is System.Windows.Controls.Button { Tag: QuestObjectiveViewModel tag })
		{
			QuestObjectiveRelocationService instance = QuestObjectiveRelocationService.Instance;
			if (instance.IsPlacementActive && instance.TargetObjectiveId == tag.ObjectiveId)
			{
				instance.CancelPlacement();
				PlacementModeBanner.Visibility = Visibility.Collapsed;
				MapCanvas.Cursor = System.Windows.Input.Cursors.Arrow;
				MapViewerGrid.Cursor = System.Windows.Input.Cursors.Arrow;
			}
			if (System.Windows.MessageBox.Show("Do you want to remove the assigned location for this objective (" + tag.Description + ") and restore its original default?", "Reset Location", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
			{
				await instance.ClearObjectiveLocationAsync(tag.ObjectiveId);
			}
		}
	}

	private void WikiLinkButton_Click(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.Button { Tag: string tag } && !string.IsNullOrEmpty(tag))
		{
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = tag,
					UseShellExecute = true
				});
			}
			catch (Exception)
			{
			}
		}
	}

	private void CenterOnObjective(TaskObjectiveWithLocation objective)
	{
		if (string.IsNullOrEmpty(_currentMapKey))
		{
			return;
		}
		MapConfig mapConfig = _trackerService?.GetMapConfig(_currentMapKey);
		if (mapConfig == null)
		{
			return;
		}
		QuestObjectiveLocation questObjectiveLocation = objective.Locations.FirstOrDefault(delegate(QuestObjectiveLocation l)
		{
			if (!l.MapName.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase))
			{
				string? mapNormalizedName = l.MapNormalizedName;
				if (mapNormalizedName == null || !mapNormalizedName.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
			}
			return string.IsNullOrEmpty(_currentFloorId) || string.IsNullOrEmpty(l.FloorId) || string.Equals(_currentFloorId, l.FloorId, StringComparison.OrdinalIgnoreCase);
		});
		if (questObjectiveLocation == null)
		{
			questObjectiveLocation = objective.Locations.FirstOrDefault((QuestObjectiveLocation l) => l.MapName.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase) || (l.MapNormalizedName?.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase) ?? false));
		}
		if (questObjectiveLocation != null)
		{
			(double screenX, double screenY) tuple = mapConfig.GameToScreenForPlayer(questObjectiveLocation.X, questObjectiveLocation.Y);
			double item = tuple.screenX;
			double item2 = tuple.screenY;
			double actualWidth = MapViewerGrid.ActualWidth;
			double actualHeight = MapViewerGrid.ActualHeight;
			MapTranslate.X = actualWidth / 2.0 - item * _zoomLevel;
			MapTranslate.Y = actualHeight / 2.0 - item2 * _zoomLevel;
		}
	}

	private async Task LoadExtractsAsync(CancellationToken ct = default(CancellationToken))
	{
		_ = 1;
		try
		{
			ct.ThrowIfCancellationRequested();
			TxtStatus.Text = "Loading extract data...";
			await _extractService.EnsureLoadedAsync(delegate(string msg)
			{
				base.Dispatcher.Invoke(() => TxtStatus.Text = msg);
			}).WaitAsync(ct);
			int count = _extractService.AllExtracts.Count;
			TxtStatus.Text = $"Loaded {count} extracts";
			if (!string.IsNullOrEmpty(_currentMapKey))
			{
				await RefreshExtractMarkers(ct);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TxtStatus.Text = "Error loading extracts: " + ex2.Message;
		}
	}

	private async Task RefreshExtractMarkers(CancellationToken ct = default(CancellationToken))
	{
		if (_extractMarkerManager != null)
		{
			_extractMarkerManager.SetCurrentMap(_currentMapKey);
			_extractMarkerManager.SetCurrentFloor(_currentFloorId);
			_extractMarkerManager.SetZoomLevel(_zoomLevel);
			_extractMarkerManager.SetShowExtractMarkers(_showExtractMarkers);
			_extractMarkerManager.SetShowPmcExtracts(_showPmcExtracts);
			_extractMarkerManager.SetShowScavExtracts(_showScavExtracts);
			_extractMarkerManager.SetShowTransitExtracts(_showTransitExtracts);
			_extractMarkerManager.SetExtractNameTextSize(_extractNameTextSize);
			await _extractMarkerManager.RefreshMarkersAsync(ct);
		}
	}

	private async Task LoadMapMarkersAsync(CancellationToken ct = default(CancellationToken))
	{
		_ = 1;
		try
		{
			ct.ThrowIfCancellationRequested();
			TxtStatus.Text = "Loading map markers...";
			await MapMarkerDbService.Instance.LoadMarkersAsync().WaitAsync(ct);
			int markerCount = MapMarkerDbService.Instance.MarkerCount;
			TxtStatus.Text = $"Loaded {markerCount} map markers";
			if (!string.IsNullOrEmpty(_currentMapKey))
			{
				await RefreshMapMarkers(ct);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TxtStatus.Text = "Error loading map markers: " + ex2.Message;
		}
	}

	private bool IsMarkerOnCurrentFloor(string? markerFloorId)
	{
		if (string.IsNullOrEmpty(_currentFloorId))
		{
			return true;
		}
		if (string.IsNullOrEmpty(markerFloorId))
		{
			return string.Equals(_currentFloorId, "main", StringComparison.OrdinalIgnoreCase);
		}
		return string.Equals(_currentFloorId, markerFloorId, StringComparison.OrdinalIgnoreCase);
	}

	private (string arrow, string floorText, System.Windows.Media.Color color)? GetFloorIndicator(string? markerFloorId)
	{
		if (string.IsNullOrEmpty(_currentMapKey) || string.IsNullOrEmpty(_currentFloorId))
		{
			return null;
		}
		MapConfig mapConfig = _trackerService?.GetMapConfig(_currentMapKey);
		if (mapConfig?.Floors == null || mapConfig.Floors.Count == 0)
		{
			return null;
		}
		int num = mapConfig.Floors.FirstOrDefault((MapFloorConfig f) => string.Equals(f.LayerId, _currentFloorId, StringComparison.OrdinalIgnoreCase))?.Order ?? 0;
		string effectiveFloorId = (string.IsNullOrEmpty(markerFloorId) ? "main" : markerFloorId);
		int num2 = mapConfig.Floors.FirstOrDefault((MapFloorConfig f) => string.Equals(f.LayerId, effectiveFloorId, StringComparison.OrdinalIgnoreCase))?.Order ?? 0;
		if (num == num2)
		{
			return null;
		}
		bool num3 = num2 > num;
		string item = (num3 ? "↑" : "↓");
		System.Windows.Media.Color item2 = (num3 ? System.Windows.Media.Color.FromRgb(100, 181, 246) : System.Windows.Media.Color.FromRgb(byte.MaxValue, 167, 38));
		string item3 = ((num2 < 0) ? "B" : ((num2 != 0) ? (num2 + 1).ToString() : "G"));
		return (item, item3, item2);
	}

	private List<List<MapExtract>> GroupExtractsByPosition(List<MapExtract> extracts)
	{
		List<List<MapExtract>> list = new List<List<MapExtract>>();
		HashSet<string> hashSet = new HashSet<string>();
		foreach (MapExtract extract in extracts)
		{
			if (hashSet.Contains(extract.Id))
			{
				continue;
			}
			List<MapExtract> list2 = new List<MapExtract> { extract };
			hashSet.Add(extract.Id);
			foreach (MapExtract extract2 in extracts)
			{
				if (!hashSet.Contains(extract2.Id))
				{
					double num = Math.Sqrt(Math.Pow(extract.X - extract2.X, 2.0) + Math.Pow(extract.Z - extract2.Z, 2.0));
					bool flag = string.Equals(extract.Name, extract2.Name, StringComparison.OrdinalIgnoreCase);
					bool flag2 = extract.Faction != extract2.Faction;
					if (num < 10.0 && (flag || flag2))
					{
						list2.Add(extract2);
						hashSet.Add(extract2.Id);
					}
				}
			}
			list.Add(list2);
		}
		return list;
	}

	private (MapExtract extract, ExtractFaction faction) DetermineExtractDisplay(List<MapExtract> group)
	{
		if (group.Count == 1)
		{
			ExtractFaction item = ((group[0].Faction != ExtractFaction.Shared) ? group[0].Faction : ExtractFaction.Pmc);
			return (extract: group[0], faction: item);
		}
		bool num = group.Any((MapExtract e) => e.Faction == ExtractFaction.Pmc || e.Faction == ExtractFaction.Shared);
		bool flag = group.Any((MapExtract e) => e.Faction == ExtractFaction.Scav);
		if (num && flag)
		{
			return (extract: group.FirstOrDefault((MapExtract e) => e.Faction == ExtractFaction.Pmc) ?? group.FirstOrDefault((MapExtract e) => e.Faction == ExtractFaction.Shared) ?? group[0], faction: ExtractFaction.Pmc);
		}
		ExtractFaction item2 = ((group[0].Faction != ExtractFaction.Shared) ? group[0].Faction : ExtractFaction.Pmc);
		return (extract: group[0], faction: item2);
	}

	private bool ShouldShowExtract(ExtractFaction faction)
	{
		return faction switch
		{
			ExtractFaction.Pmc => _showPmcExtracts, 
			ExtractFaction.Scav => _showScavExtracts, 
			ExtractFaction.Shared => _showPmcExtracts, 
			ExtractFaction.Transit => _showPmcExtracts, 
			_ => true, 
		};
	}

	private async void SaveCalibrationAndRefresh()
	{
		if (_trackerService == null)
		{
			return;
		}
		MapConfig mapConfig = _trackerService.GetMapConfig(_currentMapKey ?? "");
		if (mapConfig?.CalibrationPoints != null && mapConfig.CalibrationPoints.Count >= 3)
		{
			mapConfig.CalibratedTransform = _calibrationService.CalculateAffineTransform(mapConfig.CalibrationPoints);
			if (mapConfig.CalibratedTransform != null)
			{
				TxtStatus.Text = $"Calibration saved! ({mapConfig.CalibrationPoints.Count} points)";
				_trackerService.SaveSettings();
				await RefreshExtractMarkers();
				await RefreshQuestMarkers();
			}
			else
			{
				TxtStatus.Text = "Calibration calculation failed.";
			}
		}
		else
		{
			_trackerService.SaveSettings();
		}
	}

	private void SetupExtractMarkerForCalibration(FrameworkElement marker, MapExtract extract)
	{
		marker.Tag = extract;
		marker.Cursor = System.Windows.Input.Cursors.SizeAll;
		marker.MouseLeftButtonDown += ExtractMarker_CalibrationMouseDown;
		marker.MouseMove += ExtractMarker_CalibrationMouseMove;
		marker.MouseLeftButtonUp += ExtractMarker_CalibrationMouseUp;
	}

	private void ExtractMarker_CalibrationMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (_isCalibrationMode && sender is FrameworkElement frameworkElement)
		{
			_draggingExtractMarker = frameworkElement;
			_draggingExtract = frameworkElement.Tag as MapExtract;
			_extractDragStartPoint = e.GetPosition(ExtractMarkersContainer);
			_extractMarkerOriginalLeft = Canvas.GetLeft(frameworkElement);
			_extractMarkerOriginalTop = Canvas.GetTop(frameworkElement);
			frameworkElement.CaptureMouse();
			e.Handled = true;
		}
	}

	private void ExtractMarker_CalibrationMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (_isCalibrationMode && _draggingExtractMarker != null)
		{
			System.Windows.Point position = e.GetPosition(ExtractMarkersContainer);
			double num = position.X - _extractDragStartPoint.X;
			double num2 = position.Y - _extractDragStartPoint.Y;
			Canvas.SetLeft(_draggingExtractMarker, _extractMarkerOriginalLeft + num);
			Canvas.SetTop(_draggingExtractMarker, _extractMarkerOriginalTop + num2);
		}
	}

	private void ExtractMarker_CalibrationMouseUp(object sender, MouseButtonEventArgs e)
	{
		if (!_isCalibrationMode || _draggingExtractMarker == null || _draggingExtract == null)
		{
			return;
		}
		_draggingExtractMarker.ReleaseMouseCapture();
		double left = Canvas.GetLeft(_draggingExtractMarker);
		double top = Canvas.GetTop(_draggingExtractMarker);
		double num = ((_draggingExtractMarker.ActualWidth > 0.0) ? _draggingExtractMarker.ActualWidth : 20.0);
		double num2 = ((_draggingExtractMarker.ActualHeight > 0.0) ? _draggingExtractMarker.ActualHeight : 20.0);
		double screenX = left + num / 2.0;
		double screenY = top + num2 / 2.0;
		MapConfig mapConfig = _trackerService?.GetMapConfig(_currentMapKey ?? "");
		if (mapConfig != null)
		{
			CalibrationPoint point = new CalibrationPoint
			{
				Id = _draggingExtract.Id,
				Name = _draggingExtract.Name,
				GameX = _draggingExtract.X,
				GameZ = _draggingExtract.Z,
				ScreenX = screenX,
				ScreenY = screenY
			};
			_calibrationService.AddCalibrationPoint(mapConfig, point);
			int num3 = mapConfig.CalibrationPoints?.Count ?? 0;
			TxtStatus.Text = $"Calibration point set: {_draggingExtract.Name} ({num3} points)";
			if (num3 >= 3)
			{
				TxtStatus.Text += " - Ready to apply!";
			}
			else
			{
				TxtStatus.Text += $" - Need {3 - num3} more points";
			}
		}
		_draggingExtractMarker = null;
		_draggingExtract = null;
		e.Handled = true;
	}

	private async void ChkShowExtractMarkers_Changed(object sender, RoutedEventArgs e)
	{
		_showExtractMarkers = ChkShowExtractMarkers?.IsChecked ?? true;
		if (ExtractMarkersContainer != null)
		{
			ExtractMarkersContainer.Visibility = ((!_showExtractMarkers) ? Visibility.Collapsed : Visibility.Visible);
		}
		SettingsService.Instance.MapShowExtracts = _showExtractMarkers;
		OverlayMiniMapService.Instance.RefreshMap();
	}

	private async void ChkExtractFilter_Changed(object sender, RoutedEventArgs e)
	{
		if (!base.IsLoaded || _isSwitchingMap || _isApplyingMapConfig)
		{
			return;
		}
		try
		{
			CancellationToken newCancellationToken = GetNewCancellationToken();
			_showPmcExtracts = ChkShowPmcExtracts?.IsChecked ?? true;
			_showScavExtracts = ChkShowScavExtracts?.IsChecked ?? true;
			_showTransitExtracts = ChkShowTransitExtracts?.IsChecked ?? true;
			SettingsService instance = SettingsService.Instance;
			instance.MapShowPmcExtracts = _showPmcExtracts;
			instance.MapShowScavExtracts = _showScavExtracts;
			instance.MapShowTransits = _showTransitExtracts;
			await RefreshExtractMarkers(newCancellationToken);
			OverlayMiniMapService.Instance.RefreshMap();
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async void SliderExtractTextSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (TxtExtractTextSize == null)
		{
			return;
		}
		_extractNameTextSize = e.NewValue;
		TxtExtractTextSize.Text = e.NewValue.ToString("F0");
		SettingsService.Instance.MapExtractNameSize = _extractNameTextSize;
		if (_extractService.IsLoaded)
		{
			try
			{
				CancellationToken newCancellationToken = GetNewCancellationToken();
				await RefreshExtractMarkers(newCancellationToken);
			}
			catch (OperationCanceledException)
			{
			}
		}
	}

	private async void MarkerColor_Click(object sender, MouseButtonEventArgs e)
	{
		if (!(sender is Border { Tag: string tag } border))
		{
			return;
		}
		ColorDialog colorDialog = new ColorDialog();
		if (border.Background is SolidColorBrush { Color: var color })
		{
			colorDialog.Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
		}
		if (colorDialog.ShowDialog() != DialogResult.OK)
		{
			return;
		}
		System.Drawing.Color color2 = colorDialog.Color;
		string hexColor = $"#{color2.R:X2}{color2.G:X2}{color2.B:X2}";
		border.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(color2.R, color2.G, color2.B));
		if (_trackerService != null)
		{
			_trackerService.Settings.SetMarkerColor(tag, hexColor);
			_trackerService.SaveSettings();
			try
			{
				CancellationToken newCancellationToken = GetNewCancellationToken();
				await RefreshQuestMarkers(newCancellationToken);
			}
			catch (OperationCanceledException)
			{
			}
		}
	}

	private async void BtnResetColors_Click(object sender, RoutedEventArgs e)
	{
		Dictionary<string, string> markerColors = new Dictionary<string, string>
		{
			{ "visit", "#00E5FF" },
			{ "mark", "#FFA000" },
			{ "plantItem", "#AB47BC" },
			{ "extract", "#29B6F6" },
			{ "findItem", "#FFEB3B" }
		};
		if (_trackerService != null)
		{
			_trackerService.Settings.MarkerColors = markerColors;
			_trackerService.SaveSettings();
		}
		UpdateMarkerColorUI();
		try
		{
			CancellationToken newCancellationToken = GetNewCancellationToken();
			await RefreshQuestMarkers(newCancellationToken);
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void UpdateMarkerColorUI()
	{
		if (_trackerService != null)
		{
			ColorVisit.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_trackerService.Settings.GetMarkerColor("visit")));
			ColorMark.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_trackerService.Settings.GetMarkerColor("mark")));
			ColorPlant.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_trackerService.Settings.GetMarkerColor("plantItem")));
			ColorExtract.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_trackerService.Settings.GetMarkerColor("extract")));
			ColorFind.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_trackerService.Settings.GetMarkerColor("findItem")));
		}
	}

	private async void ObjectiveCheckBox_Checked(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.CheckBox { Tag: TaskObjectiveWithLocation tag })
		{
			ObjectiveProgressService.Instance.SetObjectiveCompletedById(tag.ObjectiveId, completed: true, tag.TaskNormalizedName, tag.ObjectiveIndex);
			try
			{
				CancellationToken newCancellationToken = GetNewCancellationToken();
				await RefreshQuestMarkers(newCancellationToken);
			}
			catch (OperationCanceledException)
			{
			}
			RefreshQuestDrawer();
		}
	}

	private async void ObjectiveCheckBox_Unchecked(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.CheckBox { Tag: TaskObjectiveWithLocation tag })
		{
			ObjectiveProgressService.Instance.SetObjectiveCompletedById(tag.ObjectiveId, completed: false, tag.TaskNormalizedName, tag.ObjectiveIndex);
			try
			{
				CancellationToken newCancellationToken = GetNewCancellationToken();
				await RefreshQuestMarkers(newCancellationToken);
			}
			catch (OperationCanceledException)
			{
			}
			RefreshQuestDrawer();
		}
	}

	private async void ChkHideCompletedObjectives_Changed(object sender, RoutedEventArgs e)
	{
		_hideCompletedObjectives = ChkHideCompletedObjectives?.IsChecked ?? true;
		SettingsService.Instance.MapHideCompletedObjectives = _hideCompletedObjectives;
		try
		{
			CancellationToken newCancellationToken = GetNewCancellationToken();
			await RefreshQuestMarkers(newCancellationToken);
		}
		catch (OperationCanceledException)
		{
		}
		OverlayMiniMapService.Instance.RefreshMap();
	}

	private void RefreshQuestDrawer()
	{
	}

	private void BtnToggleMapMarkersPanel_Click(object sender, RoutedEventArgs e)
	{
		_isMapMarkersPanelCollapsed = !_isMapMarkersPanelCollapsed;
		if (_isMapMarkersPanelCollapsed)
		{
			MapMarkersContent.Visibility = Visibility.Collapsed;
			BtnToggleMapMarkersPanel.Content = "▲";
		}
		else
		{
			MapMarkersContent.Visibility = Visibility.Visible;
			BtnToggleMapMarkersPanel.Content = "▼";
		}
	}

	private async void ChkMapMarker_Changed(object sender, RoutedEventArgs e)
	{
		if (!base.IsLoaded || _isSwitchingMap || _isApplyingMapConfig)
		{
			return;
		}
		try
		{
			CancellationToken newCancellationToken = GetNewCancellationToken();
			MapSettings instance = MapSettings.Instance;
			_showPmcSpawnsMarker = ChkShowPmcSpawns?.IsChecked ?? true;
			_showSniperScavsMarker = ChkShowSniperScavs?.IsChecked ?? true;
			_showRoguesMarker = ChkShowRogues?.IsChecked ?? true;
			_showCultistsMarker = ChkShowCultists?.IsChecked ?? true;
			_showLeversMarkerOverlay = ChkShowLeversMarker?.IsChecked ?? true;
			_showBossesMarker = ChkShowBosses?.IsChecked ?? true;
			instance.ShowPmcSpawns = _showPmcSpawnsMarker;
			instance.ShowSniperScavs = _showSniperScavsMarker;
			instance.ShowRogues = _showRoguesMarker;
			instance.ShowCultists = _showCultistsMarker;
			instance.ShowLevers = _showLeversMarkerOverlay;
			instance.ShowBosses = _showBossesMarker;
			await RefreshMapMarkers(newCancellationToken);
			OverlayMiniMapService.Instance.RefreshMarkers();
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task RefreshMapMarkers(CancellationToken ct = default(CancellationToken))
	{
		if (_mapMarkersManager != null)
		{
			_mapMarkersManager.SetShowPmcSpawns(_showPmcSpawnsMarker);
			_mapMarkersManager.SetShowSniperScavs(_showSniperScavsMarker);
			_mapMarkersManager.SetShowRogues(_showRoguesMarker);
			_mapMarkersManager.SetShowCultists(_showCultistsMarker);
			_mapMarkersManager.SetShowLevers(_showLeversMarkerOverlay);
			_mapMarkersManager.SetShowBosses(_showBossesMarker);
			_mapMarkersManager.SetCurrentMap(_currentMapKey);
			_mapMarkersManager.SetCurrentFloor(_currentFloorId);
			_mapMarkersManager.SetZoomLevel(_zoomLevel);
			await _mapMarkersManager.RefreshMarkersAsync(ct);
		}
	}

	private void MapViewer_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (QuestObjectiveRelocationService.Instance.IsPlacementActive)
		{
			BtnCancelPlacement_Click(BtnCancelPlacement, new RoutedEventArgs());
			e.Handled = true;
		}
		else
		{
			_lastRightClickPosition = e.GetPosition(MapCanvas);
		}
	}

	private async void MenuAddCustomMarker_Click(object sender, RoutedEventArgs e)
	{
		if (!string.IsNullOrEmpty(_currentMapKey) && _customMarkerManager != null)
		{
			TxtStatus.Text = "Adding custom marker...";
			await _customMarkerManager.AddMarkerAsync(_lastRightClickPosition.X, _lastRightClickPosition.Y);
		}
	}

	private void LstCustomMarkers_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!(LstCustomMarkers.SelectedItem is CustomMapMarker customMapMarker))
		{
			return;
		}
		if (!string.IsNullOrEmpty(customMapMarker.FloorId) && customMapMarker.FloorId != _currentFloorId)
		{
			foreach (ComboBoxItem item in (IEnumerable)CmbFloorSelect.Items)
			{
				if (item.Tag?.ToString() == customMapMarker.FloorId)
				{
					CmbFloorSelect.SelectedItem = item;
					break;
				}
			}
		}
		PanToMarker(customMapMarker);
	}

	private void PanToMarker(CustomMapMarker marker)
	{
		if (string.IsNullOrEmpty(_currentMapKey))
		{
			return;
		}
		MapTrackerService? trackerService = _trackerService;
		if (trackerService != null && trackerService.Transformer.TryTransform(_currentMapKey, marker.X, marker.Z, null, out ScreenPosition screenPosition) && screenPosition != null)
		{
			double actualWidth = MapViewerGrid.ActualWidth;
			double actualHeight = MapViewerGrid.ActualHeight;
			if (!(actualWidth <= 0.0) && !(actualHeight <= 0.0))
			{
				double x = actualWidth / 2.0 - screenPosition.X * _zoomLevel;
				double y = actualHeight / 2.0 - screenPosition.Y * _zoomLevel;
				MapTranslate.X = x;
				MapTranslate.Y = y;
			}
		}
	}

	private async void BtnDeleteMarker_Manual_Click(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.Button { Tag: CustomMapMarker tag })
		{
			await (_customMarkerManager?.DeleteMarkerAsync(tag));
		}
	}

	private async void BtnEditMarker_Manual_Click(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.Button { Tag: CustomMapMarker tag })
		{
			await (_customMarkerManager?.EditMarkerAsync(tag));
		}
	}

	private void BtnToggleCustomMarkersPanel_Click(object sender, RoutedEventArgs e)
	{
		_isCustomMarkersPanelCollapsed = !_isCustomMarkersPanelCollapsed;
		UpdateCustomMarkersPanelUI(_isCustomMarkersPanelCollapsed);
		SettingsService.Instance.IsCustomMarkersPanelCollapsed = _isCustomMarkersPanelCollapsed;
	}

	private void UpdateCustomMarkersPanelUI(bool isCollapsed)
	{
		if (TxtToggleIcon != null)
		{
			TxtToggleIcon.Text = (isCollapsed ? "◀" : "▶");
		}
		if (MarkerListContentColumn != null)
		{
			MarkerListContentColumn.Width = (isCollapsed ? new GridLength(0.0) : new GridLength(280.0));
		}
		if (MarkerListContentGrid != null)
		{
			MarkerListContentGrid.Visibility = (isCollapsed ? Visibility.Collapsed : Visibility.Visible);
		}
	}

	private void BtnTogglePmcExtracts_Click(object sender, RoutedEventArgs e)
	{
		if (ChkShowPmcExtracts != null)
		{
			ChkShowPmcExtracts.IsChecked = BtnTogglePmcExtracts.IsChecked;
		}
		_showPmcExtracts = BtnTogglePmcExtracts.IsChecked ?? true;
		_showTransitExtracts = _showPmcExtracts;
		SaveCurrentPerMapMarkerConfig();
		RefreshExtractMarkers();
	}

	private void BtnToggleScavExtracts_Click(object sender, RoutedEventArgs e)
	{
		if (ChkShowScavExtracts != null)
		{
			ChkShowScavExtracts.IsChecked = BtnToggleScavExtracts.IsChecked;
		}
		_showScavExtracts = BtnToggleScavExtracts.IsChecked ?? true;
		SaveCurrentPerMapMarkerConfig();
		RefreshExtractMarkers();
	}

	private void BtnToggleSpawns_Click(object sender, RoutedEventArgs e)
	{
		if (ChkShowPmcSpawns != null)
		{
			ChkShowPmcSpawns.IsChecked = BtnToggleSpawns.IsChecked;
		}
		_showPmcSpawnsMarker = BtnToggleSpawns.IsChecked ?? true;
		SaveCurrentPerMapMarkerConfig();
		RefreshMapMarkers();
	}

	private void BtnMapSettingsCog_Click(object sender, RoutedEventArgs e)
	{
		if (MapSettingsPopup != null)
		{
			MapSettingsPopup.IsOpen = !MapSettingsPopup.IsOpen;
		}
	}

	private void MapSettingItem_Changed(object sender, RoutedEventArgs e)
	{
		if (!_isSwitchingMap && !_isApplyingMapConfig)
		{
			if (sender == ChkSettingSniperScavs && ChkShowSniperScavs != null)
			{
				ChkShowSniperScavs.IsChecked = ChkSettingSniperScavs.IsChecked;
			}
			if (sender == ChkSettingRogues && ChkShowRogues != null)
			{
				ChkShowRogues.IsChecked = ChkSettingRogues.IsChecked;
			}
			if (sender == ChkSettingCultists && ChkShowCultists != null)
			{
				ChkShowCultists.IsChecked = ChkSettingCultists.IsChecked;
			}
			if (sender == ChkSettingLevers && ChkShowLeversMarker != null)
			{
				ChkShowLeversMarker.IsChecked = ChkSettingLevers.IsChecked;
			}
			if (sender == ChkSettingBosses && ChkShowBosses != null)
			{
				ChkShowBosses.IsChecked = ChkSettingBosses.IsChecked;
			}
			_showSniperScavsMarker = ChkSettingSniperScavs?.IsChecked ?? true;
			_showRoguesMarker = ChkSettingRogues?.IsChecked ?? true;
			_showCultistsMarker = ChkSettingCultists?.IsChecked ?? true;
			_showLeversMarkerOverlay = ChkSettingLevers?.IsChecked ?? true;
			_showBossesMarker = ChkSettingBosses?.IsChecked ?? true;
			SaveCurrentPerMapMarkerConfig();
			RefreshMapMarkers();
		}
	}

	private void BtnToggleFullScreen_Click(object sender, RoutedEventArgs e)
	{
		if (BtnExitFullScreen != null && BtnExitFullScreen.Visibility == Visibility.Visible)
		{
			ExitFullScreen();
		}
		else
		{
			EnterFullScreen();
		}
	}

	public void SaveCurrentPerMapMarkerConfig()
	{
		if (!string.IsNullOrEmpty(_currentMapKey) && !_isSwitchingMap && !_isApplyingMapConfig)
		{
			PerMapConfig perMapConfig = MapSettings.Instance.GetPerMapConfig(_currentMapKey);
			perMapConfig.ShowPmcExtracts = _showPmcExtracts;
			perMapConfig.ShowScavExtracts = _showScavExtracts;
			perMapConfig.ShowSpawns = _showPmcSpawnsMarker;
			perMapConfig.ShowSnipers = _showSniperScavsMarker;
			perMapConfig.ShowRogues = _showRoguesMarker;
			perMapConfig.ShowCultists = _showCultistsMarker;
			perMapConfig.ShowLevers = _showLeversMarkerOverlay;
			perMapConfig.ShowBosses = _showBossesMarker;
			perMapConfig.ZoomLevel = _zoomLevel;
			perMapConfig.TranslateX = MapTranslate?.X ?? 0.0;
			perMapConfig.TranslateY = MapTranslate?.Y ?? 0.0;
			MapSettings.Instance.SavePerMapConfig(_currentMapKey, perMapConfig);
		}
	}

	public void LoadAndApplyPerMapConfig(string mapKey)
	{
		if (string.IsNullOrEmpty(mapKey))
		{
			return;
		}
		_isApplyingMapConfig = true;
		try
		{
			PerMapConfig perMapConfig = MapSettings.Instance.GetPerMapConfig(mapKey);
			_showPmcExtracts = perMapConfig.ShowPmcExtracts;
			_showScavExtracts = perMapConfig.ShowScavExtracts;
			_showPmcSpawnsMarker = perMapConfig.ShowSpawns;
			_showSniperScavsMarker = perMapConfig.ShowSnipers;
			_showRoguesMarker = perMapConfig.ShowRogues;
			_showCultistsMarker = perMapConfig.ShowCultists;
			_showLeversMarkerOverlay = perMapConfig.ShowLevers;
			_showBossesMarker = perMapConfig.ShowBosses;
			if (BtnTogglePmcExtracts != null)
			{
				BtnTogglePmcExtracts.IsChecked = perMapConfig.ShowPmcExtracts;
			}
			if (BtnToggleScavExtracts != null)
			{
				BtnToggleScavExtracts.IsChecked = perMapConfig.ShowScavExtracts;
			}
			if (BtnToggleSpawns != null)
			{
				BtnToggleSpawns.IsChecked = perMapConfig.ShowSpawns;
			}
			if (ChkSettingSniperScavs != null)
			{
				ChkSettingSniperScavs.IsChecked = perMapConfig.ShowSnipers;
			}
			if (ChkSettingRogues != null)
			{
				ChkSettingRogues.IsChecked = perMapConfig.ShowRogues;
			}
			if (ChkSettingCultists != null)
			{
				ChkSettingCultists.IsChecked = perMapConfig.ShowCultists;
			}
			if (ChkSettingLevers != null)
			{
				ChkSettingLevers.IsChecked = perMapConfig.ShowLevers;
			}
			if (ChkSettingBosses != null)
			{
				ChkSettingBosses.IsChecked = perMapConfig.ShowBosses;
			}
			if (ChkShowPmcExtracts != null)
			{
				ChkShowPmcExtracts.IsChecked = perMapConfig.ShowPmcExtracts;
			}
			if (ChkShowScavExtracts != null)
			{
				ChkShowScavExtracts.IsChecked = perMapConfig.ShowScavExtracts;
			}
			if (ChkShowPmcSpawns != null)
			{
				ChkShowPmcSpawns.IsChecked = perMapConfig.ShowSpawns;
			}
			if (ChkShowSniperScavs != null)
			{
				ChkShowSniperScavs.IsChecked = perMapConfig.ShowSnipers;
			}
			if (ChkShowRogues != null)
			{
				ChkShowRogues.IsChecked = perMapConfig.ShowRogues;
			}
			if (ChkShowCultists != null)
			{
				ChkShowCultists.IsChecked = perMapConfig.ShowCultists;
			}
			if (ChkShowLeversMarker != null)
			{
				ChkShowLeversMarker.IsChecked = perMapConfig.ShowLevers;
			}
			if (ChkShowBosses != null)
			{
				ChkShowBosses.IsChecked = perMapConfig.ShowBosses;
			}
			if (perMapConfig.ZoomLevel >= 0.1)
			{
				SetZoom(perMapConfig.ZoomLevel);
				if (MapTranslate != null)
				{
					MapTranslate.X = perMapConfig.TranslateX;
					MapTranslate.Y = perMapConfig.TranslateY;
				}
			}
			else
			{
				FitAndCenterMapInView(forceFit: true);
			}
		}
		finally
		{
			_isApplyingMapConfig = false;
		}
	}

}


#region Value Converters

public class BoolToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => (value is bool b && b) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}

public class IntToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => (value is int i && i > 0) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}

public class NullToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}

#endregion
