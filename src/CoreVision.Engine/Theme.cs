using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoreVision.Engine;

public enum BackgroundKind { Color, Image, Gif, Video }
public enum ElementKind { Metric, Text, Clock, Date, Gauge, Capture, Bar, Shape, Music, Weather }
public enum Metric
{
    CpuTemp, CpuLoad, CpuPower, GpuTemp, GpuLoad, GpuPower, MemoryLoad,
    CpuClock, GpuClock, GpuFan, GpuHotSpot, GpuMemoryTemp, MotherboardTemp,
    PumpRpm, CpuFanRpm, CaseFanRpm,
    MemoryUsed, MemoryAvailable, MemorySpeed,
    MusicProgress, OutdoorTemp,
    GpuMemoryUsed,
}
public enum TextAlign { Left, Center, Right }
public enum ShapeKind { Line, Arc, Rectangle, Ellipse, Triangle, Diamond, Hexagon, Star }
public enum LineStyle { Solid, Dashed, Dotted }
public enum CaptureSource { Area, Screen, Window }

public sealed record BackgroundSpec(BackgroundKind Kind = BackgroundKind.Color, string Color = "#000000", string? Path = null);

public sealed record ElementSpec(ElementKind Kind, float X, float Y, float Size)
{
    public Metric Metric { get; init; }
    public string? Unit { get; init; }
    public string? Text { get; init; }
    public string Format { get; init; } = "HH:mm";
    public string Color { get; init; } = "#FFFFFF";
    public TextAlign Align { get; init; } = TextAlign.Left;
    public bool Hidden { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public string FontFamily { get; init; } = "Segoe UI";
    public bool Stroke { get; init; }
    public string StrokeColor { get; init; } = "#000000";
    public float StrokeWidth { get; init; } = 2;
    public bool Shadow { get; init; }
    public string ShadowColor { get; init; } = "#000000";
    public float ShadowX { get; init; } = 2;
    public float ShadowY { get; init; } = 2;
    public float ShadowBlur { get; init; } = 6;

    // Gauge: X and Y are the center and Size is the outer diameter. Color runs to EndColor along the ring.
    public float Min { get; init; }
    public float Max { get; init; } = 100;
    public float Thickness { get; init; } = 16;
    public float StartAngle { get; init; } = 135;
    public float Sweep { get; init; } = 270;
    public string EndColor { get; init; } = "#BF5AF2";
    public string TrackColor { get; init; } = "#2A2A2E";
    public bool Dot { get; init; } = true;
    /// <summary>Off draws the whole gauge or bar in its gradient, with a marker at the reading.</summary>
    public bool UseTrackColor { get; init; } = true;

    // Capture: a live area of the desktop, in desktop pixels. X and Y are the center and Size is the width.
    // A Screen source follows CaptureScreen, a monitor's device name, and falls back to the stored area.
    // A Window source follows the window of CaptureProcess titled CaptureTitle wherever it is, even when covered.
    // CaptureWidth and CaptureHeight always give the shape, as the screen or window was when chosen.
    public CaptureSource Source { get; init; }
    public int CaptureX { get; init; }
    public int CaptureY { get; init; }
    public int CaptureWidth { get; init; }
    public int CaptureHeight { get; init; }
    public string? CaptureScreen { get; init; }
    public string? CaptureProcess { get; init; }
    public string? CaptureTitle { get; init; }
    public float CornerRadius { get; init; } = 16;

    // Bar: X and Y are the center, Size is the length and Thickness is the height. Color runs to EndColor
    // along the whole bar, so the tip's color shows the level. Stat, range and colors work as on a gauge.
    public bool Stripes { get; init; }

    // Shape: X and Y are the center and Size is the width. A Line uses Thickness, not Height, and an Arc
    // is a circle Size wide that uses Thickness, StartAngle and Sweep. Stroke draws the outline.
    public ShapeKind Shape { get; init; }
    public LineStyle LineStyle { get; init; }
    public float Height { get; init; } = 120;
    public float Rotation { get; init; }
    public bool Filled { get; init; } = true;
    public bool Gradient { get; init; }
    public float Opacity { get; init; } = 1;

    // Music: X and Y are the center and Size is the width. Color is the title, SecondaryColor the artist and
    // times, EndColor the progress fill and TrackColor the empty bar. CornerRadius rounds the album art.
    public bool ShowArt { get; init; } = true;
    public bool ShowTitle { get; init; } = true;
    public bool ShowArtist { get; init; } = true;
    public bool ShowProgress { get; init; } = true;
    public string SecondaryColor { get; init; } = "#98989D";

    // Weather: X and Y are the center and Size is the width. Color is the temperature and forecast, SecondaryColor the rest.
    public bool ShowCondition { get; init; } = true;
    public bool ShowDetails { get; init; } = true;
    public bool ShowForecast { get; init; } = true;
}

public sealed record Theme(string Name, BackgroundSpec Background, IReadOnlyList<ElementSpec> Elements)
{
    /// <summary>Darkens the background from 0 to 1, so text stays readable over busy images.</summary>
    public float Dim { get; init; }

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Throws InvalidDataException for anything that is not a usable theme.</summary>
    public static Theme FromJson(string json)
    {
        try
        {
            var theme = JsonSerializer.Deserialize<Theme>(json, Json);
            if (theme is null || string.IsNullOrWhiteSpace(theme.Name) || theme.Background is null || theme.Elements is null)
                throw new InvalidDataException("Theme is missing its name, background or elements.");
            return theme;
        }
        catch (JsonException e) { throw new InvalidDataException($"Theme is not valid: {e.Message}", e); }
    }

    const string CpuBlue = "#0A84FF", GpuGreen = "#30D158", Secondary = "#98989D";

    /// <summary>The built-in theme, shown when nothing else is selected or a theme fails to load.</summary>
    public static Theme Default { get; } = new("System Stats", new BackgroundSpec(),
    [
        new ElementSpec(ElementKind.Clock, 240, 62, 30) { Align = TextAlign.Center, Color = Secondary },
        new ElementSpec(ElementKind.Text, 145, 118, 24) { Text = "CPU", Color = CpuBlue, Bold = true, Align = TextAlign.Center },
        new ElementSpec(ElementKind.Metric, 145, 146, 76) { Metric = Metric.CpuTemp, Unit = "°", Bold = true, Align = TextAlign.Center },
        new ElementSpec(ElementKind.Metric, 145, 250, 30) { Metric = Metric.CpuLoad, Unit = "%", Align = TextAlign.Center },
        new ElementSpec(ElementKind.Metric, 145, 288, 26) { Metric = Metric.CpuPower, Unit = " W", Align = TextAlign.Center, Color = Secondary },
        new ElementSpec(ElementKind.Text, 335, 118, 24) { Text = "GPU", Color = GpuGreen, Bold = true, Align = TextAlign.Center },
        new ElementSpec(ElementKind.Metric, 335, 146, 76) { Metric = Metric.GpuTemp, Unit = "°", Bold = true, Align = TextAlign.Center },
        new ElementSpec(ElementKind.Metric, 335, 250, 30) { Metric = Metric.GpuLoad, Unit = "%", Align = TextAlign.Center },
        new ElementSpec(ElementKind.Metric, 335, 288, 26) { Metric = Metric.GpuPower, Unit = " W", Align = TextAlign.Center, Color = Secondary },
        new ElementSpec(ElementKind.Text, 232, 362, 24) { Text = "Memory", Color = Secondary, Align = TextAlign.Right },
        new ElementSpec(ElementKind.Metric, 240, 362, 24) { Metric = Metric.MemoryLoad, Unit = "%", Color = Secondary },
    ]);

    const string HaloCyan = "#22E5E8", HaloPurple = "#A855F7", Hot = "#FF453A", Amber = "#FF9F0A";

    /// <summary>Paths of built-in themes start with this. They have no file, and editing one saves a copy.</summary>
    public const string BuiltInPrefix = "builtin:";

    static ElementSpec Label(float x, float y, float size, string text, bool bold = false, string color = "#FFFFFF") =>
        new(ElementKind.Text, x, y, size) { Text = text, Bold = bold, Color = color, Align = TextAlign.Center };

    static ElementSpec Value(float x, float y, float size, Metric metric, string unit, string color = "#FFFFFF") =>
        new(ElementKind.Metric, x, y, size) { Metric = metric, Unit = unit, Bold = true, Color = color, Align = TextAlign.Center };

    static ElementSpec Ring(Metric metric, float size, float min, float max, string from, string to, float thickness = 30) =>
        new(ElementKind.Gauge, 240, 240, size) { Metric = metric, Min = min, Max = max, Color = from, EndColor = to, Thickness = thickness };

    static ElementSpec Bar(float y, Metric metric, string from, string to, bool stripes = false) =>
        new(ElementKind.Bar, 240, y, 300) { Metric = metric, Min = 30, Max = 95, Thickness = 22, Color = from, EndColor = to, TrackColor = "#3A3A3F", Stripes = stripes };

    static ElementSpec Corner(float y, string text) =>
        new(ElementKind.Text, 92, y, 26) { Text = text, Bold = true };

    static ElementSpec BigValue(float y, Metric metric) =>
        new(ElementKind.Metric, 390, y, 104) { Metric = metric, Unit = "°", Bold = true, Color = "#E8F0D8", Align = TextAlign.Right };

    public static IReadOnlyList<Theme> BuiltIns { get; } =
    [
        new("Halo", new BackgroundSpec(),
        [
            Ring(Metric.GpuTemp, 430, 20, 100, HaloCyan, HaloPurple),
            Label(240, 112, 34, "GPU", bold: true),
            Value(240, 150, 130, Metric.GpuTemp, "°"),
            Label(240, 322, 28, "Temperature", color: Secondary),
        ]),
        new("Halo Split", new BackgroundSpec(),
        [
            Ring(Metric.CpuTemp, 448, 20, 100, "#64D2FF", "#0A84FF", 36) with { StartAngle = 130, Sweep = 100, Dot = false },
            Ring(Metric.GpuTemp, 448, 20, 100, "#FF9F0A", "#FF3B30", 36) with { StartAngle = 50, Sweep = -100, Dot = false },
            new ElementSpec(ElementKind.Clock, 240, 96, 28) { Align = TextAlign.Center, Bold = true },
            Value(182, 150, 76, Metric.CpuTemp, "°"),
            Label(176, 250, 32, "CPU", color: "#64D2FF"),
            Value(302, 198, 76, Metric.GpuTemp, "°"),
            Label(296, 298, 32, "GPU", color: "#FF6B3D"),
        ]),
        new("Halo Duo", new BackgroundSpec(),
        [
            Ring(Metric.GpuTemp, 440, 20, 100, HaloCyan, HaloPurple),
            new ElementSpec(ElementKind.Clock, 240, 84, 30) { Align = TextAlign.Center, Color = Secondary },
            Value(172, 150, 112, Metric.GpuTemp, "°"),
            Label(172, 296, 30, "GPU", bold: true),
            Value(342, 146, 54, Metric.CpuTemp, "°"),
            Label(342, 218, 21, "CPU", bold: true),
            Value(342, 252, 42, Metric.PumpRpm, ""),
            Label(342, 310, 19, "Pump RPM", bold: true),
        ]),
        new("Halo Load", new BackgroundSpec(),
        [
            Ring(Metric.CpuLoad, 452, 0, 100, HaloCyan, "#0A84FF", 24),
            Ring(Metric.GpuLoad, 388, 0, 100, "#30D158", Hot, 24),
            Label(165, 150, 28, "CPU", bold: true, color: HaloCyan),
            Value(165, 186, 70, Metric.CpuLoad, "%"),
            Label(315, 150, 28, "GPU", bold: true, color: "#30D158"),
            Value(315, 186, 70, Metric.GpuLoad, "%"),
            new ElementSpec(ElementKind.Clock, 240, 300, 30) { Align = TextAlign.Center, Color = Secondary },
        ]),
        new("Halo Cooling", new BackgroundSpec(),
        [
            Ring(Metric.PumpRpm, 452, 0, 4000, HaloCyan, HaloPurple, 24),
            Ring(Metric.CpuFanRpm, 388, 0, 2500, "#30D158", Amber, 24),
            Label(240, 106, 28, "CPU", bold: true),
            Value(240, 136, 104, Metric.CpuTemp, "°"),
            Value(165, 276, 34, Metric.PumpRpm, "", HaloCyan),
            Label(165, 322, 19, "Pump", color: Secondary),
            Value(315, 276, 34, Metric.CpuFanRpm, "", "#30D158"),
            Label(315, 322, 19, "Fan", color: Secondary),
        ]),
        new("Minimal Clock", new BackgroundSpec(),
        [
            new ElementSpec(ElementKind.Clock, 240, 138, 112) { Align = TextAlign.Center, Bold = true },
            new ElementSpec(ElementKind.Date, 240, 276, 28) { Align = TextAlign.Center, Format = "dddd, MMM d", Color = Secondary },
            Label(186, 334, 18, "CPU", color: Secondary),
            Value(186, 356, 30, Metric.CpuTemp, "°"),
            Label(294, 334, 18, "GPU", color: Secondary),
            Value(294, 356, 30, Metric.GpuTemp, "°"),
        ]),
        new("Stat Grid", new BackgroundSpec(),
        [
            Label(150, 108, 20, "CPU TEMP", color: HaloCyan),
            Value(150, 134, 64, Metric.CpuTemp, "°"),
            Label(330, 108, 20, "GPU TEMP", color: "#30D158"),
            Value(330, 134, 64, Metric.GpuTemp, "°"),
            Label(150, 256, 20, "CPU LOAD", color: HaloCyan),
            Value(150, 282, 64, Metric.CpuLoad, "%"),
            Label(330, 256, 20, "GPU LOAD", color: "#30D158"),
            Value(330, 282, 64, Metric.GpuLoad, "%"),
        ]),
        new("Triple Ring", new BackgroundSpec(),
        [
            Ring(Metric.CpuLoad, 452, 0, 100, HaloCyan, "#0A84FF", 24),
            Ring(Metric.GpuLoad, 392, 0, 100, "#30D158", "#FFD60A", 24),
            Ring(Metric.MemoryLoad, 332, 0, 100, Amber, Hot, 24),
            new ElementSpec(ElementKind.Clock, 240, 182, 58) { Align = TextAlign.Center, Bold = true },
            new ElementSpec(ElementKind.Date, 240, 262, 22) { Align = TextAlign.Center, Format = "ddd, MMM d", Color = Secondary },
        ]),
        new("Twin Arc", new BackgroundSpec(),
        [
            Ring(Metric.CpuLoad, 444, 0, 100, HaloCyan, "#0A84FF", 28) with { StartAngle = 195, Sweep = 150 },
            Ring(Metric.GpuLoad, 444, 0, 100, Amber, Hot, 28) with { StartAngle = 165, Sweep = -150 },
            Label(240, 76, 24, "CPU", bold: true, color: HaloCyan),
            Value(240, 106, 86, Metric.CpuTemp, "°"),
            Value(240, 248, 86, Metric.GpuTemp, "°"),
            Label(240, 366, 24, "GPU", bold: true, color: Amber),
        ]),
        new("Ember", new BackgroundSpec(BackgroundKind.Color, "#180804"),
        [
            Ring(Metric.CpuTemp, 436, 30, 95, "#FFD60A", Hot),
            Label(240, 112, 30, "CPU", bold: true, color: Amber),
            Value(240, 146, 124, Metric.CpuTemp, "°"),
            Value(240, 310, 36, Metric.CpuPower, " W", Secondary),
        ]),
        new("Memory", new BackgroundSpec(),
        [
            Ring(Metric.MemoryLoad, 430, 0, 100, "#64D2FF", HaloPurple),
            Label(240, 104, 28, "MEMORY", bold: true),
            Value(240, 138, 104, Metric.MemoryLoad, "%"),
            Value(168, 278, 30, Metric.MemoryUsed, " GB"),
            Label(168, 316, 18, "In Use", color: Secondary),
            Value(312, 278, 30, Metric.MemoryAvailable, " GB"),
            Label(312, 316, 18, "Available", color: Secondary),
        ]),
        new("Now Playing", new BackgroundSpec(),
        [
            Ring(Metric.MusicProgress, 452, 0, 100, HaloCyan, HaloPurple, 14) with { StartAngle = 270, Sweep = 360, Dot = false },
            new ElementSpec(ElementKind.Clock, 240, 70, 26) { Align = TextAlign.Center, Color = Secondary },
            new ElementSpec(ElementKind.Music, 240, 252, 300) { Bold = true, ShowProgress = false, CornerRadius = 20 },
        ]),
        new("Weather", new BackgroundSpec(),
        [
            new ElementSpec(ElementKind.Clock, 240, 64, 40) { Align = TextAlign.Center, Bold = true },
            new ElementSpec(ElementKind.Date, 240, 114, 20) { Align = TextAlign.Center, Format = "dddd, MMM d", Color = Secondary },
            new ElementSpec(ElementKind.Weather, 240, 286, 300) { Bold = true },
        ]),
        new("Dual Bar", new BackgroundSpec(BackgroundKind.Color, "#0C1210"),
        [
            BigValue(72, Metric.CpuTemp),
            Corner(164, "CPU"),
            Bar(218, Metric.CpuTemp, Amber, Hot, stripes: true),
            Bar(262, Metric.GpuTemp, "#7FE5D0", "#22E5E8"),
            Corner(286, "GPU"),
            BigValue(262, Metric.GpuTemp),
        ]),
    ];
}
