# Reproduce the existing tray artwork as a multi-resolution Windows ICO.
param([string]$OutputPath = (Join-Path $PSScriptRoot '..\YoyoClawCompanion\Assets\App.ico'))
Add-Type -AssemblyName System.Drawing
$frames = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([Drawing.Color]::Transparent)
    $g.ScaleTransform($size / 64.0, $size / 64.0)
    $shape = [Drawing.Drawing2D.GraphicsPath]::new()
    $shape.AddArc(4, 4, 32, 32, 180, 90)
    $shape.AddArc(28, 4, 32, 32, 270, 90)
    $shape.AddArc(28, 28, 32, 32, 0, 90)
    $shape.AddArc(4, 28, 32, 32, 90, 90)
    $shape.CloseFigure()
    $bg = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(24, 32, 51))
    $g.FillPath($bg, $shape)
    $colors = @([Drawing.Color]::FromArgb(62,213,152), [Drawing.Color]::FromArgb(143,160,255), [Drawing.Color]::FromArgb(242,201,76))
    for ($i=0; $i -lt 3; $i++) {
        $brush = [Drawing.SolidBrush]::new($colors[$i])
        $g.FillEllipse($brush, 16 + 11 * $i, 27, 10, 10)
        $brush.Dispose()
    }
    $png = [IO.MemoryStream]::new()
    $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
    $frames += @{ Size=$size; Bytes=$png.ToArray() }
    $png.Dispose(); $bg.Dispose(); $shape.Dispose(); $g.Dispose(); $bitmap.Dispose()
}
$stream = [IO.File]::Create([IO.Path]::GetFullPath($OutputPath))
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $stream.Dispose() }
