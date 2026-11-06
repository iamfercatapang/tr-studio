using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoreVision.Engine;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CoreVision.App;

/// <summary>Hosts the web interface. Closing it hides the window; the screen keeps running from the tray.</summary>
sealed class MainWindow : Form
{
    const string Host = "app.corevision";
    const int DwmUseImmersiveDarkMode = 20;
    const int DwmBorderColor = 34, DwmCaptionColor = 35, DwmTextColor = 36;
    const int WindowHideMs = 250;

    // The title bar takes the app's own colors from app.css: --sidebar-solid, --text and a solid --hairline-strong.
    static readonly Color TitleBar = Color.FromArgb(0x0A, 0x08, 0x12);
    static readonly Color TitleText = Color.FromArgb(0xF3, 0xF0, 0xFF);
    static readonly Color WindowBorder = Color.FromArgb(0x3A, 0x2F, 0x63);

    readonly TrayContext _app;
    readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    bool _ready;
    bool _allowClose;

    public MainWindow(TrayContext app)
    {
        _app = app;
        Text = "TR Studio";
        ClientSize = new Size(1280, 780);
        MinimumSize = new Size(1100, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        Icon = AppIcon.Load(SystemInformation.IconSize);
        Controls.Add(_web);
        HandleCreated += (_, _) =>
        {
            StyleTitleBar();
            // Recalculates the frame now, so the page fills the old title bar from the first paint.
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
        };
        SizeChanged += (_, _) => PostWindowState();
        Load += async (_, _) => await StartWebAsync();
    }

    public bool IsLive => _ready && Visible && WindowState != FormWindowState.Minimized;

    public void Post(JsonNode message)
    {
        if (_ready) _web.CoreWebView2.PostWebMessageAsJson(message.ToJsonString());
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    async Task StartWebAsync()
    {
        try
        {
            var data = Path.Combine(Settings.DataFolder, "webview");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: data);
            await _web.EnsureCoreWebView2Async(environment);
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or COMException or UnauthorizedAccessException or IOException)
        {
            _app.Log($"Interface failed to start: {e.Message}");
            MessageBox.Show(this, "The interface could not start. The cooler screen keeps running from the tray.", Text);
            return;
        }

        var web = _web.CoreWebView2;
        web.Settings.AreDevToolsEnabled = false;
        web.Settings.AreDefaultContextMenusEnabled = false;
        web.Settings.IsStatusBarEnabled = false;
        web.Settings.IsZoomControlEnabled = false;
        web.Settings.IsNonClientRegionSupportEnabled = true; // the page's title bar drags the window with CSS app-region
        web.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Deny);
        web.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
        };
        web.NewWindowRequested += (_, e) => e.Handled = true;
        web.WebMessageReceived += (_, e) => OnMessage(e.WebMessageAsJson);
        web.NavigationCompleted += (_, e) => _app.Log($"Interface loaded: {e.IsSuccess}");
        web.Navigate($"https://{Host}/index.html");
    }

    void OnMessage(string json)
    {
        try
        {
            var message = JsonNode.Parse(json)!.AsObject();
            switch ((string?)message["type"])
            {
                case "window":
                    MoveWindow((string?)message["action"]);
                    break;
                case "ready":
                    _ready = true;
                    _app.Log("Interface ready");
                    PostWindowState();
                    _app.SendState();
                    _app.SendSystemSpecs();
                    _app.SendPalettes();
                    _app.SendNowPlaying();
                    _app.SendWeather();
                    _app.SendUpdate();
                    break;
                case "refresh":
                    _app.SendState();
                    break;
                case "select":
                    _app.SelectTheme((string?)message["path"]);
                    break;
                case "preview":
                    _app.PreviewTheme(Theme.FromJson(message["theme"]!.ToJsonString()));
                    break;
                case "save":
                    _app.SaveTheme(Theme.FromJson(message["theme"]!.ToJsonString()), (string?)message["path"]);
                    break;
                case "delete":
                    _app.DeleteTheme((string?)message["path"]);
                    break;
                case "restoreDefaults":
                    _app.RestoreDefaultThemes();
                    break;
                case "export":
                    ExportTheme((string?)message["path"], (string?)message["name"] ?? "Theme");
                    break;
                case "import":
                    ImportTheme();
                    break;
                case "playlist":
                    _app.SetPlaylist(new ThemePlaylist((bool?)message["enabled"] == true,
                        message["paths"]!.AsArray().Select(path => (string?)path).ToList(), (int?)message["seconds"] ?? ThemePlaylist.DefaultSeconds));
                    break;
                case "alerts":
                    _app.SetAlerts(new AlertSettings(
                        (bool?)message["pump"] == true, (int?)message["pumpMinRpm"] ?? AlertSettings.DefaultPumpRpm,
                        (bool?)message["cpuTemp"] == true, (int?)message["cpuMaxTemp"] ?? AlertSettings.DefaultCpuTemp,
                        (bool?)message["gpuTemp"] == true, (int?)message["gpuMaxTemp"] ?? AlertSettings.DefaultGpuTemp));
                    break;
                case "pausePlaylist":
                    _app.PausePlaylist((bool?)message["paused"] == true);
                    break;
                case "savePalettes":
                    _app.SavePalettes(message["palettes"]!.ToJsonString());
                    break;
                case "exportPalette":
                    ExportPalette((string?)message["name"] ?? "Palette", message["colors"]!.AsArray().Select(color => (string)color!).ToList());
                    break;
                case "importPalette":
                    ImportPalette();
                    break;
                case "rotate":
                    _app.SetRotation((int?)message["degrees"] ?? 0);
                    break;
                case "crashReports":
                    _app.SetCrashReports((bool?)message["enabled"] == true);
                    break;
                case "reportProblem":
                    _app.ReportProblem();
                    break;
                case "releaseNotes":
                    _app.ShowReleaseNotes((string?)message["version"]);
                    break;
                case "askQuestion":
                    _app.AskQuestion();
                    break;
                case "openLogs":
                    _app.OpenLogFolder();
                    break;
                case "roundScreen":
                    _app.SetRoundScreen((bool?)message["round"]);
                    break;
                case "startup":
                    _app.SetStartup((bool?)message["enabled"] == true);
                    break;
                case "pickColor":
                    PickColor();
                    break;
                case "pickMedia":
                    PickMedia();
                    break;
                case "weatherSearch":
                    _ = _app.SearchWeatherAsync((string?)message["query"] ?? "");
                    break;
                case "weatherUnits":
                    _app.SetWeather(_app.WeatherPlace, (string?)message["units"] == "Imperial" ? WeatherUnits.Imperial : WeatherUnits.Metric);
                    break;
                case "weatherPlace":
                    _app.SetWeather(message["place"] is JsonObject place
                        ? new WeatherPlace((string?)place["name"] ?? "", (string?)place["region"] ?? "", (string?)place["country"] ?? "",
                            (double?)place["latitude"] ?? 0, (double?)place["longitude"] ?? 0)
                        : null, (string?)message["units"] == "Imperial" ? WeatherUnits.Imperial : WeatherUnits.Metric);
                    break;
                case "mediaControl":
                    _app.ControlMedia((string?)message["action"]);
                    break;
                case "pickRegion":
                    _ = PickRegionAsync();
                    break;
                case "checkUpdate":
                    _app.CheckForUpdate();
                    break;
                case "installUpdate":
                    _app.InstallUpdate();
                    break;
                case "autoUpdate":
                    _app.SetAutomaticUpdates((bool?)message["enabled"] == true);
                    break;
                case "listFans":
                    _app.SendFans();
                    break;
                case "setFans":
                    _app.SetFans(new FanChoice((string?)message["pump"], (string?)message["cpuFan"], (string?)message["caseFan"]));
                    break;
                case "listCaptureSources":
                    _ = ListCaptureSourcesAsync();
                    break;
            }
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or NullReferenceException or IOException or UnauthorizedAccessException)
        {
            _app.Log($"Interface message failed: {e.Message}");
            Post(new JsonObject { ["type"] = "error", ["message"] = "That didn’t work. Your theme is unchanged." });
        }
    }

    void PickColor()
    {
        new ScreenColorPicker(
            color => Post(new JsonObject { ["type"] = "pickedColor", ["color"] = color is { } picked ? ScreenColorPicker.ToHex(picked) : null }),
            _app.Log).Start();
    }

    void PickMedia()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a Background",
            Filter = "Images and video|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.mp4;*.mov;*.mkv;*.webm;*.avi",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || ThemeStore.KindOf(dialog.FileName) is not { } kind) return;
        Post(new JsonObject { ["type"] = "media", ["kind"] = kind.ToString(), ["path"] = dialog.FileName });
    }

    /// <summary>Steps aside first, so this window isn't in the picture of the desktop.</summary>
    async Task PickRegionAsync()
    {
        Hide();
        await Task.Delay(WindowHideMs);
        void Finish(Rectangle? region)
        {
            Show();
            Activate();
            Post(region is { } area
                ? new JsonObject { ["type"] = "region", ["x"] = area.X, ["y"] = area.Y, ["width"] = area.Width, ["height"] = area.Height }
                : new JsonObject { ["type"] = "region" });
        }
        if (!ScreenRegionPicker.TryShow(Finish, _app.Log)) Finish(null);
    }

    /// <summary>Copying every window takes a moment, so it happens off the interface thread.</summary>
    async Task ListCaptureSourcesAsync()
    {
        var sources = await Task.Run(() => CaptureSources.List(_app.Log));
        Post(sources);
    }

    const string PackageFilter = "TR Studio theme|*" + ThemeStore.PackageExtension;

    static string FileNameFor(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    void ExportTheme(string? path, string name)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export Theme",
            Filter = PackageFilter,
            FileName = FileNameFor(name) + ThemeStore.PackageExtension,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _app.ExportTheme(path, dialog.FileName);
    }

    void ImportTheme()
    {
        using var dialog = new OpenFileDialog { Title = "Import Theme", Filter = PackageFilter };
        if (dialog.ShowDialog(this) == DialogResult.OK) _app.ImportTheme(dialog.FileName);
    }

    const string PaletteFilter = "TR Studio palette|*" + PaletteStore.PackageExtension;

    void ExportPalette(string name, IReadOnlyList<string> colors)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export Palette",
            Filter = PaletteFilter,
            FileName = FileNameFor(name) + PaletteStore.PackageExtension,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _app.ExportPalette(new Palette(name, colors), dialog.FileName);
    }

    void ImportPalette()
    {
        using var dialog = new OpenFileDialog { Title = "Import Palette", Filter = PaletteFilter };
        if (dialog.ShowDialog(this) == DialogResult.OK) _app.ImportPalette(dialog.FileName);
    }

    [DllImport("dwmapi")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    const int WmNcCalcSize = 0x0083;
    const int SmCySizeFrame = 33, SmCxPaddedBorder = 92;
    const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpFrameChanged = 0x20;

    /// <summary>
    /// The page draws its own title bar, like Discord's. The window keeps its sizing frame, shadow and edge
    /// snapping, and only the space Windows would give its title bar goes to the page instead.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcCalcSize && m.WParam != IntPtr.Zero)
        {
            var top = Marshal.ReadInt32(m.LParam, 4); // the window's top edge, before Windows adds its title bar
            base.WndProc(ref m);
            // Maximized, the frame hangs past the screen edge, so the page starts that far down to stay on screen.
            var inset = WindowState == FormWindowState.Maximized
                ? GetSystemMetricsForDpi(SmCySizeFrame, GetDpiForWindow(Handle)) + GetSystemMetricsForDpi(SmCxPaddedBorder, GetDpiForWindow(Handle))
                : 0;
            Marshal.WriteInt32(m.LParam, 4, top + inset);
            return;
        }
        base.WndProc(ref m);
    }

    void MoveWindow(string? action)
    {
        switch (action)
        {
            case "minimize": WindowState = FormWindowState.Minimized; break;
            case "maximize": WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; break;
            case "close": Close(); break; // hides to the tray, like the old close button
        }
    }

    void PostWindowState()
    {
        if (WindowState != FormWindowState.Minimized) Post(new JsonObject { ["type"] = "windowState", ["maximized"] = WindowState == FormWindowState.Maximized });
    }

    [DllImport("user32")]
    static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32")]
    static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32")]
    static extern uint GetDpiForWindow(IntPtr window);

    /// <summary>Dark frame colors for the border Windows still draws around the window.</summary>
    void StyleTitleBar()
    {
        var dark = 1;
        DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        foreach (var (attribute, color) in new[] { (DwmCaptionColor, TitleBar), (DwmTextColor, TitleText), (DwmBorderColor, WindowBorder) })
        {
            var colorRef = color.R | color.G << 8 | color.B << 16; // COLORREF is 0x00BBGGRR
            DwmSetWindowAttribute(Handle, attribute, ref colorRef, sizeof(int));
        }
    }
}
