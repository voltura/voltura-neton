param(
    [string]$Version = '1.0.6',
    [string]$MakeNsis = 'C:\Program Files (x86)\NSIS\makensis.exe'
)
$ErrorActionPreference = 'Stop'
$netonRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $netonRoot "artifacts\release-$Version-framework"
$payload = Join-Path $releaseRoot ('app-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $payload -Force | Out-Null
dotnet publish (Join-Path $netonRoot 'Voltura.NetOn.csproj') -c Release -r win-x64 --self-contained false -p:DebugType=None -p:DebugSymbols=false -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
$forbidden = @('createdump.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Windows.Forms.dll')
foreach ($name in $forbidden) { if (Test-Path -LiteralPath (Join-Path $payload $name)) { throw "Runtime file must not be packaged: $name" } }
@'
Voltura NetOn

Run the Setup EXE and follow the instructions, then open NetOn from the Start menu.
The portable ZIP requires .NET 10 Desktop Runtime x64 to be installed separately.
Accept the administrator prompt once when NetOn starts.
Left-click the tray icon to toggle internet access across all adapters.
Right-click for network details, settings, and Exit NetOn.
ON restores the adapters enabled before OFF. Exiting while OFF leaves them disabled.

https://github.com/voltura/voltura-neton
'@ | Set-Content -LiteralPath (Join-Path $payload 'READ-ME.txt') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $netonRoot 'assets\voice\README.md') -Destination (Join-Path $payload 'VOICE-CREDITS.txt')
$installLines = [Collections.Generic.List[string]]::new()
$uninstallLines = [Collections.Generic.List[string]]::new()
$directories = [Collections.Generic.HashSet[string]]::new()
foreach ($file in Get-ChildItem -LiteralPath $payload -File -Recurse) {
    $relative = [IO.Path]::GetRelativePath($payload, $file.FullName)
    $directory = Split-Path -Parent $relative
    $safeDirectory = $directory.Replace('$', '$$')
    $safeName = $file.Name.Replace('$', '$$')
    $safePath = $file.FullName.Replace('$', '$$')
    $installLines.Add(('SetOutPath "$INSTDIR' + $(if ($directory) { '\' + $safeDirectory }) + '"'))
    $installLines.Add(('File "/oname=' + $safeName + '" "' + $safePath + '"'))
    $uninstallLines.Add(('Delete "$INSTDIR\' + $relative.Replace('$', '$$') + '"'))
    if ($directory) { [void]$directories.Add($directory) }
}
foreach ($directory in ($directories | Sort-Object Length -Descending)) { $uninstallLines.Add(('RMDir "$INSTDIR\' + $directory.Replace('$', '$$') + '"')) }
$installManifest = Join-Path $releaseRoot 'install-files.nsh'
$uninstallManifest = Join-Path $releaseRoot 'uninstall-files.nsh'
$installLines | Set-Content -LiteralPath $installManifest -Encoding utf8
$uninstallLines | Set-Content -LiteralPath $uninstallManifest -Encoding utf8
$installer = Join-Path $releaseRoot "Voltura-NetOn-$Version-Setup-win-x64.exe"
& $MakeNsis /V2 "/DAPP_VERSION=$Version" "/DOUTPUT_FILE=$installer" "/DINSTALL_FILES=$installManifest" "/DUNINSTALL_FILES=$uninstallManifest" (Join-Path $netonRoot 'installer\NetOn.nsi')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
$zip = Join-Path $releaseRoot "Voltura-NetOn-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $zip -CompressionLevel Optimal -Force
[pscustomobject]@{ Installer = $installer; Zip = $zip; Payload = $payload; InstallManifest = $installManifest; UninstallManifest = $uninstallManifest } | ConvertTo-Json
