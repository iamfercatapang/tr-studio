# README demo

The source of `docs/screenshots/editor-demo.gif`, the animated hero at the top of the README and in the release notes. It's a simplified illustration of the Editor, not the app itself, built with the Stageshow skill's React kit and its rules.

## Rebuild the GIF

1. Copy this folder to `<stageshow>/examples/tr-studio/` (the skill lives in `~/.claude/skills/stageshow`), then run `npm run dev -- --port 5180` there.
2. Check it at `http://127.0.0.1:5180/?capture=tr-studio`, and run the rehearsal gate:

   ```
   node scripts/film.mjs "http://127.0.0.1:5180/?capture=tr-studio" --gate --every 500 --count 24
   ```

3. Record one loop as frames. The scene runs on the kit's virtual clock, so every frame is exact:

   ```
   node examples/tr-studio/record.mjs http://127.0.0.1:5180 <frames-dir> 2 20
   ```

4. Encode with the bundled ffmpeg:

   ```
   third-party/ffmpeg/ffmpeg.exe -framerate 20 -i <frames-dir>/frame-%05d.png -vf "split[a][b];[a]palettegen=max_colors=256:stats_mode=diff[p];[b][p]paletteuse=dither=sierra2_4a:diff_mode=rectangle" -loop 0 docs/screenshots/editor-demo.gif
   ```

The icons in `icons.tsx` and the colors in `trs.css` are copied from `src/TRStudio.App/wwwroot`. Update them here if the app's look changes.
