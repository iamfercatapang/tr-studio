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
    const int WindowHideMs = 250;

    readonly TrayContext _app;
    readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    bool _ready;
    bool _allowClose;

    public MainWindow(TrayContext app)
    {
        _app = app;
        Text = "Thermalright Studio";
        ClientSize = new Size(1280, 780);
        MinimumSize = new Size(1100, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        Icon = AppIcon.Load(SystemInformation.IconSize);
        Controls.Add(_web);
        var dark = 1;
        HandleCreated += (_, _) => DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
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
                case "ready":
                    _ready = true;
                    _app.Log("Interface ready");
                    _app.SendState();
                    _app.SendSystemSpecs();
                    _app.SendPalettes();
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
                case "startup":
                    _app.SetStartup((bool?)message["enabled"] == true);
                    break;
                case "pickColor":
                    PickColor();
                    break;
                case "pickMedia":
                    PickMedia();
                    break;
                case "pickRegion":
                    _ = PickRegionAsync();
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

    const string PackageFilter = "Thermalright Studio theme|*" + ThemeStore.PackageExtension;

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

    const string PaletteFilter = "Thermalright Studio palette|*" + PaletteStore.PackageExtension;

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
}
