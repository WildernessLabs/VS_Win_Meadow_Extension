open System
open Meadow.Devices
open Meadow
open Meadow.Foundation.Leds
open Meadow.Foundation

type MeadowApp() =
    // Change F7FeatherV2 to F7FeatherV1 for V1.x boards
    inherit App<F7FeatherV2, MeadowApp>()
        
    do Console.WriteLine "Init with FSharp!"
    let led = new RgbPwmLed(MeadowApp.Device, MeadowApp.Device.Pins.OnboardLedRed,MeadowApp.Device.Pins.OnboardLedGreen, MeadowApp.Device.Pins.OnboardLedBlue,3.3f,3.3f,3.3f,Meadow.Peripherals.Leds.IRgbLed.CommonType.CommonAnode)
    
    let ShowcolorPulses color duration = 
        led.StartPulse(color, (duration / 2)) |> ignore
        Threading.Thread.Sleep (int duration) |> ignore
        led.Stop |> ignore
    
    let cyclecolors duration = 
        while true do
            ShowcolorPulses Color.Blue duration 
            ShowcolorPulses Color.Cyan duration
            ShowcolorPulses Color.Green duration
            ShowcolorPulses Color.GreenYellow duration
            ShowcolorPulses Color.Yellow duration
            ShowcolorPulses Color.Orange duration
            ShowcolorPulses Color.OrangeRed duration
            ShowcolorPulses Color.Red duration
            ShowcolorPulses Color.MediumVioletRed duration
            ShowcolorPulses Color.Purple duration
            ShowcolorPulses Color.Magenta duration
            ShowcolorPulses Color.Pink duration
            
    do cyclecolors 1000

[<EntryPoint>]
let main argv =
    Console.WriteLine "Hello World from F#!"
    let app = new MeadowApp()
    Threading.Thread.Sleep (System.Threading.Timeout.Infinite)
    0 // return an integer exit code