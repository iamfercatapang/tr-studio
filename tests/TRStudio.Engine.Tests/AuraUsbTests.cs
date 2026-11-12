using TRStudio.Engine;
using Xunit;

public sealed class AuraUsbTests
{
    /// <summary>Answers like the controller on an ASUS PRIME B550M-A WIFI II: two RGB headers and one addressable header.</summary>
    sealed class FakeBoard(string firmware = "AULA3-AR42-0222", byte boardLeds = 2, byte rgbHeaders = 2, byte headers = 1, bool answers = true) : IHidLink
    {
        public readonly List<byte[]> Sent = [];
        readonly Queue<byte[]> _answers = new();
        public bool Unplugged;

        public void Write(ReadOnlySpan<byte> report)
        {
            if (Unplugged) throw new IOException("The device is not connected.");
            var packet = report.ToArray();
            Sent.Add(packet);
            if (!answers) return;
            var answer = new byte[65];
            answer[0] = 0xEC;
            if (packet[1] == 0x82)
            {
                answer[1] = 0x02;
                System.Text.Encoding.ASCII.GetBytes(firmware).CopyTo(answer, 2);
                _answers.Enqueue(answer);
            }
            if (packet[1] == 0xB0)
            {
                answer[1] = 0x30;
                answer[4 + 0x02] = headers;
                answer[4 + 0x1B] = boardLeds;
                answer[4 + 0x1D] = rgbHeaders;
                _answers.Enqueue(answer);
            }
        }

        public byte[]? Read(int timeoutMs = 1000) => _answers.Count > 0 ? _answers.Dequeue() : null;
        public void Dispose() { }

        /// <summary>The packets after the three sent while connecting.</summary>
        public List<byte[]> Commands => Sent.Skip(3).ToList();
    }

    static string Head(byte[] packet, int bytes = 6) => Convert.ToHexString(packet, 0, bytes);

    [Fact]
    public void The_board_lists_its_own_leds_and_each_addressable_header()
    {
        var link = new FakeBoard();
        using var board = new AuraBoard(link, "ASUS PRIME B550M-A WIFI II", "key");

        var device = board.Describe(0);

        Assert.Equal("AULA3-AR42-0222", board.Firmware);
        Assert.Equal("Motherboard", device.Kind);
        Assert.Equal([new LightZone("Aura Mainboard", 2, 2, 2), new LightZone("Addressable RGB Header 1", 0, 0, 500)], device.Zones);
        Assert.Equal(2, device.LedCount);
        Assert.True(device.Zones[1].Resizable);
        Assert.Contains(device.Modes, mode => mode is { Name: "Rainbow", Colors: 0 });
        Assert.Contains(device.Modes, mode => mode is { Name: "Breathing", Colors: 1 });
        Assert.Equal(["EC82", "ECB0", "EC52530001"], link.Sent.Select((packet, index) => Head(packet, index == 2 ? 5 : 2)));
        Assert.All(link.Sent, packet => Assert.Equal(65, packet.Length));
    }

    [Fact]
    public void A_controller_that_does_not_answer_is_not_taken_for_a_board()
    {
        Assert.Throws<InvalidDataException>(() => new AuraBoard(new FakeBoard(answers: false), "name", "key"));
    }

    [Fact]
    public void Direct_mode_is_set_only_on_channels_that_have_leds()
    {
        var link = new FakeBoard();
        using var board = new AuraBoard(link, "name", "key");

        board.SetDirect();
        Assert.Equal(["EC35000000FF"], link.Commands.Select(packet => Head(packet)));

        board.ResizeZone(1, 30);
        link.Sent.RemoveRange(3, link.Sent.Count - 3);
        board.SetDirect();
        Assert.Equal(["EC35000000FF", "EC35010000FF"], link.Commands.Select(packet => Head(packet)));
    }

    [Fact]
    public void Colors_go_out_twenty_leds_to_a_packet_and_the_last_one_applies_them()
    {
        var link = new FakeBoard();
        using var board = new AuraBoard(link, "name", "key");
        board.ResizeZone(1, 45);
        var colors = Enumerable.Range(0, 47).Select(i => new Rgb((byte)i, (byte)(i + 100), (byte)(i + 200))).ToArray();

        board.SetLeds(colors);

        var packets = link.Commands;
        // The board's two LEDs on channel 4, then the header's 45 on channel 0 in packets of 20, 20 and 5.
        Assert.Equal(["EC40840002", "EC40000014", "EC40001414", "EC40802805"], packets.Select(packet => Head(packet, 5)));
        Assert.Equal([0, 100, 200, 1, 101, 201], packets[0][5..11]);
        Assert.Equal([2, 102, 202], packets[1][5..8]);
        Assert.Equal([22, 122, 222], packets[2][5..8]);
        Assert.Equal([46, 146, 246], packets[3][17..20]); // the last LED
    }

    [Fact]
    public void A_strip_past_256_leds_flags_the_offset_overflow()
    {
        var link = new FakeBoard(boardLeds: 0, rgbHeaders: 0);
        using var board = new AuraBoard(link, "name", "key");
        board.ResizeZone(0, 300);

        board.SetLeds(new Rgb[300]);

        var packets = link.Commands;
        Assert.Equal(15, packets.Count);
        Assert.Equal("EC4000F014", Head(packets[12], 5)); // LEDs 240 to 259: still counted in one byte
        Assert.Equal("EC40100414", Head(packets[13], 5)); // LEDs 260 to 279: offset 4 with the overflow flag
        Assert.Equal("EC40901814", Head(packets[14], 5)); // the last 20, overflow and apply together
    }

    [Fact]
    public void A_built_in_effect_sets_the_mode_then_its_color_on_the_channels_slots()
    {
        var link = new FakeBoard();
        using var board = new AuraBoard(link, "name", "key");
        board.ResizeZone(1, 30);

        board.SetMode("Breathing", new Rgb(255, 128, 0));

        var packets = link.Commands;
        Assert.Equal(4, packets.Count);
        Assert.Equal("EC3500000002", Head(packets[0]));
        Assert.Equal("EC36000300" + "FF8000FF8000", Head(packets[1], 11)); // slots 0 and 1: the two RGB headers
        Assert.Equal("EC3501000002", Head(packets[2]));
        Assert.Equal("EC36000400" + "000000000000FF8000", Head(packets[3], 14)); // slot 2: the addressable header
    }

    [Fact]
    public void An_unknown_effect_sends_nothing()
    {
        var link = new FakeBoard();
        using var board = new AuraBoard(link, "name", "key");

        board.SetMode("Disco", Rgb.Black);

        Assert.Empty(link.Commands);
    }

    [Fact]
    public void Older_firmware_numbers_the_headers_effects_first()
    {
        var link = new FakeBoard(firmware: "AULA3-AR32-0207", headers: 2);
        using var board = new AuraBoard(link, "name", "key");
        board.ResizeZone(1, 10);
        board.ResizeZone(2, 10);

        board.SetDirect();

        // The board's own LEDs use effect channel 2, after the two headers at 0 and 1.
        Assert.Equal(["EC35020000FF", "EC35000000FF", "EC35010000FF"], link.Commands.Select(packet => Head(packet)));
    }

    [Fact]
    public void A_header_size_is_kept_within_what_a_header_can_hold_and_the_board_zone_cannot_change()
    {
        using var board = new AuraBoard(new FakeBoard(), "name", "key");

        board.ResizeZone(0, 50);
        board.ResizeZone(1, 9000);
        board.ResizeZone(7, 10);

        Assert.Equal([2, 500], board.Describe(0).Zones.Select(zone => zone.LedCount));
    }

    [Fact]
    public void An_unplugged_controller_reads_as_lighting_unavailable()
    {
        var link = new FakeBoard();
        using var client = new AuraUsbLightingClient([new AuraBoard(link, "name", "key")]);
        link.Unplugged = true;

        Assert.Throws<LightingUnavailableException>(() => client.SetDirect(0));
        client.SetDirect(5); // no such device: ignored
    }

    [Fact]
    public void A_pc_without_an_aura_board_has_no_devices()
    {
        using var client = new AuraUsbLightingClient([]);

        Assert.Empty(client.Devices());
        Assert.False(client.TakeDevicesChanged());
    }

    /// <summary>A client that only records what reaches it, and can be closed like OpenRGB.</summary>
    sealed class Recorder(string name, params string[] devices) : ILightingClient
    {
        public readonly List<string> Calls = [];
        public bool Closed, Disposed;

        void Check()
        {
            if (Closed) throw new LightingUnavailableException($"{name} stopped answering.");
        }

        public bool TakeDevicesChanged() { Check(); return false; }
        public IReadOnlyList<LightDevice> Devices()
        {
            Check();
            return devices.Select((device, index) => new LightDevice(index, $"{device}|{name}", device, device.Contains("Memory") ? "Dram" : "Motherboard", [new LightZone("Zone", 4, 4, 4)], [], 4)).ToList();
        }
        public void SetDirect(int device) { Check(); Calls.Add($"direct {device}"); }
        public void SetLeds(int device, ReadOnlySpan<Rgb> colors) { Check(); Calls.Add($"leds {device}"); }
        public void SetMode(int device, string mode, int speed, IReadOnlyList<Rgb> colors) { Check(); Calls.Add($"mode {device} {mode}"); }
        public void ResizeZone(int device, int zone, int leds) { Check(); Calls.Add($"resize {device} {zone} {leds}"); }
        public void Dispose() => Disposed = true;
    }

    /// <summary>OpenRGB is looked for in the background, so the test waits for it to join.</summary>
    static IReadOnlyList<LightDevice> WaitForExtra(CombinedLightingClient combined)
    {
        for (var attempt = 0; attempt < 200 && !combined.ExtraConnected; attempt++)
        {
            combined.TakeDevicesChanged();
            Thread.Sleep(10);
        }
        return combined.Devices();
    }

    [Fact]
    public void Built_in_devices_come_first_and_openrgb_adds_the_rest()
    {
        var own = new Recorder("own", "ASUS Board");
        var extra = new Recorder("OpenRGB", "ASUS Board", "XPG Memory", "GPU");
        using var combined = new CombinedLightingClient(own, () => extra);

        Assert.Equal(["ASUS Board"], combined.Devices().Select(device => device.Name));
        var devices = WaitForExtra(combined);

        // OpenRGB lists the board too; the built-in driver keeps it.
        Assert.Equal(["ASUS Board|own", "XPG Memory|OpenRGB", "GPU|OpenRGB"], devices.Select(device => device.Key));
        Assert.Equal([0, 1, 2], devices.Select(device => device.Index));

        combined.SetDirect(0);
        combined.SetLeds(2, new Rgb[4]);
        combined.SetMode(1, "Rainbow", 50, []);
        Assert.Equal(["direct 0"], own.Calls);
        Assert.Equal(["leds 2", "mode 1 Rainbow"], extra.Calls); // its own numbering: the GPU is its third device
    }

    [Fact]
    public void Openrgb_closing_leaves_the_built_in_devices_running()
    {
        var own = new Recorder("own", "ASUS Board");
        var extra = new Recorder("OpenRGB", "XPG Memory");
        using var combined = new CombinedLightingClient(own, () => extra);
        WaitForExtra(combined);

        extra.Closed = true;
        combined.SetDirect(1); // no exception: OpenRGB just drops out
        combined.SetDirect(0);

        Assert.False(combined.ExtraConnected);
        Assert.True(extra.Disposed);
        Assert.True(combined.TakeDevicesChanged());
        Assert.Equal(["ASUS Board"], combined.Devices().Select(device => device.Name));
        Assert.Equal(["direct 0"], own.Calls);
    }

    [Fact]
    public void Memory_driven_by_the_built_in_driver_is_not_listed_again_from_openrgb()
    {
        var own = new Recorder("own", "ASUS Board", "RGB Memory 1");
        var extra = new Recorder("OpenRGB", "ENE DRAM Memory", "GPU");
        using var combined = new CombinedLightingClient(own, () => extra);

        var devices = WaitForExtra(combined);

        Assert.Equal(["ASUS Board", "RGB Memory 1", "GPU"], devices.Select(device => device.Name));
    }

    [Fact]
    public void Without_openrgb_the_built_in_devices_still_work()
    {
        var own = new Recorder("own", "ASUS Board");
        using var combined = new CombinedLightingClient(own, () => throw new LightingUnavailableException("OpenRGB isn't answering on this PC."));

        combined.TakeDevicesChanged();
        Thread.Sleep(100);
        combined.TakeDevicesChanged();

        Assert.False(combined.ExtraConnected);
        Assert.Equal(["ASUS Board"], combined.Devices().Select(device => device.Name));
    }

    [Fact]
    public void A_failing_built_in_device_is_reported_so_the_service_reconnects()
    {
        var own = new Recorder("own", "ASUS Board");
        using var combined = new CombinedLightingClient(own, () => new Recorder("OpenRGB"));
        combined.Devices();

        own.Closed = true;

        Assert.Throws<LightingUnavailableException>(() => combined.SetDirect(0));
    }
}
