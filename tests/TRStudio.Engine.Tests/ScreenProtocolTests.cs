using TRStudio.Engine;
using Xunit;

public class ScreenProtocolTests
{
    // The reply captured from the real screen on 2026-10-31.
    public static byte[] RealReply => Convert.FromHexString(
        "12345678535343524D2D563100000000000000001EDB3C8F03000000160000000000000000000000000000000000000000000000000000000100000000000000");

    [Fact]
    public void Parses_the_real_handshake_reply()
    {
        var info = ScreenProtocol.ParseHelloReply(RealReply);
        Assert.Equal(("SSCRM-V1", 3, 480, 480, true), (info.Firmware, info.Mode, info.Width, info.Height, info.Round));
    }

    [Fact]
    public void Rejects_a_reply_with_bad_magic()
    {
        var reply = RealReply;
        reply[0] = 0;
        Assert.Throws<InvalidDataException>(() => ScreenProtocol.ParseHelloReply(reply));
    }

    [Fact]
    public void Reports_an_unknown_panel_mode()
    {
        var reply = RealReply;
        reply[24] = 50;
        Assert.Throws<ScreenUnsupportedException>(() => ScreenProtocol.ParseHelloReply(reply));
    }

    [Fact]
    public void Frame_header_matches_the_decoded_layout()
    {
        var frame = ScreenProtocol.BuildFrame(480, 480, new byte[300]);
        Assert.Equal(364, frame.Length);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78, 2, 0, 0, 0, 0xE0, 1, 0, 0, 0xE0, 1, 0, 0 }, frame[..16]);
        Assert.Equal(2, frame[56]);
        Assert.Equal(300, BitConverter.ToInt32(frame, 60));
    }

    [Fact]
    public void Rejects_a_jpeg_at_the_size_limit()
    {
        Assert.Throws<ArgumentException>(() => ScreenProtocol.BuildFrame(480, 480, new byte[ScreenProtocol.MaxJpegBytes]));
    }

    [Theory]
    [InlineData(512, true)]
    [InlineData(1024, true)]
    [InlineData(513, false)]
    public void Terminates_only_on_packet_boundaries(int length, bool expected)
    {
        Assert.Equal(expected, ScreenProtocol.NeedsTerminator(length));
    }
}
