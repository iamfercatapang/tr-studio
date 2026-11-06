# Changelog

## 1.6.0 (Nov 7, 2026)

- **Select several layers in the Editor and move, resize or rotate them together.** Ctrl-click or Shift-click layers on the screen or in Layers, or drag a box across empty screen. A box appears around the selection: drag a corner to resize everything in it (Alt resizes from the middle), or the round handle above it to rotate (Shift snaps to 15°). A single layer gets the same box.
- **Groups.** Group the selection with Ctrl+G, the Group button, or right-click → Group. A group is saved with the theme, shows as a folder in Layers that you can collapse, and moves as one: clicking any member on the screen picks the whole group, and double-clicking picks just that layer. Ctrl+Shift+G or right-click → Ungroup splits it again. Drag a layer between a group's members in Layers to add it, or out of the group to take it out.
- **Every layer can rotate**: text, stats, clocks, dates, gauges, Weather, Music and Picture-in-Picture now have a Rotation slider, as bars and shapes already did.
- **Pump and fan speeds on more motherboards.** TR Studio used to look for one ASUS board's fan names, so on other boards (such as MSI's X870E boards) the AIO Pump, CPU Fan and Case Fan showed a dash. It now recognizes the names boards use, like "Pump Fan", "AIO Pump", "CPU Fan" and "System Fan #1", and checks every sensor chip on the board. If it still picks the wrong header, the new **Cooler Fans** section in Settings lists every fan header with its live speed so you can choose. The motherboard temperature is found on more boards too.
- **A new TR logo** for the app, tray, taskbar, installer and README, in the app's cyan-to-magenta gradient.
- **View Release Notes** in Settings → Updates opens the new version's release page on GitHub, in place of the notes text.
- Settings is quieter: the crash report details under the switch and the Cooler Fans intro are gone.

## 1.5.0 (Nov 4, 2026)

- **A spinner on the cooler while sensors start.** Right after TR Studio starts, a theme that shows readings is covered by a spinning ring and "Reading sensors" until they arrive, instead of a screen of dashes. Themes with only a clock or pictures show straight away.
- **Easier Theme Playlist.** Add Themes opens a picker with every theme's picture: click the ones you want, in the order they should play, then add them all at once. Each theme card also has a badge: + adds it to the playlist, and its number removes it.
- **More progress styles for the Music widget**: Bar, Line, Segments, Around Art (traces the album art) and Screen Edge (runs around the screen's border, a circle on round screens and a rounded rectangle on square or wide ones). Pick one from the pictures under Progress in the Editor.
- **The Now Playing theme fits every screen.** Its progress now runs around the screen's edge in whatever shape the screen is.
- **The Round Screen switch now reaches the cooler**, so edge styles follow the shape you choose. Your earlier choice carries over.
- **Opt-in crash reports.** TR Studio asks once whether it may send a report when it crashes, and nothing is sent until you say yes. Reports go to Sentry without your user name, PC name or folders. The switch is in Settings.
- **Help & Feedback in Settings**: Report a Problem opens a GitHub bug form with your versions filled in, Ask a Question opens Discussions, and Open Log Folder shows the log to attach. After a crash, TR Studio offers to report it the next time it opens.

## 1.4.1 (Nov 4, 2026)

- **The Theme Playlist can hold every theme you have**, not just 3. Add themes until none are left.
- **Run at Windows Startup is on for new installs.** An existing install keeps its choice, and the switch in Settings turns it off.
- The Updates panel in Settings drops its intro text.

## 1.4.0 (Nov 4, 2026)

- **Picture-in-Picture can show a window or a whole screen.** The PiP button opens a picker with Window, Screen and Area tabs, each with a live thumbnail. A window stays visible on the cooler when you move it or cover it with other windows, and is found again after the app restarts. While it's minimized, its last picture stays up. Choose Source… in the Editor changes what an existing PiP shows.
- **Updates from inside the app.** TR Studio checks GitHub for a new version soon after it starts and every 12 hours. When there is one, an Update button appears in the sidebar. Click it and TR Studio downloads the installer, checks it against the release's SHA-256, installs it silently and opens again, all in a few seconds. Settings has Check Now and a switch to turn the automatic check off. The check sends nothing about your PC. Copies older than this version need one last manual install.
- The installer takes its version from the app, so the two can't disagree.

## 1.3.0 (Nov 3, 2026)

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
