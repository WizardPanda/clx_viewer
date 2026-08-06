# Captures ONLY the Clx Viewer window (never surrounding windows) using
# PrintWindow, so no other app's content can ever leak into the shot.
param(
    [string]$Exe = "ClxViewer.exe",
    [string]$File = "Sample.clx",
    [string]$Out = "docs/viewer-screenshot.png",
    [int]$WaitMs = 6000
)

Add-Type -AssemblyName System.Drawing

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

$p = Start-Process -FilePath $Exe -ArgumentList "`"$File`"" -PassThru
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

# Bring to front so the window renders normally, then let it settle.
[void][WC]::SetForegroundWindow($target)
[void][WC]::BringWindowToTop($target)
Start-Sleep -Milliseconds 1500

$r = New-Object WC+RECT
[void][WC]::GetWindowRect($target,[ref]$r)
$w = $r.R-$r.L; $h = $r.B-$r.T

# PrintWindow renders the window's own content into the HDC, ignoring anything
# that overlaps it on screen.
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
