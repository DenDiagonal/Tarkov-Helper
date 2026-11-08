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
    }
}
