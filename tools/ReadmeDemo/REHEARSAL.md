# Rehearsal: TR Studio README hero

Gold: `dark-hero` for the look (depth from surface steps on black, a hairline edge) and `device-pair` for the layout (window inset top-left and off the bottom, a device straddling its right edge over margin). Kept their crop, gutter and satellite placement; replaced everything else with TR Studio's own tokens, icons, sidebar, Editor and frameless title bar.

Beats: Weather tile clicked → widget lands on the canvas and, 0.3s later, on the cooler → dragged below the temperatures, the cooler following live → Save becomes Saved → 2.4s hold → the row and both widgets leave together, Save returns, position resets unseen. Loop 10.05s.

Captured: wide 880×470 (loop sheet, still, gate), reduced-motion poster (Saved, widget placed on both screens), one loop at 20fps on the virtual clock for the GIF.

Film: gate ended `rehearsal passed`. Fixed on the way: uneven padding on the widget card (now 18 units on every side); a 2s dead seam (start wait shortened, the end wait dropped); the widget sitting over the temperatures for 2s before the drag (shortened); the Weather tile label's tight inset (panel widened to 200px, window to 690px).

Assumptions:
- The app's own frameless title bar is drawn from the product, not from the kit's macOS windows, which would misdescribe a Windows app.
- The cooler is a plain round screen in a dark ring, with no Thermalright mark.
- The GIF carries its own ground, the app's OLED black and mesh, because a GIF can't sit transparently on GitHub's light and dark pages.

Unresolved: the poster shows three lit gradients (the Editor nav item, the selected Weather layer and Saved), all as the real app draws them. A stricter cut would quiet the layer row.

Not checked:
- No `compact` design: a GIF is one fixed image, and GitHub scales it down in a phone column, where the small type reads small.
- Coverage was "inconclusive" on this machine (sampling gaps over 16ms), so motion between samples was judged by eye from the stepped frames, not by the tool.
- No fresh-eye review by a second reviewer.
