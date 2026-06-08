param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$sourcePath = Join-Path $repoRoot "src\Simplimixi\Native\simplimixi_native.c"
if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = Join-Path $repoRoot "src\Simplimixi\Native\bin\$Runtime\$Configuration"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputPath = Join-Path $OutputDirectory "simplimixi_native.dll"

$cl = Get-Command cl.exe -ErrorAction SilentlyContinue
if ($cl)
{
    & $cl.Source /nologo /LD /O2 /Fe:$outputPath $sourcePath
    if ($LASTEXITCODE -ne 0)
    {
        throw "cl.exe failed with exit code $LASTEXITCODE."
    }

    Remove-Item (Join-Path $OutputDirectory "simplimixi_native.obj"), (Join-Path $OutputDirectory "simplimixi_native.lib"), (Join-Path $OutputDirectory "simplimixi_native.exp") -Force -ErrorAction SilentlyContinue
    Write-Host "Built native library: $outputPath"
    exit 0
}

$msvcRoot = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC"
$windowsSdkRoot = "C:\Program Files (x86)\Windows Kits\10"
if ((Test-Path $msvcRoot) -and (Test-Path $windowsSdkRoot))
{
    $msvcVersion = Get-ChildItem $msvcRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
    $sdkVersion = Get-ChildItem (Join-Path $windowsSdkRoot "Include") -Directory | Sort-Object Name -Descending | Select-Object -First 1
    if ($msvcVersion -and $sdkVersion)
    {
        $clPath = Join-Path $msvcVersion.FullName "bin\Hostx64\x64\cl.exe"
        if (Test-Path $clPath)
        {
            $oldPath = $env:PATH
            $oldInclude = $env:INCLUDE
            $oldLib = $env:LIB
            try
            {
                $msvcBin = Join-Path $msvcVersion.FullName "bin\Hostx64\x64"
                $sdkBin = Join-Path $windowsSdkRoot "bin\$($sdkVersion.Name)\x64"
                $msvcInclude = Join-Path $msvcVersion.FullName "include"
                $sdkUcrtInclude = Join-Path $windowsSdkRoot "Include\$($sdkVersion.Name)\ucrt"
                $sdkUmInclude = Join-Path $windowsSdkRoot "Include\$($sdkVersion.Name)\um"
                $sdkSharedInclude = Join-Path $windowsSdkRoot "Include\$($sdkVersion.Name)\shared"
                $msvcLib = Join-Path $msvcVersion.FullName "lib\x64"
                $sdkUcrtLib = Join-Path $windowsSdkRoot "Lib\$($sdkVersion.Name)\ucrt\x64"
                $sdkUmLib = Join-Path $windowsSdkRoot "Lib\$($sdkVersion.Name)\um\x64"

                $env:PATH = "$msvcBin;$sdkBin;$env:PATH"
                $env:INCLUDE = "$msvcInclude;$sdkUcrtInclude;$sdkUmInclude;$sdkSharedInclude"
                $env:LIB = "$msvcLib;$sdkUcrtLib;$sdkUmLib"

                & $clPath /nologo /LD /O2 /Fe:$outputPath $sourcePath
                if ($LASTEXITCODE -ne 0)
                {
                    throw "cl.exe failed with exit code $LASTEXITCODE."
                }

                Remove-Item (Join-Path $OutputDirectory "simplimixi_native.obj"), (Join-Path $OutputDirectory "simplimixi_native.lib"), (Join-Path $OutputDirectory "simplimixi_native.exp") -Force -ErrorAction SilentlyContinue
                Write-Host "Built native library: $outputPath"
                exit 0
            } finally
            {
                $env:PATH = $oldPath
                $env:INCLUDE = $oldInclude
                $env:LIB = $oldLib
            }
        }
    }
}

$vsDevCmd = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat"
if (Test-Path $vsDevCmd)
{
    $batchPath = Join-Path $OutputDirectory "build-native.cmd"
    @"
@echo off
call "$vsDevCmd" -arch=amd64 -host_arch=amd64
cl.exe /nologo /LD /O2 /Fe:"$outputPath" "$sourcePath"
"@ | Set-Content $batchPath -Encoding ASCII

    & cmd.exe /c "`"$batchPath`""
    if ($LASTEXITCODE -ne 0)
    {
        throw "cl.exe via VsDevCmd failed with exit code $LASTEXITCODE."
    }

    Remove-Item (Join-Path $OutputDirectory "build-native.cmd"), (Join-Path $OutputDirectory "simplimixi_native.obj"), (Join-Path $OutputDirectory "simplimixi_native.lib"), (Join-Path $OutputDirectory "simplimixi_native.exp") -Force -ErrorAction SilentlyContinue
    Write-Host "Built native library: $outputPath"
    exit 0
}

$clang = Get-Command clang -ErrorAction SilentlyContinue
if ($clang)
{
    & $clang.Source -shared -O2 -o $outputPath $sourcePath
    if ($LASTEXITCODE -ne 0)
    {
        throw "clang failed with exit code $LASTEXITCODE."
    }

    Write-Host "Built native library: $outputPath"
    exit 0
}

throw "No supported C compiler found. Install Visual Studio Build Tools with C++ tools or LLVM clang, then rerun this script."
