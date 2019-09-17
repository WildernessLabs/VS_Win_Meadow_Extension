using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;

namespace Meadow
{
    [ExportDebugger("asdf")] // name of the schema from above
    [AppliesTo("Meadow")]
    internal class MyDebugLaunchProvider : DebugLaunchProviderBase
    {
        //[Import]
        //// Code-generated type from compiling "XamlPropertyRule"
        //private readonly ProjectProperties projectProperties;

        [ImportingConstructor]
        public MyDebugLaunchProvider(ConfiguredProject configuredProject)
            : base(configuredProject)
        {
            //this.projectProperties = projectProperties;
        }

        [Import]
        private ProjectProperties ProjectProperties { get; set; }

        // This is one of the methods of injecting rule xaml files into the project system.
        [ExportPropertyXamlRuleDefinition("MyPackage, Version=1.0.0.0, Culture=neutral, PublicKeyToken=9be6e469bc4921f1", "XamlRuleToCode:MyDebugger.xaml", "Project")]
        [AppliesTo("Meadow")]
        private object DebuggerXaml { get { throw new NotImplementedException(); } }

        public override async Task<bool> CanLaunchAsync(DebugLaunchOptions launchOptions)
        {
            // perform any necessary logic to determine if the debugger can launch
            return false;
        }

        public override Task LaunchAsync(DebugLaunchOptions launchOptions)
        {
            return base.LaunchAsync(launchOptions);
        }

        public override async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions)
        {
            var settings = new DebugLaunchSettings(launchOptions);

            //// The properties that are available via DebuggerProperties are determined by the property XAML files in your project.
            //var debuggerProperties = await this.ProjectProperties.GetScriptDebuggerPropertiesAsync();
            //settings.CurrentDirectory = await debuggerProperties.RunWorkingDirectory.GetEvaluatedValueAtEndAsync();

            //string scriptCommand = await debuggerProperties.RunCommand.GetEvaluatedValueAtEndAsync();
            //string scriptArguments = await debuggerProperties.RunCommandArguments.GetEvaluatedValueAtEndAsync();

            //var generalProperties = await this.ProjectProperties.GetConfigurationGeneralPropertiesAsync();
            //string startupItem = await generalProperties.StartItem.GetEvaluatedValueAtEndAsync();

            //if ((launchOptions & DebugLaunchOptions.NoDebug) == DebugLaunchOptions.NoDebug)
            //{
            //    // No debug - launch cscript using cmd.exe to introduce a pause at the end
            //    settings.Executable = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            //    settings.Arguments = string.Format("/c {0} \"{1}\" {2} & pause", scriptCommand, startupItem, scriptArguments);
            //}
            //else
            //{
            //    // Debug - launch cscript using the debugger switch //X
            //    settings.Executable = scriptCommand;
            //    settings.Arguments = string.Format("\"{0}\" //X {1}", startupItem, scriptArguments);
            //}

            //settings.LaunchOperation = DebugLaunchOperation.CreateProcess;
            //settings.LaunchDebugEngineGuid = DebuggerEngines.ScriptEngine;

            settings.Executable = string.Empty;
            settings.Arguments = string.Empty;

            return new IDebugLaunchSettings[] { settings };
        }
    }
}