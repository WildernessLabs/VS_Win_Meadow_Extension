using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Meadow.CLI;
using Meadow.Deployment;
using Meadow.Hcom;
using Meadow.Package;
using Meadow.Software;
using Meadow.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace Meadow
{
    [Export(typeof(IDeployProvider))]
    [AppliesTo(Globals.MeadowCapability)]
    internal class DeployProvider : IDeployProvider
    {
        // TODO: tad bit hack right now - maybe we can use DI to import this DeployProvider
        // in the debug launch provider ?
        internal static IMeadowConnection MeadowConnection;
        public static OutputLogger DeployOutputLogger = new OutputLogger();

        /// <summary>
        /// Provides access to the project's properties.
        /// </summary>
        [Import]
        private ProjectProperties Properties { get; set; }

        private ConfiguredProject configuredProject;

        const string MeadowSDKVersion = "Sdk=\"Meadow.Sdk/1.1.0\"";

        private SettingsManager settingsManager;

        private bool isDeploySupported = true;
        private string osVersion;

        [ImportingConstructor]
        public DeployProvider(ConfiguredProject configuredProject)
        {
            this.configuredProject = configuredProject;

            settingsManager = new CLI.SettingsManager();

            _ = Task.Run(async () => { isDeploySupported = await IsMeadowApp(); });
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
                DeployFailed();

                throw ex;
            }

            return false;
        }

        private static void DeployFailed()
        {
            MeadowPackage.DebugOrDeployInProgress = false;

            DeployOutputLogger?.Log("Deploy failed. If a Meadow device is attached, please Reset Meadow and try again.");
        }

        public async Task DeployAsync(CancellationToken cts, TextWriter outputPaneWriter)
        {
            if (cts.IsCancellationRequested)
            {
                return;
            }

            await DeployMeadowProjectsAsync(cts, outputPaneWriter);
        }

        async Task DeployAppAsync(string folder, IOutputPaneWriter outputPaneWriter, CancellationToken cancellationToken)
        {
            // TODO Meadow?.Dispose();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var route = settingsManager.GetSetting(CLI.SettingsManager.PublicSettings.Route);

            if (MeadowConnection == null)
            {
                var retryCount = 0;

            get_serial_connection:
                try
                {
                    MeadowConnection = new SerialConnection(route);
                }
                catch
                {
                    retryCount++;
                    if (retryCount > 10)
                    {
                        throw new Exception($"Cannot find port {route}");
                    }
                    Thread.Sleep(500);
                    goto get_serial_connection;
                }

            }

            MeadowConnection.FileWriteProgress += MeadowConnection_DeploymentProgress;

            try
            {
                await MeadowConnection.WaitForMeadowAttach();

                /*  device = await MeadowProvider.GetMeadowSerialDeviceAsync(DeployOutputLogger);
                if (MeadowConnection.Device == null)
                {
                    MeadowPackage.DebugOrDeployInProgress = false;
                    throw new Exception("A device has not been selected. Please attach a device, then select it from the Device list.");
                }*/

                var deviceInfo = await MeadowConnection.GetDeviceInfo(cancellationToken);
                osVersion = deviceInfo.OsVersion;

                // TODO Pass in a proper MeadowCloudClient
                var fileManager = new FileManager(null);
                await fileManager.Refresh();

                // for now we only support F7
                // TODO: add switch and support for other platforms
                var collection = fileManager.Firmware["Meadow F7"];

                /* TODO Uncomment this once we have a property MeadowCloudClient above var isAvailable = await collection.IsVersionAvailableForDownload(osVersion);

                if (!isAvailable)
                {
                    DeployOutputLogger?.Log($"Requested package version '{osVersion}' is not available");
                }
                else if (collection[osVersion] != null)
                {
                    DeployOutputLogger?.Log($"Firmware package '{osVersion}' already exists locally");
                }
                else
                {
                    DeployOutputLogger?.Log($"Downloading firmware package '{osVersion}'...");
                }


                collection.DownloadProgress += Firmware_DownloadProgress;
                try
                {
                    var result = await collection.RetrievePackage(osVersion, false);

                    if (!result)
                    {
                        DeployOutputLogger?.LogError($"Unable to download package '{osVersion}'");
                    }
                    else
                    {
                        DeployOutputLogger?.LogInformation($"Firmware package '{osVersion}' downloaded");
                    }
                }
                catch (Exception ex)
                {
                    DeployOutputLogger?.Log($"Unable to download package '{osVersion}': {ex.Message}{Environment.NewLine}Ensure you have an active internet connection.");
                }
                finally
                {
                    collection.DownloadProgress -= Firmware_DownloadProgress;
                }
                */

                var includePdbs = configuredProject?.ProjectConfiguration?.Dimensions["Configuration"].Contains("Debug");

                await AppManager.DeployApplication(null, MeadowConnection, folder, includePdbs.HasValue && includePdbs.Value, false, DeployOutputLogger, cancellationToken);
            }
            finally
            {
                MeadowConnection.FileWriteProgress -= MeadowConnection_DeploymentProgress;
            }
        }

        private async void Firmware_DownloadProgress(object sender, long e)
        {
            await DeployOutputLogger?.Report(osVersion, e);
        }

        private async void MeadowConnection_DeploymentProgress(object sender, (string fileName, long completed, long total) e)
        {
            var p = (uint)((e.completed / (double)e.total) * 100d);

            await DeployOutputLogger?.Report(e.fileName, p);

            if (p == 100)
            {
                await DeployOutputLogger?.ResetProgressBar();
            }
        }

        public bool IsDeploySupported
        {
            get { return isDeploySupported; }
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

        private async Task<bool> IsMeadowApp()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Assume configuredProject is your ConfiguredProject object
            var properties = configuredProject.Services.ProjectPropertiesProvider.GetCommonProperties();

            // We unfortunately still need to retrieve the AssemblyName property because we need both
            // the configuredProject to be a start-up project, but also an App (not library)
            string assemblyName = await properties.GetEvaluatedPropertyValueAsync("AssemblyName");
            if (!string.IsNullOrEmpty(assemblyName) && assemblyName.Equals("App", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}