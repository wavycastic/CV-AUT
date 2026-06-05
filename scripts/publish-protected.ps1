param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$SelfContained = $true
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishRoot = Join-Path $repoRoot "publish"
$publishDir = Join-Path $publishRoot $Runtime
$protectedDir = Join-Path $publishRoot "$Runtime-protected"
$packageDir = Join-Path $publishRoot "SimpliMixi-v0.5.0"
$obfuscatedDir = Join-Path $publishRoot "$Runtime-obfuscated"
$projectPath = Join-Path $repoRoot "CV-AUT.csproj"
$configPath = Join-Path $repoRoot "Obfuscar.xml"

Write-Host "Publishing $Configuration $Runtime..."
Remove-Item $publishDir, $protectedDir, $packageDir, $obfuscatedDir -Recurse -Force -ErrorAction SilentlyContinue

$selfContainedArg = if ($SelfContained)
{ "true"
} else
{ "false"
}
dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained $selfContainedArg -o $publishDir

Write-Host "Removing debug artifacts..."
Get-ChildItem $publishDir -Recurse -Include *.pdb,*.xml -ErrorAction SilentlyContinue | Remove-Item -Force

$obfuscar = Get-Command obfuscar.console -ErrorAction SilentlyContinue
if (-not $obfuscar)
{
    $obfuscar = Get-Command Obfuscar.Console -ErrorAction SilentlyContinue
}
if (-not $obfuscar)
{
    $obfuscar = Get-Command obfuscar -ErrorAction SilentlyContinue
}

if (-not $obfuscar)
{
    throw "Obfuscar CLI was not found. Install it first, then rerun this script. Example: dotnet tool install --global Obfuscar.GlobalTool"
}

Write-Host "Running Obfuscar..."
& $obfuscar.Source $configPath

if (-not (Test-Path $obfuscatedDir))
{
    throw "Obfuscar output was not found at $obfuscatedDir"
}

Write-Host "Creating protected package..."
New-Item -ItemType Directory -Path $protectedDir -Force | Out-Null
Copy-Item (Join-Path $publishDir "*") $protectedDir -Recurse -Force
Copy-Item (Join-Path $obfuscatedDir "SimpliMixi.dll") (Join-Path $protectedDir "SimpliMixi.dll") -Force

Get-ChildItem $protectedDir -Recurse -Include *.pdb,*.xml -ErrorAction SilentlyContinue | Remove-Item -Force
Remove-Item (Join-Path $protectedDir "Backgrounds"), (Join-Path $protectedDir "AppIcon"), (Join-Path $protectedDir "Templates") -Recurse -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
Copy-Item (Join-Path $protectedDir "*") $packageDir -Recurse -Force
$readmePath = Join-Path $packageDir "README.txt"
@"
SimpliMixi v0.5.0

Run SimpliMixi.exe to start the app.

Package layout:
- adb/ contains the bundled Android Debug Bridge tools.
- assets/Templates/ contains image templates required by automation.
- runtimes/ contains native dependencies required by .NET/OpenCV.

Do not remove files or folders from this package.
"@ | Set-Content $readmePath -Encoding UTF8

Write-Host "Protected build ready: $packageDir"
Write-Host "Smoke test before release: launch app, navigate all tabs, test Start/End, verify ADB/BlueStacks flow."
