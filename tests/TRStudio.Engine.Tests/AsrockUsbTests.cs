using TRStudio.Engine;
using Xunit;

public sealed class AsrockUsbTests
{
    /// <summary>
    /// Answers like a Polychrome USB board with two RGB headers, two addressable headers (32 LEDs set on the
    /// first, none on the second), an IO cover light and three PCB LEDs. The IO cover and PCB take green first.
    /// </summary>
    sealed class FakeBoard(bool answers = true) : IHidLink
    {
        public readonly List<byte[]> Sent = [];
        public readonly byte[] LedCounts = [1, 1, 32, 0, 0x1E, 1, 3, 0x1E];
        public byte Swap = 0b0110_0000;
        public const byte Present = 0b0110_1111;
        readonly Queue<byte[]> _answers = new();
        public bool Unplugged;

        public void Write(ReadOnlySpan<byte> report)
        {
            if (Unplugged) throw new IOException("The device is not connected.");
            var packet = report.ToArray();
            Sent.Add(packet);
            if (!answers) return;
            var answer = new byte[65];
            if (packet[1] == 0x14)
            {
                // The answer runs one byte later than the request: values start at the fifth byte after the report id.
                if (packet[3] == 0x02) LedCounts.CopyTo(answer, 5);
                if (packet[3] == 0x03) answer[5] = Swap;
                if (packet[3] == 0x01) answer[5] = Present;
            }
            if (packet[1] == 0x15 && packet[3] == 0x03) Swap = packet[4];
            if (packet[1] == 0x15 && packet[3] == 0x02) packet.AsSpan(4, 8).CopyTo(LedCounts);
            _answers.Enqueue(answer);
        }

        public byte[]? Read(int timeoutMs = 1000) => _answers.Count > 0 ? _answers.Dequeue() : null;
        public void Dispose() { }

        /// <summary>The packets after the three sent while connecting.</summary>
        public List<byte[]> Commands => Sent.Skip(3).ToList();
        public void Forget() => Sent.RemoveRange(3, Sent.Count - 3);
    }

    static string Head(byte[] packet, int bytes) => Convert.ToHexString(packet, 0, bytes);

    [Fact]
    public void The_board_lists_the_zones_it_has_with_the_led_counts_it_remembers()
    {
        var link = new FakeBoard();
        using var board = new AsrockBoard(link, "ASRock B550 Steel Legend", "key");

        var device = board.Describe(0);

        Assert.Equal(
        [
            new LightZone("RGB LED 1 Header", 1, 1, 1), new LightZone("RGB LED 2 Header", 1, 1, 1),
            new LightZone("Addressable Header 1", 32, 0, 100), new LightZone("Addressable Header 2", 0, 0, 100),
            new LightZone("IO Cover", 1, 1, 1), new LightZone("PCB", 3, 3, 3),
        ], device.Zones);
        Assert.Equal(38, device.LedCount);
        Assert.Equal("Motherboard", device.Kind);
        Assert.Contains(device.Modes, mode => mode is { Name: "Rainbow", HasSpeed: true, Colors: 0 });
        Assert.Contains(device.Modes, mode => mode is { Name: "Static", HasSpeed: false, Colors: 1 });
        // Three questions and nothing written: connecting changes nothing on the board.
        Assert.Equal(["00140002", "00140003", "00140001"], link.Sent.Select(packet => Head(packet, 4)));
        Assert.All(link.Sent, packet => Assert.Equal(65, packet.Length));
    }

    [Fact]
    public void A_controller_that_does_not_answer_is_not_taken_for_a_board()
    {
        Assert.Throws<InvalidDataException>(() => new AsrockBoard(new FakeBoard(answers: false), "name", "key"));
    }

    [Fact]
    public void Direct_mode_turns_the_red_green_swap_off_once_and_sets_each_zone_that_has_leds()
    {
        var link = new FakeBoard();
        using var board = new AsrockBoard(link, "name", "key");

        board.SetDirect();
        board.SetDirect();

        var packets = link.Commands;
        Assert.Equal("0015000300", Head(packets[0], 5)); // swap off
        // Zones 0, 1, 2, 5 and 6: the empty second addressable header is skipped.
        Assert.Equal(["00100000" + "0F", "00100001" + "0F", "00100002" + "0F", "00100005" + "0F", "00100006" + "0F"], packets.Skip(1).Take(5).Select(packet => Head(packet, 5)));
        Assert.Equal(11, packets.Count); // the second time sends the zones again but not the swap
        Assert.Equal(0, link.Swap);
    }

    [Fact]
    public void Colors_go_out_as_one_run_cut_into_packets_green_first_unless_the_zone_is_swapped()
    {
        var link = new FakeBoard();
        using var board = new AsrockBoard(link, "name", "key");
        var colors = Enumerable.Range(0, 38).Select(i => new Rgb((byte)(i + 1), (byte)(i + 101), (byte)(i + 201))).ToArray();

        board.SetLeds(colors);

        var packets = link.Commands;
        // Room for 1 + 1 + 100 + 100 + 1 + 3 LEDs plus 8 spare: 642 bytes, 54 in the first packet and 57 in each after.
        Assert.Equal(12, packets.Count);
        Assert.Equal("001000FFE30000FF", Head(packets[0], 8));
        Assert.Equal("001000FFE4", Head(packets[1], 5));
        Assert.All(packets, packet => Assert.Equal(0x65, packet[0x40]));
        Assert.Equal([101, 1, 201], packets[0][9..12]); // LED 1 on an unswapped zone: green, red, blue
        Assert.Equal([102, 2, 202], packets[0][12..15]);
        Assert.Equal([119, 19, 219], packets[1][5..8]); // the 19th LED opens the second packet
        // The IO cover's LED is the 35th and its zone is swapped: red, green, blue.
        var run = packets.SelectMany((packet, index) => index == 0 ? packet[9..63] : packet[5..62]).ToArray();
        Assert.Equal([35, 135, 235], run[102..105]);
        Assert.Equal([38, 138, 238], run[111..114]);
        Assert.All(run[114..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void A_built_in_effect_puts_the_swap_back_and_sets_mode_color_and_speed_on_each_zone()
    {
        var link = new FakeBoard();
        using var board = new AsrockBoard(link, "name", "key");
        board.SetDirect();
        link.Forget();

        board.SetMode("Breathing", 100, new Rgb(255, 128, 0));

        var packets = link.Commands;
        Assert.Equal("0015000360", Head(packets[0], 5)); // the board's own swap setting, back as it was
        Assert.Equal("00100000" + "02" + "80FF00" + "00FF", Head(packets[1], 10)); // unswapped: green, red, blue, then fastest speed
        Assert.Equal("00100005" + "02" + "FF8000" + "00FF", Head(packets[4], 10)); // the IO cover is swapped
        Assert.Equal(6, packets.Count);
        Assert.Equal(0b0110_0000, link.Swap);
    }

    [Fact]
    public void Speed_counts_down_and_an_unknown_effect_sends_nothing()
    {
        var link = new FakeBoard();
        using var board = new AsrockBoard(link, "name", "key");

        board.SetMode("Rainbow", 0, Rgb.Black);
        Assert.Equal((0x0E, 0xFF), (link.Commands[0][4], link.Commands[0][8]));
        link.Forget();

        board.SetMode("Disco", 50, Rgb.Black);
        Assert.Empty(link.Commands);
    }

    [Fact]
    public void A_header_led_count_is_written_to_the_board_with_every_zones_count()
    {
        var link = new FakeBoard();
        using var board = new AsrockBoard(link, "name", "key");

        board.ResizeZone(3, 60); // the second addressable header
        board.ResizeZone(3, 60); // unchanged: nothing more is written
        board.ResizeZone(0, 5); // an RGB header has no count to set
        board.ResizeZone(2, 900);

        var packets = link.Commands;
        Assert.Equal(2, packets.Count);
        Assert.Equal("00150002" + "0101203C1E01031E", Head(packets[0], 12));
        Assert.Equal([1, 1, 100, 60, 1, 3], board.Describe(0).Zones.Select(zone => zone.LedCount));
    }

    [Fact]
    public void Thirty_leds_is_stored_as_thirty_one_because_thirty_means_no_zone()
    {
        var link = new FakeBoard();
        using var board = new AsrockBoard(link, "name", "key");

        board.ResizeZone(3, 30);

        Assert.Equal(31, link.LedCounts[3]);
        Assert.Equal(31, board.Describe(0).Zones[3].LedCount);
    }

    [Fact]
    public void Closing_the_link_puts_the_boards_swap_setting_back()
    {
        var link = new FakeBoard();
        var board = new AsrockBoard(link, "name", "key");
        board.SetDirect();
        Assert.Equal(0, link.Swap);

        board.Dispose();

        Assert.Equal(0b0110_0000, link.Swap);
    }

    [Fact]
    public void A_board_never_put_in_direct_mode_has_nothing_written_on_closing()
    {
        var link = new FakeBoard();
        new AsrockBoard(link, "name", "key").Dispose();

        Assert.Empty(link.Commands);
    }

    [Fact]
    public void An_unplugged_controller_reads_as_lighting_unavailable()
    {
        var link = new FakeBoard();
        using var client = new AsrockUsbLightingClient([new AsrockBoard(link, "name", "key")]);
        link.Unplugged = true;

        Assert.Throws<LightingUnavailableException>(() => client.SetDirect(0));
        client.SetDirect(4); // no such device: ignored
    }
}
