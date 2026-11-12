// SPDX-License-Identifier: GPL-2.0-or-later
//
// I2C access to an NVIDIA graphics card's own bus through NvAPI, the library every NVIDIA driver installs.
//
// Ported to C# from the OpenRGB project (https://gitlab.com/CalcProgrammer1/OpenRGB), files
// i2c_smbus/Windows/i2c_smbus_nvapi.cpp by Adam Honse (CalcProgrammer1) and dependencies/NVFC/nvapi.cpp
// and nvapi.h (the interface ids and the NV_I2C_INFO_V3 layout), and the OpenRGB contributors, licensed
// GPL-2.0-or-later.

using System.Runtime.InteropServices;

namespace TRStudio.Engine;

/// <summary>An NVIDIA card and the four PCI ids that tell its exact model.</summary>
public sealed record NvGpu(int Index, IntPtr Handle, ushort Vendor, ushort Device, ushort SubVendor, ushort SubDevice)
{
    public string Ids => $"{Vendor:X4}:{Device:X4} {SubVendor:X4}:{SubDevice:X4}";
}

/// <summary>
/// The I2C bus on an NVIDIA card's port 1, where board makers put their lighting chips. No driver and no
/// administrator rights are needed. Reads give -1 when nothing answers; writes give false.
/// </summary>
public sealed class NvApiI2cBus : IRawI2cBus
{
    const int StatusOk = 0, MaxBlock = 32, LightingPort = 1;

    // NV_I2C_INFO_V3 on 64-bit Windows: 64 bytes, version = size | (3 << 16).
    [StructLayout(LayoutKind.Sequential)]
    struct I2cInfo
    {
        public uint Version;
        public uint DisplayMask;
        public byte IsDdcPort;
        public byte DeviceAddress;
        public IntPtr RegisterAddress;
        public uint RegisterAddressSize;
        public IntPtr Data;
        public uint Size;
        public uint Speed;
        public uint SpeedKhz;
        public byte PortId;
        public uint IsPortIdSet;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr QueryInterface(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Initialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int EnumPhysicalGpus([Out] IntPtr[] handles, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetPciIdentifiers(IntPtr gpu, out uint device, out uint subSystem, out uint revision, out uint extDevice);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int I2cTransfer(IntPtr gpu, ref I2cInfo info, out uint unknown);

    static readonly object Load = new();
    static I2cTransfer? _write, _read;
    static EnumPhysicalGpus? _enum;
    static GetPciIdentifiers? _ids;

    readonly IntPtr _gpu;
    readonly object _gate = new();

    public NvApiI2cBus(NvGpu gpu)
    {
        _gpu = gpu.Handle;
        Name = $"NvAPI I2C on GPU {gpu.Index} ({gpu.Ids})";
    }

    public string Name { get; }

    /// <summary>Every NVIDIA card in the PC with its PCI ids. Empty when there is no NVIDIA driver.</summary>
    public static IReadOnlyList<NvGpu> Gpus()
    {
        if (!Ready()) return [];
        var handles = new IntPtr[64];
        if (_enum!(handles, out var count) != StatusOk) return [];
        var gpus = new List<NvGpu>();
        for (var i = 0; i < count; i++)
        {
            if (_ids!(handles[i], out var device, out var subSystem, out _, out _) != StatusOk) continue;
            gpus.Add(new NvGpu(i, handles[i], (ushort)(device & 0xFFFF), (ushort)(device >> 16), (ushort)(subSystem & 0xFFFF), (ushort)(subSystem >> 16)));
        }
        return gpus;
    }

    static bool Ready()
    {
        lock (Load)
        {
            if (_write != null) return true;
            if (!NativeLibrary.TryLoad("nvapi64.dll", out var library)) return false;
            if (!NativeLibrary.TryGetExport(library, "nvapi_QueryInterface", out var queryAddress)) return false;
            var query = Marshal.GetDelegateForFunctionPointer<QueryInterface>(queryAddress);
            T? Get<T>(uint id) where T : Delegate => query(id) is var p && p != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<T>(p) : null;
            var initialize = Get<Initialize>(0x0150E828);
            var enumerate = Get<EnumPhysicalGpus>(0xE5AC921F);
            var ids = Get<GetPciIdentifiers>(0x2DDFB66E);
            var write = Get<I2cTransfer>(0x283AC65A);
            var read = Get<I2cTransfer>(0x4D7B0709);
            if (initialize == null || enumerate == null || ids == null || write == null || read == null || initialize() != StatusOk) return false;
            (_enum, _ids, _read, _write) = (enumerate, ids, read, write);
            return true;
        }
    }

    /// <summary>NvAPI has no lock shared between programs, so this only keeps TR Studio's own requests in one piece.</summary>
    public IDisposable Hold()
    {
        Monitor.Enter(_gate);
        return new Release(_gate);
    }

    sealed class Release(object gate) : IDisposable
    {
        public void Dispose() => Monitor.Exit(gate);
    }

    public int ReadByte(byte address)
    {
        var data = new byte[1];
        return Transfer(address, null, data, write: false) ? data[0] : -1;
    }

    public int ReadByteData(byte address, byte command)
    {
        var data = new byte[1];
        return Transfer(address, command, data, write: false) ? data[0] : -1;
    }

    public bool WriteByteData(byte address, byte command, byte value) => Transfer(address, command, [value], write: true);

    public bool WriteWordData(byte address, byte command, ushort value) => Transfer(address, command, [(byte)value, (byte)(value >> 8)], write: true);

    /// <summary>An SMBus block: its length first, then the bytes.</summary>
    public bool WriteBlockData(byte address, byte command, ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxBlock) return false;
        var block = new byte[data.Length + 1];
        block[0] = (byte)data.Length;
        data.CopyTo(block.AsSpan(1));
        return Transfer(address, command, block, write: true);
    }

    /// <summary>Raw I2C bytes with no register address in front, as some chips want for longer writes.</summary>
    public bool WriteRaw(byte address, ReadOnlySpan<byte> data) => Transfer(address, null, data.ToArray(), write: true);

    /// <summary>Raw I2C bytes read with no register address in front.</summary>
    public bool ReadRaw(byte address, Span<byte> data)
    {
        var buffer = new byte[data.Length];
        if (!Transfer(address, null, buffer, write: false)) return false;
        buffer.CopyTo(data);
        return true;
    }

    /// <summary>An I2C block: the register, then the bytes, with no length byte.</summary>
    public bool WriteI2cBlockData(byte address, byte command, ReadOnlySpan<byte> data) => Transfer(address, command, data.ToArray(), write: true);

    bool Transfer(byte address, byte? command, byte[] data, bool write)
    {
        if (!Ready()) return false;
        lock (_gate)
        {
            var register = GCHandle.Alloc(new[] { command ?? 0 }, GCHandleType.Pinned);
            var buffer = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var info = new I2cInfo
                {
                    Version = (uint)Marshal.SizeOf<I2cInfo>() | (3u << 16),
                    DeviceAddress = (byte)(address << 1),
                    RegisterAddress = command == null ? IntPtr.Zero : register.AddrOfPinnedObject(),
                    RegisterAddressSize = command == null ? 0u : 1u,
                    Data = buffer.AddrOfPinnedObject(),
                    Size = (uint)data.Length,
                    Speed = 0xFFFF,
                    SpeedKhz = 0,
                    PortId = LightingPort,
                    IsPortIdSet = 1,
                };
                return (write ? _write! : _read!)(_gpu, ref info, out _) == StatusOk;
            }
            finally
            {
                register.Free();
                buffer.Free();
            }
        }
    }

    public void Dispose() { }
}
