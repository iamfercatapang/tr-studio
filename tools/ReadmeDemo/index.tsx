import { Bar, Collapse, Stage, States, type Act } from '../../kit'
import { AppIcon, type IconName } from './icons'
import './trs.css'

// TR Studio's README hero: a device pair on the app's own OLED ground.
//
// Plan
// - Place: the top of a GitHub README (about 880 wide), filmed to a GIF, so
//   the stage carries its own ground: true black with the app's violet,
//   cyan and magenta mesh, the backdrop the real app draws behind its pages.
// - Hero: the Editor window, inset top-left, off the bottom. The app draws
//   its own frameless title bar (Windows controls on the right), so that is
//   its real chrome; the kit's macOS windows would misdescribe it.
// - Satellite: the cooler's round screen, straddling the window's right
//   edge over the canvas area's empty margin, present from frame one.
// - Story (how it feels to use): click Weather, the widget lands on the
//   canvas and, a beat later, on the cooler, because the app previews edits
//   live; drag it below the temperatures and the cooler follows; Save turns
//   into Saved. Payoff: the cooler showing the weather, held 2s.
// - Reset: what the loop added leaves around the anchor (row, widget on both
//   screens) in one reverse stagger, Saved goes back to Save as the draft
//   changes, the widget's position snaps back while hidden.
// - Real strings: the wordmark, Editor, Halo Split, the Weather tile and
//   layer, Save/Saved, and the screen's own readings. Everything else is
//   the app's icons with bars.

type P = { x: number; y: number }
type S = { added: boolean; onCooler: boolean; w: P; saved: boolean; dragging: boolean }

const CENTER: P = { x: 240, y: 240 }
const PLACED: P = { x: 240, y: 372 }
const initial: S = { added: false, onCooler: false, w: CENTER, saved: false, dragging: false }
const POSTER: S = { added: true, onCooler: true, w: PLACED, saved: true, dragging: false }

const CANVAS = 200 // the editor's preview circle, px
const COOLER = 204 // the cooler's screen inside its ring, px

async function script(a: Act<S>) {
  await a.wait(700)

  // 1. Add a Weather widget: it lands on the canvas, then on the cooler
  await a.move({ at: 'tile-weather', x: 0.5, y: 0.32 })
  await a.wait(180)
  await a.click()
  a.mark('added')
  await a.set({ added: true })
  await a.wait(320)
  await a.set({ onCooler: true })
  await a.wait(800)

  // 2. Drag it below the temperatures; the cooler follows live
  const c = a.rect('canvas')
  const scale = c.width / 480
  const grab = { x: c.x + CENTER.x * scale, y: c.y + CENTER.y * scale }
  await a.move(grab)
  await a.wait(160)
  await a.drag({ x: c.x + PLACED.x * scale, y: c.y + PLACED.y * scale }, (at) => {
    a.put({ dragging: true, w: { x: CENTER.x + (at.x - grab.x) / scale, y: CENTER.y + (at.y - grab.y) / scale } })
  }, { duration: 1100 })
  await a.set({ dragging: false, w: PLACED })
  await a.wait(800)

  // 3. Save
  await a.move({ at: 'save', x: 0.78, y: 0.62 })
  await a.wait(160)
  await a.click()
  a.mark('saved')
  await a.set({ saved: true })
  await a.move('rest')
  await a.wait(2400)

  // What the loop added leaves together; the position resets unseen
  await a.set({ added: false, onCooler: false, saved: false })
  await a.wait(900)
  await a.snap({ w: CENTER })
}

export default function TrStudioHero() {
  return (
    <Stage<S> width={880} height={470} theme="dark" initial={initial} poster={POSTER} script={script}
      cursor={{ at: { x: 470, y: 150 }, style: 'arrow' }} className="trs">
      {(s) => (
        <>
          <div className="trs-mesh" />
          <section className="trs-window">
            <header className="trs-titlebar">
              <span className="trs-winctl"><i className="trs-min" /><i className="trs-max" /><i className="trs-close" /></span>
            </header>
            <div className="trs-body">
              <Sidebar />
              <div className="trs-editor">
                <div className="trs-toolbar">
                  <div className="trs-theme-name">Halo Split</div>
                  <span className="trs-tools">
                    <span className="trs-tool"><AppIcon name="Undo" size={14} /></span>
                    <span className="trs-tool trs-tool--redo"><AppIcon name="Undo" size={14} /></span>
                    <span className="trs-save" data-target="save" data-saved={s.saved || undefined}>
                      <States value={s.saved ? 'saved' : 'save'} states={{
                        save: <span className="trs-save__label">Save</span>,
                        saved: <span className="trs-save__label"><AppIcon name="Check" size={12} />Saved</span>,
                      }} />
                    </span>
                  </span>
                </div>
                <div className="trs-work">
                  <Panel added={s.added} />
                  <div className="trs-canvas-area">
                    <div className="trs-canvas" data-target="canvas">
                      <Face size={CANVAS} weather={s.added} w={s.w} selected={s.added} dragging={s.dragging} />
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </section>

          <div className="trs-cooler">
            <div className="trs-cooler__screen">
              <Face size={COOLER} weather={s.onCooler} w={s.w} />
            </div>
          </div>
          <span className="trs-rest" data-target="rest" />
        </>
      )}
    </Stage>
  )
}

function Sidebar() {
  const items: [IconName, number | null][] = [['Metrics', 46], ['Themes', 42], ['Editor', null], ['Settings', 48]]
  return (
    <aside className="trs-sidebar">
      <div className="trs-brand">TR Studio</div>
      <Bar w={58} h={5} tone={2} className="trs-brand__sub" />
      <nav className="trs-nav">
        {items.map(([icon, w]) => (
          <span key={icon} className="trs-nav__item" data-active={w === null || undefined}>
            <AppIcon name={icon} size={14} />
            {w === null ? <span>Editor</span> : <Bar w={w} h={5} tone={2} />}
          </span>
        ))}
      </nav>
    </aside>
  )
}

const TILES: IconName[] = ['Metric', 'Text', 'Clock', 'Date', 'Gauge', 'Bar', 'Shape', 'Weather', 'Music']
const LAYERS: [IconName, number][] = [['Text', 46], ['Metric', 62], ['Clock', 38], ['Gauge', 70], ['Gauge', 58]]

function Panel({ added }: { added: boolean }) {
  return (
    <div className="trs-panel">
      <Bar w={26} h={6} tone={3} className="trs-panel__head" />
      <div className="trs-tiles">
        {TILES.map((t) => (
          <span key={t} className="trs-tile" data-target={t === 'Weather' ? 'tile-weather' : undefined} data-cursor="pointer">
            <AppIcon name={t} size={16} />
            {t === 'Weather' ? <span className="trs-tile__label">Weather</span> : <Bar w={26} h={4} tone={2} />}
          </span>
        ))}
      </div>
      <Bar w={34} h={6} tone={3} className="trs-panel__head" />
      <div className="trs-layers">
        <Collapse open={added}>
          <span className="trs-layer" data-selected>
            <AppIcon name="Weather" size={13} />
            <span>Weather</span>
          </span>
        </Collapse>
        {LAYERS.map(([icon, w], i) => (
          <span key={i} className="trs-layer">
            <AppIcon name={icon} size={13} />
            <Bar w={w} h={5} tone={2} />
          </span>
        ))}
      </div>
    </div>
  )
}

// The Halo Split theme, drawn in the app's 480-unit space and scaled to fit
function Face({ size, weather, w, selected, dragging }: { size: number; weather: boolean; w: P; selected?: boolean; dragging?: boolean }) {
  return (
    <div className="trs-face" style={{ width: size, height: size }}>
      <div className="trs-face__units" style={{ transform: `scale(${size / 480})` }}>
        <svg className="trs-arcs" width="480" height="480" viewBox="0 0 480 480" aria-hidden="true">
          <defs>
            <linearGradient id="trs-cool" x1="0" y1="1" x2="0" y2="0"><stop offset="0" stopColor="#3d7bff" /><stop offset="1" stopColor="#00e5ff" /></linearGradient>
            <linearGradient id="trs-hot" x1="0" y1="1" x2="0" y2="0"><stop offset="0" stopColor="#ffb224" /><stop offset="1" stopColor="#ff4d73" /></linearGradient>
          </defs>
          <path d={arc(128, 232)} className="trs-arc__track" />
          <path d={arc(-52, 52)} className="trs-arc__track" />
          <path d={arc(176, 232)} stroke="url(#trs-cool)" className="trs-arc__fill" />
          <path d={arc(-52, 2)} stroke="url(#trs-hot)" className="trs-arc__fill" />
        </svg>
        <div className="trs-clock">10:24</div>
        <div className="trs-temp" style={{ left: 162 }}><span className="trs-temp__value">42<i>°</i></span><span className="trs-temp__label" style={{ color: '#00e5ff' }}>CPU</span></div>
        <div className="trs-temp" style={{ left: 318 }}><span className="trs-temp__value">40<i>°</i></span><span className="trs-temp__label" style={{ color: '#ff8a4d' }}>GPU</span></div>
        <div className="trs-weather" data-show={weather || undefined} data-selected={selected || undefined} data-dragging={dragging || undefined}
          style={{ left: w.x, top: w.y }}>
          <span className="trs-weather__row"><AppIcon name="Weather" size={50} className="trs-weather__icon" /><span className="trs-weather__temp">24<i>°</i></span></span>
          <span className="trs-weather__cond">Clear</span>
        </div>
      </div>
    </div>
  )
}

// An arc on the 200-unit ring around the screen's center, angles in degrees clockwise from 3 o'clock
function arc(from: number, to: number) {
  const r = 205
  const p = (deg: number) => {
    const t = (deg * Math.PI) / 180
    return `${(240 + r * Math.cos(t)).toFixed(1)} ${(240 + r * Math.sin(t)).toFixed(1)}`
  }
  return `M ${p(to)} A ${r} ${r} 0 0 0 ${p(from)}`
}
