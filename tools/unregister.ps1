# Removes the Clx Viewer shell integration (thumbnail handler + file association).
param(
    [string]$Dll = (Join-Path (Split-Path $PSScriptRoot -Parent) "build-native\bin\ClinxThumbnailProvider.dll")
)

$ErrorActionPreference = "Stop"

# Thumbnail handler CLSID + shellex keys
if (Test-Path -LiteralPath $Dll) {
    Start-Process -FilePath regsvr32.exe -ArgumentList '/u', '/s', "`"$Dll`"" -Wait
}
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue `
    "HKCU:\Software\Classes\CLSID\{6CF20D3A-B6A8-4DCB-8305-973E1B65E7B6}"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue `
    "HKCU:\Software\Classes\.clx\shellex\{E357FCCD-A995-4576-B01F-234630154E96}"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue `
    "HKCU:\Software\Classes\.clx\shellex\ThumbnailHandler"

# File association
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "HKCU:\Software\Classes\ClxViewer.Document"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "HKCU:\Software\Classes\ClxViewer.Document\DefaultIcon"
if (Test-Path "HKCU:\Software\Classes\.clx") {
    Remove-ItemProperty -Path "HKCU:\Software\Classes\.clx" -Name "TypeOverlay" -ErrorAction SilentlyContinue
}
if (Test-Path "HKCU:\Software\Classes\.clx") {
    $default = (Get-ItemProperty "HKCU:\Software\Classes\.clx" -ErrorAction SilentlyContinue).'(default)'
    if ($default -eq "ClxViewer.Document") {
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "HKCU:\Software\Classes\.clx"
    } else {
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "HKCU:\Software\Classes\.clx\OpenWithProgids\ClxViewer.Document"
    }
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ShellNotify2 {
  [DllImport("shell32.dll", EntryPoint = "SHChangeNotify")]
  public static extern void Notify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
'@
[ShellNotify2]::Notify(0x8000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host "Unregistered."
