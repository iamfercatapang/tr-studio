// SPDX-License-Identifier: GPL-2.0-or-later
//
// Driver for the RGB Fusion 2 lighting chip on Gigabyte and AORUS graphics cards from the RTX 50 and
// Radeon RX 9000 series on.
//
// Ported to C# from the OpenRGB project (https://gitlab.com/CalcProgrammer1/OpenRGB), files
// Controllers/GigabyteRGBFusion2BlackwellGPUController/GigabyteRGBFusion2BlackwellGPUController.cpp,
// GigabyteRGBFusion2BlackwellGPUController.h, GigabyteRGBFusion2BlackwellGPUControllerDetect.cpp,
// GigabyteRGBFusion2BlackwellGPUDefinitions.h and RGBController_GigabyteRGBFusion2BlackwellGPU.cpp, by Adam
// Honse (CalcProgrammer1) and the OpenRGB contributors, licensed GPL-2.0-or-later. The packet layout, the
// identity query, the card layouts and the effect list are theirs.

namespace TRStudio.Engine;

/// <summary>Which lights a Blackwell-era Gigabyte card has, from OpenRGB's list of cards. The catalog picks it by PCI ids.</summary>
public enum Fusion2BlackwellLayout { SingleZone, Gaming, Waterforce, AorusWaterforce, AorusMaster5080, AorusMaster5090DV2Ice }

/// <summary>
/// RGB Fusion 2 on a Blackwell-era card: each hardware zone is set with one 64-byte I2C packet holding the
/// effect, speed, brightness and the zone's color, and optionally a list of colors after it. Direct uses its
/// own packet type, which updates fast but which some Gaming cards forget; effects use the slow one (at most
/// nine updates a second).
/// </summary>
public sealed class GigabyteFusion2BlackwellGpuController : IGpuLightController
{
    const byte TypeMode = 0x12, TypeColor = 0x16, QueryCommand = 0x10, SaveCommand = 0x13;
    const byte ModeDirect = 0x00, ModeStatic = 0x01, ModeBreathing = 0x02, ModeFlashing = 0x03, ModeDualFlashing = 0x04;
    const byte ModeColorCycle = 0x05, ModeWave = 0x06, ModeGradient = 0x07, ModeColorShift = 0x08, ModeDazzle = 0x0A, ModeClaws = 0x0C;
    const byte SpeedSlowest = 0x01, SpeedFastest = 0x06, BrightnessMax = 0x0A;
    const int PacketSize = 64, HardwareZones = 6, MasterFanLeds = 8;

    sealed record Effect(string Name, byte Value, bool Speed, int Colors, bool OwnColors);

    readonly IRawI2cBus _bus;
    readonly Fusion2BlackwellLayout _layout;
    readonly List<Effect> _effects;
    readonly Rgb[] _leds;
    readonly Rgb[] _zoneColors = new Rgb[HardwareZones];
    Effect _active;
    byte _speed;
    Rgb[] _own = [];

    GigabyteFusion2BlackwellGpuController(IRawI2cBus bus, byte address, Fusion2BlackwellLayout layout)
    {
        _bus = bus;
        Address = address;
        _layout = layout;
        Zones = BuildZones(layout);
        _leds = new Rgb[Zones.Sum(zone => zone.LedCount)];
        _effects = BuildEffects(layout);
        _active = _effects[0];
        Modes = _effects.Select(effect => new LightMode(effect.Name, effect.Speed, effect.Colors)).ToList();
    }

    public string Driver => "Gigabyte RGB Fusion 2 Blackwell GPU";
    public byte Address { get; }
    public IReadOnlyList<LightZone> Zones { get; }
    public IReadOnlyList<LightMode> Modes { get; }
    public int LedCount => _leds.Length;

    /// <summary>
    /// Whether a Blackwell-era RGB Fusion 2 chip answers at this address, as OpenRGB's detector asks: the identity
    /// query (0x10, 0x01 and 62 zeros) is written and four bytes read back, which must be 0x01, then 0x01 or 0x02,
    /// then 0x01. The query is the only write, and it changes nothing on the chip. On AMD cards OpenRGB also
    /// checks that the bus is the card's own I2C bus; that check belongs to whoever opens the bus.
    /// </summary>
    public static bool IsAt(IRawI2cBus bus, byte address)
    {
        var query = new byte[PacketSize];
        query[0] = QueryCommand;
        query[1] = 0x01;
        bus.WriteRaw(address, query);
        Span<byte> reply = stackalloc byte[4];
        if (!bus.ReadRaw(address, reply)) return false;
        return reply[0] == 0x01 && (reply[1] is 0x01 or 0x02) &&reply[2] == 0x01;
    }

    public static GigabyteFusion2BlackwellGpuController? Open(IRawI2cBus bus, byte address, Fusion2BlackwellLayout layout) =>
        IsAt(bus, address) ? new GigabyteFusion2BlackwellGpuController(bus, address, layout) : null;

    static List<LightZone> BuildZones(Fusion2BlackwellLayout layout)
    {
        static LightZone One(string name) => new(name, 1, 1, 1);
        return layout switch
        {
            Fusion2BlackwellLayout.SingleZone => [One("Side")],
            Fusion2BlackwellLayout.Gaming => [One("Right fan"), One("Left fan"), One("Center fan"), One("Side")],
            Fusion2BlackwellLayout.Waterforce => [One("Waterblock"), One("Backplate")],
            Fusion2BlackwellLayout.AorusWaterforce => [One("Bottom Logo"), One("Radiator Fans"), One("Top Logo"), One("Side Logo")],
            Fusion2BlackwellLayout.AorusMaster5080 =>
            [
                new("Right Fan", MasterFanLeds, MasterFanLeds, MasterFanLeds, LightShape.Ring), new("Left Fan", MasterFanLeds, MasterFanLeds, MasterFanLeds, LightShape.Ring),
                new("Middle Fan", MasterFanLeds, MasterFanLeds, MasterFanLeds, LightShape.Ring), One("Side Logo"), One("Top Logo"),
            ],
            _ => [One("Right Fan"), One("Left Fan"), One("Middle Fan"), One("Backplate"), One("Side Logo")],
        };
    }

    static List<Effect> BuildEffects(Fusion2BlackwellLayout layout)
    {
        // Direct comes first, as on every lighting device here; OpenRGB lists Static first.
        var effects = new List<Effect>
        {
            new("Direct", ModeDirect, false, 0, false), new("Static", ModeStatic, false, 1, false), new("Pulse", ModeBreathing, true, 1, false),
            new("Flash", ModeFlashing, true, 1, false), new("Double Flash", ModeDualFlashing, true, 1, false), new("Color Cycle", ModeColorCycle, true, 0, false),
        };
        if (layout != Fusion2BlackwellLayout.SingleZone)
        {
            effects.AddRange([new("Wave", ModeWave, true, 0, false), new("Gradient", ModeGradient, true, 1, false), new("Color Shift", ModeColorShift, true, 8, true)]);
            if (layout != Fusion2BlackwellLayout.AorusWaterforce) effects.Add(new("Dazzle", ModeDazzle, true, 8, true));
        }
        // OpenRGB gives Claws no colors of its own, so it sends none.
        if (layout == Fusion2BlackwellLayout.AorusMaster5080) effects.Add(new("Claws", ModeClaws, true, 0, true));
        return effects;
    }

    public void SetDirect()
    {
        _active = _effects[0];
        _speed = 0; // OpenRGB leaves Direct's speed at 0
        _own = [];
    }

    public void SetLeds(ReadOnlySpan<Rgb> colors)
    {
        colors[..Math.Min(colors.Length, _leds.Length)].CopyTo(_leds);
        Update();
    }

    public void SetMode(string mode, int speed, IReadOnlyList<Rgb> colors)
    {
        var effect = _effects.FirstOrDefault(effect => string.Equals(effect.Name, mode, StringComparison.OrdinalIgnoreCase));
        if (effect == null) return;
        _active = effect;
        _speed = effect.Speed ? (byte)GpuLighting.Scale(speed, SpeedSlowest, SpeedFastest) : (byte)0;
        _own = effect.OwnColors ? GpuLighting.Fill(colors, effect.Colors) : [];
        if (!effect.OwnColors && effect.Colors > 0 && colors.Count > 0) Array.Fill(_leds, colors[0]);
        Update();
    }

    public void Save()
    {
        var packet = new byte[PacketSize];
        packet[0] = SaveCommand;
        packet[1] = 0x01;
        using (_bus.Hold()) Send(packet);
    }

    bool Master => _layout is Fusion2BlackwellLayout.AorusMaster5080 or Fusion2BlackwellLayout.AorusMaster5090DV2Ice;

    /// <summary>
    /// OpenRGB's DeviceUpdateLEDs. It sends more hardware zones than the card shows, as Gigabyte's own app does,
    /// and keeps the color count from one zone to the next, which this copies so the packets are the same.
    /// </summary>
    void Update()
    {
        var hardwareZones = _layout switch
        {
            Fusion2BlackwellLayout.SingleZone => 1,
            Fusion2BlackwellLayout.Waterforce => 3,
            Fusion2BlackwellLayout.AorusWaterforce => 5,
            _ => 6,
        };
        var count = _own.Length;
        var colors = new Rgb[8];
        using (_bus.Hold())
        {
            for (var hardware = 0; hardware < hardwareZones; hardware++)
            {
                var ui = hardware;
                if (_layout == Fusion2BlackwellLayout.AorusWaterforce)
                {
                    if (hardware == 0) continue; // this card has no zone 0
                    ui = hardware - 1;
                }
                else if (Master && hardware == 5) ui = 4;

                if (ui >= Zones.Count) colors[0] = _leds[^1];
                else
                {
                    var start = Zones.Take(ui).Sum(zone => zone.LedCount);
                    var leds = Zones[ui].LedCount;
                    for (var i = 0; i < leds; i++) colors[i] = _leds[start + i];
                    // A zone whose LEDs differ sends all of their colors.
                    if (colors.AsSpan(0, leds).ContainsAnyExcept(colors[0])) count = leds;
                }
                for (var i = 0; i < Math.Min(count, _own.Length); i++) colors[i] = _own[i];
                SetZone((byte)hardware, _active.Value, colors, (byte)count);
            }
        }
    }

    void SetZone(byte zone, byte mode, Rgb[] colors, byte count)
    {
        var brightness = BrightnessMax; // every effect starts at full brightness in OpenRGB, and Pulse is always sent at it
        if (_layout == Fusion2BlackwellLayout.AorusMaster5090DV2Ice && mode == ModeDirect) mode = ModeStatic;
        var type = mode == ModeDirect ? TypeColor : TypeMode;

        if (count == 0 && zone < HardwareZones) _zoneColors[zone] = colors[0];
        var packet = new byte[PacketSize];
        var color = _zoneColors[zone];
        packet[0] = type;
        packet[1] = 0x01;
        packet[2] = mode;
        packet[3] = _speed;
        packet[4] = brightness;
        packet[5] = color.R;
        packet[6] = color.G;
        packet[7] = color.B;
        packet[9] = zone;
        packet[10] = count;
        var at = Master ? 11 : 12;
        for (var i = 0; i < count; i++, at += 3)
        {
            packet[at] = colors[i].R;
            packet[at + 1] = colors[i].G;
            packet[at + 2] = colors[i].B;
        }
        Send(packet);
    }

    void Send(ReadOnlySpan<byte> packet)
    {
        if (!_bus.WriteRaw(Address, packet)) throw GpuLighting.Silent(Driver, Address);
    }
}
