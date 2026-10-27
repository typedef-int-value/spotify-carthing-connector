# Regenerate the original device silhouette using Windows System.Drawing.
# No downloaded artwork or external image packages are required.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = Join-Path $PSScriptRoot '../windows-probe/Assets/carthing.ico'
$null = New-Item -ItemType Directory -Path (Split-Path $destination) -Force

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$radius) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $d = $radius * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $body = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#19242E'))
    $screen = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#46D5C3'))
    $dial = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#F0F5FA'))
    $rim = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#C4D0DB'), 2)
    $bodyPath = New-RoundedPath 3 13 58 40 6
    $screenPath = New-RoundedPath 8 19 31 27 2
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($size / 64.0, $size / 64.0)
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.FillPath($body, $bodyPath)
        $graphics.DrawPath($rim, $bodyPath)
        $graphics.FillPath($screen, $screenPath)
        $graphics.FillEllipse($dial, 41, 20, 17, 17)
        $graphics.FillEllipse($dial, 47, 41, 6, 6)
        $points = [Drawing.PointF[]]@([Drawing.PointF]::new(19, 25), [Drawing.PointF]::new(19, 40), [Drawing.PointF]::new(30, 32.5))
        $graphics.FillPolygon($body, $points)
        $memory = [IO.MemoryStream]::new()
        try {
            $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
            $frames += ,$memory.ToArray()
        }
        finally { $memory.Dispose() }
    }
    finally {
        $bodyPath.Dispose(); $screenPath.Dispose(); $rim.Dispose()
        $body.Dispose(); $screen.Dispose(); $dial.Dispose()
        $graphics.Dispose(); $bitmap.Dispose()
    }
}
$stream = [IO.File]::Create($destination)
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
}
finally { $writer.Dispose(); $stream.Dispose() }
Write-Host "Generated: $destination"
