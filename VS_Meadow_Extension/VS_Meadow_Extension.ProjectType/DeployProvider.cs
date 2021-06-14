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
        //let's keep it around
        static MeadowDeviceHelper meadow;
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
            
            if(name != "App")
            {
                return;
            }

            isAppDeploy = true;

            var projectDir = await generalProperties.Rule.GetPropertyValueAsync("ProjectDir");
            var outputPath = Path.Combine(projectDir, await generalProperties.Rule.GetPropertyValueAsync("OutputPath"));

            var device = await MeadowProvider.GetMeadowSerialDeviceAsync(logger);

            if (device == null)
            {
                return;
            }

            await DeployAppAsync(meadow, Path.Combine(projectDir, outputPath), new OutputPaneWriter(outputPaneWriter), cts).ConfigureAwait(false);
        }

        async Task DeployAppAsync(MeadowSerialDevice device, string folder, IOutputPaneWriter outputPaneWriter, CancellationToken cts)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(meadow));
            }

            meadow?.Dispose();

            meadow = new MeadowDeviceHelper(device, logger);

            var appPathDll = Path.Combine(folder, "App.dll");

            await meadow.DeployAppAsync(appPathDll, true, token);
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