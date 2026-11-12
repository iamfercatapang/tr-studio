namespace CoreVision.Engine;

/// <summary>
/// A screen that Windows sees as a HID device, such as TRCC's 0416:5302 screens. HID needs no driver and no
/// administrator rights. Each write is one report: a zero report id, then up to <see cref="PayloadSize"/> bytes.
/// Reads give the report id first, as <see cref="HidDevice"/> does.
/// </summary>
public sealed class HidScreenTransport : IScreenTransport
{
    public const int PayloadSize = 512;

    readonly IHidLink _link;

    public HidScreenTransport(IHidLink link, ScreenDevice device)
    {
        _link = link;
        Device = device;
    }

    public ScreenDevice Device { get; }

    public static HidScreenTransport Open(ScreenDevice model)
    {
        var paths = HidDevice.Find(model.VendorId, model.ProductId);
        if (paths.Count == 0) throw new ScreenNotFoundException($"{model.Name} is not connected");
        foreach (var path in paths)
        {
            var device = HidDevice.Open(path);
            if (device == null) continue;
            // The screen's own interface takes 512-byte reports; anything smaller is another part of the device.
            if (device.OutputLength > PayloadSize) return new HidScreenTransport(device, model);
            device.Dispose();
        }
        throw new IOException($"the {model.Name} has no interface that takes {PayloadSize}-byte reports");
    }

    /// <summary>Drops answers left from earlier, so the next read is the reply to what is sent next.</summary>
    public void Flush() => (_link as HidDevice)?.Flush();

    public void Write(byte[] data, int length)
    {
        if (length > PayloadSize) throw new ArgumentException($"A HID screen report holds at most {PayloadSize} bytes.", nameof(length));
        var report = new byte[PayloadSize + 1];
        data.AsSpan(0, length).CopyTo(report.AsSpan(1));
        _link.Write(report);
    }

    public int Read(byte[] buffer)
    {
        var report = _link.Read();
        if (report == null) return 0;
        var length = Math.Min(report.Length, buffer.Length);
        report.AsSpan(0, length).CopyTo(buffer);
        return length;
    }

    public void Dispose() => _link.Dispose();
}

/// <summary>Opens a screen with the transport its model uses.</summary>
public static class ScreenOpener
{
    public static IScreenTransport Open(ScreenDevice model) => model.Transport switch
    {
        ScreenTransportKind.Hid => HidScreenTransport.Open(model),
        ScreenTransportKind.Scsi => ScsiScreenTransport.Open(model),
        _ => WinUsbDevice.Open(model),
    };
}
