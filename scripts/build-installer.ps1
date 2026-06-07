param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$SelfContained = $false
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishRoot = Join-Path $repoRoot "publish"
$publishDir = Join-Path $publishRoot $Runtime
$protectedDir = Join-Path $publishRoot "$Runtime-protected"
$packageDir = Join-Path $publishRoot "SimpliMixi-v0.6.0"
$obfuscatedDir = Join-Path $publishRoot "$Runtime-obfuscated"
$projectPath = Join-Path $repoRoot "CV-AUT.csproj"
$configPath = Join-Path $repoRoot "Obfuscar.xml"
$issPath = Join-Path $repoRoot "installer\SimpliMixi.iss"
$setupPath = Join-Path $repoRoot "publish\SimpliMixi-v0.6.0-Setup.exe"
$dotNetRuntimePath = Join-Path $repoRoot "redist\windowsdesktop-runtime-8.0.0-win-x64.exe"

function Find-ObfuscarCli
{
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

    return $obfuscar.Source
}

function Find-InnoCompiler
{
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

    return $iscc.Source
}

function Protect-TemplateAssets
{
    param([string]$TemplateRoot)

    if (-not (Test-Path $TemplateRoot))
    {
        return
    }

    Add-Type -TypeDefinition @"
using System;
using System.IO;

public static class SimpliMixiTemplateEncryptor
{
    private static readonly byte[] Magic = { 0x53, 0x4D, 0x54, 0x50, 0x01 };
    private static readonly byte[] Key =
    {
        0x53, 0x69, 0x6D, 0x70, 0x6C, 0x69, 0x4D, 0x69,
        0x78, 0x69, 0x2D, 0x54, 0x65, 0x6D, 0x70, 0x6C,
        0x61, 0x74, 0x65, 0x73, 0x2D, 0x30, 0x35, 0x31
    };

    public static void EncryptDirectory(string root)
    {
        foreach (string pngPath in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
        {
            byte[] plainBytes = File.ReadAllBytes(pngPath);
            byte[] encryptedBytes = new byte[Magic.Length + plainBytes.Length];
            Buffer.BlockCopy(Magic, 0, encryptedBytes, 0, Magic.Length);

            for (int i = 0; i < plainBytes.Length; i++)
            {
                encryptedBytes[Magic.Length + i] = (byte)(plainBytes[i] ^ Key[i % Key.Length]);
            }

            string datPath = Path.ChangeExtension(pngPath, ".dat");
            File.WriteAllBytes(datPath, encryptedBytes);
            File.Delete(pngPath);
        }
    }
}
"@

    [SimpliMixiTemplateEncryptor]::EncryptDirectory($TemplateRoot)
}

if (-not (Test-Path $dotNetRuntimePath))
{
    throw "Missing .NET Desktop Runtime installer at $dotNetRuntimePath. Download Microsoft Windows Desktop Runtime 8 x64 and rename it to windowsdesktop-runtime-8.0.0-win-x64.exe."
}

Write-Host "Publishing $Configuration $Runtime..."
Remove-Item $publishDir, $protectedDir, $packageDir, $obfuscatedDir, $setupPath -Recurse -Force -ErrorAction SilentlyContinue

$selfContainedArg = if ($SelfContained)
{ "true"
} else
{ "false"
}
dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained $selfContainedArg -o $publishDir
if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Write-Host "Removing debug artifacts..."
Get-ChildItem $publishDir -Recurse -Include *.pdb,*.xml -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "Running Obfuscar..."
$obfuscar = Find-ObfuscarCli
& $obfuscar $configPath
if ($LASTEXITCODE -ne 0)
{
    throw "Obfuscar failed with exit code $LASTEXITCODE."
}

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

Write-Host "Encrypting template assets..."
Protect-TemplateAssets -TemplateRoot (Join-Path $packageDir "assets\Templates")

$readmePath = Join-Path $packageDir "README.txt"
@"
SimpliMixi v0.6.0

Run SimpliMixi.exe to start the app.

Package layout:
- adb/ contains the bundled Android Debug Bridge tools.
- assets/Templates/ contains encrypted .dat templates required by automation.
- redist runtime is installed by setup if Microsoft .NET 8 Desktop Runtime is missing.

Do not remove files or folders from this package.
"@ | Set-Content $readmePath -Encoding UTF8

Write-Host "Building installer..."
$iscc = Find-InnoCompiler
& $iscc $issPath
if ($LASTEXITCODE -ne 0)
{
    throw "Inno Setup compiler failed with exit code $LASTEXITCODE. Close any running setup window, then rerun this script."
}

if (-not (Test-Path $setupPath))
{
    throw "Installer output was not found at $setupPath"
}

Write-Host "Protected release ready: $setupPath"
Write-Host "Package folder: $packageDir"
Write-Host "Smoke test before release: install setup, launch app, verify encrypted template images, then test Start/End and ADB/BlueStacks flow."
