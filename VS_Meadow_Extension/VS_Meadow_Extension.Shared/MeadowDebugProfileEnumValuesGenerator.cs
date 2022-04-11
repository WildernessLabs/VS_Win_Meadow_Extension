using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Build.Framework.XamlTypes;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Meadow.CLI.Core.DeviceManagement;
using Meadow.Helpers;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft;
using System.Linq;

namespace Meadow
{
    /// <summary>
    /// Generates dynamic enum values.
    /// </summary>
	public class MeadowDebugProfileEnumValuesGenerator : IDynamicEnumValuesGenerator
    {
        private readonly AsyncLazy<ICollection<IEnumValue>> listedValues;

		internal MeadowDebugProfileEnumValuesGenerator(ILaunchSettingsProvider profileProvider, IProjectThreadingService threadingService)
		{
            listedValues = new AsyncLazy<ICollection<IEnumValue>>(delegate
            {
                return Task.FromResult(GetEnumeratorEnumValues());
            }, threadingService.JoinableTaskFactory);
        }

		/// <summary>
		/// Gets whether the dropdown property UI should allow users to type in custom strings
		/// which will be validated by <see cref="TryCreateEnumValueAsync"/>.
		/// </summary>
		public bool AllowCustomValues
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// The list of values for this property that should be displayed to the user as common options.
        /// It may not be a comprehensive list of all admissible values however.
        /// </summary>
        /// <returns>List of sdb devices</returns>
        /// <seealso cref="AllowCustomValues"/>
        /// <seealso cref="TryCreateEnumValueAsync"/>
        public async Task<ICollection<IEnumValue>> GetListedValuesAsync()
        {
            return await listedValues.GetValueAsync();
        }

        internal static ICollection<IEnumValue> GetEnumeratorEnumValues()
        {
            var portList = MeadowDeviceManager.GetSerialPorts();
            bool hasDevice = portList.Count > 0;

            var list = new Collection<IEnumValue>();
            if (hasDevice)
            {
                MeadowSettings settings = new MeadowSettings(Globals.SettingsFilePath);
                if (portList.Count == 1)
                {
                    settings.DeviceTarget = portList[0];
                    settings.Save();
                }
                
                foreach(var port in portList)
				{
                    list.Add(new PageEnumValue(new EnumValue() { Name = port, DisplayName = $"App {port}" }));
                }
            }
            else
            {
                list.Add(new PageEnumValue(new EnumValue() { Name = MeadowDeviceManager.NoDevicesFound, DisplayName = MeadowDeviceManager.NoDevicesFound }));
            }

            // The list should have at least 1 value
            return list;
        }

        /// <summary>
        /// Tries to find or create an <see cref="IEnumValue"/> based on some user supplied string.
        /// </summary>
        /// <param name="userSuppliedValue">The string entered by the user in the property page UI.</param>
        /// <returns>
        /// An instance of <see cref="IEnumValue"/> if the <paramref name="userSuppliedValue"/> was successfully used
        /// to generate or retrieve an appropriate matching <see cref="IEnumValue"/>.
        /// A task whose result is <c>null</c> otherwise.
        /// </returns>
        /// <remarks>
        /// If <see cref="AllowCustomValues"/> is false, this method is expected to return a task with a <c>null</c> result
        /// unless the <paramref name="userSuppliedValue"/> matches a value in <see cref="GetListedValuesAsync"/>.
        /// A new instance of an <see cref="IEnumValue"/> for a value
        /// that was previously included in <see cref="GetListedValuesAsync"/> may be returned.
        /// </remarks>
        public async Task<IEnumValue> TryCreateEnumValueAsync(string userSuppliedValue)
        {
            return (await listedValues.GetValueAsync())
                .FirstOrDefault();
        }
    }
}