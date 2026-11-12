using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TRStudio.App;

/// <summary>
/// An eyedropper for anywhere on the desktop. A magnifier follows the pointer, a left click takes
/// the color under it, and Esc or a right click cancels. The click is held back from whatever
/// is underneath, so choosing a color never presses something in another app.
/// </summary>
sealed class ScreenColorPicker : Form
{
    const int SampleSize = 11;
    const int Zoom = 11;
    const int LoupeSize = SampleSize * Zoom;
    const int LabelHeight = 28;
    const int PointerOffset = 22;
    const int WmLButtonDown = 0x0201, WmLButtonUp = 0x0202, WmRButtonDown = 0x0204, WmRButtonUp = 0x0205;
    const int WhMouseLowLevel = 14;
    const int VkEscape = 0x1B;
    const int WsExNoActivate = 0x08000000, WsExToolWindow = 0x80, WsExTopMost = 0x8;

    readonly Action<Color?> _done;
    readonly Action<string> _log;
    readonly Bitmap _sample = new(SampleSize, SampleSize);
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    readonly HookProc _hookProc; // held here so the garbage collector can't free the callback
    readonly Font _font = new("Segoe UI Semibold", 12f, GraphicsUnit.Pixel);
    IntPtr _hook;
    Color _color = Color.Black;
    Color? _result;
    bool _finishing;

    public ScreenColorPicker(Action<Color?> done, Action<string> log)
    {
        _done = done;
        _log = log;
        _hookProc = OnMouseHook;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(LoupeSize, LoupeSize + LabelHeight);
        DoubleBuffered = true;
        _timer.Tick += (_, _) => Follow();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTopMost;
            return parameters;
        }
    }

    public void Start()
    {
        _hook = SetWindowsHookEx(WhMouseLowLevel, _hookProc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            _log($"Eyedropper could not start: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            Finish();
            return;
        }
        Follow();
        Show();
        _timer.Start();
    }

    void Follow()
    {
        if ((GetAsyncKeyState(VkEscape) & 0x8000) != 0)
        {
            _result = null;
            Finish();
            return;
        }

        var cursor = Cursor.Position;
        try
        {
            using var g = Graphics.FromImage(_sample);
            g.CopyFromScreen(cursor.X - SampleSize / 2, cursor.Y - SampleSize / 2, 0, 0, _sample.Size);
            _color = _sample.GetPixel(SampleSize / 2, SampleSize / 2);
        }
        catch (Win32Exception) { return; } // the screen can't be read during a lock or a UAC prompt

        var screen = Screen.FromPoint(cursor).Bounds;
        var x = cursor.X + PointerOffset + Width > screen.Right ? cursor.X - PointerOffset - Width : cursor.X + PointerOffset;
        var y = cursor.Y + PointerOffset + Height > screen.Bottom ? cursor.Y - PointerOffset - Height : cursor.Y + PointerOffset;
        Location = new Point(x, y);
        Invalidate();
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_sample, new Rectangle(0, 0, LoupeSize, LoupeSize));

        var center = SampleSize / 2 * Zoom;
        g.PixelOffsetMode = PixelOffsetMode.Default;
        g.DrawRectangle(Pens.Black, center - 1, center - 1, Zoom + 1, Zoom + 1);
        g.DrawRectangle(Pens.White, center, center, Zoom - 1, Zoom - 1);

        var label = new Rectangle(0, LoupeSize, LoupeSize, LabelHeight);
        using var fill = new SolidBrush(_color);
        g.FillRectangle(fill, label);
        var dark = _color.GetBrightness() > 0.55f;
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(ToHex(_color), _font, dark ? Brushes.Black : Brushes.White, label, centered);
        g.DrawRectangle(Pens.Black, 0, 0, Width - 1, Height - 1);
    }

    IntPtr OnMouseHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            switch ((int)message)
            {
                case WmLButtonDown:
                    _result = _color;
                    return 1;
                case WmRButtonDown:
                    _result = null;
                    return 1;
                case WmLButtonUp or WmRButtonUp:
                    BeginInvoke(Finish); // the matching release is held back too
                    return 1;
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    void Finish()
    {
        if (_finishing) return;
        _finishing = true;
        _timer.Stop();
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _done(_result);
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _sample.Dispose();
            _font.Dispose();
        }
        base.Dispose(disposing);
    }

    delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int hook, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32", SetLastError = true)]
    static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32")]
    static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? name);
}
