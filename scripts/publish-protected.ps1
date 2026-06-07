param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$SelfContained = $false
)

$ErrorActionPreference = "Stop"

Write-Host "publish-protected.ps1 now delegates to build-installer.ps1 so one run creates the protected package and installer."
& (Join-Path $PSScriptRoot "build-installer.ps1") -Runtime $Runtime -Configuration $Configuration -SelfContained:$SelfContained
