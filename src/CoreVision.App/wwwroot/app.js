const SCREEN = 480;
/*
 * Temperatures and percentages have a real scale. Fan gauges use each fan's usual top speed,
 * since the true limit isn't known. Power and clocks have no sensible scale, so they stay numbers.
 */
const METRICS = {
  CpuTemp: { label: 'CPU Temperature', short: 'Temperature', unit: '°', min: 20, max: 100, bar: 'heat' },
  CpuLoad: { label: 'CPU Load', short: 'Load', unit: '%', min: 0, max: 100, bar: 'load' },
  CpuPower: { label: 'CPU Power', short: 'Power', unit: ' W', min: 0, max: 120 },
  CpuClock: { label: 'CPU Clock', short: 'Clock', unit: ' MHz', min: 0, max: 5000 },
  GpuTemp: { label: 'GPU Temperature', short: 'Temperature', unit: '°', min: 20, max: 100, bar: 'heat' },
  GpuLoad: { label: 'GPU Load', short: 'Load', unit: '%', min: 0, max: 100, bar: 'load' },
  GpuPower: { label: 'GPU Power', short: 'Power', unit: ' W', min: 0, max: 220 },
  GpuClock: { label: 'GPU Clock', short: 'Clock', unit: ' MHz', min: 0, max: 3200 },
  GpuHotSpot: { label: 'GPU Hot Spot', short: 'Hot Spot', unit: '°', min: 20, max: 110, bar: 'heat' },
  GpuMemoryTemp: { label: 'GPU Memory Temp', short: 'Memory Temp', unit: '°', min: 20, max: 110, bar: 'heat' },
  MotherboardTemp: { label: 'Motherboard', unit: '°', min: 20, max: 80, bar: 'heat' },
  MemoryLoad: { label: 'Memory', unit: '%', min: 0, max: 100, bar: 'load' },
  CpuFanRpm: { label: 'CPU Fan', unit: ' RPM', min: 0, max: 2500, bar: 'cool' },
  PumpRpm: { label: 'AIO Pump', unit: ' RPM', min: 0, max: 4000, bar: 'cool' },
  CaseFanRpm: { label: 'Case Fan', unit: ' RPM', min: 0, max: 2500, bar: 'cool' },
  GpuFan: { label: 'GPU Fan', unit: ' RPM', min: 0, max: 3500, bar: 'cool' },
  MemoryUsed: { label: 'Memory In Use', short: 'In Use', unit: ' GB', min: 0, max: 64, digits: 1 },
  MemoryAvailable: { label: 'Memory Available', short: 'Available', unit: ' GB', min: 0, max: 64, digits: 1 },
  MemorySpeed: { label: 'Memory Speed', short: 'Speed', unit: ' MT/s', min: 0, max: 6000 },
  MusicProgress: { label: 'Song Progress', short: 'Progress', unit: '%', min: 0, max: 100 },
  OutdoorTemp: { label: 'Outdoor Temperature', short: 'Outdoor', unit: '°', min: -10, max: 40, bar: 'heat' },
  GpuMemoryUsed: { label: 'GPU Memory In Use', short: 'VRAM', unit: ' GB', min: 0, max: 16, digits: 1 },
};
/* Listed in the default order. */
const METRIC_PANELS = [
  { id: 'pc', title: 'This PC', iconName: 'Monitor' },
  { id: 'cpu', title: 'CPU', iconName: 'Chip', rings: ['CpuTemp', 'CpuLoad'], readouts: ['CpuPower', 'CpuClock'] },
  { id: 'gpu', title: 'GPU', iconName: 'GraphicsCard', rings: ['GpuTemp', 'GpuLoad'], readouts: ['GpuHotSpot', 'GpuMemoryTemp', 'GpuPower', 'GpuClock', 'GpuMemoryUsed'] },
  { id: 'cooling', title: 'Cooling', iconName: 'Fan', rings: ['PumpRpm', 'CpuFanRpm', 'CaseFanRpm', 'GpuFan'], compact: true },
  { id: 'system', title: 'System', iconName: 'MemoryStick', rings: ['MemoryLoad'], readouts: ['MemoryUsed', 'MemoryAvailable', 'MemorySpeed', 'MotherboardTemp'] },
];
const PANEL_DRAG_START = 5;
const PANEL_SWAP_PAUSE_MS = 220;
const PANEL_MOVE = { duration: 260, easing: 'cubic-bezier(0.2, 0.8, 0.2, 1)' };
const RING_ARC = 'M21.82 98.18A54 54 0 1 1 98.18 98.18'; // 270 degrees, open at the bottom
const GAUGE_RESPONSE = 1.4; // about a second to arrive, which is how often sensors read
const LOADING_SETTLE_MS = 2000;
const LOADING_LIMIT_MS = 20000;
const DEGREE = '°';
const TIME_FORMATS = ['HH:mm', 'HH:mm:ss', 'h:mm tt', 'h:mm:ss tt'];
const DATE_FORMATS = ['MMM d, yyyy', 'MMMM d, yyyy', 'ddd, MMM d', 'dddd, MMMM d', 'dddd', 'MMM d', 'dd MMM yyyy', 'dd/MM/yyyy', 'MM/dd/yyyy', 'yyyy-MM-dd'];
const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const FONT_STYLES = ['Regular', 'Bold', 'Italic', 'Bold Italic'];
const DEFAULT_FONT = 'Segoe UI';
const BUILT_IN = 'builtin:';
const isBuiltIn = (path) => !path || path.startsWith(BUILT_IN);
const NEW_GAUGE = {
  kind: 'Gauge', x: 240, y: 240, size: 430, metric: 'GpuTemp', min: 20, max: 100, thickness: 28,
  startAngle: 135, sweep: 270, color: '#22E5E8', endColor: '#A855F7', trackColor: '#2A2A2E', useTrackColor: true, dot: true,
};
const NEW_BAR = {
  kind: 'Bar', x: 240, y: 240, size: 300, metric: 'CpuTemp', min: 20, max: 100, thickness: 22, cornerRadius: 11, rotation: 0,
  color: '#FF9F0A', endColor: '#FF453A', trackColor: '#3A3A3F', useTrackColor: true, stripes: false,
};
const SHAPES = ['Line', 'Arc', 'Rectangle', 'Ellipse', 'Triangle', 'Diamond', 'Hexagon', 'Star'];
const LINE_STYLES = ['Solid', 'Dashed', 'Dotted'];
const NEW_SHAPE = {
  kind: 'Shape', x: 240, y: 240, size: 160, height: 120, shape: 'Rectangle', lineStyle: 'Solid', thickness: 6, cornerRadius: 0, rotation: 0,
  startAngle: 135, sweep: 270, filled: true, gradient: false, color: '#0A84FF', endColor: '#BF5AF2', opacity: 1,
};
const NEW_CAPTURE = { size: 240, cornerRadius: 16 };
const NEW_WEATHER = {
  kind: 'Weather', x: 240, y: 240, size: 300, bold: true, color: '#FFFFFF', secondaryColor: '#98989D',
  showCondition: true, showDetails: true, showForecast: true,
};
const NEW_MUSIC = {
  kind: 'Music', x: 240, y: 240, size: 300, bold: true, cornerRadius: 16,
  color: '#FFFFFF', secondaryColor: '#98989D', endColor: '#00E5FF', trackColor: '#2A2A2E',
  showArt: true, showTitle: true, showArtist: true, showProgress: true, progressStyle: 'Bar',
};
const NEW_TEXT_SIZE = 40;
const MIN_HANDLE = 16;
/* Line drawings on a 16 unit grid. Shapes and layer kinds go by their own names. */
const ICONS = {
  Metrics: '<path d="M1.5 8.5h3l2-5 3 9 2-4h3"/>',
  Themes: '<rect x="2" y="2" width="5" height="5" rx="1.25"/><rect x="9" y="2" width="5" height="5" rx="1.25"/><rect x="2" y="9" width="5" height="5" rx="1.25"/><rect x="9" y="9" width="5" height="5" rx="1.25"/>',
  Editor: '<path d="M2 4.5h6.5M12.5 4.5H14M2 11.5h1.5M7.5 11.5H14"/><circle cx="10.5" cy="4.5" r="2"/><circle cx="5.5" cy="11.5" r="2"/>',
  Settings: '<circle cx="8" cy="8" r="2.25"/><path d="M8 1.5v2M8 12.5v2M1.5 8h2M12.5 8h2M3.4 3.4l1.4 1.4M11.2 11.2l1.4 1.4M3.4 12.6l1.4-1.4M11.2 4.8l1.4-1.4"/>',
  Metric: '<path d="M2.5 13.5v-4M8 13.5v-11M13.5 13.5v-7"/>',
  Text: '<path d="M3 4.5v-2h10v2M8 2.5v11M6 13.5h4"/>',
  Clock: '<circle cx="8" cy="8" r="6"/><path d="M8 4.5V8l2.5 1.5"/>',
  Date: '<rect x="2" y="3" width="12" height="11" rx="2"/><path d="M2 6.5h12M5.25 1.5v3M10.75 1.5v3"/>',
  Gauge: '<path d="M3.8 12.2a6 6 0 1 1 8.4 0"/><path d="M8 8.5l2.5-2.5"/>',
  Bar: '<rect x="1.5" y="5" width="13" height="6" rx="3"/><path d="M5 5v6M8.5 5v6"/>',
  Shape: '<circle cx="6" cy="6" r="4.5"/><path d="M7.5 7.5h7v7h-7z"/>',
  Capture: '<rect x="1.5" y="3" width="13" height="10" rx="2"/><rect x="8" y="8" width="4.5" height="3" rx="0.75"/>',
  Music: '<path d="M6 12.5V3.5l7.5-1.5v9"/><circle cx="4.25" cy="12.5" r="1.75"/><circle cx="11.75" cy="11" r="1.75"/>',
  Weather: '<circle cx="5.5" cy="5.5" r="2.25"/><path d="M5.5 1v1M1 5.5h1M2.3 2.3l.7.7M8.7 2.3l-.7.7"/><path d="M5.5 14h6.25a2.75 2.75 0 1 0-.6-5.43A3.75 3.75 0 0 0 4 9.5 2.25 2.25 0 0 0 5.5 14z"/>',
  Play: '<path d="M5 3l8 5-8 5z"/>',
  Pause: '<path d="M5.5 3.5v9M10.5 3.5v9"/>',
  Previous: '<path d="M4 3.5v9M12.5 3.5L6 8l6.5 4.5z"/>',
  Next: '<path d="M12 3.5v9M3.5 3.5L10 8l-6.5 4.5z"/>',
  Line: '<path d="M2.5 13.5l11-11"/>',
  Arc: '<path d="M3.5 12.5a6.4 6.4 0 1 1 9 0"/>',
  Rectangle: '<rect x="2" y="3.5" width="12" height="9" rx="1.5"/>',
  Ellipse: '<ellipse cx="8" cy="8" rx="6.5" ry="4.75"/>',
  Triangle: '<path d="M8 2.5l6 10.5H2z"/>',
  Diamond: '<path d="M8 1.5L14.5 8 8 14.5 1.5 8z"/>',
  Hexagon: '<path d="M8 1.5l5.6 3.25v6.5L8 14.5l-5.6-3.25v-6.5z"/>',
  Star: '<path d="M8 1.8l1.9 3.9 4.3.6-3.1 3 .7 4.3L8 11.6l-3.8 2 .7-4.3-3.1-3 4.3-.6z"/>',
  Background: '<rect x="1.5" y="2.5" width="13" height="11" rx="2"/><path d="M1.5 11l3.5-3.5 3 3 2-2 4.5 4"/><circle cx="10.5" cy="6" r="1"/>',
  Monitor: '<rect x="1.5" y="2.5" width="13" height="9" rx="1.5"/><path d="M8 11.5V14M5 14h6"/>',
  Chip: '<rect x="4" y="4" width="8" height="8" rx="1.5"/><rect x="6.5" y="6.5" width="3" height="3" rx="0.5"/><path d="M6 1.5V4M10 1.5V4M6 12v2.5M10 12v2.5M1.5 6H4M1.5 10H4M12 6h2.5M12 10h2.5"/>',
  GraphicsCard: '<rect x="1.5" y="3.5" width="13" height="8" rx="1.5"/><circle cx="5.25" cy="7.5" r="2"/><circle cx="10.75" cy="7.5" r="2"/><path d="M3.5 11.5v2M7 11.5v2"/>',
  Fan: '<circle cx="8" cy="8" r="6.5"/><circle cx="8" cy="8" r="1"/><path d="M8 7c-1.6-1.4-1.6-3.4 0-4 1.8-.4 2.8 1.4 1.8 3M8.9 8.5c2-.6 3.8.3 3.5 2-.6 1.7-2.7 1.8-3.6.3M7.1 8.5c-.4 2.1-2.1 3.2-3.4 2-1.2-1.3-.2-3.2 1.6-3.2"/>',
  MemoryStick: '<rect x="1.5" y="4" width="13" height="7" rx="1"/><path d="M4.5 6.5v2M8 6.5v2M11.5 6.5v2M4 11v2M8 11v2M12 11v2"/>',
  Reset: '<path d="M2.75 8a5.25 5.25 0 1 0 1.55-3.7"/><path d="M2.5 2v3h3"/>',
  Up: '<path d="M8 13V3M3.5 7.5L8 3l4.5 4.5"/>',
  Down: '<path d="M8 3v10M3.5 8.5L8 13l4.5-4.5"/>',
  Remove: '<path d="M4 4l8 8M12 4l-8 8"/>',
  Add: '<path d="M8 3v10M3 8h10"/>',
  ProgressOff: '<rect x="1.5" y="6.5" width="13" height="3" rx="1.5" stroke-dasharray="2 2"/>',
  ProgressBar: '<rect x="1.5" y="6.5" width="13" height="3" rx="1.5"/><path d="M3 8h5" stroke-width="3"/>',
  ProgressLine: '<path d="M1.5 8h13"/><circle cx="8" cy="8" r="1.75" fill="currentColor"/>',
  ProgressSegments: '<path d="M1.5 8h2M5 8h2M8.5 8h2M12 8h2.5" stroke-width="2.5"/>',
  ProgressAroundArt: '<rect x="4.5" y="4.5" width="7" height="7" rx="1.5"/><path d="M8 1.75h4.25a2 2 0 0 1 2 2V10"/>',
  ProgressScreenEdge: '<rect x="1.5" y="2.5" width="13" height="11" rx="2.5"/><path d="M5 6.5h6M5 9.5h4"/>',
  Check: '<path d="M3 8.5l3.25 3.25L13 5"/>',
  Warning: '<path d="M8 2l6.5 11.5h-13z"/><path d="M8 6.5v3M8 11.5v.01"/>',
  Update: '<path d="M8 2v8M4.5 6.5L8 10l3.5-3.5"/><path d="M2.5 11v1.5a1 1 0 0 0 1 1h9a1 1 0 0 0 1-1V11"/>',
  Shown: '<path d="M1.5 8S4 3.5 8 3.5 14.5 8 14.5 8 12 12.5 8 12.5 1.5 8 1.5 8z"/><circle cx="8" cy="8" r="2"/>',
  Group: '<rect x="1.5" y="1.5" width="8.5" height="8.5" rx="1.5"/><path d="M12.5 6h.5a1.5 1.5 0 0 1 1.5 1.5V13a1.5 1.5 0 0 1-1.5 1.5H7.5A1.5 1.5 0 0 1 6 13v-.5"/>',
  Chevron: '<path d="M6 3.5L10.5 8 6 12.5"/>',
  Hidden: '<path d="M1.5 8S4 3.5 8 3.5 14.5 8 14.5 8 12 12.5 8 12.5 1.5 8 1.5 8z"/><path d="M3 13L13 3"/>',
};
const PLAYLIST = { minThemes: 2 };
const ALERT_RULES = [
  { kind: 'Pump', on: 'pump', limit: 'pumpMinRpm', metric: 'PumpRpm', iconName: 'Fan', label: 'AIO Pump', limitLabel: 'Warn below', unit: 'RPM', min: 100, max: 3000, step: 50, fallback: 500 },
  { kind: 'CpuTemp', on: 'cpuTemp', limit: 'cpuMaxTemp', metric: 'CpuTemp', iconName: 'Chip', label: 'CPU Temperature', limitLabel: 'Warn at', unit: '°C', min: 50, max: 110, step: 1, fallback: 90 },
  { kind: 'GpuTemp', on: 'gpuTemp', limit: 'gpuMaxTemp', metric: 'GpuTemp', iconName: 'GraphicsCard', label: 'GPU Temperature', limitLabel: 'Warn at', unit: '°C', min: 50, max: 110, step: 1, fallback: 85 },
];
const SNAP_DISTANCE = 7;
const DEFAULT_MEDIA_DIM = 0.35;
const $ = (id) => document.getElementById(id);
const clone = (value) => structuredClone(value);
const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)');

const app = {
  themes: [], current: null, currentPath: null, stats: {},
  draft: null, draftPath: null, savedDraft: '', selected: -1, selection: [], collapsed: new Set(),
  rotation: 0, fonts: [], startup: false, removedDefaults: 0, palettes: [],
  screen: { width: SCREEN, height: SCREEN, round: true, model: '' },
  undo: [], redo: [], page: 'metrics',
  playlist: { enabled: false, paths: [], seconds: 30 },
  weather: { place: null, units: 'Metric', report: null, error: null },
  alertSettings: Object.fromEntries(ALERT_RULES.flatMap((rule) => [[rule.on, true], [rule.limit, rule.fallback]])),
  alerts: [],
};

if (!window.chrome?.webview) await import('/mock.js');
const host = window.chrome.webview;
const send = (message) => host.postMessage(message);

/*
 * Items are placed on a 480 unit square at the screen's center, scaled to its shorter side.
 * This gives the screen's area in those units, so wide screens run past 0 and 480 at the sides.
 */
function screenArea() {
  const { width, height } = app.screen;
  const scale = Math.min(width, height) / SCREEN;
  const w = width / scale;
  const h = height / scale;
  return { w, h, left: (SCREEN - w) / 2, top: (SCREEN - h) / 2 };
}

/* Older versions kept the Round Screen choice in the page only. It moves to the app once, so the cooler sees it too. */
function migrateRoundPreference(override) {
  let saved = null;
  try { saved = localStorage.getItem('roundScreen'); localStorage.removeItem('roundScreen'); } catch { /* nothing to move */ }
  if (override == null && saved !== null) send({ type: 'roundScreen', round: saved === 'true' });
}

/* The app sends the shape with the user's choice already applied. */
const isRound = () => Boolean(app.screen.round);

/* Shapes every preview to match the connected screen. */
function applyScreenShape() {
  const { width, height } = app.screen;
  const scale = Math.min(420 / width, 280 / height);
  const root = document.documentElement.style;
  root.setProperty('--screen-ratio', `${width} / ${height}`);
  root.setProperty('--preview-width', `${Math.round(width * scale)}px`);
  root.setProperty('--preview-height', `${Math.round(height * scale)}px`);
  document.body.classList.toggle('round-screen', isRound());
  if (app.page === 'editor') { fitStage(); renderHandles(); }
}

/* A critically damped spring: new targets start from the live value and keep its velocity. */
function spring({ from, to, velocity = 0, response = 0.35, always = false, onUpdate, onDone }) {
  if (reduceMotion.matches && !always) { onUpdate(to); onDone?.(); return { cancel() {}, value: () => to, velocity: () => 0 }; }
  const omega = (2 * Math.PI) / response;
  const a = from - to;
  const b = velocity + omega * a;
  const start = performance.now();
  let frame, x = from, v = velocity;
  const tick = (now) => {
    const t = (now - start) / 1000;
    const decay = Math.exp(-omega * t);
    x = to + (a + b * t) * decay;
    v = (b - omega * (a + b * t)) * decay;
    onUpdate(x);
    if (Math.abs(x - to) < 0.05 && Math.abs(v) < 1) { onUpdate(to); onDone?.(); return; }
    frame = requestAnimationFrame(tick);
  };
  frame = requestAnimationFrame(tick);
  return { cancel: () => cancelAnimationFrame(frame), value: () => x, velocity: () => v };
}

/* Past a boundary, movement resists progressively instead of stopping dead. */
function rubberband(overshoot, dimension = 120, constant = 0.55) {
  return (overshoot * dimension * constant) / (dimension + constant * Math.abs(overshoot));
}

function icon(name) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('class', 'icon');
  svg.setAttribute('viewBox', '0 0 16 16');
  svg.setAttribute('aria-hidden', 'true');
  svg.innerHTML = ICONS[name] ?? '';
  return svg;
}

const element = (tag, props = {}, ...children) => {
  const node = Object.assign(document.createElement(tag), props);
  node.append(...children);
  return node;
};

function toast(message) {
  const node = $('toast');
  node.textContent = message;
  node.hidden = false;
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => { node.hidden = true; }, 3200);
}

function confirmDialog(title, body, actions) {
  $('confirmTitle').textContent = title;
  $('confirmBody').textContent = body;
  $('confirmActions').replaceChildren(...actions.map(({ label, className, run }) =>
    element('button', { textContent: label, className: className ?? '', onclick: () => { $('confirm').close(); run?.(); } })));
  $('confirm').showModal();
}

/* Navigation */
let navSpring, navY = 0;
function moveIndicator(animate) {
  const active = document.querySelector('.nav-item[aria-current="page"]');
  const target = active.offsetTop;
  const indicator = $('navIndicator');
  const apply = (y) => { navY = y; indicator.style.transform = `translateY(${y}px)`; };
  const velocity = navSpring?.velocity() ?? 0;
  navSpring?.cancel();
  if (animate) navSpring = spring({ from: navY, to: target, velocity, response: 0.3, onUpdate: apply });
  else apply(target);
}

function showPage(page) {
  app.page = page;
  document.querySelectorAll('.page').forEach((node) => { node.hidden = node.id !== `page-${page}`; });
  document.querySelectorAll('.nav-item').forEach((node) => {
    if (node.dataset.page === page) node.setAttribute('aria-current', 'page');
    else node.removeAttribute('aria-current');
  });
  moveIndicator(true);
  send({ type: 'pausePlaylist', paused: page === 'editor' });
  if (page === 'settings') { send({ type: 'listFans' }); send({ type: 'listGpus' }); }
  if (page === 'editor') {
    if (!app.draft) openEditor(clone(app.current), app.currentPath);
    fitStage();
    renderHandles();
  }
}

const isDirty = () => app.draft && JSON.stringify(app.draft) !== app.savedDraft;

function requestPage(page) {
  if (page === app.page) return;
  if (page !== 'editor' && isDirty()) {
    confirmDialog('Save changes?', `“${app.draft.name}” has unsaved changes.`, [
      { label: 'Keep Editing' },
      { label: 'Discard', className: 'danger', run: () => { discardDraft(); showPage(page); } },
      { label: 'Save', className: 'primary', run: () => { saveDraft(); showPage(page); } },
    ]);
    return;
  }
  showPage(page);
}

function discardDraft() {
  app.draft = null;
  send({ type: 'select', path: app.currentPath });
}

/* Metrics page */
const statRows = new Map();
const formatMetric = (value, key) => value.toFixed(METRICS[key]?.digits ?? 0);

function ringGauge(key, compact) {
  const { label, short, bar } = METRICS[key];
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '0 0 120 120');
  svg.setAttribute('aria-hidden', 'true');
  const arc = (className) => {
    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    path.setAttribute('d', RING_ARC);
    path.setAttribute('pathLength', '100');
    path.setAttribute('class', className);
    svg.append(path);
    return path;
  };
  arc('ring-track');
  const ring = arc(`ring-fill ${bar}`);
  const value = element('dd');
  statRows.set(key, { value, ring, bare: compact }); // compact rings show their unit below the number
  return element('div', { className: 'ring', title: label }, svg, value, element('dt', { textContent: short ?? label }));
}

function readout(key) {
  const { label, short, bar } = METRICS[key];
  const value = element('dd');
  const row = element('div', { className: 'readout', title: label }, element('dt', { textContent: short ?? label }), value);
  const fill = bar ? element('span', { className: `stat-fill ${bar}` }) : null;
  if (fill) row.append(element('span', { className: 'stat-bar' }, fill));
  statRows.set(key, { value, fill });
  return row;
}

function buildPanel({ id, title, iconName, rings = [], readouts = [], compact }) {
  const panel = element('section', { className: 'metric-panel', tabIndex: 0 }, element('h3', {}, icon(iconName), title));
  panel.dataset.panel = id;
  panel.setAttribute('aria-label', `${title}. Drag to move it, or use the arrow keys.`);
  panel.addEventListener('pointerdown', (event) => beginPanelDrag(event, panel));
  panel.addEventListener('keydown', (event) => movePanelByKey(event, panel));
  if (id === 'pc') {
    panel.classList.add('specs');
    panel.hidden = true; // until the details arrive
    panel.append(element('dl', { className: 'spec-list', id: 'specs' }));
    return panel;
  }
  const body = element('div', { className: 'metric-body' });
  if (rings.length) body.append(element('dl', { className: `rings${compact ? ' compact' : ''}` }, ...rings.map((key) => ringGauge(key, compact))));
  if (readouts.length) body.append(element('dl', { className: 'readouts' }, ...readouts.map(readout)));
  panel.append(body);
  return panel;
}

const defaultPanelOrder = () => METRIC_PANELS.map(({ id }) => id);

function savedPanelOrder() {
  try { return JSON.parse(localStorage.getItem('metricPanels')) ?? []; } catch { return []; }
}

/* A panel added in a later version still shows up, after the ones already arranged. */
function arrangePanels(panels) {
  const ids = [...new Set([...savedPanelOrder(), ...defaultPanelOrder()])];
  $('metrics').replaceChildren(...ids.map((id) => panels.get(id)).filter(Boolean));
  showResetPanels();
}

function buildStats() {
  arrangePanels(new Map(METRIC_PANELS.map((spec) => [spec.id, buildPanel(spec)])));
}

const currentPanelOrder = () => [...$('metrics').children].map((panel) => panel.dataset.panel);

function showResetPanels() {
  $('resetPanels').hidden = currentPanelOrder().join() === defaultPanelOrder().join();
}

function savePanelOrder() {
  try { localStorage.setItem('metricPanels', JSON.stringify(currentPanelOrder())); } catch { /* the order lasts until the app closes */ }
  showResetPanels();
}

/* The moved tiles glide from where they were to where the change put them. */
function rearrangePanels(change) {
  const panels = [...$('metrics').children];
  const before = new Map(panels.map((panel) => [panel, { x: panel.offsetLeft, y: panel.offsetTop }]));
  change();
  if (reduceMotion.matches) return;
  panels.filter((panel) => !panel.hidden && !panel.classList.contains('dragging')).forEach((panel) => {
    const { x, y } = before.get(panel);
    const dx = x - panel.offsetLeft;
    const dy = y - panel.offsetTop;
    if (dx || dy) panel.animate([{ transform: `translate(${dx}px, ${dy}px)` }, { transform: 'none' }], PANEL_MOVE);
  });
}

function resetPanels() {
  try { localStorage.removeItem('metricPanels'); } catch { /* nothing was saved */ }
  rearrangePanels(() => {
    const panels = new Map([...$('metrics').children].map((panel) => [panel.dataset.panel, panel]));
    arrangePanels(panels);
  });
}

function movePanelBefore(panel, target) {
  const siblings = [...$('metrics').children];
  const after = siblings.indexOf(panel) < siblings.indexOf(target);
  $('metrics').insertBefore(panel, after ? target.nextSibling : target);
}

function movePanelByKey(event, panel) {
  const step = { ArrowLeft: -1, ArrowUp: -1, ArrowRight: 1, ArrowDown: 1 }[event.key];
  if (!step) return;
  event.preventDefault();
  const visible = [...$('metrics').children].filter((node) => !node.hidden);
  const target = visible[visible.indexOf(panel) + step];
  if (!target) return;
  rearrangePanels(() => movePanelBefore(panel, target));
  savePanelOrder();
  panel.focus();
}

/* The tile follows the pointer, and the others make room as it passes over them. */
function beginPanelDrag(event, panel) {
  if (event.button !== 0) return;
  const grid = $('metrics');
  const box = panel.getBoundingClientRect();
  const grab = { x: event.clientX - box.left, y: event.clientY - box.top };
  const start = { x: event.clientX, y: event.clientY };
  let dragging = false;
  let lastSwap = 0;

  const follow = (e) => {
    const home = grid.getBoundingClientRect();
    return { x: e.clientX - grab.x - home.left - panel.offsetLeft, y: e.clientY - grab.y - home.top - panel.offsetTop };
  };
  const place = (e) => {
    const { x, y } = follow(e);
    panel.style.transform = `translate(${x}px, ${y}px)`;
  };
  const panelUnder = (e) => {
    const home = grid.getBoundingClientRect();
    const x = e.clientX - home.left;
    const y = e.clientY - home.top;
    return [...grid.children].find((node) => node !== panel && !node.hidden
      && x >= node.offsetLeft && x < node.offsetLeft + node.offsetWidth
      && y >= node.offsetTop && y < node.offsetTop + node.offsetHeight);
  };

  const move = (e) => {
    if (!dragging) {
      if (Math.hypot(e.clientX - start.x, e.clientY - start.y) < PANEL_DRAG_START) return;
      dragging = true;
      panel.setPointerCapture(e.pointerId);
      panel.classList.add('dragging');
    }
    const now = performance.now();
    const target = panelUnder(e);
    if (target && now - lastSwap >= PANEL_SWAP_PAUSE_MS) {
      lastSwap = now; // full-width tiles change the layout, so a short pause stops them trading back and forth
      rearrangePanels(() => movePanelBefore(panel, target));
      panel.setPointerCapture(e.pointerId); // moving it in the page lets go of the pointer
    }
    place(e);
  };

  const end = (e) => {
    panel.removeEventListener('pointermove', move);
    panel.removeEventListener('pointerup', end);
    panel.removeEventListener('pointercancel', end);
    if (!dragging) return;
    const { x, y } = follow(e);
    panel.style.transform = '';
    panel.classList.remove('dragging');
    if (!reduceMotion.matches) panel.animate([{ transform: `translate(${x}px, ${y}px)` }, { transform: 'none' }], PANEL_MOVE);
    savePanelOrder();
  };

  panel.addEventListener('pointermove', move);
  panel.addEventListener('pointerup', end);
  panel.addEventListener('pointercancel', end);
}

function renderSpecs(rows) {
  $('specs').replaceChildren(...rows.map(({ label, values }) => element('div', {},
    element('dt', { textContent: label }),
    ...values.map((value) => element('dd', { textContent: value })))));
  document.querySelector('[data-panel="pc"]').hidden = rows.length === 0;
}

function paintGauge({ fill, ring }, percent) {
  if (fill) fill.style.clipPath = `inset(0 ${100 - percent}% 0 0 round 2px)`; // the color stays tied to the level
  if (ring) ring.style.strokeDasharray = `${percent} 100`;
}

/*
 * A new reading bends the moving gauge instead of restarting it, so it never pauses between readings.
 * This runs with Windows animations off too: a gauge's position is data, and it stays slow and small.
 */
function glideGauge(row, percent) {
  if (row.target === percent) return;
  row.target = percent;
  const velocity = row.motion?.velocity() ?? 0;
  row.motion?.cancel();
  row.motion = spring({
    from: row.percent ?? 0, to: percent, velocity, response: GAUGE_RESPONSE, always: true,
    onUpdate: (value) => { row.percent = value; paintGauge(row, value); },
  });
}

/* The Weather panel on the Themes page. */
function chooseWeatherPlace(place) {
  app.weatherSearching = null;
  $('weatherResults').replaceChildren();
  $('weatherQuery').value = '';
  app.weather = { ...app.weather, place: place.label, report: null, error: null };
  send({ type: 'weatherPlace', place, units: app.weather.units });
  renderWeatherPanel();
}

function minutesAgo(iso) {
  const minutes = Math.max(0, Math.round((Date.now() - Date.parse(iso)) / 60000));
  return minutes < 1 ? 'just now' : minutes === 1 ? '1 minute ago' : `${minutes} minutes ago`;
}

function renderWeatherPanel() {
  if (!$('weatherNow')) return;
  const { place, report, error, units } = app.weather;
  const lines = [element('strong', { textContent: place ?? 'No city chosen' })];
  if (!place) lines.push(element('span', { textContent: 'Search for your city below. Weather widgets show dashes until you choose one.' }));
  else if (report && !report.stale) {
    lines.push(element('span', { className: 'reading', textContent: `${report.temperature}° ${report.description}, H ${report.high}° L ${report.low}°` }),
      element('span', { textContent: `Updated ${minutesAgo(report.fetched)}` }));
  } else if (error) lines.push(element('span', { className: 'problem', textContent: `Couldn’t update the weather. TR Studio will try again in a few minutes.` }));
  else lines.push(element('span', { textContent: 'Getting the forecast…' }));
  $('weatherNow').replaceChildren(...lines);

  const options = { Metric: '°C and km/h', Imperial: '°F and mph' };
  const buttons = Object.entries(options).map(([value, label]) => {
    const button = element('button', {
      type: 'button', textContent: label,
      onclick: () => {
        if (app.weather.units === value) return;
        app.weather = { ...app.weather, units: value, report: null };
        send({ type: 'weatherUnits', units: value });
        renderWeatherPanel();
      },
    });
    button.setAttribute('aria-pressed', String(value === (units ?? 'Metric')));
    return button;
  });
  $('weatherUnits').replaceChildren(element('span', { textContent: 'Units' }), element('div', { className: 'segmented' }, ...buttons));
}

/* The sidebar player. The app reports only changes, so the position is worked out here between reports. */
function songPosition(song) {
  if (song.position == null || !song.duration) return null;
  const moved = song.playing ? (performance.now() - song.receivedAt) / 1000 : 0;
  return Math.min(song.duration, song.position + moved);
}

const songClock = (seconds) => {
  const whole = Math.floor(seconds);
  const pad = (value) => String(value).padStart(2, '0');
  return whole >= 3600 ? `${Math.floor(whole / 3600)}:${pad(Math.floor(whole / 60) % 60)}:${pad(whole % 60)}` : `${Math.floor(whole / 60)}:${pad(whole % 60)}`;
};

function renderNowPlaying() {
  const song = app.nowPlaying;
  $('nowPlaying').hidden = !song;
  if (!song) return;
  $('npArt').hidden = !song.art;
  if (song.art) $('npArt').src = song.art;
  $('npTitle').textContent = song.title;
  $('npTitle').title = song.title;
  $('npArtist').textContent = song.artist;
  const play = $('npPlay');
  play.replaceChildren(icon(song.playing ? 'Pause' : 'Play'));
  play.setAttribute('aria-label', song.playing ? 'Pause' : 'Play');
  play.disabled = !song.canPlayPause;
  $('npPrevious').disabled = !song.canPrevious;
  $('npNext').disabled = !song.canNext;
  renderSongProgress();
}

function renderSongProgress() {
  const song = app.nowPlaying;
  const position = song ? songPosition(song) : null;
  $('npProgress').hidden = position == null;
  if (position == null) return;
  $('npBar').style.transform = `scaleX(${position / song.duration})`;
  $('npTime').textContent = `${songClock(position)} / ${songClock(song.duration)}`;
}

/* Updated in place, so a number changes without rebuilding its gauge. */
function renderStats() {
  statRows.forEach((row, key) => {
    const { value, ring, bare } = row;
    const { unit, min, max } = METRICS[key];
    const reading = app.stats[key];
    const known = reading !== null && reading !== undefined;
    value.textContent = known ? `${formatMetric(reading, key)}${bare ? '' : unit}` : '–';
    value.className = known ? '' : 'empty';
    const share = known ? Math.min(1, Math.max(0, (reading - min) / (max - min))) : 0;
    glideGauge(row, share * 100);
    if (ring) ring.style.opacity = known ? 1 : 0;
  });
}

/*
 * Some readings never arrive on some PCs, so waiting for all of them could last forever.
 * Loading also ends once the set of readings stops growing, and at a hard limit if none come.
 */
const loading = { done: false, known: -1, since: 0 };

function trackLoading() {
  if (loading.done) return;
  const keys = Object.keys(METRICS);
  const known = keys.filter((key) => app.stats[key] !== null && app.stats[key] !== undefined).length;
  const now = performance.now();
  if (known !== loading.known) Object.assign(loading, { known, since: now });
  if (known === keys.length || (known > 0 && now - loading.since >= LOADING_SETTLE_MS)) finishLoading();
}

function finishLoading() {
  if (loading.done) return;
  loading.done = true;
  send({ type: 'refresh' }); // the theme cards were drawn before the readings arrived
  const node = $('loader');
  node.addEventListener('transitionend', () => { node.hidden = true; }, { once: true });
  node.classList.add('done');
  if (reduceMotion.matches) node.hidden = true;
}

/* Themes page */
function savePlaylist() {
  app.playlist.enabled &&= app.playlist.paths.length >= PLAYLIST.minThemes;
  send({ type: 'playlist', ...app.playlist });
  renderPlaylist();
  renderThemes();
}

function movePlaylistTheme(index, offset) {
  const { paths } = app.playlist;
  [paths[index], paths[index + offset]] = [paths[index + offset], paths[index]];
  savePlaylist();
}

function playlistButton(name, label, disabled, onclick) {
  const button = element('button', { title: label, disabled, onclick }, icon(name));
  button.setAttribute('aria-label', label);
  return button;
}

function playlistRow(path, index) {
  const { paths } = app.playlist;
  const theme = app.themes.find((entry) => entry.path === path);
  const row = element('li', { className: 'playlist-row' }, element('span', { className: 'playlist-number', textContent: index + 1 }));
  row.dataset.path = path ?? '';
  if (theme?.thumb) row.append(element('img', { className: 'playlist-thumb', src: `data:image/jpeg;base64,${theme.thumb}`, alt: '' }));
  else row.append(element('span', { className: 'playlist-thumb' }));
  row.append(
    element('span', { className: 'playlist-name', textContent: theme?.name ?? '' }),
    playlistButton('Up', `Move ${theme?.name} up`, index === 0, () => movePlaylistTheme(index, -1)),
    playlistButton('Down', `Move ${theme?.name} down`, index === paths.length - 1, () => movePlaylistTheme(index, 1)),
    playlistButton('Remove', `Remove ${theme?.name}`, false, () => { paths.splice(index, 1); savePlaylist(); }));
  return row;
}

const formatPlaylistTime = (seconds) => {
  const minutes = Math.floor(seconds / 60);
  if (!minutes) return `${seconds} s`;
  return seconds % 60 ? `${minutes} min ${seconds % 60} s` : `${minutes} min`;
};

function markShowingTheme() {
  document.querySelectorAll('.playlist-row, #themeGrid .theme-card').forEach((node) => {
    if (node.dataset.path === undefined) return;
    if (node.dataset.path === (app.currentPath ?? '')) node.setAttribute('aria-current', 'true');
    else node.removeAttribute('aria-current');
  });
}

function renderPlaylist() {
  const { enabled, paths, seconds } = app.playlist;
  const tooFew = paths.length < PLAYLIST.minThemes;
  $('playlistOn').checked = enabled;
  $('playlistOn').disabled = tooFew;
  $('playlistList').replaceChildren(...paths.map(playlistRow));
  if (tooFew) {
    $('playlistList').append(element('li', { className: 'playlist-empty', textContent: `Add ${PLAYLIST.minThemes} or more themes with Add Themes, or the + on a theme below. Their order here is the order they play.` }));
  }
  const left = app.themes.some((theme) => !paths.includes(theme.path));
  $('playlistAdd').textContent = left ? 'Add Themes…' : 'All Themes Added';
  $('playlistAdd').disabled = !left;
  $('playlistSeconds').value = seconds;
  $('playlistTime').textContent = formatPlaylistTime(seconds);
  markShowingTheme();
}

function togglePlaylistTheme(path) {
  const { paths } = app.playlist;
  const index = paths.indexOf(path);
  if (index >= 0) paths.splice(index, 1);
  else paths.push(path);
  savePlaylist();
}

/* Themes are picked by their picture, several at once, in the order they're clicked. */
const playlistPicks = [];

function renderPlaylistPicker() {
  const { paths } = app.playlist;
  const tiles = app.themes.map((theme) => {
    const added = paths.indexOf(theme.path);
    const picked = playlistPicks.indexOf(theme.path);
    const thumb = element('span', { className: 'capture-thumb theme-thumb' });
    if (theme.thumb) thumb.append(element('img', { src: `data:image/jpeg;base64,${theme.thumb}`, alt: '' }));
    const tile = element('button', {
      type: 'button',
      className: 'capture-card picker-card',
      disabled: added >= 0,
      title: added >= 0 ? `${theme.name} is already in the playlist` : theme.name,
      onclick: () => {
        if (picked >= 0) playlistPicks.splice(picked, 1);
        else playlistPicks.push(theme.path);
        renderPlaylistPicker();
        document.querySelector(`#playlistPicker [data-path="${CSS.escape(theme.path)}"]`)?.focus();
      },
    }, thumb, element('span', { className: 'capture-name', textContent: theme.name }),
    element('span', { className: 'capture-detail', textContent: added >= 0 ? 'Already in the playlist' : picked >= 0 ? 'Picked' : ' ' }));
    tile.dataset.path = theme.path ?? '';
    tile.setAttribute('aria-pressed', String(picked >= 0 || added >= 0));
    if (added >= 0) tile.append(element('span', { className: 'theme-order', textContent: added + 1 }));
    else if (picked >= 0) tile.append(element('span', { className: 'theme-order picked', textContent: paths.length + picked + 1 }));
    return tile;
  });
  $('playlistPicker').replaceChildren(...tiles);
  const count = playlistPicks.length;
  $('playlistPickerAdd').disabled = !count;
  $('playlistPickerAdd').textContent = count ? `Add ${count} Theme${count === 1 ? '' : 's'}` : 'Add Themes';
}

function openPlaylistPicker() {
  playlistPicks.length = 0;
  renderPlaylistPicker();
  $('playlistDialog').showModal();
  document.querySelector('#playlistPicker button:not(:disabled)')?.focus();
}

/* Warnings */
function saveAlertSettings() {
  send({ type: 'alerts', ...app.alertSettings });
}

function alertRule(rule) {
  const settings = app.alertSettings;
  const on = settings[rule.on];
  const input = element('input', { type: 'number', className: 'alert-limit', min: rule.min, max: rule.max, step: rule.step, value: settings[rule.limit], disabled: !on });
  input.setAttribute('aria-label', `${rule.label}: ${rule.limitLabel} (${rule.unit})`);
  const reset = element('button', {
    className: 'slider-reset', title: `Reset to ${rule.fallback} ${rule.unit}`, disabled: !on || settings[rule.limit] === rule.fallback,
    onclick: () => { settings[rule.limit] = rule.fallback; saveAlertSettings(); renderAlertRules(); },
  }, icon('Reset'));
  reset.setAttribute('aria-label', `Reset the ${rule.label} limit`);
  input.addEventListener('change', () => {
    const typed = Number(input.value);
    const value = input.value !== '' && Number.isFinite(typed) ? Math.round(Math.min(rule.max, Math.max(rule.min, typed))) : settings[rule.limit];
    input.value = value;
    if (value === settings[rule.limit]) return;
    settings[rule.limit] = value;
    saveAlertSettings();
    reset.disabled = value === rule.fallback;
  });
  input.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') input.blur();
    if (event.key === 'Escape') { input.value = settings[rule.limit]; input.blur(); }
  });
  const toggle = plainToggle(rule.label, on, (enabled) => { settings[rule.on] = enabled; saveAlertSettings(); renderAlertRules(); });
  toggle.classList.add('alert-switch');
  toggle.firstChild.prepend(icon(rule.iconName));
  const card = element('div', { className: 'alert-rule' }, toggle,
    element('div', { className: 'alert-limit-row' },
      element('span', { textContent: rule.limitLabel }),
      element('span', { className: 'alert-input' }, input, element('span', { className: 'slider-unit', textContent: rule.unit }), reset)),
    element('span', { className: 'alert-now' }));
  card.dataset.kind = rule.kind;
  card.classList.toggle('off', !on);
  return card;
}

function renderAlertRules() {
  $('alertRules').replaceChildren(...ALERT_RULES.map(alertRule));
  renderAlertReadings();
  markAlertRules();
}

/* A warning only works where the PC reports that reading, so each card says what it sees. */
function renderAlertReadings() {
  document.querySelectorAll('#alertRules .alert-rule').forEach((card) => {
    const rule = ALERT_RULES.find((entry) => entry.kind === card.dataset.kind);
    const reading = app.stats[rule.metric];
    card.querySelector('.alert-now').textContent = reading === null || reading === undefined
      ? 'No reading on this PC, so this warning can’t show'
      : `Now ${Math.round(reading).toLocaleString()} ${rule.unit}`;
  });
}

function markAlertRules() {
  document.querySelectorAll('#alertRules .alert-rule').forEach((card) => {
    card.classList.toggle('alerting', app.alerts.some((alert) => alert.kind === card.dataset.kind));
  });
}

function renderAlertBanner() {
  const banner = $('alertBanner');
  banner.hidden = app.alerts.length === 0;
  banner.replaceChildren(...app.alerts.map((alert) => element('p', {},
    icon('Warning'),
    element('span', {}, element('strong', { textContent: alert.title }), element('span', { textContent: alert.detail })))));
  markAlertRules();
}

/* Cooler fans. The speeds come again every couple of seconds while Settings is open. */
const FAN_ROLES = [
  { key: 'pump', label: 'AIO Pump', iconName: 'Fan' },
  { key: 'cpuFan', label: 'CPU Fan', iconName: 'Fan' },
  { key: 'caseFan', label: 'Case Fan', iconName: 'Fan' },
];
const NO_FAN = '';
const AUTOMATIC_FAN = 'auto';

const fanReading = (header) => (header?.rpm == null ? '—' : `${header.rpm.toLocaleString()} RPM`);

function fanOptionText(fans, value, role) {
  if (value === NO_FAN) return 'None';
  if (value === AUTOMATIC_FAN) {
    const guess = fans.headers.find((header) => header.id === fans[role.key].guess);
    return guess ? `Automatic · ${guess.name} · ${fanReading(guess)}` : 'Automatic · not found';
  }
  const header = fans.headers.find((entry) => entry.id === value);
  return `${header.name} · ${fanReading(header)}`;
}

function fanSelectValue(fans, role) {
  const picked = fans[role.key].picked;
  if (picked === NO_FAN) return NO_FAN;
  return picked && fans.headers.some((header) => header.id === picked) ? picked : AUTOMATIC_FAN;
}

function saveFans() {
  const choice = {};
  document.querySelectorAll('#fanRoles select').forEach((input) => {
    choice[input.dataset.role] = input.value === AUTOMATIC_FAN ? null : input.value;
  });
  send({ type: 'setFans', ...choice });
}

function renderFans() {
  const fans = app.fans;
  if (!fans) return;
  const list = $('fanRoles');
  const ids = fans.headers.map((header) => header.id).join('|');
  // Same headers as before: only the speeds change, so an open dropdown stays open.
  if (list.dataset.ids === ids && list.children.length) {
    list.querySelectorAll('select').forEach((input) => {
      const role = FAN_ROLES.find((entry) => entry.key === input.dataset.role);
      [...input.options].forEach((option) => { option.textContent = fanOptionText(fans, option.value, role); });
    });
    return;
  }
  list.dataset.ids = ids;
  if (!fans.headers.length) {
    list.replaceChildren(element('p', { className: 'alerts-intro', textContent: 'This PC doesn’t report any motherboard fan headers yet. Readings can take a few seconds after TR Studio starts.' }));
    return;
  }
  list.replaceChildren(...FAN_ROLES.map((role) => {
    const values = [AUTOMATIC_FAN, ...fans.headers.map((header) => header.id), NO_FAN];
    const input = element('select', {}, ...values.map((value) => element('option', { value, textContent: fanOptionText(fans, value, role) })));
    input.dataset.role = role.key;
    input.value = fanSelectValue(fans, role);
    input.setAttribute('aria-label', `${role.label} header`);
    input.addEventListener('change', saveFans);
    return element('div', { className: 'alert-rule fan-role' },
      element('span', { className: 'fan-role-name' }, icon(role.iconName), element('span', { textContent: role.label })),
      input);
  }));
}

/* Graphics card. Shown only when the PC has more than one GPU, such as a processor's built-in graphics and a graphics card. */
const AUTOMATIC_GPU = 'auto';

const gpuText = (gpu) => (gpu.memoryGb ? `${gpu.name} · ${gpu.memoryGb} GB` : gpu.name);

function renderGpus() {
  const state = app.gpus;
  if (!state) return;
  const panel = $('gpuPanel');
  panel.hidden = state.gpus.length < 2;
  if (panel.hidden) return;
  const list = $('gpuChoice');
  const automatic = state.gpus.find((gpu) => gpu.automatic);
  const value = state.gpus.some((gpu) => gpu.id === state.picked) ? state.picked : AUTOMATIC_GPU;
  const key = [value, ...state.gpus.map((gpu) => `${gpu.id}:${gpuText(gpu)}:${gpu.automatic}`)].join('|');
  if (list.dataset.key === key && list.children.length) return; // nothing changed, so an open dropdown stays open
  list.dataset.key = key;
  const options = [
    element('option', { value: AUTOMATIC_GPU, textContent: automatic ? `Automatic · ${gpuText(automatic)}` : 'Automatic' }),
    ...state.gpus.map((gpu) => element('option', { value: gpu.id, textContent: gpuText(gpu) })),
  ];
  const input = element('select', {}, ...options);
  input.value = value;
  input.setAttribute('aria-label', 'GPU to read');
  input.addEventListener('change', () => send({ type: 'setGpu', id: input.value === AUTOMATIC_GPU ? null : input.value }));
  list.replaceChildren(element('div', { className: 'alert-rule fan-role' },
    element('span', { className: 'fan-role-name' }, icon('GraphicsCard'), element('span', { textContent: 'GPU stats from' })),
    input));
}

function plainToggle(label, value, apply) {
  const input = element('input', { type: 'checkbox', checked: value });
  input.setAttribute('role', 'switch');
  input.addEventListener('change', () => apply(input.checked));
  return element('label', { className: 'switch' }, element('span', { textContent: label }), input);
}

function renderStartupOptions() {
  $('startupOptions').replaceChildren(plainToggle('Run at Windows Startup', app.startup, (enabled) => {
    app.startup = enabled;
    send({ type: 'startup', enabled });
  }));
}


function renderCrashOptions() {
  const children = [];
  if (app.crashReportsAvailable) {
    children.push(plainToggle('Send Crash Reports', app.crashReports === true, (enabled) => setCrashReports(enabled)));
  }
  $('crashOptions').replaceChildren(...children);
}

function setCrashReports(enabled) {
  app.crashReports = enabled;
  send({ type: 'crashReports', enabled });
  renderCrashOptions();
}

/* Asked once: nothing is sent until the user says yes. */
function askAboutCrashReports() {
  if (!app.crashReportsAvailable || app.crashReports != null || askAboutCrashReports.asked || $('confirm').open) return;
  askAboutCrashReports.asked = true;
  confirmDialog('Send crash reports?', 'If TR Studio crashes, it can send a report so the bug gets fixed. Reports hold the error, versions, cooler model and recent log lines, with your user name, PC name and folders taken out. You can change this in Settings.', [
    { label: 'No', run: () => setCrashReports(false) },
    { label: 'Send Reports', className: 'primary', run: () => setCrashReports(true) },
  ]);
}

function offerCrashReport() {
  const sent = app.crashReports === true ? ' A crash report was sent.' : '';
  confirmDialog('TR Studio closed unexpectedly', `Sorry about that.${sent} Reporting what you were doing helps get it fixed.`, [
    { label: 'Not Now' },
    { label: 'Report a Problem…', className: 'primary', run: () => send({ type: 'reportProblem' }) },
  ]);
}

/* Waits for the loading screen, so a prompt never pops up behind it. */
function afterLoading(run) {
  if (loading.done) run();
  else setTimeout(() => afterLoading(run), 500);
}

function confirmUpdate() {
  const update = app.update;
  confirmDialog(`Install TR Studio ${update.version}?`,
    'TR Studio closes, installs the update and opens again. The cooler screen goes dark for a few seconds. Your themes and settings stay.', [
      { label: 'Not Now' },
      { label: 'Install', className: 'primary', run: () => send({ type: 'installUpdate' }) },
    ]);
}

function updateSummary(update) {
  switch (update.state) {
    case 'checking': return 'Checking for updates…';
    case 'available': return `Version ${update.version} is ready to install.`;
    case 'downloading': return `Downloading version ${update.version}… ${update.progress}%`;
    case 'installing': return `Installing version ${update.version}. TR Studio opens again when it’s done.`;
    case 'upToDate': return 'You have the latest version.';
    case 'failed': return update.error ?? 'Couldn’t check for updates.';
    default: return '';
  }
}

function renderUpdate() {
  const update = app.update;
  if (!update) return;
  const busy = ['checking', 'downloading', 'installing'].includes(update.state);
  const offered = update.version && ['available', 'downloading', 'installing'].includes(update.state);
  const button = $('updateButton');
  button.hidden = !offered && !(update.state === 'failed' && update.version);
  button.disabled = busy;
  $('updateLabel').textContent = update.state === 'downloading' ? `Downloading ${update.progress}%`
    : update.state === 'installing' ? 'Installing…'
    : update.state === 'failed' ? 'Update failed. Try Again' : `Update to ${update.version}`;

  $('updateOptions').replaceChildren(plainToggle('Check for Updates Automatically', update.automatic, (enabled) => {
    update.automatic = enabled;
    send({ type: 'autoUpdate', enabled });
  }));
  const actions = element('div', { className: 'update-actions' },
    element('button', { type: 'button', textContent: 'Check Now', disabled: busy, onclick: () => send({ type: 'checkUpdate' }) }));
  if (update.version && (update.state === 'available' || update.state === 'failed')) {
    actions.append(element('button', { type: 'button', className: 'primary', textContent: `Install ${update.version}`, onclick: confirmUpdate }));
  }
  if (offered) {
    actions.append(element('button', { type: 'button', textContent: 'View Release Notes', title: 'Opens this version’s release page on GitHub', onclick: () => send({ type: 'releaseNotes', version: update.version }) }));
  }
  const summary = updateSummary(update);
  const children = [element('p', { className: 'update-version', textContent: `TR Studio ${update.current}${summary ? ` · ${summary}` : ''}` })];
  if (update.state === 'failed') children[0].classList.add('warning');
  children.push(actions);
  $('updateStatus').replaceChildren(...children);
}

function renderScreenOptions() {
  $('screenOptions').replaceChildren(
    plainToggle('Round Screen', isRound(), (round) => {
      app.screen = { ...app.screen, round };
      applyScreenShape();
      send({ type: 'roundScreen', round });
    }));
}

function renderRotation() {
  const square = app.screen.width === app.screen.height;
  const buttons = [0, 90, 180, 270].map((degrees) => {
    const button = element('button', {
      textContent: `${degrees}°`,
      disabled: !square && degrees % 180 !== 0, // a wide panel can't turn sideways
      onclick: () => {
        app.rotation = degrees;
        send({ type: 'rotate', degrees });
        renderRotation();
      },
    });
    button.setAttribute('aria-pressed', String(degrees === app.rotation));
    return button;
  });
  $('rotation').replaceChildren(
    element('span', { textContent: 'Screen Rotation' }),
    element('div', { className: 'segmented' }, ...buttons));
}

function renderThemes() {
  const cards = app.themes.map((theme) => {
    const thumb = element('span', { className: 'theme-thumb', textContent: theme.thumb ? '' : theme.kind });
    if (theme.thumb) thumb.append(element('img', { src: `data:image/jpeg;base64,${theme.thumb}`, alt: '' }));
    const open = element('button', {
      className: 'theme-open',
      title: `Show ${theme.name} on the screen`,
      onclick: () => { app.draft = null; send({ type: 'select', path: theme.path }); },
    }, thumb, element('span', { className: 'theme-name', textContent: theme.name }));

    const builtIn = isBuiltIn(theme.path);
    const actions = element('div', { className: 'theme-actions' });
    if (builtIn) actions.append(element('button', { textContent: 'Duplicate', onclick: () => duplicateTheme(theme) }));
    else {
      actions.append(
        element('button', { textContent: 'Edit', onclick: () => editTheme(theme) }),
        element('button', { textContent: 'Export', onclick: () => send({ type: 'export', path: theme.path, name: theme.name }) }));
    }
    actions.append(element('button', {
      textContent: 'Delete', className: 'danger',
      onclick: () => confirmDialog(`Delete “${theme.name}”?`, builtIn ? 'You can bring it back with Restore Default Themes.' : 'This can’t be undone.', [
        { label: 'Cancel' },
        { label: 'Delete', className: 'danger', run: () => send({ type: 'delete', path: theme.path }) },
      ]),
    }));
    const card = element('div', { className: 'theme-card' }, open, actions);
    card.dataset.path = theme.path ?? '';
    const order = app.playlist.paths.indexOf(theme.path);
    const label = order >= 0 ? `Remove ${theme.name} from the playlist` : `Add ${theme.name} to the playlist`;
    const badge = element('button', {
      type: 'button',
      className: order >= 0 ? 'theme-order' : 'theme-order add',
      title: order >= 0 ? `Number ${order + 1} in the playlist. Click to remove it.` : 'Add to the playlist',
      onclick: () => togglePlaylistTheme(theme.path),
    });
    if (order >= 0) badge.textContent = order + 1;
    else badge.append(icon('Add'));
    badge.setAttribute('aria-label', label);
    card.prepend(badge);
    return card;
  });
  const add = element('button', { className: 'theme-open', onclick: () => newTheme() },
    element('span', { className: 'theme-thumb', textContent: 'New Theme' }),
    element('span', { className: 'theme-name', textContent: ' ' }));
  $('themeGrid').replaceChildren(...cards, element('div', { className: 'theme-card' }, add));
  $('restoreDefaults').hidden = !app.removedDefaults;
  markShowingTheme();
}

function uniqueName(base) {
  const names = new Set(app.themes.map((theme) => theme.name));
  if (!names.has(base)) return base;
  for (let i = 2; ; i++) if (!names.has(`${base} ${i}`)) return `${base} ${i}`;
}

function newTheme() {
  const theme = clone(app.current);
  theme.name = uniqueName('My Theme');
  openEditor(theme, null);
  app.savedDraft = '';
  showPage('editor');
}

/* Shows the theme first, so the copy starts from it and not from whatever was on screen. */
function duplicateTheme(theme) {
  app.draft = null;
  app.pendingEdit = { duplicate: true };
  send({ type: 'select', path: theme.path });
}

function editTheme(theme) {
  app.pendingEdit = theme.path;
  send({ type: 'select', path: theme.path });
}

/* Editor */
function openEditor(theme, path) {
  app.draft = theme;
  app.draftPath = isBuiltIn(path) ? null : path; // a built-in theme saves as a new copy
  app.savedDraft = JSON.stringify(theme);
  app.selected = -1;
  app.selection = [];
  app.undo = [];
  app.redo = [];
  $('themeName').value = theme.name;
  renderInspector();
  renderHandles();
  renderLayers();
  updateHistoryButtons();
}

let previewQueued = false;
function pushPreview() {
  if (previewQueued) return;
  previewQueued = true;
  requestAnimationFrame(() => {
    previewQueued = false;
    if (app.draft) send({ type: 'preview', theme: app.draft });
  });
}

function remember() {
  app.undo.push(JSON.stringify(app.draft));
  app.redo = [];
  updateHistoryButtons();
}

function changed() {
  pushPreview();
  renderHandles();
  renderLayers();
  updateHistoryButtons();
}

function updateHistoryButtons() {
  $('undo').disabled = app.undo.length === 0;
  $('redo').disabled = app.redo.length === 0;
  $('save').disabled = !app.draft?.name.trim() || (!isDirty() && app.draftPath !== null);
  $('saveAs').hidden = !app.draftPath; // a new or built-in theme's Save already makes a new theme
}

function restore(from, to) {
  if (!from.length) return;
  to.push(JSON.stringify(app.draft));
  app.draft = JSON.parse(from.pop());
  app.selection = app.selection.filter((index) => index < app.draft.elements.length);
  app.selected = app.selection.includes(app.selected) ? app.selected : app.selection[app.selection.length - 1] ?? -1;
  $('themeName').value = app.draft.name;
  renderInspector();
  changed();
}

function saveDraft() {
  if (app.draftPath === null) {
    app.draft.name = uniqueName(app.draft.name.trim());
    $('themeName').value = app.draft.name;
  }
  app.savedDraft = JSON.stringify(app.draft);
  app.pendingEdit = 'saved';
  send({ type: 'save', theme: app.draft, path: app.draftPath });
  updateHistoryButtons();
}

function openSaveAs() {
  $('saveAsName').value = uniqueName(`${app.draft.name.trim()} Copy`);
  $('saveAsDialog').showModal();
  $('saveAsName').select();
}

/* The theme this came from keeps its last saved state, and editing carries on in the new one. */
function saveAs(name) {
  app.draft.name = name;
  app.draftPath = null;
  saveDraft();
}

const measure = document.createElement('canvas').getContext('2d');

/* Matches the .NET date tokens the engine uses, so examples read the same as the screen. */
function formatDate(date, format) {
  const pad = (value) => String(value).padStart(2, '0');
  const hour12 = date.getHours() % 12 || 12;
  const tokens = {
    yyyy: date.getFullYear(),
    MMMM: MONTHS[date.getMonth()],
    MMM: MONTHS[date.getMonth()].slice(0, 3),
    MM: pad(date.getMonth() + 1),
    dddd: DAYS[date.getDay()],
    ddd: DAYS[date.getDay()].slice(0, 3),
    dd: pad(date.getDate()),
    d: date.getDate(),
    HH: pad(date.getHours()),
    h: hour12,
    mm: pad(date.getMinutes()),
    ss: pad(date.getSeconds()),
    tt: date.getHours() < 12 ? 'AM' : 'PM',
  };
  return format.replace(/yyyy|MMMM|MMM|MM|dddd|ddd|dd|d|HH|h|mm|ss|tt/g, (token) => tokens[token]);
}

function textOf(item) {
  if (item.kind === 'Text') return item.text ?? '';
  if (item.kind === 'Clock' || item.kind === 'Date') return formatDate(new Date(), item.format ?? 'HH:mm');
  const value = app.stats[item.metric];
  return value === null || value === undefined ? '–' : `${formatMetric(value, item.metric)}${item.unit ?? ''}`;
}

/* Everything but text is placed by its center. Text goes by its edge. */
const isCentered = (item) => ['Gauge', 'Capture', 'Bar', 'Shape', 'Music', 'Weather'].includes(item.kind);

/* Mirrors ThemeRenderer.LayoutWeather. */
function weatherLayout(item) {
  const width = Math.max(item.size, 160);
  let y = width * 0.26;
  if (item.showCondition !== false) y += width * 0.03 + width * 0.062 * 1.3;
  if (item.showDetails !== false) y += width * 0.02 + width * 0.052 * 1.3 * 2;
  if (item.showForecast !== false) y += width * 0.05 + width * 0.05 * 1.3 * 2 + width * 0.12 + width * 0.01;
  return { left: item.x - width / 2, top: item.y - y / 2, width, height: y };
}

/* Mirrors ThemeRenderer.LayoutMusic, so the handle matches what the engine draws. */
function musicLayout(item) {
  const width = Math.max(item.size, 120);
  let y = 0;
  const art = item.showArt !== false ? width * 0.5 : 0;
  y += art;
  const anyText = item.showTitle !== false || item.showArtist !== false || item.showProgress !== false;
  if (art > 0 && anyText) y += width * 0.05;
  if (item.showTitle !== false) y += width * 0.075 * 1.3;
  if (item.showArtist !== false) y += width * 0.058 * 1.3;
  if (item.showProgress !== false) {
    if (y > 0) y += width * 0.03;
    const barRow = ['Bar', 'Line', 'Segments'].includes(progressStyleOf(item));
    if (barRow) y += Math.max(width * 0.018, 3) + width * 0.025;
    y += width * 0.045 * 1.3;
  }
  const height = Math.max(y, 1);
  return { left: item.x - width / 2, top: item.y - height / 2, width, height };
}

/* The upright box that fits around a turned one. */
function turnedBox(item, width, height) {
  const angle = (item.rotation ?? 0) * Math.PI / 180;
  const w = Math.max(Math.abs(Math.cos(angle)) * width + Math.abs(Math.sin(angle)) * height, MIN_HANDLE);
  const h = Math.max(Math.abs(Math.sin(angle)) * width + Math.abs(Math.cos(angle)) * height, MIN_HANDLE);
  return { left: item.x - w / 2, top: item.y - h / 2, width: w, height: h };
}

/* Mirrors ThemeRenderer.Pivot: text turns around its anchor halfway down its line, everything else around its center. */
const TEXT_LINE_SHARE = 1.35;
const isText = (item) => ['Metric', 'Text', 'Clock', 'Date'].includes(item.kind);
const textMiddle = (item) => Math.min(300, Math.max(8, item.size)) * TEXT_LINE_SHARE / 2;
const pivotOf = (item) => (isText(item) ? { x: item.x, y: item.y + textMiddle(item) } : { x: item.x, y: item.y });
/* Where x and y go so that the layer's pivot lands on a point. */
const placeAt = (item, point) => (isText(item) ? { x: point.x, y: point.y - textMiddle(item) } : { x: point.x, y: point.y });

/* Clockwise, as the screen's y axis points down. */
function turnPoint(point, center, degrees) {
  const angle = degrees * Math.PI / 180;
  const dx = point.x - center.x;
  const dy = point.y - center.y;
  return { x: center.x + dx * Math.cos(angle) - dy * Math.sin(angle), y: center.y + dx * Math.sin(angle) + dy * Math.cos(angle) };
}

function uprightBounds(item) {
  if (item.kind === 'Gauge') {
    return { left: item.x - item.size / 2, top: item.y - item.size / 2, width: item.size, height: item.size };
  }
  if (item.kind === 'Capture') {
    const height = item.size * item.captureHeight / item.captureWidth;
    return { left: item.x - item.size / 2, top: item.y - height / 2, width: item.size, height };
  }
  if (item.kind === 'Bar') return turnedBox(item, item.size, item.thickness);
  if (item.kind === 'Music') return musicLayout(item);
  if (item.kind === 'Weather') return weatherLayout(item);
  if (item.kind === 'Shape') {
    const heights = { Line: item.thickness, Arc: item.size };
    return turnedBox(item, item.size, heights[item.shape] ?? item.height);
  }
  const family = (item.fontFamily || DEFAULT_FONT).replaceAll('"', '');
  measure.font = `${item.italic ? 'italic ' : ''}${item.bold ? 600 : 400} ${item.size}px "${family}", sans-serif`;
  const text = textOf(item);
  const smallDegree = item.kind === 'Metric' && text.endsWith(DEGREE); // the engine draws this sign at half size
  const width = Math.max(measure.measureText(smallDegree ? text.slice(0, -1) : text || ' ').width + 8 + (smallDegree ? item.size * 0.28 : 0), 24);
  const height = item.size * TEXT_LINE_SHARE;
  const left = item.align === 'Right' ? item.x - width : item.align === 'Center' ? item.x - width / 2 : item.x;
  return { left, top: item.y, width, height };
}

/* The upright box around a layer as drawn. Bars and shapes already allow for their turn, and a gauge is round. */
function boundsOf(item) {
  const box = uprightBounds(item);
  const degrees = item.rotation ?? 0;
  if (['Bar', 'Shape', 'Gauge'].includes(item.kind) || degrees % 360 === 0) return box;
  const pivot = pivotOf(item);
  const corners = [[box.left, box.top], [box.left + box.width, box.top], [box.left, box.top + box.height], [box.left + box.width, box.top + box.height]]
    .map(([x, y]) => turnPoint({ x, y }, pivot, degrees));
  const left = Math.min(...corners.map((c) => c.x));
  const top = Math.min(...corners.map((c) => c.y));
  return { left, top, width: Math.max(...corners.map((c) => c.x)) - left, height: Math.max(...corners.map((c) => c.y)) - top };
}
/* The screen is round, so a box's corners are what get cut off first. */
function isOutsideCircle(item) {
  const box = boundsOf(item);
  const middle = SCREEN / 2;
  if (!isRound()) {
    const area = screenArea();
    return box.left < area.left || box.top < area.top
      || box.left + box.width > area.left + area.w || box.top + box.height > area.top + area.h;
  }
  if (item.kind === 'Gauge') return Math.hypot(item.x - middle, item.y - middle) + item.size / 2 > middle + 0.5;
  return [box.left, box.left + box.width].some((x) => [box.top, box.top + box.height]
    .some((y) => Math.hypot(x - middle, y - middle) > middle));
}

/* Only the ring is grabbable, so text inside a gauge stays clickable. */
function ringClip(item) {
  const outer = item.size / 2;
  const inner = Math.max(outer - item.thickness - 16, 1); // a generous band is easier to grab
  const circle = (radius) => `M${outer - radius},${outer}a${radius},${radius} 0 1,0 ${radius * 2},0a${radius},${radius} 0 1,0 ${-radius * 2},0Z`;
  return `path(evenodd, "${circle(outer)}${circle(inner)}")`;
}

function renderHandles() {
  if (!app.draft) return;
  const handles = app.draft.elements.map((item, index) => {
    const handle = element('div', { className: `handle${item.kind === 'Gauge' ? ' gauge' : ''}` });
    handle.addEventListener('pointerdown', (event) => beginDrag(event, index, handle));
    handle.addEventListener('dblclick', () => setSelection([index], index)); // picks one layer out of its group
    handle.addEventListener('contextmenu', (event) => openLayerMenu(event, index));
    return handle;
  });
  $('handles').replaceChildren(...handles, selectionBox());
  placeHandles();
}

/* Moves the handles in place, so a knob being dragged keeps its pointer. */
function placeHandles() {
  if (!app.draft) return;
  const area = screenArea();
  const nodes = $('handles').querySelectorAll('.handle');
  app.draft.elements.forEach((item, index) => {
    const node = nodes[index];
    if (!node) return;
    const box = boundsOf(item);
    node.hidden = Boolean(item.hidden); // kept in the list, so handles still line up with the items
    node.style.cssText = `left:${box.left - area.left}px;top:${box.top - area.top}px;width:${box.width}px;height:${box.height}px`;
    if (item.kind === 'Gauge') node.style.clipPath = ringClip(item);
    node.classList.toggle('selected', app.selection.includes(index));
    node.classList.toggle('outside', isOutsideCircle(item));
  });
  placeSelectionBox();
}

let returnSprings = [];

const centerOf = (item) => {
  const box = boundsOf(item);
  return { x: box.left + box.width / 2, y: box.top + box.height / 2 };
};

/*
 * Selection. app.selection holds every selected layer; app.selected is the one the Inspector shows, or -1 for the
 * background. A group is the layers sharing a group name. Clicking one on the screen picks the whole group.
 */
const selectedItems = () => app.selection.map((index) => app.draft.elements[index]).filter(Boolean);
const groupOf = (index) => app.draft?.elements[index]?.group || null;
const membersOf = (group) => app.draft.elements.flatMap((item, index) => (item.group === group ? [index] : []));
const withGroups = (indices) => [...new Set(indices.flatMap((index) => (groupOf(index) ? membersOf(groupOf(index)) : [index])))];
const isWholeGroup = (indices) => indices.length > 1 && Boolean(groupOf(indices[0]))
  && indices.every((index) => groupOf(index) === groupOf(indices[0])) && membersOf(groupOf(indices[0])).length === indices.length;

function setSelection(indices, primary = indices[indices.length - 1]) {
  const count = app.draft?.elements.length ?? 0;
  app.selection = [...new Set(indices)].filter((index) => index >= 0 && index < count).sort((a, b) => a - b);
  app.selected = app.selection.length === 0 ? -1 : app.selection.includes(primary) ? primary : app.selection[app.selection.length - 1];
  renderInspector();
  markSelection();
}

function select(index) {
  if (app.selected === index && app.selection.length === (index < 0 ? 0 : 1)) return;
  setSelection(index < 0 ? [] : [index], index);
}

/* Updated in place: rebuilding the list here would destroy a row while it is being pressed. */
function markSelection() {
  document.querySelectorAll('.layer').forEach((row) => {
    const indices = row.dataset.indices.split(',').map(Number);
    const selected = indices[0] === -1 ? app.selection.length === 0 : indices.every((index) => app.selection.includes(index));
    row.setAttribute('aria-selected', String(selected));
  });
  placeHandles();
}

const isAdditive = (event) => event.ctrlKey || event.shiftKey || event.metaKey;

/* Drags every selected layer together. One layer alone also snaps and springs back inside the screen. */
function beginDrag(event, index, handle) {
  if (event.button !== 0) return; // right-click opens the menu instead
  event.stopPropagation();
  event.preventDefault();
  closeLayerMenu();
  returnSprings.forEach((s) => s.cancel());
  returnSprings = [];
  const picked = withGroups([index]);
  if (isAdditive(event)) {
    const on = picked.every((i) => app.selection.includes(i));
    setSelection(on ? app.selection.filter((i) => !picked.includes(i)) : [...app.selection, ...picked], on ? undefined : index);
    return;
  }
  if (!app.selection.includes(index)) setSelection(picked, index);
  $('stage').focus?.();
  const moving = selectedItems().filter((item) => !item.hidden);
  if (!moving.length) return;
  const single = moving.length === 1 ? moving[0] : null;
  const area = screenArea();
  const scale = $('stage').getBoundingClientRect().width / area.w;
  const grab = { x: event.clientX, y: event.clientY };
  const starts = moving.map((item) => ({ item, x: item.x, y: item.y }));
  const startBox = selectionBounds(moving);
  const startCenter = { x: startBox.left + startBox.width / 2, y: startBox.top + startBox.height / 2 };
  const before = JSON.stringify(app.draft);
  let moved = false;
  handle.setPointerCapture(event.pointerId);
  handle.classList.add('dragging');

  const place = (item, x, y) => {
    // These can be nearly screen-sized, so edge resistance would leave them almost no room to move.
    if (isCentered(item)) {
      return {
        x: Math.min(area.left + area.w, Math.max(area.left, x)),
        y: Math.min(area.top + area.h, Math.max(area.top, y)),
      };
    }
    const box = boundsOf({ ...item, x, y });
    const over = (value, low, high) => (value < low ? low + rubberband(value - low) - value : value > high ? high + rubberband(value - high) - value : 0);
    return {
      x: x + over(box.left, area.left, area.left + area.w - box.width),
      y: y + over(y, area.top, area.top + area.h - box.height),
    };
  };

  /* Layers snap to the screen's center and to each other's centers, on both axes. Alt turns it off. */
  const others = app.draft.elements.filter((other) => !moving.includes(other) && !other.hidden).map(centerOf);
  const targetsX = [SCREEN / 2, ...others.map((c) => c.x)];
  const targetsY = [SCREEN / 2, ...others.map((c) => c.y)];
  const snapTo = (value, targets, off) => {
    if (off) return null;
    const nearest = targets.reduce((best, target) => (Math.abs(target - value) < Math.abs(best - value) ? target : best));
    return Math.abs(nearest - value) * scale <= SNAP_DISTANCE ? nearest : null;
  };
  const showGuide = (id, css, position) => {
    $(id).hidden = position === null;
    if (position !== null) $(id).style[css] = `${position}px`;
  };

  const move = (e) => {
    if (!moved && Math.hypot(e.clientX - grab.x, e.clientY - grab.y) < 3) return;
    moved = true;
    let dx = (e.clientX - grab.x) / scale;
    let dy = (e.clientY - grab.y) / scale;
    const center = single ? centerOf({ ...single, x: starts[0].x + dx, y: starts[0].y + dy }) : { x: startCenter.x + dx, y: startCenter.y + dy };
    const snapX = snapTo(center.x, targetsX, e.altKey);
    const snapY = snapTo(center.y, targetsY, e.altKey);
    showGuide('guide', 'left', snapX === null ? null : snapX - area.left);
    showGuide('guideH', 'top', snapY === null ? null : snapY - area.top);
    dx += snapX === null ? 0 : snapX - center.x;
    dy += snapY === null ? 0 : snapY - center.y;
    if (single) {
      const next = place(single, starts[0].x + dx, starts[0].y + dy);
      single.x = Math.round(next.x);
      single.y = Math.round(next.y);
    } else {
      // A group keeps its middle on the screen, so it can always be dragged back.
      dx = Math.min(area.left + area.w, Math.max(area.left, startCenter.x + dx)) - startCenter.x;
      dy = Math.min(area.top + area.h, Math.max(area.top, startCenter.y + dy)) - startCenter.y;
      starts.forEach((start) => {
        start.item.x = Math.round(start.x + dx);
        start.item.y = Math.round(start.y + dy);
      });
    }
    placeHandles();
    pushPreview();
  };

  const end = () => {
    handle.removeEventListener('pointermove', move);
    handle.removeEventListener('pointerup', end);
    handle.removeEventListener('pointercancel', end);
    handle.classList.remove('dragging');
    $('guide').hidden = true;
    $('guideH').hidden = true;
    if (!moved) return;
    app.undo.push(before);
    app.redo = [];
    if (single && !isCentered(single)) settleInside(single);
    updateHistoryButtons();
    renderInspector();
  };

  handle.addEventListener('pointermove', move);
  handle.addEventListener('pointerup', end);
  handle.addEventListener('pointercancel', end);
}

/* The box around the selection, with corners that resize it and a round knob above that rotates it. */
const RESIZE_CORNERS = [['nw', -1, -1], ['ne', 1, -1], ['sw', -1, 1], ['se', 1, 1]];

function selectionBox() {
  const box = element('div', { className: 'selection-box', id: 'selectionBox' });
  box.hidden = true;
  const turn = element('div', { className: 'selection-turn', title: 'Drag to rotate. Hold Shift for 15° steps.' });
  turn.addEventListener('pointerdown', (event) => beginTransform(event, 'rotate'));
  box.append(element('div', { className: 'selection-stem' }), turn, ...RESIZE_CORNERS.map(([name, sx, sy]) => {
    const knob = element('div', { className: `selection-corner ${name}`, title: 'Drag to resize. Hold Alt to resize from the middle.' });
    knob.addEventListener('pointerdown', (event) => beginTransform(event, 'resize', { sx, sy }));
    return knob;
  }));
  return box;
}

function selectionBounds(items = selectedItems().filter((item) => !item.hidden)) {
  if (!items.length) return null;
  const boxes = items.map(boundsOf);
  const left = Math.min(...boxes.map((b) => b.left));
  const top = Math.min(...boxes.map((b) => b.top));
  return {
    left, top,
    width: Math.max(...boxes.map((b) => b.left + b.width)) - left,
    height: Math.max(...boxes.map((b) => b.top + b.height)) - top,
  };
}

function placeSelectionBox() {
  const node = $('selectionBox');
  if (!node) return;
  const bounds = selectionBounds();
  node.hidden = !bounds;
  if (!bounds) return;
  const area = screenArea();
  node.style.cssText = `left:${bounds.left - area.left}px;top:${bounds.top - area.top}px;width:${bounds.width}px;height:${bounds.height}px`;
  node.classList.toggle('many', app.selection.length > 1);
}

/* Everything that sets a layer's size grows with it, so a resized group keeps its look. */
const SCALED_FIELDS = ['thickness', 'height', 'cornerRadius', 'strokeWidth', 'shadowX', 'shadowY', 'shadowBlur'];
const MIN_SCALE = 0.05;

function scaledFields(saved, factor) {
  const fields = { size: Math.max(4, Math.round(saved.size * factor)) };
  SCALED_FIELDS.forEach((key) => {
    if (typeof saved[key] === 'number') fields[key] = Math.round(saved[key] * factor * 10) / 10;
  });
  return fields;
}

/* Every change is worked out from where the drag began, so nothing drifts over a long drag. */
function beginTransform(event, mode, corner) {
  if (event.button !== 0) return;
  event.stopPropagation();
  event.preventDefault();
  closeLayerMenu();
  const items = selectedItems().filter((item) => !item.hidden);
  if (!items.length) return;
  const knob = event.currentTarget;
  knob.setPointerCapture(event.pointerId);
  const area = screenArea();
  const stageBox = $('stage').getBoundingClientRect();
  const scale = stageBox.width / area.w;
  const toTheme = (e) => ({ x: area.left + (e.clientX - stageBox.left) / scale, y: area.top + (e.clientY - stageBox.top) / scale });
  const box = selectionBounds(items);
  const center = { x: box.left + box.width / 2, y: box.top + box.height / 2 };
  const starts = items.map((item) => ({ item, saved: clone(item), pivot: pivotOf(item) }));
  const before = JSON.stringify(app.draft);
  const grab = toTheme(event);
  const startAngle = Math.atan2(grab.y - center.y, grab.x - center.x);
  let moved = false;
  $('stage').classList.add('transforming', mode);

  const resize = (e) => {
    const point = toTheme(e);
    const fromMiddle = e.altKey;
    const anchor = fromMiddle ? center : {
      x: corner.sx < 0 ? box.left + box.width : box.left,
      y: corner.sy < 0 ? box.top + box.height : box.top,
    };
    const reachX = Math.max(fromMiddle ? box.width / 2 : box.width, 1);
    const reachY = Math.max(fromMiddle ? box.height / 2 : box.height, 1);
    const factor = Math.max(MIN_SCALE, (point.x - anchor.x) * corner.sx / reachX, (point.y - anchor.y) * corner.sy / reachY);
    starts.forEach(({ item, saved, pivot }) => {
      Object.assign(item, scaledFields(saved, factor));
      const at = placeAt(item, { x: anchor.x + (pivot.x - anchor.x) * factor, y: anchor.y + (pivot.y - anchor.y) * factor });
      item.x = Math.round(at.x);
      item.y = Math.round(at.y);
    });
  };

  const rotate = (e) => {
    const point = toTheme(e);
    let degrees = (Math.atan2(point.y - center.y, point.x - center.x) - startAngle) * 180 / Math.PI;
    if (e.shiftKey) {
      // One layer snaps its own angle; a group snaps how far it has turned.
      const own = starts.length === 1 ? starts[0].saved.rotation ?? 0 : 0;
      degrees = Math.round((own + degrees) / 15) * 15 - own;
    }
    degrees = Math.round(degrees);
    starts.forEach(({ item, saved, pivot }) => {
      item.rotation = ((((saved.rotation ?? 0) + degrees) % 360) + 360) % 360;
      const at = placeAt(item, turnPoint(pivot, center, degrees));
      item.x = Math.round(at.x);
      item.y = Math.round(at.y);
    });
  };

  const move = (e) => {
    moved = true;
    (mode === 'rotate' ? rotate : resize)(e);
    placeHandles();
    pushPreview();
  };

  const end = () => {
    knob.removeEventListener('pointermove', move);
    knob.removeEventListener('pointerup', end);
    knob.removeEventListener('pointercancel', end);
    $('stage').classList.remove('transforming', mode);
    if (!moved) return;
    app.undo.push(before);
    app.redo = [];
    updateHistoryButtons();
    renderInspector();
    renderHandles();
  };

  knob.addEventListener('pointermove', move);
  knob.addEventListener('pointerup', end);
  knob.addEventListener('pointercancel', end);
}

/* Dragging across empty screen selects every layer the box touches. A click there selects the background. */
function beginMarquee(event) {
  if (event.button !== 0 || !app.draft) return;
  closeLayerMenu();
  const stage = $('stage');
  const area = screenArea();
  const stageBox = stage.getBoundingClientRect();
  const scale = stageBox.width / area.w;
  const toLocal = (e) => ({ x: (e.clientX - stageBox.left) / scale, y: (e.clientY - stageBox.top) / scale });
  const start = toLocal(event);
  const additive = isAdditive(event);
  const kept = additive ? [...app.selection] : [];
  const marquee = $('marquee');
  let moved = false;
  stage.setPointerCapture(event.pointerId);

  const move = (e) => {
    const point = toLocal(e);
    if (!moved && Math.hypot(point.x - start.x, point.y - start.y) * scale < 3) return;
    moved = true;
    const left = Math.min(point.x, start.x);
    const top = Math.min(point.y, start.y);
    const width = Math.abs(point.x - start.x);
    const height = Math.abs(point.y - start.y);
    marquee.hidden = false;
    marquee.style.cssText = `left:${left}px;top:${top}px;width:${width}px;height:${height}px`;
    const hits = app.draft.elements.flatMap((item, index) => {
      if (item.hidden) return [];
      const box = boundsOf(item);
      const boxLeft = box.left - area.left;
      const boxTop = box.top - area.top;
      return boxLeft < left + width && boxLeft + box.width > left && boxTop < top + height && boxTop + box.height > top ? [index] : [];
    });
    const next = [...new Set([...kept, ...withGroups(hits)])].sort((a, b) => a - b);
    if (next.join() !== app.selection.join()) setSelection(next);
  };

  const end = () => {
    stage.removeEventListener('pointermove', move);
    stage.removeEventListener('pointerup', end);
    stage.removeEventListener('pointercancel', end);
    marquee.hidden = true;
    if (!moved && !additive) setSelection([]);
  };

  stage.addEventListener('pointermove', move);
  stage.addEventListener('pointerup', end);
  stage.addEventListener('pointercancel', end);
}

/* Groups. The name doubles as the group's id, so it stays the same however the layers move. */
function newGroupName() {
  const used = new Set(app.draft.elements.map((item) => item.group).filter(Boolean));
  let number = 1;
  while (used.has(`Group ${number}`)) number++;
  return `Group ${number}`;
}

/* A group of one isn't a group. */
function cleanGroups() {
  const counts = {};
  app.draft.elements.forEach((item) => { if (item.group) counts[item.group] = (counts[item.group] ?? 0) + 1; });
  app.draft.elements.forEach((item) => { if (item.group && counts[item.group] < 2) delete item.group; });
}

/* The members move next to each other in the stack, where the topmost one was, so the group reads as one layer. */
function groupSelection() {
  const indices = app.selection;
  if (indices.length < 2 || isWholeGroup(indices)) return;
  remember();
  const items = app.draft.elements;
  const chosen = indices.map((index) => items[index]);
  const top = Math.max(...indices);
  const below = items.filter((item, index) => index < top && !indices.includes(index)).length;
  const rest = items.filter((item, index) => !indices.includes(index));
  const name = newGroupName();
  chosen.forEach((item) => { item.group = name; });
  app.draft.elements = [...rest.slice(0, below), ...chosen, ...rest.slice(below)];
  cleanGroups();
  app.selection = chosen.map((item) => app.draft.elements.indexOf(item));
  app.selected = app.selection[app.selection.length - 1];
  renderInspector();
  changed();
}

function ungroupSelection() {
  const items = selectedItems().filter((item) => item.group);
  if (!items.length) return;
  remember();
  items.forEach((item) => { delete item.group; });
  cleanGroups();
  renderInspector();
  changed();
}

/* Right-click on a layer, on the screen or in the list. */
function openLayerMenu(event, index) {
  event.preventDefault();
  event.stopPropagation();
  if (!app.draft) return;
  if (index !== undefined && !app.selection.includes(index)) setSelection(withGroups([index]), index);
  const indices = app.selection;
  const entry = (label, shortcut, enabled, run) => {
    const button = element('button', { type: 'button', disabled: !enabled, onclick: () => { closeLayerMenu(); run(); } },
      element('span', { textContent: label }), element('kbd', { textContent: shortcut }));
    button.setAttribute('role', 'menuitem');
    return button;
  };
  const menu = $('layerMenu');
  menu.replaceChildren(
    entry('Group', 'Ctrl+G', indices.length > 1 && !isWholeGroup(indices), groupSelection),
    entry('Ungroup', 'Ctrl+Shift+G', indices.some((i) => groupOf(i)), ungroupSelection),
    element('hr'),
    entry('Delete', 'Del', indices.length > 0, removeSelected));
  menu.hidden = false;
  const box = menu.getBoundingClientRect();
  menu.style.left = `${Math.min(event.clientX, innerWidth - box.width - 8)}px`;
  menu.style.top = `${Math.min(event.clientY, innerHeight - box.height - 8)}px`;
  menu.querySelector('button:not(:disabled)')?.focus();
}

function closeLayerMenu() {
  const menu = $('layerMenu');
  if (menu && !menu.hidden) menu.hidden = true;
}
/* A dragged element released past an edge springs back inside. */
function settleInside(item) {
  const box = boundsOf(item);
  const area = screenArea();
  const shiftX = Math.max(0, area.left - box.left) - Math.max(0, box.left + box.width - area.left - area.w);
  const shiftY = Math.max(0, area.top - box.top) - Math.max(0, box.top + box.height - area.top - area.h);
  const animate = (axis, shift) => {
    if (!shift) return;
    const from = item[axis];
    returnSprings.push(spring({
      from, to: from + shift, response: 0.3,
      onUpdate: (value) => {
        item[axis] = Math.round(value);
        placeHandles();
        pushPreview();
      },
      onDone: updateHistoryButtons,
    }));
  };
  animate('x', shiftX);
  animate('y', shiftY);
}
function addElement(spec) {
  remember();
  app.draft.elements.push({
    x: 240, y: 200, size: NEW_TEXT_SIZE, color: '#FFFFFF', align: 'Center', bold: false, italic: false,
    fontFamily: DEFAULT_FONT, format: 'HH:mm', metric: 'CpuTemp',
    stroke: false, strokeColor: '#000000', strokeWidth: 2,
    shadow: false, shadowColor: '#000000', shadowX: 2, shadowY: 2, shadowBlur: 6,
    ...spec,
  });
  app.selected = app.draft.elements.length - 1;
  app.selection = [app.selected];
  renderInspector();
  changed();
}

function removeSelected() {
  if (!app.selection.length) return;
  remember();
  [...app.selection].sort((a, b) => b - a).forEach((index) => app.draft.elements.splice(index, 1));
  cleanGroups();
  app.selection = [];
  app.selected = -1;
  renderInspector();
  changed();
}
function field(label, control, valueNode) {
  const title = element('span', { textContent: label });
  if (valueNode) title.append(valueNode);
  return element('label', { className: 'field' }, title, control);
}

const SLIDER_DECIMALS = 2;
const tidyNumber = (value) => Number(value.toFixed(SLIDER_DECIMALS));

/*
 * Every slider records one undo step per gesture, not one per pixel. Its value can also be typed,
 * past the slider's own steps but not its range, and Reset puts back what a new item starts with.
 */
function slider(label, value, { min, max, step, unit = '', reset }, apply) {
  let current = tidyNumber(value);
  const range = element('input', { type: 'range', min, max, step, value: current });
  const number = element('input', { type: 'number', className: 'slider-number', min, max, step: 'any', value: current });
  const resetButton = element('button', { type: 'button', className: 'slider-reset', title: `Reset to ${reset}${unit}` }, icon('Reset'));
  range.setAttribute('aria-label', label);
  number.setAttribute('aria-label', `${label}${unit ? ` in ${unit.trim()}` : ''}`);
  resetButton.setAttribute('aria-label', `Reset ${label} to ${reset}${unit}`);

  const show = (next) => {
    current = tidyNumber(next);
    range.value = current;
    number.value = current;
    resetButton.disabled = current === reset;
  };
  const set = (next) => {
    show(next);
    apply(current);
    changed();
  };
  show(current);

  range.addEventListener('pointerdown', remember);
  range.addEventListener('keydown', remember, { once: true });
  range.addEventListener('input', () => set(Number(range.value)));
  number.addEventListener('change', () => {
    const typed = Number(number.value);
    if (number.value.trim() === '' || !Number.isFinite(typed)) { show(current); return; }
    const next = tidyNumber(Math.min(max, Math.max(min, typed)));
    if (next === current) { show(current); return; }
    remember();
    set(next);
  });
  number.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') number.blur();
    if (event.key === 'Escape') { show(current); number.blur(); event.stopPropagation(); }
  });
  resetButton.addEventListener('click', () => {
    remember();
    set(reset);
  });

  const readout = element('span', { className: 'slider-value' }, number);
  if (unit) readout.append(element('span', { className: 'slider-unit', textContent: unit.trim() }));
  readout.append(resetButton);
  return element('div', { className: 'field' }, element('span', { textContent: label }, readout), range);
}

function colorField(label, value, apply) {
  let current = (value ?? '#FFFFFF').slice(0, 7).toUpperCase();
  const swatch = element('span', { className: 'swatch' });
  const text = element('span', { textContent: current });
  swatch.style.background = current;
  const button = element('button', {
    className: 'color-button',
    onclick: () => openPicker(button, current, (hex) => {
      current = hex;
      swatch.style.background = hex;
      text.textContent = hex;
      apply(hex);
      changed();
    }, label),
  }, swatch, text);
  button.dataset.color = label;
  button.setAttribute('aria-label', `${label}: ${current}`);
  return element('div', { className: 'field' }, element('span', { textContent: label }), button);
}

/* Color picker. It lives in the page, so it behaves the same in any window state or rights level. */
const PALETTES = [
  { name: 'Essentials', colors: ['#FFFFFF', '#98989D', '#000000', '#FF3B30', '#FF9F0A', '#FFD60A', '#30D158', '#22E5E8', '#0A84FF', '#A855F7'] },
  { name: 'Neon', colors: ['#FF00E5', '#FF2D92', '#FF3131', '#FF9100', '#FFEA00', '#39FF14', '#00FFC8', '#00E5FF', '#2979FF', '#B000FF'] },
  { name: 'Sunset', colors: ['#2D1B4E', '#5B2A86', '#9C2F8F', '#D1387A', '#F0525F', '#F97B4F', '#FBA346', '#FDC85C', '#FFE5A0', '#FFF4DC'] },
  { name: 'Ocean', colors: ['#03122B', '#06305C', '#0A4F8C', '#0F74B8', '#1C9AD6', '#3FBFE3', '#6FD8E8', '#A4EBEE', '#D5F7F5', '#2FD6B5'] },
  { name: 'Forest', colors: ['#0F1F14', '#1E3A26', '#2F5A36', '#467D45', '#67A04F', '#91BF60', '#C2D97F', '#E8EBAE', '#8A6B3D', '#5A4228'] },
  { name: 'Ember', colors: ['#1A0602', '#4A0E04', '#8A1A06', '#C72E0A', '#EE4B12', '#FF7A1A', '#FFA62B', '#FFCC4D', '#FFE699', '#FFF6D9'] },
  { name: 'Aurora', colors: ['#071A2B', '#0C3B4F', '#0E6A66', '#14A37F', '#3EDB8F', '#9BF59A', '#D2FF8E', '#6E5BFF', '#A855F7', '#E879F9'] },
  { name: 'Pastel', colors: ['#FFB3BA', '#FFCFB3', '#FFE7B3', '#FFFAB3', '#C9F2C7', '#B3EBDD', '#B3E5FF', '#BCCBFF', '#D8BFFF', '#F5C2E7'] },
  { name: 'Retro', colors: ['#2B2D42', '#EF476F', '#F78C6B', '#FFD166', '#06D6A0', '#118AB2', '#073B4C', '#F4EBD0', '#D6AD60', '#B6452C'] },
  { name: 'Mono', colors: ['#000000', '#1C1C1F', '#38383C', '#55555A', '#737378', '#919196', '#AEAEB2', '#CBCBCF', '#E5E5EA', '#FFFFFF'] },
];
const MAX_PALETTE_COLORS = 20;
const MIN_STAT_CONTRAST = 4.5;
const BLEND_FIELDS = new Set(['Color', 'Low Color', 'High Color']);
const BLEND_HUE_SHIFT = 28;
const GRAY_SATURATION = 0.12;
const MUTED_SATURATION = 0.25;
const TRACK_TINT = 0.18;
const READABLE_STEP = 0.05;
const FAMILY_LABELS = [['Cpu', /\bCPU\b/i], ['Gpu', /\bGPU\b/i], ['Memory', /\b(MEMORY|RAM)\b/i], ['Cooling', /\b(PUMP|FANS?)\b/i]];
const picker = { apply: null, anchor: null, h: 0, s: 0, v: 0, remembered: false };

function recentColors() {
  try { return JSON.parse(localStorage.getItem('recentColors')) ?? []; } catch { return []; }
}

function saveRecentColor(hex) {
  try { localStorage.setItem('recentColors', JSON.stringify([hex, ...recentColors().filter((c) => c !== hex)].slice(0, 4))); } catch { /* recents are a convenience */ }
}

function hexToHsv(hex) {
  const n = parseInt(hex.slice(1), 16);
  const [r, g, b] = [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((c) => c / 255);
  const max = Math.max(r, g, b);
  const range = max - Math.min(r, g, b);
  let h = 0;
  if (range) {
    if (max === r) h = ((g - b) / range + 6) % 6;
    else if (max === g) h = (b - r) / range + 2;
    else h = (r - g) / range + 4;
  }
  return { h: h * 60, s: max ? range / max : 0, v: max };
}

function hsvToHex({ h, s, v }) {
  const channel = (n) => {
    const k = (n + h / 60) % 6;
    return Math.round(255 * (v - v * s * Math.max(0, Math.min(k, 4 - k, 1))));
  };
  return `#${[channel(5), channel(3), channel(1)].map((c) => c.toString(16).padStart(2, '0')).join('')}`.toUpperCase();
}

function showPicker() {
  const hex = hsvToHex(picker);
  $('pickerArea').style.backgroundColor = `hsl(${picker.h} 100% 50%)`;
  $('pickerThumb').style.left = `${picker.s * 100}%`;
  $('pickerThumb').style.top = `${(1 - picker.v) * 100}%`;
  $('pickerHue').value = picker.h;
  $('pickerPreview').style.background = hex;
  if (document.activeElement !== $('pickerHex')) $('pickerHex').value = hex;
}

/* One undo step for each time the picker is open, however much the color is dragged around. */
function pickerChanged() {
  if (!picker.remembered) { remember(); picker.remembered = true; }
  showPicker();
  picker.apply?.(hsvToHex(picker));
}

function setPickerHex(hex) {
  Object.assign(picker, hexToHsv(hex));
  pickerChanged();
}

function mixColors(from, to, share) {
  const channels = (hex) => [1, 3, 5].map((start) => parseInt(hex.slice(start, start + 2), 16));
  const [a, b] = [channels(from), channels(to)];
  return `#${a.map((value, i) => Math.round(value + (b[i] - value) * share).toString(16).padStart(2, '0')).join('')}`.toUpperCase();
}

/* WCAG relative luminance and contrast ratio. */
function luminance(hex) {
  const [r, g, b] = [1, 3, 5].map((start) => {
    const c = parseInt(hex.slice(start, start + 2), 16) / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrast(a, b) {
  const [high, low] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (high + 0.05) / (low + 0.05);
}

/*
 * Changes a color only as far as it takes to read clearly. On dark surfaces it brightens first,
 * which keeps the color vivid, because mixing a dark color straight into white turns it gray.
 */
function readableOn(hex, surface) {
  const target = contrast('#FFFFFF', surface) >= contrast('#000000', surface) ? '#FFFFFF' : '#000000';
  const bright = hexToHsv(hex);
  while (target === '#FFFFFF' && bright.v < 1 && contrast(hsvToHex(bright), surface) < MIN_STAT_CONTRAST) {
    bright.v = Math.min(1, bright.v + READABLE_STEP);
  }
  const start = hsvToHex(bright);
  let share = 0;
  let color = start;
  while (contrast(color, surface) < MIN_STAT_CONTRAST && share < 1) {
    share += READABLE_STEP;
    color = mixColors(start, target, share);
  }
  return color;
}

/* What the stats sit on. Pictures and video can't be measured here, so they count as dark. */
function statSurface() {
  const { background, dim } = app.draft;
  return background.kind === 'Color' ? mixColors(background.color.slice(0, 7).toUpperCase(), '#000000', dim ?? 0) : '#000000';
}

/* A neighboring hue, so a gradient stays in one family. Grays have no hue, so they lean toward the surface. */
function gradientEnd(hex, surface) {
  const color = hexToHsv(hex);
  if (color.s < GRAY_SATURATION) return mixColors(hex, surface, 0.35);
  return hsvToHex({ ...color, h: (color.h + BLEND_HUE_SHIFT) % 360 });
}

function metricFamily(metric) {
  if (/Fan|Pump/.test(metric)) return 'Cooling';
  return ['Cpu', 'Gpu', 'Memory'].find((family) => metric.startsWith(family)) ?? 'Board';
}

/* A gauge, a bar, their stats and their label all belong together when they describe the same hardware. */
function familyOf(item) {
  if (['Metric', 'Gauge', 'Bar'].includes(item.kind)) return metricFamily(item.metric);
  if (item.kind === 'Text') return FAMILY_LABELS.find(([, pattern]) => pattern.test(item.text ?? ''))?.[0] ?? null;
  return null;
}

function applyAccent(family, accent, surface) {
  app.draft.elements.filter((item) => familyOf(item) === family).forEach((item) => {
    item.color = accent;
    if (item.kind === 'Gauge' || item.kind === 'Bar') {
      item.endColor = gradientEnd(accent, surface);
      item.trackColor = mixColors(surface, accent, TRACK_TINT);
    }
  });
}

/* A stat too close to the background shifts along with its gauge, so the two still match. */
function keepStatsReadable() {
  const surface = statSurface();
  let adjusted = false;
  app.draft.elements.filter((item) => item.kind === 'Metric' && contrast(item.color.slice(0, 7), surface) < MIN_STAT_CONTRAST).forEach((item) => {
    applyAccent(familyOf(item), readableOn(item.color.slice(0, 7), surface), surface);
    adjusted = true;
  });
  return adjusted;
}

function blendEnabled() {
  try { return localStorage.getItem('paletteBlend') !== 'false'; } catch { return true; }
}

/* Rebuilding the panel closes the picker, so it reopens on the same field for trying the next swatch. */
function showBlendedColors() {
  const buttons = () => [...$('inspector').querySelectorAll('.color-button')];
  const index = buttons().indexOf(picker.anchor);
  renderInspector();
  changed();
  buttons()[index]?.click();
}

function pickPaletteColor(hex) {
  setPickerHex(hex);
  const family = familyOf(app.draft.elements[app.selected] ?? {});
  if (!blendEnabled() || !family || !BLEND_FIELDS.has(picker.label)) return;
  const surface = statSurface();
  applyAccent(family, readableOn(hex, surface), surface);
  showBlendedColors();
}

/* Spreads the palette across the theme, skipping grays when it has enough real colors to go around. */
function applyPaletteToTheme() {
  const families = [...new Set(app.draft.elements.map(familyOf).filter(Boolean))];
  if (!families.length) { toast('This theme has no stats, gauges or bars to color.'); return; }
  const surface = statSurface();
  const readable = [...new Set(currentPalette().colors.map((hex) => readableOn(hex, surface)))];
  const vivid = readable.filter((hex) => hexToHsv(hex).s >= MUTED_SATURATION);
  const colors = vivid.length >= families.length ? vivid : readable;
  remember();
  families.forEach((family, i) => applyAccent(family, colors[Math.floor(i * colors.length / families.length)], surface));
  showBlendedColors();
}

function colorSwatch(hex, pick = setPickerHex) {
  const button = element('button', { title: hex, onclick: () => pick(hex) });
  button.style.background = hex;
  button.setAttribute('aria-label', hex);
  return button;
}

const allPalettes = () => [...PALETTES, ...app.palettes];

function currentPalette() {
  let name = null;
  try { name = localStorage.getItem('palette'); } catch { /* it opens on the first palette */ }
  return allPalettes().find((palette) => palette.name === name) ?? PALETTES[0];
}

function choosePalette(name) {
  try { localStorage.setItem('palette', name); } catch { /* the choice lasts until the picker reopens */ }
  renderSwatches();
}

function uniquePaletteName(name) {
  const taken = new Set(allPalettes().map((palette) => palette.name.toLowerCase()));
  if (!taken.has(name.toLowerCase())) return name;
  let number = 2;
  while (taken.has(`${name} ${number}`.toLowerCase())) number += 1;
  return `${name} ${number}`;
}

function addPalette({ name, colors }) {
  const palette = { name: uniquePaletteName(name), colors };
  app.palettes.push(palette);
  savePalettes();
  choosePalette(palette.name);
}

function savePalettes() {
  send({ type: 'savePalettes', palettes: app.palettes });
}

function editPalette(palette, colors) {
  palette.colors = colors;
  savePalettes();
  renderSwatches();
}

function renderSwatches() {
  const recents = recentColors();
  $('recentSection').hidden = !recents.length;
  $('recentSwatches').replaceChildren(...recents.map((hex) => colorSwatch(hex)));

  const palette = currentPalette();
  const own = app.palettes.includes(palette);
  const option = ({ name }) => element('option', { value: name, textContent: name });
  $('paletteSelect').replaceChildren(...PALETTES.map(option));
  if (app.palettes.length) {
    $('paletteSelect').append(element('optgroup', { label: 'My Palettes' }, ...app.palettes.map(option)));
  }
  $('paletteSelect').value = palette.name;
  $('pickerSwatches').replaceChildren(...palette.colors.map((hex) => {
    const swatch = colorSwatch(hex, pickPaletteColor);
    if (own) {
      swatch.addEventListener('contextmenu', (event) => {
        event.preventDefault();
        if (palette.colors.length > 1) editPalette(palette, palette.colors.filter((color) => color !== hex));
      });
    }
    return swatch;
  }));
  $('paletteAdd').hidden = !own;
  $('paletteAdd').disabled = palette.colors.length >= MAX_PALETTE_COLORS;
  $('paletteHint').hidden = !own;
  $('paletteDelete').hidden = !own;
  $('paletteBlend').checked = blendEnabled();
  placePicker();
}

function placePicker() {
  const node = $('picker');
  if (node.hidden || !picker.anchor) return;
  const box = picker.anchor.getBoundingClientRect();
  node.style.left = `${Math.max(8, box.left - node.offsetWidth - 12)}px`;
  node.style.top = `${Math.min(innerHeight - node.offsetHeight - 8, Math.max(8, box.top - 8))}px`;
}

function openNewPalette() {
  $('paletteName').value = '';
  $('paletteDialog').showModal();
}

function deletePalette() {
  const palette = currentPalette();
  confirmDialog('Delete this palette?', `“${palette.name}” will be removed. Themes keep the colors they already use.`, [
    { label: 'Cancel' },
    {
      label: 'Delete', className: 'danger',
      run: () => {
        app.palettes = app.palettes.filter((other) => other !== palette);
        savePalettes();
        renderSwatches();
      },
    },
  ]);
}

function openPicker(anchor, hex, apply, label) {
  if (picker.anchor === anchor) { closePicker(); return; }
  closePicker();
  Object.assign(picker, hexToHsv(hex), { apply, anchor, label, remembered: false });
  $('pickerHint').hidden = true;
  $('picker').hidden = false;
  renderSwatches();
  showPicker();
}

function closePicker() {
  const edited = picker.apply && picker.remembered;
  if (edited) saveRecentColor(hsvToHex(picker));
  picker.apply = null;
  picker.anchor = null;
  $('picker').hidden = true;
  if (edited && blendEnabled() && keepStatsReadable()) { renderInspector(); changed(); }
}

function trackPickerArea(event) {
  const area = $('pickerArea');
  area.setPointerCapture(event.pointerId);
  const place = (e) => {
    const box = area.getBoundingClientRect();
    picker.s = Math.min(1, Math.max(0, (e.clientX - box.left) / box.width));
    picker.v = 1 - Math.min(1, Math.max(0, (e.clientY - box.top) / box.height));
    pickerChanged();
  };
  const end = () => {
    area.removeEventListener('pointermove', place);
    area.removeEventListener('pointerup', end);
    area.removeEventListener('pointercancel', end);
  };
  place(event);
  area.addEventListener('pointermove', place);
  area.addEventListener('pointerup', end);
  area.addEventListener('pointercancel', end);
}

/* Layers. The list reads top to bottom as the stack does, so the last item drawn comes first. */
function layerName(item) {
  if (item.kind === 'Text') return item.text?.trim() || 'Text';
  if (item.kind === 'Metric') return METRICS[item.metric]?.label ?? 'Stat';
  if (item.kind === 'Gauge') return `${METRICS[item.metric]?.label ?? 'Stat'} Gauge`;
  if (item.kind === 'Capture') return 'Picture-in-Picture';
  if (item.kind === 'Music') return 'Music';
  if (item.kind === 'Weather') return 'Weather';
  if (item.kind === 'Bar') return `${METRICS[item.metric]?.label ?? 'Stat'} Bar`;
  if (item.kind === 'Shape') return item.shape;
  return item.kind;
}

/* Rows top to bottom as the stack draws them. A group's header row comes just before its members. */
function layerRows() {
  const items = app.draft.elements;
  const rows = [];
  const seen = new Set();
  for (let index = items.length - 1; index >= 0; index--) {
    const group = items[index].group || null;
    if (group && !seen.has(group)) {
      seen.add(group);
      rows.push({ group, indices: membersOf(group) });
    }
    if (!group || !app.collapsed.has(group)) rows.push({ index, group, indices: [index] });
  }
  return rows;
}

function renderLayers() {
  const list = $('layerList');
  if (!app.draft) { list.replaceChildren(); return; }
  const rows = layerRows();
  list.replaceChildren(...rows.map((row, slot) => (row.index === undefined ? groupRow(rows, slot) : itemRow(rows, slot))), backgroundLayer());
  markSelection();
}

function eyeButton(label, hidden, toggleHidden) {
  const eye = element('button', {
    title: hidden ? 'Show' : 'Hide',
    onclick: (event) => {
      event.stopPropagation();
      remember();
      toggleHidden();
      changed();
    },
  });
  eye.setAttribute('aria-label', `${hidden ? 'Show' : 'Hide'} ${label}`);
  eye.append(icon(hidden ? 'Hidden' : 'Shown'));
  eye.addEventListener('pointerdown', (event) => event.stopPropagation());
  return eye;
}

function itemRow(rows, slot) {
  const { index, group } = rows[slot];
  const item = app.draft.elements[index];
  const row = element('div', { className: `layer${item.hidden ? ' is-hidden' : ''}${group ? ' in-group' : ''}`, tabIndex: 0 });
  row.dataset.indices = String(index);
  row.setAttribute('role', 'option');
  row.append(icon(item.kind === 'Shape' ? item.shape : item.kind), element('span', { className: 'layer-name', textContent: layerName(item), title: layerName(item) }),
    eyeButton(layerName(item), item.hidden, () => { item.hidden = !item.hidden; }));
  row.addEventListener('pointerdown', (event) => beginLayerDrag(event, row, rows, slot));
  row.addEventListener('contextmenu', (event) => openLayerMenu(event, index));
  row.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' || event.key === ' ') { select(index); event.preventDefault(); }
  });
  return row;
}

function groupRow(rows, slot) {
  const { group, indices } = rows[slot];
  const items = indices.map((index) => app.draft.elements[index]);
  const hidden = items.every((item) => item.hidden);
  const collapsed = app.collapsed.has(group);
  const row = element('div', { className: `layer group-layer${hidden ? ' is-hidden' : ''}`, tabIndex: 0 });
  row.dataset.indices = indices.join(',');
  row.setAttribute('role', 'option');
  row.setAttribute('aria-expanded', String(!collapsed));
  const caret = element('button', {
    className: 'layer-caret', title: collapsed ? 'Show the layers in this group' : 'Hide the layers in this group',
    onclick: (event) => {
      event.stopPropagation();
      if (collapsed) app.collapsed.delete(group);
      else app.collapsed.add(group);
      renderLayers();
    },
  }, icon('Chevron'));
  caret.setAttribute('aria-label', `${collapsed ? 'Expand' : 'Collapse'} ${group}`);
  caret.addEventListener('pointerdown', (event) => event.stopPropagation());
  row.append(caret, icon('Group'), element('span', { className: 'layer-name', textContent: group, title: group }),
    eyeButton(group, hidden, () => items.forEach((item) => { item.hidden = !hidden; })));
  row.addEventListener('pointerdown', (event) => beginLayerDrag(event, row, rows, slot));
  row.addEventListener('contextmenu', (event) => {
    if (!indices.every((index) => app.selection.includes(index))) setSelection(indices);
    openLayerMenu(event);
  });
  row.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' || event.key === ' ') { setSelection(indices); event.preventDefault(); }
    if (event.key === 'ArrowLeft' && !collapsed) caret.click();
    if (event.key === 'ArrowRight' && collapsed) caret.click();
  });
  return row;
}

/* Always at the bottom of the stack, so it can't be dragged or hidden. */
function backgroundLayer() {
  const row = element('div', { className: 'layer', tabIndex: 0 }, icon('Background'), element('span', { className: 'layer-name', textContent: 'Background' }));
  row.dataset.indices = '-1';
  row.setAttribute('role', 'option');
  row.addEventListener('pointerdown', () => select(-1));
  row.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' || event.key === ' ') { select(-1); event.preventDefault(); }
  });
  return row;
}

/*
 * Press selects at once: Ctrl adds or removes a row, Shift selects the run of layers up to it.
 * Moving the row reorders the stack, and it stays under the pointer. A group's row moves the whole group.
 */
function beginLayerDrag(event, row, rows, slot) {
  if (event.button !== 0) return;
  closeLayerMenu();
  const source = rows[slot];
  if (event.shiftKey && app.selected >= 0) {
    const low = Math.min(app.selected, ...source.indices);
    const high = Math.max(app.selected, ...source.indices);
    setSelection(Array.from({ length: high - low + 1 }, (_, i) => low + i), app.selected);
    return;
  }
  if (event.ctrlKey || event.metaKey) {
    const on = source.indices.every((index) => app.selection.includes(index));
    setSelection(on ? app.selection.filter((index) => !source.indices.includes(index)) : [...app.selection, ...source.indices]);
    return;
  }
  setSelection(source.indices, source.index);
  const height = row.offsetHeight;
  const startY = event.clientY;
  let target = slot;
  let dragging = false;
  let marker = null;
  row.setPointerCapture(event.pointerId);

  const move = (e) => {
    const offset = e.clientY - startY;
    if (!dragging && Math.abs(offset) < 4) return;
    if (!dragging) {
      dragging = true;
      row.classList.add('dragging');
      marker = element('div', { className: 'layer-drop' });
      $('layerList').append(marker);
    }
    row.style.transform = `translateY(${offset}px)`;
    target = Math.min(rows.length - 1, Math.max(0, slot + Math.round(offset / height)));
    marker.style.top = `${(target + (target > slot ? 1 : 0)) * height - 1}px`;
  };

  const end = () => {
    row.removeEventListener('pointermove', move);
    row.removeEventListener('pointerup', end);
    row.removeEventListener('pointercancel', end);
    if (!dragging || target === slot) { renderLayers(); return; }
    moveRows(source, rows, target > slot ? target + 1 : target);
  };

  row.addEventListener('pointermove', move);
  row.addEventListener('pointerup', end);
  row.addEventListener('pointercancel', end);
}

/*
 * Puts a row's layers just above the row that ends up below the drop line. A layer dropped between two members
 * of a group joins it, and one dropped outside its group leaves it. A whole group never lands inside another.
 */
function moveRows(source, rows, line) {
  const items = app.draft.elements;
  const moving = [...source.indices].sort((a, b) => a - b).map((index) => items[index]);
  const outside = (row) => !row.indices.every((index) => source.indices.includes(index));
  let below = rows.slice(line).find(outside) ?? null;
  const above = rows.slice(0, line).reverse().find(outside) ?? null;
  const isGroup = source.index === undefined;
  const into = below && below.index !== undefined && below.group && above?.group === below.group ? below.group : null;
  if (isGroup && into) below = rows.find((row) => row.index === undefined && row.group === into);
  const anchor = below ? items[Math.max(...below.indices)] : null;
  remember();
  const rest = items.filter((item) => !moving.includes(item));
  rest.splice(anchor ? rest.indexOf(anchor) + 1 : 0, 0, ...moving);
  app.draft.elements = rest;
  if (!isGroup) {
    if (into) moving[0].group = into;
    else delete moving[0].group;
  }
  cleanGroups();
  app.selection = moving.map((item) => rest.indexOf(item)).sort((a, b) => a - b);
  app.selected = app.selection[app.selection.length - 1];
  renderInspector();
  changed();
}
function selectField(label, options, value, apply) {
  const input = element('select', {}, ...Object.entries(options).map(([key, text]) => element('option', { value: key, textContent: text })));
  input.value = value;
  input.addEventListener('change', () => { remember(); apply(input.value); renderInspector(); changed(); });
  return field(label, input);
}

function segmented(label, options, value, apply) {
  const buttons = options.map((option) => {
    const button = element('button', { textContent: option, onclick: () => { remember(); apply(option); renderInspector(); changed(); } });
    button.setAttribute('aria-pressed', String(option === value));
    return button;
  });
  return element('div', { className: 'field' }, element('span', { textContent: label }), element('div', { className: 'segmented' }, ...buttons));
}

function toggle(label, value, apply) {
  const input = element('input', { type: 'checkbox', checked: value });
  input.setAttribute('role', 'switch');
  input.addEventListener('change', () => { remember(); apply(input.checked); renderInspector(); changed(); });
  return element('label', { className: 'switch' }, element('span', { textContent: label }), input);
}

/* Lists both the option's sample and any format an older theme saved. */
function formatField(label, formats, item) {
  const options = Object.fromEntries(formats.map((format) => [format, formatDate(new Date(), format)]));
  if (!(item.format in options)) options[item.format] = formatDate(new Date(), item.format);
  return selectField(label, options, item.format, (value) => { item.format = value; });
}

function fontControls(item) {
  const family = item.fontFamily || DEFAULT_FONT;
  const names = [...new Set([family, ...app.fonts])].sort((a, b) => a.localeCompare(b));
  const style = [item.bold && 'Bold', item.italic && 'Italic'].filter(Boolean).join(' ') || 'Regular';
  const styleControl = segmented('Style', FONT_STYLES, style, (value) => {
    item.bold = value.includes('Bold');
    item.italic = value.includes('Italic');
  });
  styleControl.querySelector('.segmented').classList.add('wrap');
  return [
    selectField('Font', Object.fromEntries(names.map((name) => [name, name])), family, (value) => { item.fontFamily = value; }),
    styleControl,
  ];
}

function strokeControls(item) {
  const controls = [toggle('Stroke', item.stroke, (value) => { item.stroke = value; })];
  if (item.stroke) {
    controls.push(
      slider('Width', item.strokeWidth, { min: 0.5, max: 12, step: 0.5, unit: ' px', reset: 2 }, (v) => { item.strokeWidth = v; }),
      colorField('Stroke Color', item.strokeColor, (v) => { item.strokeColor = v; }));
  }
  return controls;
}

function shadowControls(item) {
  const controls = [toggle('Shadow', item.shadow, (value) => { item.shadow = value; })];
  if (item.shadow) {
    controls.push(
      slider('Blur', item.shadowBlur, { min: 0, max: 20, step: 1, unit: ' px', reset: 6 }, (v) => { item.shadowBlur = v; }),
      slider('Horizontal Offset', item.shadowX, { min: -20, max: 20, step: 1, unit: ' px', reset: 2 }, (v) => { item.shadowX = v; }),
      slider('Vertical Offset', item.shadowY, { min: -20, max: 20, step: 1, unit: ' px', reset: 2 }, (v) => { item.shadowY = v; }),
      colorField('Shadow Color', item.shadowColor, (v) => { item.shadowColor = v; }));
  }
  return controls;
}

function renderInspector() {
  closePicker();
  const panel = $('inspector');
  if (!app.draft) { panel.replaceChildren(); return; }
  if (app.selection.length > 1) {
    panel.replaceChildren(...selectionControls());
    return;
  }
  const item = app.draft.elements[app.selected];
  const forItem = { Gauge: gaugeControls, Bar: barControls, Shape: shapeControls, Capture: captureControls, Music: musicControls, Weather: weatherControls }[item?.kind] ?? elementControls;
  const controls = item ? forItem(item) : backgroundControls();
  panel.replaceChildren(...controls);
  renderWeatherPanel();
}

/* Exact X and Y for when dragging isn't precise enough. */
function positionFields(item) {
  const number = (label, axis) => {
    const input = element('input', { type: 'number', min: -100, max: 580, step: 1, value: item[axis] });
    input.addEventListener('focus', remember);
    input.addEventListener('input', () => {
      if (input.value === '') return;
      item[axis] = Number(input.value);
      changed();
    });
    return field(label, input);
  };
  return element('div', { className: 'position' }, number('X', 'x'), number('Y', 'y'));
}

function metricOptions() {
  return Object.fromEntries(Object.entries(METRICS).map(([key, { label }]) => [key, label]));
}

const centerButton = (item) => element('button', {
  textContent: 'Center on Screen', className: 'wide',
  onclick: () => { remember(); item.x = SCREEN / 2; item.y = SCREEN / 2; renderInspector(); changed(); },
});

function gaugeStatField(item) {
  return selectField('Shows', metricOptions(), item.metric, (metric) => {
    item.metric = metric;
    item.min = METRICS[metric].min;
    item.max = METRICS[metric].max;
  });
}

function gaugeRangeSliders(item) {
  const range = METRICS[item.metric] ?? { min: 0, max: 100 };
  const limit = Math.max(range.max * 1.5, item.max, item.min);
  const step = limit > 1000 ? 50 : 1;
  const unit = range.unit ?? '';
  return [
    slider('Empty At', item.min, { min: 0, max: limit, step, unit, reset: range.min }, (v) => { item.min = v; }),
    slider('Full At', item.max, { min: 0, max: limit, step, unit, reset: range.max }, (v) => { item.max = v; }),
  ];
}

const rotationSlider = (item) => slider('Rotation', item.rotation ?? 0, { min: 0, max: 360, step: 1, unit: '°', reset: 0 }, (v) => { item.rotation = v; });

function gaugeControls(item) {
  const controls = [element('h3', { textContent: 'Gauge' }), gaugeStatField(item)];
  if (isOutsideCircle(item)) {
    controls.push(element('p', { className: 'hint warning', textContent: 'Part of this is outside the screen.' }));
  }
  const sign = item.sweep < 0 ? -1 : 1;
  controls.push(
    positionFields(item),
    centerButton(item),
    ...gaugeRangeSliders(item),
    slider('Size', item.size, { min: 80, max: SCREEN, step: 2, unit: ' px', reset: NEW_GAUGE.size }, (v) => { item.size = v; }),
    rotationSlider(item),
    slider('Thickness', item.thickness, { min: 4, max: 48, step: 1, unit: ' px', reset: NEW_GAUGE.thickness }, (v) => { item.thickness = v; }),
    slider('Start Angle', item.startAngle, { min: 0, max: 360, step: 5, unit: '°', reset: NEW_GAUGE.startAngle }, (v) => { item.startAngle = v; }),
    slider('Arc Length', Math.abs(item.sweep), { min: 30, max: 360, step: 5, unit: '°', reset: NEW_GAUGE.sweep }, (v) => { item.sweep = sign * v; }),
    segmented('Fill Direction', ['Clockwise', 'Counterclockwise'], sign < 0 ? 'Counterclockwise' : 'Clockwise', (v) => {
      item.sweep = Math.abs(item.sweep) * (v === 'Clockwise' ? 1 : -1);
    }),
    colorField('Low Color', item.color, (v) => { item.color = v; }),
    colorField('High Color', item.endColor, (v) => { item.endColor = v; }),
    ...trackControls(item),
    ...(item.useTrackColor === false ? [] : [toggle('Dot Past Tip', item.dot, (v) => { item.dot = v; })]),
    element('button', { textContent: 'Delete', className: 'danger wide', onclick: removeSelected }));
  return controls;
}

/* Off, the whole gauge or bar shows the gradient and a marker sits at the reading. */
function trackControls(item) {
  const on = item.useTrackColor !== false;
  const controls = [toggle('Track Color', on, (v) => { item.useTrackColor = v; })];
  if (on) controls.push(colorField('Track Color', item.trackColor, (v) => { item.trackColor = v; }));
  return controls;
}

function barControls(item) {
  const controls = [element('h3', { textContent: 'Bar' }), gaugeStatField(item)];
  if (isOutsideCircle(item)) {
    controls.push(element('p', { className: 'hint warning', textContent: 'Part of this is outside the screen.' }));
  }
  controls.push(
    positionFields(item),
    centerButton(item),
    ...gaugeRangeSliders(item),
    slider('Length', item.size, { min: 40, max: SCREEN, step: 2, unit: ' px', reset: NEW_BAR.size }, (v) => { item.size = v; }),
    slider('Thickness', item.thickness, { min: 4, max: 60, step: 1, unit: ' px', reset: NEW_BAR.thickness }, (v) => { item.thickness = v; }),
    slider('Corner Radius', Math.min(item.cornerRadius ?? 0, item.thickness / 2), { min: 0, max: 30, step: 1, unit: ' px', reset: NEW_BAR.cornerRadius }, (v) => { item.cornerRadius = v; }),
    rotationSlider(item),
    colorField('Low Color', item.color, (v) => { item.color = v; }),
    colorField('High Color', item.endColor, (v) => { item.endColor = v; }),
    ...trackControls(item),
    toggle('Stripes', item.stripes, (v) => { item.stripes = v; }),
    element('button', { textContent: 'Delete', className: 'danger wide', onclick: removeSelected }));
  return controls;
}

function shapeControls(item) {
  const isLine = item.shape === 'Line';
  const isArc = item.shape === 'Arc';
  const controls = [
    element('h3', { textContent: 'Shape' }),
    selectField('Type', Object.fromEntries(SHAPES.map((name) => [name, name])), item.shape, (value) => { item.shape = value; }),
  ];
  if (isOutsideCircle(item)) {
    controls.push(element('p', { className: 'hint warning', textContent: 'Part of this is outside the screen.' }));
  }
  controls.push(
    positionFields(item),
    centerButton(item),
    slider(isLine ? 'Length' : isArc ? 'Size' : 'Width', item.size, { min: 4, max: SCREEN, step: 2, unit: ' px', reset: NEW_SHAPE.size }, (v) => { item.size = v; }));
  if (isLine || isArc) {
    controls.push(slider('Thickness', item.thickness, { min: 1, max: 60, step: 1, unit: ' px', reset: NEW_SHAPE.thickness }, (v) => { item.thickness = v; }));
  } else {
    controls.push(slider('Height', item.height, { min: 4, max: SCREEN, step: 2, unit: ' px', reset: NEW_SHAPE.height }, (v) => { item.height = v; }));
  }
  if (isLine) controls.push(segmented('Line Style', LINE_STYLES, item.lineStyle, (v) => { item.lineStyle = v; }));
  if (isArc) {
    controls.push(
      slider('Start Angle', item.startAngle, { min: 0, max: 360, step: 5, unit: '°', reset: NEW_SHAPE.startAngle }, (v) => { item.startAngle = v; }),
      slider('Arc Length', Math.abs(item.sweep), { min: 10, max: 360, step: 5, unit: '°', reset: NEW_SHAPE.sweep }, (v) => { item.sweep = v; }));
  }
  if (item.shape === 'Rectangle') {
    controls.push(slider('Corner Radius', item.cornerRadius ?? 0, { min: 0, max: 240, step: 1, unit: ' px', reset: NEW_SHAPE.cornerRadius }, (v) => { item.cornerRadius = v; }));
  }
  controls.push(rotationSlider(item));
  const painted = isLine || isArc || item.filled;
  if (!isLine && !isArc) controls.push(toggle('Fill', item.filled, (v) => { item.filled = v; }));
  if (painted) {
    controls.push(
      colorField('Color', item.color, (v) => { item.color = v; }),
      toggle('Gradient', item.gradient, (v) => { item.gradient = v; }));
    if (item.gradient) controls.push(colorField('End Color', item.endColor, (v) => { item.endColor = v; }));
  }
  controls.push(slider('Opacity', Math.round((item.opacity ?? 1) * 100), { min: 5, max: 100, step: 5, unit: '%', reset: 100 }, (v) => { item.opacity = v / 100; }));
  const effects = [...shadowControls(item)];
  if (!isLine && !isArc) {
    effects.unshift(...strokeControls(item));
    if (item.stroke) effects.splice(3, 0, segmented('Stroke Style', LINE_STYLES, item.lineStyle, (v) => { item.lineStyle = v; }));
  }
  controls.push(
    element('div', { className: 'inspector-group' }, ...effects),
    element('button', { textContent: 'Delete', className: 'danger wide', onclick: removeSelected }));
  return controls;
}

function pickRegion(target) {
  app.regionTarget = target;
  send({ type: 'pickRegion' });
}

/* Adds a Picture-in-Picture for 'new', or changes what the selected one shows. */
function applyCaptureSource(target, fields) {
  if (!app.draft) return;
  const area = { source: 'Area', captureScreen: null, captureProcess: null, captureTitle: null, ...fields };
  if (target === 'new') { addElement({ kind: 'Capture', x: SCREEN / 2, y: SCREEN / 2, ...NEW_CAPTURE, ...area }); return; }
  const item = app.draft.elements[app.selected];
  if (item?.kind !== 'Capture') return;
  remember();
  Object.assign(item, area);
  renderInspector();
  changed();
}

function openCaptureDialog(target) {
  app.captureTarget = target;
  app.captureTab ??= 'windows';
  requestCaptureSources();
  $('captureDialog').showModal();
}

function requestCaptureSources() {
  app.captureSources = null;
  renderCaptureSources();
  send({ type: 'listCaptureSources' });
}

function captureCard(name, detail, thumbnail, choose) {
  const thumb = element('span', { className: 'capture-thumb' });
  if (thumbnail) thumb.append(element('img', { src: thumbnail, alt: '' }));
  else thumb.textContent = 'Minimized';
  return element('button', { type: 'button', className: 'capture-card', title: name, onclick: choose },
    thumb, element('span', { className: 'capture-name', textContent: name }), element('span', { className: 'capture-detail', textContent: detail }));
}

function chooseCapture(fields) {
  $('captureDialog').close();
  applyCaptureSource(app.captureTarget, fields);
}

function renderCaptureSources() {
  const list = $('captureSources');
  const tab = app.captureTab ?? 'windows';
  document.querySelectorAll('#captureTabs button').forEach((button) => button.setAttribute('aria-pressed', String(button.dataset.tab === tab)));
  list.classList.remove('empty');
  const empty = (...children) => { list.classList.add('empty'); list.replaceChildren(...children); };

  if (tab === 'area') {
    empty(element('p', { className: 'hint', textContent: 'Drag a box around any part of your desktop. It stays in the same place on the screen.' }),
      element('button', { type: 'button', className: 'primary', textContent: 'Drag to Choose…', onclick: () => { $('captureDialog').close(); pickRegion(app.captureTarget); } }));
    return;
  }
  if (!app.captureSources) { empty(element('div', { className: 'spinner' }), element('p', { className: 'hint', textContent: 'Looking for screens and windows…' })); return; }

  if (tab === 'screens') {
    list.replaceChildren(...app.captureSources.screens.map((screen) => captureCard(
      screen.primary ? `${screen.name} (Main)` : screen.name, `${screen.width} × ${screen.height}`, screen.thumbnail,
      () => chooseCapture({ source: 'Screen', captureScreen: screen.device, captureX: screen.x, captureY: screen.y, captureWidth: screen.width, captureHeight: screen.height }))));
    return;
  }
  const windows = app.captureSources.windows;
  if (!windows.length) { empty(element('p', { className: 'hint', textContent: 'No open windows found. Open the app you want to show, then press Refresh.' })); return; }
  list.replaceChildren(...windows.map((win) => captureCard(win.title, win.process, win.thumbnail,
    () => chooseCapture({ source: 'Window', captureProcess: win.process, captureTitle: win.title, captureX: 0, captureY: 0, captureWidth: win.width, captureHeight: win.height }))));
}

function captureDescription(item) {
  if (item.source === 'Window') return `Showing ${item.captureTitle} (${item.captureProcess}), live. It follows the window when it moves or is covered.`;
  if (item.source === 'Screen') return `Showing a whole screen, ${item.captureWidth} × ${item.captureHeight}, live.`;
  return `Showing ${item.captureWidth} × ${item.captureHeight} of your desktop, live.`;
}

function captureControls(item) {
  const controls = [
    element('h3', { textContent: 'Picture-in-Picture' }),
    element('p', { className: 'hint', textContent: captureDescription(item) }),
    element('button', { textContent: 'Choose Source…', className: 'wide', onclick: () => openCaptureDialog('selected') }),
  ];
  if (item.source === 'Window') {
    controls.push(element('p', { className: 'hint', textContent: 'While the window is minimized, its last picture stays up.' }));
  }
  if (isOutsideCircle(item)) {
    controls.push(element('p', { className: 'hint warning', textContent: 'Part of this is outside the screen.' }));
  }
  controls.push(
    positionFields(item),
    centerButton(item),
    slider('Size', item.size, { min: 40, max: SCREEN, step: 2, unit: ' px', reset: NEW_CAPTURE.size }, (v) => { item.size = v; }),
    slider('Corner Radius', item.cornerRadius ?? 0, { min: 0, max: 240, step: 2, unit: ' px', reset: NEW_CAPTURE.cornerRadius }, (v) => { item.cornerRadius = v; }),
    rotationSlider(item),
    element('button', { textContent: 'Delete', className: 'danger wide', onclick: removeSelected }));
  return controls;
}

function weatherSearchForm() {
  const input = element('input', { type: 'search', id: 'weatherQuery', placeholder: 'Search for a city', maxLength: 80, autocomplete: 'off' });
  input.setAttribute('aria-label', 'City');
  const form = element('form', { className: 'weather-search' }, input, element('button', { type: 'submit', textContent: 'Search' }));
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    const query = input.value.trim();
    if (!query) return;
    app.weatherSearching = query;
    $('weatherResults').replaceChildren(element('p', { textContent: 'Searching…' }));
    send({ type: 'weatherSearch', query });
  });
  return form;
}

/* The city and units are shared, so every Weather widget agrees. */
function weatherControls(item) {
  const controls = [
    element('h3', { textContent: 'Weather' }),
    element('p', { className: 'hint', textContent: 'The forecast comes from Open-Meteo for the city you choose. Only that city’s location is sent, every 15 minutes. Every Weather widget uses the same city.' }),
    element('div', { className: 'widget-settings' },
      element('div', { className: 'weather-now', id: 'weatherNow' }),
      weatherSearchForm(),
      element('div', { className: 'weather-results', id: 'weatherResults', role: 'list' }),
      element('div', { className: 'field weather-units', id: 'weatherUnits' })),
  ];
  if (isOutsideCircle(item)) {
    controls.push(element('p', { className: 'hint warning', textContent: 'Part of this is outside the screen.' }));
  }
  controls.push(
    positionFields(item),
    centerButton(item),
    slider('Width', item.size, { min: 160, max: SCREEN, step: 2, unit: ' px', reset: NEW_WEATHER.size }, (v) => { item.size = v; }),
    rotationSlider(item),
    toggle('Condition and City', item.showCondition !== false, (v) => { item.showCondition = v; }),
    toggle('High, Low, Humidity, Wind', item.showDetails !== false, (v) => { item.showDetails = v; }),
    toggle('3-Day Forecast', item.showForecast !== false, (v) => { item.showForecast = v; }),
    ...fontControls(item),
    colorField('Temperature Color', item.color, (v) => { item.color = v; }),
    colorField('Detail Color', item.secondaryColor ?? NEW_WEATHER.secondaryColor, (v) => { item.secondaryColor = v; }),
    element('button', { textContent: 'Delete', className: 'danger wide', onclick: removeSelected }));
  return controls;
}

/* The widget follows whatever Windows reports as playing, so there's nothing to pick here. */
function musicControls(item) {
  const controls = [
    element('h3', { textContent: 'Music' }),
    element('p', { className: 'hint', textContent: 'Shows what Spotify or a browser tab is playing. It hides itself when nothing plays.' }),
  ];
  if (isOutsideCircle(item)) {
    controls.push(element('p', { className: 'hint warning', textContent: 'Part of this is outside the screen.' }));
  }
  controls.push(
    positionFields(item),
    centerButton(item),
    slider('Width', item.size, { min: 120, max: SCREEN, step: 2, unit: ' px', reset: NEW_MUSIC.size }, (v) => { item.size = v; }),
    rotationSlider(item),
    toggle('Album Art', item.showArt !== false, (v) => { item.showArt = v; }),
    toggle('Song Title', item.showTitle !== false, (v) => { item.showTitle = v; }),
    toggle('Artist', item.showArtist !== false, (v) => { item.showArtist = v; }),
    progressStyleField(item));
  if (item.showArt !== false) {
    const round = Math.round(item.size / 4);
    controls.push(slider('Art Corners', Math.min(item.cornerRadius ?? 0, round), { min: 0, max: round, step: 1, unit: ' px', reset: NEW_MUSIC.cornerRadius }, (v) => { item.cornerRadius = v; }));
  }
  controls.push(
    ...fontControls(item),
    colorField('Title Color', item.color, (v) => { item.color = v; }),
    colorField('Artist and Time Color', item.secondaryColor ?? NEW_MUSIC.secondaryColor, (v) => { item.secondaryColor = v; }));
  if (item.showProgress !== false) {
    controls.push(
      colorField('Progress Color', item.endColor ?? NEW_MUSIC.endColor, (v) => { item.endColor = v; }),
      colorField('Track Color', item.trackColor ?? NEW_MUSIC.trackColor, (v) => { item.trackColor = v; }));
  }
  controls.push(element('button', { textContent: 'Delete', className: 'danger wide', onclick: removeSelected }));
  return controls;
}

const PROGRESS_STYLES = [
  { value: 'Off', label: 'Off' },
  { value: 'Bar', label: 'Bar' },
  { value: 'Line', label: 'Line' },
  { value: 'Segments', label: 'Segments' },
  { value: 'AroundArt', label: 'Around Art', needsArt: true },
  { value: 'ScreenEdge', label: 'Edge', title: 'Screen Edge' },
];

/* Mirrors ThemeRenderer.EffectiveProgressStyle: Around Art needs the art, and falls back to a bar without it. */
const progressStyleOf = (item) => {
  const style = item.progressStyle ?? 'Bar';
  return style === 'AroundArt' && item.showArt === false ? 'Bar' : style;
};

/* Each style is a small picture of itself, so the choice doesn't depend on reading the names. */
function progressStyleField(item) {
  const current = item.showProgress === false ? 'Off' : (item.progressStyle ?? 'Bar');
  const tiles = PROGRESS_STYLES.map(({ value, label, title, needsArt }) => {
    const disabled = needsArt && item.showArt === false;
    const button = element('button', {
      type: 'button', className: 'style-tile', disabled,
      title: disabled ? 'Turn on Album Art to use this' : title ?? label,
      onclick: () => {
        remember();
        item.showProgress = value !== 'Off';
        if (value !== 'Off') item.progressStyle = value;
        renderInspector();
        changed();
      },
    }, icon(`Progress${value}`), element('span', { textContent: label }));
    button.setAttribute('aria-pressed', String(value === current));
    if (title) button.setAttribute('aria-label', title);
    return button;
  });
  const hint = current === 'ScreenEdge' ? element('p', { className: 'hint', textContent: 'Runs around the edge of the screen: a circle on round screens, a rounded rectangle on the rest.' }) : '';
  return element('div', { className: 'field' }, element('span', { textContent: 'Progress' }), element('div', { className: 'style-tiles', role: 'group' }, ...tiles), hint);
}

const isTemperature = (metric) => METRICS[metric]?.unit === DEGREE;

function elementControls(item) {
  const controls = [element('h3', { textContent: item.kind === 'Metric' ? 'Stat' : item.kind })];
  if (item.kind === 'Metric') {
    controls.push(selectField('Shows', metricOptions(), item.metric, (value) => {
      const bothTemperatures = isTemperature(item.metric) && isTemperature(value);
      item.metric = value;
      if (!bothTemperatures) item.unit = METRICS[value].unit;
    }));
    if (isTemperature(item.metric)) {
      controls.push(toggle('Degree Sign', item.unit === DEGREE, (value) => { item.unit = value ? DEGREE : ''; }));
    }
  }
  if (item.kind === 'Text') {
    const input = element('input', { type: 'text', value: item.text ?? '', maxLength: 60 });
    input.addEventListener('focus', remember);
    input.addEventListener('input', () => { item.text = input.value; changed(); });
    controls.push(field('Text', input));
  }
  if (item.kind === 'Clock') controls.push(formatField('Format', TIME_FORMATS, item));
  if (item.kind === 'Date') controls.push(formatField('Format', DATE_FORMATS, item));
  if (isOutsideCircle(item)) {
    controls.push(element('p', { className: 'hint warning', textContent: 'Part of this is outside the screen.' }));
  }
  controls.push(
    positionFields(item),
    element('button', {
      textContent: 'Center Horizontally', className: 'wide',
      onclick: () => { remember(); item.x = SCREEN / 2; item.align = 'Center'; renderInspector(); changed(); },
    }),
    ...fontControls(item),
    slider('Size', item.size, { min: 12, max: 160, step: 1, unit: ' px', reset: NEW_TEXT_SIZE }, (v) => { item.size = v; }),
    rotationSlider(item),
    colorField('Color', item.color, (v) => { item.color = v; }),
    segmented('Alignment', ['Left', 'Center', 'Right'], item.align ?? 'Left', (v) => { item.align = v; }),
    element('div', { className: 'inspector-group' }, ...strokeControls(item), ...shadowControls(item)),
    element('button', { textContent: 'Delete', className: 'danger wide', onclick: removeSelected }));
  return controls;
}

/* Several layers, or a group: they move, resize and rotate together on the screen. */
function selectionControls() {
  const indices = app.selection;
  const whole = isWholeGroup(indices);
  return [
    element('h3', { textContent: whole ? groupOf(indices[0]) : `${indices.length} Layers` }),
    element('p', { className: 'hint', textContent: 'Drag any of them on the screen to move them together. Drag a corner of the box to resize them, or the round handle above it to rotate them. Hold Shift to rotate in 15° steps, or Alt to resize from the middle.' }),
    whole
      ? element('button', { textContent: 'Ungroup', className: 'wide', title: 'Ctrl+Shift+G', onclick: ungroupSelection })
      : element('button', { textContent: 'Group', className: 'wide primary', title: 'Ctrl+G', onclick: groupSelection }),
    element('p', { className: 'hint', textContent: whole
      ? 'Clicking a member on the screen picks the whole group. Double-click it, or pick it in Layers, to change just that one.'
      : 'Grouped layers stay together in Layers and on the screen. You can also right-click to group them.' }),
    element('button', { textContent: 'Delete', className: 'danger wide', onclick: removeSelected }),
  ];
}
function backgroundControls() {
  const { background } = app.draft;
  const controls = [
    element('h3', { textContent: 'Background' }),
    element('p', { className: 'hint', textContent: 'Click an item on the screen to edit it, or drag it to move it.' }),
    element('button', { textContent: 'Choose Image or Video…', className: 'wide', onclick: () => send({ type: 'pickMedia' }) }),
  ];
  if (background.kind !== 'Color') {
    const name = background.path?.split(/[\\/]/).pop() ?? '';
    controls.push(
      element('p', { className: 'hint', textContent: name }),
      element('button', {
        textContent: 'Use Plain Color', className: 'wide',
        onclick: () => { remember(); background.kind = 'Color'; background.path = null; app.draft.dim = 0; renderInspector(); changed(); },
      }));
    controls.push(slider('Dim Background', Math.round((app.draft.dim ?? 0) * 100), { min: 0, max: 80, step: 5, unit: '%', reset: DEFAULT_MEDIA_DIM * 100 }, (v) => { app.draft.dim = v / 100; }));
  } else {
    controls.push(colorField('Color', background.color, (v) => { background.color = v; }));
  }
  return controls;
}

/* The Layers pane can be widened, so long names aren't cut off. */
const LAYERS_WIDTH = { min: 190, max: 340, normal: 216 };

function setLayersWidth(width, save) {
  const clamped = Math.min(LAYERS_WIDTH.max, Math.max(LAYERS_WIDTH.min, Math.round(width)));
  document.documentElement.style.setProperty('--layers-width', `${clamped}px`);
  $('layersResizer').setAttribute('aria-valuenow', clamped);
  if (app.page === 'editor') fitStage();
  if (save) {
    try { localStorage.setItem('layersWidth', clamped); } catch { /* the width lasts until the app closes */ }
  }
}

function beginLayersResize(event) {
  const bar = $('layersResizer');
  const startX = event.clientX;
  const startWidth = bar.parentElement.offsetWidth;
  bar.setPointerCapture(event.pointerId);
  bar.classList.add('dragging');
  const move = (e) => setLayersWidth(startWidth + e.clientX - startX, false);
  const end = (e) => {
    bar.removeEventListener('pointermove', move);
    bar.removeEventListener('pointerup', end);
    bar.removeEventListener('pointercancel', end);
    bar.classList.remove('dragging');
    setLayersWidth(startWidth + e.clientX - startX, true);
  };
  bar.addEventListener('pointermove', move);
  bar.addEventListener('pointerup', end);
  bar.addEventListener('pointercancel', end);
}

function savedLayersWidth() {
  try { return Number(localStorage.getItem('layersWidth')) || LAYERS_WIDTH.normal; } catch { return LAYERS_WIDTH.normal; }
}

function fitStage() {
  const room = $('stageArea').getBoundingClientRect();
  const { w, h } = screenArea();
  const scale = Math.min(1.25, (room.width - 48) / w, (room.height - 48) / h);
  const stage = $('stage');
  stage.style.width = `${w}px`;
  stage.style.height = `${h}px`;
  stage.style.margin = `${-h / 2}px 0 0 ${-w / 2}px`;
  stage.style.transform = `scale(${scale})`;
  stage.style.setProperty('--stage-scale', scale); // keeps the selection knobs the same size in any window
}

/* Messages from the app */
const handlers = {
  state(message) {
    app.themes = message.themes;
    app.current = message.current;
    app.currentPath = message.currentPath ?? null;
    app.rotation = message.rotation ?? 0;
    app.fonts = message.fonts ?? [];
    app.startup = Boolean(message.startup);
    app.removedDefaults = message.removedDefaults ?? 0;
    if (message.screen) app.screen = message.screen;
    migrateRoundPreference(message.roundOverride);
    app.crashReports = message.crashReports ?? null;
    app.crashReportsAvailable = Boolean(message.crashReportsAvailable);
    renderCrashOptions();
    if (message.crashedLastTime) afterLoading(offerCrashReport);
    else afterLoading(askAboutCrashReports);
    $('modelName').textContent = app.screen.model;
    applyScreenShape();
    renderScreenOptions();
    renderStartupOptions();
    renderRotation();
    app.playlist = message.playlist ?? app.playlist;
    renderPlaylist();
    if (message.alertSettings) app.alertSettings = message.alertSettings;
    app.alerts = message.alerts ?? [];
    renderAlertRules();
    renderAlertBanner();
    renderThemes();
    setStatus(message.status);
    if (app.pendingEdit === 'saved') {
      app.pendingEdit = null;
      app.draftPath = message.currentPath ?? null;
      updateHistoryButtons();
      toast('Saved');
    } else if (app.pendingEdit?.duplicate) {
      app.pendingEdit = null;
      newTheme();
    } else if (app.pendingEdit) {
      app.pendingEdit = null;
      openEditor(clone(app.current), app.currentPath);
      showPage('editor');
    }
  },
  status(message) { setStatus(message.status); },
  frame(message) {
    const source = `data:image/jpeg;base64,${message.jpeg}`;
    app.stats = message.stats;
    $(app.page === 'editor' ? 'editPreview' : 'livePreview').src = source;
    renderStats();
    renderAlertReadings();
    trackLoading();
  },
  windowState(message) {
    const button = $('windowMaximize');
    const label = message.maximized ? 'Restore' : 'Maximize';
    button.setAttribute('aria-label', label);
    button.title = label;
    button.querySelector('path').setAttribute('d', message.maximized ? 'M2.5.5h7v7M.5 2.5h7v7h-7z' : 'M.5.5h9v9h-9z');
  },
  weather(message) {
    app.weather = message;
    renderWeatherPanel();
  },
  weatherResults(message) {
    if (message.query !== app.weatherSearching) return; // an older search answered late
    const results = $('weatherResults');
    if (message.error) { results.replaceChildren(element('p', { textContent: message.error })); return; }
    if (!message.places?.length) { results.replaceChildren(element('p', { textContent: `No city called “${message.query}” was found.` })); return; }
    results.replaceChildren(...message.places.map((place) => {
      const button = element('button', { textContent: place.label, onclick: () => chooseWeatherPlace(place) });
      button.setAttribute('role', 'listitem');
      return button;
    }));
  },
  nowPlaying(message) {
    app.nowPlaying = message.media ? { ...message.media, receivedAt: performance.now() } : null;
    renderNowPlaying();
  },
  alerts(message) {
    app.alerts = message.active;
    renderAlertBanner();
  },
  media(message) {
    if (!app.draft) return;
    remember();
    app.draft.background = { ...app.draft.background, kind: message.kind, path: message.path };
    if (!app.draft.dim) app.draft.dim = DEFAULT_MEDIA_DIM;
    renderInspector();
    changed();
  },
  pickedColor(message) {
    $('pickerHint').hidden = true;
    if (message.color && picker.apply) setPickerHex(message.color);
  },
  system(message) { renderSpecs(message.rows); },
  showing(message) {
    app.draft = null; // the Editor must open what is on screen now
    app.current = message.current;
    app.currentPath = message.path ?? null;
    markShowingTheme();
  },
  palettes(message) { app.palettes = message.palettes; },
  palette(message) {
    addPalette(message.palette);
    toast('Palette imported');
  },
  region(message) {
    const target = app.regionTarget;
    app.regionTarget = null;
    if (message.width === undefined) return;
    applyCaptureSource(target, { captureX: message.x, captureY: message.y, captureWidth: message.width, captureHeight: message.height });
  },
  update(message) {
    app.update = message;
    renderUpdate();
  },
  captureSources(message) {
    app.captureSources = message;
    if ($('captureDialog').open) renderCaptureSources();
  },
  gpus(message) {
    app.gpus = message;
    renderGpus();
  },
  fans(message) {
    app.fans = message;
    renderFans();
  },
  notice(message) { toast(message.message); },
  error(message) { toast(message.message); },
};

function setStatus(status) {
  $('status').dataset.state = status.state;
  $('statusText').textContent = status.text;
}

host.addEventListener('message', (event) => handlers[event.data.type]?.(event.data));
setInterval(() => {
  if (app.page !== 'settings' || document.hidden) return;
  send({ type: 'listFans' });
  send({ type: 'listGpus' }); // GPUs can show up a moment after the sensors start
}, 2000);

document.querySelectorAll('.nav-item').forEach((node) => node.addEventListener('click', () => requestPage(node.dataset.page)));
$('resetPanels').onclick = resetPanels;
$('reportProblem').addEventListener('click', () => send({ type: 'reportProblem' }));
$('askQuestion').addEventListener('click', () => send({ type: 'askQuestion' }));
$('openLogs').addEventListener('click', () => send({ type: 'openLogs' }));
$('playlistAdd').addEventListener('click', openPlaylistPicker);
$('playlistPickerCancel').addEventListener('click', () => $('playlistDialog').close());
$('playlistPickerAdd').addEventListener('click', () => {
  app.playlist.paths.push(...playlistPicks.filter((path) => !app.playlist.paths.includes(path)));
  $('playlistDialog').close();
  savePlaylist();
});
$('playlistOn').addEventListener('change', (event) => { app.playlist.enabled = event.target.checked; savePlaylist(); });
$('playlistSeconds').addEventListener('input', (event) => { $('playlistTime').textContent = formatPlaylistTime(Number(event.target.value)); });
$('playlistSeconds').addEventListener('change', (event) => { app.playlist.seconds = Number(event.target.value); savePlaylist(); });
$('importTheme').onclick = () => send({ type: 'import' });
$('restoreDefaults').onclick = () => send({ type: 'restoreDefaults' });
$('addStat').onclick =() => addElement({ kind: 'Metric', unit: METRICS.CpuTemp.unit });
$('addText').onclick = () => addElement({ kind: 'Text', text: 'Text' });
$('addClock').onclick = () => addElement({ kind: 'Clock' });
$('addDate').onclick = () => addElement({ kind: 'Date', format: DATE_FORMATS[0], size: 30 });
$('addGauge').onclick = () => addElement(NEW_GAUGE);
$('addBar').onclick = () => addElement(NEW_BAR);
$('addShape').onclick = () => addElement(NEW_SHAPE);
$('addCapture').onclick = () => openCaptureDialog('new');
document.querySelectorAll('#captureTabs button').forEach((button) => button.addEventListener('click', () => { app.captureTab = button.dataset.tab; renderCaptureSources(); }));
$('captureRefresh').onclick = requestCaptureSources;
$('updateButton').onclick = confirmUpdate;
$('captureCancel').onclick = () => $('captureDialog').close();
$('addMusic').onclick = () => addElement(NEW_MUSIC);
$('addWeather').onclick = () => addElement(NEW_WEATHER);
$('windowMinimize').onclick = () => send({ type: 'window', action: 'minimize' });
$('windowMaximize').onclick = () => send({ type: 'window', action: 'maximize' });
$('windowClose').onclick = () => send({ type: 'window', action: 'close' });
$('npPrevious').onclick = () => send({ type: 'mediaControl', action: 'previous' });
$('npNext').onclick = () => send({ type: 'mediaControl', action: 'next' });
$('npPlay').onclick = () => {
  if (!app.nowPlaying) return;
  send({ type: 'mediaControl', action: 'playPause' });
  // Flipped at once; the app's next report confirms it or puts it back.
  app.nowPlaying = { ...app.nowPlaying, playing: !app.nowPlaying.playing, position: songPosition(app.nowPlaying), receivedAt: performance.now() };
  renderNowPlaying();
};
setInterval(renderSongProgress, 1000);
$('undo').onclick = () => restore(app.undo, app.redo);
$('redo').onclick = () => restore(app.redo, app.undo);
$('save').onclick = saveDraft;
$('saveAs').onclick = openSaveAs;
$('saveAsCancel').onclick = () => $('saveAsDialog').close();
$('saveAsForm').addEventListener('submit', (event) => {
  event.preventDefault();
  const name = $('saveAsName').value.trim();
  if (!name) return;
  $('saveAsDialog').close();
  saveAs(name);
});
$('themeName').addEventListener('focus', () => remember());
$('themeName').addEventListener('input', (event) => { app.draft.name = event.target.value; updateHistoryButtons(); });
$('stage').addEventListener('pointerdown', beginMarquee);
$('stage').addEventListener('contextmenu', (event) => openLayerMenu(event));
$('pickerArea').addEventListener('pointerdown', trackPickerArea);
$('pickerHue').addEventListener('input', (event) => { picker.h = Number(event.target.value); pickerChanged(); });
$('pickerHex').addEventListener('input', (event) => {
  const hex = event.target.value.trim().replace(/^#?/, '#').toUpperCase();
  if (/^#[0-9A-F]{6}$/.test(hex)) setPickerHex(hex);
});
$('pickerHex').addEventListener('blur', showPicker);
$('paletteSelect').addEventListener('change', (event) => choosePalette(event.target.value));
$('paletteBlend').addEventListener('change', (event) => {
  try { localStorage.setItem('paletteBlend', event.target.checked); } catch { /* it stays on */ }
});
$('paletteApply').onclick = applyPaletteToTheme;
$('paletteAdd').onclick = () => {
  const palette = currentPalette();
  const hex = hsvToHex(picker);
  if (!palette.colors.includes(hex)) editPalette(palette, [...palette.colors, hex]);
};
$('paletteNew').onclick = openNewPalette;
$('paletteDelete').onclick = deletePalette;
$('paletteImport').onclick = () => send({ type: 'importPalette' });
$('paletteExport').onclick = () => send({ type: 'exportPalette', ...currentPalette() });
$('paletteCancel').onclick = () => $('paletteDialog').close();
$('paletteForm').addEventListener('submit', (event) => {
  event.preventDefault();
  const name = $('paletteName').value.trim();
  if (!name) return;
  $('paletteDialog').close();
  addPalette({ name, colors: [hsvToHex(picker)] });
});
$('pickScreen').onclick = () => {
  $('pickerHint').hidden = false;
  send({ type: 'pickColor' });
};
addEventListener('pointerdown', (event) => {
  if (picker.apply && !$('picker').contains(event.target) && !picker.anchor?.contains(event.target) && !event.target.closest?.('dialog')) closePicker();
}, true);
addEventListener('pointerdown', (event) => { if (!$('layerMenu').contains(event.target)) closeLayerMenu(); }, true);
addEventListener('blur', closeLayerMenu);
addEventListener('resize', () => { closeLayerMenu(); if (app.page === 'editor') fitStage(); });

addEventListener('keydown', (event) => {
  if (document.querySelector('dialog[open]')) return;
  if (event.key === 'Escape' && picker.apply) { closePicker(); return; }
  if (event.key === 'Escape' && !$('layerMenu').hidden) { closeLayerMenu(); return; }
  if (app.page !== 'editor' || event.target.matches('input, select')) return;
  const key = event.key.toLowerCase();
  if (event.ctrlKey && key === 'z') { restore(app.undo, app.redo); event.preventDefault(); return; }
  if (event.ctrlKey && key === 'y') { restore(app.redo, app.undo); event.preventDefault(); return; }
  if (event.ctrlKey && key === 's') { if (!$('save').disabled) saveDraft(); event.preventDefault(); return; }
  if (event.ctrlKey && key === 'g') { if (event.shiftKey) ungroupSelection(); else groupSelection(); event.preventDefault(); return; }
  if (event.ctrlKey && key === 'a' && app.draft) {
    setSelection(app.draft.elements.flatMap((item, index) => (item.hidden ? [] : [index])));
    event.preventDefault();
    return;
  }
  if (key === 'escape' && app.selection.length) { setSelection([]); return; }
  if (key === 'delete' || key === 'backspace') { removeSelected(); return; }
  const items = app.draft ? selectedItems() : [];
  const step = event.shiftKey ? 10 : 1;
  const moves = { arrowleft: ['x', -step], arrowright: ['x', step], arrowup: ['y', -step], arrowdown: ['y', step] };
  if (items.length && moves[key]) {
    remember();
    items.forEach((item) => { item[moves[key][0]] += moves[key][1]; });
    renderInspector();
    changed();
    event.preventDefault();
  }
});

$('layersResizer').addEventListener('pointerdown', beginLayersResize);
$('layersResizer').addEventListener('dblclick', () => setLayersWidth(LAYERS_WIDTH.normal, true));
$('layersResizer').addEventListener('keydown', (event) => {
  const step = { ArrowLeft: -10, ArrowRight: 10 }[event.key];
  if (!step) return;
  setLayersWidth($('layersResizer').parentElement.offsetWidth + step, true);
  event.preventDefault();
});
document.querySelectorAll('[data-icon]').forEach((node) => node.prepend(icon(node.dataset.icon)));
setLayersWidth(savedLayersWidth(), false);
buildStats();
renderStats();
showPage('metrics');
moveIndicator(false);
setTimeout(finishLoading, LOADING_LIMIT_MS);
send({ type: 'ready' });
