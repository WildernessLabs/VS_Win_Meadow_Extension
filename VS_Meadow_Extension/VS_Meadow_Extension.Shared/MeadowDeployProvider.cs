using Meadow.CLI;
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
        /// <summary>
        /// When true, the DAP adapter will handle deployment during debug sessions.
        /// Set by MeadowDebuggerLaunchProvider.SupportsProfile() before DeployAsync is called.
        /// </summary>
        public static bool DapDebugPending { get; set; } = false;

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

        private readonly SettingsManager settingsManager = new SettingsManager();

        [ImportingConstructor]
        public MeadowDeployProvider(ConfiguredProject configuredProject)
        {
            this.configuredProject = configuredProject;
        }

        public async Task DeployAsync(CancellationToken cancellationToken, TextWriter textWriter)
        {
            // When a DAP debug launch is pending, the adapter handles deployment
            // in its own process (it needs exclusive serial port access).
            if (DapDebugPending)
            {
                DapDebugPending = false;
                return;
            }

            if (cancellationToken.IsCancellationRequested || !await IsProjectAMeadowApp())
            {
                return;
            }

            Globals.DebugOrDeployInProgress = true;

            await outputLogger?.ConnectTextWriter(textWriter);
            await outputLogger.ShowDebugOutputPane();

            outputLogger.Log("Preparing to deploy Meadow application...");

            var filename = configuredProject.UnconfiguredProject.FullPath;
            var projFileContent = File.ReadAllText(filename);

            if (projFileContent.Contains(MeadowSDKVersion) == false)
            {
                Globals.DebugOrDeployInProgress = false;
                outputLogger?.Log("Deploy failed - not a Meadow project");
                return;
            }

            var projectPath = Path.GetDirectoryName(filename);
            var outputPath = await GetOutputPathAsync(filename);

            outputLogger.Log($"Deploying from {outputPath}...");

            if (string.IsNullOrEmpty(outputPath))
            {
                Globals.DebugOrDeployInProgress = false;
                outputLogger?.Log("Deploy failed - could not locate Meadow app");
                return;
            }

            // Get configuration
            var configuration = "Debug";
            if (configuredProject?.ProjectConfiguration?.Dimensions != null
                && configuredProject.ProjectConfiguration.Dimensions.TryGetValue("Configuration", out var configVal))
            {
                configuration = configVal;
            }

            // Get serial port
            var serial = settingsManager.GetSetting(SettingsManager.PublicSettings.Route);
            if (string.IsNullOrEmpty(serial))
            {
                outputLogger?.Log("No Meadow device selected. Please select a device from the toolbar.");
                Globals.DebugOrDeployInProgress = false;
                return;
            }

            // Generate MSBuild property file
            var propsFile = GenerateMSBuildPropertyFile(outputPath, "App");
            if (!File.Exists(propsFile))
            {
                outputLogger?.Log($"ERROR: Failed to create MSBuild property file at: {propsFile}");
                Globals.DebugOrDeployInProgress = false;
                return;
            }

            // Get DAP adapter path for validation
            var adapterPath = GetAdapterPath();
            if (!File.Exists(adapterPath))
            {
                outputLogger?.Log($"DAP adapter not found at: {adapterPath}");
                Globals.DebugOrDeployInProgress = false;
                return;
            }

            try
            {
                // Launch DAP adapter with debugPort: 0 for deploy-only
                // Note: This spawns the adapter directly, not via VS DAP Host infrastructure,
                // so progress bars won't appear automatically. For full DAP Host support
                // (with automatic progress bars), use F5 or Ctrl+F5 instead.
                var dapHelper = new DapDeploymentHelper(outputLogger, adapterPath);
                
                bool success = await dapHelper.DeployAsync(
                    projectPath,
                    configuration,
                    serial,
                    propsFile,
                    cancellationToken);

                if (success)
                {
                    outputLogger.Log("Deployment completed successfully.");
                }
                else
                {
                    outputLogger.Log("Deployment failed.");
                }

                await outputLogger.ShowDebugOutputPane();
            }
            finally
            {
                Globals.DebugOrDeployInProgress = false;
                
                // Clean up temp MSBuild props file
                try
                {
                    if (File.Exists(propsFile))
                    {
                        File.Delete(propsFile);
                    }
                }
                catch { /* Ignore cleanup errors */ }
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

        private string GenerateMSBuildPropertyFile(string outputPath, string assemblyName)
        {
            var tempFile = Path.Combine(
                Path.GetTempPath(),
                $"meadow_deploy_{Guid.NewGuid():N}.props");

            File.WriteAllText(tempFile,
                $"OutputPath={outputPath}{Environment.NewLine}AssemblyName={assemblyName}");

            return tempFile;
        }

        private string GetAdapterPath()
        {
            // Get path to DAP adapter bundled in the VSIX (same location as debug sessions use)
            var assemblyPath = Path.GetDirectoryName(GetType().Assembly.Location);
            var adapterPath = Path.Combine(assemblyPath, "DapAdapter", "meadow-debugging.exe");
            return adapterPath;
        }

        public async void Commit()
        {
			await outputLogger?.ShowDebugOutputPane();

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