using System.Text.Json;
using System.Text.RegularExpressions;

namespace TRStudio.Engine;

/// <summary>A published TR Studio release whose installer can be downloaded and checked.</summary>
public sealed record AppRelease(Version Version, string Notes, string InstallerName, string InstallerUrl, long Size, string Sha256);

/// <summary>
/// Reads TR Studio's latest GitHub release. Only an installer GitHub gives a SHA-256 for is offered,
/// so a download that was cut short or changed on the way is never run.
/// </summary>
public static partial class AppUpdate
{
    public const string Repository = "iamfercatapang/tr-studio";
    public const string LatestReleaseUrl = $"https://api.github.com/repos/{Repository}/releases/latest";
    const string DownloadPrefix = $"https://github.com/{Repository}/releases/download/";
    const string DigestPrefix = "sha256:";

    [GeneratedRegex(@"^TRStudio-[0-9.]+-Setup\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex InstallerName();

    /// <summary>"v1.4.0", "1.4" and "1.4.0.0" all read as 1.4.0, so tags and assembly versions compare alike.</summary>
    public static Version? ParseVersion(string? text)
    {
        var trimmed = text?.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(trimmed, out var version)) return null;
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    /// <summary>Null for a draft, a prerelease, or a release without a usable installer.</summary>
    public static AppRelease? Parse(string json)
    {
        try
        {
            var root = JsonDocument.Parse(json).RootElement;
            if (Flag(root, "draft") || Flag(root, "prerelease")) return null;
            if (ParseVersion(Text(root, "tag_name")) is not { } version) return null;
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Text(asset, "name");
                var url = Text(asset, "browser_download_url");
                var digest = Text(asset, "digest");
                if (name == null || !InstallerName().IsMatch(name)) continue;
                if (url == null || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) continue;
                if (digest == null || !digest.StartsWith(DigestPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var sha = digest[DigestPrefix.Length..].ToLowerInvariant();
                if (sha.Length != 64 || !sha.All(Uri.IsHexDigit)) continue;
                var size = asset.TryGetProperty("size", out var bytes) && bytes.TryGetInt64(out var value) ? value : 0;
                return new AppRelease(version, Text(root, "body")?.Trim() ?? "", name, url, size, sha);
            }
            return null;
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; } // a field of the wrong kind
    }

    public static bool IsNewer(AppRelease release, Version current) =>
        release.Version > (ParseVersion(current.ToString()) ?? current);

    public static bool Matches(byte[] sha256, AppRelease release) =>
        Convert.ToHexString(sha256).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase);

    static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
