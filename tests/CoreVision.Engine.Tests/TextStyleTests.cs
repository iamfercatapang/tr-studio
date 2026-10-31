using System.Drawing;
using System.Globalization;
using CoreVision.Engine;
using Xunit;

public class TextStyleTests
{
    static readonly DateTime Sample = new(2026, 3, 7, 14, 5, 9);

    static Bitmap Render(ElementSpec element)
    {
        using var renderer = new ThemeRenderer();
        using var background = Backgrounds.Create(new BackgroundSpec(Color: "#FFFFFF"));
        return new Bitmap(new MemoryStream(renderer.Render(background, [element], Stats.Empty, Sample)));
    }

    static int Count(Bitmap bitmap, Func<Color, bool> match)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
            for (var x = 0; x < bitmap.Width; x += 2)
                if (match(bitmap.GetPixel(x, y))) count++;
        return count;
    }

    static bool IsRed(Color c) => c.R > 180 && c.G < 90 && c.B < 90;
    static bool IsGrayish(Color c) => c.R < 235 && Math.Abs(c.R - c.G) < 25 && Math.Abs(c.G - c.B) < 25;

    static ElementSpec Word => new(ElementKind.Text, 240, 180, 80) { Text = "HELLO", Align = TextAlign.Center, Color = "#FFFFFF" };

    [Theory]
    [InlineData("MMM d, yyyy", "Mar 7, 2026")]
    [InlineData("MMMM d, yyyy", "March 7, 2026")]
    [InlineData("ddd, MMM d", "Sat, Mar 7")]
    [InlineData("dddd", "Saturday")]
    [InlineData("dd/MM/yyyy", "07/03/2026")]
    [InlineData("MM/dd/yyyy", "03/07/2026")]
    [InlineData("yyyy-MM-dd", "2026-03-07")]
    public void Date_formats_do_not_depend_on_the_pc_region(string format, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var element = new ElementSpec(ElementKind.Date, 0, 0, 20) { Format = format };
            Assert.Equal(expected, ThemeRenderer.TextOf(element, Stats.Empty, Sample));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Stroke_outlines_white_text()
    {
        using var plain = Render(Word);
        using var stroked = Render(Word with { Stroke = true, StrokeColor = "#FF0000", StrokeWidth = 4 });
        Assert.Equal(0, Count(plain, IsRed));
        Assert.True(Count(stroked, IsRed) > 200);
    }

    [Fact]
    public void Stroke_is_ignored_when_turned_off()
    {
        using var bitmap = Render(Word with { Stroke = false, StrokeColor = "#FF0000", StrokeWidth = 4 });
        Assert.Equal(0, Count(bitmap, IsRed));
    }

    [Fact]
    public void Shadow_darkens_the_background()
    {
        using var plain = Render(Word);
        using var shadowed = Render(Word with { Shadow = true });
        Assert.True(Count(shadowed, IsGrayish) > Count(plain, IsGrayish) + 200);
    }

    [Fact]
    public void Hard_shadow_with_no_blur_renders()
    {
        using var bitmap = Render(Word with { Shadow = true, ShadowBlur = 0, ShadowX = 8, ShadowY = 8, ShadowColor = "#FF0000" });
        Assert.True(Count(bitmap, IsRed) > 200);
    }

    [Fact]
    public void Missing_font_falls_back_instead_of_failing_the_frame()
    {
        using var bitmap = Render(Word with { FontFamily = "Not A Real Font", Color = "#FF0000" });
        Assert.True(Count(bitmap, IsRed) > 200);
    }

    [Theory]
    [InlineData("Segoe UI", true, false)]
    [InlineData("Segoe UI", false, true)]
    [InlineData("Segoe UI", true, true)]
    [InlineData("Consolas", true, true)]
    [InlineData("Webdings", true, true)] // ships no bold or italic
    public void Every_style_request_renders(string family, bool bold, bool italic)
    {
        using var bitmap = Render(Word with { FontFamily = family, Bold = bold, Italic = italic, Color = "#FF0000" });
        Assert.True(Count(bitmap, IsRed) > 50);
    }

    static Bitmap RenderReading(ElementSpec element)
    {
        using var renderer = new ThemeRenderer();
        using var background = Backgrounds.Create(new BackgroundSpec(Color: "#FFFFFF"));
        return new Bitmap(new MemoryStream(renderer.Render(background, [element], Stats.From(new RawStats(GpuTemp: 88)), Sample)));
    }

    static ElementSpec Reading(string unit, TextAlign align = TextAlign.Center) =>
        new(ElementKind.Metric, 240, 150, 120) { Metric = Metric.GpuTemp, Unit = unit, Color = "#FF0000", Align = align };

    [Fact]
    public void Degree_sign_is_much_smaller_than_at_text_size()
    {
        using var number = RenderReading(Reading(""));
        using var small = RenderReading(Reading("°"));
        using var full = Render(new ElementSpec(ElementKind.Text, 240, 150, 120) { Text = "88°", Color = "#FF0000", Align = TextAlign.Center });

        var smallSign = Count(small, IsRed) - Count(number, IsRed);
        var fullSign = Count(full, IsRed) - Count(number, IsRed);
        Assert.InRange(smallSign, 1, fullSign / 2);
    }

    static (int Left, int Right) RedSpan(Bitmap bitmap)
    {
        var columns = Enumerable.Range(0, bitmap.Width)
            .Where(x => Enumerable.Range(0, bitmap.Height).Any(y => IsRed(bitmap.GetPixel(x, y)))).ToList();
        return (columns[0], columns[^1]);
    }

    [Fact]
    public void Number_and_degree_sign_stay_centered_as_one_unit()
    {
        using var bitmap = RenderReading(Reading("°"));
        var (left, right) = RedSpan(bitmap);
        Assert.InRange((left + right) / 2, 236, 244);
    }

    [Fact]
    public void Right_aligned_degree_sign_ends_at_the_anchor()
    {
        using var bitmap = RenderReading(Reading("°", TextAlign.Right) with { X = 400 });
        Assert.InRange(RedSpan(bitmap).Right, 394, 402);
    }

    [Fact]
    public void Untrusted_temperature_still_shows_only_a_dash()
    {
        var element = new ElementSpec(ElementKind.Metric, 240, 150, 60) { Metric = Metric.CpuTemp, Unit = "°", Color = "#FF0000" };
        using var bitmap = RenderReading(element);
        Assert.True(Count(bitmap, IsRed) > 0);
    }

    [Fact]
    public void Hidden_items_are_not_drawn_but_stay_in_the_theme()
    {
        using var bitmap = Render(Word with { Hidden = true, Color = "#FF0000" });
        Assert.Equal(0, Count(bitmap, IsRed));

        var theme = new Theme("A", new BackgroundSpec(), [Word with { Hidden = true }]);
        Assert.True(Theme.FromJson(theme.ToJson()).Elements[0].Hidden);
    }

    [Fact]
    public void Empty_text_is_skipped()
    {
        using var bitmap = Render(Word with { Text = "" });
        Assert.Equal(Color.White.R, bitmap.GetPixel(240, 200).R);
    }

    [Fact]
    public void Themes_saved_before_these_options_still_load()
    {
        const string json = """{"name":"Old","background":{"kind":"Color","color":"#000000"},"elements":[{"kind":"Text","x":1,"y":2,"size":20,"text":"A"}]}""";
        var item = Theme.FromJson(json).Elements[0];
        Assert.Equal("Segoe UI", item.FontFamily);
        Assert.False(item.Stroke);
        Assert.False(item.Shadow);
    }

    [Fact]
    public void New_style_options_survive_saving()
    {
        var item = new ElementSpec(ElementKind.Date, 1, 2, 30)
        {
            Format = "dd/MM/yyyy", FontFamily = "Consolas", Italic = true,
            Stroke = true, StrokeColor = "#112233", StrokeWidth = 3,
            Shadow = true, ShadowColor = "#445566", ShadowX = -4, ShadowY = 5, ShadowBlur = 9,
        };
        var theme = new Theme("A", new BackgroundSpec(), [item]);
        Assert.Equal(item, Theme.FromJson(theme.ToJson()).Elements[0]);
    }

    [Fact]
    public void Default_theme_fits_inside_the_round_screen()
    {
        // Centers plus a generous half-width for each column of text.
        foreach (var item in Theme.Default.Elements)
        {
            var halfWidth = item.Size * 1.3f;
            var left = item.Align switch { TextAlign.Center => item.X - halfWidth, TextAlign.Right => item.X - 2 * halfWidth, _ => item.X };
            var right = left + 2 * halfWidth;
            foreach (var (x, y) in new[] { (left, item.Y), (right, item.Y), (left, item.Y + item.Size * 1.35f), (right, item.Y + item.Size * 1.35f) })
                Assert.True(Math.Sqrt(Math.Pow(x - 240, 2) + Math.Pow(y - 240, 2)) < 240, $"{item.Text ?? item.Metric.ToString()} at ({x}, {y})");
        }
    }
}
