using System.Globalization;
using System.Text.Json;

namespace TRStudio.Engine;

public enum WeatherUnits { Metric, Imperial }

/// <summary>The few kinds of sky the icons draw. Open-Meteo reports WMO weather codes, which map onto these.</summary>
public enum WeatherSky { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Storm }

public sealed record WeatherDay(DateOnly Date, int Code, double High, double Low);

/// <summary>A place found by name, from Open-Meteo's geocoding search.</summary>
public sealed record WeatherPlace(string Name, string Region, string Country, double Latitude, double Longitude)
{
    public string Label => string.Join(", ", new[] { Name, Region, Country }.Where(part => !string.IsNullOrWhiteSpace(part)).Distinct());
}

/// <summary>
/// One forecast from Open-Meteo. A report older than two hours counts as stale, so a lost
/// connection shows dashes instead of yesterday's weather as if it were now.
/// </summary>
public sealed record WeatherReport(string Place, double Temperature, int Code, bool IsDay, double High, double Low,
    double Humidity, double Wind, WeatherUnits Units, IReadOnlyList<WeatherDay> Days, DateTime FetchedUtc)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);
    const double MinTemperature = -90, MaxTemperature = 140; // covers both °C and °F

    public bool IsStale(DateTime nowUtc) => nowUtc - FetchedUtc > StaleAfter;
    public string WindUnit => Units == WeatherUnits.Imperial ? "mph" : "km/h";

    /// <summary>The temperature as a reading, so gauges and bars can show it.</summary>
    public Reading TemperatureAt(DateTime nowUtc) =>
        IsStale(nowUtc) ? new Reading(null, ReadingState.Stale) : Reading.Check(Temperature, MinTemperature, MaxTemperature);

    public static WeatherSky SkyOf(int code) => code switch
    {
        0 or 1 => WeatherSky.Clear,
        2 => WeatherSky.PartlyCloudy,
        3 => WeatherSky.Cloudy,
        45 or 48 => WeatherSky.Fog,
        >= 51 and <= 57 => WeatherSky.Drizzle,
        >= 61 and <= 67 or >= 80 and <= 82 => WeatherSky.Rain,
        >= 71 and <= 77 or 85 or 86 => WeatherSky.Snow,
        >= 95 and <= 99 => WeatherSky.Storm,
        _ => WeatherSky.Cloudy,
    };

    public static string Describe(int code) => code switch
    {
        0 => "Clear",
        1 => "Mostly Clear",
        2 => "Partly Cloudy",
        3 => "Cloudy",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing Drizzle",
        61 => "Light Rain",
        63 => "Rain",
        65 => "Heavy Rain",
        66 or 67 => "Freezing Rain",
        71 => "Light Snow",
        73 => "Snow",
        75 => "Heavy Snow",
        77 => "Snow Grains",
        80 or 81 or 82 => "Showers",
        85 or 86 => "Snow Showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm, Hail",
        _ => "Unknown",
    };

    /// <summary>The query for a place, in the chosen units, with today and the next three days.</summary>
    public static string ForecastUrl(WeatherPlace place, WeatherUnits units) =>
        "https://api.open-meteo.com/v1/forecast"
        + $"?latitude={place.Latitude.ToString(CultureInfo.InvariantCulture)}&longitude={place.Longitude.ToString(CultureInfo.InvariantCulture)}"
        + "&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m,is_day"
        + "&daily=weather_code,temperature_2m_max,temperature_2m_min&forecast_days=4&timezone=auto"
        + (units == WeatherUnits.Imperial ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "");

    public static string SearchUrl(string name) =>
        $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name.Trim())}&count=6&language=en&format=json";

    /// <summary>Throws InvalidDataException for anything that isn't a complete forecast.</summary>
    public static WeatherReport Parse(string json, string place, WeatherUnits units, DateTime fetchedUtc)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var current = document.RootElement.GetProperty("current");
            var daily = document.RootElement.GetProperty("daily");
            var dates = daily.GetProperty("time").EnumerateArray().Select(d => DateOnly.Parse(d.GetString()!, CultureInfo.InvariantCulture)).ToList();
            var codes = daily.GetProperty("weather_code").EnumerateArray().Select(c => c.GetInt32()).ToList();
            var highs = daily.GetProperty("temperature_2m_max").EnumerateArray().Select(t => t.GetDouble()).ToList();
            var lows = daily.GetProperty("temperature_2m_min").EnumerateArray().Select(t => t.GetDouble()).ToList();
            if (dates.Count == 0 || codes.Count < dates.Count || highs.Count < dates.Count || lows.Count < dates.Count)
                throw new InvalidDataException("The forecast has no days.");

            var days = dates.Select((date, i) => new WeatherDay(date, codes[i], highs[i], lows[i])).ToList();
            return new WeatherReport(place,
                current.GetProperty("temperature_2m").GetDouble(),
                current.GetProperty("weather_code").GetInt32(),
                current.GetProperty("is_day").GetInt32() == 1,
                days[0].High, days[0].Low,
                current.GetProperty("relative_humidity_2m").GetDouble(),
                current.GetProperty("wind_speed_10m").GetDouble(),
                units, days.Skip(1).Take(3).ToList(), fetchedUtc);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"The forecast couldn't be read: {e.Message}", e);
        }
    }

    public static IReadOnlyList<WeatherPlace> ParsePlaces(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("results", out var results)) return [];
            string Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
            return results.EnumerateArray().Select(item => new WeatherPlace(Text(item, "name"), Text(item, "admin1"), Text(item, "country"),
                item.GetProperty("latitude").GetDouble(), item.GetProperty("longitude").GetDouble())).ToList();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException($"The place search couldn't be read: {e.Message}", e);
        }
    }
}
