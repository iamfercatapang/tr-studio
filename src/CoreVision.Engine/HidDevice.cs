using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CoreVision.Engine;

/// <summary>A device that takes fixed-size packets and sometimes answers one. Tests stand in for the hardware here.</summary>
public interface IHidLink : IDisposable
{
    void Write(ReadOnlySpan<byte> report);

    /// <summary>The next packet from the device, or null when none comes in time. Its first byte is the report id.</summary>
    byte[]? Read(int timeoutMs = 1000);
}

/// <summary>
/// A USB HID device opened by vendor and product id, such as a HID cooler screen.
/// Windows lets any program talk to these, so no driver and no administrator rights are involved.
/// </summary>
public sealed class HidDevice : IHidLink
{
    const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareReadWrite = 3, OpenExisting = 3, Overlapped = 0x40000000;
    const uint DigcfPresent = 2, DigcfDeviceInterface = 0x10;
    const int HidpStatusSuccess = 0x00110000;

    readonly SafeFileHandle _handle;
    readonly FileStream _stream;
    readonly int _inputLength;

    HidDevice(SafeFileHandle handle, string path, int inputLength, int outputLength)
    {
        _handle = handle;
        _stream = new FileStream(handle, FileAccess.ReadWrite, 0, isAsync: true);
        _inputLength = inputLength;
        Path = path;
        OutputLength = outputLength;
    }

    public string Path { get; }

    /// <summary>The size of a packet to the device, counting the report id in its first byte.</summary>
    public int OutputLength { get; }

    /// <summary>Paths of the present HID interfaces with this vendor and product id.</summary>
    public static IReadOnlyList<string> Find(ushort vendor, ushort product)
    {
        HidD_GetHidGuid(out var guid);
        var match = $"vid_{vendor:x4}&pid_{product:x4}";
        var paths = new List<string>();
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == new IntPtr(-1)) return paths;
        try
        {
            var data = new SpDeviceInterfaceData { Size = Marshal.SizeOf<SpDeviceInterfaceData>() };
            for (uint index = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data); index++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
                var detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6); // cbSize of the detail struct
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero)) continue;
                    var path = Marshal.PtrToStringUni(detail + 4);
                    if (path != null && path.Contains(match, StringComparison.OrdinalIgnoreCase)) paths.Add(path);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return paths;
    }

    /// <summary>Null when the interface can't be opened or isn't one that takes packets.</summary>
    public static HidDevice? Open(string path)
    {
        var handle = CreateFile(path, GenericRead | GenericWrite, ShareReadWrite, IntPtr.Zero, OpenExisting, Overlapped, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); return null; }
        if (!HidD_GetPreparsedData(handle, out var parsed)) { handle.Dispose(); return null; }
        try
        {
            if (HidP_GetCaps(parsed, out var caps) != HidpStatusSuccess || caps.OutputReportByteLength == 0) { handle.Dispose(); return null; }
            return new HidDevice(handle, path, caps.InputReportByteLength, caps.OutputReportByteLength);
        }
        finally { HidD_FreePreparsedData(parsed); }
    }

    /// <summary>
    /// Sends one packet. A device that doesn't take it in time (a controller still coming back after the PC wakes)
    /// fails with an IOException instead of holding the caller forever.
    /// </summary>
    public void Write(ReadOnlySpan<byte> report)
    {
        const int timeoutMs = 500;
        var packet = new byte[OutputLength];
        report[..Math.Min(report.Length, packet.Length)].CopyTo(packet);
        using var timeout = new CancellationTokenSource(timeoutMs);
        try
        {
            _stream.WriteAsync(packet, timeout.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException e) { throw new IOException($"the device didn't take a packet within {timeoutMs} ms", e); }
        catch (Exception e) when (e is UnauthorizedAccessException or ObjectDisposedException or NotSupportedException) { throw new IOException(e.Message, e); }
    }

    public byte[]? Read(int timeoutMs = 1000)
    {
        if (_inputLength == 0) return null;
        var packet = new byte[_inputLength];
        using var timeout = new CancellationTokenSource(timeoutMs);
        try
        {
            var read = _stream.ReadAsync(packet, timeout.Token).AsTask().GetAwaiter().GetResult();
            return read > 0 ? packet : null;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception e) when (e is UnauthorizedAccessException or ObjectDisposedException or NotSupportedException) { throw new IOException(e.Message, e); }
    }

    /// <summary>Throws away answers left over from an earlier request, so the next one read is the one asked for.</summary>
    public void Flush() => HidD_FlushQueue(_handle);

    public void Dispose()
    {
        _stream.Dispose();
        _handle.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SpDeviceInterfaceData { public int Size; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct HidCaps
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort LinkCollectionNodes, InputButtonCaps, InputValueCaps, InputDataIndices, OutputButtonCaps, OutputValueCaps, OutputDataIndices,
            FeatureButtonCaps, FeatureValueCaps, FeatureDataIndices;
    }

    [DllImport("hid")]
    static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid", SetLastError = true)]
    static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr parsed);
    [DllImport("hid")]
    static extern bool HidD_FreePreparsedData(IntPtr parsed);
    [DllImport("hid")]
    static extern int HidP_GetCaps(IntPtr parsed, out HidCaps caps);
    [DllImport("hid", SetLastError = true)]
    static extern bool HidD_FlushQueue(SafeFileHandle device);
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetClassDevsW")]
    static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref SpDeviceInterfaceData data);
    [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, uint size, out uint required, IntPtr info);
    [DllImport("setupapi", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
