using System.Text.RegularExpressions;

namespace CoreVision.Engine;

/// <summary>
/// Opens the screen, and names other Thermalright displays it can see but cannot drive yet.
/// Those use different protocols, so nothing is ever sent to them.
/// </summary>
public static partial class ScreenFinder
{
    public static IReadOnlySet<(ushort Vid, ushort Pid)> UnsupportedDisplays { get; } = new HashSet<(ushort, ushort)>
    {
        (0x0416, 0x5302), (0x0416, 0x5406), (0x0416, 0x5408), (0x0416, 0x5409),
    };

    public static IScreenTransport Open(Func<IScreenTransport> open, Func<IEnumerable<string>> usbPaths)
    {
        try { return open(); }
        catch (ScreenNotFoundException)
        {
            var other = usbPaths().Select(ParseIds).FirstOrDefault(ids => ids is { } found && UnsupportedDisplays.Contains(found));
            if (other is { } device)
                throw new ScreenUnsupportedException($"Found a Thermalright display (USB {device.Vid:X4}:{device.Pid:X4}) that isn't supported yet.");
            throw;
        }
    }

    public static (ushort Vid, ushort Pid)? ParseIds(string path)
    {
        var match = UsbIds().Match(path);
        if (!match.Success) return null;
        return (Convert.ToUInt16(match.Groups[1].Value, 16), Convert.ToUInt16(match.Groups[2].Value, 16));
    }

    [GeneratedRegex("vid_([0-9a-f]{4})&pid_([0-9a-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex UsbIds();
}

/// <summary>The commands behind the startup option. A task is used because a Run key can't start an admin app silently.</summary>
public static class StartupCommands
{
    public const string TaskName = "CoreVisionStudio";
    public const string TrccTaskName = "TRCCAppStartup";

    public static IReadOnlyList<string> Create(string exePath) =>
        ["/Create", "/F", "/TN", TaskName, "/SC", "ONLOGON", "/IT", "/RL", "HIGHEST", "/TR", $"\"{exePath}\" --background"];

    public static IReadOnlyList<string> Delete() => ["/Delete", "/F", "/TN", TaskName];
    public static IReadOnlyList<string> Query(string task = TaskName) => ["/Query", "/TN", task];
    public static IReadOnlyList<string> SetTrccEnabled(bool enabled) => ["/Change", "/TN", TrccTaskName, enabled ? "/ENABLE" : "/DISABLE"];
}
