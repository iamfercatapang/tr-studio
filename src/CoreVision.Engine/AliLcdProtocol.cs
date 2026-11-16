using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CoreVision.Engine;

/// <summary>
/// Wire format of TRCC's "ALi" screens (USB 0416:5406), small 320x320 and 320x240 panels such as the LC-series
/// pump heads. They take raw RGB565 pixels rather than JPEG. Decoded from TRCC 2.1.4 (USBLCDNEW.dll,
/// ThreadSendDeviceDataALi, and FormCZTV.ImageTo565 for the pixel order) and not yet tried on real hardware.
/// </summary>
public static class AliLcdProtocol
{
    public const int HeaderSize = 16;
    public const int HelloPayload = 1024;
    public const int ReplySize = 1024;
    public const int AckSize = 16;
    static readonly byte[] HelloHeader = [0xF5, 0x00, 0x01, 0x00, 0xBC, 0xFF, 0xB6, 0xC8, 0, 0, 0, 0, 0x00, 0x04, 0x00, 0x00];
    static readonly byte[] FrameHeader = [0xF5, 0x01, 0x01, 0x00, 0xBC, 0xFF, 0xB6, 0xC8, 0, 0, 0, 0];
    const byte Landscape320x240 = 54;

    /// <summary>The 16-byte hello, which announces 1024 bytes, followed by those 1024 zero bytes.</summary>
    public static byte[] BuildHello()
    {
        var hello = new byte[HeaderSize + HelloPayload];
        HelloHeader.CopyTo(hello, 0);
        return hello;
    }

    /// <summary>The first byte is TRCC's mode plus one: 54 is a 320x240 panel, 101 and 102 are 320x320.</summary>
    public static ScreenInfo ParseHelloReply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 14 || reply[0] is not (54 or 101 or 102))
            throw new InvalidDataException("The screen sent an unexpected handshake reply.");
        var (width, height) = reply[0] == Landscape320x240 ? (320, 240) : (320, 320);
        return new ScreenInfo($"ALi {Convert.ToHexString(reply.Slice(10, 4))}", reply[0] - 1, width, height)
        {
            ModelName = $"{width}×{height} Screen (experimental)",
        };
    }

    /// <summary>The 16-byte header ends with the pixel data's length; the pixels follow, two bytes each.</summary>
    public static byte[] BuildFrame(ScreenInfo screen, byte[] jpeg)
    {
        using var input = new MemoryStream(jpeg);
        using var bitmap = new Bitmap(input);
        var pixels = Rgb565.Encode(bitmap, screen.Width, screen.Height);
        var frame = new byte[HeaderSize + pixels.Length];
        FrameHeader.CopyTo(frame, 0);
        BitConverter.TryWriteBytes(frame.AsSpan(12), pixels.Length);
        pixels.CopyTo(frame, HeaderSize);
        return frame;
    }

    /// <summary>One write for the whole frame, then the screen's 16-byte answer.</summary>
    public static void Send(IScreenTransport transport, byte[] frame)
    {
        transport.Write(frame, frame.Length);
        transport.Read(new byte[AckSize]);
    }
}

/// <summary>16-bit color, five bits of red, six of green and five of blue, high byte first as TRCC sends it to these panels.</summary>
public static class Rgb565
{
    public static byte[] Encode(Bitmap source, int width, int height)
    {
        using var sized = source.Width == width && source.Height == height ? null : new Bitmap(source, width, height);
        var bitmap = sized ?? source;
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bgra = new byte[data.Stride * height];
            Marshal.Copy(data.Scan0, bgra, 0, bgra.Length);
            var output = new byte[width * height * 2];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var at = y * data.Stride + x * 4;
                    var to = (y * width + x) * 2;
                    (output[to], output[to + 1]) = Pack(bgra[at + 2], bgra[at + 1], bgra[at]);
                }
            }
            return output;
        }
        finally { bitmap.UnlockBits(data); }
    }

    public static (byte High, byte Low) Pack(byte red, byte green, byte blue) =>
        ((byte)((red & 0xF8) | (green >> 5)), (byte)(((green << 3) & 0xE0) | (blue >> 3)));
}

sealed class AliLcdFamily : IScreenFamily
{
    public ScreenInfo Connect(IScreenTransport transport)
    {
        var hello = AliLcdProtocol.BuildHello();
        transport.Write(hello, hello.Length);
        var reply = new byte[AliLcdProtocol.ReplySize];
        var read = transport.Read(reply);
        return AliLcdProtocol.ParseHelloReply(reply.AsSpan(0, read));
    }

    public byte[] BuildFrame(ScreenInfo screen, byte[] jpeg) => AliLcdProtocol.BuildFrame(screen, jpeg);
    public void Send(IScreenTransport transport, byte[] frame) => AliLcdProtocol.Send(transport, frame);
}
