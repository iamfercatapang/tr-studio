using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;

namespace CoreVision.Engine;

public sealed record DesktopWindow(IntPtr Handle, string Title, string Process, Size Client);

/// <summary>The windows a Picture-in-Picture can follow, and copies of them that work while they are covered.</summary>
public static class DesktopWindows
{
    const int MaxTitle = 512;
    const int MinSide = 16;

    /// <summary>Visible top-level app windows, largest first. TR Studio's own windows are left out.</summary>
    public static IReadOnlyList<DesktopWindow> List()
    {
        var names = new Dictionary<uint, string>();
        var found = new List<DesktopWindow>();
        EnumWindows((hwnd, _) =>
        {
            if (Describe(hwnd, names) is { } window) found.Add(window);
            return true;
        }, IntPtr.Zero);
        return found.OrderByDescending(window => window.Client.Width * window.Client.Height).ToList();
    }

    /// <summary>
    /// The window of that app with that title, or else the app's largest window, so a browser that
    /// switched tabs or an app that was restarted is still found. Without an app, the title alone decides.
    /// </summary>
    public static IntPtr Find(string? process, string? title)
    {
        var windows = List();
        bool SameApp(DesktopWindow window) => string.IsNullOrEmpty(process) || string.Equals(window.Process, process, StringComparison.OrdinalIgnoreCase);
        return windows.FirstOrDefault(window => SameApp(window) && window.Title == title)?.Handle
            ?? (string.IsNullOrEmpty(process) ? null : windows.FirstOrDefault(SameApp)?.Handle)
            ?? IntPtr.Zero;
    }

    /// <summary>The window's inside, without its frame. Null when it is gone, minimized or not answering.</summary>
    public static Bitmap? Copy(IntPtr hwnd)
    {
        if (!IsWindow(hwnd) || IsIconic(hwnd) || IsHungAppWindow(hwnd)) return null;
        if (!GetWindowRect(hwnd, out var frame) || !GetClientRect(hwnd, out var client)) return null;
        var origin = new POINT();
        if (!ClientToScreen(hwnd, ref origin)) return null;
        var frameSize = new Size(frame.Right - frame.Left, frame.Bottom - frame.Top);
        var inside = new Rectangle(origin.X - frame.Left, origin.Y - frame.Top, client.Right, client.Bottom);
        inside.Intersect(new Rectangle(Point.Empty, frameSize));
        if (inside.Width < 1 || inside.Height < 1) return null;

        using var whole = new Bitmap(frameSize.Width, frameSize.Height);
        using (var g = Graphics.FromImage(whole))
        {
            var hdc = g.GetHdc();
            try { if (!PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)) return null; }
            finally { g.ReleaseHdc(hdc); }
        }
        return whole.Clone(inside, whole.PixelFormat);
    }

    /// <summary>The middle of the source with the target's shape, so a resized window crops instead of stretching.</summary>
    public static Rectangle Cover(Size source, Size target)
    {
        var scale = Math.Max(target.Width / (double)source.Width, target.Height / (double)source.Height);
        var width = Math.Min(source.Width, (int)Math.Round(target.Width / scale));
        var height = Math.Min(source.Height, (int)Math.Round(target.Height / scale));
        return new Rectangle((source.Width - width) / 2, (source.Height - height) / 2, width, height);
    }

    static DesktopWindow? Describe(IntPtr hwnd, Dictionary<uint, string> names)
    {
        if (!IsWindowVisible(hwnd)) return null;
        if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return null;
        if ((GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0) return null;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return null;
        var text = new StringBuilder(MaxTitle);
        if (GetWindowText(hwnd, text, MaxTitle) == 0) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return null;
        if (!names.TryGetValue(pid, out var name))
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                name = process.ProcessName;
            }
            catch (ArgumentException) { name = ""; }
            catch (InvalidOperationException) { name = ""; }
            names[pid] = name;
        }
        if (name.Length == 0) return null;

        // A minimized window reports a tiny client area, so its size comes from where it would be restored.
        Size client;
        if (IsIconic(hwnd))
        {
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!GetWindowPlacement(hwnd, ref placement)) return null;
            client = new Size(placement.rcNormalPosition.Right - placement.rcNormalPosition.Left, placement.rcNormalPosition.Bottom - placement.rcNormalPosition.Top);
        }
        else
        {
            if (!GetClientRect(hwnd, out var rect)) return null;
            client = new Size(rect.Right, rect.Bottom);
        }
        if (client.Width < MinSide || client.Height < MinSide) return null;
        return new DesktopWindow(hwnd, text.ToString(), name, client);
    }

    const uint PW_RENDERFULLCONTENT = 0x2; // also copies windows drawn by the GPU, as browsers and games are
    const uint GW_OWNER = 4;
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOOLWINDOW = 0x80;
    const int DWMWA_CLOAKED = 14;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWPLACEMENT
    {
        public int length, flags, showCmd;
        public POINT ptMinPosition, ptMaxPosition;
        public RECT rcNormalPosition;
    }

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}

/// <summary>A live copy of one window, cropped to the shape it is drawn at and scaled to fit it.</summary>
sealed class WindowCapture(string? process, string? title, Size fitted) : ICapture
{
    static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(66);
    static readonly TimeSpan SearchInterval = TimeSpan.FromSeconds(2);

    readonly Stopwatch _sinceCopy = new();
    readonly Stopwatch _sinceSearch = new();
    IntPtr _window;

    public Bitmap Image { get; } = new(fitted.Width, fitted.Height);

    /// <summary>The last image stays up while the window is minimized, closed or not answering.</summary>
    public void Refresh()
    {
        if (_sinceCopy.IsRunning && _sinceCopy.Elapsed < Interval) return;
        _sinceCopy.Restart();
        using var copy = DesktopWindows.Copy(_window) ?? CopyAfterSearch();
        if (copy == null) return;

        using var g = Graphics.FromImage(Image);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(copy, new Rectangle(Point.Empty, fitted), DesktopWindows.Cover(copy.Size, fitted), GraphicsUnit.Pixel);
    }

    Bitmap? CopyAfterSearch()
    {
        if (_sinceSearch.IsRunning && _sinceSearch.Elapsed < SearchInterval) return null;
        _sinceSearch.Restart();
        _window = DesktopWindows.Find(process, title);
        return DesktopWindows.Copy(_window);
    }

    public void Dispose() => Image.Dispose();
}
