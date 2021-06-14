using System;
using System.IO;
using System.Runtime.Remoting;
using System.Runtime.Serialization.Formatters.Binary;

using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using Mono.Debugging.Soft;
using Mono.Debugging.VisualStudio;

namespace Meadow
{
    public class MonoDebugLaunchSettings : DebugLaunchSettings
    {
        // hold this as a field to ensure the object stays alive
        SessionMarshalling sessionInfo;

        public MonoDebugLaunchSettings(DebugLaunchOptions options, SessionMarshalling sessionInfo) : base(options)
        {
            this.sessionInfo = sessionInfo;

            LaunchOperation = DebugLaunchOperation.CreateProcess;
            Executable = "Mono";
            PortName = "Mono";
            PortSupplierGuid = Guids.PortSupplierGuid;
            LaunchDebugEngineGuid = Guids.EngineGuid;

            using (var stream = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(stream, RemotingServices.Marshal(sessionInfo));
                Options = Convert.ToBase64String(stream.ToArray());
            }
        }
    }
}
