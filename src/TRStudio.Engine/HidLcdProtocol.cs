using System.Drawing;
using System.Drawing.Imaging;

namespace TRStudio.Engine;

/// <summary>
/// Wire format of TRCC's HID screens (USB 0416:5302): Trofeo Vision LCD, Assassin Spirit 120 Vision,
/// Frozen Warframe SE/360/Pro, AS120 and BA120 Vision, and screens from other brands built on the same
/// module. Decoded from TRCC 2.1.4 (TRCC.exe, UCDevice and FormCZTV) and not yet tried on real hardware.
/// See docs/PROTOCOL.md.
/// </summary>
public static class HidLcdProtocol
{
    public const int HeaderSize = 20;
    static readonly byte[] Magic = [0xDA, 0xDB, 0xDC, 0xDD];
    const byte HelloReply = 1;

    /// <summary>The kind of header a frame starts with, which TRCC picks by the screen's size.</summary>
    public enum Header { Hid, CoreVision }

    /// <summary>
    /// A screen size picked by the mode byte the screen reports. Portrait: the screen wants the picture
    /// turned on its side, which TRCC does for its 320x240 panels, whose header names them 240 wide.
    /// </summary>
    public sealed record Panel(int Width, int Height, Header Header, bool Portrait = false);

    public static byte[] BuildHello()
    {
        var hello = new byte[HeaderSize];
        Magic.CopyTo(hello, 0);
        hello[12] = 0x01;
        return hello;
    }

    /// <summary>TRCC's size table for these screens. Mode 49 sends raw pixels instead of JPEG, which isn't supported yet.</summary>
    public static Panel Size(int mode) => mode switch
    {
        72 or 129 => new(480, 480, Header.Hid),
        54 => new(360, 360, Header.Hid),
        128 => new(1280, 480, Header.Hid),
        100 or 101 or 102 => new(320, 320, Header.CoreVision),
        64 => new(640, 480, Header.CoreVision),
        36 or 37 => new(240, 240, Header.CoreVision),
        49 => throw new ScreenUnsupportedException("This screen uses a raw pixel format (mode 49) that isn't supported yet."),
        // Mode 58 switches TRCC to the other 320x240 header, which keeps the picture landscape.
        58 => new(320, 240, Header.CoreVision),
        _ => new(320, 240, Header.Hid, Portrait: true),
    };

    /// <summary>The reply starts with the report id, then the magic. Byte 6 is TRCC's mode (byte 5, its sub mode, matters to no size here).</summary>
    public static ScreenInfo ParseHelloReply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 14 || !reply.Slice(1, 4).SequenceEqual(Magic) || reply[13] != HelloReply)
            throw new InvalidDataException("The screen sent an unexpected handshake reply.");
        int mode = reply[6];
        var serial = reply.Length >= 37 && reply[17] == 16 ? Convert.ToHexString(reply.Slice(21, 16)) : "";
        var panel = Size(mode);
        return new ScreenInfo($"HID {serial}".Trim(), mode, panel.Width, panel.Height)
        {
            HeaderWidth = panel.Portrait ? panel.Height : panel.Width,
            HeaderHeight = panel.Portrait ? panel.Width : panel.Height,
            ModelName = $"{panel.Width}×{panel.Height} Screen (experimental)",
        };
    }

    /// <summary>The 20-byte header: magic, command 2, width and height as 16-bit numbers, 2, then the JPEG's length.</summary>
    public static byte[] BuildFrame(ScreenInfo screen, byte[] jpeg)
    {
        var panel = Size(screen.Mode);
        if (panel.Header == Header.CoreVision) return ScreenProtocol.BuildFrame(screen.HeaderWidth, screen.HeaderHeight, jpeg);
        if (panel.Portrait) jpeg = Turn(jpeg, screen.JpegQuality);
        if (jpeg.Length >= ScreenProtocol.MaxJpegBytes) throw new ArgumentException("JPEG is too large for the screen.", nameof(jpeg));
        var frame = new byte[HeaderSize + jpeg.Length];
        Magic.CopyTo(frame, 0);
        frame[4] = 0x02;
        BitConverter.TryWriteBytes(frame.AsSpan(8), (ushort)screen.HeaderWidth);
        BitConverter.TryWriteBytes(frame.AsSpan(10), (ushort)screen.HeaderHeight);
        frame[12] = 0x02;
        BitConverter.TryWriteBytes(frame.AsSpan(16), jpeg.Length);
        jpeg.CopyTo(frame, HeaderSize);
        return frame;
    }

    /// <summary>Sends the frame as 512-byte reports, as TRCC does. The screen's answers aren't waited for.</summary>
    public static void Send(IScreenTransport transport, byte[] frame)
    {
        for (var offset = 0; offset < frame.Length; offset += HidScreenTransport.PayloadSize)
        {
            var length = Math.Min(HidScreenTransport.PayloadSize, frame.Length - offset);
            transport.Write(frame[offset..(offset + length)], length);
        }
    }

    static byte[] Turn(byte[] jpeg, long quality)
    {
        using var input = new MemoryStream(jpeg);
        using var bitmap = new Bitmap(input);
        bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);
        return JpegFrame.Encode(bitmap, quality);
    }
}

sealed class HidLcdFamily : IScreenFamily
{
    public ScreenInfo Connect(IScreenTransport transport)
    {
        (transport as HidScreenTransport)?.Flush();
        var hello = HidLcdProtocol.BuildHello();
        transport.Write(hello, hello.Length);
        var reply = new byte[HidScreenTransport.PayloadSize + 1];
        // Answers to earlier frames can still be queued; the hello reply is the one marked as such.
        for (var tries = 0; tries < 8; tries++)
        {
            var read = transport.Read(reply);
            if (read == 0) break;
            if (read >= 14 && reply[13] == 1) return HidLcdProtocol.ParseHelloReply(reply.AsSpan(0, read));
        }
        throw new InvalidDataException("The screen didn't answer the handshake.");
    }

    public byte[] BuildFrame(ScreenInfo screen, byte[] jpeg) => HidLcdProtocol.BuildFrame(screen, jpeg);
    public void Send(IScreenTransport transport, byte[] frame) => HidLcdProtocol.Send(transport, frame);
}
