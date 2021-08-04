using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Meadow.CLI.Core.DeviceManagement;
using Meadow.CLI.Core.Devices;
using Meadow.Helpers;
using Meadow.Utility;
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
            logger?.DisconnectPane();
            logger?.ConnectTextWriter(outputPaneWriter);
            isAppDeploy = false;

            var generalProperties = await Properties.GetConfigurationGeneralPropertiesAsync();
            var name = await generalProperties.Rule.GetPropertyValueAsync("AssemblyName");
            
            if(name != "App")
            {
                return;
            }

            isAppDeploy = true;

            var projectDir = await generalProperties.Rule.GetPropertyValueAsync("ProjectDir");
            var outputPath = Path.Combine(projectDir, await generalProperties.Rule.GetPropertyValueAsync("OutputPath"));

            var settings = new MeadowSettings(Globals.SettingsFilePath);

            if (string.IsNullOrEmpty(settings.DeviceTarget))
            {
                throw new Exception("Device has not been selected. Hit Ctrl+Shift+M to access the Device list.");
            }

            var attachedDevices = MeadowDeviceManager.GetSerialPorts();

            if(attachedDevices.Where(p => p.Port == settings.DeviceTarget).Any() == false)
            // if (!attachedDevices.Contains(settings.DeviceTarget))
            {
                throw new Exception($"Device on '{settings.DeviceTarget}' is not connected or busy.");
            }

            await DeployAppAsync(settings.DeviceTarget, Path.Combine(projectDir, outputPath), cts).ConfigureAwait(false);
        }

        //let's keep it around
        static MeadowDeviceHelper meadow;
        static OutputLogger logger = new OutputLogger();
        bool isAppDeploy = false;

        async Task DeployAppAsync(string target, string folder, CancellationToken token)
        {
            meadow?.Dispose();

            try
            {
                var device = await MeadowDeviceManager.GetMeadowForSerialPort(target, logger: logger);

                if(device == null)
                {
                    return;
                }

                meadow = new MeadowDeviceHelper(device, logger);

                var appPathDll = Path.Combine(folder, "App.dll");

                await meadow.DeployAppAsync(appPathDll, true, token);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public bool IsDeploySupported
        {
            get { return true; }
        }

        public void Commit()
        {
            if (isAppDeploy == false)
                return;

            IVsOutputWindow outWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            Guid generalPaneGuid = VSConstants.GUID_OutWindowDebugPane; // P.S. There's also the GUID_OutWindowDebugPane available.
            
            IVsOutputWindowPane generalPane;
            outWindow.GetPane(ref generalPaneGuid, out generalPane);
            generalPane.Activate();
            generalPane.Clear();

            generalPane.OutputString(" Launching application..." + Environment.NewLine);

            logger.DisconnectTextWriter();
            logger.ConnectPane(generalPane);
        }

        public void Rollback()
        {
        }
    }
}