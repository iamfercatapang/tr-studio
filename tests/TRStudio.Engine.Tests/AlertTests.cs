using System.Drawing;
using TRStudio.Engine;
using Xunit;

public sealed class AlertTests
{
    static readonly DateTime Start = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    static Stats With(double? pump = 3000, double? cpu = 50, double? gpu = 50) =>
        Stats.From(new RawStats(PumpRpm: pump, CpuTemp: cpu, GpuTemp: gpu));

    static IReadOnlyList<Alert> After(AlertMonitor monitor, Stats stats, TimeSpan time, AlertSettings? settings = null)
    {
        IReadOnlyList<Alert> alerts = [];
        for (var t = TimeSpan.Zero; t <= time; t += TimeSpan.FromSeconds(1))
            alerts = monitor.Check(stats, settings ?? new AlertSettings(), Start + t);
        return alerts;
    }

    [Fact]
    public void Stopped_pump_warns_only_after_it_stays_stopped()
    {
        var monitor = new AlertMonitor();

        Assert.Empty(After(monitor, With(pump: 0), AlertMonitor.PumpHold - TimeSpan.FromSeconds(1)));
        var alert = Assert.Single(monitor.Check(With(pump: 0), new AlertSettings(), Start + AlertMonitor.PumpHold));
        Assert.Equal(AlertKind.Pump, alert.Kind);
        Assert.Equal("PUMP STOPPED", alert.Title);
        Assert.Equal("0 RPM", alert.Detail);
    }

    [Fact]
    public void Brief_zero_after_waking_raises_nothing()
    {
        var monitor = new AlertMonitor();
        After(monitor, With(pump: 0), TimeSpan.FromSeconds(3));

        Assert.Empty(monitor.Check(With(pump: 3200), new AlertSettings(), Start + TimeSpan.FromSeconds(4)));
        Assert.Empty(monitor.Check(With(pump: 0), new AlertSettings(), Start + TimeSpan.FromSeconds(12)));
    }

    [Fact]
    public void Unreadable_pump_raises_nothing()
    {
        Assert.Empty(After(new AlertMonitor(), With(pump: null), TimeSpan.FromSeconds(30)));
        Assert.Empty(After(new AlertMonitor(), With(pump: 0).AsStale(), TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void Temperature_warning_uses_the_chosen_limit_and_clears_below_it()
    {
        var monitor = new AlertMonitor();
        var settings = new AlertSettings(CpuMaxTemp: 80);

        var alert = Assert.Single(After(monitor, With(cpu: 82), AlertMonitor.TempHold, settings));
        Assert.Equal("CPU HOT", alert.Title);
        Assert.Equal("82°C", alert.Detail);
        Assert.Single(monitor.Check(With(cpu: 79), settings, Start + TimeSpan.FromMinutes(1))); // within the margin
        Assert.Empty(monitor.Check(With(cpu: 76), settings, Start + TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void Turned_off_warning_never_shows()
    {
        var settings = new AlertSettings(Pump: false, GpuTemp: false);

        Assert.Empty(After(new AlertMonitor(), With(pump: 0, gpu: 105), TimeSpan.FromSeconds(30), settings));
    }

    [Fact]
    public void Several_warnings_come_in_order()
    {
        var alerts = After(new AlertMonitor(), With(pump: 0, cpu: 99, gpu: 99), TimeSpan.FromSeconds(30));

        Assert.Equal([AlertKind.Pump, AlertKind.CpuTemp, AlertKind.GpuTemp], alerts.Select(alert => alert.Kind));
    }

    [Fact]
    public void Limits_stay_in_range()
    {
        var clamped = new AlertSettings(PumpMinRpm: 0, CpuMaxTemp: 500, GpuMaxTemp: -5).Clamped();

        Assert.Equal(AlertSettings.MinPumpRpm, clamped.PumpMinRpm);
        Assert.Equal(AlertSettings.MaxTemp, clamped.CpuMaxTemp);
        Assert.Equal(AlertSettings.MinTemp, clamped.GpuMaxTemp);
    }

    [Fact]
    public void Warning_covers_the_theme_with_a_red_ring()
    {
        using var background = Backgrounds.Create(new BackgroundSpec(BackgroundKind.Color, "#FFFFFF"));
        using var renderer = new ThemeRenderer();
        var alerts = new[] { new Alert(AlertKind.Pump, "PUMP STOPPED", "0 RPM") };
        using var bitmap = new Bitmap(new MemoryStream(renderer.Render(background, [], Stats.Empty, DateTime.Now, alerts: alerts)));

        var middle = bitmap.GetPixel(100, 240);
        Assert.True(middle.R < 90, middle.ToString()); // the white theme is dimmed
        var ring = bitmap.GetPixel(14, 240); // the middle of the ring
        Assert.True(ring.R > 150 && ring.G < 120, ring.ToString());
        Assert.True(renderer.IsAnimating);
    }
}
