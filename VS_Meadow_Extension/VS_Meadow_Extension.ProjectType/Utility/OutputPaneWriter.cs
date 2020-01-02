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

        public string CurrentTimeStamp => DateTime.Now.ToLocalTime().ToString();

        public async Task WriteAsync(string text)
        {
            if (this.stopwatch.IsRunning)
            {
                await this.textWriter.WriteAsync($"{CurrentTimeStamp} {text} ({this.stopwatch.Elapsed} since last log.)").ConfigureAwait(false);
                this.stopwatch.Restart();
            }
            else
            {
                await this.textWriter.WriteAsync($"{CurrentTimeStamp} {text}").ConfigureAwait(false);
                this.stopwatch.Start();
            }
        }
    }
}
