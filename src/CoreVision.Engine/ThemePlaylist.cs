namespace CoreVision.Engine;

/// <summary>Themes shown in turn, in list order. A null path is the System Stats theme.</summary>
public sealed record ThemePlaylist(bool Enabled = false, IReadOnlyList<string?>? Paths = null, int Seconds = ThemePlaylist.DefaultSeconds)
{
    public const int MinThemes = 2, MinSeconds = 5, MaxSeconds = 600, DefaultSeconds = 30;

    /// <summary>
    /// Drops themes that no longer exist and repeats, so the list holds at most every theme once.
    /// With too few left, the playlist turns itself off.
    /// </summary>
    public ThemePlaylist Usable(IEnumerable<string?> available)
    {
        var existing = available.ToHashSet();
        var paths = (Paths ?? []).Where(existing.Contains).Distinct().ToList();
        return new ThemePlaylist(Enabled && paths.Count >= MinThemes, paths, Math.Clamp(Seconds, MinSeconds, MaxSeconds));
    }

    /// <summary>The theme after the one showing. From a theme outside the list, it starts at the top.</summary>
    public string? Next(string? showing)
    {
        var paths = Paths ?? [];
        if (paths.Count == 0) return showing;
        var index = paths.ToList().IndexOf(showing);
        return paths[(index + 1) % paths.Count];
    }

    /// <summary>A renamed theme gets a new file, and it keeps its place in the list.</summary>
    public ThemePlaylist Renamed(string? from, string? to) =>
        this with { Paths = Paths?.Select(path => path == from ? to : path).ToList() };
}
