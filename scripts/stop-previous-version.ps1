param([Parameter(Mandatory)][version]$InstallingVersion, [switch]$Elevated, [int]$OnlyProcessId = 0)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$currentSession = [Diagnostics.Process]::GetCurrentProcess().SessionId
function Test-NetOnFrameworkHost($Process) {
    try {
        $command = (Get-CimInstance Win32_Process -Filter "ProcessId=$($Process.Id)" -ErrorAction Stop).CommandLine
        $arguments = @([regex]::Matches($command, '"([^"]*)"|(\S+)') | ForEach-Object { if ($_.Groups[1].Success) { $_.Groups[1].Value } else { $_.Groups[2].Value } })
        $index = 1
        if ($arguments.Count -gt 1 -and $arguments[1] -eq 'exec') { $index++ }
        $hostOptions = @('--additionalprobingpath', '--additional-deps', '--depsfile', '--runtimeconfig', '--roll-forward', '--fx-version', '--roll-forward-on-no-candidate-fx', '--apply-patches')
        while ($index -lt $arguments.Count) {
            $argument = $arguments[$index]
            if ($argument.StartsWith('--')) {
                $option = $argument.Split('=')[0]
                if ($option -notin $hostOptions) { return $false }
                if ($argument.Contains('=')) { $index++ } else { $index += 2 }
                continue
            }
            # Only the entry assembly counts. A library or an application argument
            # named NetOn must never make an unrelated dotnet process a target.
            return [IO.Path]::GetFileName($argument) -eq 'Voltura.NetOn.dll'
        }
        return $false
    } catch { return $false }
}
$running = @(Get-Process -Name 'Voltura.NetOn', 'dotnet' -ErrorAction SilentlyContinue | Where-Object {
    try {
        if ($_.HasExited) { return $false }
        return $_.SessionId -eq $currentSession -and ($OnlyProcessId -eq 0 -or $_.Id -eq $OnlyProcessId) -and
            ($_.ProcessName -eq 'Voltura.NetOn' -or (Test-NetOnFrameworkHost $_))
    } catch { return $false } # Processes may exit while the candidate list is being read.
})
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
    if ($process.HasExited) { continue }
    $frameworkHost = $process.ProcessName -eq 'dotnet'
    try { $path = if ($frameworkHost) { @($process.Modules | Where-Object { $_.ModuleName -eq 'Voltura.NetOn.dll' } | Select-Object -First 1)[0].FileName } else { $process.Path } }
    catch { if ($process.HasExited) { continue }; throw }
    if (-not $path) { if ($process.HasExited) { continue }; throw 'Cannot identify the running NetOn executable.' }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($path)
    $expectedFile = if ($frameworkHost) { 'Voltura.NetOn.dll' } else { 'Voltura.NetOn.exe' }
    if ($info.ProductName -ne 'Voltura NetOn' -or $info.CompanyName -notlike 'Voltura*' -or [IO.Path]::GetFileName($path) -ne $expectedFile) { continue }
    $version = [version]$info.FileVersion
    if ($version -gt $InstallingVersion) { continue }
    # Newer versions can exit gracefully through their per-user named event.
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    try {
        $event = [Threading.EventWaitHandle]::OpenExisting("Local\Voltura.NetOn.v2.$sid.Exit.$($process.Id)")
        [void]$event.Set(); $event.Dispose()
    } catch { [void]$process.CloseMainWindow() }
    if (-not $process.WaitForExit(5000)) {
        try { $process.Kill() } catch { if (-not $process.HasExited) { throw } }
        if (-not $process.WaitForExit(10000)) { throw 'NetOn did not stop before installation.' }
    }
    $stopped = $true
}
if ($stopped) { exit 10 }
exit 0
