using TRStudio.Engine;
using Xunit;

public sealed class LightLayoutTests
{
    static void Near(double expected, double actual) => Assert.True(Math.Abs(expected - actual) < 1e-9, $"expected {expected}, got {actual}");

    [Fact]
    public void Leds_are_spread_evenly_along_a_wide_box_from_left_to_right()
    {
        var points = new LightPlacement(0, 90, 320, 20).Points(4);

        Assert.Equal(4, points.Length);
        Near(0.125, points[0].X);
        Near(0.375, points[1].X);
        Near(0.875, points[3].X);
        Assert.All(points, point => Near(0.5, point.Y));
    }

    [Fact]
    public void A_tall_box_runs_its_leds_from_top_to_bottom()
    {
        var points = new LightPlacement(150, 0, 20, 200).Points(2);

        Near(0.25, points[0].Y);
        Near(0.75, points[1].Y);
        Assert.All(points, point => Near(0.5, point.X));
    }

    [Fact]
    public void Turning_a_box_turns_its_leds_around_its_middle()
    {
        var box = new LightPlacement(110, 90, 100, 20); // middle at 160, 100

        var quarter = (box with { Rotation = 90 }).Points(2);
        var half = (box with { Rotation = 180 }).Points(2);

        // A quarter turn stands the strip upright, first LED at the top.
        Near(0.5, quarter[0].X);
        Near((100 - 25) / 200.0, quarter[0].Y);
        Near((100 + 25) / 200.0, quarter[1].Y);
        // A half turn swaps the ends.
        Near((160 + 25) / 320.0, half[0].X);
        Near((160 - 25) / 320.0, half[1].X);
    }

    [Fact]
    public void A_single_led_sits_in_the_middle_of_its_box_and_no_leds_gives_no_points()
    {
        var box = new LightPlacement(100, 50, 40, 40, 45);

        var point = Assert.Single(box.Points(1));

        Near(120 / 320.0, point.X);
        Near(70 / 200.0, point.Y);
        Assert.Empty(box.Points(0));
    }

    [Fact]
    public void Leds_hanging_off_the_canvas_take_its_edge()
    {
        var points = new LightPlacement(-60, 190, 120, 40).Points(3);

        Assert.All(points, point => Assert.InRange(point.X, 0, 1));
        Assert.All(points, point => Assert.InRange(point.Y, 0, 1));
        Assert.Equal(0, points[0].X);
        Assert.Equal(1, points[0].Y);
    }

    [Fact]
    public void A_box_is_kept_a_sensible_size_and_within_reach()
    {
        var tidy = new LightPlacement(5000, -5000, 0.5, 9000, -90, 400).Clamped();

        Assert.Equal(LightPlacement.MinSize, tidy.Width);
        Assert.Equal(LightPlacement.CanvasHeight, tidy.Height);
        Assert.InRange(tidy.X, 0, LightPlacement.CanvasWidth - 1); // some of it is still on the canvas to grab
        Assert.True(tidy.Y + tidy.Height > 0);
        Assert.Equal(270, tidy.Rotation);
        Assert.Equal(100, tidy.Brightness);

        var broken = new LightPlacement(double.NaN, double.PositiveInfinity, double.NaN, double.NaN, double.NaN).Clamped();
        Assert.True(double.IsFinite(broken.X) && double.IsFinite(broken.Y) && double.IsFinite(broken.Width) && double.IsFinite(broken.Rotation));
    }

    [Fact]
    public void Unarranged_strips_get_a_row_each_inside_the_canvas()
    {
        var zones = Enumerable.Range(0, 12).Select(i => ($"zone {i}", i * 10, LightShape.Strip)).ToList();

        var layout = LightPlacement.Arrange(zones);

        Assert.Equal(12, layout.Count);
        Assert.Equal(12, layout.Values.Select(box => box.Y).Distinct().Count());
        Assert.All(layout.Values, box =>
        {
            Assert.True(box.X >= 0 && box.X + box.Width <= LightPlacement.CanvasWidth, $"{box}");
            Assert.True(box.Y >= 0 && box.Y + box.Height <= LightPlacement.CanvasHeight, $"{box}");
            Assert.True(box.Width >= box.Height); // strips lie flat, LEDs left to right
        });
        Assert.True(layout["zone 11"].Width > layout["zone 1"].Width); // more LEDs, wider strip
        Assert.Empty(LightPlacement.Arrange([]));
    }

    [Fact]
    public void A_ring_puts_its_leds_round_the_oval_from_the_top_clockwise()
    {
        var box = new LightPlacement(100, 50, 80, 40);

        var points = box.Points(4, LightShape.Ring).Select(point => (X: point.X * LightPlacement.CanvasWidth, Y: point.Y * LightPlacement.CanvasHeight)).ToArray();

        (double X, double Y)[] expected = [(140, 50), (180, 70), (140, 90), (100, 70)]; // top, right, bottom, left
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(expected[i].X, points[i].X, 6);
            Assert.Equal(expected[i].Y, points[i].Y, 6);
        }
    }

    [Fact]
    public void Turning_a_ring_moves_where_its_first_led_sits()
    {
        var turned = new LightPlacement(100, 50, 40, 40, Rotation: 90).Points(12, LightShape.Ring);

        Assert.Equal(140, turned[0].X * LightPlacement.CanvasWidth, 6); // the first LED is now on the right
        Assert.Equal(70, turned[0].Y * LightPlacement.CanvasHeight, 6);
        Assert.Equal(12, turned.Distinct().Count());
    }

    [Fact]
    public void Unarranged_rings_get_square_boxes_that_do_not_overlap()
    {
        var zones = Enumerable.Range(0, 9).Select(i => ($"fan {i}", 12, LightShape.Ring)).Append(("strip", 30, LightShape.Strip)).ToList();

        var layout = LightPlacement.Arrange(zones);

        var rings = zones.Take(9).Select(zone => layout[zone.Item1]).ToList();
        Assert.All(rings, box =>
        {
            Assert.Equal(box.Width, box.Height);
            Assert.True(box.X >= 0 && box.X + box.Width <= LightPlacement.CanvasWidth && box.Y >= 0 && box.Y + box.Height <= LightPlacement.CanvasHeight, $"{box}");
        });
        Assert.Equal(9, rings.Select(box => (box.X, box.Y)).Distinct().Count());
        Assert.True(layout["strip"].Width > layout["strip"].Height);
    }
}
