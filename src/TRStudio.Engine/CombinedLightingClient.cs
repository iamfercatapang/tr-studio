using System.Diagnostics;

namespace TRStudio.Engine;

/// <summary>
/// The devices TR Studio drives by itself, plus whatever OpenRGB adds when it is running. The built-in
/// ones never wait on OpenRGB: it is tried in the background every few seconds, joins the list when it
/// answers, and drops out quietly when it closes. A device both can drive stays with the built-in driver.
/// </summary>
public sealed class CombinedLightingClient : ILightingClient
{
    static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    readonly ILightingClient _own;
    readonly Func<ILightingClient> _connectExtra;
    readonly Stopwatch _sinceTry = new();
    readonly object _gate = new();
    List<(ILightingClient Client, int Index)> _routes = [];
    ILightingClient? _extra;
    Task<ILightingClient?>? _connecting;
    bool _changed;
    volatile bool _retryNow = true;
    volatile bool _extraConnected;
    bool _disposed;

    public CombinedLightingClient(ILightingClient own, Func<ILightingClient> connectExtra)
    {
        _own = own;
        _connectExtra = connectExtra;
    }

    /// <summary>Whether OpenRGB is answering right now.</summary>
    public bool ExtraConnected => _extraConnected;

    /// <summary>Tries OpenRGB on the next pass instead of waiting out the pause.</summary>
    public void RetryExtraNow() => _retryNow = true;

    public bool TakeDevicesChanged()
    {
        var changed = _own.TakeDevicesChanged() | _changed;
        _changed = false;
        if (_extra != null)
        {
            try { changed |= _extra.TakeDevicesChanged(); }
            catch (LightingUnavailableException) { DropExtra(); changed = true; }
        }
        else if (_connecting is { IsCompleted: true } finished)
        {
            _connecting = null;
            _extra = finished.Result;
            _extraConnected = _extra != null;
            changed |= _extra != null;
        }
        else if (_connecting == null && (_retryNow || _sinceTry.Elapsed >= Retry))
        {
            // Connecting can take a couple of seconds when nothing answers, so it never runs on the thread that draws the lights.
            _retryNow = false;
            _sinceTry.Restart();
            _connecting = Task.Run(() =>
            {
                try
                {
                    var client = _connectExtra();
                    lock (_gate)
                    {
                        if (!_disposed) return client;
                    }
                    client.Dispose();
                    return null;
                }
                catch (LightingUnavailableException) { return (ILightingClient?)null; }
            });
        }
        return changed;
    }

    public IReadOnlyList<LightDevice> Devices()
    {
        var routes = new List<(ILightingClient, int)>();
        var devices = new List<LightDevice>();
        foreach (var device in _own.Devices())
        {
            routes.Add((_own, device.Index));
            devices.Add(device with { Index = devices.Count });
        }
        if (_extra != null)
        {
            try
            {
                // The same hardware under two drivers would get two sets of colors: the board is matched by its name,
                // and memory and graphics cards as a whole, since OpenRGB names those differently.
                var own = devices.Select(device => device.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var ownMemory = devices.Any(device => device.Kind == "Dram");
                var ownGpu = devices.Any(device => device.Kind == "Gpu");
                foreach (var device in _extra.Devices().Where(device => !own.Contains(device.Name) && !(ownMemory && device.Kind == "Dram") && !(ownGpu && device.Kind == "Gpu")))
                {
                    routes.Add((_extra, device.Index));
                    devices.Add(device with { Index = devices.Count });
                }
            }
            catch (LightingUnavailableException) { DropExtra(); }
        }
        _routes = routes;
        return devices;
    }

    public void SetDirect(int device) => Route(device, (client, index) => client.SetDirect(index));

    public void SetLeds(int device, ReadOnlySpan<Rgb> colors)
    {
        var leds = colors.ToArray();
        Route(device, (client, index) => client.SetLeds(index, leds));
    }

    public void SetMode(int device, string mode, int speed, IReadOnlyList<Rgb> colors) =>
        Route(device, (client, index) => client.SetMode(index, mode, speed, colors));

    public void ResizeZone(int device, int zone, int leds) => Route(device, (client, index) => client.ResizeZone(index, zone, leds));

    /// <summary>OpenRGB going away is not a failure of the whole: its devices leave the list on the next pass.</summary>
    void Route(int device, Action<ILightingClient, int> call)
    {
        if (device < 0 || device >= _routes.Count) return;
        var (client, index) = _routes[device];
        if (client == _own) { call(client, index); return; }
        if (client != _extra) return; // it was dropped earlier in this pass
        try { call(client, index); }
        catch (LightingUnavailableException) { DropExtra(); }
    }

    void DropExtra()
    {
        _extra?.Dispose();
        _extra = null;
        _extraConnected = false;
        _changed = true;
        _sinceTry.Restart();
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        _connecting?.ContinueWith(task => task.Result?.Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion);
        _extra?.Dispose();
        _extraConnected = false;
        _own.Dispose();
    }
}
