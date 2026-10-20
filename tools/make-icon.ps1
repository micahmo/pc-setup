# Draws the PC Setup icon: a white laptop glyph on a blue rounded square, with a check badge at larger sizes.
# Regenerate it from the repo root:  .\tools\make-icon.ps1 -OutIco src\PCSetup\Assets\app.ico
param([string]$OutIco, [string]$PreviewPng)
# .NET file APIs don't follow PowerShell's current location, so resolve paths up front.
$OutIco = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutIco)
if ($PreviewPng) { $PreviewPng = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PreviewPng) }
Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded square
    $pad = [Math]::Max(1, [int]($size * 0.04))
    $r = [single]($size * 0.22)
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 103, 192))), $path)

    # Laptop glyph (Segoe Fluent Icons "System"), centered on its drawn outline rather than the font's line box
    $glyph = New-Object System.Drawing.Drawing2D.GraphicsPath
    $family = New-Object System.Drawing.FontFamily 'Segoe Fluent Icons'
    $glyph.AddString([string][char]0xE770, $family, 0, [single]($size * 0.56), (New-Object System.Drawing.PointF 0, 0), [System.Drawing.StringFormat]::GenericTypographic)
    $bounds = $glyph.GetBounds()
    $center = $size / 2.0
    $move = New-Object System.Drawing.Drawing2D.Matrix
    $move.Translate([single]($center - $bounds.X - $bounds.Width / 2), [single]($center - $bounds.Y - $bounds.Height / 2))
    $glyph.Transform($move)
    $g.FillPath([System.Drawing.Brushes]::White, $glyph)

    # Check badge, only where it's legible
    if ($size -ge 32) {
        # Sits fully inside the rounded square, clear of the corner curve.
        $b = [single]($size * 0.32)
        $inset = [single]($pad + $size * 0.07)
        $bx = $size - $b - $inset
        $by = $size - $b - $inset
        $ring = [single]([Math]::Max(1.5, $size * 0.04))
        $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 103, 192))), ($bx - $ring), ($by - $ring), ($b + 2 * $ring), ($b + 2 * $ring))
        $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 16, 185, 129))), $bx, $by, $b, $b)
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single]([Math]::Max(1.5, $size * 0.045)))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
        $pts = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF ($bx + $b * 0.27), ($by + $b * 0.52)),
            (New-Object System.Drawing.PointF ($bx + $b * 0.44), ($by + $b * 0.68)),
            (New-Object System.Drawing.PointF ($bx + $b * 0.74), ($by + $b * 0.35)))
        $g.DrawLines($pen, $pts)
    }

    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256 -and $PreviewPng) { $bmp.Save($PreviewPng, [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    [pscustomobject]@{ Size = $s; Bytes = $ms.ToArray() }
}

# ICO container with PNG-compressed entries
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$img.Bytes.Length); $w.Write([uint32]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $w.Write($img.Bytes) }
$w.Flush()
[System.IO.File]::WriteAllBytes($OutIco, $out.ToArray())
"Wrote $OutIco ($($out.Length) bytes, sizes: $($sizes -join ', '))"
