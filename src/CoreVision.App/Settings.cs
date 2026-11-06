using System.Text.Json;
using CoreVision.Engine;

namespace CoreVision.App;

sealed record Settings(string? ThemePath = null, int Rotation = 0, bool PausedTrccStartup = false, ThemePlaylist? Playlist = null,
    AlertSettings? Alerts = null, WeatherPlace? WeatherPlace = null, WeatherUnits WeatherUnits = WeatherUnits.Metric, bool CheckForUpdates = true,
    bool? RoundScreen = null, bool? CrashReports = null, FanChoice? Fans = null)
{
    // RoundScreen: null follows what the screen reports. CrashReports: null means the user hasn't been asked yet.
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CoreVisionStudio");

    static string FilePath => Path.Combine(DataFolder, "settings.json");

    /// <summary>False on a new install, before anything has been saved.</summary>
    public static bool Exists => File.Exists(FilePath);

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new Settings(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}
