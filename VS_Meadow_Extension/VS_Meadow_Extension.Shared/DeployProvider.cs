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
        static OutputLogger logger = new OutputLogger();
        bool appDeployed = false;

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
            logger?.DisconnectPane();
            logger?.ConnectTextWriter(outputPaneWriter);
            appDeployed = false;

            var generalProperties = await Properties.GetConfigurationGeneralPropertiesAsync();
            var name = await generalProperties.Rule.GetPropertyValueAsync("AssemblyName");
            
            //This is to avoid repeat deploys for multiple projects in the solution
            if(name != "App")
            {
                return;
            }

            appDeployed = true;

            var projectDir = await generalProperties.Rule.GetPropertyValueAsync("ProjectDir");
            var outputPath = Path.Combine(projectDir, await generalProperties.Rule.GetPropertyValueAsync("OutputPath"));

            try
            {
                await DeployAppAsync(Path.Combine(projectDir, outputPath), new OutputPaneWriter(outputPaneWriter), cts).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                appDeployed = false;
                logger?.Log($"Deploy failed: {ex.Message}");
                logger?.Log("Reset Meadow and try again.");
                throw ex;
            }
        }

        async Task DeployAppAsync(string folder, IOutputPaneWriter outputPaneWriter, CancellationToken token)
        {
            try
            {
                Meadow?.Dispose();

                var device = await MeadowProvider.GetMeadowSerialDeviceAsync(logger);
                if (device == null)
                {
                    appDeployed = false;
                    throw new Exception("A device has not been selected. Please attach a device, then select it from the Device list.");
                }

                Meadow = new MeadowDeviceHelper(device, logger);

                var appPathDll = Path.Combine(folder, "App.dll");

                if (Meadow.DeviceAndAppVersionsMatch(appPathDll))
                {

                    //wrap this is a try/catch so it doesn't crash if the developer is offline
                    try
                    {
                        string osVersion = await Meadow.GetOSVersion(TimeSpan.FromSeconds(30), token)
                            .ConfigureAwait(false);

                        await new DownloadManager(logger).DownloadLatestAsync(osVersion)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        logger.Log("OS download failed, make sure you have an active internet connection");
                    }

                    var includePdbs = configuredProject?.ProjectConfiguration?.Dimensions["Configuration"].Contains("Debug");
                    await Meadow.DeployAppAsync(appPathDll, includePdbs.HasValue && includePdbs.Value, token);
                }
            }
            catch (Exception ex)
            {
                appDeployed = false;
                await outputPaneWriter.WriteAsync($"Deploy failed: {ex.Message}//nStackTrace://n{ex.StackTrace}");
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
            if (!appDeployed)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var meadowOutputPane = new VsOutputPaneLogger(configuredProject.UnconfiguredProject.ProjectService.Services.ThreadingPolicy);

            meadowOutputPane.Report("Launching application..." + Environment.NewLine);

            logger.DisconnectTextWriter();
            logger.ConnectPane(meadowOutputPane.Pane);
        }

        public void Rollback()
        {
            Console.Write("Rolling Back");
        }
    }
}