using System.Text;
using TRStudio.Engine;
using Xunit;

public sealed class EneRamTests
{
    /// <summary>
    /// An SMBus with ENE memory sticks on it, behaving as the real chips do: every stick starts at the shared
    /// address 0x77 and moves when told to, a register is picked first and then read or written, and
    /// registers 0xA0 to 0xAF read back 0 to 15. Anything written anywhere else is recorded as a mistake.
    /// </summary>
    sealed class FakeBus : ISmBus
    {
        public sealed class Stick(int slot, string chip, byte leds)
        {
            public readonly int Slot = slot;
            public byte Address = 0x77;
            public ushort Pointer;
            public readonly Dictionary<ushort, byte> Registers = Build(chip, leds);
            public readonly List<string> Writes = [];

            static Dictionary<ushort, byte> Build(string chip, byte leds)
            {
                var registers = new Dictionary<ushort, byte>();
                var name = Encoding.ASCII.GetBytes(chip);
                for (var i = 0; i < name.Length; i++) registers[(ushort)(0x1000 + i)] = name[i];
                registers[0x1C02] = leds;
                return registers;
            }

            public byte this[ushort register] => Registers.GetValueOrDefault(register);
        }

        public readonly List<Stick> Sticks = [];
        /// <summary>Other things on the bus that must never be written to, such as the memory's own data chips at 0x50.</summary>
        public readonly HashSet<byte> Others = [0x50, 0x51, 0x52, 0x53, 0x18, 0x19];
        public readonly List<string> Mistakes = [];
        public int Transfers, Held, MostHeld;
        public bool Dead, BlocksRefused;
        int _selectedSlot = -1;

        public string Name => "Fake bus";
        public void Dispose() { }

        sealed class Release(FakeBus bus) : IDisposable
        {
            public void Dispose() => bus.Held--;
        }

        public IDisposable Hold()
        {
            Held++;
            MostHeld = Math.Max(MostHeld, Held);
            return new Release(this);
        }

        List<Stick> At(byte address) => Sticks.Where(stick => stick.Address == address).ToList();

        bool Answers(byte address)
        {
            Transfers++;
            return !Dead && (Others.Contains(address) || At(address).Count > 0);
        }

        public int ReadByte(byte address) => Answers(address) ? 0 : -1;

        public int ReadByteData(byte address, byte command)
        {
            if (!Answers(address)) return -1;
            if (At(address).FirstOrDefault() is not { } stick) return 0x5A; // some other chip's data
            if (command is >= 0xA0 and < 0xB0) return command - 0xA0;
            return command == 0x81 ? stick[stick.Pointer] : 0;
        }

        public bool WriteWordData(byte address, byte command, ushort value)
        {
            if (!Answers(address)) return false;
            if (At(address).Count == 0 || command != 0x00) { Mistakes.Add($"word to 0x{address:X2}"); return true; }
            foreach (var stick in At(address)) stick.Pointer = (ushort)((value << 8) | (value >> 8));
            return true;
        }

        public bool WriteByteData(byte address, byte command, byte value)
        {
            if (!Answers(address)) return false;
            if (At(address).Count == 0 || command != 0x01) { Mistakes.Add($"byte to 0x{address:X2}"); return true; }
            foreach (var stick in At(address)) Store(stick, value);
            return true;
        }

        public bool WriteBlockData(byte address, byte command, ReadOnlySpan<byte> data)
        {
            if (!Answers(address) || BlocksRefused) return false;
            if (At(address).Count == 0 || command != 0x03) { Mistakes.Add($"block to 0x{address:X2}"); return true; }
            foreach (var stick in At(address))
            {
                foreach (var value in data) Store(stick, value);
            }
            return true;
        }

        void Store(Stick stick, byte value)
        {
            var register = stick.Pointer++;
            if (register == 0x80F8) { _selectedSlot = value; return; }
            if (register == 0x80F9)
            {
                if (stick.Slot == _selectedSlot) stick.Address = (byte)(value >> 1);
                return;
            }
            stick.Registers[register] = value;
            stick.Writes.Add($"{register:X4}={value:X2}");
        }
    }

    static FakeBus Xpg(params int[] slots)
    {
        var bus = new FakeBus();
        foreach (var slot in slots) bus.Sticks.Add(new FakeBus.Stick(slot, "AUDA0-E6K5-0101", 5));
        return bus;
    }

    [Fact]
    public void Four_sticks_are_each_moved_to_an_address_of_their_own_and_found()
    {
        var bus = Xpg(0, 1, 2, 3);
        var log = new List<string>();

        var sticks = EneRamLightingClient.Find(bus, log.Add);

        Assert.Equal([0x70, 0x71, 0x72, 0x73], bus.Sticks.Select(stick => (int)stick.Address));
        Assert.Equal([0x70, 0x71, 0x72, 0x73], sticks.Select(stick => (int)stick.Address));
        Assert.All(sticks, stick => Assert.Equal(("AUDA0-E6K5-0101", 5), (stick.Chip, stick.LedCount)));
        Assert.Equal(4, log.Count);
        Assert.Empty(bus.Mistakes);
        Assert.Equal(0, bus.Held); // every hold on the shared bus was let go
    }

    [Fact]
    public void Two_sticks_in_the_second_and_fourth_slots_are_found()
    {
        var bus = Xpg(1, 3);

        var sticks = EneRamLightingClient.Find(bus);

        Assert.Equal([0x71, 0x73], sticks.Select(stick => (int)stick.Address));
        Assert.Empty(bus.Mistakes);
    }

    [Fact]
    public void Sticks_already_moved_by_an_earlier_run_are_found_without_moving_them_again()
    {
        var bus = Xpg(0, 1);
        EneRamLightingClient.Find(bus);
        bus.Sticks.ForEach(stick => stick.Writes.Clear());

        var sticks = EneRamLightingClient.Find(bus);

        Assert.Equal([0x70, 0x71], sticks.Select(stick => (int)stick.Address));
        Assert.All(bus.Sticks, stick => Assert.Empty(stick.Writes));
    }

    [Fact]
    public void A_pc_with_plain_memory_gets_nothing_written_to_its_bus()
    {
        var bus = new FakeBus();

        var sticks = EneRamLightingClient.Find(bus);

        Assert.Empty(sticks);
        Assert.Empty(bus.Mistakes);
    }

    [Fact]
    public void A_chip_that_is_not_a_known_one_is_listed_with_no_leds_and_left_alone()
    {
        var bus = new FakeBus();
        bus.Sticks.Add(new FakeBus.Stick(0, "MYSTERY-0001", 8));

        Assert.Empty(EneRamLightingClient.Find(bus));
        Assert.Empty(bus.Sticks[0].Writes);
    }

    [Fact]
    public void Microns_sticks_are_skipped()
    {
        var bus = Xpg(0);
        var micron = Encoding.ASCII.GetBytes("Micron");
        for (var i = 0; i < micron.Length; i++) bus.Sticks[0].Registers[(ushort)(0x1030 + i)] = micron[i];

        Assert.Empty(EneRamLightingClient.Find(bus));
    }

    [Fact]
    public void Colors_are_written_red_blue_green_three_bytes_at_a_time()
    {
        var bus = Xpg(0);
        var stick = EneRamLightingClient.Find(bus).Single();
        bus.Sticks[0].Writes.Clear();

        stick.SetDirect();
        stick.SetLeds([new Rgb(1, 2, 3), new Rgb(4, 5, 6), new Rgb(7, 8, 9), new Rgb(10, 11, 12), new Rgb(13, 14, 15)]);

        Assert.Equal(
        [
            "8020=01", "80A0=01", // direct mode on, apply
            "8100=01", "8101=03", "8102=02", // LED 1: red, blue, green
            "8103=04", "8104=06", "8105=05",
            "8106=07", "8107=09", "8108=08",
            "8109=0A", "810A=0C", "810B=0B",
            "810C=0D", "810D=0F", "810E=0E",
        ], bus.Sticks[0].Writes);
        Assert.Equal(1, bus.MostHeld > 0 ? 1 : 0);
        Assert.Empty(bus.Mistakes);
    }

    [Fact]
    public void A_chip_that_refuses_blocks_gets_the_same_bytes_one_at_a_time()
    {
        var bus = Xpg(0);
        var stick = EneRamLightingClient.Find(bus).Single();
        bus.Sticks[0].Writes.Clear();
        bus.BlocksRefused = true;

        stick.SetLeds([new Rgb(1, 2, 3), new Rgb(4, 5, 6)]);

        Assert.Equal(["8100=01", "8101=03", "8102=02", "8103=04", "8104=06", "8105=05"], bus.Sticks[0].Writes);
    }

    [Fact]
    public void An_older_chip_uses_the_older_color_registers()
    {
        var bus = new FakeBus();
        bus.Sticks.Add(new FakeBus.Stick(0, "DIMM_LED-0102", 5));
        var stick = EneRamLightingClient.Find(bus).Single();
        bus.Sticks[0].Writes.Clear();

        stick.SetLeds([new Rgb(1, 2, 3)]);

        Assert.Equal(["8000=01", "8001=03", "8002=02"], bus.Sticks[0].Writes);
    }

    [Fact]
    public void A_built_in_effect_sets_its_color_mode_and_speed_then_leaves_direct_mode()
    {
        var bus = Xpg(0);
        var stick = EneRamLightingClient.Find(bus).Single();
        bus.Sticks[0].Writes.Clear();

        stick.SetMode("Breathing", 100, new Rgb(255, 128, 0));

        var writes = bus.Sticks[0].Writes;
        Assert.Equal(["8160=FF", "8161=00", "8162=80"], writes.Take(3)); // the effect's color, red blue green
        Assert.Equal(["80A0=01", "8021=02", "8022=00", "8023=00", "80A0=01", "8020=00", "80A0=01"], writes.Skip(15)); // fastest speed is 0
    }

    [Fact]
    public void Speed_counts_down_and_effects_without_one_send_zero()
    {
        var bus = Xpg(0);
        var stick = EneRamLightingClient.Find(bus).Single();

        stick.SetMode("Rainbow", 0, Rgb.Black);
        Assert.Equal(4, bus.Sticks[0][0x8022]); // slowest
        Assert.Equal(5, bus.Sticks[0][0x8021]);

        stick.SetMode("Rainbow", 50, Rgb.Black);
        Assert.Equal(2, bus.Sticks[0][0x8022]);

        stick.SetMode("Off", 80, Rgb.Black);
        Assert.Equal((0, 0), (bus.Sticks[0][0x8021], bus.Sticks[0][0x8022]));

        bus.Sticks[0].Writes.Clear();
        stick.SetMode("Disco", 50, Rgb.Black);
        Assert.Empty(bus.Sticks[0].Writes);
    }

    [Fact]
    public void A_stick_that_stops_answering_reads_as_lighting_unavailable()
    {
        var bus = Xpg(0);
        using var client = new EneRamLightingClient(null, EneRamLightingClient.Find(bus), background: false);
        bus.Dead = true;

        Assert.Throws<LightingUnavailableException>(() => client.SetDirect(0));
        client.SetDirect(9); // no such stick: ignored
    }

    [Fact]
    public void Each_stick_is_a_device_with_one_zone_and_its_own_effects()
    {
        var bus = Xpg(0, 1);
        using var client = new EneRamLightingClient(null, EneRamLightingClient.Find(bus), background: false);

        var devices = client.Devices();

        Assert.Equal(["RGB Memory 1", "RGB Memory 2"], devices.Select(device => device.Name));
        Assert.All(devices, device => Assert.Equal(("Dram", 5), (device.Kind, device.LedCount)));
        Assert.All(devices, device => Assert.DoesNotContain("0x", device.Key)); // a stick keeps its key when its address changes after a restart
        Assert.Equal(2, devices.Select(device => device.Key).Distinct().Count());
        Assert.Contains(devices[0].Modes, mode => mode is { Name: "Rainbow", HasSpeed: true, Colors: 0 });
        Assert.Contains(devices[0].Modes, mode => mode is { Name: "Off" });
    }

    [Fact]
    public void In_the_background_the_newest_frame_wins_and_nothing_is_lost_at_the_end()
    {
        var bus = Xpg(0);
        using (var client = new EneRamLightingClient(null, EneRamLightingClient.Find(bus)))
        {
            client.SetDirect(0);
            for (byte frame = 1; frame <= 50; frame++) client.SetLeds(0, [new Rgb(frame, 0, 0)]);
            for (var wait = 0; wait < 300 && bus.Sticks[0][0x8100] != 50; wait++) Thread.Sleep(10);
        }

        Assert.Equal(50, bus.Sticks[0][0x8100]); // the last frame reached the stick
        Assert.Equal(1, bus.Sticks[0][0x8020]);
        Assert.True(bus.Sticks[0].Writes.Count(write => write.StartsWith("8100=")) < 50, "frames that piled up should have been skipped");
        Assert.Empty(bus.Mistakes);
    }

    [Fact]
    public void Built_in_drivers_are_listed_together_and_each_gets_its_own_calls()
    {
        var bus = Xpg(0, 1);
        var memory = new EneRamLightingClient(null, EneRamLightingClient.Find(bus), background: false);
        using var all = new BuiltInLightingClient(new AuraUsbLightingClient([]), memory);

        var devices = all.Devices();
        all.SetDirect(1);

        Assert.Equal([0, 1], devices.Select(device => device.Index));
        Assert.Equal(0, bus.Sticks[0][0x8020]);
        Assert.Equal(1, bus.Sticks[1][0x8020]);
    }
}
