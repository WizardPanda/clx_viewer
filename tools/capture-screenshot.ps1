# Captures ONLY the Clx Viewer window (never surrounding windows) using
# PrintWindow, so no other app's content can ever leak into the shot.
# Uses --clean so the sidebar/metadata panels (which show the file's folder
# path) are hidden — the capture is safe to publish.
param(
    [string]$Exe = (Join-Path $PSScriptRoot "..\viewer\bin\x64\Release\net8.0-windows\ClxViewer.exe"),
    [string]$File = "",
    [string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) "docs\viewer-screenshot.png"),
    [int]$WaitMs = 6000
)

Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent

# If no file given, use a neutral demo (a submodule test sample copied to a
# generic name in %TEMP%) so no real lab path/name appears in the shot.
if ([string]::IsNullOrWhiteSpace($File)) {
    $sample = Get-ChildItem (Join-Path $root "third_party\clinx_format_cpp\tests\data") -Filter *.clx |
        Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $sample) { throw "no sample .clx in the clinx_format_cpp test data" }
    $demoDir = Join-Path $env:TEMP "clx-demo"
    New-Item -ItemType Directory -Force -Path $demoDir | Out-Null
    $File = Join-Path $demoDir "Demo_Western_Blot.clx"
    Copy-Item -Force $sample $File
}

$src = @'
using System;
using System.Runtime.InteropServices;
public static class WC {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
}
'@
Add-Type -TypeDefinition $src

$p = Start-Process -FilePath $Exe -ArgumentList "--clean", "`"$File`"" -PassThru
Start-Sleep -Milliseconds $WaitMs
if ($p.HasExited) { "PROCESS EXITED, code=$($p.ExitCode)"; exit 1 }

$target = [IntPtr]::Zero
$pidT = [uint32]$p.Id
[WC]::EnumWindows({ param($h,$l)
  $wp = 0
  [void][WC]::GetWindowThreadProcessId($h,[ref]$wp)
  if ($wp -eq $pidT -and [WC]::IsWindowVisible($h)) { $script:target = $h; return $false }
  return $true
}, [IntPtr]::Zero) | Out-Null

if ($target -eq [IntPtr]::Zero) { "no window found"; Stop-Process -Id $p.Id -Force; exit 1 }

[void][WC]::SetForegroundWindow($target)
[void][WC]::BringWindowToTop($target)
Start-Sleep -Milliseconds 1500

$r = New-Object WC+RECT
[void][WC]::GetWindowRect($target,[ref]$r)
$w = $r.R-$r.L; $h = $r.B-$r.T

$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [WC]::PrintWindow($target, $hdc, 2)   # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc)
$g.Dispose()

if (-not $ok) { "PrintWindow failed"; Stop-Process -Id $p.Id -Force; exit 1 }

$dir = Split-Path -Parent $Out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
"captured window-only $($w)x$($h) -> $Out"
