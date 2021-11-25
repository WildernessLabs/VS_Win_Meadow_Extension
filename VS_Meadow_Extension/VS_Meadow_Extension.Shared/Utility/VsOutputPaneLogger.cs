using Meadow.CLI.Core.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Meadow
{
    class VsOutputPaneLogger : IProgress<string>, ILogger
    {
        IProjectThreadingService ThreadingService;
        IVsOutputWindowPane OutputPane;

        public IDisposable BeginScope<TState>(TState state) => default;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public VsOutputPaneLogger(IProjectThreadingService threadingService)
        {
            ThreadingService = threadingService;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Report(formatter(state, exception));
            }
        }

        public async void Report(string value)
        {
            try
            {
                await ThreadingService.SwitchToUIThread();
                if (OutputPane == null)
                {
                    IVsOutputWindow outWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
                    Guid generalPaneGuid = VSConstants.GUID_OutWindowDebugPane; // P.S. There's also the GUID_OutWindowDebugPane available.
                    outWindow.GetPane(ref generalPaneGuid, out OutputPane);
                }
                OutputPane.OutputString(value + Environment.NewLine);
            } catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
