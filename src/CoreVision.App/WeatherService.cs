using CoreVision.Engine;

namespace CoreVision.App;

/// <summary>
/// Fetches the forecast from Open-Meteo every 15 minutes for the chosen place. Besides the update
/// check, this is TR Studio's only use of the internet, and all it sends is the place's coordinates. A failed fetch keeps the
/// last forecast, which turns stale after two hours, and tries again sooner.
/// </summary>
sealed class WeatherService : IDisposable
{
    static readonly TimeSpan Refresh = TimeSpan.FromMinutes(15);
    static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(2);

    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly Action<string> _log;
    readonly CancellationTokenSource _stop = new();
    readonly SemaphoreSlim _wake = new(0);
    volatile WeatherReport? _current;
    volatile string? _error;
    (WeatherPlace? Place, WeatherUnits Units) _settings;

    public WeatherService(WeatherPlace? place, WeatherUnits units, Action<string> log)
    {
        _log = log;
        _settings = (place, units);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TRStudio/1.2");
        _ = RunAsync();
    }

    /// <summary>Raised on a background thread after each fetch, or when the place changes.</summary>
    public event Action? Changed;

    public WeatherReport? Current => _current;

    /// <summary>Why the last fetch failed, or null after a success.</summary>
    public string? Error => _error;

    /// <summary>Fetches at once for the new place or units.</summary>
    public void Configure(WeatherPlace? place, WeatherUnits units)
    {
        lock (_http) _settings = (place, units);
        _current = null;
        _error = null;
        Changed?.Invoke();
        _wake.Release();
    }

    public async Task<IReadOnlyList<WeatherPlace>> SearchAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return [];
        var json = await _http.GetStringAsync(WeatherReport.SearchUrl(name), _stop.Token);
        return WeatherReport.ParsePlaces(json);
    }

    async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            (WeatherPlace? Place, WeatherUnits Units) settings;
            lock (_http) settings = _settings;
            var wait = Refresh;
            if (settings.Place is { } place)
            {
                try
                {
                    var json = await _http.GetStringAsync(WeatherReport.ForecastUrl(place, settings.Units), _stop.Token);
                    var report = WeatherReport.Parse(json, place.Name, settings.Units, DateTime.UtcNow);
                    lock (_http)
                    {
                        if (_settings != settings) continue; // the place changed while this was loading
                        _current = report;
                    }
                    if (_error != null) _log("Weather is updating again");
                    _error = null;
                }
                catch (Exception e) when (e is HttpRequestException or InvalidDataException or TaskCanceledException && !_stop.IsCancellationRequested)
                {
                    // Logged once per kind of failure, so an offline PC doesn't fill the log.
                    var message = e is TaskCanceledException ? "The weather service didn't answer in time." : e.Message;
                    if (message != _error) _log($"Weather failed: {message}");
                    _error = message;
                    wait = RetryAfterFailure;
                }
                Changed?.Invoke();
            }
            try { await _wake.WaitAsync(wait, _stop.Token); }
            catch (OperationCanceledException) { return; }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _http.Dispose();
    }
}
