﻿using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Meadow.CLI.Core;
using Meadow.CLI.Core.Devices;
using Meadow.Utility;
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
        internal static MeadowDeviceHelper Meadow;
        public static OutputLogger DeployOutputLogger = new OutputLogger();

        /// <summary>
        /// Provides access to the project's properties.
        /// </summary>
        [Import]
        private ProjectProperties Properties { get; set; }

        private ConfiguredProject configuredProject;

        const string MeadowSDKVersion = "Sdk=\"Meadow.Sdk/1.1.0\"";
        private bool isDeploySupported = true;

        [ImportingConstructor]
        public DeployProvider(ConfiguredProject configuredProject)
        {
            this.configuredProject = configuredProject;
            IsMeadowApp();
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
                var running = await Meadow.GetMonoRunState(cts);
                if (!running)
                {
					await Meadow?.MonoEnable(true, cts);
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
            if (cts.IsCancellationRequested
                || !await IsMeadowApp())
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
                isDeploySupported = true;
            }
            else
            {
                isDeploySupported = false;
            }

            return isDeploySupported;
        }
    }
}