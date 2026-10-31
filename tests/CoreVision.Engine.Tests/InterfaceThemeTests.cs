using CoreVision.Engine;
using Xunit;

public class InterfaceThemeTests
{
    [Fact]
    public void Reads_a_theme_in_the_shape_the_interface_sends()
    {
        const string json = """
            {
              "name": "My Theme", "dim": 0.35,
              "background": { "kind": "Image", "color": "#000000", "path": "C:\\Pictures\\a.jpg" },
              "elements": [
                { "kind": "Metric", "x": 240, "y": 200, "size": 40, "metric": "GpuTemp", "unit": "\u00B0", "align": "Center", "bold": true },
                { "kind": "Text", "x": 10.5, "y": 20, "size": 26, "text": "Hello" },
                { "kind": "Clock", "x": 0, "y": 0, "size": 26, "format": "h:mm tt" }
              ]
            }
            """;

        var theme = Theme.FromJson(json);

        Assert.Equal(0.35f, theme.Dim);
        Assert.Equal(BackgroundKind.Image, theme.Background.Kind);
        Assert.Equal(Metric.GpuTemp, theme.Elements[0].Metric);
        Assert.Equal(TextAlign.Center, theme.Elements[0].Align);
        Assert.Equal(10.5f, theme.Elements[1].X);
        Assert.Equal("#FFFFFF", theme.Elements[1].Color);
        Assert.Equal("h:mm tt", theme.Elements[2].Format);
        Assert.Equal(theme.Dim, Theme.FromJson(theme.ToJson()).Dim);
    }

    [Fact]
    public void Delete_never_leaves_the_themes_folder()
    {
        var folder = Directory.CreateTempSubdirectory("corevision");
        try
        {
            var themes = Directory.CreateDirectory(Path.Combine(folder.FullName, "themes")).FullName;
            var outside = Path.Combine(folder.FullName, "keep.json");
            File.WriteAllText(outside, "{}");
            var store = new ThemeStore(themes);
            var inside = store.Save(Theme.Default with { Name = "Mine" });

            store.Delete(outside);
            store.Delete(Path.Combine(themes, "..", "keep.json"));
            store.Delete(inside);

            Assert.True(File.Exists(outside));
            Assert.False(File.Exists(inside));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void Live_edits_keep_the_background_open()
    {
        var opens = 0;
        var frames = 0;
        var theme = Theme.Default;
        using var player = new ThemePlayer(theme, () => Stats.Empty, _ => Interlocked.Increment(ref frames), spec =>
        {
            Interlocked.Increment(ref opens);
            return Backgrounds.Create(spec);
        });
        player.Start();
        WaitFor(() => Volatile.Read(ref frames) >= 1);

        var before = Volatile.Read(ref frames);
        player.Play(theme with { Dim = 0.5f, Elements = [] });

        WaitFor(() => Volatile.Read(ref frames) > before); // the frame arrives at once, not after the 1 s idle delay
        Assert.Equal(1, Volatile.Read(ref opens));
    }

    static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(500);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            Thread.Sleep(5);
        }
    }
}
