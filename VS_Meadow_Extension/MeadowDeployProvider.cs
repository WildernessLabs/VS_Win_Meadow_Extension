using Meadow.CLI;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Newtonsoft.Json.Linq;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
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
        private readonly object deploySupportLock = new object();
        private DateTime deploySupportCacheStampUtc = DateTime.MinValue;
        private bool cachedDeploySupported;

        const string MeadowSDKVersion = "Sdk=\"Meadow.Sdk/1.1.0\"";

        public bool IsDeploySupported
        {
            get
            {
                if (DapDebugPending)
                {
                    return true;
                }

                return EvaluateIsDeploySupported();
            }
        }

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

            if (cancellationToken.IsCancellationRequested || !IsDeploySupported)
            {
                return;
            }

            // Keep the evaluated-property check as a secondary guard for runtime safety.
            if (!await IsProjectAMeadowApp())
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

            // Get serial port from launchSettings.json (single source of truth)
            var serial = await GetMeadowDeviceFromLaunchSettingsAsync(projectPath);
            if (string.IsNullOrEmpty(serial))
            {
                outputLogger?.Log("No Meadow device configured. Please select a device from the Debug Launch Targets dropdown and try again.");
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
            var adapterPath = DapDeploymentHelper.GetAdapterPath();
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

        private bool EvaluateIsDeploySupported()
        {
            var projectFile = configuredProject?.UnconfiguredProject?.FullPath;
            if (string.IsNullOrWhiteSpace(projectFile) || !File.Exists(projectFile))
            {
                return false;
            }

            var projectDirectory = Path.GetDirectoryName(projectFile);
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                return false;
            }

            var cacheStampUtc = GetDeploySupportCacheStampUtc(projectFile, projectDirectory);

            lock (deploySupportLock)
            {
                if (cacheStampUtc == deploySupportCacheStampUtc)
                {
                    return cachedDeploySupported;
                }

                cachedDeploySupported = LooksLikeMeadowAppProject(projectFile, projectDirectory);
                deploySupportCacheStampUtc = cacheStampUtc;
                return cachedDeploySupported;
            }
        }

        private static DateTime GetDeploySupportCacheStampUtc(string projectFile, string projectDirectory)
        {
            var stamp = File.GetLastWriteTimeUtc(projectFile);

            var meadowConfigPath = Path.Combine(projectDirectory, "meadow.config.yaml");
            if (File.Exists(meadowConfigPath))
            {
                var meadowStamp = File.GetLastWriteTimeUtc(meadowConfigPath);
                if (meadowStamp > stamp)
                {
                    stamp = meadowStamp;
                }
            }

            var appConfigPath = Path.Combine(projectDirectory, "app.config.yaml");
            if (File.Exists(appConfigPath))
            {
                var appStamp = File.GetLastWriteTimeUtc(appConfigPath);
                if (appStamp > stamp)
                {
                    stamp = appStamp;
                }
            }

            return stamp;
        }

        private static bool LooksLikeMeadowAppProject(string projectFile, string projectDirectory)
        {
            var hasMeadowConfig = File.Exists(Path.Combine(projectDirectory, "meadow.config.yaml"));
            var hasAppConfig = File.Exists(Path.Combine(projectDirectory, "app.config.yaml"));

            if (!hasMeadowConfig || !hasAppConfig)
            {
                return false;
            }

            try
            {
                var doc = XDocument.Load(projectFile);
                var sdk = doc.Root?.Attribute("Sdk")?.Value;

                var hasMeadowSdk = !string.IsNullOrWhiteSpace(sdk)
                    && sdk.IndexOf("Meadow.Sdk", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!hasMeadowSdk)
                {
                    hasMeadowSdk = doc
                        .Descendants()
                        .Any(x => x.Name.LocalName == "Import"
                            && x.Attribute("Sdk")?.Value?.IndexOf("Meadow.Sdk", StringComparison.OrdinalIgnoreCase) >= 0);
                }

                var assemblyName = doc
                    .Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "AssemblyName")
                    ?.Value
                    ?.Trim();

                var isAppAssembly = string.Equals(assemblyName, "App", StringComparison.OrdinalIgnoreCase);

                return hasMeadowSdk && isAppAssembly;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> IsProjectAMeadowApp()
        {
            try
            {
                var properties = configuredProject.Services.ProjectPropertiesProvider.GetCommonProperties();
                string assemblyName = await properties.GetEvaluatedPropertyValueAsync("AssemblyName");

                return !string.IsNullOrEmpty(assemblyName)
                    && assemblyName.Equals("App", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Reads the currently configured Meadow device from launchSettings.json.
        /// This is the single source of truth for which device to target.
        /// </summary>
        private async Task<string> GetMeadowDeviceFromLaunchSettingsAsync(string projectPath)
        {
            try
            {
                var propertiesPath = Path.Combine(projectPath, "Properties");
                var launchSettingsPath = Path.Combine(propertiesPath, "launchSettings.json");

                if (!File.Exists(launchSettingsPath))
                {
                    return null;
                }

                var launchSettingsJson = File.ReadAllText(launchSettingsPath);
                var launchSettings = JObject.Parse(launchSettingsJson);
                var profiles = launchSettings["profiles"] as JObject;

                if (profiles == null || profiles.Count == 0)
                {
                    return null;
                }

                // Find the first Meadow profile and extract the device port
                foreach (var profile in profiles.Properties())
                {
                    var profileObj = profile.Value as JObject;
                    if (profileObj != null)
                    {
                        var commandName = profileObj["commandName"]?.ToString();
                        if (commandName == "Meadow")
                        {
                            var device = profileObj["meadowDevice"]?.ToString();
                            if (!string.IsNullOrEmpty(device))
                            {
                                outputLogger?.Log($"Using device from launch profile: {device}");
                                return device;
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                outputLogger?.Log($"WARNING: Failed to read device from launchSettings.json: {ex.Message}");
                return null;
            }
        }
    }
}