namespace TRStudio.Engine;

public interface IScreenTransport : IDisposable
{
    void Write(byte[] data, int length);
    int Read(byte[] buffer);

    /// <summary>The model on the other end, which decides the wire format.</summary>
    ScreenDevice Device => ScreenDevice.CoreVision;
}
