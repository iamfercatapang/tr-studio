// SPDX-License-Identifier: GPL-2.0-or-later
//
// Graphics card lighting driven directly over the card's own I2C bus.
//
// The Galax controllers are ported to C# from the OpenRGB project (https://gitlab.com/CalcProgrammer1/OpenRGB),
// files Controllers/GalaxGPUController/GalaxGPUControllerDetect.cpp, GalaxGPUv1Controller/GalaxGPUv1Controller.cpp
// and .h, GalaxGPUv2Controller/GalaxGPUv2Controller.cpp and .h and RGBController_GalaxGPUv2.cpp, by
// Niels Westphal (crashniels), Daniel Stuart (daniel.stuart14) and the OpenRGB contributors, licensed
// GPL-2.0-or-later. The second v2 identity (02 01 at 0x50) is TR Studio's own finding on a GALAX RTX 4070
// EX Gamer, whose v2 registers were read back and written on the real card.

namespace TRStudio.Engine;

/// <summary>
/// Galax and KFA2 cards. v1 chips sit at 0x23 or 0x32, v2 chips at 0x50 or 0x51. The whole card shows one color.
/// Every write goes to one register at the card's own address.
/// </summary>
public sealed class GalaxGpuController : IGpuLightController
{
    const byte Red = 0x02, Green = 0x03, Blue = 0x04;
    const byte V1Mode1 = 0x05, V1Mode2 = 0x06;
    const byte V2SpeedA = 0x21, V2SpeedB = 0x22, V2Sync = 0x27, V2Brightness = 0x2D, V2Mode = 0x30, V2Save = 0x40;
    const byte V2Static = 0x01, V2Breathing = 0x02, V2Rainbow = 0x16, V2Off = 0x19, V2BrightnessMax = 0x03, V2SpeedMax = 0x09, V2SaveValue = 0x5A;

    readonly ISmBus _bus;
    readonly bool _v2;

    GalaxGpuController(ISmBus bus, byte address)
    {
        _bus = bus;
        Address = address;
        _v2 = address is 0x50 or 0x51;
        Modes = _v2
            ? [new("Direct", false, 0), new("Breathing", true, 1), new("Rainbow", true, 0), new("Off", false, 0)]
            : [new("Direct", false, 0), new("Breathing", false, 1), new("Rainbow", false, 0), new("Color Cycle Breathing", false, 0), new("Off", false, 0)];
    }

    public string Driver => _v2 ? "Galax GPU v2" : "Galax GPU v1";
    public byte Address { get; }
    public IReadOnlyList<LightZone> Zones { get; } = [new LightZone("GPU", 1, 1, 1)];
    public IReadOnlyList<LightMode> Modes { get; }
    public int LedCount => 1;

    /// <summary>The read-only identity check OpenRGB does before driving a Galax card, plus the 4070 EX Gamer's answer.</summary>
    public static bool IsAt(ISmBus bus, byte address)
    {
        int Read(byte register) => bus.ReadByteData(address, register);
        return address switch
        {
            0x23 => Read(0x00) is 0x27 or 0x30,
            0x32 => Read(0x00) is 0x27 or 0x26 && Read(0x01) is 0x10 or 0x20,
            0x51 => Read(0x00) == 0x80,
            0x50 => (Read(0x00), Read(0x01)) is (0x01, 0x08) or (0x02, 0x01),
            _ => false,
        };
    }

    public static GalaxGpuController? Open(ISmBus bus, byte address) => IsAt(bus, address) ? new GalaxGpuController(bus, address) : null;

    /// <summary>Static mode at full brightness, so the colors sent are the colors shown.</summary>
    public void SetDirect()
    {
        if (_v2)
        {
            Write(V2Sync, 0x00);
            Write(V2Mode, V2Static);
            Write(V2Brightness, V2BrightnessMax);
        }
        else
        {
            Write(V1Mode1, 0x00);
            Write(V1Mode2, 0x01);
        }
    }

    public void SetLeds(ReadOnlySpan<Rgb> colors)
    {
        if (colors.Length > 0) SetColor(colors[0]);
    }

    /// <summary>Speed 0 to 100 becomes the card's 0 to 9. Off on v1 is a black static color.</summary>
    public void SetMode(string mode, int speed, IReadOnlyList<Rgb> colors)
    {
        var color = colors.Count > 0 ? colors[0] : Rgb.Black;
        if (mode == "Direct") { SetDirect(); return; }
        if (_v2)
        {
            var value = mode switch { "Breathing" => V2Breathing, "Rainbow" => V2Rainbow, "Off" => V2Off, _ => (byte)0 };
            if (value == 0) return;
            if (value == V2Breathing) SetColor(color);
            var pace = (byte)GpuLighting.Scale(speed, 0, V2SpeedMax);
            Write(V2Sync, 0x00);
            Write(V2Mode, value);
            Write(V2SpeedA, pace);
            Write(V2SpeedB, pace);
            Write(V2Brightness, V2BrightnessMax);
            return;
        }
        (byte, byte)? v1 = mode switch { "Breathing" => (0x04, 0x00), "Rainbow" => (0x84, 0x02), "Color Cycle Breathing" => (0x84, 0x40), "Off" => (0x00, 0x01), _ => null };
        if (v1 is not { } values) return;
        SetColor(mode == "Off" ? Rgb.Black : color);
        Write(V1Mode1, values.Item1);
        Write(V1Mode2, values.Item2);
    }

    /// <summary>v2 keeps the current look after power off; v1 has no save. TR Studio never calls it on its own.</summary>
    public void Save()
    {
        if (_v2) Write(V2Save, V2SaveValue);
    }

    void SetColor(Rgb color)
    {
        Write(Red, color.R);
        Write(Green, color.G);
        Write(Blue, color.B);
    }

    void Write(byte register, byte value)
    {
        if (!_bus.WriteByteData(Address, register, value)) throw GpuLighting.Silent(Driver, Address);
    }
}

/// <summary>
/// Drives the graphics cards in this PC whose four PCI ids are in <see cref="GpuCatalog"/>, each after its chip
/// passes its driver's identity check, so nothing is written to an unknown chip. Colors go out on a thread of
/// their own, newest frame wins, so a slow chip (MSI's newer one takes ~140 ms a frame) never holds up the
/// other lights. A PC with no such card gives a client with no devices.
/// </summary>
public sealed class GpuLightingClient : ILightingClient
{
    sealed record Card(string Key, string Name, IGpuLightController Light, ISmBus Bus);

    readonly List<Card> _cards;
    readonly Action<string>? _log;
    readonly object _gate = new();
    readonly Dictionary<int, Queue<Action<IGpuLightController>>> _steps = [];
    readonly Dictionary<int, Rgb[]> _latest = [];
    readonly AutoResetEvent _wake = new(false);
    readonly Thread? _worker;
    volatile bool _closing;

    GpuLightingClient(List<Card> cards, Action<string>? log)
    {
        _cards = cards;
        _log = log;
        if (cards.Count == 0) return;
        _worker = new Thread(Run) { IsBackground = true, Name = "GPU lighting" };
        _worker.Start();
    }

    /// <summary>Drives the given chips as they are; for tests and probes.</summary>
    public GpuLightingClient(IEnumerable<(string Key, string Name, IGpuLightController Light, ISmBus Bus)> cards, Action<string>? log = null)
        : this(cards.Select(card => new Card(card.Key, card.Name, card.Light, card.Bus)).ToList(), log) { }

    public static GpuLightingClient Connect(Action<string>? log = null)
    {
        var cards = new List<Card>();
        foreach (var gpu in NvApiI2cBus.Gpus())
        {
            foreach (var entry in GpuCatalog.Find(gpu.Vendor, gpu.Device, gpu.SubVendor, gpu.SubDevice))
            {
                var bus = new NvApiI2cBus(gpu);
                IGpuLightController? light;
                try { light = Open(bus, entry); }
                catch (IOException e) { log?.Invoke($"Graphics card {entry.Name}: {e.Message}"); light = null; }
                if (light == null)
                {
                    log?.Invoke($"Graphics card {entry.Name} ({gpu.Ids}): no {entry.Driver} chip answered at 0x{entry.Address:X2}");
                    continue;
                }
                cards.Add(new Card($"GPU|{gpu.Ids}|{entry.Address:X2}", entry.Name, light, bus));
                log?.Invoke($"Graphics card lighting: {entry.Name}, {light.Driver} at 0x{entry.Address:X2}");
                break;
            }
        }
        return new GpuLightingClient(cards, log);
    }

    /// <summary>The catalog's driver for the card, opened only if its chip passes the identity check. Unported drivers give null.</summary>
    public static IGpuLightController? Open(IRawI2cBus bus, GpuCatalogEntry entry) => entry.Driver switch
    {
        "GalaxGPUController" => GalaxGpuController.Open(bus, entry.Address),
        "AsusAuraGPUController" => AsusAuraGpuController.Open(bus, entry.Address),
        "MSIGPUController" => MsiGpuController.Open(bus, entry.Address),
        "MSIGPUv2Controller" => MsiGpuV2Controller.Open(bus, entry.Address, entry.Device, entry.SubDevice),
        "GigabyteRGBFusion2GPUController" => GigabyteFusion2GpuController.Open(bus, entry.Address),
        "GigabyteRGBFusion2BlackwellGPUController" when BlackwellLayout(entry.Detector) is { } layout =>
            GigabyteFusion2BlackwellGpuController.Open(bus, entry.Address, layout),
        _ => null,
    };

    /// <summary>OpenRGB picks a Blackwell card's zone layout by which detect function lists it.</summary>
    static Fusion2BlackwellLayout? BlackwellLayout(string detector) => detector switch
    {
        _ when detector.Contains("SingleZone") => Fusion2BlackwellLayout.SingleZone,
        _ when detector.Contains("AorusWaterforce") => Fusion2BlackwellLayout.AorusWaterforce,
        _ when detector.Contains("AorusMaster5080") => Fusion2BlackwellLayout.AorusMaster5080,
        _ when detector.Contains("AorusMaster5090DV2Ice") => Fusion2BlackwellLayout.AorusMaster5090DV2Ice,
        _ when detector.Contains("Waterforce") => Fusion2BlackwellLayout.Waterforce,
        _ when detector.Contains("Gaming") => Fusion2BlackwellLayout.Gaming,
        _ => null,
    };

    public bool TakeDevicesChanged() => false;

    public IReadOnlyList<LightDevice> Devices() => _cards.Select((card, index) =>
        new LightDevice(index, card.Key, card.Name, "Gpu", card.Light.Zones, card.Light.Modes, card.Light.LedCount)).ToList();

    public void SetDirect(int device) => Enqueue(device, light => light.SetDirect());

    /// <summary>Colors replace a frame still waiting; mode changes wait their turn after what is queued.</summary>
    public void SetLeds(int device, ReadOnlySpan<Rgb> colors)
    {
        if (device < 0 || device >= _cards.Count || colors.Length == 0) return;
        var frame = colors.ToArray();
        lock (_gate)
        {
            var waiting = _latest.ContainsKey(device);
            _latest[device] = frame;
            if (!waiting) Queue(device).Enqueue(light => { if (Take(device) is { } newest) light.SetLeds(newest); });
        }
        _wake.Set();
    }

    public void SetMode(int device, string mode, int speed, IReadOnlyList<Rgb> colors)
    {
        var copy = colors.ToArray();
        Enqueue(device, light => light.SetMode(mode, speed, copy));
    }

    public void ResizeZone(int device, int zone, int leds) { }

    void Enqueue(int device, Action<IGpuLightController> step)
    {
        if (device < 0 || device >= _cards.Count) return;
        lock (_gate) Queue(device).Enqueue(step);
        _wake.Set();
    }

    Queue<Action<IGpuLightController>> Queue(int device)
    {
        if (!_steps.TryGetValue(device, out var queue)) _steps[device] = queue = new();
        return queue;
    }

    Rgb[]? Take(int device)
    {
        lock (_gate)
        {
            _latest.Remove(device, out var frame);
            return frame;
        }
    }

    void Run()
    {
        while (!_closing)
        {
            _wake.WaitOne(500);
            while (!_closing)
            {
                (int Device, Action<IGpuLightController> Step)? next = null;
                lock (_gate)
                {
                    foreach (var (device, queue) in _steps)
                    {
                        if (queue.Count > 0) { next = (device, queue.Dequeue()); break; }
                    }
                }
                if (next is not { } work) break;
                var card = _cards[work.Device];
                try
                {
                    using (card.Bus.Hold()) work.Step(card.Light);
                }
                catch (IOException e) { _log?.Invoke(e.Message); }
            }
        }
    }

    public void Dispose()
    {
        _closing = true;
        _wake.Set();
        _worker?.Join(1000);
        foreach (var card in _cards) card.Bus.Dispose();
    }
}
