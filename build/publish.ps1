<#
.SYNOPSIS
    Publishes a self-contained, single-file build of Moovie.

.EXAMPLE
    ./build/publish.ps1                 # build for this machine
    ./build/publish.ps1 win-arm64       # build for one target
    ./build/publish.ps1 all             # build every target

.NOTES
    Output lands in artifacts/<runtime>/. Each build bundles the .NET runtime, so the
    result needs no installation and no .NET on the target machine.
#>
param([string]$Target = "")

$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$project = "src/Moovie.App/Moovie.App.csproj"
$allTargets = @("win-x64", "win-arm64", "osx-x64", "osx-arm64", "linux-x64", "linux-arm64")

$targets = switch ($Target) {
    "all"   { $allTargets }
    ""      { @(if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq "Arm64") { "win-arm64" } else { "win-x64" }) }
    default { @($Target) }
}

foreach ($runtime in $targets) {
    Write-Host "==> publishing $runtime"
    $output = "artifacts/$runtime"
    if (Test-Path $output) { Remove-Item -Recurse -Force $output }

    dotnet publish $project `
        --configuration Release `
        --runtime $runtime `
        --self-contained true `
        --output $output `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -p:SatelliteResourceLanguages=en

    if ($LASTEXITCODE -ne 0) { throw "publish failed for $runtime" }
    Write-Host "    done: $output"
}
