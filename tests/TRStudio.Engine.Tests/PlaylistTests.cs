using System.Drawing;
using TRStudio.Engine;
using Xunit;

public class PlaylistTests
{
    const string A = "a.json", B = "b.json", C = "c.json";

    [Fact]
    public void Themes_play_in_list_order_and_wrap_around()
    {
        var playlist = new ThemePlaylist(true, [C, A, B]);

        Assert.Equal(A, playlist.Next(C));
        Assert.Equal(B, playlist.Next(A));
        Assert.Equal(C, playlist.Next(B));
    }

    [Fact]
    public void A_theme_outside_the_list_moves_to_the_first()
    {
        Assert.Equal(A, new ThemePlaylist(true, [A, B]).Next("other.json"));
    }

    [Fact]
    public void The_default_theme_can_be_in_the_list()
    {
        var playlist = new ThemePlaylist(true, [null, A]);

        Assert.Equal(A, playlist.Next(null));
        Assert.Null(playlist.Next(A));
        Assert.Equal([null, A], playlist.Usable([null, A, B]).Paths!);
    }

    [Fact]
    public void Deleted_themes_drop_out_and_order_stays()
    {
        var usable = new ThemePlaylist(true, [C, "gone.json", A]).Usable([A, B, C]);

        Assert.True(usable.Enabled);
        Assert.Equal([C, A], usable.Paths!);
    }

    [Fact]
    public void Too_few_themes_turns_it_off()
    {
        Assert.False(new ThemePlaylist(true, [A, "gone.json"]).Usable([A, B]).Enabled);
    }

    [Fact]
    public void Repeats_are_dropped()
    {
        var usable = new ThemePlaylist(true, [A, A, B, C, B]).Usable([A, B, C]);

        Assert.Equal([A, B, C], usable.Paths!);
    }

    [Fact]
    public void Every_theme_can_be_in_the_playlist()
    {
        var themes = Enumerable.Range(1, 14).Select(i => (string?)$"theme{i}.json").Prepend(null).ToList();

        var usable = new ThemePlaylist(true, themes).Usable(themes);

        Assert.Equal(themes, usable.Paths!);
        Assert.True(usable.Enabled);
    }

    [Theory]
    [InlineData(1, ThemePlaylist.MinSeconds)]
    [InlineData(45, 45)]
    [InlineData(99999, ThemePlaylist.MaxSeconds)]
    public void Timer_stays_in_range(int seconds, int expected)
    {
        Assert.Equal(expected, new ThemePlaylist(Seconds: seconds).Usable([]).Seconds);
    }

    [Fact]
    public void Renamed_theme_keeps_its_place()
    {
        Assert.Equal([A, "new.json", C], new ThemePlaylist(true, [A, B, C]).Renamed(B, "new.json").Paths!);
    }

    static Color Center(byte[] jpeg)
    {
        using var bitmap = new Bitmap(new MemoryStream(jpeg));
        return bitmap.GetPixel(240, 240);
    }

    [Fact]
    public void Fade_starts_on_the_old_frame_and_ends_on_the_new_one()
    {
        using var red = Backgrounds.Create(new BackgroundSpec(BackgroundKind.Color, "#FF0000"));
        using var blue = Backgrounds.Create(new BackgroundSpec(BackgroundKind.Color, "#0000FF"));
        using var renderer = new ThemeRenderer();

        renderer.Render(red, [], Stats.Empty, DateTime.Now);
        renderer.BeginFade();
        var start = Center(renderer.Render(blue, [], Stats.Empty, DateTime.Now));
        Assert.True(renderer.IsAnimating);
        Assert.True(start.R > 200 && start.B < 60);

        Thread.Sleep(1000);
        var end = Center(renderer.Render(blue, [], Stats.Empty, DateTime.Now));
        Assert.False(renderer.IsAnimating);
        Assert.True(end.B > 200 && end.R < 40);
    }

    [Fact]
    public void Fade_passes_through_a_mix_of_both_themes()
    {
        using var red = Backgrounds.Create(new BackgroundSpec(BackgroundKind.Color, "#FF0000"));
        using var blue = Backgrounds.Create(new BackgroundSpec(BackgroundKind.Color, "#0000FF"));
        using var renderer = new ThemeRenderer();

        renderer.Render(red, [], Stats.Empty, DateTime.Now);
        renderer.BeginFade();
        Thread.Sleep(400);
        var middle = Center(renderer.Render(blue, [], Stats.Empty, DateTime.Now));

        Assert.True(middle.R > 40 && middle.B > 40, middle.ToString());
    }
}
