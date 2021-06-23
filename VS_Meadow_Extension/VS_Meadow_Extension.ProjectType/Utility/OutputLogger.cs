using Meadow.CLI.Core.Logging;
using Meadow.Utility;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Diagnostics;
using System.IO;

namespace Meadow
{
    public class OutputLogger : IMeadowLogger
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

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel)) { return; }

            var msg = formatter(state, exception);

            if (stopwatch.IsRunning)
                stopwatch.Restart();
            else
                stopwatch.Start();

            msg = $"{CurrentTimeStamp} {msg,-25}";

            textWriter?.WriteLine(msg);
            outputPane?.OutputString(msg);
        }
    }
}