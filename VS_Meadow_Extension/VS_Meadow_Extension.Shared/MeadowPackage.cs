using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Composition;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.Win32;
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
    [InstalledProductRegistration("#1110", "#1112", "1.0", IconResourceID = 1400)] // Info on this package for Help/About
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(GuidList.guidMeadowPackageString)]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    [ProvideToolWindow(typeof(MeadowWindow))]
    public sealed class MeadowPackage : AsyncPackage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MeadowPackage"/> class.
        /// </summary>
        public MeadowPackage()
        {
            // Inside this method you can place any initialization code that does not require
            // any Visual Studio service because at this point the package object is created but
            // not sited yet inside Visual Studio environment. The place to do all the other
            // initialization is the Initialize method.
        }


        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Add our command handlers for menu (commands must be declared in the .vsct file)
            OleMenuCommandService mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (null != mcs)
            {
                CommandID menuMeadowDeviceListComboCommandID = new CommandID(GuidList.guidMeadowPackageCmdSet, (int)PkgCmdIDList.cmdidMeadowDeviceListCombo);
                OleMenuCommand menuMeadowDeviceListComboCommand = new OleMenuCommand(new EventHandler(OnMeadowDeviceListCombo), menuMeadowDeviceListComboCommandID);
                mcs.AddCommand(menuMeadowDeviceListComboCommand);

                CommandID menuMeadowDeviceListComboGetListCommandID = new CommandID(GuidList.guidMeadowPackageCmdSet, (int)PkgCmdIDList.cmdidMeadowDeviceListComboGetList);
                MenuCommand menuMeadowDeviceListComboGetListCommand = new OleMenuCommand(new EventHandler(OnMeadowDeviceListComboGetList), menuMeadowDeviceListComboGetListCommandID);
                mcs.AddCommand(menuMeadowDeviceListComboGetListCommand);
            }

            await MeadowWindowCommand.InitializeAsync(this);
        }
        #endregion

        private void OnMeadowDeviceListCombo(object sender, EventArgs e)
        {
            OleMenuCmdEventArgs eventArgs = e as OleMenuCmdEventArgs;

            if (eventArgs != null)
            {
                /* string newChoice = eventArgs.InValue as string;
                IntPtr vOut = eventArgs.OutValue;

                if (vOut != IntPtr.Zero)
                {
                    // when vOut is non-NULL, the IDE is requesting the current value for the combo
                    Marshal.GetNativeVariantForObject(currentDropDownComboChoice, vOut);
                }

                else if (newChoice != null)
                {
                    // new value was selected or typed in
                    // see if it is one of our items
                    bool validInput = false;
                    int indexInput = -1;
                    for (indexInput = 0; indexInput < dropDownComboChoices.Length; indexInput++)
                    {
                        if (string.Compare(dropDownComboChoices[indexInput], newChoice, StringComparison.CurrentCultureIgnoreCase) == 0)
                        {
                            validInput = true;
                            break;
                        }
                    }

                    if (validInput)
                    {
                        currentDropDownComboChoice = dropDownComboChoices[indexInput];
                        ShowMessage(Resources.MyDropDownCombo, currentDropDownComboChoice);
                    }
                    else
                    {
                        throw (new ArgumentException(Resources.ParamNotValidStringInList)); // force an exception to be thrown
                    }
                }*/
            }
            else
            {
                // We should never get here; EventArgs are required.
                throw (new ArgumentException("EventArgsRequired")); // force an exception to be thrown
            }
        }

        private void OnMeadowDeviceListComboGetList(object sender, EventArgs e)
        {
            OleMenuCmdEventArgs eventArgs = e as OleMenuCmdEventArgs;

            if (eventArgs != null)
            {
                object inParam = eventArgs.InValue;
                IntPtr vOut = eventArgs.OutValue;

                /*if (inParam != null)
                {
                    throw (new ArgumentException(Resources.InParamIllegal)); // force an exception to be thrown
                }
                else if (vOut != IntPtr.Zero)
                {
                    Marshal.GetNativeVariantForObject(dropDownComboChoices, vOut);
                }
                else
                {
                    throw (new ArgumentException(Resources.OutParamRequired)); // force an exception to be thrown
                }*/
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
