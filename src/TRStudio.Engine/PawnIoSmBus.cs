// SPDX-License-Identifier: GPL-2.0-or-later
//
// SMBus access through the PawnIO driver.
//
// Ported to C# from the OpenRGB project (https://gitlab.com/CalcProgrammer1/OpenRGB), files
// i2c_smbus/Windows/i2c_smbus_pawnio.cpp and i2c_smbus/i2c_smbus.cpp, by Adam Honse (CalcProgrammer1)
// and the OpenRGB contributors, licensed GPL-2.0-or-later. PawnIO and its SmbusPIIX4 module are by
// namazso (https://pawnio.eu/); the modules in PawnIo/ (SmbusPIIX4.bin for AMD, SmbusI801.bin for Intel)
// are the signed builds OpenRGB ships.

using System.Reflection;
using System.Runtime.InteropServices;

namespace TRStudio.Engine;

/// <summary>
/// The motherboard's SMBus: a slow shared wire that memory sticks, sensors and lighting chips all sit on,
/// each at its own address. Reads give -1 when nothing answers; writes give false.
/// </summary>
public interface ISmBus : IDisposable
{
    string Name { get; }

    /// <summary>Keeps every other program off the bus until disposed, so a request made of two steps isn't split.</summary>
    IDisposable Hold();

    int ReadByte(byte address);
    int ReadByteData(byte address, byte command);
    bool WriteByteData(byte address, byte command, byte value);
    bool WriteWordData(byte address, byte command, ushort value);
    bool WriteBlockData(byte address, byte command, ReadOnlySpan<byte> data);
}

/// <summary>
/// The SMBus of an Intel or AMD chipset, reached through the PawnIO driver TR Studio already installs for its
/// sensors. It needs administrator rights. Every transfer waits for the lock that monitoring and lighting
/// programs share by name, so they take turns on the bus instead of talking over each other.
/// </summary>
public sealed class PawnIoSmBus : ISmBus
{
    /// <summary>The name every well-behaved SMBus program on Windows uses for the shared lock.</summary>
    public const string SharedLock = @"Global\Access_SMBUS.HTP.Method";
    const string AmdModule = "TRStudio.Engine.PawnIo.SmbusPIIX4.bin", IntelModule = "TRStudio.Engine.PawnIo.SmbusI801.bin";
    const int Read = 1, Write = 0;
    const int SizeByte = 1, SizeByteData = 2, SizeWordData = 3, SizeBlockData = 5;
    const int BlockMax = 32, DataBytes = BlockMax + 2;
    public const ulong AlwaysSleep = 2; // the driver sleeps while the bus is busy instead of spinning a CPU core

    readonly IntPtr _handle;
    readonly PawnIo _library;
    readonly Mutex? _lock;

    PawnIoSmBus(PawnIo library, IntPtr handle, string name)
    {
        _library = library;
        _handle = handle;
        Name = name;
        // The driver sleeps while each transfer is on the wire, and Windows rounds a sleep up to its timer tick:
        // about 31 ms a transfer at the default tick, measured on a B550 board, against 1.2 ms with a 1 ms tick.
        // The finer tick is held only while this bus is open, which is only while memory lighting is on.
        timeBeginPeriod(1);
        try { _lock = new Mutex(false, SharedLock); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException) { _lock = null; }
    }

    public string Name { get; }

    /// <summary>How many transfers this bus has made, for measuring its pace.</summary>
    public long Transfers { get; private set; }

    /// <summary>
    /// Null when the bus can't be used: PawnIO isn't installed, the app isn't running as administrator, or
    /// the chipset is neither Intel nor AMD. The reason goes to the log. Intel's bus is tried first, then
    /// AMD's, the order OpenRGB uses; a module refuses to load on a chipset that isn't its own.
    /// </summary>
    public static PawnIoSmBus? Open(int port, Action<string>? log = null, ulong sleepMode = AlwaysSleep)
    {
        var library = PawnIo.Load();
        if (library == null) { log?.Invoke("Memory lighting: the PawnIO driver isn't installed"); return null; }

        var intel = Start(library, IntelModule, log, out var failed);
        if (intel != IntPtr.Zero)
        {
            library.Run(intel, "ioctl_set_sleep_mode", [sleepMode], 0);
            return new PawnIoSmBus(library, intel, "Intel SMBus");
        }
        if (failed != PawnIo.NotSupported) return null;

        var amd = Start(library, AmdModule, log, out failed);
        if (amd == IntPtr.Zero)
        {
            if (failed == PawnIo.NotSupported) log?.Invoke("Memory lighting: this PC's chipset isn't an Intel or AMD one TR Studio can drive");
            return null;
        }
        if (library.Run(amd, "ioctl_piix4_port_sel", [(ulong)port], 1) == null)
        {
            library.Close(amd);
            log?.Invoke($"Memory lighting: SMBus port {port} couldn't be selected");
            return null;
        }
        library.Run(amd, "ioctl_set_sleep_mode", [sleepMode], 0);
        return new PawnIoSmBus(library, amd, $"AMD SMBus port {port}");
    }

    /// <summary>
    /// Opens the driver and loads one module into it. Zero when that fails, with the driver's answer in
    /// failed; a chipset the module isn't for answers NotSupported and is logged by the caller.
    /// </summary>
    static IntPtr Start(PawnIo library, string resource, Action<string>? log, out int failed)
    {
        failed = 0;
        using var module = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (module == null) { failed = -1; return IntPtr.Zero; }
        var blob = new byte[module.Length];
        module.ReadExactly(blob);

        failed = library.Open(out var handle);
        if (failed != 0)
        {
            log?.Invoke(failed == PawnIo.AccessDenied ? "Memory lighting: needs TR Studio to run as administrator" : $"Memory lighting: PawnIO didn't open (0x{failed:X8})");
            return IntPtr.Zero;
        }
        failed = library.LoadModule(handle, blob, (nuint)blob.Length);
        if (failed == 0) return handle;
        library.Close(handle);
        if (failed != PawnIo.NotSupported) log?.Invoke($"Memory lighting: the SMBus module didn't load (0x{failed:X8})");
        return IntPtr.Zero;
    }

    sealed class Held(Mutex? mutex) : IDisposable
    {
        public void Dispose() => mutex?.ReleaseMutex();
    }

    /// <summary>How long a transfer waits for another program to let go of the bus before giving up.</summary>
    public static readonly TimeSpan LockWait = TimeSpan.FromMilliseconds(250);

    /// <summary>Takes the shared bus lock, or fails with an IOException when another program keeps it too long.</summary>
    public IDisposable Hold()
    {
        try
        {
            if (_lock?.WaitOne(LockWait) == false) throw new IOException($"another program held the SMBus for over {LockWait.TotalMilliseconds:0} ms");
        }
        catch (AbandonedMutexException) { /* another program died holding it; the lock is ours now */ }
        return new Held(_lock);
    }

    /// <summary>One SMBus transfer. The data travels packed into 64-bit slots, as the driver's module expects.</summary>
    bool Transfer(byte address, int direction, byte command, int size, Span<byte> data)
    {
        var input = new ulong[9];
        input[0] = address;
        input[1] = (ulong)direction;
        input[2] = command;
        input[3] = (ulong)size;
        MemoryMarshal.AsBytes(input.AsSpan(4)).Clear();
        data.CopyTo(MemoryMarshal.AsBytes(input.AsSpan(4)));
        using (Hold())
        {
            Transfers++;
            var output = _library.Run(_handle, "ioctl_smbus_xfer", input, 5);
            if (output == null) return false;
            MemoryMarshal.AsBytes(output.AsSpan())[..data.Length].CopyTo(data);
            return true;
        }
    }

    public int ReadByte(byte address)
    {
        Span<byte> data = stackalloc byte[DataBytes];
        return Transfer(address, Read, 0, SizeByte, data) ? data[0] : -1;
    }

    public int ReadByteData(byte address, byte command)
    {
        Span<byte> data = stackalloc byte[DataBytes];
        return Transfer(address, Read, command, SizeByteData, data) ? data[0] : -1;
    }

    public bool WriteByteData(byte address, byte command, byte value)
    {
        Span<byte> data = stackalloc byte[DataBytes];
        data[0] = value;
        return Transfer(address, Write, command, SizeByteData, data);
    }

    public bool WriteWordData(byte address, byte command, ushort value)
    {
        Span<byte> data = stackalloc byte[DataBytes];
        data[0] = (byte)value;
        data[1] = (byte)(value >> 8);
        return Transfer(address, Write, command, SizeWordData, data);
    }

    public bool WriteBlockData(byte address, byte command, ReadOnlySpan<byte> values)
    {
        Span<byte> data = stackalloc byte[DataBytes];
        var length = Math.Min(values.Length, BlockMax);
        data[0] = (byte)length;
        values[..length].CopyTo(data[1..]);
        return Transfer(address, Write, command, SizeBlockData, data);
    }

    [DllImport("winmm")]
    static extern uint timeBeginPeriod(uint milliseconds);
    [DllImport("winmm")]
    static extern uint timeEndPeriod(uint milliseconds);

    public void Dispose()
    {
        timeEndPeriod(1);
        _library.Close(_handle);
        _lock?.Dispose();
    }

    /// <summary>PawnIO's own library, loaded only from the folder its installer put it in.</summary>
    sealed class PawnIo
    {
        public const int AccessDenied = unchecked((int)0x80070005), NotSupported = unchecked((int)0x80070032);

        delegate int OpenFn(out IntPtr handle);
        delegate int LoadFn(IntPtr handle, byte[] blob, nuint size);
        delegate int ExecuteFn(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string name, ulong[] input, nuint inputSize, [Out] ulong[] output, nuint outputSize, out nuint returned);
        delegate int CloseFn(IntPtr handle);

        static readonly object Gate = new();
        static PawnIo? _loaded;

        readonly OpenFn _open;
        readonly LoadFn _load;
        readonly ExecuteFn _execute;
        readonly CloseFn _close;

        PawnIo(IntPtr library)
        {
            _open = Marshal.GetDelegateForFunctionPointer<OpenFn>(NativeLibrary.GetExport(library, "pawnio_open"));
            _load = Marshal.GetDelegateForFunctionPointer<LoadFn>(NativeLibrary.GetExport(library, "pawnio_load"));
            _execute = Marshal.GetDelegateForFunctionPointer<ExecuteFn>(NativeLibrary.GetExport(library, "pawnio_execute"));
            _close = Marshal.GetDelegateForFunctionPointer<CloseFn>(NativeLibrary.GetExport(library, "pawnio_close"));
        }

        public static PawnIo? Load()
        {
            lock (Gate)
            {
                if (_loaded != null) return _loaded;
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO", "PawnIOLib.dll");
                if (!File.Exists(path)) return null;
                try { return _loaded = new PawnIo(NativeLibrary.Load(path)); }
                catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException) { return null; }
            }
        }

        public int Open(out IntPtr handle) => _open(out handle);
        public int LoadModule(IntPtr handle, byte[] blob, nuint size) => _load(handle, blob, size);
        public void Close(IntPtr handle) => _close(handle);

        /// <summary>Null when the driver refuses or fails the call.</summary>
        public ulong[]? Run(IntPtr handle, string name, ulong[] input, int outputSize)
        {
            var output = new ulong[Math.Max(1, outputSize)];
            return _execute(handle, name, input, (nuint)input.Length, output, (nuint)outputSize, out _) == 0 ? output : null;
        }
    }
}
