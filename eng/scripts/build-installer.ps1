param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

# Native AOT release pipeline for the Avalonia frontend (src\frontend\Simplimixi.csproj).
# The whole app (frontend + backend) compiles to a single native SimpliMixi.exe — no IL,
# no Obfuscar, no .NET runtime redist. Requires the MSVC toolchain (link.exe) on PATH.

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$publishRoot = Join-Path $repoRoot "publish"
$publishDir = Join-Path $publishRoot $Runtime
$packageDir = Join-Path $publishRoot "SimpliMixi-v0.6.2"
$projectPath = Join-Path $repoRoot "src\frontend\Simplimixi.csproj"
$issPath = Join-Path $repoRoot "eng\installer\SimpliMixi.iss"
$setupPath = Join-Path $repoRoot "publish\SimpliMixi-v0.6.2-Setup.exe"

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

function Find-VcVars64
{
    # Prefer vswhere to locate the latest VS install with the C++ toolchain.
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere)
    {
        $vcvars = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
            -find "VC\Auxiliary\Build\vcvars64.bat" 2>$null | Select-Object -First 1
        if ($vcvars -and (Test-Path $vcvars))
        {
            return $vcvars
        }
    }

    # Common fallback locations.
    $candidates = @(
        "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat",
        "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
    )
    foreach ($candidate in $candidates)
    {
        if (Test-Path $candidate)
        {
            return $candidate
        }
    }

    return $null
}

function Import-vcVars64
{
    param([string]$VcVarsPath)

    # Run vcvars64.bat in a child cmd and capture the resulting environment block,
    # then apply it to this PowerShell session so the AOT compiler/linker can find MSVC.
    $envOutput = cmd /c "call `"$VcVarsPath`" >nul 2>&1 && set" 2>&1
    if ($LASTEXITCODE -ne 0)
    {
        throw "vcvars64.bat failed with exit code $LASTEXITCODE."
    }

    foreach ($line in $envOutput)
    {
        if ($line -match '^([^=]+)=(.*)$')
        {
            Set-Item -Path "env:$($matches[1])" -Value $matches[2]
        }
    }
}

function Sanitize-PathForNativeLink
{
    # Git ships a Unix hardlink tool at <Git>\usr\bin\link.exe that shadows MSVC's link.exe
    # and breaks the AOT LinkNative step. Strip Git's usr\bin (and the compat bin) from PATH
    # so the MSVC linker discovered via vcvars64 wins.
    $segments = $env:PATH -split ';'
    $filtered = $segments | Where-Object {
        $_ -and
        ($_ -notmatch '\\Git\\usr\\bin') -and
        ($_ -notmatch '\\Git\\mingw(\\|$)')
    }
    $env:PATH = $filtered -join ';'
}

function Test-NoSensitiveTerms
{
    param(
        [string]$AssemblyPath,
        [string[]]$Terms
    )

    if (-not (Test-Path $AssemblyPath))
    {
        throw "Protected file was not found at $AssemblyPath"
    }

    $assemblyInfo = Get-Item $AssemblyPath
    if ($assemblyInfo.Length -le 0)
    {
        throw "Protected file '$([System.IO.Path]::GetFileName($AssemblyPath))' is empty."
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

        if ($hits.Count -gt 0)
        {
            break
        }
    }

    if ($hits.Count -gt 0)
    {
        throw "Protected file '$([System.IO.Path]::GetFileName($AssemblyPath))' still exposes sensitive terms: $($hits -join ', ')"
    }
}

function Test-ProtectedPackage
{
    param([string]$PackagePath)

    # Native AOT links frontend + backend into a single SimpliMixi.exe, so both the
    # app-level and backend-level sensitive terms are scanned against that one binary.
    $appExe = Join-Path $PackagePath "SimpliMixi.exe"
    $nativeLibrary = Join-Path $PackagePath "simplimixi_native.dll"
    $oldTemplateKeys = @("SimpliMixi-Templates-051")
    $runtimeConfigAllowList = @(
        "Config\test_config.json",
        "security\integrity.manifest.json"
    )
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

    Test-NoSensitiveTerms -AssemblyPath $appExe -Terms $oldTemplateKeys
    Test-NoSensitiveTerms -AssemblyPath $appExe -Terms $backendSensitiveTerms

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
    $devOnlyPathPattern = '(^|\\)(tests?|samples?|fixtures?|debug|scripts?|tools?|devtools?|bench|diagnostics|\.git|\.vs|obj|TestResults)(\\|$)'
    $devOnlyArtifacts = $packageFiles | Where-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($PackagePath, $_.FullName)
        $normalizedPath = $relativePath.Replace('/', '\')
        $normalizedPath -match $devOnlyPathPattern
    }
    if ($devOnlyArtifacts)
    {
        throw "Protected package contains development-only assets or directories: $($devOnlyArtifacts.FullName -join ', ')"
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

# --- Pipeline start ---

Write-Host "Locating MSVC toolchain for Native AOT..."
$vcvars = Find-VcVars64
if (-not $vcvars)
{
    throw "vcvars64.bat was not found. Install Visual Studio 2022 (or Build Tools) with the 'Desktop development with C++' workload, then rerun this script."
}
Write-Host "Using vcvars64: $vcvars"
Import-vcVars64 -VcVarsPath $vcvars

# AOT must use the environmental (MSVC) linker; without this the publish reports
# "Platform linker not found" even when vcvars64 has been imported.
$env:IlcUseEnvironmentalTools = "true"
Sanitize-PathForNativeLink

if (-not (Get-Command link.exe -ErrorAction SilentlyContinue))
{
    throw "MSVC link.exe is not on PATH after importing vcvars64. Verify the C++ workload is installed."
}

Write-Host "Building native helper library (simplimixi_native.dll)..."
& (Join-Path $PSScriptRoot "build-native.ps1") -Runtime $Runtime -Configuration $Configuration
if ($LASTEXITCODE -ne 0)
{
    throw "build-native.ps1 failed with exit code $LASTEXITCODE."
}

Write-Host "Publishing $Configuration $Runtime (Native AOT)..."
Remove-Item $publishDir, $packageDir, $setupPath -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish $projectPath -c $Configuration -r $Runtime -o $publishDir
if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$nativeExe = Join-Path $publishDir "SimpliMixi.exe"
if (-not (Test-Path $nativeExe))
{
    throw "Native AOT publish did not produce SimpliMixi.exe in $publishDir"
}

Write-Host "Removing debug artifacts and unused dependencies..."
Get-ChildItem $publishDir -Recurse -Include *.pdb,*.xml,opencv_videoio_ffmpeg*.dll -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "Creating protected package..."
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
Copy-Item (Join-Path $publishDir "*") $packageDir -Recurse -Force

Write-Host "Encrypting template assets..."
Protect-TemplateAssets -TemplateRoot (Join-Path $packageDir "assets\Templates")

Write-Host "Writing integrity manifest..."
& (Join-Path $PSScriptRoot "write-integrity-manifest.ps1") -PackageRoot $packageDir
if ($LASTEXITCODE -ne 0)
{
    throw "write-integrity-manifest.ps1 failed with exit code $LASTEXITCODE."
}

$readmePath = Join-Path $packageDir "README.txt"
@"
SimpliMixi v0.6.2

Run SimpliMixi.exe to start the app. This is a self-contained native build;
no .NET runtime installation is required.

Package layout:
- SimpliMixi.exe is the native (Native AOT) application executable.
- adb/ contains the bundled Android Debug Bridge tools.
- assets/Templates/ contains encrypted .dat templates required by automation.
- security/integrity.manifest.json guards the executable, native helper and templates.

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

Write-Host "Protected native release ready: $setupPath"
Write-Host "Package folder: $packageDir"
Write-Host "Smoke test before release: install setup, launch app, verify encrypted template images, then test Start/Pause/Stop and ADB/BlueStacks flow."
