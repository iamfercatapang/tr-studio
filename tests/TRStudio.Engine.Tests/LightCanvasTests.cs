using TRStudio.Engine;
using Xunit;

public sealed class LightCanvasTests
{
    const double Frame = 1.0 / LightingService.FramesPerSecond;

    static int Difference(Rgb a, Rgb b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    public static TheoryData<string> Effects => new(CanvasEffects.All.Select(effect => effect.Id));

    [Fact]
    public void Every_effect_has_its_own_id_and_a_name_and_settings_with_unique_keys()
    {
        Assert.Equal(CanvasEffects.All.Count, CanvasEffects.All.Select(effect => effect.Id).Distinct().Count());
        Assert.All(CanvasEffects.All, effect =>
        {
            Assert.False(string.IsNullOrWhiteSpace(effect.Name));
            Assert.False(string.IsNullOrWhiteSpace(effect.Description));
            Assert.Equal(effect.Params.Count, effect.Params.Select(parameter => parameter.Key).Distinct().Count());
        });
        Assert.Contains(CanvasEffects.All, effect => effect.Id == "sweep");
        Assert.Same(CanvasEffects.Default, CanvasEffects.Find("no such effect"));
    }

    [Theory]
    [MemberData(nameof(Effects))]
    public void Colors_move_a_little_from_one_frame_to_the_next(string id)
    {
        var effect = CanvasEffects.Find(id);
        var values = effect.Read(null);
        // Edges that are meant to be sharp, such as a comet's head or a beam's front, may move more.
        var allowed = id is "comet" or "sweep" or "radar" ? 140 : 24;
        for (var frame = 0; frame < 400; frame++)
        {
            var seconds = frame * Frame;
            for (var point = 0; point < 35; point++)
            {
                var (x, y) = ((point % 7 + 0.5) / 7, (point / 7 + 0.5) / 5);
                var jump = Difference(effect.Draw(x, y, seconds, values), effect.Draw(x, y, seconds + Frame, values));
                Assert.True(jump <= allowed, $"{id} jumped {jump} at {seconds:0.00}s, ({x:0.00}, {y:0.00})");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Effects))]
    public void The_same_moment_and_place_always_give_the_same_color(string id)
    {
        var effect = CanvasEffects.Find(id);
        var values = effect.Read(null);

        Assert.Equal(effect.Draw(0.3, 0.7, 12.5, values), effect.Draw(0.3, 0.7, 12.5, values));
        // The canvas corners and edges are valid places too.
        foreach (var (x, y) in new[] { (0.0, 0.0), (1.0, 1.0), (0.5, 0.5), (1.0, 0.0) }) effect.Draw(x, y, 3, values);
    }

    [Fact]
    public void Solid_color_stands_still_and_everything_else_moves()
    {
        Assert.False(CanvasEffects.Find("solid").Moving);
        Assert.All(CanvasEffects.All.Where(effect => effect.Id != "solid"), effect => Assert.True(effect.Moving));
    }

    [Fact]
    public void Rainbow_runs_left_to_right_or_top_to_bottom()
    {
        var effect = CanvasEffects.Find("rainbow");
        var across = effect.Read(null);
        var down = effect.Read(new Dictionary<string, string> { ["vertical"] = "true" });

        Assert.NotEqual(effect.Draw(0.1, 0.5, 0, across), effect.Draw(0.9, 0.5, 0, across));
        Assert.Equal(effect.Draw(0.5, 0.1, 0, across), effect.Draw(0.5, 0.9, 0, across));
        Assert.NotEqual(effect.Draw(0.5, 0.1, 0, down), effect.Draw(0.5, 0.9, 0, down));
        Assert.Equal(effect.Draw(0.1, 0.5, 0, down), effect.Draw(0.9, 0.5, 0, down));
    }

    [Fact]
    public void Sweep_is_a_bar_of_its_color_on_its_background_that_crosses_the_canvas()
    {
        var effect = CanvasEffects.Find("sweep");
        var values = effect.Read(new Dictionary<string, string> { ["color"] = "#FF0000", ["background"] = "#000010", ["width"] = "20", ["trail"] = "0" });
        var pass = 1 / LightEffects.Rate(50);

        // It starts at the left edge, is in the middle a quarter of the way through, and at the right edge halfway.
        Assert.Equal(new Rgb(255, 0, 0), effect.Draw(0.05, 0.5, 0, values));
        Assert.Equal(new Rgb(0, 0, 16), effect.Draw(0.9, 0.5, 0, values));
        Assert.Equal(new Rgb(255, 0, 0), effect.Draw(0.5, 0.5, pass * 0.25, values));
        Assert.Equal(new Rgb(255, 0, 0), effect.Draw(0.95, 0.2, pass * 0.5, values));
        Assert.Equal(new Rgb(0, 0, 16), effect.Draw(0.05, 0.2, pass * 0.5, values));
    }

    [Fact]
    public void A_sweeps_trail_follows_behind_it()
    {
        var effect = CanvasEffects.Find("sweep");
        var values = effect.Read(new Dictionary<string, string> { ["color"] = "#FFFFFF", ["background"] = "#000000", ["width"] = "10", ["trail"] = "100" });
        var seconds = 0.25 / LightEffects.Rate(50); // mid canvas, moving right

        var behind = effect.Draw(0.35, 0.5, seconds, values).R;
        var ahead = effect.Draw(0.65, 0.5, seconds, values).R;

        Assert.True(behind > ahead + 40, $"behind {behind}, ahead {ahead}");
    }

    [Fact]
    public void Settings_that_are_missing_or_damaged_fall_back_to_the_effects_own()
    {
        var effect = CanvasEffects.Find("sweep");

        var values = effect.Read(new Dictionary<string, string> { ["color"] = "not a color", ["width"] = "abc", ["speed"] = "250", ["vertical"] = "maybe" });

        Assert.Equal(Rgb.Parse("#FF0066"), values.Color("color"));
        Assert.Equal(12, values.Number("width"));
        Assert.Equal(100, values.Number("speed")); // past the top of its range
        Assert.False(values.On("vertical"));
        Assert.Equal(effect.Params.Count, values.ToText().Count);
        Assert.Equal("#FF0066", values.ToText()["color"]);
    }

    [Fact]
    public void A_picture_of_the_canvas_has_three_bytes_a_pixel_and_can_be_dimmed()
    {
        var effect = CanvasEffects.Find("solid");
        var values = effect.Read(new Dictionary<string, string> { ["color"] = "#204060" });

        var picture = CanvasEffects.Render(effect, values, 0, 6, 4);
        var dim = CanvasEffects.Render(effect, values, 0, 6, 4, 0.5);

        Assert.Equal(6 * 4 * 3, picture.Length);
        Assert.Equal([0x20, 0x40, 0x60], picture[^3..]);
        Assert.Equal([0x10, 0x20, 0x30], dim[..3]);
    }
}
