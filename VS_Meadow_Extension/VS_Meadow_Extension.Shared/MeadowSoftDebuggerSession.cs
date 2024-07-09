using Meadow.Hcom;
using Microsoft.Extensions.Logging;
using Mono.Debugging.Client;
using Mono.Debugging.Soft;
using System;
using System.Threading;

namespace Meadow
{
    class MeadowSoftDebuggerSession : SoftDebuggerSession, IDisposable
    {
        private readonly CancellationTokenSource meadowDebugCancelTokenSource;
        private DebuggingServer meadowDebugServer;
        private readonly IMeadowConnection connection;
        private readonly ILogger logger;

        private readonly OutputLogger outputLogger = OutputLogger.Instance;
        private bool disposed = false;

        public MeadowSoftDebuggerSession(IMeadowConnection connection, ILogger deployOutputLogger)
        {
            this.connection = connection;
            this.logger = deployOutputLogger;
            meadowDebugCancelTokenSource = new CancellationTokenSource();
        }

        protected override async void OnRun(DebuggerStartInfo startInfo)
        {
            try
            {
                var meadowStartInfo = startInfo as SoftDebuggerStartInfo;
                var connectArgs = meadowStartInfo?.StartArgs as SoftDebuggerConnectArgs;
                var port = connectArgs?.DebugPort ?? 0;

                meadowDebugServer = await connection.StartDebuggingSession(port, logger, meadowDebugCancelTokenSource.Token);

                base.OnRun(startInfo);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start debugging session");
                throw;
            }
        }

        protected override async void OnExit()
        {
            try
            {
                if (!meadowDebugCancelTokenSource.IsCancellationRequested)
                {
                    meadowDebugCancelTokenSource.Cancel();
                }

                await meadowDebugServer?.StopListening();
                meadowDebugServer?.Dispose();
                meadowDebugServer = null;

                base.OnExit();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to stop debugging session");
                throw;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    meadowDebugCancelTokenSource?.Dispose();
                    meadowDebugServer?.Dispose();
                }

                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}