using TRStudio.Engine;
using Windows.Media.Control;

// Lists every media session Windows knows about, to check what TR Studio can read.
// "render <file>" also draws the built-in Now Playing theme with the first session.
// Windows' media objects only work on the thread that made them, so every call here waits in place.
static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

var manager = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask());
var sessions = manager.GetSessions();
Console.WriteLine($"Sessions: {sessions.Count}, current: {manager.GetCurrentSession()?.SourceAppUserModelId ?? "none"}");

foreach (var session in sessions)
{
    var media = Wait(session.TryGetMediaPropertiesAsync().AsTask());
    var playback = session.GetPlaybackInfo();
    var timeline = session.GetTimelineProperties();
    Console.WriteLine($"- {session.SourceAppUserModelId}: '{media.Title}' by '{media.Artist}', {playback.PlaybackStatus}, {timeline.Position} of {timeline.EndTime}");
}

if (args is ["render", var output] && sessions.Count > 0)
{
    var session = sessions[0];
    var media = Wait(session.TryGetMediaPropertiesAsync().AsTask());
    byte[]? art = null;
    if (media.Thumbnail is { } thumbnail)
    {
        using var stream = Wait(thumbnail.OpenReadAsync().AsTask()).AsStreamForRead();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        art = copy.ToArray();
    }
    var timeline = session.GetTimelineProperties();
    var song = new NowPlaying(media.Title, media.Artist, true, TimeSpan.FromSeconds(83), timeline.EndTime - timeline.StartTime, DateTime.UtcNow) { Art = art };
    var stats = Stats.Empty with { MusicProgress = song.ProgressAt(DateTime.UtcNow) };

    var theme = Theme.BuiltIns.Single(t => t.Name == "Now Playing");
    using var background = Backgrounds.Create(theme.Background);
    using (var renderer = new ThemeRenderer())
        File.WriteAllBytes(output, renderer.Render(background, theme.Elements, stats, DateTime.Now, media: song));

    var widget = new ElementSpec(ElementKind.Music, 240, 240, 320) { Bold = true, EndColor = "#00E5FF", TrackColor = "#2A2A2E", CornerRadius = 160 };
    using (var renderer = new ThemeRenderer())
        File.WriteAllBytes(Path.ChangeExtension(output, ".paused.jpg"), renderer.Render(background, [widget], stats, DateTime.Now, media: song with { IsPlaying = false }));
}
