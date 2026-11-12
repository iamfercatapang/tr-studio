using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TRStudio.Engine;

public interface IBackground : IDisposable
{
    /// <summary>How long the current frame should stay on screen.</summary>
    TimeSpan FrameDelay { get; }
    void Draw(Graphics g);
    void Advance();
}

public static class Backgrounds
{
    /// <summary>A background that cannot load falls back to its plain color, so a moved file never stops the screen.</summary>
    public static IBackground Create(BackgroundSpec spec, string? ffmpegPath = null, Action<string>? log = null,
        int width = ThemeRenderer.ScreenSize, int height = ThemeRenderer.ScreenSize)
    {
        try
        {
            return spec.Kind switch
            {
                BackgroundKind.Image => new ImageBackground(RequirePath(spec), width, height),
                BackgroundKind.Gif => new GifBackground(RequirePath(spec), width, height),
                BackgroundKind.Video => new VideoBackground(RequirePath(spec), ffmpegPath ?? throw new FileNotFoundException("ffmpeg was not found."), log, width, height),
                _ => new ColorBackground(spec.Color),
            };
        }
        catch (Exception e) when (e is IOException or ArgumentException or OutOfMemoryException or UnauthorizedAccessException)
        {
            log?.Invoke($"Background failed, using plain color: {e.Message}");
            return new ColorBackground(spec.Color);
        }
    }

    /// <summary>
    /// A single frame for theme cards: a GIF's first frame, or a typical frame of a video. Nothing keeps running afterwards.
    /// Falls back to the plain color like <see cref="Create"/>.
    /// </summary>
    public static IBackground Still(BackgroundSpec spec, string? ffmpegPath = null, Action<string>? log = null,
        int width = ThemeRenderer.ScreenSize, int height = ThemeRenderer.ScreenSize)
    {
        try
        {
            return spec.Kind switch
            {
                BackgroundKind.Gif => new ImageBackground(RequirePath(spec), width, height),
                BackgroundKind.Video => new ImageBackground(VideoStill.Read(RequirePath(spec), ffmpegPath ?? throw new FileNotFoundException("ffmpeg was not found."), width, height)),
                _ => Create(spec, ffmpegPath, log, width, height),
            };
        }
        catch (Exception e) when (e is IOException or ArgumentException or OutOfMemoryException or UnauthorizedAccessException
            or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            log?.Invoke($"Background still failed, using plain color: {e.Message}");
            return new ColorBackground(spec.Color);
        }
    }

    static string RequirePath(BackgroundSpec spec) =>
        File.Exists(spec.Path) ? spec.Path! : throw new FileNotFoundException($"Missing file: {spec.Path}");

    /// <summary>The centered source area that fills the target shape without distorting the image.</summary>
    public static Rectangle CoverSource(int width, int height, int targetWidth = 1, int targetHeight = 1)
    {
        var scale = Math.Min(width / (double)targetWidth, height / (double)targetHeight);
        var (cropWidth, cropHeight) = ((int)Math.Round(targetWidth * scale), (int)Math.Round(targetHeight * scale));
        return new Rectangle((width - cropWidth) / 2, (height - cropHeight) / 2, cropWidth, cropHeight);
    }

    internal static void DrawCover(Graphics g, Image image, int width, int height)
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var target = new Rectangle(0, 0, width, height);
        g.DrawImage(image, target, CoverSource(image.Width, image.Height, width, height), GraphicsUnit.Pixel);
    }

    /// <summary>Copies a file into memory so the original can be moved or edited while it plays.</summary>
    internal static Image LoadImage(string path) => Image.FromStream(new MemoryStream(File.ReadAllBytes(path)));

    /// <summary>Copies tightly packed BGR rows, as ffmpeg writes them, into a 24-bit bitmap of the same size.</summary>
    internal static void CopyPixels(Bitmap target, byte[] pixels)
    {
        var (width, height) = (target.Width, target.Height);
        var data = target.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            for (var row = 0; row < height; row++)
                Marshal.Copy(pixels, row * width * 3, data.Scan0 + row * data.Stride, width * 3);
        }
        finally { target.UnlockBits(data); }
    }
}

/// <summary>One frame of a video for theme cards. Frames are kept per file, so ffmpeg runs once until the file changes.</summary>
public static class VideoStill
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    static readonly ConcurrentDictionary<(string Path, DateTime Written, int Width, int Height), byte[]> Frames = new();

    /// <summary>ffmpeg's thumbnail filter picks a typical frame from the start, which skips black or faded openings.</summary>
    public static IReadOnlyList<string> Arguments(string path, int width, int height) =>
    [
        "-nostdin", "-loglevel", "error", "-i", path, "-an", "-sn",
        "-vf", $"thumbnail,scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height}",
        "-frames:v", "1", "-pix_fmt", "bgr24", "-f", "rawvideo", "pipe:1",
    ];

    internal static Bitmap Read(string path, string ffmpeg, int width, int height)
    {
        var key = (path, File.GetLastWriteTimeUtc(path), width, height);
        if (!Frames.TryGetValue(key, out var pixels))
        {
            pixels = Decode(path, ffmpeg, width, height);
            Frames[key] = pixels;
        }
        var frame = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        Backgrounds.CopyPixels(frame, pixels);
        return frame;
    }

    static byte[] Decode(string path, string ffmpeg, int width, int height)
    {
        if (!File.Exists(ffmpeg)) throw new FileNotFoundException("ffmpeg was not found.");
        var start = new ProcessStartInfo(ffmpeg) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in Arguments(path, width, height)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start.");
        try
        {
            var pixels = new byte[width * height * 3];
            var read = Task.Run(() => process.StandardOutput.BaseStream.ReadExactly(pixels));
            if (Task.WhenAny(read, Task.Delay(Timeout)).GetAwaiter().GetResult() != read)
                throw new TimeoutException("ffmpeg took too long to read a frame.");
            read.GetAwaiter().GetResult();
            return pixels;
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }
}

sealed class ColorBackground(string color) : IBackground
{
    readonly Color _color = ThemeRenderer.ParseColor(color, Color.Black);
    public TimeSpan FrameDelay => TimeSpan.FromSeconds(1);
    public void Draw(Graphics g) => g.Clear(_color);
    public void Advance() { }
    public void Dispose() { }
}

sealed class ImageBackground : IBackground
{
    readonly Bitmap _image;

    /// <summary>Takes ownership of an already sized frame.</summary>
    public ImageBackground(Bitmap image) => _image = image;

    public ImageBackground(string path, int width, int height)
    {
        _image = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var source = Backgrounds.LoadImage(path);
        using var g = Graphics.FromImage(_image);
        Backgrounds.DrawCover(g, source, width, height);
    }

    public TimeSpan FrameDelay => TimeSpan.FromSeconds(1);
    public void Draw(Graphics g) => g.DrawImageUnscaled(_image, 0, 0);
    public void Advance() { }
    public void Dispose() => _image.Dispose();
}

public static class VideoArguments
{
    public static IReadOnlyList<string> For(string path, int width, int height) => VideoBackground.Arguments(path, width, height);
}

public static class GifTiming
{
    const int DelayProperty = 0x5100;
    public const int MaxFrames = 240;
    static readonly TimeSpan Unspecified = TimeSpan.FromMilliseconds(100);
    static readonly TimeSpan Shortest = TimeSpan.FromMilliseconds(40);

    /// <summary>GIFs store delays in 1/100 s. Like browsers, very short delays are treated as unspecified.</summary>
    public static TimeSpan Normalize(int hundredths)
    {
        if (hundredths <= 1) return Unspecified;
        var delay = TimeSpan.FromMilliseconds(hundredths * 10);
        return delay < Shortest ? Shortest : delay;
    }

    /// <summary>Long GIFs keep every Nth frame, which bounds memory and keeps the playback speed.</summary>
    public static int Step(int frameCount) => Math.Max(1, (int)Math.Ceiling(frameCount / (double)MaxFrames));

    internal static int[] ReadDelays(Image gif, int frames)
    {
        var delays = new int[frames];
        if (!gif.PropertyIdList.Contains(DelayProperty)) return delays;
        var bytes = gif.GetPropertyItem(DelayProperty)?.Value ?? [];
        for (var i = 0; i < frames && (i + 1) * 4 <= bytes.Length; i++) delays[i] = BitConverter.ToInt32(bytes, i * 4);
        return delays;
    }
}

sealed class GifBackground : IBackground
{
    readonly List<(Bitmap Frame, TimeSpan Delay)> _frames = [];
    int _index;

    public GifBackground(string path, int width, int height)
    {
        using var gif = Backgrounds.LoadImage(path);
        var dimension = new FrameDimension(gif.FrameDimensionsList[0]);
        var count = gif.GetFrameCount(dimension);
        var delays = GifTiming.ReadDelays(gif, count);
        var step = GifTiming.Step(count);

        for (var i = 0; i < count; i += step)
        {
            gif.SelectActiveFrame(dimension, i);
            var frame = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(frame)) Backgrounds.DrawCover(g, gif, width, height);
            var delay = TimeSpan.Zero;
            for (var j = i; j < Math.Min(i + step, count); j++) delay += GifTiming.Normalize(delays[j]);
            _frames.Add((frame, delay));
        }
    }

    public TimeSpan FrameDelay => _frames[_index].Delay;
    public void Draw(Graphics g) => g.DrawImageUnscaled(_frames[_index].Frame, 0, 0);
    public void Advance() => _index = (_index + 1) % _frames.Count;

    public void Dispose()
    {
        foreach (var (frame, _) in _frames) frame.Dispose();
    }
}

/// <summary>Decodes on a separate thread, so a stalled ffmpeg leaves the last frame up and stats still update.</summary>
sealed class VideoBackground : IBackground
{
    public const int FramesPerSecond = 24;
    static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(1);

    readonly string _path;
    readonly string _ffmpeg;
    readonly Action<string>? _log;
    readonly int _width;
    readonly int _height;
    readonly Bitmap _frame;
    readonly object _gate = new();
    readonly CancellationTokenSource _stop = new();
    readonly Thread _thread;
    Process? _process;

    public VideoBackground(string path, string ffmpeg, Action<string>? log, int width, int height)
    {
        if (!File.Exists(ffmpeg)) throw new FileNotFoundException("ffmpeg was not found.");
        _path = path;
        _width = width;
        _height = height;
        _frame = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        _ffmpeg = ffmpeg;
        _log = log;
        using (var g = Graphics.FromImage(_frame)) g.Clear(Color.Black);
        _thread = new Thread(Decode) { IsBackground = true, Name = "VideoBackground" };
        _thread.Start();
    }

    public TimeSpan FrameDelay => TimeSpan.FromSeconds(1.0 / FramesPerSecond);
    public void Advance() { }

    public void Draw(Graphics g)
    {
        lock (_gate) g.DrawImageUnscaled(_frame, 0, 0);
    }

    public static IReadOnlyList<string> Arguments(string path, int width, int height) =>
    [
        "-nostdin", "-loglevel", "error", "-re", "-stream_loop", "-1", "-i", path, "-an", "-sn",
        "-vf", $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height},fps={FramesPerSecond}",
        "-pix_fmt", "bgr24", "-f", "rawvideo", "pipe:1",
    ];

    void Decode()
    {
        var buffer = new byte[_width * _height * 3];
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var process = StartDecoder();
                _process = process;
                var output = process.StandardOutput.BaseStream;
                var frames = 0;
                while (!_stop.IsCancellationRequested)
                {
                    output.ReadExactly(buffer);
                    Copy(buffer);
                    frames++;
                }
            }
            catch (Exception e) when (e is EndOfStreamException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception or ObjectDisposedException)
            {
                if (!_stop.IsCancellationRequested) _log?.Invoke($"Video decoder stopped, restarting: {e.Message}");
            }
            _stop.Token.WaitHandle.WaitOne(RestartDelay);
        }
    }

    Process StartDecoder()
    {
        var start = new ProcessStartInfo(_ffmpeg)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in Arguments(_path, _width, _height)) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start.");
    }

    void Copy(byte[] pixels)
    {
        lock (_gate) Backgrounds.CopyPixels(_frame, pixels);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _process?.Kill(); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        _thread.Join(TimeSpan.FromSeconds(3));
        _stop.Dispose();
        _frame.Dispose();
    }
}
