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
    [ExportDynamicEnumValuesProvider("MeadowDebugProfileEnumValuesProvider")]
    [AppliesTo(Globals.MeadowCapability)]
    [Export(typeof(IDynamicDebugTargetsGenerator))]
    [ExportMetadata("Name", "MeadowDebugProfileEnumValuesProvider")]
    public class MeadowDebugProfileEnumValuesProvider : ProjectValueDataSourceBase<IReadOnlyList<IEnumValue>>, IDynamicEnumValuesProvider, IDynamicDebugTargetsGenerator
    {
        private IReceivableSourceBlock<IProjectVersionedValue<IReadOnlyList<IEnumValue>>> publicBlock;
        private TransformBlock<string, IProjectVersionedValue<IReadOnlyList<IEnumValue>>> debugProfilesBlock;

        // Represents the link to the launch profiles
        private IDisposable _launchProfileProviderLink;

        // Represents the link to our source provider
        private IDisposable debugProviderLink;

        private NamedIdentity _dataSourceKey = new NamedIdentity();
        public override NamedIdentity DataSourceKey
        {
            get { return _dataSourceKey; }
        }

        private int dataSourceVersion;
        public override IComparable DataSourceVersion
        {
            get { return dataSourceVersion; }
        }


        public override IReceivableSourceBlock<IProjectVersionedValue<IReadOnlyList<IEnumValue>>> SourceBlock
        {
            get
            {
                EnsureInitialized();
                return publicBlock;
            }
        }

        ISourceBlock<IProjectVersionedValue<object>> IProjectValueDataSource.SourceBlock => throw new NotImplementedException();

		public ILaunchSettingsProvider LaunchTargetsProvider { get; private set; }

		[ImportingConstructor]
        public MeadowDebugProfileEnumValuesProvider(UnconfiguredProject unconfiguredProject, ILaunchSettingsProvider launchSettingsProvider)
            : base(unconfiguredProject.Services)
        {
            LaunchTargetsProvider = launchSettingsProvider;
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

            return new MeadowDebugProfileEnumValuesGenerator();
        }

        protected override void Initialize()
        {
            debugProfilesBlock = new TransformBlock<string, IProjectVersionedValue<IReadOnlyList<IEnumValue>>>(
                update =>
                {
                    // Compute the new enum values from the profile provider
                    var generatedResult = MeadowDebugProfileEnumValuesGenerator.GetEnumeratorEnumValues().ToImmutableList();
                    dataSourceVersion++;
                    var dataSources = ImmutableDictionary<NamedIdentity, IComparable>.Empty.Add(DataSourceKey, DataSourceVersion);
                    return new ProjectVersionedValue<IReadOnlyList<IEnumValue>>(generatedResult, dataSources);
                });

            var broadcastBlock = new BroadcastBlock<IProjectVersionedValue<IReadOnlyList<IEnumValue>>>(b => b);

            // TODO var tizenLaunchSetting = _tizenLaunchSettingsProvider.TizenLaunchSetting;

            debugProviderLink = debugProfilesBlock.LinkTo(
                broadcastBlock,
                linkOptions: new DataflowLinkOptions { PropagateCompletion = true });

            publicBlock = broadcastBlock.SafePublicize();
            debugProfilesBlock.Post("InitDebugTargetDeviceList");
            // TODO DeviceManager.DebugProfilesBlockList?.Add(debugProfilesBlock);
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

                if (debugProviderLink != null)
                {
                    debugProviderLink.Dispose();
                    debugProviderLink = null;
                }
            }

            base.Dispose(disposing);
        }
    }
}