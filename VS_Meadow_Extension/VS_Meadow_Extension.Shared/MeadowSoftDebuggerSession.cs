using System;
using System.Net;
using System.Diagnostics;
using System.Threading;

using Mono.Debugging.Client;
using Mono.Debugging.Soft;

using Meadow.CLI.Core.DeviceManagement;
using Meadow.CLI.Core.Devices;
using Meadow.CLI.Core.Internals.MeadowCommunication.ReceiveClasses;

namespace Meadow
{
    class MeadowSoftDebuggerSession : SoftDebuggerSession
    {
		CancellationTokenSource meadowDebugCancelTokenSource;
		DebuggingServer meadowDebugServer;
        MeadowDeviceHelper meadow;

        public MeadowSoftDebuggerSession(MeadowDeviceHelper meadow)
        {
            this.meadow = meadow;
            meadowDebugCancelTokenSource = new CancellationTokenSource();
        }

        protected override async void OnRun(DebuggerStartInfo startInfo)
        {
            var meadowStartInfo = startInfo as SoftDebuggerStartInfo;
            var connectArgs = meadowStartInfo.StartArgs as SoftDebuggerConnectArgs;
            var port = connectArgs?.DebugPort ?? 0;

            meadowDebugServer = await meadow.StartDebuggingSession(port, meadowDebugCancelTokenSource.Token);

            base.OnRun(startInfo);
        }

        protected override async void OnExit()
        {
            if (!meadowDebugCancelTokenSource.IsCancellationRequested)
                meadowDebugCancelTokenSource?.Cancel();

            await meadowDebugServer?.StopListening();
            meadowDebugServer?.Dispose();
            meadowDebugServer = null;
            meadow?.Dispose();

            base.OnExit();
        }
    }
}
