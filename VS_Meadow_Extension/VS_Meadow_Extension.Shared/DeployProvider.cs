using Meadow.CLI;
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

        private readonly string osVersion;

        [ImportingConstructor]
        public MeadowDeployProvider(ConfiguredProject configuredProject)
        {
            this.configuredProject = configuredProject;
        }

        private bool eventSubscribed = false;

        public async Task DeployAsync(CancellationToken cancellationToken, TextWriter textWriter)
        {
            if (await IsProjectAMeadowApp() == false)
            {
                return;
            }

            MeadowPackage.DebugOrDeployInProgress = true;

            var filename = configuredProject.UnconfiguredProject.FullPath;

            var projFileContent = File.ReadAllText(filename);

            if (projFileContent.Contains(MeadowSDKVersion) == false)
            {
                DeployFailed();
                return;
            }

            await outputLogger?.ConnectTextWriter(textWriter);

            var outputPath = await GetOutputPathAsync(filename);

            if (string.IsNullOrEmpty(outputPath))
            {
                DeployFailed();
                return;
            }

            var connection = MeadowConnection.GetCurrentConnection();

            if (eventSubscribed == false)
            {
                connection.FileWriteProgress += MeadowConnection_DeploymentProgress;
                connection.DeviceMessageReceived += MeadowConnection_DeviceMessageReceived;
                eventSubscribed = true;
            }

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

                await packageManager.TrimApplication(new FileInfo(Path.Combine(outputPath, "App.dll")), osVersion, includePdbs, cancellationToken: cancellationToken);

                await Task.Run(async () => await AppManager.DeployApplication(packageManager, connection, osVersion, outputPath, includePdbs, false, outputLogger, cancellationToken));

                await connection.RuntimeEnable();
            }
            finally
            {
                // connection.FileWriteProgress -= MeadowConnection_DeploymentProgress;
                // connection.DeviceMessageReceived -= MeadowConnection_DeviceMessageReceived;
            }
        }

        private async Task<string> GetOutputPathAsync(string filename)
        {
            var generalProperties = await Properties.GetConfigurationGeneralPropertiesAsync();

            var projectFullPath = await generalProperties.Rule.GetPropertyValueAsync("MSBuildProjectFullPath");

            if (projectFullPath.Contains(filename) == false)
            {
                DeployFailed();
                return string.Empty;
            }

            var projectDir = await generalProperties.Rule.GetPropertyValueAsync("ProjectDir");
            var outputPath = Path.Combine(projectDir, await generalProperties.Rule.GetPropertyValueAsync("OutputPath"));

            return outputPath;
        }

        private static void DeployFailed()
        {
            MeadowPackage.DebugOrDeployInProgress = false;
            outputLogger?.Log("Deploy failed - please reset Meadow and try again");
        }

        private void MeadowConnection_DeviceMessageReceived(object sender, (string message, string source) e)
        {
            _ = outputLogger.ReportDeviceMessage(e.message);
        }

        private async void MeadowConnection_DeploymentProgress(object sender, (string fileName, long completed, long total) e)
        {
            if (e.total == 0)
            {
                return;
            }

            var p = (uint)(e.completed / e.total * 100d);

            await outputLogger?.ReportFileProgress(e.fileName, p);

            if (p == 100)
            {
                await outputLogger?.ResetProgressBar();
            }
        }

        public async void Commit()
        {
            await outputLogger?.ShowMeadowLogs();
            outputLogger?.Log("Launching application..." + Environment.NewLine);

            outputLogger?.DisconnectTextWriter();

            MeadowPackage.DebugOrDeployInProgress = false;
        }

        public void Rollback()
        {
            MeadowPackage.DebugOrDeployInProgress = false;
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