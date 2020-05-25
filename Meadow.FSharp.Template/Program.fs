// Learn more about F# at http://fsharp.org

open System
open Meadow.Devices
open Meadow
open Meadow.Foundation.Leds
open Meadow.Foundation



type MeadowApp() =
    inherit App<F7Micro, MeadowApp>()
        
    do Console.WriteLine "Init with FSharp!"
    let led = new RgbPwmLed(MeadowApp.Device, MeadowApp.Device.Pins.OnboardLedRed,MeadowApp.Device.Pins.OnboardLedGreen, MeadowApp.Device.Pins.OnboardLedBlue,3.3f,3.3f,3.3f,Meadow.Peripherals.Leds.IRgbLed.CommonType.CommonAnode)
    
    let ShowColourPulses colour duration = 
        led.StartPulse(colour, (duration / 2u)) |> ignore
        Threading.Thread.Sleep (int duration) |> ignore
        led.Stop |> ignore
    
    let cycleColours duration = 
        while true do
            ShowColourPulses Color.Blue duration 
            ShowColourPulses Color.Cyan duration
            ShowColourPulses Color.Green duration
            ShowColourPulses Color.GreenYellow duration
            ShowColourPulses Color.Yellow duration
            ShowColourPulses Color.Orange duration
            ShowColourPulses Color.OrangeRed duration
            ShowColourPulses Color.Red duration
            ShowColourPulses Color.MediumVioletRed duration
            ShowColourPulses Color.Purple duration
            ShowColourPulses Color.Magenta duration
            ShowColourPulses Color.Pink duration
            
    do cycleColours 1000u

    



[<EntryPoint>]
let main argv =
    Console.WriteLine "Hello World from F#!"
    let app = new MeadowApp()
    Threading.Thread.Sleep (System.Threading.Timeout.Infinite)
    0 // return an integer exit code
