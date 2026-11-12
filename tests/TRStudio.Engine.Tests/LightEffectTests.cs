using TRStudio.Engine;
using Xunit;

public sealed class LightEffectTests
{
    static readonly IReadOnlyList<Rgb> Colors = [new(0, 229, 255), new(139, 92, 255), new(255, 61, 216)];
    const double Frame = 1.0 / LightingService.FramesPerSecond;

    static int Difference(Rgb a, Rgb b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    public static TheoryData<SmoothEffect> Effects => new(Enum.GetValues<SmoothEffect>());

    [Theory]
    [MemberData(nameof(Effects))]
    public void Colors_move_a_little_from_one_frame_to_the_next(SmoothEffect effect)
    {
        // The comet's head is meant to be a sharp edge, so it is measured away from it.
        for (var frame = 0; frame < 600; frame++)
        {
            var seconds = frame * Frame;
            for (var led = 0; led < 20; led++)
            {
                var position = led / 20.0;
                var now = LightEffects.At(effect, seconds, position, Colors, 50);
                var next = LightEffects.At(effect, seconds + Frame, position, Colors, 50);
                var allowed = effect == SmoothEffect.Comet ? 80 : 20;
                Assert.True(Difference(now, next) <= allowed, $"{effect} jumped {Difference(now, next)} at {seconds:0.00}s, LED {led}");
            }
        }
    }

    [Theory]
    [InlineData(SmoothEffect.RainbowWave)]
    [InlineData(SmoothEffect.GradientFlow)]
    [InlineData(SmoothEffect.Aurora)]
    public void A_ring_has_no_seam_where_its_ends_meet(SmoothEffect effect)
    {
        for (var seconds = 0.0; seconds < 20; seconds += 0.37)
        {
            var first = LightEffects.At(effect, seconds, 0, Colors, 50);
            var last = LightEffects.At(effect, seconds, 0.999, Colors, 50);
            Assert.True(Difference(first, last) <= 6, $"{effect} has a seam of {Difference(first, last)} at {seconds:0.00}s");
        }
    }

    [Fact]
    public void The_same_moment_always_gives_the_same_color()
    {
        Assert.Equal(LightEffects.At(SmoothEffect.Aurora, 12.5, 0.3, Colors, 70), LightEffects.At(SmoothEffect.Aurora, 12.5, 0.3, Colors, 70));
    }

    [Fact]
    public void Rainbow_runs_through_the_whole_color_wheel_along_the_strip()
    {
        Assert.Equal(new Rgb(255, 0, 0), LightEffects.At(SmoothEffect.RainbowWave, 0, 0, Colors, 50));
        Assert.Equal(new Rgb(0, 255, 0), LightEffects.At(SmoothEffect.RainbowWave, 0, 1 / 3.0, Colors, 50));
        Assert.Equal(new Rgb(0, 0, 255), LightEffects.At(SmoothEffect.RainbowWave, 0, 2 / 3.0, Colors, 50));
    }

    [Fact]
    public void Color_cycle_shows_one_color_on_every_led()
    {
        var leds = new Rgb[12];
        LightEffects.Fill(leds, SmoothEffect.ColorCycle, 3.3, Colors, 50, 1);
        Assert.All(leds, led => Assert.Equal(leds[0], led));
    }

    [Fact]
    public void Breathing_is_dark_between_breaths_and_changes_color_there()
    {
        var breath = 1 / LightEffects.Rate(50);
        Assert.Equal(Rgb.Black, LightEffects.At(SmoothEffect.Breathing, 0, 0, Colors, 50));
        Assert.Equal(Colors[0], LightEffects.At(SmoothEffect.Breathing, breath * 0.5, 0, Colors, 50));
        Assert.Equal(Colors[1], LightEffects.At(SmoothEffect.Breathing, breath * 1.5, 0, Colors, 50));
    }

    [Fact]
    public void Comet_is_brightest_at_its_head_and_fades_behind_it()
    {
        static int Light(Rgb color) => color.R + color.G + color.B;
        var seconds = 0.5 / LightEffects.Rate(50); // the head is halfway round
        var head = Light(LightEffects.At(SmoothEffect.Comet, seconds, 0.5, [Colors[0]], 50));
        var tail = Light(LightEffects.At(SmoothEffect.Comet, seconds, 0.3, [Colors[0]], 50));
        var ahead = Light(LightEffects.At(SmoothEffect.Comet, seconds, 0.7, [Colors[0]], 50));
        Assert.True(head > tail && tail > ahead, $"head {head}, tail {tail}, ahead {ahead}");
    }

    [Fact]
    public void Faster_speed_repeats_sooner()
    {
        Assert.True(LightEffects.Rate(100) > LightEffects.Rate(50));
        Assert.True(LightEffects.Rate(50) > LightEffects.Rate(0));
        Assert.Equal(LightEffects.Rate(100), LightEffects.Rate(500));
    }

    [Fact]
    public void Brightness_dims_every_led()
    {
        var full = new Rgb[8];
        var half = new Rgb[8];
        LightEffects.Fill(full, SmoothEffect.RainbowWave, 2, Colors, 50, 1);
        LightEffects.Fill(half, SmoothEffect.RainbowWave, 2, Colors, 50, 0.5);
        for (var i = 0; i < full.Length; i++) Assert.Equal(full[i].Scaled(0.5), half[i]);

        var off = new Rgb[8];
        LightEffects.Fill(off, SmoothEffect.RainbowWave, 2, Colors, 50, 0);
        Assert.All(off, led => Assert.Equal(Rgb.Black, led));
    }

    [Fact]
    public void Mixing_two_colors_meets_halfway()
    {
        var middle = LightEffects.Mix(new Rgb(255, 0, 0), new Rgb(0, 255, 0), 0.5);
        Assert.Equal(new Rgb(128, 128, 0), middle);
        Assert.Equal(new Rgb(255, 0, 0), LightEffects.Mix(new Rgb(255, 0, 0), new Rgb(0, 255, 0), 0));
        Assert.Equal(new Rgb(0, 255, 0), LightEffects.Mix(new Rgb(255, 0, 0), new Rgb(0, 255, 0), 1));
    }

    [Fact]
    public void A_color_reads_from_hex_and_writes_back()
    {
        Assert.Equal(new Rgb(0, 229, 255), Rgb.Parse("#00E5FF"));
        Assert.Equal("#00E5FF", new Rgb(0, 229, 255).ToHex());
        Assert.Equal(Rgb.Black, Rgb.Parse("nonsense"));
        Assert.Equal(new Rgb(1, 2, 3), Rgb.Parse(null, new Rgb(1, 2, 3)));
    }
}
