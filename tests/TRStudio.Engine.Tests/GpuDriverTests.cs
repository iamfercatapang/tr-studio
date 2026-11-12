// SPDX-License-Identifier: GPL-2.0-or-later
//
// The expected register writes and packets below were worked out by hand from OpenRGB's
// AsusAuraGPUController, MSIGPUController, MSIGPUv2Controller, GigabyteRGBFusion2GPUController and
// GigabyteRGBFusion2BlackwellGPUController (https://gitlab.com/CalcProgrammer1/OpenRGB), GPL-2.0-or-later.

using TRStudio.Engine;
using Xunit;

public sealed class GpuDriverTests
{
    /// <summary>
    /// A graphics card's I2C bus that records every transfer. Registers answer reads at the addresses given;
    /// a plain I2C read gives <see cref="RawReply"/>, or nothing when it is null.
    /// </summary>
    sealed class FakeBus : IRawI2cBus
    {
        public readonly Dictionary<(byte Address, byte Register), byte> Registers = [];
        public byte[]? RawReply;
        public readonly List<string> Writes = [];
        public readonly List<string> Reads = [];
        public int Held;

        public string Name => "Fake GPU bus";
        public void Dispose() { }

        sealed class Release(FakeBus bus) : IDisposable
        {
            public void Dispose() => bus.Held--;
        }

        public IDisposable Hold()
        {
            Held++;
            return new Release(this);
        }

        public int ReadByte(byte address)
        {
            Reads.Add($"{address:X2}");
            return Registers.Keys.Any(key => key.Address == address) ? 0 : -1;
        }

        public int ReadByteData(byte address, byte command)
        {
            Reads.Add($"{address:X2} {command:X2}");
            return Registers.TryGetValue((address, command), out var value) ? value : -1;
        }

        public bool WriteByteData(byte address, byte command, byte value) { Writes.Add($"{address:X2} {command:X2}={value:X2}"); return true; }
        public bool WriteWordData(byte address, byte command, ushort value) { Writes.Add($"{address:X2} {command:X2}={value:X4}"); return true; }
        public bool WriteBlockData(byte address, byte command, ReadOnlySpan<byte> data) { Writes.Add($"{address:X2} {command:X2} block {Convert.ToHexString(data)}"); return true; }
        public bool WriteI2cBlockData(byte address, byte command, ReadOnlySpan<byte> data) { Writes.Add($"{address:X2} {command:X2} i2c {Convert.ToHexString(data)}"); return true; }
        public bool WriteRaw(byte address, ReadOnlySpan<byte> data) { Writes.Add($"{address:X2} raw {Convert.ToHexString(data)}"); return true; }

        public bool ReadRaw(byte address, Span<byte> data)
        {
            Reads.Add($"{address:X2} raw");
            if (RawReply == null) return false;
            RawReply.AsSpan(0, data.Length).CopyTo(data);
            return true;
        }
    }

    static readonly Rgb Red = new(0xFF, 0x00, 0x00), Teal = new(0x12, 0x34, 0x56), Gold = new(0xAA, 0xBB, 0xCC);

    /// <summary>A 64-byte packet written out from its first bytes, the rest zero.</summary>
    static string Packet64(params byte[] start)
    {
        var packet = new byte[64];
        start.CopyTo(packet, 0);
        return Convert.ToHexString(packet);
    }

    // ASUS Aura GPU

    [Fact]
    public void AuraFailingIdentityWritesNothing()
    {
        var bus = new FakeBus();
        bus.Registers[(0x29, 0x20)] = 0x15;
        bus.Registers[(0x29, 0x21)] = 0x88; // one off the magic
        Assert.Null(AsusAuraGpuController.Open(bus, 0x29));
        Assert.Null(AsusAuraGpuController.Open(bus, 0x2A)); // nothing there at all
        Assert.Empty(bus.Writes);
        Assert.Equal(["29 20", "29 21", "2A 20", "2A 21"], bus.Reads);
    }

    static (FakeBus Bus, AsusAuraGpuController Aura) Aura()
    {
        var bus = new FakeBus();
        bus.Registers[(0x2A, 0x20)] = 0x15;
        bus.Registers[(0x2A, 0x21)] = 0x89;
        return (bus, AsusAuraGpuController.Open(bus, 0x2A)!);
    }

    [Fact]
    public void AuraDirectColorIsStaticModeThenThreeChannelRegisters()
    {
        var (bus, aura) = Aura();
        Assert.Empty(bus.Writes);
        aura.SetDirect();
        aura.SetLeds([Teal]);
        Assert.Equal(["2A 07=01", "2A 04=12", "2A 05=34", "2A 06=56"], bus.Writes);
        Assert.Equal(0, bus.Held);
    }

    [Fact]
    public void AuraEffectsMatchOpenRgb()
    {
        var (bus, aura) = Aura();
        aura.SetMode("Breathing", 50, [Red]);
        aura.SetMode("Off", 0, []);
        aura.SetMode("Spectrum Cycle", 0, [Red]);
        aura.SetMode("No such effect", 0, [Red]);
        Assert.Equal(["2A 07=02", "2A 04=FF", "2A 05=00", "2A 06=00", "2A 04=00", "2A 05=00", "2A 06=00", "2A 07=01", "2A 07=04"], bus.Writes);
        Assert.Equal(["Direct", "Off", "Static", "Breathing", "Flashing", "Spectrum Cycle"], aura.Modes.Select(mode => mode.Name));
    }

    // MSI GPU, first kind

    [Fact]
    public void MsiFailingIdentityWritesNothing()
    {
        var bus = new FakeBus();
        Assert.Null(MsiGpuController.Open(bus, 0x68));
        Assert.Null(MsiGpuV2Controller.Open(bus, 0x68, 0x2704, 0x5110, TimeSpan.Zero));
        Assert.Empty(bus.Writes);
    }

    static FakeBus MsiBus()
    {
        var bus = new FakeBus();
        bus.Registers[(0x68, 0x22)] = 0x13;
        return bus;
    }

    [Fact]
    public void MsiDirectColorAndEffectMatchOpenRgb()
    {
        var bus = MsiBus();
        var msi = MsiGpuController.Open(bus, 0x68)!;
        msi.SetDirect();
        msi.SetLeds([Teal]);
        Assert.Equal(["68 36=64", "68 22=13", "68 26=00", "68 30=12", "68 31=34", "68 32=56"], bus.Writes);

        bus.Writes.Clear();
        msi.SetMode("Breathing", 100, [Red]);
        Assert.Equal(["68 36=64", "68 38=01", "68 22=04", "68 26=00", "68 30=FF", "68 31=00", "68 32=00"], bus.Writes);

        bus.Writes.Clear();
        msi.SetMode("Fadein", 0, [Teal, Gold]);
        Assert.Equal(["68 36=64", "68 38=04", "68 22=14", "68 26=00", "68 27=12", "68 28=34", "68 29=56", "68 2A=AA", "68 2B=BB", "68 2C=CC"], bus.Writes);

        bus.Writes.Clear();
        msi.SetMode("Rainbow", 50, []);
        Assert.Equal(["68 36=64", "68 38=02", "68 22=08"], bus.Writes);
    }

    // MSI GPU, second kind

    [Fact]
    public void MsiV2DirectColorMatchesOpenRgb()
    {
        var bus = MsiBus();
        var msi = MsiGpuV2Controller.Open(bus, 0x68, 0x2704, 0x5110, TimeSpan.Zero)!;
        msi.SetDirect();
        bus.Writes.Clear();
        msi.SetLeds([Teal]);
        Assert.Equal(["68 2E=00", "68 22=1C", "68 30=12", "68 31=34", "68 32=56", "68 36=64", "68 22=13"], bus.Writes);
    }

    [Fact]
    public void MsiV2EffectsMatchOpenRgb()
    {
        var bus = MsiBus();
        var msi = MsiGpuV2Controller.Open(bus, 0x68, 0x2704, 0x5110, TimeSpan.Zero)!;
        msi.SetMode("Wave", 50, [Red, Teal, Gold]);
        Assert.Equal(["68 46=03", "68 2E=00", "68 22=1C", "68 27 i2c 0000FF", "68 28 i2c 563412", "68 29 i2c CCBBAA", "68 36=64", "68 38=02", "68 22=08"], bus.Writes);

        bus.Writes.Clear();
        msi.SetMode("Off", 0, []);
        Assert.Equal(["68 2E=00", "68 22=1C", "68 22=01"], bus.Writes);

        bus.Writes.Clear();
        msi.SetMode("Magic", 0, []);
        Assert.Equal(["68 2E=00", "68 22=1C", "68 46=00", "68 36=64", "68 38=04", "68 22=07"], bus.Writes);
    }

    [Fact]
    public void MsiV2EffectListFollowsTheCard()
    {
        var bus = MsiBus();
        var rtx4080 = MsiGpuV2Controller.Open(bus, 0x68, 0x2704, 0x5110, TimeSpan.Zero)!;
        var slimWhite = MsiGpuV2Controller.Open(bus, 0x68, 0x2702, 0x511A, TimeSpan.Zero)!;
        var rtx5080 = MsiGpuV2Controller.Open(bus, 0x68, 0x2C02, 0x5300, TimeSpan.Zero)!;
        Assert.Contains(rtx4080.Modes, mode => mode.Name == "Stack");
        Assert.DoesNotContain(slimWhite.Modes, mode => mode.Name == "Stack");
        Assert.DoesNotContain(rtx4080.Modes, mode => mode.Name == "Arrow");
        Assert.Contains(rtx5080.Modes, mode => mode.Name == "Arrow");
        Assert.Equal("Direct", rtx4080.Modes[0].Name);

        // On the RTX 50 series Rhythm becomes the colored one (0x0B) started at its rainbow variant (0x0C).
        bus.Writes.Clear();
        rtx5080.SetMode("Rhythm", 0, []);
        Assert.Equal(["68 2E=00", "68 22=1C", "68 27 i2c 0000FF", "68 36=64", "68 38=04", "68 22=0C"], bus.Writes);
    }

    // Gigabyte RGB Fusion 2

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[] { 0xAB, 0xAB, 0xAB, 0xAB })] // a display's bus echoing the query
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00 })] // the LCD's bus on a 4080 Master
    public void Fusion2FailingIdentityWritesOnlyTheQuery(byte[]? reply)
    {
        var bus = new FakeBus { RawReply = reply };
        Assert.Null(GigabyteFusion2GpuController.Open(bus, 0x71));
        Assert.Equal(["71 raw AB00000000000000"], bus.Writes);
    }

    static (FakeBus Bus, GigabyteFusion2GpuController Chip) Fusion2()
    {
        var bus = new FakeBus { RawReply = [0xAB, 0x11, 0x52, 0x03] };
        var chip = GigabyteFusion2GpuController.Open(bus, 0x71)!;
        bus.Writes.Clear();
        return (bus, chip);
    }

    [Fact]
    public void Fusion2DirectColorsMatchOpenRgb()
    {
        var (bus, chip) = Fusion2();
        Rgb a = new(1, 2, 3), b = new(4, 5, 6), c = new(7, 8, 9), d = new(10, 11, 12), e = new(13, 14, 15);
        chip.SetDirect();
        chip.SetLeds([a, b, c, d, e]);
        Assert.Equal(
        [
            "71 raw 8801006300010000", "71 raw B001010203000000",
            "71 raw 8801006300020000", "71 raw B001010203040506",
            "71 raw 8801006300030000", "71 raw B101070809000000",
            "71 raw 8801006300040000", "71 raw 400A0B0C04000000",
            "71 raw 8801006300050000", "71 raw 400D0E0F05000000",
        ], bus.Writes);
    }

    [Fact]
    public void Fusion2EffectsMatchOpenRgb()
    {
        var (bus, chip) = Fusion2();
        chip.SetMode("Pulse", 100, [Red]);
        Assert.Equal(
        [
            "71 raw 8802056300010000", "71 raw B002FF0000000000",
            "71 raw 8802056300020000", "71 raw B002FF0000FF0000",
            "71 raw 8802056300030000", "71 raw B102FF0000000000",
            "71 raw 8802056300040000", "71 raw 40FF000004000000",
            "71 raw 8802056300050000", "71 raw 40FF000005000000",
        ], bus.Writes);

        bus.Writes.Clear();
        chip.SetMode("Color Cycle", 0, []);
        Assert.Equal(Enumerable.Range(1, 5).Select(zone => $"71 raw 8803006300{zone:X2}0000"), bus.Writes);

        bus.Writes.Clear();
        chip.SetMode("Tricolor", 40, [Red, Teal, Gold]);
        Assert.Equal(25, bus.Writes.Count); // each zone: the effect, then four banks of two colors
        Assert.Equal(
        [
            "71 raw 880B026308010000", "71 raw B00BFF0000123456", "71 raw B10BAABBCC000000", "71 raw B20B000000000000", "71 raw B30B000000000000",
        ], bus.Writes.Take(5));
        Assert.Equal("71 raw 880B026308050000", bus.Writes[20]);
        Assert.Equal("71 raw C00BFF0000123456", bus.Writes[21]);

        bus.Writes.Clear();
        chip.Save();
        Assert.Equal(["71 raw AA00000000000000"], bus.Writes);
    }

    // Gigabyte RGB Fusion 2, Blackwell era

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[] { 0x01, 0x03, 0x01, 0x00 })]
    [InlineData(new byte[] { 0xAB, 0x11, 0x52, 0x03 })] // the older chip's answer
    public void BlackwellFailingIdentityWritesOnlyTheQuery(byte[]? reply)
    {
        var bus = new FakeBus { RawReply = reply };
        Assert.Null(GigabyteFusion2BlackwellGpuController.Open(bus, 0x75, Fusion2BlackwellLayout.Gaming));
        Assert.Equal([$"75 raw {Packet64(0x10, 0x01)}"], bus.Writes);
    }

    static (FakeBus Bus, GigabyteFusion2BlackwellGpuController Chip) Blackwell(Fusion2BlackwellLayout layout)
    {
        var bus = new FakeBus { RawReply = [0x01, 0x02, 0x01, 0x00] }; // as a Radeon RX 9070 XT Gaming OC answers
        var chip = GigabyteFusion2BlackwellGpuController.Open(bus, 0x75, layout)!;
        bus.Writes.Clear();
        return (bus, chip);
    }

    [Fact]
    public void BlackwellDirectColorsMatchOpenRgb()
    {
        var (bus, chip) = Blackwell(Fusion2BlackwellLayout.Gaming);
        Rgb a = new(1, 2, 3), b = new(4, 5, 6), c = new(7, 8, 9), d = new(10, 11, 12);
        chip.SetDirect();
        chip.SetLeds([a, b, c, d]);
        // Six hardware zones for four on the card; the last two take the last LED's color.
        Assert.Equal(
        [
            $"75 raw {Packet64(0x16, 0x01, 0x00, 0x00, 0x0A, 1, 2, 3, 0x00, 0)}",
            $"75 raw {Packet64(0x16, 0x01, 0x00, 0x00, 0x0A, 4, 5, 6, 0x00, 1)}",
            $"75 raw {Packet64(0x16, 0x01, 0x00, 0x00, 0x0A, 7, 8, 9, 0x00, 2)}",
            $"75 raw {Packet64(0x16, 0x01, 0x00, 0x00, 0x0A, 10, 11, 12, 0x00, 3)}",
            $"75 raw {Packet64(0x16, 0x01, 0x00, 0x00, 0x0A, 10, 11, 12, 0x00, 4)}",
            $"75 raw {Packet64(0x16, 0x01, 0x00, 0x00, 0x0A, 10, 11, 12, 0x00, 5)}",
        ], bus.Writes);
    }

    [Fact]
    public void BlackwellEffectsMatchOpenRgb()
    {
        var (bus, chip) = Blackwell(Fusion2BlackwellLayout.Gaming);
        chip.SetMode("Pulse", 100, [Red]);
        Assert.Equal(Enumerable.Range(0, 6).Select(zone => $"75 raw {Packet64(0x12, 0x01, 0x02, 0x06, 0x0A, 0xFF, 0x00, 0x00, 0x00, (byte)zone)}"), bus.Writes);

        bus.Writes.Clear();
        Rgb[] eight = [Red, Teal, Gold, Red, Teal, Gold, Red, Teal];
        chip.SetMode("Color Shift", 0, eight);
        var colors = eight.SelectMany(color => new[] { color.R, color.G, color.B }).ToArray();
        // With colors of its own the zone's color isn't updated, so the red from Pulse stays in bytes 5 to 7.
        Assert.Equal(Enumerable.Range(0, 6).Select(zone => $"75 raw {Packet64([0x12, 0x01, 0x08, 0x01, 0x0A, 0xFF, 0x00, 0x00, 0x00, (byte)zone, 0x08, 0x00, .. colors])}"), bus.Writes);

        bus.Writes.Clear();
        chip.Save();
        Assert.Equal([$"75 raw {Packet64(0x13, 0x01)}"], bus.Writes);
    }

    [Fact]
    public void BlackwellSingleZoneSendsOnePacketAndHasNoWave()
    {
        var (bus, chip) = Blackwell(Fusion2BlackwellLayout.SingleZone);
        chip.SetLeds([Teal]);
        Assert.Equal([$"75 raw {Packet64(0x16, 0x01, 0x00, 0x00, 0x0A, 0x12, 0x34, 0x56, 0x00, 0)}"], bus.Writes);
        Assert.DoesNotContain(chip.Modes, mode => mode.Name == "Wave");
    }

    [Fact]
    public void BlackwellAorusMasterSendsEachFanLedOnceTheyDiffer()
    {
        var (bus, chip) = Blackwell(Fusion2BlackwellLayout.AorusMaster5080);
        Assert.Equal(26, chip.LedCount);
        var leds = Enumerable.Repeat(Gold, 26).ToArray();
        leds[0] = Red; // the right fan's first LED differs from the rest
        chip.SetLeds(leds);
        Assert.Equal(6, bus.Writes.Count);
        // The right fan sends its eight colors from byte 11 on; the count then carries to the next zones, as in OpenRGB.
        var first = Convert.FromHexString(bus.Writes[0][7..]);
        Assert.Equal(8, first[10]);
        Assert.Equal([0xFF, 0x00, 0x00, 0xAA, 0xBB, 0xCC], first[11..17]);
        Assert.Equal(8, Convert.FromHexString(bus.Writes[1][7..])[10]);
        // Hardware zone 5 is the card's fifth zone again (the top logo).
        Assert.Equal(5, Convert.FromHexString(bus.Writes[5][7..])[9]);
    }

    [Fact]
    public void BlackwellAorusWaterforceSkipsHardwareZoneZero()
    {
        var (bus, chip) = Blackwell(Fusion2BlackwellLayout.AorusWaterforce);
        chip.SetLeds([Red, Teal, Gold, Red]);
        Assert.Equal([1, 2, 3, 4], bus.Writes.Select(write => (int)Convert.FromHexString(write[7..])[9]));
        Assert.DoesNotContain(chip.Modes, mode => mode.Name == "Dazzle");
    }
}
