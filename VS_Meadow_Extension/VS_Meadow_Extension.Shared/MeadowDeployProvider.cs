using Meadow.CLI;
using Meadow.CLI.Commands.DeviceManagement;
using Meadow.Package;
using Meadow.Software;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace Meadow
{
    [Export(typeof(IDeployProvider))]
    [AppliesTo(Globals.MeadowCapability)]
    internal class MeadowDeployProvider : IDeployProvider
    {
        static readonly OutputLogger outputLogger = OutputLogger.Instance;

        /// <summary>
        /// Provides access to the project's properties
        /// </summary>
        [Import]
        private ProjectProperties Properties { get; set; }

        private readonly ConfiguredProject configuredProject;

        const string MeadowSDKVersion = "Sdk=\"Meadow.Sdk/1.1.0\"";

        public bool IsDeploySupported
        {
            get
            {
                return true;

                //  IsProjectAMeadowApp().ContinueWith(t => IsDeploySupported = t.Result);
            }
        }

        static Hcom.IMeadowConnection connection = null;
        private readonly CLI.SettingsManager settingsManager = new CLI.SettingsManager();
        private readonly MeadowConnectionManager connectionManager = null;

        [ImportingConstructor]
        public MeadowDeployProvider(ConfiguredProject configuredProject)
        {
            this.configuredProject = configuredProject;
            this.connectionManager = new MeadowConnectionManager(settingsManager);
        }

        public async Task DeployAsync(CancellationToken cancellationToken, TextWriter textWriter)
        {
            if (cancellationToken.IsCancellationRequested || !await IsProjectAMeadowApp())
            {
                return;
            }


            Globals.DebugOrDeployInProgress = true;

            await outputLogger?.ConnectTextWriter(textWriter);
            await outputLogger.ShowBuildOutputPane();

            outputLogger.Log("Preparing to deploy Meadow application...");

            var filename = configuredProject.UnconfiguredProject.FullPath;

            var projFileContent = File.ReadAllText(filename);

            if (projFileContent.Contains(MeadowSDKVersion) == false)
            {
                Globals.DebugOrDeployInProgress = false;
                outputLogger?.Log("Deploy failed - not a Meadow project");
                return;
            }

            var outputPath = await GetOutputPathAsync(filename);

            if (string.IsNullOrEmpty(outputPath))
            {
                Globals.DebugOrDeployInProgress = false;
                outputLogger?.Log("Deploy failed - could not locate Meadow app");
                return;
            }

            if (connection != null)
            {
                connection.FileWriteProgress -= MeadowConnection_DeploymentProgress;
                connection.DeviceMessageReceived -= MeadowConnection_DeviceMessageReceived;
                connection = null;
            }

            var route = settingsManager.GetSetting(CLI.SettingsManager.PublicSettings.Route);
            connection = connectionManager.GetConnectionForRoute(route);

            connection.FileWriteProgress += MeadowConnection_DeploymentProgress;
            connection.DeviceMessageReceived += MeadowConnection_DeviceMessageReceived;

            try
            {
                await connection.WaitForMeadowAttach();

                if (await connection.IsRuntimeEnabled() == true)
                {
                    await connection.RuntimeDisable();
                }

                var deviceInfo = await connection.GetDeviceInfo();

                string osVersion = deviceInfo.OsVersion;

                var fileManager = new FileManager(null);
                await fileManager.Refresh();

                bool includePdbs = configuredProject?.ProjectConfiguration?.Dimensions["Configuration"].Contains("Debug") ?? false;

                var packageManager = new PackageManager(fileManager);

                outputLogger.Log("Trimming application binaries...");
                await packageManager.TrimApplication(new FileInfo(Path.Combine(outputPath, "App.dll")), osVersion, includePdbs, cancellationToken: cancellationToken);

                outputLogger.Log("Deploying application...");
                await AppManager.DeployApplication(packageManager, connection, osVersion, outputPath, includePdbs, false, outputLogger, cancellationToken);

                await connection.RuntimeEnable();

                await outputLogger.ShowBuildOutputPane();
            }
            finally
            {
                connection.FileWriteProgress -= MeadowConnection_DeploymentProgress;
            }
        }

        private async Task<string> GetOutputPathAsync(string filename)
        {
            var generalProperties = await Properties.GetConfigurationGeneralPropertiesAsync();

            var projectFullPath = await generalProperties.Rule.GetPropertyValueAsync("MSBuildProjectFullPath");

            if (projectFullPath.Contains(filename) == false)
            {
                return string.Empty;
            }

            var projectDir = await generalProperties.Rule.GetPropertyValueAsync("ProjectDir");
            var outputPath = Path.Combine(projectDir, await generalProperties.Rule.GetPropertyValueAsync("OutputPath"));

            return outputPath;
        }

        private static async void MeadowConnection_DeviceMessageReceived(object sender, (string message, string source) e)
        {
            await outputLogger.ReportDeviceMessage(e.message);
        }

        private static async void MeadowConnection_DeploymentProgress(object sender, (string fileName, long completed, long total) e)
        {
            uint p = 0;

            if (e.total != 0)
            {
                p = (uint)(e.completed * 100f / e.total);
            }
            else
            {
                await outputLogger?.ResetProgressBar();
            }

            await outputLogger?.ReportFileProgress(e.fileName, p);
        }

        public async void Commit()
        {
            await outputLogger?.ShowMeadowOutputPane();
            outputLogger?.Log("Launching application..." + Environment.NewLine);

            Globals.DebugOrDeployInProgress = false;
        }

        public void Rollback()
        {
            Globals.DebugOrDeployInProgress = false;
            Console.Write("Rolling Back");
        }

        private async Task<bool> IsProjectAMeadowApp()
        {
            // Assume configuredProject is your ConfiguredProject object
            var properties = configuredProject.Services.ProjectPropertiesProvider.GetCommonProperties();

            // We need to retrieve the AssemblyName property because we need both
            // the configuredProject to be a start-up project, and also an App (not library)
            string assemblyName = await properties.GetEvaluatedPropertyValueAsync("AssemblyName");

            if (!string.IsNullOrEmpty(assemblyName) &&
                assemblyName.Equals("App", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }
    }
}