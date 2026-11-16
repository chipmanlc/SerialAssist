param([string]$Output = "SerialAssist.UI\app.ico", [string]$Preview = "")
Add-Type -AssemblyName System.Drawing

function New-LogoBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [single]($size * 0.035)
    $rect = New-Object System.Drawing.RectangleF($pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad))
    $radius = [single]($size * 0.23)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 18, 28, 43), [System.Drawing.Color]::FromArgb(255, 5, 10, 18), 45)
    $g.FillPath($bg, $path)
    $border = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(90, 45, 212, 191), [single]([Math]::Max(1, $size * 0.012)))
    $g.DrawPath($border, $path)

    $cx = [single]($size * 0.47); $cy = [single]($size * 0.50)
    $ringRadius = [single]($size * 0.255)
    $ringRect = New-Object System.Drawing.RectangleF(($cx - $ringRadius), ($cy - $ringRadius), (2 * $ringRadius), (2 * $ringRadius))
    $ringBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($ringRect, [System.Drawing.Color]::FromArgb(255, 45, 212, 191), [System.Drawing.Color]::FromArgb(255, 56, 189, 248), 20)
    $ringPen = New-Object System.Drawing.Pen($ringBrush, [single]($size * 0.105))
    $ringPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $ringPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($ringPen, $ringRect, 42, 276)

    $bolt = New-Object System.Drawing.Drawing2D.GraphicsPath
    $points = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([single]($size * 0.53), [single]($size * 0.265))),
        (New-Object System.Drawing.PointF([single]($size * 0.36), [single]($size * 0.535))),
        (New-Object System.Drawing.PointF([single]($size * 0.49), [single]($size * 0.535))),
        (New-Object System.Drawing.PointF([single]($size * 0.42), [single]($size * 0.745))),
        (New-Object System.Drawing.PointF([single]($size * 0.65), [single]($size * 0.455))),
        (New-Object System.Drawing.PointF([single]($size * 0.52), [single]($size * 0.455))),
        (New-Object System.Drawing.PointF([single]($size * 0.61), [single]($size * 0.265)))
    )
    $bolt.AddPolygon($points)
    $boltBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 248, 250, 252))
    $g.FillPath($boltBrush, $bolt)

    if ($size -ge 64) {
        $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(230, 45, 212, 191))
        $dot = [single]($size * 0.035)
        foreach ($yy in @(0.39, 0.50, 0.61)) {
            $g.FillEllipse($dotBrush, [single]($size * 0.795), [single]($size * $yy - $dot / 2), $dot, $dot)
        }
        $dotBrush.Dispose()
    }

    $boltBrush.Dispose(); $bolt.Dispose(); $ringPen.Dispose(); $ringBrush.Dispose(); $border.Dispose(); $bg.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$bitmaps = @()
foreach ($s in $sizes) { $bitmaps += New-LogoBitmap $s }

if ($Preview) { $bitmaps[-1].Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png) }

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $row = New-Object byte[] $data.Stride
    $pixels = New-Object byte[] ($size * $size * 4)
    for ($y = 0; $y -lt $size; $y++) {
        [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $data.Stride)
        $dstRow = $size - 1 - $y
        for ($x = 0; $x -lt $size; $x++) {
            $src = $x * 4
            $dst = ($dstRow * $size + $x) * 4
            $pixels[$dst] = $row[$src]
            $pixels[$dst + 1] = $row[$src + 1]
            $pixels[$dst + 2] = $row[$src + 2]
            $pixels[$dst + 3] = $row[$src + 3]
        }
    }
    $bmp.UnlockBits($data)

    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
    $mask = New-Object byte[] ($maskStride * $size)
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([UInt32]40)
    $bw.Write([Int32]$size)
    $bw.Write([Int32]($size * 2))
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]0)
    $bw.Write([UInt32]($pixels.Length + $mask.Length))
    $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    $bw.Write($pixels)
    $bw.Write($mask)
    $bw.Dispose()
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return ,$bytes
}

$blobs = @()
foreach ($b in $bitmaps) { $blobs += ,(Get-DibBytes $b) }

$fs = [System.IO.File]::Create($Output)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([byte]$(if ($s -eq 256) { 0 } else { $s }))
    $bw.Write([byte]$(if ($s -eq 256) { 0 } else { $s }))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$blobs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $blobs[$i].Length
}
foreach ($blob in $blobs) { $bw.Write($blob) }
$bw.Dispose(); $fs.Dispose()
foreach ($b in $bitmaps) { $b.Dispose() }
Write-Output "icon written: $Output"