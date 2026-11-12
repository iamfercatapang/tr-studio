namespace TRStudio.Engine;

/// <summary>
/// The smooth effects. Each is a plain function of time and of where the LED sits along its strip
/// (0 at the start, up to but not including 1), so any frame rate gives the same motion and every
/// device stays in step. Effects that travel along the strip join up at its ends, for fans and rings.
/// </summary>
public static class LightEffects
{
    const double Tau = Math.PI * 2;
    const double SlowestHz = 0.04;
    const double SpeedDoubling = 20; // every 20 points of speed doubles the pace
    const double CometTail = 6;
    const double CometLead = 0.04;

    /// <summary>How many times a second the effect repeats. Speed 0 to 100 gives 0.04 to 1.28.</summary>
    public static double Rate(int speed) => SlowestHz * Math.Pow(2, Math.Clamp(speed, 0, 100) / SpeedDoubling);

    public static Rgb At(SmoothEffect effect, double seconds, double position, IReadOnlyList<Rgb> colors, int speed)
    {
        if (colors.Count == 0) colors = [Rgb.Parse(LightChoice.DefaultColor)];
        var turns = seconds * Rate(speed);
        return effect switch
        {
            SmoothEffect.ColorCycle => Hue(turns / 2),
            SmoothEffect.Breathing => Breathing(turns, colors),
            SmoothEffect.GradientFlow => Loop(colors, position + turns),
            SmoothEffect.Aurora => Aurora(turns, position, colors),
            SmoothEffect.Comet => Comet(turns, position, colors),
            _ => Hue(position - turns),
        };
    }

    /// <summary>Fills a strip. The same call at the same time always gives the same colors.</summary>
    public static void Fill(Span<Rgb> leds, SmoothEffect effect, double seconds, IReadOnlyList<Rgb> colors, int speed, double brightness)
    {
        for (var i = 0; i < leds.Length; i++)
            leds[i] = At(effect, seconds, (double)i / leds.Length, colors, speed).Scaled(brightness);
    }

    public static double Wrap(double value) => value - Math.Floor(value);

    /// <summary>A full-strength color from around the color wheel.</summary>
    public static Rgb Hue(double turn)
    {
        var h = Wrap(turn) * 6;
        double Channel(double offset)
        {
            var k = (offset + h) % 6;
            return 1 - Math.Max(0, Math.Min(Math.Min(k, 4 - k), 1));
        }
        return FromUnit(Channel(5), Channel(3), Channel(1));
    }

    static Rgb FromUnit(double r, double g, double b) =>
        new((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));

    /// <summary>Fades in and out, moving to the next color while dark.</summary>
    static Rgb Breathing(double turns, IReadOnlyList<Rgb> colors)
    {
        var color = colors[(int)(Math.Floor(turns) % colors.Count)];
        var level = 0.5 - 0.5 * Math.Cos(Wrap(turns) * Tau);
        return color.Scaled(level * level); // squared, because eyes notice the dim end more
    }

    /// <summary>The colors in a ring: the last blends back into the first.</summary>
    static Rgb Loop(IReadOnlyList<Rgb> colors, double turn)
    {
        if (colors.Count == 1) return colors[0];
        var place = Wrap(turn) * colors.Count;
        var index = (int)place % colors.Count;
        return Mix(colors[index], colors[(index + 1) % colors.Count], place - Math.Floor(place));
    }

    /// <summary>The colors end to end, first at 0 and last at 1.</summary>
    static Rgb Ramp(IReadOnlyList<Rgb> colors, double share)
    {
        if (colors.Count == 1) return colors[0];
        var place = Math.Clamp(share, 0, 1) * (colors.Count - 1);
        var index = Math.Min((int)place, colors.Count - 2);
        return Mix(colors[index], colors[index + 1], place - index);
    }

    /// <summary>Slow overlapping waves, each drifting at its own pace, so the pattern never visibly repeats.</summary>
    static Rgb Aurora(double turns, double position, IReadOnlyList<Rgb> colors)
    {
        var x = position * Tau;
        var t = turns * Tau;
        var tone = 0.5 + 0.26 * Math.Sin(x + t * 0.6) + 0.15 * Math.Sin(2 * x - t * 0.37 + 1.3) + 0.09 * Math.Sin(3 * x + t * 0.23 + 4.1);
        var glow = 0.7 + 0.3 * Math.Sin(2 * x + t * 0.29 + 2.2);
        return Ramp(colors, tone).Scaled(glow);
    }

    /// <summary>A bright head circling the strip with a fading tail. The tail cools toward the second color.</summary>
    static Rgb Comet(double turns, double position, IReadOnlyList<Rgb> colors)
    {
        var behind = Wrap(turns - position);
        var ahead = 1 - behind;
        var level = Math.Max(Math.Exp(-behind * CometTail), 1 - ahead / CometLead);
        // The glow just ahead of the head is the head's color; by then the old tail there has faded out.
        var cooled = ahead < CometLead ? 0 : Math.Min(1, behind * 2);
        var color = colors.Count > 1 ? Mix(colors[0], colors[1], cooled) : colors[0];
        return color.Scaled(level);
    }

    /// <summary>
    /// A straight blend of the channel values. An LED's light follows its value directly, unlike a monitor's,
    /// so this is the even fade between two colors, with no jump near the dark end.
    /// </summary>
    public static Rgb Mix(Rgb from, Rgb to, double share)
    {
        share = Math.Clamp(share, 0, 1);
        byte Blend(byte a, byte b) => (byte)Math.Round(a + (b - a) * share);
        return new Rgb(Blend(from.R, to.R), Blend(from.G, to.G), Blend(from.B, to.B));
    }
}
