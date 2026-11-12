using System.Drawing;
using TRStudio.Engine;
using Xunit;

public class ScreenSizeTests
{
    [Theory]
    [InlineData(3, 0, 480, 480, true)]
    [InlineData(63, 0, 1600, 720, false)]
    [InlineData(1, 48, 1600, 720, false)]
    [InlineData(65, 0, 1920, 462, false)]
    [InlineData(1, 49, 1920, 462, false)]
    [InlineData(69, 0, 1920, 440, false)]
    [InlineData(68, 3, 1280, 480, false)]
    [InlineData(9, 0, 854, 480, false)]
    [InlineData(10, 0, 960, 540, false)]
    [InlineData(12, 0, 800, 480, false)]
    [InlineData(13, 0, 960, 320, false)]
    [InlineData(15, 0, 640, 172, false)]
    [InlineData(4, 0, 480, 480, false)]
    public void Maps_each_mode_to_its_panel(int mode, int subMode, int width, int height, bool round)
    {
        var screen = ScreenProtocol.Identify("SSCRM-V1", mode, subMode);
        Assert.Equal((width, height, round), (screen.Width, screen.Height, screen.Round));
    }

    [Theory]
    [InlineData(69, 0)]
    [InlineData(68, 3)]
    public void Copies_trccs_wide_header_quirk(int mode, int subMode)
    {
        var screen = ScreenProtocol.Identify("SSCRM-V1", mode, subMode);
        Assert.Equal((1920, 462), (screen.HeaderWidth, screen.HeaderHeight));
    }

    [Fact]
    public void Header_matches_the_panel_for_ordinary_sizes()
    {
        var screen = ScreenProtocol.Identify("SSCRM-V1", 9, 0);
        Assert.Equal((854, 480), (screen.HeaderWidth, screen.HeaderHeight));
    }

    [Theory]
    [InlineData(5, 50)]
    [InlineData(7, 64)]
    [InlineData(14, 64)]
    [InlineData(3, 95)]
    public void Uses_trccs_jpeg_quality(int mode, int quality)
    {
        Assert.Equal(quality, ScreenProtocol.Identify("SSCRM-V1", mode, 0).JpegQuality);
    }

    [Theory]
    [InlineData(50, 0)]
    [InlineData(68, 2)]
    public void Refuses_formats_it_cannot_send(int mode, int subMode)
    {
        Assert.Throws<ScreenUnsupportedException>(() => ScreenProtocol.Identify("SSCRM-V1", mode, subMode));
    }

    [Theory]
    [InlineData(1920, 462)]
    [InlineData(640, 172)]
    [InlineData(854, 480)]
    public void Renders_a_frame_at_the_exact_panel_size(int width, int height)
    {
        using var renderer = new ThemeRenderer(width, height);
        using var background = Backgrounds.Create(new BackgroundSpec(Color: "#FF0000"), width: width, height: height);
        using var bitmap = new Bitmap(new MemoryStream(renderer.Render(background, Theme.Default.Elements, Stats.Empty, DateTime.Now)));
        Assert.Equal((width, height), (bitmap.Width, bitmap.Height));
        Assert.True(bitmap.GetPixel(5, 5).R > 180); // the background fills the whole panel
    }

    [Fact]
    public void Square_theme_centers_on_a_wide_screen()
    {
        var box = new ElementSpec(ElementKind.Text, 240, 200, 60) { Text = "HELLO", Align = TextAlign.Center, Color = "#FF0000" };
        using var renderer = new ThemeRenderer(1600, 720);
        using var background = Backgrounds.Create(new BackgroundSpec(), width: 1600, height: 720);
        using var bitmap = new Bitmap(new MemoryStream(renderer.Render(background, [box], Stats.Empty, DateTime.Now)));
        var red = Enumerable.Range(0, 800).Select(i => i * 2).Where(x => Enumerable.Range(0, 360).Any(y => bitmap.GetPixel(x, y * 2).R > 180)).ToList();
        Assert.InRange((red[0] + red[^1]) / 2, 780, 820);
    }

    [Fact]
    public void Rotating_a_wide_screen_sideways_keeps_the_panel_size()
    {
        using var renderer = new ThemeRenderer(960, 320);
        using var background = Backgrounds.Create(new BackgroundSpec(), width: 960, height: 320);
        renderer.Render(background, [], Stats.Empty, DateTime.Now);
        using var turned = new Bitmap(new MemoryStream(renderer.EncodeRotated(90)));
        using var flipped = new Bitmap(new MemoryStream(renderer.EncodeRotated(180)));
        Assert.Equal((960, 320), (turned.Width, turned.Height));
        Assert.Equal((960, 320), (flipped.Width, flipped.Height));
    }

    [Theory]
    [InlineData(1920, 1080, 960, 320, 0, 220, 1920, 640)]
    [InlineData(1000, 1000, 1600, 720, 0, 275, 1000, 450)]
    public void Cover_crop_keeps_the_target_shape(int width, int height, int targetWidth, int targetHeight, int x, int y, int cropWidth, int cropHeight)
    {
        Assert.Equal(new Rectangle(x, y, cropWidth, cropHeight), Backgrounds.CoverSource(width, height, targetWidth, targetHeight));
    }

    [Fact]
    public void Video_decoder_outputs_the_panel_size()
    {
        var arguments = string.Join(' ', VideoArguments.For("a.mp4", 960, 320));
        Assert.Contains("scale=960:320", arguments);
        Assert.Contains("crop=960:320", arguments);
    }

    [Fact]
    public void Player_redraws_at_a_new_screen_size()
    {
        byte[]? last = null;
        var opened = 0;
        using var player = new ThemePlayer(Theme.Default with { Elements = [] }, () => Stats.Empty, frame => Volatile.Write(ref last, frame), spec =>
        {
            Interlocked.Increment(ref opened);
            return Backgrounds.Create(spec);
        });
        player.Start();
        player.Screen = ScreenProtocol.Identify("SSCRM-V1", 15, 0);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var jpeg = Volatile.Read(ref last);
            if (jpeg != null)
            {
                using var bitmap = new Bitmap(new MemoryStream(jpeg));
                if (bitmap.Width == 640) break;
            }
            Assert.True(DateTime.UtcNow < deadline);
            Thread.Sleep(5);
        }
    }

    sealed class NoScreen : IScreenTransport
    {
        public void Write(byte[] data, int length) { }
        public int Read(byte[] buffer) => 0;
        public void Dispose() { }
    }

    static IScreenTransport Missing() => throw new ScreenNotFoundException("Screen is not connected");

    [Fact]
    public void Names_a_display_it_cannot_drive_without_opening_it()
    {
        string[] usb = [@"\\?\usb#vid_046d&pid_c52b#x#{a5dcbf10}", @"\\?\usb#vid_0416&pid_5409#abc#{a5dcbf10}"];
        var error = Assert.Throws<ScreenUnsupportedException>(() => ScreenFinder.Open(Missing, () => usb));
        Assert.Contains("0416:5409", error.Message);
    }

    [Fact]
    public void Stays_searching_when_nothing_is_recognized()
    {
        Assert.Throws<ScreenNotFoundException>(() => ScreenFinder.Open(Missing, () => [@"\\?\usb#vid_046d&pid_c52b#x"]));
    }

    [Fact]
    public void Prefers_the_supported_screen_over_other_displays()
    {
        var screen = new NoScreen();
        Assert.Same(screen, ScreenFinder.Open(() => screen, () => throw new InvalidOperationException()));
    }

    [Theory]
    [InlineData(@"\\?\usb#vid_87ad&pid_70db#454865#{a5dcbf10}", 0x87AD, 0x70DB)]
    [InlineData(@"\\?\USB#VID_0416&PID_5302#1", 0x0416, 0x5302)]
    public void Reads_usb_ids(string path, int vid, int pid)
    {
        Assert.Equal(((ushort)vid, (ushort)pid), ScreenFinder.ParseIds(path));
    }

    [Fact]
    public void Startup_task_starts_hidden_with_admin_rights()
    {
        var command = string.Join(' ', StartupCommands.Create(@"C:\Program Files\App\TRStudio.exe"));
        Assert.Contains("/RL HIGHEST", command);
        Assert.Contains("/SC ONLOGON", command);
        Assert.Contains("\"C:\\Program Files\\App\\TRStudio.exe\" --background", command);
    }
}
