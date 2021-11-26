using System;
using System.Net;
using System.Diagnostics;
using System.Threading;

using Mono.Debugging.Client;
using Mono.Debugging.Soft;

using Meadow.CLI.Core.DeviceManagement;
using Meadow.CLI.Core.Devices;
using Meadow.CLI.Core.Internals.MeadowCommunication.ReceiveClasses;

namespace Meadow
{
    class MeadowSoftDebuggerSession : SoftDebuggerSession
    {
        MeadowDeviceHelper meadow;
        DebuggingServer meadowDebugServer;

        public MeadowSoftDebuggerSession(MeadowDeviceHelper meadow)
        {
            this.meadow = meadow;
        }

        protected override async void OnRun(DebuggerStartInfo startInfo)
        {
            try
            {
                var softStartInfo = (SoftDebuggerStartInfo)startInfo;
                switch (softStartInfo.StartArgs)
                {
                    case SoftDebuggerConnectArgs args:
                        meadowDebugServer = await meadow.StartDebuggingSessionAsync(args.DebugPort, CancellationToken.None);
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
            catch (Meadow.CLI.Core.Exceptions.DeviceDisconnectedException e)
            {

            }
        }

        protected override async void OnExit()
        {
            await meadowDebugServer?.StopListeningAsync();
            meadowDebugServer?.Dispose();
            meadowDebugServer = null;
            meadow?.Dispose();

            base.OnExit();
        }
    }
}
