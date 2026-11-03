# TR Studio

Tray app that drives the Thermalright Core Vision 360 AIO screen. .NET 8 WinForms with a WebView2 UI (vanilla JS and CSS in `src/CoreVision.App/wwwroot`), rendering in `src/CoreVision.Engine`.

## Commands

- Build: `dotnet build CoreVisionStudio.sln`
- Test: `dotnet test tests/CoreVision.Engine.Tests`
- UI preview without the app: `node tools/UiPreview/server.js` (port 4173). It loads `tools/UiPreview/mock.js`; when you add a message between the page and the app, add it to the mock too.
- Installer: `powershell -File tools/BuildInstaller.ps1` (needs Inno Setup 6 and `third-party/ffmpeg/ffmpeg.exe`), output in `artifacts/`.
- `third-party/` isn't in git.
- README hero GIF: built from `tools/ReadmeDemo` with the Stageshow skill; its README says how to rebuild it.
- Probes for checking features by hand: `tools/MediaProbe` (what Windows reports as playing), `tools/WeatherProbe <city> <out.jpg>` (live Open-Meteo forecast), `tools/ScreenProbe`, `tools/SensorProbe`.

## Conventions

- UI styling follows [docs/DESIGN.md](docs/DESIGN.md): use the tokens in `app.css`, keep data on solid panels.
- Record user-facing changes in [CHANGELOG.md](CHANGELOG.md).
- The product name is TR Studio. Internal names stay as they are: `CoreVision.App.exe`, the `CoreVision.*` namespaces, `C:\ProgramData\CoreVisionStudio`, the `CoreVisionStudio` startup task and mutex.
- A sensor that can't be read shows a dash and never raises a warning.
- Windows' media objects only work on the thread that created them: keep `MediaMonitor`'s calls on its own thread.
- The weather is TR Studio's only internet use. It sends only the chosen city's coordinates to Open-Meteo.
- Fan and pump speeds are read-only. Fan control was considered and left out: it needs admin and a kernel driver, and FanControl (Rem0o) already does it. Don't reverse engineer FanControl.
