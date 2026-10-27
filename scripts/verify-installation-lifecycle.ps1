param([string]$Version = '0.0.0')
$ErrorActionPreference = 'Stop'
$netonRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $netonRoot "artifacts\release-$Version-framework"
$testId = [guid]::NewGuid().ToString('N')
$testRoot = Join-Path $netonRoot "artifacts\installer-lifecycle-$testId"
$uninstallRelative = "Software\Microsoft\Windows\CurrentVersion\Uninstall\NetOnLifecycle.$testId"
$installerRelative = "Software\Voltura\NetOn\InstallerTests\$testId\Installer"
$runRelative = "Software\Voltura\NetOn\InstallerTests\$testId\Run"
$uninstallKey = "HKCU:\$uninstallRelative"
$installerKey = "HKCU:\$installerRelative"
$runKey = "HKCU:\$runRelative"
$menuName = "NetOn Lifecycle $testId"
$menuRoot = Join-Path ([Environment]::GetFolderPath('Programs')) $menuName
$desktopLink = Join-Path $testRoot 'DesktopLink.lnk'
$installer = Join-Path $testRoot 'LifecycleVerification.exe'
$stopStub = Join-Path $testRoot 'stop-test.ps1'
$ready = Join-Path $testRoot 'ready.txt'
$paths = @('A','B','C','D') | ForEach-Object { Join-Path $testRoot $_ }
$script:failures = 0
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$isolation = @("/DUNINSTALL_KEY=$uninstallRelative", "/DINSTALLER_KEY=$installerRelative", "/DMENU_NAME=$menuName", "/DTEST_UPGRADE_HELPER=$stopStub", "/DRUN_KEY=$runRelative", "/DSTARTUP_VALUE=NetOnTest", "/DDESKTOP_LINK=$desktopLink")
function Build-Probe {
    & 'C:\Program Files (x86)\NSIS\makensis.exe' /V2 "/DAPP_VERSION=$Version" "/DOUTPUT_FILE=$installer" "/DINSTALL_FILES=$releaseRoot\install-files.nsh" "/DUNINSTALL_FILES=$releaseRoot\uninstall-files.nsh" @isolation (Join-Path $netonRoot 'installer\NetOn.nsi')
    if ($LASTEXITCODE -ne 0) { throw 'Isolated lifecycle installer build failed' }
}
function Install-Probe([string]$Path) {
    $process = Start-Process -FilePath $installer -ArgumentList @('/S', "/D=$Path") -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Test install failed: $($process.ExitCode)" }
    $process.Dispose()
}
function Uninstall-Probe([string]$Path) {
    $uninstaller = Join-Path $Path 'Uninstall.exe'
    if (-not (Test-Path -LiteralPath $uninstaller)) { return }
    Start-Process -FilePath $uninstaller -ArgumentList '/S' -WindowStyle Hidden -Wait
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ((Test-Path -LiteralPath (Join-Path $Path 'Voltura.NetOn.exe')) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (Test-Path -LiteralPath (Join-Path $Path 'Voltura.NetOn.exe')) { throw 'Isolated uninstall did not finish' }
}
function Check([bool]$Condition, [string]$Name) {
    if ($Condition) { Write-Output "PASS NEW: $Name" }
    else { $script:failures++; Write-Output "FAIL NEW: $Name" }
}
$shell = New-Object -ComObject WScript.Shell
$first = $null
try {
    'param($InstallingVersion); exit 0' | Set-Content -LiteralPath $stopStub
    Build-Probe
    Install-Probe $paths[0]
    New-Item -Path $runKey -Force | Out-Null
    $oldExe = Join-Path $paths[0] 'Voltura.NetOn.exe'
    New-ItemProperty -LiteralPath $runKey -Name NetOnTest -Value ('"' + $oldExe + '"') -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name DesktopShortcut -Value $paths[0] -PropertyType String -Force | Out-Null
    $link = $shell.CreateShortcut($desktopLink); $link.TargetPath = $oldExe; $link.Save()
    Install-Probe $paths[1]
    $newExe = Join-Path $paths[1] 'Voltura.NetOn.exe'
    $startup = (Get-ItemProperty -LiteralPath $runKey).NetOnTest
    $target = $shell.CreateShortcut($desktopLink).TargetPath
    Check ($startup -eq ('"' + $newExe + '"') -and $target -eq $newExe) 'moving the installation updates its owned startup entry and desktop shortcut'
    Uninstall-Probe $paths[0]
    $current = (Get-ItemProperty -LiteralPath $installerKey -ErrorAction SilentlyContinue).InstallDir
    $menuLink = Join-Path $menuRoot 'Voltura NetOn.lnk'
    Check ($current -eq $paths[1] -and (Test-Path -LiteralPath $uninstallKey) -and (Test-Path -LiteralPath $menuLink)) 'an old uninstaller preserves the current installation registration and shortcuts'
    Uninstall-Probe $paths[1]
    @"
param(`$InstallingVersion)
'Ready' | Set-Content -LiteralPath '$ready'
Start-Sleep -Seconds 5
exit 0
"@ | Set-Content -LiteralPath $stopStub
    Build-Probe
    $first = Start-Process -FilePath $installer -ArgumentList @('/S', "/D=$($paths[2])") -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $ready)) {
        if ($first.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'First isolated installer did not reach its helper' }
        Start-Sleep -Milliseconds 100
    }
    $second = Start-Process -FilePath $installer -ArgumentList @('/S', "/D=$($paths[3])") -WindowStyle Hidden -Wait -PassThru
    Check ($second.ExitCode -eq 1618) 'concurrent installer is rejected before changing shared registration'
    $second.Dispose()
    $first.WaitForExit(); $first.Dispose(); $first = $null
} finally {
    if ($null -ne $first) { $first.WaitForExit(); $first.Dispose() }
    foreach ($path in $paths) { Uninstall-Probe $path }
    if (Test-Path -LiteralPath $desktopLink) { Remove-Item -LiteralPath $desktopLink }
    foreach ($key in @($runKey, $installerKey, $uninstallKey)) {
        if (Test-Path -LiteralPath $key) { Remove-Item -LiteralPath $key }
    }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
}
if ($script:failures) { throw "New lifecycle regressions failed: $script:failures" }
