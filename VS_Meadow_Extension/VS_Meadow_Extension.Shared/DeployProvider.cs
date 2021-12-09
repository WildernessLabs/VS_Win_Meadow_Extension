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
    [AppliesTo(Globals.MeadowCapability)]
    internal class DeployProvider : IDeployProvider
    {
        // TODO: tad bit hack right now - maybe we can use DI to import this DeployProvider
        // in the debug launch provider ?
        internal static MeadowDeviceHelper Meadow;
        static OutputLogger logger = new OutputLogger();
        bool isAppDeploy = false;

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
            
            //This is to avoid repeat deploys for multiple projects in the solution
            if(name != "App")
            {
                return;
            }

            isAppDeploy = true;

            var projectDir = await generalProperties.Rule.GetPropertyValueAsync("ProjectDir");
            var outputPath = Path.Combine(projectDir, await generalProperties.Rule.GetPropertyValueAsync("OutputPath"));

            try
            {
                var device = await MeadowProvider.GetMeadowSerialDeviceAsync(logger);

                if (device == null)
                {
                    isAppDeploy = false;
                    logger?.Log("Device has not been selected. Hit Ctrl+Shift+M to access the Device list.");
                }

                await DeployAppAsync(device, Path.Combine(projectDir, outputPath), new OutputPaneWriter(outputPaneWriter), cts).ConfigureAwait(false);
            }
            catch
            {
                isAppDeploy = false;
                logger?.Log("Deploy failed - reset Meadow and try again.");
            }
        }

        async Task DeployAppAsync(IMeadowDevice device, string folder, IOutputPaneWriter outputPaneWriter, CancellationToken token)
        {
            try
            {
                Meadow?.Dispose();

                Meadow = new MeadowDeviceHelper(device, logger);

                var appPathDll = Path.Combine(folder, "App.dll");

                await Meadow.DeployAppAsync(appPathDll, true, token);
            }
            catch (Exception ex)
            {
                isAppDeploy = false;
                await outputPaneWriter.WriteAsync($"Deploy failed: {ex.Message}");
                await outputPaneWriter.WriteAsync($"Reset Meadow and try again");
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

            ThreadHelper.ThrowIfNotOnUIThread();

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