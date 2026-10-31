using System.Diagnostics;
using System.Drawing;
using CoreVision.Engine;

// Usage:
//   ScreenProbe soak <seconds> [fps]      numbered frames through ScreenLink
//   ScreenProbe render <media|-> <out> [theme.json|built-in name] [WxH]  one frame to a file; the screen is not touched
//   ScreenProbe play <media|-> <seconds>  plays a background on the screen
var clock = Stopwatch.StartNew();
void Log(string line) => Console.WriteLine($"[{clock.Elapsed.TotalSeconds,7:F2}s] {line}");
static BackgroundSpec Spec(string media) =>
    media == "-" ? new BackgroundSpec() : new BackgroundSpec(ThemeStore.KindOf(media) ?? BackgroundKind.Color, Path: media);

switch (args.ElementAtOrDefault(0))
{
    case "render":
    {
        using var sensors = new SensorMonitor(() => new LibreSensorSource());
        sensors.Start();
        Thread.Sleep(TimeSpan.FromSeconds(2));
        var theme = args.Length <= 3 ? Theme.Default
            : Theme.BuiltIns.FirstOrDefault(t => t.Name == args[3]) ?? Theme.FromJson(File.ReadAllText(args[3]));
        var size = args.ElementAtOrDefault(4)?.Split('x').Select(int.Parse).ToArray() ?? [480, 480];
        using var background = Backgrounds.Create(args[1] == "-" ? theme.Background : Spec(args[1]), ThemeStore.FindFfmpeg(), Log, size[0], size[1]);
        if (ThemeStore.KindOf(args[1]) == BackgroundKind.Video) Thread.Sleep(TimeSpan.FromSeconds(2));
        using var renderer = new ThemeRenderer(size[0], size[1]);
        File.WriteAllBytes(args[2], renderer.Render(background, theme.Elements, sensors.Current, DateTime.Now, theme.Dim));
        break;
    }
    case "play":
    {
        var seconds = int.Parse(args[2]);
        using var sensors = new SensorMonitor(() => new LibreSensorSource());
        sensors.Start();
        using var link = new ScreenLink(() => WinUsbDevice.Open(), log: Log);
        link.Start();
        var theme = Theme.Default with { Background = Spec(args[1]) };
        using var player = new ThemePlayer(theme, () => sensors.Current, link.Submit, spec => Backgrounds.Create(spec, ThemeStore.FindFfmpeg(), Log), Log);
        var process = Process.GetCurrentProcess();
        var startCpu = process.TotalProcessorTime;
        player.Start();
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        process.Refresh();
        var cpu = (process.TotalProcessorTime - startCpu).TotalSeconds / seconds / Environment.ProcessorCount * 100;
        Console.WriteLine($"Rendered {player.FramesRendered}, sent {link.FramesSent} ({link.FramesSent / (double)seconds:F1} fps), " +
            $"state {link.Status.State}, CPU {cpu:F1}%, {process.WorkingSet64 / 1024 / 1024} MB");
        break;
    }
    default:
        Soak(int.Parse(args.ElementAtOrDefault(1) ?? "60"), int.Parse(args.ElementAtOrDefault(2) ?? "10"));
        break;
}

void Soak(int seconds, int fps)
{
    using var link = new ScreenLink(() => WinUsbDevice.Open(), log: Log);
    link.Start();
    using var font = new Font("Segoe UI Semibold", 72f, GraphicsUnit.Pixel);
    using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    var submitted = 0;
    while (clock.Elapsed.TotalSeconds < seconds)
    {
        using var bitmap = new Bitmap(480, 480);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(10, 132, 255));
        g.DrawString(submitted.ToString(), font, Brushes.White, new RectangleF(0, 0, 480, 480), centered);
        link.Submit(JpegFrame.Encode(bitmap));
        submitted++;
        Thread.Sleep(1000 / fps);
    }
    Console.WriteLine($"Submitted {submitted}, sent {link.FramesSent}, state {link.Status.State}");
}
