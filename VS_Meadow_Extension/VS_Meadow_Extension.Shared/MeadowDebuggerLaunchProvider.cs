using Meadow.CLI.Commands.DeviceManagement;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Mono.Debugging.Soft;
using Mono.Debugging.VisualStudio;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Net;
using System.Threading.Tasks;

namespace Meadow
{
    [Export(typeof(IDebugProfileLaunchTargetsProvider))]
    [AppliesTo(Globals.MeadowCapability)]
    [Order(999)]
    public class MeadowDebuggerLaunchProvider : IDebugProfileLaunchTargetsProvider, IDebugLauncher
    {
        static readonly DebuggingOptions debuggingOptions = new DebuggingOptions
        {
            EvaluationTimeout = 10000,
            MemberEvaluationTimeout = 15000,
            ModificationTimeout = 10000,
            SocketTimeout = 0
        };

        // FIXME: Find a nicer way than storing this
        DebuggerSession vsSession;
        private readonly ConfiguredProject configuredProject;

        [ImportMany(ExportContractNames.VsTypes.IVsHierarchy)]
        readonly OrderPrecedenceImportCollection<IVsHierarchy> vsHierarchies;

        IVsHierarchy VsHierarchy => vsHierarchies.Single().Value;

        public bool SupportsProfile(ILaunchProfile profile) => true;

        private readonly int DebugPort = 55898;

        static readonly OutputLogger outputLogger = OutputLogger.Instance;

        private readonly CLI.SettingsManager settingsManager = new CLI.SettingsManager();
        private readonly MeadowConnectionManager connectionManager = null;

        [ImportingConstructor]
        public MeadowDebuggerLaunchProvider(ConfiguredProject configuredProject)
        {
            this.configuredProject = configuredProject;
            this.connectionManager = new MeadowConnectionManager(settingsManager);

            vsHierarchies = new OrderPrecedenceImportCollection<IVsHierarchy>(
                projectCapabilityCheckProvider: configuredProject);
        }

        public async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile)
        {
            // If debugging is requested, warn the user and cancel launching.
            if (!launchOptions.HasFlag(DebugLaunchOptions.NoDebug))
            {
                outputLogger.Log("Debugging isn't supported from Visual Visual Studio 2022 - deploying app");

                // Return no debug launch settings so that nothing happens.
                return Array.Empty<IDebugLaunchSettings>();
            }

            var meadowConnection = MeadowDeployProvider.MeadowConnection;

            if (!launchOptions.HasFlag(DebugLaunchOptions.NoDebug)
                && await IsProjectAMeadowApp()
                && meadowConnection != null)
            {
                Globals.DebugOrDeployInProgress = true;

                var meadowSession = new MeadowSoftDebuggerSession(meadowConnection, outputLogger);

                var startArgs = new SoftDebuggerConnectArgs(profile.Name, IPAddress.Loopback, DebugPort);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var startInfo = new StartInfo(startArgs, debuggingOptions, VsHierarchy.GetProject());

                var sessionInfo = new SessionMarshalling(meadowSession, startInfo);
                vsSession = new DebuggerSession(startInfo, outputLogger, meadowSession, this);

                var settings = new MeadowDebugLaunchSettings(launchOptions, sessionInfo);

                return new[] { settings };
            }
            else
            {
                vsSession = null;
                return Array.Empty<IDebugLaunchSettings>();
            }
        }

        public Task OnBeforeLaunchAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile) => Task.CompletedTask;

        public async Task OnAfterLaunchAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile)
        {
            if (vsSession != null)
            {
                vsSession.Start();
                Globals.DebugOrDeployInProgress = false;

                await OutputLogger.Instance.ShowMeadowOutputPane();
            }
        }

        bool IDebugLauncher.StartDebugger(SoftDebuggerSession session, StartInfo startInfo)
        {
            // nop here because VS is responsible for starting us and then calling OnLaunchAsync above
            return true;
        }

        private async Task<bool> IsProjectAMeadowApp()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var properties = configuredProject.Services.ProjectPropertiesProvider.GetCommonProperties();
            string assemblyName = await properties.GetEvaluatedPropertyValueAsync("AssemblyName");

            if (!string.IsNullOrEmpty(assemblyName) && assemblyName.Equals("App", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}