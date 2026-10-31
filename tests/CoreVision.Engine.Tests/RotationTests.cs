using System.Drawing;
using CoreVision.Engine;
using Xunit;

public class RotationTests
{
    // Red square in the top-left corner of an otherwise black frame.
    sealed class MarkerBackground : IBackground
    {
        public TimeSpan FrameDelay => TimeSpan.FromMilliseconds(5);
        public void Advance() { }
        public void Dispose() { }

        public void Draw(Graphics g)
        {
            g.Clear(Color.Black);
            g.FillRectangle(Brushes.Red, 0, 0, 120, 120);
        }
    }

    static bool IsRed(byte[] jpeg, int x, int y)
    {
        using var stream = new MemoryStream(jpeg);
        using var bitmap = new Bitmap(stream);
        var color = bitmap.GetPixel(x, y);
        return color.R > 180 && color.G < 80;
    }

    [Theory]
    [InlineData(0, 40, 40)]
    [InlineData(90, 440, 40)]
    [InlineData(180, 440, 440)]
    [InlineData(270, 40, 440)]
    public void Turns_the_frame_clockwise(int degrees, int x, int y)
    {
        using var renderer = new ThemeRenderer();
        using var background = new MarkerBackground();
        renderer.Render(background, [], Stats.Empty, DateTime.Now);
        Assert.True(IsRed(renderer.EncodeRotated(degrees), x, y));
    }

    [Fact]
    public void Screen_is_rotated_but_the_preview_stays_upright()
    {
        byte[]? sent = null, preview = null;
        using var player = new ThemePlayer(Theme.Default with { Elements = [] }, () => Stats.Empty,
            frame => Volatile.Write(ref sent, frame), _ => new MarkerBackground()) { Rotation = 90 };
        player.FrameRendered += frame => Volatile.Write(ref preview, frame);
        player.Start();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref sent) == null || Volatile.Read(ref preview) == null)
        {
            Assert.True(DateTime.UtcNow < deadline);
            Thread.Sleep(5);
        }

        Assert.True(IsRed(Volatile.Read(ref sent)!, 440, 40));
        Assert.True(IsRed(Volatile.Read(ref preview)!, 40, 40));
    }

    [Theory]
    [InlineData(45, 0)]
    [InlineData(-90, 0)]
    [InlineData(270, 270)]
    public void Only_right_angles_are_accepted(int requested, int actual)
    {
        using var player = new ThemePlayer(Theme.Default, () => Stats.Empty, _ => { }, spec => Backgrounds.Create(spec)) { Rotation = requested };
        Assert.Equal(actual, player.Rotation);
    }
}
