using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
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

        private string _outputPath { get; set; }
        
        private MeadowSerialDevice _currentDevice;

        public async Task DeployAsync(CancellationToken cts, TextWriter outputPaneWriter)
        {
            var generalProperties = await this.Properties.GetConfigurationGeneralPropertiesAsync();
            var projectDir = await generalProperties.Rule.GetPropertyValueAsync("ProjectDir");
            _outputPath = Path.Combine(projectDir, await generalProperties.Rule.GetPropertyValueAsync("OutputPath"));

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

            await DeployAppAsync(settings.DeviceTarget, Path.Combine(projectDir, _outputPath), new OutputPaneWriter(outputPaneWriter), cts).ConfigureAwait(false);
        }

        async Task DeployAppAsync(string target, string folder, IOutputPaneWriter outputPaneWriter, CancellationToken cts)
        {
            Stopwatch sw = Stopwatch.StartNew();
            await outputPaneWriter.WriteAsync($"Deploying to Meadow on {target}...");

            try
            {
                var meadow = _currentDevice = await MeadowDeviceManager.GetMeadowForSerialPort(target);

                EventHandler<MeadowMessageEventArgs> handler = (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Message))
                    {
                        outputPaneWriter.WriteAsync(e.Message).Wait();
                    }
                };

                await MeadowDeviceManager.MonoDisable(meadow).ConfigureAwait(false);
                meadow.OnMeadowMessage += handler;
                //rename App.exe to App.dll
                //check for App.dll ... if exists, rename 
                var appPathDll = Path.Combine(folder, "App.dll");
                var appPathExe = Path.Combine(folder, "App.exe");

                if (File.Exists(appPathDll))
                {
                    if (File.Exists(appPathExe))
                    {
                        File.Delete(appPathExe);
                    }
                    File.Copy(appPathDll, appPathExe);
                    File.Delete(appPathDll);
                }

                await MeadowDeviceManager.DeployApp(meadow, appPathExe);
                meadow.OnMeadowMessage -= handler;
                await MeadowDeviceManager.MonoEnable(meadow).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw ex;
            }

            sw.Stop();
            await outputPaneWriter.WriteAsync($"Deployment Duration: {sw.Elapsed}");
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

            generalPane.OutputString(" Launching application..." + Environment.NewLine);

            if (_currentDevice?.OnMeadowMessage == null)
            {
                _currentDevice.OnMeadowMessage += (s, e) =>
                {
                    generalPane.OutputString(" " + e.Message);
                };
            }
        }

        public void Rollback()
        {
        }
    }
}