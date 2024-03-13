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
        IMeadowConnection meadow;
        private ILogger logger;

        public MeadowSoftDebuggerSession(IMeadowConnection meadow, ILogger deployOutputLogger)
        {
            this.meadow = meadow;
            this.logger = deployOutputLogger;
            meadowDebugCancelTokenSource = new CancellationTokenSource();
        }

        protected override async void OnRun(DebuggerStartInfo startInfo)
        {
            var meadowStartInfo = startInfo as SoftDebuggerStartInfo;
            var connectArgs = meadowStartInfo.StartArgs as SoftDebuggerConnectArgs;
            var port = connectArgs?.DebugPort ?? 0;

            meadowDebugServer = await meadow.StartDebuggingSession(port, logger, meadowDebugCancelTokenSource.Token);

            meadow.DeviceMessageReceived += MeadowConnection_DeviceMessageReceived;
            meadow.DebuggerMessageReceived += MeadowConnection_DebuggerMessageReceived;

            base.OnRun(startInfo);
        }

        private void MeadowConnection_DebuggerMessageReceived(object sender, byte[] e)
        {
            logger.LogInformation($"Bytes Received: {e}");
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

            if (meadow != null)
            {
                meadow.DeviceMessageReceived -= MeadowConnection_DeviceMessageReceived;
                meadow.DebuggerMessageReceived -= MeadowConnection_DebuggerMessageReceived;
                meadow.Dispose();
                meadow = null;
            }

            base.OnExit();
        }
    }
}