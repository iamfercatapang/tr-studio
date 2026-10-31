using CoreVision.Engine;
using Xunit;

public sealed class PaletteTests : IDisposable
{
    readonly string _folder = Directory.CreateTempSubdirectory("corevision").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    string File_(string name) => Path.Combine(_folder, name);

    [Fact]
    public void Palettes_survive_a_save_and_load()
    {
        var store = new PaletteStore(File_("nested/palettes.json"));
        var palette = new Palette("Mine", ["#112233", "#AABBCC"]);

        store.Save([palette]);

        var loaded = Assert.Single(store.Load());
        Assert.Equal(palette.Name, loaded.Name);
        Assert.Equal(palette.Colors, loaded.Colors);
    }

    [Fact]
    public void No_file_means_no_palettes()
    {
        Assert.Empty(new PaletteStore(File_("missing.json")).Load());
    }

    [Fact]
    public void Damaged_file_reads_as_no_palettes()
    {
        File.WriteAllText(File_("palettes.json"), "{ not json");
        Assert.Empty(new PaletteStore(File_("palettes.json")).Load());
    }

    [Fact]
    public void Export_then_import_gives_the_same_palette()
    {
        var path = File_("mine" + PaletteStore.PackageExtension);
        PaletteStore.Export(new Palette("Mine", ["#112233"]), path);

        var imported = PaletteStore.Import(path);

        Assert.Equal("Mine", imported.Name);
        Assert.Equal(["#112233"], imported.Colors);
    }

    [Fact]
    public void Import_keeps_only_real_colors_and_normalizes_them()
    {
        var path = File_("mixed.tspalette");
        File.WriteAllText(path, """{ "name": "  Mixed  ", "colors": ["#abcdef", "red", "#12", "<script>"] }""");

        var imported = PaletteStore.Import(path);

        Assert.Equal("Mixed", imported.Name);
        Assert.Equal(["#ABCDEF"], imported.Colors);
    }

    [Fact]
    public void Import_caps_the_number_of_colors()
    {
        var path = File_("long.tspalette");
        var colors = string.Join(",", Enumerable.Repeat("\"#112233\"", 50));
        File.WriteAllText(path, $$"""{ "name": "Long", "colors": [{{colors}}] }""");

        Assert.Equal(PaletteStore.MaxColors, PaletteStore.Import(path).Colors.Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "name": "Empty", "colors": [] }""")]
    [InlineData("""{ "name": "", "colors": ["#112233"] }""")]
    [InlineData("""{ "colors": ["#112233"] }""")]
    [InlineData("null")]
    public void Unusable_file_is_rejected(string content)
    {
        var path = File_("bad.tspalette");
        File.WriteAllText(path, content);

        Assert.Throws<InvalidDataException>(() => PaletteStore.Import(path));
    }
}
