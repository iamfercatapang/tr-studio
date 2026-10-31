# Draws src\CoreVision.App\app.ico. Each size is drawn on whole pixels, so the small ones stay sharp.
Add-Type -AssemblyName System.Drawing
$output = Join-Path $PSScriptRoot '..\src\CoreVision.App\app.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function Draw-Frame([int] $size) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'

    # A dark tile keeps the white mark readable on a light taskbar or in Explorer.
    $diameter = [Math]::Max(4, $size * 0.44)
    $far = $size - $diameter - 1
    $tile = [Drawing.Drawing2D.GraphicsPath]::new()
    $tile.AddArc(0, 0, $diameter, $diameter, 180, 90)
    $tile.AddArc($far, 0, $diameter, $diameter, 270, 90)
    $tile.AddArc($far, $far, $diameter, $diameter, 0, 90)
    $tile.AddArc(0, $far, $diameter, $diameter, 90, 90)
    $tile.CloseFigure()
    $g.FillPath([Drawing.Brushes]::Black, $tile)

    # The logo on a 22 unit square: a bar 10 high, a gap of 1, then two blocks with a gap of 2.
    $g.SmoothingMode = 'None'
    $margin = [int][Math]::Round($size * 0.15)
    $mark = $size - 2 * $margin
    $barHeight = [int][Math]::Round($mark * 10 / 22)
    $rowGap = [Math]::Max(1, [int][Math]::Round($mark / 22))
    $columnGap = [Math]::Max(1, [int][Math]::Round($mark * 2 / 22))
    $blockWidth = [int][Math]::Floor(($mark - $columnGap) / 2)
    $lowerTop = $margin + $barHeight + $rowGap
    $lowerHeight = $mark - $barHeight - $rowGap
    $g.FillRectangle([Drawing.Brushes]::White, $margin, $margin, $mark, $barHeight)
    $g.FillRectangle([Drawing.Brushes]::White, $margin, $lowerTop, $blockWidth, $lowerHeight)
    $g.FillRectangle([Drawing.Brushes]::White, ($margin + $mark - $blockWidth), $lowerTop, $blockWidth, $lowerHeight)
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
