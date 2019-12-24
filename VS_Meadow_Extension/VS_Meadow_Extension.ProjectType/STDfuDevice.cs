using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Meadow
{
    public class STDfuDevice : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct DFU_FUNCTIONAL_DESCRIPTOR
        {
            public byte bLength;
            public byte bDescriptorType;
            public byte bmAttributes;
            public ushort wDetachTimeOut;
            public ushort wTransferSize;
            public ushort bcdDFUVersion;
        };

        [StructLayout(LayoutKind.Sequential)]
        struct DFU_STATUS
        {
            public byte bStatus;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public byte[] bwPollTimeout;
            public byte bState;
            public byte iString;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct USB_DEVICE_DESCRIPTOR
        {
            public byte bLength;
            public byte bDescriptorType;
            public ushort bcdUSB;
            public byte bDeviceClass;
            public byte bDeviceSubClass;
            public byte bDeviceProtocol;
            public byte bMaxPacketSize0;
            public ushort idVendor;
            public ushort idProduct;
            public ushort bcdDevice;
            public byte iManufacturer;
            public byte iProduct;
            public byte iSerialNumber;
            public byte bNumConfigurations;
        };

        const byte ATTR_DNLOAD_CAPABLE = 0x01;
        const byte ATTR_UPLOAD_CAPABLE = 0x02;
        const byte ATTR_MANIFESTATION_TOLERANT = 0x04;
        const byte ATTR_WILL_DETACH = 0x08;
        const byte ATTR_ST_CAN_ACCELERATE = 0x80;

        const byte STATE_IDLE = 0x00;
        //const byte STATE_DETACH = 0x01;
        const byte STATE_DFU_IDLE = 0x02;
        //const byte STATE_DFU_DOWNLOAD_SYNC = 0x03;
        const byte STATE_DFU_DOWNLOAD_BUSY = 0x04;
        const byte STATE_DFU_DOWNLOAD_IDLE = 0x05;
        //const byte STATE_DFU_MANIFEST_SYNC = 0x06;
        const byte STATE_DFU_MANIFEST = 0x07;
        //const byte STATE_DFU_MANIFEST_WAIT_RESET = 0x08;
        const byte STATE_DFU_UPLOAD_IDLE = 0x09;
        //const byte STATE_DFU_ERROR = 0x0A;
        //const byte STATE_DFU_UPLOAD_SYNC = 0x91;
        const byte STATE_DFU_UPLOAD_BUSY = 0x92;

        const byte STATUS_OK = 0x00;
        //const byte STATUS_errTARGET = 0x01;
        //const byte STATUS_errFILE = 0x02;
        //const byte STATUS_errWRITE = 0x03;
        //const byte STATUS_errERASE = 0x04;
        //const byte STATUS_errCHECK_ERASE = 0x05;
        //const byte STATUS_errPROG = 0x06;
        //const byte STATUS_errVERIFY = 0x07;
        //const byte STATUS_errADDRESS = 0x08;
        //const byte STATUS_errNOTDONE = 0x09;
        //const byte STATUS_errFIRMWARE = 0x0A;
        //const byte STATUS_errVENDOR = 0x0B;
        //const byte STATUS_errUSBR = 0x0C;
        //const byte STATUS_errPOR = 0x0D;
        //const byte STATUS_errUNKNOWN = 0x0E;
        //const byte STATUS_errSTALLEDPKT = 0x0F;

        const uint STDFU_ERROR_OFFSET = 0x12340000;
        const uint STDFU_NOERROR = STDFU_ERROR_OFFSET;
        const uint STDFU_DESCRIPTORNOTFOUND = STDFU_ERROR_OFFSET + 0x14;

        [DllImport("stdfu.dll")]
        static extern uint STDFU_Close(
            ref IntPtr phDevice
            );

        [DllImport("stdfu.dll")]
        static extern uint STDFU_Clrstatus(
            ref IntPtr phDevice
            );

        [DllImport("stdfu.dll", CharSet = CharSet.Ansi)]
        static extern uint STDFU_Dnload(
            ref IntPtr phDevice,
            [MarshalAs(UnmanagedType.LPArray)] byte[] pBuffer,
            uint nBytes,
            ushort nBlock
            );

        [DllImport("stdfu.dll")]
        static extern uint STDFU_GetDeviceDescriptor(
            ref IntPtr phDevice,
            ref USB_DEVICE_DESCRIPTOR pDesc
            );

        [DllImport("stdfu.dll")]
        static extern uint STDFU_GetDFUDescriptor(
            ref IntPtr phDevice,
            ref uint pDFUInterfaceNum,
            ref uint pNbOfAlternates,
            ref DFU_FUNCTIONAL_DESCRIPTOR pDesc
            );

        [DllImport("stdfu.dll")]
        static extern uint STDFU_Getstate(
            ref IntPtr phDevice,
            out byte pState
            );

        [DllImport("stdfu.dll")]
        static extern uint STDFU_Getstatus(
            ref IntPtr phDevice,
            ref DFU_STATUS DfuStatus
            );

        [DllImport("stdfu.dll", CharSet = CharSet.Ansi)]
        static extern uint STDFU_Open(
            [MarshalAs(UnmanagedType.LPStr)] string szDevicePath,
            out IntPtr phDevice
            );

        [DllImport("stdfu.dll", CharSet = CharSet.Ansi)]
        static extern uint STDFU_Upload(
            ref IntPtr phDevice,
            [MarshalAs(UnmanagedType.LPArray)] byte[] pBuffer,
            uint nBytes,
            ushort nBlock
            );

        TimeSpan _timeoutClearState = new TimeSpan(0, 0, 10); // 10 seconds maximum timeout waiting for state to clear back to STATE_DFU_IDLE.

        IntPtr _handle = IntPtr.Zero;
        bool _isClosed = false;
        ushort _blockTransferSize = 0;

        // attributes
        bool _isDownloadCapable = false;
        bool _isUploadCapable = false;
        bool _isWillDetach = false;
        bool _isManifestationTolerant = false;
        bool _isStCanAccelerate = false;

        byte _downloadCommandSetAddressPointer = 0x21;
        byte _downloadCommandErase = 0x41;
        byte _downloadCommandReadUnprotect = 0x92;

        // NOTE: STDFU_GetDFUDescriptor_Proxy handles the scenario where STDFU_GetDFUDescriptor never returns.
        uint STDFU_GetDFUDescriptor_Proxy(
            ref IntPtr phDevice,
            ref uint pDFUInterfaceNum,
            ref uint pNbOfAlternates,
            ref DFU_FUNCTIONAL_DESCRIPTOR pDesc)
        {
            uint returnValue = STDFU_DESCRIPTORNOTFOUND; /* NOTE: this appears to be the most appropriate error code. */

            IntPtr temp_phDevice = phDevice;
            uint temp_pDFUInterfaceNum = pDFUInterfaceNum;
            uint temp_pNbOfAlternates = pNbOfAlternates;
            DFU_FUNCTIONAL_DESCRIPTOR temp_pDesc = pDesc;

            System.Threading.AutoResetEvent waitForComplete = new System.Threading.AutoResetEvent(false);
            var functionThread = new System.Threading.Thread(
                delegate ()
                {
                    returnValue = STDFU_GetDFUDescriptor(ref temp_phDevice, ref temp_pDFUInterfaceNum, ref temp_pNbOfAlternates, ref temp_pDesc);
                    waitForComplete.Set();
                }
                );
            functionThread.Start();

            bool success = waitForComplete.WaitOne(100); // wait 100 ms for completion
            if (!success)
                functionThread.Abort(); /* NOTE: in our experience, this does not actually abort the thread. */

            phDevice = temp_phDevice;
            pDFUInterfaceNum = temp_pDFUInterfaceNum;
            pNbOfAlternates = temp_pNbOfAlternates;
            pDesc = temp_pDesc;

            return returnValue;
        }

        public STDfuDevice(string devicePath)
        {
            if (STDFU_Open(devicePath, out _handle) != STDFU_NOERROR)
                throw new Exception(); // exception: could not open connection to device

            try
            {
                // retrieve our device descriptor (to retrieve STDFU version)
                USB_DEVICE_DESCRIPTOR usbDeviceDescriptor = new USB_DEVICE_DESCRIPTOR();
                if (STDFU_GetDeviceDescriptor(ref _handle, ref usbDeviceDescriptor) != STDFU_NOERROR)
                    throw new Exception(); // exception: could not retrieve device descriptor

                // retrieve our DFU functional desscriptor
                uint dfuInterfaceNum = 0;
                uint nbOfAlternates = 0;
                DFU_FUNCTIONAL_DESCRIPTOR dfuFunctionalDescriptor = new DFU_FUNCTIONAL_DESCRIPTOR();
                if (STDFU_GetDFUDescriptor_Proxy(ref _handle, ref dfuInterfaceNum, ref nbOfAlternates, ref dfuFunctionalDescriptor) != STDFU_NOERROR)
                    //if (STDFU_GetDFUDescriptor(ref _handle, ref dfuInterfaceNum, ref nbOfAlternates, ref dfuFunctionalDescriptor) != STDFU_NOERROR)
                    throw new Exception(); // exception: could not retrieve DFU descriptor

                // retrieve our block transfer size (# of bytes per block in upload/download requests)
                _blockTransferSize = dfuFunctionalDescriptor.wTransferSize;

                // verify that our DFU protocol version is valid.
                if (dfuFunctionalDescriptor.bcdDFUVersion < 0x011A || dfuFunctionalDescriptor.bcdDFUVersion > 0x0120)
                    throw new Exception(); // unknown DFU protocol version

                // retrieve our attributes (supported operations, etc.)
                if ((dfuFunctionalDescriptor.bmAttributes & ATTR_DNLOAD_CAPABLE) > 0)
                    _isDownloadCapable = true;
                if ((dfuFunctionalDescriptor.bmAttributes & ATTR_UPLOAD_CAPABLE) > 0)
                    _isUploadCapable = true;
                if ((dfuFunctionalDescriptor.bmAttributes & ATTR_WILL_DETACH) > 0)
                    _isWillDetach = true;
                if ((dfuFunctionalDescriptor.bmAttributes & ATTR_MANIFESTATION_TOLERANT) > 0)
                    _isManifestationTolerant = true;
                if ((dfuFunctionalDescriptor.bmAttributes & ATTR_ST_CAN_ACCELERATE) > 0)
                    _isStCanAccelerate = true;

                byte[] commands = new byte[4];
                if (GetDownloadCommands(out commands) == true && commands.Length >= 4)
                {
                    // we were able to load the command bytes for DOWNLOAD functions; set them now.
                    _downloadCommandSetAddressPointer = commands[1];
                    _downloadCommandErase = commands[2];
                    _downloadCommandReadUnprotect = commands[3];
                }
                else
                {
                    // use defaults
                }
            }
            catch
            {
                STDFU_Close(ref _handle);
            }

        }

        public ushort BlockTransferSize
        {
            get
            {
                return _blockTransferSize;
            }
        }

        // returns true if successful
        public bool EraseAllSectors()
        {
            DFU_STATUS dfuStatus = new DFU_STATUS();

            STDFU_Getstatus(ref _handle, ref dfuStatus);
            if (dfuStatus.bState != STATE_DFU_IDLE && dfuStatus.bState != STATE_DFU_DOWNLOAD_IDLE)
            {
                // reset our dfu state to idle, preparing for the current operation.
                if (ResetDfuStateToIdle() == false)
                    return false; // timeout resetting our DFU state; we will not be able to execute this operation
            }

            // call STDFU_Dnload with the Erase command (0x41) only; this will set up the mass erase operation.
            STDFU_Dnload(ref _handle, new byte[] {
                    0x41
                }, 1, 0);
            // call STDFU_Getstatus to begin the erase
            STDFU_Getstatus(ref _handle, ref dfuStatus);
            // DFU state should now be "download busy"
            if (dfuStatus.bState != STATE_DFU_DOWNLOAD_BUSY)
            {
                // erase operation was unable to begin.
                ResetDfuStateToIdle();
                return false;
            }
            // call STDFU_Getstatus again to complete the erase
            STDFU_Getstatus(ref _handle, ref dfuStatus);
            // DFU state should now be "download idle"
            if (dfuStatus.bState != STATE_DFU_DOWNLOAD_IDLE || dfuStatus.bStatus != STATUS_OK)
            {
                // erase operation failed.
                ResetDfuStateToIdle();
                return false;
            }

            // all sectors have been succesfully erased; return true.
            return true;
        }

        // returns true if successful
        public bool EraseSector(uint sectorBaseAddress)
        {
            DFU_STATUS dfuStatus = new DFU_STATUS();

            STDFU_Getstatus(ref _handle, ref dfuStatus);
            if (dfuStatus.bState != STATE_DFU_IDLE && dfuStatus.bState != STATE_DFU_DOWNLOAD_IDLE)
            {
                // reset our dfu state to idle, preparing for the current operation.
                if (ResetDfuStateToIdle() == false)
                    return false; // timeout resetting our DFU state; we will not be able to execute this operation
            }

            // call STDFU_Dnload with the Erase command (0x41) and sector base address; this will set up the erase operation.
            STDFU_Dnload(ref _handle, new byte[] {
                    0x41,
                    (byte)(sectorBaseAddress & 0xFF),
                    (byte)((sectorBaseAddress >> 08) & 0xFF),
                    (byte)((sectorBaseAddress >> 16) & 0xFF),
                    (byte)((sectorBaseAddress >> 24) & 0xFF)
                }, 5, 0);
            // call STDFU_Getstatus to begin the erase
            STDFU_Getstatus(ref _handle, ref dfuStatus);
            // DFU state should now be "download busy"
            if (dfuStatus.bState != STATE_DFU_DOWNLOAD_BUSY)
            {
                // erase operation was unable to begin.
                ResetDfuStateToIdle();
                return false;
            }
            // call STDFU_Getstatus again to complete the erase
            STDFU_Getstatus(ref _handle, ref dfuStatus);
            // DFU state should now be "download idle"
            if (dfuStatus.bState != STATE_DFU_DOWNLOAD_IDLE || dfuStatus.bStatus != STATUS_OK)
            {
                // erase operation failed.
                ResetDfuStateToIdle();
                return false;
            }

            // sector has been succesfully erased; return true.
            return true;
        }

        // returns true if successful
        private bool GetDownloadCommands(out byte[] commands)
        {
            commands = new byte[4];

            DFU_STATUS dfuStatus = new DFU_STATUS();

            STDFU_Getstatus(ref _handle, ref dfuStatus);
            if (dfuStatus.bState != STATE_DFU_IDLE && dfuStatus.bState != STATE_DFU_UPLOAD_IDLE)
            {
                // reset our dfu state to idle, preparing for the current operation.
                if (ResetDfuStateToIdle() == false)
                    return false; // timeout resetting our DFU state; we will not be able to execute this operation
            }

            // call STDFU_Upload with nBlock=0; this will set up the operation.
            STDFU_Upload(ref _handle, commands, (uint)commands.Length, 0);
            // call STDFU_Getstatus to begin the operation
            STDFU_Getstatus(ref _handle, ref dfuStatus);
            // DFU state should now be "upload busy"
            while (dfuStatus.bState == STATE_DFU_UPLOAD_BUSY)
            {
                STDFU_Clrstatus(ref _handle);
                STDFU_Getstatus(ref _handle, ref dfuStatus);
            }
            // DFU state should now be "upload idle"
            if (dfuStatus.bState != STATE_DFU_IDLE)
            {
                // operation failed.
                ResetDfuStateToIdle();
                return false;
            }

            // operation succeeded; return true.
            return true;
        }

        // returns true if successful
        public bool LeaveDfuMode()
        {
            DFU_STATUS dfuStatus = new DFU_STATUS();

            STDFU_Getstatus(ref _handle, ref dfuStatus);
            if (dfuStatus.bState != STATE_DFU_IDLE && dfuStatus.bState != STATE_DFU_DOWNLOAD_IDLE)
            {
                // reset our dfu state to idle, preparing for the current operation.
                if (ResetDfuStateToIdle() == false)
                    return false; // timeout resetting our DFU state; we will not be able to execute this operation
            }

            // call STDFU_Dnload with an empty command; this will set up the operation.
            STDFU_Dnload(ref _handle, new byte[] {
                }, 0, 0);
            // call STDFU_Getstatus to begin the operation
            STDFU_Getstatus(ref _handle, ref dfuStatus);
            // DFU state should now be "manifest"
            if (dfuStatus.bState != STATE_DFU_MANIFEST)
            {
                // operation was unable to begin.
                ResetDfuStateToIdle();
                return false;
            }
            // call STDFU_Getstatus again to verify that the operation did not error out.  State will be IDLE or, if the device has already detached, DFU_DOWNLOAD_BUSY
            STDFU_Getstatus(ref _handle, ref dfuStatus);
            if (dfuStatus.bState != STATE_IDLE && dfuStatus.bState != STATE_DFU_MANIFEST)
            {
                // operation failed.
                ResetDfuStateToIdle();
                return false;
            }

            // the operation has successfully completed; return true.
            return true;
        }

        // returns true if successful
        public bool ReadMemoryBlock(ushort blockNumber, byte[] buffer)
        {
            if (!_isUploadCapable)
                throw new NotSupportedException();

            DFU_STATUS dfuStatus = new DFU_STATUS();

            STDFU_Getstatus(ref _handle, ref dfuStatus);
            if (dfuStatus.bState != STATE_DFU_IDLE && dfuStatus.bState != STATE_DFU_UPLOAD_IDLE)
            {
                // reset our dfu state to idle, preparing for the current operation.
                if (ResetDfuStateToIdle() == false)
                    return false; // timeout resetting our DFU state; we will not be able to execute this operation
            }

            // call STDFU_Upload with the blockNumber+2; this will set up the operation.
            STDFU_Upload(ref _handle, buffer, (uint)buffer.Length, (ushort)(blockNumber + 2));
            // call STDFU_Getstatus to begin the operation
            STDFU_Getstatus(ref _handle, ref dfuStatus);
            // DFU state should now be "upload busy"
            while (dfuStatus.bState == STATE_DFU_UPLOAD_BUSY)
            {
                STDFU_Clrstatus(ref _handle);
                STDFU_Getstatus(ref _handle, ref dfuStatus);
            }
            // DFU state should now be "upload idle"
            if (dfuStatus.bState != STATE_DFU_UPLOAD_IDLE || dfuStatus.bStatus != STATUS_OK)
            {
                // operation failed.
                ResetDfuStateToIdle();
                return false;
            }

            // operation succeeded; return true.
            return true;
        }

        // returns true if successful
        public bool ReadUnprotect()
        {
            DFU_STATUS dfuStatus = new DFU_STATUS();

            STDFU_Getstatus(ref _handle, ref dfuStatus);
            if (dfuStatus.bState != STATE_DFU_IDLE && dfuStatus.bState != STATE_DFU_DOWNLOAD_IDLE)
            {
                // reset our dfu state to idle, preparing for the current operation.
                if (ResetDfuStateToIdle() == false)
                    return false; // timeout resetting our DFU state; we will not be able to execute this operation
            }

            // call STDFU_Dnload with the Read Unprotect command (0x92) only; this will set up the operation.
            STDFU_Dnload(ref _handle, new byte[] {
                    0x92
                }, 1, 0);
            // call STDFU_Getstatus to begin the operation
            STDFU_Getstatus(ref _handle, ref dfuStatus);
            // DFU state should now be "download busy"
            if (dfuStatus.bState != STATE_DFU_DOWNLOAD_BUSY)
            {
                // operation was unable to begin.
                ResetDfuStateToIdle();
                return false;
            }
            // call STDFU_Getstatus again to verify that the operation did not error out.  State will be IDLE or, if the device has already detached, DFU_DOWNLOAD_BUSY
            STDFU_Getstatus(ref _handle, ref dfuStatus);
            if (dfuStatus.bState != STATE_IDLE && dfuStatus.bState != STATE_DFU_DOWNLOAD_BUSY)
            {
                // operation failed.
                ResetDfuStateToIdle();
                return false;
            }

            // the operation has successfully completed; return true.
            return true;
        }

        private bool ResetDfuStateToIdle()
        {
            byte dfuState;

            // force our DFU state machine to STATE_DFU_IDLE, ignoring any error conditions.  Wait up to _timeoutClearState timespan.
            Stopwatch stopwatch = Stopwatch.StartNew();
            STDFU_Getstate(ref _handle, out dfuState);
            while (dfuState != STATE_DFU_IDLE)
            {
                if (stopwatch.Elapsed.CompareTo(_timeoutClearState) > 0)
                    break; // timeout

                STDFU_Clrstatus(ref _handle);
                STDFU_Getstate(ref _handle, out dfuState);
            }

            // return success or failure
            return (dfuState == STATE_DFU_IDLE);
        }

        // returns true if successful
        public bool SetAddressPointer(uint address)
        {
            DFU_STATUS dfuStatus = new DFU_STATUS();

            STDFU_Getstatus(ref _handle, ref dfuStatus);
            if (dfuStatus.bState != STATE_DFU_IDLE && dfuStatus.bState != STATE_DFU_DOWNLOAD_IDLE)
            {
                // reset our dfu state to idle, preparing for the current operation.
                if (ResetDfuStateToIdle() == false)
                    return false; // timeout resetting our DFU state; we will not be able to execute this operation
            }

            // call STDFU_Dnload with the SetAddressPointer command (0x21) and address; this will set up the SetAddressPointer operation.
            STDFU_Dnload(ref _handle, new byte[] {
                    0x21,
                    (byte)(address & 0xFF),
                    (byte)((address >> 08) & 0xFF),
                    (byte)((address >> 16) & 0xFF),
                    (byte)((address >> 24) & 0xFF)
                }, 5, 0);
            // call STDFU_Getstatus to begin the operation
            STDFU_Getstatus(ref _handle, ref dfuStatus);
            // DFU state should now be "download busy"
            if (dfuStatus.bState != STATE_DFU_DOWNLOAD_BUSY)
            {
                // operation was unable to begin.
                ResetDfuStateToIdle();
                return false;
            }
            // call STDFU_Getstatus again to verify that the operation completed successfully
            STDFU_Getstatus(ref _handle, ref dfuStatus);
            // DFU state should now be "download idle"
            if (dfuStatus.bState != STATE_DFU_DOWNLOAD_IDLE || dfuStatus.bStatus != STATUS_OK)
            {
                // operation failed.
                ResetDfuStateToIdle();
                return false;
            }

            // address pointer has been successfully set; return true.
            return true;
        }

        // returns true if successful
        public bool WriteMemoryBlock(ushort blockNumber, byte[] buffer)
        {
            DFU_STATUS dfuStatus = new DFU_STATUS();

            STDFU_Getstatus(ref _handle, ref dfuStatus);
            if (dfuStatus.bState != STATE_DFU_IDLE && dfuStatus.bState != STATE_DFU_DOWNLOAD_IDLE)
            {
                // reset our dfu state to idle, preparing for the current operation.
                if (ResetDfuStateToIdle() == false)
                    return false; // timeout resetting our DFU state; we will not be able to execute this operation
            }

            // call STDFU_Dnload with the blockNumber+2; this will set up the operation.
            STDFU_Dnload(ref _handle, buffer, (uint)buffer.Length, (ushort)(blockNumber + 2));
            // call STDFU_Getstatus to begin the operation
            STDFU_Getstatus(ref _handle, ref dfuStatus);
            // DFU state should now be "download busy"
            if (dfuStatus.bState != STATE_DFU_DOWNLOAD_BUSY)
            {
                // operation was unable to begin.
                ResetDfuStateToIdle();
                return false;
            }
            // call STDFU_Getstatus again to verify that the operation completed successfully
            STDFU_Getstatus(ref _handle, ref dfuStatus);
            // DFU state should now be "download idle"
            if (dfuStatus.bState != STATE_DFU_DOWNLOAD_IDLE || dfuStatus.bStatus != STATUS_OK)
            {
                // operation failed.
                ResetDfuStateToIdle();
                return false;
            }

            // operation succeeded; return true.
            return true;
        }

        public void Dispose()
        {
            if (!_isClosed)
            {
                STDFU_Close(ref _handle);
                _isClosed = true;
            }
        }
    }
}
