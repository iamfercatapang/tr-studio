using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.Json.Nodes;
using CoreVision.Engine;

namespace CoreVision.App;

/// <summary>The screens and windows a Picture-in-Picture can show, each with a small picture to choose by.</summary>
static class CaptureSources
{
    const int ThumbnailWidth = 240;
    const int ThumbnailQuality = 70;

    public static JsonObject List(Action<string> log)
    {
        var screens = Screen.AllScreens.OrderBy(screen => screen.Bounds.X).ThenBy(screen => screen.Bounds.Y).Select((screen, index) => (JsonNode?)new JsonObject
        {
            ["device"] = screen.DeviceName,
            ["name"] = $"Screen {index + 1}",
            ["primary"] = screen.Primary,
            ["x"] = screen.Bounds.X,
            ["y"] = screen.Bounds.Y,
            ["width"] = screen.Bounds.Width,
            ["height"] = screen.Bounds.Height,
            ["thumbnail"] = Thumbnail(CopyScreen(screen.Bounds, log)),
        }).ToArray();

        var windows = DesktopWindows.List().Select(window => (JsonNode?)new JsonObject
        {
            ["title"] = window.Title,
            ["process"] = window.Process,
            ["width"] = window.Client.Width,
            ["height"] = window.Client.Height,
            ["thumbnail"] = Thumbnail(DesktopWindows.Copy(window.Handle)),
        }).ToArray();

        return new JsonObject { ["type"] = "captureSources", ["screens"] = new JsonArray(screens), ["windows"] = new JsonArray(windows) };
    }

    static Bitmap? CopyScreen(Rectangle bounds, Action<string> log)
    {
        var copy = new Bitmap(bounds.Width, bounds.Height);
        try
        {
            using var g = Graphics.FromImage(copy);
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            return copy;
        }
        catch (Win32Exception e)
        {
            log($"Could not read the desktop: {e.Message}");
            copy.Dispose();
            return null;
        }
    }

    /// <summary>Null for a window with nothing to show now, such as a minimized one. The page shows its name alone.</summary>
    static string? Thumbnail(Bitmap? image)
    {
        if (image == null) return null;
        using (image)
        {
            var height = Math.Max(1, ThumbnailWidth * image.Height / image.Width);
            using var small = new Bitmap(ThumbnailWidth, height);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(image, 0, 0, ThumbnailWidth, height);
            }
            return "data:image/jpeg;base64," + Convert.ToBase64String(JpegFrame.Encode(small, ThumbnailQuality));
        }
    }
}
