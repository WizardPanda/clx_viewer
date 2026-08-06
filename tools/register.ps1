# Registers the Clx Viewer shell integration for the current user (no admin):
#   * the .clx Explorer thumbnail provider (via the extension's DllRegisterServer)
#   * .clx default file association -> opens the viewer
#   * the .clx file-type icon (uses the viewer exe's embedded icon)
param(
    [string]$Dll = (Join-Path (Split-Path $PSScriptRoot -Parent) "build-native\bin\ClinxThumbnailProvider.dll"),
    [string]$ViewerExe = $null
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

if (-not $ViewerExe) {
    $ViewerExe = Get-ChildItem -Path (Join-Path $root "viewer\bin") -Recurse -Filter ClxViewer.exe -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Dll -or -not (Test-Path -LiteralPath $Dll)) { throw "thumbnail DLL not found: $Dll" }
if (-not $ViewerExe) { throw "ClxViewer.exe not found (run build.ps1 first)" }

Write-Host "Thumbnail DLL : $Dll"
Write-Host "Viewer        : $ViewerExe"

# 1. Thumbnail handler (writes HKCU CLSID + .clx shellex keys itself)
Start-Process -FilePath regsvr32.exe -ArgumentList '/s', "`"$Dll`"" -Wait

# 2. Default file association -> ClxViewer.Document
$classes = "HKCU:\Software\Classes"
New-Item -Force -Path "$classes\.clx" | Out-Null
Set-ItemProperty -Path "$classes\.clx" -Name "(default)" -Value "ClxViewer.Document"

# 2. Default file association -> ClxViewer.Document
$classes = "HKCU:\Software\Classes"
New-Item -Force -Path "$classes\.clx" | Out-Null
Set-ItemProperty -Path "$classes\.clx" -Name "(default)" -Value "ClxViewer.Document"

New-Item -Force -Path "$classes\.clx\OpenWithProgids" | Out-Null
New-Item -Force -Path "$classes\.clx\OpenWithProgids\ClxViewer.Document" | Out-Null

New-Item -Force -Path "$classes\ClxViewer.Document" | Out-Null
Set-ItemProperty -Path "$classes\ClxViewer.Document" -Name "(default)" -Value "Clinx CLX Capture"
# No DefaultIcon is registered; the thumbnail provider draws no badge of its
# own, so .clx behaves like a normal file type and Explorer applies its usual
# thumbnail icon overlay according to the global "Display file icon on
# thumbnails" folder option.
New-Item -Force -Path "$classes\ClxViewer.Document\shell\open\command" | Out-Null
Set-ItemProperty -Path "$classes\ClxViewer.Document\shell\open\command" -Name "(default)" -Value "`"$ViewerExe`" `"%1`""

# 3. Tell Explorer to re-read associations
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ShellNotify {
  [DllImport("shell32.dll", EntryPoint = "SHChangeNotify")]
  public static extern void Notify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
'@
[ShellNotify]::Notify(0x8000, 0, [IntPtr]::Zero, [IntPtr]::Zero)  # SHCNE_ASSOCCHANGED

Write-Host ""
Write-Host "Registered. Explorer may need a thumbnail-cache reset to show fresh "
Write-Host ".clx previews: kill explorer, delete %LocalAppData%\Microsoft\Windows\"
Write-Host "Explorer\thumbcache_*.db, then restart explorer."
