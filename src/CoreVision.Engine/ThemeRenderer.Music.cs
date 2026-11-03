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
        if (element.ShowProgress)
        {
            if (y > 0) y += width * BarGapShare;
            bar = new RectangleF(-width * BarWidthShare / 2, y, width * BarWidthShare, barHeight);
            y += barHeight + width * TimeGapShare;
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

    void DrawProgress(Graphics g, ElementSpec element, MusicLayout layout, NowPlaying media, TimeSpan duration, FontFamily family, FontStyle style)
    {
        var bar = layout.Bar;
        var radius = bar.Height / 2;
        using (var trackShape = RoundedBox(bar, radius))
        using (var track = new SolidBrush(ParseColor(element.TrackColor, Color.FromArgb(42, 42, 46))))
            g.FillPath(track, trackShape);

        var position = media.PositionAt(DateTime.UtcNow);
        if (position is { } elapsed)
        {
            var filled = (float)(elapsed / duration) * bar.Width;
            if (filled > 0.5f)
            {
                using var fillShape = RoundedBox(bar with { Width = filled }, Math.Min(radius, filled / 2));
                using var fill = new SolidBrush(ParseColor(element.EndColor, Color.Cyan));
                g.FillPath(fill, fillShape);
            }
        }

        var color = ParseColor(element.SecondaryColor, Color.Gray);
        DrawLine(g, position is { } shown ? NowPlaying.Clock(shown) : NoValue, family, style, layout.TimeSize, bar.Left, layout.TimeY, StringAlignment.Near, color);
        DrawLine(g, NowPlaying.Clock(duration), family, style, layout.TimeSize, bar.Right, layout.TimeY, StringAlignment.Far, color);
    }

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
