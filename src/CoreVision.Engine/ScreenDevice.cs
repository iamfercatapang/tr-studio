namespace CoreVision.Engine;

/// <summary>How to greet a screen and send it pictures. Each USB model speaks its own format.</summary>
public interface IScreenFamily
{
    ScreenInfo Connect(IScreenTransport transport);
    byte[] BuildFrame(ScreenInfo screen, byte[] jpeg);
    void Send(IScreenTransport transport, byte[] frame);
}

/// <summary>A screen model TR Studio can drive: where it is on USB and which format it speaks.</summary>
public sealed record ScreenDevice(string Name, ushort VendorId, ushort ProductId, byte EndpointOut, IScreenFamily Family)
{
    public const byte EndpointIn = 0x81;

    public static ScreenDevice CoreVision { get; } = new("Core Vision", ScreenProtocol.VendorId, ScreenProtocol.ProductId, 0x01, new CoreVisionFamily());

    /// <summary>Experimental: decoded from TRCC, untested on a real screen.</summary>
    public static ScreenDevice TrofeoVision { get; } = new("Trofeo Vision", 0x0416, 0x5408, 0x09, new TrofeoFamily());

    /// <summary>Tried in this order, so the tested Core Vision always wins when both are plugged in.</summary>
    public static IReadOnlyList<ScreenDevice> All { get; } = [CoreVision, TrofeoVision];
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
