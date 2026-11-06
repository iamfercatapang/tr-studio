using System.Drawing;
using System.Drawing.Drawing2D;

namespace CoreVision.Engine;

/// <summary>Where each part of a Music widget sits, on the 480 unit square. The editor mirrors these numbers.</summary>
public sealed record MusicLayout(RectangleF Art, float TitleY, float TitleSize, float ArtistY, float ArtistSize,
    RectangleF Bar, float TimeY, float TimeSize, RectangleF Bounds);

public sealed partial class ThemeRenderer
{
    const float MinMusicWidth = 120f;
    const float ArtShare = 0.5f;
    const float ArtGapShare = 0.05f;
    const float TitleShare = 0.075f;
    const float ArtistShare = 0.058f;
    const float LineSpacing = 1.3f;
    const float BarGapShare = 0.03f;
    const float BarShare = 0.018f;
    const float MinBarHeight = 3f;
    const float BarWidthShare = 0.86f;
    const float TimeGapShare = 0.025f;
    const float TimeShare = 0.045f;
    const float PauseBadgeShare = 0.28f;
    const int PauseBadgeAlpha = 150;
    const int MaxFittedTexts = 64;
    const float LineShare = 0.4f;
    const float MinLineWidth = 2f;
    const float KnobShare = 1.1f;
    const int SegmentCount = 12;
    const float SegmentGapShare = 0.25f;
    const float SegmentHeightShare = 1.3f;
    const float ArtRingGapShare = 0.025f;
    const float EdgeThickness = 12f;
    const float EdgeInset = 6f;
    const float EdgeCornerRadius = 28f;
    const int OutlineCornerSteps = 24;
    const string Ellipsis = "…";

    byte[]? _artBytes;
    Image? _artImage;
    readonly Dictionary<(string Text, string Family, FontStyle Style, float Size, float Width), string> _fitted = [];

    public static MusicLayout LayoutMusic(ElementSpec element)
    {
        var width = Math.Max(element.Size, MinMusicWidth);
        var y = 0f;
        var artSide = element.ShowArt ? width * ArtShare : 0;
        var art = new RectangleF(-artSide / 2, 0, artSide, artSide);
        y += artSide;
        if (artSide > 0 && (element.ShowTitle || element.ShowArtist || element.ShowProgress)) y += width * ArtGapShare;

        var titleSize = width * TitleShare;
        var titleY = y;
        if (element.ShowTitle) y += titleSize * LineSpacing;
        var artistSize = width * ArtistShare;
        var artistY = y;
        if (element.ShowArtist) y += artistSize * LineSpacing;

        var barHeight = Math.Max(width * BarShare, MinBarHeight);
        var timeSize = width * TimeShare;
        var bar = RectangleF.Empty;
        var timeY = y;
        if (element.ShowProgress && HasBarRow(element))
        {
            if (y > 0) y += width * BarGapShare;
            bar = new RectangleF(-width * BarWidthShare / 2, y, width * BarWidthShare, barHeight);
            y += barHeight + width * TimeGapShare;
            timeY = y;
            y += timeSize * LineSpacing;
        }
        else if (element.ShowProgress)
        {
            // The progress runs around the art or the screen, so only the times need a row.
            if (y > 0) y += width * BarGapShare;
            timeY = y;
            y += timeSize * LineSpacing;
        }

        var top = element.Y - Math.Max(y, 1) / 2;
        RectangleF Place(RectangleF box) => box with { X = box.X + element.X, Y = box.Y + top };
        return new MusicLayout(Place(art), titleY + top, titleSize, artistY + top, artistSize, Place(bar), timeY + top, timeSize,
            new RectangleF(element.X - width / 2, top, width, Math.Max(y, 1)));
    }

    /// <summary>With nothing playing, the widget draws nothing and the rest of the theme shows as designed.</summary>
    void DrawMusic(Graphics g, ElementSpec element, NowPlaying? media)
    {
        if (media == null) return;
        var layout = LayoutMusic(element);
        var width = layout.Bounds.Width;
        var (family, style) = FontFor(element);
        var (plainFamily, plainStyle) = FontFor(element with { Bold = false });

        if (element.ShowArt) DrawArt(g, element, layout.Art, media);
        if (element.ShowTitle) DrawFitted(g, media.Title, family, style, layout.TitleSize, element.X, layout.TitleY, width, ParseColor(element.Color, Color.White));
        if (element.ShowArtist) DrawFitted(g, media.Artist, plainFamily, plainStyle, layout.ArtistSize, element.X, layout.ArtistY, width, ParseColor(element.SecondaryColor, Color.Gray));
        if (element.ShowProgress && media.Duration is { } duration && duration > TimeSpan.Zero) DrawProgress(g, element, layout, media, duration, plainFamily, plainStyle);
    }

    void DrawArt(Graphics g, ElementSpec element, RectangleF box, NowPlaying media)
    {
        var radius = Math.Clamp(element.CornerRadius, 0, box.Width / 2);
        using var shape = RoundedBox(box, radius);
        if (ArtImage(media.Art) is { } art)
        {
            // Cover the square: the middle of a wide thumbnail is kept, like a video's own preview.
            var side = Math.Min(art.Width, art.Height);
            var scale = box.Width / side;
            using var brush = new TextureBrush(art, WrapMode.Clamp);
            brush.TranslateTransform(box.X - (art.Width - side) / 2f * scale, box.Y - (art.Height - side) / 2f * scale);
            brush.ScaleTransform(scale, scale);
            g.FillPath(brush, shape);
        }
        else
        {
            using var empty = new SolidBrush(ParseColor(element.TrackColor, Color.FromArgb(42, 42, 46)));
            g.FillPath(empty, shape);
        }
        if (media.IsPlaying) return;

        var badge = box.Width * PauseBadgeShare;
        var center = new PointF(box.X + box.Width / 2, box.Y + box.Height / 2);
        using (var dark = new SolidBrush(Color.FromArgb(PauseBadgeAlpha, Color.Black)))
            g.FillEllipse(dark, center.X - badge / 2, center.Y - badge / 2, badge, badge);
        var barWidth = badge * 0.13f;
        var barHeight = badge * 0.42f;
        using var white = new SolidBrush(Color.White);
        g.FillRectangle(white, center.X - barWidth * 1.6f, center.Y - barHeight / 2, barWidth, barHeight);
        g.FillRectangle(white, center.X + barWidth * 0.6f, center.Y - barHeight / 2, barWidth, barHeight);
    }

    /// <summary>Around Art needs the art; without it the widget falls back to a bar.</summary>
    public static ProgressStyle EffectiveProgressStyle(ElementSpec element) =>
        element.ProgressStyle == ProgressStyle.AroundArt && !element.ShowArt ? ProgressStyle.Bar : element.ProgressStyle;

    static bool HasBarRow(ElementSpec element) => EffectiveProgressStyle(element) is ProgressStyle.Bar or ProgressStyle.Line or ProgressStyle.Segments;

    void DrawProgress(Graphics g, ElementSpec element, MusicLayout layout, NowPlaying media, TimeSpan duration, FontFamily family, FontStyle style)
    {
        var position = media.PositionAt(DateTime.UtcNow);
        var share = position is { } elapsed ? (float)Math.Clamp(elapsed / duration, 0, 1) : 0f;
        var track = ParseColor(element.TrackColor, Color.FromArgb(42, 42, 46));
        var fill = ParseColor(element.EndColor, Color.Cyan);
        var color = ParseColor(element.SecondaryColor, Color.Gray);
        var shown = position is { } at ? NowPlaying.Clock(at) : NoValue;

        var progressStyle = EffectiveProgressStyle(element);
        switch (progressStyle)
        {
            case ProgressStyle.Line: DrawLineProgress(g, layout.Bar, share, track, fill); break;
            case ProgressStyle.Segments: DrawSegments(g, layout.Bar, share, track, fill); break;
            case ProgressStyle.AroundArt: DrawArtRing(g, element, layout, share, track, fill); break;
            case ProgressStyle.ScreenEdge:
                // It follows the screen's edge, so the widget's rotation doesn't apply to it.
                var turned = g.Save();
                if (_screenTransform != null) g.Transform = _screenTransform;
                DrawScreenEdge(g, share, track, fill);
                g.Restore(turned);
                break;
            default: DrawBar(g, layout.Bar, share, track, fill); break;
        }

        if (HasBarRow(element))
        {
            DrawLine(g, shown, family, style, layout.TimeSize, layout.Bar.Left, layout.TimeY, StringAlignment.Near, color);
            DrawLine(g, NowPlaying.Clock(duration), family, style, layout.TimeSize, layout.Bar.Right, layout.TimeY, StringAlignment.Far, color);
        }
        else DrawLine(g, $"{shown} / {NowPlaying.Clock(duration)}", family, style, layout.TimeSize, element.X, layout.TimeY, StringAlignment.Center, color);
    }

    static void DrawBar(Graphics g, RectangleF bar, float share, Color trackColor, Color fillColor)
    {
        var radius = bar.Height / 2;
        using (var trackShape = RoundedBox(bar, radius))
        using (var track = new SolidBrush(trackColor))
            g.FillPath(track, trackShape);
        var filled = share * bar.Width;
        if (filled <= 0.5f) return;
        using var fillShape = RoundedBox(bar with { Width = filled }, Math.Min(radius, filled / 2));
        using var fill = new SolidBrush(fillColor);
        g.FillPath(fill, fillShape);
    }

    /// <summary>A hairline with a knob at the playhead, like a media player's scrubber.</summary>
    static void DrawLineProgress(Graphics g, RectangleF bar, float share, Color trackColor, Color fillColor)
    {
        var y = bar.Top + bar.Height / 2;
        var thickness = Math.Max(bar.Height * LineShare, MinLineWidth);
        var playhead = bar.Left + share * bar.Width;
        using (var track = new Pen(trackColor, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(track, bar.Left, y, bar.Right, y);
        using (var fill = new Pen(fillColor, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            if (playhead > bar.Left + 0.5f) g.DrawLine(fill, bar.Left, y, playhead, y);
        var knob = bar.Height * KnobShare;
        using var brush = new SolidBrush(fillColor);
        g.FillEllipse(brush, playhead - knob, y - knob, knob * 2, knob * 2);
    }

    /// <summary>Short pills that light up in turn. The one being played fades in as the song moves through it.</summary>
    static void DrawSegments(Graphics g, RectangleF bar, float share, Color trackColor, Color fillColor)
    {
        var height = bar.Height * SegmentHeightShare;
        var top = bar.Top + (bar.Height - height) / 2;
        var step = bar.Width / SegmentCount;
        var width = step * (1 - SegmentGapShare);
        var lit = share * SegmentCount;
        using var track = new SolidBrush(trackColor);
        for (var i = 0; i < SegmentCount; i++)
        {
            var box = new RectangleF(bar.Left + i * step + (step - width) / 2, top, width, height);
            using var shape = RoundedBox(box, Math.Min(height, width) / 2);
            g.FillPath(track, shape);
            var amount = Math.Clamp(lit - i, 0, 1);
            if (amount <= 0) continue;
            using var fill = new SolidBrush(Color.FromArgb((int)(fillColor.A * amount), fillColor));
            g.FillPath(fill, shape);
        }
    }

    /// <summary>Traces the album art's outline, so it's a circle around round art and a rounded square otherwise.</summary>
    static void DrawArtRing(Graphics g, ElementSpec element, MusicLayout layout, float share, Color trackColor, Color fillColor)
    {
        var width = layout.Bounds.Width;
        var thickness = Math.Max(width * BarShare, MinBarHeight);
        var gap = width * ArtRingGapShare + thickness / 2;
        var box = RectangleF.Inflate(layout.Art, gap, gap);
        var radius = Math.Clamp(element.CornerRadius, 0, layout.Art.Width / 2);
        var outline = Outline(box, radius >= layout.Art.Width / 2 ? box.Width / 2 : radius + gap);
        DrawAlong(g, outline, share, thickness, trackColor, fillColor);
    }

    /// <summary>Runs around the screen's own border: a circle on round screens, a rounded rectangle on the rest.</summary>
    void DrawScreenEdge(Graphics g, float share, Color trackColor, Color fillColor)
    {
        var inset = EdgeInset + EdgeThickness / 2;
        RectangleF box;
        float radius;
        if (RoundScreen)
        {
            box = new RectangleF(inset, inset, ScreenSize - 2 * inset, ScreenSize - 2 * inset);
            radius = box.Width / 2;
        }
        else
        {
            // Wide screens show more than the 480 unit square, so the border is wherever the canvas ends.
            var width = _canvas.Width / _scale;
            var height = _canvas.Height / _scale;
            box = new RectangleF(ScreenSize / 2f - width / 2 + inset, ScreenSize / 2f - height / 2 + inset, width - 2 * inset, height - 2 * inset);
            radius = EdgeCornerRadius;
        }
        DrawAlong(g, Outline(box, radius), share, EdgeThickness, trackColor, fillColor);
    }

    /// <summary>
    /// Points around a rounded box, clockwise from the middle of the top edge, so progress starts at twelve o'clock.
    /// A radius of half the side makes a circle.
    /// </summary>
    public static PointF[] Outline(RectangleF box, float radius)
    {
        radius = Math.Clamp(radius, 0, Math.Min(box.Width, box.Height) / 2);
        var points = new List<PointF> { new(box.Left + box.Width / 2, box.Top) };
        void Corner(float cx, float cy, float from)
        {
            for (var step = 0; step <= OutlineCornerSteps; step++)
            {
                var angle = (from + 90f * step / OutlineCornerSteps) * MathF.PI / 180;
                points.Add(new PointF(cx + radius * MathF.Cos(angle), cy + radius * MathF.Sin(angle)));
            }
        }
        if (radius <= 0)
        {
            points.AddRange([new(box.Right, box.Top), new(box.Right, box.Bottom), new(box.Left, box.Bottom), new(box.Left, box.Top)]);
        }
        else
        {
            Corner(box.Right - radius, box.Top + radius, 270);
            Corner(box.Right - radius, box.Bottom - radius, 0);
            Corner(box.Left + radius, box.Bottom - radius, 90);
            Corner(box.Left + radius, box.Top + radius, 180);
        }
        points.Add(points[0]);
        return [.. points];
    }

    /// <summary>The whole outline in the track color, then the played share of it from the start.</summary>
    static void DrawAlong(Graphics g, PointF[] outline, float share, float thickness, Color trackColor, Color fillColor)
    {
        using (var track = new Pen(trackColor, thickness) { LineJoin = LineJoin.Round })
            g.DrawLines(track, outline);
        var played = PartOf(outline, share);
        if (played.Length < 2) return;
        using var fill = new Pen(fillColor, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(fill, played);
    }

    /// <summary>The start of a path, up to a share of its length.</summary>
    public static PointF[] PartOf(PointF[] path, float share)
    {
        if (share <= 0 || path.Length < 2) return [];
        var lengths = new float[path.Length - 1];
        for (var i = 0; i < lengths.Length; i++) lengths[i] = Distance(path[i], path[i + 1]);
        var wanted = lengths.Sum() * Math.Min(share, 1);
        var part = new List<PointF> { path[0] };
        for (var i = 0; i < lengths.Length; i++)
        {
            if (wanted >= lengths[i])
            {
                part.Add(path[i + 1]);
                wanted -= lengths[i];
                continue;
            }
            if (wanted > 0 && lengths[i] > 0)
            {
                var t = wanted / lengths[i];
                part.Add(new PointF(path[i].X + (path[i + 1].X - path[i].X) * t, path[i].Y + (path[i + 1].Y - path[i].Y) * t));
            }
            break;
        }
        return [.. part];
    }

    static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Long names end in an ellipsis instead of running past the widget or off the round screen.</summary>
    void DrawFitted(Graphics g, string text, FontFamily family, FontStyle style, float size, float centerX, float y, float maxWidth, Color color)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        DrawLine(g, Fit(text, family, style, size, maxWidth), family, style, size, centerX, y, StringAlignment.Center, color);
    }

    static void DrawLine(Graphics g, string text, FontFamily family, FontStyle style, float size, float x, float y, StringAlignment align, Color color)
    {
        using var format = new StringFormat { Alignment = align };
        using var path = new GraphicsPath();
        path.AddString(text, family, (int)style, size, new PointF(x, y), format);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    string Fit(string text, FontFamily family, FontStyle style, float size, float maxWidth)
    {
        var key = (text, family.Name, style, size, maxWidth);
        if (_fitted.TryGetValue(key, out var fitted)) return fitted;
        if (_fitted.Count >= MaxFittedTexts) _fitted.Clear();

        float WidthOf(string candidate)
        {
            using var path = new GraphicsPath();
            path.AddString(candidate, family, (int)style, size, PointF.Empty, StringFormat.GenericDefault);
            return path.GetBounds().Width;
        }

        fitted = text;
        if (WidthOf(text) > maxWidth)
        {
            // The longest start of the text that still fits with the ellipsis.
            var (low, high) = (0, text.Length);
            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                if (WidthOf(text[..middle].TrimEnd() + Ellipsis) <= maxWidth) low = middle;
                else high = middle - 1;
            }
            fitted = low == 0 ? Ellipsis : text[..low].TrimEnd() + Ellipsis;
        }
        _fitted[key] = fitted;
        return fitted;
    }

    /// <summary>A bad image from a media app shows the empty square instead of failing the frame.</summary>
    Image? ArtImage(byte[]? bytes)
    {
        if (ReferenceEquals(bytes, _artBytes)) return _artImage;
        _artImage?.Dispose();
        _artImage = null;
        _artBytes = bytes;
        if (bytes is not { Length: > 0 }) return null;
        try { _artImage = Image.FromStream(new MemoryStream(bytes)); }
        catch (ArgumentException) { _artImage = null; }
        return _artImage;
    }
}
