using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace CoreVision.Engine;

/// <summary>Where each part of a Weather widget sits, on the 480 unit square. The editor mirrors these numbers.</summary>
public sealed record WeatherLayout(float Top, float IconSide, float TempSize, float ConditionY, float ConditionSize,
    float DetailY, float DetailSize, float ForecastY, float ForecastTextSize, float ForecastIcon, RectangleF Bounds);

public sealed partial class ThemeRenderer
{
    const float MinWeatherWidth = 160f;
    const float WeatherIconShare = 0.26f;
    const float WeatherTempShare = 0.2f;
    const float WeatherMainGapShare = 0.03f;
    const float ConditionGapShare = 0.03f;
    const float ConditionShare = 0.062f;
    const float DetailGapShare = 0.02f;
    const float DetailShare = 0.052f;
    const float ForecastGapShare = 0.05f;
    const float ForecastTextShare = 0.05f;
    const float ForecastIconShare = 0.12f;
    const float ForecastIconGapShare = 0.01f;
    const float ForecastColumnShare = 0.3f;

    public static WeatherLayout LayoutWeather(ElementSpec element)
    {
        var width = Math.Max(element.Size, MinWeatherWidth);
        var iconSide = width * WeatherIconShare;
        var y = iconSide;

        var conditionSize = width * ConditionShare;
        if (element.ShowCondition) y += width * ConditionGapShare;
        var conditionY = y;
        if (element.ShowCondition) y += conditionSize * LineSpacing;

        var detailSize = width * DetailShare;
        if (element.ShowDetails) y += width * DetailGapShare;
        var detailY = y;
        if (element.ShowDetails) y += detailSize * LineSpacing * 2;

        var textSize = width * ForecastTextShare;
        var forecastIcon = width * ForecastIconShare;
        if (element.ShowForecast) y += width * ForecastGapShare;
        var forecastY = y;
        if (element.ShowForecast) y += textSize * LineSpacing * 2 + forecastIcon + width * ForecastIconGapShare;

        var top = element.Y - y / 2;
        return new WeatherLayout(top, iconSide, width * WeatherTempShare, conditionY + top, conditionSize, detailY + top, detailSize,
            forecastY + top, textSize, forecastIcon, new RectangleF(element.X - width / 2, top, width, y));
    }

    static string Degrees(double value) => Math.Round(value).ToString("0", CultureInfo.InvariantCulture) + "°";

    /// <summary>Without a fresh forecast, every value shows a dash, as an unreadable sensor does.</summary>
    void DrawWeather(Graphics g, ElementSpec element, WeatherReport? weather)
    {
        var layout = LayoutWeather(element);
        var width = layout.Bounds.Width;
        var report = weather is { } fresh && !fresh.IsStale(DateTime.UtcNow) ? fresh : null;
        var (family, style) = FontFor(element);
        var (plainFamily, plainStyle) = FontFor(element with { Bold = false });
        var primary = ParseColor(element.Color, Color.White);
        var secondary = ParseColor(element.SecondaryColor, Color.Gray);

        DrawWeatherMain(g, element, layout, report, family, style, primary);

        if (element.ShowCondition)
        {
            var condition = report == null ? NoValue : $"{WeatherReport.Describe(report.Code)} · {report.Place}";
            DrawFitted(g, condition, plainFamily, plainStyle, layout.ConditionSize, element.X, layout.ConditionY, width, secondary);
        }

        if (element.ShowDetails)
        {
            var range = report == null ? $"H {NoValue}   L {NoValue}" : $"H {Degrees(report.High)}   L {Degrees(report.Low)}";
            var air = report == null ? $"Humidity {NoValue}   Wind {NoValue}"
                : $"Humidity {report.Humidity:0}%   Wind {report.Wind:0} {report.WindUnit}";
            DrawFitted(g, range, plainFamily, plainStyle, layout.DetailSize, element.X, layout.DetailY, width, secondary);
            DrawFitted(g, air, plainFamily, plainStyle, layout.DetailSize, element.X, layout.DetailY + layout.DetailSize * LineSpacing, width, secondary);
        }

        if (element.ShowForecast && report != null)
        {
            for (var i = 0; i < report.Days.Count; i++)
            {
                var day = report.Days[i];
                var x = element.X + (i - (report.Days.Count - 1) / 2f) * width * ForecastColumnShare;
                var y = layout.ForecastY;
                DrawLine(g, day.Date.ToString("ddd", CultureInfo.InvariantCulture), plainFamily, plainStyle, layout.ForecastTextSize, x, y, StringAlignment.Center, secondary);
                y += layout.ForecastTextSize * LineSpacing;
                WeatherIcons.Draw(g, WeatherReport.SkyOf(day.Code), true, new PointF(x, y + layout.ForecastIcon / 2), layout.ForecastIcon);
                y += layout.ForecastIcon + width * ForecastIconGapShare;
                DrawLine(g, $"{Degrees(day.High)} / {Degrees(day.Low)}", plainFamily, plainStyle, layout.ForecastTextSize, x, y, StringAlignment.Center, primary);
            }
        }
    }

    /// <summary>The icon and temperature sit side by side and center as one unit.</summary>
    static void DrawWeatherMain(Graphics g, ElementSpec element, WeatherLayout layout, WeatherReport? report, FontFamily family, FontStyle style, Color color)
    {
        using var temperature = new GraphicsPath();
        temperature.AddString(report == null ? NoValue + "°" : Degrees(report.Temperature), family, (int)style, layout.TempSize, PointF.Empty, StringFormat.GenericDefault);
        var bounds = temperature.GetBounds();

        var gap = report == null ? 0 : layout.Bounds.Width * WeatherMainGapShare;
        var icon = report == null ? 0 : layout.IconSide;
        var left = element.X - (icon + gap + bounds.Width) / 2;
        var middle = layout.Top + layout.IconSide / 2;
        if (report != null) WeatherIcons.Draw(g, WeatherReport.SkyOf(report.Code), report.IsDay, new PointF(left + icon / 2, middle), icon);

        using var place = new Matrix();
        place.Translate(left + icon + gap - bounds.Left, middle - bounds.Top - bounds.Height / 2);
        temperature.Transform(place);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, temperature);
    }
}
