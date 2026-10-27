param([Parameter(Mandatory)][version]$InstallingVersion, [switch]$Elevated, [int]$OnlyProcessId = 0)
$ErrorActionPreference = 'Stop'
$currentSession = [Diagnostics.Process]::GetCurrentProcess().SessionId
$running = @(Get-Process -Name 'Voltura.NetOn' -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $currentSession -and ($OnlyProcessId -eq 0 -or $_.Id -eq $OnlyProcessId) })
if ($running.Count -eq 0) { exit 0 }
$administrator = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $administrator -and -not $Elevated) {
    try {
        $helper = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'), '-InstallingVersion', $InstallingVersion.ToString(), '-OnlyProcessId', $OnlyProcessId, '-Elevated') -Verb RunAs -WindowStyle Hidden -Wait -PassThru
        exit $helper.ExitCode
    } catch { exit 1223 }
}
$stopped = $false
foreach ($process in $running) {
    $path = $process.Path
    if (-not $path) { throw 'Cannot identify the running NetOn executable.' }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($path)
    if ($info.ProductName -ne 'Voltura NetOn' -or $info.CompanyName -notlike 'Voltura*' -or [IO.Path]::GetFileName($path) -ne 'Voltura.NetOn.exe') { continue }
    $version = [version]$info.FileVersion
    if ($version -ge $InstallingVersion) { continue }
    # Newer versions can exit gracefully through their per-user named event.
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    try {
        $event = [Threading.EventWaitHandle]::OpenExisting("Local\Voltura.NetOn.v2.$sid.Exit.$($process.Id)")
        [void]$event.Set(); $event.Dispose()
    } catch { [void]$process.CloseMainWindow() }
    if (-not $process.WaitForExit(2500)) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
    $stopped = $true
}
if ($stopped) { exit 10 }
exit 0
