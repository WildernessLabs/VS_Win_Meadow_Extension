using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        bool appDeploying = false;

        /// <summary>
        /// Provides access to the project's properties.
        /// </summary>
        [Import]
        private ProjectProperties Properties { get; set; }

        private ConfiguredProject configuredProject;
        private static ConfigurationGeneral generalProperties;

        [ImportingConstructor]
        public DeployProvider(ConfiguredProject configuredProject)
        {
            this.configuredProject = configuredProject;
        }

        public void FindDeployingProject(EnvDTE.Project project, string csProjFilename)
        {
            if (!string.IsNullOrEmpty(project.FileName))
            {
                if (project.FileName.Contains(csProjFilename))
                {
                    var name = project?.Properties.Cast<EnvDTE.Property>().FirstOrDefault(x => x.Name == "AssemblyName")?.Value as string;
                    if (!string.IsNullOrEmpty(name) && name == "App")
                    {

                        var outputPath = project?.ConfigurationManager?.ActiveConfiguration?.Properties.Cast<EnvDTE.Property>().FirstOrDefault(x => x.Name == "OutputPath")?.Value as string;
                        var projectDir = Path.GetDirectoryName(fullPathToCsProj);
                        fullOutputPath = Path.Combine(projectDir, outputPath);
                    }
                    return;
                }
            }
            else
            {
                if (project.ProjectItems != null)
                {
                    foreach (EnvDTE.ProjectItem item in project.ProjectItems)
                    {
                        EnvDTE.Project nextlevelprj = item.Object as EnvDTE.Project;
                        if (nextlevelprj != null)
                            FindDeployingProject(nextlevelprj, csProjFilename);
                        if (!string.IsNullOrEmpty(fullOutputPath))
                            return;
                    }
                }
            }
            fullOutputPath = string.Empty;
            return;
        }

        string fullOutputPath = string.Empty;
        string fullPathToCsProj = string.Empty;

        async Task SetFullOutputPath()
        {
            var name = await GetPropertyValueFromRule("AssemblyName");
            if (!string.IsNullOrEmpty(name) && name != "App")
            {
                var dte2 = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;

                string msg = string.Empty;

                var solution = dte2?.Solution;
                var sb = solution.SolutionBuild;
                var ac = sb.ActiveConfiguration;

                Array startupProjs = sb.StartupProjects as Array;
                if (startupProjs.Length > 1)
                    throw new Exception("Too Many Startup projects");

                var relativePathToCsProj = startupProjs.GetValue(0) as string;
                var csProjFilename = Path.GetFileName(relativePathToCsProj);
                var solDir = Path.GetDirectoryName(solution.FileName);
                fullPathToCsProj = Path.Combine(solDir, relativePathToCsProj);

                foreach (object prj in dte2.Solution.Projects)
                {
                    EnvDTE.Project proj = prj as EnvDTE.Project;
                    if (proj != null)
                    {
                        FindDeployingProject(proj, csProjFilename);
                        if (!string.IsNullOrEmpty(fullOutputPath))
                            break;
                    }

                }
            }
            else
            {
                var projectDir = await GetPropertyValueFromRule("ProjectDir");
                fullOutputPath = Path.Combine(projectDir, await GetPropertyValueFromRule("OutputPath"));
            }
        }

        private async Task<string> GetPropertyValueFromRule(string property)
        {
            return await generalProperties.Rule.GetPropertyValueAsync(property);
        }

        public async Task DeployAsync(CancellationToken cts, TextWriter outputPaneWriter)
        {
            if (appDeploying)
                return;

            try
            {
                logger?.DisconnectPane();
                logger?.ConnectTextWriter(outputPaneWriter);

                if (generalProperties == null)
                    generalProperties = await Properties.GetConfigurationGeneralPropertiesAsync();

                await SetFullOutputPath();

                //This is to avoid repeat deploys for multiple projects in the solution
                if (string.IsNullOrEmpty(fullOutputPath))
                {
                    throw new Exception("Meadow project not found. Please select a Meadow project to Deploy or Debug.");
                }

                appDeploying = true;

                await DeployAppAsync(fullOutputPath, new OutputPaneWriter(outputPaneWriter), cts);
            }
            catch (Exception ex)
            {
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
                    appDeploying = false;
                    throw new Exception("A device has not been selected. Please attach a device, then select it from the Device list.");
                }

                Meadow = new MeadowDeviceHelper(device, logger);

                //wrap this is a try/catch so it doesn't crash if the developer is offline
                try
                {
                    string osVersion = await Meadow.GetOSVersion(TimeSpan.FromSeconds(30), token);

                    await new DownloadManager(logger).DownloadOsBinaries(osVersion);
                }
                catch
                {
                    logger.Log("OS download failed, make sure you have an active internet connection");
                }

                var appPathDll = Path.Combine(folder, "App.dll");

                var includePdbs = configuredProject?.ProjectConfiguration?.Dimensions["Configuration"].Contains("Debug");
                await Meadow.DeployApp(appPathDll, includePdbs.HasValue && includePdbs.Value, token);
            }
            catch (Exception ex)
            {
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
            if (!appDeploying)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var meadowOutputPane = new VsOutputPaneLogger(configuredProject.UnconfiguredProject.ProjectService.Services.ThreadingPolicy);

            meadowOutputPane.Report("Launching application..." + Environment.NewLine);

            logger.DisconnectTextWriter();
            logger.ConnectPane(meadowOutputPane.Pane);

            // Deployment has finished so we're not long Deploying
            appDeploying = false;
        }

        public void Rollback()
        {
            // Deployment failed so we're no long Deploying
            appDeploying = false;
        }
    }
}