$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'install-runtime.ps1')
$script:scenario = ''
$script:started = 0
$script:downloaded = ''
function Invoke-RestMethod {
    param($Uri, $TimeoutSec)
    $url = if ($script:scenario -eq 'untrusted-host') { 'https://example.com/runtime.exe' } else { 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.14/windowsdesktop-runtime-10.0.14-win-x64.exe' }
    return @{ 'latest-runtime' = '10.0.14'; releases = @(@{ windowsdesktop = @{ version = '10.0.14'; files = @(@{ rid = 'win-x64'; name = 'windowsdesktop-runtime-win-x64.exe'; url = $url; hash = ('a' * 128) }) } }) }
}
function Invoke-WebRequest {
    param($Uri, $OutFile, $TimeoutSec, [switch]$UseBasicParsing)
    if ($script:scenario -eq 'download-failed') { throw 'Simulated network failure' }
    $script:downloaded = $OutFile
    'Test data, not an executable.' | Set-Content -LiteralPath $OutFile
}
function Get-FileHash {
    param($LiteralPath, $Algorithm)
    return @{ Hash = $(if ($script:scenario -eq 'bad-hash') { 'b' * 128 } else { 'a' * 128 }) }
}
function Get-AuthenticodeSignature {
    param($LiteralPath)
    return @{ Status = $(if ($script:scenario -eq 'bad-signature') { 'NotTrusted' } else { 'Valid' }); SignerCertificate = @{ Subject = 'CN=Microsoft Corporation, O=Microsoft Corporation, C=US' } }
}
function Start-Process {
    param($FilePath, $ArgumentList, $Verb, $WindowStyle, [switch]$Wait, [switch]$PassThru)
    $script:started++
    if ($Verb -ne 'RunAs' -or '/norestart' -notin $ArgumentList -or '/quiet' -notin $ArgumentList) { throw 'Incorrect Microsoft installer invocation' }
    if ($script:scenario -eq 'canceled') { throw [ComponentModel.Win32Exception]::new(1223) }
    return @{ ExitCode = $(switch ($script:scenario) { 'reboot' { 3010 }; 'install-failed' { 1603 }; default { 0 } }) }
}
function Remove-Item {
    param($LiteralPath, [switch]$Force, $ErrorAction)
    if ($script:scenario -eq 'cleanup-locked') { throw [IO.IOException]::new('Temporary installer is locked by a scanner') }
    Microsoft.PowerShell.Management\Remove-Item -LiteralPath $LiteralPath -Force:$Force
}
foreach ($case in @(
    @{ Name='success'; Exit=0; Starts=1 },
    @{ Name='reboot'; Exit=3010; Starts=1 },
    @{ Name='canceled'; Exit=1223; Starts=1 },
    @{ Name='install-failed'; Exit=1603; Starts=1 },
    @{ Name='download-failed'; Exit=1603; Starts=0 },
    @{ Name='bad-hash'; Exit=1603; Starts=0 },
    @{ Name='bad-signature'; Exit=1603; Starts=0 },
    @{ Name='untrusted-host'; Exit=1603; Starts=0 },
    @{ Name='cleanup-locked'; Exit=0; Starts=1 }
)) {
    $script:scenario = $case.Name; $script:started = 0; $script:downloaded = ''
    try {
        $result = Invoke-NetOnRuntimeSetup
        if ($result -ne $case.Exit -or $script:started -ne $case.Starts) { throw "Runtime bootstrap case failed: $($case.Name), exit=$result, starts=$script:started" }
        if ($case.Name -ne 'cleanup-locked' -and $script:downloaded -and (Test-Path -LiteralPath $script:downloaded)) { throw 'Temporary runtime download was not removed' }
        Write-Output "PASS: runtime bootstrap $($case.Name)"
    } finally {
        if ($script:downloaded -and (Test-Path -LiteralPath $script:downloaded)) { [IO.File]::Delete($script:downloaded) }
    }
}
