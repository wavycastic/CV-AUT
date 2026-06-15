param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot
)

$ErrorActionPreference = "Stop"

$resolvedRoot = (Resolve-Path $PackageRoot).Path
$entries = New-Object System.Collections.Generic.List[object]

function Add-ManifestEntry
{
    param([string]$FullPath)

    if (-not (Test-Path $FullPath -PathType Leaf))
    {
        throw "Required protected file was not found: $FullPath"
    }

    $item = Get-Item $FullPath
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $FullPath).Hash.ToUpperInvariant()
    $relativePath = [System.IO.Path]::GetRelativePath($resolvedRoot, $item.FullName).Replace('\\', '/')
    $entries.Add([pscustomobject]@{
        path = $relativePath
        sha256 = $hash
        size = [int64]$item.Length
    }) | Out-Null
}

Add-ManifestEntry (Join-Path $resolvedRoot "Simplimixi.Backend.dll")
Add-ManifestEntry (Join-Path $resolvedRoot "simplimixi_native.dll")

$templateRoot = Join-Path $resolvedRoot "assets\Templates"
if (-not (Test-Path $templateRoot -PathType Container))
{
    throw "Encrypted template directory was not found: $templateRoot"
}

$templateFiles = Get-ChildItem $templateRoot -Recurse -File -Filter *.dat | Sort-Object FullName
if (-not $templateFiles)
{
    throw "No encrypted template assets (*.dat) were found under $templateRoot"
}

foreach ($templateFile in $templateFiles)
{
    Add-ManifestEntry $templateFile.FullName
}

$securityDir = Join-Path $resolvedRoot "security"
New-Item -ItemType Directory -Path $securityDir -Force | Out-Null

$manifest = [pscustomobject]@{
    generatedUtc = [DateTime]::UtcNow.ToString("o")
    files = $entries
}

$manifestPath = Join-Path $securityDir "integrity.manifest.json"
$manifest | ConvertTo-Json -Depth 4 | Set-Content -Path $manifestPath -Encoding UTF8
Write-Host "Integrity manifest written: $manifestPath"
