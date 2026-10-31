# TR Studio

A modern Windows app for the screen on the Thermalright Core Vision 360 AIO cooler. It shows live system stats, themes, and images or video on the cooler, and replaces Thermalright's TRCC software.

> **Unofficial.** TR Studio is an independent project. It is not made, endorsed or supported by Thermalright. Thermalright and Core Vision are their owner's trademarks.

![The TR Studio Metrics page, with live CPU, GPU, cooling and system stats](docs/screenshots/metrics.png)

## Why I built it

The Core Vision 360 is great hardware, but the software it ships with let it down. TRCC was buggy and hard to use:

- **It crashed and froze.**
- **Its interface was confusing.**
- **It showed wrong or stale data,** which defeats the point of a stats screen.

So I built a replacement with three priorities: stay reliable, be easy to use, and show numbers you can trust. If a sensor can't be read, TR Studio shows a dash instead of a stale or made-up number.

## Features

### Metrics

- Live dashboard for CPU, GPU, cooling and system stats, with ring gauges and bars
- CPU and GPU temperature, load, power and clock, plus GPU hot spot and memory temperature
- AIO pump, CPU fan, case fan and GPU fan speeds
- Memory load, in use, available and speed, and motherboard temperature
- A **This PC** tile listing your processor, graphics card, motherboard, memory, storage and Windows version
- Tiles you can drag into your own order

### Themes

- Built-in themes, including Halo, Twin Arc, Triple Ring, Stat Grid, Minimal Clock, Ember, Memory and Dual Bar
- Delete the defaults you don't want, and restore them at any time
- Export and import themes as `.tstheme` files, with their media included
- Screen rotation in 90° steps

![The Themes page, with a live preview, the Theme Playlist and the theme gallery](docs/screenshots/themes.png)

### Theme Playlist

- Choose 2 or 3 themes to show in turn, in the order you set
- Switch every 5 seconds to 10 minutes, with a smooth crossfade between themes
- Pauses while you're in the Editor

### Editor

- Live preview that also runs on the cooler as you edit
- Add stats, text, clocks, dates, ring gauges, bars, shapes and lines
- **Picture-in-Picture:** show any live area of your desktop on the cooler
- Image, GIF and video backgrounds, or a solid color
- Layers with drag to reorder, show and hide, and a resizable pane
- Fonts, text stroke and shadow, snapping guides and full undo and redo
- Shapes in eight types, with rotation, gradients, opacity, outlines and dashed or dotted lines
- Save, Save As, and warnings when an item runs off a round screen

![The Editor, with Add tiles, the Layers list and a live preview of a theme](docs/screenshots/editor.png)

### Color

- Color picker with hex entry, recent colors and an eyedropper for anything on your screen
- 10 built-in palettes of 10 colors each
- Create your own palettes, and share them as `.tspalette` files
- **Blend With Theme:** pick a palette swatch and the matching gauge, bar, stats and label all update together, with a gradient to suit
- **Apply to Theme:** color the whole theme from a palette in one click
- Stats keep a clear contrast against the background automatically (solid colors only, since pictures and video can't be measured)

### Smooth gauges

Gauges and bars glide between readings instead of stepping once a second, both on the cooler and in the app.

### Reliability

- Runs quietly in the system tray, and the cooler keeps working with the window closed
- Pauses for PC sleep and reconnects on wake
- Reconnects on its own if the cooler can't be reached
- Optional start with Windows

## Requirements

- Windows 10 version 1809 or later, or Windows 11, 64-bit
- Administrator rights, which the app needs to read CPU sensors
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/), which most PCs already have
- A Thermalright Core Vision 360, USB ID `87AD:70DB`

TRCC and TR Studio can't drive the screen at the same time, so close TRCC first.

### Tested hardware

So far TR Studio has only been tested on one PC, with a Core Vision 360 and its 480×480 screen. The device reports other screen sizes through the same protocol, and the app has code for them, but those are untested.

## Install

Build the installer as shown below, then run `TRStudio-1.0.0-Setup.exe`. It isn't code-signed, so Windows SmartScreen may warn you. Choose **More info**, then **Run anyway**.

Uninstalling keeps your themes and settings in `C:\ProgramData\CoreVisionStudio`.

## Build from source

You'll need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build CoreVisionStudio.sln -c Release
```

```bash
dotnet test tests/CoreVision.Engine.Tests
```

To build the installer, you also need:

- [Inno Setup 6](https://jrsoftware.org/isinfo.php)
- `ffmpeg.exe` in `third-party/ffmpeg/`, from an [LGPL build](https://github.com/BtbN/FFmpeg-Builds/releases). It isn't kept in this repository.

```bash
pwsh tools/BuildInstaller.ps1
```

The installer is written to `artifacts/`.

## How it works

| Part | Purpose |
| --- | --- |
| `src/CoreVision.Engine` | USB protocol, sensors, theme rendering and storage |
| `src/CoreVision.App` | Tray app, plus the WebView2 interface in `wwwroot` |
| `tests/CoreVision.Engine.Tests` | Engine tests |
| `tools/` | Hardware probes, the browser interface preview and the build scripts |
| `installer/` | Inno Setup script |
| `docs/PROTOCOL.md` | Notes on the screen's USB protocol |

Some internal names, such as `CoreVision.App.exe`, come from the project's working title.

Sensors are read with [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), and video is decoded with [FFmpeg](https://ffmpeg.org/).

The interface uses the GothamSSm font if it's installed on your PC, and Segoe UI if not. The font isn't included.
