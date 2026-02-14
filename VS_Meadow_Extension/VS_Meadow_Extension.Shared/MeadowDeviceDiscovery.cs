using Meadow.CLI;
using Meadow.CLI.Commands.DeviceManagement;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;

namespace Meadow
{
    /// <summary>
    /// Service for discovering and retrieving detailed information about connected Meadow devices.
    /// </summary>
    internal static class MeadowDeviceDiscovery
    {
        private static readonly object _cacheLock = new object();
        private static Dictionary<string, MeadowDeviceInfo> _deviceCache;
        private static DateTime _lastCacheUpdate = DateTime.MinValue;
        private static readonly TimeSpan CacheExpiration = TimeSpan.FromSeconds(5);

        static MeadowDeviceDiscovery()
        {
            _deviceCache = new Dictionary<string, MeadowDeviceInfo>();
        }

        /// <summary>
        /// Gets detailed information about all connected Meadow devices.
        /// </summary>
        /// <param name="forceRefresh">If true, bypasses the cache and queries devices directly.</param>
        /// <returns>A list of MeadowDeviceInfo objects.</returns>
        public static async Task<List<MeadowDeviceInfo>> GetDetailedDeviceInfoAsync(bool forceRefresh = false)
        {
            try
            {
                // Use cache if available and not expired
                if (!forceRefresh)
                {
                    lock (_cacheLock)
                    {
                        if (_deviceCache != null && _deviceCache.Count > 0 && DateTime.Now - _lastCacheUpdate < CacheExpiration)
                        {
                            return _deviceCache.Values.Where(d => d != null).OrderBy(d => d.Port).ToList();
                        }
                    }
                }

                var devices = new List<MeadowDeviceInfo>();

                try
                {
                    // Get COM ports from Meadow.CLI
                    var portList = await MeadowConnectionManager.GetSerialPorts();

                    if (portList != null && portList.Count > 0)
                    {
                        // Enrich each port with additional information
                        foreach (var port in portList)
                        {
                            if (string.IsNullOrEmpty(port))
                                continue;

                            var deviceInfo = await GetDeviceInfoForPortAsync(port);
                            if (deviceInfo != null)
                            {
                                devices.Add(deviceInfo);

                                // Update cache with lock
                                lock (_cacheLock)
                                {
                                    if (_deviceCache == null)
                                    {
                                        _deviceCache = new Dictionary<string, MeadowDeviceInfo>();
                                    }

                                    if (_deviceCache.ContainsKey(port))
                                    {
                                        // Preserve last used timestamp if it exists
                                        var existing = _deviceCache[port];
                                        if (existing != null && existing.LastUsedTimestamp.HasValue)
                                        {
                                            deviceInfo.LastUsedTimestamp = existing.LastUsedTimestamp;
                                        }
                                    }
                                    _deviceCache[port] = deviceInfo;
                                }
                            }
                        }

                        // Update cache timestamp and clean up disconnected devices
                        lock (_cacheLock)
                        {
                            _lastCacheUpdate = DateTime.Now;

                            if (_deviceCache != null)
                            {
                                var disconnectedPorts = _deviceCache.Keys.Except(portList).ToList();
                                foreach (var port in disconnectedPorts)
                                {
                                    _deviceCache.Remove(port);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error discovering Meadow devices: {ex.Message}");
                }

                return devices.Where(d => d != null).OrderBy(d => d?.Port ?? "").ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fatal error in GetDetailedDeviceInfoAsync: {ex.Message}\\n{ex.StackTrace}");
                return new List<MeadowDeviceInfo>();
            }
        }

        /// <summary>
        /// Gets detailed information for a specific COM port.
        /// </summary>
        private static async Task<MeadowDeviceInfo> GetDeviceInfoForPortAsync(string port)
        {
            if (string.IsNullOrEmpty(port))
            {
                return null;
            }

            var deviceInfo = new MeadowDeviceInfo
            {
                Port = port,
                DeviceName = "Meadow",
                Status = DeviceStatus.Available
            };

            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        // Test if port is accessible
                        try
                        {
                            using (var serialPort = new SerialPort(port))
                            {
                                serialPort.ReadTimeout = 100;
                                serialPort.WriteTimeout = 100;
                                serialPort.Open();
                                serialPort.Close();
                                deviceInfo.Status = DeviceStatus.Available;
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Port exists but is in use
                            deviceInfo.Status = DeviceStatus.Busy;
                        }
                        catch (Exception)
                        {
                            // Port exists but has issues
                            deviceInfo.Status = DeviceStatus.Unknown;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error getting device info for {port}: {ex.Message}");
                        deviceInfo.Status = DeviceStatus.Unknown;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetDeviceInfoForPortAsync for {port}: {ex.Message}");
                deviceInfo.Status = DeviceStatus.Unknown;
            }

            return deviceInfo;
        }

        /// <summary>
        /// Updates the last used timestamp for a specific device.
        /// </summary>
        public static void MarkDeviceAsUsed(string port)
        {
            if (_deviceCache.ContainsKey(port))
            {
                _deviceCache[port].LastUsedTimestamp = DateTime.Now;
            }
        }

        /// <summary>
        /// Gets display-friendly formatted string for a device.
        /// Format: "Icon DeviceName [Port]" where icon indicates status.
        /// </summary>
        public static string GetDeviceDisplayString(MeadowDeviceInfo device)
        {
            if (device == null) return string.Empty;

            var statusIcon = GetStatusIcon(device.Status);
            return $"{statusIcon} {device.DisplayName}";
        }

        /// <summary>
        /// Gets a text icon representing the device status.
        /// ✓ = Available, ● = Connected, ⚠ = Busy, ✗ = Error, ○ = Unknown
        /// </summary>
        private static string GetStatusIcon(DeviceStatus status)
        {
            switch (status)
            {
                case DeviceStatus.Available:
                    return "✓";
                case DeviceStatus.Connected:
                    return "●";
                case DeviceStatus.Busy:
                    return "⚠";
                case DeviceStatus.Error:
                    return "✗";
                default:
                    return "○";
            }
        }

        /// <summary>
        /// Parses a formatted display string back to the COM port.
        /// Handles format: "Icon DeviceName [COMxx]" or "Icon [COMxx]"
        /// </summary>
        public static string ParsePortFromDisplayString(string displayString)
        {
            if (string.IsNullOrEmpty(displayString))
                return null;

            // Extract port from format: "✓ Meadow Device [COM3] - Available"
            var startIndex = displayString.IndexOf('[');
            var endIndex = displayString.IndexOf(']');

            if (startIndex >= 0 && endIndex > startIndex)
            {
                return displayString.Substring(startIndex + 1, endIndex - startIndex - 1).Trim();
            }

            // Fallback: look for "COM" pattern
            var parts = displayString.Split(new[] { ' ', '[', ']', '-' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                {
                    return part;
                }
            }

            return null;
        }
    }
}
