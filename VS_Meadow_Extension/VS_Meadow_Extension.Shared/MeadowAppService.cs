using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell;

namespace Meadow
{
	[Export(typeof(IMeadowAppService))]
	public class MeadowAppService : IMeadowAppService
	{
		public async Task<bool> IsMeadowApp(ConfiguredProject configuredProject)
		{
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

			var properties = configuredProject.Services.ProjectPropertiesProvider.GetCommonProperties();
			string assemblyName = await properties.GetEvaluatedPropertyValueAsync("AssemblyName");

			return !string.IsNullOrEmpty(assemblyName) && assemblyName.Equals("App", StringComparison.OrdinalIgnoreCase);
		}
	}
}