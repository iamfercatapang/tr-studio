using System.Diagnostics;

namespace CoreVision.Engine;

/// <summary>Renders the current theme on one thread. A theme change takes effect on the next frame.</summary>
public sealed class ThemePlayer : IDisposable
{
    static readonly TimeSpan GaugeFrame = TimeSpan.FromMilliseconds(33);

    readonly Func<Stats> _stats;
    readonly Action<byte[]> _submit;
    readonly Func<BackgroundSpec, IBackground> _openBackground;
    readonly Action<string> _log;
    readonly ManualResetEventSlim _changed = new(false);
    readonly Thread _thread;
    volatile Theme _theme;
    volatile ScreenInfo _screen = ScreenInfo.Default;
    volatile bool _stopping;
    volatile int _rotation;
    volatile IReadOnlyList<Alert> _alerts = [];
    int _fadePending;
    long _framesRendered;

    public ThemePlayer(Theme theme, Func<Stats> stats, Action<byte[]> submit,
        Func<BackgroundSpec, IBackground> openBackground, Action<string>? log = null)
    {
        _theme = theme;
        _stats = stats;
        _submit = submit;
        _openBackground = openBackground;
        _log = log ?? (_ => { });
        _thread = new Thread(Run) { IsBackground = true, Name = "ThemePlayer" };
    }

    /// <summary>What is playing, read on every frame. Null hides Music widgets.</summary>
    public Func<NowPlaying?> Media { get; init; } = () => null;

    /// <summary>The latest forecast, read on every frame. Null shows dashes in Weather widgets.</summary>
    public Func<WeatherReport?> Weather { get; init; } = () => null;

    public event Action<byte[]>? FrameRendered;
    public Theme Theme => _theme;
    public long FramesRendered => Interlocked.Read(ref _framesRendered);

    /// <summary>Turns only the image sent to the screen, so previews and the editor stay upright.</summary>
    public int Rotation
    {
        get => _rotation;
        set
        {
            _rotation = value is 90 or 180 or 270 ? value : 0;
            _changed.Set();
        }
    }

    /// <summary>Warnings cover whatever theme is playing until the list is empty again.</summary>
    public IReadOnlyList<Alert> Alerts
    {
        get => _alerts;
        set
        {
            _alerts = value;
            _changed.Set();
        }
    }

    public void Start() => _thread.Start();

    /// <summary>Draws a new frame now, for example when the song changes.</summary>
    public void Invalidate() => _changed.Set();

    /// <summary>Only a changed background reopens media, so live edits never restart a video.</summary>
    public void Play(Theme theme)
    {
        _theme = theme;
        _changed.Set();
    }

    /// <summary>Dissolves into the theme instead of cutting to it.</summary>
    public void FadeTo(Theme theme)
    {
        Interlocked.Exchange(ref _fadePending, 1);
        Play(theme);
    }

    /// <summary>The next frame is drawn at this screen's size and quality.</summary>
    public ScreenInfo Screen
    {
        get => _screen;
        set
        {
            _screen = value;
            _changed.Set();
        }
    }

    void Run()
    {
        while (!_stopping)
        {
            var screen = _screen;
            using var renderer = new ThemeRenderer(screen.Width, screen.Height, screen.JpegQuality);
            PlayOnScreen(renderer, screen);
        }
    }

    void PlayOnScreen(ThemeRenderer renderer, ScreenInfo screen)
    {
        var sinceAdvance = new Stopwatch();
        while (!_stopping && ReferenceEquals(screen, _screen))
        {
            var theme = _theme;
            using var background = _openBackground(theme.Background);
            sinceAdvance.Restart();
            while (!_stopping && ReferenceEquals(screen, _screen) && theme.Background == _theme.Background)
            {
                _changed.Reset();
                theme = _theme;
                try
                {
                    if (Interlocked.Exchange(ref _fadePending, 0) == 1) renderer.BeginFade();
                    var media = Media();
                    var stats = _stats();
                    var weather = Weather();
                    if (media != null) stats = stats with { MusicProgress = media.ProgressAt(DateTime.UtcNow) };
                    if (weather != null) stats = stats with { OutdoorTemp = weather.TemperatureAt(DateTime.UtcNow) };
                    var frame = renderer.Render(background, theme.Elements, stats, DateTime.Now, theme.Dim, _alerts, media, weather);
                    var rotation = _rotation;
                    _submit(rotation == 0 ? frame : renderer.EncodeRotated(rotation));
                    FrameRendered?.Invoke(frame);
                    Interlocked.Increment(ref _framesRendered);
                }
                catch (Exception e) when (e is InvalidOperationException or ArgumentException or OutOfMemoryException)
                {
                    _log($"Frame failed: {e.Message}");
                }
                // Extra frames for gauges or live edits must not speed up a GIF.
                if (sinceAdvance.Elapsed >= background.FrameDelay)
                {
                    background.Advance();
                    sinceAdvance.Restart();
                }
                var wait = background.FrameDelay - sinceAdvance.Elapsed;
                if (renderer.IsAnimating && wait > GaugeFrame) wait = GaugeFrame;
                if (wait > TimeSpan.Zero) _changed.Wait(wait);
            }
        }
    }

    public void Dispose()
    {
        _stopping = true;
        _changed.Set();
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(5));
        _changed.Dispose();
    }
}
