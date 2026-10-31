using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoreVision.Engine;

public sealed record Palette(string Name, IReadOnlyList<string> Colors);

/// <summary>Keeps the user's own palettes in one file. Anything unusable is left out, so a bad file can't break the picker.</summary>
public sealed partial class PaletteStore(string file, Action<string>? log = null)
{
    public const string PackageExtension = ".tspalette";
    public const int MaxColors = 20;
    const int MaxName = 40;

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [GeneratedRegex("^#[0-9A-F]{6}$")]
    private static partial Regex HexColor();

    public IReadOnlyList<Palette> Load()
    {
        try { return File.Exists(file) ? Parse(File.ReadAllText(file)) : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log?.Invoke($"Could not read palettes: {e.Message}");
            return [];
        }
    }

    public void Save(IEnumerable<Palette> palettes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var partial = file + ".partial";
        File.WriteAllText(partial, JsonSerializer.Serialize(palettes.Select(Clean).OfType<Palette>(), Json));
        File.Move(partial, file, true);
    }

    /// <summary>Throws InvalidDataException if this isn't a list of palettes.</summary>
    public static IReadOnlyList<Palette> Parse(string json)
    {
        try { return (JsonSerializer.Deserialize<List<Palette>>(json, Json) ?? []).Select(Clean).OfType<Palette>().ToList(); }
        catch (JsonException e) { throw new InvalidDataException($"Palettes are not valid: {e.Message}", e); }
    }

    public static void Export(Palette palette, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(Clean(palette) ?? throw new InvalidDataException("Palette has no colors."), Json));

    /// <summary>Throws InvalidDataException for anything that is not a usable palette.</summary>
    public static Palette Import(string path)
    {
        try
        {
            return Clean(JsonSerializer.Deserialize<Palette>(File.ReadAllText(path), Json))
                ?? throw new InvalidDataException("File has no palette in it.");
        }
        catch (JsonException e) { throw new InvalidDataException($"Palette is not valid: {e.Message}", e); }
    }

    static Palette? Clean(Palette? palette)
    {
        var name = palette?.Name?.Trim();
        var colors = palette?.Colors?.Select(color => color?.ToUpperInvariant() ?? "").Where(color => HexColor().IsMatch(color)).Take(MaxColors).ToList();
        if (string.IsNullOrEmpty(name) || colors is not { Count: > 0 }) return null;
        return new Palette(name.Length > MaxName ? name[..MaxName] : name, colors);
    }
}
