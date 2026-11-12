namespace TRStudio.Engine;

/// <summary>
/// Other lighting apps that write to the same devices. Two apps driving one device make it flicker, so the
/// Lighting page says when one is running. OpenRGB isn't here: TR Studio works through it.
/// </summary>
public static class LightingConflicts
{
    static readonly (string Process, string App)[] Known =
    [
        ("ArmouryCrate.Service", "Armoury Crate"),
        ("LightingService", "Armoury Crate"),
        ("iCUE", "iCUE"),
        ("SignalRgb", "SignalRGB"),
        ("AsrPolychromeRGB", "ASRock Polychrome"),
        ("ASRRGBLED", "ASRock Polychrome"),
        ("MSI.CentralServer", "MSI Center"),
        ("LEDKeeper2", "MSI Center"),
        ("RGBFusion", "RGB Fusion"),
    ];

    /// <summary>The apps among these running process names, each once, in the order of the list above.</summary>
    public static IReadOnlyList<string> Among(IEnumerable<string> processNames)
    {
        var running = new HashSet<string>(processNames, StringComparer.OrdinalIgnoreCase);
        return Known.Where(known => running.Contains(known.Process)).Select(known => known.App).Distinct().ToList();
    }
}
