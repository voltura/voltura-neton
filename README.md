# Voltura NetOn

Turn your PC's internet access on or off with one click from the icon next to the clock.

## Use

1. Download the **[Windows installer](https://github.com/voltura/voltura-neton/releases/latest)**.
2. Run Setup and follow the steps.
3. Open **Voltura NetOn** from the Start menu and accept the Windows permission prompt.

For Windows 10 and 11. NetOn appears next to the clock:

- **Green ON icon:** at least one network adapter is enabled; network access is allowed.
- **Red OFF icon:** all discovered network adapters are confirmed disabled.
- **Left-click:** toggle network access across all adapters.
- **Right-click:** adapter details, settings, external IP, About, and **Exit NetOn**.
- **Launch again:** reopen the existing instance's adapter window.

OFF disables all enabled network adapters, including Ethernet, Wi-Fi, Bluetooth PAN, mobile broadband, and virtual Ethernet. It saves the previously enabled adapters before changing them. ON restores that set, leaving previously disabled adapters disabled. Recovery information survives restarting NetOn while OFF. If there is no saved set, ON enables the available adapters.

The dropdown under **Network adapters…** selects the adapter whose details you view; it does not limit the global ON/OFF action. A connected adapter is selected initially, and your detail selection is remembered by GUID.

The original speech announcements, mute setting, customizable icon colors, reset colors, start-with-Windows setting, external-IP tooltip/copying, About, and tray toggle are included. Speech begins with a quiet 1.2-second audible cue, followed by the complete phrase in the same audio stream. This wakes outputs that discard digital silence. An active phrase finishes before the latest pending announcement plays. The default ON background is green as requested, with red for OFF. Color settings are available for both states.

Windows requires administrator rights to change adapter state. The launcher requests elevation at startup; canceling that prompt exits without making network changes. The startup registration uses the launcher, which also requests elevation when launched at sign-in. There are no per-toggle elevation helpers.

ON permits network access; it does not guarantee that a cable, Wi-Fi association, or internet service is available. A failed or partial OFF operation never displays the red OFF state while an adapter remains enabled. Status is refreshed periodically; if an adapter is enabled outside NetOn or new hardware appears, the indicator reflects the new state. Unknown status uses a gray question-mark icon.

Closing the adapter window hides it to the tray. **Exit NetOn** always closes the app, including during a refresh or a pending operation. Changes already applied by Windows remain in effect. Exiting never automatically turns adapters back on; reopening NetOn and choosing ON restores the saved set.

## Build

Requires Windows 10/11 and the .NET 10 SDK. Windows PowerShell's NetAdapter module is used only for changes; native Windows IP Helper APIs provide status without PowerShell or JSON parsing.

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

The framework-dependent `publish` folder requires the separately installed .NET 10 Desktop Runtime x64. Keep its files together and publish into a fresh folder when changing deployment types. User preferences are stored in `%LOCALAPPDATA%\Voltura\NetOn\preferences.json`; the startup option uses the current user's Windows Run key.

Build release packages with [NSIS](https://nsis.sourceforge.io/) installed:

```powershell
.\scripts\build-release.ps1
.\scripts\verify-installer.ps1
```

Release files are placed in `artifacts\release-1.0.3-framework`. The build creates a fresh framework-dependent payload, checks that runtime binaries are absent, builds the installer, and creates the portable ZIP. No checksum files are generated; GitHub provides asset digests.

The installer is per-user and does not launch NetOn automatically. If .NET 10 Desktop Runtime x64 is missing, it downloads the current stable release from Microsoft's official metadata, checks its SHA-512 digest and Microsoft signature, requests elevation for Microsoft's installer, runs it without restarting Windows, rechecks the dependency, and continues. Download failure, cancellation, or failed component setup prevents a partial NetOn installation.

Uninstall removes only the packaged files and installer integration, preserving preferences and unrelated files. Verification covers silent installation, upgrade, uninstall, and both failure and continuation through the component-setup flow, without launching NetOn or changing adapter state. It refuses to overwrite an existing installation during these checks. Run `scripts/verify-runtime-bootstrap.ps1` for download validation, integrity/signature rejection, installation failure, cancellation, and reboot-code checks using simulated installers; it does not install .NET on the development PC.

External IP lookup uses `https://api.ipify.org` over HTTPS with a four-second timeout. Failure or lack of internet does not block adapter controls or Exit. Toggling and discovery work offline.

## Verification

```powershell
dotnet run --project verification\NetOn.Verification.csproj -c Release
```

Checks native discovery against live adapters and exercises controller behavior with simulated adapter changes: global Ethernet/Wi-Fi shutdown, restoration of the original set across restarts, partial-failure reporting and icon state, refusal to disable if recovery information cannot be saved, selection during slow refresh, failure/recovery, Exit while work is pending, duplicate-instance activation, and restart after an abandoned instance. It shows no windows and changes no real network adapters. A real OFF/ON cycle, startup UAC, and physical speech playback remain manual acceptance checks.
