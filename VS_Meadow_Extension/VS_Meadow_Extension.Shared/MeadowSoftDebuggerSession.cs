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
        private readonly CancellationTokenSource meadowDebugCancelTokenSource = new CancellationTokenSource();
        private DebuggingServer meadowDebugServer;
        private readonly IMeadowConnection meadowConnection;
        private readonly ILogger logger;

        private bool disposed = false;

        public MeadowSoftDebuggerSession(IMeadowConnection connection, ILogger deployOutputLogger)
        {
            this.meadowConnection = connection ?? throw new ArgumentNullException(nameof(connection));
            this.logger = deployOutputLogger ?? throw new ArgumentNullException(nameof(deployOutputLogger));

        }

        protected override async void OnRun(DebuggerStartInfo startInfo)
        {
            try
            {
                if (!(startInfo is SoftDebuggerStartInfo meadowStartInfo)
                || !(meadowStartInfo.StartArgs is SoftDebuggerConnectArgs connectArgs))
                {
                    throw new ArgumentException("Invalid DebuggerStartInfo or DebuggerConnectArgs.");
                }

                var port = connectArgs.DebugPort;

                meadowDebugServer = await meadowConnection.StartDebuggingSession(port, logger, meadowDebugCancelTokenSource.Token);
            }
            catch (Exception ex)
            {
                throw new DebuggerException("Failed to start debugging session", ex);
            }
            finally
            {
                base.OnRun(startInfo);
            }
        }

        protected override async void OnExit()
        {
            try
            {
                meadowDebugCancelTokenSource.Cancel();

                await meadowDebugServer?.StopListening();
                meadowDebugServer?.Dispose();
                meadowDebugServer = null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to stop debugging session");
                throw;
            }
            finally
            {
                base.OnExit();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    meadowDebugCancelTokenSource.Cancel();
                    meadowDebugCancelTokenSource.Dispose();
                    meadowDebugServer?.Dispose();
                }

                disposed = true;
            }
        }

        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);

            base.Dispose();
        }
    }
}