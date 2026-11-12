using System.Drawing;

namespace TRStudio.Engine;

/// <summary>
/// Wire format of TRCC's SCSI screens (USB 87CD:70DB: Frozen Horizon Pro, Frozen Magic Pro, Frozen Vision V2;
/// USB 0402:3922: Frozen Warframe, Frozen Warframe 240). Windows sees them as USB drives; TRCC talks to them with
/// vendor SCSI commands (opcode F5) through its native USBLCD.exe, and they take raw RGB565 pixels rather than JPEG.
/// Decoded from TRCC 2.1.4 (USBLCD.exe, and FormCZTV.ImageTo565 for the pixels) and not yet tried on real hardware.
/// </summary>
public static class ScsiLcdProtocol
{
    public const int CdbSize = 16;
    public const int PollSize = 0xE100;
    public const int SmallChunk = 0xE100;
    public const int LargeChunk = 0x10000;
    const byte Opcode = 0xF5, ReadDirection = 0, WriteDirection = 1;
    const byte StatusCommand = 0, FrameCommand = 1;

    /// <summary>The reply's first byte. 51 and the 320x320 codes take pixels high byte first; 36 and 50 low byte first.</summary>
    public const byte Code240 = 36, Code320x240 = 50, Code320x240Spi = 51, Code320 = 100, Code320b = 101;

    /// <summary>16 bytes: F5, read (0) or write (1), the command, the chunk index, eight zeros, then the data length.</summary>
    public static byte[] BuildCdb(byte direction, byte command, byte index, int length)
    {
        var cdb = new byte[CdbSize];
        (cdb[0], cdb[1], cdb[2], cdb[3]) = (Opcode, direction, command, index);
        BitConverter.TryWriteBytes(cdb.AsSpan(12), length);
        return cdb;
    }

    /// <summary>The status read that opens the session: 0xE100 bytes come back.</summary>
    public static byte[] BuildPoll() => BuildCdb(ReadDirection, StatusCommand, 0, PollSize);

    /// <summary>Bytes 4 to 7 read A1 A2 A3 A4 while the screen is still starting; TRCC waits 3 seconds and asks again.</summary>
    public static bool IsStarting(ReadOnlySpan<byte> reply) =>
        reply.Length >= 8 && reply[4] == 0xA1 && reply[5] == 0xA2 && reply[6] == 0xA3 && reply[7] == 0xA4;

    public static ScreenInfo ParseReply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 8) throw new InvalidDataException("The screen sent an unexpected status reply.");
        var (width, height) = reply[0] switch
        {
            Code240 => (240, 240),
            Code320x240 or Code320x240Spi => (320, 240),
            Code320 or Code320b => (320, 320),
            _ => throw new ScreenUnsupportedException($"Found a SCSI screen with an unknown panel ({reply[0]})."),
        };
        return new ScreenInfo($"SCSI {reply[0]}", reply[0], width, height)
        {
            // The 320x240 panels scan portrait, so pictures go to them a quarter turn clockwise.
            HeaderWidth = width > height ? height : width,
            HeaderHeight = width > height ? width : height,
            ModelName = $"{width}×{height} Screen (experimental)",
        };
    }

    public static bool HighByteFirst(int code) => code is Code320x240Spi or Code320 or Code320b;

    /// <summary>The frame is the bare pixels, turned for portrait panels, in the byte order the panel takes.</summary>
    public static byte[] BuildFrame(ScreenInfo screen, byte[] jpeg)
    {
        using var input = new MemoryStream(jpeg);
        using var source = new Bitmap(input);
        using var sized = new Bitmap(source, screen.Width, screen.Height);
        if (screen.HeaderWidth != screen.Width) sized.RotateFlip(RotateFlipType.Rotate90FlipNone);
        var pixels = Rgb565.Encode(sized, screen.HeaderWidth, screen.HeaderHeight);
        if (!HighByteFirst(screen.Mode))
            for (var i = 0; i < pixels.Length; i += 2) (pixels[i], pixels[i + 1]) = (pixels[i + 1], pixels[i]);
        return pixels;
    }

    /// <summary>320x320 frames go in 64 KiB pieces; the smaller panels in 0xE100-byte pieces.</summary>
    public static int ChunkSize(int frameLength) => frameLength > 320 * 240 * 2 ? LargeChunk : SmallChunk;

    /// <summary>
    /// The writes for one frame: commands F5 01 01 &lt;index&gt;, each carrying the next piece of pixels. Before the
    /// first frame of a session TRCC sends F5 01 00 00 with the frame's first 0xE100 bytes.
    /// </summary>
    public static IEnumerable<(byte[] Cdb, ArraySegment<byte> Data)> Writes(byte[] frame, bool first)
    {
        if (first) yield return (BuildCdb(WriteDirection, 0, 0, SmallChunk), Slice(frame, 0, SmallChunk));
        var size = ChunkSize(frame.Length);
        for (int at = 0, index = 0; at < frame.Length; at += size, index++)
        {
            var length = Math.Min(size, frame.Length - at);
            yield return (BuildCdb(WriteDirection, FrameCommand, (byte)index, length), new ArraySegment<byte>(frame, at, length));
        }
    }

    static ArraySegment<byte> Slice(byte[] frame, int at, int length)
    {
        if (frame.Length >= at + length) return new ArraySegment<byte>(frame, at, length);
        var padded = new byte[length];
        frame.AsSpan(at).CopyTo(padded);
        return padded;
    }
}

sealed class ScsiLcdFamily : IScreenFamily
{
    /// <summary>How long a starting screen is given. TRCC waits forever, 3 seconds at a time.</summary>
    internal int StartTries { get; init; } = 5;
    internal TimeSpan StartWait { get; init; } = TimeSpan.FromSeconds(3);

    public ScreenInfo Connect(IScreenTransport transport)
    {
        var scsi = Scsi(transport);
        var reply = new byte[ScsiLcdProtocol.PollSize];
        for (var attempt = 1; ; attempt++)
        {
            scsi.Receive(ScsiLcdProtocol.BuildPoll(), reply);
            if (!ScsiLcdProtocol.IsStarting(reply)) break;
            if (attempt >= StartTries) throw new IOException("the screen is still starting");
            Thread.Sleep(StartWait);
        }
        scsi.Started = false;
        return ScsiLcdProtocol.ParseReply(reply);
    }

    public byte[] BuildFrame(ScreenInfo screen, byte[] jpeg) => ScsiLcdProtocol.BuildFrame(screen, jpeg);

    public void Send(IScreenTransport transport, byte[] frame)
    {
        var scsi = Scsi(transport);
        foreach (var (cdb, data) in ScsiLcdProtocol.Writes(frame, first: !scsi.Started)) scsi.Send(cdb, data);
        scsi.Started = true;
    }

    static ScsiScreenTransport Scsi(IScreenTransport transport) =>
        transport as ScsiScreenTransport ?? throw new InvalidOperationException("SCSI screens need a SCSI transport.");
}
