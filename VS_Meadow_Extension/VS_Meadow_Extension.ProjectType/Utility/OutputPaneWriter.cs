using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Meadow.Utility
{
    public interface IOutputPaneWriter : ILogger
    {
        Task WriteAsync(string text);
    }

    class OutputPaneWriter : IOutputPaneWriter, ILogger
    {
        private readonly TextWriter textWriter;
        private readonly Stopwatch stopwatch;

        public OutputPaneWriter(TextWriter textWriter)
        {
            if (textWriter is null)
            {
                throw new ArgumentNullException(nameof(textWriter));
            }

            this.textWriter = textWriter;
            this.stopwatch = new Stopwatch();
        }

        public string CurrentTimeStamp => $"[{DateTime.Now.ToLocalTime()}]";

        public string ElapsedTime => $"({this.stopwatch.Elapsed} since last.)";

        public async Task WriteAsync(string text)
        {
            if (stopwatch.IsRunning)
            {
                await textWriter.WriteAsync($"{CurrentTimeStamp,-25} {text,-120} {ElapsedTime}").ConfigureAwait(false);
                stopwatch.Restart();
            }
            else
            {
                await textWriter.WriteAsync($"{CurrentTimeStamp,-25} {text,-120}").ConfigureAwait(false);
                stopwatch.Start();
            }
        }

        public IDisposable BeginScope<TState>(TState state) => default;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel)) { return; }

            var msg = formatter(state, exception);

            _ = WriteAsync(msg);

            /*
            if(msg.Contains("StdOut"))
            {

            }
            else
            {

            }
            */
        }
    }
}
