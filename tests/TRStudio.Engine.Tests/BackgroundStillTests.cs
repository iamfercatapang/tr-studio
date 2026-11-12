using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using TRStudio.Engine;
using Xunit;

public class BackgroundStillTests
{
    static Color CenterOf(IBackground background)
    {
        using var frame = new Bitmap(ThemeRenderer.ScreenSize, ThemeRenderer.ScreenSize);
        using (var g = Graphics.FromImage(frame)) background.Draw(g);
        return frame.GetPixel(ThemeRenderer.ScreenSize / 2, ThemeRenderer.ScreenSize / 2);
    }

    static string TempFile(string extension) => Path.Combine(Path.GetTempPath(), $"still-{Guid.NewGuid():N}{extension}");

    /// <summary>The ffmpeg the installer bundles, if this checkout has it. third-party isn't in git.</summary>
    static string? RepoFfmpeg()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "third-party", "ffmpeg", "ffmpeg.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    [Fact]
    public void A_gif_shows_its_first_frame()
    {
        var path = TempFile(".gif");
        try
        {
            using (var gif = new Bitmap(40, 40))
            {
                using (var g = Graphics.FromImage(gif)) g.Clear(Color.Red);
                gif.Save(path, ImageFormat.Gif);
            }
            using var still = Backgrounds.Still(new BackgroundSpec(BackgroundKind.Gif, "#000000", path));
            var color = CenterOf(still);
            Assert.True(color.R > 200 && color.G < 60 && color.B < 60, color.ToString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_missing_file_falls_back_to_the_color()
    {
        using var still = Backgrounds.Still(new BackgroundSpec(BackgroundKind.Gif, "#00FF00", @"C:\nowhere\missing.gif"));
        Assert.Equal(Color.FromArgb(0, 255, 0).ToArgb(), CenterOf(still).ToArgb());
    }

    [Fact]
    public void A_video_without_ffmpeg_falls_back_to_the_color()
    {
        var path = TempFile(".mp4");
        File.WriteAllBytes(path, [0]);
        try
        {
            using var still = Backgrounds.Still(new BackgroundSpec(BackgroundKind.Video, "#0000FF", path), ffmpegPath: null);
            Assert.Equal(Color.FromArgb(0, 0, 255).ToArgb(), CenterOf(still).ToArgb());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Asks_ffmpeg_for_one_frame_scaled_to_cover_the_screen()
    {
        var arguments = VideoStill.Arguments("clip.mp4", 480, 480);
        Assert.Contains("-frames:v", arguments);
        Assert.Equal("1", arguments[arguments.ToList().IndexOf("-frames:v") + 1]);
        Assert.Contains(arguments, argument => argument.StartsWith("thumbnail,scale=480:480") && argument.EndsWith("crop=480:480"));
        Assert.DoesNotContain("-stream_loop", arguments);
    }

    // Needs the bundled ffmpeg. Without it there's nothing to check, so the test passes quietly.
    [Fact]
    public void A_video_shows_a_frame_from_ffmpeg()
    {
        if (RepoFfmpeg() is not { } ffmpeg) return;
        var path = TempFile(".mp4");
        try
        {
            using (var make = Process.Start(new ProcessStartInfo(ffmpeg, ["-nostdin", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=red:s=64x64:d=1", "-pix_fmt", "yuv420p", path])
                { UseShellExecute = false, CreateNoWindow = true })!)
                make.WaitForExit(20000);
            using var still = Backgrounds.Still(new BackgroundSpec(BackgroundKind.Video, "#000000", path), ffmpeg);
            var color = CenterOf(still);
            Assert.True(color.R > 200 && color.G < 60 && color.B < 60, color.ToString());
        }
        finally { File.Delete(path); }
    }
}
