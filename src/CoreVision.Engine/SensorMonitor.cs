namespace CoreVision.Engine;

public enum ReadingState { Ok, Missing, Invalid, Stale }

public readonly record struct Reading(double? Value, ReadingState State)
{
    /// <summary>
    /// Range checks catch sensors that report a false value, such as the 0.0 CPU temperature
    /// that LibreHardwareMonitor returns without admin rights.
    /// </summary>
    public static Reading Check(double? value, double min, double max)
    {
        if (value is not { } number) return new Reading(null, ReadingState.Missing);
        return double.IsFinite(number) && number >= min && number <= max
            ? new Reading(number, ReadingState.Ok)
            : new Reading(null, ReadingState.Invalid);
    }

    public Reading AsStale() => this with { Value = null, State = ReadingState.Stale };
}

/// <summary>Unchecked values from a sensor source. Null means the sensor does not exist.</summary>
public sealed record RawStats(
    double? CpuTemp = null, double? CpuLoad = null, double? CpuPower = null,
    double? GpuTemp = null, double? GpuLoad = null, double? GpuPower = null,
    double? MemoryLoad = null,
    double? CpuClock = null, double? GpuClock = null, double? GpuFan = null,
    double? GpuHotSpot = null, double? GpuMemoryTemp = null, double? MotherboardTemp = null,
    double? PumpRpm = null, double? CpuFanRpm = null, double? CaseFanRpm = null,
    double? MemoryUsed = null, double? MemoryAvailable = null, double? MemorySpeed = null,
    double? GpuMemoryUsed = null);

public sealed record Stats(
    Reading CpuTemp, Reading CpuLoad, Reading CpuPower,
    Reading GpuTemp, Reading GpuLoad, Reading GpuPower,
    Reading MemoryLoad,
    Reading CpuClock, Reading GpuClock, Reading GpuFan,
    Reading GpuHotSpot, Reading GpuMemoryTemp, Reading MotherboardTemp,
    Reading PumpRpm, Reading CpuFanRpm, Reading CaseFanRpm,
    Reading MemoryUsed, Reading MemoryAvailable, Reading MemorySpeed)
{
    const double MinTemp = 1, MaxTemp = 125, MaxLoad = 100, MinCpuPower = 0.5, MaxPower = 1000;
    const double MinClock = 1, MaxClock = 12000, MaxRpm = 20000, MaxMemoryGb = 8192, MaxMemorySpeed = 20000, MaxGpuMemoryGb = 256;

    /// <summary>Song progress from 0 to 100. It comes from the media app, not a sensor.</summary>
    public Reading MusicProgress { get; init; } = new(null, ReadingState.Missing);

    /// <summary>The temperature outside, from the weather forecast.</summary>
    public Reading OutdoorTemp { get; init; } = new(null, ReadingState.Missing);

    /// <summary>Video memory in use, in GB.</summary>
    public Reading GpuMemoryUsed { get; init; } = new(null, ReadingState.Missing);

    public Reading Get(Metric metric) => metric switch
    {
        Metric.CpuTemp => CpuTemp,
        Metric.CpuLoad => CpuLoad,
        Metric.CpuPower => CpuPower,
        Metric.GpuTemp => GpuTemp,
        Metric.GpuLoad => GpuLoad,
        Metric.GpuPower => GpuPower,
        Metric.MemoryLoad => MemoryLoad,
        Metric.CpuClock => CpuClock,
        Metric.GpuClock => GpuClock,
        Metric.GpuFan => GpuFan,
        Metric.GpuHotSpot => GpuHotSpot,
        Metric.GpuMemoryTemp => GpuMemoryTemp,
        Metric.MotherboardTemp => MotherboardTemp,
        Metric.PumpRpm => PumpRpm,
        Metric.CpuFanRpm => CpuFanRpm,
        Metric.CaseFanRpm => CaseFanRpm,
        Metric.MemoryUsed => MemoryUsed,
        Metric.MemoryAvailable => MemoryAvailable,
        Metric.MemorySpeed => MemorySpeed,
        Metric.MusicProgress => MusicProgress,
        Metric.OutdoorTemp => OutdoorTemp,
        Metric.GpuMemoryUsed => GpuMemoryUsed,
        _ => new Reading(null, ReadingState.Missing),
    };

    public static Stats From(RawStats raw) => new(
        Reading.Check(raw.CpuTemp, MinTemp, MaxTemp),
        Reading.Check(raw.CpuLoad, 0, MaxLoad),
        Reading.Check(raw.CpuPower, MinCpuPower, MaxPower),
        Reading.Check(raw.GpuTemp, MinTemp, MaxTemp),
        Reading.Check(raw.GpuLoad, 0, MaxLoad),
        Reading.Check(raw.GpuPower, 0, MaxPower),
        Reading.Check(raw.MemoryLoad, 0, MaxLoad),
        Reading.Check(raw.CpuClock, MinClock, MaxClock),
        Reading.Check(raw.GpuClock, MinClock, MaxClock),
        Reading.Check(raw.GpuFan, 0, MaxRpm),
        Reading.Check(raw.GpuHotSpot, MinTemp, MaxTemp),
        Reading.Check(raw.GpuMemoryTemp, MinTemp, MaxTemp),
        Reading.Check(raw.MotherboardTemp, MinTemp, MaxTemp),
        Reading.Check(raw.PumpRpm, 0, MaxRpm),
        Reading.Check(raw.CpuFanRpm, 0, MaxRpm),
        Reading.Check(raw.CaseFanRpm, 0, MaxRpm),
        Reading.Check(raw.MemoryUsed, 0, MaxMemoryGb),
        Reading.Check(raw.MemoryAvailable, 0, MaxMemoryGb),
        Reading.Check(raw.MemorySpeed, 1, MaxMemorySpeed))
    {
        GpuMemoryUsed = Reading.Check(raw.GpuMemoryUsed / 1024, 0, MaxGpuMemoryGb), // the sensor reports MB
    };

    public Stats AsStale() => new(
        CpuTemp.AsStale(), CpuLoad.AsStale(), CpuPower.AsStale(),
        GpuTemp.AsStale(), GpuLoad.AsStale(), GpuPower.AsStale(),
        MemoryLoad.AsStale(),
        CpuClock.AsStale(), GpuClock.AsStale(), GpuFan.AsStale(),
        GpuHotSpot.AsStale(), GpuMemoryTemp.AsStale(), MotherboardTemp.AsStale(),
        PumpRpm.AsStale(), CpuFanRpm.AsStale(), CaseFanRpm.AsStale(),
        MemoryUsed.AsStale(), MemoryAvailable.AsStale(), MemorySpeed.AsStale())
    {
        GpuMemoryUsed = GpuMemoryUsed.AsStale(),
    };

    public static Stats Empty { get; } = From(new RawStats());
}

public interface ISensorSource : IDisposable
{
    RawStats Read();
}

/// <summary>
/// Polls sensors on its own thread, so a slow or hung sensor can never delay the screen.
/// If polling stops, readings turn stale instead of showing old numbers as if they were live.
/// </summary>
public sealed class SensorMonitor : IDisposable
{
    readonly Func<ISensorSource> _open;
    readonly TimeSpan _interval;
    readonly TimeSpan _staleAfter;
    readonly Func<DateTime> _now;
    readonly Action<string> _log;
    readonly ManualResetEventSlim _stop = new(false);
    readonly Thread _thread;
    volatile Sample? _latest;
    string? _lastError;

    sealed record Sample(Stats Stats, DateTime Time);

    public SensorMonitor(Func<ISensorSource> open, TimeSpan? interval = null, TimeSpan? staleAfter = null,
        Func<DateTime>? now = null, Action<string>? log = null)
    {
        _open = open;
        _interval = interval ?? TimeSpan.FromSeconds(1);
        _staleAfter = staleAfter ?? TimeSpan.FromSeconds(5);
        _now = now ?? (() => DateTime.UtcNow);
        _log = log ?? (_ => { });
        _thread = new Thread(Run) { IsBackground = true, Name = "SensorMonitor" };
    }

    public void Start() => _thread.Start();

    public Stats Current
    {
        get
        {
            var sample = _latest;
            if (sample == null) return Stats.Empty;
            return _now() - sample.Time > _staleAfter ? sample.Stats.AsStale() : sample.Stats;
        }
    }

    public void Dispose()
    {
        _stop.Set();
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(5));
        _stop.Dispose();
    }

    void Run()
    {
        ISensorSource? source = null;
        try
        {
            while (!_stop.IsSet)
            {
                try
                {
                    source ??= _open();
                    _latest = new Sample(Stats.From(source.Read()), _now());
                    _lastError = null;
                }
                catch (Exception e)
                {
                    // Sensor libraries fail in many undocumented ways, and none may stop the screen.
                    if (e.Message != _lastError) _log($"Sensors failed: {e.Message}");
                    _lastError = e.Message;
                    source?.Dispose();
                    source = null;
                }
                _stop.Wait(_interval);
            }
        }
        finally { source?.Dispose(); }
    }
}
