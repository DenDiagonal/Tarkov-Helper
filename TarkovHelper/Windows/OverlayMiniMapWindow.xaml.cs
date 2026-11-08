using System;



using System.CodeDom.Compiler;



using System.Collections.Generic;



using System.ComponentModel;



using System.Diagnostics;



using System.IO;



using System.Linq;



using System.Runtime.InteropServices;



using System.Threading;



using System.Threading.Tasks;



using System.Windows;



using System.Windows.Controls;



using System.Windows.Controls.Primitives;



using System.Windows.Input;



using System.Windows.Interop;



using System.Windows.Markup;



using System.Windows.Media;



using System.Windows.Shapes;



using System.Windows.Threading;



using SharpVectors.Converters;



using TarkovHelper.Models;



using TarkovHelper.Models.Map;



using TarkovHelper.Pages.Map.Components;



using TarkovHelper.Services;



using TarkovHelper.Services.Logging;



using TarkovHelper.Services.Map;



using TarkovHelper.Windows.Dialogs;







namespace TarkovHelper.Windows;







public partial class OverlayMiniMapWindow : Window



{



	private const int GWL_EXSTYLE = -20;







	private const int WS_EX_TRANSPARENT = 32;







	private const int WS_EX_LAYERED = 524288;







	private static readonly ILogger _log = Log.For<OverlayMiniMapWindow>();







	private readonly OverlayMiniMapSettings _settings;







	private MapTrackerService? _trackerService;







	private string? _currentMapKey;







	private MapConfig? _currentMapConfig;







	private string? _currentFloorId;







	private TaskObjectiveWithLocation? _pinnedObjective;







	private nint _hwnd;







	private bool _isClickThrough;







	private CancellationTokenSource? _markerLoadingCts;







	private string? _currentTempSvgPath;







	private bool _isPanning;







	private Point _panStartPoint;







	private double _panStartOffsetX;







	private double _panStartOffsetY;







	private Point _lastRightClickPosition;







	private class SubSpawnClusterInfo
	{
		public FrameworkElement MarkerElement { get; set; } = null!;
		public FrameworkElement? PrimaryElement { get; set; }
		public double CanvasX { get; set; }
		public double CanvasY { get; set; }
		public double PrimaryCanvasX { get; set; }
		public double PrimaryCanvasY { get; set; }
		public string ObjectiveKey { get; set; } = string.Empty;
	}

	private readonly List<SubSpawnClusterInfo> _optionalSpawnMarkers = new List<SubSpawnClusterInfo>();
	private readonly List<ScaleTransform> _markerScales = new List<ScaleTransform>();







	private DispatcherTimer? _resizeMarkerTimer;







	private DateTime _lastMapSelectorClosedTime = DateTime.MinValue;







	private string? _lastLoadedMapKey;







	private DateTime _lastMapLoadTime = DateTime.MinValue;







	private DateTime _lastClickThroughToggleTime = DateTime.MinValue;







































































































































































	public bool IsClickThrough => _isClickThrough;







	public event Action<OverlayMiniMapSettings>? SettingsChanged;







	public event Action? OverlayClosed;







	[DllImport("user32.dll")]



	private static extern int GetWindowLong(nint hwnd, int index);







	[DllImport("user32.dll")]



	private static extern int SetWindowLong(nint hwnd, int index, int newStyle);







	private void CleanupTempSvg()



	{



		if (string.IsNullOrEmpty(_currentTempSvgPath))



		{



			return;



		}



		try



		{



			if (File.Exists(_currentTempSvgPath))



			{



				File.Delete(_currentTempSvgPath);



			}



		}



		catch (Exception ex)



		{



			_log.Warning("Failed to delete temp SVG: " + ex.Message);



		}



		_currentTempSvgPath = null;



	}







	public OverlayMiniMapWindow(OverlayMiniMapSettings settings)



	{



		InitializeComponent();



		_settings = settings;

		_settings.ViewMode = MiniMapViewMode.Fixed;



		if (MapSelectorContextMenu != null)



		{



			MapSelectorContextMenu.Closed += (object s, RoutedEventArgs e) =>



			{



				_lastMapSelectorClosedTime = DateTime.UtcNow;



			};



			MapSelectorContextMenu.Opened += (object s, RoutedEventArgs e) =>



			{



				PopulateMapSelectorMenu();



			};



		}



		_resizeMarkerTimer = new DispatcherTimer



		{



			Interval = TimeSpan.FromMilliseconds(250.0)



		};



		_resizeMarkerTimer.Tick += (object? s, EventArgs e) =>



		{



			_resizeMarkerTimer.Stop();



			if (_settings.ShowCustomMarkers && _currentMapKey != null)



			{



				LoadMarkersAsync();



			}



		};



		ApplySettings();



		Loaded += OnLoaded;



		Closing += OnClosing;



		SizeChanged += OnSizeChanged;



		LocationChanged += OnLocationChanged;



	}







	private void OnLoaded(object sender, RoutedEventArgs e)



	{



		_hwnd = new WindowInteropHelper(this).Handle;



		if (_settings.PositionX < 0.0 || _settings.PositionY < 0.0)



		{



			PositionToTopRight();



		}



		else



		{



			ValidateWindowPosition();



		}



		if (_settings.ClickThrough)



		{



			EnableClickThrough();



		}



		ConnectToMapTracker();



		Dispatcher.BeginInvoke((Action)(() =>



		{



			FitMapToWindow();



			UpdateMapView();



		}), DispatcherPriority.Loaded);



		QuestProgressService.Instance.ProgressChanged += OnQuestProgressChanged;

		TarkovHelper.Services.Settings.MapSettings.Instance.HiddenQuestsChanged += OnHiddenQuestsChangedMiniMap;



		ObjectiveProgressService.Instance.ObjectiveProgressChanged += OnObjectiveProgressChanged;



		UserDataDbService.Instance.CustomMarkersChanged += OnCustomMarkersChanged;



		UpdateFloatingButtonsVisuals();



		UpdateOpenAppButtonTooltip();

		LocalizationService.Instance.LanguageChanged += OnMiniMapLanguageChanged;



		_log.Info("OverlayMiniMapWindow loaded");



	}







	private void OnMiniMapLanguageChanged(object? sender, AppLanguage lang)

	{

		UpdateOpenAppButtonTooltip();

	}



	private void UpdateOpenAppButtonTooltip()

	{

		if (BtnOpenApp != null)

		{

			BtnOpenApp.ToolTip = LocalizationService.Instance.CurrentLanguage switch

			{

				AppLanguage.ES => "Abrir Tarkov Helper (Llamar a la aplicación)",

				AppLanguage.KO => "Tarkov Helper 열기 (앱 호출)",

				AppLanguage.JA => "Tarkov Helperを開く (アプリを呼出)",

				_ => "Open Tarkov Helper (Bring app to front)"

			};

		}

	}



	private void OnClosing(object? sender, CancelEventArgs e)



	{



		QuestProgressService.Instance.ProgressChanged -= OnQuestProgressChanged;

		TarkovHelper.Services.Settings.MapSettings.Instance.HiddenQuestsChanged -= OnHiddenQuestsChangedMiniMap;



		ObjectiveProgressService.Instance.ObjectiveProgressChanged -= OnObjectiveProgressChanged;



		UserDataDbService.Instance.CustomMarkersChanged -= OnCustomMarkersChanged;

		LocalizationService.Instance.LanguageChanged -= OnMiniMapLanguageChanged;



		if (_trackerService != null)



		{



			_trackerService.PositionUpdated -= OnPositionUpdated;



			_trackerService.MapChanged -= OnMapChanged;



			_trackerService.FloorChanged -= OnFloorChanged;



			_trackerService.PinnedObjectiveChanged -= OnPinnedObjectiveChanged;



		}



		LogMapWatcherService.Instance.MapChanged -= OnLogMapWatcherMapChanged;



		_markerLoadingCts?.Cancel();



		_markerLoadingCts?.Dispose();



		_markerLoadingCts = null;



		CleanupTempSvg();



		_resizeMarkerTimer?.Stop();



		SaveSettings();



		OverlayClosed?.Invoke();



		_log.Info("OverlayMiniMapWindow closing");



	}







	private void OnCustomMarkersChanged()



	{



		Dispatcher.BeginInvoke((Action)(() =>



		{



			if (_settings.ShowCustomMarkers && _currentMapKey != null)



			{



				LoadMarkersAsync();



			}



		}));



	}







	private void OnHiddenQuestsChangedMiniMap()

	{

		OnQuestProgressChanged(null, EventArgs.Empty);

	}



	private void OnQuestProgressChanged(object? sender, EventArgs e)



	{



		Dispatcher.BeginInvoke((Action)(() =>



		{



			if (_settings.ShowQuestMarkers)



			{



				RefreshQuestMarkersOnlyAsync();



			}



		}));



	}







	private void OnObjectiveProgressChanged(object? sender, ObjectiveProgressChangedEventArgs e)



	{



		Dispatcher.BeginInvoke((Action)(() =>



		{



			if (_settings.ShowQuestMarkers)



			{



				RefreshQuestMarkersOnlyAsync();



			}



		}));



	}







	private async Task RefreshQuestMarkersOnlyAsync()



	{



		if (_currentMapKey != null && _currentMapConfig != null)



		{



			await LoadMarkersAsync();



		}



	}







	



	private DispatcherTimer? _saveSettingsDebounceTimer;







	private void SaveSettingsDebounced()



	{



		if (_saveSettingsDebounceTimer == null)



		{



			_saveSettingsDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };



			_saveSettingsDebounceTimer.Tick += (s, e) =>



			{



				_saveSettingsDebounceTimer?.Stop();



				SaveSettings();



			};



		}



		_saveSettingsDebounceTimer.Stop();



		_saveSettingsDebounceTimer.Start();



	}







	private void OnSizeChanged(object sender, SizeChangedEventArgs e)



	{



		_settings.Width = e.NewSize.Width;



		_settings.Height = e.NewSize.Height;



		if (_currentMapConfig == null)



		{



			return;



		}



		double num = Math.Max(50.0, e.NewSize.Width - 2.0);



		double num2 = Math.Max(50.0, e.NewSize.Height - 62.0);



		ScreenPosition validPlayerPosition = GetValidPlayerPosition();



		if (e.PreviousSize.Width <= 0.0 || e.PreviousSize.Height <= 0.0)



		{



			if (_settings.ViewMode == MiniMapViewMode.PlayerTracking && validPlayerPosition != null)



			{



				CenterOnPlayer(validPlayerPosition, num, num2);



			}



			else



			{



				FitMapToWindow(num, num2);



				UpdateMapView(num, num2);



			}



		}



		else if (_settings.ViewMode == MiniMapViewMode.PlayerTracking && validPlayerPosition != null)



		{



			CenterOnPlayer(validPlayerPosition, num, num2);



		}



		else if (Math.Abs(_settings.ZoomLevel - 1.0) < 0.05)



		{



			FitMapToWindow(num, num2);



			UpdateMapView(num, num2);



		}



		else



		{



			double num3 = Math.Max(50.0, e.PreviousSize.Width - 2.0);



			double num4 = Math.Max(50.0, e.PreviousSize.Height - 62.0);



			double effectiveScale = GetEffectiveScale(num3, num4);



			double effectiveScale2 = GetEffectiveScale(num, num2);



			if (effectiveScale > 0.0001 && double.IsFinite(effectiveScale))



			{



				double num5 = (num3 / 2.0 - _settings.MapOffsetX) / effectiveScale;



				double num6 = (num4 / 2.0 - _settings.MapOffsetY) / effectiveScale;



				_settings.MapOffsetX = num / 2.0 - num5 * effectiveScale2;



				_settings.MapOffsetY = num2 / 2.0 - num6 * effectiveScale2;



			}



			var (mapOffsetX, mapOffsetY) = ClampMapOffset(_settings.MapOffsetX, _settings.MapOffsetY, num, num2);



			_settings.MapOffsetX = mapOffsetX;



			_settings.MapOffsetY = mapOffsetY;



			UpdateMapView(num, num2);



		}



		_resizeMarkerTimer?.Stop();



		_resizeMarkerTimer?.Start();



	}







	private void OnLocationChanged(object? sender, EventArgs e)



	{



		if (double.IsFinite(Left) && double.IsFinite(Top))



		{



			_settings.PositionX = Left;



			_settings.PositionY = Top;



		}



	}







	private void ApplySettings()



	{



		Width = Math.Clamp(_settings.Width, 200.0, 1600.0);



		Height = Math.Clamp(_settings.Height, 200.0, 1600.0);



		if (_settings.PositionX >= 0.0 && _settings.PositionY >= 0.0)



		{



			Left = _settings.PositionX;



			Top = _settings.PositionY;



		}



		Opacity = Math.Clamp(_settings.Opacity, 0.1, 1.0);



		MainBorder.Opacity = 1.0;



		if (ExtractMarkersContainer != null)



		{



			ExtractMarkersContainer.Visibility = ((!_settings.ShowExtractMarkers) ? Visibility.Collapsed : Visibility.Visible);



		}



		if (QuestMarkersContainer != null)



		{



			QuestMarkersContainer.Visibility = ((!_settings.ShowQuestMarkers) ? Visibility.Collapsed : Visibility.Visible);



		}



		if (MapMarkersContainer != null)



		{



			MapMarkersContainer.Visibility = ((!_settings.ShowPoiMarkers && !_settings.ShowPmcSpawns) ? Visibility.Collapsed : Visibility.Visible);



		}



		UpdateFloatingButtonsVisuals();



	}







	public void SetOpacity(double opacity)



	{



		_settings.Opacity = Math.Clamp(opacity, 0.1, 1.0);



		Opacity = _settings.Opacity;



	}







	private void SaveSettings()



	{



		if (double.IsFinite(Left) && double.IsFinite(Top))



		{



			_settings.PositionX = Left;



			_settings.PositionY = Top;



		}



		if (double.IsFinite(ActualWidth) && ActualWidth >= OverlayMiniMapSettings.MinWidth)



		{



			_settings.Width = ActualWidth;



		}



		if (double.IsFinite(ActualHeight) && ActualHeight >= OverlayMiniMapSettings.MinHeight)



		{



			_settings.Height = ActualHeight;



		}



		SettingsChanged?.Invoke(_settings);



	}







	private void ValidateWindowPosition()



	{



		double virtualScreenLeft = SystemParameters.VirtualScreenLeft;



		double virtualScreenTop = SystemParameters.VirtualScreenTop;



		double virtualScreenWidth = SystemParameters.VirtualScreenWidth;



		double virtualScreenHeight = SystemParameters.VirtualScreenHeight;



		if (!double.IsFinite(Left) || !double.IsFinite(Top) || Left < virtualScreenLeft || Left > virtualScreenLeft + virtualScreenWidth - 50.0 || Top < virtualScreenTop || Top > virtualScreenTop + virtualScreenHeight - 50.0)



		{



			PositionToTopRight();



		}



	}







	private void PositionToTopRight()



	{



		Rect workArea = SystemParameters.WorkArea;



		double num;



		if (Width > 0.0 && double.IsFinite(Width))



		{



			num = Width;



		}



		else



		{



			num = ((_settings.Width > 0.0 && double.IsFinite(_settings.Width)) ? _settings.Width : 300.0);



		}



		Left = workArea.Right - num - 20.0;



		Top = workArea.Top + 20.0;



		_settings.PositionX = Left;



		_settings.PositionY = Top;



	}







	private void ConnectToMapTracker()



	{



		try



		{



			_trackerService = MapTrackerService.Instance;



			_trackerService.PositionUpdated += OnPositionUpdated;



			_trackerService.MapChanged += OnMapChanged;



			_trackerService.FloorChanged += OnFloorChanged;



			_trackerService.PinnedObjectiveChanged += OnPinnedObjectiveChanged;



			LogMapWatcherService.Instance.MapChanged += OnLogMapWatcherMapChanged;



			string currentMapKey = _trackerService.CurrentMapKey;



			_log.Info("ConnectToMapTracker: CurrentMapKey = '" + currentMapKey + "'");



			if (!string.IsNullOrEmpty(currentMapKey))



			{



				LoadMap(currentMapKey);



			}



			if (_trackerService.PinnedObjective != null)



			{



				OnPinnedObjectiveChanged(_trackerService.PinnedObjective);



			}



			else



			{



				_log.Warning("No current map set in MapTrackerService");



			}



			ScreenPosition validPlayerPosition = GetValidPlayerPosition();



			if (validPlayerPosition != null)



			{



				UpdatePlayerMarker(validPlayerPosition);



				if (_settings.ViewMode == MiniMapViewMode.PlayerTracking)



				{



					CenterOnPlayer(validPlayerPosition);



				}



			}



		}



		catch (Exception exception)



		{



			_log.Error("Failed to connect to MapTrackerService", exception);



		}



	}







	private void OnPositionUpdated(object? sender, ScreenPosition position)



	{



		Dispatcher.BeginInvoke((Action)(() =>



		{



			UpdatePlayerMarker(position);



			UpdateTrailPath();



			if (position.OriginalPosition != null && !string.IsNullOrEmpty(position.MapKey) && string.Equals(position.MapKey, _currentMapKey, StringComparison.OrdinalIgnoreCase))



			{



				string text = FloorDetectionService.Instance.DetectFloor(position.MapKey, position.OriginalPosition.X, position.OriginalPosition.Y, position.OriginalPosition.Z.GetValueOrDefault());



				if (!string.IsNullOrEmpty(text) && !string.Equals(_currentFloorId, text, StringComparison.OrdinalIgnoreCase))



				{



					_currentFloorId = text;



					_trackerService?.SetCurrentFloor(text);



					_log.Info("Floor auto-detected on minimap: '" + _currentFloorId + "'");



					if (_currentMapConfig != null && !string.IsNullOrEmpty(_currentMapConfig.SvgFileName))



					{



						string text2 = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Maps", _currentMapConfig.SvgFileName);



						if (File.Exists(text2))



						{



							LoadSvgMap(text2, resetView: false);



						}



					}



					LoadMarkersAsync();



				}



			}



			if (_settings.ViewMode == MiniMapViewMode.PlayerTracking)



			{



				CenterOnPlayer(position);



			}



		}));



	}







	private void UpdateTrailPath()



	{



		if (_trackerService == null)



		{



			return;



		}



		TrailPath.Points.Clear();



		if (!_trackerService.Settings.ShowTrail)



		{



			return;



		}



		foreach (ScreenPosition trailPosition in _trackerService.TrailPositions)



		{



			if (string.Equals(trailPosition.MapKey, _currentMapKey, StringComparison.OrdinalIgnoreCase))



			{



				TrailPath.Points.Add(new Point(trailPosition.X, trailPosition.Y));



			}



		}



	}







	private void OnMapChanged(string mapKey)



	{



		Dispatcher.BeginInvoke((Action)(() =>



		{



			string text = _trackerService?.ResolveMapKey(mapKey) ?? mapKey;



			if (!string.Equals(_lastLoadedMapKey, text, StringComparison.OrdinalIgnoreCase) || !((DateTime.UtcNow - _lastMapLoadTime).TotalMilliseconds < 350.0))



			{



				LoadMap(text);



				ScreenPosition validPlayerPosition = GetValidPlayerPosition();



				if (_settings.ViewMode == MiniMapViewMode.PlayerTracking && validPlayerPosition != null)



				{



					CenterOnPlayer(validPlayerPosition);



				}



				else



				{



					FitMapToWindow();



				}



			}



		}));



	}







	private void OnLogMapWatcherMapChanged(object? sender, MapChangedEventArgs e)



	{



		_log.Info($"OverlayMiniMapWindow: LogMapWatcherService raid map detected: '{e.NewMapKey}' (previous: '{e.PreviousMapKey}')");



		if (!string.IsNullOrEmpty(e.NewMapKey))



		{



			OnMapChanged(e.NewMapKey);



		}



	}







	private void OnPinnedObjectiveChanged(TaskObjectiveWithLocation? objective)



	{



		Dispatcher.BeginInvoke((Action)(() =>



		{



			_pinnedObjective = objective;



			UpdatePinnedObjectiveHighlight();



		}));



	}







	private void UpdatePinnedObjectiveHighlight()



	{



		if (QuestMarkersContainer == null) return;







		var toRemove = new List<UIElement>();



		foreach (UIElement child in QuestMarkersContainer.Children)



		{



			if (child is FrameworkElement fe && fe.Tag as string == "MinimapPinnedBeacon")



			{



				toRemove.Add(child);



			}



		}



		foreach (var el in toRemove)



		{



			QuestMarkersContainer.Children.Remove(el);



		}







		if (_pinnedObjective == null || _currentMapConfig == null) return;







		var loc = _pinnedObjective.Locations?.FirstOrDefault(l =>



			IsLocationOnCurrentMap(l, _currentMapConfig) &&



			(string.IsNullOrEmpty(_currentFloorId) || string.IsNullOrEmpty(l.FloorId) ||



			 string.Equals(_currentFloorId, l.FloorId, StringComparison.OrdinalIgnoreCase)));







		if (loc == null && _pinnedObjective.Locations != null)



		{



			loc = _pinnedObjective.Locations.FirstOrDefault(l => IsLocationOnCurrentMap(l, _currentMapConfig));



		}







		if (loc == null) return;







		var (screenX, screenY) = _currentMapConfig.GameToScreenForPlayer(loc.X, loc.Y);







		double beaconSize = 32.0;



		var beacon = new Ellipse



		{



			Width = beaconSize,



			Height = beaconSize,



			Stroke = new SolidColorBrush(Color.FromArgb(240, 255, 215, 0)),



			StrokeThickness = 3.0,



			Fill = new SolidColorBrush(Color.FromArgb(50, 255, 215, 0)),



			Tag = "MinimapPinnedBeacon",



			IsHitTestVisible = false



		};







		double scale = Math.Max(0.001, GetEffectiveScale());



		var st = new ScaleTransform(1.0 / scale, 1.0 / scale, beaconSize / 2.0, beaconSize / 2.0);



		beacon.RenderTransform = st;



		_markerScales.Add(st);







		var pulse = new System.Windows.Media.Animation.DoubleAnimation



		{



			From = 0.8 / scale,



			To = 1.3 / scale,



			Duration = TimeSpan.FromMilliseconds(700),



			AutoReverse = true,



			RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever



		};



		st.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);



		st.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);







		Canvas.SetLeft(beacon, screenX - beaconSize / 2.0);



		Canvas.SetTop(beacon, screenY - beaconSize / 2.0);



		Panel.SetZIndex(beacon, 200);







		QuestMarkersContainer.Children.Add(beacon);



	}







	private void BtnOpenApp_Click(object sender, RoutedEventArgs e)

	{

		try

		{

			var mainWindow = MainWindow.Instance ?? System.Windows.Application.Current.MainWindow as MainWindow;

			if (mainWindow != null)

			{

				mainWindow.RestoreFromTray();

			}

			else

			{

				foreach (Window win in System.Windows.Application.Current.Windows)

				{

					if (win is MainWindow mw)

					{

						mw.RestoreFromTray();

						return;

					}

				}

			}

		}

		catch (Exception ex)

		{

			_log.Error("Failed to restore main window from minimap overlay", ex);

		}

	}



	private void BtnMinimize_Click(object sender, RoutedEventArgs e)



	{



		WindowState = WindowState.Minimized;



	}







	private void BtnTitleMapSelector_Click(object sender, RoutedEventArgs e)



	{



		if (!((DateTime.UtcNow - _lastMapSelectorClosedTime).TotalMilliseconds < 200.0))



		{



			PopulateMapSelectorMenu();



			if (BtnTitleMapSelector?.ContextMenu != null)



			{



				BtnTitleMapSelector.ContextMenu.PlacementTarget = BtnTitleMapSelector;



				BtnTitleMapSelector.ContextMenu.Placement = PlacementMode.Bottom;



				BtnTitleMapSelector.ContextMenu.IsOpen = true;



			}



		}



	}







	public void PopulateMapSelectorMenu()



	{



		ContextMenu contextMenu = BtnTitleMapSelector?.ContextMenu ?? MapSelectorContextMenu;



		if (contextMenu == null)



		{



			return;



		}



		contextMenu.Items.Clear();



		MapTrackerService tracker = _trackerService ?? MapTrackerService.Instance;



		IReadOnlyList<string> readOnlyList = tracker?.GetAllMapKeys() ?? Array.Empty<string>();



		if (readOnlyList.Count == 0)



		{



			return;



		}



		LocalizationService loc = LocalizationService.Instance;



		var list = readOnlyList.Select((string k) =>



		{



			string displayName = (tracker?.GetMapConfig(k))?.DisplayName ?? loc.GetLocalizedMapName(k);



			return new



			{



				Key = k,



				DisplayName = displayName



			};



		}).OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();



		foreach (var map in list)



		{



			bool flag = string.Equals(map.Key, _currentMapKey, StringComparison.OrdinalIgnoreCase);



			MenuItem menuItem = new MenuItem



			{



				Header = (flag ? ("✓ " + map.DisplayName) : ("   " + map.DisplayName)),



				Tag = map.Key,



				Foreground = (flag ? new SolidColorBrush(Color.FromRgb(byte.MaxValue, 215, 0)) : Brushes.White),



				FontWeight = (flag ? FontWeights.Bold : FontWeights.Normal)



			};



			menuItem.Click += (object s, RoutedEventArgs e) =>



			{



				SelectMap(map.Key);



			};



			contextMenu.Items.Add(menuItem);



		}



	}







	public void SelectMap(string mapKey)



	{



		_log.Info("Map selected from minimap title dropdown: '" + mapKey + "'");



		(_trackerService ?? MapTrackerService.Instance)?.SetCurrentMap(mapKey);



		LoadMap(mapKey);



		ScreenPosition validPlayerPosition = GetValidPlayerPosition();



		if (_settings.ViewMode == MiniMapViewMode.PlayerTracking && validPlayerPosition != null)



		{



			CenterOnPlayer(validPlayerPosition);



		}



		else



		{



			FitMapToWindow();



		}



	}







	private void OnFloorChanged(string? floorId)



	{



		Dispatcher.BeginInvoke((Action)(() =>



		{



			if (!string.Equals(_currentFloorId, floorId, StringComparison.OrdinalIgnoreCase))



			{



				_currentFloorId = floorId;



				_log.Info("Floor changed from MapTrackerService: '" + _currentFloorId + "'");



				if (_currentMapConfig != null && !string.IsNullOrEmpty(_currentMapConfig.SvgFileName))



				{



					string text = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Maps", _currentMapConfig.SvgFileName);



					if (File.Exists(text))



					{



						LoadSvgMap(text, resetView: false);



					}



				}



				LoadMarkersAsync();



			}



		}));



	}







	private void ClearAllMapElements()



	{



		MapSvg.Source = null;



		MapMarkersContainer.Children.Clear();



		ExtractMarkersContainer.Children.Clear();



		QuestMarkersContainer.Children.Clear();



		CustomMarkersContainer.Children.Clear();



		_markerScales.Clear();

		_optionalSpawnMarkers.Clear();



		TrailPath?.Points.Clear();



		if (PlayerMarkerContainer != null)



		{



			PlayerMarkerContainer.Visibility = Visibility.Collapsed;



		}



	}







	private void LoadMap(string mapKey)



	{



		try



		{



			string text = _trackerService?.ResolveMapKey(mapKey) ?? mapKey;



			_log.Info($"LoadMap called with mapKey: '{mapKey}' (resolved: '{text}')");



			_lastLoadedMapKey = text;



			_lastMapLoadTime = DateTime.UtcNow;



			_currentMapKey = text;



			_currentMapConfig = _trackerService?.GetMapConfig(text);



			if (TxtTitleMapName != null)



			{



				TxtTitleMapName.Text = _currentMapConfig?.DisplayName ?? LocalizationService.Instance.GetLocalizedMapName(mapKey);



			}



			_log.Info("MapConfig: " + ((_currentMapConfig != null) ? $"found, SvgFileName={_currentMapConfig.SvgFileName}, Size={_currentMapConfig.ImageWidth}x{_currentMapConfig.ImageHeight}" : "null"));



			if (_currentMapConfig == null)



			{



				TxtNoMap.Visibility = Visibility.Visible;



				ClearAllMapElements();



				return;



			}



			TxtNoMap.Visibility = Visibility.Collapsed;



			if (string.IsNullOrEmpty(_currentMapConfig.SvgFileName))



			{



				TxtNoMap.Text = "SVG file not found: " + mapKey;



				TxtNoMap.Visibility = Visibility.Visible;



				ClearAllMapElements();



				return;



			}



			string text2 = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Maps", _currentMapConfig.SvgFileName);



			_log.Info($"SVG path: {text2}, exists: {File.Exists(text2)}");



			if (File.Exists(text2))



			{



				LoadSvgMap(text2);



				LoadMarkersAsync();



				_log.Info("Map loaded successfully: " + mapKey);



			}



			else



			{



				_log.Warning("Map SVG not found: " + text2);



				TxtNoMap.Text = "Map not found: " + mapKey;



				TxtNoMap.Visibility = Visibility.Visible;



				ClearAllMapElements();



			}



		}



		catch (Exception exception)



		{



			_log.Error("Failed to load map: " + mapKey, exception);



		}



	}







	private void LoadSvgMap(string svgPath, bool resetView = true)



	{



		try



		{



			_log.Info($"LoadSvgMap: Loading {svgPath} (Floor: '{_currentFloorId}', ResetView: {resetView})");



			string uriString = svgPath;



			if (_currentMapConfig != null)



			{



				try



				{



					IEnumerable<string> visibleFloorIds = null;



					IEnumerable<string> allFloorIds = null;



					string backgroundFloorId = null;



					double backgroundOpacity = 0.3;



					if (_currentMapConfig.Floors != null && _currentMapConfig.Floors.Count > 0)



					{



						if (string.IsNullOrEmpty(_currentFloorId))



						{



							_currentFloorId = _currentMapConfig.Floors.FirstOrDefault((MapFloorConfig f) => f.IsDefault)?.LayerId ?? _currentMapConfig.Floors.FirstOrDefault()?.LayerId;



						}



						if (!string.IsNullOrEmpty(_currentFloorId))



						{



							allFloorIds = _currentMapConfig.Floors.Select((MapFloorConfig f) => f.LayerId);



							visibleFloorIds = new string[1] { _currentFloorId };



							MapFloorConfig mapFloorConfig = _currentMapConfig.Floors.FirstOrDefault((MapFloorConfig f) => f.IsDefault);



							MapFloorConfig mapFloorConfig2 = _currentMapConfig.Floors.FirstOrDefault((MapFloorConfig f) => string.Equals(f.LayerId, _currentFloorId, StringComparison.OrdinalIgnoreCase));



							if (mapFloorConfig != null && !string.Equals(_currentFloorId, mapFloorConfig.LayerId, StringComparison.OrdinalIgnoreCase))



							{



								backgroundFloorId = mapFloorConfig.LayerId;



								if (mapFloorConfig2 != null && mapFloorConfig2.Order < 0)



								{



									backgroundOpacity = 0.15;



								}



							}



						}



					}



					TarkovHelper.Services.Map.SvgStylePreprocessor svgStylePreprocessor = new TarkovHelper.Services.Map.SvgStylePreprocessor();



					string contents = svgStylePreprocessor.ProcessSvgFile(svgPath, visibleFloorIds, allFloorIds, backgroundFloorId, backgroundOpacity);



					string text = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"minimap_{Guid.NewGuid():N}.svg");



					File.WriteAllText(text, contents);



					string currentTempSvgPath = _currentTempSvgPath;



					_currentTempSvgPath = text;



					if (!string.IsNullOrEmpty(currentTempSvgPath) && !string.Equals(currentTempSvgPath, text, StringComparison.OrdinalIgnoreCase))



					{



						try



						{



							if (File.Exists(currentTempSvgPath))



							{



								File.Delete(currentTempSvgPath);



							}



						}



						catch



						{



						}



					}



					uriString = text;



				}



				catch (Exception ex)



				{



					_log.Warning("SvgStylePreprocessor error, falling back to raw SVG: " + ex.Message);



					uriString = svgPath;



				}



			}



			MapSvg.Source = new Uri(uriString, UriKind.Absolute);



			MapSvg.Visibility = Visibility.Visible;



			if (_currentMapConfig != null)



			{



				MapSvg.Width = _currentMapConfig.ImageWidth;



				MapSvg.Height = _currentMapConfig.ImageHeight;



				MapCanvas.Width = _currentMapConfig.ImageWidth;



				MapCanvas.Height = _currentMapConfig.ImageHeight;



				Canvas.SetLeft(MapSvg, 0.0);



				Canvas.SetTop(MapSvg, 0.0);



				_log.Debug($"MapSvg: Size={MapSvg.Width}x{MapSvg.Height}, Visibility={MapSvg.Visibility}");



				_log.Debug($"MapCanvas: Size={MapCanvas.Width}x{MapCanvas.Height}");



			}



			if (resetView)



			{



				FitMapToWindow();



			}



			else



			{



				UpdateMapView();



			}



			_log.Info($"SVG loaded, Zoom={_settings.ZoomLevel:F4}, Offset=({_settings.MapOffsetX:F1}, {_settings.MapOffsetY:F1})");



		}



		catch (Exception exception)



		{



			_log.Error("Failed to load SVG: " + svgPath, exception);



		}



	}







	public double GetViewportWidth()



	{



		double num;



		if (Width > 0.0 && !double.IsNaN(Width))



		{



			num = Width;



		}



		else if (_settings.Width > 0.0)



		{



			num = _settings.Width;



		}



		else



		{



			num = ((ActualWidth > 0.0) ? ActualWidth : 300.0);



		}



		if (MapContainer != null && MapContainer.ActualWidth > 0.0 && Math.Abs(MapContainer.ActualWidth - (num - 2.0)) <= 2.0)



		{



			return MapContainer.ActualWidth;



		}



		return Math.Max(50.0, num - 2.0);



	}







	public double GetViewportHeight()



	{



		double num;



		if (Height > 0.0 && !double.IsNaN(Height))



		{



			num = Height;



		}



		else if (_settings.Height > 0.0)



		{



			num = _settings.Height;



		}



		else



		{



			num = ((ActualHeight > 0.0) ? ActualHeight : 300.0);



		}



		if (MapContainer != null && MapContainer.ActualHeight > 0.0)



		{



			return MapContainer.ActualHeight;



		}



		return Math.Max(50.0, num - 62.0);



	}







	public void AdaptWindowToMapAspectRatio(double? referenceDimension = null)



	{



		if (_currentMapConfig == null || _currentMapConfig.ImageWidth <= 0 || _currentMapConfig.ImageHeight <= 0)



		{



			return;



		}



		double num = (double)_currentMapConfig.ImageWidth / (double)_currentMapConfig.ImageHeight;



		if (double.IsFinite(num) && !(num <= 0.001))



		{



			double num2;



			if (ActualWidth > 100.0)



			{



				num2 = ActualWidth;



			}



			else



			{



				num2 = ((_settings.Width > 100.0) ? _settings.Width : 300.0);



			}



			double num3;



			if (ActualHeight > 100.0)



			{



				num3 = ActualHeight;



			}



			else



			{



				num3 = ((_settings.Height > 100.0) ? _settings.Height : 300.0);



			}



			double num4 = Math.Max(100.0, num2 - 2.0);



			double num5 = Math.Max(100.0, num3 - 28.0 - 2.0);



			double d = num4 * num5;



			double num6 = referenceDimension ?? Math.Sqrt(d);



			double num7 = Math.Round(num6 * Math.Sqrt(num));



			double num8 = Math.Round(num6 / Math.Sqrt(num));



			double num9 = 198.0;



			double num10 = 170.0;



			double num11 = 1598.0;



			double num12 = 1570.0;



			if (num7 < num9)



			{



				num7 = num9;



				num8 = Math.Round(num9 / num);



			}



			if (num8 < num10)



			{



				num8 = num10;



				num7 = Math.Round(num10 * num);



			}



			if (num7 > num11)



			{



				num7 = num11;



				num8 = Math.Round(num11 / num);



			}



			if (num8 > num12)



			{



				num8 = num12;



				num7 = Math.Round(num12 * num);



			}



			double width = num7 + 2.0;



			double height = num8 + 28.0 + 2.0;



			Width = width;



			Height = height;



			_settings.Width = width;



			_settings.Height = height;



			ValidateWindowPosition();



			UpdateLayout();



			FitMapToWindow(num7, num8);



			UpdateMapView(num7, num8);



		}



	}







	public static Rect GetMapPlayableBounds(MapConfig? config)



	{



		if (config == null) return new Rect(0, 0, 1000, 1000);



		string key = config.Key ?? "";



		if (key.Equals("Woods", StringComparison.OrdinalIgnoreCase)) return new Rect(1000, 1000, 2800, 2800);

		if (key.Equals("Shoreline", StringComparison.OrdinalIgnoreCase)) return new Rect(1066.0, 1036.5, 1559.57, 1032.49);

		if (key.Equals("StreetsOfTarkov", StringComparison.OrdinalIgnoreCase) || key.Equals("TarkovStreets", StringComparison.OrdinalIgnoreCase)) return new Rect(1014.0, 831.5, 1210.65, 1663.15);

		if (key.Equals("Customs", StringComparison.OrdinalIgnoreCase)) return new Rect(1000, 1000, 2400, 1200);

		if (key.Equals("Factory", StringComparison.OrdinalIgnoreCase)) return new Rect(1000, 1000, 1600, 1600);

		if (key.Equals("GroundZero", StringComparison.OrdinalIgnoreCase)) return new Rect(1000, 1000, 800, 1100);

		if (key.Equals("Interchange", StringComparison.OrdinalIgnoreCase)) return new Rect(1000, 1000, 2000, 1900);

		if (key.Equals("Lighthouse", StringComparison.OrdinalIgnoreCase)) return new Rect(1000, 1000, 1100, 1700);

		if (key.Equals("Reserve", StringComparison.OrdinalIgnoreCase)) return new Rect(1000, 1000, 1200, 1000);

		if (key.Equals("Labs", StringComparison.OrdinalIgnoreCase)) return new Rect(1000, 1000, 3500, 2200);

		if (key.Equals("Labyrinth", StringComparison.OrdinalIgnoreCase)) return new Rect(1000, 1000, 1300, 1200);



		return new Rect(0, 0, config.ImageWidth, config.ImageHeight);



	}







	public double CalculateBaseFitScale(double? overrideWidth = null, double? overrideHeight = null)



	{



		if (_currentMapConfig == null || _currentMapConfig.ImageWidth <= 0 || _currentMapConfig.ImageHeight <= 0)



		{



			return 1.0;



		}







		double val = overrideWidth ?? GetViewportWidth();



		double val2 = overrideHeight ?? GetViewportHeight();



		val = Math.Max(50.0, val);



		val2 = Math.Max(50.0, val2);







		Rect bounds = GetMapPlayableBounds(_currentMapConfig);



		double val3 = val / bounds.Width;



		double val4 = val2 / bounds.Height;



		double num = Math.Max(val3, val4);







		return (double.IsFinite(num) && num > 1E-05) ? num : 1.0;



	}







	public double GetEffectiveScale(double? overrideWidth = null, double? overrideHeight = null)



	{



		return Math.Max(0.001, CalculateBaseFitScale(overrideWidth, overrideHeight) * _settings.ZoomLevel);



	}







	public void FitMapToWindow(double? overrideWidth = null, double? overrideHeight = null)



	{



		if (_currentMapConfig != null && _currentMapConfig.ImageWidth > 0 && _currentMapConfig.ImageHeight > 0)



		{



			_settings.ZoomLevel = 1.0;



			double effectiveScale = GetEffectiveScale(overrideWidth, overrideHeight);



			double num = overrideWidth ?? GetViewportWidth();



			double num2 = overrideHeight ?? GetViewportHeight();







			Rect bounds = GetMapPlayableBounds(_currentMapConfig);



			double centerX = bounds.X + bounds.Width / 2.0;



			double centerY = bounds.Y + bounds.Height / 2.0;







			_settings.MapOffsetX = num / 2.0 - centerX * effectiveScale;



			_settings.MapOffsetY = num2 / 2.0 - centerY * effectiveScale;







			var (num5, num6) = ClampMapOffset(_settings.MapOffsetX, _settings.MapOffsetY, overrideWidth, overrideHeight);



			_settings.MapOffsetX = num5;



			_settings.MapOffsetY = num6;



			MapTranslate.X = num5;



			MapTranslate.Y = num6;



			MapScale.ScaleX = effectiveScale;



			MapScale.ScaleY = effectiveScale;



			UpdateMarkerScales(overrideWidth, overrideHeight);



			_log.Debug($"FitMapToWindow: ZoomLevel={_settings.ZoomLevel:F2}, EffectiveScale={effectiveScale:F4}, Center=({centerX:F0},{centerY:F0}), Offset=({_settings.MapOffsetX:F1}, {_settings.MapOffsetY:F1})");



		}



	}







	public void ResetViewToFit()



	{



		FitMapToWindow();



		UpdateMapView();



		UpdateRecenterButtonVisibility();



		SettingsChanged?.Invoke(_settings);



	}







	private void AddTestMarker()



	{



		if (_currentMapConfig != null)



		{



			double num = (double)_currentMapConfig.ImageWidth / 2.0;



			double num2 = (double)_currentMapConfig.ImageHeight / 2.0;



			double num3 = Math.Max(0.001, GetEffectiveScale());



			double num4 = 15.0 / num3;



			double strokeThickness = 3.0 / num3;



			Ellipse element = new Ellipse



			{



				Width = num4,



				Height = num4,



				Fill = new SolidColorBrush(Colors.Red),



				Stroke = new SolidColorBrush(Colors.Yellow),



				StrokeThickness = strokeThickness,



				Opacity = 1.0



			};



			Canvas.SetLeft(element, num - num4 / 2.0);



			Canvas.SetTop(element, num2 - num4 / 2.0);



			ExtractMarkersContainer.Children.Add(element);



			_log.Info($"AddTestMarker: Added test marker at map center ({num:F0}, {num2:F0}), size={num4:F1}");



		}



	}







	private async Task LoadMarkersAsync()



	{



		_markerLoadingCts?.Cancel();



		_markerLoadingCts?.Dispose();



		_markerLoadingCts = new CancellationTokenSource();



		CancellationToken ct = _markerLoadingCts.Token;



		MarkerRefreshHelper.SafeClear(MapMarkersContainer);



		MarkerRefreshHelper.SafeClear(ExtractMarkersContainer);



		MarkerRefreshHelper.SafeClear(QuestMarkersContainer);



		MarkerRefreshHelper.SafeClear(CustomMarkersContainer);



		_markerScales.Clear();

		_optionalSpawnMarkers.Clear();



		if (_currentMapKey == null || _currentMapConfig == null)



		{



			_log.Debug("LoadMarkersAsync: mapKey or mapConfig is null, skipping");



			return;



		}



		try



		{



			if (_settings.ShowPoiMarkers || _settings.ShowPmcSpawns)



			{



				await LoadMapMarkersAsync(ct);



			}



			if (ct.IsCancellationRequested)



			{



				return;



			}



			if (_settings.ShowExtractMarkers)



			{



				await LoadExtractMarkersAsync(ct);



			}



			if (ct.IsCancellationRequested)



			{



				return;



			}



			if (_settings.ShowQuestMarkers)



			{



				await LoadQuestMarkersAsync(ct);



			}



			if (!ct.IsCancellationRequested)



			{



				if (_settings.ShowCustomMarkers)



				{



					await LoadCustomMarkersAsync(ct);



				}



				if (!ct.IsCancellationRequested)



				{



					UpdateMarkerScales();



					UpdatePinnedObjectiveHighlight();



					_log.Info($"LoadMarkersAsync completed for {_currentMapKey}: Extracts={_settings.ShowExtractMarkers}, Quests={_settings.ShowQuestMarkers}, Custom={_settings.ShowCustomMarkers}");



				}



			}



		}



		catch (OperationCanceledException)



		{



			_log.Debug("LoadMarkersAsync operation cancelled");



		}



		catch (Exception exception)



		{



			_log.Error("Failed to load markers", exception);



		}



	}







	private void UpdateMarkerScales(double? overrideWidth = null, double? overrideHeight = null)



	{



		double effectiveScale = GetEffectiveScale(overrideWidth, overrideHeight);



		if (effectiveScale <= 0.0)



		{



			return;



		}



		double num = 1.0 / effectiveScale;



		foreach (ScaleTransform markerScale in _markerScales)



		{



			markerScale.ScaleX = num;



			markerScale.ScaleY = num;



		}



		Canvas[] array = new Canvas[4] { MapMarkersContainer, ExtractMarkersContainer, QuestMarkersContainer, CustomMarkersContainer };



		foreach (Canvas canvas in array)



		{



			if (canvas == null)



			{



				continue;



			}



			foreach (object child in canvas.Children)



			{



				if (child is FrameworkElement frameworkElement && MarkerRefreshHelper.GetIsRetained(frameworkElement) && frameworkElement.RenderTransform is ScaleTransform scaleTransform)



				{



					scaleTransform.ScaleX = num;



					scaleTransform.ScaleY = num;



				}



			}



		}



		if (TrailPath != null)



		{



			TrailPath.StrokeThickness = 2.0 * num;



		}



		if (QuestMarkersContainer == null)



		{



			return;



		}



		foreach (object child2 in QuestMarkersContainer.Children)



		{



			if (child2 is Polygon polygon)



			{



				polygon.StrokeThickness = 2.0 * num;



			}



		}



		UpdateSubSpawnClusterVisibility(overrideWidth, overrideHeight);



	}



	private void UpdateSubSpawnClusterVisibility(double? overrideWidth = null, double? overrideHeight = null)
	{
		if (_optionalSpawnMarkers.Count == 0) return;

		double effectiveScale = GetEffectiveScale(overrideWidth, overrideHeight);
		double zoomLevel = _settings.ZoomLevel;

		foreach (var subSpawn in _optionalSpawnMarkers)
		{
			if (subSpawn.MarkerElement == null) continue;

			if (subSpawn.PrimaryElement != null)
			{
				if (subSpawn.PrimaryElement.Visibility != Visibility.Visible)
				{
					subSpawn.MarkerElement.Visibility = Visibility.Collapsed;
					continue;
				}

				var dx = subSpawn.CanvasX - subSpawn.PrimaryCanvasX;
				var dy = subSpawn.CanvasY - subSpawn.PrimaryCanvasY;
				var distOnScreen = Math.Sqrt(dx * dx + dy * dy) * effectiveScale;

				// Micro-spawns within a small room / building:
				// When zoomed out (zoomLevel < 1.5) or if on-screen distance is less than 25 pixels,
				// collapse into the primary marker to prevent cluttering the minimap overview!
				if (zoomLevel < 1.5 && distOnScreen < 25.0)
				{
					subSpawn.MarkerElement.Visibility = Visibility.Collapsed;
				}
				else
				{
					subSpawn.MarkerElement.Visibility = Visibility.Visible;
				}
			}
			else
			{
				subSpawn.MarkerElement.Visibility = Visibility.Visible;
			}
		}
	}







	private async Task LoadMapMarkersAsync(CancellationToken ct = default(CancellationToken))



	{



		if (_currentMapKey == null || _currentMapConfig == null)



		{



			return;



		}



		MapMarkerDbService markerDb = MapMarkerDbService.Instance;



		if (!markerDb.IsLoaded)



		{



			await markerDb.LoadMarkersAsync();



		}



		if (ct.IsCancellationRequested)



		{



			return;



		}



		IReadOnlyList<MapMarker> markers = markerDb.GetMarkersForMap(_currentMapKey);



		if (markers == null || markers.Count == 0)



		{



			return;



		}



		Math.Max(0.001, GetEffectiveScale());



		foreach (MapMarker marker in markers)



		{



			if (ct.IsCancellationRequested)



			{



				return;



			}



			if (marker.Type != MarkerType.PmcSpawn && marker.Type != MarkerType.BossSpawn && marker.Type != MarkerType.SniperScavSpawn && marker.Type != MarkerType.RogueSpawn && marker.Type != MarkerType.CultistSpawn && marker.Type != MarkerType.Lever)



			{



				continue;



			}



			bool isSpawnMarker = marker.Type is MarkerType.PmcSpawn or MarkerType.ScavSpawn or MarkerType.SniperScavSpawn or MarkerType.RogueSpawn or MarkerType.CultistSpawn;

			bool isPoiMarker = marker.Type is MarkerType.BossSpawn or MarkerType.RaiderSpawn or MarkerType.Lever;



			if (isSpawnMarker && !_settings.ShowPmcSpawns)

			{

				continue;

			}

			if (isPoiMarker && !_settings.ShowPoiMarkers)

			{

				continue;

			}



			(double, double) tuple = _currentMapConfig.GameToScreenForPlayer(marker.X, marker.Z);



			double screenX = tuple.Item1;



			double screenY = tuple.Item2;



			FrameworkElement element = CreateMapMarkerElement(marker, screenX, screenY);



			MapMarkersContainer.Children.Add(element);



		}



	}







	private FrameworkElement CreateMapMarkerElement(MapMarker marker, double screenX, double screenY)



	{



		bool flag = MapFloorHelper.IsMarkerOnCurrentFloor(_currentFloorId, marker.FloorId);



		(byte R, byte G, byte B) markerColor = MapMarker.GetMarkerColor(marker.Type);



		Color color = Color.FromRgb(markerColor.R, markerColor.G, markerColor.B);



		MarkerType type = marker.Type;



		double item5 = type switch



		{



			MarkerType.BossSpawn => 12.0,



			MarkerType.PmcSpawn => 10.0,



			_ => 10.0



		};



		(string, string, Color)? floorIndicator = MapFloorHelper.GetFloorIndicator(_currentMapConfig, _currentFloorId, marker.FloorId);



		string value;



		if (floorIndicator.HasValue)



		{



			value = " [" + floorIndicator.Value.Item1 + floorIndicator.Value.Item2 + "]";



		}



		else



		{



			value = ((!string.IsNullOrEmpty(marker.FloorId)) ? (" [" + marker.FloorId + "]") : "");



		}



		Canvas canvas = new Canvas



		{



			Width = 0.0,



			Height = 0.0,



			Cursor = Cursors.Hand,



			Opacity = (flag ? 1.0 : 0.5),



			Tag = marker



		};



		double num = Math.Max(24.0, item5 * 1.5);



		var hitArea = new Ellipse



		{



			Width = num,



			Height = num,



			Fill = Brushes.Transparent,



			Margin = new Thickness((0.0 - num) / 2.0, (0.0 - num) / 2.0, 0.0, 0.0),



			IsHitTestVisible = true



		};



		canvas.Children.Add(hitArea);







		var mapTooltip = MapTooltipHelper.CreateTooltip(marker.Name, $"{marker.Type}{value}", placementTarget: hitArea);



		canvas.ToolTip = mapTooltip;



		hitArea.ToolTip = mapTooltip;



		MapTooltipHelper.ConfigureToolTipService(canvas);



		MapTooltipHelper.ConfigureToolTipService(hitArea);







		Ellipse element2 = new Ellipse



		{



			Width = item5,



			Height = item5,



			Fill = new SolidColorBrush(color),



			Stroke = new SolidColorBrush(Colors.White),



			StrokeThickness = 1.2,



			Margin = new Thickness((0.0 - item5) / 2.0, (0.0 - item5) / 2.0, 0.0, 0.0),



			IsHitTestVisible = false



		};



		canvas.Children.Add(element2);



		// Zero letters/text inside circles (clean tactical dot)



		double num2 = Math.Max(0.001, GetEffectiveScale());



		ScaleTransform item6 = (ScaleTransform)(canvas.RenderTransform = new ScaleTransform(1.0 / num2, 1.0 / num2));



		_markerScales.Add(item6);



		Canvas.SetLeft(canvas, screenX);



		Canvas.SetTop(canvas, screenY);



		return canvas;



	}







	private FrameworkElement CreateExtractMarker(MapExtract extract, double screenX, double screenY)



	{



		bool flag = MapFloorHelper.IsMarkerOnCurrentFloor(_currentFloorId, extract.FloorId);



		(Color fill, Color stroke) extractStyle = MapExtractMarkerManager.GetExtractStyle(extract.Faction);



		Color item = extractStyle.fill;



		Color item2 = extractStyle.stroke;



		(string, string, Color)? floorIndicator = MapFloorHelper.GetFloorIndicator(_currentMapConfig, _currentFloorId, extract.FloorId);



		string value;



		if (floorIndicator.HasValue)



		{



			value = " [" + floorIndicator.Value.Item1 + floorIndicator.Value.Item2 + "]";



		}



		else



		{



			value = ((!string.IsNullOrEmpty(extract.FloorId)) ? (" [" + extract.FloorId + "]") : "");



		}







		string factionName = extract.Faction switch



		{



			ExtractFaction.Shared => "Co-op (PMC + Scav)",



			ExtractFaction.Pmc => "PMC",



			ExtractFaction.Scav => "Scav",



			ExtractFaction.Transit => "Transit",



			_ => extract.Faction.ToString()



		};







		Canvas canvas = new Canvas



		{



			Width = 0.0,



			Height = 0.0,



			Cursor = Cursors.Hand,



			Opacity = (flag ? 1.0 : 0.5),



			Tag = extract



		};







		double num = 28.0;



		var hitArea = new Ellipse



		{



			Width = num,



			Height = num,



			Fill = Brushes.Transparent,



			Margin = new Thickness((0.0 - num) / 2.0, (0.0 - num) / 2.0, 0.0, 0.0),



			IsHitTestVisible = true



		};



		canvas.Children.Add(hitArea);







		var extractTooltip = MapTooltipHelper.CreateTooltip(extract.Name, $"Extraction ({factionName}){value}", placementTarget: hitArea);



		canvas.ToolTip = extractTooltip;



		hitArea.ToolTip = extractTooltip;



		MapTooltipHelper.ConfigureToolTipService(canvas);



		MapTooltipHelper.ConfigureToolTipService(hitArea);







		// Tactical circular dot + name label matching MapPage (zero emojis)

		var dot = new Ellipse

		{

			Width = 10.0,

			Height = 10.0,

			Fill = new SolidColorBrush(item),

			Stroke = Brushes.White,

			StrokeThickness = 1.5,

			Margin = new Thickness(-5.0, -5.0, 0.0, 0.0),

			IsHitTestVisible = false

		};

		canvas.Children.Add(dot);



		var nameLabel = new Border

		{

			Background = new SolidColorBrush(Color.FromArgb(235, 18, 18, 24)),

			BorderBrush = new SolidColorBrush(Color.FromArgb(180, item.R, item.G, item.B)),

			BorderThickness = new Thickness(1),

			CornerRadius = new CornerRadius(3),

			Padding = new Thickness(4, 1, 4, 1),

			Margin = new Thickness(0, 7, 0, 0),

			HorizontalAlignment = HorizontalAlignment.Center,

			IsHitTestVisible = false,

			Child = new TextBlock

			{

				Text = extract.Name,

				FontSize = 9.5,

				FontWeight = FontWeights.Bold,

				Foreground = new SolidColorBrush(item),

				TextAlignment = TextAlignment.Center

			}

		};

		nameLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

		double labelW = nameLabel.DesiredSize.Width;

		Canvas.SetLeft(nameLabel, -labelW / 2);

		canvas.Children.Add(nameLabel);







		double num2 = Math.Max(0.001, GetEffectiveScale());



		ScaleTransform item3 = (ScaleTransform)(canvas.RenderTransform = new ScaleTransform(1.0 / num2, 1.0 / num2));



		_markerScales.Add(item3);



		Canvas.SetLeft(canvas, screenX);



		Canvas.SetTop(canvas, screenY);



		return canvas;



	}







	private FrameworkElement CreateQuestMarker(TaskObjectiveWithLocation obj, double screenX, double screenY, string? floorId = null, QuestObjectiveLocation? loc = null)
	{
		bool flag = MapFloorHelper.IsMarkerOnCurrentFloor(_currentFloorId, floorId);
		bool isOrPointer = loc != null && !string.IsNullOrEmpty(loc.Id) && loc.Id.Contains("_opt_");
		int orIndex = 1;
		if (isOrPointer && loc != null)
		{
			var match = System.Text.RegularExpressions.Regex.Match(loc.Id, @"_opt_(\d+)");
			if (match.Success && int.TryParse(match.Groups[1].Value, out var parsedIdx))
			{
				orIndex = parsedIdx + 1;
			}
		}

		Color color;
		if (isOrPointer)
		{
			color = Color.FromRgb(0, 188, 212);
		}
		else
		{
			color = ResolveQuestMarkerColor(obj);
		}

		(string, string, Color)? floorIndicator = MapFloorHelper.GetFloorIndicator(_currentMapConfig, _currentFloorId, floorId);
		string floorSuffix;
		if (floorIndicator.HasValue)
		{
			floorSuffix = " [" + floorIndicator.Value.Item1 + floorIndicator.Value.Item2 + "]";
		}
		else
		{
			floorSuffix = ((!string.IsNullOrEmpty(floorId)) ? (" [" + floorId + "]") : "");
		}

		Canvas canvas = new Canvas
		{
			Width = 0.0,
			Height = 0.0,
			Cursor = Cursors.Hand,
			Opacity = (flag ? 1.0 : 0.5),
			Tag = obj
		};

		double num = 26.0;
		var hitArea = new Ellipse
		{
			Width = num,
			Height = num,
			Fill = Brushes.Transparent,
			Margin = new Thickness((0.0 - num) / 2.0, (0.0 - num) / 2.0, 0.0, 0.0),
			IsHitTestVisible = true
		};
		canvas.Children.Add(hitArea);

		string tooltipHeader;
		if (isOrPointer)
		{
			tooltipHeader = $"{(obj.TaskName ?? "Quest Objective")} [Spawn alternativo #{orIndex}]";
		}
		else if (obj.Locations != null && obj.Locations.Count > 1)
		{
			tooltipHeader = $"{(obj.TaskName ?? "Quest Objective")} ({obj.Locations.Count} posibles spawns)";
		}
		else
		{
			tooltipHeader = obj.TaskName ?? "Quest Objective";
		}

		var questTooltip = MapTooltipHelper.CreateTooltip(tooltipHeader, obj.Description, floorSuffix, placementTarget: hitArea);
		canvas.ToolTip = questTooltip;
		hitArea.ToolTip = questTooltip;
		MapTooltipHelper.ConfigureToolTipService(canvas);
		MapTooltipHelper.ConfigureToolTipService(hitArea);

		// Circular quest dot with white border (25% smaller for optional sub-spawns: 10.5 vs 14.0)
		double dotSize = isOrPointer ? 10.5 : 14.0;
		double halfSize = dotSize / 2.0;
		Ellipse element2 = new Ellipse
		{
			Width = dotSize,
			Height = dotSize,
			Fill = new SolidColorBrush(color),
			Stroke = new SolidColorBrush(Colors.White),
			StrokeThickness = isOrPointer ? 1.2 : 1.5,
			Margin = new Thickness(-halfSize, -halfSize, 0.0, 0.0),
			IsHitTestVisible = false
		};
		canvas.Children.Add(element2);

		double num2 = Math.Max(0.001, GetEffectiveScale());
		ScaleTransform item = (ScaleTransform)(canvas.RenderTransform = new ScaleTransform(1.0 / num2, 1.0 / num2));
		_markerScales.Add(item);
		Canvas.SetLeft(canvas, screenX);
		Canvas.SetTop(canvas, screenY);
		return canvas;
	}







	private async Task LoadExtractMarkersAsync(CancellationToken ct = default(CancellationToken))



	{



		ExtractService extractService = ExtractService.Instance;



		if (extractService == null)



		{



			_log.Warning("LoadExtractMarkersAsync: ExtractService.Instance is null");



			return;



		}



		if (!extractService.IsLoaded)



		{



			_log.Info("LoadExtractMarkersAsync: Loading extract data...");



			bool loadResult = await extractService.LoadAsync();



			_log.Info($"LoadExtractMarkersAsync: Load result = {loadResult}");



		}



		List<MapExtract> extracts = extractService.GetExtractsForMap(_currentMapKey, _currentMapConfig);



		if (extracts == null || extracts.Count == 0)



		{



			_log.Warning("LoadExtractMarkersAsync: No extracts found for map: " + _currentMapKey);



			return;



		}



		_log.Info($"LoadExtractMarkersAsync: Found {extracts.Count} extracts for {_currentMapKey}");



		bool showPmc = _settings.ShowPmcExtracts;



		bool showScav = _settings.ShowScavExtracts;



		bool showTransit = SettingsService.Instance.MapShowTransits;



		int addedCount = 0;



		foreach (MapExtract extract in extracts)



		{



			ExtractFaction faction = extract.Faction;



			if (1 == 0)



			{



			}



			bool flag = faction switch



			{



				ExtractFaction.Pmc => showPmc, 



				ExtractFaction.Scav => showScav, 



				ExtractFaction.Transit => showPmc, 



				ExtractFaction.Shared => showPmc | showScav, 



				_ => true, 



			};



			if (1 == 0)



			{



			}



			if (flag)



			{



				(double, double) tuple = _currentMapConfig.GameToScreenForPlayer(extract.X, extract.Z);



				double screenX = tuple.Item1;



				double screenY = tuple.Item2;



				FrameworkElement marker = CreateExtractMarker(extract, screenX, screenY);



				ExtractMarkersContainer.Children.Add(marker);



				addedCount++;



			}



		}



		_log.Info($"LoadExtractMarkersAsync: Added {addedCount} extract markers (PMC={showPmc}, Scav={showScav}, Transit={showTransit})");



	}







	private async Task LoadQuestMarkersAsync(CancellationToken ct = default(CancellationToken))



	{



		if (_currentMapConfig == null || string.IsNullOrEmpty(_currentMapKey))



		{



			_log.Warning("LoadQuestMarkersAsync: _currentMapConfig or _currentMapKey is null");



			return;



		}



		QuestObjectiveService objectiveService = QuestObjectiveService.Instance;



		if (objectiveService == null)



		{



			_log.Warning("LoadQuestMarkersAsync: QuestObjectiveService.Instance is null");



			return;



		}



		if (!objectiveService.IsLoaded)



		{



			_log.Info("LoadQuestMarkersAsync: Loading quest objectives...");



			await objectiveService.LoadAsync();



		}



		QuestProgressService progressService = QuestProgressService.Instance;



		if (!progressService.IsLoadedFromDb)



		{



			ProfileType profile = ProfileService.Instance.CurrentProfile;



			await progressService.InitializeFromDbAsync(profile);



		}



		List<string> mapNamesToSearch = new List<string> { _currentMapKey };



		if (_currentMapConfig.Aliases != null)



		{



			mapNamesToSearch.AddRange(_currentMapConfig.Aliases);



		}



		if (!string.IsNullOrEmpty(_currentMapConfig.DisplayName) && !mapNamesToSearch.Contains(_currentMapConfig.DisplayName))



		{



			mapNamesToSearch.Add(_currentMapConfig.DisplayName);



		}



		List<TaskObjectiveWithLocation> objectivesToDisplay = new List<TaskObjectiveWithLocation>();



		if (_settings.ActiveQuestsOnly)



		{



			foreach (string mapName in mapNamesToSearch)



			{



				List<TaskObjectiveWithLocation> activeObjs = objectiveService.GetActiveObjectivesForMap(mapName, progressService);



				foreach (TaskObjectiveWithLocation obj in activeObjs)



				{



					if (!objectivesToDisplay.Any((TaskObjectiveWithLocation o) => o.ObjectiveId == obj.ObjectiveId))



					{



						objectivesToDisplay.Add(obj);



					}



				}



			}



		}



		else



		{



			List<TaskObjectiveWithLocation> allObjs = objectiveService.GetObjectivesForMap(_currentMapKey, _currentMapConfig);



			if (allObjs != null)



			{



				objectivesToDisplay.AddRange(allObjs);



			}



		}



		HashSet<string> hiddenQuests = SettingsService.Instance.MapHiddenQuests;



		int addedCount = 0;



		foreach (TaskObjectiveWithLocation obj2 in objectivesToDisplay)



		{



			if (obj2.Locations == null || obj2.Locations.Count == 0 || (!string.IsNullOrEmpty(obj2.QuestId) && hiddenQuests.Contains(obj2.QuestId)) || (!string.IsNullOrEmpty(obj2.TaskNormalizedName) && hiddenQuests.Contains(obj2.TaskNormalizedName)) || (!string.IsNullOrEmpty(obj2.TaskName) && hiddenQuests.Contains(obj2.TaskName)))



			{



				continue;



			}



			bool isCompleted = (obj2.IsCompleted = ObjectiveProgressService.Instance.IsObjectiveCompletedById(obj2.ObjectiveId));



			if (_settings.HideCompletedObjectives & isCompleted)



			{



				continue;



			}



			List<QuestObjectiveLocation> locationsOnCurrentMap = obj2.Locations.Where((QuestObjectiveLocation location) => IsLocationOnCurrentMap(location, _currentMapConfig)).ToList();
			FrameworkElement? primaryMarker = null;
			double primaryScreenX = 0;
			double primaryScreenY = 0;

			foreach (QuestObjectiveLocation loc in locationsOnCurrentMap)



			{



				if (loc.Outline != null && loc.Outline.Count >= 3)



				{



					bool isAreaOnCurrentFloor = MapFloorHelper.IsMarkerOnCurrentFloor(_currentFloorId, loc.FloorId);



					Color areaColor = ResolveQuestMarkerColor(obj2);



					(string arrow, string floorText, Color color)? areaIndicator = MapFloorHelper.GetFloorIndicator(_currentMapConfig, _currentFloorId, loc.FloorId);



					string areaFloorSuffix;



					if (areaIndicator.HasValue)



					{



						areaFloorSuffix = " [" + areaIndicator.Value.arrow + areaIndicator.Value.floorText + "]";



					}



					else



					{



						areaFloorSuffix = ((!string.IsNullOrEmpty(loc.FloorId)) ? (" [" + loc.FloorId + "]") : "");



					}



					double polygonInvScale = 1.0 / Math.Max(0.001, GetEffectiveScale());



					StackPanel areaTtPanel = new StackPanel();



					if (!string.IsNullOrEmpty(obj2.TaskName))



					{



						areaTtPanel.Children.Add(new TextBlock



						{



							Text = obj2.TaskName,



							Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")),



							FontWeight = FontWeights.SemiBold,



							FontSize = 11.0



						});



						areaTtPanel.Children.Add(new TextBlock



						{



							Text = "• " + obj2.Description + areaFloorSuffix,



							Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F0F0")),



							FontSize = 10.0,



							TextWrapping = TextWrapping.Wrap,



							MaxWidth = 250.0,



							Margin = new Thickness(0.0, 2.0, 0.0, 0.0)



						});



					}



					else



					{



						areaTtPanel.Children.Add(new TextBlock



						{



							Text = (obj2.Description ?? "Quest Objective") + areaFloorSuffix,



							Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")),



							FontWeight = FontWeights.SemiBold,



							FontSize = 11.0,



							TextWrapping = TextWrapping.Wrap,



							MaxWidth = 250.0



						});



					}



					Polygon polygon = new Polygon



					{



						Fill = new SolidColorBrush(Color.FromArgb(40, areaColor.R, areaColor.G, areaColor.B)),



						Stroke = new SolidColorBrush(Color.FromArgb(180, areaColor.R, areaColor.G, areaColor.B)),



						StrokeThickness = 2.0 * polygonInvScale,



						Opacity = (isAreaOnCurrentFloor ? 1.0 : 0.5),



						ToolTip = MapTooltipHelper.CreateTooltip(obj2.TaskName ?? "Quest Objective", obj2.Description, areaFloorSuffix)



					};



					MapTooltipHelper.ConfigureToolTipService(polygon);



					foreach (OutlinePoint pt in loc.Outline)



					{



						var (px, py) = _currentMapConfig.GameToScreenForPlayer(pt.X, pt.Y);



						polygon.Points.Add(new Point(px, py));



					}



					QuestMarkersContainer.Children.Add(polygon);



				}



				(double, double) tuple2 = _currentMapConfig.GameToScreenForPlayer(loc.X, loc.Y);



				double screenX = tuple2.Item1;



				double screenY = tuple2.Item2;



				FrameworkElement marker = CreateQuestMarker(obj2, screenX, screenY, loc.FloorId, loc);

				bool isOrPointer = loc != null && !string.IsNullOrEmpty(loc.Id) && loc.Id.Contains("_opt_");
				if (!isOrPointer)
				{
					primaryMarker = marker;
					primaryScreenX = screenX;
					primaryScreenY = screenY;
				}
				else if (primaryMarker != null)
				{
					_optionalSpawnMarkers.Add(new SubSpawnClusterInfo
					{
						MarkerElement = marker,
						PrimaryElement = primaryMarker,
						CanvasX = screenX,
						CanvasY = screenY,
						PrimaryCanvasX = primaryScreenX,
						PrimaryCanvasY = primaryScreenY,
						ObjectiveKey = obj2.ObjectiveId
					});
				}

				QuestMarkersContainer.Children.Add(marker);

				addedCount++;



			}



		}



		UpdateSubSpawnClusterVisibility();
		_log.Info($"LoadQuestMarkersAsync: Added {addedCount} quest markers for {_currentMapKey} (ActiveOnly={_settings.ActiveQuestsOnly}, HideCompleted={_settings.HideCompletedObjectives})");



	}







	private bool IsLocationOnCurrentMap(QuestObjectiveLocation location, MapConfig config)



	{



		if (string.IsNullOrEmpty(_currentMapKey))



		{



			return false;



		}



		List<string> list = new List<string> { _currentMapKey };



		if (config.Aliases != null)



		{



			list.AddRange(config.Aliases);



		}



		if (!string.IsNullOrEmpty(config.DisplayName) && !list.Contains(config.DisplayName))



		{



			list.Add(config.DisplayName);



		}



		string text = location.MapNormalizedName?.Replace(" ", "").Replace("-", "").ToLowerInvariant();



		foreach (string item in list)



		{



			string text2 = item.Replace(" ", "").Replace("-", "").ToLowerInvariant();



			if (text == text2)



			{



				return true;



			}



			string mapName = location.MapName;



			if (mapName != null && mapName.Equals(item, StringComparison.OrdinalIgnoreCase))



			{



				return true;



			}



			string mapId = location.MapId;



			if (mapId != null && mapId.Equals(item, StringComparison.OrdinalIgnoreCase))



			{



				return true;



			}



		}



		return false;



	}







	private Color ResolveQuestMarkerColor(TaskObjectiveWithLocation obj)

	{

		var norm = (obj.Type ?? "").Trim().ToLowerInvariant();

		string value = _trackerService?.Settings?.GetMarkerColor(norm) ?? obj.MarkerColor;

		if (!string.IsNullOrEmpty(value))

		{

			try

			{

				return (Color)ColorConverter.ConvertFromString(value);

			}

			catch {}

		}



		return norm switch

		{

			"visit" => (Color)ColorConverter.ConvertFromString("#00E5FF"),

			"mark" => (Color)ColorConverter.ConvertFromString("#FFA000"),

			"finditem" or "find" or "collect" or "handover" => (Color)ColorConverter.ConvertFromString("#FFEB3B"),

			"plantitem" or "plant" or "stash" => (Color)ColorConverter.ConvertFromString("#AB47BC"),

			"extract" or "survive" => (Color)ColorConverter.ConvertFromString("#29B6F6"),

			_ => (Color)ColorConverter.ConvertFromString("#00E5FF")

		};

	}







	private async Task LoadCustomMarkersAsync(CancellationToken ct = default(CancellationToken))



	{



		if (_currentMapKey == null || _currentMapConfig == null)



		{



			return;



		}



		try



		{



			List<CustomMapMarker> markers = await UserDataDbService.Instance.LoadCustomMarkersAsync(_currentMapKey);



			if (markers == null || markers.Count == 0)



			{



				return;



			}



			foreach (CustomMapMarker marker in markers)



			{



				double screenX;



				double screenY;



				if (_trackerService?.Transformer != null && _trackerService.Transformer.TryTransform(_currentMapKey, marker.X, marker.Z, null, out ScreenPosition screenPos) && screenPos != null)



				{



					screenX = screenPos.X;



					screenY = screenPos.Y;



				}



				else



				{



					(screenX, screenY) = _currentMapConfig.GameToScreenForPlayer(marker.X, marker.Z);



				}



				FrameworkElement markerElement = CreateCustomMarkerElement(marker, screenX, screenY);



				CustomMarkersContainer.Children.Add(markerElement);



				screenPos = null;



			}



			_log.Info($"LoadCustomMarkersAsync: Added {markers.Count} custom markers");



		}



		catch (Exception exception)



		{



			_log.Error("Failed to load custom markers in overlay", exception);



		}



	}







	private FrameworkElement CreateCustomMarkerElement(CustomMapMarker marker, double screenX, double screenY)



	{



		bool flag = MapFloorHelper.IsMarkerOnCurrentFloor(_currentFloorId, marker.FloorId);



		double viewportWidth = GetViewportWidth();



		double viewportHeight = GetViewportHeight();



		double num = Math.Min(viewportWidth, viewportHeight);



		double num2 = Math.Clamp(num / 350.0, 0.6, 2.0);



		double num3 = ((marker.Size > 0.0) ? marker.Size : 12.0);



		MarkerSizePreset markerPreset = _settings.MarkerPreset;



		double num4 = markerPreset switch



		{



			MarkerSizePreset.Small => 0.7,



			MarkerSizePreset.Large => 1.4,



			_ => 1.0



		};



		double num5 = num4;



		double num6 = Math.Round(num3 * num5 * num2, 1);



		double num7 = num6 / 2.0;



		(string, string, Color)? floorIndicator = MapFloorHelper.GetFloorIndicator(_currentMapConfig, _currentFloorId, marker.FloorId);



		string text2;



		if (floorIndicator.HasValue)



		{



			text2 = " [" + floorIndicator.Value.Item1 + floorIndicator.Value.Item2 + "]";



		}



		else



		{



			text2 = ((!string.IsNullOrEmpty(marker.FloorId)) ? (" [" + marker.FloorId + "]") : "");



		}



		Canvas canvas = new Canvas



		{



			Width = 0.0,



			Height = 0.0,



			Tag = marker,



			Cursor = Cursors.Hand



		};







		double num8 = Math.Max(24.0, num6 * 1.5);



		var hitArea = new Ellipse



		{



			Width = num8,



			Height = num8,



			Fill = Brushes.Transparent,



			Margin = new Thickness((0.0 - num8) / 2.0, (0.0 - num8) / 2.0, 0.0, 0.0),



			IsHitTestVisible = true



		};



		canvas.Children.Add(hitArea);







		var customTooltip = MapTooltipHelper.CreateTooltip(string.IsNullOrEmpty(marker.Name) ? "Custom Marker" : marker.Name, $"Custom Marker{text2}", placementTarget: hitArea);



		canvas.ToolTip = customTooltip;



		hitArea.ToolTip = customTooltip;



		MapTooltipHelper.ConfigureToolTipService(canvas);



		MapTooltipHelper.ConfigureToolTipService(hitArea);







		Ellipse element2 = new Ellipse



		{



			Width = num6,



			Height = num6,



			Fill = GetColorBrush(marker.Color ?? "#FFD700"),



			Stroke = new SolidColorBrush(Colors.White),



			StrokeThickness = 1.5,



			Margin = new Thickness(0.0 - num7, 0.0 - num7, 0.0, 0.0),



			IsHitTestVisible = false,



			Opacity = (flag ? 1.0 : 0.5) * marker.Opacity * (SettingsService.Instance.MapMarkerOpacity / 100.0)



		};



		canvas.Children.Add(element2);



		ContextMenu contextMenu = new ContextMenu();



		MenuItem menuItem = new MenuItem



		{



			Header = "Edit Marker..."



		};



		menuItem.Click += async (object s, RoutedEventArgs e) =>



		{



			CustomMarkerEditorWindow editor = new CustomMarkerEditorWindow(marker)



			{



				Owner = this,



				WindowStartupLocation = WindowStartupLocation.CenterOwner



			};



			if (editor.ShowDialog() == true && editor.ResultMarker != null)



			{



				await UserDataDbService.Instance.SaveCustomMarkerAsync(editor.ResultMarker);



				await LoadMarkersAsync();



			}



		};



		contextMenu.Items.Add(menuItem);



		MenuItem menuItem2 = new MenuItem



		{



			Header = "Delete Marker"



		};



		menuItem2.Click += async (object s, RoutedEventArgs e) =>



		{



			MessageBoxResult result = MessageBox.Show("Are you sure you want to delete marker '" + marker.Name + "'?", "Delete Marker", MessageBoxButton.YesNo, MessageBoxImage.Question);



			if (result == MessageBoxResult.Yes)



			{



				await UserDataDbService.Instance.DeleteCustomMarkerAsync(marker.Id);



				await LoadMarkersAsync();



			}



		};



		contextMenu.Items.Add(menuItem2);



		canvas.ContextMenu = contextMenu;



		double num9 = Math.Max(0.001, GetEffectiveScale());



		ScaleTransform item = (ScaleTransform)(canvas.RenderTransform = new ScaleTransform(1.0 / num9, 1.0 / num9));



		_markerScales.Add(item);



		Canvas.SetLeft(canvas, screenX);



		Canvas.SetTop(canvas, screenY);



		return canvas;



	}







	private static Brush GetColorBrush(string? colorHex)



	{



		if (string.IsNullOrEmpty(colorHex))



		{



			return new SolidColorBrush(Color.FromRgb(byte.MaxValue, 215, 0));



		}



		try



		{



			return (Brush)new BrushConverter().ConvertFromString(colorHex);



		}



		catch



		{



			return new SolidColorBrush(Color.FromRgb(byte.MaxValue, 215, 0));



		}



	}







	private void UpdatePlayerMarker(ScreenPosition position)



	{



		if (_currentMapConfig == null)



		{



			return;



		}



		if (!string.IsNullOrEmpty(position.MapKey) && !string.Equals(position.MapKey, _currentMapKey, StringComparison.OrdinalIgnoreCase))



		{



			PlayerMarkerContainer.Visibility = Visibility.Collapsed;



			return;



		}



		PlayerMarkerContainer.Visibility = Visibility.Visible;



		Canvas.SetLeft(PlayerMarkerContainer, position.X - 10.0);



		Canvas.SetTop(PlayerMarkerContainer, position.Y - 10.0);



		if (position.Angle.HasValue)



		{



			double num = position.Angle.Value;



			if (string.Equals(_currentMapKey, "Labs", StringComparison.OrdinalIgnoreCase))



			{



				num -= 90.0;



			}



			else if (string.Equals(_currentMapKey, "Factory", StringComparison.OrdinalIgnoreCase))



			{



				num += 90.0;



			}



			PlayerRotation.Angle = num;



			PlayerDirectionArrow.Visibility = Visibility.Visible;



		}



		else



		{



			PlayerDirectionArrow.Visibility = Visibility.Collapsed;



		}



	}







	public void UpdateMapView(double? overrideWidth = null, double? overrideHeight = null)



	{



		if (_currentMapConfig == null)



		{



			_log.Debug("UpdateMapView: _currentMapConfig is null, skipping");



			return;



		}



		double effectiveScale = GetEffectiveScale(overrideWidth, overrideHeight);



		MapScale.ScaleX = effectiveScale;



		MapScale.ScaleY = effectiveScale;



		double playerMarkerSize = _settings.PlayerMarkerSize;



		if (PlayerMarkerScale != null && effectiveScale > 0.0)



		{



			PlayerMarkerScale.ScaleX = playerMarkerSize / effectiveScale;



			PlayerMarkerScale.ScaleY = playerMarkerSize / effectiveScale;



		}



		UpdateMarkerScales(overrideWidth, overrideHeight);



		var (num, num2) = ClampMapOffset(_settings.MapOffsetX, _settings.MapOffsetY, overrideWidth, overrideHeight);



		_settings.MapOffsetX = num;



		_settings.MapOffsetY = num2;



		MapTranslate.X = num;



		MapTranslate.Y = num2;



		_log.Debug($"UpdateMapView: ZoomLevel={_settings.ZoomLevel:F2}, EffectiveScale={effectiveScale:F4}, Translate=({MapTranslate.X:F1}, {MapTranslate.Y:F1}), ViewMode={_settings.ViewMode}");



	}







	public ScreenPosition? GetValidPlayerPosition()



	{



		ScreenPosition screenPosition = _trackerService?.LastPosition;



		if (screenPosition == null)



		{



			return null;



		}



		if (!string.IsNullOrEmpty(screenPosition.MapKey) && !string.Equals(screenPosition.MapKey, _currentMapKey, StringComparison.OrdinalIgnoreCase))



		{



			return null;



		}



		return screenPosition;



	}







	public void CenterOnPlayer(ScreenPosition? position = null, double? overrideWidth = null, double? overrideHeight = null)



	{



		if (position == null)



		{



			position = GetValidPlayerPosition();



		}



		if (position != null && _currentMapConfig != null && (string.IsNullOrEmpty(position.MapKey) || string.Equals(position.MapKey, _currentMapKey, StringComparison.OrdinalIgnoreCase)))



		{



			double num = overrideWidth ?? GetViewportWidth();



			double num2 = overrideHeight ?? GetViewportHeight();



			double effectiveScale = GetEffectiveScale(overrideWidth, overrideHeight);



			double offsetX = num / 2.0 - position.X * effectiveScale;



			double offsetY = num2 / 2.0 - position.Y * effectiveScale;



			(offsetX, offsetY) = ClampMapOffset(offsetX, offsetY, overrideWidth, overrideHeight);



			_settings.MapOffsetX = offsetX;



			_settings.MapOffsetY = offsetY;



			UpdateMapView(overrideWidth, overrideHeight);



			UpdateRecenterButtonVisibility();



		}



	}







	private (double x, double y) ClampMapOffset(double offsetX, double offsetY, double? overrideWidth = null, double? overrideHeight = null)



	{



		if (_currentMapConfig == null || _currentMapConfig.ImageWidth <= 0 || _currentMapConfig.ImageHeight <= 0)



		{



			return (x: offsetX, y: offsetY);



		}







		double val = overrideWidth ?? GetViewportWidth();



		double val2 = overrideHeight ?? GetViewportHeight();



		val = Math.Max(50.0, val);



		val2 = Math.Max(50.0, val2);







		double effectiveScale = GetEffectiveScale(overrideWidth, overrideHeight);



		if (!double.IsFinite(effectiveScale) || effectiveScale <= 1E-05)



		{



			return (x: 0.0, y: 0.0);



		}







		Rect bounds = GetMapPlayableBounds(_currentMapConfig);



		double renderedW = bounds.Width * effectiveScale;



		double renderedH = bounds.Height * effectiveScale;







		double num3;



		if (renderedW <= val)



		{



			num3 = (val - renderedW) / 2.0 - bounds.X * effectiveScale;



		}



		else



		{



			double padding = Math.Min(30.0, (renderedW - val) / 2.0);



			double min = val - (bounds.X + bounds.Width) * effectiveScale - padding;



			double max = -bounds.X * effectiveScale + padding;



			num3 = Math.Clamp(offsetX, Math.Min(min, max), Math.Max(min, max));



		}







		double num5;



		if (renderedH <= val2)



		{



			num5 = (val2 - renderedH) / 2.0 - bounds.Y * effectiveScale;



		}



		else



		{



			double padding = Math.Min(30.0, (renderedH - val2) / 2.0);



			double min = val2 - (bounds.Y + bounds.Height) * effectiveScale - padding;



			double max = -bounds.Y * effectiveScale + padding;



			num5 = Math.Clamp(offsetY, Math.Min(min, max), Math.Max(min, max));



		}







		return (x: num3, y: num5);



	}







	private void EnableClickThrough()



	{



		if (_hwnd != IntPtr.Zero)



		{



			int windowLong = GetWindowLong(_hwnd, -20);



			SetWindowLong(_hwnd, -20, windowLong | 0x20 | 0x80000);



			_isClickThrough = true;



			_settings.ClickThrough = true;



			UpdateFloatingButtonsVisuals();



			_log.Debug("Click-through mode enabled");



			SettingsChanged?.Invoke(_settings);



		}



	}







	private void DisableClickThrough()



	{



		if (_hwnd != IntPtr.Zero)



		{



			int windowLong = GetWindowLong(_hwnd, -20);



			SetWindowLong(_hwnd, -20, windowLong & -33);



			_isClickThrough = false;



			_settings.ClickThrough = false;



			UpdateFloatingButtonsVisuals();



			_log.Debug("Click-through mode disabled");



			SettingsChanged?.Invoke(_settings);



		}



	}







	public void SetClickThrough(bool enabled)



	{



		if (enabled)



		{



			EnableClickThrough();



		}



		else



		{



			DisableClickThrough();



		}



	}







	public void ToggleClickThrough()



	{



		DateTime utcNow = DateTime.UtcNow;



		if (!((utcNow - _lastClickThroughToggleTime).TotalMilliseconds < 250.0))



		{



			_lastClickThroughToggleTime = utcNow;



			if (_isClickThrough)



			{



				DisableClickThrough();



			}



			else



			{



				EnableClickThrough();



			}



		}



	}







	protected override void OnPreviewKeyDown(KeyEventArgs e)



	{



		base.OnPreviewKeyDown(e);



		if (Keyboard.Modifiers == ModifierKeys.Control)



		{



			if (e.Key == Key.D1 || e.Key == Key.NumPad1)



			{



				e.Handled = true;



				OverlayMiniMapService.Instance.ToggleOverlay();



			}



			else if (e.Key == Key.D7 || e.Key == Key.NumPad7)



			{



				e.Handled = true;



				OverlayMiniMapService.Instance.ShowSettingsWindow();



			}



			else if (e.Key == Key.D9 || e.Key == Key.NumPad9)



			{



				e.Handled = true;



				ToggleClickThrough();



			}



		}



	}







	private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)



	{



		if (_isClickThrough)



		{



			return;



		}



		if (e.ClickCount == 2)



		{



			PositionToTopRight();



		}



		else if (e.LeftButton == MouseButtonState.Pressed)



		{



			try



			{



				DragMove();



			}



			catch (InvalidOperationException)



			{



			}



		}



	}







	private void MapContainer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)



	{



		if (_isClickThrough || _currentMapConfig == null)



		{



			return;



		}



		double effectiveScale = GetEffectiveScale();



		if (!double.IsFinite(effectiveScale) || effectiveScale <= 0.0001)



		{



			return;



		}



		double num = ((e.Delta > 0) ? 1.15 : 0.8695652173913044);



		double num2 = Math.Clamp(_settings.ZoomLevel * num, 0.5, 4.0);



		if (!(Math.Abs(num2 - _settings.ZoomLevel) < 0.001))



		{



			double viewportWidth = GetViewportWidth();



			double viewportHeight = GetViewportHeight();



			_settings.ZoomLevel = num2;



			double effectiveScale2 = GetEffectiveScale();



			ScreenPosition validPlayerPosition = GetValidPlayerPosition();



			if (_settings.ViewMode == MiniMapViewMode.PlayerTracking && validPlayerPosition != null)



			{



				_settings.MapOffsetX = viewportWidth / 2.0 - validPlayerPosition.X * effectiveScale2;



				_settings.MapOffsetY = viewportHeight / 2.0 - validPlayerPosition.Y * effectiveScale2;



			}



			else



			{



				Point position = e.GetPosition(MapContainer);



				double num3 = ((position.X >= 0.0 && position.X <= viewportWidth) ? position.X : (viewportWidth / 2.0));



				double num4 = ((position.Y >= 0.0 && position.Y <= viewportHeight) ? position.Y : (viewportHeight / 2.0));



				double num5 = (num3 - _settings.MapOffsetX) / effectiveScale;



				double num6 = (num4 - _settings.MapOffsetY) / effectiveScale;



				_settings.MapOffsetX = num3 - num5 * effectiveScale2;



				_settings.MapOffsetY = num4 - num6 * effectiveScale2;



			}



			var (mapOffsetX, mapOffsetY) = ClampMapOffset(_settings.MapOffsetX, _settings.MapOffsetY);



			_settings.MapOffsetX = mapOffsetX;



			_settings.MapOffsetY = mapOffsetY;



			UpdateMapView();



			UpdateRecenterButtonVisibility();



			SettingsChanged?.Invoke(_settings);



			e.Handled = true;



		}



	}







	private void MapContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)



	{



		if (!_isClickThrough)



		{



			_isPanning = true;



			_panStartPoint = e.GetPosition(MapContainer);



			_panStartOffsetX = _settings.MapOffsetX;



			_panStartOffsetY = _settings.MapOffsetY;



			MapContainer.CaptureMouse();



			Cursor = Cursors.SizeAll;



			e.Handled = true;



			if (_settings.ViewMode == MiniMapViewMode.PlayerTracking)



			{



				_settings.ViewMode = MiniMapViewMode.Fixed;



				UpdateRecenterButtonVisibility();



			}



		}



	}







	private void MapContainer_MouseDown(object sender, MouseButtonEventArgs e)



	{



		if (!_isClickThrough && e.ChangedButton == MouseButton.Middle && e.ButtonState == MouseButtonState.Pressed)



		{



			_isPanning = true;



			_panStartPoint = e.GetPosition(MapContainer);



			_panStartOffsetX = _settings.MapOffsetX;



			_panStartOffsetY = _settings.MapOffsetY;



			MapContainer.CaptureMouse();



			Cursor = Cursors.SizeAll;



			e.Handled = true;



			if (_settings.ViewMode == MiniMapViewMode.PlayerTracking)



			{



				_settings.ViewMode = MiniMapViewMode.Fixed;



				UpdateRecenterButtonVisibility();



			}



		}



	}







	private void MapContainer_MouseUp(object sender, MouseButtonEventArgs e)



	{



		if (e.ChangedButton == MouseButton.Middle && _isPanning)



		{



			_isPanning = false;



			MapContainer.ReleaseMouseCapture();



			Cursor = Cursors.Arrow;



			e.Handled = true;



		}



	}







	private void MapContainer_MouseMove(object sender, MouseEventArgs e)



	{



		if (_isPanning)



		{



			Point position = e.GetPosition(MapContainer);



			double num = position.X - _panStartPoint.X;



			double num2 = position.Y - _panStartPoint.Y;



			double offsetX = _panStartOffsetX + num;



			double offsetY = _panStartOffsetY + num2;



			(offsetX, offsetY) = ClampMapOffset(offsetX, offsetY);



			_settings.MapOffsetX = offsetX;



			_settings.MapOffsetY = offsetY;



			UpdateMapView();



			e.Handled = true;



		}



	}







	private void MapContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)



	{



		if (_isPanning)



		{



			_isPanning = false;



			MapContainer.ReleaseMouseCapture();



			Cursor = Cursors.Arrow;



			e.Handled = true;



		}



	}







	private void TitleBar_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)



	{



		_lastRightClickPosition = new Point(-1.0, -1.0);



	}







	private void MainBorder_ContextMenuOpening(object sender, ContextMenuEventArgs e)



	{



		bool isEnabled = _currentMapConfig != null && _lastRightClickPosition.X >= 0.0 && _lastRightClickPosition.X <= (double)_currentMapConfig.ImageWidth && _lastRightClickPosition.Y >= 0.0 && _lastRightClickPosition.Y <= (double)_currentMapConfig.ImageHeight;



		MenuAddCustomMarker.IsEnabled = isEnabled;



	}







	private void MapContainer_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)



	{



		if (!_isClickThrough)



		{



			_lastRightClickPosition = e.GetPosition(MapCanvas);



		}



	}







	private void MapContainer_MouseRightButtonDown(object sender, MouseButtonEventArgs e)



	{



		if (!_isClickThrough)



		{



			_lastRightClickPosition = e.GetPosition(MapCanvas);



		}



	}







	private async void MenuAddCustomMarker_Click(object sender, RoutedEventArgs e)



	{



		if (_currentMapKey == null || _currentMapConfig == null)



		{



			return;



		}



		double canvasX = _lastRightClickPosition.X;



		double canvasY = _lastRightClickPosition.Y;



		if (canvasX < 0.0 || canvasX > (double)_currentMapConfig.ImageWidth || canvasY < 0.0 || canvasY > (double)_currentMapConfig.ImageHeight)



		{



			_log.Warning($"MenuAddCustomMarker: Click coordinates ({canvasX:F1}, {canvasY:F1}) outside map bounds [0, {_currentMapConfig.ImageWidth}]x[0, {_currentMapConfig.ImageHeight}].");



			return;



		}



		double worldX;



		double worldY;



		double worldZ;



		if (_trackerService?.Transformer != null && _trackerService.Transformer.TryTransformToWorld(_currentMapKey, canvasX, canvasY, out EftPosition pos) && pos != null)



		{



			worldX = pos.X;



			worldY = pos.Y;



			worldZ = pos.Z.GetValueOrDefault();



		}



		else



		{



			(double, double) tuple = _currentMapConfig.ScreenToGameForPlayer(canvasX, canvasY);



			double gx = tuple.Item1;



			double gz = tuple.Item2;



			worldX = gx;



			worldY = 0.0;



			worldZ = gz;



		}



		CustomMapMarker tempMarker = new CustomMapMarker(_currentMapKey, "New Marker", worldX, worldY, worldZ)



		{



			FloorId = _currentFloorId



		};



		CustomMarkerEditorWindow editor = new CustomMarkerEditorWindow(tempMarker)



		{



			Owner = this,



			WindowStartupLocation = WindowStartupLocation.CenterOwner



		};



		if (editor.ShowDialog() == true && editor.ResultMarker != null)



		{



			await UserDataDbService.Instance.SaveCustomMarkerAsync(editor.ResultMarker);



			await LoadMarkersAsync();



		}



	}







	private void MenuSettings_Click(object sender, RoutedEventArgs e)



	{



		OverlayMiniMapService.Instance.ShowSettingsWindow();



	}







	private void BtnZoomIn_Click(object sender, RoutedEventArgs e)



	{



		ZoomIn();



	}







	private void BtnZoomOut_Click(object sender, RoutedEventArgs e)



	{



		ZoomOut();



	}







	private void BtnFitMap_Click(object sender, RoutedEventArgs e)



	{



		// Refit map to fill window without mutating window size set by user



		FitMapToWindow();



		UpdateMapView();



	}







	private void BtnTogglePmcExtracts_Click(object sender, RoutedEventArgs e)



	{



		_settings.ShowPmcExtracts = !_settings.ShowPmcExtracts;



		UpdateFloatingButtonsVisuals();



		LoadMarkersAsync();



		SettingsChanged?.Invoke(_settings);



	}







	private void BtnToggleScavExtracts_Click(object sender, RoutedEventArgs e)



	{



		_settings.ShowScavExtracts = !_settings.ShowScavExtracts;



		UpdateFloatingButtonsVisuals();



		LoadMarkersAsync();



		SettingsChanged?.Invoke(_settings);



	}







	private void BtnToggleQuests_Click(object sender, RoutedEventArgs e)



	{



		_settings.ShowQuestMarkers = !_settings.ShowQuestMarkers;



		QuestMarkersContainer.Visibility = ((!_settings.ShowQuestMarkers) ? Visibility.Collapsed : Visibility.Visible);



		UpdateFloatingButtonsVisuals();



		LoadMarkersAsync();



		SettingsChanged?.Invoke(_settings);



	}







	private void BtnTogglePmcSpawns_Click(object sender, RoutedEventArgs e)



	{



		_settings.ShowPmcSpawns = !_settings.ShowPmcSpawns;



		UpdateFloatingButtonsVisuals();



		LoadMarkersAsync();



		SettingsChanged?.Invoke(_settings);



	}







	private void BtnTogglePois_Click(object sender, RoutedEventArgs e)



	{



		_settings.ShowPoiMarkers = !_settings.ShowPoiMarkers;



		UpdateFloatingButtonsVisuals();



		LoadMarkersAsync();



		SettingsChanged?.Invoke(_settings);



	}







	private void BtnClearPlayerTrail_Click(object sender, RoutedEventArgs e)



	{



		_trackerService?.ClearTrail();



		TrailPath?.Points.Clear();



	}







	private void BtnToggleClickThrough_Click(object sender, RoutedEventArgs e)



	{



		ToggleClickThrough();



	}







	private void BtnRecenter_Click(object sender, RoutedEventArgs e)



	{



		CenterOnPlayer();



		_settings.ViewMode = MiniMapViewMode.PlayerTracking;



		UpdateRecenterButtonVisibility();



	}







	private void UpdateRecenterButtonVisibility()



	{



		if (BtnRecenter != null)



		{



			bool flag = GetValidPlayerPosition() != null;



			BtnRecenter.Visibility = Visibility.Collapsed;



		}



		UpdateFloatingButtonsVisuals();



	}







	private void UpdateFloatingButtonsVisuals()



	{



		if (ExtractMarkersContainer != null)



		{



			ExtractMarkersContainer.Visibility = (_settings.ShowPmcExtracts || _settings.ShowScavExtracts || _settings.ShowExtractMarkers) ? Visibility.Visible : Visibility.Collapsed;



			foreach (UIElement child in ExtractMarkersContainer.Children)



			{



				if (child is FrameworkElement fe && fe.Tag is MapExtract ext)



				{



					bool visible = ext.Faction switch



					{



						ExtractFaction.Pmc => _settings.ShowPmcExtracts,



						ExtractFaction.Scav => _settings.ShowScavExtracts,



						ExtractFaction.Transit => _settings.ShowPmcExtracts,



						ExtractFaction.Shared => _settings.ShowPmcExtracts || _settings.ShowScavExtracts,



						_ => true



					};



					fe.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;



				}



			}



		}







		if (QuestMarkersContainer != null)



		{



			QuestMarkersContainer.Visibility = ((!_settings.ShowQuestMarkers) ? Visibility.Collapsed : Visibility.Visible);



		}







		if (MapMarkersContainer != null)



		{



			MapMarkersContainer.Visibility = (_settings.ShowPmcSpawns || _settings.ShowPoiMarkers) ? Visibility.Visible : Visibility.Collapsed;



			foreach (UIElement child in MapMarkersContainer.Children)



			{



				if (child is FrameworkElement fe && fe.Tag is MapMarker m)



				{



					bool isSpawn = m.Type is MarkerType.PmcSpawn or MarkerType.ScavSpawn or MarkerType.SniperScavSpawn or MarkerType.RogueSpawn or MarkerType.CultistSpawn;

					if (isSpawn)

						fe.Visibility = _settings.ShowPmcSpawns ? Visibility.Visible : Visibility.Collapsed;

					else

						fe.Visibility = _settings.ShowPoiMarkers ? Visibility.Visible : Visibility.Collapsed;



				}



			}



		}







		if (BtnTogglePmcExtracts != null)



		{



			bool showPmcExtracts = _settings.ShowPmcExtracts;



			BtnTogglePmcExtracts.Opacity = (showPmcExtracts ? 1.0 : 0.45);



			BtnTogglePmcExtracts.BorderBrush = (showPmcExtracts ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8800E676")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#44FFFFFF")));



			BtnTogglePmcExtracts.Foreground = (showPmcExtracts ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#777777")));



			BtnTogglePmcExtracts.ToolTip = (showPmcExtracts ? "PMC Extractions: Visible (+ Co-op in Cyan) (Click to hide)" : "PMC Extractions: Hidden (Click to show)");



		}







		if (BtnToggleScavExtracts != null)



		{



			bool showScavExtracts = _settings.ShowScavExtracts;



			BtnToggleScavExtracts.Opacity = (showScavExtracts ? 1.0 : 0.45);



			BtnToggleScavExtracts.BorderBrush = (showScavExtracts ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#88FF9800")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#44FFFFFF")));



			BtnToggleScavExtracts.Foreground = (showScavExtracts ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9800")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#777777")));



			BtnToggleScavExtracts.ToolTip = (showScavExtracts ? "Scav Extractions: Visible (+ Co-op in Cyan) (Click to hide)" : "Scav Extractions: Hidden (Click to show)");



		}







		if (BtnToggleQuests != null)



		{



			bool showQuestMarkers = _settings.ShowQuestMarkers;



			BtnToggleQuests.Opacity = (showQuestMarkers ? 1.0 : 0.45);



			BtnToggleQuests.BorderBrush = (showQuestMarkers ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8800E5FF")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#44FFFFFF")));



			BtnToggleQuests.Foreground = (showQuestMarkers ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#777777")));



			BtnToggleQuests.ToolTip = (showQuestMarkers ? "Quests: Visible (Click to hide)" : "Quests: Hidden (Click to show)");



		}







		if (BtnTogglePmcSpawns != null)



		{



			bool showPmcSpawns = _settings.ShowPmcSpawns;



			BtnTogglePmcSpawns.Opacity = (showPmcSpawns ? 1.0 : 0.45);



			BtnTogglePmcSpawns.BorderBrush = (showPmcSpawns ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8842A5F5")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#44FFFFFF")));



			BtnTogglePmcSpawns.Foreground = (showPmcSpawns ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#42A5F5")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#777777")));



			BtnTogglePmcSpawns.ToolTip = (showPmcSpawns ? "PMC Spawns: Visible (Click to hide)" : "PMC Spawns: Hidden (Click to show)");



		}







		if (BtnTogglePois != null)



		{



			bool showPoiMarkers = _settings.ShowPoiMarkers;



			BtnTogglePois.Opacity = (showPoiMarkers ? 1.0 : 0.45);



			BtnTogglePois.BorderBrush = (showPoiMarkers ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#88FF5722")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#44FFFFFF")));



			BtnTogglePois.Foreground = (showPoiMarkers ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5722")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#777777")));



			BtnTogglePois.ToolTip = (showPoiMarkers ? "POIs & Bosses: Visible (Click to hide)" : "POIs & Bosses: Hidden (Click to show)");



		}







			if (BtnClearPlayerTrail != null)

		{

			BtnClearPlayerTrail.Opacity = 0.65;

			BtnClearPlayerTrail.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33FFFFFF"));

			BtnClearPlayerTrail.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA"));

			BtnClearPlayerTrail.ToolTip = "Clear Raid Trail";

		}



		if (BtnToggleClickThrough != null)

		{

			bool isClickThrough = _isClickThrough;

			BtnToggleClickThrough.Content = (isClickThrough ? "Pass" : "Click");

			BtnToggleClickThrough.Opacity = (isClickThrough ? 1.0 : 0.65);

			BtnToggleClickThrough.BorderBrush = (isClickThrough ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#88FF5252")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33FFFFFF")));

			BtnToggleClickThrough.Foreground = (isClickThrough ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5252")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA")));

			BtnToggleClickThrough.ToolTip = (isClickThrough ? "Click-Through: Active (Ctrl+9 to disable)" : "Click-Through: Inactive (Ctrl+9 to enable)");

		}



		UpdateFloorUI();



	}

	private void UpdateFloorUI()

	{

		if (_currentMapConfig?.Floors != null && _currentMapConfig.Floors.Count > 1)

		{

			if (PnlFloorSelector != null) PnlFloorSelector.Visibility = Visibility.Visible;

			var currentFloor = _currentMapConfig.Floors.FirstOrDefault(f => string.Equals(f.LayerId, _currentFloorId, StringComparison.OrdinalIgnoreCase));

			var order = currentFloor?.Order ?? 0;

			if (TxtMiniMapFloor != null)

			{

				TxtMiniMapFloor.Text = order < 0 ? $"B{Math.Abs(order)}" : (order == 0 ? "G" : $"{order + 1}F");

				TxtMiniMapFloor.ToolTip = currentFloor?.DisplayName ?? $"Floor {order}";

			}

		}

		else

		{

			if (PnlFloorSelector != null) PnlFloorSelector.Visibility = Visibility.Collapsed;

		}

	}



	private void BtnFloorDown_Click(object sender, RoutedEventArgs e)

	{

		if (_currentMapConfig?.Floors == null || _currentMapConfig.Floors.Count <= 1) return;

		var sorted = _currentMapConfig.Floors.OrderBy(f => f.Order).ToList();

		int idx = sorted.FindIndex(f => string.Equals(f.LayerId, _currentFloorId, StringComparison.OrdinalIgnoreCase));

		if (idx > 0)

		{

			var target = sorted[idx - 1];

			_currentFloorId = target.LayerId;

			_trackerService?.SetCurrentFloor(target.LayerId);

			UpdateFloorUI();

			if (!string.IsNullOrEmpty(_currentMapConfig.SvgFileName))

			{

				string text = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Maps", _currentMapConfig.SvgFileName);

				if (File.Exists(text)) LoadSvgMap(text, resetView: false);

			}

			LoadMarkersAsync();

		}

	}



	private void BtnFloorUp_Click(object sender, RoutedEventArgs e)

	{

		if (_currentMapConfig?.Floors == null || _currentMapConfig.Floors.Count <= 1) return;

		var sorted = _currentMapConfig.Floors.OrderBy(f => f.Order).ToList();

		int idx = sorted.FindIndex(f => string.Equals(f.LayerId, _currentFloorId, StringComparison.OrdinalIgnoreCase));

		if (idx >= 0 && idx < sorted.Count - 1)

		{

			var target = sorted[idx + 1];

			_currentFloorId = target.LayerId;

			_trackerService?.SetCurrentFloor(target.LayerId);

			UpdateFloorUI();

			if (!string.IsNullOrEmpty(_currentMapConfig.SvgFileName))

			{

				string text = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Maps", _currentMapConfig.SvgFileName);

				if (File.Exists(text)) LoadSvgMap(text, resetView: false);

			}

			LoadMarkersAsync();

		}

	}









	private void MenuCenterPlayer_Click(object sender, RoutedEventArgs e)



	{



		CenterOnPlayer();



	}







	private void MenuViewMode_Click(object sender, RoutedEventArgs e)



	{



		ToggleViewMode();



	}







	private void MenuClickThrough_Click(object sender, RoutedEventArgs e)



	{



		ToggleClickThrough();



	}







	private void MenuZoomIn_Click(object sender, RoutedEventArgs e)



	{



		ZoomIn();



	}







	private void MenuZoomOut_Click(object sender, RoutedEventArgs e)



	{



		ZoomOut();



	}







	private void MenuResetView_Click(object sender, RoutedEventArgs e)



	{



		ResetViewToFit();



	}







	private void MenuMarkerPreset_Click(object sender, RoutedEventArgs e)



	{



		if (sender is MenuItem { Tag: string tag })



		{



			OverlayMiniMapSettings settings = _settings;



			if (1 == 0)



			{



			}



			MarkerSizePreset markerPreset = ((!(tag == "Small")) ? ((!(tag == "Large")) ? MarkerSizePreset.Normal : MarkerSizePreset.Large) : MarkerSizePreset.Small);



			if (1 == 0)



			{



			}



			settings.MarkerPreset = markerPreset;



			OverlayMiniMapSettings settings2 = _settings;



			MarkerSizePreset markerPreset2 = _settings.MarkerPreset;



			if (1 == 0)



			{



			}



			double customMarkerScale = markerPreset2 switch



			{



				MarkerSizePreset.Small => 0.7, 



				MarkerSizePreset.Large => 1.4, 



				_ => 1.0, 



			};



			if (1 == 0)



			{



			}



			settings2.CustomMarkerScale = customMarkerScale;



			SaveSettings();



			RefreshMarkers();



			SettingsChanged?.Invoke(_settings);



		}



	}







	private void MenuClose_Click(object sender, RoutedEventArgs e)



	{



		Close();



	}







	public void ZoomIn()



	{



		SetZoomLevel(_settings.ZoomLevel * 1.15);



	}







	public void ZoomOut()



	{



		SetZoomLevel(_settings.ZoomLevel / 1.15);



	}







	public void SetZoomLevel(double newZoom)



	{



		double effectiveScale = GetEffectiveScale();



		_settings.ZoomLevel = Math.Clamp(newZoom, 0.5, 4.0);



		if (_currentMapConfig == null)



		{



			UpdateMapView();



			return;



		}



		double viewportWidth = GetViewportWidth();



		double viewportHeight = GetViewportHeight();



		double effectiveScale2 = GetEffectiveScale();



		ScreenPosition validPlayerPosition = GetValidPlayerPosition();



		if (_settings.ViewMode == MiniMapViewMode.PlayerTracking && validPlayerPosition != null)



		{



			_settings.MapOffsetX = viewportWidth / 2.0 - validPlayerPosition.X * effectiveScale2;



			_settings.MapOffsetY = viewportHeight / 2.0 - validPlayerPosition.Y * effectiveScale2;



		}



		else



		{



			double num = viewportWidth / 2.0;



			double num2 = viewportHeight / 2.0;



			if (effectiveScale > 0.0001 && double.IsFinite(effectiveScale))



			{



				double num3 = (num - _settings.MapOffsetX) / effectiveScale;



				double num4 = (num2 - _settings.MapOffsetY) / effectiveScale;



				_settings.MapOffsetX = num - num3 * effectiveScale2;



				_settings.MapOffsetY = num2 - num4 * effectiveScale2;



			}



			else



			{



				double num5 = (double)_currentMapConfig.ImageWidth / 2.0;



				double num6 = (double)_currentMapConfig.ImageHeight / 2.0;



				_settings.MapOffsetX = num - num5 * effectiveScale2;



				_settings.MapOffsetY = num2 - num6 * effectiveScale2;



			}



		}



		var (mapOffsetX, mapOffsetY) = ClampMapOffset(_settings.MapOffsetX, _settings.MapOffsetY);



		_settings.MapOffsetX = mapOffsetX;



		_settings.MapOffsetY = mapOffsetY;



		UpdateMapView();



		UpdateRecenterButtonVisibility();



		SettingsChanged?.Invoke(_settings);



	}







	public void ToggleViewMode()



	{



		_settings.ToggleViewMode();



		if (_settings.ViewMode == MiniMapViewMode.PlayerTracking)



		{



			CenterOnPlayer();



		}



	}







	public void RefreshMap()



	{



		if (!string.IsNullOrEmpty(_currentMapKey))



		{



			LoadMap(_currentMapKey);



		}



	}







	public void RefreshMarkers()



	{



		LoadMarkersAsync();



	}







	public void ApplyNewSettings(OverlayMiniMapSettings newSettings)



	{



		_settings.CopyFrom(newSettings);



		Opacity = Math.Clamp(_settings.Opacity, 0.1, 1.0);



		SetClickThrough(_settings.ClickThrough);



		ScreenPosition validPlayerPosition = GetValidPlayerPosition();



		if (_settings.ViewMode == MiniMapViewMode.PlayerTracking && validPlayerPosition != null)



		{



			CenterOnPlayer(validPlayerPosition);



		}



		else



		{



			UpdateMapView();



		}



		RefreshMarkers();



		UpdateFloatingButtonsVisuals();



		UpdateRecenterButtonVisibility();



	}







	protected override void OnClosed(EventArgs e)



	{



		QuestProgressService.Instance.ProgressChanged -= OnQuestProgressChanged;

		TarkovHelper.Services.Settings.MapSettings.Instance.HiddenQuestsChanged -= OnHiddenQuestsChangedMiniMap;



		ObjectiveProgressService.Instance.ObjectiveProgressChanged -= OnObjectiveProgressChanged;



		UserDataDbService.Instance.CustomMarkersChanged -= OnCustomMarkersChanged;



		if (_trackerService != null)



		{



			_trackerService.PositionUpdated -= OnPositionUpdated;



			_trackerService.MapChanged -= OnMapChanged;



			_trackerService.FloorChanged -= OnFloorChanged;



			_trackerService.PinnedObjectiveChanged -= OnPinnedObjectiveChanged;



		}



		LogMapWatcherService.Instance.MapChanged -= OnLogMapWatcherMapChanged;



		base.OnClosed(e);



	}



}

