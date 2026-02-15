using EnvDTE;
using EnvDTE80;
using Meadow.CLI;
using Meadow.CLI.Commands.DeviceManagement;
using Microsoft;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace Meadow
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#1110", "#1112", Globals.AssemblyVersion, IconResourceID = 1400)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(GuidList.guidMeadowPackageString)]
    public sealed class MeadowPackage : AsyncPackage
    {
        private const string NoDevicesFound = "No Devices Found";
        private static SettingsManager SettingsManager { get; set; } = new SettingsManager();

        private DTE2 _dte;
        private DebuggerEvents _debuggerEvents;
        private volatile bool _isInitialized = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="MeadowPackage"/> class.
        /// </summary>
        public MeadowPackage() { }

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that relies on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            try
            {
                await base.InitializeAsync(cancellationToken, progress);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Meadow Package] ERROR in base.InitializeAsync: {ex.Message}");
                throw;
            }

            try
            {
                await InstallDependencies();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Meadow Package] ERROR in InstallDependencies: {ex.Message}");
                // Don't throw - allow extension to continue loading
            }

            try
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                // Initialize DTE2 and subscribe to debugger events
                _dte = await GetServiceAsync(typeof(DTE)) as DTE2;
                Assumes.Present(_dte);
                _debuggerEvents = _dte.Events.DebuggerEvents;
                _debuggerEvents.OnEnterDesignMode += OnEnterDesignMode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Meadow Package] ERROR in DTE initialization: {ex.Message}\\n{ex.StackTrace}");
                throw;
            }
            
            _isInitialized = true;
        }

        /// <summary>
        /// Event handler called when the debugger enters design mode (i.e., when the debugging session stops).
        /// </summary>
        /// <param name="reason">The reason the debugger entered design mode.</param>
        private void OnEnterDesignMode(dbgEventReason reason)
        {
            // Add your custom logic here
        }

        /// <summary>
        /// Installs necessary dependencies.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        private async Task InstallDependencies()
        {
            if (NetworkInterface.GetIsNetworkAvailable())
            {
                string packageName = "WildernessLabs.Meadow.Template";
                if (!await InstallPackage(packageName))
                {
                    // Handle installation failure
                }
            }
        }

        /// <summary>
        /// Installs the specified package.
        /// </summary>
        /// <param name="packageName">The name of the package to install.</param>
        /// <returns><c>true</c> if the package is installed successfully; otherwise, <c>false</c>.</returns>
        private async Task<bool> InstallPackage(string packageName)
        {
            return await StartDotNetProcess("new install", packageName);
        }

        /// <summary>
        /// Checks if the specified template is installed.
        /// </summary>
        /// <param name="templateName">The name of the template to check.</param>
        /// <returns><c>true</c> if the template is installed; otherwise, <c>false</c>.</returns>
        private async Task<bool> IsTemplateInstalled(string templateName)
        {
            return await StartDotNetProcess("new list", templateName);
        }

        /// <summary>
        /// Starts a .NET process with the specified command and parameters.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        /// <param name="parameters">The parameters for the command.</param>
        /// <returns><c>true</c> if the process completes successfully; otherwise, <c>false</c>.</returns>
        private async Task<bool> StartDotNetProcess(string command, string parameters)
        {
            return await Task.Run(async () =>
            {
                System.Diagnostics.Process process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "dotnet";
                process.StartInfo.Arguments = $"{command} {parameters}";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit();

                return output.Contains(parameters);
            });
        }
    }

    /// <summary>
    /// Contains GUID constants for the Meadow package.
    /// </summary>
    static class GuidList
    {
        /// <summary>
        /// MeadowPackage GUID string.
        /// </summary>
        public const string guidMeadowPackageString = "9e640b9d-2a9e-4da3-ba5e-351adc854fd2";
        public const string guidMeadowPackageCmdSetString = "0af06414-3c09-44ff-88a1-c4e1a35b0bdf";

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        public static readonly Guid guidMeadowPackage = new Guid(guidMeadowPackageString);
        public static readonly Guid guidMeadowPackageCmdSet = new Guid(guidMeadowPackageCmdSetString);
    }
}