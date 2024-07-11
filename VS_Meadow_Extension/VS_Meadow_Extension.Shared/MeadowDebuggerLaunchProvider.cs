using System;
using System.Net;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;

using Mono.Debugging.Soft;
using Mono.Debugging.VisualStudio;

namespace Meadow
{
    using DebuggerSession = Mono.Debugging.VisualStudio.DebuggerSession;

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
        private ConfiguredProject configuredProject;
        private IMeadowAppService meadowAppService;

        // https://github.com/microsoft/VSProjectSystem/blob/master/doc/overview/mef.md
        [ImportMany(ExportContractNames.VsTypes.IVsHierarchy)]
        OrderPrecedenceImportCollection<IVsHierarchy> vsHierarchies;

        IVsHierarchy VsHierarchy => vsHierarchies.Single().Value;

        public bool SupportsProfile(ILaunchProfile profile) => true; // FIXME: Would we ever not?

        [ImportingConstructor]
        public MeadowDebuggerLaunchProvider(ConfiguredProject configuredProject, IMeadowAppService meadowAppService)
        {
            this.configuredProject = configuredProject;
            this.meadowAppService = meadowAppService;

            vsHierarchies = new OrderPrecedenceImportCollection<IVsHierarchy>(
                projectCapabilityCheckProvider: this.configuredProject);
        }

        public async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile)
        {
            // TODO DeployProvider.MeadowConnection?.Dispose();

            //var device = await MeadowProvider.GetMeadowSerialDeviceAsync(logger: DeployProvider.DeployOutputLogger);

            if (!launchOptions.HasFlag(DebugLaunchOptions.NoDebug)
                && await meadowAppService.IsMeadowApp(configuredProject)
                && DeployProvider.MeadowConnection != null)
            {
                MeadowPackage.DebugOrDeployInProgress = true;

                var meadowSession = new MeadowSoftDebuggerSession(DeployProvider.MeadowConnection, DeployProvider.DeployOutputLogger);

                var startArgs = new SoftDebuggerConnectArgs(profile.Name, IPAddress.Loopback, 55898);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var startInfo = new StartInfo(startArgs, debuggingOptions, VsHierarchy.GetProject());

                var sessionInfo = new SessionMarshalling(meadowSession, startInfo);
                vsSession = new DebuggerSession(startInfo, DeployProvider.DeployOutputLogger, meadowSession, this);

                var settings = new MonoDebugLaunchSettings(launchOptions, sessionInfo);

                return new[] { settings };
            }
            else
            {
                vsSession = null;
                return Array.Empty<IDebugLaunchSettings>();
            }
        }

        public Task OnBeforeLaunchAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile) => Task.CompletedTask;

        public Task OnAfterLaunchAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile)
        {
            if (vsSession != null)
            {
                vsSession.Start();
                MeadowPackage.DebugOrDeployInProgress = false;

                _ = Task.Run(async () =>
                {
                    await Task.Delay(10000);
                    await DeployProvider.DeployOutputLogger?.ShowMeadowLogs();
                });
            }

            return Task.CompletedTask;
        }

        bool IDebugLauncher.StartDebugger(SoftDebuggerSession session, StartInfo startInfo)
        {
            // nop here because VS is responsible for starting us and then calling On*LaunchAsync above
            return true;
        }
    }
}