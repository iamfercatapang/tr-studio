using System.Drawing;

namespace TRStudio.App;

static class AppIcon
{
    /// <summary>Picks the frame drawn for this size, so Windows doesn't blur a larger one down.</summary>
    public static Icon Load(Size size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("app.ico")!;
        return new Icon(stream, size);
    }
}
