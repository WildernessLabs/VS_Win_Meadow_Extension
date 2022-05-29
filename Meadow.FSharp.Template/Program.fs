open System
open Meadow.Devices
open Meadow
open Meadow.Foundation.Leds
open Meadow.Foundation

type MeadowApp() =
    // Change F7FeatherV2 to F7Feather for V1.x boards
    inherit App<F7FeatherV2, MeadowApp>()
        
    do Console.WriteLine "Init with FSharp!"
    let led = new RgbPwmLed(MeadowApp.Device, MeadowApp.Device.Pins.OnboardLedRed,MeadowApp.Device.Pins.OnboardLedGreen, MeadowApp.Device.Pins.OnboardLedBlue,Meadow.Peripherals.Leds.IRgbLed.CommonType.CommonAnode)
    
    let ShowcolorPulse (color : Color) (duration : TimeSpan)  = 
        led.StartPulse(color, duration.Divide(2)) |> ignore
        Threading.Thread.Sleep (duration) |> ignore
        led.Stop |> ignore
    
    let cyclecolors (duration : TimeSpan)  = 
        while true do
            ShowcolorPulse Color.Blue duration 
            ShowcolorPulse Color.Cyan duration
            ShowcolorPulse Color.Green duration
            ShowcolorPulse Color.GreenYellow duration
            ShowcolorPulse Color.Yellow duration
            ShowcolorPulse Color.Orange duration
            ShowcolorPulse Color.OrangeRed duration
            ShowcolorPulse Color.Red duration
            ShowcolorPulse Color.MediumVioletRed duration
            ShowcolorPulse Color.Purple duration
            ShowcolorPulse Color.Magenta duration
            ShowcolorPulse Color.Pink duration
            
    do cyclecolors (TimeSpan.FromSeconds(1))

[<EntryPoint>]
let main argv =
    Console.WriteLine "Hello World from F#!"
    let app = new MeadowApp()
    Threading.Thread.Sleep (System.Threading.Timeout.Infinite)
    0 // return an integer exit code