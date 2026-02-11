using Meadow.CLI;
using Meadow.CLI.Commands.DeviceManagement;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;

namespace Meadow
{
    [Export(typeof(IDebugProfileLaunchTargetsProvider))]
    [AppliesTo(Globals.MeadowCapability)]
    [Order(999)]
    public class MeadowDebuggerLaunchProvider : IDebugProfileLaunchTargetsProvider
    {
        /// <summary>
        /// Custom Meadow Debug Engine GUID registered in MeadowDebugAdapter.pkgdef.
        /// Maps to VS2022's built-in Debug Adapter Host (CLSID DAB324E9...) which
        /// launches meadow-debugging.exe and communicates via DAP over stdin/stdout.
        /// </summary>
        private static readonly Guid DapEngineGuid =
            new Guid("17F23ACB-E784-4F24-B961-A43A06C5E5D8");

        private const int DebugPort = 55555;

        static readonly OutputLogger outputLogger = OutputLogger.Instance;

        private readonly ConfiguredProject configuredProject;
        private readonly SettingsManager settingsManager = new SettingsManager();

        [ImportingConstructor]
        public MeadowDebuggerLaunchProvider(ConfiguredProject configuredProject)
        {
            this.configuredProject = configuredProject;
        }

        public bool SupportsProfile(ILaunchProfile profile)
        {
            // Signal to deploy provider that a DAP debug launch is pending.
            // This must be set here because SupportsProfile is called BEFORE DeployAsync.
            MeadowDeployProvider.DapDebugPending = true;
            return true;
        }

        public async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(
            DebugLaunchOptions launchOptions, ILaunchProfile profile)
        {
            if (launchOptions.HasFlag(DebugLaunchOptions.NoDebug))
            {
                // "Start Without Debugging" — let MeadowDeployProvider handle it
                // (VS2022 doesn't properly use DAP Host for NoDebug scenarios)
                MeadowDeployProvider.DapDebugPending = false;
                return Array.Empty<IDebugLaunchSettings>();
            }

            if (!await IsProjectAMeadowApp())
            {
                MeadowDeployProvider.DapDebugPending = false;
                return Array.Empty<IDebugLaunchSettings>();
            }

            Globals.DebugOrDeployInProgress = true;

            // Get device serial port from settings
            var serial = settingsManager.GetSetting(SettingsManager.PublicSettings.Route);
            if (string.IsNullOrEmpty(serial))
            {
                outputLogger?.Log("No Meadow device selected. Please select a device from the toolbar.");
                Globals.DebugOrDeployInProgress = false;
                MeadowDeployProvider.DapDebugPending = false;
                return Array.Empty<IDebugLaunchSettings>();
            }

            // Get project info
            var projectFullPath = configuredProject.UnconfiguredProject.FullPath;
            var projectPath = Path.GetDirectoryName(projectFullPath);

            var configuration = "Debug";
            if (configuredProject?.ProjectConfiguration?.Dimensions != null
                && configuredProject.ProjectConfiguration.Dimensions.TryGetValue("Configuration", out var configVal))
            {
                configuration = configVal;
            }

            // Get output path from project properties
            var outputPath = await GetOutputPathAsync(projectFullPath);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputLogger?.Log("ERROR: Failed to determine output path from project properties.");
                Globals.DebugOrDeployInProgress = false;
                MeadowDeployProvider.DapDebugPending = false;
                return Array.Empty<IDebugLaunchSettings>();
            }

            if (!Directory.Exists(outputPath))
            {
                outputLogger?.Log($"WARNING: Output path does not exist: {outputPath}. Debug may fail.");
            }

            // Generate the MSBuild property file that the adapter expects
            var propsFile = GenerateMSBuildPropertyFile(outputPath, "App");
            if (!File.Exists(propsFile))
            {
                outputLogger?.Log($"ERROR: Failed to create MSBuild property file at: {propsFile}");
                Globals.DebugOrDeployInProgress = false;
                MeadowDeployProvider.DapDebugPending = false;
                return Array.Empty<IDebugLaunchSettings>();
            }

            // Locate the DAP adapter executable bundled in the VSIX
            var adapterPath = GetAdapterPath();

            if (!File.Exists(adapterPath))
            {
                outputLogger?.Log($"DAP adapter not found at: {adapterPath}");
                Globals.DebugOrDeployInProgress = false;
                MeadowDeployProvider.DapDebugPending = false;
                return Array.Empty<IDebugLaunchSettings>();
            }

            // Build launch configuration JSON for the DAP adapter.
            // The adapter path is registered in MeadowDebugAdapter.pkgdef,
            // so VS2022's DAP Host knows where to find vscode-meadow.exe.
            // The properties below become the "arguments" in the DAP "launch" request.
            var launchConfig = new JObject
            {
                ["type"] = "meadow",
                ["request"] = "launch",
                ["projectPath"] = projectPath,
                ["projectConfiguration"] = configuration,
                ["serial"] = serial,
                ["msbuildPropertyFile"] = propsFile,
                ["debugPort"] = DebugPort  // Enable Mono soft debugger
            };

            var settings = new DebugLaunchSettings(launchOptions)
            {
                LaunchDebugEngineGuid = DapEngineGuid,
                LaunchOperation = DebugLaunchOperation.CreateProcess,
                // Executable is required by DebugLaunchSettings but the DAP Host
                // uses the adapter registered in the pkgdef, not this value.
                Executable = adapterPath,
                Options = launchConfig.ToString()
            };

            return new IDebugLaunchSettings[] { settings };
        }

        public Task OnBeforeLaunchAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile)
            => Task.CompletedTask;

        public async Task OnAfterLaunchAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile)
        {
            Globals.DebugOrDeployInProgress = false;
            await OutputLogger.Instance.ShowDebugOutputPane();
        }

        private string GenerateMSBuildPropertyFile(string outputPath, string assemblyName)
        {
            var tempFile = Path.Combine(
                Path.GetTempPath(),
                $"meadow_debug_{Guid.NewGuid():N}.props");

            File.WriteAllText(tempFile,
                $"OutputPath={outputPath}{Environment.NewLine}AssemblyName={assemblyName}");

            return tempFile;
        }

        private string GetAdapterPath()
        {
            // The adapter is bundled inside the VSIX extension directory under DapAdapter/
            var extensionDir = Path.GetDirectoryName(
                typeof(MeadowDebuggerLaunchProvider).Assembly.Location);

            return Path.Combine(extensionDir, "DapAdapter", "vscode-meadow.exe");
        }

        private async Task<string> GetOutputPathAsync(string filename)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var properties = configuredProject.Services.ProjectPropertiesProvider.GetCommonProperties();
            var projectDir = await properties.GetEvaluatedPropertyValueAsync("ProjectDir");
            var relativeOutputPath = await properties.GetEvaluatedPropertyValueAsync("OutputPath");

            return Path.Combine(projectDir, relativeOutputPath);
        }

        private async Task<bool> IsProjectAMeadowApp()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var properties = configuredProject.Services.ProjectPropertiesProvider.GetCommonProperties();
            string assemblyName = await properties.GetEvaluatedPropertyValueAsync("AssemblyName");

            return !string.IsNullOrEmpty(assemblyName)
                && assemblyName.Equals("App", StringComparison.OrdinalIgnoreCase);
        }
    }
}
