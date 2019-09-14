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
using MeadowCLI.DeviceManagement;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;

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
                throw new Exception("Device has not been selected. Hit Ctrl+2 to access the Device list.");
            }

            var attachedDevices = MeadowDeviceManager.FindSerialDevices();
            if (!attachedDevices.Contains(settings.DeviceTarget))
            {
                throw new Exception($"Device on '{settings.DeviceTarget}' is not connected or busy.");
            }

            await DeployAppAsync(settings.DeviceTarget, Path.Combine(projectDir, outputPath), outputPaneWriter, cts);
        }

        async Task DeployAppAsync(string target, string folder, TextWriter outputPaneWriter, CancellationToken cts)
        {
            await outputPaneWriter.WriteAsync("Deploying to Meadow ...");

            try
            {
                var meadow = await MeadowDeviceManager.GetMeadowForSerialPort(target);

                if (await InitializeMeadowDeviceAsync(meadow, outputPaneWriter, cts) == false)
                {
                    throw new Exception("Failed to initialize Meadow. Trying reconnecting the device.");
                }

                await GetFilesOnDeviceAsync(meadow, outputPaneWriter, cts);

                await DeployRequiredLibrariesAsync(meadow, outputPaneWriter, cts, folder);

                await DeployMeadowAppAsync(meadow, outputPaneWriter, cts, folder);

                await ResetMeadowAndStartMonoAsync(meadow, outputPaneWriter, cts);

            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        async Task<bool> InitializeMeadowDeviceAsync(MeadowSerialDevice meadow, TextWriter outputPaneWriter, CancellationToken cts)
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
            return true;
        }

        async Task<List<string>> GetFilesOnDeviceAsync(MeadowSerialDevice meadow, TextWriter outputPaneWriter, CancellationToken cts)
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

        async Task WriteFileToMeadowAsync(MeadowSerialDevice meadow, TextWriter outputPaneWriter, CancellationToken cts, string folder, string file, bool overwrite = false)
        {
            if (cts.IsCancellationRequested) { return; }

            if (overwrite || await meadow.IsFileOnDevice(file).ConfigureAwait(false) == false)
            {
                await outputPaneWriter.WriteAsync($"Writing {file}").ConfigureAwait(false);
                await meadow.WriteFile(file, folder).ConfigureAwait(false);
            }
        }

        async Task DeployRequiredLibrariesAsync(MeadowSerialDevice meadow, TextWriter outputPaneWriter, CancellationToken cts, string folder)
        {
            await outputPaneWriter.WriteAsync("Deploying required libraries (this may take several minutes)");

            await WriteFileToMeadowAsync(meadow, outputPaneWriter, cts, folder, MeadowDevice.SYSTEM);
            await WriteFileToMeadowAsync(meadow, outputPaneWriter, cts, folder, MeadowDevice.SYSTEM_CORE);
            await WriteFileToMeadowAsync(meadow, outputPaneWriter, cts, folder, MeadowDevice.MEADOW_CORE);
            await WriteFileToMeadowAsync(meadow, outputPaneWriter, cts, folder, MeadowDevice.MSCORLIB);
        }

        async Task DeployMeadowAppAsync(MeadowSerialDevice meadow, TextWriter outputPaneWriter, CancellationToken cts, string folder)
        {
            if (cts.IsCancellationRequested) { return; }

            await outputPaneWriter.WriteAsync("Deploying executable and dependencies");
            await meadow.DeployApp(folder);
        }

        async Task ResetMeadowAndStartMonoAsync(MeadowSerialDevice meadow, TextWriter outputPaneWriter, CancellationToken cts)
        {
            if (cts.IsCancellationRequested) { return; }

            string serial = meadow.DeviceInfo.SerialNumber;

            await outputPaneWriter.WriteAsync("Resetting Meadow and starting app (30-60s)");

            MeadowDeviceManager.MonoEnable(meadow);

            try
            {
                MeadowDeviceManager.ResetTargetMcu(meadow);
            }
            catch
            {
                //gulp
            }

            await Task.Delay(2500);//wait for reboot

            ////reconnect serial port
            //if (meadow.Initialize() == false)
            //{
            //    //find device with matching serial

            //}
        }

        public bool IsDeploySupported
        {
            get { return true; }
        }

        public void Commit()
        {
        }

        public void Rollback()
        {
        }
    }
}