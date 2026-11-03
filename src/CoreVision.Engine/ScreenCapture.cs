using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CoreVision.Engine;

/// <summary>A live picture for Picture-in-Picture, already scaled to the size it is drawn at.</summary>
interface ICapture : IDisposable
{
    Bitmap Image { get; }
    void Refresh();
}

/// <summary>A live copy of one area of the desktop, already scaled to the size it is drawn at.</summary>
sealed class ScreenCapture(Rectangle source, Size fitted) : ICapture
{
    /// <summary>The monitor's area now, so a rearranged desktop still shows the same screen.</summary>
    public static Rectangle? ScreenBounds(string? deviceName) =>
        Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName == deviceName)?.Bounds;

    static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    readonly Bitmap _raw = new(source.Width, source.Height);
    readonly Stopwatch _sinceCopy = new();

    public Bitmap Image { get; } = new(fitted.Width, fitted.Height);

    /// <summary>The last image stays up when the desktop can't be read, as during a lock or a UAC prompt.</summary>
    public void Refresh()
    {
        if (_sinceCopy.IsRunning && _sinceCopy.Elapsed < Interval) return;
        _sinceCopy.Restart();
        try
        {
            using var copy = Graphics.FromImage(_raw);
            copy.CopyFromScreen(source.Location, Point.Empty, source.Size);
        }
        catch (Win32Exception) { return; }

        using var g = Graphics.FromImage(Image);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_raw, new Rectangle(Point.Empty, fitted));
    }

    public void Dispose()
    {
        _raw.Dispose();
        Image.Dispose();
    }
}
