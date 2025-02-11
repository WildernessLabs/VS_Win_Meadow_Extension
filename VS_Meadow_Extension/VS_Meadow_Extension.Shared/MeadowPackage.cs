using EnvDTE;
using EnvDTE80;
using Meadow.CLI;
using Meadow.CLI.Commands.DeviceManagement;
using Microsoft;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
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
            await base.InitializeAsync(cancellationToken, progress);

            await InstallDependencies();

            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService mcs)
            {
                CommandID menuMeadowDeviceListComboCommandID = new CommandID(GuidList.guidMeadowPackageCmdSet, (int)PkgCmdIDList.cmdidMeadowDeviceListCombo);
                OleMenuCommand menuMeadowDeviceListComboCommand = new OleMenuCommand(new EventHandler(OnMeadowDeviceListCombo), menuMeadowDeviceListComboCommandID);
                mcs.AddCommand(menuMeadowDeviceListComboCommand);

                CommandID menuMeadowDeviceListComboGetListCommandID = new CommandID(GuidList.guidMeadowPackageCmdSet, (int)PkgCmdIDList.cmdidMeadowDeviceListComboGetList);
                MenuCommand menuMeadowDeviceListComboGetListCommand = new OleMenuCommand(new EventHandler(OnMeadowDeviceListComboGetList), menuMeadowDeviceListComboGetListCommandID);
                mcs.AddCommand(menuMeadowDeviceListComboGetListCommand);
            }

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Initialize DTE2 and subscribe to debugger events
            _dte = await GetServiceAsync(typeof(DTE)) as DTE2;
            Assumes.Present(_dte);
            _debuggerEvents = _dte.Events.DebuggerEvents;
            _debuggerEvents.OnEnterDesignMode += OnEnterDesignMode;
        }

        /// <summary>
        /// Event handler called when the debugger enters design mode (i.e., when the debugging session stops).
        /// </summary>
        /// <param name="reason">The reason the debugger entered design mode.</param>
        private void OnEnterDesignMode(dbgEventReason reason)
        {
            System.Diagnostics.Debug.WriteLine("Debugging session stopped.");
            // Add your custom logic here
        }

        /// <summary>
        /// Event handler for the Meadow device list combo box.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="EventArgs"/> that contains the event data.</param>
        private async void OnMeadowDeviceListCombo(object sender, EventArgs e)
        {
            if (!Globals.DebugOrDeployInProgress)
            {
                if (e is OleMenuCmdEventArgs eventArgs)
                {
                    var portList = await MeadowConnectionManager.GetSerialPorts();

                    IntPtr vOut = eventArgs.OutValue;

                    if (vOut != IntPtr.Zero)
                    {
                        if (portList.Count > 0)
                        {
                            string deviceTarget = string.Empty;

                            var route = SettingsManager.GetSetting(SettingsManager.PublicSettings.Route);
                            bool IsSavedValueInPortList = IsValueInPortList(portList, route);
                            if (IsSavedValueInPortList)
                            {
                                deviceTarget = route;
                            }

                            Marshal.GetNativeVariantForObject(deviceTarget, vOut);
                        }
                        else
                        {
                            Marshal.GetNativeVariantForObject(NoDevicesFound, vOut);
                        }
                    }
                    else if (eventArgs.InValue is string newChoice)
                    {
                        bool valueInPortList = IsValueInPortList(portList, newChoice);

                        if (valueInPortList)
                        {
                            SaveDeviceChoiceToSettings(newChoice);
                        }
                        else
                        {
                            if (!newChoice.Equals(NoDevicesFound))
                            {
                                throw (new ArgumentException("Invalid Device Selected"));
                            }
                        }
                    }
                }
                else
                {
                    throw new ArgumentException("EventArgs Required");
                }
            }
        }

        /// <summary>
        /// Event handler to get the list of Meadow devices for the combo box.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="EventArgs"/> that contains the event data.</param>
        private async void OnMeadowDeviceListComboGetList(object sender, EventArgs e)
        {
            if (!Globals.DebugOrDeployInProgress)
            {
                if (e is OleMenuCmdEventArgs eventArgs)
                {
                    object inParam = eventArgs.InValue;
                    IntPtr vOut = eventArgs.OutValue;

                    if (inParam != null)
                    {
                        throw new ArgumentException("InParam Invalid");
                    }
                    else if (vOut != IntPtr.Zero)
                    {
                        var portList = await MeadowConnectionManager.GetSerialPorts();
                        if (portList.Count > 0)
                        {
                            Marshal.GetNativeVariantForObject(portList, vOut);
                        }
                        else
                        {
                            Marshal.GetNativeVariantForObject(new string[] { NoDevicesFound }, vOut);
                        }
                    }
                    else
                    {
                        throw (new ArgumentException("OutParam Required"));
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a given value is in the port list.
        /// </summary>
        /// <param name="portList">The list of ports.</param>
        /// <param name="newChoice">The new choice to check.</param>
        /// <returns><c>true</c> if the value is in the port list; otherwise, <c>false</c>.</returns>
        private static bool IsValueInPortList(IList<string> portList, string newChoice)
        {
            bool validInput = false;
            for (int i = 0; i < portList.Count; i++)
            {
                if (string.Compare(portList[i], newChoice, StringComparison.CurrentCultureIgnoreCase) == 0)
                {
                    validInput = true;
                    break;
                }
            }

            return validInput;
        }

        /// <summary>
        /// Saves the selected device choice to settings.
        /// </summary>
        /// <param name="newChoice">The new choice to save.</param>
        private void SaveDeviceChoiceToSettings(string newChoice)
        {
            SettingsManager.SaveSetting(SettingsManager.PublicSettings.Route, newChoice);
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

    /// <summary>
    /// Contains command ID constants for the Meadow package.
    /// </summary>
    static class PkgCmdIDList
    {
        /// <summary>
        /// Command ID for the Meadow device list combo box.
        /// </summary>
        public const uint cmdidMeadowDeviceListCombo = 0x101;
        /// <summary>
        /// Command ID for getting the list of Meadow devices.
        /// </summary>
        public const uint cmdidMeadowDeviceListComboGetList = 0x102;
    }
}