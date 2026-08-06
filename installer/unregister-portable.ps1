# Removes the Clx Viewer shell integration registered for a portable install.
$clsid = "{6CF20D3A-B6A8-4DCB-8305-973E1B65E7B6}"
$thumb = "{E357FCCD-A995-4576-B01F-234630154E96}"
$c = "HKCU:\Software\Classes"

Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "$c\CLSID\$clsid"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "$c\.clx\shellex\$thumb"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "$c\.clx\shellex\ThumbnailHandler"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "$c\ClxViewer.Document"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "$c\.clx\OpenWithProgids\ClxViewer.Document"
if (Test-Path "$c\.clx") {
    $default = (Get-ItemProperty "$c\.clx" -ErrorAction SilentlyContinue).'(default)'
    if ($default -eq "ClxViewer.Document") {
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "$c\.clx"
    }
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ShellNotifyU {
  [DllImport("shell32.dll", EntryPoint = "SHChangeNotify")]
  public static extern void Notify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
'@
[ShellNotifyU]::Notify(0x8000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host "Removed Clx Viewer shell integration."
