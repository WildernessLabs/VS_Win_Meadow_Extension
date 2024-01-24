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
using Microsoft.Build.Evaluation;
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

        private DTE dte;

        [ImportingConstructor]
        public DeployProvider(ConfiguredProject configuredProject)
        {
            this.configuredProject = configuredProject;
        }

        public async Task<bool> DeployMeadowProjectsAsync(CancellationToken cts, TextWriter outputPaneWriter)
        {
            if (cts.IsCancellationRequested)
            {
                return false;
            }

            MeadowPackage.DebugOrDeployInProgress = false;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var filename = this.configuredProject.UnconfiguredProject.FullPath;

            var projFileContent = File.ReadAllText(filename);
            if (projFileContent.Contains(MeadowSDKVersion))
            {
                if (await IsMeadowApp())
                {
                    await DeployOutputLogger?.ConnectTextWriter(outputPaneWriter);

                    return await DeployMeadowAppAsync(cts, outputPaneWriter, filename);
                }
            }

            return false;
        }

        private async Task<bool> IsMeadowApp()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Get the DTE service
            if (dte == null)
            {
                dte = Package.GetGlobalService(typeof(SDTE)) as DTE;
            }

            if (dte.Solution != null)
            {
                var startupProjects = dte.Solution.SolutionBuild?.StartupProjects as Array;
                foreach (string startupProject in startupProjects)
                {
                    if (configuredProject.UnconfiguredProject.FullPath.Contains(startupProject))
                    {
                        // Assume configuredProject is your ConfiguredProject object
                        var properties = configuredProject.Services.ProjectPropertiesProvider.GetCommonProperties();

                        // We unfortunately still need to retrieve the AssemblyName property because we need both
                        // the configuredProject to be a start-up project, but also an App (not library)
                        string assemblyName = await properties.GetEvaluatedPropertyValueAsync("AssemblyName");
                        if (!string.IsNullOrEmpty(assemblyName) && assemblyName.Equals("App", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
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

            var includePdbs = configuredProject?.ProjectConfiguration?.Dimensions["Configuration"].Contains("Debug");
            await Meadow.DeployApp(appPathDll, includePdbs.HasValue && includePdbs.Value, token);
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