# Builds the whole Clx Viewer: native clxreader.dll + ClinxThumbnailProvider.dll,
# then the WPF viewer, and stages the native reader next to the exe.
# Requires VS 2022 Build Tools (MSVC) — runs through vcvars64 like clxcpp.
param(
    [switch]$Register,
    [switch]$SkipNative,
    [string]$BuildDir = "build-native",
    [string]$Config = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$vcvars = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path -LiteralPath $vcvars)) {
    $vcvars = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
}
if (-not (Test-Path -LiteralPath $vcvars)) {
    throw "vcvars64.bat not found"
}

# 1. Native build (clxreader + thumbnail provider, linked to clxcpp).
#    Static CRT (/MT) so the DLLs need no VC++ redistributable at runtime.
if (-not $SkipNative) {
    Push-Location $root
    try {
        cmd /c "call `"$vcvars`" >nul 2>&1 && cmake -S . -B $BuildDir -G Ninja -DCMAKE_BUILD_TYPE=$Config -DCLXCPP_BUILD_TESTS=OFF -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded"
        if ($LASTEXITCODE -ne 0) { throw "cmake configure failed" }
        cmd /c "call `"$vcvars`" >nul 2>&1 && cmake --build $BuildDir"
        if ($LASTEXITCODE -ne 0) { throw "cmake build failed" }
    } finally {
        Pop-Location
    }
}

# 2. App / file icon
& (Join-Path $PSScriptRoot "make-icon.ps1") -OutPath (Join-Path $root "assets\clxviewer.ico")

# 3. WPF viewer
dotnet build (Join-Path $root "viewer\ClxViewer.csproj") -c $Config -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

# 4. Stage clxreader.dll next to the exe
$exeDir = Get-ChildItem -Path (Join-Path $root "viewer\bin") -Recurse -Filter ClxViewer.exe -ErrorAction SilentlyContinue |
          Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty DirectoryName
if (-not $exeDir) { throw "could not locate viewer output" }

$readerSrc = Join-Path $root "$BuildDir\bin\clxreader.dll"
if (-not (Test-Path -LiteralPath $readerSrc)) { $readerSrc = Get-ChildItem -Path $root -Recurse -Filter clxreader.dll -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName }
if (-not $readerSrc) { throw "clxreader.dll not found" }

Copy-Item -Force $readerSrc (Join-Path $exeDir "clxreader.dll")
Write-Output "staged clxreader.dll -> $exeDir"

Write-Output ""
Write-Output "Build OK."
Write-Output "  Viewer : $exeDir\ClxViewer.exe"
Write-Output "  Shell  : $root\$BuildDir\bin\ClinxThumbnailProvider.dll"
if ($Register) {
    & (Join-Path $PSScriptRoot "register.ps1")
}
