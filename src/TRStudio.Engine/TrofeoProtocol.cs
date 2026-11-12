namespace TRStudio.Engine;

/// <summary>
/// Wire format of the Trofeo Vision 9.16" screen (USB 0416:5408), which TRCC calls "LY".
/// Decoded from TRCC's code and not yet tried on real hardware. See docs/PROTOCOL.md.
/// </summary>
public static class TrofeoProtocol
{
    public const int HelloSize = 2048;
    public const int ReplySize = 512;
    public const int PacketSize = 512;
    public const int ChunkSize = 496;
    public const int BlockSize = 4096;
    public const int MaxJpegBytes = 691_200 - 64; // TRCC's shared buffer, less its 64-byte header
    const int HeaderSize = PacketSize - ChunkSize;
    const int ModeBase = 64;
    static readonly byte[] HelloStart = [0x02, 0xFF, 0, 0, 0, 0, 0, 0, 0x01];

    public static byte[] BuildHello()
    {
        var hello = new byte[HelloSize];
        HelloStart.CopyTo(hello, 0);
        return hello;
    }

    /// <summary>The panel type gives TRCC's mode, which picks the size from the same table as the Core Vision family.</summary>
    public static ScreenInfo ParseHelloReply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 23 || reply[0] != 3 || reply[1] != 0xFF || reply[8] != 1)
            throw new InvalidDataException("The screen sent an unexpected handshake reply.");
        var serial = Convert.ToHexString(reply.Slice(16, 4));
        var panel = reply[20] <= 3 ? 1 : reply[20];
        var mode = ModeBase + panel;
        var subMode = 1 + reply[22];
        var known = ScreenProtocol.Identify($"LY {serial}", mode, subMode);
        if (known.Width == ThemeRenderer.ScreenSize && known.Height == ThemeRenderer.ScreenSize)
            throw new ScreenUnsupportedException($"This Trofeo-type screen reported mode {mode}, which isn't supported yet.");
        return known with { ModelName = mode is 65 or 66 ? "Trofeo Vision (experimental)" : null };
    }

    /// <summary>
    /// Splits the JPEG into 512-byte packets with a 16-byte header each. Like TRCC, there are always
    /// size / 496 + 1 chunks, and empty packets pad the count to a multiple of 4.
    /// </summary>
    public static byte[] BuildFrame(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length > MaxJpegBytes) throw new ArgumentException("JPEG is too large for the screen.", nameof(jpeg));
        var chunks = jpeg.Length / ChunkSize + 1;
        var packets = (chunks + 3) / 4 * 4;
        var frame = new byte[packets * PacketSize];
        for (var index = 0; index < chunks; index++)
        {
            var packet = frame.AsSpan(index * PacketSize, PacketSize);
            var length = index == chunks - 1 ? jpeg.Length % ChunkSize : ChunkSize;
            packet[0] = 0x01;
            packet[1] = 0xFF;
            BitConverter.TryWriteBytes(packet[2..], jpeg.Length);
            BitConverter.TryWriteBytes(packet[6..], (ushort)length);
            packet[8] = 0x01;
            BitConverter.TryWriteBytes(packet[9..], (ushort)chunks);
            BitConverter.TryWriteBytes(packet[11..], (ushort)index);
            jpeg.Slice(index * ChunkSize, length).CopyTo(packet[HeaderSize..]);
        }
        return frame;
    }

    /// <summary>Writes 4096 bytes at a time, 2048 for a final half block, then reads the screen's 512-byte answer.</summary>
    public static void Send(IScreenTransport transport, byte[] frame)
    {
        for (var offset = 0; offset < frame.Length; offset += BlockSize)
        {
            var length = Math.Min(BlockSize, frame.Length - offset);
            transport.Write(frame[offset..(offset + length)], length);
        }
        transport.Read(new byte[ReplySize]);
    }
}
