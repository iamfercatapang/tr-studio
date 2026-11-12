using System.Drawing;
using System.Drawing.Imaging;
using TRStudio.Engine;
using Xunit;

public sealed class ThemeTests : IDisposable
{
    readonly string _folder = Directory.CreateTempSubdirectory("trstudio").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    string SaveImage(int width, int height, Color color, string name = "image.png")
    {
        var path = Path.Combine(_folder, name);
        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap)) g.Clear(color);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    static Color CenterOf(byte[] jpeg)
    {
        using var stream = new MemoryStream(jpeg);
        using var bitmap = new Bitmap(stream);
        return bitmap.GetPixel(240, 240);
    }

    static byte[] Render(IBackground background, params ElementSpec[] elements)
    {
        using var renderer = new ThemeRenderer();
        return renderer.Render(background, elements, Stats.Empty, DateTime.Now);
    }

    [Fact]
    public void Default_theme_survives_a_json_round_trip()
    {
        var loaded = Theme.FromJson(Theme.Default.ToJson());
        Assert.Equal(Theme.Default.Name, loaded.Name);
        Assert.Equal(Theme.Default.Elements, loaded.Elements);
    }

    [Fact]
    public void Groups_and_rotation_survive_a_save()
    {
        var json = """{"name":"Grouped","background":{"kind":"Color","color":"#000000"},"elements":[{"kind":"Text","x":10,"y":20,"size":30,"text":"A","group":"Group 1","rotation":45},{"kind":"Gauge","x":240,"y":240,"size":200,"group":"Group 1"}]}""";
        var theme = Theme.FromJson(Theme.FromJson(json).ToJson());
        Assert.All(theme.Elements, element => Assert.Equal("Group 1", element.Group));
        Assert.Equal(45, theme.Elements[0].Rotation);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"name\":\"x\"}")]
    public void Rejects_broken_theme_files(string json)
    {
        Assert.Throws<InvalidDataException>(() => Theme.FromJson(json));
    }

    [Fact]
    public void Store_skips_broken_files_and_lists_the_rest()
    {
        File.WriteAllText(Path.Combine(_folder, "broken.json"), "nope");
        var store = new ThemeStore(_folder);
        var path = store.Save(Theme.Default with { Name = "Mine: 1" });

        var names = store.List().Select(t => t.Name).ToList();

        Assert.Equal(["System Stats", .. Theme.BuiltIns.Select(t => t.Name), "Mine: 1"], names);
        Assert.Equal("Mine: 1", store.Load(path).Name);
    }

    [Fact]
    public void Memory_sizes_show_one_decimal_and_other_stats_none()
    {
        var stats = Stats.From(new RawStats(MemoryUsed: 11.64, MemoryAvailable: 20.3, MemorySpeed: 3200));

        Assert.Equal("11.6 GB", ThemeRenderer.TextOf(new ElementSpec(ElementKind.Metric, 0, 0, 20) { Metric = Metric.MemoryUsed, Unit = " GB" }, stats, DateTime.Now));
        Assert.Equal("3200", ThemeRenderer.TextOf(new ElementSpec(ElementKind.Metric, 0, 0, 20) { Metric = Metric.MemorySpeed }, stats, DateTime.Now));
    }

    [Fact]
    public void Memory_readings_turn_stale_with_the_rest()
    {
        var stats = Stats.From(new RawStats(MemoryUsed: 8, MemoryAvailable: 8, MemorySpeed: 3200));

        Assert.Equal(8, stats.Get(Metric.MemoryAvailable).Value);
        Assert.Equal(ReadingState.Stale, stats.AsStale().Get(Metric.MemorySpeed).State);
    }

    [Fact]
    public void Real_memory_speed_can_be_read()
    {
        Assert.True(SystemSpecs.ReadMemorySpeed() > 0);
    }

    [Fact]
    public void Capture_keeps_the_shape_of_its_desktop_area()
    {
        var element = new ElementSpec(ElementKind.Capture, 240, 240, 200) { CaptureWidth = 1600, CaptureHeight = 900 };

        Assert.Equal(new RectangleF(140, 183.75f, 200, 112.5f), ThemeRenderer.CaptureBox(element));
    }

    [Fact]
    public void Capture_without_an_area_draws_nothing()
    {
        var element = new ElementSpec(ElementKind.Capture, 240, 240, 200);
        using var background = Backgrounds.Create(new BackgroundSpec(BackgroundKind.Color, "#102030"));
        using var renderer = new ThemeRenderer();

        var withCapture = renderer.Render(background, [element], Stats.Empty, DateTime.Now);

        Assert.Null(ThemeRenderer.CaptureBox(element));
        Assert.False(renderer.IsAnimating);
        Assert.Equal(Render(background), withCapture);
    }

    [Fact]
    public void Capture_keeps_the_frames_coming_and_survives_repeated_draws()
    {
        var element = new ElementSpec(ElementKind.Capture, 240, 240, 200) { CaptureWidth = 64, CaptureHeight = 64 };
        using var background = Backgrounds.Create(new BackgroundSpec());
        using var renderer = new ThemeRenderer();

        renderer.Render(background, [element], Stats.Empty, DateTime.Now);
        renderer.Render(background, [element with { Size = 300 }], Stats.Empty, DateTime.Now);

        Assert.True(renderer.IsAnimating);
    }

    [Fact]
    public void Capture_settings_survive_a_json_round_trip()
    {
        var element = new ElementSpec(ElementKind.Capture, 10, 20, 200) { CaptureX = -1920, CaptureY = 5, CaptureWidth = 800, CaptureHeight = 600, CornerRadius = 40 };
        var loaded = Theme.FromJson((Theme.Default with { Elements = [element] }).ToJson());

        Assert.Equal(element, loaded.Elements[0]);
    }

    [Fact]
    public void Window_and_screen_captures_survive_a_json_round_trip()
    {
        var window = new ElementSpec(ElementKind.Capture, 240, 240, 200)
            { Source = CaptureSource.Window, CaptureProcess = "Notepad", CaptureTitle = "Untitled - Notepad", CaptureWidth = 900, CaptureHeight = 700 };
        var screen = new ElementSpec(ElementKind.Capture, 240, 240, 200)
            { Source = CaptureSource.Screen, CaptureScreen = @"\\.\DISPLAY2", CaptureX = 2560, CaptureWidth = 1920, CaptureHeight = 1080 };
        var loaded = Theme.FromJson((Theme.Default with { Elements = [window, screen] }).ToJson());

        Assert.Equal(window, loaded.Elements[0]);
        Assert.Equal(screen, loaded.Elements[1]);
    }

    [Fact]
    public void Captures_saved_before_sources_existed_show_their_area()
    {
        var json = """{"name":"Old","background":{"kind":"Color","color":"#000000"},"elements":[{"kind":"Capture","x":240,"y":240,"size":200,"captureX":10,"captureY":20,"captureWidth":800,"captureHeight":600}]}""";

        var element = Theme.FromJson(json).Elements[0];

        Assert.Equal(CaptureSource.Area, element.Source);
        Assert.Equal(800, element.CaptureWidth);
    }

    [Fact]
    public void A_window_that_is_not_open_leaves_the_background_showing()
    {
        var element = new ElementSpec(ElementKind.Capture, 240, 240, 200)
            { Source = CaptureSource.Window, CaptureProcess = "NoSuchApp-1f6c", CaptureTitle = "Nothing", CaptureWidth = 800, CaptureHeight = 600 };
        using var background = Backgrounds.Create(new BackgroundSpec(BackgroundKind.Color, "#102030"));
        using var renderer = new ThemeRenderer();

        var frame = renderer.Render(background, [element], Stats.Empty, DateTime.Now);

        Assert.True(renderer.IsAnimating); // keeps looking, so the window shows up once it opens
        Assert.Equal(Render(background), frame);
    }

    [Fact]
    public void No_window_is_found_for_an_app_that_is_not_running()
    {
        Assert.Equal(IntPtr.Zero, DesktopWindows.Find("NoSuchApp-1f6c", "Nothing"));
    }

    [Theory]
    [InlineData(1600, 900, 160, 90, 0, 0, 1600, 900)]
    [InlineData(1600, 900, 100, 100, 350, 0, 900, 900)]
    [InlineData(800, 1200, 160, 90, 0, 375, 800, 450)]
    public void A_resized_window_is_cropped_to_the_box_not_stretched(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight,
        int x, int y, int width, int height)
    {
        Assert.Equal(new Rectangle(x, y, width, height), DesktopWindows.Cover(new Size(sourceWidth, sourceHeight), new Size(targetWidth, targetHeight)));
    }

    [Theory]
    [InlineData("AMD Ryzen 7 5800X 8-Core Processor             ", "AMD Ryzen 7 5800X 8-Core Processor")]
    [InlineData("Intel(R) Core(TM) i7-12700K CPU", "Intel Core i7-12700K CPU")]
    public void Part_names_lose_padding_and_marks(string raw, string clean)
    {
        Assert.Equal(clean, SystemSpecs.CleanName(raw));
    }

    [Theory]
    [InlineData("ASUSTeK COMPUTER INC.", "PRIME B550M-A WIFI II", "ASUS PRIME B550M-A WIFI II")]
    [InlineData("Micro-Star International Co., Ltd.", "MAG B650", "Micro-Star International MAG B650")]
    [InlineData("Gigabyte Technology Co., Ltd.", "B550 AORUS", "Gigabyte Technology B550 AORUS")]
    public void Board_names_lose_company_suffixes(string maker, string product, string name)
    {
        Assert.Equal(name, SystemSpecs.BoardName(maker, product));
    }

    [Fact]
    public void Memory_reads_as_total_type_speed_and_modules()
    {
        const long Gb = 1024L * 1024 * 1024;
        MemoryModule stick = new(16 * Gb, 26, 3200, "Corsair", "CMK32GX4M2E3200C16  ");

        Assert.Equal("32 GB DDR4-3200 (2 × 16 GB) CMK32GX4M2E3200C16", SystemSpecs.DescribeMemory([stick, stick]));
        Assert.Null(SystemSpecs.DescribeMemory([]));
    }

    [Theory]
    [InlineData("Samsung SSD 980 PRO 1TB", 1_000_204_886_016, "Samsung SSD 980 PRO 1TB, 1 TB")]
    [InlineData("WD Blue", 500_105_249_280, "WD Blue, 500 GB")]
    [InlineData("Mystery", 0, "Mystery")]
    public void Disks_show_their_sold_size(string model, long bytes, string text)
    {
        Assert.Equal(text, SystemSpecs.DescribeDisk(model, bytes));
    }

    [Fact]
    public void Operating_system_reads_as_name_version_and_build()
    {
        Assert.Equal("Windows 11 Pro 25H2 (Build 26300)", SystemSpecs.DescribeSystem("Microsoft Windows 11 Pro", "25H2", "26300"));
    }

    [Fact]
    public void Real_pc_details_can_be_read()
    {
        var rows = SystemSpecs.Read();

        Assert.Contains(rows, row => row.Label == "Processor");
        Assert.Contains(rows, row => row.Label == "Operating System");
    }

    [Fact]
    public void Default_themes_can_be_removed_and_restored()
    {
        var store = new ThemeStore(_folder);
        var count = store.List().Count;

        store.Delete(null);
        store.Delete(Theme.BuiltInPrefix + Theme.BuiltIns[0].Name);

        Assert.Equal(2, store.RemovedDefaultCount);
        Assert.DoesNotContain(store.List(), entry => entry.Name == Theme.Default.Name || entry.Name == Theme.BuiltIns[0].Name);

        store.RestoreDefaults();

        Assert.Equal(0, store.RemovedDefaultCount);
        Assert.Equal(count, store.List().Count);
    }

    [Fact]
    public void Exported_theme_carries_its_media_to_another_store()
    {
        var image = SaveImage(32, 32, Color.Red);
        var source = new ThemeStore(Path.Combine(_folder, "source"));
        var path = source.Save(Theme.Default with { Name = "Mine", Background = new BackgroundSpec(BackgroundKind.Image, Path: image) });
        var package = Path.Combine(_folder, "Mine" + ThemeStore.PackageExtension);

        source.Export(path, package);
        var target = new ThemeStore(Path.Combine(_folder, "target"));
        var imported = target.Load(target.Import(package));

        Assert.Equal("Mine", imported.Name);
        Assert.Equal(BackgroundKind.Image, imported.Background.Kind);
        Assert.StartsWith(Path.Combine(target.Folder, "media"), imported.Background.Path);
        Assert.Equal(File.ReadAllBytes(image), File.ReadAllBytes(imported.Background.Path!));
    }

    [Fact]
    public void Importing_twice_keeps_both_themes()
    {
        var store = new ThemeStore(_folder);
        var package = Path.Combine(_folder, "Mine" + ThemeStore.PackageExtension);
        store.Export(store.Save(Theme.Default with { Name = "Mine" }), package);

        Assert.Equal("Mine 2", store.Load(store.Import(package)).Name);
        Assert.Equal("Mine 3", store.Load(store.Import(package)).Name);
    }

    [Fact]
    public void Import_ignores_a_path_saved_in_the_package()
    {
        var package = Path.Combine(_folder, "Odd" + ThemeStore.PackageExtension);
        using (var zip = System.IO.Compression.ZipFile.Open(package, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("theme.json").Open()))
            writer.Write((Theme.Default with { Background = new BackgroundSpec(BackgroundKind.Image, Path: @"C:\Windows\secret.png") }).ToJson());
        var store = new ThemeStore(_folder);

        var background = store.Load(store.Import(package)).Background;

        Assert.Equal(BackgroundKind.Color, background.Kind);
        Assert.Null(background.Path);
    }

    [Fact]
    public void Import_rejects_a_file_that_is_not_a_package()
    {
        var file = Path.Combine(_folder, "broken" + ThemeStore.PackageExtension);
        File.WriteAllText(file, "nope");

        Assert.Throws<InvalidDataException>(() => new ThemeStore(_folder).Import(file));
    }

    [Fact]
    public void Deleting_a_theme_removes_only_imported_media_no_theme_uses()
    {
        var store = new ThemeStore(_folder);
        var own = SaveImage(32, 32, Color.Red);
        var package = Path.Combine(_folder, "Mine" + ThemeStore.PackageExtension);
        store.Export(store.Save(Theme.Default with { Name = "Mine", Background = new BackgroundSpec(BackgroundKind.Image, Path: own) }), package);
        var first = store.Import(package);
        var media = store.Load(first).Background.Path!;
        var second = store.Save(store.Load(first) with { Name = "Copy" });

        store.Delete(first);
        Assert.True(File.Exists(media));

        store.Delete(second);
        Assert.False(File.Exists(media));

        store.Delete(Path.Combine(_folder, "Mine.json"));
        Assert.True(File.Exists(own));
    }

    [Fact]
    public void Missing_theme_file_falls_back_to_the_default()
    {
        Assert.Same(Theme.Default, new ThemeStore(_folder).Load(Path.Combine(_folder, "gone.json")));
    }

    [Theory]
    [InlineData(1920, 1080, 420, 0, 1080)]
    [InlineData(1080, 1920, 0, 420, 1080)]
    [InlineData(480, 480, 0, 0, 480)]
    public void Cover_crop_uses_the_centered_square(int width, int height, int x, int y, int side)
    {
        Assert.Equal(new Rectangle(x, y, side, side), Backgrounds.CoverSource(width, height));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 100)]
    [InlineData(2, 40)]
    [InlineData(10, 100)]
    public void Gif_delays_follow_browser_behavior(int hundredths, int milliseconds)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), GifTiming.Normalize(hundredths));
    }

    [Theory]
    [InlineData(10, 1)]
    [InlineData(240, 1)]
    [InlineData(241, 2)]
    [InlineData(1000, 5)]
    public void Long_gifs_are_thinned_to_a_memory_limit(int frames, int step)
    {
        Assert.Equal(step, GifTiming.Step(frames));
    }

    [Fact]
    public void Color_background_renders_a_valid_frame()
    {
        var color = CenterOf(Render(Backgrounds.Create(new BackgroundSpec(Color: "#FF0000"))));
        Assert.True(color.R > 200 && color.G < 60);
    }

    [Fact]
    public void Image_background_fills_the_frame()
    {
        var path = SaveImage(1920, 1080, Color.Lime);
        using var background = Backgrounds.Create(new BackgroundSpec(BackgroundKind.Image, Path: path));
        var color = CenterOf(Render(background));
        Assert.True(color.G > 200 && color.R < 60);
    }

    [Fact]
    public void Single_frame_gif_loads()
    {
        var path = Path.Combine(_folder, "one.gif");
        using (var bitmap = new Bitmap(100, 100)) bitmap.Save(path, ImageFormat.Gif);
        using var background = Backgrounds.Create(new BackgroundSpec(BackgroundKind.Gif, Path: path));
        Assert.Equal(TimeSpan.FromMilliseconds(100), background.FrameDelay);
    }

    [Theory]
    [InlineData(BackgroundKind.Image)]
    [InlineData(BackgroundKind.Gif)]
    [InlineData(BackgroundKind.Video)]
    public void Missing_media_falls_back_to_the_plain_color(BackgroundKind kind)
    {
        var messages = new List<string>();
        using var background = Backgrounds.Create(new BackgroundSpec(kind, "#0000FF", Path.Combine(_folder, "gone")), log: messages.Add);
        Assert.True(CenterOf(Render(background)).B > 200);
        Assert.Single(messages);
    }

    [Fact]
    public void Corrupt_image_falls_back_to_the_plain_color()
    {
        var path = Path.Combine(_folder, "bad.png");
        File.WriteAllText(path, "not an image");
        using var background = Backgrounds.Create(new BackgroundSpec(BackgroundKind.Image, "#0000FF", path));
        Assert.True(CenterOf(Render(background)).B > 200);
    }

    [Fact]
    public void Untrusted_readings_show_a_dash()
    {
        var element = new ElementSpec(ElementKind.Metric, 0, 0, 20) { Metric = Metric.CpuTemp, Unit = "°" };
        Assert.Equal("–", ThemeRenderer.TextOf(element, Stats.From(new RawStats(CpuTemp: 0)), DateTime.Now));
        Assert.Equal("56°", ThemeRenderer.TextOf(element, Stats.From(new RawStats(CpuTemp: 55.9)), DateTime.Now));
    }

    [Fact]
    public void Bad_clock_format_and_color_do_not_fail_a_frame()
    {
        var element = new ElementSpec(ElementKind.Clock, 0, 0, 20) { Format = "%", Color = "nonsense" };
        Assert.NotEmpty(Render(Backgrounds.Create(new BackgroundSpec()), element));
    }

    [Fact]
    public void Player_switches_themes_and_stops_cleanly()
    {
        var opened = new List<string>();
        var frames = 0;
        using var player = new ThemePlayer(Theme.Default, () => Stats.Empty, _ => Interlocked.Increment(ref frames), spec =>
        {
            lock (opened) opened.Add(spec.Color);
            return Backgrounds.Create(spec);
        });
        player.Start();
        WaitFor(() => Volatile.Read(ref frames) >= 1);

        player.Play(Theme.Default with { Background = new BackgroundSpec(Color: "#112233") });

        WaitFor(() => { lock (opened) return opened.Contains("#112233"); });
    }

    [Theory]
    [InlineData("a.PNG", BackgroundKind.Image)]
    [InlineData("a.gif", BackgroundKind.Gif)]
    [InlineData("a.mp4", BackgroundKind.Video)]
    [InlineData("a.txt", null)]
    public void Recognizes_media_by_extension(string path, BackgroundKind? kind)
    {
        Assert.Equal(kind, ThemeStore.KindOf(path));
    }

    static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            Thread.Sleep(5);
        }
    }
}
