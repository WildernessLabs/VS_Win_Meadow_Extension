using Microsoft.VisualStudio.ProjectSystem;
using System.Threading.Tasks;

namespace Meadow
{
	public interface IMeadowAppService
	{
		Task<bool> IsMeadowApp(ConfiguredProject configuredProject);
	}
}