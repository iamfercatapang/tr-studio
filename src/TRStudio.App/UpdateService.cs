using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using TRStudio.Engine;

namespace TRStudio.App;

/// <summary>
/// Looks for a newer release on GitHub soon after startup and every 12 hours, if allowed, and installs it
/// when asked: the installer is downloaded, checked against GitHub's SHA-256, run silently, and it opens
/// TR Studio again when it's done. The check sends nothing about the PC.
/// </summary>
sealed class UpdateService : IDisposable
{
    static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(30);
    static readonly TimeSpan Recheck = TimeSpan.FromHours(12);
    const string InstallerArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1";

    readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    readonly Action<string> _log;
    readonly CancellationTokenSource _stop = new();
    readonly System.Threading.Timer _timer;
    readonly string _folder = Path.Combine(Settings.DataFolder, "updates");
    readonly object _gate = new();
    bool _automatic;
    string _state = "idle";
    string? _error;
    int _progress;
    AppRelease? _release;
    DateTime? _checkedAt;

    public UpdateService(bool automatic, Action<string> log)
    {
        _log = log;
        _automatic = automatic;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"TRStudio/{Current}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _timer = new System.Threading.Timer(_ => { if (_automatic) _ = CheckAsync(quiet: true); }, null, FirstCheck, Recheck);
        RemoveOldDownloads();
    }

    public static Version Current { get; } = AppUpdate.ParseVersion(Assembly.GetExecutingAssembly().GetName().Version?.ToString()) ?? new Version(0, 0, 0);

    /// <summary>Raised on a background thread whenever the state changes.</summary>
    public event Action? Changed;

    /// <summary>Raised once the installer is running, so the app gets out of its way.</summary>
    public event Action? Installing;

    public bool Automatic
    {
        get => _automatic;
        set
        {
            _automatic = value;
            Changed?.Invoke();
        }
    }

    public JsonObject ToJson()
    {
        lock (_gate)
        {
            return new JsonObject
            {
                ["type"] = "update",
                ["current"] = Current.ToString(),
                ["automatic"] = _automatic,
                ["state"] = _state,
                ["version"] = _release?.Version.ToString(),
                ["progress"] = _progress,
                ["error"] = _error,
                ["checkedAt"] = _checkedAt?.ToString("o"),
            };
        }
    }

    /// <summary>A quiet check, the automatic kind, keeps its failures in the log rather than the page.</summary>
    public async Task CheckAsync(bool quiet = false)
    {
        lock (_gate)
        {
            if (_state is "checking" or "downloading" or "installing") return;
            _state = "checking";
            _error = null;
        }
        Changed?.Invoke();
        try
        {
            using var response = await _http.GetAsync(AppUpdate.LatestReleaseUrl, _stop.Token);
            var release = response.IsSuccessStatusCode ? AppUpdate.Parse(await response.Content.ReadAsStringAsync(_stop.Token)) : null;
            if (!response.IsSuccessStatusCode) _log($"Update check: GitHub answered {(int)response.StatusCode}");
            lock (_gate)
            {
                _checkedAt = DateTime.Now;
                _release = release != null && AppUpdate.IsNewer(release, Current) ? release : null;
                _state = _release != null ? "available" : response.IsSuccessStatusCode || quiet ? "upToDate" : "failed";
                if (_state == "failed") _error = "Couldn’t reach GitHub to check for updates.";
            }
            if (_release != null) _log($"Update available: {_release.Version}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            _log($"Update check failed: {e.Message}");
            lock (_gate)
            {
                _state = quiet ? "idle" : "failed";
                _error = quiet ? null : "Couldn’t reach GitHub to check for updates. Check your internet connection.";
            }
        }
        Changed?.Invoke();
    }

    public async Task InstallAsync()
    {
        AppRelease release;
        lock (_gate)
        {
            if (_state != "available" && _state != "failed" || _release == null) return;
            release = _release;
            _state = "downloading";
            _progress = 0;
            _error = null;
        }
        Changed?.Invoke();
        try
        {
            var path = await DownloadAsync(release);
            Run(path, release);
            lock (_gate) _state = "installing";
            Changed?.Invoke();
            Installing?.Invoke();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException
            or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            _log($"Update to {release.Version} failed: {e.Message}");
            lock (_gate)
            {
                _state = "failed";
                _error = e is InvalidDataException ? e.Message : "The update couldn’t be downloaded. Try again in a moment.";
            }
            Changed?.Invoke();
        }
    }

    /// <summary>Hashes while it writes, and only a file whose hash matches gets its real name.</summary>
    async Task<string> DownloadAsync(AppRelease release)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, release.InstallerName);
        var partial = path + ".part";
        using var response = await _http.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, _stop.Token);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? release.Size;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var source = await response.Content.ReadAsStreamAsync(_stop.Token))
        await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, _stop.Token)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), _stop.Token);
                hash.AppendData(buffer, 0, read);
                written += read;
                var progress = total > 0 ? (int)(written * 100 / total) : 0;
                if (progress != _progress)
                {
                    _progress = progress;
                    Changed?.Invoke();
                }
            }
        }
        if (!AppUpdate.Matches(hash.GetHashAndReset(), release))
        {
            File.Delete(partial);
            throw new InvalidDataException("The download didn’t match the release, so it wasn’t installed. Try again.");
        }
        File.Move(partial, path, overwrite: true);
        return path;
    }

    /// <summary>
    /// The file stays open against writes while it's checked once more and started, so nothing can swap it
    /// in between. The app runs as administrator, so the installer does too, without another prompt.
    /// </summary>
    void Run(string path, AppRelease release)
    {
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!AppUpdate.Matches(SHA256.HashData(locked), release))
            throw new InvalidDataException("The download didn’t match the release, so it wasn’t installed. Try again.");
        _log($"Starting the {release.Version} installer");
        Process.Start(new ProcessStartInfo(path, InstallerArguments) { UseShellExecute = false })?.Dispose();
    }

    void RemoveOldDownloads()
    {
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log($"Could not remove old updates: {e.Message}"); }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _timer.Dispose();
        _http.Dispose();
    }
}
