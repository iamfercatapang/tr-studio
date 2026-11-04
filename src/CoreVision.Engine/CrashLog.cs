using System.Text.RegularExpressions;

namespace CoreVision.Engine;

/// <summary>
/// Reads TR Studio's own log for crashes, and takes anything personal out of text before it leaves the PC.
/// </summary>
public static partial class CrashLog
{
    public const string LaunchMarker = " Launch: ";
    public const string CrashMarker = " Crash: ";

    /// <summary>True when the last session in the log, the one after the latest launch, ended in a crash.</summary>
    public static bool LastSessionCrashed(IEnumerable<string> lines)
    {
        var crashed = false;
        foreach (var line in lines)
        {
            if (line.Contains(LaunchMarker, StringComparison.Ordinal)) crashed = false;
            else if (line.Contains(CrashMarker, StringComparison.Ordinal)) crashed = true;
        }
        return crashed;
    }

    /// <summary>
    /// Replaces the Windows user name, the PC's name and the user's folders, so a report says what broke and not whose PC it was.
    /// </summary>
    public static string Scrub(string text, string userName, string machineName, string userProfile)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (!string.IsNullOrEmpty(userProfile)) text = text.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        text = UsersFolder().Replace(text, @"$1%USERNAME%");
        if (userName.Length >= 2) text = Regex.Replace(text, $@"\b{Regex.Escape(userName)}\b", "%USERNAME%", RegexOptions.IgnoreCase);
        if (machineName.Length >= 2) text = Regex.Replace(text, $@"\b{Regex.Escape(machineName)}\b", "%COMPUTERNAME%", RegexOptions.IgnoreCase);
        return text;
    }

    /// <summary>Any C:\Users\name folder, including other accounts' and forward-slash paths.</summary>
    [GeneratedRegex(@"([A-Za-z]:[\\/]+Users[\\/]+)(?!%USERNAME%|%USERPROFILE%|Public[\\/]|Default[\\/])[^\\/\r\n""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UsersFolder();
}
