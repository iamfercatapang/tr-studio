using System.Collections.Concurrent;
using CoreVision.Engine;
using Windows.Media.Control;

namespace CoreVision.App;

/// <summary>
/// Reads what is playing through Windows' media controls, which Spotify and browsers report to.
/// Windows' media objects only work on the thread that made them, so everything, including the
/// play and skip buttons, runs on one thread that checks once a second.
/// </summary>
sealed class MediaMonitor : IDisposable
{
    static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

    readonly Action<string> _log;
    readonly BlockingCollection<string> _commands = new();
    readonly CancellationTokenSource _stop = new();
    readonly Thread _thread;
    volatile NowPlaying? _current;

    public MediaMonitor(Action<string> log)
    {
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = "MediaMonitor" };
        _thread.Start();
    }

    /// <summary>Raised on the monitor's thread when the song, art or play state changes.</summary>
    public event Action? Changed;

    public NowPlaying? Current => _current;

    /// <summary>One of "playPause", "next" or "previous". Unknown commands are ignored.</summary>
    public void Send(string command)
    {
        if (!_stop.IsCancellationRequested) _commands.Add(command);
    }

    void Run()
    {
        GlobalSystemMediaTransportControlsSessionManager? manager = null;
        string? lastError = null;
        var wait = TimeSpan.Zero;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                // A button press wakes the loop early, so the screen and sidebar update at once.
                if (_commands.TryTake(out var command, (int)wait.TotalMilliseconds, _stop.Token) && manager != null) Control(manager, command);
            }
            catch (OperationCanceledException) { return; }

            wait = Interval;
            try
            {
                if (manager == null)
                {
                    manager = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask());
                    _log("Reading media info from Windows");
                }
                Update(Read(manager));
                if (lastError != null) _log("Media info is available again");
                lastError = null;
            }
            catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException)
            {
                // Logged once, so a PC without media support doesn't fill the log every second.
                if (e.Message != lastError) _log($"Media info unavailable: {e.Message}");
                lastError = e.Message;
                manager = null;
                Update(null);
                wait = RetryAfterFailure;
            }
        }
    }

    static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

    /// <summary>A session that is playing wins over Windows' choice, which can be a paused tab.</summary>
    static GlobalSystemMediaTransportControlsSession? Pick(GlobalSystemMediaTransportControlsSessionManager manager) =>
        manager.GetSessions().FirstOrDefault(s => s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        ?? manager.GetCurrentSession();

    void Control(GlobalSystemMediaTransportControlsSessionManager manager, string command)
    {
        try
        {
            if (Pick(manager) is not { } session) return;
            var done = command switch
            {
                "playPause" => Wait(session.TryTogglePlayPauseAsync().AsTask()),
                "next" => Wait(session.TrySkipNextAsync().AsTask()),
                "previous" => Wait(session.TrySkipPreviousAsync().AsTask()),
                _ => true,
            };
            if (!done) _log($"The media app ignored {command}");
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _log($"Media control failed: {e.Message}");
        }
    }

    NowPlaying? Read(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        if (Pick(manager) is not { } session) return null;

        var playback = session.GetPlaybackInfo();
        var status = playback?.PlaybackStatus;
        if (status is not (GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)) return null;

        var media = Wait(session.TryGetMediaPropertiesAsync().AsTask());
        if (media == null || string.IsNullOrWhiteSpace(media.Title)) return null;

        var timeline = session.GetTimelineProperties();
        var duration = timeline.EndTime - timeline.StartTime;
        // The position was true when the app last reported it, which can be well before now.
        var readAt = timeline.LastUpdatedTime.UtcDateTime;
        if (readAt == default || readAt > DateTime.UtcNow) readAt = DateTime.UtcNow;

        var previous = _current;
        var source = session.SourceAppUserModelId ?? "";
        var sameSong = previous != null && previous.Title == media.Title && previous.Artist == (media.Artist ?? "") && previous.Source == source;

        return new NowPlaying(media.Title, media.Artist ?? "", status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            duration > TimeSpan.Zero ? timeline.Position - timeline.StartTime : null,
            duration > TimeSpan.Zero ? duration : null, readAt)
        {
            Art = sameSong ? previous!.Art : ReadArt(media),
            Source = source,
            CanPlayPause = playback!.Controls.IsPlayPauseToggleEnabled,
            CanNext = playback.Controls.IsNextEnabled,
            CanPrevious = playback.Controls.IsPreviousEnabled,
        };
    }

    static byte[]? ReadArt(GlobalSystemMediaTransportControlsSessionMediaProperties media)
    {
        if (media.Thumbnail == null) return null;
        using var stream = Wait(media.Thumbnail.OpenReadAsync().AsTask()).AsStreamForRead();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.Length > 0 ? copy.ToArray() : null;
    }

    void Update(NowPlaying? next)
    {
        var previous = _current;
        _current = next;
        var now = DateTime.UtcNow;
        var jumped = Math.Abs(((previous?.PositionAt(now) ?? TimeSpan.Zero) - (next?.PositionAt(now) ?? TimeSpan.Zero)).TotalSeconds) > 2;
        var changed = previous?.Title != next?.Title || previous?.Artist != next?.Artist || previous?.IsPlaying != next?.IsPlaying
            || !ReferenceEquals(previous?.Art, next?.Art) || previous?.Duration != next?.Duration || jumped;
        if (changed) Changed?.Invoke();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread.Join(TimeSpan.FromSeconds(3));
        _commands.Dispose();
        _stop.Dispose();
    }
}
