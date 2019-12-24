namespace Meadow
{
    using EnvDTE;
    using EnvDTE80;
    using Meadow.Helpers;
    using MeadowCLI.DeviceManagement;
    using Microsoft.VisualStudio;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.Interop;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.Composition;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Linq;
    using System.Management;
    using task = System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using Microsoft.VisualStudio.Threading;
    using System.Threading.Tasks;
    using System.Net.Http;
    using Microsoft.VisualStudio.Settings.Internal;
    using Newtonsoft.Json.Linq;
    using System.Net;
    using System.IO.Compression;
    using System.Windows.Ink;

    /// <summary>
    /// Interaction logic for MeadowWindowControl.
    /// </summary>
    public partial class MeadowWindowControl : UserControl
    {
        readonly Guid DEVICE_INTERFACE_GUID_STDFU = new Guid(0x3fe809ab, 0xfb91, 0x4cb5, 0xa6, 0x43, 0x69, 0x67, 0x0d, 0x52, 0x36, 0x6e);
        readonly string osKernalFileName = "Meadow.OS_Kernel.bin";
        readonly string osRuntimeFileName = "Meadow.OS_Runtime.bin";
        static Guid windowGuid = new Guid("AD01DF73-6990-4361-8587-4FC3CB91A65F");
        readonly string versionCheckUrl = "https://s3-us-west-2.amazonaws.com/downloads.wildernesslabs.co/Meadow_Beta/latest.json";
        string latestJson = "latest.json";

        /// <summary>
        /// Initializes a new instance of the <see cref="MeadowWindowControl"/> class.
        /// </summary>
        public MeadowWindowControl()
        {
            this.InitializeComponent();
        }

        //[SuppressMessage("Microsoft.Globalization", "CA1300:SpecifyMessageBoxOptions", Justification = "Sample code")]
        //[SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:ElementMustBeginWithUpperCaseLetter", Justification = "Default event handler naming pattern")]

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshDeviceList();
        }

        public void RefreshDeviceList()
        {
            MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath);

            Devices.Items.Clear();
            Devices.Items.Add("Select Target Device Port");

            var devices = MeadowDeviceManager.FindSerialDevices();
            var selectedIndex = 0;

            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i] == settings.DeviceTarget)
                {
                    selectedIndex = i + 1;
                }
                Devices.Items.Add(devices[i]);
            }

            Devices.SelectedIndex = selectedIndex;
        }

        private void Devices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Devices.SelectedIndex == 0 || Devices.SelectedItem == null) return;
            
            MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath, false);
            settings.DeviceTarget = Devices.SelectedItem.ToString();
            settings.Save();
        }

        private async void Flash_Device(object sender, RoutedEventArgs e)
        {
            OutputMessage("Preparing to update device firmware...", true);

            List<FileInfo> files = new List<FileInfo>();
            if (Directory.Exists(Globals.FirmwareDownloadsFilePath))
            {
                DirectoryInfo di = new DirectoryInfo(Globals.FirmwareDownloadsFilePath);
                files = di.GetFiles().ToList();
            }

            if(!files.Any(x=>x.Name == osKernalFileName) || !files.Any(x=>x.Name == osRuntimeFileName) || !files.Any(x => x.Name == latestJson))
            {
                OutputMessage("Download the latest firmware before flashing the device.");
                return;
            }

            var query = "SELECT * FROM Win32_USBHub";
            ManagementObjectSearcher device_searcher = new ManagementObjectSearcher(query);
            string deviceId = string.Empty;
            foreach (ManagementObject usb_device in device_searcher.Get())
            {
                if(usb_device.Properties["Name"].Value.ToString() == "STM Device in DFU Mode")
                {
                    deviceId = usb_device.Properties["DeviceID"].Value.ToString();
                }
            }

            var payload = File.ReadAllText(Path.Combine(Globals.FirmwareDownloadsFilePath, latestJson));

            if (!string.IsNullOrEmpty(deviceId))
            {
                OutputMessage($"Deploying firmare version {GetVersionFromPayload(payload)} to device.");

                var device = new STDfuDevice($@"\\?\{deviceId.Replace("\\", "#")}#{{{DEVICE_INTERFACE_GUID_STDFU.ToString()}}}");

                await task.Task.Run(async() =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    OutputMessage("Erasing sectors");

                    await TaskScheduler.Default;
                    device.EraseAllSectors();

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    OutputMessage($"Uploading {osKernalFileName}");

                    await TaskScheduler.Default;
                    UploadFile(device, Path.Combine(Globals.FirmwareDownloadsFilePath, osKernalFileName), 0x08000000);

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    OutputMessage($"Uploading {osRuntimeFileName} (This could take up to a minute)");

                    await TaskScheduler.Default;
                    UploadFile(device, Path.Combine(Globals.FirmwareDownloadsFilePath, osRuntimeFileName), 0x08040000);

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    OutputMessage($"Resetting device");

                    await TaskScheduler.Default;
                    device.LeaveDfuMode();
                    device.Dispose();
                    OutputMessage($"Complete");
                });
            }
            else
            {
                OutputMessage("Device not found, double check your device in DFU mode. For more help, visit http://developer.wildernesslabs.co/Meadow/Getting_Started/Troubleshooting/VS");
            }
        }

        private void UploadFile(STDfuDevice device, string filepath, uint address)
        {
            ushort blockSize = device.BlockTransferSize;
            byte[] hexFileBytes = File.ReadAllBytes(filepath);

            // set our download address pointer
            if (device.SetAddressPointer(address) == false)
            {
                throw new Exception("Could not set base address for flash operation.");
            }

            // write blocks to the board and verify; we must have already erased our sectors before this point
            for (ushort index = 0; index <= (hexFileBytes.Length / blockSize); index++)
            {
                // write block to the board
                byte[] buffer = new byte[Math.Min(hexFileBytes.Length - (index * blockSize), blockSize)];
                Array.Copy(hexFileBytes, index * blockSize, buffer, 0, buffer.Length);
                bool success = device.WriteMemoryBlock(index, buffer);
                if (!success)
                {
                    Console.WriteLine("write failed");
                }

                //// verify written block
                //byte[] verifyBuffer = new byte[buffer.Length];
                //success = device.ReadMemoryBlock(index, verifyBuffer);
                //if (!success || !buffer.SequenceEqual(verifyBuffer))
                //{
                //    Console.WriteLine("verify failed");
                //}
            }
        }

        private async void Download_Firmware(object sender, RoutedEventArgs e)
        {
            OutputMessage($"Checking for updates...", true);
            HttpClient httpClient = new HttpClient();
            var payload = await httpClient.GetStringAsync(versionCheckUrl);
            var json = JObject.Parse(payload);
            var version = GetVersionFromPayload(payload);

            Uri download = new Uri(GetDownloadUrlFromPayload(payload));
            var fileName = download.Segments.ToList().Last();

            if (!Directory.Exists(Globals.FirmwareDownloadsFilePath))
            {
                Directory.CreateDirectory(Globals.FirmwareDownloadsFilePath);
            }

            DirectoryInfo di = new DirectoryInfo(Globals.FirmwareDownloadsFilePath);

            var files = di.GetFiles();

            if(!files.ToList().Any(x=> string.Compare(x.Name, fileName, StringComparison.OrdinalIgnoreCase) == 0))
            {
                OutputMessage($"Downloading firmware version: {version}.");
                di.Delete(true);
                Directory.CreateDirectory(Globals.FirmwareDownloadsFilePath);

                WebClient webClient = new WebClient();
                webClient.DownloadFile(download, Path.Combine(Globals.FirmwareDownloadsFilePath, fileName));
                webClient.DownloadFile(versionCheckUrl, Path.Combine(Globals.FirmwareDownloadsFilePath, latestJson));
                ZipFile.ExtractToDirectory(Path.Combine(Globals.FirmwareDownloadsFilePath, fileName), Globals.FirmwareDownloadsFilePath);
                OutputMessage($"Download complete.");
            }
            else
            {
                OutputMessage($"No updates found {DateTime.Now.ToLongTimeString()}.");
            }
        }

        private void OutputMessage(string message, bool clear = false)
        {
            IVsOutputWindow outWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;

            string customTitle = "Meadow Device Explorer";
            outWindow.CreatePane(ref windowGuid, customTitle, 1, 1);

            IVsOutputWindowPane customPane;
            outWindow.GetPane(ref windowGuid, out customPane);

            customPane.Activate(); // Brings this pane into view
            if (clear)
            {
                customPane.Clear();
            }
            customPane.OutputString(message + Environment.NewLine);
        }

        private string GetVersionFromPayload(string payload)
        {
            var json = JObject.Parse(payload);
            return json["version"].Value<string>();
        }

        private string GetDownloadUrlFromPayload(string payload)
        {
            var json = JObject.Parse(payload);
            return json["downloadUrl"].Value<string>();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }
    }
}
