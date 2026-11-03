using System.Globalization;

namespace CoreVision.Engine;

/// <summary>
/// What a media app reports through Windows, such as Spotify or a browser tab. Browsers report
/// the position rarely, so while a song plays the position is worked out from when it was read.
/// </summary>
public sealed record NowPlaying(string Title, string Artist, bool IsPlaying, TimeSpan? Position, TimeSpan? Duration, DateTime ReadUtc)
{
    /// <summary>Encoded image bytes. The same array is kept while the song is the same, so a decoded copy can be reused.</summary>
    public byte[]? Art { get; init; }
    public string Source { get; init; } = "";
    public bool CanPlayPause { get; init; }
    public bool CanNext { get; init; }
    public bool CanPrevious { get; init; }

    public TimeSpan? PositionAt(DateTime nowUtc)
    {
        if (Position is not { } position || Duration is not { } duration || duration <= TimeSpan.Zero) return null;
        if (IsPlaying) position += nowUtc - ReadUtc;
        return position < TimeSpan.Zero ? TimeSpan.Zero : position > duration ? duration : position;
    }

    /// <summary>Progress as a reading from 0 to 100, so gauges and bars can show it.</summary>
    public Reading ProgressAt(DateTime nowUtc) =>
        PositionAt(nowUtc) is { } position ? Reading.Check(position / Duration!.Value * 100, 0, 100) : new Reading(null, ReadingState.Missing);

    /// <summary>3:07, or 1:02:07 past an hour.</summary>
    public static string Clock(TimeSpan time) => time.TotalHours >= 1
        ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
        : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
}
