using System;
using System.Linq;
using System.Threading.Tasks;

using Meadow.Helpers;
using Meadow.CLI.Core.DeviceManagement;
using Meadow.CLI.Core.Devices;
using Microsoft.Extensions.Logging;

namespace Meadow
{
    static class MeadowProvider
    {
        public static Task<IMeadowDevice> GetMeadowSerialDeviceAsync(ILogger logger = null)
        {
            var settings = new MeadowSettings(Globals.SettingsFilePath);

            if (string.IsNullOrEmpty(settings.DeviceTarget))
            {
                throw new Exception("Device has not been selected. Hit Ctrl+Shift+M to access the Device list.");
            }

            return MeadowDeviceManager.GetMeadowForSerialPort(settings.DeviceTarget, logger: logger);
        }
    }
}
