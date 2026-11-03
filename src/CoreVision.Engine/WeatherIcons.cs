using System.Drawing;
using System.Drawing.Drawing2D;

namespace CoreVision.Engine;

/// <summary>Weather icons drawn as shapes, so they stay sharp at any size and need no image files.</summary>
public static class WeatherIcons
{
    static readonly Color Sun = Color.FromArgb(255, 214, 10);
    static readonly Color Moon = Color.FromArgb(230, 230, 240);
    static readonly Color Cloud = Color.FromArgb(209, 209, 214);
    static readonly Color DarkCloud = Color.FromArgb(142, 142, 147);
    static readonly Color Water = Color.FromArgb(100, 210, 255);
    static readonly Color Snow = Color.White;

    /// <summary>Draws the icon for a sky inside a square of the given side, centered on the point.</summary>
    public static void Draw(Graphics g, WeatherSky sky, bool isDay, PointF center, float side)
    {
        var s = side;
        switch (sky)
        {
            case WeatherSky.Clear:
                if (isDay) DrawSun(g, center, s);
                else DrawMoon(g, center, s);
                break;
            case WeatherSky.PartlyCloudy:
                var behind = new PointF(center.X - s * 0.14f, center.Y - s * 0.14f);
                if (isDay) DrawSun(g, behind, s * 0.72f);
                else DrawMoon(g, behind, s * 0.72f);
                DrawCloud(g, new PointF(center.X + s * 0.08f, center.Y + s * 0.1f), s * 0.8f, Cloud);
                break;
            case WeatherSky.Cloudy:
                DrawCloud(g, center, s, Cloud);
                break;
            case WeatherSky.Fog:
                DrawFog(g, center, s);
                break;
            case WeatherSky.Drizzle:
            case WeatherSky.Rain:
                DrawCloud(g, new PointF(center.X, center.Y - s * 0.12f), s * 0.9f, Cloud);
                DrawDrops(g, center, s, sky == WeatherSky.Rain ? 0.2f : 0.11f);
                break;
            case WeatherSky.Snow:
                DrawCloud(g, new PointF(center.X, center.Y - s * 0.12f), s * 0.9f, Cloud);
                DrawFlakes(g, center, s);
                break;
            case WeatherSky.Storm:
                DrawCloud(g, new PointF(center.X, center.Y - s * 0.12f), s * 0.9f, DarkCloud);
                DrawBolt(g, center, s);
                break;
        }
    }

    static void DrawSun(Graphics g, PointF c, float s)
    {
        var radius = s * 0.2f;
        using var brush = new SolidBrush(Sun);
        g.FillEllipse(brush, c.X - radius, c.Y - radius, radius * 2, radius * 2);
        using var pen = new Pen(Sun, Math.Max(s * 0.06f, 1)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (var i = 0; i < 8; i++)
        {
            var angle = i * MathF.PI / 4;
            var (cos, sin) = (MathF.Cos(angle), MathF.Sin(angle));
            g.DrawLine(pen, c.X + cos * radius * 1.45f, c.Y + sin * radius * 1.45f, c.X + cos * radius * 2f, c.Y + sin * radius * 2f);
        }
    }

    /// <summary>A crescent: a disc with an offset disc taken out of it.</summary>
    static void DrawMoon(Graphics g, PointF c, float s)
    {
        var radius = s * 0.3f;
        using var disc = new GraphicsPath();
        disc.AddEllipse(c.X - radius, c.Y - radius, radius * 2, radius * 2);
        using var bite = new GraphicsPath();
        bite.AddEllipse(c.X - radius * 0.35f, c.Y - radius * 1.25f, radius * 2, radius * 2);
        using var crescent = new Region(disc);
        crescent.Exclude(bite);
        using var brush = new SolidBrush(Moon);
        g.FillRegion(brush, crescent);
    }

    /// <summary>Three overlapping puffs on a flat base, filled as one shape.</summary>
    static void DrawCloud(Graphics g, PointF c, float s, Color color)
    {
        using var path = new GraphicsPath(FillMode.Winding);
        var baseTop = c.Y;
        var baseHeight = s * 0.2f;
        path.AddEllipse(c.X - s * 0.4f, baseTop - s * 0.06f, s * 0.3f, s * 0.26f);
        path.AddEllipse(c.X - s * 0.2f, baseTop - s * 0.24f, s * 0.38f, s * 0.38f);
        path.AddEllipse(c.X + s * 0.08f, baseTop - s * 0.12f, s * 0.32f, s * 0.32f);
        using (var bottom = RoundedBar(c.X - s * 0.36f, baseTop + s * 0.02f, s * 0.72f, baseHeight)) path.AddPath(bottom, false);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    static GraphicsPath RoundedBar(float x, float y, float width, float height)
    {
        var path = new GraphicsPath();
        path.AddArc(x, y, height, height, 90, 180);
        path.AddArc(x + width - height, y, height, height, 270, 180);
        path.CloseFigure();
        return path;
    }

    static void DrawDrops(Graphics g, PointF c, float s, float length)
    {
        using var pen = new Pen(Water, Math.Max(s * 0.055f, 1)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (var i = -1; i <= 1; i++)
        {
            var x = c.X + i * s * 0.17f;
            var top = c.Y + s * 0.2f;
            g.DrawLine(pen, x, top, x - length * s * 0.35f, top + length * s);
        }
    }

    static void DrawFlakes(Graphics g, PointF c, float s)
    {
        using var brush = new SolidBrush(Snow);
        var size = s * 0.09f;
        foreach (var (dx, dy) in new[] { (-0.17f, 0.24f), (0f, 0.34f), (0.17f, 0.24f) })
            g.FillEllipse(brush, c.X + dx * s - size / 2, c.Y + dy * s - size / 2, size, size);
    }

    static void DrawBolt(Graphics g, PointF c, float s)
    {
        using var brush = new SolidBrush(Sun);
        g.FillPolygon(brush, new PointF[]
        {
            new(c.X + s * 0.04f, c.Y + s * 0.1f), new(c.X - s * 0.1f, c.Y + s * 0.3f), new(c.X, c.Y + s * 0.3f),
            new(c.X - s * 0.06f, c.Y + s * 0.46f), new(c.X + s * 0.12f, c.Y + s * 0.24f), new(c.X + s * 0.02f, c.Y + s * 0.24f),
        });
    }

    static void DrawFog(Graphics g, PointF c, float s)
    {
        using var pen = new Pen(Cloud, Math.Max(s * 0.07f, 1)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, c.X - s * 0.32f, c.Y - s * 0.16f, c.X + s * 0.32f, c.Y - s * 0.16f);
        g.DrawLine(pen, c.X - s * 0.38f, c.Y, c.X + s * 0.26f, c.Y);
        g.DrawLine(pen, c.X - s * 0.26f, c.Y + s * 0.16f, c.X + s * 0.38f, c.Y + s * 0.16f);
    }
}
