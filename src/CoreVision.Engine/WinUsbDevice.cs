using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace CoreVision.Engine;

/// <summary>Minimal WinUSB access to the cooler screen, with no third-party USB library.</summary>
public sealed class WinUsbDevice : IScreenTransport
{
    const byte EndpointIn = ScreenDevice.EndpointIn;
    const uint PipeTransferTimeout = 0x03;
    const int ErrorAccessDenied = 5, ErrorSharingViolation = 32, ErrorBusy = 170;

    readonly SafeFileHandle _file;
    readonly IntPtr _usb;
    readonly byte _endpointOut;

    WinUsbDevice(SafeFileHandle file, IntPtr usb, ScreenDevice device)
    {
        _file = file;
        _usb = usb;
        Device = device;
        _endpointOut = device.EndpointOut;
    }

    public ScreenDevice Device { get; }

    public static WinUsbDevice Open(ScreenDevice? model = null, uint timeoutMs = 2000)
    {
        model ??= ScreenDevice.CoreVision;
        var path = FindDevicePath(model.VendorId, model.ProductId);
        var file = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x80 | 0x40000000, IntPtr.Zero);
        if (file.IsInvalid) throw OpenFailure(Marshal.GetLastWin32Error());
        if (!WinUsb_Initialize(file, out var usb))
        {
            var error = Marshal.GetLastWin32Error();
            file.Dispose();
            throw OpenFailure(error);
        }
        var device = new WinUsbDevice(file, usb, model);
        foreach (var pipe in new[] { model.EndpointOut, EndpointIn })
            WinUsb_SetPipePolicy(usb, pipe, PipeTransferTimeout, sizeof(uint), ref timeoutMs);
        WinUsb_FlushPipe(usb, EndpointIn); // drop any reply left by an earlier session
        return device;
    }

    static IOException OpenFailure(int error) => error is ErrorAccessDenied or ErrorSharingViolation or ErrorBusy
        ? new ScreenBusyException("Another app is using the screen. Close TRCC to continue.")
        : new IOException(new Win32Exception(error).Message);

    public void Write(byte[] data, int length)
    {
        if (!WinUsb_WritePipe(_usb, _endpointOut, data, (uint)length, out var written, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "USB write failed");
        if (written != length) throw new IOException($"Short USB write: {written} of {length}");
    }

    public int Read(byte[] buffer)
    {
        if (!WinUsb_ReadPipe(_usb, EndpointIn, buffer, (uint)buffer.Length, out var read, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "USB read failed");
        return (int)read;
    }

    public void Dispose()
    {
        WinUsb_Free(_usb);
        _file.Dispose();
    }

    /// <summary>Paths of every USB device present, used to name displays that can't be opened yet.</summary>
    public static IReadOnlyList<string> PresentUsbPaths()
    {
        const uint DigcfPresent = 2, DigcfDeviceInterface = 0x10;
        var guid = new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");
        var paths = new List<string>();
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        try
        {
            var data = new SpDeviceInterfaceData { Size = Marshal.SizeOf<SpDeviceInterfaceData>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
                var detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero))
                        paths.Add(Marshal.PtrToStringUni(detail + 4)!);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
            return paths;
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    /// <summary>The interface GUID is device-specific, so it is read from the device's registry key.</summary>
    static string FindDevicePath(ushort vid, ushort pid)
    {
        var usbKey = $@"SYSTEM\CurrentControlSet\Enum\USB\VID_{vid:X4}&PID_{pid:X4}";
        using var root = Registry.LocalMachine.OpenSubKey(usbKey) ?? throw new ScreenNotFoundException("Screen has never been connected");
        foreach (var serial in root.GetSubKeyNames())
        {
            using var parameters = root.OpenSubKey($@"{serial}\Device Parameters");
            var guids = parameters?.GetValue("DeviceInterfaceGUIDs") as string[];
            var single = parameters?.GetValue("DeviceInterfaceGUID") as string;
            foreach (var text in (guids ?? []).Append(single))
            {
                if (string.IsNullOrEmpty(text)) continue;
                var path = FirstInterfacePath(Guid.Parse(text), vid, pid);
                if (path != null) return path;
            }
        }
        throw new ScreenNotFoundException("Screen is not connected");
    }

    static string? FirstInterfacePath(Guid guid, ushort vid, ushort pid)
    {
        const uint DigcfPresent = 2, DigcfDeviceInterface = 0x10;
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        try
        {
            var data = new SpDeviceInterfaceData { Size = Marshal.SizeOf<SpDeviceInterfaceData>() };
            var wanted = $"vid_{vid:x4}&pid_{pid:x4}";
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
                var detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero)) continue;
                    var path = Marshal.PtrToStringUni(detail + 4)!;
                    if (path.Contains(wanted, StringComparison.OrdinalIgnoreCase)) return path;
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
            return null;
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SpDeviceInterfaceData { public int Size; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

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
    [DllImport("winusb", SetLastError = true)]
    static extern bool WinUsb_Initialize(SafeFileHandle file, out IntPtr handle);
    [DllImport("winusb", SetLastError = true)]
    static extern bool WinUsb_Free(IntPtr handle);
    [DllImport("winusb", SetLastError = true)]
    static extern bool WinUsb_FlushPipe(IntPtr handle, byte pipe);
    [DllImport("winusb", SetLastError = true)]
    static extern bool WinUsb_SetPipePolicy(IntPtr handle, byte pipe, uint policy, uint length, ref uint value);
    [DllImport("winusb", SetLastError = true)]
    static extern bool WinUsb_WritePipe(IntPtr handle, byte pipe, byte[] buffer, uint length, out uint written, IntPtr overlapped);
    [DllImport("winusb", SetLastError = true)]
    static extern bool WinUsb_ReadPipe(IntPtr handle, byte pipe, byte[] buffer, uint length, out uint read, IntPtr overlapped);
}
