using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Meadow.CLI.Core;
using Meadow.CLI.Core.DeviceManagement;
using Meadow.CLI.Core.Devices;
using Meadow.Helpers;
using Meadow.Utility;
using Microsoft.Build.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using static IdentityModel.OidcConstants;
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

        const string MeadowSDKVersion = "Sdk=\"Meadow.Sdk/1.1.0\"";

        [ImportingConstructor]
        public DeployProvider(ConfiguredProject configuredProject)
        {
            this.configuredProject = configuredProject;
        }

        public async Task<bool> DeployMeadowProjectsAsync(CancellationToken cts, TextWriter outputPaneWriter)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            if (dte == null)
            {
                return false;
            }

            var solution = dte.Solution;
            var startupProjects = solution.SolutionBuild.StartupProjects;
            if (startupProjects == null)
            {
                return false;
            }

            MeadowPackage.DebugOrDeployInProgress = false;

            foreach (string filename in (Array)startupProjects)
            {
                if (cts.IsCancellationRequested)
                {
                    return false;
                }

                if (!filename.EndsWith(".csproj"))
                {
                    continue;
                }

                var csprojContent = File.ReadAllText(filename);
                if (csprojContent.Contains(MeadowSDKVersion))
                {
                    await DeployOutputLogger?.ConnectTextWriter(outputPaneWriter);

                    return await DeployMeadowAppAsync(cts, outputPaneWriter, filename);
                }
            }

            return false;
        }

        private async Task<bool> DeployMeadowAppAsync(CancellationToken cts, TextWriter outputPaneWriter, string filename)
        {
            try
            {
                var generalProperties = await Properties.GetConfigurationGeneralPropertiesAsync();

				var projectFullPath = await generalProperties.Rule.GetPropertyValueAsync("MSBuildProjectFullPath");
                if (projectFullPath.Contains(filename))
                {
                    var projectDir = await generalProperties.Rule.GetPropertyValueAsync("ProjectDir");
					var outputPath = Path.Combine(projectDir, await generalProperties.Rule.GetPropertyValueAsync("OutputPath"));

                    MeadowPackage.DebugOrDeployInProgress = true;
                    await DeployAppAsync(outputPath, new OutputPaneWriter(outputPaneWriter), cts);

                    return true;
                }
                else
                {
                    DeployFailed();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);

                DeployFailed();
            }

            return false;
        }

        private static void DeployFailed()
        {
            MeadowPackage.DebugOrDeployInProgress = false;

            DeployOutputLogger?.Log("Deploy failed. Please Reset Meadow and try again.");
        }

        public async Task DeployAsync(CancellationToken cts, TextWriter outputPaneWriter)
        {
            if (cts.IsCancellationRequested)
            {
                return;
            }

            await DeployMeadowProjectsAsync(cts, outputPaneWriter);
        }

        async Task DeployAppAsync(string folder, IOutputPaneWriter outputPaneWriter, CancellationToken token)
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

            var includePdbs = false;
            await Meadow.DeployApp(appPathDll, includePdbs, token);
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