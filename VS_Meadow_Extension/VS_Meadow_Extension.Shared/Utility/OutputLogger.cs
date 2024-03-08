using System;
using System.Diagnostics;
using System.IO;

using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

using Microsoft.VisualStudio;
using EnvDTE;
using System.Net;
using Microsoft.Extensions.Logging;
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
            _= Task.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (meadowOutputPane == null)
                {
                    IVsOutputWindow outputWindow = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
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
                    }
                }

                // Activate the pane, it should have been created by now
                await ShowMeadowLogs();

                statusBar = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsStatusbar)) as IVsStatusbar;
            });
        }

        public async System.Threading.Tasks.Task ConnectTextWriter(TextWriter writer)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            textWriter = writer;

            // It should exist now, so clear it for this run
            meadowOutputPane?.Clear();
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
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"A Disposed Object Exception may have occured. Let's not crash the IDE.{Environment.NewLine}Exception:{Environment.NewLine}{ex.Message}{Environment.NewLine}StackTrace:{Environment.NewLine}{ex.StackTrace}");
            }
        }


        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel)) { return; }

            var msg = formatter(state, exception);

            Log(msg);
        }

        public void Report(string message)
        {
            Log(message);
        }

        internal async System.Threading.Tasks.Task ShowMeadowLogs()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            meadowOutputPane?.Activate();
        }

        internal async Task ResetProgressBar()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            statusBar?.Progress(ref progressBarCookie, 0, string.Empty, 0, TOTAL_PROGRESS);
        }

        internal async Task Report(string fileName, uint percentage)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            statusBar?.Progress(ref progressBarCookie, 1, $"Transferring: {fileName}", percentage, TOTAL_PROGRESS);
        }

        internal async Task Report(string osVersion, long byteReceived)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            statusBar?.SetText($"Downloading OsVersion: {osVersion}; Bytes Received {(uint)byteReceived}");
        }
    }
}