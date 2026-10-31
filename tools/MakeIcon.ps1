# Draws src\CoreVision.App\app.ico. Each size is drawn on whole pixels, so the small ones stay sharp.
Add-Type -AssemblyName System.Drawing
$output = Join-Path $PSScriptRoot '..\src\CoreVision.App\app.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function Draw-Frame([int] $size) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality' # otherwise the rounded corners sit half a pixel off

    # A plain window: a blue frame with a title bar around a light pane. Both colors show on any taskbar.
    $diameter = [Math]::Max(3, $size * 0.2)
    $far = $size - $diameter
    $frame = [Drawing.Drawing2D.GraphicsPath]::new()
    $frame.AddArc(0, 0, $diameter, $diameter, 180, 90)
    $frame.AddArc($far, 0, $diameter, $diameter, 270, 90)
    $frame.AddArc($far, $far, $diameter, $diameter, 0, 90)
    $frame.AddArc(0, $far, $diameter, $diameter, 90, 90)
    $frame.CloseFigure()
    $blue = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(10, 132, 255))
    $pane = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(245, 245, 247))
    $g.FillPath($blue, $frame)

    $g.SmoothingMode = 'None'
    $border = [Math]::Max(1, [int][Math]::Round($size * 0.08))
    $titleBar = [Math]::Max(3, [int][Math]::Round($size * 0.24))
    $g.FillRectangle($pane, $border, $titleBar, $size - 2 * $border, $size - $titleBar - $border)
    $g.Dispose()

    $png = [IO.MemoryStream]::new()
    $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $png.ToArray()
}

$frames = @($sizes | ForEach-Object { , (Draw-Frame $_) })
$stream = [IO.File]::Create($output)
$writer = [IO.BinaryWriter]::new($stream)
$writer.Write([uint16] 0)
$writer.Write([uint16] 1)
$writer.Write([uint16] $sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $edge = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] } # 0 means 256 in an icon directory
    $writer.Write([byte] $edge)
    $writer.Write([byte] $edge)
    $writer.Write([byte] 0)
    $writer.Write([byte] 0)
    $writer.Write([uint16] 1)
    $writer.Write([uint16] 32)
    $writer.Write([uint32] $frames[$i].Length)
    $writer.Write([uint32] $offset)
    $offset += $frames[$i].Length
}
$frames | ForEach-Object { $writer.Write([byte[]] $_) }
$writer.Dispose()
