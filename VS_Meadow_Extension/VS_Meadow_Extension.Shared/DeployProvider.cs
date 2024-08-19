using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Meadow.CLI;
using Meadow.CLI.Commands.DeviceManagement;
using Meadow.Hcom;
using Meadow.Package;
using Meadow.Software;
using Meadow.Utility;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace Meadow
{
    [Export(typeof(IDeployProvider))]
    [AppliesTo(Globals.MeadowCapability)]
    internal class DeployProvider : IDeployProvider, IDisposable
    {
        private static IMeadowConnection meadowConnection = null;
        public static IMeadowConnection MeadowConnection => meadowConnection;

		public static OutputLogger DeployOutputLogger = new OutputLogger();

        /// <summary>
        /// Provides access to the project's properties.
        /// </summary>
        [Import]
        private ProjectProperties Properties { get; set; }

        private ConfiguredProject configuredProject;
        private IMeadowAppService meadowAppService;
        const string MeadowSDKVersion = "Sdk=\"Meadow.Sdk/1.1.0\"";

        private bool isDeploySupported = true;
        private string osVersion;
        private bool disposedValue;

        [ImportingConstructor]
        public DeployProvider(ConfiguredProject configuredProject, IMeadowAppService meadowAppService)
        {
            this.configuredProject = configuredProject;
            this.meadowAppService = meadowAppService;
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
                await DeployOutputLogger?.ConnectTextWriter(outputPaneWriter);

                return await DeployMeadowAppAsync(cts, outputPaneWriter, filename);
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
            finally
            {
                if (!await meadowConnection.IsRuntimeEnabled())
                {
                    await meadowConnection.RuntimeEnable();
                }
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
            isDeploySupported = await meadowAppService.IsMeadowApp(configuredProject);

            if (cts.IsCancellationRequested
                || !isDeploySupported)
            {
                return;
            }

            await DeployMeadowProjectsAsync(cts, outputPaneWriter);
        }

        async Task DeployAppAsync(string folder, IOutputPaneWriter outputPaneWriter, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

			if (meadowConnection != null)
			{
				meadowConnection.FileWriteProgress -= MeadowConnection_DeploymentProgress;
				meadowConnection.DeviceMessageReceived -= MeadowConnection_DeviceMessageReceived;
			}

			var route = MeadowPackage.SettingsManager.GetSetting(SettingsManager.PublicSettings.Route);

			meadowConnection = await MeadowConnectionManager.GetConnectionForRoute(route);

			meadowConnection.FileWriteProgress += MeadowConnection_DeploymentProgress;
			meadowConnection.DeviceMessageReceived += MeadowConnection_DeviceMessageReceived;

			try
            {
                await meadowConnection.WaitForMeadowAttach();

                await meadowConnection.RuntimeDisable();

				/*  device = await MeadowProvider.GetMeadowSerialDeviceAsync(DeployOutputLogger);
                if (meadowConnection.Device == null)
                {
                    MeadowPackage.DebugOrDeployInProgress = false;
                    throw new Exception("A device has not been selected. Please attach a device, then select it from the Device list.");
                }*/

				var deviceInfo = await meadowConnection.GetDeviceInfo(cancellationToken);
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

                var packageManager = new PackageManager(fileManager);

                DeployOutputLogger.Log("Trimming...");
                await packageManager.TrimApplication(new FileInfo(Path.Combine(folder, "App.dll")), osVersion, includePdbs.HasValue && includePdbs.Value, cancellationToken: cancellationToken);

                DeployOutputLogger.Log("Deploying...");
                await AppManager.DeployApplication(packageManager, meadowConnection, osVersion, folder, includePdbs.HasValue && includePdbs.Value, false, DeployOutputLogger, cancellationToken);

                await meadowConnection.RuntimeEnable();
            }
            finally
            {
				meadowConnection.FileWriteProgress -= MeadowConnection_DeploymentProgress;
            }
        }

        private async void MeadowConnection_DeviceMessageReceived(object sender, (string message, string source) e)
        {
            await DeployOutputLogger?.ReportDeviceMessage(e.source, e.message);
        }

        private async void Firmware_DownloadProgress(object sender, long e)
        {
            await DeployOutputLogger?.ReportDownloadProgress(osVersion, e);
        }

        private async void MeadowConnection_DeploymentProgress(object sender, (string fileName, long completed, long total) e)
        {
            var p = (uint)((e.completed / (double)e.total) * 100d);

            await DeployOutputLogger?.ReportFileProgress(e.fileName, p);

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

        private static void MeadowConnectionDispose()
        {
            if (meadowConnection != null)
            {
                meadowConnection.Dispose();
                meadowConnection = null;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    MeadowConnectionDispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}