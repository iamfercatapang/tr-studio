# Changelog

## 1.7.0 (Nov 11, 2026)

- **Lighting.** A new Lighting page sets the color and effects of your PC's RGB parts. Lighting is off until you turn it on, and nothing goes over the internet.
- **ASUS Aura USB motherboards work by themselves.** TR Studio drives the board's RGB and addressable headers directly, so the fans and strips plugged into them light up with no other app running. The driver is ported from [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB).
- **RGB memory, if you turn it on.** On AMD boards TR Studio can also drive RGB memory with an ENE lighting chip (XPG, G.Skill, T-Force and others) by itself, through the PawnIO driver it already installs. It is new and works on the bus the memory's own data chips use, so it is off until you switch on **Drive RGB Memory Directly** on the Lighting page. It needs TR Studio running as administrator. This driver is ported from OpenRGB too.
- **ASRock Polychrome boards, if you turn it on.** A driver for the headers and onboard lights of ASRock boards with a Polychrome USB controller, ported from OpenRGB. Nobody has run it on a real board yet, so it is off until you switch on **Drive ASRock Polychrome Boards**. If you try it, please report how it goes.
- **Graphics cards, if you turn it on.** TR Studio can drive the lights of many graphics cards by itself, over the card's own I2C bus through the NVIDIA driver: GALAX/KFA2, ASUS, MSI and Gigabyte cards, about 350 models from OpenRGB's list, with drivers ported from OpenRGB. The GALAX RTX 4070 EX Gamer is confirmed on real hardware; the other cards aren't yet, so this is off until you switch on **Drive Graphics Card RGB Directly**. AMD cards and other brands still come through OpenRGB. If you try it, please report how it goes.
- **Everything else through OpenRGB.** Graphics cards, other brands of board and other memory appear on the page when [OpenRGB](https://openrgb.org/), a free app, is installed. TR Studio starts it and connects by itself, and the page walks you through it.
- **A lighting canvas.** One effect is drawn on a canvas, and every strip of LEDs (a header, a memory stick, an onboard light) is a box you place on it. Each LED shows the color under its spot, so an effect travels across your PC in the order you laid the hardware out.
- **Ten effects, each with its own settings**: Solid Color, Rainbow Wave, Color Cycle, Breathing, Gradient Flow, Aurora, Comet, Sweep, Radar and Ripple. Colors, speed, direction, trail, width and background are set per effect, and a grid of live tiles shows each one as you have set it.
- **Layout.** Drag a strip to move it, pull its corner to size it, turn it with the knob above it (Shift snaps to 15°), nudge it with the arrow keys, or type exact numbers. Each strip has its own brightness, and the layout has one for everything.
- **Fans and strips on one header.** List what is chained on a header, in order from the plug, and each part gets its own box: a ring for a fan, a line for a strip. Turn a ring to match where its first LED sits.
- **Presets** for effect settings and for layouts.
- **A device can leave the canvas**: set it to Off, or to one of the effects it runs by itself.
- **Addressable headers.** A header can't count its LEDs, so each one has a box for how many are plugged in.
- **More screens, experimental.** TR Studio now talks to two more kinds of Thermalright-style screen, worked out from TRCC without the hardware: HID screens (USB 0416:5302: Trofeo Vision LCD, Assassin Spirit 120 Vision, Frozen Warframe SE, 360 and Pro, AS120 and BA120 Vision) and small RGB565 screens (USB 0416:5406, such as the LC-series pump heads). Screens from other brands built on the same parts should work too. If you own one, please report how it goes.
- **SCSI screens, experimental.** TR Studio now also drives the screens that Windows sees as a USB drive (USB 87CD:70DB on the Frozen Horizon Pro, Frozen Magic Pro and Frozen Vision V2, and USB 0402:3922 on the Frozen Warframe and Frozen Warframe 240), worked out from TRCC without the hardware. They need TR Studio running as administrator, as the installed app does. If you own one, please report how it goes.
- The version now shows under TR Studio in the sidebar.
- The program file is now `TRStudio.exe` instead of `CoreVision.App.exe`. Updating removes the old file, and your themes and settings stay where they are.
- The Lighting page says when another lighting app, such as Armoury Crate or iCUE, is running, since two apps driving the same lights make them flicker.
- RGB memory on Intel boards is new and nobody has confirmed it yet; AMD boards are tested.
- Lights get their colors back after the PC wakes from sleep or OpenRGB restarts. After waking, TR Studio waits a few seconds for the hardware to come back, then sets every light again, including the size of each addressable header, so a header no longer stays on the board's own rainbow after waking. A light that goes back to its own effect is taken back within seconds, and a controller that stops answering no longer freezes every light.
- Effects stay smooth while TR Studio sits in the tray. Windows had been slowing apps with no open window, which made RGB memory stutter.
- **TR Studio is now licensed under the GPL, version 2 or later**, instead of MIT, so it can use OpenRGB's drivers. Versions up to 1.6.2 stay MIT. The installer now puts the license in the app folder.

## 1.6.2 (Nov 10, 2026)

- **CPU temperature and fan speeds on every PC.** The sensor library now reads the CPU's temperature, clock and power and the motherboard's fans through the PawnIO driver, which many PCs don't have, so those showed a dash. The installer now installs PawnIO when it's missing (a box you can untick), and an update from inside the app installs it too. If it's still missing, Metrics says so and the log notes it.
- **A calmer look.** The color wash behind the pages and the glows around buttons, switches and selections are gone, so the cooler screen preview is the one thing that lights up. The selected layer and switches use plain cyan, the TR Studio name is plain text, headings are lighter, and spacing is evened out across every page. The CPU panel's readings now sit right under its gauges.

## 1.6.1 (Nov 10, 2026)

- **GPU stats on PCs with built-in graphics and a graphics card.** TR Studio read whichever GPU came first, often the processor's idle built-in graphics, so temperature, power and fan showed a dash and load sat at 0%. It now reads the graphics card. AMD cards also show memory temperature, power and fan speed, which were missing because AMD names those sensors differently. The log lists every GPU found and which one is read.
- **Choose the GPU in Settings.** On a PC with more than one GPU, a new Graphics Card section picks which one the GPU stats come from. Automatic, the default, reads the graphics card.

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
