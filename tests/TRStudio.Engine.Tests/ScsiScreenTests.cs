using System.Drawing;
using TRStudio.Engine;
using Xunit;

/// <summary>Checks the SCSI screen format (87CD:70DB and 0402:3922) decoded from TRCC 2.1.4's USBLCD.exe. No such screen was available to test on.</summary>
public class ScsiScreenTests
{
    sealed class FakeDrive(byte code) : IScsiLink
    {
        public readonly List<(byte[] Cdb, byte[] Data)> Sent = [];
        public readonly List<byte[]> Asked = [];
        public void Send(ReadOnlySpan<byte> cdb, ReadOnlySpan<byte> data) => Sent.Add((cdb.ToArray(), data.ToArray()));
        public void Receive(ReadOnlySpan<byte> cdb, Span<byte> buffer)
        {
            Asked.Add(cdb.ToArray());
            buffer.Clear();
            buffer[0] = code;
        }
        public void Dispose() { }
    }

    static byte[] Reply(byte code)
    {
        var reply = new byte[ScsiLcdProtocol.PollSize];
        reply[0] = code;
        return reply;
    }

    static byte[] Jpeg(int width, int height, Action<Graphics> draw)
    {
        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap)) draw(g);
        return JpegFrame.Encode(bitmap);
    }

    [Fact]
    public void Poll_is_TRCCs_status_read()
    {
        Assert.Equal(new byte[] { 0xF5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x00, 0xE1, 0, 0 }, ScsiLcdProtocol.BuildPoll());
    }

    [Theory]
    [InlineData(36, 240, 240)]
    [InlineData(50, 320, 240)]
    [InlineData(51, 320, 240)]
    [InlineData(100, 320, 320)]
    [InlineData(101, 320, 320)]
    public void Reply_code_picks_the_size(byte code, int width, int height)
    {
        var screen = ScsiLcdProtocol.ParseReply(Reply(code));
        Assert.Equal((width, height), (screen.Width, screen.Height));
        Assert.EndsWith("(experimental)", screen.Model);
    }

    [Fact]
    public void Unknown_code_is_refused_rather_than_guessed()
    {
        Assert.Throws<ScreenUnsupportedException>(() => ScsiLcdProtocol.ParseReply(Reply(72)));
    }

    [Fact]
    public void A_starting_screen_is_recognised()
    {
        var reply = Reply(0);
        new byte[] { 0xA1, 0xA2, 0xA3, 0xA4 }.CopyTo(reply, 4);
        Assert.True(ScsiLcdProtocol.IsStarting(reply));
        Assert.False(ScsiLcdProtocol.IsStarting(Reply(50)));
    }

    [Fact]
    public void Connect_reads_the_status_with_the_poll()
    {
        var drive = new FakeDrive(100);
        using var transport = new ScsiScreenTransport(drive, ScreenDevice.FrozenScsi);
        var screen = ScreenDevice.FrozenScsi.Family.Connect(transport);
        Assert.Equal(320, screen.Height);
        Assert.Equal(ScsiLcdProtocol.BuildPoll(), Assert.Single(drive.Asked));
    }

    [Fact]
    public void Square_320_frame_is_big_endian_RGB565()
    {
        var screen = ScsiLcdProtocol.ParseReply(Reply(100));
        var frame = ScsiLcdProtocol.BuildFrame(screen, Jpeg(320, 320, g => g.Clear(Color.Red)));
        Assert.Equal(320 * 320 * 2, frame.Length);
        Assert.InRange(frame[0], 0xF0, 0xF8); // red's five bits lead the high byte, which comes first
    }

    [Fact]
    public void Code_50_sends_the_low_byte_first()
    {
        var screen = ScsiLcdProtocol.ParseReply(Reply(50));
        var frame = ScsiLcdProtocol.BuildFrame(screen, Jpeg(320, 240, g => g.Clear(Color.Red)));
        Assert.Equal(320 * 240 * 2, frame.Length);
        Assert.InRange(frame[1], 0xF0, 0xF8);
        Assert.InRange(frame[0], 0x00, 0x08);
    }

    [Fact]
    public void Landscape_320x240_is_turned_a_quarter_clockwise()
    {
        var screen = ScsiLcdProtocol.ParseReply(Reply(51));
        Assert.Equal((240, 320), (screen.HeaderWidth, screen.HeaderHeight));
        // Blue on the left half: after a clockwise turn it is the top half of the 240-wide, 320-tall picture.
        var frame = ScsiLcdProtocol.BuildFrame(screen, Jpeg(320, 240, g =>
        {
            g.Clear(Color.White);
            g.FillRectangle(Brushes.Blue, 0, 0, 160, 240);
        }));
        int Pixel(int x, int y) => (frame[(y * 240 + x) * 2] << 8) | frame[(y * 240 + x) * 2 + 1];
        Assert.True((Pixel(120, 40) & 0xF800) < 0x4000, "top should be blue");
        Assert.True((Pixel(120, 280) & 0xF800) > 0xC000, "bottom should be white");
    }

    [Fact]
    public void First_frame_starts_the_session_then_goes_in_E100_pieces()
    {
        var frame = new byte[320 * 240 * 2];
        var writes = ScsiLcdProtocol.Writes(frame, first: true).ToList();
        Assert.Equal(new byte[] { 0xF5, 1, 0, 0 }, writes[0].Cdb[..4]);
        Assert.Equal([(1, 0, 0xE100), (1, 1, 0xE100), (1, 2, 0x9600)],
            writes.Skip(1).Select(w => ((int)w.Cdb[2], (int)w.Cdb[3], BitConverter.ToInt32(w.Cdb, 12))));
        Assert.All(writes, w => Assert.Equal(BitConverter.ToInt32(w.Cdb, 12), w.Data.Count));
    }

    [Fact]
    public void Frame_320_square_goes_in_64K_pieces()
    {
        var writes = ScsiLcdProtocol.Writes(new byte[320 * 320 * 2], first: false).ToList();
        Assert.Equal([0x10000, 0x10000, 0x10000, 0x2000], writes.Select(w => BitConverter.ToInt32(w.Cdb, 12)));
        Assert.Equal([0, 1, 2, 3], writes.Select(w => (int)w.Cdb[3]));
    }

    [Fact]
    public void Start_command_goes_out_once_per_connection()
    {
        var drive = new FakeDrive(36);
        using var transport = new ScsiScreenTransport(drive, ScreenDevice.WarframeScsi);
        var family = ScreenDevice.WarframeScsi.Family;
        var screen = family.Connect(transport);
        var frame = family.BuildFrame(screen, Jpeg(240, 240, g => g.Clear(Color.Green)));
        family.Send(transport, frame);
        family.Send(transport, frame);
        Assert.Equal(1 + 2 + 2, drive.Sent.Count);
        Assert.Single(drive.Sent, s => s.Cdb[2] == 0);
    }

    [Fact]
    public void Scsi_models_are_tried_last_and_no_longer_listed_as_unsupported()
    {
        Assert.Equal([ScreenDevice.CoreVision, ScreenDevice.TrofeoVision, ScreenDevice.HidLcd, ScreenDevice.AliLcd, ScreenDevice.FrozenScsi, ScreenDevice.WarframeScsi], ScreenDevice.All);
        Assert.All(new[] { ScreenDevice.FrozenScsi, ScreenDevice.WarframeScsi }, model =>
        {
            Assert.Equal(ScreenTransportKind.Scsi, model.Transport);
            Assert.DoesNotContain((model.VendorId, model.ProductId), ScreenFinder.UnsupportedDisplays);
        });
    }
}
