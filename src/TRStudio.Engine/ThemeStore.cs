using System.IO.Compression;

namespace TRStudio.Engine;

public sealed record ThemeEntry(string Name, string? Path);

/// <summary>Themes are JSON files in one folder. A broken file is skipped, never fatal.</summary>
public sealed class ThemeStore(string folder, Action<string>? log = null)
{
    public const string PackageExtension = ".tstheme";

    const string PackageTheme = "theme.json";
    const string PackageMedia = "media";
    const long MaxPackageThemeBytes = 1024 * 1024;

    public string Folder => folder;

    string RemovedDefaultsFile => System.IO.Path.Combine(folder, "removed-defaults.txt");
    string MediaFolder => System.IO.Path.Combine(folder, PackageMedia);

    public IReadOnlyList<ThemeEntry> List()
    {
        var removed = RemovedDefaults();
        var themes = new List<ThemeEntry> { new(Theme.Default.Name, null) };
        themes.AddRange(Theme.BuiltIns.Select(theme => new ThemeEntry(theme.Name, Theme.BuiltInPrefix + theme.Name)));
        themes.RemoveAll(entry => removed.Contains(DefaultKey(entry.Path)));
        if (!Directory.Exists(folder)) return themes;
        foreach (var path in Directory.GetFiles(folder, "*.json").Order())
            if (TryLoad(path) is { } theme) themes.Add(new ThemeEntry(theme.Name, path));
        return themes;
    }

    public Theme Load(string? path)
    {
        if (path?.StartsWith(Theme.BuiltInPrefix) == true)
            return Theme.BuiltIns.FirstOrDefault(theme => Theme.BuiltInPrefix + theme.Name == path) ?? Theme.Default;
        return (path == null ? null : TryLoad(path)) ?? Theme.Default;
    }

    public string Save(Theme theme)
    {
        Directory.CreateDirectory(folder);
        var path = FileFor(theme.Name);
        File.WriteAllText(path, theme.ToJson());
        return path;
    }

    /// <summary>Default themes are only hidden, so they can come back. A theme file is really removed, and only inside the themes folder.</summary>
    public void Delete(string? path)
    {
        if (path == null || path.StartsWith(Theme.BuiltInPrefix))
        {
            Directory.CreateDirectory(folder);
            File.WriteAllLines(RemovedDefaultsFile, RemovedDefaults().Append(DefaultKey(path)).Distinct());
            return;
        }
        var full = System.IO.Path.GetFullPath(path);
        if (System.IO.Path.GetDirectoryName(full) != System.IO.Path.GetFullPath(folder) || !File.Exists(full)) return;
        var media = TryLoad(full)?.Background.Path;
        File.Delete(full);
        if (media != null) DeleteImportedMedia(media);
    }

    public int RemovedDefaultCount => RemovedDefaults().Length;

    public void RestoreDefaults() => File.Delete(RemovedDefaultsFile);

    /// <summary>A package holds the theme and its image or video, so it works on another PC.</summary>
    public void Export(string? path, string file)
    {
        var theme = Load(path);
        var media = theme.Background.Kind != BackgroundKind.Color && File.Exists(theme.Background.Path) ? theme.Background.Path : null;
        var partial = file + ".part";
        try
        {
            using (var stream = File.Create(partial))
            using (var package = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var mediaEntry = media == null ? null : PackageMedia + System.IO.Path.GetExtension(media);
                if (media != null) package.CreateEntryFromFile(media, mediaEntry!, CompressionLevel.Fastest);
                using var writer = new StreamWriter(package.CreateEntry(PackageTheme).Open());
                writer.Write((theme with { Background = theme.Background with { Path = mediaEntry } }).ToJson());
            }
            File.Move(partial, file, true);
        }
        finally { File.Delete(partial); }
    }

    /// <summary>Saves a package as a new theme and returns its path. Throws InvalidDataException for anything else.</summary>
    public string Import(string file)
    {
        using var package = ZipFile.OpenRead(file);
        var entry = package.GetEntry(PackageTheme) ?? throw new InvalidDataException("This isn't a theme package.");
        if (entry.Length > MaxPackageThemeBytes) throw new InvalidDataException("This theme package is too large.");
        using var reader = new StreamReader(entry.Open());
        var theme = Theme.FromJson(reader.ReadToEnd());

        // The saved name and path are never trusted: only a media file of a known type is unpacked, under a new name.
        var mediaEntry = package.Entries.FirstOrDefault(e => e.FullName.StartsWith(PackageMedia + ".") && KindOf(e.FullName) == theme.Background.Kind);
        var background = theme.Background with { Kind = BackgroundKind.Color, Path = null };
        if (mediaEntry != null)
        {
            Directory.CreateDirectory(MediaFolder);
            var media = System.IO.Path.Combine(MediaFolder, Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(mediaEntry.FullName));
            mediaEntry.ExtractToFile(media);
            background = theme.Background with { Path = media };
        }
        return Save(theme with { Name = UniqueName(theme.Name), Background = background });
    }

    string UniqueName(string name)
    {
        var used = List().Select(entry => entry.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = name;
        for (var i = 2; used.Contains(candidate) || File.Exists(FileFor(candidate)); i++) candidate = $"{name} {i}";
        return candidate;
    }

    string FileFor(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return System.IO.Path.Combine(folder, new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()) + ".json");
    }

    /// <summary>Only a file an import unpacked, once no theme uses it. The user's own pictures are never touched.</summary>
    void DeleteImportedMedia(string media)
    {
        var full = System.IO.Path.GetFullPath(media);
        if (System.IO.Path.GetDirectoryName(full) != System.IO.Path.GetFullPath(MediaFolder)) return;
        if (List().Any(entry => string.Equals(Load(entry.Path).Background.Path, media, StringComparison.OrdinalIgnoreCase))) return;
        try { File.Delete(full); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log?.Invoke($"Kept {full}: {e.Message}"); }
    }

    static string DefaultKey(string? path) => path ?? Theme.BuiltInPrefix + Theme.Default.Name;

    string[] RemovedDefaults() => File.Exists(RemovedDefaultsFile) ? File.ReadAllLines(RemovedDefaultsFile) : [];

    Theme? TryLoad(string path)
    {
        try { return Theme.FromJson(File.ReadAllText(path)); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            log?.Invoke($"Skipped theme {path}: {e.Message}");
            return null;
        }
    }

    /// <summary>TRCC's copy is the fallback until the app ships its own.</summary>
    public static string? FindFfmpeg()
    {
        var onPath = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => System.IO.Path.Combine(dir.Trim(), "ffmpeg.exe"));
        var installed = System.IO.Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        return onPath.Prepend(installed).Append(@"C:\Program Files\TRCCCAP\ffmpeg.exe").FirstOrDefault(File.Exists);
    }

    public static BackgroundKind? KindOf(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".bmp" => BackgroundKind.Image,
        ".gif" => BackgroundKind.Gif,
        ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi" => BackgroundKind.Video,
        _ => null,
    };
}
