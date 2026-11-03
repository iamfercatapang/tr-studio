using System.Drawing;
using CoreVision.Engine;
using Xunit;

public class WeatherTests
{
    static readonly DateTime Fetched = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    // The shape Open-Meteo returns for the forecast query, cut down to the fields TR Studio asks for.
    const string Forecast = """
        {
          "latitude": 51.5, "longitude": -0.12, "timezone": "Europe/London",
          "current": { "time": "2026-11-03T12:00", "temperature_2m": 11.6, "relative_humidity_2m": 78, "weather_code": 2, "wind_speed_10m": 14.2, "is_day": 1 },
          "daily": {
            "time": ["2026-11-03", "2026-11-04", "2026-11-05", "2026-11-06"],
            "weather_code": [2, 61, 3, 95],
            "temperature_2m_max": [13.1, 12.4, 10.0, 9.5],
            "temperature_2m_min": [7.2, 8.0, 5.5, 4.1]
          }
        }
        """;

    const string Places = """
        { "results": [
          { "name": "London", "latitude": 51.50853, "longitude": -0.12574, "country": "United Kingdom", "admin1": "England" },
          { "name": "London", "latitude": 42.98339, "longitude": -81.23304, "country": "Canada", "admin1": "Ontario" }
        ] }
        """;

    static WeatherReport Report() => WeatherReport.Parse(Forecast, "London", WeatherUnits.Metric, Fetched);

    sealed class Black : IBackground
    {
        public TimeSpan FrameDelay => TimeSpan.FromSeconds(1);
        public void Advance() { }
        public void Dispose() { }
        public void Draw(Graphics g) => g.Clear(Color.Black);
    }

    static readonly ElementSpec Widget = new(ElementKind.Weather, 240, 240, 300) { Bold = true };

    [Fact]
    public void Reads_an_Open_Meteo_forecast()
    {
        var report = Report();
        Assert.Equal(11.6, report.Temperature);
        Assert.Equal(2, report.Code);
        Assert.True(report.IsDay);
        Assert.Equal((13.1, 7.2), (report.High, report.Low));
        Assert.Equal((78.0, 14.2), (report.Humidity, report.Wind));
        Assert.Equal([new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 6)], report.Days.Select(d => d.Date));
        Assert.Equal(95, report.Days[2].Code);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "current": {}, "daily": { "time": [] } }""")]
    public void Rejects_a_broken_forecast(string json)
    {
        Assert.Throws<InvalidDataException>(() => WeatherReport.Parse(json, "X", WeatherUnits.Metric, Fetched));
    }

    [Fact]
    public void Finds_places_with_their_region_and_country()
    {
        var places = WeatherReport.ParsePlaces(Places);
        Assert.Equal(["London, England, United Kingdom", "London, Ontario, Canada"], places.Select(p => p.Label));
        Assert.Empty(WeatherReport.ParsePlaces("""{ "generationtime_ms": 0.5 }""")); // no match has no results
    }

    [Theory]
    [InlineData(0, WeatherSky.Clear, "Clear")]
    [InlineData(2, WeatherSky.PartlyCloudy, "Partly Cloudy")]
    [InlineData(45, WeatherSky.Fog, "Fog")]
    [InlineData(53, WeatherSky.Drizzle, "Drizzle")]
    [InlineData(81, WeatherSky.Rain, "Showers")]
    [InlineData(75, WeatherSky.Snow, "Heavy Snow")]
    [InlineData(99, WeatherSky.Storm, "Thunderstorm, Hail")]
    public void Weather_codes_map_to_icons_and_names(int code, WeatherSky sky, string name)
    {
        Assert.Equal(sky, WeatherReport.SkyOf(code));
        Assert.Equal(name, WeatherReport.Describe(code));
    }

    [Fact]
    public void Units_follow_the_setting()
    {
        var place = new WeatherPlace("London", "", "", 51.5, -0.12);
        Assert.Contains("temperature_unit=fahrenheit", WeatherReport.ForecastUrl(place, WeatherUnits.Imperial));
        Assert.DoesNotContain("fahrenheit", WeatherReport.ForecastUrl(place, WeatherUnits.Metric));
        Assert.Equal("mph", (Report() with { Units = WeatherUnits.Imperial }).WindUnit);
        Assert.Contains("latitude=51.5&", WeatherReport.ForecastUrl(place, WeatherUnits.Metric)); // never a comma decimal
    }

    [Fact]
    public void An_old_forecast_goes_stale()
    {
        Assert.Equal(11.6, Report().TemperatureAt(Fetched.AddMinutes(30)).Value);
        Assert.Equal(ReadingState.Stale, Report().TemperatureAt(Fetched.AddHours(3)).State);
    }

    [Fact]
    public void Gauges_can_show_the_outdoor_temperature()
    {
        var stats = Stats.Empty with { OutdoorTemp = Report().TemperatureAt(Fetched) };
        Assert.Equal(11.6, stats.Get(Metric.OutdoorTemp).Value);
    }

    [Fact]
    public void A_sunny_forecast_draws_a_sun()
    {
        using var renderer = new ThemeRenderer();
        var clear = Report() with { Code = 0, FetchedUtc = DateTime.UtcNow };
        using var bitmap = new Bitmap(new MemoryStream(renderer.Render(new Black(), [Widget], Stats.Empty, DateTime.Now, weather: clear)));
        var layout = ThemeRenderer.LayoutWeather(Widget);
        var yellow = 0;
        for (var y = (int)layout.Top; y < layout.Top + layout.IconSide; y += 2)
            for (var x = (int)layout.Bounds.Left; x < layout.Bounds.Right; x += 2)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.R > 200 && color.G > 170 && color.B < 90) yellow++;
            }
        Assert.True(yellow > 50, $"only {yellow} yellow pixels");
    }

    [Fact]
    public void Without_a_forecast_the_widget_still_draws_and_shows_no_icon()
    {
        using var renderer = new ThemeRenderer();
        Assert.NotEmpty(renderer.Render(new Black(), [Widget], Stats.Empty, DateTime.Now, weather: null));
        Assert.NotEmpty(renderer.Render(new Black(), [Widget], Stats.Empty, DateTime.Now, weather: Report()));  // stale, from a fixed past date
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(45)]
    [InlineData(61)] [InlineData(71)] [InlineData(95)]
    public void Every_icon_draws_at_any_size(int code)
    {
        using var bitmap = new Bitmap(100, 100);
        using var g = Graphics.FromImage(bitmap);
        foreach (var side in new[] { 4f, 40f, 200f })
            foreach (var day in new[] { true, false })
                WeatherIcons.Draw(g, WeatherReport.SkyOf(code), day, new PointF(50, 50), side);
    }

    [Fact]
    public void Hidden_parts_take_no_space()
    {
        var full = ThemeRenderer.LayoutWeather(Widget).Bounds.Height;
        var main = ThemeRenderer.LayoutWeather(Widget with { ShowCondition = false, ShowDetails = false, ShowForecast = false }).Bounds.Height;
        Assert.Equal(300 * 0.26, main, 1);
        Assert.True(full > main * 2.5);
    }

    [Fact]
    public void Weather_theme_fits_inside_the_round_screen()
    {
        var theme = Theme.BuiltIns.Single(t => t.Name == "Weather");
        var box = ThemeRenderer.LayoutWeather(theme.Elements.Single(e => e.Kind == ElementKind.Weather)).Bounds;
        foreach (var (x, y) in new[] { (box.Left, box.Top), (box.Right, box.Top), (box.Left, box.Bottom), (box.Right, box.Bottom) })
            Assert.True(Math.Sqrt((x - 240) * (x - 240) + (y - 240) * (y - 240)) < 240, $"{x},{y} is off the screen");
    }

    [Fact]
    public void Weather_settings_survive_saving()
    {
        var theme = Theme.Default with { Elements = [Widget with { ShowForecast = false }] };
        var loaded = Theme.FromJson(theme.ToJson()).Elements[0];
        Assert.Equal(ElementKind.Weather, loaded.Kind);
        Assert.False(loaded.ShowForecast);
        Assert.True(loaded.ShowDetails);
    }
}
