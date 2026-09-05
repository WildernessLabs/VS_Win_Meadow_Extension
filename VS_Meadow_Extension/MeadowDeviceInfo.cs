using System;

namespace Meadow
{
    /// <summary>
    /// Represents detailed information about a connected Meadow device.
    /// </summary>
    public class MeadowDeviceInfo
    {
        /// <summary>
        /// Gets or sets the COM port (e.g., "COM3").
        /// </summary>
        public string Port { get; set; }

        /// <summary>
        /// Gets or sets the friendly device name (e.g., "Meadow F7 Micro").
        /// </summary>
        public string DeviceName { get; set; }

        /// <summary>
        /// Gets or sets the device status.
        /// </summary>
        public DeviceStatus Status { get; set; }

        /// <summary>
        /// Gets or sets the device serial number (if available).
        /// </summary>
        public string SerialNumber { get; set; }

        /// <summary>
        /// Gets or sets the firmware version (if available).
        /// </summary>
        public string FirmwareVersion { get; set; }

        /// <summary>
        /// Gets or sets the last time this device was selected.
        /// </summary>
        public DateTime? LastUsedTimestamp { get; set; }

        /// <summary>
        /// Gets a formatted display string for the device.
        /// Format: "DeviceName [Port]"
        /// </summary>
        public string DisplayName
        {
            get
            {
                try
                {
                    var port = Port ?? "Unknown";
                    
                    if (string.IsNullOrEmpty(DeviceName))
                    {
                        return port;
                    }
                    return $"{DeviceName} [{port}]";
                }
                catch
                {
                    return "Unknown Device";
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MeadowDeviceInfo"/> class.
        /// </summary>
        public MeadowDeviceInfo()
        {
            Status = DeviceStatus.Unknown;
        }

        /// <summary>
        /// Creates a basic device info from just a port name.
        /// </summary>
        public static MeadowDeviceInfo FromPort(string port)
        {
            return new MeadowDeviceInfo
            {
                Port = port,
                DeviceName = "Meadow Device",
                Status = DeviceStatus.Available
            };
        }
    }

    /// <summary>
    /// Represents the connection status of a Meadow device.
    /// </summary>
    public enum DeviceStatus
    {
        /// <summary>
        /// Device status is unknown.
        /// </summary>
        Unknown,

        /// <summary>
        /// Device is available for connection.
        /// </summary>
        Available,

        /// <summary>
        /// Device is currently connected and in use.
        /// </summary>
        Connected,

        /// <summary>
        /// Device is busy or locked by another process.
        /// </summary>
        Busy,

        /// <summary>
        /// Device has encountered an error.
        /// </summary>
        Error
    }
}
