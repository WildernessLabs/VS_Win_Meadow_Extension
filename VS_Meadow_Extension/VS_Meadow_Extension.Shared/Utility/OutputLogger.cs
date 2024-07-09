using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Diagnostics;
using System.IO;
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
        private const uint TOTAL_PROGRESS = 100;
        private readonly object _lock = new object();

        public static OutputLogger Instance { get; } = new OutputLogger();

        private OutputLogger()
        {
            _ = Task.Run(async () =>
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

                await ShowMeadowOutputPane();

                statusBar = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsStatusbar)) as IVsStatusbar;
            });
        }

        public async Task ConnectTextWriter(TextWriter writer)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            textWriter = writer;

            meadowOutputPane?.Clear();
        }

        public IDisposable BeginScope<TState>(TState state) => default;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public async void Log(string message)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                lock (_lock)
                {
                    textWriter?.Write(message);
                }
            }
            catch (ObjectDisposedException ex)
            {
                Debug.WriteLine($"TextWriter has been disposed {ex}");
                textWriter = null;
            }
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);

            Log(message);
        }

        public void Report(string message)
        {
            Log(message);
        }

        internal async Task ShowMeadowOutputPane()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            meadowOutputPane?.Activate();
        }

        internal async Task ShowBuildOutputPane()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            IVsOutputWindow outputWindow = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            Guid buildPaneGuid = VSConstants.GUID_BuildOutputWindowPane;
            IVsOutputWindowPane buildOutputPane;

            outputWindow.GetPane(ref buildPaneGuid, out buildOutputPane);
            buildOutputPane?.Activate();
        }

        internal async Task ResetProgressBar()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            statusBar?.Progress(ref progressBarCookie, 0, string.Empty, TOTAL_PROGRESS, TOTAL_PROGRESS);
        }

        internal async Task ReportFileProgress(string fileName, uint percentage)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            statusBar?.Progress(ref progressBarCookie, 1, $"Transferring: {fileName}", percentage, TOTAL_PROGRESS);
        }

        internal async Task ReportDownloadProgress(string osVersion, long byteReceived)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            statusBar?.SetText($"Downloading OsVersion: {osVersion}; Bytes Received {byteReceived}");
        }

        internal async Task ReportDeviceMessage(string message)
        {
            try
            {
                //check and see if the message ends with a newline, if not add one
                if (!message.EndsWith("\n"))
                {
                    message += Environment.NewLine;
                }
                meadowOutputPane?.OutputStringThreadSafe(message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Let's not crash the IDE.{Environment.NewLine}Exception:{Environment.NewLine}{ex.Message}{Environment.NewLine}StackTrace:{Environment.NewLine}{ex.StackTrace}");
            }
        }
    }
}