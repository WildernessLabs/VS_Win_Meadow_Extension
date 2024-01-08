using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using EnvDTE;
using Meadow.CLI.Core;
using Meadow.CLI.Core.DeviceManagement;
using Meadow.CLI.Core.Devices;
using Meadow.Helpers;
using Meadow.Utility;
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

        [ImportingConstructor]
        public DeployProvider(ConfiguredProject configuredProject)
        {
            this.configuredProject = configuredProject;
        }

        public async Task DeployAsync(CancellationToken cts, TextWriter outputPaneWriter)
        {
            if (cts.IsCancellationRequested)
            {
                return;
            }

            // Get the currently selected project
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            if (dte == null)
            {
                return;
            }

            var solution = dte.Solution;
            var startupProjects = solution.SolutionBuild.StartupProjects;
            if (startupProjects == null)
            {
                return;
            }

            MeadowPackage.DebugOrDeployInProgress = false;

            foreach (string filename in (Array)startupProjects)
            {
                if (!filename.EndsWith(".csproj"))
                {
                    continue;
                }
                var csprojContent = File.ReadAllText(filename);
                if (csprojContent.Contains("Sdk=\"Meadow.Sdk/1.1.0\""))
                {
                    await DeployOutputLogger?.ConnectTextWriter(outputPaneWriter);

                    var generalProperties = await Properties.GetConfigurationGeneralPropertiesAsync();

                    foreach (var item in generalProperties.Rule.Properties)
                    {
                        var val = await item.GetValueAsync();
                        DeployOutputLogger?.Log($"{item.Name}: {val}");
                    }

                    var csprojPath = await generalProperties.Rule.GetPropertyValueAsync("ProjectDir");

                    if (csprojPath.Contains(filename))
                    {
                        var projectDir = await generalProperties.Rule.GetPropertyValueAsync("ProjectDir");
                        var outputPath = Path.Combine(projectDir, await generalProperties.Rule.GetPropertyValueAsync("OutputPath"));

                        try
                        {
                            MeadowPackage.DebugOrDeployInProgress = true;
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
                    break;
                }
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