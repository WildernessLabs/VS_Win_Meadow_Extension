using Meadow.CLI;
using Meadow.Package;
using Meadow.Software;
using Meadow.Utility;
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
    internal class DeployProvider : IDeployProvider
    {
        public static OutputLogger DeployOutputLogger = new OutputLogger();

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
        public DeployProvider(ConfiguredProject configuredProject)
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

            await DeployOutputLogger?.ConnectTextWriter(textWriter);

            var outputPath = await GetOutputPath(filename);

            if (string.IsNullOrEmpty(outputPath))
            {
                DeployFailed();
                return;
            }

            var outputPaneWriter = new OutputPaneWriter(textWriter);

            var meadowConnection = MeadowConnection.GetCurrentConnection();

            meadowConnection.FileWriteProgress += MeadowConnection_DeploymentProgress;

            if (eventSubscribed == false)
            {
                meadowConnection.DeviceMessageReceived += MeadowConnection_DeviceMessageReceived;
                eventSubscribed = true;
            }

            try
            {
                await meadowConnection.WaitForMeadowAttach();

                var fileManager = new FileManager(null);
                await fileManager.Refresh();

                bool includePdbs = configuredProject?.ProjectConfiguration?.Dimensions["Configuration"].Contains("Debug") ?? false;

                var packageManager = new PackageManager(fileManager);

                await packageManager.TrimApplication(new FileInfo(Path.Combine(outputPath, "App.dll")), includePdbs, cancellationToken: cancellationToken);

                await AppManager.DeployApplication(packageManager, meadowConnection, outputPath, includePdbs, false, DeployOutputLogger, cancellationToken);
            }
            finally
            {
                meadowConnection.FileWriteProgress -= MeadowConnection_DeploymentProgress;
                //meadowConnection.DeviceMessageReceived -= MeadowConnection_DeviceMessageReceived;
            }
        }

        private async Task<string> GetOutputPath(string filename)
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
            DeployOutputLogger?.Log("Deploy failed - please reset Meadow and try again");
        }

        private void MeadowConnection_DeviceMessageReceived(object sender, (string message, string source) e)
        {
            _ = DeployOutputLogger.ReportDeviceMessage(e.source, e.message);
        }

        private void Firmware_DownloadProgress(object sender, long e)
        {
            _ = DeployOutputLogger.ReportDownloadProgress(osVersion, e);
        }

        private async void MeadowConnection_DeploymentProgress(object sender, (string fileName, long completed, long total) e)
        {
            var p = (uint)(e.completed / e.total * 100d);

            await DeployOutputLogger?.ReportFileProgress(e.fileName, p);

            if (p == 100)
            {
                await DeployOutputLogger?.ResetProgressBar();
            }
        }

        public async void Commit()
        {
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