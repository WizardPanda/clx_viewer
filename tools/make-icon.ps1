# Generates assets/clxviewer.ico — the Clx brand icon used by the viewer and
# as the .clx file-type icon. Draws a rounded rectangle (#012c5d) with white
# "Clx" text, matching the Explorer thumbnail badge.
param(
    [string]$OutPath = (Join-Path $PSScriptRoot "..\assets\clxviewer.ico")
)

Add-Type -AssemblyName System.Drawing

function New-ClxIcon {
    param([int]$Size)
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $margin = $Size * 0.06
    $w = $Size - 2 * $margin
    $h = $Size * 0.72
    $y = ($Size - $h) / 2
    $radius = $h * 0.30

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $radius
    $path.AddArc($margin, $y, $d, $d, 180, 90)
    $path.AddArc($margin + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($margin + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($margin, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0x01, 0x2C, 0x5D))
    $g.FillPath($brush, $path)

    $fontSize = [single]($h * 0.52)
    $font = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF($margin, $y, $w, $h)
    $g.DrawString("Clx", $font, $white, $rect, $fmt)

    $font.Dispose(); $white.Dispose(); $fmt.Dispose(); $brush.Dispose()
    $path.Dispose(); $g.Dispose()
    return $bmp
}

# No 16px frame on purpose: a native 16px render (tiny "Clx" text) looks
# blurry. Omitting it makes Windows downscale the 32px frame for small usages,
# which is noticeably crisper.
$sizes = 32, 48, 64, 128, 256
$pngs = @()
$total = 0
foreach ($s in $sizes) {
    $bmp = New-ClxIcon -Size $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose(); $bmp.Dispose()
    $pngs += , @($s, $bytes)
    $total += $bytes.Length
}

# ICO header + directory entries
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([UInt16]0)             # reserved
$bw.Write([UInt16]1)             # type: icon
$bw.Write([UInt16]$pngs.Count)   # count

$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $s = $p[0]; $bytes = $p[1]
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))  # width
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))  # height
    $bw.Write([Byte]0)  # color count
    $bw.Write([Byte]0)  # reserved
    $bw.Write([UInt16]1)  # planes
    $bw.Write([UInt16]32) # bpp
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($p in $pngs) {
    $bw.Write($p[1])
}
$bw.Flush()

$dir = Split-Path -Parent $OutPath
if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
[System.IO.File]::WriteAllBytes($OutPath, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()
Write-Output "wrote $OutPath ($((Get-Item $OutPath).Length) bytes)"

# The viewer embeds the icon, so also publish a copy into the WPF project.
$viewerAsset = Join-Path $PSScriptRoot "..\viewer\Assets\clxviewer.ico"
$vd = Split-Path -Parent $viewerAsset
if (-not (Test-Path -LiteralPath $vd)) { New-Item -ItemType Directory -Path $vd | Out-Null }
Copy-Item -Force $OutPath $viewerAsset
Write-Output "wrote $viewerAsset"
