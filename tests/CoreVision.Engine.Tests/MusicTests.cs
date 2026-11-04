using System.Drawing;
using System.Drawing.Imaging;
using CoreVision.Engine;
using Xunit;

public class MusicTests
{
    static readonly DateTime Read = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    static NowPlaying Song(bool playing = true, double? position = 60, double? duration = 200, byte[]? art = null) =>
        new("A Song With A Very Long Title That Will Not Fit On The Screen", "Artist", playing,
            position is { } p ? TimeSpan.FromSeconds(p) : null, duration is { } d ? TimeSpan.FromSeconds(d) : null, Read) { Art = art };

    sealed class Black : IBackground
    {
        public TimeSpan FrameDelay => TimeSpan.FromSeconds(1);
        public void Advance() { }
        public void Dispose() { }
        public void Draw(Graphics g) => g.Clear(Color.Black);
    }

    static Bitmap Decode(byte[] jpeg) => new(new MemoryStream(jpeg));

    static byte[] RedImage(int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap)) g.Clear(Color.Red);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    static readonly ElementSpec Widget = new(ElementKind.Music, 240, 240, 300);

    [Fact]
    public void A_playing_song_moves_on_from_when_it_was_read()
    {
        Assert.Equal(TimeSpan.FromSeconds(70), Song().PositionAt(Read.AddSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(60), Song(playing: false).PositionAt(Read.AddSeconds(10)));
    }

    [Fact]
    public void Position_stops_at_the_end_of_the_song()
    {
        Assert.Equal(TimeSpan.FromSeconds(200), Song().PositionAt(Read.AddMinutes(30)));
        Assert.Equal(100, Song().ProgressAt(Read.AddMinutes(30)).Value);
    }

    [Fact]
    public void A_stream_without_a_length_has_no_progress()
    {
        Assert.Null(Song(duration: null).PositionAt(Read));
        Assert.Equal(ReadingState.Missing, Song(duration: null).ProgressAt(Read).State);
        Assert.Equal(30, Song(position: 60, duration: 200).ProgressAt(Read).Value);
    }

    [Theory]
    [InlineData(5, "0:05")]
    [InlineData(187, "3:07")]
    [InlineData(3727, "1:02:07")]
    public void Times_read_like_a_music_player(double seconds, string expected)
    {
        Assert.Equal(expected, NowPlaying.Clock(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Nothing_playing_hides_the_widget()
    {
        using var renderer = new ThemeRenderer();
        using var bitmap = Decode(renderer.Render(new Black(), [Widget], Stats.Empty, DateTime.Now, media: null));
        for (var y = 100; y < 380; y += 20)
            for (var x = 100; x < 380; x += 20)
                Assert.True(bitmap.GetPixel(x, y).GetBrightness() < 0.1f);
    }

    [Fact]
    public void Album_art_fills_its_square_even_from_a_wide_thumbnail()
    {
        using var renderer = new ThemeRenderer();
        var layout = ThemeRenderer.LayoutMusic(Widget);
        using var bitmap = Decode(renderer.Render(new Black(), [Widget], Stats.Empty, DateTime.Now, media: Song(art: RedImage(320, 180))));
        foreach (var (x, y) in new[] { (layout.Art.Left + 20, layout.Art.Top + 20), (layout.Art.Right - 20, layout.Art.Bottom - 20) })
        {
            var color = bitmap.GetPixel((int)x, (int)y);
            Assert.True(color.R > 200 && color.G < 60, $"{x},{y} was {color}");
        }
    }

    [Fact]
    public void Broken_album_art_does_not_fail_the_frame()
    {
        using var renderer = new ThemeRenderer();
        Assert.NotEmpty(renderer.Render(new Black(), [Widget], Stats.Empty, DateTime.Now, media: Song(art: [1, 2, 3])));
    }

    [Fact]
    public void Hidden_parts_take_no_space()
    {
        var full = ThemeRenderer.LayoutMusic(Widget).Bounds.Height;
        var artOnly = ThemeRenderer.LayoutMusic(Widget with { ShowTitle = false, ShowArtist = false, ShowProgress = false }).Bounds.Height;
        Assert.Equal(150, artOnly, 1);
        Assert.True(full > artOnly + 60);
    }

    [Fact]
    public void Layout_is_centered_on_the_widget()
    {
        var bounds = ThemeRenderer.LayoutMusic(Widget).Bounds;
        Assert.Equal(240, bounds.Left + bounds.Width / 2, 1);
        Assert.Equal(240, bounds.Top + bounds.Height / 2, 1);
    }

    [Fact]
    public void Gauges_can_show_song_progress()
    {
        var stats = Stats.Empty with { MusicProgress = Song().ProgressAt(Read) };
        Assert.Equal(30, stats.Get(Metric.MusicProgress).Value);
        Assert.Equal(ReadingState.Missing, Stats.Empty.Get(Metric.MusicProgress).State);
    }

    [Fact]
    public void Now_Playing_theme_fits_inside_the_round_screen()
    {
        var theme = Theme.BuiltIns.Single(t => t.Name == "Now Playing");
        var widget = theme.Elements.Single(e => e.Kind == ElementKind.Music);
        Assert.Equal(ProgressStyle.ScreenEdge, widget.ProgressStyle); // fits square and wide screens too
        const float inside = 240 - 18; // inside the progress that runs around the screen's edge
        var box = ThemeRenderer.LayoutMusic(widget).Bounds;
        foreach (var (x, y) in new[] { (box.Left, box.Top), (box.Right, box.Top), (box.Left, box.Bottom), (box.Right, box.Bottom) })
            Assert.True(Math.Sqrt((x - 240) * (x - 240) + (y - 240) * (y - 240)) < inside, $"{x},{y} crosses the ring");
    }

    [Fact]
    public void Music_settings_survive_saving()
    {
        var theme = Theme.Default with { Elements = [Widget with { ShowArt = false, SecondaryColor = "#123456" }] };
        var loaded = Theme.FromJson(theme.ToJson()).Elements[0];
        Assert.Equal(ElementKind.Music, loaded.Kind);
        Assert.False(loaded.ShowArt);
        Assert.Equal("#123456", loaded.SecondaryColor);
    }

    [Fact]
    public void The_player_draws_what_is_playing()
    {
        byte[]? frame = null;
        var theme = Theme.Default with { Elements = [Widget with { ShowTitle = false, ShowArtist = false, ShowProgress = false }] };
        using var player = new ThemePlayer(theme, () => Stats.Empty, f => Volatile.Write(ref frame, f), _ => new Black())
        {
            Media = () => Song(art: RedImage(100, 100)),
        };
        player.Start();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref frame) == null) { Assert.True(DateTime.UtcNow < deadline); Thread.Sleep(5); }
        using var bitmap = Decode(Volatile.Read(ref frame)!);
        Assert.True(bitmap.GetPixel(240, 240).R > 200);
    }
}
