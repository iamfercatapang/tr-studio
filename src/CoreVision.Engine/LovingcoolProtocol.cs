using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoreVision.Engine;

/// <summary>
/// Wire format of Lovingcool (Lovi.Cool) AIO screens, USB 33C3:7788, a USB serial port at 2,000,000 baud.
/// Written from the protocol facts documented by the lovi-ctrl project (github.com/newan0805/loving_cool_aio_display_manage),
/// which reverse engineered the vendor's app; no code was taken from it. Not yet tried on real hardware here.
/// </summary>
public static partial class LovingcoolProtocol
{
    public const byte GetInfo = 0x06, StartLive = 0x11;
    public const int MaxJpegBytes = 50 * 1024;
    const int Overhead = 7; // 55 AA, two length bytes, the command, two checksum bytes
    const double WrappedFromVersion = 2.8;

    /// <summary>55 AA, the whole command's length (payload + 7, little-endian), the command, the payload, then the sum of everything before it.</summary>
    public static byte[] Command(byte key, ReadOnlySpan<byte> payload = default)
    {
        var command = new byte[payload.Length + Overhead];
        command[0] = 0x55;
        command[1] = 0xAA;
        BitConverter.TryWriteBytes(command.AsSpan(2), (ushort)command.Length);
        command[4] = key;
        payload.CopyTo(command.AsSpan(5));
        BitConverter.TryWriteBytes(command.AsSpan(command.Length - 2), Sum(command.AsSpan(0, command.Length - 2)));
        return command;
    }

    /// <summary>
    /// The info reply is a command frame whose payload is JSON such as
    /// {"width":480,"height":320,"model":"txm818-7inch","version":"Ver1.0"}, sometimes inside a "data" field.
    /// </summary>
    public static ScreenInfo ParseInfo(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < Overhead || reply[0] != 0x55 || reply[1] != 0xAA)
            throw new InvalidDataException("The screen sent an unexpected reply.");
        var length = BitConverter.ToUInt16(reply[2..]);
        if (length < Overhead || length > reply.Length) throw new InvalidDataException("The screen's reply was cut short.");
        JsonElement info;
        try
        {
            using var json = JsonDocument.Parse(Encoding.UTF8.GetString(reply[5..(length - 2)]));
            info = (json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object ? data : json.RootElement).Clone();
        }
        catch (JsonException e) { throw new InvalidDataException("The screen's info wasn't readable.", e); }
        if (!info.TryGetProperty("width", out var w) || !info.TryGetProperty("height", out var h) || !w.TryGetInt32(out var width) || !h.TryGetInt32(out var height)
            || width is < 16 or > 4096 || height is < 16 or > 4096)
            throw new InvalidDataException("The screen didn't report its size.");
        var model = info.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "";
        var version = info.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
        return new ScreenInfo($"{model} {version}".Trim(), Wrapped(version) ? 1 : 0, width, height)
        {
            ModelName = $"Lovingcool {width}×{height} Screen (experimental)",
            JpegQuality = 85,
        };
    }

    /// <summary>Firmware above 2.8 wants each picture wrapped with its length and a checksum, read as lovi-ctrl reads it.</summary>
    public static bool Wrapped(string version)
    {
        var number = Number().Match(version);
        return number.Success && double.TryParse(number.Value.TrimEnd('.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > WrappedFromVersion;
    }

    /// <summary>The JPEG as is for older firmware; otherwise its length (4 bytes), the JPEG, and the sum of both (2 bytes).</summary>
    public static byte[] BuildFrame(ScreenInfo screen, byte[] jpeg)
    {
        jpeg = FitSize(jpeg, screen.JpegQuality);
        if (screen.Mode == 0) return jpeg;
        var frame = new byte[4 + jpeg.Length + 2];
        BitConverter.TryWriteBytes(frame, jpeg.Length);
        jpeg.CopyTo(frame, 4);
        BitConverter.TryWriteBytes(frame.AsSpan(frame.Length - 2), Sum(frame.AsSpan(0, frame.Length - 2)));
        return frame;
    }

    /// <summary>The screen takes at most 50 KB a picture, so a larger one is encoded again at lower quality.</summary>
    static byte[] FitSize(byte[] jpeg, int quality)
    {
        if (jpeg.Length <= MaxJpegBytes) return jpeg;
        using var input = new MemoryStream(jpeg);
        using var bitmap = new Bitmap(input);
        for (var q = Math.Min(quality, 80); q >= 10; q -= 10)
        {
            var smaller = JpegFrame.Encode(bitmap, q);
            if (smaller.Length <= MaxJpegBytes) return smaller;
        }
        throw new InvalidOperationException("Frame is too detailed to fit the screen's limit.");
    }

    static ushort Sum(ReadOnlySpan<byte> bytes)
    {
        var sum = 0;
        foreach (var b in bytes) sum += b;
        return (ushort)sum;
    }

    [GeneratedRegex(@"\d+(\.\d+)?")]
    private static partial Regex Number();
}

sealed class LovingcoolFamily : IScreenFamily
{
    public ScreenInfo Connect(IScreenTransport transport)
    {
        var ask = LovingcoolProtocol.Command(LovingcoolProtocol.GetInfo);
        transport.Write(ask, ask.Length);
        var screen = LovingcoolProtocol.ParseInfo(ReadReply(transport));
        var start = LovingcoolProtocol.Command(LovingcoolProtocol.StartLive);
        transport.Write(start, start.Length);
        return screen;
    }

    /// <summary>A serial port hands over bytes as they come, so the reply is gathered until its stated length is in.</summary>
    static byte[] ReadReply(IScreenTransport transport)
    {
        var reply = new List<byte>();
        var buffer = new byte[4096];
        while (true)
        {
            var read = transport.Read(buffer);
            if (read == 0) throw new InvalidDataException("The screen didn't answer.");
            reply.AddRange(buffer.AsSpan(0, read));
            var start = reply.IndexOf(0x55);
            if (start > 0) reply.RemoveRange(0, start);
            if (reply.Count >= 4 && reply[1] == 0xAA && reply.Count >= BitConverter.ToUInt16([reply[2], reply[3]])) return [.. reply];
            if (reply.Count > 65536) throw new InvalidDataException("The screen's reply was too long.");
        }
    }

    public byte[] BuildFrame(ScreenInfo screen, byte[] jpeg) => LovingcoolProtocol.BuildFrame(screen, jpeg);
    public void Send(IScreenTransport transport, byte[] frame) => transport.Write(frame, frame.Length);
}
