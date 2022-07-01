using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

using Meadow.CLI.Core.Logging;

namespace Meadow
{
    class VsOutputPaneLogger : IProgress<string>, ILogger
    {
        IProjectThreadingService ThreadingService;
        IVsOutputWindowPane meadowOutputPane;
  
        Guid meadowPaneGuid = new Guid("C2FCAB2F-BFEB-4B1A-B385-08D4C81107FE");

		public IVsOutputWindowPane Pane { get; internal set; }

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
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (meadowOutputPane == null)
                {
                    IVsOutputWindow outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
                    if (outputWindow != null)
                    {
                        //check if the meadowOutputPane already exists, there can be only 1
                        outputWindow.GetPane(ref meadowPaneGuid, out meadowOutputPane);

                        if (meadowOutputPane == null) {
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
                            meadowOutputPane.Clear();
                        }

                        // Activate the pane, it should have been created by now
                        meadowOutputPane?.Activate();
                    }
                }
                Pane = meadowOutputPane;
                meadowOutputPane?.OutputString(value + Environment.NewLine);
            } catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
