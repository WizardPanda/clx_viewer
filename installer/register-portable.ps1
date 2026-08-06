# Registers Clx Viewer shell integration for a portable install (no admin).
# Run from the folder that contains ClxViewer.exe and ClinxThumbnailProvider.dll.
# Uses direct HKCU registry writes (no regsvr32).
$ErrorActionPreference = "Stop"

$dir = $PSScriptRoot
$exe = Join-Path $dir "ClxViewer.exe"
$dll = Join-Path $dir "ClinxThumbnailProvider.dll"
if (-not (Test-Path -LiteralPath $exe)) { throw "ClxViewer.exe not found next to this script" }
if (-not (Test-Path -LiteralPath $dll)) { throw "ClinxThumbnailProvider.dll not found next to this script" }

$clsid = "{6CF20D3A-B6A8-4DCB-8305-973E1B65E7B6}"
$thumb = "{E357FCCD-A995-4576-B01F-234630154E96}"
$c = "HKCU:\Software\Classes"

# Thumbnail provider COM server
New-Item -Force -Path "$c\CLSID\$clsid\InprocServer32" | Out-Null
Set-ItemProperty -Path "$c\CLSID\$clsid\InprocServer32" -Name "(default)" -Value $dll
Set-ItemProperty -Path "$c\CLSID\$clsid\InprocServer32" -Name "ThreadingModel" -Value "Apartment"

# .clx shellex thumbnail handler (+ name alias)
New-Item -Force -Path "$c\.clx\shellex\$thumb" | Out-Null
Set-ItemProperty -Path "$c\.clx\shellex\$thumb" -Name "(default)" -Value $clsid
New-Item -Force -Path "$c\.clx\shellex\ThumbnailHandler" | Out-Null
Set-ItemProperty -Path "$c\.clx\shellex\ThumbnailHandler" -Name "(default)" -Value $clsid

# .clx association -> ClxViewer.Document
New-Item -Force -Path "$c\.clx" | Out-Null
Set-ItemProperty -Path "$c\.clx" -Name "(default)" -Value "ClxViewer.Document"
New-Item -Force -Path "$c\.clx\OpenWithProgids" | Out-Null
New-Item -Force -Path "$c\.clx\OpenWithProgids\ClxViewer.Document" | Out-Null
New-Item -Force -Path "$c\ClxViewer.Document" | Out-Null
Set-ItemProperty -Path "$c\ClxViewer.Document" -Name "(default)" -Value "Clinx CLX Capture"
New-Item -Force -Path "$c\ClxViewer.Document\shell\open\command" | Out-Null
Set-ItemProperty -Path "$c\ClxViewer.Document\shell\open\command" -Name "(default)" -Value "`"$exe`" `"%1`""

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ShellNotifyP {
  [DllImport("shell32.dll", EntryPoint = "SHChangeNotify")]
  public static extern void Notify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
'@
[ShellNotifyP]::Notify(0x8000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host "Registered Clx Viewer shell integration for $dir"
