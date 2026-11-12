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
        (0x0416, 0x5409), (0x0418, 0x5303), (0x0418, 0x5304),
    };

    public static IScreenTransport Open(Func<IScreenTransport> open, Func<IEnumerable<string>> usbPaths)
    {
        try { return open(); }
        catch (ScreenNotFoundException)
        {
            var present = usbPaths().Select(ParseIds).OfType<(ushort Vid, ushort Pid)>().ToList();
            // A supported model can be plugged in yet not openable, when Windows gave it a driver other than WinUSB.
            var unopened = ScreenDevice.All.FirstOrDefault(model => present.Contains((model.VendorId, model.ProductId)));
            if (unopened != null)
                throw new ScreenUnsupportedException(unopened.Transport != ScreenTransportKind.WinUsb
                    ? $"Found a {unopened.Name}, but couldn't open it."
                    : $"Found a {unopened.Name}, but couldn't open it. Its USB driver may not be WinUSB.");
            var other = present.FirstOrDefault(UnsupportedDisplays.Contains);
            if (other != default)
                throw new ScreenUnsupportedException($"Found a Thermalright display (USB {other.Vid:X4}:{other.Pid:X4}) that isn't supported yet.");
            throw;
        }
    }

    /// <summary>Opens the first model that is plugged in. A model that is present but fails to open stops the search with its error.</summary>
    public static IScreenTransport OpenFirst(IEnumerable<ScreenDevice> models, Func<ScreenDevice, IScreenTransport> open)
    {
        ScreenNotFoundException? missing = null;
        foreach (var model in models)
        {
            try { return open(model); }
            catch (ScreenNotFoundException e) { missing ??= e; }
            catch (IOException e) when (e is not ScreenBusyException && model != ScreenDevice.CoreVision)
            {
                throw new ScreenUnsupportedException(model.Transport != ScreenTransportKind.WinUsb
                    ? $"Found a {model.Name}, but couldn't open it ({e.Message})."
                    : $"Found a {model.Name}, but couldn't open it ({e.Message}). Its USB driver may not be WinUSB.");
            }
        }
        throw missing ?? new ScreenNotFoundException("Screen is not connected");
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
