// SPDX-License-Identifier: GPL-2.0-or-later
//
// Driver for ENE lighting controllers on RGB memory sticks (the chip behind most "Aura compatible" DDR4
// and DDR5 kits: XPG, G.Skill, T-Force, Geil and others).
//
// Ported to C# from the OpenRGB project (https://gitlab.com/CalcProgrammer1/OpenRGB), files
// Controllers/ENESMBusController/ENESMBusController.cpp, ENESMBusControllerDetect.cpp,
// RGBController_ENESMBus.cpp and ENESMBusInterface/ENESMBusInterface_i2c_smbus.cpp, by Adam Honse
// (CalcProgrammer1) and the OpenRGB contributors, licensed GPL-2.0-or-later. The register map, the
// address remapping and the detection checks are theirs.

using System.Text;

namespace TRStudio.Engine;

/// <summary>
/// One memory stick's lighting chip. Its registers are reached in two steps: the register's number is
/// written first, then the value is read or written. Colors are stored red, blue, green.
/// </summary>
public sealed class EneController
{
    const ushort RegName = 0x1000, RegMicron = 0x1030, RegConfig = 0x1C00;
    const ushort RegDirectV1 = 0x8000, RegEffectV1 = 0x8010, RegDirectV2 = 0x8100, RegEffectV2 = 0x8160;
    const ushort RegDirect = 0x8020, RegMode = 0x8021, RegSpeed = 0x8022, RegDirection = 0x8023, RegApply = 0x80A0;
    public const ushort RegSlotIndex = 0x80F8, RegAddress = 0x80F9;
    const byte Apply = 0x01;
    const byte CommandRegister = 0x00, CommandWrite = 0x01, CommandBlock = 0x03, CommandRead = 0x81;
    const int MaxBlock = 3, ConfigLedCount = 0x02, ConfigLedCountGpu = 0x03, Slowest = 4;
    const int FirstTestRegister = 0xA0, TestRegisters = 16;

    /// <summary>Known chips by the name they report: whether they use the newer color registers, and where their LED count is.</summary>
    static readonly Dictionary<string, (bool NewRegisters, int LedCountAt)> Chips = new()
    {
        ["LED-0116"] = (false, ConfigLedCount), ["DIMM_LED-0102"] = (false, ConfigLedCount), ["DIMM_LED-0103"] = (false, ConfigLedCount),
        ["AUDA0-E6K5-0101"] = (true, ConfigLedCount), ["AUMA0-E6K5-0106"] = (true, ConfigLedCount), ["AUMA0-E6K5-0105"] = (true, ConfigLedCount),
        ["AUMA0-E6K5-0104"] = (true, ConfigLedCount), ["AUMA0-E8K4-0101"] = (false, ConfigLedCount),
        ["AUMA0-E6K5-0107"] = (true, ConfigLedCountGpu), ["AUMA0-E6K5-1110"] = (true, ConfigLedCountGpu), ["AUMA0-E6K5-1111"] = (true, ConfigLedCountGpu),
        ["AUMA0-E6K5-1107"] = (true, ConfigLedCountGpu), ["AUMA0-E6K5-0008"] = (true, ConfigLedCountGpu), ["AUMA0-E6K5-1113"] = (true, ConfigLedCountGpu),
        ["AUMA0-E6K5-1114"] = (true, ConfigLedCountGpu),
    };

    /// <summary>The chip's own effects by number. Colors: whether the effect uses the stick's colors.</summary>
    static readonly (string Name, byte Value, bool Speed, bool Colors)[] Effects =
    [
        ("Off", 0, false, false), ("Static", 1, false, true), ("Breathing", 2, true, true), ("Flashing", 3, true, true),
        ("Spectrum Cycle", 4, true, false), ("Rainbow", 5, true, false), ("Chase Fade", 7, true, true), ("Chase", 9, true, true),
        ("Random Flicker", 13, true, false),
    ];

    readonly ISmBus _bus;
    readonly ushort _directColors, _effectColors;

    /// <summary>Reads the chip's name and configuration. An unknown chip gets no LEDs, so nothing is ever written to it.</summary>
    public EneController(ISmBus bus, byte address)
    {
        _bus = bus;
        Address = address;
        var name = new byte[16];
        for (var i = 0; i < name.Length; i++) name[i] = Read(bus, address, (ushort)(RegName + i));
        name[15] = 0;
        Chip = Encoding.ASCII.GetString(name, 0, Array.IndexOf(name, (byte)0));
        var known = Chips.TryGetValue(Chip, out var chip);
        _directColors = known && chip.NewRegisters ? RegDirectV2 : RegDirectV1;
        _effectColors = known && chip.NewRegisters ? RegEffectV2 : RegEffectV1;
        // Only the LED count is needed from the chip's configuration, and every register read costs two transfers.
        LedCount = known ? Read(bus, address, (ushort)(RegConfig + chip.LedCountAt)) : 0;
    }

    public byte Address { get; }
    public string Chip { get; }
    public int LedCount { get; }

    public static IReadOnlyList<LightMode> Modes { get; } =
        [new LightMode("Direct", false, 0), .. Effects.Select(effect => new LightMode(effect.Name, effect.Speed, effect.Colors ? 1 : 0))];

    static ushort Swapped(ushort register) => (ushort)((register << 8) | (register >> 8));

    static byte Read(ISmBus bus, byte address, ushort register)
    {
        using (bus.Hold())
        {
            bus.WriteWordData(address, CommandRegister, Swapped(register));
            return (byte)bus.ReadByteData(address, CommandRead);
        }
    }

    public static void Write(ISmBus bus, byte address, ushort register, byte value)
    {
        using (bus.Hold())
        {
            if (!bus.WriteWordData(address, CommandRegister, Swapped(register)) || !bus.WriteByteData(address, CommandWrite, value))
                throw new IOException($"The memory lighting chip at 0x{address:X2} stopped answering.");
        }
    }

    void Write(ushort register, byte value) => Write(_bus, Address, register, value);

    void WriteBlock(ushort register, ReadOnlySpan<byte> data)
    {
        using (_bus.Hold())
        {
            if (!_bus.WriteWordData(Address, CommandRegister, Swapped(register))) throw new IOException($"The memory lighting chip at 0x{Address:X2} stopped answering.");
            if (_bus.WriteBlockData(Address, CommandBlock, data)) return;
            // Some chips don't take blocks: the same bytes go one at a time.
            foreach (var value in data)
            {
                if (!_bus.WriteByteData(Address, CommandWrite, value)) throw new IOException($"The memory lighting chip at 0x{Address:X2} stopped answering.");
            }
        }
    }

    /// <summary>
    /// Whether an ENE lighting chip answers at this address. Its registers 0xA0 to 0xAF read back 0 to 15,
    /// which nothing else on the bus does, so no other kind of device is mistaken for one.
    /// </summary>
    public static bool IsAt(ISmBus bus, byte address)
    {
        if (bus.ReadByte(address) < 0 && bus.ReadByteData(address, 0x00) < 0) return false;
        for (var i = 0; i < TestRegisters; i++)
        {
            if (bus.ReadByteData(address, (byte)(FirstTestRegister + i)) != i) return false;
        }
        // Micron's sticks pass the same check but work differently, so they are left alone.
        var maker = new byte[16];
        for (var i = 0; i < maker.Length; i++) maker[i] = Read(bus, address, (ushort)(RegMicron + i));
        var end = Array.IndexOf(maker, (byte)0);
        return Encoding.ASCII.GetString(maker, 0, end < 0 ? maker.Length : end) != "Micron";
    }

    /// <summary>What the chip holds right now, read back from it: for checking that writes arrive.</summary>
    public string ReadState()
    {
        string Bytes(ushort from, int count) => Convert.ToHexString(Enumerable.Range(0, count).Select(i => Read(_bus, Address, (ushort)(from + i))).ToArray());
        return $"direct {Bytes(RegDirect, 1)}, mode {Bytes(RegMode, 1)}, speed {Bytes(RegSpeed, 1)}, direct colors {Bytes(_directColors, Math.Min(LedCount, 3) * 3)}, effect colors {Bytes(_effectColors, Math.Min(LedCount, 3) * 3)}";
    }

    /// <summary>Hands every LED over to the colors sent from the PC.</summary>
    public void SetDirect()
    {
        Write(RegDirect, 1);
        Write(RegApply, Apply);
    }

    public void SetLeds(ReadOnlySpan<Rgb> colors) => WriteColors(_directColors, colors);

    /// <summary>Starts one of the chip's own effects. An unknown name does nothing.</summary>
    public void SetMode(string mode, int speed, Rgb color)
    {
        var found = Array.FindIndex(Effects, effect => string.Equals(effect.Name, mode, StringComparison.OrdinalIgnoreCase));
        if (found < 0) return;
        var effect = Effects[found];
        if (effect.Colors)
        {
            var colors = new Rgb[LedCount];
            Array.Fill(colors, color);
            WriteColors(_effectColors, colors);
            Write(RegApply, Apply);
        }
        // The chip counts speed downward: 4 is the slowest and 0 the fastest.
        var pace = effect.Speed ? (byte)Math.Round(Slowest - Slowest * Math.Clamp(speed, 0, 100) / 100.0) : (byte)0;
        Write(RegMode, effect.Value);
        Write(RegSpeed, pace);
        Write(RegDirection, 0);
        Write(RegApply, Apply);
        Write(RegDirect, 0);
        Write(RegApply, Apply);
    }

    void WriteColors(ushort register, ReadOnlySpan<Rgb> colors)
    {
        var count = Math.Min(colors.Length, LedCount);
        var bytes = new byte[count * 3];
        for (var i = 0; i < count; i++)
        {
            bytes[i * 3] = colors[i].R;
            bytes[i * 3 + 1] = colors[i].B;
            bytes[i * 3 + 2] = colors[i].G;
        }
        for (var sent = 0; sent < bytes.Length; sent += MaxBlock)
            WriteBlock((ushort)(register + sent), bytes.AsSpan(sent, Math.Min(MaxBlock, bytes.Length - sent)));
    }
}

/// <summary>
/// The RGB memory sticks on this PC, driven with no other program running. Sticks all start at one shared
/// address, so each is first moved to an address of its own, as every lighting program does; the move
/// lasts until the PC powers off. The bus is slow (a frame for four 8-LED sticks takes about 80 ms), so
/// colors are written on a thread of their own and a frame that arrives while the last is still going out
/// replaces any frame waiting behind it. The shared lock is let go between transfers, so sensors get their turn.
/// </summary>
public sealed class EneRamLightingClient : ILightingClient
{
    const byte SharedAddress = 0x77;
    const int Slots = 8;
    /// <summary>The addresses sticks may be moved to. None is one that memory's own data or sensors use.</summary>
    static readonly byte[] Addresses = [0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x4F, 0x66, 0x67, 0x39, 0x3A, 0x3B, 0x3C, 0x3D];

    readonly ISmBus? _bus;
    readonly List<EneController> _sticks;
    readonly List<(int Device, Action<EneController> Work, bool Frame)> _queue = [];
    readonly AutoResetEvent _wake = new(false);
    readonly Thread? _worker;
    volatile Exception? _failure;
    long _framesWritten;

    /// <summary>How many color frames have reached the sticks, counting each stick's separately.</summary>
    public long FramesWritten => Interlocked.Read(ref _framesWritten);
    volatile bool _stopping;

    public EneRamLightingClient(ISmBus? bus, IEnumerable<EneController> sticks, bool background = true)
    {
        _bus = bus;
        _sticks = sticks.ToList();
        if (!background || _sticks.Count == 0) return;
        _worker = new Thread(Run) { IsBackground = true, Name = "Memory lighting" };
        _worker.Start();
    }

    /// <summary>A PC without PawnIO, without administrator rights, or without such memory gives a client with no devices.</summary>
    public static EneRamLightingClient Connect(Action<string>? log = null, ulong sleepMode = PawnIoSmBus.AlwaysSleep)
    {
        var bus = PawnIoSmBus.Open(0, log, sleepMode);
        if (bus == null) return new EneRamLightingClient(null, []);
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var sticks = Find(bus, log);
            // About 1.2 ms a transfer is right; about 31 means Windows is ignoring the fine timer and every frame will crawl.
            var pace = bus.Transfers > 0 ? clock.Elapsed.TotalMilliseconds / bus.Transfers : 0;
            log?.Invoke($"Memory lighting: found {sticks.Count} stick(s) in {clock.ElapsedMilliseconds} ms, {bus.Transfers} transfers, {pace:0.0} ms a transfer");
            if (sticks.Count == 0) { bus.Dispose(); return new EneRamLightingClient(null, []); }
            return new EneRamLightingClient(bus, sticks);
        }
        catch (IOException e)
        {
            bus.Dispose();
            log?.Invoke($"Memory lighting: {e.Message}");
            return new EneRamLightingClient(null, []);
        }
    }

    /// <summary>Gives each stick still at the shared address one of its own, then lists the chips that answer.</summary>
    public static List<EneController> Find(ISmBus bus, Action<string>? log = null)
    {
        var next = -1;
        for (var slot = 0; slot < Slots; slot++)
        {
            if (bus.ReadByte(SharedAddress) < 0) break; // every stick has been moved already
            int taken;
            do
            {
                next++;
                taken = next < Addresses.Length ? bus.ReadByte(Addresses[next]) : -1;
            } while (next < Addresses.Length && taken >= 0);
            if (next >= Addresses.Length) break;
            EneController.Write(bus, SharedAddress, EneController.RegSlotIndex, (byte)slot);
            EneController.Write(bus, SharedAddress, EneController.RegAddress, (byte)(Addresses[next] << 1));
        }

        var sticks = new List<EneController>();
        foreach (var address in Addresses)
        {
            if (EneController.IsAt(bus, address))
            {
                var stick = new EneController(bus, address);
                log?.Invoke($"Memory lighting chip {stick.Chip} at 0x{address:X2} on {bus.Name}, {stick.LedCount} LEDs");
                if (stick.LedCount > 0) sticks.Add(stick);
            }
            Thread.Sleep(1);
        }
        return sticks;
    }

    void Run()
    {
        while (!_stopping)
        {
            (int Device, Action<EneController> Work, bool Frame) job;
            lock (_queue)
            {
                if (_queue.Count == 0) job = (-1, _ => { }, false);
                else
                {
                    job = _queue[0];
                    _queue.RemoveAt(0);
                }
            }
            if (job.Device < 0) { _wake.WaitOne(); continue; }
            try
            {
                job.Work(_sticks[job.Device]);
                if (job.Frame) Interlocked.Increment(ref _framesWritten);
            }
            catch (IOException e)
            {
                _failure = e;
                return;
            }
        }
    }

    void Enqueue(int device, Action<EneController> work, bool frame = false)
    {
        if (_failure is { } failure) throw new LightingUnavailableException($"Memory lighting stopped: {failure.Message}", failure);
        if (device < 0 || device >= _sticks.Count) return;
        if (_worker == null)
        {
            try { work(_sticks[device]); }
            catch (IOException e) { throw new LightingUnavailableException($"Memory lighting stopped: {e.Message}", e); }
            return;
        }
        lock (_queue)
        {
            // A newer frame replaces one still waiting, unless something else for the stick was asked in between.
            var last = _queue.FindLastIndex(job => job.Device == device);
            if (frame && last >= 0 && _queue[last].Frame) _queue[last] = (device, work, true);
            else _queue.Add((device, work, frame));
        }
        _wake.Set();
    }

    public bool TakeDevicesChanged() => false;

    // Keyed by its place among the sticks, not its address: the addresses are handed out afresh after every
    // restart, by whichever program gets there first, so they would lose the stick its spot on the canvas.
    public IReadOnlyList<LightDevice> Devices() => _sticks.Select((stick, index) => new LightDevice(index,
        $"ENE DRAM|{_bus?.Name}|Stick {index + 1}", $"RGB Memory {index + 1}", "Dram",
        [new LightZone("DRAM", stick.LedCount, stick.LedCount, stick.LedCount)], EneController.Modes, stick.LedCount)).ToList();

    public void SetDirect(int device) => Enqueue(device, stick => stick.SetDirect());

    public void SetLeds(int device, ReadOnlySpan<Rgb> colors)
    {
        var leds = colors.ToArray();
        Enqueue(device, stick => stick.SetLeds(leds), frame: true);
    }

    public void SetMode(int device, string mode, int speed, IReadOnlyList<Rgb> colors)
    {
        var color = colors.Count > 0 ? colors[0] : Rgb.Black;
        Enqueue(device, stick => stick.SetMode(mode, speed, color));
    }

    public void ResizeZone(int device, int zone, int leds) { }

    public void Dispose()
    {
        _stopping = true;
        _wake.Set();
        if (_worker is { IsAlive: true }) _worker.Join(TimeSpan.FromSeconds(3)); // lets a color being written finish
        _bus?.Dispose();
        _wake.Dispose();
    }
}

/// <summary>Several built-in drivers as one list of devices, in the order given.</summary>
public sealed class BuiltInLightingClient(params ILightingClient[] drivers) : ILightingClient
{
    List<(ILightingClient Driver, int Index)> _routes = [];

    public bool TakeDevicesChanged()
    {
        var changed = false;
        foreach (var driver in drivers) changed |= driver.TakeDevicesChanged();
        return changed;
    }

    public IReadOnlyList<LightDevice> Devices()
    {
        var routes = new List<(ILightingClient, int)>();
        var devices = new List<LightDevice>();
        foreach (var driver in drivers)
        {
            foreach (var device in driver.Devices())
            {
                routes.Add((driver, device.Index));
                devices.Add(device with { Index = devices.Count });
            }
        }
        _routes = routes;
        return devices;
    }

    (ILightingClient Driver, int Index)? Route(int device) => device >= 0 && device < _routes.Count ? _routes[device] : null;

    public void SetDirect(int device)
    {
        if (Route(device) is { } route) route.Driver.SetDirect(route.Index);
    }

    public void SetLeds(int device, ReadOnlySpan<Rgb> colors)
    {
        if (Route(device) is { } route) route.Driver.SetLeds(route.Index, colors);
    }

    public void SetMode(int device, string mode, int speed, IReadOnlyList<Rgb> colors)
    {
        if (Route(device) is { } route) route.Driver.SetMode(route.Index, mode, speed, colors);
    }

    public void ResizeZone(int device, int zone, int leds)
    {
        if (Route(device) is { } route) route.Driver.ResizeZone(route.Index, zone, leds);
    }

    public void Dispose()
    {
        foreach (var driver in drivers) driver.Dispose();
    }
}
