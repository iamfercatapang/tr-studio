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

    readonly FileLog _log = new();
    readonly ScreenLink _link;
    readonly SensorMonitor _sensors;
    readonly ThemePlayer _player;
    readonly ThemeStore _themes;
    readonly PaletteStore _palettes = new(Path.Combine(Settings.DataFolder, "palettes.json"));
    readonly NotifyIcon _icon;
    readonly ToolStripMenuItem _statusItem = new("Starting") { Enabled = false };
    readonly SynchronizationContext _ui = new WindowsFormsSynchronizationContext();
    readonly Stopwatch _sincePreview = Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer _playlistTimer = new();
    bool _playlistPaused;
    MainWindow? _window;
    readonly Lazy<Task<IReadOnlyList<SpecRow>>> _specs;
    Settings _settings = Settings.Load();
    int _previewPending;

    public TrayContext(bool startHidden)
    {
        _specs = new(() => Task.Run(() => SystemSpecs.Read(Log)));
        Log($"Started as {Environment.UserName}, profile {Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}");
        _themes = new ThemeStore(Path.Combine(Settings.DataFolder, "themes"), Log);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Thermalright Studio", null, (_, _) => ShowWindow());
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        _icon = new NotifyIcon { Icon = AppIcon.Load(SystemInformation.SmallIconSize), Text = "Thermalright Studio", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => ShowWindow();

        _sensors = new SensorMonitor(() => new LibreSensorSource(), log: Log);
        _sensors.Start();
        _link = new ScreenLink(() => ScreenFinder.Open(() => WinUsbDevice.Open(), WinUsbDevice.PresentUsbPaths), log: Log);

        var ffmpeg = ThemeStore.FindFfmpeg();
        _player = new ThemePlayer(_themes.Load(_settings.ThemePath), () => _sensors.Current, _link.Submit,
            spec => Backgrounds.Create(spec, ffmpeg, Log, _player!.Screen.Width, _player.Screen.Height), Log);
        _player.Rotation = _settings.Rotation;
        _player.FrameRendered += OnFrameRendered;
        _player.Start();
        _playlistTimer.Tick += (_, _) => ShowNextInPlaylist();
        RestartPlaylist();

        _link.StatusChanged += OnLinkStatus;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _link.Start();

        if (!startHidden) ShowWindow();
    }

    public void Log(string line) => _log.Write(line);

    /// <summary>A newly detected screen resizes the frames first, so its first image already fits.</summary>
    void OnLinkStatus(LinkStatus status)
    {
        var resized = status.Screen is { } screen && screen != _player.Screen;
        if (resized) _player.Screen = status.Screen!;
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

    /// <summary>Thumbnails are rendered off the UI thread. Video and GIF cards use a label instead.</summary>
    public void SendState()
    {
        var entries = _themes.List();
        var current = _player.Theme;
        var status = _link.Status;
        var path = _settings.ThemePath;
        var playlist = Playlist;
        Task.Run(() =>
        {
            var themes = new JsonArray();
            var screen = _player.Screen;
            using var renderer = new ThemeRenderer(screen.Width, screen.Height, screen.JpegQuality);
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
                ["screen"] = new JsonObject { ["width"] = screen.Width, ["height"] = screen.Height, ["round"] = screen.Round, ["model"] = screen.Model },
                ["startup"] = WindowsStartup.IsEnabled(),
                ["fonts"] = new JsonArray(InstalledFonts.Value.Select(name => (JsonNode?)name).ToArray()),
                ["status"] = StatusJson(status),
            };
            _ui.Post(_ => _window?.Post(state), null);
        });
    }

    string? Thumbnail(ThemeRenderer renderer, Theme theme)
    {
        if (theme.Background.Kind is BackgroundKind.Video or BackgroundKind.Gif) return null;
        using var background = Backgrounds.Create(theme.Background, log: Log, width: _player.Screen.Width, height: _player.Screen.Height);
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
        _icon.Text = text.Length > MaxTrayText ? text[..MaxTrayText] : text;
        _window?.Post(new JsonObject { ["type"] = "status", ["status"] = StatusJson(status) });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _player.Dispose();
            _link.Dispose();
            _sensors.Dispose();
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
