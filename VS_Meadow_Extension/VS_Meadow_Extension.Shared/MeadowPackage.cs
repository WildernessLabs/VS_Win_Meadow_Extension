using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;
using Microsoft.VisualStudio.Shell;

using Meadow.Helpers;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using Meadow.CLI.Commands.DeviceManagement;
using Meadow.CLI;
using System.Linq;

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

		private static readonly object debugOrDeployLock = new object();
		private static bool debugOrDeployInProgress = false;

		public static bool DebugOrDeployInProgress
		{
			get
			{
				lock (debugOrDeployLock)
				{
					return debugOrDeployInProgress;
				}
			}
			set
			{
				lock (debugOrDeployLock)
				{
					debugOrDeployInProgress = value;
				}
			}
		}

		internal static SettingsManager SettingsManager { get; set; } = new SettingsManager();

		protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
		{
			await base.InitializeAsync(cancellationToken, progress);
			await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

			await InstallDependencies();

			if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService mcs)
			{
				var deviceListComboCommandID = new CommandID(GuidList.guidMeadowPackageCmdSet, (int)PkgCmdIDList.cmdidMeadowDeviceListCombo);
				var deviceListComboCommand = new OleMenuCommand(OnMeadowDeviceListCombo, deviceListComboCommandID);
				mcs.AddCommand(deviceListComboCommand);

				var deviceListComboGetListCommandID = new CommandID(GuidList.guidMeadowPackageCmdSet, (int)PkgCmdIDList.cmdidMeadowDeviceListComboGetList);
				var deviceListComboGetListCommand = new OleMenuCommand(OnMeadowDeviceListComboGetList, deviceListComboGetListCommandID);
				mcs.AddCommand(deviceListComboGetListCommand);
			}
		}

		private async void OnMeadowDeviceListCombo(object sender, EventArgs e)
		{
			if (DebugOrDeployInProgress || !(e is OleMenuCmdEventArgs eventArgs)) return;

			var portList = await MeadowConnectionManager.GetSerialPorts();
			IntPtr vOut = eventArgs.OutValue;

			if (vOut != IntPtr.Zero)
			{
				string deviceTarget = NoDevicesFound;
				if (portList.Count > 0)
				{
					var route = SettingsManager.GetSetting(SettingsManager.PublicSettings.Route);
					deviceTarget = IsValueInPortList(portList, route) ? route : string.Empty;
				}
				Marshal.GetNativeVariantForObject(deviceTarget, vOut);
			}
			else if (eventArgs.InValue is string newChoice && IsValueInPortList(portList, newChoice))
			{
				SaveDeviceChoiceToSettings(newChoice);
			}
		}

		private async void OnMeadowDeviceListComboGetList(object sender, EventArgs e)
		{
			if (DebugOrDeployInProgress || !(e is OleMenuCmdEventArgs eventArgs)) return;

			if (eventArgs.InValue != null || eventArgs.OutValue == IntPtr.Zero)
			{
				throw new ArgumentException("Invalid parameters for device list retrieval.");
			}

			var portList = await MeadowConnectionManager.GetSerialPorts();
			var outputList = portList.Count > 0 ? portList : new List<string> { NoDevicesFound };
			Marshal.GetNativeVariantForObject(outputList, eventArgs.OutValue);
		}

		private static bool IsValueInPortList(IList<string> portList, string newChoice)
		{
			return portList.Contains(newChoice, StringComparer.CurrentCultureIgnoreCase);
		}

		private void SaveDeviceChoiceToSettings(string newChoice)
		{
			SettingsManager.SaveSetting(SettingsManager.PublicSettings.Route, newChoice);
		}

		private async Task InstallDependencies()
		{
			if (!NetworkInterface.GetIsNetworkAvailable())
				return;

			string packageName = "WildernessLabs.Meadow.Template";
			if (!await InstallPackage(packageName))
			{
				// Consider logging or notifying the user that the package installation failed.
			}
		}

		private async Task<bool> InstallPackage(string packageName)
		{
			return await StartDotNetProcess("new install", packageName);
		}

		private async Task<bool> StartDotNetProcess(string command, string parameters)
		{
			using (var process = new System.Diagnostics.Process())
			{
				process.StartInfo.FileName = "dotnet";
				process.StartInfo.Arguments = $"{command} {parameters}";
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;
				process.Start();

				string output = await process.StandardOutput.ReadToEndAsync();
				process.WaitForExit();

				return output.Contains(parameters);
			}
		}
	}

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

	static class PkgCmdIDList
	{
		public const uint cmdidMeadowDeviceListCombo = 0x101;
		public const uint cmdidMeadowDeviceListComboGetList = 0x102;
	}
}