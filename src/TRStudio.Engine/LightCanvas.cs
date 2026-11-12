using System.Globalization;

namespace TRStudio.Engine;

public enum ParamKind { Color, Number, Toggle }

/// <summary>One setting of an effect, as the page shows it. Default is text: a hex color, a number, or "true"/"false".</summary>
public sealed record EffectParam(string Key, string Label, ParamKind Kind, string Default, int Min = 0, int Max = 100);

/// <summary>An effect's settings read once a frame, with anything missing or damaged replaced by its default.</summary>
public sealed class EffectValues
{
    readonly Dictionary<string, Rgb> _colors = [];
    readonly Dictionary<string, double> _numbers = [];
    readonly Dictionary<string, bool> _toggles = [];

    public EffectValues(IReadOnlyList<EffectParam> parameters, IReadOnlyDictionary<string, string>? saved)
    {
        foreach (var parameter in parameters)
        {
            var text = saved != null && saved.TryGetValue(parameter.Key, out var value) ? value : parameter.Default;
            switch (parameter.Kind)
            {
                case ParamKind.Color:
                    _colors[parameter.Key] = Rgb.Parse(text, Rgb.Parse(parameter.Default));
                    break;
                case ParamKind.Number:
                    var number = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed)
                        ? parsed : double.Parse(parameter.Default, CultureInfo.InvariantCulture);
                    _numbers[parameter.Key] = Math.Clamp(number, parameter.Min, parameter.Max);
                    break;
                default:
                    _toggles[parameter.Key] = bool.TryParse(text, out var on) ? on : parameter.Default == "true";
                    break;
            }
        }
    }

    public Rgb Color(string key) => _colors.GetValueOrDefault(key);
    public double Number(string key) => _numbers.GetValueOrDefault(key);
    public bool On(string key) => _toggles.GetValueOrDefault(key);

    /// <summary>The values as text, for the page and the settings file.</summary>
    public Dictionary<string, string> ToText()
    {
        var text = new Dictionary<string, string>();
        foreach (var (key, color) in _colors) text[key] = color.ToHex();
        foreach (var (key, number) in _numbers) text[key] = number.ToString("0.##", CultureInfo.InvariantCulture);
        foreach (var (key, on) in _toggles) text[key] = on ? "true" : "false";
        return text;
    }
}

/// <summary>
/// An effect drawn on the lighting canvas: a plain function of where (x and y from 0 to 1, left to right and
/// top to bottom) and when. Every LED takes the color at its own place on the canvas, so an effect travels
/// across the PC in the order the hardware is laid out. Moving is false for an effect that never changes.
/// </summary>
public sealed record CanvasEffect(string Id, string Name, string Description, IReadOnlyList<EffectParam> Params,
    Func<double, double, double, EffectValues, Rgb> Draw, bool Moving = true)
{
    public EffectValues Read(IReadOnlyDictionary<string, string>? saved) => new(Params, saved);
}

/// <summary>TR Studio's own effects. To add one, add an entry to <see cref="All"/>: its settings appear on the page by themselves.</summary>
public static class CanvasEffects
{
    const double Tau = Math.PI * 2;
    /// <summary>The canvas is wider than tall. Effects that turn or spread use this so circles stay round.</summary>
    const double Aspect = LightPlacement.CanvasWidth / LightPlacement.CanvasHeight;
    const string Cyan = "#00E5FF", Violet = "#8B5CFF", Magenta = "#FF3DD8", Black = "#000000";

    static EffectParam Color(string key, string label, string value) => new(key, label, ParamKind.Color, value);
    static EffectParam Number(string key, string label, int value, int min = 0, int max = 100) =>
        new(key, label, ParamKind.Number, value.ToString(CultureInfo.InvariantCulture), min, max);
    static EffectParam Toggle(string key, string label, bool value) => new(key, label, ParamKind.Toggle, value ? "true" : "false");

    static readonly EffectParam Speed = Number("speed", "Speed", 50);
    static readonly EffectParam Vertical = Toggle("vertical", "Top to Bottom", false);

    static double Along(double x, double y, EffectValues values) => values.On("vertical") ? y : x;
    static double Turns(double seconds, EffectValues values) => seconds * LightEffects.Rate((int)values.Number("speed"));
    static IReadOnlyList<Rgb> Three(EffectValues values) => [values.Color("color"), values.Color("color2"), values.Color("color3")];

    /// <summary>The older strip effects, run along the canvas from left to right or top to bottom.</summary>
    static Rgb Strip(SmoothEffect effect, double position, double seconds, EffectValues values, IReadOnlyList<Rgb> colors) =>
        LightEffects.At(effect, seconds, LightEffects.Wrap(position), colors, (int)values.Number("speed"));

    public static IReadOnlyList<CanvasEffect> All { get; } =
    [
        new("solid", "Solid Color", "One color on everything.", [Color("color", "Color", Cyan)],
            (_, _, _, values) => values.Color("color"), Moving: false),

        new("rainbow", "Rainbow Wave", "The whole color wheel, rolling across your PC.",
            [Speed, Number("bands", "Rainbows Across", 1, 1, 6), Vertical],
            (x, y, seconds, values) => LightEffects.Hue(Along(x, y, values) * values.Number("bands") - Turns(seconds, values))),

        new("cycle", "Color Cycle", "Everything fades through the color wheel together.", [Speed],
            (_, _, seconds, values) => LightEffects.Hue(Turns(seconds, values) / 2)),

        new("breathing", "Breathing", "Fades in and out, taking the next color each breath.",
            [Color("color", "Color 1", Cyan), Color("color2", "Color 2", Violet), Color("color3", "Color 3", Magenta), Speed],
            (_, _, seconds, values) => Strip(SmoothEffect.Breathing, 0, seconds, values, Three(values))),

        new("gradient", "Gradient Flow", "Three colors blending into each other as they drift.",
            [Color("color", "Color 1", Cyan), Color("color2", "Color 2", Violet), Color("color3", "Color 3", Magenta), Speed, Vertical],
            (x, y, seconds, values) => Strip(SmoothEffect.GradientFlow, Along(x, y, values), seconds, values, Three(values))),

        new("aurora", "Aurora", "Slow ribbons of color that never quite repeat.",
            [Color("color", "Color 1", "#00FFC8"), Color("color2", "Color 2", Violet), Color("color3", "Color 3", "#2979FF"), Speed, Vertical],
            (x, y, seconds, values) =>
            {
                // The ribbons bend a little across the other direction, so stacked strips don't all match.
                var across = values.On("vertical") ? x : y;
                return Strip(SmoothEffect.Aurora, Along(x, y, values) + 0.12 * Math.Sin(Tau * (across + seconds * 0.05)), seconds, values, Three(values));
            }),

        new("comet", "Comet", "A bright head circling with a fading tail.",
            [Color("color", "Head Color", Cyan), Color("color2", "Tail Color", Violet), Speed, Vertical],
            (x, y, seconds, values) => Strip(SmoothEffect.Comet, Along(x, y, values), seconds, values, [values.Color("color"), values.Color("color2")])),

        new("sweep", "Sweep", "A bar that glides from side to side, trailing light behind it.",
            [Color("color", "Color", "#FF0066"), Toggle("random", "New Color Each Pass", false), Speed, Number("trail", "Trail", 50),
                Number("width", "Bar Width", 12, 2, 60), Toggle("vertical", "Top to Bottom", false), Color("background", "Background Color", "#101014")],
            Sweep),

        new("radar", "Radar", "A beam turning around the middle of the canvas.",
            [Color("color", "Color", "#3DFF9A"), Speed, Number("width", "Beam Width", 30, 5, 100), Color("background", "Background Color", Black)],
            Radar),

        new("ripple", "Ripple", "Rings spreading out from the middle.",
            [Color("color", "Color", Cyan), Color("color2", "Second Color", Magenta), Speed, Number("rings", "Rings", 3, 1, 8), Color("background", "Background Color", Black)],
            Ripple),
    ];

    public static CanvasEffect Default => All[1];

    /// <summary>An effect that no longer exists, such as one from a newer version's settings, falls back to the default.</summary>
    public static CanvasEffect Find(string? id) => All.FirstOrDefault(effect => effect.Id == id) ?? Default;

    /// <summary>
    /// The bar moves like a pendulum, slowing at each side, and its trail is as long as it is fast, so the
    /// trail shrinks away at the turn instead of jumping to the other side.
    /// </summary>
    static Rgb Sweep(double x, double y, double seconds, EffectValues values)
    {
        var turns = Turns(seconds, values);
        var phase = LightEffects.Wrap(turns) * Tau;
        var width = values.Number("width") / 100;
        var center = width / 2 + (1 - width) * (0.5 - 0.5 * Math.Cos(phase));
        var pace = Math.Sin(phase); // positive while moving right or down
        var from = Along(x, y, values) - center;
        var color = values.On("random") ? LightEffects.Hue(Math.Floor(turns * 2) * 0.381966) : values.Color("color");

        double level;
        if (Math.Abs(from) <= width / 2) level = 1;
        else
        {
            var behind = (pace >= 0 ? -from : from) - width / 2;
            var trail = values.Number("trail") / 100 * 0.6 * Math.Abs(pace);
            level = behind > 0 && trail > 0.001 ? Math.Exp(-behind / trail * 3) : 0;
            // A soft edge a twentieth of the canvas wide, so the bar glides between LEDs instead of snapping.
            level = Math.Max(level, 1 - (Math.Abs(from) - width / 2) / 0.05);
        }
        return LightEffects.Mix(values.Color("background"), color, Math.Clamp(level, 0, 1));
    }

    static Rgb Radar(double x, double y, double seconds, EffectValues values)
    {
        var angle = Math.Atan2(y - 0.5, (x - 0.5) * Aspect) / Tau;
        var behind = LightEffects.Wrap(Turns(seconds, values) - angle);
        var width = values.Number("width") / 100 * 0.6;
        var fade = Math.Max(0, 1 - behind / width);
        var level = Math.Max(fade * fade, 1 - (1 - behind) / 0.02); // a short glow ahead of the beam's edge
        return LightEffects.Mix(values.Color("background"), values.Color("color"), Math.Clamp(level, 0, 1));
    }

    static Rgb Ripple(double x, double y, double seconds, EffectValues values)
    {
        var dx = (x - 0.5) * Aspect;
        var dy = y - 0.5;
        var distance = Math.Sqrt(dx * dx + dy * dy) / 0.943; // 1 at the canvas corners
        var wave = distance * values.Number("rings") - Turns(seconds, values);
        var level = 0.5 + 0.5 * Math.Cos(Tau * wave);
        var ring = (long)Math.Floor(wave + 0.5);
        var color = (ring & 1) == 0 ? values.Color("color") : values.Color("color2");
        return LightEffects.Mix(values.Color("background"), color, level * level);
    }

    /// <summary>A small picture of the canvas, three bytes a pixel, for the page's preview and the effect tiles.</summary>
    public static byte[] Render(CanvasEffect effect, EffectValues values, double seconds, int width, int height, double brightness = 1)
    {
        var pixels = new byte[width * height * 3];
        // Rows are independent, and the page asks for a full picture many times a second.
        Parallel.For(0, height, row =>
        {
            for (var column = 0; column < width; column++)
            {
                var color = effect.Draw((column + 0.5) / width, (row + 0.5) / height, seconds, values).Scaled(brightness);
                var at = (row * width + column) * 3;
                pixels[at] = color.R;
                pixels[at + 1] = color.G;
                pixels[at + 2] = color.B;
            }
        });
        return pixels;
    }
}
