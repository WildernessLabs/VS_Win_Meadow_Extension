using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Meadow
{
    public static class Globals
    {
        public const string AssemblyVersion = "2.0.0.0";

        public const string MeadowCapability = "Meadow";

        public static string SettingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WildernessLabs", ".meadowsettings");
        public static string ExtensionLogFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WildernessLabs", "extension.log");
        public static string FirmwareDownloadsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WildernessLabs", "Firmware");
    }
}
