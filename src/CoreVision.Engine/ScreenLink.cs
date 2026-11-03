namespace CoreVision.Engine;

public enum LinkState { Stopped, Searching, Busy, Connected, Suspended, Unsupported }

public sealed record LinkStatus(LinkState State, ScreenInfo? Screen, string Detail);

/// <summary>
/// Owns the screen on one thread and keeps it alive: any USB failure closes the device and
/// reconnects, and the newest frame always wins, so a slow transfer never builds a backlog.
/// </summary>
public sealed class ScreenLink : IDisposable
{
    static readonly TimeSpan MinRetry = TimeSpan.FromMilliseconds(250);
    static readonly TimeSpan MaxRetry = TimeSpan.FromSeconds(2);

    readonly Func<IScreenTransport> _open;
    readonly TimeSpan _keepAlive;
    readonly Action<string> _log;
    readonly AutoResetEvent _wake = new(false);
    readonly Thread _thread;
    byte[]? _pendingJpeg;
    byte[]? _lastJpeg;
    volatile bool _stopping;
    volatile bool _suspended;
    volatile LinkStatus _status = new(LinkState.Stopped, null, "Not started");

    public ScreenLink(Func<IScreenTransport> open, TimeSpan? keepAlive = null, Action<string>? log = null)
    {
        _open = open;
        _keepAlive = keepAlive ?? TimeSpan.FromSeconds(1);
        _log = log ?? (_ => { });
        _thread = new Thread(Run) { IsBackground = true, Name = "ScreenLink" };
    }

    public event Action<LinkStatus>? StatusChanged;
    public LinkStatus Status => _status;
    public long FramesSent => Interlocked.Read(ref _framesSent);
    long _framesSent;

    public void Start() => _thread.Start();

    /// <summary>Replaces any frame still waiting to be sent.</summary>
    public void Submit(byte[] jpeg)
    {
        Volatile.Write(ref _pendingJpeg, jpeg);
        _wake.Set();
    }

    public void Suspend()
    {
        _suspended = true;
        _wake.Set();
    }

    public void Resume()
    {
        _suspended = false;
        _wake.Set();
    }

    public void Dispose()
    {
        _stopping = true;
        _wake.Set();
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(5));
        _wake.Dispose();
    }

    void Run()
    {
        var retry = MinRetry;
        while (!_stopping)
        {
            if (_suspended)
            {
                SetStatus(LinkState.Suspended, null, "Paused while the PC sleeps");
                _wake.WaitOne();
                continue;
            }

            try
            {
                using var transport = _open();
                var screen = Connect(transport);
                retry = MinRetry;
                SetStatus(LinkState.Connected, screen, $"{screen.Firmware} {screen.Width}x{screen.Height}");
                Pump(transport, screen);
                continue;
            }
            catch (ScreenNotFoundException e) { SetStatus(LinkState.Searching, null, e.Message); }
            catch (ScreenBusyException e) { SetStatus(LinkState.Busy, null, e.Message); }
            catch (ScreenUnsupportedException e) { SetStatus(LinkState.Unsupported, null, e.Message); }
            catch (Exception e) when (e is IOException or InvalidDataException or System.ComponentModel.Win32Exception)
            {
                SetStatus(LinkState.Searching, null, e.Message);
            }

            _wake.WaitOne(retry);
            retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, MaxRetry.Ticks));
        }
        SetStatus(LinkState.Stopped, null, "Stopped");
    }

    static ScreenInfo Connect(IScreenTransport transport) => transport.Device.Family.Connect(transport);

    /// <summary>Returns to reconnect after a pause; USB failures reach the caller as exceptions.</summary>
    void Pump(IScreenTransport transport, ScreenInfo screen)
    {
        // The last picture is rebuilt for this connection, since a reconnect can reach a different model.
        var family = transport.Device.Family;
        var frame = _lastJpeg == null ? null : family.BuildFrame(screen, _lastJpeg);
        while (!_stopping && !_suspended)
        {
            var jpeg = Interlocked.Exchange(ref _pendingJpeg, null);
            if (jpeg != null)
            {
                frame = family.BuildFrame(screen, jpeg);
                _lastJpeg = jpeg;
            }
            if (frame != null)
            {
                family.Send(transport, frame);
                Interlocked.Increment(ref _framesSent);
            }
            _wake.WaitOne(_keepAlive);
        }
    }

    void SetStatus(LinkState state, ScreenInfo? screen, string detail)
    {
        if (_status.State == state && _status.Detail == detail) return;
        _status = new LinkStatus(state, screen, detail);
        _log($"{state}: {detail}");
        StatusChanged?.Invoke(_status);
    }
}
