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
    byte[]? _lastFrame;
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

    static ScreenInfo Connect(IScreenTransport transport)
    {
        var hello = ScreenProtocol.BuildHello();
        transport.Write(hello, hello.Length);
        var buffer = new byte[1024];
        var read = transport.Read(buffer);
        return ScreenProtocol.ParseHelloReply(buffer.AsSpan(0, read));
    }

    /// <summary>Returns to reconnect after a pause; USB failures reach the caller as exceptions.</summary>
    void Pump(IScreenTransport transport, ScreenInfo screen)
    {
        while (!_stopping && !_suspended)
        {
            var jpeg = Interlocked.Exchange(ref _pendingJpeg, null);
            if (jpeg != null) _lastFrame = ScreenProtocol.BuildFrame(screen.HeaderWidth, screen.HeaderHeight, jpeg);
            if (_lastFrame != null)
            {
                transport.Write(_lastFrame, _lastFrame.Length);
                if (ScreenProtocol.NeedsTerminator(_lastFrame.Length)) transport.Write([], 0);
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
