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
        readonly IMeadowConnection connection;
        private readonly ILogger logger;

        private readonly OutputLogger outputLogger = OutputLogger.Instance;

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

            base.OnRun(startInfo);
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

            base.OnExit();
        }
    }
}