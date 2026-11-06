using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoreVision.Engine;
using Microsoft.Win32;
using LinkState = CoreVision.Engine.LinkState;

namespace CoreVision.App;

sealed class TrayContext : ApplicationContext
{
    const int PreviewIntervalMs = 66;
    const int MaxTrayText = 63;
    static readonly JsonSerializerOptions PageJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static readonly Lazy<string[]> InstalledFonts = new(() =>
    {
        using var fonts = new System.Drawing.Text.InstalledFontCollection();
        return fonts.Families.Select(family => family.Name).ToArray();
    });

    readonly FileLog _log;
    bool _crashedLastTime;
    readonly ScreenLink _link;
    readonly SensorMonitor _sensors;
    readonly MediaMonitor _media;
    readonly WeatherService _weather;
    readonly UpdateService _updates;
    readonly ThemePlayer _player;
    readonly ThemeStore _themes;
    readonly string? _ffmpeg;
    readonly PaletteStore _palettes = new(Path.Combine(Settings.DataFolder, "palettes.json"));
    readonly NotifyIcon _icon;
    readonly ToolStripMenuItem _statusItem = new("Starting") { Enabled = false };
    readonly SynchronizationContext _ui = new WindowsFormsSynchronizationContext();
    readonly Stopwatch _sincePreview = Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer _playlistTimer = new();
    readonly System.Windows.Forms.Timer _alertTimer = new() { Interval = 1000 };
    readonly AlertMonitor _alertMonitor = new();
    IReadOnlyList<Alert> _alerts = [];
    string _statusText = "Starting";
    bool _playlistPaused;
    MainWindow? _window;
    readonly Lazy<Task<IReadOnlyList<SpecRow>>> _specs;
    readonly bool _firstRun = !Settings.Exists;
    Settings _settings = Settings.Load();
    int _previewPending;

    public TrayContext(bool startHidden, FileLog log, bool crashedLastTime)
    {
        _log = log;
        _crashedLastTime = crashedLastTime;
        _specs = new(() => Task.Run(() => SystemSpecs.Read(Log)));
        Log($"Started as {Environment.UserName}, profile {Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}");
        _themes = new ThemeStore(Path.Combine(Settings.DataFolder, "themes"), Log);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open TR Studio", null, (_, _) => ShowWindow());
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        _icon = new NotifyIcon { Icon = AppIcon.Load(SystemInformation.SmallIconSize), Text = "TR Studio", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => ShowWindow();

        _media = new MediaMonitor(Log);
        _weather = new WeatherService(_settings.WeatherPlace, _settings.WeatherUnits, Log);
        _updates = new UpdateService(_settings.CheckForUpdates, Log);
        _updates.Changed += () => _ui.Post(_ => SendUpdate(), null);
        _updates.Installing += () => _ui.Post(_ => Quit(), null);
        _sensors = new SensorMonitor(() => new LibreSensorSource(() => _settings.Fans ?? new FanChoice()), log: Log);
        _sensors.Start();
        _link = new ScreenLink(() => ScreenFinder.Open(() => ScreenFinder.OpenFirst(ScreenDevice.All, model => WinUsbDevice.Open(model)),
            WinUsbDevice.PresentUsbPaths), log: Log);

        _ffmpeg = ThemeStore.FindFfmpeg();
        _player = new ThemePlayer(_themes.Load(_settings.ThemePath), () => _sensors.Current, _link.Submit,
            spec => Backgrounds.Create(spec, _ffmpeg, Log, _player!.Screen.Width, _player.Screen.Height), Log) { Media = () => _media.Current, Weather = () => _weather.Current, Loading = () => _sensors.IsLoading,
            ReportError = error => CrashReporter.ReportOnce(error, "frame") };
        _player.Rotation = _settings.Rotation;
        _player.Screen = Effective(_detectedScreen);
        _player.FrameRendered += OnFrameRendered;
        _player.Start();
        _media.Changed += () =>
        {
            _player.Invalidate();
            _ui.Post(_ => SendNowPlaying(), null);
        };
        _weather.Changed += () =>
        {
            _player.Invalidate();
            _ui.Post(_ => SendWeather(), null);
        };
        _playlistTimer.Tick += (_, _) => ShowNextInPlaylist();
        RestartPlaylist();
        _alertTimer.Tick += (_, _) => CheckAlerts();
        _alertTimer.Start();

        _link.StatusChanged += OnLinkStatus;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _link.Start();

        // A new install starts with Windows; after that, the switch in Settings decides.
        if (_firstRun) SetStartup(true);
        else Task.Run(() =>
        {
            try { WindowsStartup.PointAtThisCopy(); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                Log($"Could not update the Windows startup task: {e.Message}");
            }
        });

        if (!startHidden) ShowWindow();
    }

    public void Log(string line) => _log.Write(line);

    /// <summary>The screen as reported, before the user's Round Screen choice is applied.</summary>
    ScreenInfo _detectedScreen = ScreenInfo.Default;

    ScreenInfo Effective(ScreenInfo screen) => screen with { Round = _settings.RoundScreen ?? screen.Round };

    /// <summary>A newly detected screen resizes the frames first, so its first image already fits.</summary>
    void OnLinkStatus(LinkStatus status)
    {
        if (status.Screen is { } detected)
        {
            _detectedScreen = detected;
            CrashReporter.ScreenModel = detected.Model;
        }
        var resized = status.Screen is { } screen && Effective(screen) != _player.Screen;
        if (resized) _player.Screen = Effective(status.Screen!);
        _ui.Post(_ =>
        {
            ShowStatus(status);
            if (resized) SendState();
        }, null);
    }

    public void SetStartup(bool enabled)
    {
        Task.Run(() =>
        {
            try
            {
                if (enabled)
                {
                    var paused = WindowsStartup.Enable();
                    _settings = _settings with { PausedTrccStartup = paused || _settings.PausedTrccStartup };
                }
                else
                {
                    WindowsStartup.Disable(_settings.PausedTrccStartup);
                    _settings = _settings with { PausedTrccStartup = false };
                }
                SaveSettings();
                Log($"Windows startup {(enabled ? "on" : "off")}");
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                Log($"Windows startup change failed: {e.Message}");
                _ui.Post(_ => _window?.Post(new JsonObject { ["type"] = "error", ["message"] = "Couldn’t change Windows startup. Run the app as administrator." }), null);
            }
            SendState();
        });
    }

    /// <summary>The first read can take a few seconds, so the page gets it whenever it's done.</summary>
    public void SendSystemSpecs()
    {
        _specs.Value.ContinueWith(task =>
        {
            var rows = new JsonArray(task.Result.Select(row => (JsonNode?)new JsonObject
            {
                ["label"] = row.Label,
                ["values"] = new JsonArray(row.Values.Select(value => (JsonNode?)value).ToArray()),
            }).ToArray());
            _ui.Post(_ => _window?.Post(new JsonObject { ["type"] = "system", ["rows"] = rows }), null);
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    public void ShowWindow()
    {
        _window ??= new MainWindow(this);
        _window.Show();
        if (_window.WindowState == FormWindowState.Minimized) _window.WindowState = FormWindowState.Normal;
        _window.Activate();
    }

    void Quit()
    {
        Log("Quit from the tray");
        _window?.CloseForExit();
        ExitThread();
    }

    public void SelectTheme(string? path)
    {
        _player.Play(_themes.Load(path));
        _settings = _settings with { ThemePath = path };
        SaveSettings();
        RestartPlaylist();
        SendState();
    }

    ThemePlaylist Playlist => (_settings.Playlist ?? new ThemePlaylist()).Usable(_themes.List().Select(entry => entry.Path));

    /// <summary>Each theme gets its full time on screen, whatever just happened.</summary>
    void RestartPlaylist()
    {
        _playlistTimer.Stop();
        var playlist = Playlist;
        if (!playlist.Enabled || _playlistPaused) return;
        _playlistTimer.Interval = playlist.Seconds * 1000;
        _playlistTimer.Start();
    }

    public void SetPlaylist(ThemePlaylist playlist)
    {
        _settings = _settings with { Playlist = playlist };
        SaveSettings();
        RestartPlaylist();
    }

    /// <summary>The Editor shows the theme being edited, so nothing may replace it there.</summary>
    public void PausePlaylist(bool paused)
    {
        _playlistPaused = paused;
        RestartPlaylist();
    }

    /// <summary>Doesn't touch the settings file: this can run every few seconds, all day.</summary>
    void ShowNextInPlaylist()
    {
        var playlist = Playlist;
        if (!playlist.Enabled)
        {
            RestartPlaylist();
            SendState();
            return;
        }
        var path = playlist.Next(_settings.ThemePath);
        var theme = _themes.Load(path);
        _player.FadeTo(theme);
        _settings = _settings with { ThemePath = path };
        _window?.Post(new JsonObject { ["type"] = "showing", ["path"] = path, ["current"] = JsonNode.Parse(theme.ToJson()) });
    }

    public void PreviewTheme(Theme theme) => _player.Play(theme);

    AlertSettings Alerts => (_settings.Alerts ?? new AlertSettings()).Clamped();

    public void SetAlerts(AlertSettings alerts)
    {
        _settings = _settings with { Alerts = alerts.Clamped() };
        SaveSettings();
        CheckAlerts();
    }

    /// <summary>Runs every second. Windows gets one notification when a warning starts, not one a second.</summary>
    void CheckAlerts()
    {
        var alerts = _alertMonitor.Check(_sensors.Current, Alerts, DateTime.UtcNow);
        if (alerts.SequenceEqual(_alerts)) return;
        foreach (var alert in alerts.Where(alert => _alerts.All(old => old.Kind != alert.Kind)))
        {
            Log($"Warning: {alert.Title} at {alert.Detail}");
            _icon.ShowBalloonTip(10_000, NotificationTitle(alert), NotificationText(alert), ToolTipIcon.Warning);
        }
        foreach (var ended in _alerts.Where(old => alerts.All(alert => alert.Kind != old.Kind))) Log($"Warning over: {ended.Title}");
        _alerts = alerts;
        _player.Alerts = alerts;
        ShowTrayText();
        _window?.Post(new JsonObject { ["type"] = "alerts", ["active"] = ActiveAlertsJson() });
    }

    static string NotificationTitle(Alert alert) => alert.Kind switch
    {
        AlertKind.Pump => "Your AIO pump has slowed or stopped",
        AlertKind.CpuTemp => "Your CPU is running hot",
        _ => "Your GPU is running hot",
    };

    static string NotificationText(Alert alert) => alert.Kind switch
    {
        AlertKind.Pump => $"The pump reads {alert.Detail}. Save your work and check the cooler before the CPU overheats.",
        _ => $"It's at {alert.Detail}, above the limit you set in TR Studio.",
    };

    JsonArray ActiveAlertsJson() => new(_alerts.Select(alert => (JsonNode?)new JsonObject
    {
        ["kind"] = alert.Kind.ToString(),
        ["title"] = NotificationTitle(alert),
        ["detail"] = alert.Detail,
    }).ToArray());

    /// <summary>The page asks again every couple of seconds while Settings is open, so the speeds stay live.</summary>
    public void SendFans()
    {
        var stats = _sensors.Current;
        var choice = _settings.Fans ?? new FanChoice();
        JsonObject Role(FanRole role, string? picked) => new()
        {
            ["picked"] = picked,
            ["guess"] = FanMatch.Guess(stats.Fans, role)?.Id,
        };
        _window?.Post(new JsonObject
        {
            ["type"] = "fans",
            ["headers"] = new JsonArray(stats.Fans.Select(fan => (JsonNode?)new JsonObject
            {
                ["id"] = fan.Id, ["name"] = fan.Name, ["rpm"] = fan.Rpm is { } rpm && double.IsFinite(rpm) ? Math.Round(rpm) : null,
            }).ToArray()),
            ["pump"] = Role(FanRole.Pump, choice.Pump),
            ["cpuFan"] = Role(FanRole.CpuFan, choice.CpuFan),
            ["caseFan"] = Role(FanRole.CaseFan, choice.CaseFan),
        });
    }

    public void SetFans(FanChoice choice)
    {
        _settings = _settings with { Fans = choice };
        SaveSettings();
        SendFans();
    }

    /// <summary>Some screens report the wrong shape. Null goes back to what the screen reports.</summary>
    public void SetRoundScreen(bool? round)
    {
        _settings = _settings with { RoundScreen = round };
        SaveSettings();
        _player.Screen = Effective(_detectedScreen);
        SendState();
    }

    public void SetRotation(int degrees)
    {
        _player.Rotation = degrees;
        _settings = _settings with { Rotation = _player.Rotation };
        SaveSettings();
    }

    public void SaveTheme(Theme theme, string? oldPath)
    {
        var path = _themes.Save(theme);
        if (oldPath != null && !string.Equals(oldPath, path, StringComparison.OrdinalIgnoreCase))
        {
            _themes.Delete(oldPath);
            _settings = _settings with { Playlist = _settings.Playlist?.Renamed(oldPath, path) };
        }
        SelectTheme(path);
    }

    /// <summary>Removing the theme on screen moves to the first one left. With none left, the screen keeps the built-in fallback.</summary>
    public void DeleteTheme(string? path)
    {
        _themes.Delete(path);
        if (string.Equals(path, _settings.ThemePath, StringComparison.OrdinalIgnoreCase)) SelectTheme(_themes.List().FirstOrDefault()?.Path);
        else SendState();
    }

    public void RestoreDefaultThemes()
    {
        _themes.RestoreDefaults();
        SendState();
    }

    /// <summary>A package can hold a large video, so this stays off the UI thread.</summary>
    public void ExportTheme(string? path, string file)
    {
        Task.Run(() =>
        {
            try
            {
                _themes.Export(path, file);
                _ui.Post(_ => _window?.Post(new JsonObject { ["type"] = "notice", ["message"] = "Theme exported" }), null);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Log($"Theme export failed: {e.Message}");
                _ui.Post(_ => _window?.Post(new JsonObject { ["type"] = "error", ["message"] = "Couldn’t export that theme." }), null);
            }
        });
    }

    public void ImportTheme(string file)
    {
        Task.Run(() =>
        {
            try
            {
                var path = _themes.Import(file);
                _ui.Post(_ =>
                {
                    SelectTheme(path);
                    _window?.Post(new JsonObject { ["type"] = "notice", ["message"] = "Theme imported" });
                }, null);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Log($"Theme import failed: {e.Message}");
                _ui.Post(_ => _window?.Post(new JsonObject { ["type"] = "error", ["message"] = "That file isn’t a theme this app can open." }), null);
            }
        });
    }

    public void SendPalettes() =>
        _window?.Post(new JsonObject { ["type"] = "palettes", ["palettes"] = JsonSerializer.SerializeToNode(_palettes.Load(), PageJson) });

    public void SavePalettes(string json) => _palettes.Save(PaletteStore.Parse(json));

    public void ExportPalette(Palette palette, string file)
    {
        try
        {
            PaletteStore.Export(palette, file);
            _window?.Post(new JsonObject { ["type"] = "notice", ["message"] = "Palette exported" });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log($"Palette export failed: {e.Message}");
            _window?.Post(new JsonObject { ["type"] = "error", ["message"] = "Couldn’t export that palette." });
        }
    }

    /// <summary>The page names it and stores it, because only the page knows the built-in palettes' names.</summary>
    public void ImportPalette(string file)
    {
        try
        {
            var palette = PaletteStore.Import(file);
            _window?.Post(new JsonObject { ["type"] = "palette", ["palette"] = JsonSerializer.SerializeToNode(palette, PageJson) });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log($"Palette import failed: {e.Message}");
            _window?.Post(new JsonObject { ["type"] = "error", ["message"] = "That file isn’t a palette this app can open." });
        }
    }

    /// <summary>The sidebar player works out the position itself from this, so it needs no message every second.</summary>
    public void SendNowPlaying()
    {
        var media = _media.Current;
        JsonNode? song = media == null ? null : new JsonObject
        {
            ["title"] = media.Title,
            ["artist"] = media.Artist,
            ["playing"] = media.IsPlaying,
            ["position"] = media.PositionAt(DateTime.UtcNow)?.TotalSeconds,
            ["duration"] = media.Duration?.TotalSeconds,
            ["art"] = media.Art is { } art ? $"data:{ImageType(art)};base64,{Convert.ToBase64String(art)}" : null,
            ["canPlayPause"] = media.CanPlayPause,
            ["canNext"] = media.CanNext,
            ["canPrevious"] = media.CanPrevious,
        };
        _window?.Post(new JsonObject { ["type"] = "nowPlaying", ["media"] = song });
    }

    static string ImageType(byte[] bytes) => bytes is [0xFF, 0xD8, ..] ? "image/jpeg" : "image/png";

    public void SendWeather()
    {
        var report = _weather.Current;
        _window?.Post(new JsonObject
        {
            ["type"] = "weather",
            ["place"] = _settings.WeatherPlace?.Label,
            ["units"] = _settings.WeatherUnits.ToString(),
            ["error"] = _weather.Error,
            ["report"] = report == null ? null : new JsonObject
            {
                ["temperature"] = Math.Round(report.Temperature),
                ["description"] = WeatherReport.Describe(report.Code),
                ["high"] = Math.Round(report.High),
                ["low"] = Math.Round(report.Low),
                ["fetched"] = report.FetchedUtc.ToString("O"),
                ["stale"] = report.IsStale(DateTime.UtcNow),
            },
        });
    }

    public async Task SearchWeatherAsync(string query)
    {
        var message = new JsonObject { ["type"] = "weatherResults", ["query"] = query };
        try
        {
            var places = await _weather.SearchAsync(query);
            message["places"] = JsonSerializer.SerializeToNode(places.Select(place => new
            {
                place.Label, place.Name, place.Region, place.Country, place.Latitude, place.Longitude,
            }), PageJson);
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            Log($"Place search failed: {e.Message}");
            message["error"] = "Couldn’t reach the weather service. Check your internet connection.";
        }
        _window?.Post(message);
    }

    public void SetWeather(WeatherPlace? place, WeatherUnits units)
    {
        _settings = _settings with { WeatherPlace = place, WeatherUnits = units };
        SaveSettings();
        _weather.Configure(place, units);
    }

    public WeatherPlace? WeatherPlace => _settings.WeatherPlace;

    public void SendUpdate() => _window?.Post(_updates.ToJson());

    public void CheckForUpdate() => _ = _updates.CheckAsync();

    /// <summary>The installer opens TR Studio again when it is done, so this one quits once it starts.</summary>
    public void InstallUpdate()
    {
        Log($"Update requested from {UpdateService.Current}");
        _ = _updates.InstallAsync();
    }

    public void SetAutomaticUpdates(bool enabled)
    {
        _settings = _settings with { CheckForUpdates = enabled };
        SaveSettings();
        _updates.Automatic = enabled;
    }

    public void SetCrashReports(bool enabled)
    {
        _settings = _settings with { CrashReports = enabled };
        SaveSettings();
        CrashReporter.Apply(enabled, _log);
        SendState();
    }

    const string Repository = "https://github.com/iamfercatapang/tr-studio";

    /// <summary>Opens a bug report form with the versions filled in. Nothing personal goes in the link.</summary>
    public void ReportProblem()
    {
        var model = _detectedScreen == ScreenInfo.Default ? "" : _detectedScreen.Model;
        OpenInBrowser($"{Repository}/issues/new?template=bug_report.yml" +
            $"&version={Uri.EscapeDataString(UpdateService.Current.ToString())}" +
            $"&model={Uri.EscapeDataString(model)}" +
            $"&windows={Uri.EscapeDataString(System.Runtime.InteropServices.RuntimeInformation.OSDescription)}");
    }

    /// <summary>The release's page on GitHub. Only a real version number goes into the link.</summary>
    public void ShowReleaseNotes(string? version)
    {
        if (Version.TryParse(version, out var parsed)) OpenInBrowser($"{Repository}/releases/tag/v{parsed}");
    }

    public void AskQuestion() => OpenInBrowser($"{Repository}/discussions");

    public void OpenLogFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_log.Folder}\"") { UseShellExecute = true })?.Dispose(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Log($"Could not open the log folder: {e.Message}"); }
    }

    /// <summary>Only TR Studio's own pages are opened, never an address the page made up.</summary>
    void OpenInBrowser(string url)
    {
        if (!url.StartsWith(Repository + "/", StringComparison.Ordinal)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Log($"Could not open the browser: {e.Message}"); }
    }

    public void ControlMedia(string? action)
    {
        if (action != null) _media.Send(action);
    }

    /// <summary>Thumbnails are rendered off the UI thread. GIF and video cards show a still frame.</summary>
    public void SendState()
    {
        var entries = _themes.List();
        var current = _player.Theme;
        var status = _link.Status;
        var path = _settings.ThemePath;
        var playlist = Playlist;
        var alertSettings = Alerts;
        var activeAlerts = ActiveAlertsJson();
        var crashedLastTime = _crashedLastTime;
        if (_window != null) _crashedLastTime = false; // the page offers a report once
        Task.Run(() =>
        {
            var themes = new JsonArray();
            var screen = _player.Screen;
            using var renderer = new ThemeRenderer(screen.Width, screen.Height, screen.JpegQuality) { RoundScreen = screen.Round };
            foreach (var entry in entries)
            {
                var theme = _themes.Load(entry.Path);
                themes.Add(new JsonObject
                {
                    ["name"] = theme.Name,
                    ["path"] = entry.Path,
                    ["kind"] = theme.Background.Kind.ToString(),
                    ["thumb"] = Thumbnail(renderer, theme),
                });
            }
            var state = new JsonObject
            {
                ["type"] = "state",
                ["themes"] = themes,
                ["current"] = JsonNode.Parse(current.ToJson()),
                ["currentPath"] = path,
                ["rotation"] = _player.Rotation,
                ["removedDefaults"] = _themes.RemovedDefaultCount,
                ["playlist"] = JsonSerializer.SerializeToNode(playlist, PageJson),
                ["alertSettings"] = JsonSerializer.SerializeToNode(alertSettings, PageJson),
                ["alerts"] = activeAlerts,
                ["screen"] = new JsonObject { ["width"] = screen.Width, ["height"] = screen.Height, ["round"] = screen.Round, ["model"] = screen.Model },
                ["roundOverride"] = _settings.RoundScreen,
                ["crashReports"] = _settings.CrashReports,
                ["crashReportsAvailable"] = CrashReporter.IsAvailable,
                ["crashedLastTime"] = crashedLastTime,
                ["startup"] = WindowsStartup.IsEnabled(),
                ["fonts"] = new JsonArray(InstalledFonts.Value.Select(name => (JsonNode?)name).ToArray()),
                ["status"] = StatusJson(status),
            };
            _ui.Post(_ => _window?.Post(state), null);
        });
    }

    string? Thumbnail(ThemeRenderer renderer, Theme theme)
    {
        using var background = Backgrounds.Still(theme.Background, _ffmpeg, Log, _player.Screen.Width, _player.Screen.Height);
        return Convert.ToBase64String(renderer.Render(background, theme.Elements, _sensors.Current, DateTime.Now, theme.Dim));
    }

    void OnFrameRendered(byte[] jpeg)
    {
        if (_sincePreview.ElapsedMilliseconds < PreviewIntervalMs) return;
        if (Interlocked.Exchange(ref _previewPending, 1) == 1) return; // the UI has not taken the last one yet
        _sincePreview.Restart();
        var stats = _sensors.Current;
        _ui.Post(_ =>
        {
            Interlocked.Exchange(ref _previewPending, 0);
            if (_window is not { IsLive: true } window) return;
            window.Post(new JsonObject
            {
                ["type"] = "frame",
                ["jpeg"] = Convert.ToBase64String(jpeg),
                ["stats"] = new JsonObject(Enum.GetValues<Metric>().Select(metric =>
                    KeyValuePair.Create(metric.ToString(), (JsonNode?)JsonValue.Create(stats.Get(metric).Value)))),
            });
        }, null);
    }

    void SaveSettings()
    {
        try { _settings.Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log($"Could not save settings: {e.Message}"); }
    }

    void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        Log($"Power: {e.Mode}");
        if (e.Mode == PowerModes.Suspend) _link.Suspend();
        else if (e.Mode == PowerModes.Resume) _link.Resume();
    }

    static string StatusText(LinkStatus status) => status.State switch
    {
        LinkState.Connected => $"Screen connected, {status.Screen?.Width}×{status.Screen?.Height}",
        LinkState.Busy => "Close TRCC to use the screen",
        LinkState.Searching => "Looking for the screen",
        LinkState.Suspended => "Paused",
        LinkState.Unsupported => status.Detail,
        _ => "Stopped",
    };

    static JsonObject StatusJson(LinkStatus status) => new()
    {
        ["state"] = status.State.ToString(),
        ["text"] = StatusText(status),
    };

    void ShowStatus(LinkStatus status)
    {
        var text = StatusText(status);
        _statusItem.Text = text;
        _statusText = text;
        ShowTrayText();
        _window?.Post(new JsonObject { ["type"] = "status", ["status"] = StatusJson(status) });
    }

    void ShowTrayText()
    {
        var text = _alerts.Count > 0 ? $"Warning: {string.Join(", ", _alerts.Select(alert => $"{alert.Title} {alert.Detail}"))}" : _statusText;
        _icon.Text = text.Length > MaxTrayText ? text[..MaxTrayText] : text;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _alertTimer.Dispose();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _player.Dispose();
            _link.Dispose();
            _sensors.Dispose();
            _media.Dispose();
            _weather.Dispose();
            _updates.Dispose();
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
