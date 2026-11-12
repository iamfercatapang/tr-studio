using TRStudio.Engine;

// Usage: WeatherProbe <city> <output.jpg> [imperial]
// Finds the city on Open-Meteo, fetches its forecast the way TR Studio does, and draws the Weather theme.
var units = args.Contains("imperial") ? WeatherUnits.Imperial : WeatherUnits.Metric;
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("TRStudio/1.2");

var places = WeatherReport.ParsePlaces(await http.GetStringAsync(WeatherReport.SearchUrl(args[0])));
foreach (var found in places) Console.WriteLine($"found: {found.Label} ({found.Latitude}, {found.Longitude})");
var place = places.FirstOrDefault() ?? throw new InvalidOperationException("No place found.");

var report = WeatherReport.Parse(await http.GetStringAsync(WeatherReport.ForecastUrl(place, units)), place.Name, units, DateTime.UtcNow);
Console.WriteLine($"{report.Temperature}° {WeatherReport.Describe(report.Code)}, H {report.High} L {report.Low}, " +
    $"humidity {report.Humidity}%, wind {report.Wind} {report.WindUnit}, day={report.IsDay}");
foreach (var day in report.Days) Console.WriteLine($"  {day.Date:ddd}: {WeatherReport.Describe(day.Code)} {day.High}/{day.Low}");

var theme = Theme.BuiltIns.Single(t => t.Name == "Weather");
using var background = Backgrounds.Create(theme.Background);
using var renderer = new ThemeRenderer();
File.WriteAllBytes(args[1], renderer.Render(background, theme.Elements, Stats.Empty, DateTime.Now, weather: report));

// Every icon once, day and night, to check them by eye.
var icons = new ThemeRenderer();
var grid = new List<ElementSpec>();
File.WriteAllBytes(Path.ChangeExtension(args[1], ".icons.jpg"), RenderIcons());
icons.Dispose();

static byte[] RenderIcons()
{
    using var bitmap = new System.Drawing.Bitmap(480, 240);
    using (var g = System.Drawing.Graphics.FromImage(bitmap))
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Black);
        var skies = Enum.GetValues<WeatherSky>();
        for (var i = 0; i < skies.Length; i++)
        {
            WeatherIcons.Draw(g, skies[i], true, new System.Drawing.PointF(30 + i * 60, 60), 52);
            WeatherIcons.Draw(g, skies[i], false, new System.Drawing.PointF(30 + i * 60, 170), 52);
        }
    }
    return JpegFrame.Encode(bitmap);
}
