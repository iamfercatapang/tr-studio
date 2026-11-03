using CoreVision.Engine;
using Xunit;

/// <summary>Checks the encoder against the format decoded from TRCC. No Trofeo Vision was available to test on.</summary>
public class TrofeoProtocolTests
{
    static byte[] Reply(byte panel = 1, byte sub = 0)
    {
        var reply = new byte[TrofeoProtocol.ReplySize];
        reply[0] = 3;
        reply[1] = 0xFF;
        reply[8] = 1;
        new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }.CopyTo(reply, 16);
        reply[20] = panel;
        reply[22] = sub;
        return reply;
    }

    sealed class FakeTrofeo : IScreenTransport
    {
        public readonly List<byte[]> Writes = [];
        public int Reads;
        public ScreenDevice Device => ScreenDevice.TrofeoVision;

        public void Write(byte[] data, int length) { lock (Writes) Writes.Add(data[..length]); }

        public int Read(byte[] buffer)
        {
            Interlocked.Increment(ref Reads);
            Reply().CopyTo(buffer, 0);
            return TrofeoProtocol.ReplySize;
        }

        public void Dispose() { }
    }

    [Fact]
    public void Hello_is_2048_bytes_starting_like_TRCCs()
    {
        var hello = TrofeoProtocol.BuildHello();
        Assert.Equal(2048, hello.Length);
        Assert.Equal(new byte[] { 0x02, 0xFF, 0, 0, 0, 0, 0, 0, 0x01 }, hello[..9]);
        Assert.All(hello[9..], b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Small_panel_types_are_the_1920x462_Trofeo(byte panel)
    {
        var screen = TrofeoProtocol.ParseHelloReply(Reply(panel));
        Assert.Equal((65, 1920, 462), (screen.Mode, screen.Width, screen.Height));
        Assert.Equal("Trofeo Vision (experimental)", screen.Model);
        Assert.Equal("LY DEADBEEF", screen.Firmware);
        Assert.False(screen.Round);
    }

    [Fact]
    public void Panel_type_5_is_a_1920x440_screen_named_by_size()
    {
        var screen = TrofeoProtocol.ParseHelloReply(Reply(5));
        Assert.Equal((69, 1920, 440), (screen.Mode, screen.Width, screen.Height));
        Assert.Equal("1920×440 Screen", screen.Model);
    }

    [Fact]
    public void Unknown_panel_types_are_unsupported_rather_than_guessed()
    {
        Assert.Throws<ScreenUnsupportedException>(() => TrofeoProtocol.ParseHelloReply(Reply(7)));
    }

    [Fact]
    public void A_wrong_reply_is_rejected()
    {
        var reply = Reply();
        reply[0] = 0x12;
        Assert.Throws<InvalidDataException>(() => TrofeoProtocol.ParseHelloReply(reply));
    }

    [Fact]
    public void Frame_packets_carry_size_length_count_and_index()
    {
        var jpeg = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();
        var frame = TrofeoProtocol.BuildFrame(jpeg);

        Assert.Equal(4 * 512, frame.Length); // 3 chunks, padded to 4 packets
        int[] lengths = [496, 496, 8];
        for (var index = 0; index < 3; index++)
        {
            var packet = frame.AsSpan(index * 512, 512);
            Assert.Equal(1, packet[0]);
            Assert.Equal(0xFF, packet[1]);
            Assert.Equal(1000, BitConverter.ToInt32(packet[2..6]));
            Assert.Equal(lengths[index], BitConverter.ToUInt16(packet[6..8]));
            Assert.Equal(1, packet[8]);
            Assert.Equal(3, BitConverter.ToUInt16(packet[9..11]));
            Assert.Equal(index, BitConverter.ToUInt16(packet[11..13]));
            Assert.Equal(jpeg.AsSpan(index * 496, lengths[index]).ToArray(), packet.Slice(16, lengths[index]).ToArray());
        }
        Assert.All(frame[(3 * 512)..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void A_jpeg_that_fills_its_chunks_ends_with_an_empty_one_like_TRCC()
    {
        var frame = TrofeoProtocol.BuildFrame(new byte[2 * 496]);
        var last = frame.AsSpan(2 * 512, 512);
        Assert.Equal(3, BitConverter.ToUInt16(last[9..11]));
        Assert.Equal(2, BitConverter.ToUInt16(last[11..13]));
        Assert.Equal(0, BitConverter.ToUInt16(last[6..8]));
    }

    [Fact]
    public void Sends_4096_byte_blocks_then_a_half_block_and_reads_the_answer()
    {
        var transport = new FakeTrofeo();
        var frame = TrofeoProtocol.BuildFrame(new byte[496 * 9]); // 10 chunks, padded to 12 packets
        TrofeoProtocol.Send(transport, frame);
        Assert.Equal([4096, 2048], transport.Writes.Select(w => w.Length));
        Assert.Equal(frame, transport.Writes.SelectMany(w => w).ToArray());
        Assert.Equal(1, transport.Reads);
    }

    [Fact]
    public void Rejects_a_jpeg_larger_than_TRCCs_buffer()
    {
        Assert.Throws<ArgumentException>(() => TrofeoProtocol.BuildFrame(new byte[TrofeoProtocol.MaxJpegBytes + 1]));
    }

    [Fact]
    public void The_link_greets_and_drives_a_Trofeo_with_its_own_format()
    {
        var screen = new FakeTrofeo();
        using var link = new ScreenLink(() => screen);
        link.Start();
        link.Submit(new byte[100]);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (link.FramesSent < 1)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            Thread.Sleep(5);
        }
        Assert.Equal(LinkState.Connected, link.Status.State);
        Assert.Equal((1920, 462), (link.Status.Screen!.Width, link.Status.Screen.Height));
        lock (screen.Writes)
        {
            Assert.Equal(TrofeoProtocol.HelloSize, screen.Writes[0].Length);
            Assert.Equal(2048, screen.Writes[1].Length); // one chunk, padded to 4 packets
            Assert.Equal(1, screen.Writes[1][0]);
        }
    }

    [Fact]
    public void Finder_tries_the_Core_Vision_first_then_the_Trofeo()
    {
        var tried = new List<string>();
        var trofeo = new FakeTrofeo();
        var opened = ScreenFinder.OpenFirst(ScreenDevice.All, model =>
        {
            tried.Add(model.Name);
            return model == ScreenDevice.CoreVision ? throw new ScreenNotFoundException("Screen is not connected") : trofeo;
        });
        Assert.Same(trofeo, opened);
        Assert.Equal(["Core Vision", "Trofeo Vision"], tried);
    }

    [Fact]
    public void A_Trofeo_that_wont_open_explains_the_driver()
    {
        var error = Assert.Throws<ScreenUnsupportedException>(() => ScreenFinder.OpenFirst(ScreenDevice.All, model =>
            model == ScreenDevice.CoreVision ? throw new ScreenNotFoundException("missing") : throw new IOException("The parameter is incorrect.")));
        Assert.Contains("Trofeo Vision", error.Message);
        Assert.Contains("WinUSB", error.Message);
    }

    [Fact]
    public void A_busy_Trofeo_says_so()
    {
        Assert.Throws<ScreenBusyException>(() => ScreenFinder.OpenFirst(ScreenDevice.All, model =>
            model == ScreenDevice.CoreVision ? throw new ScreenNotFoundException("missing") : throw new ScreenBusyException("TRCC")));
    }

    [Fact]
    public void A_plugged_in_Trofeo_that_Windows_hides_from_WinUSB_is_named()
    {
        static IScreenTransport Missing() => throw new ScreenNotFoundException("Screen is not connected");
        var error = Assert.Throws<ScreenUnsupportedException>(() =>
            ScreenFinder.Open(Missing, () => [@"\\?\usb#vid_0416&pid_5408#123#{a5dcbf10-6530-11d2-901f-00c04fb951ed}"]));
        Assert.Contains("Trofeo Vision", error.Message);
    }
}
