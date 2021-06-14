using System;
using System.Threading.Tasks;

using Meadow.Helpers;
using MeadowCLI.DeviceManagement;

namespace Meadow
{
    static class MeadowProvider
    {
        public static Task<MeadowSerialDevice> GetMeadowSerialDeviceAsync()
        {
            MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath);

            if (string.IsNullOrEmpty(settings.DeviceTarget))
            {
                throw new Exception("Device has not been selected. Hit Ctrl+Shift+M to access the Device list.");
            }

            var attachedDevices = MeadowDeviceManager.FindSerialDevices();
            if (!attachedDevices.Contains(settings.DeviceTarget))
            {
                throw new Exception($"Device on '{settings.DeviceTarget}' is not connected or busy.");
            }

            return MeadowDeviceManager.GetMeadowForSerialPort(settings.DeviceTarget, existing: true);
        }
    }
}
