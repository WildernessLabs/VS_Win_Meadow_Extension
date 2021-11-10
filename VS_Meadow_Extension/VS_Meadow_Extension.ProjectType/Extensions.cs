using System;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

namespace Meadow
{
    static class Extensions
    {
        public static EnvDTE.Project GetProject(this IVsHierarchy hierarchy)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            ErrorHandler.ThrowOnFailure(hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_ExtObject, out var obj));
            return obj as EnvDTE.Project;
        }
    }
}
