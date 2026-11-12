using System.Drawing;
using TRStudio.Engine;
using Xunit;

public sealed class ShapeTests
{
    static Bitmap Render(ElementSpec element, Stats? stats = null)
    {
        using var background = Backgrounds.Create(new BackgroundSpec(BackgroundKind.Color, "#000000"));
        using var renderer = new ThemeRenderer();
        return new Bitmap(new MemoryStream(renderer.Render(background, [element], stats ?? Stats.Empty, DateTime.Now)));
    }

    static bool IsLit(Color pixel) => pixel.R + pixel.G + pixel.B > 120;

    static Stats StatsWith(double cpuTemp) => Stats.From(new RawStats(CpuTemp: cpuTemp));

    static ElementSpec Shape(ShapeKind kind) =>
        new(ElementKind.Shape, 240, 240, 200) { Shape = kind, Height = 200, Thickness = 20, Color = "#FFFFFF", Sweep = 270, StartAngle = 135 };

    [Theory]
    [InlineData(ShapeKind.Rectangle, 150, 150, true)]
    [InlineData(ShapeKind.Ellipse, 150, 150, false)]
    [InlineData(ShapeKind.Diamond, 170, 170, false)]
    [InlineData(ShapeKind.Triangle, 240, 160, true)]
    [InlineData(ShapeKind.Triangle, 160, 160, false)]
    [InlineData(ShapeKind.Hexagon, 240, 240, true)]
    [InlineData(ShapeKind.Star, 240, 150, true)]
    [InlineData(ShapeKind.Star, 170, 300, false)]
    public void Shapes_cover_their_own_outline(ShapeKind kind, int x, int y, bool lit)
    {
        using var bitmap = Render(Shape(kind));

        Assert.Equal(lit, IsLit(bitmap.GetPixel(x, y)));
    }

    [Fact]
    public void Line_runs_along_its_length_and_turns_with_its_rotation()
    {
        var line = Shape(ShapeKind.Line) with { Thickness = 10 };
        using var flat = Render(line);
        using var upright = Render(line with { Rotation = 90 });

        Assert.True(IsLit(flat.GetPixel(320, 240)));
        Assert.False(IsLit(flat.GetPixel(240, 320)));
        Assert.True(IsLit(upright.GetPixel(240, 320)));
        Assert.False(IsLit(upright.GetPixel(320, 240)));
    }

    [Fact]
    public void Dashed_line_has_gaps_a_solid_line_does_not()
    {
        var line = Shape(ShapeKind.Line) with { Thickness = 10, Size = 300 };
        int LitAlong(ElementSpec spec)
        {
            using var bitmap = Render(spec);
            return Enumerable.Range(100, 280).Count(x => IsLit(bitmap.GetPixel(x, 240)));
        }

        var solid = LitAlong(line);
        Assert.Equal(280, solid);
        Assert.InRange(LitAlong(line with { LineStyle = LineStyle.Dashed }), 100, 220);
        Assert.InRange(LitAlong(line with { LineStyle = LineStyle.Dotted }), 40, 160);
    }

    [Fact]
    public void Arc_leaves_its_opening_empty()
    {
        using var bitmap = Render(Shape(ShapeKind.Arc));

        Assert.True(IsLit(bitmap.GetPixel(240, 150)));
        Assert.False(IsLit(bitmap.GetPixel(240, 330))); // the gap faces down
        Assert.False(IsLit(bitmap.GetPixel(240, 240)));
    }

    [Fact]
    public void Unfilled_shape_shows_only_its_outline()
    {
        var outline = Shape(ShapeKind.Rectangle) with { Filled = false, Stroke = true, StrokeColor = "#FFFFFF", StrokeWidth = 10 };
        using var bitmap = Render(outline);

        Assert.False(IsLit(bitmap.GetPixel(240, 240)));
        Assert.True(IsLit(bitmap.GetPixel(140, 240)));
    }

    [Fact]
    public void Gradient_runs_from_color_to_end_color()
    {
        var shape = Shape(ShapeKind.Rectangle) with { Color = "#FF0000", EndColor = "#0000FF", Gradient = true };
        using var bitmap = Render(shape);

        Assert.True(bitmap.GetPixel(150, 240).R > 200);
        Assert.True(bitmap.GetPixel(330, 240).B > 200);
    }

    [Fact]
    public void Opacity_fades_the_shape()
    {
        using var bitmap = Render(Shape(ShapeKind.Rectangle) with { Opacity = 0.5f });

        Assert.InRange(bitmap.GetPixel(240, 240).R, 110, 145);
    }

    [Fact]
    public void Bar_fills_to_its_reading()
    {
        var bar = new ElementSpec(ElementKind.Bar, 240, 240, 300) { Metric = Metric.CpuTemp, Min = 0, Max = 100, Thickness = 30, Color = "#FF0000", EndColor = "#FF0000", TrackColor = "#000000" };
        using var half = Render(bar, StatsWith(50));

        Assert.True(IsLit(half.GetPixel(200, 240)));
        Assert.False(IsLit(half.GetPixel(300, 240)));
    }

    [Fact]
    public void Bar_with_no_reading_shows_only_its_track()
    {
        var bar = new ElementSpec(ElementKind.Bar, 240, 240, 300) { Thickness = 30, Color = "#FF0000", EndColor = "#FF0000", TrackColor = "#00FF00" };
        using var bitmap = Render(bar);

        var pixel = bitmap.GetPixel(120, 240);
        Assert.True(pixel.G > 200 && pixel.R < 60);
    }

    [Fact]
    public void Rotated_bar_can_stand_upright()
    {
        var bar = new ElementSpec(ElementKind.Bar, 240, 240, 300) { Rotation = 90, Metric = Metric.CpuTemp, Min = 0, Max = 100, Thickness = 30, Color = "#FF0000", EndColor = "#FF0000", TrackColor = "#000000" };
        using var bitmap = Render(bar, StatsWith(50));

        Assert.True(IsLit(bitmap.GetPixel(240, 150))); // zero is at the top after a quarter turn
        Assert.False(IsLit(bitmap.GetPixel(240, 330)));
    }

    [Fact]
    public void Gauge_without_track_color_shows_its_gradient_past_the_reading()
    {
        // Starts at the top and runs clockwise, so a low reading leaves the left half unfilled.
        var gauge = new ElementSpec(ElementKind.Gauge, 240, 240, 400) { Metric = Metric.CpuTemp, Min = 0, Max = 100, StartAngle = -90, Sweep = 360, Thickness = 30, Color = "#FF0000", EndColor = "#FF0000", TrackColor = "#00FF00", UseTrackColor = false };
        using var bitmap = Render(gauge, StatsWith(20));

        var unfilled = bitmap.GetPixel(55, 240);
        Assert.True(unfilled.R > 180 && unfilled.G < 90, unfilled.ToString());
    }

    [Fact]
    public void Bar_without_track_color_has_a_marker_at_the_reading_and_gradient_beyond()
    {
        var bar = new ElementSpec(ElementKind.Bar, 240, 240, 300) { Metric = Metric.CpuTemp, Min = 0, Max = 100, Thickness = 20, Color = "#FF0000", EndColor = "#FF0000", TrackColor = "#00FF00", UseTrackColor = false };
        using var bitmap = Render(bar, StatsWith(50));

        Assert.True(IsLit(bitmap.GetPixel(240, 227))); // the marker stands taller than the bar
        Assert.False(IsLit(bitmap.GetPixel(200, 227)));
        var beyond = bitmap.GetPixel(350, 240);
        Assert.True(beyond.R > 180 && beyond.G < 90, beyond.ToString());
    }

    [Theory]
    [InlineData(ShapeKind.Star)]
    [InlineData(ShapeKind.Arc)]
    public void Shape_settings_survive_a_json_round_trip(ShapeKind kind)
    {
        var element = Shape(kind) with { Rotation = 45, LineStyle = LineStyle.Dotted, Filled = false, Gradient = true, Opacity = 0.4f, Height = 33 };
        var loaded = Theme.FromJson((Theme.Default with { Elements = [element] }).ToJson());

        Assert.Equal(element, loaded.Elements[0]);
    }

    [Fact]
    public void Dual_Bar_shows_both_temperatures_in_numbers_and_bars()
    {
        var theme = Theme.BuiltIns.Single(t => t.Name == "Dual Bar");

        Assert.Equal([Metric.CpuTemp, Metric.GpuTemp], theme.Elements.Where(e => e.Kind == ElementKind.Bar).Select(e => e.Metric));
        Assert.Equal([Metric.CpuTemp, Metric.GpuTemp], theme.Elements.Where(e => e.Kind == ElementKind.Metric).Select(e => e.Metric));
    }

    [Fact]
    public void Gauge_motion_has_no_jump_when_a_new_reading_arrives()
    {
        const double frame = 1 / 30.0;
        var (shown, speed) = (0.2, 0.0);
        var largestStep = 0.0;
        var target = 0.8;
        for (var i = 0; i < 90; i++)
        {
            if (i == 30) target = 0.6;
            if (i == 60) target = 0.7;
            var (next, nextSpeed) = ThemeRenderer.GaugeStep(shown, speed, target, frame);
            Assert.True(Math.Abs(nextSpeed - speed) < 0.7, $"speed jumped at frame {i}");
            largestStep = Math.Max(largestStep, Math.Abs(next - shown));
            (shown, speed) = (next, nextSpeed);
        }

        Assert.True(largestStep < 0.04, $"largest step was {largestStep}");
    }

    [Fact]
    public void Gauge_motion_arrives_without_overshooting()
    {
        var (shown, speed) = (0.0, 0.0);
        for (var i = 0; i < 120; i++)
        {
            (shown, speed) = ThemeRenderer.GaugeStep(shown, speed, 1, 1 / 30.0);
            Assert.True(shown <= 1);
        }

        Assert.InRange(shown, 0.99, 1);
    }

    [Fact]
    public void Gauge_keeps_moving_after_its_reading_changes()
    {
        var gauge = new ElementSpec(ElementKind.Gauge, 240, 240, 400) { Metric = Metric.CpuTemp, Min = 0, Max = 100 };
        using var background = Backgrounds.Create(new BackgroundSpec());
        using var renderer = new ThemeRenderer();

        renderer.Render(background, [gauge], StatsWith(20), DateTime.Now);
        renderer.Render(background, [gauge], StatsWith(80), DateTime.Now);

        Assert.True(renderer.IsAnimating);
    }
}

public sealed class LayerRotationTests
{
    static Bitmap Render(ElementSpec element)
    {
        using var background = Backgrounds.Create(new BackgroundSpec(BackgroundKind.Color, "#000000"));
        using var renderer = new ThemeRenderer();
        return new Bitmap(new MemoryStream(renderer.Render(background, [element], Stats.Empty, DateTime.Now)));
    }

    static bool AnyLit(Bitmap bitmap, int x, int y)
    {
        for (var dx = -5; dx <= 5; dx++)
            for (var dy = -5; dy <= 5; dy++)
                if (bitmap.GetPixel(x + dx, y + dy) is var pixel && pixel.R + pixel.G + pixel.B > 300) return true;
        return false;
    }

    static readonly ElementSpec Word = new(ElementKind.Text, 240, 200, 40) { Text = "WWWWWW", Align = TextAlign.Center, Bold = true };

    [Fact]
    public void Text_turns_around_the_middle_of_its_line()
    {
        var pivot = ThemeRenderer.Pivot(Word);
        Assert.Equal(new PointF(240, 227), pivot);

        using var flat = Render(Word);
        Assert.True(AnyLit(flat, 160, 227));
        Assert.False(AnyLit(flat, 240, 147));

        using var upright = Render(Word with { Rotation = 90 });
        Assert.True(AnyLit(upright, 240, 147));
        Assert.False(AnyLit(upright, 160, 227));
    }

    [Fact]
    public void Gauges_turn_around_their_center()
    {
        // A quarter ring on the right side, from 315 to 45 degrees, ends up at the bottom when turned 90 degrees.
        var gauge = new ElementSpec(ElementKind.Gauge, 240, 240, 300) { StartAngle = 315, Sweep = 90, Thickness = 20, Color = "#FFFFFF", EndColor = "#FFFFFF", TrackColor = "#FFFFFF", Dot = false };
        using var flat = Render(gauge);
        Assert.True(AnyLit(flat, 380, 240));
        Assert.False(AnyLit(flat, 240, 380));

        using var turned = Render(gauge with { Rotation = 90 });
        Assert.True(AnyLit(turned, 240, 380));
        Assert.False(AnyLit(turned, 380, 240));
    }
}
