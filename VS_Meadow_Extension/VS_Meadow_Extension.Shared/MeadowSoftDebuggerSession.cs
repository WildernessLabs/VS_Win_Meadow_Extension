using Meadow.Hcom;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;
using Mono.Debugging.Client;
using Mono.Debugging.Soft;
using System;
using System.Threading;

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
			try
			{
				if (startInfo is SoftDebuggerStartInfo meadowStartInfo)
				{
					if (meadowStartInfo.StartArgs is SoftDebuggerConnectArgs connectArgs)
					{
						if (connectArgs.DebugPort == 0)
						{
							logger.LogError("Invalid debug port specified.");
							return;
						}

						var port = connectArgs?.DebugPort ?? 0;

						await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

						meadowDebugServer = await meadowConnection.StartDebuggingSession(port, logger, meadowDebugCancelTokenSource.Token);
					}
					else
					{
						logger.LogError("Invalid debug arguments specified.");
						return;
					}
				}
			}
			catch(Exception ex)
			{
				logger.LogError($"Unable to Debug due to:{ex.Message}");
			}
			finally
			{
				base.OnRun(startInfo);
			}
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
				meadowConnection.Dispose();
				meadowConnection = null;
			}

			base.OnExit();
		}

		public override void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);

			base.Dispose();
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (meadowDebugCancelTokenSource != null
				&& !meadowDebugCancelTokenSource.IsCancellationRequested)
				{
					meadowDebugCancelTokenSource.Cancel();
					meadowDebugCancelTokenSource.Dispose();
					meadowDebugCancelTokenSource = null;
				}

				if (meadowDebugServer != null)
				{
					meadowDebugServer.Dispose();
					meadowDebugServer = null;
				}

				if (meadowConnection != null)
				{
					meadowConnection.Dispose();
					meadowConnection = null;
				}
			}
		}
	}
}