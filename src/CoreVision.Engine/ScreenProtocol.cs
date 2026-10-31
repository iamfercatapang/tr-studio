using System.Text;

namespace CoreVision.Engine;

/// <summary>What the screen reported about itself. The header size can differ from the panel size.</summary>
public sealed record ScreenInfo(string Firmware, int Mode, int Width, int Height)
{
    public int HeaderWidth { get; init; } = Width;
    public int HeaderHeight { get; init; } = Height;
    public int JpegQuality { get; init; } = ScreenProtocol.DefaultJpegQuality;
    public bool Round { get; init; }

    /// <summary>The handshake gives the panel, not the product, so other models are named by their size.</summary>
    public string Model => Mode == ScreenProtocol.CoreVisionMode ? "Core Vision 360" : $"{Width}×{Height} Screen";

    /// <summary>Used until a screen connects. It matches the only model this was tested on.</summary>
    public static ScreenInfo Default { get; } = new("", ScreenProtocol.CoreVisionMode, ThemeRenderer.ScreenSize, ThemeRenderer.ScreenSize) { Round = true };
}

/// <summary>Wire format of the cooler screen. See docs/PROTOCOL.md.</summary>
public static class ScreenProtocol
{
    public const ushort VendorId = 0x87AD;
    public const ushort ProductId = 0x70DB;
    public const int HeaderSize = 64;
    public const int MaxJpegBytes = 450_000;
    public const int DefaultJpegQuality = 95;
    public const int CoreVisionMode = 3;

    const int PacketSize = 512;
    const int PictureCommand = 2;
    const int Rgb565Mode = 50;
    const int ModeOffset = 24;
    const int SubModeOffset = 36;
    static readonly byte[] Magic = [0x12, 0x34, 0x56, 0x78];

    public static byte[] BuildHello()
    {
        var hello = new byte[HeaderSize];
        Magic.CopyTo(hello, 0);
        hello[56] = 0x01;
        return hello;
    }

    public static ScreenInfo ParseHelloReply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < HeaderSize || !reply[..4].SequenceEqual(Magic))
            throw new InvalidDataException("The screen sent an unexpected handshake reply.");
        var firmware = Encoding.ASCII.GetString(reply.Slice(4, 8)).TrimEnd('\0');
        return Identify(firmware, reply[ModeOffset], reply[SubModeOffset]);
    }

    /// <summary>
    /// The size table from TRCC 2.1.8, including its oddities: 1920x440 panels get a 1920x462
    /// header, and so do 1280x480 panels with a high sub mode. Only mode 3 is tested on real hardware.
    /// </summary>
    public static ScreenInfo Identify(string firmware, int mode, int subMode)
    {
        if (mode == Rgb565Mode) throw new ScreenUnsupportedException($"This screen uses a format that isn't supported yet (mode {mode}).");
        if (mode == 68 && subMode <= 2) throw new ScreenUnsupportedException($"This 1280x480 screen uses a format that isn't supported yet (sub mode {subMode}).");

        var (width, height) = mode switch
        {
            63 or 64 => (1600, 720),
            1 when subMode == 48 => (1600, 720),
            65 or 66 => (1920, 462),
            1 when subMode == 49 => (1920, 462),
            69 => (1920, 440),
            68 => (1280, 480),
            9 or 11 or 20 => (854, 480),
            10 or 16 => (960, 540),
            12 => (800, 480),
            13 or 17 or 18 => (960, 320),
            15 => (640, 172),
            _ => (ThemeRenderer.ScreenSize, ThemeRenderer.ScreenSize),
        };
        var usesWideHeader = mode is 68 or 69;
        return new ScreenInfo(firmware, mode, width, height)
        {
            HeaderWidth = usesWideHeader ? 1920 : width,
            HeaderHeight = usesWideHeader ? 462 : height,
            JpegQuality = mode switch { 5 => 50, 7 or 14 => 64, _ => DefaultJpegQuality },
            Round = mode == CoreVisionMode,
        };
    }

    public static byte[] BuildFrame(int width, int height, ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length >= MaxJpegBytes) throw new ArgumentException("JPEG is too large for the screen.", nameof(jpeg));
        var frame = new byte[HeaderSize + jpeg.Length];
        Magic.CopyTo(frame, 0);
        BitConverter.TryWriteBytes(frame.AsSpan(4), PictureCommand);
        BitConverter.TryWriteBytes(frame.AsSpan(8), width);
        BitConverter.TryWriteBytes(frame.AsSpan(12), height);
        frame[56] = 0x02;
        BitConverter.TryWriteBytes(frame.AsSpan(60), jpeg.Length);
        jpeg.CopyTo(frame.AsSpan(HeaderSize));
        return frame;
    }

    /// <summary>A transfer that ends exactly on a packet boundary needs a zero-length write to finish it.</summary>
    public static bool NeedsTerminator(int length) => length % PacketSize == 0;
}

public class ScreenNotFoundException(string message) : IOException(message);
public class ScreenBusyException(string message) : IOException(message);
public class ScreenUnsupportedException(string message) : IOException(message);
