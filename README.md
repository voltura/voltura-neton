# Voltura NetOn

The .NET 10 port of [NetOn by Voltura AB](https://github.com/voltura/VolturaTools/tree/master/NetOn), a Windows tray app for switching a network adapter on and off.

## Use

Run `publish\Voltura.NetOn.exe`. NetOn starts in the system tray, as the original did:

- **Green ON icon:** selected adapter enabled.
- **Red OFF icon:** selected adapter disabled.
- **Left-click:** toggle the selected adapter.
- **Right-click:** adapter selection, settings, external IP, About, and **Exit NetOn**.
- **Launch again:** reopen the existing instance's adapter window.

The connected adapter is selected initially. Use **Network adapter…** in the tray menu to change it. The adapter is remembered by GUID, so renaming it does not break the selection. An unavailable saved adapter is not silently replaced by a different one; choose an available adapter in the window.

The original speech announcements, mute setting, customizable icon colors, reset colors, start-with-Windows setting, external-IP tooltip/copying, About, and tray toggle are included. The default ON background is green as requested, with red for OFF. Color settings are available for both states.

The app opens and reads status without administrator privileges. Changing adapter state asks Windows for administrator permission through a short-lived helper. Canceling that prompt leaves the adapter unchanged. You can also run NetOn as administrator to avoid a prompt for each toggle.

ON/OFF describes the selected adapter's administrative state. A disconnected but enabled adapter is ON. Other enabled adapters may still provide internet access. Enabling an adapter does not guarantee internet connectivity.

Closing the adapter window hides it to the tray. **Exit NetOn** always closes the app, including during a refresh or a pending operation. An adapter command already submitted to Windows may finish after Exit. Exiting never automatically turns an adapter back on.

## Build

Requires Windows 10/11 and the .NET 10 SDK. Windows PowerShell's NetAdapter module is used only for changes; native Windows IP Helper APIs provide status without PowerShell or JSON parsing.

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

The self-contained `publish` folder includes the runtime. Keep its files together. User preferences are stored in `%LOCALAPPDATA%\Voltura\NetOn\preferences.json`; the startup option uses the current user's Windows Run key.

External IP lookup uses `https://api.ipify.org` over HTTPS with a four-second timeout. Failure or lack of internet does not block adapter controls or Exit. Toggling and discovery work offline.

## Verification

```powershell
dotnet run --project verification\NetOn.Verification.csproj -c Release
```

Checks native discovery against live adapters and exercises controller behavior with simulated adapter changes: selection during slow refresh, failure/recovery, ON/OFF controls and icon colors, Exit while work is pending, duplicate-instance activation, and restart after an abandoned instance. It shows no windows and changes no real network adapters. A real OFF/ON cycle and physical tray appearance remain manual acceptance checks.
