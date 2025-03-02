# VS Tools for Meadow

For step by step instructions on using this extension, [check out the tutorial](http://developer.wildernesslabs.co/Meadow/Getting_Started/Hello_World/).

## Release Notes

### 2.0.0

- Use Meadow.CLI v2 under the hood

### 1.9.9.2

- Lazy load settings and ensure Template installation happens in a background thread.

### 1.9.9.1

- Update NoLink list

### 1.9.9

- Include ProjectLab in NoLink list

### 1.9.8

- Fix for right-click Deploy issue, which regressed in recent version

### 1.9.7

- Add extra check to re-enable the runtime, if it isn't enabled after deployment.

### 1.9.6

- Do IsMeadowApp check earlier to avoid deployment of wrong project types

### 1.9.4

- Fix for VS2022 v17.9.4 so that debugging works again.

### 1.9.2

- Bump to pick up UnauthorisedAccess Exception

### 1.9.0

- Bump to protocol 8

### 1.8.1

- Moved Template update to background thread
- Deploy menu item is now more project sensitive

### 1.8.0

- Add a cancelation check
- Add a more realiable check to make sure out start-up project is a Meadow one.

### 1.5.0

- Improve dependency filter for App.dll
- Do a directory existing check before we check for internet.

### 1.4.0

- Fix IDE crash caused by uncaught TimeoutException

### 1.3.4

- Update dfu-util version check
- Push App.dll to device instead of App.exe

### 1.3.0

- Sync with Meadow.CLI versioning

### 1.2.0

- Clear Meadow Log each run. Move focus back to Meadow pane each run.

### 1.1.2

- Update spacing meadow.*.yaml to meet specs and sync with Meadow.Sdk templates.

### 1.1.1

- Update spacing app.*.yaml to meet specs and sync with Meadow.Sdk templates.

### 1.1.0

- Update overview.md. 
- Sync extension release numbers

### 1.0.4

- Fix Deploy/Debug regression (we’ve tested deploying/debugging on Windows, macOS and Linux (using VSCode))

### 1.0.0

- Official 1.0.0 release to coincide with Meadow OS 1.0.0 release

### 0.9.0

- Compatibility updates for Meadow OS beta 3.10.

### 0.8.0

- Compatibility updates for Meadow OS beta 3.9.

### 0.7.0

- Compatibility updates for Meadow OS beta 3.7.

### 0.6.0

- Add support to update device firmware from within the extension.

### 0.5.1

- Update project template to reference latest version (0.12.0) of Meadow.Foundation.

### 0.5.0

- Meadow OS beta 3.6 compatibility updates
- Fix incorrect Ctrl+2 messaging in debug output.
- Update project template with RgbPmwLed

### 0.4.0

- CRC checks for efficient deployment.

### 0.1.6

- Add Meadow.Foundation reference to project template.

### 0.1.5

- Support for new Meadow OS file system.

### 0.1.4

- Debug output enabled. Simply deploy the app and `Console.WriteLine()` outputs to the Debug Output pane.

### 0.1.3

- Create a Meadow application from "Create a new project" screen by searching for **Meadow**.

## Known Issues

