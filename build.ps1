[CmdletBinding()]
param(
    [string]$DotNetPath = 'dotnet',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\dist')
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

function Invoke-DotNet([string[]]$Arguments) {
    & $DotNetPath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Build command failed ($LASTEXITCODE)." }
}

Push-Location $PSScriptRoot
try {
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    foreach ($platform in @('AnyCPU', 'x86')) {
        Invoke-DotNet @('restore', 'tests\NeteaseLyricsTest.csproj', '--locked-mode', "-p:Platform=$platform")
        Invoke-DotNet @('build', 'tests\NeteaseLyricsTest.csproj', '-c', 'Release', '--no-restore', "-p:Platform=$platform", '--nologo')
        $binaryDirectory = if ($platform -eq 'AnyCPU') { 'tests\bin\Release\net48' } else { 'tests\bin\x86\Release\net48' }
        # Only the plugin and the test executable are copied: embedded dependencies must load on their own.
        $isolated = Join-Path $PSScriptRoot ('artifacts\isolated-' + $platform + '-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $isolated | Out-Null
        Copy-Item -LiteralPath 'bin\Release\net48\mb_NeteaseLyrics.dll' -Destination $isolated
        foreach ($file in @('NeteaseLyricsTest.exe', 'NeteaseLyricsTest.exe.config')) {
            Copy-Item -LiteralPath (Join-Path $binaryDirectory $file) -Destination $isolated
        }
        $expectedArchitecture = if ($platform -eq 'AnyCPU' -and [Environment]::Is64BitOperatingSystem) { '--expect-x64' } else { '--expect-x86' }
        & (Join-Path $isolated 'NeteaseLyricsTest.exe') $expectedArchitecture | Tee-Object -FilePath (Join-Path $OutputDirectory "tests-$platform.txt")
        if ($LASTEXITCODE -ne 0) { throw "Regression tests failed for $platform." }
    }

    $plugin = Join-Path $PSScriptRoot 'bin\Release\net48\mb_NeteaseLyrics.dll'
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($plugin).ProductVersion
    $package = Join-Path $OutputDirectory "NeteaseLyrics_$version.zip"
    Compress-Archive -LiteralPath $plugin -DestinationPath $package -Force
    $hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($package))" | Set-Content -LiteralPath ($package + '.sha256') -Encoding ascii
    Write-Output "Package: $package"
} finally {
    Pop-Location
}
