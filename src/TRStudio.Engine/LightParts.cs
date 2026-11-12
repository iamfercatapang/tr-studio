namespace TRStudio.Engine;

/// <summary>
/// Cuts a strip into the parts plugged into it. A header with three fans and a strip chained on it is one
/// run of LEDs to the board; the user lists the parts in order, and each becomes a strip of its own on the
/// canvas. The LEDs still go to the device as one run, in the same order.
/// </summary>
public static class LightParts
{
    public const int MaxParts = 16;
    public const string RestLabel = "Rest";

    /// <summary>The name of a part's strip: the zone's name and the part's place in it, so placements find it again.</summary>
    public static string PartName(string zone, int index) => $"{zone}#{index + 1}";

    public static List<LightPart> Clamped(IEnumerable<LightPart>? parts) =>
        (parts ?? []).Where(part => part != null).Take(MaxParts)
            .Select(part => new LightPart((part.Name ?? "").Trim() is { Length: > 0 } name ? name[..Math.Min(name.Length, 40)] : part.Shape == LightShape.Ring ? "Fan" : "Strip",
                Math.Clamp(part.Leds, 1, LightingSettings.MaxHeaderLeds), Enum.IsDefined(part.Shape) ? part.Shape : LightShape.Strip))
            .ToList();

    /// <summary>
    /// The devices with every divided zone replaced by its parts. A part that runs past the zone's LEDs is
    /// cut short, parts after the end are left out, and LEDs after the last part become one more strip.
    /// A zone nobody divided comes back as it was, under the same name.
    /// </summary>
    public static IReadOnlyList<LightDevice> Split(IReadOnlyList<LightDevice> devices, IReadOnlyDictionary<string, List<LightPart>>? parts)
    {
        if (parts is not { Count: > 0 }) return devices;
        return devices.Select(device => device with { Zones = device.Zones.SelectMany(zone => Split(device.Key, zone, parts)).ToList() }).ToList();
    }

    static IEnumerable<LightZone> Split(string deviceKey, LightZone zone, IReadOnlyDictionary<string, List<LightPart>> parts)
    {
        if (!parts.TryGetValue(LightingSettings.HeaderKey(deviceKey, zone.Name), out var listed) || listed.Count == 0 || zone.LedCount == 0)
        {
            yield return zone;
            yield break;
        }
        var left = zone.LedCount;
        var index = 0;
        foreach (var part in Clamped(listed))
        {
            if (left <= 0) break;
            var leds = Math.Min(part.Leds, left);
            yield return new LightZone(PartName(zone.Name, index++), leds, leds, leds, part.Shape, part.Name);
            left -= leds;
        }
        if (left > 0) yield return new LightZone(PartName(zone.Name, index), left, left, left, LightShape.Strip, RestLabel);
    }
}
