param([string]$Version = '1.0.4')
$ErrorActionPreference = 'Stop'
$netonRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $netonRoot "artifacts\release-$Version-framework"
$testId = [guid]::NewGuid().ToString('N')
$uninstallRelativeKey = "Software\Microsoft\Windows\CurrentVersion\Uninstall\NetOnVerify.$testId"
$installerRelativeKey = "Software\Voltura\NetOn\InstallerTests\$testId"
$menuName = "NetOn Verification $testId"
$uninstallKey = "HKCU:\$uninstallRelativeKey"
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) $menuName
$testRoot = Join-Path $netonRoot ('artifacts\installer-smoke-' + $testId)
$installer = Join-Path $testRoot 'SetupVerification.exe'
$isolationArgs = @("/DUNINSTALL_KEY=$uninstallRelativeKey", "/DINSTALLER_KEY=$installerRelativeKey", "/DMENU_NAME=$menuName")
$preferences = Join-Path $env:LOCALAPPDATA 'Voltura\NetOn\preferences.json'
$originalPreferences = if (Test-Path -LiteralPath $preferences) { (Get-FileHash -LiteralPath $preferences).Hash } else { '' }
New-Item -ItemType Directory -Path $testRoot | Out-Null
& 'C:\Program Files (x86)\NSIS\makensis.exe' /V2 "/DAPP_VERSION=$Version" "/DOUTPUT_FILE=$installer" "/DINSTALL_FILES=$releaseRoot\install-files.nsh" "/DUNINSTALL_FILES=$releaseRoot\uninstall-files.nsh" @isolationArgs (Join-Path $netonRoot 'installer\NetOn.nsi')
if ($LASTEXITCODE -ne 0) { throw 'Isolated verification installer build failed' }
foreach ($pass in 1..2) {
    $installed = Start-Process -FilePath $installer -ArgumentList @('/S', "/D=$testRoot") -WindowStyle Hidden -PassThru -Wait
    if ($installed.ExitCode -ne 0) { throw "Installer pass $pass failed: $($installed.ExitCode)" }
    if (-not (Test-Path -LiteralPath (Join-Path $testRoot 'Voltura.NetOn.exe'))) { throw 'App was not installed' }
    if (-not (Test-Path -LiteralPath (Join-Path $menu 'Voltura NetOn.lnk'))) { throw 'Start-menu shortcut is missing' }
    if ((Get-ItemProperty -LiteralPath $uninstallKey).DisplayVersion -ne $Version) { throw 'Uninstall registration is missing' }
    foreach ($forbidden in @('createdump.exe', 'coreclr.dll', 'hostfxr.dll', 'System.Windows.Forms.dll')) {
        if (Test-Path -LiteralPath (Join-Path $testRoot $forbidden)) { throw "Runtime file was bundled: $forbidden" }
    }
    Write-Output "PASS: silent install/upgrade pass $pass, Start-menu shortcut, registration, and runtime-free payload"
}
$lockedApp = [IO.File]::Open((Join-Path $testRoot 'Voltura.NetOn.exe'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    $blocked = Start-Process -FilePath $installer -ArgumentList @('/S', "/D=$testRoot") -WindowStyle Hidden -PassThru -Wait
    if ($blocked.ExitCode -ne 1618) { throw 'Installer did not refuse to overwrite a locked app' }
} finally { $lockedApp.Dispose() }
Write-Output 'PASS: installer refuses to overwrite an app that is still in use'
'Preserve unrelated files on uninstall.' | Set-Content -LiteralPath (Join-Path $testRoot 'preserve-me.txt')
Start-Process -FilePath (Join-Path $testRoot 'Uninstall.exe') -ArgumentList '/S' -WindowStyle Hidden -Wait
$deadline = [DateTime]::UtcNow.AddSeconds(15)
while ((Test-Path -LiteralPath $uninstallKey) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
if ((Test-Path -LiteralPath $uninstallKey) -or (Test-Path -LiteralPath (Join-Path $testRoot 'Voltura.NetOn.exe')) -or (Test-Path -LiteralPath $menu)) { throw 'Uninstall did not remove product files and integration' }
if (-not (Test-Path -LiteralPath (Join-Path $testRoot 'preserve-me.txt'))) { throw 'Uninstaller removed an unrelated file' }
$afterPreferences = if (Test-Path -LiteralPath $preferences) { (Get-FileHash -LiteralPath $preferences).Hash } else { '' }
if ($originalPreferences -ne $afterPreferences) { throw 'Installer changed application preferences' }
Write-Output 'PASS: uninstall removes product files, registry entry, and shortcuts; preserves preferences and unrelated files'
$probe = Join-Path $releaseRoot 'MissingRuntimeProbe.exe'
$missingRoot = Join-Path $testRoot 'no-runtime'
$stub = Join-Path $testRoot 'runtime-helper-test.ps1'
'exit 1603' | Set-Content -LiteralPath $stub
& 'C:\Program Files (x86)\NSIS\makensis.exe' /V2 "/DAPP_VERSION=$Version" "/DOUTPUT_FILE=$probe" "/DINSTALL_FILES=$releaseRoot\install-files.nsh" "/DUNINSTALL_FILES=$releaseRoot\uninstall-files.nsh" "/DTEST_RUNTIME_ROOT=$missingRoot" "/DTEST_RUNTIME_HELPER=$stub" @isolationArgs (Join-Path $netonRoot 'installer\NetOn.nsi')
if ($LASTEXITCODE -ne 0) { throw 'Missing-runtime probe build failed' }
$failedInstallRoot = Join-Path $testRoot 'must-not-install'
$missing = Start-Process -FilePath $probe -ArgumentList @('/S', "/D=$failedInstallRoot") -WindowStyle Hidden -PassThru -Wait
if ($missing.ExitCode -ne 1603 -or (Test-Path -LiteralPath $failedInstallRoot) -or (Test-Path -LiteralPath $uninstallKey)) { throw 'Missing-runtime prerequisite check failed' }
Write-Output 'PASS: component setup failure prevents installation (exit 1603)'
$fakeDesktop = Join-Path $missingRoot 'shared\Microsoft.WindowsDesktop.App\10.0.99'
@"
New-Item -ItemType Directory -Path '$fakeDesktop' -Force | Out-Null
'Test marker only.' | Set-Content -LiteralPath '$fakeDesktop\System.Windows.Forms.dll'
exit 0
"@ | Set-Content -LiteralPath $stub
& 'C:\Program Files (x86)\NSIS\makensis.exe' /V2 "/DAPP_VERSION=$Version" "/DOUTPUT_FILE=$probe" "/DINSTALL_FILES=$releaseRoot\install-files.nsh" "/DUNINSTALL_FILES=$releaseRoot\uninstall-files.nsh" "/DTEST_RUNTIME_ROOT=$missingRoot" "/DTEST_RUNTIME_HELPER=$stub" @isolationArgs (Join-Path $netonRoot 'installer\NetOn.nsi')
if ($LASTEXITCODE -ne 0) { throw 'Automatic-component-setup probe build failed' }
$completed = Start-Process -FilePath $probe -ArgumentList @('/S', "/D=$failedInstallRoot") -WindowStyle Hidden -PassThru -Wait
if ($completed.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $failedInstallRoot 'Voltura.NetOn.exe'))) { throw 'Installer did not continue after component setup' }
Start-Process -FilePath (Join-Path $failedInstallRoot 'Uninstall.exe') -ArgumentList '/S' -WindowStyle Hidden -Wait
$deadline = [DateTime]::UtcNow.AddSeconds(15)
while ((Test-Path -LiteralPath $uninstallKey) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
if (Test-Path -LiteralPath $uninstallKey) { throw 'Automatic-setup probe uninstall failed' }
Write-Output 'PASS: missing component starts the setup helper, rechecks availability, and continues installation'
