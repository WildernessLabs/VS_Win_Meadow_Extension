using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Meadow
{
    /// <summary>
    /// Implements IDebugPortSupplier2 to provide Meadow devices to Visual Studio's native debug port discovery.
    /// This allows Meadow devices to appear in VS debug dialogs (device picker, attach to process, etc).
    /// </summary>
    [ComVisible(true)]
    [Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890")]
    public class MeadowDebugPortSupplier : IDebugPortSupplier2
    {
        private const string PortSupplierName = "Meadow Devices";
        private const string PortSupplierDescription = "Meadow Device Port Supplier";

        /// <summary>
        /// Gets the port supplier name and description.
        /// </summary>
        public int GetPortSupplierName(out string pbstrName)
        {
            pbstrName = PortSupplierName;
            return VSConstants.S_OK;
        }

        /// <summary>
        /// Enumerates all Meadow debug ports (devices).
        /// </summary>
        public int EnumPorts(out IEnumDebugPorts2 ppEnum)
        {
            try
            {
                var devices = GetMeadowDevices().Result;
                var ports = devices
                    .Select(d => (IDebugPort2)new MeadowDebugPort(d))
                    .ToArray();

                ppEnum = new DebugPortEnumerator(ports);
                return VSConstants.S_OK;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MeadowDebugPortSupplier] ERROR in EnumPorts: {ex.Message}");
                ppEnum = null;
                return VSConstants.E_FAIL;
            }
        }

        /// <summary>
        /// Gets a specific port by name.
        /// </summary>
        public int GetPort(IDebugPortRequest2 pPortRequest, out IDebugPort2 ppPort)
        {
            ppPort = null;
            return 1; // E_NOTIMPL
        }

        /// <summary>
        /// Gets a specific port by GUID.
        /// </summary>
        public int GetPort(ref Guid guidPort, out IDebugPort2 ppPort)
        {
            ppPort = null;
            return 1; // E_NOTIMPL
        }

        /// <summary>
        /// Indicates whether ports can be added to this supplier.
        /// </summary>
        public int CanAddPort()
        {
            return 1; // E_NOTIMPL - ports are enumerated, not manually added
        }

        /// <summary>
        /// Enumerates all available ports for this supplier.
        /// </summary>
        public int EnumPersistedPorts(BSTR_ARRAY portNames, out IEnumDebugPorts2 ppEnum)
        {
            ppEnum = null;
            return 1; // E_NOTIMPL
        }

        /// <summary>
        /// Called by VS to verify the port supplier can handle the specified port.
        /// </summary>
        public int CanPersistPort(IDebugPort2 pPort)
        {
            return 1; // E_NOTIMPL
        }

        public int RemovePort(IDebugPort2 pPort)
        {
            return 1; // E_NOTIMPL
        }

        /// <summary>
        /// Gets detailed information about the port supplier.
        /// </summary>
        public int GetPortSupplierId(out Guid pguidPortSupplier)
        {
            // Unique GUID for Meadow port supplier
            pguidPortSupplier = new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
            return VSConstants.S_OK;
        }

        public int SetServer(IDebugCoreServer2 pServer)
        {
            return VSConstants.S_OK;
        }

        /// <summary>
        /// Adds a new port to the port supplier (not implemented - ports are enumerated).
        /// </summary>
        public int AddPort(IDebugPortRequest2 pPortRequest, out IDebugPort2 ppPort)
        {
            ppPort = null;
            return 1; // E_NOTIMPL
        }

        private async Task<List<MeadowDeviceInfo>> GetMeadowDevices()
        {
            try
            {
                return await MeadowDeviceDiscovery.GetDetailedDeviceInfoAsync(forceRefresh: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MeadowDebugPortSupplier] ERROR getting devices: {ex.Message}");
                return new List<MeadowDeviceInfo>();
            }
        }
    }

    /// <summary>
    /// Enumerator for debug ports (Meadow devices).
    /// </summary>
    public class DebugPortEnumerator : IEnumDebugPorts2
    {
        private readonly IDebugPort2[] _ports;
        private int _currentIndex = 0;

        public DebugPortEnumerator(IDebugPort2[] ports)
        {
            _ports = ports ?? new IDebugPort2[0];
        }

        public int Next(uint celt, IDebugPort2[] rgelt, ref uint pceltFetched)
        {
            if (rgelt == null)
                return 1; // E_INVALIDARG

            uint fetched = 0;
            for (uint i = 0; i < celt && _currentIndex < _ports.Length; i++)
            {
                rgelt[i] = _ports[_currentIndex++];
                fetched++;
            }

            pceltFetched = fetched;
            return fetched == celt ? VSConstants.S_OK : VSConstants.S_FALSE;
        }

        public int Skip(uint celt)
        {
            _currentIndex += (int)celt;
            return VSConstants.S_OK;
        }

        public int Reset()
        {
            _currentIndex = 0;
            return VSConstants.S_OK;
        }

        public int Clone(out IEnumDebugPorts2 ppEnum)
        {
            ppEnum = new DebugPortEnumerator(_ports);
            return VSConstants.S_OK;
        }

        public int GetCount(out uint pcelt)
        {
            pcelt = (uint)_ports.Length;
            return VSConstants.S_OK;
        }
    }
}
