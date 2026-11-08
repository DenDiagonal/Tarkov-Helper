using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TarkovHelper.Debug
{
    public static class AppEnv
    {
        #if DEBUG
        public static bool IsDebugMode { get; set; } = true;
        #else
        public static bool IsDebugMode { get; set; } = false;
        #endif

        private static bool IsTestEnvironment()
        {
            var friendly = AppDomain.CurrentDomain.FriendlyName ?? "";
            if (friendly.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0 ||
                friendly.IndexOf("vstest", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    var name = assemblies[i].GetName().Name ?? "";
                    if (name.IndexOf("testhost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("TarkovHelper.Tests", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("xunit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("nunit", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private static readonly string AppDataHelperPath =
            IsTestEnvironment()
            ? Path.Combine(Path.GetTempPath(), "TarkovHelper_UnitTests")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TarkovHelper"
            );

        public static string DataPath { get; set; } = Path.Combine(AppDataHelperPath, "Data");
        public static string CachePath { get; set; } = Path.Combine(AppDataHelperPath, "Cache");
        public static string ConfigPath { get; set; } = Path.Combine(AppDataHelperPath, "Config");
    }
}
