using System.Windows;
using TarkovHelper.Models.Map;
using TarkovHelper.Services;

namespace TarkovHelper.Windows.Dialogs;

/// <summary>
/// Overlay settings window
/// </summary>
public partial class OverlaySettingsWindow : Window
{
    private readonly OverlayMiniMapSettings _settings;
    private readonly OverlayMiniMapWindow? _overlayWindow;
    private bool _isInitializing = true;
    
    private enum KeyCaptureMode { None, ZoomIn, ZoomOut }
    private KeyCaptureMode _captureMode = KeyCaptureMode.None;

    /// <summary>
    /// Settings applied event
    /// </summary>
    public event Action<OverlayMiniMapSettings>? SettingsApplied;

    public OverlaySettingsWindow(OverlayMiniMapSettings settings, OverlayMiniMapWindow? overlayWindow)
    {
        InitializeComponent();

        _settings = settings.Clone();
        _overlayWindow = overlayWindow;

        LoadSettings();
        _isInitializing = false;
    }

    private void LoadSettings()
    {
        SliderOpacity.Value = _settings.Opacity * 100;
        SliderZoom.Value = _settings.ZoomLevel * 100;
        SliderMarkerSize.Value = _settings.PlayerMarkerSize * 100;
        if (_settings.MarkerPreset == MarkerSizePreset.Small || _settings.CustomMarkerScale <= 0.75)
            RbMarkerSmall.IsChecked = true;
        else if (_settings.MarkerPreset == MarkerSizePreset.Large || _settings.CustomMarkerScale >= 1.25)
            RbMarkerLarge.IsChecked = true;
        else
            RbMarkerNormal.IsChecked = true;

        RbFixed.IsChecked = _settings.ViewMode == MiniMapViewMode.Fixed;
        RbTracking.IsChecked = _settings.ViewMode == MiniMapViewMode.PlayerTracking;

        ChkClickThrough.IsChecked = _settings.ClickThrough;
        ChkShowExtracts.IsChecked = _settings.ShowExtractMarkers;
        ChkShowQuests.IsChecked = _settings.ShowQuestMarkers;
        ChkActiveQuestsOnly.IsChecked = _settings.ActiveQuestsOnly;
        ChkHideCompletedObjectives.IsChecked = _settings.HideCompletedObjectives;
        QuestSubOptionsPanel.IsEnabled = _settings.ShowQuestMarkers;
        ChkShowCustomMarkers.IsChecked = _settings.ShowCustomMarkers;
        if (ChkShowPmcSpawns != null)
            ChkShowPmcSpawns.IsChecked = _settings.ShowPmcSpawns;
        if (ChkShowPoiMarkers != null)
            ChkShowPoiMarkers.IsChecked = _settings.ShowPoiMarkers;

        UpdateDisplays();
        UpdateKeyDisplays();
    }

    private void UpdateDisplays()
    {
        if (TxtOpacity != null)
            TxtOpacity.Text = $"{(int)SliderOpacity.Value}%";
        if (TxtZoom != null)
            TxtZoom.Text = $"{SliderZoom.Value / 100:F2}x";
        if (TxtMarkerSize != null)
            TxtMarkerSize.Text = $"{SliderMarkerSize.Value / 100:F1}x";

    }

    private void UpdateKeyDisplays()
    {
        if (BtnZoomInKey != null)
        {
            var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey(_settings.ZoomInKey);
            BtnZoomInKey.Content = key.ToString();
        }
        if (BtnZoomOutKey != null)
        {
            var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey(_settings.ZoomOutKey);
            BtnZoomOutKey.Content = key.ToString();
        }
    }

    private void ApplySettings()
    {
        if (_isInitializing) return;

        _settings.Opacity = SliderOpacity.Value / 100.0;
        _settings.ZoomLevel = SliderZoom.Value / 100.0;
        _settings.PlayerMarkerSize = SliderMarkerSize.Value / 100.0;
        if (RbMarkerSmall?.IsChecked == true)
        {
            _settings.MarkerPreset = MarkerSizePreset.Small;
            _settings.CustomMarkerScale = 0.7;
        }
        else if (RbMarkerLarge?.IsChecked == true)
        {
            _settings.MarkerPreset = MarkerSizePreset.Large;
            _settings.CustomMarkerScale = 1.4;
        }
        else
        {
            _settings.MarkerPreset = MarkerSizePreset.Normal;
            _settings.CustomMarkerScale = 1.0;
        }
        _settings.ViewMode = RbTracking.IsChecked == true ? MiniMapViewMode.PlayerTracking : MiniMapViewMode.Fixed;
        _settings.ClickThrough = ChkClickThrough.IsChecked == true;
        _settings.ShowExtractMarkers = ChkShowExtracts.IsChecked == true;
        _settings.ShowQuestMarkers = ChkShowQuests.IsChecked == true;
        _settings.ActiveQuestsOnly = ChkActiveQuestsOnly.IsChecked == true;
        _settings.HideCompletedObjectives = ChkHideCompletedObjectives.IsChecked == true;
        if (QuestSubOptionsPanel != null)
        {
            QuestSubOptionsPanel.IsEnabled = _settings.ShowQuestMarkers;
        }
        _settings.ShowCustomMarkers = ChkShowCustomMarkers.IsChecked == true;
        if (ChkShowPmcSpawns != null)
            _settings.ShowPmcSpawns = ChkShowPmcSpawns.IsChecked == true;
        if (ChkShowPoiMarkers != null)
            _settings.ShowPoiMarkers = ChkShowPoiMarkers.IsChecked == true;

        SettingsApplied?.Invoke(_settings);

        // Apply to overlay window immediately
        ApplyToOverlay();
    }

    private void ApplyToOverlay()
    {
        var overlay = _overlayWindow ?? OverlayMiniMapService.Instance.CurrentOverlayWindow;
        if (overlay == null) return;

        overlay.Dispatcher.Invoke(() =>
        {
            overlay.ApplyNewSettings(_settings);
        });
    }

    #region Event Handlers

    private void SliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateDisplays();
        ApplySettings();
    }

    private void SliderZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateDisplays();
        ApplySettings();
    }

    private void SliderMarkerSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateDisplays();
        ApplySettings();
    }

    private void MarkerPreset_Changed(object sender, RoutedEventArgs e)
    {
        ApplySettings();
        var overlay = _overlayWindow ?? OverlayMiniMapService.Instance.CurrentOverlayWindow;
        overlay?.RefreshMarkers();
    }

    private void ShowPmcSpawns_Changed(object sender, RoutedEventArgs e)
    {
        ApplySettings();
    }

    private void ShowPoiMarkers_Changed(object sender, RoutedEventArgs e)
    {
        ApplySettings();
    }

    private void ViewMode_Changed(object sender, RoutedEventArgs e)
    {
        ApplySettings();
    }

    private void ShowExtracts_Changed(object sender, RoutedEventArgs e)
    {
        ApplySettings();
        _overlayWindow?.RefreshMarkers();
    }

    private void ShowQuests_Changed(object sender, RoutedEventArgs e)
    {
        if (QuestSubOptionsPanel != null)
        {
            QuestSubOptionsPanel.IsEnabled = ChkShowQuests.IsChecked == true;
        }
        ApplySettings();
        _overlayWindow?.RefreshMarkers();
    }

    private void QuestSubOptions_Changed(object sender, RoutedEventArgs e)
    {
        ApplySettings();
        _overlayWindow?.RefreshMarkers();
    }

    private void ShowCustomMarkers_Changed(object sender, RoutedEventArgs e)
    {
        ApplySettings();
        _overlayWindow?.RefreshMarkers();
    }

    private void ClickThrough_Changed(object sender, RoutedEventArgs e)
    {
        ApplySettings();
        _overlayWindow?.SetClickThrough(ChkClickThrough.IsChecked == true);
    }

    private void BtnCenterPlayer_Click(object sender, RoutedEventArgs e)
    {
        _overlayWindow?.CenterOnPlayer();
        _settings.ViewMode = MiniMapViewMode.PlayerTracking;
        RbTracking.IsChecked = true;
        ApplySettings();
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetToDefaults();
        _isInitializing = true;
        LoadSettings();
        _isInitializing = false;
        ApplySettings();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnZoomInKey_Click(object sender, RoutedEventArgs e)
    {
        _captureMode = KeyCaptureMode.ZoomIn;
        BtnZoomInKey.Content = "Press a key...";
        BtnZoomOutKey.Content = System.Windows.Input.KeyInterop.KeyFromVirtualKey(_settings.ZoomOutKey).ToString();
        this.Focus();
    }

    private void BtnZoomOutKey_Click(object sender, RoutedEventArgs e)
    {
        _captureMode = KeyCaptureMode.ZoomOut;
        BtnZoomOutKey.Content = "Press a key...";
        BtnZoomInKey.Content = System.Windows.Input.KeyInterop.KeyFromVirtualKey(_settings.ZoomInKey).ToString();
        this.Focus();
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (_captureMode != KeyCaptureMode.None)
        {
            e.Handled = true;
            
            // ESC키로 취소
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                _captureMode = KeyCaptureMode.None;
                UpdateKeyDisplays();
                return;
            }

            // 시스템 키인 경우 (예: F10 등) ImeProcessed나 System 등을 확인
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
            
            if (_captureMode == KeyCaptureMode.ZoomIn)
            {
                _settings.ZoomInKey = vk;
            }
            else if (_captureMode == KeyCaptureMode.ZoomOut)
            {
                _settings.ZoomOutKey = vk;
            }

            _captureMode = KeyCaptureMode.None;
            UpdateKeyDisplays();
            ApplySettings();
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    #endregion
}
