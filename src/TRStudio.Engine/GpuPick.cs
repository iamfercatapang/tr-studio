using System.Text.RegularExpressions;

namespace TRStudio.Engine;

public enum GpuMaker { Nvidia, Amd, Intel }

/// <param name="MemoryTotal">Dedicated memory in MB, from "GPU Memory Total"; null when the GPU doesn't report it.</param>
public sealed record GpuCandidate(string Name, GpuMaker Maker, double? MemoryTotal);

/// <summary>A GPU as Settings lists it. <paramref name="Id"/> is the sensor library's identifier, such as "/gpu-amd/1".</summary>
/// <param name="Automatic">True for the GPU that <see cref="GpuPick.Choose"/> picks when the user hasn't chosen one.</param>
public sealed record GpuInfo(string Id, string Name, double? MemoryTotal, bool Automatic);

/// <summary>
/// Picks the graphics card to read when the PC has more than one GPU. LibreHardwareMonitor often lists the
/// processor's built-in graphics first, which sits idle while the real card does the work.
/// </summary>
public static partial class GpuPick
{
    // Ryzen APUs report a name with no model number; a discrete Radeon always has one (RX 9070 XT, Pro W7800).
    [GeneratedRegex(@"^(AMD )?Radeon(\(TM\))? Graphics$", RegexOptions.IgnoreCase)]
    private static partial Regex AmdIntegrated();

    [GeneratedRegex(@"Arc.*\b[AB]\d{3}", RegexOptions.IgnoreCase)]
    private static partial Regex IntelDiscrete();

    /// <summary>True for graphics built into the processor.</summary>
    public static bool LooksIntegrated(GpuCandidate gpu) => gpu.Maker switch
    {
        GpuMaker.Amd => AmdIntegrated().IsMatch(gpu.Name.Trim()),
        // Discrete Arc cards carry a model (A770, B580); Core Ultra's built-in "Intel(R) Arc(TM) Graphics" doesn't.
        GpuMaker.Intel => !IntelDiscrete().IsMatch(gpu.Name),
        _ => false,
    };

    /// <summary>Index of the GPU to read, or -1 for none. Discrete cards win, then the one with the most memory, then list order.</summary>
    public static int Choose(IReadOnlyList<GpuCandidate> gpus)
    {
        var best = -1;
        for (var i = 0; i < gpus.Count; i++)
            if (best < 0 || Better(gpus[i], gpus[best])) best = i;
        return best;
    }

    static bool Better(GpuCandidate a, GpuCandidate b)
    {
        bool aIntegrated = LooksIntegrated(a), bIntegrated = LooksIntegrated(b);
        if (aIntegrated != bIntegrated) return !aIntegrated;
        return (a.MemoryTotal ?? 0) > (b.MemoryTotal ?? 0);
    }
}
