namespace CoreVision.Engine;

/// <summary>How to greet a screen and send it pictures. Each USB model speaks its own format.</summary>
public interface IScreenFamily
{
    ScreenInfo Connect(IScreenTransport transport);
    byte[] BuildFrame(ScreenInfo screen, byte[] jpeg);
    void Send(IScreenTransport transport, byte[] frame);
}

/// <summary>How Windows talks to the screen: WinUSB bulk pipes, HID reports (no driver needed), SCSI commands to a USB drive (admin needed), or a USB serial port.</summary>
public enum ScreenTransportKind { WinUsb, Hid, Scsi, Serial }

/// <summary>A screen model TR Studio can drive: where it is on USB and which format it speaks.</summary>
public sealed record ScreenDevice(string Name, ushort VendorId, ushort ProductId, byte EndpointOut, IScreenFamily Family,
    ScreenTransportKind Transport = ScreenTransportKind.WinUsb)
{
    public const byte EndpointIn = 0x81;

    public static ScreenDevice CoreVision { get; } = new("Core Vision", ScreenProtocol.VendorId, ScreenProtocol.ProductId, 0x01, new CoreVisionFamily());

    /// <summary>Experimental: decoded from TRCC, untested on a real screen.</summary>
    public static ScreenDevice TrofeoVision { get; } = new("Trofeo Vision", 0x0416, 0x5408, 0x09, new TrofeoFamily());

    /// <summary>Experimental: TRCC's HID screens (Trofeo Vision LCD, Assassin Spirit 120 Vision, Frozen Warframe SE and others).</summary>
    public static ScreenDevice HidLcd { get; } = new("HID screen", 0x0416, 0x5302, 0, new HidLcdFamily(), ScreenTransportKind.Hid);

    /// <summary>Experimental: TRCC's small RGB565 screens, such as the LC-series pump heads.</summary>
    public static ScreenDevice AliLcd { get; } = new("320×320 screen", 0x0416, 0x5406, 0x02, new AliLcdFamily());

    /// <summary>Experimental: TRCC's SCSI screens on the Frozen Horizon Pro, Frozen Magic Pro and Frozen Vision V2.</summary>
    public static ScreenDevice FrozenScsi { get; } = new("Frozen Horizon Pro / Magic Pro / Vision V2 screen", 0x87CD, 0x70DB, 0, new ScsiLcdFamily(), ScreenTransportKind.Scsi);

    /// <summary>Experimental: TRCC's SCSI screens on the Frozen Warframe and Frozen Warframe 240.</summary>
    public static ScreenDevice WarframeScsi { get; } = new("Frozen Warframe screen", 0x0402, 0x3922, 0, new ScsiLcdFamily(), ScreenTransportKind.Scsi);

    /// <summary>Experimental: Lovingcool (Lovi.Cool) AIO screens, a USB serial port.</summary>

    public static ScreenDevice Lovingcool { get; } = new("Lovingcool screen", 0x33C3, 0x7788, 0, new LovingcoolFamily(), ScreenTransportKind.Serial);


    /// <summary>Tried in this order, so the tested Core Vision always wins when more than one is plugged in.</summary>
    public static IReadOnlyList<ScreenDevice> All { get; } = [CoreVision, TrofeoVision, HidLcd, AliLcd, FrozenScsi, WarframeScsi, Lovingcool];
}

sealed class CoreVisionFamily : IScreenFamily
{
    public ScreenInfo Connect(IScreenTransport transport)
    {
        var hello = ScreenProtocol.BuildHello();
        transport.Write(hello, hello.Length);
        var buffer = new byte[1024];
        var read = transport.Read(buffer);
        return ScreenProtocol.ParseHelloReply(buffer.AsSpan(0, read));
    }

    public byte[] BuildFrame(ScreenInfo screen, byte[] jpeg) => ScreenProtocol.BuildFrame(screen.HeaderWidth, screen.HeaderHeight, jpeg);

    public void Send(IScreenTransport transport, byte[] frame)
    {
        transport.Write(frame, frame.Length);
        if (ScreenProtocol.NeedsTerminator(frame.Length)) transport.Write([], 0);
    }
}

sealed class TrofeoFamily : IScreenFamily
{
    public ScreenInfo Connect(IScreenTransport transport)
    {
        var hello = TrofeoProtocol.BuildHello();
        transport.Write(hello, hello.Length);
        var reply = new byte[TrofeoProtocol.ReplySize];
        var read = transport.Read(reply);
        return TrofeoProtocol.ParseHelloReply(reply.AsSpan(0, read));
    }

    public byte[] BuildFrame(ScreenInfo screen, byte[] jpeg) => TrofeoProtocol.BuildFrame(jpeg);
    public void Send(IScreenTransport transport, byte[] frame) => TrofeoProtocol.Send(transport, frame);
}
