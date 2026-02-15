using Microsoft.VisualStudio.ProjectSystem;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Meadow
{
    /// <summary>
    /// Dynamically generates launchSettings.json with one profile per connected Meadow device.
    /// This populates the Debug Launch Targets dropdown in Visual Studio.
    /// </summary>
    [Export]
    [AppliesTo(Globals.MeadowCapability)]
    public class MeadowLaunchSettingsProvider
    {
        private readonly ConfiguredProject _configuredProject;
        private const string LaunchSettingsFileName = "launchSettings.json";

        [ImportingConstructor]
        public MeadowLaunchSettingsProvider(ConfiguredProject configuredProject)
        {
            _configuredProject = configuredProject;
        }

        /// <summary>
        /// Updates launchSettings.json with one profile per connected Meadow device.
        /// </summary>
        public async Task UpdateLaunchSettingsAsync()
        {
            try
            {
                var projectPath = Path.GetDirectoryName(_configuredProject.UnconfiguredProject.FullPath);
                var propertiesPath = Path.Combine(projectPath, "Properties");
                var launchSettingsPath = Path.Combine(propertiesPath, LaunchSettingsFileName);

                // Get connected devices
                var devices = await MeadowDeviceDiscovery.GetDetailedDeviceInfoAsync(forceRefresh: true);

                // Create profiles object
                var profiles = new JObject();

                if (devices != null && devices.Count > 0)
                {
                    foreach (var device in devices)
                    {
                        // Use device display name or default to "Meadow Device"
                        var displayName = !string.IsNullOrEmpty(device.DisplayName) 
                            ? device.DisplayName 
                            : "Meadow Device";
                        var profileName = $"{displayName}";
                        
                        profiles[profileName] = new JObject
                        {
                            ["commandName"] = "Meadow",
                            ["meadowDevice"] = device.Port,
                            ["meadowDeviceName"] = displayName
                        };
                    }
                }
                else
                {
                    // No devices - create a placeholder profile
                    profiles["No Meadow Devices"] = new JObject
                    {
                        ["commandName"] = "Meadow",
                        ["meadowDevice"] = ""
                    };
                }

                // Create launchSettings structure
                var launchSettings = new JObject
                {
                    ["profiles"] = profiles
                };

                // Ensure Properties directory exists
                if (!Directory.Exists(propertiesPath))
                {
                    Directory.CreateDirectory(propertiesPath);
                }

                // Write launchSettings.json
                File.WriteAllText(launchSettingsPath, launchSettings.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MeadowLaunchSettings] ERROR: {ex.Message}");
           }
        }
    }
}
