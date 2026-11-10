# TR Studio

Tray app that drives the Thermalright Core Vision 360 AIO screen. .NET 8 WinForms with a WebView2 UI (vanilla JS and CSS in `src/CoreVision.App/wwwroot`), rendering in `src/CoreVision.Engine`.

## Commands

- Build: `dotnet build CoreVisionStudio.sln`
- Test: `dotnet test tests/CoreVision.Engine.Tests`
- UI preview without the app: `node tools/UiPreview/server.js` (port 4173). It loads `tools/UiPreview/mock.js`; when you add a message between the page and the app, add it to the mock too.
- Installer: `powershell -File tools/BuildInstaller.ps1` (needs Inno Setup 6, `third-party/ffmpeg/ffmpeg.exe`, and PawnIO 2.2.0's signed `PawnIO_setup.exe` plus its GPL-2.0 `COPYING` as `PawnIO-LICENSE.txt` in `third-party/pawnio/`; the script checks the setup's SHA-256 and signature), output in `artifacts/`.
- `third-party/` isn't in git.
- README hero GIF: built from `tools/ReadmeDemo` with the Stageshow skill; its README says how to rebuild it.
- Probes for checking features by hand: `tools/MediaProbe` (what Windows reports as playing), `tools/WeatherProbe <city> <out.jpg>` (live Open-Meteo forecast), `tools/ScreenProbe`, `tools/SensorProbe`.

## Conventions

- UI styling follows [docs/DESIGN.md](docs/DESIGN.md): use the tokens in `app.css`, keep data on solid panels.
- Record user-facing changes in [CHANGELOG.md](CHANGELOG.md).
- The product name is TR Studio. Internal names stay as they are: `CoreVision.App.exe`, the `CoreVision.*` namespaces, `C:\ProgramData\CoreVisionStudio`, the `CoreVisionStudio` startup task and mutex.
- A sensor that can't be read shows a dash and never raises a warning.
- Windows' media objects only work on the thread that created them: keep `MediaMonitor`'s calls on its own thread.
- TR Studio uses the internet for three things only. The weather sends only the chosen city's coordinates to Open-Meteo. The update check asks GitHub for the latest release of `iamfercatapang/tr-studio` and sends nothing about the PC; it can be turned off in Settings. Crash reports go to Sentry only after the user says yes (`Settings.CrashReports`, null = not asked yet); `CrashReporter` never starts otherwise, and `CrashLog.Scrub` takes the user name, PC name and user folders out of everything sent. The DSN lives in `CrashReporter.Dsn`; empty turns reporting off and hides the switch.
- Help links open only pages under `https://github.com/iamfercatapang/tr-studio/` (issue forms in `.github/ISSUE_TEMPLATE`).
- Updates: `UpdateService` installs only an asset named `TRStudio-<version>-Setup.exe` whose SHA-256 matches the digest GitHub gives for it, and runs it with `/VERYSILENT /UPDATE=1`. The installer waits for the app to quit and starts it again. Bump `<Version>` in `CoreVision.App.csproj` and tag the release `v<version>`, or installed copies won't see it as newer.
- Fan and pump speeds are read-only. Fan control was considered and left out: it needs admin and a kernel driver, and FanControl (Rem0o) already does it. Don't reverse engineer FanControl.
- CPU temperature, clock and power and the motherboard's fans are read through the PawnIO driver (LibreHardwareMonitor 0.9.5+ dropped WinRing0). The installer runs PawnIO's own setup when it's missing (task `pawnio`, on by default); uninstalling TR Studio leaves PawnIO, since other monitoring apps share it. Without it those readings show a dash and Metrics shows a note.
