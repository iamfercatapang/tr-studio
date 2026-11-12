using System.Drawing;
using TRStudio.Engine;
using Xunit;

public class GaugeTests
{
    // Starts at the top and runs clockwise, so the right half fills at 50%.
    static ElementSpec Gauge => new(ElementKind.Gauge, 240, 240, 400)
    {
        Metric = Metric.CpuLoad, Min = 0, Max = 100, StartAngle = -90, Sweep = 360,
        Color = "#FF0000", EndColor = "#FF0000", TrackColor = "#00FF00", Dot = false, Thickness = 30,
    };

    static Bitmap Render(ElementSpec gauge, RawStats raw)
    {
        using var renderer = new ThemeRenderer();
        using var background = Backgrounds.Create(new BackgroundSpec());
        return new Bitmap(new MemoryStream(renderer.Render(background, [gauge], Stats.From(raw), DateTime.Now)));
    }

    static bool IsRed(Color c) => c.R > 180 && c.G < 90;
    static bool IsGreen(Color c) => c.G > 180 && c.R < 90;

    // On the ring, at 3 and 9 o'clock.
    const int RingRight = 425, RingLeft = 55;

    [Fact]
    public void Half_value_fills_half_the_ring()
    {
        using var bitmap = Render(Gauge, new RawStats(CpuLoad: 50));
        Assert.True(IsRed(bitmap.GetPixel(RingRight, 240)));
        Assert.True(IsGreen(bitmap.GetPixel(RingLeft, 200)));
    }

    [Fact]
    public void Full_value_fills_the_whole_ring()
    {
        using var bitmap = Render(Gauge, new RawStats(CpuLoad: 100));
        Assert.True(IsRed(bitmap.GetPixel(RingLeft, 200)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(500.0)] // out of range for a load reading
    public void Untrusted_reading_shows_an_empty_ring(double? load)
    {
        using var bitmap = Render(Gauge, new RawStats(CpuLoad: load));
        Assert.True(IsGreen(bitmap.GetPixel(RingRight, 240)));
    }

    [Fact]
    public void Tip_color_follows_the_value()
    {
        var gauge = Gauge with { Color = "#0000FF", EndColor = "#FF0000" };
        using var low = Render(gauge, new RawStats(CpuLoad: 25));
        using var high = Render(gauge, new RawStats(CpuLoad: 100));
        Assert.True(low.GetPixel(RingRight, 235).B > low.GetPixel(RingRight, 235).R); // a quarter round is still blue
        Assert.True(high.GetPixel(RingLeft, 240).R > high.GetPixel(RingLeft, 240).B); // three quarters round is red
    }

    [Fact]
    public void Negative_sweep_fills_counterclockwise()
    {
        using var bitmap = Render(Gauge with { Sweep = -360 }, new RawStats(CpuLoad: 50));
        Assert.True(IsRed(bitmap.GetPixel(RingLeft, 240)));
        Assert.True(IsGreen(bitmap.GetPixel(RingRight, 200)));
    }

    [Fact]
    public void Dot_sits_apart_from_the_tip_with_gaps_around_it()
    {
        // Half full at the bottom of a clockwise ring from the top: walk on from the tip up its left side.
        var gauge = Gauge with { Dot = true, Thickness = 40 };
        using var bitmap = Render(gauge, new RawStats(CpuLoad: 50));
        const int radius = 180;
        var colors = Enumerable.Range(0, 40).Select(degrees =>
        {
            var radians = (90 + degrees) * Math.PI / 180;
            return bitmap.GetPixel(240 + (int)(radius * Math.Cos(radians)), 240 + (int)(radius * Math.Sin(radians)));
        }).ToList();
        static bool IsDark(Color c) => c.R < 70 && c.G < 70 && c.B < 70;

        var firstGap = colors.FindIndex(IsDark);
        var dot = colors.FindIndex(firstGap, IsRed);
        var secondGap = colors.FindIndex(dot, IsDark);
        var track = colors.FindIndex(secondGap, c => c.G > 100);
        Assert.True(firstGap >= 0 && dot > firstGap && secondGap > dot && track > secondGap);
    }

    [Fact]
    public void Dot_disappears_when_the_ring_is_full()
    {
        var gauge = Gauge with { Dot = true, Sweep = 270 };
        using var bitmap = Render(gauge, new RawStats(CpuLoad: 100));
        Assert.Equal(480, bitmap.Width);
    }

    [Theory]
    [InlineData(50, 0, 100, 0.5)]
    [InlineData(-10, 0, 100, 0)]
    [InlineData(900, 0, 100, 1)]
    [InlineData(60, 20, 100, 0.5)]
    [InlineData(50, 100, 100, 0)] // a broken range must not divide by zero
    [InlineData(50, 100, 0, 0)]
    public void Maps_values_onto_the_range(double value, float min, float max, double share)
    {
        Assert.Equal(share, ThemeRenderer.GaugeShare(value, min, max));
    }

    [Fact]
    public void Ring_eases_toward_a_new_reading()
    {
        using var renderer = new ThemeRenderer();
        using var background = Backgrounds.Create(new BackgroundSpec());
        ElementSpec[] gauge = [Gauge];
        renderer.Render(background, gauge, Stats.From(new RawStats(CpuLoad: 10)), DateTime.Now);
        Assert.False(renderer.IsAnimating); // the first frame starts on the reading

        renderer.Render(background, gauge, Stats.From(new RawStats(CpuLoad: 90)), DateTime.Now);
        Assert.True(renderer.IsAnimating);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (renderer.IsAnimating)
        {
            Assert.True(DateTime.UtcNow < deadline);
            Thread.Sleep(20);
            renderer.Render(background, gauge, Stats.From(new RawStats(CpuLoad: 90)), DateTime.Now);
        }
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(10f, 500f)]
    [InlineData(1000f, 1f)]
    public void Odd_sizes_do_not_fail_a_frame(float size, float thickness)
    {
        using var bitmap = Render(Gauge with { Size = size, Thickness = thickness }, new RawStats(CpuLoad: 50));
        Assert.Equal(480, bitmap.Width);
    }

    [Fact]
    public void Gauge_options_survive_saving()
    {
        var gauge = Gauge with { Min = 20, Max = 95, StartAngle = 10, Sweep = 200, EndColor = "#123456", TrackColor = "#654321", Dot = false };
        var theme = new Theme("A", new BackgroundSpec(), [gauge]);
        Assert.Equal(gauge, Theme.FromJson(theme.ToJson()).Elements[0]);
    }

    [Fact]
    public void Built_in_themes_load_list_and_cannot_be_deleted()
    {
        var folder = Directory.CreateTempSubdirectory("trstudio");
        try
        {
            var store = new ThemeStore(folder.FullName);
            var listed = store.List().Where(entry => entry.Path?.StartsWith(Theme.BuiltInPrefix) == true).ToList();
            Assert.Equal(Theme.BuiltIns.Count, listed.Count);
            foreach (var entry in listed)
            {
                Assert.Equal(entry.Name, store.Load(entry.Path).Name);
                store.Delete(entry.Path!);
            }
            Assert.Equal(Theme.Default, store.Load(Theme.BuiltInPrefix + "Missing"));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void Built_in_rings_fit_the_round_screen()
    {
        foreach (var gauge in Theme.BuiltIns.SelectMany(theme => theme.Elements).Where(item => item.Kind == ElementKind.Gauge))
            Assert.True(MathF.Sqrt(MathF.Pow(gauge.X - 240, 2) + MathF.Pow(gauge.Y - 240, 2)) + gauge.Size / 2 <= 240);
    }

    [Fact]
    public void New_stats_are_range_checked_and_stale_with_the_rest()
    {
        var stats = Stats.From(new RawStats(PumpRpm: 2428, CaseFanRpm: 0, CpuClock: 0, MotherboardTemp: 40.5, GpuHotSpot: 300));
        Assert.Equal(2428, stats.Get(Metric.PumpRpm).Value);
        Assert.Equal(0, stats.Get(Metric.CaseFanRpm).Value); // a stopped fan is real
        Assert.Equal(ReadingState.Invalid, stats.Get(Metric.CpuClock).State); // a false 0 without admin rights
        Assert.Equal(ReadingState.Invalid, stats.Get(Metric.GpuHotSpot).State);
        Assert.Equal(ReadingState.Missing, stats.Get(Metric.GpuFan).State);
        foreach (var metric in Enum.GetValues<Metric>().Where(metric => metric is not (Metric.MusicProgress or Metric.OutdoorTemp))) // not from sensors
            Assert.Equal(ReadingState.Stale, stats.AsStale().Get(metric).State);
    }
}
