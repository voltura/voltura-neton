# Voltura NetOn

The .NET 10 port of [NetOn by Voltura AB](https://github.com/voltura/VolturaTools/tree/master/NetOn), a Windows tray app for switching network access on and off across the PC.

## Use

Run `publish\Voltura.NetOn.exe` and accept the administrator prompt once. NetOn stays elevated until Exit, so subsequent ON/OFF clicks do not prompt again. NetOn starts in the system tray, as the original did:

- **Green ON icon:** at least one network adapter is enabled; network access is allowed.
- **Red OFF icon:** all discovered network adapters are confirmed disabled.
- **Left-click:** toggle network access across all adapters.
- **Right-click:** adapter details, settings, external IP, About, and **Exit NetOn**.
- **Launch again:** reopen the existing instance's adapter window.

OFF disables all enabled network adapters, including Ethernet, Wi-Fi, Bluetooth PAN, mobile broadband, and virtual Ethernet. It saves the previously enabled adapters before changing them. ON restores that set, leaving previously disabled adapters disabled. Recovery information survives restarting NetOn while OFF. If there is no saved set, ON enables the available adapters.

The dropdown under **Network adapters…** selects the adapter whose details you view; it does not limit the global ON/OFF action. A connected adapter is selected initially, and your detail selection is remembered by GUID.

The original speech announcements, mute setting, customizable icon colors, reset colors, start-with-Windows setting, external-IP tooltip/copying, About, and tray toggle are included. Speech includes 650 ms of leading audio-stream silence to allow the output device to wake before speaking. The default ON background is green as requested, with red for OFF. Color settings are available for both states.

Windows requires administrator rights to change adapter state. The launcher requests elevation at startup; canceling that prompt exits without making network changes. The startup registration uses the launcher, which also requests elevation when launched at sign-in. There are no per-toggle elevation helpers.

ON permits network access; it does not guarantee that a cable, Wi-Fi association, or internet service is available. A failed or partial OFF operation never displays the red OFF state while an adapter remains enabled. Status is refreshed periodically; if an adapter is enabled outside NetOn or new hardware appears, the indicator reflects the new state. Unknown status uses a gray question-mark icon.

Closing the adapter window hides it to the tray. **Exit NetOn** always closes the app, including during a refresh or a pending operation. Changes already applied by Windows remain in effect. Exiting never automatically turns adapters back on; reopening NetOn and choosing ON restores the saved set.

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

Checks native discovery against live adapters and exercises controller behavior with simulated adapter changes: global Ethernet/Wi-Fi shutdown, restoration of the original set across restarts, partial-failure reporting and icon state, refusal to disable if recovery information cannot be saved, selection during slow refresh, failure/recovery, Exit while work is pending, duplicate-instance activation, and restart after an abandoned instance. It shows no windows and changes no real network adapters. A real OFF/ON cycle, startup UAC, and physical speech playback remain manual acceptance checks.
