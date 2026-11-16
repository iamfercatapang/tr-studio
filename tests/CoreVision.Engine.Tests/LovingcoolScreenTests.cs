using System.Drawing;
using System.Text;
using CoreVision.Engine;
using Xunit;

/// <summary>Checks the Lovingcool serial format as lovi-ctrl documents it. No Lovingcool screen was available to test on.</summary>
public class LovingcoolScreenTests
{
    static byte[] InfoReply(string json) => LovingcoolProtocol.Command(LovingcoolProtocol.GetInfo, Encoding.UTF8.GetBytes(json));

    const string Ver1 = """{"width":480,"height":320,"brightness":80,"model":"txm818-7inch","version":"Ver1.0","uid":"0C014F018787","region":"00000"}""";

    sealed class FakeSerial(byte[] reply, int pieces = 1) : IScreenTransport
    {
        public readonly List<byte[]> Writes = [];
        int _at;
        public ScreenDevice Device => ScreenDevice.Lovingcool;
        public void Write(byte[] data, int length) => Writes.Add(data[..length]);

        public int Read(byte[] buffer)
        {
            if (_at >= reply.Length) return 0;
            var count = Math.Min(reply.Length - _at, (reply.Length + pieces - 1) / pieces);
            reply.AsSpan(_at, count).CopyTo(buffer);
            _at += count;
            return count;
        }

        public void Dispose() { }
    }

    [Fact]
    public void Command_frame_has_sync_length_key_and_sum()
    {
        Assert.Equal(new byte[] { 0x55, 0xAA, 0x07, 0x00, 0x06, 0x0C, 0x01 }, LovingcoolProtocol.Command(0x06));
        var brightness = LovingcoolProtocol.Command(0x03, [50]);
        Assert.Equal(8, brightness.Length);
        Assert.Equal(0x55 + 0xAA + 8 + 3 + 50, BitConverter.ToUInt16(brightness, 6));
    }

    [Fact]
    public void Info_gives_the_size_and_model()
    {
        var screen = LovingcoolProtocol.ParseInfo(InfoReply(Ver1));
        Assert.Equal((480, 320), (screen.Width, screen.Height));
        Assert.Equal("txm818-7inch Ver1.0", screen.Firmware);
        Assert.Contains("Lovingcool", screen.Model);
        Assert.Contains("experimental", screen.Model);
    }

    [Fact]
    public void Info_inside_a_data_field_is_read_too()
    {
        var screen = LovingcoolProtocol.ParseInfo(InfoReply("""{"code":0,"data":{"width":320,"height":240,"version":"Ver3.1"}}"""));
        Assert.Equal((320, 240), (screen.Width, screen.Height));
    }

    [Fact]
    public void Info_without_a_size_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => LovingcoolProtocol.ParseInfo(InfoReply("""{"model":"x"}""")));
        Assert.Throws<InvalidDataException>(() => LovingcoolProtocol.ParseInfo(new byte[] { 1, 2, 3, 4, 5, 6, 7 }));
    }

    [Theory]
    [InlineData("Ver1.0", false)]
    [InlineData("Ver2.8", false)]
    [InlineData("Ver2.9", true)]
    [InlineData("V3.0.1", true)]
    [InlineData("", false)]
    public void Pictures_are_wrapped_only_after_version_2_8(string version, bool wrapped)
    {
        Assert.Equal(wrapped, LovingcoolProtocol.Wrapped(version));
    }

    [Fact]
    public void Old_firmware_gets_the_bare_JPEG()
    {
        var screen = LovingcoolProtocol.ParseInfo(InfoReply(Ver1));
        var jpeg = new byte[] { 0xFF, 0xD8, 1, 2, 3, 0xFF, 0xD9 };
        Assert.Equal(jpeg, LovingcoolProtocol.BuildFrame(screen, jpeg));
    }

    [Fact]
    public void New_firmware_gets_length_JPEG_and_sum()
    {
        var screen = LovingcoolProtocol.ParseInfo(InfoReply("""{"width":480,"height":320,"version":"Ver3.0"}"""));
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var frame = LovingcoolProtocol.BuildFrame(screen, jpeg);
        Assert.Equal(4 + 4 + 2, frame.Length);
        Assert.Equal(4, BitConverter.ToInt32(frame, 0));
        Assert.Equal(jpeg, frame[4..8]);
        Assert.Equal((4 + 0xFF + 0xD8 + 0xFF + 0xD9) & 0xFFFF, BitConverter.ToUInt16(frame, 8));
    }

    [Fact]
    public void Large_pictures_are_squeezed_under_50_KB()
    {
        using var bitmap = new Bitmap(480, 320);
        var random = new Random(1);
        for (var y = 0; y < 320; y++)
            for (var x = 0; x < 480; x++) bitmap.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
        var big = JpegFrame.Encode(bitmap, 100);
        Assert.True(big.Length > LovingcoolProtocol.MaxJpegBytes);
        var screen = LovingcoolProtocol.ParseInfo(InfoReply(Ver1));
        Assert.True(LovingcoolProtocol.BuildFrame(screen, big).Length <= LovingcoolProtocol.MaxJpegBytes);
    }

    [Fact]
    public void Connect_asks_for_info_even_when_it_arrives_in_pieces_then_starts_live()
    {
        var serial = new FakeSerial([0x00, .. InfoReply(Ver1)], pieces: 5);
        var screen = ScreenDevice.Lovingcool.Family.Connect(serial);
        Assert.Equal(480, screen.Width);
        Assert.Equal(LovingcoolProtocol.Command(LovingcoolProtocol.GetInfo), serial.Writes[0]);
        Assert.Equal(LovingcoolProtocol.Command(LovingcoolProtocol.StartLive), serial.Writes[1]);
    }

    [Fact]
    public void A_silent_screen_fails_the_handshake()
    {
        Assert.Throws<InvalidDataException>(() => ScreenDevice.Lovingcool.Family.Connect(new FakeSerial([])));
    }

    [Fact]
    public void Lovingcool_is_tried_last()
    {
        Assert.Same(ScreenDevice.Lovingcool, ScreenDevice.All[^1]);
        Assert.Equal(ScreenTransportKind.Serial, ScreenDevice.Lovingcool.Transport);
    }
}
