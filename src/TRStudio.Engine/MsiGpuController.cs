// SPDX-License-Identifier: GPL-2.0-or-later
//
// Drivers for the lighting chips on MSI graphics cards: the first kind (Gaming X and Trio cards up to the
// RTX 30 series) and the second, ITE9 kind (RTX 40 and 50 series).
//
// Ported to C# from the OpenRGB project (https://gitlab.com/CalcProgrammer1/OpenRGB), files
// Controllers/MSIGPUController/MSIGPUController/MSIGPUController.cpp, MSIGPUController.h,
// MSIGPUControllerDetect.cpp, RGBController_MSIGPU.cpp, and
// Controllers/MSIGPUController/MSIGPUv2Controller/MSIGPUv2Controller.cpp, MSIGPUv2Controller.h,
// MSIGPUv2ControllerDetect.cpp, RGBController_MSIGPUv2.cpp, by Wojciech Lazarski, Adam Honse
// (CalcProgrammer1) and the OpenRGB contributors, licensed GPL-2.0-or-later. The register maps, the effect
// lists, the speed values and the write sequences are theirs.

namespace TRStudio.Engine;

/// <summary>
/// The first kind of MSI graphics-card lighting chip: one color for the card, a brightness, a speed and an
/// effect, each in a register of its own. OpenRGB's Direct is the Static effect.
/// </summary>
public sealed class MsiGpuController : IGpuLightController
{
    const byte RegBrightness = 0x36, RegSpeed = 0x38, RegUnknown = 0x26, RegMode = 0x22, RegSave = 0x3F;
    const byte RegR1 = 0x30, RegG1 = 0x31, RegB1 = 0x32, RegR2 = 0x27, RegG2 = 0x28, RegB2 = 0x29, RegR3 = 0x2A, RegG3 = 0x2B, RegB3 = 0x2C;
    const byte ModeOff = 0x01, ModeStatic = 0x13, ModeFadeIn = 0x14;
    const int BrightnessMax = 5, BrightnessStep = 20;
    /// <summary>The speed register's values from slowest to fastest.</summary>
    static readonly byte[] Speeds = [0x04, 0x02, 0x01];

    /// <summary>The chip's effects in OpenRGB's order. Fade In uses the second and third colors; the rest the first.</summary>
    static readonly (string Name, byte Value, bool Speed, int Colors)[] Effects =
    [
        ("Rainbow", 0x08, true, 0), ("Raindrop", 0x1A, true, 1), ("Magic", 0x07, true, 0), ("Patrolling", 0x05, true, 1),
        ("Streaming", 0x06, true, 0), ("Lightning", 0x15, true, 0), ("Wave", 0x1F, true, 1), ("Meteor", 0x16, true, 1),
        ("Stack", 0x0D, true, 1), ("Rhythm", 0x0B, true, 1), ("Flowing", 0x09, true, 1), ("Whirling", 0x0F, true, 1),
        ("Twisting", 0x11, true, 1), ("Laminating", 0x1D, true, 1), ("Fadein", ModeFadeIn, true, 2), ("Breathing", 0x04, true, 1),
        ("Flashing", 0x02, true, 1), ("Doubleflashing", 0x03, true, 1), ("Off", ModeOff, false, 0),
    ];

    readonly ISmBus _bus;

    MsiGpuController(ISmBus bus, byte address)
    {
        _bus = bus;
        Address = address;
    }

    public string Driver => "MSI GPU";
    public byte Address { get; }
    public IReadOnlyList<LightZone> Zones { get; } = [new LightZone("GPU", 1, 1, 1)];
    public int LedCount => 1;

    public IReadOnlyList<LightMode> Modes { get; } =
        [new LightMode("Direct", false, 0), .. Effects.Select(effect => new LightMode(effect.Name, effect.Speed, effect.Colors))];

    /// <summary>
    /// Whether something answers at this address: a read of the effect register. OpenRGB's detector reads
    /// nothing from the chip and trusts the card's PCI ids and the bus (port 1 on NVIDIA, the card's own I2C bus
    /// on AMD); this read is the least that keeps an empty address from being written to. Only reads.
    /// </summary>
    public static bool IsAt(ISmBus bus, byte address) => bus.ReadByteData(address, RegMode) >= 0;

    public static MsiGpuController? Open(ISmBus bus, byte address) => IsAt(bus, address) ? new MsiGpuController(bus, address) : null;

    public void SetDirect()
    {
        using (_bus.Hold())
        {
            Write(RegBrightness, BrightnessMax * BrightnessStep);
            Write(RegMode, ModeStatic);
        }
    }

    public void SetLeds(ReadOnlySpan<Rgb> colors)
    {
        if (colors.Length == 0) return;
        using (_bus.Hold())
        {
            Write(RegUnknown, 0x00);
            WriteColor(RegR1, RegG1, RegB1, colors[0]);
        }
    }

    public void SetMode(string mode, int speed, IReadOnlyList<Rgb> colors)
    {
        var found = Array.FindIndex(Effects, effect => string.Equals(effect.Name, mode, StringComparison.OrdinalIgnoreCase));
        if (found < 0) return;
        var effect = Effects[found];
        using (_bus.Hold())
        {
            if (effect.Value == ModeOff)
            {
                // OpenRGB's Off has no brightness, and its mode update writes nothing without one, so Off never
                // reached the chip. Here it writes the chip's own Off value.
                Write(RegMode, ModeOff);
                return;
            }
            Write(RegBrightness, BrightnessMax * BrightnessStep);
            Write(RegSpeed, Speeds[GpuLighting.Scale(speed, 0, Speeds.Length - 1)]);
            Write(RegMode, effect.Value);
            if (effect.Colors == 0 || colors.Count == 0) return;
            Write(RegUnknown, 0x00);
            if (effect.Value == ModeFadeIn)
            {
                var pair = GpuLighting.Fill(colors, 2);
                WriteColor(RegR2, RegG2, RegB2, pair[0]);
                WriteColor(RegR3, RegG3, RegB3, pair[1]);
            }
            else WriteColor(RegR1, RegG1, RegB1, colors[0]);
        }
    }

    public void Save()
    {
        using (_bus.Hold()) Write(RegSave, 0x01);
    }

    void WriteColor(byte red, byte green, byte blue, Rgb color)
    {
        Write(red, color.R);
        Write(green, color.G);
        Write(blue, color.B);
    }

    void Write(byte register, byte value)
    {
        if (!_bus.WriteByteData(Address, register, value)) throw GpuLighting.Silent(Driver, Address);
    }
}

/// <summary>
/// The second, ITE9 kind of MSI graphics-card lighting chip. Every change parks the chip in an idle mode, sends
/// the colors (some as three-byte blocks stored blue, green, red), the brightness and speed, then the effect.
/// The chip needs a pause after every write (20 ms in OpenRGB), so a frame takes about 140 ms: send colors from
/// a thread of their own.
/// </summary>
public sealed class MsiGpuV2Controller : IGpuLightController
{
    const byte RegBrightness = 0x36, RegSpeed = 0x38, RegUnknown = 0x2E, RegMode = 0x22, RegSave = 0x3F, RegControl = 0x46;
    const byte RegR1 = 0x30, RegG1 = 0x31, RegB1 = 0x32, RegBlock1 = 0x27, RegBlock2 = 0x28, RegBlock3 = 0x29;
    const byte ControlRight = 0x00, ControlLeft = 0x02, ControlNonRgb = 0x01;
    const byte ModeIdle = 0x1C, ModeOff = 0x01, ModeRainbow = 0x08, ModeStatic = 0x13, ModeRaindrop = 0x1A, ModeMagic = 0x07;
    const byte ModePatrolling = 0x05, ModeStreaming = 0x06, ModeLightning = 0x15, ModeMeteor = 0x16, ModeStack = 0x0D;
    const byte ModeRhythm = 0x0C, ModeFlowing = 0x09, ModeWhirling = 0x0F, ModeTwisting = 0x11, ModeLaminating = 0x1D;
    const byte ModeFadeIn = 0x14, ModeBreathing = 0x04, ModeFlashing = 0x02, ModeDoubleFlashing = 0x03, ModeRhythmColor = 0x0B;
    const byte ModeMarquee = 0x18, ModeArrow = 0x21;
    const int BrightnessMax = 5, BrightnessStep = 20;
    static readonly byte[] Speeds = [0x04, 0x02, 0x01];
    public static readonly TimeSpan DefaultWriteDelay = TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// How an effect takes its colors. PerLed: the card's color. ModeColors: colors of its own. Random: none
    /// (with PerLedFlag and RandomFlag also set, OpenRGB starts it at the next mode value, its rainbow variant).
    /// </summary>
    enum Kind { None, PerLed, ModeColors, Random }

    sealed record Effect(string Name, byte Value, bool Speed, bool Brightness, Kind Colors, int ModeColorCount, bool PerLedFlag, bool RandomFlag);

    /// <summary>Cards that lack Stack and Rhythm, by PCI device and subsystem device (OpenRGB's list).</summary>
    static readonly (ushort Device, ushort SubDevice)[] WithoutStack =
        [(0x2805, 0x5171), (0x2783, 0x513F), (0x2705, 0x5139), (0x2702, 0x5111), (0x2704, 0x511A), (0x2702, 0x511A)];

    /// <summary>The RTX 50 series, which has more effects and a rainbow variant of the single-color ones.</summary>
    static readonly ushort[] Rtx50 = [0x2D05, 0x2D04, 0x2F04, 0x2C05, 0x2C02, 0x2B85, 0x2B87, 0x2B8C];

    readonly IRawI2cBus _bus;
    readonly List<Effect> _effects;
    readonly TimeSpan _delay;
    readonly bool _rainbowVariants;
    Effect _active;
    Rgb _color = new(0xFF, 0, 0); // OpenRGB's starting color; the chip's own can't be read

    MsiGpuV2Controller(IRawI2cBus bus, byte address, ushort pciDevice, ushort subDevice, TimeSpan delay)
    {
        _bus = bus;
        Address = address;
        _delay = delay;
        _rainbowVariants = Rtx50.Contains(pciDevice);
        _effects = BuildEffects(pciDevice, subDevice, _rainbowVariants);
        _active = _effects[0];
        Modes = [new LightMode("Direct", false, 0), .. _effects.Where(effect => effect.Name != "Direct").Select(effect => new LightMode(effect.Name, effect.Speed,
            effect.Colors switch { Kind.PerLed => 1, Kind.ModeColors => effect.ModeColorCount, _ => 0 }))];
    }

    public string Driver => "MSI GPU v2";
    public byte Address { get; }
    public IReadOnlyList<LightZone> Zones { get; } = [new LightZone("GPU", 1, 1, 1)];
    public IReadOnlyList<LightMode> Modes { get; }
    public int LedCount => 1;

    /// <summary>Whether something answers at this address. As for the first kind, OpenRGB checks only the card's PCI ids and the bus. Only reads.</summary>
    public static bool IsAt(ISmBus bus, byte address) => bus.ReadByteData(address, RegMode) >= 0;

    /// <summary>The chip at this address. The card's PCI device and subsystem device ids choose its effect list.</summary>
    public static MsiGpuV2Controller? Open(IRawI2cBus bus, byte address, ushort pciDevice, ushort subDevice, TimeSpan? writeDelay = null) =>
        IsAt(bus, address) ? new MsiGpuV2Controller(bus, address, pciDevice, subDevice, writeDelay ?? DefaultWriteDelay) : null;

    static List<Effect> BuildEffects(ushort device, ushort subDevice, bool rainbowVariants)
    {
        Effect PerLed(string name, byte value) => new(name, value, true, true, Kind.PerLed, 0, true, false);
        Effect Random(string name, byte value) => new(name, value, true, true, Kind.Random, 0, false, true);
        Effect Own(string name, byte value, int count) => new(name, value, true, true, Kind.ModeColors, count, false, false);

        var effects = new List<Effect>
        {
            new("Off", ModeOff, false, false, Kind.None, 0, false, false),
            new("Direct", ModeStatic, false, true, Kind.PerLed, 0, true, false),
            Random("Rainbow Wave", ModeRainbow), Random("Magic", ModeMagic), Own("Color Cycle", ModeMagic, 3),
            PerLed("Patrolling", ModePatrolling), Random("Streaming", ModeStreaming), Random("Lightning", ModeLightning),
            Own("Wave", ModeRainbow, 3), PerLed("Meteor", ModeMeteor),
        };
        if (!WithoutStack.Contains((device, subDevice)))
        {
            effects.Add(PerLed("Stack", ModeStack));
            effects.Add(PerLed("Rhythm", ModeRhythm));
        }
        effects.AddRange([PerLed("Flowing", ModeFlowing), PerLed("Whirling", ModeWhirling), Own("Fade In", ModeFadeIn, 2),
            Own("Breathing", ModeBreathing, 2), PerLed("Flashing", ModeFlashing), PerLed("Double Flashing", ModeDoubleFlashing)]);
        if (!rainbowVariants) return effects;

        for (var i = 0; i < effects.Count; i++)
        {
            var effect = effects[i];
            if (effect.Value == ModeRhythm) effects[i] = effect with { Value = ModeRhythmColor, RandomFlag = true, Colors = Kind.Random };
            else if (effect.Value is ModeFlowing or ModeWhirling or ModeStack or ModeMeteor) effects[i] = effect with { RandomFlag = true };
        }
        effects.AddRange([PerLed("Twisting", ModeTwisting) with { RandomFlag = true }, PerLed("Laminating", ModeLaminating) with { RandomFlag = true },
            PerLed("Marquee", ModeMarquee) with { RandomFlag = true }, Own("Arrow", ModeArrow, 2)]);
        return effects;
    }

    public void SetDirect()
    {
        _active = _effects.First(effect => effect.Name == "Direct");
        Update(Speeds[1], []);
    }

    public void SetLeds(ReadOnlySpan<Rgb> colors)
    {
        if (colors.Length == 0) return;
        _color = colors[0];
        Update(Speeds[1], []);
    }

    public void SetMode(string mode, int speed, IReadOnlyList<Rgb> colors)
    {
        var effect = _effects.FirstOrDefault(effect => string.Equals(effect.Name, mode, StringComparison.OrdinalIgnoreCase));
        if (effect == null) return;
        _active = effect;
        if (effect.Colors == Kind.PerLed && colors.Count > 0) _color = colors[0];
        var own = effect.Colors == Kind.ModeColors ? GpuLighting.Fill(colors, effect.ModeColorCount) : [];
        Update(Speeds[GpuLighting.Scale(speed, 0, Speeds.Length - 1)], own);
    }

    public void Save()
    {
        using (_bus.Hold()) Write(RegSave, 0x00);
    }

    /// <summary>OpenRGB's DeviceUpdateAll for the active effect. Its default direction is left.</summary>
    void Update(byte speed, Rgb[] own)
    {
        var effect = _active;
        var modeColors = effect.Colors == Kind.ModeColors;
        using (_bus.Hold())
        {
            switch (effect.Value)
            {
                case ModeRainbow:
                    Write(RegControl, modeColors ? (byte)(ControlLeft | ControlNonRgb) : ControlLeft);
                    Idle();
                    if (modeColors) Blocks(own, 3);
                    break;
                case ModeMagic:
                    Idle();
                    Write(RegControl, modeColors ? ControlNonRgb : ControlRight);
                    if (modeColors) Blocks(own, 3);
                    break;
                case ModeBreathing or ModeFadeIn or ModeArrow:
                    Idle();
                    Blocks(own, 2);
                    break;
                case ModeFlowing or ModeWhirling or ModePatrolling or ModeFlashing or ModeDoubleFlashing or ModeStatic or ModeRhythm or ModeStack or ModeMeteor:
                    Idle();
                    Write(RegR1, _color.R);
                    Write(RegG1, _color.G);
                    Write(RegB1, _color.B);
                    break;
                case ModeTwisting or ModeLaminating or ModeMarquee or ModeRhythmColor:
                    Idle();
                    Block(RegBlock1, _color);
                    break;
                case ModeStreaming or ModeLightning or ModeOff:
                    Idle();
                    break;
                default:
                    Idle();
                    Write(RegMode, ModeOff);
                    break;
            }
            if (effect.Brightness)
            {
                Write(RegBrightness, BrightnessMax * BrightnessStep);
                if (effect.Speed) Write(RegSpeed, speed);
            }
            var rainbow = _rainbowVariants && effect.PerLedFlag && effect.RandomFlag && effect.Colors == Kind.Random;
            Write(RegMode, (byte)(rainbow ? effect.Value + 1 : effect.Value));
        }
    }

    void Idle()
    {
        Write(RegUnknown, 0x00);
        Write(RegMode, ModeIdle);
    }

    void Blocks(Rgb[] colors, int count)
    {
        byte[] registers = [RegBlock1, RegBlock2, RegBlock3];
        for (var i = 0; i < count; i++) Block(registers[i], i < colors.Length ? colors[i] : Rgb.Black);
    }

    void Block(byte register, Rgb color)
    {
        if (!_bus.WriteI2cBlockData(Address, register, [color.B, color.G, color.R])) throw GpuLighting.Silent(Driver, Address);
        Pause();
    }

    void Write(byte register, byte value)
    {
        if (!_bus.WriteByteData(Address, register, value)) throw GpuLighting.Silent(Driver, Address);
        Pause();
    }

    void Pause()
    {
        if (_delay > TimeSpan.Zero) Thread.Sleep(_delay);
    }
}
