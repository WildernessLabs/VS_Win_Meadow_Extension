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

        // https://github.com/microsoft/VSProjectSystem/blob/master/doc/overview/mef.md
        [ImportMany(ExportContractNames.VsTypes.IVsHierarchy)]
        OrderPrecedenceImportCollection<IVsHierarchy> vsHierarchies;

        IVsHierarchy VsHierarchy => vsHierarchies.Single().Value;

        public bool SupportsProfile(ILaunchProfile profile) => true; // FIXME: Would we ever not?

        [ImportingConstructor]
        public MeadowDebuggerLaunchProvider(ConfiguredProject configuredProj)
        {
            this.configuredProject = configuredProj;

            vsHierarchies = new OrderPrecedenceImportCollection<IVsHierarchy>(
                projectCapabilityCheckProvider: configuredProj);
        }

        public async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile)
        {
            // TODO DeployProvider.MeadowConnection?.Dispose();

            //var device = await MeadowProvider.GetMeadowSerialDeviceAsync(logger: DeployProvider.DeployOutputLogger);

            if (!launchOptions.HasFlag(DebugLaunchOptions.NoDebug)
                && await IsMeadowApp()
                && DeployProvider.MeadowConnection != null)
            {
                MeadowPackage.DebugOrDeployInProgress = true;
                // TODO DeployProvider.Meadow = new MeadowDeviceHelper(device, DeployProvider.DeployOutputLogger);

                var meadowSession = new MeadowSoftDebuggerSession(DeployProvider.MeadowConnection);

                var startArgs = new SoftDebuggerConnectArgs(profile.Name, IPAddress.Loopback, 55898);
                await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
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
            // nop here because VS is responseable for starting us and then calling On*LaunchAsync above
            return true;
        }

        private async Task<bool> IsMeadowApp()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Assume configuredProject is your ConfiguredProject object
            var properties = configuredProject.Services.ProjectPropertiesProvider.GetCommonProperties();

            // We unfortunately still need to retrieve the AssemblyName property because we need both
            // the configuredProject to be a start-up project, but also an App (not library)
            string assemblyName = await properties.GetEvaluatedPropertyValueAsync("AssemblyName");
            if (!string.IsNullOrEmpty(assemblyName) && assemblyName.Equals("App", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}