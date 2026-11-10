# Security

## Reporting a vulnerability

Please don't open a public issue for a security problem. Report it privately instead:

1. Go to the [Security tab](https://github.com/iamfercatapang/tr-studio/security) of this repository.
2. Click **Report a vulnerability** and describe what you found, how to reproduce it, and which version of TR Studio you used.

You'll get a reply on the report within a week. Once a fix is released, the report is published as a security advisory, with credit to you if you'd like.

## Supported versions

Only the latest release gets security fixes. TR Studio updates itself from the sidebar's **Update** button or **Settings → Updates**, so the fix reaches you there.

## What's in scope

TR Studio runs as administrator, so these matter most:

- **The in-app updater**, which downloads `TRStudio-<version>-Setup.exe` from this repository's latest release and installs it only if its SHA-256 matches the digest GitHub publishes for it.
- **The installer**, including the step that runs the bundled PawnIO setup.
- **Themes and imported theme files**, which the app reads and renders.
- **The local web interface** (WebView2) and the messages it exchanges with the app.
- **Anything sent over the internet**: the weather lookup, the update check and opt-in crash reports.

Problems in the bundled third-party software belong with their own projects:

- [PawnIO](https://github.com/namazso/PawnIO), the driver used to read CPU and motherboard sensors
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), which reads the sensors
- [FFmpeg](https://ffmpeg.org/security.html), which decodes video backgrounds

If you're not sure where something belongs, report it here and it'll be passed on.
