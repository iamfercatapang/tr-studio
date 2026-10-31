namespace CoreVision.Engine;

public interface IScreenTransport : IDisposable
{
    void Write(byte[] data, int length);
    int Read(byte[] buffer);
}
