using TRStudio.Engine;
using Xunit;

/// <summary>Checks the Galax driver against OpenRGB's register sequences and the 4070 EX Gamer found on real hardware.</summary>
public class GalaxGpuTests
{
    sealed class Chip(byte address, Dictionary<byte, byte> registers) : ISmBus
    {
        public readonly List<(byte Address, byte Register, byte Value)> Writes = [];
        public List<(byte Address, byte Register, byte Value)> Snapshot() { lock (Writes) return [.. Writes]; }
        public string Name => "fake GPU bus";
        public IDisposable Hold() => new Nothing();
        public int ReadByte(byte a) => -1;
        public int ReadByteData(byte a, byte command) => a == address && registers.TryGetValue(command, out var value) ? value : -1;
        public bool WriteByteData(byte a, byte command, byte value)
        {
            lock (Writes) Writes.Add((a, command, value));
            return a == address;
        }
        public bool WriteWordData(byte a, byte command, ushort value) => false;
        public bool WriteBlockData(byte a, byte command, ReadOnlySpan<byte> data) => false;
        public void Dispose() { }
        sealed class Nothing : IDisposable { public void Dispose() { } }
    }

    // The registers read back from the user's GALAX RTX 4070 EX Gamer (cyan, breathing).
    static Chip Ex4070() => new(0x50, new() { [0x00] = 0x02, [0x01] = 0x01, [0x02] = 0x00, [0x03] = 0xFF, [0x04] = 0xEA, [0x30] = 0x02 });

    [Theory]
    [InlineData(0x50, 0x01, 0x08, true)]  // OpenRGB's KFA2 4080 SG
    [InlineData(0x50, 0x02, 0x01, true)]  // the 4070 EX Gamer
    [InlineData(0x50, 0x02, 0x08, false)]
    [InlineData(0x51, 0x80, 0x00, true)]
    [InlineData(0x23, 0x30, 0x00, true)]
    [InlineData(0x32, 0x26, 0x20, true)]
    [InlineData(0x32, 0x26, 0x30, false)]
    public void Identity_is_checked_like_OpenRGB(byte address, byte first, byte second, bool found)
    {
        var chip = new Chip(address, new() { [0x00] = first, [0x01] = second });
        Assert.Equal(found, GalaxGpuController.IsAt(chip, address));
        Assert.Empty(chip.Writes);
    }

    [Fact]
    public void A_chip_that_fails_the_check_gets_no_driver_and_no_writes()
    {
        var chip = new Chip(0x50, new() { [0x00] = 0x77 });
        Assert.Null(GalaxGpuController.Open(chip, 0x50));
        Assert.Empty(chip.Writes);
    }

    [Fact]
    public void V2_direct_is_sync_off_static_full_brightness_then_the_color()
    {
        var chip = Ex4070();
        var light = GalaxGpuController.Open(chip, 0x50)!;
        light.SetDirect();
        light.SetLeds([new Rgb(0xFF, 0x00, 0x00)]);
        Assert.Equal([(0x50, 0x27, 0x00), (0x50, 0x30, 0x01), (0x50, 0x2D, 0x03), (0x50, 0x02, 0xFF), (0x50, 0x03, 0x00), (0x50, 0x04, 0x00)],
            chip.Writes.Select(w => ((int)w.Address, (int)w.Register, (int)w.Value)));
    }

    [Fact]
    public void V2_breathing_sets_color_mode_and_both_speed_registers()
    {
        var chip = Ex4070();
        GalaxGpuController.Open(chip, 0x50)!.SetMode("Breathing", 100, [new Rgb(1, 2, 3)]);
        Assert.Contains((0x50, 0x30, 0x02), chip.Writes.Select(w => ((int)w.Address, (int)w.Register, (int)w.Value)));
        Assert.Contains((byte)0x21, chip.Writes.Where(w => w.Value == 9).Select(w => w.Register));
        Assert.Contains((byte)0x22, chip.Writes.Where(w => w.Value == 9).Select(w => w.Register));
        Assert.Contains((byte)0x02, chip.Writes.Where(w => w.Value == 1).Select(w => w.Register));
    }

    [Fact]
    public void V2_off_and_rainbow_use_OpenRGBs_values()
    {
        var chip = Ex4070();
        var light = GalaxGpuController.Open(chip, 0x50)!;
        light.SetMode("Off", 0, []);
        light.SetMode("Rainbow", 0, []);
        Assert.Equal([0x19, 0x16], chip.Writes.Where(w => w.Register == 0x30).Select(w => (int)w.Value));
    }

    [Fact]
    public void V1_modes_use_both_mode_registers()
    {
        var chip = new Chip(0x23, new() { [0x00] = 0x27 });
        var light = GalaxGpuController.Open(chip, 0x23)!;
        light.SetMode("Rainbow", 0, []);
        Assert.Contains((0x23, 0x05, 0x84), chip.Writes.Select(w => ((int)w.Address, (int)w.Register, (int)w.Value)));
        Assert.Contains((0x23, 0x06, 0x02), chip.Writes.Select(w => ((int)w.Address, (int)w.Register, (int)w.Value)));
    }

    [Fact]
    public void The_client_lists_the_card_and_sends_the_newest_color()
    {
        var chip = Ex4070();
        var light = GalaxGpuController.Open(chip, 0x50)!;
        using var client = new GpuLightingClient([("GPU|test", "GALAX GeForce RTX 4070 EX Gamer", (IGpuLightController)light, (ISmBus)chip)]);
        var device = Assert.Single(client.Devices());
        Assert.Equal("Gpu", device.Kind);
        Assert.Equal(1, device.LedCount);
        Assert.Equal("Direct", device.Modes[0].Name);
        client.SetDirect(0);
        for (var i = 0; i < 50; i++) client.SetLeds(0, [new Rgb((byte)i, 0, 0)]);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!chip.Snapshot().Any(w => w.Register == 0x02 && w.Value == 49))
        {
            Assert.True(DateTime.UtcNow < deadline, "the newest color never arrived");
            Thread.Sleep(5);
        }
        Assert.True(chip.Snapshot().Count(w => w.Register == 0x02) < 50, "every frame went out instead of the newest");
    }

    [Fact]
    public void The_4070_EX_Gamer_is_in_the_catalog_at_0x50()
    {
        var entry = Assert.Single(GpuCatalog.Find(0x10DE, 0x2786, 0x10DE, 0x1875));
        Assert.Equal(("GalaxGPUController", (byte)0x50), (entry.Driver, entry.Address));
    }
}

public class OneLedZoneTests
{
    [Fact]
    public void Average_blends_channels_straight()
    {
        Assert.Equal(new Rgb(128, 64, 0), LightingService.Average([new Rgb(255, 128, 0), new Rgb(0, 0, 0)]));
        Assert.Equal(Rgb.Black, LightingService.Average([]));
    }

    [Fact]
    public void Area_covers_the_whole_box_not_just_its_middle()
    {
        var box = new LightPlacement(0, 0, 320, 200);
        var points = box.Area(8, 4);
        Assert.Equal(32, points.Length);
        Assert.True(points.Min(p => p.X) < 0.1 && points.Max(p => p.X) > 0.9);
        Assert.True(points.Min(p => p.Y) < 0.2 && points.Max(p => p.Y) > 0.8);
    }

    [Fact]
    public void A_band_crossing_part_of_a_box_gives_a_partial_color_not_a_flash()
    {
        var box = new LightPlacement(100, 50, 80, 20);
        // A band lit only over the box's left quarter: the middle point alone would read black.
        var lit = box.Area().Select(p => p.X * LightPlacement.CanvasWidth < 120 ? new Rgb(255, 255, 255) : Rgb.Black);
        var color = LightingService.Average(lit);
        Assert.InRange(color.R, 40, 90);
    }
}
