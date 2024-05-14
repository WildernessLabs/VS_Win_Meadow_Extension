using Meadow.Hcom;
using Microsoft.Extensions.Logging;
using Mono.Debugging.Client;
using Mono.Debugging.Soft;
using System.Threading;

namespace Meadow
{
    class MeadowSoftDebuggerSession : SoftDebuggerSession
    {
        readonly CancellationTokenSource meadowDebugCancelTokenSource;
        DebuggingServer meadowDebugServer;
        IMeadowConnection connection;
        private readonly ILogger logger;

        public MeadowSoftDebuggerSession(IMeadowConnection connection, ILogger deployOutputLogger)
        {
            this.connection = connection;
            this.logger = deployOutputLogger;
            meadowDebugCancelTokenSource = new CancellationTokenSource();
        }

        protected override async void OnRun(DebuggerStartInfo startInfo)
        {
            var meadowStartInfo = startInfo as SoftDebuggerStartInfo;
            var connectArgs = meadowStartInfo.StartArgs as SoftDebuggerConnectArgs;
            var port = connectArgs?.DebugPort ?? 0;

            meadowDebugServer = await connection.StartDebuggingSession(port, logger, meadowDebugCancelTokenSource.Token);

            connection.DeviceMessageReceived += MeadowConnection_DeviceMessageReceived;

            base.OnRun(startInfo);
        }

        private void MeadowConnection_DeviceMessageReceived(object sender, (string message, string source) e)
        {
            logger.LogInformation($"{e.message}");
        }

        protected override async void OnExit()
        {
            if (!meadowDebugCancelTokenSource.IsCancellationRequested)
            {
                meadowDebugCancelTokenSource?.Cancel();
            }

            if (meadowDebugServer != null)
            {
                await meadowDebugServer.StopListening();
                meadowDebugServer.Dispose();
                meadowDebugServer = null;
            }

            if (connection != null)
            {
                connection.DeviceMessageReceived -= MeadowConnection_DeviceMessageReceived;
                connection.Dispose();
                connection = null;
            }

            base.OnExit();
        }
    }
}