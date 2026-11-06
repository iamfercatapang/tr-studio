namespace CoreVision.Engine;

/// <summary>A fan header the motherboard reports. Id is LibreHardwareMonitor's sensor identifier, which stays the same between runs.</summary>
public sealed record FanHeader(string Id, string Name, double? Rpm);

/// <summary>
/// Which header is the pump, the CPU fan and the case fan. Null means pick automatically; <see cref="None"/> means show nothing.
/// Boards name their headers differently, so the user can correct a wrong guess in Settings.
/// </summary>
public sealed record FanChoice(string? Pump = null, string? CpuFan = null, string? CaseFan = null)
{
    public const string None = "";
}

public enum FanRole { Pump, CpuFan, CaseFan }

public static class FanMatch
{
    // Boards that only number their headers (the ASUS PRIME B550M-A WIFI II this app started on) keep these old picks.
    static readonly Dictionary<FanRole, string> NumberedFallback = new()
    {
        [FanRole.Pump] = "Fan #7", [FanRole.CpuFan] = "Fan #2", [FanRole.CaseFan] = "Fan #3",
    };

    /// <summary>The header that best fits a role by its name, such as MSI's "Pump Fan" or ASUS's "AIO Pump".</summary>
    public static FanHeader? Guess(IReadOnlyList<FanHeader> headers, FanRole role)
    {
        bool Has(FanHeader header, string word) => header.Name.Contains(word, StringComparison.OrdinalIgnoreCase);
        var named = role switch
        {
            FanRole.Pump => headers.FirstOrDefault(h => Has(h, "pump")),
            FanRole.CpuFan => headers.FirstOrDefault(h => Has(h, "cpu") && !Has(h, "pump") && !Has(h, "opt"))
                ?? headers.FirstOrDefault(h => Has(h, "cpu") && !Has(h, "pump")),
            _ => headers.FirstOrDefault(h => !Has(h, "pump") && (Has(h, "system") || Has(h, "chassis") || Has(h, "case") || Has(h, "sys"))),
        };
        if (named != null) return named;
        // Only fall back to numbers when the board doesn't name its headers at all, or the guess would be a wrong named fan.
        return headers.All(h => h.Name.StartsWith("Fan #", StringComparison.Ordinal))
            ? headers.FirstOrDefault(h => h.Name == NumberedFallback[role])
            : null;
    }

    /// <summary>The user's pick when it's still on this PC, otherwise the guess.</summary>
    public static FanHeader? Resolve(IReadOnlyList<FanHeader> headers, FanChoice choice, FanRole role)
    {
        var picked = role switch { FanRole.Pump => choice.Pump, FanRole.CpuFan => choice.CpuFan, _ => choice.CaseFan };
        if (picked == FanChoice.None) return null;
        return (picked == null ? null : headers.FirstOrDefault(h => h.Id == picked)) ?? Guess(headers, role);
    }
}
