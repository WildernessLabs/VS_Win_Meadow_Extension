using System.Threading;

using Mono.Debugging.Client;
using Mono.Debugging.Soft;
using Meadow.Hcom;
using System;
using Microsoft.Build.Utilities;
using Microsoft.Extensions.Logging;

namespace Meadow
{
    class MeadowSoftDebuggerSession : SoftDebuggerSession
    {
        CancellationTokenSource meadowDebugCancelTokenSource;
        DebuggingServer meadowDebugServer;
        IMeadowConnection meadowConnection;
        private ILogger logger;

        public MeadowSoftDebuggerSession(IMeadowConnection meadowConnection, ILogger deployOutputLogger)
        {
            this.meadowConnection = meadowConnection;
            this.logger = deployOutputLogger;
            meadowDebugCancelTokenSource = new CancellationTokenSource();
        }

        protected override async void OnRun(DebuggerStartInfo startInfo)
        {
            var meadowStartInfo = startInfo as SoftDebuggerStartInfo;
            var connectArgs = meadowStartInfo.StartArgs as SoftDebuggerConnectArgs;
            var port = connectArgs?.DebugPort ?? 0;

            meadowDebugServer = await meadowConnection.StartDebuggingSession(port, logger, meadowDebugCancelTokenSource.Token);

            meadowConnection.DeviceMessageReceived += MeadowConnection_DeviceMessageReceived;

            base.OnRun(startInfo);
        }

        private void MeadowConnection_DeviceMessageReceived(object sender, (string message, string source) e)
        {
            logger.LogInformation($"{e.message}");
        }

        protected override async void OnExit()
        {
            if (!meadowDebugCancelTokenSource.IsCancellationRequested)
                meadowDebugCancelTokenSource?.Cancel();

            if (meadowDebugServer != null)
            {
                await meadowDebugServer.StopListening();
                meadowDebugServer.Dispose();
                meadowDebugServer = null;
            }

            if (meadowConnection != null)
            {
                meadowConnection.DeviceMessageReceived -= MeadowConnection_DeviceMessageReceived;
                meadowConnection.Dispose();
                meadowConnection = null;
            }

            base.OnExit();
        }
    }
}