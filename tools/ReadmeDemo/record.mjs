// Records exactly one loop of the TR Studio demo as PNG frames, for the README GIF.
//   node examples/tr-studio/record.mjs <origin> <out-dir> [scale] [fps]
// Uses the playground's ?embed route, whose scene runs on the kit's virtual clock:
// playback is paused and the clock stepped one frame at a time, so every frame is
// exact. The loop is seamless, so one loop measured between two `added` marks
// makes a seamless GIF; the cut starts 1.4s before the first mark.
import { mkdirSync, rmSync } from 'node:fs'
import { launch } from '../../scripts/browser.mjs'

const [origin, out, scaleArg, fpsArg] = process.argv.slice(2)
const scale = Number(scaleArg ?? 2)
const fps = Number(fpsArg ?? 20)
const step = 60 / fps // the virtual clock runs at 60 frames a second
if (!Number.isInteger(step)) throw new Error('fps must divide 60')
rmSync(out, { recursive: true, force: true })
mkdirSync(out, { recursive: true })

const browser = await launch()
const page = await browser.newPage({ viewport: { width: 1000, height: 600 }, deviceScaleFactor: scale })
await page.goto(`${origin}/?embed=tr-studio`)
// Polled from here: the page's own timers and frames run on the virtual clock
for (let tries = 0; !(await page.evaluate(() => !!(window.__stageshow?.clock && document.querySelector('.ss-stage')))); tries++) {
  if (tries > 100) throw new Error('The scene did not mount')
  await page.waitForTimeout(200)
}
await page.evaluate(() => window.postMessage({ type: 'stageshow:control', playing: false }, location.origin))
await page.waitForTimeout(300)

const stage = page.locator('.ss-stage').first()
const box = await stage.boundingBox()
if (Math.round(box.width) !== 880) throw new Error(`stage drawn at ${box.width}px, expected 880`)

const preroll = Math.round(1.4 * fps)
const frames = []
let seen = 0
let first = -1
for (let i = 0; i < 60 * fps; i++) {
  await page.evaluate((n) => window.__stageshow.clock.step(n), step)
  const mark = await stage.getAttribute('data-mark')
  if (mark === 'added' && frames.at(-1)?.mark !== 'added') {
    seen++
    if (seen === 1) first = i
    if (seen === 2) { frames.push({ i, mark }); break }
  }
  const file = `${out}/raw-${String(i).padStart(5, '0')}.png`
  await page.screenshot({ path: file, clip: box })
  frames.push({ i, mark, file })
}
await browser.close()
if (seen < 2) throw new Error('Did not see two loops')

const last = frames.at(-1).i
const start = Math.max(0, first - preroll)
const end = last - preroll // exclusive: one loop exactly
const { renameSync } = await import('node:fs')
let n = 0
for (const f of frames) {
  if (!f.file) continue
  if (f.i >= start && f.i < end) renameSync(f.file, `${out}/frame-${String(n++).padStart(5, '0')}.png`)
  else rmSync(f.file)
}
console.log(JSON.stringify({ fps, frames: n, seconds: +(n / fps).toFixed(2) }))
