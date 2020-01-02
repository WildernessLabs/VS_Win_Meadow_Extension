using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Meadow.Utility
{
    interface IOutputPaneWriter
    {
        Task WriteAsync(string text);
    }

    class OutputPaneWriter : IOutputPaneWriter
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
            if (this.stopwatch.IsRunning)
            {
                await this.textWriter.WriteAsync($"{CurrentTimeStamp,-25} {text,-120} {ElapsedTime}").ConfigureAwait(false);
                this.stopwatch.Restart();
            }
            else
            {
                await this.textWriter.WriteAsync($"{CurrentTimeStamp,-25} {text,-120}").ConfigureAwait(false);
                this.stopwatch.Start();
            }
        }
    }
}
