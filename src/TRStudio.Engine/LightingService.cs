using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TRStudio.Engine;

public enum LightingState { Off, NoServer, Connected }

/// <summary>
/// Keeps the PC's lights on what the user chose. It owns the link to the lighting hardware on one thread:
/// a lost link is dropped and tried again every few seconds, and after each connect every device gets
/// its colors again. Every device follows the lighting canvas, drawn here and sent 30 times a second while
/// the effect moves, unless the user set it to Off or to one of its own effects, which is sent once.
/// Turning lighting off, sleeping or quitting leaves the LEDs as they are.
/// </summary>
public sealed class LightingService : IDisposable
{
    public static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);
    public const int FramesPerSecond = 30;
    static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(1);
    /// <summary>How long waking waits before connecting: USB devices come back and the board puts back its own effect in that time.</summary>
    public static readonly TimeSpan ResumeSettle = TimeSpan.FromSeconds(3);
    /// <summary>
    /// Seconds after a connect at which every device is set up and sent its colors again from scratch. Board
    /// firmware can put its own effect back a while after waking, after TR Studio has already taken over.
    /// </summary>
    public static readonly double[] RefreshAfterConnect = [5, 15];
    /// <summary>
    /// How often every device on the canvas or set to Off is set up and sent its colors again, even when nothing
    /// changed, so a device that went back to its own effect is put right.
    /// </summary>
    public static readonly TimeSpan KeepFresh = TimeSpan.FromSeconds(5);

    readonly Func<ILightingClient> _connect;
    readonly Action<string> _log;
    readonly object _gate = new();
    readonly AutoResetEvent _wake = new(false);
    readonly Dictionary<string, LightChoice> _applied = [];
    readonly HashSet<string> _resized = [];
    readonly Dictionary<string, Rgb[]> _colors = [];
    readonly Dictionary<string, Rgb[]> _sent = [];
    /// <summary>Stands for "follows the canvas" among the choices already applied to devices.</summary>
    static readonly LightChoice OnCanvas = new(LightKind.Smooth);
    double _stepSeconds;
    long _stepAt = Stopwatch.GetTimestamp();
    Thread? _thread;
    ILightingClient? _client;
    LightingSettings _settings;
    IReadOnlyList<LightDevice> _devices = [];
    double _nextTry;
    double _connectedAt;
    double _freshAt;
    int _refreshes;
    string? _error;
    volatile LightingState _state = LightingState.Off;
    volatile bool _suspended;
    volatile bool _stopping;
    volatile bool _reconnect;
    volatile bool _resumed;

    public LightingService(Func<ILightingClient> connect, LightingSettings settings, Action<string>? log = null)
    {
        _connect = connect;
        _settings = settings;
        _log = log ?? (_ => { });
    }

    /// <summary>Raised on the service's thread when the state or the device list changes.</summary>
    public event Action? Changed;

    public LightingState State => _state;

    public IReadOnlyList<LightDevice> Devices
    {
        get { lock (_gate) return _devices; }
    }

    /// <summary>
    /// Where every strip of the listed devices sits on the canvas: where the user put it, or a default spot.
    /// Pass the devices through <see cref="LightParts.Split"/> first so divided strips get a place for each part.
    /// </summary>
    public static Dictionary<string, LightPlacement> Layout(CanvasSettings canvas, IReadOnlyList<LightDevice> devices)
    {
        var zones = devices.SelectMany(device => device.Zones.Select(zone => (LightingSettings.HeaderKey(device.Key, zone.Name), zone.LedCount, zone.Shape))).ToList();
        var layout = LightPlacement.Arrange(zones);
        foreach (var (zone, placement) in canvas.Placements ?? [])
        {
            if (layout.ContainsKey(zone)) layout[zone] = placement.Clamped();
        }
        return layout;
    }

    /// <summary>A small picture of the canvas as it is right now, three bytes a pixel, for the page.</summary>
    public byte[] RenderPreview(int width, int height)
    {
        LightingSettings settings;
        double seconds;
        lock (_gate)
        {
            settings = _settings;
            seconds = _stepSeconds + Stopwatch.GetElapsedTime(_stepAt).TotalSeconds;
        }
        var canvas = settings.ActiveCanvas;
        var effect = CanvasEffects.Find(canvas.Effect);
        return CanvasEffects.Render(effect, canvas.ValuesFor(effect), seconds, width, height, canvas.Brightness / 100.0);
    }

    /// <summary>The colors last sent to the device, for the page to draw. Null while the device runs its own effect.</summary>
    public Rgb[]? Preview(string deviceKey)
    {
        lock (_gate) return _colors.TryGetValue(deviceKey, out var colors) ? (Rgb[])colors.Clone() : null;
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Lighting" };
        _thread.Start();
    }

    /// <summary>Takes effect on the next pass, at once if the service is waiting.</summary>
    public void Configure(LightingSettings settings)
    {
        lock (_gate)
        {
            if (settings.Enabled && !_settings.Enabled) _nextTry = 0;
            _settings = settings;
        }
        _wake.Set();
    }

    /// <summary>Closes the link and opens it again on the next pass, for when which drivers are in use has changed.</summary>
    public void Reconnect()
    {
        _reconnect = true;
        RetryNow();
    }

    /// <summary>Tries the server now instead of waiting out the retry pause.</summary>
    public void RetryNow()
    {
        lock (_gate) _nextTry = 0;
        _wake.Set();
    }

    public void Suspend()
    {
        _suspended = true;
        _wake.Set();
    }

    /// <summary>
    /// Devices forget their colors over sleep, so waking connects again and sends every choice, after
    /// <see cref="ResumeSettle"/> so the hardware is back first.
    /// </summary>
    public void Resume()
    {
        _resumed = true;
        _suspended = false;
        _wake.Set();
    }

    public void Dispose()
    {
        _stopping = true;
        _wake.Set();
        if (_thread is { IsAlive: true }) _thread.Join(TimeSpan.FromSeconds(5));
        Drop();
        _wake.Dispose();
    }

    void Run()
    {
        var clock = Stopwatch.StartNew();
        var frame = TimeSpan.FromSeconds(1.0 / FramesPerSecond);
        var fineTimer = false;
        while (!_stopping)
        {
            var started = clock.Elapsed;
            var drawing = Step(started.TotalSeconds);
            // Windows wakes a waiting thread only every 15 ms or so unless asked for better, which would drop
            // a 30 a second effect to about 20. The finer timer is held only while an effect is running.
            if (drawing != fineTimer)
            {
                if (drawing) timeBeginPeriod(1); else timeEndPeriod(1);
                fineTimer = drawing;
            }
            var wait = drawing ? frame - (clock.Elapsed - started) : IdleCheck;
            if (wait > TimeSpan.Zero) _wake.WaitOne(wait);
        }
        if (fineTimer) timeEndPeriod(1);
    }

    [DllImport("winmm")]
    static extern uint timeBeginPeriod(uint milliseconds);
    [DllImport("winmm")]
    static extern uint timeEndPeriod(uint milliseconds);

    /// <summary>
    /// One pass: connect if needed, send what changed, draw one frame of the canvas.
    /// Returns true while a moving effect is showing, which is when the next pass is due a frame later.
    /// </summary>
    public bool Step(double seconds)
    {
        LightingSettings settings;
        double nextTry;
        lock (_gate)
        {
            (settings, nextTry) = (_settings, _nextTry);
            (_stepSeconds, _stepAt) = (seconds, Stopwatch.GetTimestamp());
        }

        if (_reconnect)
        {
            _reconnect = false;
            Drop();
        }
        if (!settings.Enabled)
        {
            Drop();
            SetState(LightingState.Off, []);
            return false;
        }
        if (_suspended)
        {
            Drop();
            return false;
        }
        if (_resumed)
        {
            _resumed = false;
            Drop();
            nextTry = seconds + ResumeSettle.TotalSeconds;
            lock (_gate) _nextTry = nextTry;
            _log($"Lighting: reconnecting {ResumeSettle.TotalSeconds:0} s after wake");
        }

        try
        {
            if (_client == null)
            {
                if (seconds < nextTry) return false;
                _client = _connect();
                var found = _client.Devices();
                _client.TakeDevicesChanged();
                _log($"Lighting connected: {found.Count} device{(found.Count == 1 ? "" : "s")}");
                _error = null;
                (_connectedAt, _freshAt, _refreshes) = (seconds, seconds, 0);
                // Always the new list: the old one may say a header was already told its size, which the new link
                // hasn't been, and the header would then get no colors at all.
                SetState(LightingState.Connected, found, always: true);
            }
            else if (_client.TakeDevicesChanged()) Reload();
            else RefreshIfDue(seconds);

            if (ResizeHeaders(settings)) Reload();

            var canvas = settings.ActiveCanvas;
            var effect = CanvasEffects.Find(canvas.Effect);
            var values = canvas.ValuesFor(effect);
            var parted = LightParts.Split(Devices, settings.Parts);
            var layout = Layout(canvas, parted);
            var drawing = false;
            foreach (var device in parted)
            {
                var own = settings.Override(device.Key);
                var wanted = own ?? OnCanvas;
                if (!_applied.TryGetValue(device.Key, out var applied) || !applied.Same(wanted))
                {
                    if (own != null) Apply(device, own);
                    else
                    {
                        _client.SetDirect(device.Index);
                        _sent.Remove(device.Key);
                    }
                    _applied[device.Key] = wanted;
                }
                if (own != null || device.LedCount == 0) continue;
                Draw(device, effect, values, canvas, layout, seconds);
                drawing |= effect.Moving;
            }
            return drawing;
        }
        catch (LightingUnavailableException e)
        {
            Drop();
            lock (_gate) _nextTry = seconds + Retry.TotalSeconds;
            // Logged once per kind of failure, so a PC without the server doesn't fill the log.
            if (e.Message != _error) _log($"Lighting unavailable: {e.Message}");
            _error = e.Message;
            SetState(LightingState.NoServer, []);
            return false;
        }
    }

    /// <summary>
    /// Sets every device up again a couple of times shortly after connecting, then sends the current frame
    /// again every <see cref="KeepFresh"/>, even when it hasn't changed.
    /// </summary>
    void RefreshIfDue(double seconds)
    {
        if (_refreshes < RefreshAfterConnect.Length && seconds - _connectedAt >= RefreshAfterConnect[_refreshes])
        {
            while (_refreshes < RefreshAfterConnect.Length && seconds - _connectedAt >= RefreshAfterConnect[_refreshes]) _refreshes++;
            _applied.Clear();
            _sent.Clear();
            _freshAt = seconds;
        }
        else if (seconds - _freshAt >= KeepFresh.TotalSeconds)
        {
            // A board can drop back to its own effect well after waking, and then ignores colors until it is
            // put in direct mode again, so that goes out too. A device on one of its own effects is left alone,
            // since setting it again would restart its animation every few seconds.
            foreach (var (key, applied) in _applied.ToList())
            {
                if (applied.Kind != LightKind.DeviceEffect) _applied.Remove(key);
            }
            _sent.Clear();
            _freshAt = seconds;
        }
    }

    void Reload()
    {
        _applied.Clear();
        _sent.Clear();
        SetState(LightingState.Connected, _client!.Devices(), always: true);
    }

    /// <summary>Tells the server how many LEDs each addressable header has. Each size is tried once per connection.</summary>
    bool ResizeHeaders(LightingSettings settings)
    {
        if (settings.HeaderLeds is not { Count: > 0 } headers) return false;
        var resized = false;
        foreach (var device in Devices)
        {
            for (var zone = 0; zone < device.Zones.Count; zone++)
            {
                var header = device.Zones[zone];
                var key = LightingSettings.HeaderKey(device.Key, header.Name);
                if (!header.Resizable || !headers.TryGetValue(key, out var wanted)) continue;
                var leds = Math.Clamp(wanted, header.MinLeds, header.MaxLeds);
                if (leds == header.LedCount || !_resized.Add($"{key}={leds}")) continue;
                _client!.ResizeZone(device.Index, zone, leds);
                resized = true;
            }
        }
        return resized;
    }

    void Apply(LightDevice device, LightChoice choice)
    {
        var client = _client!;
        var mode = choice.Kind switch
        {
            LightKind.Off => Mode(device, "Off"),
            LightKind.DeviceEffect => Mode(device, choice.Mode),
            _ => null,
        };
        if (mode != null)
        {
            var colors = mode.Colors == 1 ? [Rgb.Parse(choice.Color)] : choice.Palette().Take(mode.Colors).ToList();
            client.SetMode(device.Index, mode.Name, choice.Speed, colors);
            ShowPreview(device.Key, choice.Kind == LightKind.Off ? new Rgb[device.LedCount] : null);
            return;
        }

        client.SetDirect(device.Index);
        // A device without the chosen effect shows the plain color instead of staying on something stale.
        var color = choice.Kind == LightKind.Off ? Rgb.Black : Rgb.Parse(choice.Color).Scaled(choice.Brightness / 100.0);
        var leds = new Rgb[device.LedCount];
        Array.Fill(leds, color);
        if (leds.Length > 0) client.SetLeds(device.Index, leds);
        ShowPreview(device.Key, leds);
    }

    static LightMode? Mode(LightDevice device, string? name) =>
        name == null ? null : device.Modes.FirstOrDefault(mode => string.Equals(mode.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Every LED takes the canvas color at its own place, through its strip's box. A frame the same as the
    /// last one sent is not sent again, so an effect that stands still costs the hardware nothing.
    /// </summary>
    /// <summary>The colors blended as straight channel values, like every blend on the LEDs.</summary>
    public static Rgb Average(IEnumerable<Rgb> colors)
    {
        int red = 0, green = 0, blue = 0, count = 0;
        foreach (var color in colors)
        {
            red += color.R;
            green += color.G;
            blue += color.B;
            count++;
        }
        return count == 0 ? Rgb.Black : new Rgb((byte)Math.Round((double)red / count), (byte)Math.Round((double)green / count), (byte)Math.Round((double)blue / count));
    }

    void Draw(LightDevice device, CanvasEffect effect, EffectValues values, CanvasSettings canvas, Dictionary<string, LightPlacement> layout, double seconds)
    {
        var leds = new Rgb[device.LedCount];
        // A device whose zones don't add up to its LEDs is treated as one strip in its first zone's box.
        var zones = device.Zones.Sum(zone => zone.LedCount) == leds.Length ? device.Zones : [new LightZone(device.Zones.FirstOrDefault()?.Name ?? "", leds.Length, 0, 0)];
        var start = 0;
        foreach (var zone in zones)
        {
            if (layout.TryGetValue(LightingSettings.HeaderKey(device.Key, zone.Name), out var placement))
            {
                var dim = canvas.Brightness / 100.0 * placement.Brightness / 100.0;
                if (zone.LedCount == 1)
                {
                    // One color for the whole zone (a graphics card, a plain RGB header): the average over its box.
                    leds[start] = Average(placement.Area().Select(point => effect.Draw(point.X, point.Y, seconds, values))).Scaled(dim);
                }
                else
                {
                    var points = placement.Points(zone.LedCount, zone.Shape);
                    for (var i = 0; i < points.Length; i++) leds[start + i] = effect.Draw(points[i].X, points[i].Y, seconds, values).Scaled(dim);
                }
            }
            start += zone.LedCount;
        }
        if (_sent.TryGetValue(device.Key, out var last) && last.AsSpan().SequenceEqual(leds)) return;
        _client!.SetLeds(device.Index, leds);
        _sent[device.Key] = leds;
        ShowPreview(device.Key, leds);
    }

    void ShowPreview(string deviceKey, Rgb[]? colors)
    {
        lock (_gate)
        {
            if (colors == null) _colors.Remove(deviceKey);
            else _colors[deviceKey] = colors;
        }
    }

    void Drop()
    {
        _client?.Dispose();
        _client = null;
        _applied.Clear();
        _resized.Clear();
        _sent.Clear();
    }

    void SetState(LightingState state, IReadOnlyList<LightDevice> devices, bool always = false)
    {
        lock (_gate)
        {
            if (!always && state == _state && devices.Count == _devices.Count) return;
            _state = state;
            _devices = devices;
            foreach (var gone in _colors.Keys.Where(key => devices.All(device => device.Key != key)).ToList()) _colors.Remove(gone);
        }
        Changed?.Invoke();
    }
}
