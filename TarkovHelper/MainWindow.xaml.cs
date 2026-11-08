using System.Windows.Controls.Primitives;
using System.Collections.ObjectModel;

using System.Diagnostics;

using System.IO;

using System.Runtime.InteropServices;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using System.Windows.Interop;

using System.Windows.Media;

using System.Windows.Media.Animation;

using TarkovHelper.Debug;

using TarkovHelper.Models;

using TarkovHelper.Pages;

using TarkovHelper.Pages.Map;

using TarkovHelper.Services;

using TarkovHelper.Services.Logging;

using TarkovHelper.Windows;

using TarkovHelper.Windows.Dialogs;

using TarkovHelper.Models.DataAudit;



namespace TarkovHelper;



public partial class MainWindow : Window

{

    private static readonly ILogger _log = Log.For<MainWindow>();

    private readonly LocalizationService _loc = LocalizationService.Instance;

    private readonly HideoutProgressService _hideoutProgressService = HideoutProgressService.Instance;

    private readonly SettingsService _settingsService = SettingsService.Instance;

    private readonly LogSyncService _logSyncService = LogSyncService.Instance;

    private bool _isLoading;

    private DashboardPage? _dashboardPage;

    private StorylinePage? _storylinePage;

    private QuestListPage? _questListPage;

    private HideoutPage? _hideoutPage;

    private ItemsPage? _itemsPage;

    private List<HideoutModule>? _hideoutModules;

    private bool _isFullScreen;

    private FileSystemWatcher? _fontWatcher;

    private System.Windows.Forms.NotifyIcon? _notifyIcon;

    private bool _isExplicitExit = false;



    // Public accessors for child controls and navigation

    public static MainWindow? Instance { get; private set; }

    public QuestListPage? QuestListPage => _questListPage;

    public ItemsPage? ItemsPage => _itemsPage;



    // Backward compatibility aliases for legacy internal/external calls

    public RadioButton TabQuests => TabTasks;

    public RadioButton TabItems => TabNeededItems;



    // Windows API for dark title bar

    [DllImport("dwmapi.dll", PreserveSig = true)]

    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);



    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;



    public MainWindow()

    {

        Instance = this;

        _isLoading = true; // InitializeComponent 중 발생하는 ProfileRadio_Checked 이벤트를 차단하기 위해 미리 true로 설정합니다.

        InitializeComponent();

        if (ProfilePopup != null)
        {
            ProfilePopup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            {
                var point = new Point(targetSize.Width - popupSize.Width, targetSize.Height + 4);
                return new[] { new CustomPopupPlacement(point, PopupPrimaryAxis.Horizontal) };
            };
        }

        _loc.LanguageChanged += OnLanguageChanged;

        _settingsService.PlayerLevelChanged += OnPlayerLevelChanged;

        _settingsService.ScavRepChanged += OnScavRepChanged;

        _settingsService.DspDecodeCountChanged += OnDspDecodeCountChanged;

        _settingsService.HasEodEditionChanged += OnEditionChanged;

        _settingsService.HasUnheardEditionChanged += OnEditionChanged;
        _settingsService.PlayerFactionChanged += OnPlayerFactionChanged;

        _settingsService.PrestigeLevelChanged += OnPrestigeLevelChanged;

        _settingsService.FontFamilyNameChanged += OnFontFamilyNameChanged;

        ProfileService.Instance.ProfileChanged += OnProfileChanged;



        // Apply dark title bar

        SourceInitialized += (s, e) => EnableDarkTitleBar();

    }



    private void OnProfileChanged(object? sender, ProfileType e)

    {

        // 데이터 -> UI 동기화

        _isLoading = true; // 이벤트 루프 방지

        try

        {

            if (e == ProfileType.Pve)

                RadioPve.IsChecked = true;

            else if (e == ProfileType.Seasonal)

                RadioSeasonal.IsChecked = true;

            else

                RadioPvp.IsChecked = true;

        }

        finally

        {

            _isLoading = false;

        }

    }



    private void EnableDarkTitleBar()

    {

        var hwnd = new WindowInteropHelper(this).Handle;

        var useDarkMode = 1;

        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));

    }



    private void OnLanguageChanged(object? sender, AppLanguage e)

    {

        UpdateAllLocalizedText();

    }



    private void UpdateAllLocalizedText()

    {

        TxtWelcome.Text = _loc.Welcome;

    }



    private async void Window_Loaded(object sender, RoutedEventArgs e)

    {

        _isLoading = true;



        // 1. 데이터베이스(tarkov_data.db) 존재 여부 확인 및 선제적 다운로드

        var dbPath = DatabaseUpdateService.Instance.DatabasePath;

        if (!File.Exists(dbPath))

        {

            LoadingOverlay.Visibility = Visibility.Visible;

            LoadingStatusText.Text = "Downloading data. Please wait...";

            TabContentArea.Visibility = Visibility.Collapsed; // 다운로드 중에는 탭 숨김

            

            try

            {

                // await DatabaseUpdateService.Instance.CheckAndUpdateAsync();

            }

            finally

            {

                LoadingOverlay.Visibility = Visibility.Collapsed;

            }

        }



        // 2. 서비스 리셋 및 초기화 (병렬 비동기 부트스트랩)

        await Task.Run(async () =>

        {

            await DatabaseBootstrapService.Instance.EnsureDatabaseSchemaAsync();

            await UserDataDbService.Instance.InitializeAsync();

        });

        

        UpdatePlayerLevelUI();

                UpdateDspDecodeUI();

        UpdateEditionUI();

        UpdatePrestigeLevelUI();

        UpdateFactionUI();

        UpdateAllLocalizedText();



        // 3. 현재 프로필 로드 및 데이터 무결성 검증 (내부적으로 데이터 로드 대기 수행)

        var currentProfile = ProfileService.Instance.CurrentProfile;

        OnProfileChanged(this, currentProfile);



        // RefreshCurrentProfileDataAsync 호출로 모든 페이지 객체 생성 및 데이터 로드 실천

        await RefreshCurrentProfileDataAsync();



        // 4. 기타 백그라운드 서비스 시작

        InitializeFontSettings();

        ApplyFont(_settingsService.FontFamilyName);

        StartDatabaseUpdateService();

        AutoStartLogMonitoring();



        RadioPvp.Checked += ProfileRadio_Checked;

        RadioSeasonal.Checked += ProfileRadio_Checked;

        RadioPve.Checked += ProfileRadio_Checked;



        InitializeTrayIcon();



        _isLoading = false;

    }

    #region Font Settings



    private void InitializeFontSettings()

    {

        try

        {

            var fontDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts");

            if (!Directory.Exists(fontDir))

            {

                Directory.CreateDirectory(fontDir);

                LogFontDebug("Fonts directory created.");

            }



            var fontFiles = Directory.GetFiles(fontDir, "*.*")

                .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || 

                            f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))

                .ToList();



            var fontItems = new List<FontItem>();

            var koCulture = System.Globalization.CultureInfo.GetCultureInfo("ko-KR");

            var enCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");

            var koXmlLang = System.Windows.Markup.XmlLanguage.GetLanguage("ko-KR");

            var enXmlLang = System.Windows.Markup.XmlLanguage.GetLanguage("en-US");



            foreach (var file in fontFiles)

            {

                string fileName = Path.GetFileName(file);

                string? internalName = null;

                

                try

                {

                    // Phase 1: GlyphTypeface

                    var glyphTypeface = new System.Windows.Media.GlyphTypeface(new Uri(file));

                    if (glyphTypeface.FamilyNames != null)

                    {

                        if (glyphTypeface.FamilyNames.TryGetValue(koCulture, out var koName))

                            internalName = koName;

                        else if (glyphTypeface.FamilyNames.TryGetValue(enCulture, out var enName))

                            internalName = enName;

                        else

                            internalName = glyphTypeface.FamilyNames.Values.FirstOrDefault();

                    }

                }

                catch (Exception ex)

                {

                    LogFontDebug($"DEBUG: GlyphTypeface failed for {fileName}: {ex.Message}");

                }



                // Phase 2: Typeface fallback

                if (string.IsNullOrEmpty(internalName))

                {

                    try

                    {

                        var typefaces = System.Windows.Media.Fonts.GetTypefaces(new Uri(file));

                        var firstTypeface = typefaces.FirstOrDefault();

                        if (firstTypeface?.FontFamily?.FamilyNames != null)

                        {

                            var names = firstTypeface.FontFamily.FamilyNames;

                            if (names.TryGetValue(koXmlLang, out var koName))

                                internalName = koName;

                            else if (names.TryGetValue(enXmlLang, out var enName))

                                internalName = enName;

                            else

                                internalName = names.Values.FirstOrDefault();

                        }

                    }

                    catch (Exception ex)

                    {

                        LogFontDebug($"DEBUG: GetTypefaces failed for {fileName}: {ex.Message}");

                    }

                }



                // Phase 3: Filename fallback

                if (string.IsNullOrEmpty(internalName))

                {

                    internalName = Path.GetFileNameWithoutExtension(file);

                }



                if (!string.IsNullOrEmpty(internalName))

                {

                    fontItems.Add(new FontItem 

                    { 

                        InternalName = internalName, 

                        FileName = fileName,

                        DisplayName = fileName // Use filename as display name per user request

                    });

                }

            }



            // Sort by DisplayName

            fontItems = fontItems.OrderBy(i => i.DisplayName).ToList();

            

            // Add internal default at the very top

            fontItems.Insert(0, new FontItem 

            { 

                DisplayName = "Default (Segoe UI)", 

                InternalName = "Segoe UI", 

                FileName = "" 

            });



            LogFontDebug($"Scan complete. Found {fontItems.Count} font options (including default).");



            ComboFontFamily.SelectionChanged -= ComboFontFamily_SelectionChanged;

            ComboFontFamily.ItemsSource = null;

            ComboFontFamily.ItemsSource = fontItems;



            // set current selection

            var savedSetting = _settingsService.FontFamilyName;

            var (savedIname, savedFname) = FontItem.ParseSetting(savedSetting);



            // If savedIname is "Maplestory" and savedFname is empty, treat it as the default "Segoe UI"

            if (string.Equals(savedIname, "Maplestory", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(savedFname))

            {

                savedIname = "Segoe UI";

            }



            FontItem? toSelect = null;

            if (!string.IsNullOrEmpty(savedFname))

            {

                toSelect = fontItems.FirstOrDefault(i => i.FileName.Equals(savedFname, StringComparison.OrdinalIgnoreCase));

            }

            

            if (toSelect == null && !string.IsNullOrEmpty(savedIname))

            {

                // Try to match internal name

                toSelect = fontItems.FirstOrDefault(i => i.InternalName.Equals(savedIname, StringComparison.OrdinalIgnoreCase));

            }



            // If still nothing matches (or it's the first run), default to our default entry

            if (toSelect == null)

            {

                toSelect = fontItems.FirstOrDefault(i => (i.InternalName == "Segoe UI" || i.InternalName == "Maplestory") && string.IsNullOrEmpty(i.FileName));

            }



            if (toSelect != null)

            {

                ComboFontFamily.SelectedItem = toSelect;

            }

            // Removed: else if (fontItems.Count > 0) { ComboFontFamily.SelectedIndex = 0; }

            // This prevents accidental auto-selection of discovered files on first run.



            ComboFontFamily.SelectionChanged += ComboFontFamily_SelectionChanged;



            // Initialize FileSystemWatcher if not already done

            if (_fontWatcher == null)

            {

                _fontWatcher = new FileSystemWatcher(fontDir)

                {

                    Filter = "*.*", 

                    EnableRaisingEvents = true,

                    NotifyFilter = NotifyFilters.FileName | 

                                   NotifyFilters.DirectoryName | 

                                   NotifyFilters.Attributes | 

                                   NotifyFilters.Size | 

                                   NotifyFilters.LastWrite

                };



                _fontWatcher.Created += (s, e) => { LogFontDebug($"Watcher: Created {e.Name}"); RequestFontRefresh(); };

                _fontWatcher.Deleted += (s, e) => { LogFontDebug($"Watcher: Deleted {e.Name}"); RequestFontRefresh(); };

                _fontWatcher.Renamed += (s, e) => { LogFontDebug($"Watcher: Renamed {e.OldName} -> {e.Name}"); RequestFontRefresh(); };

                _fontWatcher.Changed += (s, e) => { LogFontDebug($"Watcher: Changed {e.Name}"); RequestFontRefresh(); };



                LogFontDebug("FileSystemWatcher initialized and started.");

            }

        }

        catch (Exception ex)

        {

            _log.Error($"Failed to initialize fonts: {ex.Message}");

            LogFontDebug($"ERR: {ex.Message}");

        }

    }



    private void LogFontDebug(string message)

    {

        try

        {

            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "font_debug.log");

            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");

            _log.Debug($"FontDebug: {message}");

        }

        catch { }

    }



    private void BtnRefreshFonts_Click(object sender, RoutedEventArgs e)

    {

        InitializeFontSettings();

    }



    private System.Threading.Timer? _fontRefreshTimer;



    private void RequestFontRefresh()

    {

        // Debounce refresh requests

        _fontRefreshTimer?.Dispose();

        _fontRefreshTimer = new System.Threading.Timer(_ =>

        {

            Dispatcher.Invoke(() => InitializeFontSettings());

        }, null, 500, Timeout.Infinite);

    }



    private void ApplyFont(string setting)

    {

        try

        {

            var (fontFamilyName, fileName) = FontItem.ParseSetting(setting);

            if (string.IsNullOrEmpty(fontFamilyName)) return;



            // Treat legacy "Maplestory" without a file as default "Segoe UI"

            if (string.Equals(fontFamilyName, "Maplestory", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(fileName))

            {

                fontFamilyName = "Segoe UI";

            }



            var fontDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts");

            FontFamily? family = null;



            if (!string.IsNullOrEmpty(fileName) && Directory.Exists(fontDir))

            {

                var filePath = Path.Combine(fontDir, fileName);

                if (File.Exists(filePath))

                {

                    // More robust URI construction for local font files

                    var folderUri = new Uri(fontDir.EndsWith("/") ? fontDir : fontDir + "/");

                    family = new FontFamily(folderUri, $"./{fileName}#{fontFamilyName}");

                    LogFontDebug($"Constructed FontFamily URI: {folderUri} with source ./{fileName}#{fontFamilyName}");

                }

            }



            if (family == null)

            {

                if (fontFamilyName.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase) ||

                    fontFamilyName.Equals("Maplestory", StringComparison.OrdinalIgnoreCase))

                {

                    family = new FontFamily("Segoe UI, Bahnschrift, Malgun Gothic");

                }

                else

                {

                    family = new FontFamily(fontFamilyName);

                }

            }



            Application.Current.Resources["MaplestoryFont"] = family;

            _log.Debug($"Applied font: {fontFamilyName} (from {fileName ?? "system"})");

        }

        catch (Exception ex)

        {

            _log.Error($"Failed to apply font {setting}: {ex.Message}");

        }

    }



    private void ComboFontFamily_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (_isLoading || ComboFontFamily.SelectedItem == null) return;



        if (ComboFontFamily.SelectedItem is FontItem selectedFontItem)

        {

            _settingsService.FontFamilyName = selectedFontItem.ToSettingString();

        }

    }



    private void OnFontFamilyNameChanged(object? sender, string newFontName)

    {

        Dispatcher.Invoke(() =>

        {

            ApplyFont(newFontName);

        });

    }



    #endregion





    /// <summary>

    /// 데이터베이스 업데이트 서비스 시작

    /// </summary>

    private void StartDatabaseUpdateService()

    {

        var dbUpdateService = DatabaseUpdateService.Instance;



        // 업데이트 완료 이벤트 구독 (UI 새로고침용)

        dbUpdateService.DatabaseUpdated += OnDatabaseUpdated;



        // 백그라운드 업데이트 체크 시작 (5분마다)

        // dbUpdateService.StartBackgroundUpdates();



        _log.Info("Database update service started");

    }



    /// <summary>

    /// 데이터베이스 업데이트 완료 시 UI 새로고침

    /// </summary>

    private void OnDatabaseUpdated(object? sender, EventArgs e)

    {

        _log.Info("Database updated, all services will reload data automatically");



        // 서비스들이 이미 DatabaseUpdated 이벤트를 구독하고 있으므로

        // 각 서비스의 RefreshAsync()가 자동으로 호출됨

        // UI 페이지들은 서비스의 새로운 데이터를 사용하게 됨



        // 필요시 사용자에게 알림 표시 가능

        Dispatcher.Invoke(() =>

        {

            // 상태 표시줄이나 토스트 메시지로 업데이트 완료 알림 가능

            _log.Debug("Database update notification displayed");

        });

    }



    /// <summary>

    /// Automatically start log monitoring on app launch if enabled

    /// </summary>

    private void AutoStartLogMonitoring()

    {

        if (!_settingsService.LogMonitoringEnabled)

            return;



        // Try to get log folder path (auto-detect if not set)

        var logPath = _settingsService.LogFolderPath;



        // If no path and auto-detect failed, try to save auto-detected path

        if (string.IsNullOrEmpty(logPath))

        {

            logPath = _settingsService.AutoDetectLogFolder();

            if (!string.IsNullOrEmpty(logPath))

            {

                _settingsService.LogFolderPath = logPath;

            }

        }



        if (!string.IsNullOrEmpty(logPath) && Directory.Exists(logPath))

        {

            _logSyncService.StartMonitoring(logPath);

            _logSyncService.QuestEventDetected -= OnQuestEventDetected;

            _logSyncService.QuestEventDetected += OnQuestEventDetected;

            _log.Info($"Auto-started log monitoring: {logPath}");

        }



        UpdateQuestSyncUI();

    }



    /// <summary>

    /// Load and show quest data from DB

    /// </summary>

    private async Task CheckAndRefreshDataAsync()

    {

        // Quest data is now bundled in tarkov_data.db, load directly

        await LoadAndShowQuestListAsync();

    }



    /// <summary>

    /// Show loading overlay with blur effect

    /// </summary>

    public void ShowLoadingOverlay(string status = "Loading...")

    {

        LoadingStatusText.Text = status;

        LoadingOverlay.Visibility = Visibility.Visible;



        var blurAnimation = new DoubleAnimation(0, 4, TimeSpan.FromMilliseconds(50));

        BlurEffect.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnimation);

    }



    /// <summary>

    /// Hide loading overlay

    /// </summary>

    public void HideLoadingOverlay()

    {

        var blurAnimation = new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(50));

        blurAnimation.Completed += (s, e) =>

        {

            LoadingOverlay.Visibility = Visibility.Collapsed;

        };

        BlurEffect.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnimation);

    }



    /// <summary>

    /// Update loading status text

    /// </summary>

    public void UpdateLoadingStatus(string status)

    {

        LoadingStatusText.Text = status;

    }



    /// <summary>

    /// 마이그레이션 진행 상황 업데이트

    /// </summary>

    private void OnMigrationProgress(string message)

    {

        // BeginInvoke를 사용하여 비동기로 UI 업데이트 (데드락 방지)

        Dispatcher.BeginInvoke(() => UpdateLoadingStatus(message));

    }



    /// <summary>

    /// Load task data and show Quest List page

    /// </summary>

    private async Task LoadAndShowQuestListAsync()

    {

        var progressService = QuestProgressService.Instance;

        var migrationService = ConfigMigrationService.Instance;



        List<TarkovTask>? tasks = null;

        ConfigMigrationService.MigrationResult? migrationResult = null;



        // 자동 마이그레이션 필요 여부 확인 (3.5 버전 등에서 업데이트 시)

        bool needsMigration = migrationService.NeedsAutoMigration();

        if (needsMigration)

        {

            ShowLoadingOverlay("Migrating data...");



            try

            {

                var progress = new Progress<string>(message =>

                {

                    Dispatcher.BeginInvoke(() => UpdateLoadingStatus(message));

                });



                // ConfigMigrationService를 사용하여 마이그레이션 수행

                migrationResult = await migrationService.MigrateFromCurrentConfigAsync(progress);



                // 마이그레이션 결과 로깅

                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "migration_log.txt");

                var logContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Migration completed\n" +

                                 $"  Success: {migrationResult?.Success}\n" +

                                 $"  QuestProgress: {migrationResult?.QuestProgressCount}\n" +

                                 $"  HideoutProgress: {migrationResult?.HideoutProgressCount}\n" +

                                 $"  ItemInventory: {migrationResult?.ItemInventoryCount}\n" +

                                 $"  Settings: {migrationResult?.SettingsCount}\n" +

                                 $"  TotalCount: {migrationResult?.TotalCount}\n" +

                                 $"  Warnings: {string.Join(", ", migrationResult?.Warnings ?? [])}\n" +

                                 $"  Errors: {string.Join(", ", migrationResult?.Errors ?? [])}\n\n";

                File.AppendAllText(logPath, logContent);

            }

            catch (Exception ex)

            {

                // 마이그레이션 실패 시 로그 파일에 기록

                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "migration_error.log");

                File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Migration failed:\n{ex}\n\nStack trace:\n{ex.StackTrace}");

                _log.Error($"Migration failed: {ex.Message}");

            }

            finally

            {

                // LoadingOverlay만 숨기고, Blur는 마이그레이션 결과 팝업 표시 여부에 따라 처리

                LoadingOverlay.Visibility = Visibility.Collapsed;

            }

        }



        try

        {

            // 현재 프로필 정보를 명시적으로 전달하여 각 서비스 초기화

            var currentProfile = ProfileService.Instance.CurrentProfile;



            // DB에서 퀘스트 데이터 로드

            if (await progressService.InitializeFromDbAsync(currentProfile))

            {

                tasks = progressService.AllTasks.ToList();

                _log.Debug($"Loaded {tasks.Count} quests from DB for {currentProfile}");

            }



            // ObjectiveProgressService 비동기 로드

            await ObjectiveProgressService.Instance.LoadObjectiveProgressAsync(currentProfile);



            // NeededItemProgressService 비동기 로드

            await NeededItemProgressService.Instance.LoadProgressAsync(currentProfile);



            // ItemInventoryService 비동기 로드

            await ItemInventoryService.Instance.LoadInventoryAsync();

        }

        catch (Exception ex)

        {

            _log.Error($"Failed to load quests: {ex.Message}");

        }



        // Load hideout data from DB

        var hideoutDbService = HideoutDbService.Instance;

        var hideoutLoaded = await hideoutDbService.LoadStationsAsync();

        _log.Debug($"Hideout DB loaded: {hideoutLoaded}, StationCount: {hideoutDbService.StationCount}");

        if (hideoutLoaded)

        {

            _hideoutModules = hideoutDbService.AllStations.ToList();

            _log.Debug($"Hideout modules count: {_hideoutModules.Count}");

            

            // HideoutProgressService 비동기 초기화

            await HideoutProgressService.Instance.InitializeAsync(_hideoutModules);

        }

        else

        {

            _log.Warning($"Hideout loading failed. DB exists: {hideoutDbService.DatabaseExists}");

        }



        _log.Debug($"Tasks count: {tasks?.Count ?? 0}");



        // Log diagnostic info to file

        try

        {

            var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_log.txt");

            var logContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Startup Diagnostics\n" +

                             $"  Hideout DB Loaded: {hideoutLoaded}\n" +

                             $"  Hideout Stations: {hideoutDbService.StationCount}\n" +

                             $"  Hideout Modules: {_hideoutModules?.Count ?? 0}\n" +

                             $"  Tasks Count: {tasks?.Count ?? 0}\n" +

                             $"  Database Path: {hideoutDbService.DatabaseExists}\n\n";

            System.IO.File.AppendAllText(logPath, logContent);

        }

        catch { /* Ignore logging errors */ }



        if (tasks != null && tasks.Count > 0)

        {

            // Initialize quest graph service for dependency tracking

            QuestGraphService.Instance.Initialize(tasks);



            // Initialize hideout progress service

            if (_hideoutModules != null && _hideoutModules.Count > 0)

            {

                _hideoutProgressService.Initialize(_hideoutModules);

            }



            // Check if pages already exist (refresh scenario)

            if (_questListPage != null)

            {

                // Reload data in existing pages to pick up new translations

                await _questListPage.ReloadDataAsync();

            }

            else

            {

                // Create pages for the first time

                _questListPage = new QuestListPage();

            }



            // Debug: Show hideout module status

            _log.Debug($"Creating HideoutPage: modules={_hideoutModules?.Count ?? 0}");

            _hideoutPage = _hideoutModules != null && _hideoutModules.Count > 0

                ? new HideoutPage()

                : null;

            _log.Debug($"HideoutPage created: {_hideoutPage != null}");

            _dashboardPage = new DashboardPage();

            _itemsPage = new ItemsPage();



            // Show tab area with Tasks selected

            TxtWelcome.Visibility = Visibility.Collapsed;

            TabContentArea.Visibility = Visibility.Visible;

            TabTasks.IsChecked = true;

            PageContent.Content = _questListPage;



            if (_itemsPage != null)

            {

                Dispatcher.InvokeAsync(async () =>

                {

                    try

                    {

                        await _itemsPage.ViewModel.LoadCardsAsync();

                    }

                    catch { }

                }, System.Windows.Threading.DispatcherPriority.Background);

            }

        }

        else

        {

            TxtWelcome.Text = "No quest data available. Please refresh data.";

            TxtWelcome.Visibility = Visibility.Visible;

            TabContentArea.Visibility = Visibility.Collapsed;

        }



        // 마이그레이션 결과가 있으면 팝업 표시 (자동 마이그레이션 후)

        if (migrationResult != null && migrationResult.TotalCount > 0)

        {

            ShowMigrationResultDialog(migrationResult);

        }

        else if (needsMigration)

        {

            // 마이그레이션이 필요했지만 결과가 없는 경우 Blur 해제

            var blurAnimation = new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(50));

            BlurEffect.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnimation);

        }

    }



    /// <summary>

    /// 프로필 라디오 버튼 변경 핸들러

    /// </summary>

    private async void ProfileRadio_Checked(object sender, RoutedEventArgs e)

    {

        if (_isLoading) return;



        ProfileType newProfile;
        if (sender == RadioPve)
            newProfile = ProfileType.Pve;
        else if (sender == RadioSeasonal)
            newProfile = ProfileType.Seasonal;
        else
            newProfile = ProfileType.Pvp;

        

        if (ProfileService.Instance.CurrentProfile != newProfile)

        {

            ProfileService.Instance.CurrentProfile = newProfile;

            await RefreshCurrentProfileDataAsync();

        }

    }



    /// <summary>

    /// 현재 프로필 데이터를 기반으로 모든 서비스 및 UI 새로고침

    /// </summary>

    private async Task RefreshCurrentProfileDataAsync()

    {

        ShowLoadingOverlay($"Loading {ProfileService.Instance.CurrentProfile.ToString().ToUpper()} data...");



        try

        {

            var currentProfile = ProfileService.Instance.CurrentProfile;

            

            // 1. 서비스들을 물리적으로 리셋

            QuestProgressService.ResetInstance();

            ItemInventoryService.ResetInstance();

            HideoutProgressService.ResetInstance();



            // 2. 백엔드 서비스 리셋 및 데이터 다시 로드

            _settingsService.ReloadSettings();

            await QuestProgressService.Instance.InitializeFromDbAsync(currentProfile);

            

            // [중요] 은신처 데이터 DB로부터 명시적 로드 및 주입

            await HideoutDbService.Instance.LoadStationsAsync();

            await HideoutProgressService.Instance.InitializeAsync(HideoutDbService.Instance.AllStations.ToList());



            await HideoutProgressService.Instance.ReloadProgressAsync();

            await ItemInventoryService.Instance.InitializeAsync();

            // 서브 목표 및 아이템 부분 납품 진행 상태 로드
            await NeededItemProgressService.Instance.LoadProgressAsync(currentProfile);
            await ObjectiveProgressService.Instance.LoadObjectiveProgressAsync(currentProfile);



            // 3. [핵심] 데이터 무결성 검증 루프 (최대 3초)

            // 퀘스트와 은신처 데이터가 모두 채워질 때까지 기다려 화이트아웃 방지

            int retryCount = 0;

            while ((QuestProgressService.Instance.AllTasks.Count == 0 || 

                    HideoutProgressService.Instance.AllModules.Count == 0) && retryCount < 30)

            {

                await Task.Delay(100);

                retryCount++;

                // 1초마다 재시도 강제 호출

                {

                    await QuestProgressService.Instance.InitializeFromDbAsync(currentProfile);

                    await HideoutDbService.Instance.LoadStationsAsync();

                    await HideoutProgressService.Instance.InitializeAsync(HideoutDbService.Instance.AllStations.ToList());

                    await HideoutProgressService.Instance.ReloadProgressAsync();

                }

            }



            _log.Info($"Data verification complete. Quests: {QuestProgressService.Instance.AllTasks.Count}, Retry: {retryCount}");



            // 4. 퀘스트 그래프 서비스 초기화 (의존성 추적 및 카파 게이지용)

            QuestGraphService.Instance.Initialize(QuestProgressService.Instance.AllTasks.ToList());



            // 5. Lazy-create only the active tab page immediately; reset others to null so they instantiate on demand

            _dashboardPage = null;

            _storylinePage = null;

            _questListPage = null;

            _itemsPage = null;

            _hideoutPage = null;



            if (TabDashboard.IsChecked == true)

            {

                _dashboardPage = new DashboardPage();

                PageContent.Content = _dashboardPage;

            }

            else if (TabStoryline.IsChecked == true)

            {

                _storylinePage = new StorylinePage();

                PageContent.Content = _storylinePage;

            }

            else if (TabTasks.IsChecked == true)

            {

                _questListPage = new QuestListPage();

                PageContent.Content = _questListPage;

            }

            else if (TabNeededItems.IsChecked == true)

            {

                _itemsPage = new ItemsPage();

                PageContent.Content = _itemsPage;

            }

            else if (TabHideout.IsChecked == true)

            {

                var hideoutModules = HideoutProgressService.Instance.AllModules;

                _hideoutPage = (hideoutModules != null && hideoutModules.Count > 0) ? new HideoutPage() : null;

                PageContent.Content = _hideoutPage;

            }



            UpdatePlayerLevelUI();

            UpdateDspDecodeUI();

            UpdateEditionUI();

            UpdatePrestigeLevelUI();

            UpdateFactionUI();

            

            TabContentArea.Visibility = Visibility.Visible;

            TxtWelcome.Visibility = Visibility.Collapsed;



            // Pre-warm background data caches off the UI thread so switching to Tasks or Needed Items is instant

            _ = Task.Run(() =>

            {

                try

                {

                    NeededKeysService.Instance.LoadTrackerKeysFromResources();

                    TaskImpactEngine.Instance.GetAllTaskImpacts();

                    TarkovHelper.ViewModels.NeededItemsViewModel.PrewarmCache();

                }

                catch { }

            });

        }

        catch (Exception ex)

        {

            _log.Error($"Profile refresh failed: {ex.Message}");

        }

        finally

        {

            HideLoadingOverlay();

        }

    }



    /// <summary>

    /// Handle tab selection change

    /// </summary>

    private void Tab_Checked(object sender, RoutedEventArgs e)

    {

        if (_isLoading) return;



        if (sender == TabDashboard)

        {

            _dashboardPage ??= new DashboardPage();

            PageContent.Content = _dashboardPage;

            _dashboardPage.Refresh();

        }

        else if (sender == TabStoryline)

        {

            _storylinePage ??= new StorylinePage();

            PageContent.Content = _storylinePage;

        }

        else if (sender == TabTasks)

        {

            _questListPage ??= new QuestListPage();

            PageContent.Content = _questListPage;

        }

        else if (sender == TabNeededItems)

        {

            _itemsPage ??= new ItemsPage();

            PageContent.Content = _itemsPage;

        }

        else if (sender == TabHideout)

        {

            if (_hideoutPage == null)

            {

                // 탭 전환 시점에 페이지가 없으면(데이터 로딩 지연 등) 다시 시도

                var modules = HideoutProgressService.Instance.AllModules;

                if (modules != null && modules.Count > 0)

                    _hideoutPage = new HideoutPage();

            }



            if (_hideoutPage != null)

            {

                PageContent.Content = _hideoutPage;

            }

            else

            {

                // Hideout data not available, show message or load it

                PageContent.Content = new TextBlock

                {

                    Text = "No hideout data available. Please refresh data.",

                    Foreground = FindResource("TextSecondaryBrush") as System.Windows.Media.Brush,

                    FontSize = 16,

                    HorizontalAlignment = HorizontalAlignment.Center,

                    VerticalAlignment = VerticalAlignment.Center

                };

            }

        }

    }



    #region Player Level



    /// <summary>

    /// Update player level UI

    /// </summary>

    private void UpdatePlayerLevelUI()

    {

        var level = _settingsService.PlayerLevel;

        TxtPlayerLevel.Text = level.ToString();



        // Disable buttons at min/max level

        BtnLevelDown.IsEnabled = level > SettingsService.MinPlayerLevel;

        BtnLevelUp.IsEnabled = level < SettingsService.MaxPlayerLevel;

    }



    /// <summary>

    /// Handle player level decrease

    /// </summary>

    private void BtnLevelDown_Click(object sender, RoutedEventArgs e)

    {

        _settingsService.PlayerLevel--;

    }



    /// <summary>

    /// Handle player level increase

    /// </summary>

    private void BtnLevelUp_Click(object sender, RoutedEventArgs e)

    {

        _settingsService.PlayerLevel++;

    }



    /// <summary>

    /// Handle player level change from settings service

    /// </summary>

    private void OnPlayerLevelChanged(object? sender, int newLevel)

    {

        Dispatcher.Invoke(() =>

        {

            UpdatePlayerLevelUI();



            // Refresh quest list if visible

            _questListPage?.RefreshDisplay();

        });

    }



    /// <summary>

    /// Only allow numeric input for player level

    /// </summary>

    private void TxtPlayerLevel_PreviewTextInput(object sender, TextCompositionEventArgs e)

    {

        e.Handled = !int.TryParse(e.Text, out _);

    }



    /// <summary>

    /// Apply level when losing focus

    /// </summary>

    private void TxtPlayerLevel_LostFocus(object sender, RoutedEventArgs e)

    {

        ApplyPlayerLevelFromTextBox();

    }



    /// <summary>

    /// Apply level when pressing Enter

    /// </summary>

    private void TxtPlayerLevel_KeyDown(object sender, KeyEventArgs e)

    {

        if (e.Key == Key.Enter)

        {

            ApplyPlayerLevelFromTextBox();

            Keyboard.ClearFocus();

        }

    }



    /// <summary>

    /// Parse and apply player level from TextBox input

    /// </summary>

    private void ApplyPlayerLevelFromTextBox()

    {

        if (int.TryParse(TxtPlayerLevel.Text, out var level))

        {

            // Clamp to valid range

            level = Math.Clamp(level, SettingsService.MinPlayerLevel, SettingsService.MaxPlayerLevel);

            _settingsService.PlayerLevel = level;

        }

        else

        {

            // Reset to current value if invalid

            TxtPlayerLevel.Text = _settingsService.PlayerLevel.ToString();

        }

    }



    #endregion



    #region Scav Rep



    /// <summary>

    /// Handle Scav Rep change from settings service (managed via Dashboard Fence card)

    /// </summary>

    private void OnScavRepChanged(object? sender, double newScavRep)

    {

        Dispatcher.Invoke(() =>

        {

            // Refresh quest list if visible

            _questListPage?.RefreshDisplay();

        });

    }



    #endregion



    #region DSP Decode Count



    /// <summary>

    /// Update DSP Decode Count UI - highlight the selected button

    /// </summary>

    private void UpdateDspDecodeUI()

    {

        var dspCount = _settingsService.DspDecodeCount;



        // Reset all buttons to default style

        var buttons = new[] { BtnDsp0, BtnDsp1, BtnDsp2, BtnDsp3 };

        foreach (var btn in buttons)

        {

            btn.Background = (Brush)FindResource("BackgroundMediumBrush");

            btn.Foreground = (Brush)FindResource("TextPrimaryBrush");

        }



        // Highlight the selected button

        var selectedBtn = buttons[dspCount];

        selectedBtn.Background = (Brush)FindResource("AccentBrush");

        selectedBtn.Foreground = (Brush)FindResource("BackgroundDarkBrush");

    }



    /// <summary>

    /// Handle DSP Decode button click

    /// </summary>

    private void BtnDsp_Click(object sender, RoutedEventArgs e)

    {

        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out var count))

        {

            _settingsService.DspDecodeCount = count;

        }

    }



    /// <summary>

    /// Handle DSP Decode Count change from settings service

    /// </summary>

    private void OnDspDecodeCountChanged(object? sender, int newCount)

    {

        Dispatcher.Invoke(() =>

        {

            UpdateDspDecodeUI();



            // Refresh quest list if visible

            _questListPage?.RefreshDisplay();

        });

    }



    #endregion



    #region Edition Settings



    /// <summary>

    /// Update Edition UI checkboxes

    /// </summary>

    private void UpdateEditionUI()

    {

        ChkEodEdition.IsChecked = _settingsService.HasEodEdition;

        ChkUnheardEdition.IsChecked = _settingsService.HasUnheardEdition;

    }



    /// <summary>

    /// Handle edition checkbox change

    /// </summary>

    private void ChkEdition_Changed(object sender, RoutedEventArgs e)

    {

        if (_isLoading) return;



        if (sender == ChkEodEdition)

        {

            _settingsService.HasEodEdition = ChkEodEdition.IsChecked == true;

        }

        else if (sender == ChkUnheardEdition)

        {

            _settingsService.HasUnheardEdition = ChkUnheardEdition.IsChecked == true;

        }

    }



    /// <summary>

    /// Handle edition change from settings service

    /// </summary>

    private void OnPlayerFactionChanged(object? sender, string? faction)
    {
        Dispatcher.Invoke(UpdateFactionUI);
    }

    private void UpdateFactionUI()
    {
        var faction = _settingsService.PlayerFaction;
        if (faction == "bear")
        {
            if (RbProfileBear != null) RbProfileBear.IsChecked = true;
            if (RbProfileUsec != null) RbProfileUsec.IsChecked = false;
        }
        else if (faction == "usec")
        {
            if (RbProfileUsec != null) RbProfileUsec.IsChecked = true;
            if (RbProfileBear != null) RbProfileBear.IsChecked = false;
        }
        else
        {
            if (RbProfileBear != null) RbProfileBear.IsChecked = false;
            if (RbProfileUsec != null) RbProfileUsec.IsChecked = false;
        }
    }

    private void ProfileFaction_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is RadioButton rb && rb.IsChecked == true)
        {
            rb.IsChecked = false;
            _settingsService.PlayerFaction = null;
            e.Handled = true;
        }
    }

    private void ProfileFaction_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        var faction = RbProfileBear?.IsChecked == true ? "bear" : (RbProfileUsec?.IsChecked == true ? "usec" : null);
        _settingsService.PlayerFaction = faction;
    }

    private void OnEditionChanged(object? sender, bool value)

    {

        Dispatcher.Invoke(() =>

        {

            UpdateEditionUI();



            // Refresh quest list if visible

            _questListPage?.RefreshDisplay();

        });

    }



    #endregion



    #region Prestige Level



    /// <summary>

    /// Update Prestige Level UI

    /// </summary>

    private void UpdatePrestigeLevelUI()

    {

        var prestigeLevel = _settingsService.PrestigeLevel;

        TxtPrestigeLevel.Text = prestigeLevel.ToString();



        // Disable buttons at min/max prestige level

        BtnPrestigeDown.IsEnabled = prestigeLevel > SettingsService.MinPrestigeLevel;

        BtnPrestigeUp.IsEnabled = prestigeLevel < SettingsService.MaxPrestigeLevel;

    }



    /// <summary>

    /// Handle prestige level decrease

    /// </summary>

    private void BtnPrestigeDown_Click(object sender, RoutedEventArgs e)

    {

        _settingsService.PrestigeLevel--;

    }



    /// <summary>

    /// Handle prestige level increase

    /// </summary>

    private void BtnPrestigeUp_Click(object sender, RoutedEventArgs e)

    {

        _settingsService.PrestigeLevel++;

    }



    /// <summary>

    /// Handle prestige level change from settings service

    /// </summary>

    private void OnPrestigeLevelChanged(object? sender, int newLevel)

    {

        Dispatcher.Invoke(() =>

        {

            UpdatePrestigeLevelUI();



            // Refresh quest list if visible

            _questListPage?.RefreshDisplay();

        });

    }



    #endregion





    /// <summary>

    /// Reset all progress with confirmation

    /// </summary>

    private async void BtnResetProgress_Click(object sender, RoutedEventArgs e)

    {

        var result = MessageBox.Show(

            "Are you sure you want to reset all progress?",

            "Reset Progress",

            MessageBoxButton.YesNo,

            MessageBoxImage.Warning);



        if (result == MessageBoxResult.Yes)

        {

            // Reset quest progress

            QuestProgressService.Instance.ResetAllProgress();



            // Reset hideout progress

            _hideoutProgressService.ResetAllProgress();



            // Reload pages

            await LoadAndShowQuestListAsync();



            MessageBox.Show(

            "Progress has been reset.",

            "Reset Complete",

                MessageBoxButton.OK,

                MessageBoxImage.Information);

        }

    }



    #region Profile Drawer



    /// <summary>

    /// Toggle profile popup visibility

    /// </summary>

    private void BtnProfile_Click(object sender, RoutedEventArgs e)

    {

        ProfilePopup.IsOpen = !ProfilePopup.IsOpen;

    }



    private void BtnQuickSync_Click(object sender, RoutedEventArgs e)

    {

        BtnSyncQuest_Click(sender, e);

    }



    private void BtnSyncFromLogs_Click(object sender, RoutedEventArgs e)

    {

        BtnSyncQuest_Click(sender, e);

    }



    public async Task PerformResetAllProgressAsync()

    {

        ShowLoadingOverlay("Resetting all progress...");

        QuestProgressService.Instance.ResetAllProgress();

        _hideoutProgressService.ResetAllProgress();

        await LoadAndShowQuestListAsync();

        HideLoadingOverlay();

        MessageBox.Show(

            "All progress has been reset.",

            "Reset Complete",

            MessageBoxButton.OK,

            MessageBoxImage.Information);

    }



    #endregion



    #region Settings



    /// <summary>

    /// Open settings dialog

    /// </summary>

    private void BtnSettings_Click(object sender, RoutedEventArgs e)

    {

        ShowSettingsOverlay();

    }



    /// <summary>

    /// Show settings overlay

    /// </summary>

    private void ShowSettingsOverlay()

    {

        UpdateSettingsUI();

        SettingsOverlay.Visibility = Visibility.Visible;



        var blurAnimation = new DoubleAnimation(0, 4, TimeSpan.FromMilliseconds(50));

        BlurEffect.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnimation);

    }



    /// <summary>

    /// Hide settings overlay

    /// </summary>

    private void HideSettingsOverlay()

    {

        var blurAnimation = new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(50));

        blurAnimation.Completed += (s, e) =>

        {

            SettingsOverlay.Visibility = Visibility.Collapsed;

        };

        BlurEffect.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnimation);

    }



    /// <summary>

    /// Update settings UI with current values

    /// </summary>

    private void UpdateSettingsUI()

    {

        var logPath = _settingsService.LogFolderPath;

        var isValid = _settingsService.IsLogFolderValid;

        var method = _settingsService.DetectionMethod;



        // Update localized text

        UpdateSettingsLocalizedText();



        // Update quest sync section

        UpdateQuestSyncUI();



        // Update cache size display

        UpdateCacheSizeDisplay();



        // Update font size display

        UpdateFontSizeDisplay();



        // Update path display

        if (!string.IsNullOrEmpty(logPath))

        {

            TxtCurrentLogPath.Text = logPath;

            TxtCurrentLogPath.Foreground = (Brush)FindResource("TextPrimaryBrush");

        }

        else

        {

            TxtCurrentLogPath.Text = "Not configured";

            TxtCurrentLogPath.Foreground = (TryFindResource("TextSecondaryBrush") as Brush) ?? (TryFindResource("TacticalTextSecondaryBrush") as Brush) ?? System.Windows.Media.Brushes.Gray;

        }



        // Update detection method

        if (!string.IsNullOrEmpty(method))

        {

            TxtDetectionMethod.Text = $"({method})";

        }

        else

        {

            TxtDetectionMethod.Text = "";

        }



        // Update status indicator

        if (isValid)

        {

            LogFolderStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green

            TxtLogFolderStatus.Text = _loc.CurrentLanguage switch

            {

                AppLanguage.KO => "유효한 경로",

                _ => "Valid path"

            };

        }

        else

        {

            LogFolderStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red

            TxtLogFolderStatus.Text = "Invalid path";

        }

    }



    /// <summary>

    /// Update settings dialog localized text

    /// </summary>

    private void UpdateSettingsLocalizedText()

    {



        TxtSettingsTitle.Text = "Settings";



        TxtLogFolderLabel.Text = "Tarkov Log Folder";



        TxtLogFolderDesc.Text = "Set the Tarkov Logs folder path to automatically track completed quests.";



        BtnAutoDetect.Content = "Auto Detect";



        BtnBrowseLogFolder.Content = "Browse...";



        BtnResetLogFolder.Content = "Reset";

    }



    /// <summary>

    /// Close settings overlay when clicking outside the dialog

    /// </summary>

    private void SettingsOverlay_MouseDown(object sender, MouseButtonEventArgs e)

    {

        if (e.OriginalSource == SettingsOverlay)

        {

            HideSettingsOverlay();

        }

    }



    /// <summary>

    /// Close settings button click

    /// </summary>

    private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)

    {

        HideSettingsOverlay();

    }



    /// <summary>

    /// Auto detect Tarkov log folder

    /// </summary>

    private void BtnAutoDetect_Click(object sender, RoutedEventArgs e)

    {

        _settingsService.ResetLogFolderPath();

        var detectedPath = _settingsService.AutoDetectLogFolder();



        if (!string.IsNullOrEmpty(detectedPath))

        {

            _settingsService.LogFolderPath = detectedPath;

            UpdateSettingsUI();



            var message = $"Found log folder:\n{detectedPath}";



            MessageBox.Show(message, "Auto Detect",

                MessageBoxButton.OK,

                MessageBoxImage.Information);

        }

        else

        {

            UpdateSettingsUI();



            var message = "Tarkov installation not found.\nPlease select the Logs folder manually.";



            MessageBox.Show(message, "Warning",

                MessageBoxButton.OK,

                MessageBoxImage.Warning);

        }

    }



    /// <summary>

    /// Browse for log folder

    /// </summary>

    private void BtnBrowseLogFolder_Click(object sender, RoutedEventArgs e)

    {

        var dialog = new Microsoft.Win32.OpenFolderDialog

        {

            Title = "Select Tarkov Logs Folder"

        };



        if (dialog.ShowDialog() == true)

        {

            var selectedPath = dialog.FolderName;



            // Check if it looks like a valid logs folder

            if (Directory.Exists(selectedPath))

            {

                _settingsService.LogFolderPath = selectedPath;

                UpdateSettingsUI();

            }

        }

    }



    /// <summary>

    /// Reset log folder setting

    /// </summary>

    private void BtnResetLogFolder_Click(object sender, RoutedEventArgs e)

    {

        _settingsService.ResetLogFolderPath();

        UpdateSettingsUI();

    }



    #endregion



    #region Cross-Tab Navigation



    /// <summary>

    /// Navigate to Dashboard tab

    /// </summary>

    public void NavigateToDashboard()

    {

        TabDashboard.IsChecked = true;

        _dashboardPage ??= new DashboardPage();

        PageContent.Content = _dashboardPage;

        _dashboardPage.Refresh();

    }



    /// <summary>

    /// Navigate to Storyline tab

    /// </summary>

    public void NavigateToStoryline()

    {

        TabStoryline.IsChecked = true;

        _storylinePage ??= new StorylinePage();

        PageContent.Content = _storylinePage;

    }



    /// <summary>

    /// Navigate to Tasks tab (Misiones)

    /// </summary>

    public void NavigateToTasks()

    {

        TabTasks.IsChecked = true;

        _questListPage ??= new QuestListPage();

        PageContent.Content = _questListPage;

    }



    /// <summary>

    /// Navigate to Tasks tab and select a specific quest

    /// </summary>

    public void NavigateToQuest(string questNormalizedName)

    {

        TabTasks.IsChecked = true;

        _questListPage ??= new QuestListPage();

        PageContent.Content = _questListPage;

        _questListPage?.SelectQuest(questNormalizedName);

    }



    /// <summary>

    /// Navigate to Tasks tab filtered by trader

    /// </summary>

    public void NavigateToTrader(string traderName)

    {

        TabTasks.IsChecked = true;

        _questListPage ??= new QuestListPage();

        PageContent.Content = _questListPage;

        _questListPage?.FilterByTrader(traderName);

    }



    /// <summary>

    /// Navigate to Tasks tab filtered by Kappa required quests

    /// </summary>

    public void NavigateToKappaQuests()

    {

        TabTasks.IsChecked = true;

        _questListPage ??= new QuestListPage();

        PageContent.Content = _questListPage;

        _questListPage?.FilterByKappa();

    }



    /// <summary>

    /// Navigate to Needed Items tab with optional tab/category filter or search term

    /// </summary>

    public void NavigateToNeededItems(string? filter = null)

    {

        TabNeededItems.IsChecked = true;

        _itemsPage ??= new ItemsPage();

        PageContent.Content = _itemsPage;



        if (!string.IsNullOrEmpty(filter) && _itemsPage != null)

        {

            if (string.Equals(filter, "Kappa", StringComparison.OrdinalIgnoreCase))

            {

                _itemsPage.ViewModel.ActiveTab = "Kappa";

            }

            else if (string.Equals(filter, "Tasks", StringComparison.OrdinalIgnoreCase))

            {

                _itemsPage.ViewModel.ActiveTab = "Tasks";

            }

            else if (string.Equals(filter, "Hideout", StringComparison.OrdinalIgnoreCase))

            {

                _itemsPage.ViewModel.ActiveTab = "Hideout";

            }

            else if (string.Equals(filter, "Completed", StringComparison.OrdinalIgnoreCase))

            {

                _itemsPage.ViewModel.ActiveTab = "Completed";

            }

            else

            {

                _itemsPage.ViewModel.ActiveTab = "All";

                _itemsPage.ViewModel.SearchText = filter;

            }

        }

    }



    /// <summary>

    /// Navigate to Needed Items tab and select a specific item by its ID

    /// </summary>

    public void NavigateToItem(string itemId)

    {

        TabNeededItems.IsChecked = true;

        _itemsPage ??= new ItemsPage();

        PageContent.Content = _itemsPage;

        _itemsPage?.SelectItem(itemId);

    }



    /// <summary>

    /// Navigate to Tasks tab in MAPS view mode for a specific map and optional floor

    /// </summary>

    public void NavigateToMap(string mapKey, string? floorId = null)

    {

        TabTasks.IsChecked = true;

        _questListPage ??= new QuestListPage();

        PageContent.Content = _questListPage;

        _questListPage.ShowObjectiveOnMap(mapKey, null!, floorId);

    }



    /// <summary>

    /// Navigate to Tasks tab in MAPS view mode focusing on a specific task and map

    /// </summary>

    public void NavigateToTaskOnMap(TarkovTask task, string targetMap)

    {

        TabTasks.IsChecked = true;

        _questListPage ??= new QuestListPage();

        PageContent.Content = _questListPage;

        _questListPage.NavigateToTaskOnMap(task, targetMap);

    }



    /// <summary>

    /// Navigate to Hideout tab and select a specific module

    /// </summary>

    public void NavigateToHideout(string stationId)

    {

        TabHideout.IsChecked = true;

        _hideoutPage ??= (HideoutProgressService.Instance.AllModules?.Count > 0 ? new HideoutPage() : null);

        PageContent.Content = _hideoutPage;

        _hideoutPage?.SelectModule(stationId);

    }



    #endregion



    #region Quest Log Sync



    /// <summary>

    /// Update quest sync UI elements

    /// </summary>

    private void UpdateQuestSyncUI()

    {

        // Update localized text

        TxtQuestSyncLabel.Text = "Quest Log Sync";

        TxtQuestSyncDesc.Text = "Sync quest progress from game log files. Analyzes Tarkov logs to update completed quests.";

        BtnSyncQuest.Content = "Sync Quests";



        // Update monitoring status

        var isMonitoring = _logSyncService.IsMonitoring;

        MonitoringStatusIndicator.Fill = isMonitoring

            ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) // Green

            : new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red



        TxtMonitoringStatus.Text = isMonitoring ? "Monitoring" : "Not monitoring";



        BtnToggleMonitoring.Content = isMonitoring ? "Stop Monitoring" : "Start Monitoring";



        // Disable sync button if log folder is not valid

        BtnSyncQuest.IsEnabled = _settingsService.IsLogFolderValid;

        BtnToggleMonitoring.IsEnabled = _settingsService.IsLogFolderValid;

    }



    /// <summary>

    /// Sync quest progress from logs

    /// </summary>

    private void BtnSyncQuest_Click(object sender, RoutedEventArgs e)

    {

        var logPath = _settingsService.LogFolderPath;

        if (string.IsNullOrEmpty(logPath) || !Directory.Exists(logPath))

        {

            MessageBox.Show(

                "Log folder is not set or does not exist.",

                "Error",

                MessageBoxButton.OK,

                MessageBoxImage.Warning);

            return;

        }



        // Hide settings overlay

        HideSettingsOverlay();



        // Show wipe warning if not hidden

        if (!_settingsService.HideWipeWarning)

        {

            if (!WipeWarningDialog.ShowWarning(logPath, this))

            {

                return; // User cancelled

            }

        }



        // Proceed with sync

        PerformQuestSync(logPath);

    }



    /// <summary>

    /// Perform the actual quest sync

    /// </summary>

    private async void PerformQuestSync(string logPath)

    {

        ShowLoadingOverlay("Scanning log files...");



        try

        {

            var progress = new Progress<string>(message =>

            {

                Dispatcher.Invoke(() => UpdateLoadingStatus(message));

            });



            var result = await _logSyncService.SyncFromLogsAsync(logPath, progress);



            // Immediately hide LoadingOverlay to prevent animation collision

            // (HideLoadingOverlay animation may be cancelled by ShowSyncResultDialog's blur animation)

            LoadingOverlay.Visibility = Visibility.Collapsed;

            HideLoadingOverlay();



            // Show result dialog even if no quests to complete (to show in-progress quests)

            if (result.QuestsToComplete.Count == 0 && result.InProgressQuests.Count == 0)

            {

                MessageBox.Show(

                    result.TotalEventsFound > 0

                        ? $"Found {result.TotalEventsFound} quest events, but no quests need updating."

                        : "No quest events found in logs.",

                    "Sync Complete",

                    MessageBoxButton.OK,

                    MessageBoxImage.Information);

                return;

            }



            // Show confirmation dialog

            ShowSyncResultDialog(result);

        }

        catch (Exception ex)

        {

            HideLoadingOverlay();

            MessageBox.Show(

                $"An error occurred: {ex.Message}",

                "Sync Error",

                MessageBoxButton.OK,

                MessageBoxImage.Error);

        }

    }



    /// <summary>

    /// Toggle log monitoring

    /// </summary>

    private void BtnToggleMonitoring_Click(object sender, RoutedEventArgs e)

    {

        if (_logSyncService.IsMonitoring)

        {

            _logSyncService.StopMonitoring();

        }

        else

        {

            var logPath = _settingsService.LogFolderPath;

            if (!string.IsNullOrEmpty(logPath) && Directory.Exists(logPath))

            {

                _logSyncService.StartMonitoring(logPath);



                // Subscribe to quest events

                _logSyncService.QuestEventDetected -= OnQuestEventDetected;

                _logSyncService.QuestEventDetected += OnQuestEventDetected;

            }

        }



        UpdateQuestSyncUI();

    }



    /// <summary>

    /// Handle real-time quest event detection

    /// </summary>

    private void OnQuestEventDetected(object? sender, QuestLogEvent evt)

    {

        Dispatcher.Invoke(() =>

        {

            // Find the task

            var progressService = QuestProgressService.Instance;

            var tasksByQuestId = BuildQuestIdLookup(progressService.AllTasks);



            var cleanId = evt.QuestId?.Trim() ?? "";

            if (cleanId.Contains(' '))

            {

                cleanId = cleanId.Split(' ')[0];

            }



            if (!tasksByQuestId.TryGetValue(cleanId, out var task))

            {

                // Fallback search across BsgId, Id, NormalizedName, Name, or Ids

                task = progressService.AllTasks.FirstOrDefault(t =>

                    string.Equals(t.BsgId, cleanId, StringComparison.OrdinalIgnoreCase) ||

                    string.Equals(t.Id, cleanId, StringComparison.OrdinalIgnoreCase) ||

                    string.Equals(t.NormalizedName, cleanId, StringComparison.OrdinalIgnoreCase) ||

                    string.Equals(t.Name, cleanId, StringComparison.OrdinalIgnoreCase) ||

                    (t.Ids != null && t.Ids.Contains(cleanId, StringComparer.OrdinalIgnoreCase)));

            }



            if (task != null)

            {

                var message = evt.EventType switch

                {

                    QuestEventType.Started => $"Quest started: {task.Name}",

                    QuestEventType.Completed => $"Quest completed: {task.Name}",

                    QuestEventType.Failed => $"Quest failed: {task.Name}",

                    _ => ""

                };



                // Auto-update progress based on event

                switch (evt.EventType)

                {

                    case QuestEventType.Completed:

                        progressService.CompleteQuest(task, completePrerequisites: true);

                        _log.Info($"[AutoSync] Live quest completed: {task.Name} (ID: {task.BsgId})");

                        break;

                    case QuestEventType.Failed:

                        progressService.FailQuest(task);

                        _log.Info($"[AutoSync] Live quest failed: {task.Name} (ID: {task.BsgId})");

                        break;

                    case QuestEventType.Started:

                        // For started quests, complete all prerequisites in batch

                        var graphService = QuestGraphService.Instance;

                        if (!string.IsNullOrEmpty(task.NormalizedName))

                        {

                            var prereqs = graphService.GetAllPrerequisites(task.NormalizedName);

                            var prereqsToComplete = prereqs

                                .Where(p => progressService.GetStatus(p) != QuestStatus.Done)

                                .ToList();



                            if (prereqsToComplete.Count > 0)

                            {

                                // Use batch completion for better performance

                                progressService.CompleteQuestsBatch(prereqsToComplete);

                            }

                        }

                        _log.Info($"[AutoSync] Live quest started: {task.Name} (ID: {task.BsgId})");

                        break;

                }



                // Refresh quest list and dashboard if visible

                _questListPage?.RefreshDisplay();

                _dashboardPage?.Refresh();

            }

            else

            {

                _log.Debug($"[AutoSync] Quest event detected for unindexed quest ID '{cleanId}' ({evt.EventType})");

            }

        });

    }



    /// <summary>

    /// Build quest ID lookup dictionary indexing all canonical IDs, BSG IDs, normalized names, and titles

    /// </summary>

    private Dictionary<string, TarkovTask> BuildQuestIdLookup(IReadOnlyList<TarkovTask> tasks)

    {

        var lookup = new Dictionary<string, TarkovTask>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in tasks)

        {

            if (task.Ids != null)

            {

                foreach (var id in task.Ids)

                {

                    if (!string.IsNullOrEmpty(id) && !lookup.ContainsKey(id))

                    {

                        lookup[id] = task;

                    }

                }

            }

            if (!string.IsNullOrEmpty(task.Id) && !lookup.ContainsKey(task.Id))

            {

                lookup[task.Id] = task;

            }

            if (!string.IsNullOrEmpty(task.BsgId) && !lookup.ContainsKey(task.BsgId))

            {

                lookup[task.BsgId] = task;

            }

            if (!string.IsNullOrEmpty(task.NormalizedName) && !lookup.ContainsKey(task.NormalizedName))

            {

                lookup[task.NormalizedName] = task;

            }

            if (!string.IsNullOrEmpty(task.Name) && !lookup.ContainsKey(task.Name))

            {

                lookup[task.Name] = task;

            }

        }

        return lookup;

    }



    /// <summary>

    /// Show sync result confirmation dialog and apply changes

    /// </summary>

    private async void ShowSyncResultDialog(SyncResult result)

    {

        var (selectedChanges, resetRequested, alternativeCount) = SyncResultDialog.ShowResult(result, this);



        if (resetRequested)

        {

            await PerformResetAllProgressAsync();

            return;

        }



        if (selectedChanges == null || selectedChanges.Count == 0)

        {

            return;

        }



        ShowLoadingOverlay("Updating quest progress...");



        await _logSyncService.ApplyQuestChangesAsync(selectedChanges);



        HideLoadingOverlay();



        // Refresh quest list

        await LoadAndShowQuestListAsync();



        var totalUpdated = selectedChanges.Count;



        MessageBox.Show(

            alternativeCount > 0

                ? $"{totalUpdated} quests were updated.\n(Including {alternativeCount} alternative choice groups)"

                : $"{totalUpdated} quests were updated.",

            "Sync Complete",

            MessageBoxButton.OK,

            MessageBoxImage.Information);

    }



    #endregion



    #region In-Progress Quest Input



    /// <summary>

    /// Open in-progress quest input dialog

    /// </summary>

    private void BtnInProgressQuestInput_Click(object sender, RoutedEventArgs e)

    {

        HideSettingsOverlay();



        var result = InProgressQuestInputDialog.ShowDialog(this);

        if (result == null) return;



        // Apply the result

        ApplyInProgressQuestResult(result);

    }



    /// <summary>

    /// Apply the in-progress quest selection result

    /// </summary>

    private void ApplyInProgressQuestResult(InProgressQuestInputResult result)

    {

        var progressService = QuestProgressService.Instance;



        // Complete all prerequisites

        var completedCount = 0;

        foreach (var prereqName in result.PrerequisitesToComplete)

        {

            var prereqTask = progressService.GetTask(prereqName);

            if (prereqTask != null && progressService.GetStatus(prereqTask) != QuestStatus.Done)

            {

                progressService.CompleteQuest(prereqTask, completePrerequisites: false);

                completedCount++;

            }

        }



        // Refresh quest list

        _questListPage?.RefreshDisplay();



        // Show success message

        MessageBox.Show(

            string.Format(_loc.QuestsAppliedSuccess, result.SelectedQuests.Count, completedCount),

            "Notice",

            MessageBoxButton.OK,

            MessageBoxImage.Information);

    }



    #endregion



    #region Cache Management



    /// <summary>

    /// Calculate total cache size

    /// </summary>

    private long CalculateCacheSize()

    {

        long totalSize = 0;



        // Cache directory (wiki pages, images, etc.)

        var cachePath = AppEnv.CachePath;

        if (Directory.Exists(cachePath))

        {

            totalSize += GetDirectorySize(cachePath);

        }



        return totalSize;

    }



    /// <summary>

    /// Calculate total data size (JSON files)

    /// </summary>

    private long CalculateDataSize()

    {

        long totalSize = 0;



        // Data directory (JSON files)

        var dataPath = AppEnv.DataPath;

        if (Directory.Exists(dataPath))

        {

            totalSize += GetDirectorySize(dataPath);

        }



        return totalSize;

    }



    /// <summary>

    /// Get directory size recursively

    /// </summary>

    private long GetDirectorySize(string path)

    {

        long size = 0;

        try

        {

            var dir = new DirectoryInfo(path);

            foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))

            {

                size += file.Length;

            }

        }

        catch

        {

            // Ignore errors (access denied, etc.)

        }

        return size;

    }



    /// <summary>

    /// Format bytes to human readable string

    /// </summary>

    private string FormatBytes(long bytes)

    {

        string[] sizes = { "B", "KB", "MB", "GB" };

        int order = 0;

        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)

        {

            order++;

            size /= 1024;

        }

        return $"{size:0.##} {sizes[order]}";

    }



    /// <summary>

    /// Update cache size display

    /// </summary>

    private void UpdateCacheSizeDisplay()

    {

        var cacheSize = CalculateCacheSize();

        var dataSize = CalculateDataSize();

        var totalSize = cacheSize + dataSize;

        TxtCacheSize.Text = $"{FormatBytes(totalSize)} (Cache: {FormatBytes(cacheSize)}, Data: {FormatBytes(dataSize)})";

    }



    /// <summary>

    /// Clear cache button click handler

    /// </summary>

    private void BtnClearCache_Click(object sender, RoutedEventArgs e)

    {

        var result = MessageBox.Show(

            "Do you want to delete the cache?\n(Wiki pages, images, etc. will be deleted)",

            "Clear Cache",

            MessageBoxButton.YesNo,

            MessageBoxImage.Question);



        if (result != MessageBoxResult.Yes) return;



        try

        {

            BtnClearCache.IsEnabled = false;

            BtnClearAllData.IsEnabled = false;



            var cachePath = AppEnv.CachePath;

            if (Directory.Exists(cachePath))

            {

                Directory.Delete(cachePath, true);

            }



            UpdateCacheSizeDisplay();



            MessageBox.Show(

                "Cache has been cleared.\nPress the Refresh button to re-fetch data.",

                "Clear Complete",

                MessageBoxButton.OK,

                MessageBoxImage.Information);

        }

        catch (Exception ex)

        {

            MessageBox.Show(

                $"Error clearing cache: {ex.Message}",

                "Error",

                MessageBoxButton.OK,

                MessageBoxImage.Error);

        }

        finally

        {

            BtnClearCache.IsEnabled = true;

            BtnClearAllData.IsEnabled = true;

        }

    }



    /// <summary>

    /// Clear all data button click handler

    /// </summary>

    private async void BtnClearAllData_Click(object sender, RoutedEventArgs e)

    {

        var result = MessageBox.Show(

            "Do you want to delete all data?\n(Cache, quest data, item data, etc. will be deleted)\n\n⚠️ Quest progress will be preserved.",

            "Reset Data",

            MessageBoxButton.YesNo,

            MessageBoxImage.Warning);



        if (result != MessageBoxResult.Yes) return;



        try

        {

            BtnClearCache.IsEnabled = false;

            BtnClearAllData.IsEnabled = false;



            // Clear cache

            var cachePath = AppEnv.CachePath;

            if (Directory.Exists(cachePath))

            {

                Directory.Delete(cachePath, true);

            }



            // Clear data files (user data is now in Config/user_data.db, safe to delete all)

            var dataPath = AppEnv.DataPath;

            if (Directory.Exists(dataPath))

            {

                Directory.Delete(dataPath, true);

            }



            UpdateCacheSizeDisplay();



            // Hide settings overlay

            HideSettingsOverlay();



            // Show confirmation

            MessageBox.Show(

                "Cache cleared.",

                "Complete",

                MessageBoxButton.OK,

                MessageBoxImage.Information);

        }

        catch (Exception ex)

        {

            MessageBox.Show(

                $"Error deleting data: {ex.Message}",

                "Error",

                MessageBoxButton.OK,

                MessageBoxImage.Error);

        }

        finally

        {

            BtnClearCache.IsEnabled = true;

            BtnClearAllData.IsEnabled = true;

        }

    }



    /// <summary>

    /// Opens the visual backup manager to inspect and restore past database backups (.bak)

    /// </summary>

    private void BtnRollbackBackup_Click(object sender, RoutedEventArgs e)

    {

        try

        {

            var dialog = DiffViewerDialog.CreateRollbackDialog(this);

            var dialogResult = dialog.ShowDialog();

            if (dialog.WasRestored)

            {

                TxtApiUpdateStatus.Text = "Base de datos restaurada correctamente.";

                TxtApiUpdateStatus.Foreground = (TryFindResource("SuccessBrush") as Brush) ?? (TryFindResource("TacticalSuccessBrush") as Brush) ?? System.Windows.Media.Brushes.MediumSeaGreen;

                _ = RefreshActiveViewAsync();

            }

        }

        catch (Exception ex)

        {

            _log.Error($"Error opening rollback dialog: {ex.Message}");

            MessageBox.Show($"Error al abrir el gestor de respaldos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

        }

    }



    /// <summary>

    /// Checks for task/requirement updates from TarkovTracker API and presents differences in DiffViewerDialog.

    /// </summary>

    private async void BtnUpdateApiData_Click(object sender, RoutedEventArgs e)

    {

        try

        {

            BtnUpdateApiData.IsEnabled = false;

            TxtApiUpdateStatus.Text = "Conectando con Tarkov Tracker...";

            TxtApiUpdateStatus.Foreground = (TryFindResource("TextSecondaryBrush") as Brush) ?? (TryFindResource("TacticalTextSecondaryBrush") as Brush) ?? System.Windows.Media.Brushes.Gray;



            var progress = new Progress<string>(msg =>

            {

                Dispatcher.Invoke(() =>

                {

                    TxtApiUpdateStatus.Text = msg;

                });

            });



            var result = await DatabaseUpdateService.Instance.CheckAndUpdateAsync(progress);



            if (!result.Success)

            {

                TxtApiUpdateStatus.Text = string.Format(_loc.ApiUpdateFail, result.Message);

                TxtApiUpdateStatus.Foreground = (TryFindResource("ErrorBrush") as Brush) ?? (TryFindResource("TacticalDangerBrush") as Brush) ?? System.Windows.Media.Brushes.Crimson;

                return;

            }



            if (result.DiffReport == null || !result.DiffReport.HasChanges)

            {

                TxtApiUpdateStatus.Text = "Base de datos al día con Tarkov Tracker.";

                TxtApiUpdateStatus.Foreground = (TryFindResource("SuccessBrush") as Brush) ?? (TryFindResource("TacticalSuccessBrush") as Brush) ?? System.Windows.Media.Brushes.MediumSeaGreen;

                return;

            }



            // Diffs found: open visual DiffViewerDialog for user inspection & confirmation

            TxtApiUpdateStatus.Text = $"{result.DiffReport.TotalChanges} cambios detectados. Abriendo visor...";

            var dialog = new DiffViewerDialog(result.DiffReport)

            {

                Owner = this

            };



            var dialogResult = dialog.ShowDialog();

            if (dialog.WasApplied)

            {

                TxtApiUpdateStatus.Text = $"Actualización completada: {result.DiffReport.TotalChanges} cambios aplicados.";

                TxtApiUpdateStatus.Foreground = (TryFindResource("SuccessBrush") as Brush) ?? (TryFindResource("TacticalSuccessBrush") as Brush) ?? System.Windows.Media.Brushes.MediumSeaGreen;

                await RefreshActiveViewAsync();

            }

            else

            {

                TxtApiUpdateStatus.Text = "Actualización cancelada por el usuario.";

                TxtApiUpdateStatus.Foreground = (TryFindResource("TextSecondaryBrush") as Brush) ?? (TryFindResource("TacticalTextSecondaryBrush") as Brush) ?? System.Windows.Media.Brushes.Gray;

            }

        }

        catch (Exception ex)

        {

            _log.Error($"Manual TarkovTracker API update failed: {ex.Message}");

            TxtApiUpdateStatus.Text = string.Format(_loc.ApiUpdateFail, ex.Message);

            TxtApiUpdateStatus.Foreground = (TryFindResource("ErrorBrush") as Brush) ?? (TryFindResource("TacticalDangerBrush") as Brush) ?? System.Windows.Media.Brushes.Crimson;

        }

        finally

        {

            BtnUpdateApiData.IsEnabled = true;

        }

    }



    private async Task RefreshActiveViewAsync()

    {

        if (_questListPage != null && _questListPage.IsVisible)

        {

            await LoadAndShowQuestListAsync();

        }

        else if (_itemsPage != null && _itemsPage.IsVisible)

        {

            _itemsPage = new Pages.ItemsPage();

            PageContent.Content = _itemsPage;

        }

    }



    private void BtnFontSizeDown_Click(object sender, RoutedEventArgs e)

    {

        var currentSize = SettingsService.Instance.BaseFontSize;

        if (currentSize > SettingsService.MinFontSize)

        {

            SettingsService.Instance.BaseFontSize = currentSize - 1;

            UpdateFontSizeDisplay();

        }

    }



    private void BtnFontSizeUp_Click(object sender, RoutedEventArgs e)

    {

        var currentSize = SettingsService.Instance.BaseFontSize;

        if (currentSize < SettingsService.MaxFontSize)

        {

            SettingsService.Instance.BaseFontSize = currentSize + 1;

            UpdateFontSizeDisplay();

        }

    }



    private void BtnResetFontSize_Click(object sender, RoutedEventArgs e)

    {

        SettingsService.Instance.BaseFontSize = SettingsService.DefaultBaseFontSize;

        UpdateFontSizeDisplay();

    }



    private void UpdateFontSizeDisplay()

    {

        TxtCurrentFontSize.Text = SettingsService.Instance.BaseFontSize.ToString("0");

    }



    #endregion



    #region Full Screen Mode



    /// <summary>

    /// 전체화면 모드를 설정합니다.

    /// Map 페이지에서 호출됩니다.

    /// </summary>

    /// <param name="fullScreen">true이면 전체화면 모드 진입, false이면 해제</param>

    public void SetFullScreenMode(bool fullScreen)

    {

        _isFullScreen = fullScreen;



        if (fullScreen)

        {

            // 타이틀 바와 탭 네비게이션 숨기기

            TitleBar.Visibility = Visibility.Collapsed;

            TabNavigation.Visibility = Visibility.Collapsed;



            // 전체화면 모드 진입

            WindowStyle = WindowStyle.None;

            WindowState = WindowState.Maximized;

        }

        else

        {

            // 타이틀 바와 탭 네비게이션 다시 표시

            TitleBar.Visibility = Visibility.Visible;

            TabNavigation.Visibility = Visibility.Visible;



            // 전체화면 모드 해제

            WindowStyle = WindowStyle.SingleBorderWindow;

            WindowState = WindowState.Normal;

        }

    }



    #endregion





    /// <summary>

    /// Show migration result dialog

    /// </summary>

    private void ShowMigrationResultDialog(ConfigMigrationService.MigrationResult result)

    {

        MigrationResultDialog.Show(result, this);

    }



    #region System Tray (NotifyIcon) Management



    private void InitializeTrayIcon()

    {

        try

        {

            if (_notifyIcon != null) return;



            System.Drawing.Icon? appIcon = null;

            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "1764579946.ico");

            if (File.Exists(iconPath))

            {

                appIcon = new System.Drawing.Icon(iconPath);

            }

            else

            {

                try

                {

                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

                    if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))

                    {

                        appIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);

                    }

                }

                catch { }

            }



            if (appIcon == null)

            {

                appIcon = System.Drawing.SystemIcons.Application;

            }



            var contextMenu = new System.Windows.Forms.ContextMenuStrip();

            var openItem = new System.Windows.Forms.ToolStripMenuItem("Open Tarkov Helper", null, (s, e) => RestoreFromTray());

            openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);

            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit", null, (s, e) => ExitApplication());

            contextMenu.Items.Add(openItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            contextMenu.Items.Add(exitItem);



            _notifyIcon = new System.Windows.Forms.NotifyIcon

            {

                Icon = appIcon,

                Text = "Tarkov Helper",

                Visible = true,

                ContextMenuStrip = contextMenu

            };



            _notifyIcon.DoubleClick += (s, e) => RestoreFromTray();

            _notifyIcon.Click += (s, e) =>

            {

                if (e is System.Windows.Forms.MouseEventArgs mouseArgs && mouseArgs.Button == System.Windows.Forms.MouseButtons.Left)

                {

                    RestoreFromTray();

                }

            };



            _log.Debug("System tray icon initialized successfully.");

        }

        catch (Exception ex)

        {

            _log.Error("Failed to initialize system tray icon", ex);

        }

    }



    public void RestoreFromTray()

    {

        if (!Dispatcher.CheckAccess())

        {

            Dispatcher.Invoke(RestoreFromTray);

            return;

        }

        Show();

        if (WindowState == WindowState.Minimized)

        {

            WindowState = WindowState.Normal;

        }

        Activate();

        Topmost = true;

        Topmost = false;

        Focus();

    }



    public void ExitApplication()

    {

        _isExplicitExit = true;

        if (_notifyIcon != null)

        {

            _notifyIcon.Visible = false;

            _notifyIcon.Dispose();

            _notifyIcon = null;

        }

        System.Windows.Application.Current.Shutdown();

    }



    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)

    {
        try
        {
            NeededItemProgressService.Instance.FlushPendingSaves();
            ObjectiveProgressService.Instance.SaveObjectiveProgress();
        }
        catch { }

        if (!_isExplicitExit)

        {

            e.Cancel = true;

            Hide();

            if (_notifyIcon != null)

            {

                _notifyIcon.ShowBalloonTip(1500, "Tarkov Helper", "Minimized to system tray (hidden icons).", System.Windows.Forms.ToolTipIcon.Info);

            }

            return;

        }



        base.OnClosing(e);

    }



    protected override void OnClosed(EventArgs e)

    {

        if (Instance == this) Instance = null;

        if (_notifyIcon != null)

        {

            _notifyIcon.Visible = false;

            _notifyIcon.Dispose();

            _notifyIcon = null;

        }

        base.OnClosed(e);

    }



    #endregion



}

