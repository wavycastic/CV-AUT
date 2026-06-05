$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$issPath = Join-Path $repoRoot "installer\SimpliMixi.iss"
$setupPath = Join-Path $repoRoot "publish\SimpliMixi-v0.5.0-Setup.exe"

Write-Host "Building protected package first..."
& (Join-Path $PSScriptRoot "publish-protected.ps1")

$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $iscc)
{
    $candidateX86 = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    $candidateX64 = "C:\Program Files\Inno Setup 6\ISCC.exe"

    if (Test-Path $candidateX86)
    {
        $iscc = [pscustomobject]@{ Source = $candidateX86 }
    } elseif (Test-Path $candidateX64)
    {
        $iscc = [pscustomobject]@{ Source = $candidateX64 }
    }
}

if (-not $iscc)
{
    throw "Inno Setup 6 compiler was not found. Install Inno Setup 6, then rerun this script. Download: https://jrsoftware.org/isdl.php"
}

Write-Host "Building installer..."
& $iscc.Source $issPath

if (-not (Test-Path $setupPath))
{
    throw "Installer output was not found at $setupPath"
}

Write-Host "Installer ready: $setupPath"
