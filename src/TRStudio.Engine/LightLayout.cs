namespace TRStudio.Engine;

/// <summary>
/// Where one strip of LEDs sits on the lighting canvas: a box given by its top left corner and size in
/// canvas units, turned around its middle by Rotation degrees. The LEDs run along the box's longer side,
/// from left to right or top to bottom before turning; a ring's LEDs go round the oval that fills the box.
/// Brightness dims just this strip, 0 to 100.
/// </summary>
public sealed record LightPlacement(double X, double Y, double Width, double Height, double Rotation = 0, int Brightness = 100)
{
    public const double CanvasWidth = 320, CanvasHeight = 200;
    public const double MinSize = 4;
    const double Reach = 8; // how much of a box must stay on the canvas, so it can always be grabbed

    public LightPlacement Clamped()
    {
        static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;
        var width = Math.Clamp(Finite(Width, 40), MinSize, CanvasWidth);
        var height = Math.Clamp(Finite(Height, 12), MinSize, CanvasHeight);
        var rotation = Finite(Rotation, 0) % 360;
        return new LightPlacement(
            Math.Clamp(Finite(X, 0), Reach - width, CanvasWidth - Reach),
            Math.Clamp(Finite(Y, 0), Reach - height, CanvasHeight - Reach),
            width, height, rotation < 0 ? rotation + 360 : rotation, Math.Clamp(Brightness, 0, 100));
    }

    /// <summary>
    /// Each LED's place on the canvas, as x and y from 0 to 1. An LED past the canvas edge takes the edge,
    /// so a strip hanging off the side still lights.
    /// </summary>
    public (double X, double Y)[] Points(int leds, LightShape shape = LightShape.Strip)
    {
        var points = new (double, double)[Math.Max(0, leds)];
        var turn = Rotation * Math.PI / 180;
        var (sin, cos) = Math.SinCos(turn);
        var (centerX, centerY) = (X + Width / 2, Y + Height / 2);
        var wide = Width >= Height;
        for (var i = 0; i < points.Length; i++)
        {
            double localX, localY;
            if (shape == LightShape.Ring)
            {
                // Round the oval that fills the box: the first LED at the top, the rest clockwise.
                var (ringSin, ringCos) = Math.SinCos(i * Math.Tau / points.Length - Math.PI / 2);
                (localX, localY) = (ringCos * Width / 2, ringSin * Height / 2);
            }
            else
            {
                var along = ((i + 0.5) / points.Length - 0.5) * (wide ? Width : Height);
                (localX, localY) = wide ? (along, 0.0) : (0.0, along);
            }
            points[i] = (
                Math.Clamp((centerX + localX * cos - localY * sin) / CanvasWidth, 0, 1),
                Math.Clamp((centerY + localX * sin + localY * cos) / CanvasHeight, 0, 1));
        }
        return points;
    }

    /// <summary>
    /// A grid of places spread over the whole box, turned with it, as x and y from 0 to 1. A device that can show
    /// only one color takes the average over these, so an effect passing over its box rises and fades through it
    /// instead of flashing as it crosses the one middle point.
    /// </summary>
    public (double X, double Y)[] Area(int along = 8, int across = 4)
    {
        var wide = Width >= Height;
        var (columns, rows) = wide ? (along, across) : (across, along);
        var (sin, cos) = Math.SinCos(Rotation * Math.PI / 180);
        var (centerX, centerY) = (X + Width / 2, Y + Height / 2);
        var points = new (double, double)[columns * rows];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var localX = ((column + 0.5) / columns - 0.5) * Width;
                var localY = ((row + 0.5) / rows - 0.5) * Height;
                points[row * columns + column] = (
                    Math.Clamp((centerX + localX * cos - localY * sin) / CanvasWidth, 0, 1),
                    Math.Clamp((centerY + localX * sin + localY * cos) / CanvasHeight, 0, 1));
            }
        }
        return points;
    }

    /// <summary>
    /// Starting places for strips nobody has arranged yet: rows down the canvas, the longest strip nearly as
    /// wide as the canvas and the rest in proportion, so the lights work before the layout is touched. Rings get
    /// square boxes in a row along the bottom.
    /// </summary>
    public static Dictionary<string, LightPlacement> Arrange(IReadOnlyList<(string Key, int Leds, LightShape Shape)> all)
    {
        const double margin = 12, widest = CanvasWidth - margin * 2, narrowest = 60, ring = 40, ringGap = 8;
        var placed = new Dictionary<string, LightPlacement>();
        // Rings sit side by side along the bottom, wrapping upward; strips take the rows from the top.
        var rings = all.Where(zone => zone.Shape == LightShape.Ring).ToList();
        var perRow = Math.Max(1, (int)((widest + ringGap) / (ring + ringGap)));
        for (var i = 0; i < rings.Count; i++)
        {
            placed[rings[i].Key] = new LightPlacement(margin + i % perRow * (ring + ringGap), CanvasHeight - margin - ring - i / perRow * (ring + ringGap), ring, ring);
        }
        var zones = all.Where(zone => zone.Shape != LightShape.Ring).ToList();
        if (zones.Count == 0) return placed;
        // Rows of rings along the bottom take their height out of what the strips share.
        var ringRows = (rings.Count + perRow - 1) / perRow;
        var step = Math.Min(22, Math.Max(MinSize, (CanvasHeight - margin * 2 - ringRows * (ring + ringGap)) / zones.Count));
        var height = Math.Max(MinSize, step - 6);
        var most = Math.Max(1, zones.Max(zone => zone.Leds));
        for (var i = 0; i < zones.Count; i++)
        {
            var width = Math.Round(Math.Clamp(widest * zones[i].Leds / most, narrowest, widest));
            placed[zones[i].Key] = new LightPlacement(Math.Round((CanvasWidth - width) / 2), Math.Round(margin + i * step), width, Math.Round(height));
        }
        return placed;
    }
}
