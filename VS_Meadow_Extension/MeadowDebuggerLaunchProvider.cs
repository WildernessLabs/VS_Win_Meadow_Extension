using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Meadow
{
    [Export(typeof(IDebugProfileLaunchTargetsProvider))]
    [AppliesTo(Globals.MeadowCapability)]
    [Order(999)]
    public class MeadowDebuggerLaunchProvider : IDebugProfileLaunchTargetsProvider, IDisposable
    {
        private static readonly Guid DapEngineGuid = new Guid("17F23ACB-E784-4F24-B961-A43A06C5E5D8");
        private const int DebugPort = 55555;

        private readonly ConfiguredProject configuredProject;
        private readonly MeadowLaunchSettingsProvider launchSettingsProvider;
        private FileSystemWatcher _launchSettingsWatcher;
        private Timer _devicePollTimer;
        private DateTime _lastRefreshTime = DateTime.MinValue;
        private DateTime _nextRefreshAllowedUtc = DateTime.MinValue;
        private DateTime _ignoreWatcherEventsUntilUtc = DateTime.MinValue;
        private int _consecutiveRefreshFailures;
        private int _isRefreshing;
        private volatile bool _suppressWatcherEvents;
        private string _lastDeviceSignature = string.Empty;

        private static readonly TimeSpan RefreshDebounce = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan SelfWriteWatcherQuietPeriod = TimeSpan.FromSeconds(2);

        [ImportingConstructor]
        public MeadowDebuggerLaunchProvider(
            ConfiguredProject configuredProject,
            MeadowLaunchSettingsProvider launchSettingsProvider)
        {
            this.configuredProject = configuredProject;
            this.launchSettingsProvider = launchSettingsProvider;

            _ = launchSettingsProvider.UpdateLaunchSettingsAsync();

            // Initialize file system watcher for launchSettings.json changes (real-time device detection)
            InitializeFileSystemWatcher();

            // Poll connected Meadow devices and refresh launch settings only when device list changes.
            _devicePollTimer = new Timer(
                _ => _ = PollDevicesAndRefreshIfChangedAsync(),
                null,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(2));
        }

        private void InitializeFileSystemWatcher()
        {
            try
            {
                var projectPath = Path.GetDirectoryName(configuredProject.UnconfiguredProject.FullPath);
                var propertiesPath = Path.Combine(projectPath, "Properties");

                if (!Directory.Exists(propertiesPath))
                {
                    return; // Will be created on first UpdateLaunchSettingsAsync
                }

                _launchSettingsWatcher = new FileSystemWatcher(propertiesPath)
                {
                    Filter = "launchSettings.json",
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = false // Enabled after first update
                };

                _launchSettingsWatcher.Changed += OnLaunchSettingsChanged;
                _launchSettingsWatcher.Created += OnLaunchSettingsChanged;
                _launchSettingsWatcher.Deleted += OnLaunchSettingsChanged;
                _launchSettingsWatcher.Renamed += OnLaunchSettingsChanged;
                System.Diagnostics.Debug.WriteLine($"[MeadowDebuggerLaunchProvider] FileSystemWatcher initialized for {propertiesPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MeadowDebuggerLaunchProvider] Failed to initialize FileSystemWatcher: {ex.Message}");
            }
        }

        private void OnLaunchSettingsChanged(object sender, FileSystemEventArgs e)
        {
            if (_suppressWatcherEvents)
            {
                return;
            }

            if (DateTime.UtcNow < _ignoreWatcherEventsUntilUtc)
            {
                return;
            }

            if (Globals.DebugOrDeployInProgress || MeadowDeployProvider.DapDebugPending)
            {
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[MeadowDebuggerLaunchProvider] launchSettings.json changed: {e.ChangeType}");
            _ = RefreshDevicesIfNeededAsync();
        }

        private async Task RefreshDevicesIfNeededAsync()
        {
            // Never rewrite launch settings while a deploy/debug operation is active.
            if (Globals.DebugOrDeployInProgress || MeadowDeployProvider.DapDebugPending)
            {
                return;
            }

            if (_suppressWatcherEvents)
            {
                return;
            }

            var utcNow = DateTime.UtcNow;
            if (utcNow < _nextRefreshAllowedUtc)
            {
                return;
            }

            if (Interlocked.Exchange(ref _isRefreshing, 1) == 1)
            {
                return;
            }

            // Debounce: only refresh if at least 1 second has passed since last refresh
            try
            {
                if (DateTime.Now - _lastRefreshTime < TimeSpan.FromSeconds(1))
                {
                    return;
                }

                _nextRefreshAllowedUtc = DateTime.UtcNow.Add(RefreshDebounce);
                _lastRefreshTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"[MeadowDebuggerLaunchProvider] Refreshing device list...");

                // Regenerate launchSettings.json with updated device list
                _suppressWatcherEvents = true;
                _ignoreWatcherEventsUntilUtc = DateTime.UtcNow.Add(SelfWriteWatcherQuietPeriod);
                await launchSettingsProvider.UpdateLaunchSettingsAsync();
                _consecutiveRefreshFailures = 0;
                _nextRefreshAllowedUtc = DateTime.UtcNow.Add(RefreshDebounce);

                System.Diagnostics.Debug.WriteLine($"[MeadowDebuggerLaunchProvider] Device list refreshed");
            }
            catch (Exception ex)
            {
                _consecutiveRefreshFailures = Math.Min(_consecutiveRefreshFailures + 1, 5);
                var retryDelaySeconds = Math.Min(2 << (_consecutiveRefreshFailures - 1), 30);
                _nextRefreshAllowedUtc = DateTime.UtcNow.AddSeconds(retryDelaySeconds);
                System.Diagnostics.Debug.WriteLine($"[MeadowDebuggerLaunchProvider] Error during device refresh: {ex.Message}");
            }
            finally
            {
                _suppressWatcherEvents = false;
                Interlocked.Exchange(ref _isRefreshing, 0);
            }
        }

        private async Task PollDevicesAndRefreshIfChangedAsync()
        {
            // Avoid touching device discovery during active deploy/debug to prevent contention.
            if (Globals.DebugOrDeployInProgress || MeadowDeployProvider.DapDebugPending)
            {
                return;
            }

            if (_suppressWatcherEvents)
            {
                return;
            }

            try
            {
                var devices = await MeadowDeviceDiscovery.GetDetailedDeviceInfoAsync(forceRefresh: true);

                var signature = string.Join("|", (devices ?? new System.Collections.Generic.List<MeadowDeviceInfo>())
                    .Where(d => d != null && !string.IsNullOrWhiteSpace(d.Port))
                    .Select(d => d.Port)
                    .OrderBy(p => p));

                if (string.Equals(signature, _lastDeviceSignature, StringComparison.Ordinal))
                {
                    return;
                }

                _lastDeviceSignature = signature;
                await RefreshDevicesIfNeededAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MeadowDebuggerLaunchProvider] PollDevicesAndRefreshIfChangedAsync error: {ex.Message}");
            }
        }

        public void EnableFileSystemWatcher()
        {
            if (_launchSettingsWatcher == null)
            {
                InitializeFileSystemWatcher();
            }

            if (_launchSettingsWatcher != null)
            {
                _launchSettingsWatcher.EnableRaisingEvents = true;
                System.Diagnostics.Debug.WriteLine($"[MeadowDebuggerLaunchProvider] FileSystemWatcher enabled");
            }
        }

        public void Dispose()
        {
            _devicePollTimer?.Dispose();
            _launchSettingsWatcher?.Dispose();
            System.Diagnostics.Debug.WriteLine($"[MeadowDebuggerLaunchProvider] Disposed");
        }

        public bool SupportsProfile(ILaunchProfile profile)
        {
            // Enable file watcher on first interaction with debug dropdown
            if (_launchSettingsWatcher?.EnableRaisingEvents == false)
            {
                EnableFileSystemWatcher();
            }

            // Trigger an on-demand poll to reduce stale dropdown state.
            _ = PollDevicesAndRefreshIfChangedAsync();

            if (profile?.CommandName != "Meadow")
            {
                return false;
            }
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

            var adapterPath = DapDeploymentHelper.GetAdapterPath();
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

            return new[] { settings };
        }

        public Task OnBeforeLaunchAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile)
        {
            if (profile?.CommandName == "Meadow" && !launchOptions.HasFlag(DebugLaunchOptions.NoDebug))
            {
                // Only mark pending at actual launch time, not during profile discovery.
                MeadowDeployProvider.DapDebugPending = true;
            }

            return Task.CompletedTask;
        }

        public async Task OnAfterLaunchAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile)
        {
            MeadowDeployProvider.DapDebugPending = false;
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