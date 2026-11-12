using System.Text.Json;
using TRStudio.Engine;
using Xunit;

public sealed class LightingServiceTests
{
    const string Board = "ASUS Board|HID 1";
    const string Memory = "XPG Memory|I2C 0x72";

    /// <summary>Stands in for OpenRGB: remembers what it was told, and can be switched off like a closed program.</summary>
    sealed class FakeServer
    {
        public bool Running = true;
        public int Connections;
        public bool DevicesChanged;
        public readonly List<string> Calls = [];
        public readonly Dictionary<int, Rgb[]> Leds = [];
        public List<LightDevice> Devices =
        [
            new(0, Board, "ASUS Board", "Motherboard",
                [new LightZone("Aura Mainboard", 1, 1, 1), new LightZone("Aura Addressable 1", 0, 0, 120)],
                [new LightMode("Direct", false, 0), new LightMode("Off", false, 0), new LightMode("Rainbow", true, 0), new LightMode("Breathing", true, 1)], 1),
            new(1, Memory, "XPG Memory", "Dram", [new LightZone("DRAM", 5, 5, 5)], [new LightMode("Direct", false, 0), new LightMode("Rainbow", true, 0)], 5),
        ];

        public ILightingClient Connect()
        {
            if (!Running) throw new LightingUnavailableException("OpenRGB isn't answering on this PC.");
            Connections++;
            return new Client(this);
        }

        sealed class Client(FakeServer server) : ILightingClient
        {
            void Check()
            {
                if (!server.Running) throw new LightingUnavailableException("OpenRGB stopped answering.");
            }

            public bool TakeDevicesChanged()
            {
                var changed = server.DevicesChanged;
                server.DevicesChanged = false;
                return changed;
            }

            public IReadOnlyList<LightDevice> Devices() { Check(); return server.Devices.ToList(); }
            public void SetDirect(int device) { Check(); server.Calls.Add($"direct {device}"); }

            public void SetLeds(int device, ReadOnlySpan<Rgb> colors)
            {
                Check();
                server.Leds[device] = colors.ToArray();
                server.Calls.Add($"leds {device}");
            }

            public void SetMode(int device, string mode, int speed, IReadOnlyList<Rgb> colors)
            {
                Check();
                server.Calls.Add($"mode {device} {mode} {speed} {string.Join(",", colors.Select(color => color.ToHex()))}".TrimEnd());
            }

            public void ResizeZone(int device, int zone, int leds)
            {
                Check();
                server.Calls.Add($"resize {device} {zone} {leds}");
                var old = server.Devices[device];
                var zones = old.Zones.Select((entry, index) => index == zone ? entry with { LedCount = leds } : entry).ToList();
                server.Devices[device] = old with { Zones = zones, LedCount = zones.Sum(entry => entry.LedCount) };
            }

            public void Dispose() { }
        }
    }

    const string BoardLed = Board + "|Aura Mainboard", Header = Board + "|Aura Addressable 1", Sticks = Memory + "|DRAM";

    /// <summary>Lighting on, with one effect on the canvas and its settings as given.</summary>
    static LightingSettings On(string effect = "rainbow", params (string Key, string Value)[] values)
    {
        var canvas = new CanvasSettings(effect);
        foreach (var (key, value) in values) canvas = canvas.WithValue(effect, key, value);
        return new LightingSettings(Enabled: true, Canvas: canvas);
    }

    static LightingSettings Solid(string color) => On("solid", ("color", color));

    static (LightingService Service, FakeServer Server) Start(LightingSettings settings)
    {
        var server = new FakeServer();
        return (new LightingService(server.Connect, settings), server);
    }

    [Fact]
    public void Nothing_connects_until_lighting_is_turned_on()
    {
        var (service, server) = Start(new LightingSettings());

        Assert.False(service.Step(0));

        Assert.Equal(0, server.Connections);
        Assert.Equal(LightingState.Off, service.State);
        Assert.Empty(service.Devices);
    }

    [Fact]
    public void A_solid_color_is_sent_once_and_then_the_service_rests()
    {
        var (service, server) = Start(Solid("#FF8000") with { Canvas = Solid("#FF8000").Canvas! with { Brightness = 50 } });

        Assert.False(service.Step(0)); // nothing moves, so no frame is due
        service.Step(1);
        service.Step(2);

        Assert.Equal(LightingState.Connected, service.State);
        Assert.Equal(["direct 0", "leds 0", "direct 1", "leds 1"], server.Calls);
        Assert.All(server.Leds[1], led => Assert.Equal(new Rgb(128, 64, 0), led));
        Assert.Equal(5, service.Preview(Memory)!.Length);
    }

    [Fact]
    public void A_moving_effect_sends_a_new_frame_every_pass()
    {
        var (service, server) = Start(On("rainbow"));

        Assert.True(service.Step(0));
        var first = server.Leds[1];
        Assert.True(service.Step(1));

        Assert.Equal(2, server.Calls.Count(call => call == "leds 1"));
        Assert.Equal(1, server.Calls.Count(call => call == "direct 1"));
        Assert.NotEqual(first, server.Leds[1]);
        Assert.Equal(5, server.Leds[1].Distinct().Count()); // a wave along the strip, not one color
        Assert.Equal(server.Leds[1], service.Preview(Memory));
    }

    [Fact]
    public void Each_led_takes_the_canvas_color_under_its_place_in_the_box()
    {
        var settings = On("rainbow");
        var box = new LightPlacement(40, 90, 200, 10);
        settings = settings with { Canvas = settings.Canvas!.WithPlacement(Sticks, box) };
        var (service, server) = Start(settings);

        service.Step(0);

        var effect = CanvasEffects.Find("rainbow");
        var values = settings.Canvas!.ValuesFor(effect);
        var points = box.Points(5);
        for (var i = 0; i < 5; i++) Assert.Equal(effect.Draw(points[i].X, points[i].Y, 0, values), server.Leds[1][i]);
    }

    [Fact]
    public void Moving_a_box_changes_its_colors_and_turning_it_around_reverses_them()
    {
        var settings = On("rainbow");
        var (service, server) = Start(settings with { Canvas = settings.Canvas!.WithPlacement(Sticks, new LightPlacement(0, 90, 100, 10)) });
        service.Step(0);
        var left = server.Leds[1];

        service.Configure(settings with { Canvas = settings.Canvas!.WithPlacement(Sticks, new LightPlacement(220, 90, 100, 10)) });
        service.Step(0);
        Assert.NotEqual(left, server.Leds[1]);

        service.Configure(settings with { Canvas = settings.Canvas!.WithPlacement(Sticks, new LightPlacement(0, 90, 100, 10, Rotation: 180)) });
        service.Step(0);
        Assert.Equal(left.Reverse(), server.Leds[1]);
    }

    [Fact]
    public void A_strip_can_be_dimmed_by_itself()
    {
        var settings = Solid("#FFFFFF");
        var canvas = settings.Canvas!.WithPlacement(Sticks, new LightPlacement(10, 10, 100, 10, Brightness: 50)) with { Brightness = 50 };
        var (service, server) = Start(settings with { Canvas = canvas });

        service.Step(0);

        Assert.All(server.Leds[1], led => Assert.Equal(new Rgb(64, 64, 64), led)); // half of half
        Assert.Equal(new Rgb(128, 128, 128), server.Leds[0][0]); // the board keeps only the layout's dimming
    }

    [Fact]
    public void A_device_set_to_off_uses_its_own_off_mode_or_black_and_ignores_the_canvas()
    {
        var settings = On("rainbow").WithDevice(Board, new LightChoice(LightKind.Off)).WithDevice(Memory, new LightChoice(LightKind.Off));
        var (service, server) = Start(settings);

        Assert.False(service.Step(0));
        service.Step(1);

        Assert.Equal(["mode 0 Off 50", "direct 1", "leds 1"], server.Calls);
        Assert.All(server.Leds[1], led => Assert.Equal(Rgb.Black, led));
    }

    [Fact]
    public void A_device_on_its_own_effect_ignores_the_canvas_while_the_rest_follow_it()
    {
        var settings = On("rainbow").WithDevice(Board, new LightChoice(LightKind.DeviceEffect, "#FF0000", Mode: "Breathing", Speed: 80));
        var (service, server) = Start(settings);

        Assert.True(service.Step(0));
        server.Calls.RemoveAll(call => call.StartsWith("direct") || call.StartsWith("mode"));
        service.Step(1);

        Assert.Null(service.Preview(Board)); // the device draws it, so there's nothing to show
        Assert.DoesNotContain("leds 0", server.Calls);
        Assert.Contains("leds 1", server.Calls);
    }

    [Fact]
    public void A_device_asked_for_an_effect_it_lacks_shows_the_color_instead()
    {
        var settings = On("rainbow").WithDevice(Memory, new LightChoice(LightKind.DeviceEffect, "#FF0000", Mode: "Breathing"));
        var (service, server) = Start(settings);

        service.Step(0);

        Assert.All(server.Leds[1], led => Assert.Equal(new Rgb(255, 0, 0), led));
    }

    [Fact]
    public void Putting_a_device_back_on_the_canvas_takes_it_out_of_its_own_effect()
    {
        var settings = On("rainbow").WithDevice(Board, new LightChoice(LightKind.Off));
        var (service, server) = Start(settings);
        service.Step(0);
        server.Calls.Clear();

        service.Configure(settings.WithDevice(Board, null));
        service.Step(1);

        Assert.Equal(["direct 0", "leds 0", "leds 1"], server.Calls);
    }

    [Fact]
    public void A_changed_effect_setting_shows_on_the_next_pass()
    {
        var (service, server) = Start(Solid("#FF0000"));
        service.Step(0);
        server.Calls.Clear();

        service.Configure(Solid("#0000FF"));
        service.Step(1);

        Assert.Equal(["leds 0", "leds 1"], server.Calls); // already in direct mode, so only the colors go out
        Assert.All(server.Leds[1], led => Assert.Equal(new Rgb(0, 0, 255), led));
    }

    [Fact]
    public void A_missing_server_is_tried_again_after_a_pause_and_everything_is_sent_again()
    {
        var (service, server) = Start(Solid("#00E5FF"));
        server.Running = false;
        var changes = 0;
        service.Changed += () => changes++;

        service.Step(0);
        Assert.Equal(LightingState.NoServer, service.State);
        server.Running = true;
        service.Step(LightingService.Retry.TotalSeconds - 1);
        Assert.Equal(0, server.Connections); // still waiting out the pause

        service.Step(LightingService.Retry.TotalSeconds);
        Assert.Equal(LightingState.Connected, service.State);
        Assert.Equal(2, changes);
        Assert.Contains("leds 1", server.Calls);

        // The server closes and comes back: the lights get their colors again.
        server.Running = false;
        service.Configure(Solid("#123456"));
        service.Step(10);
        Assert.Equal(LightingState.NoServer, service.State);
        server.Running = true;
        server.Calls.Clear();
        service.Step(10 + LightingService.Retry.TotalSeconds);
        Assert.Equal(2, server.Connections);
        Assert.Equal(["direct 0", "leds 0", "direct 1", "leds 1"], server.Calls);
    }

    [Fact]
    public void Sleep_stops_the_frames_and_waking_sends_everything_again()
    {
        var (service, server) = Start(On("rainbow"));
        service.Step(0);

        service.Suspend();
        server.Calls.Clear();
        Assert.False(service.Step(1));
        Assert.Empty(server.Calls);

        service.Resume();
        Assert.False(service.Step(2)); // the hardware gets a moment to come back first
        Assert.Equal(1, server.Connections);

        Assert.True(service.Step(2 + LightingService.ResumeSettle.TotalSeconds));
        Assert.Equal(2, server.Connections);
        Assert.Contains("direct 1", server.Calls);
    }

    [Fact]
    public void Shortly_after_connecting_every_device_is_set_up_again_in_case_the_board_took_over()
    {
        var (service, server) = Start(Solid("#FF0000"));
        service.Step(0);
        server.Calls.Clear();

        service.Step(LightingService.RefreshAfterConnect[0] - 1);
        Assert.Empty(server.Calls); // a still color isn't sent again before then

        service.Step(LightingService.RefreshAfterConnect[0]);
        Assert.Equal(["direct 0", "leds 0", "direct 1", "leds 1"], server.Calls);

        server.Calls.Clear();
        service.Step(LightingService.RefreshAfterConnect[1]);
        Assert.Equal(["direct 0", "leds 0", "direct 1", "leds 1"], server.Calls);
    }

    [Fact]
    public void Direct_mode_and_a_still_frame_are_sent_again_now_and_then()
    {
        var (service, server) = Start(Solid("#FF0000"));
        var last = LightingService.RefreshAfterConnect[^1];
        service.Step(0);
        service.Step(last);
        server.Calls.Clear();

        service.Step(last + 1);
        Assert.Empty(server.Calls);

        // The board may have gone back to its own effect long after waking: it is taken back.
        service.Step(last + LightingService.KeepFresh.TotalSeconds);
        Assert.Equal(["direct 0", "leds 0", "direct 1", "leds 1"], server.Calls);
    }

    [Fact]
    public void A_device_on_its_own_effect_is_not_restarted_by_the_refresh()
    {
        var settings = Solid("#FF0000").WithDevice(Board, new LightChoice(LightKind.DeviceEffect, "#FF0000", Mode: "Breathing"));
        var (service, server) = Start(settings);
        var last = LightingService.RefreshAfterConnect[^1];
        service.Step(0);
        service.Step(last);
        server.Calls.Clear();

        service.Step(last + LightingService.KeepFresh.TotalSeconds);

        Assert.DoesNotContain(server.Calls, call => call.StartsWith("mode 0"));
        Assert.Equal(["direct 1", "leds 1"], server.Calls);
    }

    [Fact]
    public void A_device_that_stops_answering_mid_frame_is_reconnected_and_lit_again()
    {
        var (service, server) = Start(On("rainbow"));
        service.Step(0);

        server.Running = false; // as a write that timed out turns into LightingUnavailableException
        Assert.False(service.Step(1));
        Assert.Equal(LightingState.NoServer, service.State);

        server.Running = true;
        server.Calls.Clear();
        Assert.True(service.Step(1 + LightingService.Retry.TotalSeconds));
        Assert.Equal(LightingState.Connected, service.State);
        Assert.Contains("leds 0", server.Calls);
        Assert.Contains("leds 1", server.Calls);
    }

    [Fact]
    public void Turning_lighting_off_disconnects_and_leaves_the_leds_alone()
    {
        var (service, server) = Start(Solid("#00E5FF"));
        service.Step(0);
        server.Calls.Clear();

        service.Configure(new LightingSettings());
        service.Step(1);

        Assert.Empty(server.Calls);
        Assert.Equal(LightingState.Off, service.State);
        Assert.Empty(service.Devices);
    }

    [Fact]
    public void A_header_is_told_how_many_leds_it_has_then_lit()
    {
        var settings = Solid("#FFFFFF").WithHeaderLeds(Board, "Aura Addressable 1", 30);
        var (service, server) = Start(settings);

        service.Step(0);
        service.Step(1);

        Assert.Equal(1, server.Calls.Count(call => call == "resize 0 1 30"));
        Assert.Equal(31, service.Devices[0].LedCount);
        Assert.Equal(31, server.Leds[0].Length);
    }

    [Fact]
    public void After_waking_a_header_is_told_its_size_again_and_lit()
    {
        var settings = Solid("#FFFFFF").WithHeaderLeds(Board, "Aura Addressable 1", 30);
        var server = new FakeServer();
        // A new connection knows nothing of the size set on the last one, like a newly opened board.
        var service = new LightingService(() =>
        {
            server.Devices[0] = server.Devices[0] with
            {
                Zones = [new LightZone("Aura Mainboard", 1, 1, 1), new LightZone("Aura Addressable 1", 0, 0, 120)],
                LedCount = 1,
            };
            return server.Connect();
        }, settings);
        service.Step(0);
        Assert.Equal(31, server.Leds[0].Length);

        service.Suspend();
        service.Step(1);
        service.Resume();
        service.Step(2);
        server.Calls.Clear();
        service.Step(2 + LightingService.ResumeSettle.TotalSeconds);

        Assert.Contains("resize 0 1 30", server.Calls);
        Assert.Equal(31, service.Devices[0].LedCount);
        Assert.Equal(31, server.Leds[0].Length);
    }

    [Fact]
    public void A_header_size_past_what_the_board_allows_is_capped()
    {
        var (service, server) = Start(Solid("#FFFFFF").WithHeaderLeds(Board, "Aura Addressable 1", 900));

        service.Step(0);

        Assert.Contains("resize 0 1 120", server.Calls);
    }

    [Fact]
    public void A_newly_plugged_device_gets_its_color()
    {
        var (service, server) = Start(Solid("#00E5FF"));
        service.Step(0);
        server.Calls.Clear();

        server.Devices.Add(new LightDevice(2, "Strip|USB", "Strip", "Ledstrip", [new LightZone("Strip", 10, 10, 10)], [new LightMode("Direct", false, 0)], 10));
        server.DevicesChanged = true;
        service.Step(1);

        Assert.Equal(3, service.Devices.Count);
        Assert.Contains("leds 2", server.Calls);
    }

    [Fact]
    public void Every_strip_has_a_place_on_the_canvas_before_the_user_arranges_anything()
    {
        var (service, _) = Start(On("rainbow"));
        service.Step(0);

        var layout = LightingService.Layout(new CanvasSettings(), service.Devices);

        Assert.Equal([BoardLed, Header, Sticks], layout.Keys);
        Assert.All(layout.Values, box => Assert.Equal(box, box.Clamped()));
        Assert.Equal(3, layout.Values.Select(box => box.Y).Distinct().Count()); // one row each

        var moved = LightingService.Layout(new CanvasSettings().WithPlacement(Sticks, new LightPlacement(5, 6, 70, 8)), service.Devices);
        Assert.Equal(new LightPlacement(5, 6, 70, 8), moved[Sticks]);
        Assert.Equal(layout[Header], moved[Header]);
    }

    [Fact]
    public void The_preview_is_a_picture_of_the_canvas_at_the_layouts_brightness()
    {
        var settings = Solid("#FF0000");
        var (service, _) = Start(settings with { Canvas = settings.Canvas! with { Brightness = 50 } });

        var picture = service.RenderPreview(4, 2);

        Assert.Equal(4 * 2 * 3, picture.Length);
        Assert.Equal([128, 0, 0], picture[..3]);
    }

    [Fact]
    public void Settings_from_before_the_canvas_become_the_matching_canvas_effect()
    {
        var color = new LightingSettings(Enabled: true, All: new LightChoice(LightKind.Static, "#FF8000", Brightness: 40)).ActiveCanvas;
        Assert.Equal(("solid", 40), (color.Effect, color.Brightness));
        Assert.Equal(new Rgb(255, 128, 0), color.ValuesFor(CanvasEffects.Find("solid")).Color("color"));

        var aurora = new LightingSettings(All: new LightChoice(LightKind.Smooth, Effect: SmoothEffect.Aurora, Speed: 70, Colors: ["#112233", "#445566"])).ActiveCanvas;
        var values = aurora.ValuesFor(CanvasEffects.Find("aurora"));
        Assert.Equal("aurora", aurora.Effect);
        Assert.Equal((70, new Rgb(0x11, 0x22, 0x33), new Rgb(0x44, 0x55, 0x66)), (values.Number("speed"), values.Color("color"), values.Color("color2")));

        Assert.Equal(0, new LightingSettings(All: new LightChoice(LightKind.Off)).ActiveCanvas.Brightness);
        Assert.Equal("rainbow", new LightingSettings().ActiveCanvas.Effect);

        // A device that had its own color or effect now follows the canvas; Off and its own effects are kept.
        var settings = new LightingSettings().WithDevice(Board, new LightChoice(LightKind.Static)).WithDevice(Memory, new LightChoice(LightKind.Off));
        Assert.Null(settings.Override(Board));
        Assert.Equal(LightKind.Off, settings.Override(Memory)!.Kind);
        Assert.Null(settings.Override("something else"));
    }

    [Fact]
    public void Effect_presets_bring_back_the_effect_and_its_settings()
    {
        var canvas = new CanvasSettings("sweep").WithValue("sweep", "color", "#00FF00").WithValue("sweep", "width", "30").WithEffectPreset("  Green bar  ");

        var changed = canvas.WithEffect("rainbow").WithValue("sweep", "color", "#FF0000");
        var loaded = changed.LoadEffectPreset("Green bar");

        Assert.Equal("sweep", loaded.Effect);
        var values = loaded.ValuesFor(CanvasEffects.Find("sweep"));
        Assert.Equal((new Rgb(0, 255, 0), 30), (values.Color("color"), values.Number("width")));
        Assert.Single(canvas.WithEffectPreset("green BAR").EffectPresets!); // the same name replaces
        Assert.Empty(canvas.WithoutEffectPreset("Green bar").EffectPresets!);
        Assert.Same(canvas, canvas.WithEffectPreset("   "));
        Assert.Same(canvas, canvas.LoadEffectPreset("Nothing by that name"));
    }

    [Fact]
    public void Layout_presets_bring_back_where_the_hardware_was()
    {
        var first = new LightPlacement(10, 20, 100, 8);
        var canvas = new CanvasSettings().WithPlacement(Sticks, first);
        canvas = canvas.WithLayoutPreset("Desk", canvas.Placements!);

        var loaded = canvas.WithPlacement(Sticks, new LightPlacement(200, 150, 50, 8)).WithPlacement(Header, new LightPlacement(1, 12, 30, 4)).LoadLayoutPreset("Desk");

        Assert.Equal(first, loaded.Placements![Sticks]);
        Assert.Equal(new LightPlacement(1, 12, 30, 4), loaded.Placements[Header]); // not in the preset, so left where it is
        Assert.Empty(canvas.WithoutLayoutPreset("Desk").LayoutPresets!);
    }

    [Fact]
    public void A_setting_the_effect_does_not_have_or_a_bad_value_never_reaches_the_file()
    {
        var canvas = new CanvasSettings("sweep");

        Assert.Same(canvas, canvas.WithValue("sweep", "nonsense", "1"));
        Assert.Same(canvas, canvas.WithValue("no such effect", "color", "#FFFFFF"));
        var tidied = canvas.WithValue("sweep", "width", "9000").WithValue("sweep", "color", "red");
        Assert.Equal("60", tidied.Values!["sweep"]["width"]);
        Assert.Equal("#FF0066", tidied.Values["sweep"]["color"]); // back to the effect's own default
        Assert.Equal("rainbow", new CanvasSettings().WithEffect("gone in a later version").Effect);
    }

    [Fact]
    public void Settings_survive_the_settings_file()
    {
        var settings = On("sweep", ("color", "#112233"), ("vertical", "true"))
            .WithDevice(Memory, new LightChoice(LightKind.DeviceEffect, Mode: "Rainbow"))
            .WithHeaderLeds(Board, "Aura Addressable 1", 30);
        settings = settings with { Canvas = settings.Canvas!.WithPlacement(Sticks, new LightPlacement(5, 6, 70, 8, 90, 60)).WithEffectPreset("Mine") };

        var read = JsonSerializer.Deserialize<LightingSettings>(JsonSerializer.Serialize(settings))!;

        Assert.True(read.Enabled);
        Assert.Equal("sweep", read.ActiveCanvas.Effect);
        var values = read.ActiveCanvas.ValuesFor(CanvasEffects.Find("sweep"));
        Assert.Equal((new Rgb(0x11, 0x22, 0x33), true), (values.Color("color"), values.On("vertical")));
        Assert.Equal(new LightPlacement(5, 6, 70, 8, 90, 60), read.ActiveCanvas.Placements![Sticks]);
        Assert.Equal("Mine", read.ActiveCanvas.EffectPresets!.Single().Name);
        Assert.Equal("Rainbow", read.Override(Memory)!.Mode);
        Assert.Equal(30, read.HeaderLeds![LightingSettings.HeaderKey(Board, "Aura Addressable 1")]);
    }

    [Fact]
    public void A_settings_file_from_before_lighting_reads_as_lighting_off()
    {
        var read = JsonSerializer.Deserialize<LightingSettings>("{}")!;

        Assert.False(read.Enabled);
        Assert.True(read.StartOpenRgb);
        Assert.False(read.Memory);
        Assert.Equal("rainbow", read.ActiveCanvas.Effect);
    }

    [Fact]
    public void A_divided_strip_is_sent_as_one_run_with_each_part_showing_what_is_under_its_own_box()
    {
        // A red bar down the left edge of the canvas; everything else is the dark background.
        var settings = On("sweep", ("speed", "0"), ("width", "20"), ("color", "#FF0000"), ("background", "#000000"));
        settings = settings.WithParts(Memory, "DRAM", [new LightPart("Fan", 2, LightShape.Ring), new LightPart("Strip", 2, LightShape.Strip)]);
        var canvas = settings.Canvas!
            .WithPlacement(Sticks + "#1", new LightPlacement(250, 150, 30, 30))  // far from the bar
            .WithPlacement(Sticks + "#2", new LightPlacement(0, 20, 20, 6))      // on the bar
            .WithPlacement(Sticks + "#3", new LightPlacement(250, 20, 30, 6));   // the leftover LED, far from the bar
        var (service, server) = Start(settings with { Canvas = canvas });

        service.Step(0);

        var leds = server.Leds[1];
        Assert.Equal(5, leds.Length);
        Assert.Equal([new Rgb(0, 0, 0), new Rgb(0, 0, 0)], leds[..2]);
        Assert.All(leds[2..4], led => Assert.True(led.R > 200 && led.G == 0, $"{led}"));
        Assert.Equal(new Rgb(0, 0, 0), leds[4]);
    }
}
