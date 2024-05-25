/*

namespace Meadow.Utility
{
    public interface IOutputPaneWriter 
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
    }
}

*/