using CoreVision.Engine;
using Xunit;

public class ScreenLinkTests
{
    sealed class FakeScreen : IScreenTransport
    {
        public readonly List<int> Writes = [];
        public volatile bool Fail;
        public volatile bool Disposed;

        public void Write(byte[] data, int length)
        {
            if (Fail) throw new IOException("Device vanished");
            lock (Writes) Writes.Add(length);
        }

        public int Read(byte[] buffer)
        {
            ScreenProtocolTests.RealReply.CopyTo(buffer, 0);
            return ScreenProtocol.HeaderSize;
        }

        public int Frames { get { lock (Writes) return Writes.Count(w => w > ScreenProtocol.HeaderSize); } }
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
    public void Connects_and_sends_a_frame()
    {
        var screen = new FakeScreen();
        using var link = new ScreenLink(() => screen);
        link.Start();
        link.Submit(new byte[100]);
        WaitFor(() => screen.Frames >= 1);
        Assert.Equal(LinkState.Connected, link.Status.State);
        Assert.Equal(480, link.Status.Screen!.Width);
    }

    [Fact]
    public void Reconnects_after_a_usb_failure_and_resends_the_image()
    {
        var first = new FakeScreen();
        var second = new FakeScreen();
        var opens = 0;
        using var link = new ScreenLink(() => Interlocked.Increment(ref opens) == 1 ? first : second);
        link.Start();
        link.Submit(new byte[100]);
        WaitFor(() => first.Frames >= 1);

        first.Fail = true;
        link.Submit(new byte[100]);

        WaitFor(() => second.Frames >= 1);
        Assert.True(first.Disposed);
    }

    [Fact]
    public void Waits_while_another_app_holds_the_screen()
    {
        var busy = true;
        var screen = new FakeScreen();
        using var link = new ScreenLink(() => Volatile.Read(ref busy) ? throw new ScreenBusyException("TRCC") : screen);
        link.Start();
        WaitFor(() => link.Status.State == LinkState.Busy);

        Volatile.Write(ref busy, false);

        WaitFor(() => link.Status.State == LinkState.Connected);
    }

    [Fact]
    public void Keeps_searching_when_the_screen_is_missing()
    {
        var attempts = 0;
        using var link = new ScreenLink(() =>
        {
            Interlocked.Increment(ref attempts);
            throw new ScreenNotFoundException("Unplugged");
        });
        link.Start();
        WaitFor(() => Volatile.Read(ref attempts) >= 3);
        Assert.Equal(LinkState.Searching, link.Status.State);
    }

    [Fact]
    public void Closes_the_device_during_sleep_and_reopens_on_wake()
    {
        var screens = new List<FakeScreen>();
        using var link = new ScreenLink(() =>
        {
            var screen = new FakeScreen();
            lock (screens) screens.Add(screen);
            return screen;
        });
        link.Start();
        link.Submit(new byte[100]);
        WaitFor(() => link.Status.State == LinkState.Connected);

        link.Suspend();
        WaitFor(() => link.Status.State == LinkState.Suspended);
        lock (screens) Assert.True(screens[0].Disposed);

        link.Resume();
        WaitFor(() => { lock (screens) return screens.Count == 2 && screens[1].Frames >= 1; });
    }

    [Fact]
    public void Newest_frame_wins_when_frames_arrive_faster_than_usb()
    {
        var screen = new FakeScreen();
        using var link = new ScreenLink(() => screen);
        for (var i = 1; i <= 50; i++) link.Submit(new byte[i * 10]);
        link.Start();
        WaitFor(() => screen.Frames >= 1);
        lock (screen.Writes) Assert.Equal(ScreenProtocol.HeaderSize + 500, screen.Writes.First(w => w > ScreenProtocol.HeaderSize));
    }

    [Fact]
    public void Keeps_the_image_alive_without_new_frames()
    {
        var screen = new FakeScreen();
        using var link = new ScreenLink(() => screen, keepAlive: TimeSpan.FromMilliseconds(10));
        link.Start();
        link.Submit(new byte[100]);
        WaitFor(() => screen.Frames >= 5);
    }

    [Fact]
    public void Dispose_stops_the_thread_promptly()
    {
        var screen = new FakeScreen();
        var link = new ScreenLink(() => screen);
        link.Start();
        WaitFor(() => link.Status.State == LinkState.Connected);
        link.Dispose();
        Assert.True(screen.Disposed);
    }
}
