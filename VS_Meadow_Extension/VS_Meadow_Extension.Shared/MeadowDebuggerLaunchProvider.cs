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
        private static readonly Guid DapEngineGuid = new Guid("17F23ACB-E784-4F24-B961-A43A06C5E5D8");
        private const int DebugPort = 55555;

        private readonly ConfiguredProject configuredProject;
        private readonly SettingsManager settingsManager = new SettingsManager();
        private readonly MeadowLaunchSettingsProvider launchSettingsProvider;

        [ImportingConstructor]
        public MeadowDebuggerLaunchProvider(
            ConfiguredProject configuredProject,
            MeadowLaunchSettingsProvider launchSettingsProvider)
        {
            this.configuredProject = configuredProject;
            this.launchSettingsProvider = launchSettingsProvider;

            _ = launchSettingsProvider.UpdateLaunchSettingsAsync();
        }

        public bool SupportsProfile(ILaunchProfile profile)
        {
            if (profile?.CommandName != "Meadow")
            {
                return false;
            }

            MeadowDeployProvider.DapDebugPending = true;
            return true;
        }

        public async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(
            DebugLaunchOptions launchOptions, ILaunchProfile profile)
        {
            if (launchOptions.HasFlag(DebugLaunchOptions.NoDebug))
            {
                MeadowDeployProvider.DapDebugPending = false;
                return Array.Empty<IDebugLaunchSettings>();
            }

            if (!await IsProjectAMeadowApp())
            {
                MeadowDeployProvider.DapDebugPending = false;
                return Array.Empty<IDebugLaunchSettings>();
            }

            string serial = null;
            if (profile?.OtherSettings != null &&
                profile.OtherSettings.TryGetValue("meadowDevice", out var deviceObj))
            {
                serial = deviceObj as string;
            }

            if (string.IsNullOrEmpty(serial))
            {
                OutputLogger.Instance?.Log("No Meadow device selected. Please select a device from the Debug Launch Targets dropdown.");
                MeadowDeployProvider.DapDebugPending = false;
                return Array.Empty<IDebugLaunchSettings>();
            }

            Globals.DebugOrDeployInProgress = true;

            var projectFullPath = configuredProject.UnconfiguredProject.FullPath;
            var projectPath = Path.GetDirectoryName(projectFullPath);

            var configuration = "Debug";
            if (configuredProject?.ProjectConfiguration?.Dimensions != null &&
                configuredProject.ProjectConfiguration.Dimensions.TryGetValue("Configuration", out var configVal))
            {
                configuration = configVal;
            }

            var outputPath = await GetOutputPathAsync(projectFullPath);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                OutputLogger.Instance?.Log("ERROR: Failed to determine output path from project properties.");
                Globals.DebugOrDeployInProgress = false;
                MeadowDeployProvider.DapDebugPending = false;
                return Array.Empty<IDebugLaunchSettings>();
            }

            var propsFile = GenerateMSBuildPropertyFile(outputPath, "App");
            if (!File.Exists(propsFile))
            {
                OutputLogger.Instance?.Log($"ERROR: Failed to create MSBuild property file at: {propsFile}");
                Globals.DebugOrDeployInProgress = false;
                MeadowDeployProvider.DapDebugPending = false;
                return Array.Empty<IDebugLaunchSettings>();
            }

            var adapterPath = GetAdapterPath();
            if (!File.Exists(adapterPath))
            {
                OutputLogger.Instance?.Log($"DAP adapter not found at: {adapterPath}");
                Globals.DebugOrDeployInProgress = false;
                MeadowDeployProvider.DapDebugPending = false;
                return Array.Empty<IDebugLaunchSettings>();
            }

            var launchConfig = new JObject
            {
                ["type"] = "meadow",
                ["request"] = "launch",
                ["projectPath"] = projectPath,
                ["projectConfiguration"] = configuration,
                ["serial"] = serial,
                ["msbuildPropertyFile"] = propsFile,
                ["debugPort"] = DebugPort
            };

            var settings = new DebugLaunchSettings(launchOptions)
            {
                LaunchDebugEngineGuid = DapEngineGuid,
                LaunchOperation = DebugLaunchOperation.CreateProcess,
                Executable = adapterPath,
                Options = launchConfig.ToString()
            };

            MeadowDeployProvider.DapDebugPending = true;
            return new[] { settings };
        }

        public Task OnBeforeLaunchAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile)
        {
            return Task.CompletedTask;
        }

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