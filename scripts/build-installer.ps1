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
$packageDir = Join-Path $publishRoot "SimpliMixi-v0.6.2"
$obfuscatedInputDir = Join-Path $publishRoot "$Runtime-obfuscator-input"
$obfuscatedDepsDir = Join-Path $publishRoot "$Runtime-obfuscator-deps"
$obfuscatedDir = Join-Path $publishRoot "$Runtime-obfuscated"
$projectPath = Join-Path $repoRoot "CV-AUT.csproj"
$backendBuildAssembly = Join-Path $repoRoot "src\Simplimixi\Backend\bin\$Configuration\net8.0-windows\Simplimixi.Backend.dll"
$configPath = Join-Path $repoRoot "Obfuscar.xml"
$issPath = Join-Path $repoRoot "installer\SimpliMixi.iss"
$setupPath = Join-Path $repoRoot "publish\SimpliMixi-v0.6.2-Setup.exe"
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

function Test-NoSensitiveTerms
{
    param(
        [string]$AssemblyPath,
        [string[]]$Terms
    )

    if (-not (Test-Path $AssemblyPath))
    {
        throw "Protected assembly was not found at $AssemblyPath"
    }

    $assemblyInfo = Get-Item $AssemblyPath
    if ($assemblyInfo.Length -le 0)
    {
        throw "Protected assembly '$([System.IO.Path]::GetFileName($AssemblyPath))' is empty."
    }

    $bytes = [System.IO.File]::ReadAllBytes($AssemblyPath)
    $hits = New-Object System.Collections.Generic.List[string]
    foreach ($term in $Terms)
    {
        $needle = [System.Text.Encoding]::UTF8.GetBytes($term)
        for ($i = 0; $i -le $bytes.Length - $needle.Length; $i++)
        {
            $matched = $true
            for ($j = 0; $j -lt $needle.Length; $j++)
            {
                if ($bytes[$i + $j] -ne $needle[$j])
                {
                    $matched = $false
                    break
                }
            }

            if ($matched)
            {
                $hits.Add($term)
                break
            }
        }
    }

    if ($hits.Count -gt 0)
    {
        throw "Protected assembly '$([System.IO.Path]::GetFileName($AssemblyPath))' still exposes sensitive terms: $($hits -join ', ')"
    }
}

function Test-ProtectedPackage
{
    param([string]$PackagePath)

    $appAssembly = Join-Path $PackagePath "SimpliMixi.dll"
    $backendAssembly = Join-Path $PackagePath "Simplimixi.Backend.dll"
    $nativeLibrary = Join-Path $PackagePath "simplimixi_native.dll"
    $oldTemplateKeys = @("SimpliMixi-Templates-051")
    $runtimeConfigAllowList = @(
        "SimpliMixi.deps.json",
        "SimpliMixi.runtimeconfig.json",
        "Config\test_config.json",
        "security\integrity.manifest.json"
    )
    $devOnlyPathPattern = '(^|\\)(tests?|samples?|fixtures?|debug|scripts?|tools?|devtools?|bench|diagnostics|\.git|\.vs|obj|TestResults)(\\|$)'
    $backendSensitiveTerms = @(
        "CVAutomationFramework",
        "VisionEngine",
        "ADBHelper",
        "Training",
        "Attacks",
        "WallUpdater",
        "IsTarget",
        "TemplateAssetLoader",
        "EmulatorBootstrapper",
        "ImageUtils",
        "NativeTemplateCodec",
        "Simplimixi.Backend.Core",
        "Dragon_Attack",
        "ElectroDragon_Attack",
        "[FSM-CS]",
        "[ATTACK-CS]",
        "[SCOUT-CS]",
        "phase=run_attack",
        "phase=select_strategy",
        "input tap",
        "input swipe",
        "exec-out screencap",
        "uiautomator dump",
        "pm list packages",
        "com.wetest.uia2.Main",
        "simplimixi_decode_template"
    )

    Test-NoSensitiveTerms -AssemblyPath $appAssembly -Terms $oldTemplateKeys
    Test-NoSensitiveTerms -AssemblyPath $backendAssembly -Terms $oldTemplateKeys
    Test-NoSensitiveTerms -AssemblyPath $backendAssembly -Terms $backendSensitiveTerms

    if (Test-Path $nativeLibrary)
    {
        Test-NoSensitiveTerms -AssemblyPath $nativeLibrary -Terms $oldTemplateKeys
    }

    $packageFiles = Get-ChildItem $PackagePath -Recurse -File -ErrorAction SilentlyContinue

    # 1. Scan for forbidden source/script files
    $forbiddenExtensions = @(".cs", ".csx", ".ps1", ".psm1", ".psd1", ".md", ".py", ".cmd", ".bat", ".sh")
    $forbiddenFiles = $packageFiles | Where-Object {
        $_.Extension.ToLowerInvariant() -in $forbiddenExtensions
    }
    if ($forbiddenFiles)
    {
        throw "Protected package contains forbidden source/script files: $($forbiddenFiles.FullName -join ', ')"
    }

    # 2. Fail on config-like files that are not explicitly required at runtime
    $unexpectedConfigs = $packageFiles | Where-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($PackagePath, $_.FullName)
        $normalizedPath = $relativePath.Replace('/', '\')
        $extension = $_.Extension.ToLowerInvariant()
        ($extension -in @(".json", ".config", ".ini", ".yaml", ".yml")) -and
        ($normalizedPath -notin $runtimeConfigAllowList)
    }
    if ($unexpectedConfigs)
    {
        throw "Protected package contains config files that are not runtime-approved: $($unexpectedConfigs.FullName -join ', ')"
    }

    # 3. Scan for raw unencrypted template files
    $templatePath = Join-Path $PackagePath "assets\Templates"
    if (Test-Path $templatePath)
    {
        $rawTemplates = Get-ChildItem $templatePath -Recurse -File -ErrorAction SilentlyContinue | Where-Object {
            $_.Extension.ToLowerInvariant() -in @(".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp")
        }
        if ($rawTemplates)
        {
            throw "Protected package contains unencrypted template images: $($rawTemplates.FullName -join ', ')"
        }
    }

    # 4. Scan for dev/test-only directories or artifacts accidentally copied into the package
    $devOnlyArtifacts = $packageFiles | Where-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($PackagePath, $_.FullName)
        $normalizedPath = $relativePath.Replace('/', '\')
        $normalizedPath -match $devOnlyPathPattern
    }
    if ($devOnlyArtifacts)
    {
        throw "Protected package contains development-only assets or directories: $($devOnlyArtifacts.FullName -join ', ')"
    }

    $devOnlyDirectories = Get-ChildItem $PackagePath -Recurse -Directory -ErrorAction SilentlyContinue | Where-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($PackagePath, $_.FullName)
        $normalizedPath = $relativePath.Replace('/', '\')
        $normalizedPath -match $devOnlyPathPattern
    }
    if ($devOnlyDirectories)
    {
        throw "Protected package contains development-only directories: $($devOnlyDirectories.FullName -join ', ')"
    }

    # 5. Scan for debug symbols or documentation
    $debugArtifacts = $packageFiles | Where-Object {
        $_.Extension.ToLowerInvariant() -in @(".pdb", ".xml")
    }
    if ($debugArtifacts)
    {
        throw "Protected package contains debug artifacts: $($debugArtifacts.FullName -join ', ')"
    }
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
    private static readonly byte[] Key = CreateKey();

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

    private static byte[] CreateKey()
    {
        byte[] seed =
        {
            0x31, 0xA4, 0x5C, 0x27, 0xE8, 0x09, 0xD3, 0x76,
            0x42, 0xBD, 0x18, 0xC1, 0x6F, 0x90, 0x2A, 0x55,
            0xCE, 0x03, 0xB7, 0x64, 0x1D, 0x88, 0xF2, 0x0B,
            0x79, 0xE1, 0x34, 0xAC, 0x5A, 0x17, 0xC9, 0x60
        };
        byte[] mask =
        {
            0x4F, 0x12, 0xE0, 0x99, 0x3B, 0xC6, 0x70, 0x2D,
            0x84, 0x5E, 0xA9, 0x01, 0xF3, 0x6C, 0x1A, 0xD5
        };

        byte[] key = new byte[24];
        for (int i = 0; i < key.Length; i++)
        {
            int mixed = seed[i] ^ mask[(i * 7 + 3) % mask.Length] ^ (i * 29 + 0x41);
            key[i] = (byte)((mixed << 3) | (mixed >> 5));
        }

        return key;
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
Remove-Item $publishDir, $protectedDir, $packageDir, $obfuscatedInputDir, $obfuscatedDepsDir, $obfuscatedDir, $setupPath -Recurse -Force -ErrorAction SilentlyContinue

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

Write-Host "Removing debug artifacts and unused dependencies..."
Get-ChildItem $publishDir -Recurse -Include *.pdb,*.xml,opencv_videoio_ffmpeg*.dll -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "Preparing Obfuscar input..."
New-Item -ItemType Directory -Path $obfuscatedInputDir -Force | Out-Null
New-Item -ItemType Directory -Path $obfuscatedDepsDir -Force | Out-Null
Copy-Item $backendBuildAssembly (Join-Path $obfuscatedInputDir "Simplimixi.Backend.dll") -Force
Copy-Item (Join-Path $publishDir "OpenCvSharp.dll") (Join-Path $obfuscatedDepsDir "OpenCvSharp.dll") -Force
Copy-Item (Join-Path $publishDir "SharpAdbClient.dll") (Join-Path $obfuscatedDepsDir "SharpAdbClient.dll") -Force

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
Copy-Item (Join-Path $obfuscatedDir "Simplimixi.Backend.dll") (Join-Path $protectedDir "Simplimixi.Backend.dll") -Force

Get-ChildItem $protectedDir -Recurse -Include *.pdb,*.xml -ErrorAction SilentlyContinue | Remove-Item -Force
Remove-Item (Join-Path $protectedDir "Backgrounds"), (Join-Path $protectedDir "AppIcon"), (Join-Path $protectedDir "Templates") -Recurse -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
Copy-Item (Join-Path $protectedDir "*") $packageDir -Recurse -Force

Write-Host "Encrypting template assets..."
Protect-TemplateAssets -TemplateRoot (Join-Path $packageDir "assets\Templates")

$readmePath = Join-Path $packageDir "README.txt"
@"
SimpliMixi v0.6.2

Run SimpliMixi.exe to start the app.

Package layout:
- adb/ contains the bundled Android Debug Bridge tools.
- assets/Templates/ contains encrypted .dat templates required by automation.
- redist runtime is installed by setup if Microsoft .NET 8 Desktop Runtime is missing.

Do not remove files or folders from this package.
"@ | Set-Content $readmePath -Encoding UTF8

Write-Host "Verifying protected package..."
Test-ProtectedPackage -PackagePath $packageDir

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




