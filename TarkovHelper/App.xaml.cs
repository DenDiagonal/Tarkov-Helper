using System.IO;
using System.Reflection;
using System.Windows;
using AutoUpdaterDotNET;
using TarkovHelper.Services;
using TarkovHelper.Services.DataAudit;
using TarkovHelper.Services.Logging;

namespace TarkovHelper
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly ILogger _log = Log.For<App>();

        private const string UpdateXmlUrl = "https://raw.githubusercontent.com/DenDiagonal/Tarkov-Helper/main/update.xml";

        private static string DataDirectory => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Data"
        );

        private static string VersionFilePath => Path.Combine(DataDirectory, "app_version.txt");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 전역 예외 처리 - 에러 로그 파일에 안전하게 기록
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                WriteCrashLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Unhandled Exception:\n{ex}\n\nStack trace:\n{ex?.StackTrace}");
            };

            DispatcherUnhandledException += (s, args) =>
            {
                WriteCrashLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Dispatcher Exception:\n{args.Exception}\n\nStack trace:\n{args.Exception.StackTrace}");
                args.Handled = false; // 앱 종료 허용
            };

            // 버전 변경 시 캐시 데이터 초기화 (사용자 진행 상황은 유지)
            CheckAndRefreshDataOnVersionChange();

            // 폰트 크기 적용 (초기 프레임은 기본값 적용, DB 로드 완료 시 이벤트로 자동 반영)
            ApplyBaseFontSize(SettingsService.DefaultBaseFontSize);
            SettingsService.Instance.BaseFontSizeChanged += (_, size) => ApplyBaseFontSize(size);

            // Purgado asíncrono de logs antiguos y copias de seguridad obsoletas en segundo plano (baja prioridad)
            _ = Task.Run(async () =>
            {
                try
                {
                    await LogCleanupService.CreateFromSettings().CleanupAsync().ConfigureAwait(false);
                    DatabaseBackupManager.Instance.PruneOldBackups(maxRetainedBackups: 5);
                }
                catch (Exception ex)
                {
                    _log.Warning($"Background maintenance cleanup failed: {ex.Message}");
                }
            });

            // Note: AutoUpdater is now managed by UpdateService in MainWindow
            // It will show update dialog only when user clicks "Update to vX.X.X" button
        }

        private static void WriteCrashLog(string message)
        {
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
                File.WriteAllText(logPath, message);
            }
            catch
            {
                try
                {
                    var fallbackDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TarkovHelper");
                    if (!Directory.Exists(fallbackDir)) Directory.CreateDirectory(fallbackDir);
                    var fallbackPath = Path.Combine(fallbackDir, "crash_log.txt");
                    File.WriteAllText(fallbackPath, message);
                }
                catch { }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _log.Info("Application shutting down cleanly...");

            // 1. Detener emisores de eventos de red, watchers de archivos y timers ANTES de volcar BD
            TryDispose(() => UpdateService.Instance.StopAutoCheck(), nameof(UpdateService));
            TryDispose(() => DatabaseUpdateService.Instance.Dispose(), nameof(DatabaseUpdateService));
            TryDispose(() => LogSyncService.Instance.Dispose(), nameof(LogSyncService));
            TryDispose(() => EftRaidEventService.Instance.Dispose(), nameof(EftRaidEventService));
            TryDispose(() => TarkovHelper.Services.Map.LogMapWatcherService.Instance.Dispose(), "LogMapWatcherService");
            TryDispose(() => TarkovHelper.Services.Map.MapTrackerService.Instance.Dispose(), "MapTrackerService");
            TryDispose(() => OverlayMiniMapService.Instance.Dispose(), nameof(OverlayMiniMapService));
            TryDispose(() =>
            {
                GlobalKeyboardHookService.Instance.IsEnabled = false;
                GlobalKeyboardHookService.Instance.Dispose();
            }, nameof(GlobalKeyboardHookService));

            // 2. Volcado determinista y bloqueante con timeout acotado (3s) de todas las escrituras pendientes a SQLite
            TryDispose(() =>
            {
                Task.Run(async () =>
                {
                    await ProfileService.Instance.FlushPendingSaveAsync().ConfigureAwait(false);
                    await NeededItemProgressService.Instance.FlushPendingSavesAsync().ConfigureAwait(false);
                    await ObjectiveProgressService.Instance.FlushPendingSavesAsync().ConfigureAwait(false);
                    await ItemInventoryService.Instance.FlushPendingSavesAsync().ConfigureAwait(false);
                    await HideoutProgressService.Instance.FlushPendingSavesAsync().ConfigureAwait(false);
                }).Wait(TimeSpan.FromSeconds(3));

                // Checkpoint limpio del WAL antes de cerrar el pool de conexiones
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            }, "SQLiteFlushAndCheckpoint");

            TryDispose(() => SingleInstanceService.Instance.Dispose(), nameof(SingleInstanceService));
            TryDispose(() => LoggingService.Instance.Dispose(), nameof(LoggingService));

            base.OnExit(e);
        }

        private static void TryDispose(Action action, string componentName)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                try { _log.Error($"Error shutting down {componentName}", ex); } catch { }
            }
        }

        /// <summary>
        /// Apply base font size to application resources
        /// </summary>
        public void ApplyBaseFontSize(double baseFontSize)
        {
            Resources["BaseFontSize"] = baseFontSize;
            Resources["FontSizeTiny"] = Math.Max(9.5, baseFontSize - 3.5);
            Resources["FontSizeXSmall"] = Math.Max(10.5, baseFontSize - 2.5);
            Resources["FontSizeSmall"] = Math.Max(11.5, baseFontSize - 1.5);
            Resources["FontSizeMedium"] = baseFontSize;
            Resources["FontSizeLarge"] = baseFontSize + 2;
            Resources["FontSizeXLarge"] = baseFontSize + 4;
            Resources["FontSizeTitle"] = baseFontSize + 6;
            Resources["FontSizeHeader"] = baseFontSize + 10;
        }

        /// <summary>
        /// 앱 버전이 변경되었으면 API 데이터 캐시를 삭제하여 새로 받도록 함
        /// progress.json, settings.json 등 사용자 데이터는 유지
        /// </summary>
        private void CheckAndRefreshDataOnVersionChange()
        {
            try
            {
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
                var savedVersion = GetSavedVersion();

                if (savedVersion != currentVersion)
                {
                    // 버전이 다르면 캐시 데이터만 삭제 (사용자 데이터는 유지)
                    DeleteCacheDataFiles();
                    SaveCurrentVersion(currentVersion);
                }
            }
            catch
            {
                // 버전 체크 실패 시 무시하고 진행
            }
        }

        private string? GetSavedVersion()
        {
            if (!File.Exists(VersionFilePath))
                return null;

            return File.ReadAllText(VersionFilePath).Trim();
        }

        private void SaveCurrentVersion(string version)
        {
            if (!Directory.Exists(DataDirectory))
                Directory.CreateDirectory(DataDirectory);

            File.WriteAllText(VersionFilePath, version);
        }

        /// <summary>
        /// API에서 받은 캐시 데이터만 삭제
        /// tasks.json, items.json, hideout.json 등 캐시 데이터 삭제
        /// app_settings.json, quest_progress.json, hideout_progress.json 등 사용자 데이터는 유지
        /// </summary>
        /// <summary>
        /// API에서 받은 캐시 데이터만 삭제 (화이트리스트 기반 안전 삭제)
        /// tasks.json, items.json, hideout.json 등 캐시 데이터만 삭제하며,
        /// DB 파일(*.db, *.db-wal)이나 사용자 데이터 파일은 절대 삭제하지 않음.
        /// </summary>
        private void DeleteCacheDataFiles()
        {
            if (!Directory.Exists(DataDirectory)) return;

            var cacheFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "tasks.json",
                "items.json",
                "hideout.json",
                "traders.json",
                "maps.json"
            };

            foreach (var file in Directory.GetFiles(DataDirectory, "*.json"))
            {
                var fileName = Path.GetFileName(file);
                if (cacheFileNames.Contains(fileName))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // 삭제 실패 시 무시
                    }
                }
            }
        }
    }
}
