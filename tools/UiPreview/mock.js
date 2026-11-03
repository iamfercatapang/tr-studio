// Stands in for the C# app, so the interface can be tested in a plain browser.
const listeners = [];
const canvas = Object.assign(document.createElement('canvas'), { width: 480, height: 480 });
const ctx = canvas.getContext('2d');
const readings = { CpuTemp: 56, CpuLoad: 42, CpuPower: 48, CpuClock: 3200, GpuTemp: 60, GpuLoad: 85, GpuPower: 165, GpuClock: 2580, GpuHotSpot: 74, GpuMemoryTemp: 92, MotherboardTemp: 45, MemoryLoad: 38, MemoryUsed: 12.2, MemoryAvailable: 19.7, MemorySpeed: 3200, PumpRpm: 2576, CpuFanRpm: 1418, CaseFanRpm: 1398, GpuFan: 1103 };
const stats = {};
setTimeout(() => Object.assign(stats, readings), 1500); // the first sensor read takes a moment
const emit = (data) => listeners.forEach((listener) => listener({ data }));

const defaultTheme = {
  name: 'System Stats', background: { kind: 'Color', color: '#000000', path: null }, dim: 0,
  elements: [
    { kind: 'Text', x: 40, y: 40, size: 26, text: 'CPU', color: '#0A84FF', bold: true, align: 'Left' },
    { kind: 'Metric', x: 30, y: 68, size: 100, metric: 'CpuTemp', unit: '\u00B0', bold: true, color: '#FFFFFF', align: 'Left' },
    { kind: 'Metric', x: 440, y: 90, size: 34, metric: 'CpuLoad', unit: '%', color: '#FFFFFF', align: 'Right' },
    { kind: 'Text', x: 40, y: 230, size: 26, text: 'GPU', color: '#30D158', bold: true, align: 'Left' },
    { kind: 'Metric', x: 30, y: 258, size: 100, metric: 'GpuTemp', unit: '\u00B0', bold: true, color: '#FFFFFF', align: 'Left' },
    { kind: 'Metric', x: 440, y: 280, size: 34, metric: 'GpuLoad', unit: '%', color: '#FFFFFF', align: 'Right' },
    { kind: 'Clock', x: 40, y: 420, size: 26, format: 'HH:mm', color: '#98989D', align: 'Left' },
  ],
};

const [screenWidth, screenHeight] = (new URLSearchParams(location.search).get('screen') ?? '480x480').split('x').map(Number);
canvas.width = screenWidth;
canvas.height = screenHeight;
let theme = defaultTheme;
const saved = new Map();
const removed = new Set();

const song = { title: 'Uncle Dags - Viva La Raza (feat. THUGSTA) [Official Music Video]', artist: 'Uncle Dags', playing: true, position: 83, duration: 272, art: null, canPlayPause: true, canNext: true, canPrevious: false };

function draw(item) {
  if (item.hidden) return;
  if (item.kind === 'Weather') {
    ctx.fillStyle = '#FFD60A';
    ctx.beginPath(); ctx.arc(item.x - 60, item.y - 50, 26, 0, 7); ctx.fill();
    ctx.fillStyle = item.color ?? '#FFFFFF';
    ctx.textAlign = 'left'; ctx.textBaseline = 'middle';
    ctx.font = `600 ${item.size * 0.2}px "Segoe UI"`;
    ctx.fillText(window.mock.weather?.place ? '12°' : '–°', item.x - 20, item.y - 50);
    return;
  }
  if (item.kind === 'Music') {
    if (!window.mock.playing) return; // the widget hides when nothing plays
    const width = Math.max(item.size, 120);
    const art = item.showArt !== false ? width * 0.5 : 0;
    ctx.fillStyle = '#7a3cff';
    ctx.beginPath(); ctx.roundRect(item.x - art / 2, item.y - width * 0.42, art, art, item.cornerRadius ?? 16); ctx.fill();
    ctx.fillStyle = item.color ?? '#FFFFFF';
    ctx.textAlign = 'center'; ctx.textBaseline = 'top';
    ctx.font = `600 ${width * 0.075}px "Segoe UI"`;
    if (item.showTitle !== false) ctx.fillText('Uncle Dags - Viva La Raza (…', item.x, item.y - width * 0.42 + art + width * 0.05);
    return;
  }
  if (item.kind === 'Capture') {
    const height = item.size * item.captureHeight / item.captureWidth;
    ctx.fillStyle = '#2d6cdf';
    ctx.beginPath();
    ctx.roundRect(item.x - item.size / 2, item.y - height / 2, item.size, height, item.cornerRadius);
    ctx.fill();
    return;
  }
  if (item.kind === 'Bar') {
    const share = Math.min(1, Math.max(0, ((stats[item.metric] ?? item.min) - item.min) / ((item.max - item.min) || 1)));
    ctx.save();
    ctx.translate(item.x, item.y);
    ctx.rotate((item.rotation ?? 0) * Math.PI / 180);
    const radius = Math.min(item.cornerRadius ?? 0, item.thickness / 2);
    ctx.fillStyle = item.trackColor;
    ctx.beginPath(); ctx.roundRect(-item.size / 2, -item.thickness / 2, item.size, item.thickness, radius); ctx.fill();
    ctx.fillStyle = item.color;
    ctx.beginPath(); ctx.roundRect(-item.size / 2, -item.thickness / 2, item.size * share, item.thickness, radius); ctx.fill();
    ctx.restore();
    return;
  }
  if (item.kind === 'Shape') {
    ctx.save();
    ctx.translate(item.x, item.y);
    ctx.rotate((item.rotation ?? 0) * Math.PI / 180);
    ctx.globalAlpha = item.opacity ?? 1;
    ctx.fillStyle = item.color;
    if (item.shape === 'Line') ctx.fillRect(-item.size / 2, -item.thickness / 2, item.size, item.thickness);
    else if (item.shape === 'Ellipse') { ctx.beginPath(); ctx.ellipse(0, 0, item.size / 2, item.height / 2, 0, 0, 7); ctx.fill(); }
    else ctx.fillRect(-item.size / 2, -item.height / 2, item.size, item.height);
    ctx.restore();
    return;
  }
  if (item.kind === 'Gauge') {
    const share = Math.min(1, Math.max(0, ((stats[item.metric] ?? item.min) - item.min) / ((item.max - item.min) || 1)));
    const start = item.startAngle * Math.PI / 180;
    const radius = (item.size - item.thickness) / 2;
    ctx.lineWidth = item.thickness;
    ctx.lineCap = 'round';
    ctx.strokeStyle = item.trackColor;
    const backwards = item.sweep < 0;
    ctx.beginPath(); ctx.arc(item.x, item.y, radius, start, start + item.sweep * Math.PI / 180, backwards); ctx.stroke();
    ctx.strokeStyle = item.color;
    ctx.beginPath(); ctx.arc(item.x, item.y, radius, start, start + share * item.sweep * Math.PI / 180, backwards); ctx.stroke();
    return;
  }
  const value = item.kind === 'Text' ? item.text
    : item.kind === 'Clock' ? '11:30'
      : item.kind === 'Date' ? item.format
        : stats[item.metric] === null ? '\u2013' : `${stats[item.metric]}${item.unit ?? ''}`;
  ctx.font = `${item.italic ? 'italic ' : ''}${item.bold ? 600 : 400} ${item.size}px "${item.fontFamily ?? 'Segoe UI'}"`;
  ctx.textBaseline = 'top';
  ctx.textAlign = (item.align ?? 'Left').toLowerCase();
  ctx.shadowColor = item.shadow ? item.shadowColor : 'transparent';
  ctx.shadowBlur = item.shadow ? item.shadowBlur : 0;
  ctx.shadowOffsetX = item.shadow ? item.shadowX : 0;
  ctx.shadowOffsetY = item.shadow ? item.shadowY : 0;
  if (item.stroke) {
    ctx.strokeStyle = item.strokeColor;
    ctx.lineWidth = item.strokeWidth * 2;
    ctx.strokeText(value, item.x, item.y);
  }
  ctx.fillStyle = item.color;
  ctx.fillText(value, item.x, item.y);
  ctx.shadowColor = 'transparent';
}

function render() {
  ctx.fillStyle = theme.background.kind === 'Color' ? theme.background.color : '#553388';
  ctx.fillRect(0, 0, screenWidth, screenHeight);
  ctx.fillStyle = `rgba(0,0,0,${theme.dim ?? 0})`;
  ctx.fillRect(0, 0, screenWidth, screenHeight);
  const scale = Math.min(screenWidth, screenHeight) / 480;
  ctx.save();
  ctx.translate(screenWidth / 2, screenHeight / 2);
  ctx.scale(scale, scale);
  ctx.translate(-240, -240);
  theme.elements.forEach(draw);
  ctx.restore();
  return canvas.toDataURL('image/jpeg', 0.9).split(',')[1];
}

function state() {
  const themes = [{ name: 'System Stats', path: null, kind: 'Color', thumb: null },
    { name: 'Halo', path: 'builtin:Halo', kind: 'Color', thumb: null },
    { name: 'Dual Bar', path: 'builtin:Dual Bar', kind: 'Color', thumb: null },
    { name: 'Memory', path: 'builtin:Memory', kind: 'Color', thumb: null },
    ...[...saved].map(([path, value]) => ({ name: value.name, path, kind: value.background.kind, thumb: null }))]
    .filter((entry) => !removed.has(entry.path));
  theme = { ...theme };
  if (themes[0]) themes[0].thumb = render();
  emit({ type: 'state', themes, current: theme, currentPath: window.mock.path, rotation: window.mock.rotation ?? 0, startup: window.mock.startup ?? false, removedDefaults: removed.size, playlist: window.mock.playlist ?? { enabled: false, paths: [], seconds: 30 }, alertSettings: window.mock.alertSettings, alerts: window.mock.alerts ?? [], screen: { width: screenWidth, height: screenHeight, round: screenWidth === 480, model: screenWidth === 480 ? 'Core Vision 360' : `${screenWidth}×${screenHeight} Screen` }, fonts: ['Arial', 'Consolas', 'Georgia', 'Impact', 'Segoe UI'], status: { state: 'Connected', text: 'Screen connected' } });
}

window.mock = { sent: [], path: null, saved, emit };
window.chrome = window.chrome ?? {};
window.chrome.webview = {
  addEventListener: (_, listener) => listeners.push(listener),
  postMessage(message) {
    window.mock.sent.push(message);
    if (message.type === 'ready' || message.type === 'refresh') state();
    if (message.type === 'savePalettes') window.mock.palettes = message.palettes;
    if (message.type === 'playlist') window.mock.playlist = { enabled: message.enabled, paths: message.paths, seconds: message.seconds };
    if (message.type === 'alerts') { const { type, ...settings } = message; window.mock.alertSettings = settings; }
    if (message.type === 'exportPalette') emit({ type: 'notice', message: 'Palette exported' });
    if (message.type === 'importPalette') emit({ type: 'palette', palette: { name: 'Essentials', colors: ['#112233', '#445566'] } });
    if (message.type === 'ready') {
      emit({ type: 'palettes', palettes: [] });
      window.mock.playing = true;
      emit({ type: 'nowPlaying', media: song });
      window.mock.weather = { place: null, units: 'Metric', error: null, report: null };
      emit({ type: 'weather', ...window.mock.weather });
      setTimeout(() => emit({ type: 'system', rows: [
        { label: 'Processor', values: ['AMD Ryzen 7 5800X 8-Core Processor'] },
        { label: 'Graphics', values: ['NVIDIA GeForce RTX 3070'] },
        { label: 'Motherboard', values: ['ASUS PRIME B550M-A WIFI II'] },
        { label: 'Memory', values: ['32 GB DDR4-3200 (2 × 16 GB) CMK32GX4M2E3200C16'] },
        { label: 'Storage', values: ['Samsung SSD 980 PRO 1TB, 1 TB', 'WD Blue, 2 TB'] },
        { label: 'Operating System', values: ['Windows 11 Pro 25H2 (Build 26300)'] },
      ] }), 500);
    }
    if (message.type === 'pickRegion') setTimeout(() => emit({ type: 'region', x: 100, y: 100, width: 800, height: 450 }), 50);
    if (message.type === 'preview') { theme = message.theme; emit({ type: 'frame', jpeg: render(), stats }); }
    if (message.type === 'select') { window.mock.path = message.path; theme = saved.get(message.path) ?? defaultTheme; state(); }
    if (message.type === 'save') {
      const path = `${message.theme.name}.json`;
      if (message.path && message.path !== path) saved.delete(message.path);
      saved.set(path, message.theme);
      window.mock.path = path;
      theme = message.theme;
      state();
    }
    if (message.type === 'rotate') window.mock.rotation = message.degrees;
    if (message.type === 'startup') { window.mock.startup = message.enabled; state(); }
    if (message.type === 'pickColor') setTimeout(() => emit({ type: 'pickedColor', color: '#12AB34' }), 50);
    if (message.type === 'delete') {
      if (saved.has(message.path)) saved.delete(message.path); else removed.add(message.path);
      state();
    }
    if (message.type === 'restoreDefaults') { removed.clear(); state(); }
    if (message.type === 'export') emit({ type: 'notice', message: 'Theme exported' });
    if (message.type === 'import') {
      saved.set(`Imported ${saved.size}.json`, { ...defaultTheme, name: `Imported ${saved.size}` });
      state();
      emit({ type: 'notice', message: 'Theme imported' });
    }
    if (message.type === 'mediaControl') {
      if (message.action === 'playPause') song.playing = !song.playing;
      if (message.action === 'next') { song.title = 'Next Song'; song.position = 0; }
      setTimeout(() => emit({ type: 'nowPlaying', media: { ...song } }), 80);
    }
    if (message.type === 'weatherSearch') {
      const places = message.query.toLowerCase() === 'nowhere' ? [] : [
        { label: 'London, England, United Kingdom', name: 'London', region: 'England', country: 'United Kingdom', latitude: 51.5, longitude: -0.13 },
        { label: 'London, Ontario, Canada', name: 'London', region: 'Ontario', country: 'Canada', latitude: 42.98, longitude: -81.23 },
      ];
      setTimeout(() => emit({ type: 'weatherResults', query: message.query, places }), 120);
    }
    if (message.type === 'weatherUnits') message = { type: 'weatherPlace', place: null, units: message.units };
    if (message.type === 'weatherPlace') {
      const place = message.place?.label ?? window.mock.weather.place;
      window.mock.weather = { place, units: message.units, error: null, report: null };
      emit({ type: 'weather', ...window.mock.weather });
      const report = message.units === 'Imperial' ? { temperature: 54, high: 56, low: 45 } : { temperature: 12, high: 13, low: 7 };
      setTimeout(() => { window.mock.weather.report = { ...report, description: 'Partly Cloudy', fetched: new Date().toISOString(), stale: false }; emit({ type: 'weather', ...window.mock.weather }); }, 300);
    }
    if (message.type === 'stopMusic') { window.mock.playing = false; emit({ type: 'nowPlaying', media: null }); }
    if (message.type === 'pickMedia') emit({ type: 'media', kind: 'Image', path: 'C:\\Pictures\\wallpaper.jpg' });
  },
};
setInterval(() => {
  if (stats.CpuLoad !== undefined) stats.CpuLoad = Math.round(50 + 40 * Math.sin(Date.now() / 2300));
  emit({ type: 'frame', jpeg: render(), stats });
}, 500);
