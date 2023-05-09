using System;
using System.Diagnostics;
using System.IO;

using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

using Meadow.CLI.Core.Logging;
using Microsoft.VisualStudio;
using EnvDTE;
using System.Net;
using System.Threading.Tasks;

namespace Meadow
{
    public class OutputLogger : IProgress<string>, ILogger
    {
        private TextWriter textWriter;
        private IVsOutputWindowPane meadowOutputPane;
		Guid meadowPaneGuid = new Guid("C2FCAB2F-BFEB-4B1A-B385-08D4C81107FE");
		private IVsStatusbar statusBar;
		private uint progressBarCookie = 0;
		private uint nextProgress = 0;
		private const uint PROGESS_INCREMENTS = 5;
		private const uint TOTAL_PROGRESS = 100;

		public OutputLogger()
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (meadowOutputPane == null)
                {
                    IVsOutputWindow outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
                    if (outputWindow != null)
                    {
                        //check if the meadowOutputPane already exists, there can be only 1
                        outputWindow.GetPane(ref meadowPaneGuid, out meadowOutputPane);

                        if (meadowOutputPane == null)
                        {
                            var returnStatus = outputWindow.CreatePane(ref meadowPaneGuid, "Meadow", Convert.ToInt32(true), Convert.ToInt32(true));
                            if (returnStatus == VSConstants.S_OK)
                            {
                                //Retrieve newly created Pane
                                outputWindow.GetPane(ref meadowPaneGuid, out meadowOutputPane);
                            }
                        }
                        else
                        {
                            // It already exists, so clear it for this run
                            meadowOutputPane?.Clear();
                        }

                        // Activate the pane, it should have been created by now
                        await ShowMeadowLogs();
					}
                }

                statusBar = Package.GetGlobalService(typeof(SVsStatusbar)) as IVsStatusbar;
			});
        }

        public void ConnectTextWriter(TextWriter writer)
        {
            textWriter = writer;
        }

        public void DisconnectTextWriter()
        {
            textWriter?.Dispose();
            textWriter = null;
        }

        public IDisposable BeginScope<TState>(TState state) => default;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public async void Log(string msg)
        {
			try
			{
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (msg.Contains("StdOut") || msg.Contains("StdInfo"))
                {
                    // This appears in the 
                    meadowOutputPane?.OutputStringThreadSafe(msg.Substring(15) + Environment.NewLine);
                }
                else
                {
                    if (msg == "[")
                    {
						// Display the progress bar.
						nextProgress = 0;
						statusBar?.Progress(ref progressBarCookie, 1, "File Transfer Started", 0, 0);
					}
                    else if (msg == "]")
                    {
						// Clear the progress bar.
						statusBar?.Progress(ref progressBarCookie, 0, "File Transfer Completed", 0, 0);
					}
                    else if (msg == "=")
                    {
                        statusBar?.Progress(ref progressBarCookie, 1, "File Transferring", nextProgress, TOTAL_PROGRESS);
						nextProgress += PROGESS_INCREMENTS;
					}
                    else
                    {
						statusBar?.Progress(ref progressBarCookie, 0, "", 0, 0);

						textWriter?.Write(msg);
                    }
                }
            }
			catch (Exception ex)
			{
                //Debug.WriteLine($"A Disposed Object Exception may have occured. Let's not crash the IDE.{Environment.NewLine}Exception:{Environment.NewLine}{ex.Message}{Environment.NewLine}StackTrace:{Environment.NewLine}{ex.StackTrace}");
            }
        }


        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel)) { return; }

            var msg = formatter(state, exception);

            Log(msg);
        }

		public void Report(string msg)
		{
			Log(msg);
		}

		internal async Task ShowMeadowLogs()
		{
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
			meadowOutputPane?.Activate();
		}
	}
}
