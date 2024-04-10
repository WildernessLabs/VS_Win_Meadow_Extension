using Meadow.CLI;
using Meadow.Hcom;
using System;
using System.Threading;

namespace Meadow
{
    static class MeadowConnection
    {
        static readonly int RETRY_COUNT = 10;
        static readonly int RETRY_DELAY = 500;

        internal static IMeadowConnection GetCurrentConnection()
        {
            var route = MeadowPackage.SettingsManager.GetSetting(SettingsManager.PublicSettings.Route);

            if (meadowConnection != null &&
                meadowConnection.Name == route)
            {
                return meadowConnection;
            }
            else if (meadowConnection != null)
            {
                meadowConnection.Dispose();
                meadowConnection = null;
            }

            var retryCount = 0;

        get_serial_connection:
            try
            {
                meadowConnection = new SerialConnection(route);
            }
            catch
            {
                retryCount++;
                if (retryCount > RETRY_COUNT)
                {
                    throw new Exception($"Cannot create SerialConnection on port: {route}");
                }
                Thread.Sleep(RETRY_DELAY);
                goto get_serial_connection;
            }

            return meadowConnection;
        }

        private static IMeadowConnection meadowConnection = null;
    }
}