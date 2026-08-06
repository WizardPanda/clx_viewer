# Builds the Clx Viewer release artifacts (installers + portable zips).
# Publishes framework-dependent and self-contained variants, stages the native
# DLLs, compresses the portable zips, and compiles both Inno Setup installers.
#
# Usage:
#   .\installer\prepare.ps1 -AppVersion 1.0.0            (all artifacts)
#   .\installer\prepare.ps1 -AppVersion 1.0.0 -SkipPortable
#   .\installer\prepare.ps1 -AppVersion 1.0.0 -SkipInstallers
param(
    [string]$AppVersion = "0.1.0",
    [string]$Config = "Release",
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\dist"),
    [string]$NativeBin = (Join-Path $PSScriptRoot "..\build-native\bin"),
    [switch]$SkipPortable,
    [switch]$SkipInstallers,
    [switch]$SkipNative   # use already-built native DLLs
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $root "viewer\ClxViewer.csproj"
$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { $iscc = "C:\Program Files\Inno Setup 6\ISCC.exe" }
if (-not $SkipInstallers -and -not (Test-Path $iscc)) { throw "ISCC.exe not found (install Inno Setup 6)" }

# 0. Native DLLs (rebuilt unless -SkipNative)
if (-not $SkipNative) {
    & (Join-Path $root "tools\build.ps1")
}
$clxreader = Join-Path $NativeBin "clxreader.dll"
$thumbDll  = Join-Path $NativeBin "ClinxThumbnailProvider.dll"
if (-not (Test-Path $clxreader) -or -not (Test-Path $thumbDll)) { throw "native DLLs not found in $NativeBin" }

$staging = Join-Path $root "dist\staging"
$dist    = $OutputDir
New-Item -ItemType Directory -Force -Path $dist | Out-Null

foreach ($variant in @("fd", "sc")) {
    $selfContained = $variant -eq "sc"
    $stageDir = Join-Path $staging $variant

    # 1. Publish
    Remove-Item -Recurse -Force $stageDir -ErrorAction SilentlyContinue
    dotnet publish $csproj -c $Config -r win-x64 --self-contained $selfContained -o $stageDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($variant) failed" }

    # 2. Stage native DLLs + portable registration scripts
    Copy-Item -Force $clxreader $stageDir
    Copy-Item -Force $thumbDll $stageDir
    Copy-Item -Force (Join-Path $PSScriptRoot "register-portable.ps1") $stageDir
    Copy-Item -Force (Join-Path $PSScriptRoot "unregister-portable.ps1") $stageDir

    # 3. Portable zip
    if (-not $SkipPortable) {
        $zip = Join-Path $dist "ClxViewer-$AppVersion-win-x64-$variant-portable.zip"
        Remove-Item -Force $zip -ErrorAction SilentlyContinue
        Compress-Archive -Path "$stageDir\*" -DestinationPath $zip -CompressionLevel Optimal
        Write-Output "portable: $zip"
    }

    # 4. Installer
    if (-not $SkipInstallers) {
        & $iscc (Join-Path $PSScriptRoot "clx-viewer.iss") `
            /DAppVersion=$AppVersion /DVariant=$variant `
            /DSourceDir=$stageDir /DOutputDir=$dist | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "iscc ($variant) failed" }
    }
}

Write-Output "Release artifacts in $dist"
Get-ChildItem $dist -Filter "ClxViewer-$AppVersion*" | Select-Object -ExpandProperty Name
