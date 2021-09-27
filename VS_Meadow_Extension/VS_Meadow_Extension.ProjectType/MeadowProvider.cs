using System;
using System.Linq;
using System.Threading.Tasks;

using Meadow.Helpers;
using Meadow.CLI.Core.DeviceManagement;
using Meadow.CLI.Core.Devices;
using Meadow.CLI.Core.Logging;

namespace Meadow
{
    static class MeadowProvider
    {
        public static Task<IMeadowDevice> GetMeadowSerialDeviceAsync(ILogger logger = null)
        {
            MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath);

            if (string.IsNullOrEmpty(settings.DeviceTarget))
            {
                throw new Exception("Device has not been selected. Hit Ctrl+Shift+M to access the Device list.");
            }

            var attachedDevices = MeadowDeviceManager.GetSerialPorts();

            if(attachedDevices.Where(p => p.Port == settings.DeviceTarget).Any() == false)
            // if (!attachedDevices.Contains(settings.DeviceTarget))
            {
                throw new Exception($"Device on '{settings.DeviceTarget}' is not connected or busy.");
            }

            return MeadowDeviceManager.GetMeadowForSerialPort(settings.DeviceTarget, logger: logger);
        }
    }
}
