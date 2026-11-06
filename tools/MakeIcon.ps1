# Draws the TR mark everywhere it's used, from one set of shapes:
#   src\CoreVision.App\app.ico             the app, tray, taskbar and installer icon
#   src\CoreVision.App\wwwroot\logo.svg    the sidebar logo
#   docs\logo.svg                          the README logo
#   installer\wizard-small-*.bmp           the installer's header image
# Each icon size is drawn on its own, so the small ones stay sharp.
Add-Type -AssemblyName System.Drawing
$root = Join-Path $PSScriptRoot '..'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$inv = [Globalization.CultureInfo]::InvariantCulture

# The mark: a "T" built from thick bars, joined to a block "R". Rectangles are x1, y1, x2, y2.
$bars = @(
    @(49.224, 68.716, 918.461, 165.051), @(223.333, 145.184, 919.450, 241.519),
    @(47.998, 144.979, 144.333, 395.551), @(129.883, 144.979, 226.218, 395.551),
    @(49.224, 355.084, 443.641, 451.419), @(223.333, 298.240, 419.273, 394.575),
    @(302.513, 298.246, 398.847, 859.644), @(354.724, 298.246, 451.059, 859.644)
)
# The R and its counter, filled even-odd so the counter stays open.
$rOuter = @(@(921.227, 859.457), @(838.634, 702.696), @(921.227, 702.696), @(921.227, 297.313), @(498.989, 297.313),
    @(498.989, 859.457), @(639.735, 859.457), @(639.735, 702.696), @(700.415, 702.696), @(780.481, 859.457))
$rCounter = @(@(780.481, 574.594), @(639.735, 574.594), @(639.735, 426.262), @(780.481, 426.262))
$markLeft = 47.998; $markTop = 68.716; $markWidth = 873.229; $markHeight = 790.928

# The app's colors from app.css: --accent, --accent-3, --accent-2, and the sidebar and window border.
$cyan = [Drawing.Color]::FromArgb(0x00, 0xE5, 0xFF)
$violet = [Drawing.Color]::FromArgb(0x8B, 0x5C, 0xFF)
$magenta = [Drawing.Color]::FromArgb(0xFF, 0x3D, 0xD8)
$tileTop = [Drawing.Color]::FromArgb(0x1C, 0x16, 0x36)
$tileBottom = [Drawing.Color]::FromArgb(0x0A, 0x08, 0x12)
$edge = [Drawing.Color]::FromArgb(0x3A, 0x2F, 0x63)

# Small sizes drop the glow and let the mark fill more of the tile, so it reads at 16 pixels.
function Layout([double] $size) {
    $small = $size -lt 32
    $share = if ($small) { 0.78 } else { 0.62 }
    $scale = $size * $share / $markWidth
    @{
        Small = $small
        Radius = $size * $(if ($small) { 0.2 } else { 0.225 })
        Scale = $scale
        X = ($size - $markWidth * $scale) / 2 - $markLeft * $scale
        Y = ($size - $markHeight * $scale) / 2 - $markTop * $scale
    }
}

function Rounded-Path([double] $x, [double] $y, [double] $size, [double] $radius) {
    $d = $radius * 2
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $size - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $size - $d, $y + $size - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    , $path
}

function Draw-Tile([Drawing.Graphics] $g, [int] $size) {
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality' # otherwise the rounded corners sit half a pixel off
    $layout = Layout $size
    $tile = Rounded-Path 0 0 $size $layout.Radius
    $background = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.PointF]::new(0, 0), [Drawing.PointF]::new(0, $size), $tileTop, $tileBottom)
    $g.FillPath($background, $tile)

    if (-not $layout.Small) {
        # A soft violet glow behind the mark, like the app's --glow-brand.
        $glow = [Drawing.Drawing2D.GraphicsPath]::new()
        $glow.AddEllipse($size * 0.1, $size * 0.1, $size * 0.8, $size * 0.8)
        $glowBrush = [Drawing.Drawing2D.PathGradientBrush]::new($glow)
        $glowBrush.CenterColor = [Drawing.Color]::FromArgb(70, $violet)
        $glowBrush.SurroundColors = @([Drawing.Color]::FromArgb(0, $violet))
        $g.SetClip($tile)
        $g.FillPath($glowBrush, $glow)
        $g.ResetClip()
    }

    $border = [Math]::Max(1, $size / 64)
    $pen = [Drawing.Pen]::new($edge, $border)
    $inner = Rounded-Path ($border / 2) ($border / 2) ($size - $border) ([Math]::Max(0.5, $layout.Radius - $border / 2))
    $g.DrawPath($pen, $inner)

    $mark = [Drawing.Drawing2D.GraphicsPath]::new([Drawing.Drawing2D.FillMode]::Alternate)
    $s = $layout.Scale; $ox = $layout.X; $oy = $layout.Y
    $point = { param($p) [Drawing.PointF]::new($ox + $p[0] * $s, $oy + $p[1] * $s) }
    $mark.AddPolygon([Drawing.PointF[]] ($rOuter | ForEach-Object { & $point $_ }))
    $mark.AddPolygon([Drawing.PointF[]] ($rCounter | ForEach-Object { & $point $_ }))
    $bars = $script:bars | ForEach-Object { [Drawing.RectangleF]::new($ox + $_[0] * $s, $oy + $_[1] * $s, ($_[2] - $_[0]) * $s, ($_[3] - $_[1]) * $s) }

    # The brand gradient runs at 120 degrees across the mark, as --gradient-brand does.
    $from = & $point @($markLeft, $markTop)
    $to = & $point @(($markLeft + $markWidth), ($markTop + $markHeight))
    $brand = [Drawing.Drawing2D.LinearGradientBrush]::new($from, $to, $cyan, $magenta)
    $blend = [Drawing.Drawing2D.ColorBlend]::new(3)
    $blend.Colors = @($cyan, $violet, $magenta)
    $blend.Positions = @([single] 0, [single] 0.55, [single] 1)
    $brand.InterpolationColors = $blend
    $brand.WrapMode = 'TileFlipXY'
    $g.FillPath($brand, $mark)
    foreach ($bar in $bars) { $g.FillRectangle($brand, $bar) }
}

function Draw-Png([int] $size) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    Draw-Tile $g $size
    $g.Dispose()
    $png = [IO.MemoryStream]::new()
    $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $png.ToArray()
}

# The icon file: a directory of PNG frames.
$frames = @($sizes | ForEach-Object { , (Draw-Png $_) })
$stream = [IO.File]::Create((Join-Path $root 'src\CoreVision.App\app.ico'))
$writer = [IO.BinaryWriter]::new($stream)
$writer.Write([uint16] 0)
$writer.Write([uint16] 1)
$writer.Write([uint16] $sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $edgeLength = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] } # 0 means 256 in an icon directory
    $writer.Write([byte] $edgeLength)
    $writer.Write([byte] $edgeLength)
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

# The installer header sits on white, so these are drawn onto white with no transparency.
foreach ($size in 55, 83, 110, 138) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.Clear([Drawing.Color]::White)
    Draw-Tile $g $size
    $g.Dispose()
    $bitmap.Save((Join-Path $root "installer\wizard-small-$size.bmp"), [Drawing.Imaging.ImageFormat]::Bmp)
    $bitmap.Dispose()
}

# The SVG logo, drawn at 1024 units with the same layout as the large icon.
function N([double] $value) { $value.ToString('0.###', $inv) }
$size = 1024
$layout = Layout $size
$rects = ($bars | ForEach-Object { "<rect x=`"$(N $_[0])`" y=`"$(N $_[1])`" width=`"$(N ($_[2] - $_[0]))`" height=`"$(N ($_[3] - $_[1]))`"/>" }) -join ''
$polygon = { param($points) 'M' + (($points | ForEach-Object { "$(N $_[0]) $(N $_[1])" }) -join 'L') + 'Z' }
$hex = { param($c) '#{0:X2}{1:X2}{2:X2}' -f $c.R, $c.G, $c.B }
$border = $size / 64
$svg = @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 $size $size" role="img" aria-label="TR Studio">
<defs>
<linearGradient id="tile" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="$(& $hex $tileTop)"/><stop offset="1" stop-color="$(& $hex $tileBottom)"/></linearGradient>
<radialGradient id="glow"><stop offset="0" stop-color="$(& $hex $violet)" stop-opacity="0.27"/><stop offset="1" stop-color="$(& $hex $violet)" stop-opacity="0"/></radialGradient>
<linearGradient id="brand" gradientUnits="userSpaceOnUse" x1="$(N $markLeft)" y1="$(N $markTop)" x2="$(N ($markLeft + $markWidth))" y2="$(N ($markTop + $markHeight))"><stop offset="0" stop-color="$(& $hex $cyan)"/><stop offset="0.55" stop-color="$(& $hex $violet)"/><stop offset="1" stop-color="$(& $hex $magenta)"/></linearGradient>
</defs>
<rect width="$size" height="$size" rx="$(N $layout.Radius)" fill="url(#tile)"/>
<circle cx="$(N ($size / 2))" cy="$(N ($size / 2))" r="$(N ($size * 0.4))" fill="url(#glow)"/>
<rect x="$(N ($border / 2))" y="$(N ($border / 2))" width="$(N ($size - $border))" height="$(N ($size - $border))" rx="$(N ($layout.Radius - $border / 2))" fill="none" stroke="$(& $hex $edge)" stroke-width="$(N $border)"/>
<g transform="translate($(N $layout.X) $(N $layout.Y)) scale($(N $layout.Scale))" fill="url(#brand)">$rects<path fill-rule="evenodd" d="$(& $polygon $rOuter)$(& $polygon $rCounter)"/></g>
</svg>
"@
foreach ($file in 'src\CoreVision.App\wwwroot\logo.svg', 'docs\logo.svg') {
    [IO.File]::WriteAllText((Join-Path $root $file), $svg.Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
}
