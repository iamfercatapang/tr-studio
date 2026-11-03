# TR Studio design system

The app's look is OLED dark with a cyberpunk accent set: true black, violet-tinted panels, neon cyan, violet and magenta, and a soft mesh gradient behind the pages. It was chosen with the ui-ux-pro-max skill's Dark Mode (OLED), Cyberpunk UI and Gradient Mesh styles.

Every value lives as a token on `:root` in [app.css](../src/CoreVision.App/wwwroot/app.css). New UI uses these tokens; don't add raw colors or a second theme system.

## Rules

- **Data sits on solid panels.** The mesh gradient goes behind the pages only, never behind readings, gauges or form fields.
- **The brand gradient marks one thing at a time:** the primary action (Save), the selected layer, switches that are on, and the playlist order badges. Text on it uses `--on-accent`.
- **Cyan glow marks "current":** the active nav item, the theme on screen, the selected canvas item, the playing playlist row.
- **Red is for warnings only.** Use `--danger` for marks and `--danger-text` for text.
- **Readings use the mono font** (`--font-mono`, Cascadia Mono with Consolas as fallback). Names and labels stay in `--font-sans`.
- **Glow stays small.** No glitch, scanline or animated mesh effects; the app is a monitoring tool and runs all day.
- **No web fonts.** The WebView's CSP only allows `'self'`, and the app works offline.

## Tokens

| Group | Tokens |
|---|---|
| Surfaces | `--bg` #000000, `--surface` #0c0a16, `--surface-raised` #18142b, `--surface-hover` #231d3e, `--surface-active` #2e2752, `--sidebar`, `--sidebar-solid`, `--overlay`, `--scrim` |
| Lines | `--hairline`, `--hairline-strong`, `--track`, `--edge` (inset hairline shadow) |
| Text | `--text` #f3f0ff, `--text-secondary` #aaa3cc |
| Brand | `--accent` #00e5ff, `--accent-2` #ff3dd8, `--accent-3` #8b5cff, `--on-accent` #06020e, `--accent-soft`, `--gradient-brand`, `--gradient-nav` |
| Status | `--ok` #3dff9a, `--warn` #ffb224, `--danger` #ff4d73, `--danger-text` #ff8fa8, `--danger-soft`, `--danger-line` |
| Data | `--data-cool`, `--data-ok`, `--data-warm`, `--data-hot`, `--data-violet`, `--data-blue`, `--gradient-heat`, `--gradient-load` |
| Backdrop | `--mesh` |
| Depth | `--shadow-sm`, `--shadow-md`, `--shadow-lg`, `--glow-accent`, `--glow-brand`, `--glow-danger`, `--glow-screen` |
| Shape | `--radius` 14, `--radius-md` 10, `--radius-sm` 8, `--radius-xs` 6 |
| Spacing | `--space-1` to `--space-8`: 4, 8, 12, 16, 20, 24, 32, 40 px |
| Type | `--font-sans`, `--font-mono` |
| Motion | `--duration-fast` 120ms, `--duration` 160ms, `--duration-slow` 220ms, `--ease-out` |

The ring gauges' SVG gradients in `index.html` take their stop colors from the data tokens through CSS.

## Accessibility

Checked when the theme landed (Nov 3, 2026):

- Text contrast is at least 5.6:1 on every surface; dark text on the brand gradient is at least 4.9:1.
- Keyboard focus is a 2px `--accent` outline, 11.6:1 against panels.
- No page scrolls sideways at the window's minimum size, 1100×680.
- `prefers-contrast: more` strengthens lines and secondary text and turns the mesh off.
- `prefers-reduced-transparency` makes every translucent pane solid.
- `forced-colors` replaces the gradients with system colors.
- `prefers-reduced-motion` turns off page fades, press scaling and switch motion.

The app has a dark mode only.

## Still literal

- The color picker's square, hue bar and white thumbs, which must show exact colors.
- The switch knob's thin dark outline and the loading spinner's glow.
- The high-contrast override values (they're values for existing tokens).
- Control heights and small paddings, such as 32px buttons. Page, panel and gap spacing use the scale.
