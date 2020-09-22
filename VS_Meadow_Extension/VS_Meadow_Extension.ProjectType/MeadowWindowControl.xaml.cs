namespace Meadow
{
    using Meadow.Helpers;
    using MeadowCLI.DeviceManagement;
    using Microsoft.VisualStudio;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.Interop;
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Management;
    using System.Windows;
    using System.Windows.Controls;
    using Microsoft.VisualStudio.Threading;
    using System.Net.Http;
    using Newtonsoft.Json.Linq;
    using System.Net;
    using System.IO.Compression;
    using Task = System.Threading.Tasks.Task;
    using Microsoft.Win32;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using System.Windows.Input;
    using Microsoft.Build.Tasks;

    /// <summary>
    /// Interaction logic for MeadowWindowControl.
    /// </summary>
    public partial class MeadowWindowControl : UserControl
    {
        readonly Guid DEVICE_INTERFACE_GUID_STDFU = new Guid(0x3fe809ab, 0xfb91, 0x4cb5, 0xa6, 0x43, 0x69, 0x67, 0x0d, 0x52, 0x36, 0x6e);
        static Guid windowGuid = new Guid("AD01DF73-6990-4361-8587-4FC3CB91A65F");
        readonly string versionCheckUrl = "https://s3-us-west-2.amazonaws.com/downloads.wildernesslabs.co/Meadow_Beta/latest.json";
        public string VersionCheckFile { get { return new Uri(versionCheckUrl).Segments.Last(); } }

        public readonly string osFilename = "Meadow.OS.bin";
        public readonly string runtimeFilename = "Meadow.OS.Runtime.bin";
        public readonly string networkBootloaderFilename = "bootloader.bin";
        public readonly string networkMeadowCommsFilename = "MeadowComms.bin";
        public readonly string networkPartitionTableFilename = "partition-table.bin";

        public readonly uint osAddress = 0x08000000;

        public readonly string Flash_OS_Text = "Flash OS";
        public readonly string Flash_Runtime_Text = "Flash Runtime";
        public readonly string Flash_Device_Text = "Flash Device";
        public readonly string Check_Version_Text = "Check Version";
        public readonly string Erase_Flash_Text = "Erase Flash";

        public bool _skipFlashToSelectDevice = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="MeadowWindowControl"/> class.
        /// </summary>
        public MeadowWindowControl()
        {
            this.InitializeComponent();
            Devices.DisplayMemberPath = "Caption";
            Devices.SelectedValuePath = "Port";
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshDeviceList();
        }

        public void RefreshDeviceList()
        {
            MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath);

            Devices.Items.Clear();
            Devices.Items.Add(new SerialDevice { Caption = "Select Target Device Port" });
            Devices.SelectedIndex = 0;

            var index = 1;
            var captions = MeadowDeviceManager.GetSerialDeviceCaptions();
            foreach (var c in captions.Distinct())
            {
                var port = Regex.Match(c, @"(?<=\().+?(?=\))").Value;
                Devices.Items.Add(new SerialDevice()
                {
                    Caption = c,
                    Port = port
                });

                if (port == settings.DeviceTarget) { Devices.SelectedIndex = index; }
                index++;
            }
        }

        private void Devices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Devices.SelectedIndex <= 0) return;

            MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath, false);
            settings.DeviceTarget = Devices.SelectedValue.ToString();
            settings.Save();
        }

        private async void Check_Version(object sender, RoutedEventArgs e)
        {
            try
            {
                if (IsDfuMode())
                {
                    await OutputMessageAsync($"Device is in bootloader mode. Connect device in normal mode to check version.", true);
                    return;
                }

                EnableControls(false);

                MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath);
                if (string.IsNullOrEmpty(settings.DeviceTarget))
                {
                    await OutputMessageAsync($"Select Target Device Port and try again.", true);
                    EnableControls(true);
                    return;
                }
                else if (MeadowDeviceManager.CurrentDevice == null)
                {
                    await MeadowDeviceManager.GetMeadowForSerialPort(settings.DeviceTarget);
                }
                else
                {
                    MeadowDeviceManager.CurrentDevice.Initialize();
                }

                MeadowDeviceManager.GetDeviceInfo(MeadowDeviceManager.CurrentDevice);
                await Task.Delay(1500); // wait for device info to populate
                await OutputMessageAsync($"Device {MeadowDeviceManager.CurrentDevice.DeviceInfo.MeadowOSVersion}", true);
            }
            catch (Exception ex)
            {
                await OutputMessageAsync($"Could not read device version.");
            }

            EnableControls(true);

        }

        private async void Erase_Flash(object sender, RoutedEventArgs e)
        {
            try
            {
                if (IsDfuMode())
                {
                    await OutputMessageAsync($"Device is in bootloader mode. Connect device in normal mode to erase flash.", true);
                    return;
                }

                EnableControls(false);
                await OutputMessageAsync($"Erase flash (~3 mins)", true);

                MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath);
                if (string.IsNullOrEmpty(settings.DeviceTarget))
                {
                    await OutputMessageAsync($"Select Target Device Port and try again.", true);
                    EnableControls(true);
                    return;
                }
                else if (MeadowDeviceManager.CurrentDevice == null)
                {
                    await MeadowDeviceManager.GetMeadowForSerialPort(settings.DeviceTarget);
                }
                else
                {
                    MeadowDeviceManager.CurrentDevice.Initialize();
                }

                if (!await Process(() => MeadowDeviceManager.MonoDisable(MeadowDeviceManager.CurrentDevice))) return;

                MeadowDeviceManager.CurrentDevice.Initialize(true);

                if (!await Process(() => MeadowFileManager.EraseFlash(MeadowDeviceManager.CurrentDevice))) return;

                await OutputMessageAsync($"'{Erase_Flash_Text}' completed");
            }
            catch (Exception ex)
            {
                await OutputMessageAsync($"Could not read erase flash.");
            }

            EnableControls(true);

        }

        private async void Flash_Device(object sender, RoutedEventArgs e)
        {
            try
            {
                MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath);

                var (osFilePath, runtimeFilePath) = await GetWorkingFiles();
                if (string.IsNullOrEmpty(osFilePath) && string.IsNullOrEmpty(runtimeFilePath))
                {
                    await OutputMessageAsync($"Meadow OS files not found. 'Download Meadow OS' first.");
                    return;
                }

                EnableControls(false);

                await OutputMessageAsync($"Begin '{Flash_Device_Text}'", true);

                if (!string.IsNullOrEmpty(osFilePath))
                {
                    if (!await DfuFlash(osFilePath, osAddress))
                    {
                        EnableControls(true);
                        return;
                    }
                }
                else
                {
                    await OutputMessageAsync($"{osFilename} not selected. Skipping OS flash.");
                }

                //reset skip flash flag
                _skipFlashToSelectDevice = false;

                if (!string.IsNullOrEmpty(runtimeFilePath))
                {
                    await OutputMessageAsync($"Initialize device");

                    //MeadowDeviceManager.CurrentDevice = null;

                    if (string.IsNullOrEmpty(settings.DeviceTarget))
                    {
                        await OutputMessageAsync($"Select Target Device Port and click '{Flash_Device_Text}' to resume.");
                        _skipFlashToSelectDevice = true;
                        EnableControls(true);
                        return;
                    }
                    else if(MeadowDeviceManager.CurrentDevice == null)
                    {
                        await MeadowDeviceManager.GetMeadowForSerialPort(settings.DeviceTarget);
                    }
                    else
                    {
                        MeadowDeviceManager.CurrentDevice.Initialize();
                    }

                    if (MeadowDeviceManager.CurrentDevice == null)
                    {
                        await OutputMessageAsync($"Initialization failed. Try again.");
                        return;
                    }

                    if (!await Process(() => MeadowDeviceManager.MonoDisable(MeadowDeviceManager.CurrentDevice))) return;

                    MeadowDeviceManager.CurrentDevice.Initialize(true);

                    await OutputMessageAsync($"Erase flash (~3 mins)");
                    if (!await Process(() => MeadowFileManager.EraseFlash(MeadowDeviceManager.CurrentDevice))) return;

                    await OutputMessageAsync($"Restart device");
                    if (!await Process(() => MeadowDeviceManager.ResetMeadow(MeadowDeviceManager.CurrentDevice, 0))) return;

                    MeadowDeviceManager.CurrentDevice.Initialize(true);

                    await OutputMessageAsync($"Upload {runtimeFilename} (~1 min)");
                    if (!await Process(() => MeadowFileManager.WriteFileToFlash(MeadowDeviceManager.CurrentDevice, runtimeFilePath))) return;

                    await OutputMessageAsync($"Process {runtimeFilename} (~30 secs)");
                    if (!await Process(() => MeadowDeviceManager.MonoFlash(MeadowDeviceManager.CurrentDevice))) return;

                    await OutputMessageAsync($"Flash coprocessor (~25 secs)");
                    await Task.Run(() =>
                    {
                        MeadowFileManager.WriteFileToEspFlash(MeadowDeviceManager.CurrentDevice, Path.Combine(Globals.FirmwareDownloadsFilePath, networkBootloaderFilename), mcuDestAddr: "0x1000");
                        MeadowFileManager.WriteFileToEspFlash(MeadowDeviceManager.CurrentDevice, Path.Combine(Globals.FirmwareDownloadsFilePath, networkPartitionTableFilename), mcuDestAddr: "0x8000");
                        MeadowFileManager.WriteFileToEspFlash(MeadowDeviceManager.CurrentDevice, Path.Combine(Globals.FirmwareDownloadsFilePath, networkMeadowCommsFilename), mcuDestAddr: "0x10000");
                    });

                    await OutputMessageAsync($"Clean up temporary files");
                    await MeadowDeviceManager.CurrentDevice.DeleteFile(runtimeFilename);

                    await OutputMessageAsync($"Restart device");
                    if (!await Process(() => MeadowDeviceManager.MonoEnable(MeadowDeviceManager.CurrentDevice))) return;
                    if (!await Process(() => MeadowDeviceManager.ResetMeadow(MeadowDeviceManager.CurrentDevice, 0))) return;
                }
                else
                {
                    await OutputMessageAsync($"{runtimeFilename} not selected. Skipping Runtime flash.");
                }

                EnableControls(true);

                await OutputMessageAsync($"'{Flash_Device_Text}' completed");
            }
            catch (Exception ex)
            {
                await OutputMessageAsync($"An unexpected error occurred. Please try again.");
                EnableControls(true);
            }
        }

        private async Task<(string osFilePath, string runtimeFilePath)> GetWorkingFiles()
        {
            var selectCustom = Keyboard.IsKeyDown(Key.LeftShift);
            if (selectCustom)
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.Filter = "Meadow OS|Meadow.OS*.bin";
                dlg.InitialDirectory = Globals.FirmwareDownloadsFilePath;
                dlg.Multiselect = true;

                var result = dlg.ShowDialog();

                if (result.HasValue && result.Value && dlg.FileNames.Any())
                {
                    var osFilePath = dlg.FileNames
                        .Select(x => new { Path = x, Filename = Path.GetFileName(x) })
                        .SingleOrDefault(x => string.Compare(x.Filename, osFilename, StringComparison.OrdinalIgnoreCase) == 0)?.Path;

                    var runtimeFilePath = dlg.FileNames
                        .Select(x => new { Path = x, Filename = Path.GetFileName(x) })
                        .SingleOrDefault(x => string.Compare(x.Filename, runtimeFilename, StringComparison.OrdinalIgnoreCase) == 0)?.Path;

                    return (osFilePath, runtimeFilePath);
                }
                else
                {
                    await OutputMessageAsync($"Flash canceled");
                    return (null, null);
                }
            }
            else
            {
                // get download path files
                var osFilePath = Path.Combine(Globals.FirmwareDownloadsFilePath, osFilename);
                var runtimeFilePath = Path.Combine(Globals.FirmwareDownloadsFilePath, runtimeFilename);

                if (File.Exists(osFilePath) && File.Exists(runtimeFilePath))
                {
                    return (osFilePath, runtimeFilePath);
                }
                else
                {
                    return (null, null);
                }
            }
        }

        private async Task<bool> Process(Func<Task<bool>> func)
        {
            bool result;
            if (!(result = await func.Invoke()))
            {
                await OutputMessageAsync($"An unexpected error occurred. Please try again.");
                EnableControls(true);
            }
            return result;
        }

        private async Task<bool> DfuFlash(string filepath, uint address)
        {
            FileInfo fi = new FileInfo(filepath);

            string display = $"file: {filepath}";
            if (filepath.StartsWith(Globals.FirmwareDownloadsFilePath))
            {
                var payload = File.ReadAllText(Path.Combine(Globals.FirmwareDownloadsFilePath, VersionCheckFile));
                display = $"downloaded version: {ExtractJsonValue(payload, "version")}";
            }

            var query = "SELECT * FROM Win32_USBHub";
            ManagementObjectSearcher device_searcher = new ManagementObjectSearcher(query);
            string deviceId = string.Empty;
            foreach (ManagementObject usb_device in device_searcher.Get())
            {
                if (usb_device.Properties["Name"].Value.ToString() == "STM Device in DFU Mode")
                {
                    deviceId = usb_device.Properties["DeviceID"].Value.ToString();
                    break;
                }
            }

            if (!string.IsNullOrEmpty(deviceId))
            {
                using (var device = new STDfuDevice($@"\\?\{deviceId.Replace("\\", "#")}#{{{DEVICE_INTERFACE_GUID_STDFU.ToString()}}}"))
                {
                    try
                    {
                        await OutputMessageAsync($"Upload {fi.Name} (~2 mins)");

                        await Task.Run(() =>
                        {
                            device.EraseAllSectors();
                            UploadFile(device, filepath, address);
                            device.LeaveDfuMode();
                        });

                        return true;
                    }
                    catch (Exception ex)
                    {
                        await OutputMessageAsync($"An error occurred while flashing the device: {ex.Message}");
                    }
                    return false;
                }
            }
            else
            {
                if (_skipFlashToSelectDevice)
                {
                    return true;
                }
                else
                {
                    await OutputMessageAsync("Device not found. Connect the device in bootloader mode by plugging in the device while holding down the BOOT button.");
                    await OutputMessageAsync("For more help, visit http://developer.wildernesslabs.co/Meadow/Meadow_Basics/Troubleshooting/VisualStudio/");
                }
            }

            return false;
        }

        private bool IsDfuMode()
        {
            var query = "SELECT * FROM Win32_USBHub";
            ManagementObjectSearcher device_searcher = new ManagementObjectSearcher(query);
            string deviceId = string.Empty;
            foreach (ManagementObject usb_device in device_searcher.Get())
            {
                if (usb_device.Properties["Name"].Value.ToString() == "STM Device in DFU Mode")
                {
                    return true;
                }
            }
            return false;
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
            }
        }

        private async void Download_Firmware(object sender, RoutedEventArgs e)
        {
            try
            {
                HttpClient httpClient = new HttpClient();
                var payload = await httpClient.GetStringAsync(versionCheckUrl);
                var version = ExtractJsonValue(payload, "version");
                var minVsixVersion = ExtractJsonValue(payload, "minVsixVersion");

                if (!CheckCompatibility(minVsixVersion, VsixManifest.GetManifest().Version))
                {
                    await OutputMessageAsync($"Meadow OS ({version}) is not compatible with VS Tools for Meadow ({VsixManifest.GetManifest().Version}). Please update the extension to continue.", true);
                    return;
                }

                if (Directory.Exists(Globals.FirmwareDownloadsFilePath))
                {
                    Directory.Delete(Globals.FirmwareDownloadsFilePath, true);
                }
                Directory.CreateDirectory(Globals.FirmwareDownloadsFilePath);

                await OutputMessageAsync($"Downloading firmware version: {version}.", true);
                await DownloadFile(new Uri(ExtractJsonValue(payload, "downloadUrl")));
                await DownloadFile(new Uri(ExtractJsonValue(payload, "networkDownloadUrl")));

                await OutputMessageAsync($"Download complete.");
            }
            catch (Exception ex)
            {
                await OutputMessageAsync($"Error occurred while downloading latest OS. Please try again later.");
            }
        }

        private async Task DownloadFile(Uri uri)
        {
            var fileName = uri.Segments.ToList().Last();

            WebClient webClient = new WebClient();
            webClient.DownloadFile(uri, Path.Combine(Globals.FirmwareDownloadsFilePath, fileName));
            webClient.DownloadFile(versionCheckUrl, Path.Combine(Globals.FirmwareDownloadsFilePath, VersionCheckFile));
            ZipFile.ExtractToDirectory(Path.Combine(Globals.FirmwareDownloadsFilePath, fileName), Globals.FirmwareDownloadsFilePath);
        }

        private bool CheckCompatibility(string minVsixVersion, string vsixVersion)
        {
            Version vsix, minVsix;
            Version.TryParse(minVsixVersion, out minVsix);
            Version.TryParse(vsixVersion, out vsix);

            return (vsix ?? new Version(0, 0, 0)) >= (minVsix ?? new Version(0, 0, 0));
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private async Task OutputMessageAsync(string message, bool clear = false)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            IVsOutputWindowPane outputPane = null;
            IVsWindowFrame windowFrame = null;
            var outputWindow = ServiceProvider.GlobalProvider.GetService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (outputWindow != null && ErrorHandler.Failed(outputWindow.GetPane(windowGuid, out outputPane)))
            {
                outputWindow.CreatePane(windowGuid, "Meadow Device Explorer", 1, 1);
                outputWindow.GetPane(windowGuid, out outputPane);
            }

            var vsUiShell = ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShell)) as IVsUIShell;
            uint flags = (uint)__VSFINDTOOLWIN.FTW_fForceCreate;
            vsUiShell?.FindToolWindow(flags, VSConstants.StandardToolWindows.Output, out windowFrame);

            if (clear) { outputPane?.Clear(); }

            windowFrame?.Show();
            outputPane?.Activate();
            outputPane?.OutputString($"[{DateTime.Now.ToLocalTime()}] {message}" + Environment.NewLine);
        }

        private void EnableControls(bool enabled)
        {
            Flash.IsEnabled = enabled;
            EraseFlash.IsEnabled = enabled;
            CheckVersion.IsEnabled = enabled;
            Download.IsEnabled = enabled;
            Devices.IsEnabled = enabled;
            Refresh.IsEnabled = enabled;
            RefreshDeviceList();
        }

        private string ExtractJsonValue(string json, string field)
        {
            var jo = JObject.Parse(json);
            if (jo.ContainsKey(field))
            {
                return jo[field].Value<string>();
            }
            return string.Empty;
        }
    }
}
