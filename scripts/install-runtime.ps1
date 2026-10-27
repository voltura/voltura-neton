param()
$ErrorActionPreference = 'Stop'

function Get-NetOnRuntimePackage {
    $metadata = Invoke-RestMethod -Uri 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json' -TimeoutSec 30
    $version = $metadata.'latest-runtime'
    if ($version -notmatch '^10\.0\.\d+$') { throw 'Invalid runtime version in Microsoft release metadata.' }
    $release = @($metadata.releases | Where-Object { $_.windowsdesktop.version -eq $version })[0]
    $package = @($release.windowsdesktop.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.exe' })[0]
    if (-not $package) { throw 'Windows Desktop Runtime installer was not found.' }
    $uri = [uri]$package.url
    if ($uri.Scheme -ne 'https' -or $uri.Host -notin @('builds.dotnet.microsoft.com', 'download.visualstudio.microsoft.com') -or $package.hash -notmatch '^[a-fA-F0-9]{128}$') {
        throw 'Microsoft runtime download metadata is invalid.'
    }
    return $package
}

function Test-NetOnRuntimePackage([string]$Path, [string]$ExpectedHash) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA512).Hash -ne $ExpectedHash) { throw 'Microsoft runtime download integrity check failed.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {
        throw 'The downloaded runtime does not have a valid Microsoft signature.'
    }
}

function Invoke-NetOnRuntimeSetup {
    $runtimeFile = Join-Path ([IO.Path]::GetTempPath()) ('Voltura-NetOn-runtime-' + [guid]::NewGuid().ToString('N') + '.exe')
    try {
        [Console]::WriteLine('Downloading required Windows components...')
        $package = Get-NetOnRuntimePackage
        Invoke-WebRequest -Uri $package.url -OutFile $runtimeFile -TimeoutSec 180 -UseBasicParsing
        Test-NetOnRuntimePackage $runtimeFile $package.hash
        [Console]::WriteLine('Installing required Windows components...')
        $process = Start-Process -FilePath $runtimeFile -ArgumentList @('/install', '/quiet', '/norestart') -Verb RunAs -WindowStyle Hidden -Wait -PassThru
        switch ($process.ExitCode) {
            0 { return 0 }
            3010 { return 3010 }
            1641 { return 3010 }
            1223 { return 1223 }
            default { throw "Microsoft component installer failed with exit code $($process.ExitCode)." }
        }
    } catch [System.ComponentModel.Win32Exception] {
        if ($_.Exception.NativeErrorCode -eq 1223) { return 1223 }
        [Console]::WriteLine($_.Exception.Message)
        return 1603
    } catch {
        [Console]::WriteLine($_.Exception.Message)
        return 1603
    } finally {
        # A scanner may still hold the downloaded file after Setup exits. Cleanup
        # must not change the actual component-installation result.
        try { if (Test-Path -LiteralPath $runtimeFile) { Remove-Item -LiteralPath $runtimeFile -Force -ErrorAction SilentlyContinue } } catch { }
    }
}

if ($MyInvocation.InvocationName -ne '.') { exit (Invoke-NetOnRuntimeSetup) }
