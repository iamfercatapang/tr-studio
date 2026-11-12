using TRStudio.Engine;
using Xunit;

public sealed class LightPartsTests
{
    const string Board = "ASUS Board|HID 1";
    const string Header = "Addressable Header 1";

    static LightDevice Device(int headerLeds) => new(0, Board, "ASUS Board", "Motherboard",
        [new LightZone("Mainboard", 2, 2, 2), new LightZone(Header, headerLeds, 0, 500)], [], 2 + headerLeds);

    static Dictionary<string, List<LightPart>> Parts(params LightPart[] parts) => new() { [LightingSettings.HeaderKey(Board, Header)] = [.. parts] };

    [Fact]
    public void A_strip_nobody_divided_stays_as_it_was()
    {
        var devices = new[] { Device(30) };

        Assert.Same(devices, LightParts.Split(devices, null));
        Assert.Equal(devices[0].Zones, LightParts.Split(devices, new Dictionary<string, List<LightPart>> { ["Other|Zone"] = [new("Fan", 12, LightShape.Ring)] })[0].Zones);
    }

    [Fact]
    public void A_divided_strip_becomes_its_parts_in_order_with_the_leftover_as_a_strip()
    {
        var split = LightParts.Split([Device(40)], Parts(new("Top fan", 12, LightShape.Ring), new("Rear fan", 12, LightShape.Ring)))[0];

        Assert.Equal(["Mainboard", $"{Header}#1", $"{Header}#2", $"{Header}#3"], split.Zones.Select(zone => zone.Name));
        Assert.Equal([2, 12, 12, 16], split.Zones.Select(zone => zone.LedCount));
        Assert.Equal([LightShape.Strip, LightShape.Ring, LightShape.Ring, LightShape.Strip], split.Zones.Select(zone => zone.Shape));
        Assert.Equal([null, "Top fan", "Rear fan", LightParts.RestLabel], split.Zones.Select(zone => zone.Label));
        Assert.Equal(42, split.Zones.Sum(zone => zone.LedCount)); // the device still gets every LED, in the same order
        Assert.All(split.Zones.Skip(1), zone => Assert.False(zone.Resizable));
    }

    [Fact]
    public void Parts_past_the_end_of_the_strip_are_cut_short_or_left_out()
    {
        var split = LightParts.Split([Device(20)], Parts(new("A", 12, LightShape.Ring), new("B", 12, LightShape.Ring), new("C", 12, LightShape.Ring)))[0];

        Assert.Equal([2, 12, 8], split.Zones.Select(zone => zone.LedCount));
    }

    [Fact]
    public void A_header_with_no_leds_yet_is_not_divided()
    {
        var split = LightParts.Split([Device(0)], Parts(new LightPart("Fan", 12, LightShape.Ring)))[0];

        Assert.Equal(["Mainboard", Header], split.Zones.Select(zone => zone.Name));
    }

    [Fact]
    public void Saved_parts_are_tidied_and_clearing_them_drops_their_places()
    {
        var settings = new LightingSettings(Enabled: true, Canvas: new CanvasSettings()
            .WithPlacement($"{Board}|{Header}#1", new LightPlacement(10, 10, 40, 40))
            .WithPlacement($"{Board}|Mainboard", new LightPlacement(0, 0, 50, 10)));

        var parted = settings.WithParts(Board, Header, [new("  ", 0, LightShape.Ring), new("Strip under desk", 5000, LightShape.Strip)]);
        var saved = parted.Parts![LightingSettings.HeaderKey(Board, Header)];
        Assert.Equal([new LightPart("Fan", 1, LightShape.Ring), new LightPart("Strip under desk", LightingSettings.MaxHeaderLeds, LightShape.Strip)], saved);
        Assert.Equal([$"{Board}|Mainboard"], parted.Canvas!.Placements!.Keys); // the old part's place went with it

        Assert.Null(parted.WithParts(Board, Header, []).Parts);
        Assert.Equal(LightParts.MaxParts, settings.WithParts(Board, Header, Enumerable.Repeat(new LightPart("Fan", 8, LightShape.Ring), 40)).Parts!.Values.Single().Count);
    }
}

public sealed class LightingConflictsTests
{
    [Fact]
    public void Known_lighting_apps_are_named_once_whatever_the_case()
    {
        var found = LightingConflicts.Among(["explorer", "lightingservice", "ArmouryCrate.Service", "ICUE", "chrome"]);

        Assert.Equal(["Armoury Crate", "iCUE"], found);
    }

    [Fact]
    public void OpenRgb_and_unknown_programs_are_not_conflicts()
    {
        Assert.Empty(LightingConflicts.Among(["OpenRGB", "TRStudio", "notepad"]));
        Assert.Empty(LightingConflicts.Among([]));
    }
}
