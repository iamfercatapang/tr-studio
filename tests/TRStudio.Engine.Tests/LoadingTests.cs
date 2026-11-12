using System.Drawing;
using TRStudio.Engine;
using Xunit;

public class LoadingTests
{
    sealed class FakeSource(Func<RawStats> read) : ISensorSource
    {
        public RawStats Read() => read();
        public void Dispose() { }
    }

    sealed class Black : IBackground
    {
        public TimeSpan FrameDelay => TimeSpan.FromSeconds(1);
        public void Advance() { }
        public void Dispose() { }
        public void Draw(Graphics g) => g.Clear(Color.Black);
    }

    sealed class Clock
    {
        object _now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        public DateTime Now => (DateTime)Volatile.Read(ref _now);
        public void Add(TimeSpan time) => Volatile.Write(ref _now, Now + time);
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

    static SensorMonitor Monitor(Clock clock, Func<RawStats> read) =>
        new(() => new FakeSource(read), TimeSpan.FromMilliseconds(5), TimeSpan.FromHours(1), () => clock.Now);

    [Fact]
    public void Loads_until_the_first_readings_arrive()
    {
        var clock = new Clock();
        using var ready = new ManualResetEventSlim(false);
        using var monitor = Monitor(clock, () => { ready.Wait(); return new RawStats(CpuTemp: 50); });
        monitor.Start();
        Assert.True(monitor.IsLoading);
        ready.Set();
        WaitFor(() => monitor.Current.CpuTemp.Value == 50);
        Assert.True(monitor.IsLoading); // more readings may still come
        clock.Add(SensorMonitor.LoadingSettle);
        Assert.False(monitor.IsLoading);
    }

    [Fact]
    public void Stops_loading_at_the_limit_when_nothing_reads()
    {
        var clock = new Clock();
        using var monitor = Monitor(clock, () => new RawStats());
        monitor.Start();
        Thread.Sleep(50); // a few empty samples
        clock.Add(SensorMonitor.LoadingSettle * 2);
        Assert.True(monitor.IsLoading);
        clock.Add(SensorMonitor.LoadingLimit);
        Assert.False(monitor.IsLoading);
    }

    [Fact]
    public void Once_loaded_it_stays_loaded()
    {
        var clock = new Clock();
        var raw = new RawStats(CpuTemp: 50);
        using var monitor = Monitor(clock, () => Volatile.Read(ref raw));
        monitor.Start();
        WaitFor(() => monitor.Current.CpuTemp.Value == 50);
        clock.Add(SensorMonitor.LoadingSettle);
        Assert.False(monitor.IsLoading);
        Volatile.Write(ref raw, new RawStats(CpuTemp: 50, GpuTemp: 40));
        WaitFor(() => monitor.Current.GpuTemp.Value == 40);
        Assert.False(monitor.IsLoading);
    }

    [Fact]
    public void Only_themes_with_sensor_readings_wait()
    {
        Assert.True(ThemeRenderer.UsesSensors(Theme.Default.Elements));
        Assert.False(ThemeRenderer.UsesSensors([new ElementSpec(ElementKind.Clock, 240, 240, 40)]));
        Assert.False(ThemeRenderer.UsesSensors([new ElementSpec(ElementKind.Gauge, 240, 240, 400) { Metric = Metric.MusicProgress }]));
        Assert.False(ThemeRenderer.UsesSensors([new ElementSpec(ElementKind.Metric, 240, 240, 40) { Metric = Metric.OutdoorTemp }]));
        Assert.False(ThemeRenderer.UsesSensors([new ElementSpec(ElementKind.Metric, 240, 240, 40) { Hidden = true }]));
    }

    [Fact]
    public void The_spinner_covers_the_theme_and_keeps_frames_coming()
    {
        using var renderer = new ThemeRenderer();
        using var background = new Black();
        var plain = renderer.Render(background, Theme.Default.Elements, Stats.Empty, DateTime.Now);
        Assert.False(renderer.IsAnimating);
        var loading = renderer.Render(background, Theme.Default.Elements, Stats.Empty, DateTime.Now, loading: true);
        Assert.True(renderer.IsAnimating);
        Assert.NotEqual(plain, loading);

        var clockOnly = new[] { new ElementSpec(ElementKind.Clock, 240, 240, 40) };
        renderer.Render(background, clockOnly, Stats.Empty, DateTime.Now, loading: true);
        Assert.False(renderer.IsAnimating);
    }
}
