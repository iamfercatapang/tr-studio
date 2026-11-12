using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using TRStudio.Engine;
using Microsoft.Win32;
using LinkState = TRStudio.Engine.LinkState;

namespace TRStudio.App;

sealed class TrayContext : ApplicationContext
{
    const int PreviewIntervalMs = 66;
    const int MaxTrayText = 63;
    const int LightingPreviewMs = 66;
    const int LightingSaveMs = 400;
    const int CanvasPreviewWidth = 320, CanvasPreviewHeight = 200, EffectTileWidth = 96, EffectTileHeight = 60;
    const double EffectTileSeconds = 1.3; // a moment that shows each effect well in its tile
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
    readonly LightingService _lighting;
    readonly System.Windows.Forms.Timer _lightingTimer = new() { Interval = LightingPreviewMs };
    bool _startedOpenRgb;
    readonly System.Windows.Forms.Timer _lightingSave = new() { Interval = LightingSaveMs };
    volatile CombinedLightingClient? _lights;
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
        Log(HasPawnIO() ? "PawnIO driver installed" : "PawnIO driver missing: CPU temperature, clock and power and motherboard fans can't be read");
        _sensors = new SensorMonitor(() => new LibreSensorSource(() => _settings.Fans ?? new FanChoice(), Log, () => _settings.Gpu), log: Log);
        _sensors.Start();
        _link = new ScreenLink(() => ScreenFinder.Open(() => ScreenFinder.OpenFirst(ScreenDevice.All, ScreenOpener.Open),
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
        _lighting = new LightingService(ConnectLights, Lighting, Log);
        _lighting.Changed += () => _ui.Post(_ => OnLightingChanged(), null);
        _lighting.Start();
        _lightingTimer.Tick += (_, _) => SendLightingFrame();
        _lightingSave.Tick += (_, _) =>
        {
            _lightingSave.Stop();
            SaveSettings();
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

    /// <summary>
    /// The sensor library reads CPU temperature, clock and power and the motherboard's fans through the PawnIO
    /// driver. The installer offers it; this is the key its setup registers, the same one the library checks.
    /// </summary>
    static bool HasPawnIO()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            return key != null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
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
            _ui.Post(_ => _window?.Post(new JsonObject { ["type"] = "system", ["rows"] = rows, ["sensorDriver"] = HasPawnIO() }), null);
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

    /// <summary>Settings shows the Graphics Card picker only when this lists more than one GPU.</summary>
    public void SendGpus()
    {
        _window?.Post(new JsonObject
        {
            ["type"] = "gpus",
            ["picked"] = _settings.Gpu,
            ["gpus"] = new JsonArray(_sensors.Current.Gpus.Select(gpu => (JsonNode?)new JsonObject
            {
                ["id"] = gpu.Id, ["name"] = gpu.Name, ["automatic"] = gpu.Automatic,
                ["memoryGb"] = gpu.MemoryTotal is { } mb && double.IsFinite(mb) && mb > 0 ? Math.Round(mb / 1024, 1) : null,
            }).ToArray()),
        });
    }

    /// <summary>Null goes back to picking the graphics card automatically.</summary>
    public void SetGpu(string? id)
    {
        _settings = _settings with { Gpu = string.IsNullOrEmpty(id) ? null : id };
        SaveSettings();
        SendGpus();
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

    LightingSettings Lighting => _settings.Lighting ?? new LightingSettings();

    /// <summary>
    /// The built-in drivers first, then OpenRGB for whatever they don't cover. The memory driver is used
    /// only when the user turned it on: with it off, TR Studio's lighting never touches the memory's bus.
    /// The ASRock driver is opt-in too, until someone with such a board has confirmed it.
    /// </summary>
    ILightingClient ConnectLights()
    {
        var memory = Lighting.Memory ? EneRamLightingClient.Connect(Log) : new EneRamLightingClient(null, []);
        var asrock = Lighting.Asrock ? AsrockUsbLightingClient.Connect(Log) : new AsrockUsbLightingClient([]);
        var gpu = Lighting.Gpu ? GpuLightingClient.Connect(Log) : new GpuLightingClient([]);
        return _lights = new CombinedLightingClient(new BuiltInLightingClient(AuraUsbLightingClient.Connect(Log), asrock, memory, gpu), OpenRgbLightingClient.Connect);
    }

    /// <summary>
    /// TR Studio drives ASUS Aura headers itself; other devices come through OpenRGB. With lighting on and
    /// OpenRGB not answering, it is started once if it's installed. After that the link keeps trying by
    /// itself, so closing OpenRGB by hand doesn't bring it back.
    /// </summary>
    void OnLightingChanged()
    {
        if (_lighting.State == LightingState.Connected && _lights is { ExtraConnected: false } && Lighting is { Enabled: true, StartOpenRgb: true } && !_startedOpenRgb)
        {
            _startedOpenRgb = true;
            Task.Run(() => OpenRgbApp.TryStart(Log));
        }
        SendLighting();
    }

    /// <summary>Looking for OpenRGB reads the registry and the process list, so it stays off the UI thread.</summary>
    public void SendLighting()
    {
        var settings = Lighting;
        var state = _lighting.State;
        var devices = _lighting.Devices;
        var openRgb = _lights?.ExtraConnected == true && state == LightingState.Connected;
        var parted = LightParts.Split(devices, settings.Parts);
        Task.Run(() =>
        {
            var message = new JsonObject
            {
                ["type"] = "lighting",
                ["enabled"] = settings.Enabled,
                ["startOpenRgb"] = settings.StartOpenRgb,
                ["memory"] = settings.Memory,
                ["asrock"] = settings.Asrock,
                ["gpu"] = settings.Gpu,
                ["state"] = state.ToString(),
                ["openRgbConnected"] = openRgb,
                ["openRgb"] = (openRgb ? OpenRgbPresence.Running : OpenRgbApp.Presence()).ToString(),
                ["conflicts"] = new JsonArray(RunningLightingApps().Select(app => (JsonNode?)app).ToArray()),
                ["effects"] = new JsonArray(CanvasEffects.All.Select(effect => (JsonNode?)new JsonObject
                {
                    ["id"] = effect.Id, ["name"] = effect.Name, ["description"] = effect.Description,
                    ["params"] = new JsonArray(effect.Params.Select(parameter => (JsonNode?)new JsonObject
                    {
                        ["key"] = parameter.Key, ["label"] = parameter.Label, ["kind"] = parameter.Kind.ToString(),
                        ["default"] = parameter.Default, ["min"] = parameter.Min, ["max"] = parameter.Max,
                    }).ToArray()),
                }).ToArray()),
                ["canvas"] = CanvasJson(settings.ActiveCanvas, parted),
                ["devices"] = new JsonArray(devices.Select((device, at) => (JsonNode?)new JsonObject
                {
                    ["key"] = device.Key,
                    ["name"] = device.Name,
                    ["kind"] = device.Kind,
                    ["ledCount"] = device.LedCount,
                    ["override"] = settings.Override(device.Key) is { } own ? OpenRgbApp.ToJson(own) : null,
                    ["zones"] = new JsonArray(device.Zones.Select(zone => (JsonNode?)new JsonObject
                    {
                        ["key"] = LightingSettings.HeaderKey(device.Key, zone.Name),
                        ["name"] = zone.Name, ["ledCount"] = zone.LedCount, ["resizable"] = zone.Resizable, ["maxLeds"] = zone.MaxLeds,
                        ["parts"] = new JsonArray((settings.Parts?.GetValueOrDefault(LightingSettings.HeaderKey(device.Key, zone.Name)) ?? []).Select(part => (JsonNode?)new JsonObject
                        {
                            ["name"] = part.Name, ["leds"] = part.Leds, ["shape"] = part.Shape.ToString(),
                        }).ToArray()),
                    }).ToArray()),
                    // What the canvas shows: a box for each strip, or for each part of a strip the user divided.
                    ["boxes"] = new JsonArray(parted[at].Zones.Select(box => (JsonNode?)new JsonObject
                    {
                        ["key"] = LightingSettings.HeaderKey(device.Key, box.Name), ["zone"] = box.Label == null ? box.Name : box.Name[..box.Name.LastIndexOf('#')],
                        ["name"] = box.Label ?? box.Name, ["ledCount"] = box.LedCount, ["shape"] = box.Shape.ToString(),
                    }).ToArray()),
                    ["modes"] = new JsonArray(device.Modes.Where(mode => OpenRgbLightingClient.IsEffect(mode.Name)).Select(mode => (JsonNode?)new JsonObject
                    {
                        ["name"] = mode.Name, ["hasSpeed"] = mode.HasSpeed, ["colors"] = mode.Colors,
                    }).ToArray()),
                }).ToArray()),
            };
            _ui.Post(_ => _window?.Post(message), null);
        });
    }

    /// <summary>Other lighting apps running now. Reads the process list, so call it off the UI thread.</summary>
    static IReadOnlyList<string> RunningLightingApps()
    {
        try
        {
            var processes = Process.GetProcesses();
            var names = processes.Select(process => process.ProcessName).ToList();
            foreach (var process in processes) process.Dispose();
            return LightingConflicts.Among(names);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { return []; }
    }

    static JsonObject PlacementJson(LightPlacement placement) => new()
    {
        ["x"] = Math.Round(placement.X, 1), ["y"] = Math.Round(placement.Y, 1), ["width"] = Math.Round(placement.Width, 1),
        ["height"] = Math.Round(placement.Height, 1), ["rotation"] = Math.Round(placement.Rotation, 1), ["brightness"] = placement.Brightness,
    };

    /// <summary>Every effect's settings are sent with defaults filled in, and every strip with a place, so the page needs no rules of its own.</summary>
    static JsonObject CanvasJson(CanvasSettings canvas, IReadOnlyList<LightDevice> devices) => new()
    {
        ["effect"] = CanvasEffects.Find(canvas.Effect).Id,
        ["brightness"] = Math.Clamp(canvas.Brightness, 0, 100),
        ["width"] = LightPlacement.CanvasWidth,
        ["height"] = LightPlacement.CanvasHeight,
        ["values"] = new JsonObject(CanvasEffects.All.Select(effect => KeyValuePair.Create(effect.Id,
            (JsonNode?)new JsonObject(canvas.ValuesFor(effect).ToText().Select(value => KeyValuePair.Create(value.Key, (JsonNode?)value.Value)))))),
        ["placements"] = new JsonObject(LightingService.Layout(canvas, devices).Select(place => KeyValuePair.Create(place.Key, (JsonNode?)PlacementJson(place.Value)))),
        ["effectPresets"] = new JsonArray((canvas.EffectPresets ?? []).Select(preset => (JsonNode?)new JsonObject
        {
            ["name"] = preset.Name, ["effect"] = CanvasEffects.Find(preset.Effect).Name,
        }).ToArray()),
        ["layoutPresets"] = new JsonArray((canvas.LayoutPresets ?? []).Select(preset => (JsonNode?)preset.Name).ToArray()),
    };

    /// <summary>
    /// Lighting changes reach the lights at once but the settings file a moment later, because dragging a
    /// slider or a strip sends many changes a second.
    /// </summary>
    void SetLighting(LightingSettings settings)
    {
        _settings = _settings with { Lighting = settings };
        _lighting.Configure(settings);
        _lightingSave.Stop();
        _lightingSave.Start();
    }

    void SetCanvas(Func<CanvasSettings, CanvasSettings> change, bool tellPage = false)
    {
        SetLighting(Lighting with { Canvas = change(Lighting.ActiveCanvas) });
        if (tellPage) SendLighting();
    }

    public void SetCanvasEffect(string? effect) => SetCanvas(canvas => canvas.WithEffect(effect ?? ""), tellPage: true);

    public void SetEffectValue(string? effect, string? key, string? value) => SetCanvas(canvas => canvas.WithValue(effect ?? "", key ?? "", value ?? ""));

    public void SetPlacement(string? zone, LightPlacement placement)
    {
        if (!string.IsNullOrEmpty(zone)) SetCanvas(canvas => canvas.WithPlacement(zone, placement));
    }

    public void SetCanvasBrightness(int brightness) => SetCanvas(canvas => canvas with { Brightness = Math.Clamp(brightness, 0, 100) });

    /// <summary>Puts every strip back on its default spot.</summary>
    public void ResetLayout() => SetCanvas(canvas => canvas with { Placements = null }, tellPage: true);

    public void SaveEffectPreset(string? name) => SetCanvas(canvas => canvas.WithEffectPreset(name ?? ""), tellPage: true);
    public void LoadEffectPreset(string? name) => SetCanvas(canvas => canvas.LoadEffectPreset(name ?? ""), tellPage: true);
    public void DeleteEffectPreset(string? name) => SetCanvas(canvas => canvas.WithoutEffectPreset(name ?? ""), tellPage: true);

    public void SaveLayoutPreset(string? name)
    {
        var devices = LightParts.Split(_lighting.Devices, Lighting.Parts);
        SetCanvas(canvas => canvas.WithLayoutPreset(name ?? "", LightingService.Layout(canvas, devices)), tellPage: true);
    }

    public void LoadLayoutPreset(string? name) => SetCanvas(canvas => canvas.LoadLayoutPreset(name ?? ""), tellPage: true);
    public void DeleteLayoutPreset(string? name) => SetCanvas(canvas => canvas.WithoutLayoutPreset(name ?? ""), tellPage: true);

    /// <summary>A still of every effect with its current settings, for the tiles on the Lighting page.</summary>
    public void SendLightingThumbs()
    {
        var canvas = Lighting.ActiveCanvas;
        Task.Run(() =>
        {
            var thumbs = new JsonObject(CanvasEffects.All.Select(effect => KeyValuePair.Create(effect.Id, (JsonNode?)Convert.ToBase64String(
                CanvasEffects.Render(effect, canvas.ValuesFor(effect), EffectTileSeconds, EffectTileWidth, EffectTileHeight)))));
            _ui.Post(_ => _window?.Post(new JsonObject { ["type"] = "lightingThumbs", ["width"] = EffectTileWidth, ["height"] = EffectTileHeight, ["thumbs"] = thumbs }), null);
        });
    }

    public void SetLightingEnabled(bool enabled)
    {
        Log($"Lighting control {(enabled ? "on" : "off")}");
        _startedOpenRgb = false;
        SetLighting(Lighting with { Enabled = enabled });
        SendLighting();
    }

    public void SetStartOpenRgb(bool enabled) => SetLighting(Lighting with { StartOpenRgb = enabled });

    public void SetAsrockLighting(bool enabled)
    {
        Log($"Built-in ASRock lighting {(enabled ? "on" : "off")}");
        SetLighting(Lighting with { Asrock = enabled });
        _lighting.Reconnect();
        SendLighting();
    }

    public void SetGpuLighting(bool enabled)
    {
        Log($"Built-in graphics card lighting {(enabled ? "on" : "off")}");
        SetLighting(Lighting with { Gpu = enabled });
        _lighting.Reconnect();
        SendLighting();
    }

    public void SetMemoryLighting(bool enabled)
    {
        Log($"Built-in memory lighting {(enabled ? "on" : "off")}");
        SetLighting(Lighting with { Memory = enabled });
        _lighting.Reconnect();
        SendLighting();
    }

    /// <summary>A choice takes the device off the canvas: Off, or one of its own effects. No choice puts it back.</summary>
    public void SetLightChoice(string? device, LightChoice? choice)
    {
        if (!string.IsNullOrEmpty(device)) SetLighting(Lighting.WithDevice(device, choice));
    }

    public void SetHeaderLeds(string device, string zone, int leds) => SetLighting(Lighting.WithHeaderLeds(device, zone, leds));

    /// <summary>What is plugged into a strip, in order. An empty list makes it one strip again.</summary>
    public void SetZoneParts(string device, string zone, IEnumerable<LightPart> parts)
    {
        if (device.Length == 0 || zone.Length == 0) return;
        SetLighting(Lighting.WithParts(device, zone, parts));
        SendLighting();
    }

    /// <summary>The Try Again button: starts OpenRGB again if it was closed, and asks the server now.</summary>
    public void RetryLighting()
    {
        _startedOpenRgb = false;
        _lights?.RetryExtraNow();
        _lighting.RetryNow();
        OnLightingChanged();
    }

    /// <summary>The page draws each device's LEDs from these while the Lighting page is open, and not otherwise.</summary>
    public void WatchLighting(bool watching)
    {
        if (watching) _lightingTimer.Start();
        else _lightingTimer.Stop();
    }

    void SendLightingFrame()
    {
        if (_window is not { IsLive: true } window || _lighting.State != LightingState.Connected) return;
        var colors = new JsonObject();
        foreach (var device in _lighting.Devices)
        {
            if (_lighting.Preview(device.Key) is { } leds) colors[device.Key] = string.Concat(leds.Select(led => $"{led.R:X2}{led.G:X2}{led.B:X2}"));
        }
        window.Post(new JsonObject
        {
            ["type"] = "lightingFrame", ["colors"] = colors, ["width"] = CanvasPreviewWidth, ["height"] = CanvasPreviewHeight,
            ["canvas"] = Convert.ToBase64String(_lighting.RenderPreview(CanvasPreviewWidth, CanvasPreviewHeight)),
        });
    }

    /// <summary>The one page outside TR Studio's own that the app opens: where OpenRGB is downloaded.</summary>
    public void OpenOpenRgbSite()
    {
        try { Process.Start(new ProcessStartInfo(OpenRgbApp.Site) { UseShellExecute = true })?.Dispose(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Log($"Could not open the browser: {e.Message}"); }
    }

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
                ["version"] = UpdateService.Current.ToString(),
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
        if (e.Mode == PowerModes.Suspend)
        {
            _link.Suspend();
            _lighting.Suspend();
        }
        else if (e.Mode == PowerModes.Resume)
        {
            _link.Resume();
            _lighting.Resume();
        }
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
            _lightingTimer.Dispose();
            if (_lightingSave.Enabled) SaveSettings(); // a change still waiting to be written
            _lightingSave.Dispose();
            _lighting.Dispose();
            _updates.Dispose();
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
