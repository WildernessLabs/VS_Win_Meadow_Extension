using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Meadow.CLI.Core;
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
        public static OutputLogger DeployOutputLogger = new OutputLogger();

        /// <summary>
        /// Provides access to the project's properties.
        /// </summary>
        [Import]
        private ProjectProperties Properties { get; set; }

        private ConfiguredProject configuredProject;

        [ImportingConstructor]
        public DeployProvider(ConfiguredProject configuredProject)
        {
            this.configuredProject = configuredProject;
        }

        public async Task DeployAsync(CancellationToken cts, TextWriter outputPaneWriter)
        {
            await DeployOutputLogger?.ConnectTextWriter(outputPaneWriter);
            MeadowPackage.DebugOrDeployInProgress = false;

            var generalProperties = await Properties.GetConfigurationGeneralPropertiesAsync();
            var name = await generalProperties.Rule.GetPropertyValueAsync("AssemblyName");

            //This is to avoid repeat deploys for multiple projects in the solution
            if (name != "App")
            {
                return;
            }

            MeadowPackage.DebugOrDeployInProgress = true;

            var projectDir = await generalProperties.Rule.GetPropertyValueAsync("ProjectDir");
            var outputPath = Path.Combine(projectDir, await generalProperties.Rule.GetPropertyValueAsync("OutputPath"));

            try
            {
                await DeployAppAsync(Path.Combine(projectDir, outputPath), new OutputPaneWriter(outputPaneWriter), cts);
            }
            catch (Exception ex)
            {
                MeadowPackage.DebugOrDeployInProgress = false;

                DeployOutputLogger?.Log($"Deploy failed: {ex.Message}{Environment.NewLine}StackTrace:{Environment.NewLine}{ex.StackTrace}");
                DeployOutputLogger?.Log("Reset Meadow and try again.");

                throw ex;
            }
        }

        async Task DeployAppAsync(string folder, IOutputPaneWriter outputPaneWriter, CancellationToken token)
        {
            try
            {
                Meadow?.Dispose();

                var device = await MeadowProvider.GetMeadowSerialDeviceAsync(DeployOutputLogger);
                if (device == null)
                {
                    MeadowPackage.DebugOrDeployInProgress = false;
                    throw new Exception("A device has not been selected. Please attach a device, then select it from the Device list.");
                }

                Meadow = new MeadowDeviceHelper(device, DeployOutputLogger);

                //wrap this is a try/catch so it doesn't crash if the developer is offline
                try
                {
                    string osVersion = await Meadow.GetOSVersion(TimeSpan.FromSeconds(30), token);

                    await new DownloadManager(DeployOutputLogger).DownloadOsBinaries(osVersion);
                }
                catch
                {
                    DeployOutputLogger?.Log("OS download failed, make sure you have an active internet connection");
                }

                var appPathDll = Path.Combine(folder, "App.dll");

                var includePdbs = configuredProject?.ProjectConfiguration?.Dimensions["Configuration"].Contains("Debug");
                await Meadow.DeployApp(appPathDll, includePdbs.HasValue && includePdbs.Value, token);
            }
            catch (Exception ex)
            {
                MeadowPackage.DebugOrDeployInProgress = false;
                await outputPaneWriter.WriteAsync($"Deploy failed: {ex.Message}{Environment.NewLine}StackTrace:{Environment.NewLine}{ex.StackTrace}");
                await outputPaneWriter.WriteAsync($"Reset Meadow and try again.");
                throw ex;
            }
        }

        public bool IsDeploySupported
        {
            get { return true; }
        }

        public async void Commit()
        {
            if (!MeadowPackage.DebugOrDeployInProgress)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            await DeployOutputLogger?.ShowMeadowLogs();
            DeployOutputLogger?.Log("Launching application..." + Environment.NewLine);

            DeployOutputLogger?.DisconnectTextWriter();

            MeadowPackage.DebugOrDeployInProgress = false;
        }

        public void Rollback()
        {
            MeadowPackage.DebugOrDeployInProgress = false;
            Console.Write("Rolling Back");
        }
    }
}
