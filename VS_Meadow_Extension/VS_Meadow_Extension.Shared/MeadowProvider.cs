using System;
using System.Linq;
using System.Threading.Tasks;
using Meadow.Helpers;
using Microsoft.Extensions.Logging;

namespace Meadow
{
    static class MeadowProvider
    {
        /*public static Task<IMeadowDevice> GetMeadowSerialDeviceAsync(ILogger logger = null)
        {
            var settings = new MeadowSettings(Globals.SettingsFilePath);

            if (string.IsNullOrEmpty(settings.DeviceTarget))
            {
                throw new Exception("Meadow device has not been selected. Select your device using the Meadow Device Selector on the toolbar.");
            }

            return MeadowDeviceManager.GetMeadowForSerialPort(settings.DeviceTarget, logger: logger);
        }*/
    }
}
