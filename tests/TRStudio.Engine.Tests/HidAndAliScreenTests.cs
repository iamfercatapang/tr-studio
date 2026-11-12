using System.Drawing;
using TRStudio.Engine;
using Xunit;

/// <summary>Checks the HID (0416:5302) and ALi (0416:5406) formats decoded from TRCC 2.1.4. Neither screen was available to test on.</summary>
public class HidAndAliScreenTests
{
    static byte[] HidReply(byte mode, byte message = 1)
    {
        var reply = new byte[513];
        new byte[] { 0xDA, 0xDB, 0xDC, 0xDD }.CopyTo(reply, 1);
        reply[6] = mode;
        reply[13] = message;
        return reply;
    }

    static byte[] Jpeg(int width, int height, Color color)
    {
        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap)) g.Clear(color);
        return JpegFrame.Encode(bitmap);
    }

    sealed class FakeLink(Func<byte[]?> reply) : IHidLink
    {
        public readonly List<byte[]> Reports = [];
        public void Write(ReadOnlySpan<byte> report) { lock (Reports) Reports.Add(report.ToArray()); }
        public byte[]? Read(int timeoutMs = 1000) => reply();
        public void Dispose() { }
    }

    [Fact]
    public void Hid_hello_is_TRCCs_20_bytes()
    {
        Assert.Equal(new byte[] { 0xDA, 0xDB, 0xDC, 0xDD, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0 }, HidLcdProtocol.BuildHello());
    }

    [Theory]
    [InlineData(72, 480, 480)]
    [InlineData(129, 480, 480)]
    [InlineData(54, 360, 360)]
    [InlineData(128, 1280, 480)]
    [InlineData(100, 320, 320)]
    [InlineData(36, 240, 240)]
    [InlineData(64, 640, 480)]
    [InlineData(3, 320, 240)]
    public void Hid_mode_byte_picks_the_size(byte mode, int width, int height)
    {
        var screen = HidLcdProtocol.ParseHelloReply(HidReply(mode));
        Assert.Equal((width, height), (screen.Width, screen.Height));
        Assert.Contains("experimental", screen.Model);
    }

    [Fact]
    public void Hid_mode_49_is_refused_rather_than_guessed()
    {
        Assert.Throws<ScreenUnsupportedException>(() => HidLcdProtocol.ParseHelloReply(HidReply(49)));
    }

    [Fact]
    public void Hid_reply_without_the_magic_is_rejected()
    {
        var reply = HidReply(72);
        reply[1] = 0;
        Assert.Throws<InvalidDataException>(() => HidLcdProtocol.ParseHelloReply(reply));
    }

    [Fact]
    public void Hid_480_frame_has_the_20_byte_header()
    {
        var screen = HidLcdProtocol.ParseHelloReply(HidReply(72));
        var jpeg = new byte[1000];
        var frame = HidLcdProtocol.BuildFrame(screen, jpeg);
        Assert.Equal(20 + 1000, frame.Length);
        Assert.Equal(new byte[] { 0xDA, 0xDB, 0xDC, 0xDD, 2, 0, 0, 0, 0xE0, 0x01, 0xE0, 0x01, 2, 0, 0, 0, 0xE8, 0x03, 0, 0 }, frame[..20]);
    }

    [Fact]
    public void Hid_320_square_uses_the_Core_Vision_style_header()
    {
        var screen = HidLcdProtocol.ParseHelloReply(HidReply(100));
        var frame = HidLcdProtocol.BuildFrame(screen, new byte[10]);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, frame[..4]);
        Assert.Equal(320, BitConverter.ToInt32(frame, 8));
    }

    [Fact]
    public void Hid_320x240_picture_is_turned_and_its_header_says_240_wide()
    {
        var screen = HidLcdProtocol.ParseHelloReply(HidReply(3));
        var frame = HidLcdProtocol.BuildFrame(screen, Jpeg(320, 240, Color.Red));
        Assert.Equal((240, 320), (BitConverter.ToUInt16(frame, 8), BitConverter.ToUInt16(frame, 10)));
        using var turned = new Bitmap(new MemoryStream(frame[20..]));
        Assert.Equal((240, 320), (turned.Width, turned.Height));
        Assert.Equal(frame.Length - 20, BitConverter.ToInt32(frame, 16));
    }

    [Fact]
    public void Hid_frames_go_out_as_513_byte_reports()
    {
        var link = new FakeLink(() => null);
        var transport = new HidScreenTransport(link, ScreenDevice.HidLcd);
        HidLcdProtocol.Send(transport, new byte[1100]);
        Assert.Equal(3, link.Reports.Count);
        Assert.All(link.Reports, report => Assert.Equal(513, report.Length));
        Assert.All(link.Reports, report => Assert.Equal(0, report[0]));
    }

    [Fact]
    public void Hid_handshake_skips_queued_frame_answers()
    {
        var replies = new Queue<byte[]>([HidReply(72, message: 8), HidReply(54)]);
        var transport = new HidScreenTransport(new FakeLink(() => replies.Count > 0 ? replies.Dequeue() : null), ScreenDevice.HidLcd);
        var screen = ScreenDevice.HidLcd.Family.Connect(transport);
        Assert.Equal(360, screen.Width);
    }

    [Fact]
    public void Ali_hello_announces_1024_bytes_and_carries_them()
    {
        var hello = AliLcdProtocol.BuildHello();
        Assert.Equal(16 + 1024, hello.Length);
        Assert.Equal(new byte[] { 0xF5, 0, 1, 0, 0xBC, 0xFF, 0xB6, 0xC8, 0, 0, 0, 0, 0, 4, 0, 0 }, hello[..16]);
    }

    [Theory]
    [InlineData(101, 320, 320)]
    [InlineData(102, 320, 320)]
    [InlineData(54, 320, 240)]
    public void Ali_reply_picks_the_size(byte first, int width, int height)
    {
        var reply = new byte[1024];
        reply[0] = first;
        var screen = AliLcdProtocol.ParseHelloReply(reply);
        Assert.Equal((width, height), (screen.Width, screen.Height));
    }

    [Fact]
    public void Ali_unknown_reply_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => AliLcdProtocol.ParseHelloReply(new byte[1024]));
    }

    [Fact]
    public void Ali_frame_is_header_plus_big_endian_RGB565()
    {
        var reply = new byte[1024];
        reply[0] = 101;
        var screen = AliLcdProtocol.ParseHelloReply(reply);
        var frame = AliLcdProtocol.BuildFrame(screen, Jpeg(320, 320, Color.FromArgb(255, 0, 0)));
        Assert.Equal(16 + 320 * 320 * 2, frame.Length);
        Assert.Equal(new byte[] { 0xF5, 1, 1, 0, 0xBC, 0xFF, 0xB6, 0xC8, 0, 0, 0, 0 }, frame[..12]);
        Assert.Equal(320 * 320 * 2, BitConverter.ToInt32(frame, 12));
        Assert.InRange(frame[16], 0xF0, 0xF8); // red's five bits lead the high byte (JPEG may shift it slightly)
    }

    [Fact]
    public void Rgb565_packs_like_TRCC()
    {
        Assert.Equal(((byte)0xF8, (byte)0x00), Rgb565.Pack(255, 0, 0));
        Assert.Equal(((byte)0x07, (byte)0xE0), Rgb565.Pack(0, 255, 0));
        Assert.Equal(((byte)0x00, (byte)0x1F), Rgb565.Pack(0, 0, 255));
    }

    [Fact]
    public void New_models_come_after_the_Core_Vision_and_Trofeo()
    {
        Assert.Equal([ScreenDevice.CoreVision, ScreenDevice.TrofeoVision, ScreenDevice.HidLcd, ScreenDevice.AliLcd], ScreenDevice.All.Take(4));
        Assert.DoesNotContain((ScreenDevice.HidLcd.VendorId, ScreenDevice.HidLcd.ProductId), ScreenFinder.UnsupportedDisplays);
        Assert.DoesNotContain((ScreenDevice.AliLcd.VendorId, ScreenDevice.AliLcd.ProductId), ScreenFinder.UnsupportedDisplays);
    }
}
