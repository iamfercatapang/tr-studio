using System.Globalization;

namespace CoreVision.Engine;

public enum AlertKind { Pump, CpuTemp, GpuTemp }

/// <summary>Which warnings are on, and where each one starts.</summary>
public sealed record AlertSettings(
    bool Pump = true, int PumpMinRpm = AlertSettings.DefaultPumpRpm,
    bool CpuTemp = true, int CpuMaxTemp = AlertSettings.DefaultCpuTemp,
    bool GpuTemp = true, int GpuMaxTemp = AlertSettings.DefaultGpuTemp)
{
    public const int MinPumpRpm = 100, MaxPumpRpm = 3000, DefaultPumpRpm = 500;
    public const int MinTemp = 50, MaxTemp = 110, DefaultCpuTemp = 90, DefaultGpuTemp = 85;

    public AlertSettings Clamped() => this with
    {
        PumpMinRpm = Math.Clamp(PumpMinRpm, MinPumpRpm, MaxPumpRpm),
        CpuMaxTemp = Math.Clamp(CpuMaxTemp, MinTemp, MaxTemp),
        GpuMaxTemp = Math.Clamp(GpuMaxTemp, MinTemp, MaxTemp),
    };
}

public sealed record Alert(AlertKind Kind, string Title, string Detail);

/// <summary>
/// A warning starts only after its condition has held for a while, so a brief spike, or the zero a fan
/// header reads right after waking from sleep, never raises one. It ends once the reading is safely back.
/// A sensor that can't be read raises nothing, because a missing reading says nothing about the cooler.
/// </summary>
public sealed class AlertMonitor
{
    public static readonly TimeSpan PumpHold = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan TempHold = TimeSpan.FromSeconds(5);
    public const double TempClearMargin = 3;
    public const double PumpClearMargin = 100;

    readonly Dictionary<AlertKind, DateTime> _since = [];
    readonly HashSet<AlertKind> _active = [];

    public IReadOnlyList<Alert> Check(Stats stats, AlertSettings settings, DateTime now)
    {
        settings = settings.Clamped();
        var alerts = new List<Alert>();
        if (Update(AlertKind.Pump, settings.Pump, stats.PumpRpm, PumpHold, now,
            rpm => rpm < settings.PumpMinRpm, rpm => rpm >= settings.PumpMinRpm + PumpClearMargin) is { } pump)
        {
            alerts.Add(new Alert(AlertKind.Pump, pump < 1 ? "PUMP STOPPED" : "PUMP SLOW", $"{pump.ToString("N0", CultureInfo.InvariantCulture)} RPM"));
        }
        if (Update(AlertKind.CpuTemp, settings.CpuTemp, stats.CpuTemp, TempHold, now,
            temp => temp >= settings.CpuMaxTemp, temp => temp <= settings.CpuMaxTemp - TempClearMargin) is { } cpu)
        {
            alerts.Add(new Alert(AlertKind.CpuTemp, "CPU HOT", Degrees(cpu)));
        }
        if (Update(AlertKind.GpuTemp, settings.GpuTemp, stats.GpuTemp, TempHold, now,
            temp => temp >= settings.GpuMaxTemp, temp => temp <= settings.GpuMaxTemp - TempClearMargin) is { } gpu)
        {
            alerts.Add(new Alert(AlertKind.GpuTemp, "GPU HOT", Degrees(gpu)));
        }
        return alerts;
    }

    static string Degrees(double temp) => temp.ToString("F0", CultureInfo.InvariantCulture) + "°C";

    /// <summary>The reading while the warning is up, or null while it isn't.</summary>
    double? Update(AlertKind kind, bool enabled, Reading reading, TimeSpan hold, DateTime now, Func<double, bool> bad, Func<double, bool> safe)
    {
        if (!enabled || reading is not { State: ReadingState.Ok, Value: { } value })
        {
            _since.Remove(kind);
            _active.Remove(kind);
            return null;
        }
        if (_active.Contains(kind))
        {
            if (!safe(value)) return value;
            _active.Remove(kind);
            _since.Remove(kind);
            return null;
        }
        if (!bad(value))
        {
            _since.Remove(kind);
            return null;
        }
        if (!_since.TryGetValue(kind, out var since)) _since[kind] = since = now;
        if (now - since < hold) return null;
        _active.Add(kind);
        return value;
    }
}
