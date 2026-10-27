# Voltura NetOn

A Windows desktop app for turning a selected physical network adapter on and off. A modern .NET 10 port of [NetOn in VolturaTools](https://github.com/voltura/VolturaTools/tree/master/NetOn).

## Use

Launch **Voltura.NetOn.exe** and accept the Windows administrator prompt. Choose your Ethernet or Wi-Fi adapter, then use **Turn internet OFF** or **Turn internet ON**. The app initially selects a connected adapter and remembers your selection.

The status describes the adapter's administrative state and link state. Other enabled adapters may still provide internet access; enabling an adapter does not guarantee internet connectivity. All operations run locally and work while offline.

Closing the window hides it to the system tray. Double-click its tray icon to reopen it, or use its context menu to toggle the selected adapter. Use **Exit** to close the application. Exiting does not change adapter state, so turn the adapter back on before exiting if you want it enabled.

## Build and run

Requires Windows 10/11, Windows PowerShell with the NetAdapter module, and the .NET 10 SDK.

```powershell
dotnet build -c Release
Start-Process .\bin\Release\net10.0-windows\Voltura.NetOn.exe -Verb RunAs
```

Create a self-contained Windows x64 distribution (no separate .NET installation required):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o artifacts\win-x64
```

## Modernization

- .NET 10 Windows Forms, nullable types, and DPI-aware layout.
- Discover physical adapters instead of hard-coding `Ethernet`.
- Identify adapters by GUID, so names are not inserted into commands.
- Explicit administrator elevation, asynchronous operations, timeouts, error reporting, and state verification after changes.
- Visible controls, tray access, remembered selection, and periodic status refresh.
- No external IP lookup, third-party packages, or telemetry.

The original speech and color-customization features are not included in this version.
