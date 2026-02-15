using Microsoft.VisualStudio.Debugger.Interop;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;

namespace Meadow
{
    /// <summary>
    /// Implements IDebugPort2 to represent a single Meadow device for debugging.
    /// Each port corresponds to one connected Meadow device identified by its COM port.
    /// </summary>
    [ComVisible(true)]
    public class MeadowDebugPort : IDebugPort2
    {
        private readonly MeadowDeviceInfo _deviceInfo;

        public MeadowDebugPort(MeadowDeviceInfo deviceInfo)
        {
            _deviceInfo = deviceInfo ?? throw new ArgumentNullException(nameof(deviceInfo));
        }

        /// <summary>
        /// Gets the port name.
        /// Format: "Meadow [COM11]" or similar.
        /// </summary>
        public int GetPortName(out string pbstrName)
        {
            try
            {
                pbstrName = _deviceInfo.DisplayName ?? $"Meadow [{_deviceInfo.Port}]";
                return VSConstants.S_OK; 
            }
            catch
            {
                pbstrName = null;
                return VSConstants.E_FAIL;
            }
        }

        /// <summary>
        /// Gets the port ID (unique identifier).
        /// Uses the COM port as the unique ID.
        /// </summary>
        public int GetPortId(out Guid pguidPort)
        {
            try
            {
                // Create a deterministic GUID based on the COM port
                // This ensures the same port always has the same GUID
                pguidPort = GuidFromString($"Meadow-{_deviceInfo.Port}");
                return VSConstants.S_OK;
            }
            catch
            {
                pguidPort = Guid.Empty;
                return VSConstants.E_FAIL;
            }
        }

        /// <summary>
        /// Gets the number and type of processes running on this port.
        /// </summary>
        public int GetProcess(Guid guidProcessId, out IDebugProcess2 ppProcess)
        {
            // For now, we don't support individual process enumeration
            ppProcess = null;
            return 1; // E_NOTIMPL
        }

        /// <summary>
        /// Gets a process by its AD_PROCESS_ID.
        /// </summary>
        public int GetProcess(AD_PROCESS_ID ProcessId, out IDebugProcess2 ppProcess)
        {
            // For Meadow, we don't have traditional processes
            ppProcess = null;
            return 1; // E_NOTIMPL
        }

        /// <summary>
        /// Gets the port supplier that owns this port.
        /// </summary>
        public int GetPortSupplier(out IDebugPortSupplier2 ppSupplier)
        {
            // Return a new instance of the port supplier
            // In a real implementation, you might cache this
            ppSupplier = new MeadowDebugPortSupplier();
            return VSConstants.S_OK;
        }

        /// <summary>
        /// Enumerates processes running on this port.
        /// </summary>
        public int EnumProcesses(out IEnumDebugProcesses2 ppEnum)
        {
            // Could enumerate Meadow processes/threads here in future
            ppEnum = null;
            return 1; // E_NOTIMPL
        }

        /// <summary>
        /// Gets information about the port (COM port, status, etc).
        /// </summary>
        public int GetPortRequest(out IDebugPortRequest2 ppRequest)
        {
            ppRequest = null;
            return 1; // E_NOTIMPL
        }

        /// <summary>
        /// Gets the port supplier for this port.
        /// </summary>
        public int GetServer(out IDebugCoreServer2 ppServer)
        {
            ppServer = null;
            return 1; // E_NOTIMPL
        }

        /// <summary>
        /// Determines if the given GUID matches this port.
        /// </summary>
        public int IsPortSupplierPresent(ref Guid guidPortSupplier)
        {
            // Check if this is our Meadow port supplier
            Guid meadowSupplierGuid = new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
            return guidPortSupplier == meadowSupplierGuid ? 0 : 1;
        }

        /// <summary>
        /// Creates a deterministic GUID from a string.
        /// Uses MD5 hash to generate a stable GUID based on the input string.
        /// </summary>
        private static Guid GuidFromString(string input)
        {
            var md5 = System.Security.Cryptography.MD5.Create();
            var hash = md5.ComputeHash(System.Text.Encoding.Default.GetBytes(input));
            return new Guid(hash);
        }

        public string ComPort => _deviceInfo.Port;

        public MeadowDeviceInfo DeviceInfo => _deviceInfo;
    }
}
