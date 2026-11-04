using System.Drawing;
using CoreVision.Engine;
using Xunit;

public class ProgressStyleTests
{
    static readonly DateTime Read = DateTime.UtcNow;

    static NowPlaying Song() => new("Song", "Artist", false, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(200), Read);

    sealed class Black : IBackground
    {
        public TimeSpan FrameDelay => TimeSpan.FromSeconds(1);
        public void Advance() { }
        public void Dispose() { }
        public void Draw(Graphics g) => g.Clear(Color.Black);
    }

    static ElementSpec Widget(ProgressStyle style) => new(ElementKind.Music, 240, 240, 300) { ProgressStyle = style };

    static byte[] Render(ProgressStyle style, bool round, int width = 480, int height = 480)
    {
        using var renderer = new ThemeRenderer(width, height) { RoundScreen = round };
        using var background = new Black();
        return renderer.Render(background, [Widget(style)], Stats.Empty, DateTime.Now, media: Song());
    }

    [Fact]
    public void Old_themes_without_a_style_show_a_bar()
    {
        var theme = Theme.FromJson("""{ "name": "Old", "background": {}, "elements": [ { "kind": "Music", "x": 240, "y": 240, "size": 300 } ] }""");
        Assert.Equal(ProgressStyle.Bar, theme.Elements[0].ProgressStyle);
        var saved = Theme.FromJson((theme with { Elements = [Widget(ProgressStyle.ScreenEdge)] }).ToJson());
        Assert.Equal(ProgressStyle.ScreenEdge, saved.Elements[0].ProgressStyle);
    }

    [Fact]
    public void Styles_around_the_art_or_screen_keep_only_the_time_row()
    {
        var bar = ThemeRenderer.LayoutMusic(Widget(ProgressStyle.Bar));
        var edge = ThemeRenderer.LayoutMusic(Widget(ProgressStyle.ScreenEdge));
        Assert.False(bar.Bar.IsEmpty);
        Assert.True(edge.Bar.IsEmpty);
        Assert.True(edge.Bounds.Height < bar.Bounds.Height);
        Assert.Equal(bar.Bounds.Height, ThemeRenderer.LayoutMusic(Widget(ProgressStyle.Line)).Bounds.Height);
    }

    [Fact]
    public void Around_art_without_art_falls_back_to_a_bar()
    {
        var noArt = Widget(ProgressStyle.AroundArt) with { ShowArt = false };
        Assert.Equal(ProgressStyle.Bar, ThemeRenderer.EffectiveProgressStyle(noArt));
        Assert.False(ThemeRenderer.LayoutMusic(noArt).Bar.IsEmpty);
    }

    [Theory]
    [InlineData(ProgressStyle.Bar)]
    [InlineData(ProgressStyle.Line)]
    [InlineData(ProgressStyle.Segments)]
    [InlineData(ProgressStyle.AroundArt)]
    [InlineData(ProgressStyle.ScreenEdge)]
    public void Every_style_draws_on_any_screen(ProgressStyle style)
    {
        Assert.NotEmpty(Render(style, round: true));
        Assert.NotEmpty(Render(style, round: false));
        Assert.NotEmpty(Render(style, round: false, 1920, 462));
    }

    [Fact]
    public void Screen_edge_follows_the_screen_shape()
    {
        using var round = new Bitmap(new MemoryStream(Render(ProgressStyle.ScreenEdge, round: true)));
        using var square = new Bitmap(new MemoryStream(Render(ProgressStyle.ScreenEdge, round: false)));
        // The top-left corner is outside the circle but on a square screen's border.
        Assert.True(round.GetPixel(30, 30).GetBrightness() < 0.1f);
        Assert.True(square.GetPixel(14, 60).GetBrightness() > 0.1f);
    }

    [Fact]
    public void Outline_starts_at_the_top_center_and_closes()
    {
        var outline = ThemeRenderer.Outline(new RectangleF(0, 0, 100, 100), 50);
        Assert.Equal(new PointF(50, 0), outline[0]);
        Assert.Equal(outline[0], outline[^1]);
        var half = ThemeRenderer.PartOf(outline, 0.5f);
        Assert.InRange(half[^1].X, 49, 51);
        Assert.InRange(half[^1].Y, 99, 100);
        Assert.Empty(ThemeRenderer.PartOf(outline, 0));
    }

    [Fact(Skip = "Writes stills for a person to look at; set TRSTUDIO_STILLS and remove Skip")]
    public void Save_stills()
    {
        var folder = Environment.GetEnvironmentVariable("TRSTUDIO_STILLS") ?? Path.GetTempPath();
        foreach (var style in Enum.GetValues<ProgressStyle>())
        {
            File.WriteAllBytes(Path.Combine(folder, $"music-{style}-round.jpg"), Render(style, true));
            File.WriteAllBytes(Path.Combine(folder, $"music-{style}-square.jpg"), Render(style, false));
        }
        using var renderer = new ThemeRenderer();
        using var background = new Black();
        File.WriteAllBytes(Path.Combine(folder, "loading.jpg"), renderer.Render(background, Theme.Default.Elements, Stats.Empty, DateTime.Now, loading: true));
    }
}
