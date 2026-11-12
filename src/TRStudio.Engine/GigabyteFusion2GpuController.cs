// SPDX-License-Identifier: GPL-2.0-or-later
//
// Driver for the RGB Fusion 2 lighting chip on Gigabyte and AORUS graphics cards (RTX 20 to RTX 40 series,
// Radeon RX 5000 to 7000 series).
//
// Ported to C# from the OpenRGB project (https://gitlab.com/CalcProgrammer1/OpenRGB), files
// Controllers/GigabyteRGBFusion2GPUController/GigabyteRGBFusion2GPUController.cpp,
// GigabyteRGBFusion2GPUController.h, GigabyteRGBFusion2GPUControllerDetect.cpp,
// RGBController_GigabyteRGBFusion2GPU.cpp and RGBController_GigabyteRGBFusion2GPU.h, by Adam Honse
// (CalcProgrammer1) and the OpenRGB contributors, licensed GPL-2.0-or-later. The packet layouts, the
// identity query and the effect list are theirs.

namespace TRStudio.Engine;

/// <summary>
/// RGB Fusion 2 on a graphics card: five zones of one color each, set with eight-byte I2C packets that carry
/// no register in front. Each zone gets an effect packet and then a color packet; zones 1 and 2 share one color
/// packet, so the chip is always sent both of their colors.
/// </summary>
public sealed class GigabyteFusion2GpuController : IGpuLightController
{
    const byte RegColor = 0x40, RegMode = 0x88, RegColorLeftMid = 0xB0, RegColorRight = 0xB1, Query = 0xAB, SaveCommand = 0xAA;
    const byte ModeStatic = 0x01, ModeBreathing = 0x02, ModeColorCycle = 0x03, ModeFlashing = 0x04, ModeGradient = 0x05;
    const byte ModeColorShift = 0x06, ModeWave = 0x07, ModeDualFlashing = 0x08, ModeTricolor = 0x0B;
    const byte SpeedSlowest = 0x00, SpeedFastest = 0x05, BrightnessMax = 0x63;
    const int ZoneCount = 5, PacketSize = 8;

    /// <summary>Direct, then the chip's effects in OpenRGB's order. Colors: per-zone colors (1) or colors of the effect's own.</summary>
    static readonly (string Name, byte Value, bool Speed, int Colors, bool OwnColors)[] Effects =
    [
        ("Direct", ModeStatic, false, 0, false), ("Pulse", ModeBreathing, true, 1, false), ("Flash", ModeFlashing, true, 1, false),
        ("Double Flash", ModeDualFlashing, true, 1, false), ("Color Cycle", ModeColorCycle, true, 0, false),
        ("Gradient", ModeGradient, true, 1, false), ("Wave", ModeWave, true, 0, false),
        ("Color Shift", ModeColorShift, true, 8, true), ("Tricolor", ModeTricolor, true, 3, true),
    ];

    readonly IRawI2cBus _bus;
    readonly Rgb[] _leds = new Rgb[ZoneCount];
    /// <summary>The colors last sent to zones 1 to 4, kept because zones 1 and 2 go out in one packet.</summary>
    readonly Rgb[] _zoneColors = new Rgb[4];

    GigabyteFusion2GpuController(IRawI2cBus bus, byte address)
    {
        _bus = bus;
        Address = address;
    }

    public string Driver => "Gigabyte RGB Fusion 2 GPU";
    public byte Address { get; }
    public IReadOnlyList<LightZone> Zones { get; } = Enumerable.Range(1, ZoneCount).Select(zone => new LightZone($"GPU zone {zone}", 1, 1, 1)).ToList();
    public int LedCount => ZoneCount;

    public IReadOnlyList<LightMode> Modes { get; } = Effects.Select(effect => new LightMode(effect.Name, effect.Speed, effect.Colors)).ToList();

    /// <summary>
    /// Whether an RGB Fusion 2 chip answers at this address, as OpenRGB's detector asks: the identity query
    /// (0xAB and seven zeros) is written, and four bytes are read back. A chip answers 0xAB and its version
    /// bytes; an address that echoes 0xAB in every byte is a display's mirror, not a chip. The query is the only
    /// write, and it changes nothing on the chip. On AMD cards OpenRGB also checks that the bus is the card's own
    /// I2C bus; that check belongs to whoever opens the bus.
    /// </summary>
    public static bool IsAt(IRawI2cBus bus, byte address)
    {
        Span<byte> query = stackalloc byte[PacketSize];
        query[0] = Query;
        bus.WriteRaw(address, query);
        Span<byte> reply = stackalloc byte[4];
        if (!bus.ReadRaw(address, reply)) return false;
        return reply[0] == Query && !(reply[1] == Query && reply[2] == Query && reply[3] == Query);
    }

    public static GigabyteFusion2GpuController? Open(IRawI2cBus bus, byte address) => IsAt(bus, address) ? new GigabyteFusion2GpuController(bus, address) : null;

    (string Name, byte Value, bool Speed, int Colors, bool OwnColors) _active = Effects[0];
    byte _speed;
    Rgb[] _own = [];

    public void SetDirect()
    {
        _active = Effects[0];
        _speed = 0; // OpenRGB leaves Direct's speed at 0
        _own = [];
    }

    public void SetLeds(ReadOnlySpan<Rgb> colors)
    {
        for (var i = 0; i < Math.Min(colors.Length, ZoneCount); i++) _leds[i] = colors[i];
        Update();
    }

    public void SetMode(string mode, int speed, IReadOnlyList<Rgb> colors)
    {
        var found = Array.FindIndex(Effects, effect => string.Equals(effect.Name, mode, StringComparison.OrdinalIgnoreCase));
        if (found < 0) return;
        _active = Effects[found];
        _speed = _active.Speed ? (byte)GpuLighting.Scale(speed, SpeedSlowest, SpeedFastest) : (byte)0;
        _own = _active.OwnColors ? GpuLighting.Fill(colors, _active.Colors) : [];
        if (!_active.OwnColors && _active.Colors > 0 && colors.Count > 0) Array.Fill(_leds, colors[0]);
        Update();
    }

    public void Save()
    {
        using (_bus.Hold()) Send([SaveCommand, 0, 0, 0, 0, 0, 0, 0]);
    }

    /// <summary>OpenRGB's DeviceUpdateLEDs: every zone in turn, with the effect's own colors when it has them.</summary>
    void Update()
    {
        using (_bus.Hold())
        {
            for (var zone = 0; zone < ZoneCount; zone++)
            {
                var colors = new Rgb[8];
                colors[0] = _leds[zone];
                for (var i = 0; i < _own.Length; i++) colors[i] = _own[i];
                SetZone((byte)zone, _active.Value, BrightnessMax, colors, (byte)_own.Length);
            }
        }
    }

    void SetZone(byte zone, byte mode, byte brightness, Rgb[] colors, byte count)
    {
        switch (mode)
        {
            case ModeStatic or ModeFlashing or ModeDualFlashing or ModeWave:
                SetEffect(zone, mode, brightness, colors, 0x00);
                break;
            case ModeBreathing:
                SetEffect(zone, mode, BrightnessMax, colors, 0x00);
                break;
            case ModeGradient:
                SetEffect(zone, mode, brightness, colors, 0x08);
                break;
            case ModeColorCycle:
                Send([RegMode, mode, _speed, brightness, 0x00, (byte)(zone + 1), 0, 0]);
                break;
            case ModeColorShift or ModeTricolor:
                Send([RegMode, mode, _speed, brightness, mode == ModeColorShift ? count : (byte)0x08, (byte)(zone + 1), 0, 0]);
                var bank = (byte)(0xB0 + zone * 4);
                for (var i = 0; i < 4; i++)
                {
                    Rgb first = colors[i * 2], second = colors[i * 2 + 1];
                    Send([(byte)(bank + i), mode, first.R, first.G, first.B, second.R, second.G, second.B]);
                }
                break;
        }
    }

    /// <summary>OpenRGB's SetMode: the effect packet, then the zone's color packet.</summary>
    void SetEffect(byte zone, byte mode, byte brightness, Rgb[] colors, byte flag)
    {
        if (zone < _zoneColors.Length) _zoneColors[zone] = colors[0];
        Send([RegMode, mode, _speed, brightness, flag, (byte)(zone + 1), 0, 0]);
        switch (zone)
        {
            case 0 or 1:
                Rgb left = _zoneColors[0], middle = _zoneColors[1];
                Send([RegColorLeftMid, mode, left.R, left.G, left.B, middle.R, middle.G, middle.B]);
                break;
            case 2:
                var right = _zoneColors[2];
                Send([RegColorRight, mode, right.R, right.G, right.B, 0, 0, 0]);
                break;
            default:
                Send([RegColor, colors[0].R, colors[0].G, colors[0].B, (byte)(zone + 1), 0, 0, 0]);
                break;
        }
    }

    void Send(ReadOnlySpan<byte> packet)
    {
        if (!_bus.WriteRaw(Address, packet)) throw GpuLighting.Silent(Driver, Address);
    }
}
