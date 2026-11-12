using TRStudio.Engine;
using Xunit;

public class SensorMonitorTests
{
    sealed class FakeSource(Func<RawStats> read) : ISensorSource
    {
        public volatile bool Disposed;
        public RawStats Read() => read();
        public void Dispose() => Disposed = true;
    }

    static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            Thread.Sleep(5);
        }
    }

    [Fact]
    public void Zero_cpu_temperature_without_admin_rights_is_not_shown_as_real()
    {
        var stats = Stats.From(new RawStats(CpuTemp: 0, CpuPower: 0, GpuTemp: 59));
        Assert.Equal(ReadingState.Invalid, stats.CpuTemp.State);
        Assert.Null(stats.CpuTemp.Value);
        Assert.Equal(ReadingState.Invalid, stats.CpuPower.State);
        Assert.Equal(59, stats.GpuTemp.Value);
    }

    [Theory]
    [InlineData(null, ReadingState.Missing)]
    [InlineData(double.NaN, ReadingState.Invalid)]
    [InlineData(200.0, ReadingState.Invalid)]
    [InlineData(-5.0, ReadingState.Invalid)]
    [InlineData(65.0, ReadingState.Ok)]
    public void Checks_temperature_ranges(double? value, ReadingState expected)
    {
        Assert.Equal(expected, Stats.From(new RawStats(CpuTemp: value)).CpuTemp.State);
    }

    [Fact]
    public void Idle_gpu_at_zero_load_and_power_is_valid()
    {
        var stats = Stats.From(new RawStats(GpuLoad: 0, GpuPower: 0));
        Assert.Equal(ReadingState.Ok, stats.GpuLoad.State);
        Assert.Equal(ReadingState.Ok, stats.GpuPower.State);
    }

    [Fact]
    public void Publishes_readings()
    {
        using var monitor = new SensorMonitor(() => new FakeSource(() => new RawStats(GpuTemp: 60)), TimeSpan.FromMilliseconds(5));
        monitor.Start();
        WaitFor(() => monitor.Current.GpuTemp.Value == 60);
    }

    [Fact]
    public void Readings_turn_stale_when_polling_stops()
    {
        object clock = DateTime.UtcNow;
        using var hang = new ManualResetEventSlim(false);
        var reads = 0;
        var monitor = new SensorMonitor(
            () => new FakeSource(() =>
            {
                if (Interlocked.Increment(ref reads) > 1) hang.Wait();
                return new RawStats(GpuTemp: 60);
            }),
            TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(5), () => (DateTime)Volatile.Read(ref clock));
        monitor.Start();
        WaitFor(() => monitor.Current.GpuTemp.Value == 60);

        Volatile.Write(ref clock, DateTime.UtcNow.AddSeconds(6));

        Assert.Equal(ReadingState.Stale, monitor.Current.GpuTemp.State);
        Assert.Null(monitor.Current.GpuTemp.Value);
        hang.Set();
        monitor.Dispose();
    }

    [Fact]
    public void Reopens_the_source_after_a_failure()
    {
        var opens = 0;
        var first = true;
        FakeSource? failed = null;
        using var monitor = new SensorMonitor(() =>
        {
            Interlocked.Increment(ref opens);
            if (!first) return new FakeSource(() => new RawStats(GpuTemp: 70));
            first = false;
            return failed = new FakeSource(() => throw new InvalidOperationException("Sensor driver failed"));
        }, TimeSpan.FromMilliseconds(5));
        monitor.Start();
        WaitFor(() => monitor.Current.GpuTemp.Value == 70);
        Assert.True(failed!.Disposed);
        Assert.True(opens >= 2);
    }
}
