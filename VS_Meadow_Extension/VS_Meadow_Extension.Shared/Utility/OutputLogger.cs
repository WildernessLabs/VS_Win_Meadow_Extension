using System;
using System.Diagnostics;
using System.IO;

using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

using Meadow.CLI.Core.Logging;

namespace Meadow
{
    public class OutputLogger : ILogger
    {
        private TextWriter textWriter;
        private IVsOutputWindowPane outputPane;
        private Stopwatch stopwatch;

        public string CurrentTimeStamp => $"[{DateTime.Now.ToLocalTime()}]";

        public OutputLogger ()
        {
            stopwatch = new Stopwatch();
        }

        public void ConnectPane(IVsOutputWindowPane pane)
        {
            outputPane = pane;
        }

        public void DisconnectPane()
        {
            outputPane = null;
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
                if (stopwatch.IsRunning)
                    stopwatch.Restart();
                else
                    stopwatch.Start();

                msg = $"{CurrentTimeStamp} {msg,-25}";

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                textWriter?.WriteLine(msg);
                outputPane?.OutputStringThreadSafe(msg);
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
    }
}