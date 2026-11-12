using System.Diagnostics;
using System.Text.Json.Nodes;
using TRStudio.Engine;
using Microsoft.Win32;

namespace TRStudio.App;

enum OpenRgbPresence { Missing, Installed, Running }

/// <summary>
/// Finds OpenRGB on this PC and starts it with its SDK server on, so the user doesn't have to open it
/// first. TR Studio never installs it: the Lighting page sends the user to OpenRGB's own site for that.
/// </summary>
static class OpenRgbApp
{
    public const string Site = "https://openrgb.org/";
    const string ProcessName = "OpenRGB";
    const string ExeName = "OpenRGB.exe";
    const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static OpenRgbPresence Presence()
    {
        if (IsRunning()) return OpenRgbPresence.Running;
        return FindExe() == null ? OpenRgbPresence.Missing : OpenRgbPresence.Installed;
    }

    static bool IsRunning()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    /// <summary>The installer's folder first, then the place its setup uses by default.</summary>
    static string? FindExe()
    {
        var folders = new List<string?>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var uninstall = root.OpenSubKey(Uninstall);
                foreach (var name in uninstall?.GetSubKeyNames() ?? [])
                {
                    using var entry = uninstall!.OpenSubKey(name);
                    if (entry?.GetValue("DisplayName") is string title && title.StartsWith("OpenRGB", StringComparison.OrdinalIgnoreCase))
                        folders.Add(entry.GetValue("InstallLocation") as string);
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { /* fall back to the default folders */ }
        }
        folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenRGB"));
        folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "OpenRGB"));
        return folders.Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => Path.Combine(folder!.Trim('"'), ExeName))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Starts OpenRGB hidden in its tray with the SDK server on. False when it isn't installed or is already open.</summary>
    public static bool TryStart(Action<string> log)
    {
        if (IsRunning() || FindExe() is not { } exe) return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe, "--server --startminimized")
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            })?.Dispose();
            log("Started OpenRGB for lighting");
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            log($"Could not start OpenRGB: {e.Message}");
            return false;
        }
    }

    public static JsonObject ToJson(LightChoice choice) => new()
    {
        ["kind"] = choice.Kind.ToString(),
        ["color"] = choice.Color,
        ["brightness"] = choice.Brightness,
        ["mode"] = choice.Mode,
        ["speed"] = choice.Speed,
        ["effect"] = choice.Effect.ToString(),
        ["colors"] = new JsonArray((choice.Colors ?? LightChoice.DefaultColors).Select(color => (JsonNode?)color).ToArray()),
    };

    /// <summary>Whatever the page sends is tidied, so a bad value can't reach the lights.</summary>
    public static LightChoice FromJson(JsonObject choice) => new LightChoice(
        Enum.TryParse<LightKind>((string?)choice["kind"], out var kind) ? kind : LightKind.Static,
        (string?)choice["color"] ?? LightChoice.DefaultColor,
        (int?)choice["brightness"] ?? 100,
        (string?)choice["mode"],
        (int?)choice["speed"] ?? 50,
        Enum.TryParse<SmoothEffect>((string?)choice["effect"], out var effect) ? effect : SmoothEffect.RainbowWave,
        choice["colors"] is JsonArray colors ? colors.Select(color => (string?)color ?? LightChoice.DefaultColor).ToList() : null).Clamped();
}
