using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using Microsoft.Build.Framework.XamlTypes;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;

namespace Meadow
{
    /// <summary>
    /// Custom property page dynamic enum value provider
    /// </summary>
    [ExportDynamicEnumValuesProvider("MeadowDebugProfileEnumValueProvider")]
    [AppliesTo(Globals.MeadowCapability)]
    [Export(typeof(IDynamicDebugTargetsGenerator))]
    [ExportMetadata("Name", "MeadowDebugProfileEnumValueProvider")]
    public class MeadowDebugProfileEnumValueProvider : ProjectValueDataSourceBase<IReadOnlyList<IEnumValue>>, IDynamicEnumValuesProvider, IDynamicDebugTargetsGenerator
    {
        private IReceivableSourceBlock<IProjectVersionedValue<IReadOnlyList<IEnumValue>>> _publicBlock;

        // Represents the link to the launch profiles
        private IDisposable _launchProfileProviderLink;

        // Represents the link to our source provider
        private IDisposable _debugProviderLink;

        private NamedIdentity _dataSourceKey = new NamedIdentity();
        public override NamedIdentity DataSourceKey
        {
            get { return _dataSourceKey; }
        }

        private int _dataSourceVersion;
		private IDebugProfileLaunchTargetsProvider launchTargetsProvider;

		public override IComparable DataSourceVersion
        {
            get { return _dataSourceVersion; }
        }


        public override IReceivableSourceBlock<IProjectVersionedValue<IReadOnlyList<IEnumValue>>> SourceBlock
        {
            get
            {
                EnsureInitialized();
                return _publicBlock;
            }
        }

        ISourceBlock<IProjectVersionedValue<object>> IProjectValueDataSource.SourceBlock => throw new NotImplementedException();


        [ImportingConstructor]
        public MeadowDebugProfileEnumValueProvider(UnconfiguredProject unconfiguredProject, IDebugProfileLaunchTargetsProvider launchTargetsProvider)
            : base(unconfiguredProject.Services)
        {
            this.launchTargetsProvider = launchTargetsProvider;
        }

        /// <summary>
        /// Returns an <see cref="IDynamicEnumValuesGenerator"/> instance prepared to generate dynamic enum values
        /// given an (optional) set of options.
        /// </summary>
        /// <param name="options">
        /// A set of options set in XAML that helps to customize the behavior of the
        /// <see cref="IDynamicEnumValuesGenerator "/> instance in some way.
        /// </param>
        /// <returns>
        /// Either a new <see cref="IDynamicEnumValuesGenerator"/> instance
        /// or an existing one, if the existing one can serve responses based on the given <paramref name="options"/>.
        /// </returns>
        public async Task<IDynamicEnumValuesGenerator> GetProviderAsync(IList<NameValuePair> options)
        {
            // TODO: Provide your own implementation
            await Task.Yield();

            return new MeadowDebugProfileEnumValueGenerator();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_launchProfileProviderLink != null)
                {
                    _launchProfileProviderLink.Dispose();
                    _launchProfileProviderLink = null;
                }

                if (_debugProviderLink != null)
                {
                    _debugProviderLink.Dispose();
                    _debugProviderLink = null;
                }
            }

            base.Dispose(disposing);
        }
    }
}