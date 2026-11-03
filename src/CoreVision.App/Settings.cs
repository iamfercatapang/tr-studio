using System.Text.Json;
using CoreVision.Engine;

namespace CoreVision.App;

sealed record Settings(string? ThemePath = null, int Rotation = 0, bool PausedTrccStartup = false, ThemePlaylist? Playlist = null,
    AlertSettings? Alerts = null, WeatherPlace? WeatherPlace = null, WeatherUnits WeatherUnits = WeatherUnits.Metric)
{
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CoreVisionStudio");

    static string FilePath => Path.Combine(DataFolder, "settings.json");

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
