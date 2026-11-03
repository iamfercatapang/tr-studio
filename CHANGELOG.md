# Changelog

## Unreleased

- **Experimental Trofeo Vision support.** TR Studio can now drive the Trofeo Vision 9.16" screen (USB 0416:5408, 1920×462). Its protocol was worked out from TRCC and hasn’t been tried on a real screen yet. Themes show in the middle of the wide screen for now. If you have one, please report how it goes.

## 1.2.0 (Nov 3, 2026)

- **New title bar.** The window draws its own dark title bar with minimize, maximize and close buttons, like Discord’s, in place of the gray Windows one.
- **Settings page** in the sidebar, with Run at Windows Startup and the Warnings. They moved there from the Themes page.
- **Music widget.** Shows what Spotify or a YouTube tab in Chrome or Edge is playing: album art, song, artist, and a progress bar with times. Add it from the Editor's Music button, then switch each part on or off. It hides itself when nothing plays, and long names end in an ellipsis.
- **Now Playing theme**, built in: large album art with a progress ring around the edge of the screen.
- **Song Progress** can drive any gauge or bar, like a sensor reading.
- **GPU Memory In Use** can drive any gauge or bar. The Metrics page shows video memory in use.
- **Weather widget.** Shows the temperature with a condition icon, the city, today’s high and low, humidity and wind, and a 3-day forecast. Each part can be switched off. Choose your city and °C or °F in the Weather widget’s pane in the Editor. The forecast comes from Open-Meteo every 15 minutes, and only the city’s location is sent. If it can’t update for two hours, the widget shows dashes.
- **Weather theme**, built in: the clock, the date and the forecast.
- **Outdoor Temperature** can drive any gauge or bar.
- **Sidebar player.** Play, pause, previous and next buttons, with the song and its progress, whenever something plays.
- **Previews for GIF and video themes.** Theme cards and the playlist show a still frame of the background with the theme on top, in place of the words GIF or Video. A GIF shows its first frame, a video a typical frame from its start.

## 1.1.0 (Nov 3, 2026)

- **Warnings.** The cooler screen, Windows and the app warn you when the AIO pump slows or stops, or when the CPU or GPU passes a temperature you set. Each warning has its own switch and limit on the Themes page. A pump warning waits 10 seconds and a temperature warning 5, so brief spikes and the zero some boards read after sleep don't count. A sensor the PC doesn't report never raises one.
- **Track Color switch** on gauges and bars. Turned off, the whole gauge shows its gradient with a marker at the reading.
- **Typed slider values.** Every Editor slider has a number field and a reset button.
- **New look.** OLED black with neon accents and a mesh backdrop. See [docs/DESIGN.md](docs/DESIGN.md).
- README rewritten, with new screenshots.

## 1.0.0 (Nov 3, 2026)

First release, with the Windows installer: live stats themes, image, GIF and video backgrounds, the Editor, shapes and bars, palettes, the Theme Playlist and the tray app.
