using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TarkovHelper.Debug;
using TarkovHelper.Services;

namespace TarkovHelper;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            if (args.Contains("--capture-screenshots"))
            {
                MigrationService.RunMigrationIfNeeded();

                var screenshotApp = new App();
                screenshotApp.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                screenshotApp.InitializeComponent();
                CaptureScreenshotsRoutine(screenshotApp);
                return;
            }

            // Enforce single instance: if another instance is already running,
            // signal it to restore/focus and exit immediately.
            if (!SingleInstanceService.Instance.Start())
            {
                return;
            }

            // Run migration before anything else
            MigrationService.RunMigrationIfNeeded();

            var app = new App();
            app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            app.InitializeComponent();

            var mainWindow = new MainWindow();

            // Debug 모드에서 Toolbox 창 표시
            if (AppEnv.IsDebugMode)
            {
                TestMenu.MainWindow = mainWindow;
                var toolbox = new ToolboxWindow
                {

                };
                mainWindow.Loaded += (s, e) => {
                    toolbox.Owner = mainWindow;
                    toolbox.Show();
                };
            }

            app.Run(mainWindow);
        }
        catch (Exception ex)
        {
            var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_crash.log");
            System.IO.File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Fatal Main Exception:\n{ex}\n\nStack:\n{ex.StackTrace}");
            throw;
        }
        finally
        {
            SingleInstanceService.Instance.Dispose();
        }
    }

    private static void CaptureScreenshotsRoutine(App app)
    {
        string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshot_debug.log");
        void Log(string msg) => File.AppendAllText(logFile, $"[{DateTime.UtcNow:HH:mm:ss.fff}] {msg}\n");

        Log("Starting CaptureScreenshotsRoutine...");
        try
        {
            UserDataDbService.Instance.EnsureInitialized();
            Log("UserDataDbService initialized");

            var mainWindow = new MainWindow();
            Log("MainWindow instantiated");

            mainWindow.Width = 1440;
            mainWindow.Height = 850;
            mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            mainWindow.Left = 0;
            mainWindow.Top = 0;
            mainWindow.Show();
            Log("MainWindow.Show() called");

            // Allow initial startup to process
            DoEvents(4000);
            Log("Finished initial DoEvents");

            string repoRoot = AppDomain.CurrentDomain.BaseDirectory;
            string screenshotsDir = Path.Combine(repoRoot, "screenshots");
            while (!Directory.Exists(screenshotsDir) && Directory.GetParent(repoRoot) != null)
            {
                repoRoot = Directory.GetParent(repoRoot)!.FullName;
                screenshotsDir = Path.Combine(repoRoot, "screenshots");
            }

            if (!Directory.Exists(screenshotsDir))
            {
                Directory.CreateDirectory(screenshotsDir);
            }
            Log($"Target screenshots dir: {screenshotsDir}");

            // 1. Quests Tab (TRADERS card feed view)
            mainWindow.NavigateToTasks();
            DoEvents(1000);
            if (mainWindow.QuestListPage != null)
            {
                mainWindow.QuestListPage.RbViewTraders.IsChecked = true;
            }
            DoEvents(2500);
            string questPath = Path.Combine(screenshotsDir, "quest.png");
            SaveVisualToPng(mainWindow, questPath);
            Log($"Saved quest.png: {File.Exists(questPath)} (size: {new FileInfo(questPath).Length})");

            // 2. Map Tab (Woods Map with SVG and task objectives)
            mainWindow.NavigateToTasks();
            DoEvents(500);
            if (mainWindow.QuestListPage != null)
            {
                mainWindow.QuestListPage.RbViewMaps.IsChecked = true;
                mainWindow.QuestListPage.SelectMapChipByKey("woods");
            }
            DoEvents(3000);
            string mapPath = Path.Combine(screenshotsDir, "map.png");
            SaveVisualToPng(mainWindow, mapPath);
            Log($"Saved map.png: {File.Exists(mapPath)} (size: {new FileInfo(mapPath).Length})");

            // 3. Hideout with Booze Generator module selected
            mainWindow.NavigateToHideout("5d494a3f5b56502f18c98a0e");
            DoEvents(2500);
            string hideoutPath = Path.Combine(screenshotsDir, "hideout.png");
            SaveVisualToPng(mainWindow, hideoutPath);
            Log($"Saved hideout.png: {File.Exists(hideoutPath)} (size: {new FileInfo(hideoutPath).Length})");

            mainWindow.Close();
            Log("Completed successfully");
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION: {ex}\n{ex.StackTrace}");
        }
        finally
        {
            app.Shutdown(0);
        }
    }

    private static void DoEvents(int milliseconds)
    {
        var start = DateTime.UtcNow;
        while ((DateTime.UtcNow - start).TotalMilliseconds < milliseconds)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(ExitFrame), frame);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(25);
        }
    }

    private static object? ExitFrame(object? frame)
    {
        if (frame is DispatcherFrame df)
        {
            df.Continue = false;
        }
        return null;
    }

    private static void SaveVisualToPng(Visual visual, string filePath)
    {
        if (visual is FrameworkElement fe)
        {
            fe.UpdateLayout();
        }

        int width = visual is FrameworkElement feW && feW.ActualWidth > 0 ? (int)feW.ActualWidth : 1440;
        int height = visual is FrameworkElement feH && feH.ActualHeight > 0 ? (int)feH.ActualHeight : 850;

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(filePath);
        encoder.Save(fs);
    }
}
