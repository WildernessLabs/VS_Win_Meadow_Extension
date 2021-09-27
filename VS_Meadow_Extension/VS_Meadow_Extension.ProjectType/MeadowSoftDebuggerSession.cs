using System;
using System.Net;
using System.Diagnostics;
using System.Threading;

using Mono.Debugging.Client;
using Mono.Debugging.Soft;

using Meadow.CLI.Core.DeviceManagement;
using Meadow.CLI.Core.Devices;

namespace Meadow
{
    class MeadowSoftDebuggerSession : SoftDebuggerSession
    {
        MeadowDeviceHelper meadow;

        public MeadowSoftDebuggerSession(MeadowDeviceHelper meadow)
        {
            this.meadow = meadow;
        }

        protected override async void OnRun(DebuggerStartInfo startInfo)
        {
            var softStartInfo = (SoftDebuggerStartInfo)startInfo;
            switch (softStartInfo.StartArgs)
            {
                case SoftDebuggerConnectArgs args:
                    await meadow.StartDebuggingSessionAsync(args.DebugPort, CancellationToken.None);
                    StartConnecting(softStartInfo);
                    break;

                case SoftDebuggerListenArgs args:
                    throw new NotImplementedException("FIXME");
                    //StartListening(softStartInfo, out var debugPort);
                    //(await MeadowDeviceManager.CreateDebuggingServer(meadow, new IPEndPoint(IPAddress.Loopback, debugPort))).Connect(meadow);
                    //break;

                default:
                    base.OnRun(startInfo);
                    break;
            }
        }
    }
}
