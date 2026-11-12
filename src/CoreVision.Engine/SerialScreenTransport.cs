using System.IO.Ports;
using Microsoft.Win32;

namespace CoreVision.Engine;

/// <summary>
/// A screen that Windows sees as a USB serial (COM) port, such as Lovingcool's. Found by its USB ids through
/// the port name Windows keeps in the registry, so no driver or administrator rights are involved.
/// </summary>
public sealed class SerialScreenTransport : IScreenTransport
{
    public const int BaudRate = 2_000_000;
    const int ReadTimeoutMs = 1000, WriteTimeoutMs = 3000;

    readonly SerialPort _port;

    SerialScreenTransport(SerialPort port, ScreenDevice device)
    {
        _port = port;
        Device = device;
    }

    public ScreenDevice Device { get; }

    public static SerialScreenTransport Open(ScreenDevice model)
    {
        var present = SerialPort.GetPortNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = PortNames(model.VendorId, model.ProductId).FirstOrDefault(present.Contains)
            ?? throw new ScreenNotFoundException($"{model.Name} is not connected");
        var port = new SerialPort(name, BaudRate) { DtrEnable = true, ReadTimeout = ReadTimeoutMs, WriteTimeout = WriteTimeoutMs };
        try { port.Open(); }
        catch (UnauthorizedAccessException)
        {
            port.Dispose();
            throw new ScreenBusyException("Another app is using the screen. Close the cooler's own app to continue.");
        }
        // The screen wants a moment after DTR goes up, and may have sent something before we were listening.
        Thread.Sleep(200);
        port.DiscardInBuffer();
        return new SerialScreenTransport(port, model);
    }

    /// <summary>The COM port names Windows gave this USB device, on the device itself or on one of its interfaces.</summary>
    public static IEnumerable<string> PortNames(ushort vid, ushort pid)
    {
        using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
        if (usb == null) yield break;
        var prefix = $"VID_{vid:X4}&PID_{pid:X4}";
        foreach (var device in usb.GetSubKeyNames().Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            using var key = usb.OpenSubKey(device);
            foreach (var instance in key?.GetSubKeyNames() ?? [])
            {
                using var parameters = key!.OpenSubKey($@"{instance}\Device Parameters");
                if (parameters?.GetValue("PortName") is string name && name.Length > 0) yield return name;
            }
        }
    }

    public void Write(byte[] data, int length)
    {
        try { _port.Write(data, 0, length); }
        catch (TimeoutException e) { throw new IOException("The screen didn't take the data in time.", e); }
        catch (InvalidOperationException e) { throw new IOException(e.Message, e); }
    }

    /// <summary>Whatever has arrived, waiting up to a second for the first byte. Zero when nothing comes.</summary>
    public int Read(byte[] buffer)
    {
        try { return _port.Read(buffer, 0, buffer.Length); }
        catch (TimeoutException) { return 0; }
        catch (InvalidOperationException e) { throw new IOException(e.Message, e); }
    }

    public void Dispose() => _port.Dispose();
}
