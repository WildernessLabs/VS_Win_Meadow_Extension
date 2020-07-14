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

    /// <summary>
    /// Interaction logic for MeadowWindowControl.
    /// </summary>
    public partial class MeadowWindowControl : UserControl
    {
        readonly Guid DEVICE_INTERFACE_GUID_STDFU = new Guid(0x3fe809ab, 0xfb91, 0x4cb5, 0xa6, 0x43, 0x69, 0x67, 0x0d, 0x52, 0x36, 0x6e);
        static Guid windowGuid = new Guid("AD01DF73-6990-4361-8587-4FC3CB91A65F");
        readonly string versionCheckUrl = "https://s3-us-west-2.amazonaws.com/downloads.wildernesslabs.co/Meadow_Beta/latest_dev.json";
        public string VersionCheckFile { get { return new Uri(versionCheckUrl).Segments.Last(); } }

        public readonly string osFilename = "Meadow.OS.bin";
        public readonly string runtimeFilename = "Meadow.OS.Runtime.bin";

        public readonly uint osAddress = 0x08000000;

        public readonly string Flash_Device_Text = "Flash Device";
        public readonly string Resume_Flash_Text = "Resume Flash";

        public FlashState FlashState { get; set; } = FlashState.Initial;

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

        private async void Flash_Device(object sender, RoutedEventArgs e)
        {
            try
            {
                MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath);

                var (osFilePath, runtimeFilePath) = await GetWorkingFiles();
                if (string.IsNullOrEmpty(osFilePath) || string.IsNullOrEmpty(runtimeFilePath))
                {
                    await OutputMessageAsync($"Meadow OS files not found. 'Download Meadow OS' first.");
                    return;
                }

                if(FlashState == FlashState.Initial)
                {
                    EnableControls(false);

                    await OutputMessageAsync($"Begin '{Flash_Device_Text}'", true);

                    if (await DfuFlash(osFilePath, osAddress))
                    {
                        await OutputMessageAsync($"Manually reset device and click '{Resume_Flash_Text}' to continue. To reset device, either hit the RST button or reconnect.");
                        NextFlashState(FlashState);
                    }
                    else
                    {
                        EnableControls(true);
                        RefreshDeviceList();
                    }
                    return;
                }
                
                if(FlashState == FlashState.OSFlashed)
                {
                    EnableControls(false);

                    await OutputMessageAsync($"Initialize device");

                    MeadowDeviceManager.CurrentDevice = null;

                    if (string.IsNullOrEmpty(settings.DeviceTarget))
                    {
                        await OutputMessageAsync($"Select Target Device and click '{Resume_Flash_Text}' to continue.");
                        EnableControls(true);
                        RefreshDeviceList();
                        return;
                    }
                    else
                    {
                        await MeadowDeviceManager.GetMeadowForSerialPort(settings.DeviceTarget);
                    }

                    if (MeadowDeviceManager.CurrentDevice == null)
                    {
                        await OutputMessageAsync($"Initialization failed. Reset device and click '{Resume_Flash_Text}' to continue.");
                        return;
                    }

                    if (!await Process(() => MeadowDeviceManager.ResetMeadow(MeadowDeviceManager.CurrentDevice, 0))) return;

                    if (!await Process(() => MeadowDeviceManager.MonoDisable(MeadowDeviceManager.CurrentDevice))) return;

                    await OutputMessageAsync($"Erase flash (~3 mins)");
                    if (!await Process(() => MeadowFileManager.EraseFlash(MeadowDeviceManager.CurrentDevice))) return;

                    await OutputMessageAsync($"Restart device");
                    if (!await Process(() => MeadowDeviceManager.ResetMeadow(MeadowDeviceManager.CurrentDevice, 0))) return;

                    await OutputMessageAsync($"Upload {runtimeFilename} (~1 min)");
                    if (!await Process(() => MeadowFileManager.WriteFileToFlash(MeadowDeviceManager.CurrentDevice, runtimeFilePath))) return;

                    await OutputMessageAsync($"Process {runtimeFilename} (~30 secs)");
                    if (!await Process(() => MeadowDeviceManager.MonoFlash(MeadowDeviceManager.CurrentDevice))) return;

                    await MeadowDeviceManager.CurrentDevice.DeleteFile(runtimeFilename);

                    if (!await Process(() => MeadowDeviceManager.MonoEnable(MeadowDeviceManager.CurrentDevice))) return;

                    await OutputMessageAsync($"Restart device");
                    if (!await Process(() => MeadowDeviceManager.ResetMeadow(MeadowDeviceManager.CurrentDevice, 0))) return;

                    NextFlashState(FlashState);
                    
                    await OutputMessageAsync($"'{Flash_Device_Text}' completed");
                }
                
            }
            catch (Exception ex)
            {
                await OutputMessageAsync($"An unexpected error occurred. Please try again.");
                EnableControls(true);
                RefreshDeviceList();
                ResetFlashState();
            }
        }

        private void NextFlashState(FlashState currentState)
        {
            switch (currentState)
            {
                case FlashState.Initial:
                    this.Flash.Content = Resume_Flash_Text;
                    FlashState = FlashState.OSFlashed;
                    break;
                case FlashState.OSFlashed:
                    this.Flash.Content = Flash_Device_Text;
                    FlashState = FlashState.Initial;
                    break;
            }
            RefreshDeviceList();
            EnableControls(true);
        }

        private void ResetFlashState()
        {
            this.Flash.Content = Flash_Device_Text;
            FlashState = FlashState.Initial;
        }

        private async Task<(string osFilePath, string runtimeFilePath)> GetWorkingFiles()
        {
            var selectCustom = Keyboard.IsKeyDown(Key.LeftShift);
            if (selectCustom)
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.Filter = "Binary (*.BIN;)|*.BIN;";
                dlg.InitialDirectory = Globals.FirmwareDownloadsFilePath;
                dlg.Multiselect = true;

                var result = dlg.ShowDialog();

                if (result.HasValue && result.Value)
                {
                    if (dlg.FileNames.Select(x => Path.GetFileName(x).ToLower()).Contains(osFilename.ToLower())
                        && dlg.FileNames.Select(x => Path.GetFileName(x).ToLower()).Contains(runtimeFilename.ToLower()))
                    {
                        var osFilePath = dlg.FileNames
                            .Select(x => new { Path = x, Filename = Path.GetFileName(x) })
                            .Single(x => string.Compare(x.Filename, osFilename, StringComparison.OrdinalIgnoreCase) == 0).Path;

                        var runtimeFilePath = dlg.FileNames
                            .Select(x => new { Path = x, Filename = Path.GetFileName(x) })
                            .Single(x => string.Compare(x.Filename, runtimeFilename, StringComparison.OrdinalIgnoreCase) == 0).Path;

                        return (osFilePath, runtimeFilePath);
                    }
                    else
                    {
                        await OutputMessageAsync($"Please select both '{osFilename}' and '{runtimeFilename}'");
                        return (null, null);
                    }
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
                RefreshDeviceList();
                ResetFlashState();
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
                await OutputMessageAsync("Device not found. Connect the device in bootloader mode by plugging in the device while holding down the BOOT button.");
                await OutputMessageAsync("For more help, visit http://developer.wildernesslabs.co/Meadow/Meadow_Basics/Troubleshooting/VisualStudio/");
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

                Uri download = new Uri(ExtractJsonValue(payload, "downloadUrl"));
                var fileName = download.Segments.ToList().Last();

                if (Directory.Exists(Globals.FirmwareDownloadsFilePath))
                {
                    Directory.Delete(Globals.FirmwareDownloadsFilePath, true);
                }
                Directory.CreateDirectory(Globals.FirmwareDownloadsFilePath);

                await OutputMessageAsync($"Downloading firmware version: {version}.", true);
                WebClient webClient = new WebClient();
                webClient.DownloadFile(download, Path.Combine(Globals.FirmwareDownloadsFilePath, fileName));
                webClient.DownloadFile(versionCheckUrl, Path.Combine(Globals.FirmwareDownloadsFilePath, VersionCheckFile));
                ZipFile.ExtractToDirectory(Path.Combine(Globals.FirmwareDownloadsFilePath, fileName), Globals.FirmwareDownloadsFilePath);

                await OutputMessageAsync($"Download complete.");
            }
            catch (Exception ex)
            {
                await OutputMessageAsync($"Error occurred while downloading latest OS. Please try again later.");
            }
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
            Download.IsEnabled = enabled;
            Devices.IsEnabled = enabled;
            Refresh.IsEnabled = enabled;
        }

        private string ExtractJsonValue(string json, string field)
        {
            var jo = JObject.Parse(json);
            return jo[field].Value<string>();
        }
    }

    public enum FlashState
    {
        Initial,
        OSFlashed
    }
}
