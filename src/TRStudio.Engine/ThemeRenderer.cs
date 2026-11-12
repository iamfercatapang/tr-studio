using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;

namespace TRStudio.Engine;

/// <summary>Draws a theme's elements. Readings that cannot be trusted show a dash, never a number.</summary>
public sealed partial class ThemeRenderer : IDisposable
{
    public const int ScreenSize = 480;
    const string NoValue = "–";

    readonly Bitmap _canvas;
    readonly int _quality;

    /// <summary>
    /// Themes are designed around a 480 unit square. Other screens scale that square to their
    /// shorter side and center it, so any theme fits, and wide screens gain room at the sides.
    /// </summary>
    public ThemeRenderer(int width = ScreenSize, int height = ScreenSize, int quality = ScreenProtocol.DefaultJpegQuality)
    {
        _canvas = new Bitmap(width, height);
        _quality = quality;
    }
    const int MaxBlurSteps = 12;
    const int ShadowStrength = 200;
    const float GaugeStepDegrees = 2.5f;
    const string Degree = "°";
    const float DegreeShare = 0.5f;
    const float DegreeGap = 0.08f;
    const float TrackStepDegrees = 6f;
    const float TrackTint = 0.26f;
    const float MarkerGrowthShare = 0.4f;
    const float MinMarkerGrowth = 10f;
    const float MarkerGapShare = 0.6f;
    const float GaugeDotShare = 0.8f;
    const float GaugeGapShare = 0.4f;
    const double GaugeSpringRate = 4.5; // about a second to arrive, which is how often sensors read
    const double GaugeLongestStep = 0.05;
    const double GaugeSettledShare = 0.001;
    const double GaugeSettledSpeed = 0.002;
    const float BarStripeSpacing = 6f;
    const float BarStripeWidth = 2f;
    const float MinBarThickness = 2f;
    const int BarStripeAlpha = 110;
    const float MinShapeSize = 1f;
    const float StarInnerShare = 0.45f;
    const float DashShare = 3f;
    const float DashGapShare = 2f;
    const float DotGapShare = 2f;

    const float MinCaptureSize = 20f;
    const double FadeSeconds = 0.8;
    const int AlertScrimAlpha = 235;
    const double AlertSecondsEach = 3;
    const double AlertPulsesPerSecond = 1;
    const float AlertRingWidth = 12f;
    const float AlertRingInset = 14f;
    static readonly Color AlertRed = Color.FromArgb(255, 59, 48);
    static readonly Color AlertReading = Color.FromArgb(255, 120, 110);

    Bitmap? _fadeFrom;
    readonly Stopwatch _sinceFade = new();

    readonly Dictionary<(int Index, Metric Metric), (double Shown, double Speed)> _gaugeValues = [];
    readonly Dictionary<CaptureKey, ICapture> _captures = [];

    /// <summary>What a capture shows. An area or screen is a desktop rectangle; a window is its app and title.</summary>
    readonly record struct CaptureKey(Rectangle Area, string? Process, string? Title, Size Fitted);
    readonly Stopwatch _sinceRender = Stopwatch.StartNew();
    double _frameSeconds;
    float _scale = 1;

    /// <summary>True while a gauge is still easing toward its latest reading.</summary>
    public bool IsAnimating { get; private set; }

    /// <summary>A round screen shows only the circle inside its square, so edge-hugging parts follow that circle.</summary>
    public bool RoundScreen { get; init; }

    const int LoadingScrimAlpha = 200;
    const float LoadingRingSize = 96f;
    const float LoadingRingWidth = 8f;
    const float LoadingArcSweep = 100f;
    const double LoadingTurnsPerSecond = 1.1;
    static readonly Color LoadingAccent = Color.FromArgb(0x22, 0xE5, 0xE8);
    static readonly Color LoadingTrack = Color.FromArgb(60, 255, 255, 255);
    readonly Stopwatch _sinceLoading = Stopwatch.StartNew();

    readonly Dictionary<(string Family, bool Bold, bool Italic), (FontFamily Family, FontStyle Style)> _fonts = [];

    /// <param name="loading">Sensors are still starting up. A theme that shows readings is covered by a spinner until they arrive.</param>
    public byte[] Render(IBackground background, IReadOnlyList<ElementSpec> elements, Stats stats, DateTime now, float dim = 0,
        IReadOnlyList<Alert>? alerts = null, NowPlaying? media = null, WeatherReport? weather = null, bool loading = false)
    {
        using var g = Graphics.FromImage(_canvas);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        background.Draw(g);
        if (dim > 0)
        {
            using var scrim = new SolidBrush(Color.FromArgb((int)(Math.Min(dim, 1f) * 255), Color.Black));
            g.FillRectangle(scrim, 0, 0, _canvas.Width, _canvas.Height);
        }
        var scale = Math.Min(_canvas.Width, _canvas.Height) / (float)ScreenSize;
        _scale = scale;
        g.TranslateTransform(_canvas.Width / 2f, _canvas.Height / 2f);
        g.ScaleTransform(scale, scale);
        g.TranslateTransform(-ScreenSize / 2f, -ScreenSize / 2f);
        _frameSeconds = _sinceRender.Elapsed.TotalSeconds;
        _sinceRender.Restart();
        IsAnimating = false;
        _screenTransform?.Dispose();
        _screenTransform = g.Transform;
        var capturesUsed = new HashSet<CaptureKey>();
        for (var i = 0; i < elements.Count; i++)
        {
            if (elements[i].Hidden) continue;
            // Bars and shapes turn inside their own drawing; everything else is turned here, around its pivot.
            GraphicsState? turned = null;
            if (elements[i].Rotation % 360 != 0 && elements[i].Kind is not (ElementKind.Bar or ElementKind.Shape))
            {
                turned = g.Save();
                var pivot = Pivot(elements[i]);
                g.TranslateTransform(pivot.X, pivot.Y);
                g.RotateTransform(elements[i].Rotation);
                g.TranslateTransform(-pivot.X, -pivot.Y);
            }
            if (elements[i].Kind == ElementKind.Gauge) DrawGauge(g, i, elements[i], stats);
            else if (elements[i].Kind == ElementKind.Bar) DrawBar(g, i, elements[i], stats);
            else if (elements[i].Kind == ElementKind.Shape) DrawShape(g, elements[i]);
            else if (elements[i].Kind == ElementKind.Capture) DrawCapture(g, elements[i], scale, capturesUsed);
            else if (elements[i].Kind == ElementKind.Music) DrawMusic(g, elements[i], media);
            else if (elements[i].Kind == ElementKind.Weather) DrawWeather(g, elements[i], weather);
            else Draw(g, elements[i], stats, now);
            if (turned != null) g.Restore(turned);
        }
        foreach (var key in _captures.Keys.Where(key => !capturesUsed.Contains(key)).ToList())
        {
            _captures[key].Dispose();
            _captures.Remove(key);
        }
        DrawFade(g);
        if (loading && UsesSensors(elements)) DrawLoading(g, scale);
        if (alerts is { Count: > 0 }) DrawAlert(g, alerts, now, scale);
        return JpegFrame.Encode(_canvas, _quality);
    }

    /// <summary>A text layer's line box is this many times its size tall. The Editor's handles use the same.</summary>
    public const float TextLineShare = 1.35f;

    /// <summary>
    /// The point a layer turns around: the center for everything placed by its center, and for text the anchor
    /// (left edge, middle or right edge, as aligned) halfway down the line, so changing digits never move it.
    /// </summary>
    public static PointF Pivot(ElementSpec element) => element.Kind is ElementKind.Metric or ElementKind.Text or ElementKind.Clock or ElementKind.Date
        ? new PointF(element.X, element.Y + Math.Clamp(element.Size, 8f, 300f) * TextLineShare / 2)
        : new PointF(element.X, element.Y);

    /// <summary>The screen's own transform, for parts drawn along the screen edge whatever their layer's rotation.</summary>
    Matrix? _screenTransform;

    /// <summary>True when the theme shows a reading from the PC's sensors, as text, a gauge or a bar.</summary>
    public static bool UsesSensors(IReadOnlyList<ElementSpec> elements) =>
        elements.Any(element => !element.Hidden && element.Kind is ElementKind.Metric or ElementKind.Gauge or ElementKind.Bar
            && Stats.IsSensor(element.Metric));

    /// <summary>
    /// One spinner covers the whole theme while sensors start, so a screen of dashes never reads as broken.
    /// It turns with a stopwatch, not the clock, so it spins evenly between frames.
    /// </summary>
    void DrawLoading(Graphics g, float scale)
    {
        IsAnimating = true;
        g.ResetTransform();
        using (var scrim = new SolidBrush(Color.FromArgb(LoadingScrimAlpha, Color.Black)))
            g.FillRectangle(scrim, 0, 0, _canvas.Width, _canvas.Height);
        g.TranslateTransform(_canvas.Width / 2f, _canvas.Height / 2f);
        g.ScaleTransform(scale, scale);
        g.TranslateTransform(-ScreenSize / 2f, -ScreenSize / 2f);

        var center = ScreenSize / 2f;
        var ring = new RectangleF(center - LoadingRingSize / 2, center - 30 - LoadingRingSize / 2, LoadingRingSize, LoadingRingSize);
        using (var track = new Pen(LoadingTrack, LoadingRingWidth))
            g.DrawEllipse(track, ring);
        var start = (float)(_sinceLoading.Elapsed.TotalSeconds * LoadingTurnsPerSecond * 360 % 360);
        using (var arc = new Pen(LoadingAccent, LoadingRingWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(arc, ring, start, LoadingArcSweep);

        using var family = OpenFamily("Segoe UI");
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var path = new GraphicsPath();
        path.AddString("Reading sensors", family, (int)FontStyle.Regular, 24, new PointF(center, ring.Bottom + 44), centered);
        using var brush = new SolidBrush(Color.FromArgb(200, 200, 206));
        g.FillPath(brush, path);
    }

    /// <summary>The next frames dissolve from what is on the screen now to whatever is drawn next.</summary>
    public void BeginFade()
    {
        _fadeFrom?.Dispose();
        _fadeFrom = (Bitmap)_canvas.Clone();
        _sinceFade.Restart();
    }

    void DrawFade(Graphics g)
    {
        if (_fadeFrom == null) return;
        var progress = _sinceFade.Elapsed.TotalSeconds / FadeSeconds;
        if (progress >= 1)
        {
            _fadeFrom.Dispose();
            _fadeFrom = null;
            return;
        }
        IsAnimating = true;
        var eased = progress * progress * (3 - 2 * progress); // starts and ends gently
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = (float)(1 - eased) });
        g.ResetTransform();
        g.DrawImage(_fadeFrom, new Rectangle(Point.Empty, _canvas.Size), 0, 0, _canvas.Width, _canvas.Height, GraphicsUnit.Pixel, attributes);
    }

    /// <summary>
    /// A warning covers the theme, so it reads from across the room. A pulsing red ring draws the eye,
    /// and with more than one warning they take turns.
    /// </summary>
    void DrawAlert(Graphics g, IReadOnlyList<Alert> alerts, DateTime now, float scale)
    {
        IsAnimating = true;
        var seconds = now.TimeOfDay.TotalSeconds;
        var index = (int)(seconds / AlertSecondsEach) % alerts.Count;
        var alert = alerts[index];

        g.ResetTransform();
        using (var scrim = new SolidBrush(Color.FromArgb(AlertScrimAlpha, Color.Black)))
            g.FillRectangle(scrim, 0, 0, _canvas.Width, _canvas.Height);
        g.TranslateTransform(_canvas.Width / 2f, _canvas.Height / 2f);
        g.ScaleTransform(scale, scale);
        g.TranslateTransform(-ScreenSize / 2f, -ScreenSize / 2f);

        var pulse = 0.5 + 0.5 * Math.Sin(seconds * AlertPulsesPerSecond * 2 * Math.PI);
        using (var ring = new Pen(Color.FromArgb((int)(170 + 85 * pulse), AlertRed), AlertRingWidth))
        {
            var size = ScreenSize - 2 * AlertRingInset;
            g.DrawEllipse(ring, AlertRingInset, AlertRingInset, size, size);
        }

        using (var sign = new GraphicsPath())
        using (var red = new SolidBrush(AlertRed))
        {
            sign.AddPolygon([new PointF(240, 110), new PointF(282, 182), new PointF(198, 182)]);
            g.FillPath(red, sign);
            using var mark = new SolidBrush(Color.Black);
            g.FillRectangle(mark, 236, 132, 8, 28);
            g.FillEllipse(mark, 235.5f, 165, 9, 9);
        }

        using var family = OpenFamily("Segoe UI");
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        void Text(string text, float size, float y, Color color)
        {
            using var path = new GraphicsPath();
            path.AddString(text, family, (int)FontStyle.Bold, size, new PointF(240, y), centered);
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }
        Text(alert.Title, 40, 232, Color.White);
        Text(alert.Detail, 72, 305, AlertReading);
        if (alerts.Count > 1) Text($"{index + 1} of {alerts.Count}", 20, 378, Color.FromArgb(170, 170, 176));
    }

    /// <summary>Where a capture sits on the 480 unit square. It keeps the shape of its desktop area.</summary>
    public static RectangleF? CaptureBox(ElementSpec element)
    {
        if (element.CaptureWidth <= 0 || element.CaptureHeight <= 0) return null;
        var width = Math.Max(element.Size, MinCaptureSize);
        var height = width * element.CaptureHeight / element.CaptureWidth;
        return new RectangleF(element.X - width / 2, element.Y - height / 2, width, height);
    }

    /// <summary>Painted with a brush, not a clip, because only a fill gives the rounded corners smooth edges.</summary>
    void DrawCapture(Graphics g, ElementSpec element, float scale, HashSet<CaptureKey> used)
    {
        if (CaptureBox(element) is not { } box) return;
        var fitted = new Size(Math.Max((int)Math.Ceiling(box.Width * scale), 1), Math.Max((int)Math.Ceiling(box.Height * scale), 1));
        var stored = new Rectangle(element.CaptureX, element.CaptureY, element.CaptureWidth, element.CaptureHeight);
        var key = element.Source switch
        {
            CaptureSource.Window => new CaptureKey(Rectangle.Empty, element.CaptureProcess, element.CaptureTitle, fitted),
            CaptureSource.Screen => new CaptureKey(ScreenCapture.ScreenBounds(element.CaptureScreen) ?? stored, null, null, fitted),
            _ => new CaptureKey(stored, null, null, fitted),
        };
        if (!_captures.TryGetValue(key, out var capture))
        {
            _captures[key] = capture = element.Source == CaptureSource.Window
                ? new WindowCapture(key.Process, key.Title, fitted)
                : new ScreenCapture(key.Area, fitted);
        }
        used.Add(key);
        capture.Refresh();
        IsAnimating = true; // the desktop can change at any moment

        using var brush = new TextureBrush(capture.Image, WrapMode.Clamp);
        brush.TranslateTransform(box.X, box.Y);
        brush.ScaleTransform(box.Width / fitted.Width, box.Height / fitted.Height);
        using var shape = RoundedBox(box, Math.Clamp(element.CornerRadius, 0, Math.Min(box.Width, box.Height) / 2));
        g.FillPath(brush, shape);
    }

    static GraphicsPath RoundedBox(RectangleF box, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(box);
            return path;
        }
        var diameter = radius * 2;
        path.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
        path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
        path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Encodes the last rendered frame turned clockwise. The canvas itself stays upright.</summary>
    public byte[] EncodeRotated(int degrees)
    {
        if (degrees is 90 or 270 && _canvas.Width != _canvas.Height) degrees = 0; // the panel's size can't swap
        using var rotated = (Bitmap)_canvas.Clone();
        rotated.RotateFlip(degrees switch
        {
            90 => RotateFlipType.Rotate90FlipNone,
            180 => RotateFlipType.Rotate180FlipNone,
            270 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone,
        });
        return JpegFrame.Encode(rotated, _quality);
    }

    public static string TextOf(ElementSpec element, Stats stats, DateTime now) => element.Kind switch
    {
        ElementKind.Metric => FormatReading(stats.Get(element.Metric), element.Unit, element.Metric is Metric.MemoryUsed or Metric.MemoryAvailable),
        ElementKind.Clock or ElementKind.Date => FormatTime(now, element.Format),
        _ => element.Text ?? "",
    };

    /// <summary>Uses a fixed culture, so a format shows the same separators as its preview in the editor.</summary>
    static string FormatTime(DateTime now, string format)
    {
        try { return now.ToString(format, CultureInfo.InvariantCulture); }
        catch (FormatException) { return now.ToString("HH:mm", CultureInfo.InvariantCulture); }
    }

    static string FormatReading(Reading reading, string? unit, bool oneDecimal) =>
        reading is { State: ReadingState.Ok, Value: { } value } ? value.ToString(oneDecimal ? "F1" : "F0", CultureInfo.InvariantCulture) + unit : NoValue;

    /// <summary>Where a value sits on the gauge, from 0 to 1. A bad range reads as empty.</summary>
    public static double GaugeShare(double value, float min, float max) =>
        max > min ? Math.Clamp((value - min) / (max - min), 0, 1) : 0;

    static Color Blend(Color from, Color to, float share) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * share),
        (int)(from.G + (to.G - from.G) * share),
        (int)(from.B + (to.B - from.B) * share));

    /// <summary>
    /// One step of a critically damped spring. It keeps its speed when the target changes, so a new
    /// reading bends the motion instead of restarting it, and a once-a-second sensor never shows a pause.
    /// </summary>
    public static (double Shown, double Speed) GaugeStep(double shown, double speed, double target, double seconds)
    {
        var offset = shown - target;
        var push = (speed + GaugeSpringRate * offset) * seconds;
        var decay = Math.Exp(-GaugeSpringRate * seconds);
        return (target + (offset + push) * decay, (speed - GaugeSpringRate * push) * decay);
    }

    double EasedShare(int index, ElementSpec element, double target)
    {
        var key = (index, element.Metric);
        var (shown, speed) = _gaugeValues.TryGetValue(key, out var last) ? last : (target, 0);
        // A settled theme redraws rarely, so the frame after a pause must not count that pause as motion.
        (shown, speed) = GaugeStep(shown, speed, target, Math.Min(_frameSeconds, GaugeLongestStep));
        if (Math.Abs(target - shown) < GaugeSettledShare && Math.Abs(speed) < GaugeSettledSpeed) (shown, speed) = (target, 0);
        else IsAnimating = true;
        _gaugeValues[key] = (shown, speed);
        return shown;
    }

    /// <summary>The ring fills with the stat, and its color at the tip shows how high the stat is.</summary>
    void DrawGauge(Graphics g, int index, ElementSpec element, Stats stats)
    {
        var size = Math.Max(element.Size, 40f);
        var thickness = Math.Clamp(element.Thickness, 2f, size / 3);
        var sweep = Math.Clamp(Math.Abs(element.Sweep), 10f, 360f);
        var direction = element.Sweep < 0 ? -1f : 1f; // a negative sweep runs counterclockwise
        var ring = new RectangleF(element.X - size / 2 + thickness / 2, element.Y - size / 2 + thickness / 2, size - thickness, size - thickness);

        var radius = ring.Width / 2;
        var degreesPerPixel = 180 / (MathF.PI * Math.Max(radius, 1));
        var from = ParseColor(element.Color, Color.Cyan);
        var to = ParseColor(element.EndColor, from);
        var trackBase = ParseColor(element.TrackColor, Color.FromArgb(42, 42, 46));
        Color Shade(float angle) => Blend(from, to, angle / sweep);
        Color Track(float angle) => Blend(trackBase, Shade(angle), TrackTint);

        // Angles here are measured along the gauge from its start, in the gauge's own direction.
        void Cap(Color color, float angle, float diameter)
        {
            var radians = (element.StartAngle + direction * angle) * MathF.PI / 180;
            var center = new PointF(element.X + radius * MathF.Cos(radians), element.Y + radius * MathF.Sin(radians));
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, center.X - diameter / 2, center.Y - diameter / 2, diameter, diameter);
        }

        // One long wide arc leaves a spike at its end, and round pen caps come out jagged,
        // so every arc is built from short pieces and its ends are drawn as circles.
        void Arc(float start, float end, float pieceDegrees, Func<float, Color> color, float width)
        {
            for (var angle = start; angle < end; angle += pieceDegrees)
            {
                using var pen = new Pen(color(angle), width);
                var piece = Math.Min(pieceDegrees, end - angle) + 0.4f; // the overlap hides seams
                g.DrawArc(pen, ring, element.StartAngle + direction * angle, direction * piece);
            }
        }

        // The last of the track narrows to nothing, so nothing pops out of view near the top of the range.
        void TrackFrom(float start)
        {
            if (start >= sweep) return;
            var width = thickness * Math.Min((sweep - start) / (thickness * degreesPerPixel), 1);
            Arc(start, sweep, TrackStepDegrees, Track, width);
            Cap(Track(start), start, width);
            Cap(Track(sweep), sweep, width);
        }

        if (stats.Get(element.Metric) is not { State: ReadingState.Ok, Value: { } value })
        {
            _gaugeValues.Remove((index, element.Metric));
            if (element.UseTrackColor) TrackFrom(0);
            else GradientFrom(0, sweep);
            return;
        }

        var filled = (float)EasedShare(index, element, GaugeShare(value, element.Min, element.Max)) * sweep;

        void GradientFrom(float start, float end)
        {
            if (end <= start) return;
            Arc(start, end, GaugeStepDegrees, Shade, thickness);
            Cap(Shade(start), start, thickness);
            Cap(Shade(end), end, thickness);
        }

        // The whole arc shows the gradient, broken by a gap around a larger marker at the reading.
        if (!element.UseTrackColor)
        {
            var marker = MarkerSize(thickness);
            var clearance = (marker / 2 + thickness * MarkerGapShare + thickness / 2) * degreesPerPixel;
            GradientFrom(0, filled - clearance);
            GradientFrom(filled + clearance, sweep);
            Cap(Shade(filled), filled, marker);
            return;
        }

        // Past the rounded tip comes a gap, a separate dot, another gap, and then the unfilled track.
        var dotDiameter = thickness * GaugeDotShare;
        var gap = thickness * GaugeGapShare;
        var dotAngle = filled + (thickness / 2 + gap + dotDiameter / 2) * degreesPerPixel;
        var trackStart = dotAngle + (dotDiameter / 2 + gap + thickness / 2) * degreesPerPixel;
        TrackFrom(element.Dot ? trackStart : filled);
        var dotRoom = (sweep - dotAngle) / (dotDiameter * degreesPerPixel);
        if (element.Dot && dotRoom > 0) Cap(Shade(dotAngle), dotAngle, dotDiameter * Math.Min(dotRoom, 1));

        Cap(from, 0, thickness);
        Arc(0, filled, GaugeStepDegrees, Shade, thickness);
        Cap(Shade(filled), filled, thickness);
    }

    /// <summary>A pill that fills from its left end. Rotation points it any way, and 180 fills it right to left.</summary>
    void DrawBar(Graphics g, int index, ElementSpec element, Stats stats)
    {
        var length = Math.Max(element.Size, MinCaptureSize);
        var height = Math.Clamp(element.Thickness, MinBarThickness, length);
        var box = new RectangleF(-length / 2, -height / 2, length, height);
        var radius = Math.Clamp(element.CornerRadius, 0, height / 2);
        var saved = g.Save();
        g.TranslateTransform(element.X, element.Y);
        g.RotateTransform(element.Rotation);

        var start = ParseColor(element.Color, Color.Cyan);
        var end = ParseColor(element.EndColor, start);
        if (!element.UseTrackColor)
        {
            DrawMarkerBar(g, index, element, stats, box, radius, start, end);
            g.Restore(saved);
            return;
        }

        using (var trackShape = RoundedBox(box, radius))
        using (var track = new SolidBrush(ParseColor(element.TrackColor, Color.FromArgb(42, 42, 46))))
            g.FillPath(track, trackShape);

        if (stats.Get(element.Metric) is { State: ReadingState.Ok, Value: { } value })
        {
            var filled = (float)EasedShare(index, element, GaugeShare(value, element.Min, element.Max)) * length;
            if (filled > 0)
            {
                var fill = box with { Width = filled };
                var corner = Math.Min(radius, filled / 2);
                using var brush = new LinearGradientBrush(Inflated(box), start, end, 0f);
                using var shape = RoundedBox(fill, corner);
                g.FillPath(brush, shape);
                if (element.Stripes) DrawStripes(g, fill, corner);
            }
        }
        else _gaugeValues.Remove((index, element.Metric));
        g.Restore(saved);
    }

    static float MarkerSize(float thickness) => thickness + Math.Max(MinMarkerGrowth, thickness * MarkerGrowthShare);

    /// <summary>The whole bar shows the gradient, broken by a gap around a round marker at the reading.</summary>
    void DrawMarkerBar(Graphics g, int index, ElementSpec element, Stats stats, RectangleF box, float radius, Color start, Color end)
    {
        using var brush = new LinearGradientBrush(Inflated(box), start, end, 0f);
        void Piece(float left, float right)
        {
            if (right - left <= 0.5f) return;
            var piece = box with { X = left, Width = right - left };
            var corner = Math.Min(radius, piece.Width / 2);
            using var shape = RoundedBox(piece, corner);
            g.FillPath(brush, shape);
            if (element.Stripes) DrawStripes(g, piece, corner);
        }

        if (stats.Get(element.Metric) is not { State: ReadingState.Ok, Value: { } value })
        {
            _gaugeValues.Remove((index, element.Metric));
            Piece(box.Left, box.Right);
            return;
        }

        var share = (float)EasedShare(index, element, GaugeShare(value, element.Min, element.Max));
        var marker = MarkerSize(box.Height);
        var x = Math.Clamp(box.Left + share * box.Width, box.Left + box.Height / 2, box.Right - box.Height / 2);
        var clearance = marker / 2 + box.Height * MarkerGapShare;
        Piece(box.Left, x - clearance);
        Piece(x + clearance, box.Right);
        using var dot = new SolidBrush(Blend(start, end, share));
        g.FillEllipse(dot, x - marker / 2, -marker / 2, marker, marker);
    }

    /// <summary>Each stripe stops at the fill's edge. Near the rounded tip that edge curves in, so a stripe shrinks away.</summary>
    static void DrawStripes(Graphics g, RectangleF fill, float radius)
    {
        using var pen = new Pen(Color.FromArgb(BarStripeAlpha, Color.Black), BarStripeWidth);
        for (var x = fill.Left + BarStripeSpacing; x < fill.Right; x += BarStripeSpacing)
        {
            var intoCorner = Math.Max(fill.Left + radius - x, x - (fill.Right - radius));
            var trim = intoCorner > 0 ? radius - MathF.Sqrt(Math.Max(radius * radius - intoCorner * intoCorner, 0)) : 0;
            g.DrawLine(pen, x, fill.Top + trim, x, fill.Bottom - trim);
        }
    }

    /// <summary>A gradient repeats past its edges, so its box runs just wider than the shape.</summary>
    static RectangleF Inflated(RectangleF box) => RectangleF.Inflate(box, 1, 1);

    /// <summary>Lines and arcs are filled outlines, because wide pens leave jagged ends.</summary>
    static void DrawShape(Graphics g, ElementSpec element)
    {
        var width = Math.Max(element.Size, MinShapeSize);
        var stroked = element.Shape is ShapeKind.Line or ShapeKind.Arc;
        var thickness = Math.Max(element.Thickness, MinShapeSize);
        var height = element.Shape switch { ShapeKind.Line => thickness, ShapeKind.Arc => width, _ => Math.Max(element.Height, MinShapeSize) };
        var box = new RectangleF(-width / 2, -height / 2, width, height);

        using var place = new Matrix();
        place.Translate(element.X, element.Y);
        place.Rotate(element.Rotation);
        using var path = ShapePath(element, box, thickness);
        path.Transform(place);

        if (element.Shadow) DrawShadow(g, path, element);
        var alpha = (int)(Math.Clamp(element.Opacity, 0f, 1f) * 255);
        if (element.Filled || stroked)
        {
            var color = Color.FromArgb(alpha, ParseColor(element.Color, Color.White));
            using var solid = new SolidBrush(color);
            using var fade = new LinearGradientBrush(Inflated(box), color, Color.FromArgb(alpha, ParseColor(element.EndColor, color)), 0f) { Transform = place };
            g.FillPath(element.Gradient ? fade : solid, path);
        }
        if (element.Stroke && element.StrokeWidth > 0 && !stroked)
        {
            using var pen = new Pen(Color.FromArgb(alpha, ParseColor(element.StrokeColor, Color.Black)), element.StrokeWidth)
            {
                LineJoin = LineJoin.Round,
                DashStyle = element.LineStyle switch { LineStyle.Dashed => DashStyle.Dash, LineStyle.Dotted => DashStyle.Dot, _ => DashStyle.Solid },
            };
            g.DrawPath(pen, path);
        }
    }

    static GraphicsPath ShapePath(ElementSpec element, RectangleF box, float thickness)
    {
        var path = new GraphicsPath(FillMode.Winding);
        switch (element.Shape)
        {
            case ShapeKind.Line: AddLine(path, box, element.LineStyle); break;
            case ShapeKind.Arc: AddArcBand(path, box, Math.Min(thickness, box.Width / 2), element.StartAngle, Math.Clamp(element.Sweep, -360f, 360f)); break;
            case ShapeKind.Ellipse: path.AddEllipse(box); break;
            case ShapeKind.Rectangle: AddRoundedBox(path, box, element.CornerRadius); break;
            case ShapeKind.Triangle: path.AddPolygon(new PointF[] { new(0, box.Top), new(box.Right, box.Bottom), new(box.Left, box.Bottom) }); break;
            case ShapeKind.Diamond: path.AddPolygon(PointsAround(box, 4, 1)); break;
            case ShapeKind.Hexagon: path.AddPolygon(PointsAround(box, 6, 1)); break;
            default: path.AddPolygon(PointsAround(box, 10, StarInnerShare)); break;
        }
        return path;
    }

    static void AddRoundedBox(GraphicsPath path, RectangleF box, float radius)
    {
        using var rounded = RoundedBox(box, Math.Clamp(radius, 0, Math.Min(box.Width, box.Height) / 2));
        path.AddPath(rounded, false);
    }

    /// <summary>Points around the box, from the top. With an inner share, every second point sits closer in.</summary>
    static PointF[] PointsAround(RectangleF box, int count, float innerShare) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var angle = i * 2 * MathF.PI / count - MathF.PI / 2;
            var reach = i % 2 == 0 ? 1 : innerShare;
            return new PointF(MathF.Cos(angle) * box.Width / 2 * reach, MathF.Sin(angle) * box.Height / 2 * reach);
        }).ToArray();

    static void AddLine(GraphicsPath path, RectangleF box, LineStyle style)
    {
        var thickness = box.Height;
        if (style == LineStyle.Solid)
        {
            AddRoundedBox(path, box, thickness / 2);
            return;
        }
        var dotted = style == LineStyle.Dotted;
        var piece = dotted ? thickness : thickness * DashShare;
        var gap = thickness * (dotted ? DotGapShare : DashGapShare);
        var count = Math.Max((int)((box.Width + gap) / (piece + gap)), 1);
        var left = box.Left + (box.Width - (count * piece + (count - 1) * gap)) / 2; // the pattern stays centered
        for (var i = 0; i < count; i++, left += piece + gap)
            AddRoundedBox(path, new RectangleF(left, box.Top, Math.Min(piece, box.Width), thickness), thickness / 2);
    }

    static void AddArcBand(GraphicsPath path, RectangleF box, float thickness, float start, float sweep)
    {
        var inner = RectangleF.Inflate(box, -thickness, -thickness);
        path.AddArc(box, start, sweep);
        if (inner.Width > 0) path.AddArc(inner, start + sweep, -sweep);
        path.CloseFigure();
        var middle = (box.Width - thickness) / 2;
        foreach (var angle in new[] { start, start + sweep })
        {
            var radians = angle * MathF.PI / 180;
            path.AddEllipse(middle * MathF.Cos(radians) - thickness / 2, middle * MathF.Sin(radians) - thickness / 2, thickness, thickness);
        }
    }

    /// <summary>All text is a vector path, so enabling a stroke or shadow never shifts the letters.</summary>
    void Draw(Graphics g, ElementSpec element, Stats stats, DateTime now)
    {
        var text = TextOf(element, stats, now);
        if (text.Length == 0) return;

        using var format = new StringFormat
        {
            Alignment = element.Align switch
            {
                TextAlign.Center => StringAlignment.Center,
                TextAlign.Right => StringAlignment.Far,
                _ => StringAlignment.Near,
            },
        };
        var (family, style) = FontFor(element);
        var size = Math.Clamp(element.Size, 8f, 300f);
        using var path = new GraphicsPath();
        if (element.Kind == ElementKind.Metric && text.EndsWith(Degree)) AddDegreeReading(path, text[..^1], element, family, style, size);
        else path.AddString(text, family, (int)style, size, new PointF(element.X, element.Y), format);

        if (element.Shadow) DrawShadow(g, path, element);
        if (element.Stroke && element.StrokeWidth > 0)
        {
            // The outline is centered on the letter edge, and the fill covers its inner half.
            using var pen = new Pen(ParseColor(element.StrokeColor, Color.Black), element.StrokeWidth * 2) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, path);
        }
        using var brush = new SolidBrush(ParseColor(element.Color, Color.White));
        g.FillPath(brush, path);
    }

    /// <summary>A small raised degree sign. The number and sign align as one unit, so the pair stays centered.</summary>
    static void AddDegreeReading(GraphicsPath path, string number, ElementSpec element, FontFamily family, FontStyle style, float size)
    {
        using var layout = new StringFormat(StringFormat.GenericDefault);
        path.AddString(number, family, (int)style, size, new PointF(0, element.Y), layout);
        var digits = path.GetBounds();

        using var sign = new GraphicsPath();
        sign.AddString(Degree, family, (int)style, size * DegreeShare, PointF.Empty, layout);
        var ring = sign.GetBounds();
        using var raise = new Matrix();
        raise.Translate(digits.Right + size * DegreeGap - ring.Left, digits.Top - ring.Top);
        sign.Transform(raise);
        path.AddPath(sign, false);

        var whole = path.GetBounds();
        var left = element.Align switch
        {
            TextAlign.Center => element.X - (whole.Left + whole.Width / 2),
            TextAlign.Right => element.X - whole.Right,
            _ => element.X,
        };
        using var place = new Matrix();
        place.Translate(left, 0);
        path.Transform(place);
    }

    /// <summary>Blur is built from widening, faint outlines, which is cheap enough for every video frame.</summary>
    static void DrawShadow(Graphics g, GraphicsPath text, ElementSpec element)
    {
        var color = ParseColor(element.ShadowColor, Color.Black);
        using var shadow = (GraphicsPath)text.Clone();
        using var offset = new Matrix();
        offset.Translate(element.ShadowX, element.ShadowY);
        shadow.Transform(offset);

        var blur = Math.Max(element.ShadowBlur, 0);
        var steps = Math.Min((int)Math.Ceiling(blur), MaxBlurSteps);
        var alpha = steps == 0 ? 255 : Math.Max(ShadowStrength / (steps + 1), 12);
        using var brush = new SolidBrush(Color.FromArgb(alpha, color));
        for (var step = steps; step > 0; step--)
        {
            using var pen = new Pen(brush, blur * 2 * step / steps) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, shadow);
        }
        g.FillPath(brush, shadow);
    }

    (FontFamily Family, FontStyle Style) FontFor(ElementSpec element)
    {
        var key = (element.FontFamily ?? "", element.Bold, element.Italic);
        if (_fonts.TryGetValue(key, out var font)) return font;

        var name = string.IsNullOrWhiteSpace(element.FontFamily) ? "Segoe UI" : element.FontFamily;
        var bold = element.Bold;
        if (bold && name == "Segoe UI")
        {
            name = "Segoe UI Semibold"; // the system font's preferred display weight
            bold = false;
        }
        var style = (bold ? FontStyle.Bold : FontStyle.Regular) | (element.Italic ? FontStyle.Italic : FontStyle.Regular);
        var family = OpenFamily(name);
        font = (family, FirstAvailable(family, style));
        _fonts[key] = font;
        return font;
    }

    /// <summary>A theme can name a font that was since uninstalled.</summary>
    static FontFamily OpenFamily(string name)
    {
        try { return new FontFamily(name); }
        catch (ArgumentException) { return new FontFamily(GenericFontFamilies.SansSerif); }
    }

    /// <summary>Some fonts only ship certain styles, and asking for a missing one throws.</summary>
    static FontStyle FirstAvailable(FontFamily family, FontStyle wanted)
    {
        FontStyle[] options = [wanted, FontStyle.Regular, FontStyle.Bold, FontStyle.Italic, FontStyle.Bold | FontStyle.Italic];
        return options.FirstOrDefault(family.IsStyleAvailable, wanted);
    }

    public static Color ParseColor(string? text, Color fallback)
    {
        try { return string.IsNullOrWhiteSpace(text) ? fallback : ColorTranslator.FromHtml(text); }
        catch (Exception e) when (e is ArgumentException or FormatException) { return fallback; }
    }

    public void Dispose()
    {
        _canvas.Dispose();
        _fadeFrom?.Dispose();
        foreach (var capture in _captures.Values) capture.Dispose();
        _artImage?.Dispose();
        _screenTransform?.Dispose();
        foreach (var (family, _) in _fonts.Values) family.Dispose();
    }
}
