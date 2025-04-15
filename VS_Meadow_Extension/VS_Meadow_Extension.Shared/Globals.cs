using System;
using System.IO;
using System.Threading;

namespace Meadow
{
    public static class Globals
    {
        public const string AssemblyVersion = "2.0.0.15";

        public const string MeadowCapability = "Meadow";

        public static bool DebugOrDeployInProgress { get; set; } = false;

        public static string SettingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WildernessLabs", ".meadowsettings");
        public static string ExtensionLogFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WildernessLabs", "extension.log");
        public static string FirmwareDownloadsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WildernessLabs", "Firmware");

        public static CancellationTokenSource DebugDeployTokenSource { get; set; }
    }
}