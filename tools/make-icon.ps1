# Generates app.ico: a chunky dark-blue speaker on the app's dark background, in several sizes.
# Run:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 [-Preview preview.png]
param(
    [string]$Out = (Join-Path $PSScriptRoot '..\app.ico'),
    [string]$Preview = ''
)

Add-Type -AssemblyName System.Drawing
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

$bgColor = [System.Drawing.Color]::FromArgb(255, 0x15, 0x16, 0x1A)   # app background
$accent  = [System.Drawing.Color]::FromArgb(255, 0x68, 0x82, 0xD6)   # app accent (periwinkle)

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($s / 256.0, $s / 256.0)   # draw in a 256x256 design space

    # Dark rounded-square background (Windows doesn't round icons itself)
    $m = 4; $w = 256 - 2 * $m; $d = 64   # radius = 1/8 of the size, Windows 11's corner rounding (4 px at 32 px)
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bg.AddArc($m, $m, $d, $d, 180, 90)
    $bg.AddArc($m + $w - $d, $m, $d, $d, 270, 90)
    $bg.AddArc($m + $w - $d, $m + $w - $d, $d, $d, 0, 90)
    $bg.AddArc($m, $m + $w - $d, $d, $d, 90, 90)
    $bg.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush($bgColor)), $bg)

    # Speaker, flat accent color, scaled down around the centre so it sits comfortably inside the circle
    $small = $s -lt 32
    $k = if ($small) { 0.8 } else { 0.66 }
    $g.TranslateTransform(128, 128)
    $g.ScaleTransform($k, $k)
    $g.TranslateTransform($(if ($small) { -120 } else { -133 }), -128)
    $brush = New-Object System.Drawing.SolidBrush($accent)

    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(52, 100)), (New-Object System.Drawing.PointF(86, 100)),
        (New-Object System.Drawing.PointF(130, 62)), (New-Object System.Drawing.PointF(130, 194)),
        (New-Object System.Drawing.PointF(86, 156)), (New-Object System.Drawing.PointF(52, 156))))
    $bodyPen = New-Object System.Drawing.Pen($brush, $(if ($small) { 26 } else { 28 }))
    $bodyPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.FillPath($brush, $body)
    $g.DrawPath($bodyPen, $body)

    $wavePen = New-Object System.Drawing.Pen($brush, $(if ($small) { 30 } else { 28 }))
    $wavePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $wavePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $radii = if ($small) { @(56) } else { @(46, 90) }
    foreach ($r in $radii) { $g.DrawArc($wavePen, 130 - $r, 128 - $r, 2 * $r, 2 * $r, -50, 100) }

    $g.Dispose()
    return $bmp
}

if ($Preview) {
    # Strip of all sizes on the app background, for eyeballing
    $strip = New-Object System.Drawing.Bitmap(560, 280)
    $sg = [System.Drawing.Graphics]::FromImage($strip)
    $sg.Clear([System.Drawing.Color]::FromArgb(255, 32, 32, 32))
    $x = 8
    foreach ($s in 16, 24, 32, 48, 64, 256) { $f = New-Frame $s; $sg.DrawImage($f, $x, 8, $s, $s); $x += $s + 12; $f.Dispose() }
    $sg.Dispose(); $strip.Save($Preview); $strip.Dispose()
    Write-Host "Wrote $Preview"
}

$frames = @()
foreach ($s in $sizes) {
    $bmp = New-Frame $s
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # Classic 32-bit DIB frame (BITMAPINFOHEADER + bottom-up BGRA + empty AND mask)
        $bw = New-Object System.IO.BinaryWriter($ms)
        $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
        $bw.Write([int]40); $bw.Write([int]$s); $bw.Write([int]($s * 2))
        $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
        $bw.Write([int]($s * $s * 4 + $maskStride * $s)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
        $data = $bmp.LockBits((New-Object System.Drawing.Rectangle(0, 0, $s, $s)),
            [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $row = New-Object byte[] ($s * 4)
        for ($y = $s - 1; $y -ge 0; $y--) {
            [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $s * 4)
            $bw.Write($row)
        }
        $bmp.UnlockBits($data)
        $bw.Write((New-Object byte[] ($maskStride * $s)))
        $bw.Flush()
    }
    $frames += , @($s, $ms.ToArray())
    $bmp.Dispose()
}

$fs = [System.IO.File]::Create([System.IO.Path]::GetFullPath($Out))
$w = New-Object System.IO.BinaryWriter($fs)
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f[0] -ge 256) { 0 } else { $f[0] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$f[1].Length); $w.Write([int]$offset)
    $offset += $f[1].Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
$w.Close()
Write-Host "Wrote $Out"
