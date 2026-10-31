using System.ComponentModel;
using System.Drawing;

namespace CoreVision.App;

/// <summary>
/// Covers every monitor with a dimmed still of the desktop and lets a drag mark an area.
/// Esc or a right click cancels. The result is in desktop coordinates.
/// </summary>
sealed class ScreenRegionPicker : Form
{
    const int MinSide = 16;
    const int DimAlpha = 150;
    const int HintTop = 24;
    const string Hint = "Drag to choose what to show. Press Esc to cancel.";

    readonly Action<Rectangle?> _done;
    readonly Bitmap _desktop;
    readonly Font _font = new("Segoe UI Semibold", 15f, GraphicsUnit.Pixel);
    Point? _start;
    Rectangle _selection;
    Rectangle? _result;

    ScreenRegionPicker(Bitmap desktop, Rectangle bounds, Action<Rectangle?> done)
    {
        _desktop = desktop;
        _done = done;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
    }

    /// <summary>False when the desktop can't be read, as during a lock or a UAC prompt.</summary>
    public static bool TryShow(Action<Rectangle?> done, Action<string> log)
    {
        var bounds = SystemInformation.VirtualScreen;
        var desktop = new Bitmap(bounds.Width, bounds.Height);
        try
        {
            using var g = Graphics.FromImage(desktop);
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }
        catch (Win32Exception e)
        {
            log($"Could not read the desktop: {e.Message}");
            desktop.Dispose();
            return false;
        }
        new ScreenRegionPicker(desktop, bounds, done).Show();
        return true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) Close();
        else if (e.Button == MouseButtons.Left) _start = e.Location;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_start is not { } start) return;
        _selection = Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X), Math.Max(start.Y, e.Y));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _start == null) return;
        _start = null;
        if (_selection.Width < MinSide || _selection.Height < MinSide)
        {
            _selection = Rectangle.Empty; // a stray click, so the user can try again
            Invalidate();
            return;
        }
        _result = new Rectangle(_selection.X + Left, _selection.Y + Top, _selection.Width, _selection.Height);
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(_desktop, 0, 0);
        using (var dim = new SolidBrush(Color.FromArgb(DimAlpha, Color.Black)))
        using (var outside = new Region(ClientRectangle))
        {
            outside.Exclude(_selection);
            g.FillRegion(dim, outside);
        }

        var label = Hint;
        if (!_selection.IsEmpty)
        {
            g.DrawRectangle(Pens.White, _selection);
            label = $"{_selection.Width} × {_selection.Height}";
        }
        var size = g.MeasureString(label, _font);
        var primary = Screen.PrimaryScreen?.Bounds ?? Bounds;
        var x = primary.X - Left + (primary.Width - size.Width) / 2;
        var y = primary.Y - Top + HintTop;
        using var pill = new SolidBrush(Color.FromArgb(210, 20, 20, 22));
        g.FillRectangle(pill, x - 14, y - 8, size.Width + 28, size.Height + 16);
        g.DrawString(label, _font, Brushes.White, x, y);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _done(_result);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _desktop.Dispose();
            _font.Dispose();
        }
        base.Dispose(disposing);
    }
}
