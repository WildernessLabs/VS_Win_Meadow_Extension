using System;
using System.Threading;
using Meadow;
using Meadow.Devices;
using Meadow.Hardware;

namespace BasicMeadow
{
    public class MeadowApp : App<F7Micro, MeadowApp>
    {
        IDigitalOutputPort redLed;
        IDigitalOutputPort blueLed;
        IDigitalOutputPort greenLed;

        public MeadowApp()
        {
            ConfigurePorts();
            BlinkLeds();
        }

        public void ConfigurePorts()
        {
            Console.WriteLine("Creating Outputs...");
            redLed = Device.CreateDigitalOutputPort(Device.Pins.OnboardLedRed);
            blueLed = Device.CreateDigitalOutputPort(Device.Pins.OnboardLedBlue);
            greenLed = Device.CreateDigitalOutputPort(Device.Pins.OnboardLedGreen);
        }

        public void BlinkLeds()
        {
            var state = false;

            while (true)
            {
                int wait = 200;

                state = !state;

                Console.WriteLine($"State: {state}");

                redLed.State = state;
                Thread.Sleep(wait);
                blueLed.State = state;
                Thread.Sleep(wait);
                greenLed.State = state;
                Thread.Sleep(wait);
            }
        }
    }
}
