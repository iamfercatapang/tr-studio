using System.Globalization;

namespace TRStudio.Engine;

/// <summary>One LED's color, as sent to the hardware.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);

    /// <summary>Reads "#RRGGBB". Anything else gives the fallback, so a damaged settings file can't stop the lights.</summary>
    public static Rgb Parse(string? hex, Rgb fallback = default)
    {
        if (hex is not { Length: 7 } || hex[0] != '#') return fallback;
        return int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? new Rgb((byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : fallback;
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>Dims toward black. 1 leaves the color as it is.</summary>
    public Rgb Scaled(double share)
    {
        share = Math.Clamp(share, 0, 1);
        return new Rgb((byte)Math.Round(R * share), (byte)Math.Round(G * share), (byte)Math.Round(B * share));
    }
}

public enum LightKind { Off, Static, DeviceEffect, Smooth }

/// <summary>The effects TR Studio draws itself, LED by LED.</summary>
public enum SmoothEffect { RainbowWave, ColorCycle, Breathing, GradientFlow, Aurora, Comet }

/// <summary>
/// What one device should show. Mode names a device's own effect and is used by DeviceEffect only;
/// Effect and Colors are used by Smooth only. Brightness and Speed run 0 to 100.
/// </summary>
public sealed record LightChoice(
    LightKind Kind = LightKind.Static, string Color = LightChoice.DefaultColor, int Brightness = 100,
    string? Mode = null, int Speed = 50, SmoothEffect Effect = SmoothEffect.RainbowWave, IReadOnlyList<string>? Colors = null)
{
    public const string DefaultColor = "#00E5FF";
    public const int MaxColors = 3;
    public static readonly IReadOnlyList<string> DefaultColors = ["#00E5FF", "#8B5CFF", "#FF3DD8"];

    public LightChoice Clamped() => this with
    {
        Kind = Enum.IsDefined(Kind) ? Kind : LightKind.Static,
        Effect = Enum.IsDefined(Effect) ? Effect : SmoothEffect.RainbowWave,
        Color = Rgb.Parse(Color, Rgb.Parse(DefaultColor)).ToHex(),
        Brightness = Math.Clamp(Brightness, 0, 100),
        Speed = Math.Clamp(Speed, 0, 100),
        Colors = Colors?.Take(MaxColors).Select(color => Rgb.Parse(color, Rgb.Parse(DefaultColor)).ToHex()).ToList(),
    };

    /// <summary>The colors a smooth effect runs through. Never empty.</summary>
    public IReadOnlyList<Rgb> Palette()
    {
        var colors = Colors is { Count: > 0 } ? Colors : DefaultColors;
        return colors.Select(color => Rgb.Parse(color)).ToList();
    }

    /// <summary>Records compare lists by reference, and a choice read back from the settings file is a new list.</summary>
    public bool Same(LightChoice? other) => other != null
        && (this with { Colors = null }) == (other with { Colors = null })
        && (Colors ?? []).SequenceEqual(other.Colors ?? []);
}

/// <summary>Named effect settings the user saved, to bring back later.</summary>
public sealed record EffectPreset(string Name, string Effect, Dictionary<string, string> Values);

/// <summary>A named arrangement of the hardware on the canvas.</summary>
public sealed record LayoutPreset(string Name, Dictionary<string, LightPlacement> Placements);

/// <summary>
/// The lighting canvas: which effect is drawn on it, each effect's settings (kept per effect, so going back
/// to one finds it as it was left), where each strip of LEDs sits, and one brightness for the whole layout.
/// A strip with no placement yet gets a default one from <see cref="LightPlacement.Arrange"/>.
/// </summary>
public sealed record CanvasSettings(
    string Effect = "rainbow", Dictionary<string, Dictionary<string, string>>? Values = null,
    Dictionary<string, LightPlacement>? Placements = null, int Brightness = 100,
    List<EffectPreset>? EffectPresets = null, List<LayoutPreset>? LayoutPresets = null)
{
    public const int MaxPresets = 30, MaxPresetName = 40;

    public EffectValues ValuesFor(CanvasEffect effect) =>
        effect.Read(Values != null && Values.TryGetValue(effect.Id, out var saved) ? saved : null);

    public CanvasSettings WithEffect(string effect) => this with { Effect = CanvasEffects.Find(effect).Id };

    public CanvasSettings WithValue(string effect, string key, string value)
    {
        var found = CanvasEffects.Find(effect);
        if (found.Id != effect || found.Params.All(parameter => parameter.Key != key)) return this;
        var all = new Dictionary<string, Dictionary<string, string>>(Values ?? []);
        var own = new Dictionary<string, string>(all.GetValueOrDefault(found.Id) ?? []) { [key] = value };
        all[found.Id] = found.Read(own).ToText(); // tidied, so a bad value never reaches the file
        return this with { Values = all };
    }

    public CanvasSettings WithPlacement(string zone, LightPlacement placement) =>
        this with { Placements = new Dictionary<string, LightPlacement>(Placements ?? []) { [zone] = placement.Clamped() } };

    static string Tidy(string name)
    {
        var trimmed = (name ?? "").Trim();
        return trimmed.Length > MaxPresetName ? trimmed[..MaxPresetName] : trimmed;
    }

    /// <summary>Saving under a name that exists replaces that preset.</summary>
    public CanvasSettings WithEffectPreset(string name)
    {
        name = Tidy(name);
        if (name.Length == 0) return this;
        var effect = CanvasEffects.Find(Effect);
        var presets = (EffectPresets ?? []).Where(preset => !preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        presets.Add(new EffectPreset(name, effect.Id, ValuesFor(effect).ToText()));
        return this with { EffectPresets = presets.TakeLast(MaxPresets).ToList() };
    }

    public CanvasSettings LoadEffectPreset(string name)
    {
        if ((EffectPresets ?? []).FirstOrDefault(preset => preset.Name == name) is not { } preset) return this;
        var effect = CanvasEffects.Find(preset.Effect);
        var all = new Dictionary<string, Dictionary<string, string>>(Values ?? []) { [effect.Id] = effect.Read(preset.Values).ToText() };
        return this with { Effect = effect.Id, Values = all };
    }

    public CanvasSettings WithoutEffectPreset(string name) =>
        this with { EffectPresets = (EffectPresets ?? []).Where(preset => preset.Name != name).ToList() };

    /// <summary>Stores where every listed strip is now, defaults included, so the preset is complete by itself.</summary>
    public CanvasSettings WithLayoutPreset(string name, IReadOnlyDictionary<string, LightPlacement> current)
    {
        name = Tidy(name);
        if (name.Length == 0) return this;
        var presets = (LayoutPresets ?? []).Where(preset => !preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        presets.Add(new LayoutPreset(name, new Dictionary<string, LightPlacement>(current)));
        return this with { LayoutPresets = presets.TakeLast(MaxPresets).ToList() };
    }

    /// <summary>Strips the preset doesn't mention keep their place.</summary>
    public CanvasSettings LoadLayoutPreset(string name)
    {
        if ((LayoutPresets ?? []).FirstOrDefault(preset => preset.Name == name) is not { } preset) return this;
        var placements = new Dictionary<string, LightPlacement>(Placements ?? []);
        foreach (var (zone, placement) in preset.Placements) placements[zone] = placement.Clamped();
        return this with { Placements = placements };
    }

    public CanvasSettings WithoutLayoutPreset(string name) =>
        this with { LayoutPresets = (LayoutPresets ?? []).Where(preset => preset.Name != name).ToList() };

    /// <summary>
    /// What an older version's "All Devices" choice becomes on the canvas: a color is Solid Color, and each
    /// strip effect is its canvas effect with the same colors and speed.
    /// </summary>
    public static CanvasSettings From(LightChoice? all)
    {
        if (all == null || all.Kind == LightKind.DeviceEffect) return new CanvasSettings();
        all = all.Clamped();
        if (all.Kind == LightKind.Off) return new CanvasSettings("solid", Brightness: 0);
        if (all.Kind == LightKind.Static)
            return new CanvasSettings("solid", new() { ["solid"] = new() { ["color"] = all.Color } }, Brightness: all.Brightness);
        var effect = all.Effect switch
        {
            SmoothEffect.ColorCycle => "cycle", SmoothEffect.Breathing => "breathing", SmoothEffect.GradientFlow => "gradient",
            SmoothEffect.Aurora => "aurora", SmoothEffect.Comet => "comet", _ => "rainbow",
        };
        var colors = all.Palette();
        var values = new Dictionary<string, string> { ["speed"] = all.Speed.ToString(System.Globalization.CultureInfo.InvariantCulture), ["color"] = colors[0].ToHex() };
        if (colors.Count > 1) values["color2"] = colors[1].ToHex();
        if (colors.Count > 2) values["color3"] = colors[2].ToHex();
        var found = CanvasEffects.Find(effect);
        return new CanvasSettings(effect, new() { [effect] = found.Read(values).ToText() }, Brightness: all.Brightness);
    }
}

/// <summary>
/// Lighting is off until the user turns it on, and nothing connects before that. Canvas is what every device
/// shows; a device listed in Devices as Off or with one of its own effects ignores the canvas instead. All is
/// only read from settings written before the canvas existed. HeaderLeds holds how many LEDs are plugged
/// into each addressable header, since a header can't tell. Memory turns on the built-in driver for RGB
/// memory, which is off unless asked for because it uses the bus the memory's own data chips sit on.
/// Asrock turns on the ASRock board driver, off unless asked for because nobody has yet run it on a real board.
/// Gpu turns on the graphics card driver, off unless asked for because it writes to the card's own I2C bus.
/// Parts lists what is plugged into a strip, in order, when the user has divided it (see <see cref="LightParts"/>).
/// </summary>
public sealed record LightingSettings(
    bool Enabled = false, bool StartOpenRgb = true, LightChoice? All = null,
    Dictionary<string, LightChoice>? Devices = null, Dictionary<string, int>? HeaderLeds = null, bool Memory = false, bool Asrock = false, CanvasSettings? Canvas = null,
    Dictionary<string, List<LightPart>>? Parts = null, bool Gpu = false)
{
    /// <summary>The canvas in use: the saved one, or what an older version's settings come to.</summary>
    public CanvasSettings ActiveCanvas => Canvas ?? CanvasSettings.From(All);

    /// <summary>The device's own choice when it has opted out of the canvas, else null.</summary>
    public LightChoice? Override(string deviceKey) =>
        Devices != null && Devices.TryGetValue(deviceKey, out var own) && own.Kind is LightKind.Off or LightKind.DeviceEffect ? own.Clamped() : null;

    public const int MaxHeaderLeds = 1000;

    public LightChoice For(string deviceKey) =>
        (Devices != null && Devices.TryGetValue(deviceKey, out var own) ? own : All ?? new LightChoice()).Clamped();

    public bool HasOwn(string deviceKey) => Devices?.ContainsKey(deviceKey) == true;

    public static string HeaderKey(string deviceKey, string zone) => $"{deviceKey}|{zone}";

    public LightingSettings WithAll(LightChoice choice) => this with { All = choice.Clamped() };

    /// <summary>Null puts the device back on what every device shows.</summary>
    public LightingSettings WithDevice(string deviceKey, LightChoice? choice)
    {
        var devices = new Dictionary<string, LightChoice>(Devices ?? []);
        if (choice == null) devices.Remove(deviceKey);
        else devices[deviceKey] = choice.Clamped();
        return this with { Devices = devices };
    }

    public LightingSettings WithHeaderLeds(string deviceKey, string zone, int leds)
    {
        var headers = new Dictionary<string, int>(HeaderLeds ?? []) { [HeaderKey(deviceKey, zone)] = Math.Clamp(leds, 0, MaxHeaderLeds) };
        return this with { HeaderLeds = headers };
    }

    /// <summary>No parts puts the zone back to one strip. The places of the old parts go, since their strips are gone.</summary>
    public LightingSettings WithParts(string deviceKey, string zone, IEnumerable<LightPart>? parts)
    {
        var key = HeaderKey(deviceKey, zone);
        var all = new Dictionary<string, List<LightPart>>(Parts ?? []);
        var clamped = LightParts.Clamped(parts);
        if (clamped.Count == 0) all.Remove(key);
        else all[key] = clamped;
        var canvas = ActiveCanvas;
        if (canvas.Placements is { } placed)
        {
            canvas = canvas with { Placements = placed.Where(place => !place.Key.StartsWith(key + "#", StringComparison.Ordinal)).ToDictionary(place => place.Key, place => place.Value) };
        }
        return this with { Parts = all.Count == 0 ? null : all, Canvas = canvas };
    }
}

/// <summary>How a strip's LEDs are laid out inside its box: in a line, or round a ring as on a fan.</summary>
public enum LightShape { Strip, Ring }

/// <summary>One thing plugged into a strip, such as a fan on a header: its name, how many LEDs it has and their shape.</summary>
public sealed record LightPart(string Name, int Leds, LightShape Shape = LightShape.Strip);

/// <summary>
/// A strip of LEDs inside a device, such as one motherboard header. Resizable ones can't count their own LEDs.
/// Label is what the page calls it when that differs from Name, as for a part of a divided strip.
/// </summary>
public sealed record LightZone(string Name, int LedCount, int MinLeds, int MaxLeds, LightShape Shape = LightShape.Strip, string? Label = null)
{
    public bool Resizable => MaxLeds > MinLeds;
}

/// <summary>An effect the device runs by itself. Colors is how many colors it takes, 0 when it picks its own.</summary>
public sealed record LightMode(string Name, bool HasSpeed, int Colors);

/// <summary>
/// A device as the lighting server lists it. Index is its place in that list and can change between
/// connections; Key stays the same, so the user's choice finds the device again.
/// </summary>
public sealed record LightDevice(int Index, string Key, string Name, string Kind, IReadOnlyList<LightZone> Zones, IReadOnlyList<LightMode> Modes, int LedCount);

/// <summary>The lighting server went away or refused. The service tries again later.</summary>
public sealed class LightingUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The link to whatever drives the LEDs. Every call can throw <see cref="LightingUnavailableException"/>.</summary>
public interface ILightingClient : IDisposable
{
    /// <summary>True once after the server's device list changed, such as a device plugged in.</summary>
    bool TakeDevicesChanged();

    IReadOnlyList<LightDevice> Devices();

    /// <summary>Puts the device in the mode where every LED takes the color it is sent.</summary>
    void SetDirect(int device);

    void SetLeds(int device, ReadOnlySpan<Rgb> colors);

    /// <summary>Starts one of the device's own effects. Speed runs 0 to 100.</summary>
    void SetMode(int device, string mode, int speed, IReadOnlyList<Rgb> colors);

    void ResizeZone(int device, int zone, int leds);
}
