using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Meadow.Helpers;
using Meadow.Utility;
using MeadowCLI.DeviceManagement;
using MeadowCLI.Hcom;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace Meadow
{
    [Export(typeof(IDeployProvider))]
    [AppliesTo("Meadow")]
    internal class DeployProvider : IDeployProvider
    {
        /// <summary>
        /// Provides access to the project's properties.
        /// </summary>
        [Import]
        private ProjectProperties Properties { get; set; }

        public async Task DeployAsync(CancellationToken cts, TextWriter outputPaneWriter)
        {
            var generalProperties = await this.Properties.GetConfigurationGeneralPropertiesAsync();
            var projectDir = await generalProperties.Rule.GetPropertyValueAsync("ProjectDir");
            var outputPath = Path.Combine(projectDir, await generalProperties.Rule.GetPropertyValueAsync("OutputPath"));

            MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath);

            if (string.IsNullOrEmpty(settings.DeviceTarget))
            {
                throw new Exception("Device has not been selected. Hit Ctrl+Shift+M to access the Device list.");
            }

            var attachedDevices = MeadowDeviceManager.FindSerialDevices();
            if (!attachedDevices.Contains(settings.DeviceTarget))
            {
                throw new Exception($"Device on '{settings.DeviceTarget}' is not connected or busy.");
            }

            await DeployAppAsync(settings.DeviceTarget, Path.Combine(projectDir, outputPath), new OutputPaneWriter(outputPaneWriter), cts);
        }

        async Task DeployAppAsync(string target, string folder, IOutputPaneWriter outputPaneWriter, CancellationToken cts)
        {
            await outputPaneWriter.WriteAsync($"Deploying to Meadow on {target}...");

            try
            {
                var meadow = MeadowDeviceManager.CurrentDevice;
                if (meadow == null)
                {
                    meadow = await MeadowDeviceManager.GetMeadowForSerialPort(target);
                }

                if (await InitializeMeadowDeviceAsync(meadow, outputPaneWriter, cts) == false)
                {
                    throw new Exception("Failed to initialize Meadow. Try resetting or reconnecting the device.");
                }

                var meadowFiles = await GetFilesOnDevice(meadow, outputPaneWriter, cts);

                var localFiles = await GetLocalFiles(outputPaneWriter, cts, folder);

                await DeleteUnusedFiles(meadow, outputPaneWriter, cts, meadowFiles, localFiles);

                await DeployApp(meadow, outputPaneWriter, cts, folder, meadowFiles, localFiles);

                await ResetMeadowAndStartMonoAsync(meadow, outputPaneWriter, cts);

            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        async Task<bool> InitializeMeadowDeviceAsync(MeadowSerialDevice meadow, IOutputPaneWriter outputPaneWriter, CancellationToken cts)
        {
            if (cts.IsCancellationRequested) return true;

            await outputPaneWriter.WriteAsync("Initializing Meadow");

            if (meadow == null)
            {
                await outputPaneWriter.WriteAsync("Can't read Meadow device");
                return false;
            }

            meadow.Initialize(false);
            MeadowDeviceManager.MonoDisable(meadow);
            await Task.Delay(5000); //hack for testing

            if (meadow.Initialize() == false)
            {
                await outputPaneWriter.WriteAsync("Couldn't initialize serial port");
                return false;
            }
            else
            {
                MeadowDeviceManager.GetDeviceInfo(meadow);
                await Task.Delay(1000); // wait for device info to populate
                await outputPaneWriter.WriteAsync($"Device {meadow.DeviceInfo.MeadowOSVersion}");
            }
            
            return true;
        }

        async Task<(List<string> files, List<UInt32> crcs)> GetFilesOnDevice(MeadowSerialDevice meadow, IOutputPaneWriter outputPaneWriter, CancellationToken cts)
        {
            if (cts.IsCancellationRequested) { return (new List<string>(), new List<UInt32>()); }

            await outputPaneWriter.WriteAsync("Checking files on device (may take several seconds)");

            var meadowFiles = await meadow.GetFilesAndCrcs(30000);

            foreach (var f in meadowFiles.files)
            {
                if (cts.IsCancellationRequested) break;
                await outputPaneWriter.WriteAsync($"Found {f}").ConfigureAwait(false);
            }

            if(meadowFiles.files.Count == 0)
            {
                await outputPaneWriter.WriteAsync($"Deploying for the first time may take several minutes.").ConfigureAwait(false);
            }

            return meadowFiles;
        }

        async Task<(List<string> files, List<UInt32> crcs)> GetLocalFiles(IOutputPaneWriter outputPaneWriter, CancellationToken cts, string folder)
        {
            // get list of files in folder
            // var files = Directory.GetFiles(folder, "*.dll");

            var paths = Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
            .Where(s => s.EndsWith(".exe") ||
                        s.EndsWith(".dll") ||
                        s.EndsWith(".bmp") ||
                        s.EndsWith(".jpg") ||
                        s.EndsWith(".jpeg") ||
                        s.EndsWith(".txt"));

            var files = new List<string>();
            var crcs = new List<UInt32>();

            foreach (var file in paths)
            {
                if (cts.IsCancellationRequested) break;

                using (FileStream fs = File.Open(file, FileMode.Open))
                {
                    var len = (int)fs.Length;
                    var bytes = new byte[len];

                    fs.Read(bytes, 0, len);

                    //0x
                    var crc = CrcTools.Crc32part(bytes, len, 0);// 0x04C11DB7);

                    Console.WriteLine($"{file} crc is {crc}");
                    files.Add(Path.GetFileName(file));
                    crcs.Add(crc);
                }
            }

            return (files, crcs);
        }

        async Task DeleteUnusedFiles(MeadowSerialDevice meadow, IOutputPaneWriter outputPaneWriter, CancellationToken cts,
            (List<string> files, List<UInt32> crcs) meadowFiles, (List<string> files, List<UInt32> crcs) localFiles)
        {
            if (cts.IsCancellationRequested)
                return;

            foreach (var file in meadowFiles.files)
            {
                if (cts.IsCancellationRequested) { break; }

                if (localFiles.files.Contains(file) == false)
                {
                    await meadow.DeleteFile(file);
                    await outputPaneWriter.WriteAsync($"Removing {file}").ConfigureAwait(false);
                }
            }
        }

        async Task DeployApp(MeadowSerialDevice meadow, IOutputPaneWriter outputPaneWriter, CancellationToken cts, string folder,
            (List<string> files, List<UInt32> crcs) meadowFiles, (List<string> files, List<UInt32> crcs) localFiles)
        {
            if (cts.IsCancellationRequested)
                return;

            for (int i = 0; i < localFiles.files.Count; i++)
            {
                if (meadowFiles.crcs.Contains(localFiles.crcs[i])) continue;

                await WriteFileToMeadowAsync(meadow, outputPaneWriter, cts, folder, localFiles.files[i], true);
            }
        }

        async Task<List<string>> GetFilesOnDeviceAsync(MeadowSerialDevice meadow, IOutputPaneWriter outputPaneWriter, CancellationToken cts)
        {
            if (cts.IsCancellationRequested) { return new List<string>(); }

            var files = await meadow.GetFilesOnDevice();

            await outputPaneWriter.WriteAsync("Checking files on device");

            foreach (var f in files)
            {
                await outputPaneWriter.WriteAsync($"Found {f}").ConfigureAwait(false);
            }

            return files;
        }

        async Task WriteFileToMeadowAsync(MeadowSerialDevice meadow, IOutputPaneWriter outputPaneWriter, CancellationToken cts, string folder, string file, bool overwrite = false)
        {
            if (cts.IsCancellationRequested) { return; }

            if (overwrite || await meadow.IsFileOnDevice(file).ConfigureAwait(false) == false)
            {
                await outputPaneWriter.WriteAsync($"Writing {file}").ConfigureAwait(false);
                await meadow.WriteFile(file, folder).ConfigureAwait(false);
            }
        }

        async Task ResetMeadowAndStartMonoAsync(MeadowSerialDevice meadow, IOutputPaneWriter outputPaneWriter, CancellationToken cts)
        {
            if (cts.IsCancellationRequested) { return; }

            string serial = meadow.DeviceInfo.SerialNumber;

            await outputPaneWriter.WriteAsync("Resetting Meadow and starting app (30-60s)");

            MeadowDeviceManager.MonoEnable(meadow);

            try
            {
                MeadowDeviceManager.ResetMeadow(meadow, 0);
            }
            catch
            {
                //gulp
            }

            await Task.Delay(2500);//wait for reboot

            //reconnect serial port
            if (meadow.Initialize() == false)
            {
                //find device with matching serial
            }
        }

        public bool IsDeploySupported
        {
            get { return true; }
        }

        public void Commit()
        {
            IVsOutputWindow outWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            Guid generalPaneGuid = VSConstants.GUID_OutWindowDebugPane; // P.S. There's also the GUID_OutWindowDebugPane available.
            IVsOutputWindowPane generalPane;
            outWindow.GetPane(ref generalPaneGuid, out generalPane);
            generalPane.Activate();
            generalPane.Clear();

            var meadow = MeadowDeviceManager.CurrentDevice;
            if (meadow?.OnMeadowMessage == null)
            {
                meadow.OnMeadowMessage += (s, e) =>
                {
                    generalPane.OutputString(" " + e.Message + Environment.NewLine);
                };
            }
        }

        public void Rollback()
        {
        }
    }
}