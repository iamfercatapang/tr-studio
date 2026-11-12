// SPDX-License-Identifier: GPL-2.0-or-later
//
// Driver for the ASUS Aura lighting chip on ROG Strix, TUF and Poseidon graphics cards, and the pieces every
// graphics-card lighting driver here shares.
//
// Ported to C# from the OpenRGB project (https://gitlab.com/CalcProgrammer1/OpenRGB), files
// Controllers/AsusAuraGPUController/AsusAuraGPUController.cpp, AsusAuraGPUController.h,
// AsusAuraGPUControllerDetect.cpp and RGBController_AsusAuraGPU.cpp, by Jan Rettig (Klapstuhl), Adam Honse
// (CalcProgrammer1) and the OpenRGB contributors, licensed GPL-2.0-or-later. The register map, the magic
// value and the effect list are theirs.

namespace TRStudio.Engine;

/// <summary>
/// A graphics card's I2C bus with the transfers some lighting chips need beyond the SMBus ones: plain I2C
/// writes and reads with no register in front, and an I2C block (a register, then the bytes, with no length
/// byte), as OpenRGB's <c>i2c_write_block</c>, <c>i2c_read_block</c> and <c>i2c_smbus_write_i2c_block_data</c>.
/// </summary>
public interface IRawI2cBus : ISmBus
{
    bool WriteRaw(byte address, ReadOnlySpan<byte> data);

    /// <summary>Fills <paramref name="data"/> from the chip; false when nothing answers.</summary>
    bool ReadRaw(byte address, Span<byte> data);

    bool WriteI2cBlockData(byte address, byte command, ReadOnlySpan<byte> data);
}

/// <summary>
/// The lighting chip of one graphics card, found by a driver's <c>Open</c>, which runs the same identity check
/// as OpenRGB's detector and gives null when the chip isn't the one expected, so nothing is ever written to it.
/// </summary>
public interface IGpuLightController
{
    /// <summary>Which driver this is, such as "ASUS Aura GPU".</summary>
    string Driver { get; }
    byte Address { get; }
    IReadOnlyList<LightZone> Zones { get; }
    /// <summary>Direct first, then the chip's own effects.</summary>
    IReadOnlyList<LightMode> Modes { get; }
    int LedCount { get; }

    /// <summary>Hands the LEDs over to the colors sent from the PC.</summary>
    void SetDirect();

    /// <summary>One color for each LED, zone after zone.</summary>
    void SetLeds(ReadOnlySpan<Rgb> colors);

    /// <summary>Starts one of the chip's own effects. Speed runs 0 to 100. An unknown name does nothing.</summary>
    void SetMode(string mode, int speed, IReadOnlyList<Rgb> colors);

    /// <summary>Makes the chip keep what it shows now after the PC powers off. TR Studio never calls it on its own.</summary>
    void Save();
}

static class GpuLighting
{
    /// <summary>A 0 to 100 speed on the chip's own scale.</summary>
    public static int Scale(int speed, int slowest, int fastest) =>
        slowest + (int)Math.Round((fastest - slowest) * Math.Clamp(speed, 0, 100) / 100.0);

    /// <summary>
    /// Exactly <paramref name="count"/> colors from what the page sent: the list repeats when it is shorter,
    /// as an effect with several color slots always sends all of them.
    /// </summary>
    public static Rgb[] Fill(IReadOnlyList<Rgb> colors, int count)
    {
        var filled = new Rgb[count];
        for (var i = 0; i < count; i++) filled[i] = colors.Count == 0 ? Rgb.Black : colors[i % colors.Count];
        return filled;
    }

    public static IOException Silent(string driver, byte address) =>
        new($"The {driver} lighting chip at 0x{address:X2} stopped answering.");
}

/// <summary>
/// ASUS Aura on a graphics card: one color for the whole card, one register for each channel. OpenRGB's Direct
/// is the Static effect with colors sent from the PC; the chip has no Off, so Off is black on Static.
/// </summary>
public sealed class AsusAuraGpuController : IGpuLightController
{
    public const ushort Magic = 0x1589;
    const byte RegRed = 0x04, RegGreen = 0x05, RegBlue = 0x06, RegMode = 0x07, RegApply = 0x0E;
    const byte RegMagicHigh = 0x20, RegMagicLow = 0x21, Apply = 0x01;
    const byte ModeStatic = 1;

    /// <summary>The chip's effects by number, in OpenRGB's order. Off isn't a real mode on the chip.</summary>
    static readonly (string Name, byte Value, int Colors)[] Effects =
        [("Off", 0, 0), ("Static", ModeStatic, 1), ("Breathing", 2, 1), ("Flashing", 3, 1), ("Spectrum Cycle", 4, 0)];

    readonly ISmBus _bus;

    AsusAuraGpuController(ISmBus bus, byte address)
    {
        _bus = bus;
        Address = address;
    }

    public string Driver => "ASUS Aura GPU";
    public byte Address { get; }
    public IReadOnlyList<LightZone> Zones { get; } = [new LightZone("GPU", 1, 1, 1)];
    public int LedCount => 1;

    public IReadOnlyList<LightMode> Modes { get; } =
        [new LightMode("Direct", false, 0), .. Effects.Select(effect => new LightMode(effect.Name, false, effect.Colors))];

    /// <summary>
    /// Whether an Aura chip answers at this address: registers 0x20 and 0x21 read 0x15 and 0x89, the magic every
    /// Aura graphics card holds. Only reads. On AMD cards OpenRGB also checks that the bus is the card's own I2C
    /// bus; that check belongs to whoever opens the bus.
    /// </summary>
    public static bool IsAt(ISmBus bus, byte address)
    {
        var high = bus.ReadByteData(address, RegMagicHigh);
        var low = bus.ReadByteData(address, RegMagicLow);
        return high >= 0 && low >= 0 && ((high << 8) | low) == Magic;
    }

    /// <summary>The chip at this address, or null when it isn't an Aura chip.</summary>
    public static AsusAuraGpuController? Open(ISmBus bus, byte address) => IsAt(bus, address) ? new AsusAuraGpuController(bus, address) : null;

    public void SetDirect()
    {
        using (_bus.Hold()) Write(RegMode, ModeStatic);
    }

    public void SetLeds(ReadOnlySpan<Rgb> colors)
    {
        if (colors.Length == 0) return;
        using (_bus.Hold()) WriteColor(colors[0]);
    }

    public void SetMode(string mode, int speed, IReadOnlyList<Rgb> colors)
    {
        var found = Array.FindIndex(Effects, effect => string.Equals(effect.Name, mode, StringComparison.OrdinalIgnoreCase));
        if (found < 0) return;
        var effect = Effects[found];
        using (_bus.Hold())
        {
            if (effect.Value == 0)
            {
                WriteColor(Rgb.Black);
                Write(RegMode, ModeStatic);
                return;
            }
            Write(RegMode, effect.Value);
            // OpenRGB only sends Apply after a color on models where Apply doesn't also save, and it knows of none.
            if (effect.Colors > 0 && colors.Count > 0) WriteColor(colors[0]);
        }
    }

    public void Save()
    {
        using (_bus.Hold()) Write(RegApply, Apply);
    }

    void WriteColor(Rgb color)
    {
        Write(RegRed, color.R);
        Write(RegGreen, color.G);
        Write(RegBlue, color.B);
    }

    void Write(byte register, byte value)
    {
        if (!_bus.WriteByteData(Address, register, value)) throw GpuLighting.Silent(Driver, Address);
    }
}
