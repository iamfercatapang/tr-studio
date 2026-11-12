using System.Net.Sockets;
using OpenRGB.NET;
using Color = OpenRGB.NET.Color;

namespace TRStudio.Engine;

/// <summary>
/// Talks to OpenRGB's SDK server on this PC. OpenRGB does the hardware work for every brand of board,
/// memory, graphics card and strip; TR Studio only tells it what to show. Nothing leaves the PC.
/// </summary>
public sealed class OpenRgbLightingClient : ILightingClient
{
    public const string Host = "127.0.0.1";
    public const int Port = 6742;
    const int TimeoutMs = 1500;
    /// <summary>Modes that aren't effects: they're how TR Studio sends its own colors, or how it turns a device off.</summary>
    static readonly string[] PlainModes = ["Direct", "Custom", "Static", "Off"];

    readonly OpenRgbClient _client;
    Device[] _devices = [];
    int _changed;

    OpenRgbLightingClient(OpenRgbClient client)
    {
        _client = client;
        _client.DeviceListUpdated += (_, _) => Interlocked.Exchange(ref _changed, 1);
    }

    /// <summary>Throws <see cref="LightingUnavailableException"/> when OpenRGB isn't running or its server is off.</summary>
    public static OpenRgbLightingClient Connect()
    {
        if (!IsListening()) throw new LightingUnavailableException("OpenRGB isn't answering on this PC.");
        OpenRgbClient? client = null;
        try
        {
            client = new OpenRgbClient(name: "TR Studio", ip: Host, port: Port, autoConnect: false, timeoutMs: TimeoutMs);
            client.Connect();
            return new OpenRgbLightingClient(client);
        }
        catch (Exception e) when (IsLinkFailure(e))
        {
            client?.Dispose();
            throw new LightingUnavailableException("OpenRGB isn't answering on this PC.", e);
        }
    }

    /// <summary>
    /// Whether anything answers on OpenRGB's port. Asked first, every few seconds while OpenRGB is closed,
    /// because the client library abandons a connect that times out and its failure then surfaces later
    /// as an error nobody was waiting for.
    /// </summary>
    static bool IsListening()
    {
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connect = probe.ConnectAsync(System.Net.IPAddress.Loopback, Port);
        try
        {
            if (connect.Wait(TimeoutMs)) return true;
        }
        catch (AggregateException e) when (e.InnerException is SocketException) { return false; }
        // Still trying after the wait: closing the socket ends it, and its failure is taken here so it isn't left unobserved.
        connect.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        return false;
    }

    public static bool IsEffect(string mode) => !PlainModes.Contains(mode, StringComparer.OrdinalIgnoreCase);

    static bool IsLinkFailure(Exception e) =>
        e is SocketException or IOException or TimeoutException or ObjectDisposedException or InvalidOperationException
            or OperationCanceledException or ArgumentException or IndexOutOfRangeException;

    T Guard<T>(Func<T> call)
    {
        try { return call(); }
        catch (Exception e) when (IsLinkFailure(e)) { throw new LightingUnavailableException($"OpenRGB stopped answering: {e.Message}", e); }
    }

    void Guard(Action call) => Guard(() => { call(); return 0; });

    public bool TakeDevicesChanged() => Interlocked.Exchange(ref _changed, 0) == 1;

    public IReadOnlyList<LightDevice> Devices() => Guard(() =>
    {
        _devices = _client.GetAllControllerData();
        var keys = new HashSet<string>();
        return (IReadOnlyList<LightDevice>)_devices.Select(device =>
        {
            // Two of the same memory stick differ only by where they sit; a number breaks any tie left.
            var key = $"{device.Name}|{device.Location}";
            for (var copy = 2; !keys.Add(key); copy++) key = $"{device.Name}|{device.Location}|{copy}";
            return new LightDevice(device.Index, key, device.Name, device.Type.ToString(),
                device.Zones.Select(zone => new LightZone(zone.Name, (int)zone.LedCount, (int)zone.LedsMin, (int)zone.LedsMax)).ToList(),
                device.Modes.Select(mode => new LightMode(mode.Name, mode.SupportsSpeed,
                    mode.ColorMode == ColorMode.ModeSpecific ? (int)Math.Max(1, mode.ColorMax) : 0)).ToList(),
                device.Colors.Length);
        }).ToList();
    });

    public void SetDirect(int device) => Guard(() => _client.SetCustomMode(device));

    public void SetLeds(int device, ReadOnlySpan<Rgb> colors)
    {
        var leds = new Color[colors.Length];
        for (var i = 0; i < leds.Length; i++) leds[i] = new Color(colors[i].R, colors[i].G, colors[i].B);
        Guard(() => _client.UpdateLeds(device, leds));
    }

    public void SetMode(int device, string mode, int speed, IReadOnlyList<Rgb> colors) => Guard(() =>
    {
        var found = _devices.FirstOrDefault(entry => entry.Index == device)?.Modes
            .FirstOrDefault(entry => string.Equals(entry.Name, mode, StringComparison.OrdinalIgnoreCase));
        if (found == null) return;
        // Some devices count speed downward, with the smaller number the faster one; the two ends say which.
        uint? pace = found.SupportsSpeed
            ? (uint)Math.Round(found.SpeedMin + ((double)found.SpeedMax - found.SpeedMin) * Math.Clamp(speed, 0, 100) / 100)
            : null;
        Color[]? fill = null;
        if (found.ColorMode == ColorMode.ModeSpecific && colors.Count > 0)
        {
            var count = (int)Math.Clamp((uint)colors.Count, Math.Max(1, found.ColorMin), Math.Max(1, found.ColorMax));
            fill = Enumerable.Range(0, count).Select(i => colors[i % colors.Count]).Select(color => new Color(color.R, color.G, color.B)).ToArray();
        }
        _client.UpdateMode(device, found.Index, pace, null, fill);
    });

    public void ResizeZone(int device, int zone, int leds) => Guard(() => _client.ResizeZone(device, zone, leds));

    public void Dispose()
    {
        try { _client.Dispose(); }
        catch (Exception e) when (IsLinkFailure(e)) { /* already gone */ }
    }
}
