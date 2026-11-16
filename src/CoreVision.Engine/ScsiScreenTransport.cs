using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CoreVision.Engine;

/// <summary>Sends SCSI commands with a 16-byte CDB. Tests stand in for the drive here.</summary>
public interface IScsiLink : IDisposable
{
    /// <summary>A command that carries data to the device.</summary>
    void Send(ReadOnlySpan<byte> cdb, ReadOnlySpan<byte> data);

    /// <summary>A command that reads data from the device into the buffer.</summary>
    void Receive(ReadOnlySpan<byte> cdb, Span<byte> buffer);
}

/// <summary>
/// A screen that Windows sees as a USB drive, such as TRCC's 87CD:70DB and 0402:3922 screens. Commands go through
/// IOCTL_SCSI_PASS_THROUGH_DIRECT on the drive, which needs administrator rights (TR Studio runs as administrator).
/// </summary>
public sealed class ScsiScreenTransport : IScreenTransport
{
    readonly IScsiLink _link;

    public ScsiScreenTransport(IScsiLink link, ScreenDevice device)
    {
        _link = link;
        Device = device;
    }

    public ScreenDevice Device { get; }

    /// <summary>Set once the first frame of this session has gone out; TRCC sends a start command before that one.</summary>
    public bool Started { get; set; }

    public static ScsiScreenTransport Open(ScreenDevice model)
    {
        var paths = ScsiDisk.Find(model.VendorId, model.ProductId);
        if (paths.Count == 0) throw new ScreenNotFoundException($"{model.Name} is not connected");
        return new ScsiScreenTransport(ScsiDisk.Open(paths[0]), model);
    }

    public void Send(ReadOnlySpan<byte> cdb, ReadOnlySpan<byte> data) => _link.Send(cdb, data);
    public void Receive(ReadOnlySpan<byte> cdb, Span<byte> buffer) => _link.Receive(cdb, buffer);

    /// <summary>The first 16 bytes are the command, the rest is its data.</summary>
    public void Write(byte[] data, int length)
    {
        if (length < ScsiLcdProtocol.CdbSize) throw new ArgumentException("A SCSI write starts with a 16-byte command.", nameof(length));
        _link.Send(data.AsSpan(0, ScsiLcdProtocol.CdbSize), data.AsSpan(ScsiLcdProtocol.CdbSize, length - ScsiLcdProtocol.CdbSize));
    }

    /// <summary>A SCSI screen only answers a command; use <see cref="Receive"/>.</summary>
    public int Read(byte[] buffer) => throw new NotSupportedException("A SCSI screen answers only a command; use Receive.");

    public void Dispose() => _link.Dispose();
}

/// <summary>A USB drive opened by the vendor and product id of the USB device it belongs to.</summary>
public sealed class ScsiDisk : IScsiLink
{
    const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareReadWrite = 3, OpenExisting = 3;
    const uint DigcfPresent = 2, DigcfDeviceInterface = 0x10;
    const uint IoctlScsiPassThroughDirect = 0x4D014;
    const byte DataOut = 0, DataIn = 1;
    const uint TimeoutSeconds = 10;
    const int SenseSize = 32;
    static readonly Guid DiskInterface = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");

    readonly SafeFileHandle _handle;

    ScsiDisk(SafeFileHandle handle) => _handle = handle;

    /// <summary>Paths of the present disk interfaces whose USB device has this vendor and product id.</summary>
    public static IReadOnlyList<string> Find(ushort vendor, ushort product)
    {
        var match = $"VID_{vendor:X4}&PID_{product:X4}";
        var guid = DiskInterface;
        var paths = new List<string>();
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == new IntPtr(-1)) return paths;
        try
        {
            var data = new SpDeviceInterfaceData { Size = Marshal.SizeOf<SpDeviceInterfaceData>() };
            for (uint index = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data); index++)
            {
                var info = new SpDevinfoData { Size = Marshal.SizeOf<SpDevinfoData>() };
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
                var detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6); // cbSize of the detail struct
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, ref info)) continue;
                    var path = Marshal.PtrToStringUni(detail + 4);
                    if (path != null && BelongsTo(info.DevInst, match)) paths.Add(path);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return paths;
    }

    /// <summary>A disk's own id names the drive, not the USB device, so its parents are checked: storage, then USB.</summary>
    static bool BelongsTo(uint node, string match)
    {
        for (var level = 0; level < 4; level++)
        {
            if (CM_Get_Parent(out var parent, node, 0) != 0) return false;
            var id = new char[512];
            if (CM_Get_Device_ID(parent, id, id.Length, 0) != 0) return false;
            if (new string(id).TrimEnd('\0').Contains(match, StringComparison.OrdinalIgnoreCase)) return true;
            node = parent;
        }
        return false;
    }

    public static ScsiDisk Open(string path)
    {
        var handle = CreateFile(path, GenericRead | GenericWrite, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (!handle.IsInvalid) return new ScsiDisk(handle);
        var error = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new IOException(error == 5 ? "it needs TR Studio to run as administrator" : new Win32Exception(error).Message);
    }

    public void Send(ReadOnlySpan<byte> cdb, ReadOnlySpan<byte> data)
    {
        var copy = data.ToArray();
        Execute(cdb, copy, DataOut);
    }

    public void Receive(ReadOnlySpan<byte> cdb, Span<byte> buffer)
    {
        var copy = new byte[buffer.Length];
        Execute(cdb, copy, DataIn);
        copy.CopyTo(buffer);
    }

    void Execute(ReadOnlySpan<byte> cdb, byte[] data, byte direction)
    {
        var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var command = new PassThroughWithSense
            {
                Request = new ScsiPassThroughDirect
                {
                    Length = (ushort)Marshal.SizeOf<ScsiPassThroughDirect>(),
                    TargetId = 1, // as TRCC sets it; the disk driver fills in the real address
                    CdbLength = (byte)cdb.Length,
                    SenseInfoLength = SenseSize,
                    DataIn = direction,
                    DataTransferLength = (uint)data.Length,
                    TimeOutValue = TimeoutSeconds,
                    DataBuffer = pinned.AddrOfPinnedObject(),
                    SenseInfoOffset = (uint)Marshal.OffsetOf<PassThroughWithSense>(nameof(PassThroughWithSense.Sense)),
                    Cdb = new byte[16],
                },
                Sense = new byte[SenseSize],
            };
            cdb.CopyTo(command.Request.Cdb);
            var size = Marshal.SizeOf<PassThroughWithSense>();
            if (!DeviceIoControl(_handle, IoctlScsiPassThroughDirect, ref command, size, ref command, size, out _, IntPtr.Zero))
                throw new IOException($"the screen didn't take a command ({new Win32Exception(Marshal.GetLastWin32Error()).Message})");
            if (command.Request.ScsiStatus != 0)
                throw new IOException($"the screen refused a command (SCSI status {command.Request.ScsiStatus:X2}, sense key {command.Sense[2] & 0x0F:X})");
        }
        finally { pinned.Free(); }
    }

    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    struct ScsiPassThroughDirect
    {
        public ushort Length;
        public byte ScsiStatus, PathId, TargetId, Lun, CdbLength, SenseInfoLength, DataIn;
        public uint DataTransferLength, TimeOutValue;
        public IntPtr DataBuffer;
        public uint SenseInfoOffset;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] Cdb;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PassThroughWithSense
    {
        public ScsiPassThroughDirect Request;
        public uint Filler;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = SenseSize)] public byte[] Sense;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SpDeviceInterfaceData { public int Size; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct SpDevinfoData { public int Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle device, uint code, ref PassThroughWithSense input, int inputSize,
        ref PassThroughWithSense output, int outputSize, out int returned, IntPtr overlapped);
    [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetClassDevsW")]
    static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref SpDeviceInterfaceData data);
    [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, uint size, out uint required, IntPtr info);
    [DllImport("setupapi", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, uint size, out uint required, ref SpDevinfoData info);
    [DllImport("setupapi", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32")]
    static extern int CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_IDW")]
    static extern int CM_Get_Device_ID(uint node, [Out] char[] buffer, int length, uint flags);
}
