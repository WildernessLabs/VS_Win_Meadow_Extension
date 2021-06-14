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
    class VsOutputPaneProgress : IProgress<string>
    {
        IProjectThreadingService ThreadingService;
        IVsOutputWindowPane OutputPane;

        public VsOutputPaneProgress(IProjectThreadingService threadingService)
        {
            ThreadingService = threadingService;
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
