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

/* Like ThemeRenderer: bars and shapes turn themselves; the rest turn around ThemeRenderer.Pivot. */
function draw(item) {
  if (item.hidden) return;
  const degrees = item.rotation ?? 0;
  if (degrees % 360 === 0 || item.kind === 'Bar' || item.kind === 'Shape') { drawUpright(item); return; }
  const text = ['Metric', 'Text', 'Clock', 'Date'].includes(item.kind);
  const pivotY = text ? item.y + Math.min(300, Math.max(8, item.size)) * 1.35 / 2 : item.y;
  ctx.save();
  ctx.translate(item.x, pivotY);
  ctx.rotate(degrees * Math.PI / 180);
  ctx.translate(-item.x, -pivotY);
  drawUpright(item);
  ctx.restore();
}

function drawUpright(item) {
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
  emit({ type: 'state', version: '1.7.0', themes, current: theme, currentPath: window.mock.path, rotation: window.mock.rotation ?? 0, startup: window.mock.startup ?? false, removedDefaults: removed.size, playlist: window.mock.playlist ?? { enabled: false, paths: [], seconds: 30 }, alertSettings: window.mock.alertSettings, alerts: window.mock.alerts ?? [], roundOverride: window.mock.roundScreen ?? null, crashReports: window.mock.crashReports ?? null, crashReportsAvailable: true, crashedLastTime: window.mock.crashedLastTime ?? false, screen: { width: screenWidth, height: screenHeight, round: window.mock.roundScreen ?? screenWidth === 480, model: screenWidth === 480 ? 'Core Vision 360' : `${screenWidth}×${screenHeight} Screen` }, fonts: ['Arial', 'Consolas', 'Georgia', 'Impact', 'Segoe UI'], status: { state: 'Connected', text: 'Screen connected' } });
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
    // Updates: a newer release is waiting. Install counts up the download, then shows the installer starting.
    const update = (fields) => { window.mock.update = { ...window.mock.update, ...fields }; emit({ type: 'update', ...window.mock.update }); };
    if (message.type === 'ready') {
      window.mock.update = { current: '1.3.0', automatic: true, state: 'available', version: '1.4.0', progress: 0, error: null };
      update({});
    }
    if (message.type === 'releaseNotes') emit({ type: 'notice', message: `Opens the ${message.version} release on GitHub` });
    if (message.type === 'autoUpdate') update({ automatic: message.enabled });
    if (message.type === 'checkUpdate') {
      update({ state: 'checking' });
      setTimeout(() => update({ state: 'available' }), 600);
    }
    if (message.type === 'installUpdate') {
      let progress = 0;
      const timer = setInterval(() => {
        progress += 20;
        if (progress <= 100) { update({ state: 'downloading', progress }); return; }
        clearInterval(timer);
        update({ state: 'installing' });
      }, 250);
    }
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
      ], sensorDriver: !new URLSearchParams(location.search).has('nopawnio') }), 500); // open with ?nopawnio to see the driver note
    }
    // Fan headers named the way MSI boards name them, with speeds that wobble like live ones.
    const fanHeaders = () => [
      ['/lpc/nct6687d/0/fan/0', 'CPU Fan', 1180], ['/lpc/nct6687d/0/fan/1', 'Pump Fan', 2650],
      ['/lpc/nct6687d/0/fan/2', 'System Fan #1', 920], ['/lpc/nct6687d/0/fan/3', 'System Fan #2', 0],
    ].map(([id, name, rpm]) => ({ id, name, rpm: rpm && Math.round(rpm + (Math.random() - 0.5) * 40) }));
    const guesses = { pump: '/lpc/nct6687d/0/fan/1', cpuFan: '/lpc/nct6687d/0/fan/0', caseFan: '/lpc/nct6687d/0/fan/2' };
    const fans = () => {
      const choice = window.mock.fans ?? {};
      emit({ type: 'fans', headers: fanHeaders(), ...Object.fromEntries(Object.entries(guesses).map(([key, guess]) => [key, { picked: choice[key] ?? null, guess }])) });
    };
    if (message.type === 'listFans') fans();
    if (message.type === 'setFans') { const { type, ...choice } = message; window.mock.fans = choice; fans(); }
    // A Ryzen's built-in graphics listed before the graphics card, as on the PC in the GPU bug report.
    // Set window.mock.singleGpu = true to see Settings without the picker.
    const gpus = () => emit({ type: 'gpus', picked: window.mock.gpu ?? null, gpus: [
      { id: '/gpu-amd/0', name: 'AMD Radeon(TM) Graphics', memoryGb: 0.5, automatic: false },
      { id: '/gpu-amd/1', name: 'AMD Radeon RX 9070 XT', memoryGb: 15.9, automatic: true },
    ].slice(window.mock.singleGpu ? 1 : 0) });
    if (message.type === 'listGpus') gpus();
    if (message.type === 'setGpu') { window.mock.gpu = message.id; gpus(); }
    if (message.type === 'pickRegion') setTimeout(() => emit({ type: 'region', x: 100, y: 100, width: 800, height: 450 }), 50);
    if (message.type === 'listCaptureSources') {
      const thumb = (color, width, height) => `data:image/svg+xml,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${width} ${height}"><rect width="100%" height="100%" fill="${color}"/></svg>`)}`;
      setTimeout(() => emit({
        type: 'captureSources',
        screens: [
          { device: '\\\\.\\DISPLAY1', name: 'Screen 1', primary: true, x: 0, y: 0, width: 2560, height: 1440, thumbnail: thumb('#1d3b6e', 16, 9) },
          { device: '\\\\.\\DISPLAY2', name: 'Screen 2', primary: false, x: 2560, y: 0, width: 1920, height: 1080, thumbnail: thumb('#3b1d6e', 16, 9) },
        ],
        windows: [
          { title: 'YouTube – Google Chrome', process: 'chrome', width: 1600, height: 900, thumbnail: thumb('#6e1d1d', 16, 9) },
          { title: 'Untitled - Notepad', process: 'Notepad', width: 900, height: 700, thumbnail: thumb('#2b2b2b', 9, 7) },
          { title: 'Discord', process: 'Discord', width: 1200, height: 800, thumbnail: null },
        ],
      }), 400);
    }
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
    if (message.type === 'roundScreen') { window.mock.roundScreen = message.round; state(); }
    if (message.type === 'crashReports') { window.mock.crashReports = message.enabled; state(); }
    if (['openLogs', 'reportProblem', 'askQuestion'].includes(message.type)) emit({ type: 'notice', message: { openLogs: 'Log folder opened', reportProblem: 'Bug report form opened in the browser', askQuestion: 'Discussions opened in the browser' }[message.type] });
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
    // Lighting. The ASUS board is driven directly, so it shows as soon as lighting is on; the memory and
    // graphics card arrive a moment later, as if OpenRGB had connected. Open with ?openrgb=Missing, Installed
    // or Running to stay without OpenRGB, and ?lighting=on to start with everything connected.
    // The canvas here is a rough stand-in for the app's: every effect shows as a rainbow, a bar or a flat color.
    const lightEffects = [
      { id: 'solid', name: 'Solid Color', description: 'One color on everything.', params: [{ key: 'color', label: 'Color', kind: 'Color', default: '#00E5FF', min: 0, max: 100 }] },
      { id: 'rainbow', name: 'Rainbow Wave', description: 'The whole color wheel, rolling across your PC.', params: [
        { key: 'speed', label: 'Speed', kind: 'Number', default: '50', min: 0, max: 100 }, { key: 'bands', label: 'Rainbows Across', kind: 'Number', default: '1', min: 1, max: 6 },
        { key: 'vertical', label: 'Top to Bottom', kind: 'Toggle', default: 'false', min: 0, max: 100 }] },
      { id: 'sweep', name: 'Sweep', description: 'A bar that glides from side to side, trailing light behind it.', params: [
        { key: 'color', label: 'Color', kind: 'Color', default: '#FF0066', min: 0, max: 100 }, { key: 'random', label: 'New Color Each Pass', kind: 'Toggle', default: 'false', min: 0, max: 100 },
        { key: 'speed', label: 'Speed', kind: 'Number', default: '50', min: 0, max: 100 }, { key: 'trail', label: 'Trail', kind: 'Number', default: '50', min: 0, max: 100 },
        { key: 'width', label: 'Bar Width', kind: 'Number', default: '12', min: 2, max: 60 }, { key: 'vertical', label: 'Top to Bottom', kind: 'Toggle', default: 'false', min: 0, max: 100 },
        { key: 'background', label: 'Background Color', kind: 'Color', default: '#101014', min: 0, max: 100 }] },
      { id: 'aurora', name: 'Aurora', description: 'Slow ribbons of color that never quite repeat.', params: [
        { key: 'color', label: 'Color 1', kind: 'Color', default: '#00FFC8', min: 0, max: 100 }, { key: 'speed', label: 'Speed', kind: 'Number', default: '50', min: 0, max: 100 }] },
    ];
    const light = window.mock.lighting ??= {
      enabled: false, startOpenRgb: true, state: 'Off', openRgb: 'Installed', openRgbUp: false,
      headers: { 'Addressable RGB Header 1': 0 }, own: {}, parts: {},
      canvas: { effect: 'rainbow', brightness: 100, width: 320, height: 200, placements: {}, effectPresets: [], layoutPresets: [],
        values: Object.fromEntries(lightEffects.map((effect) => [effect.id, Object.fromEntries(effect.params.map((param) => [param.key, param.default]))])) },
      savedEffects: {}, savedLayouts: {},
    };
    const lightModes = [{ name: 'Breathing', hasSpeed: true, colors: 1 }, { name: 'Rainbow', hasSpeed: true, colors: 0 }];
    const lightDevices = () => [
      { key: 'ASUS PRIME B550M-A WIFI II|AuraUSB 1939', name: 'ASUS PRIME B550M-A WIFI II', kind: 'Motherboard', modes: [...lightModes, { name: 'Spectrum Cycle', hasSpeed: false, colors: 0 }],
        zones: [{ name: 'Aura Mainboard', ledCount: 2, resizable: false, maxLeds: 2 }, { name: 'Addressable RGB Header 1', ledCount: light.headers['Addressable RGB Header 1'], resizable: true, maxLeds: 500 }] },
      ...[1, 2, 3, 4].map((stick) => ({ key: `ENE DRAM|AMD SMBus port 0|Stick ${stick}`, name: `RGB Memory ${stick}`, kind: 'Dram', modes: lightModes, zones: [{ name: 'DRAM', ledCount: 8, resizable: false, maxLeds: 8 }] })),
      { key: 'MSI GeForce RTX 4070|I2C 0x68', name: 'MSI GeForce RTX 4070 Ventus', kind: 'Gpu', modes: [], zones: [{ name: 'GPU', ledCount: 1, resizable: false, maxLeds: 1 }] },
    ].filter((device) => light.openRgbUp || device.kind === 'Motherboard' || (light.memory && device.kind === 'Dram'))
      .map((device) => ({ ...device, ledCount: device.zones.reduce((sum, zone) => sum + zone.ledCount, 0), override: light.own[device.key] ?? null,
        zones: device.zones.map((zone) => ({ ...zone, key: `${device.key}|${zone.name}`, parts: light.parts[`${device.key}|${zone.name}`] ?? [] })) }))
      .map((device) => ({ ...device, boxes: device.zones.flatMap(splitZone) }));
    // Like LightParts.Split: a divided strip shows as its parts, and LEDs past the last part as "Rest".
    const splitZone = (zone) => {
      if (!zone.parts.length || !zone.ledCount) return [{ key: zone.key, zone: zone.name, name: zone.name, ledCount: zone.ledCount, shape: 'Strip' }];
      const boxes = [];
      let left = zone.ledCount;
      zone.parts.forEach((part) => {
        if (left <= 0) return;
        const leds = Math.min(part.leds, left);
        boxes.push({ key: `${zone.key}#${boxes.length + 1}`, zone: zone.name, name: part.name, ledCount: leds, shape: part.shape });
        left -= leds;
      });
      if (left > 0) boxes.push({ key: `${zone.key}#${boxes.length + 1}`, zone: zone.name, name: 'Rest', ledCount: left, shape: 'Strip' });
      return boxes;
    };
    // Like LightPlacement.Arrange: a row for each strip, as wide as its LED count suggests, and rings along the bottom.
    const arrange = (devices) => {
      const all = devices.flatMap((device) => device.boxes);
      const rings = Object.fromEntries(all.filter((box) => box.shape === 'Ring').map((box, index) =>
        [box.key, light.canvas.placements[box.key] ?? { x: 12 + (index % 6) * 48, y: 148 - Math.floor(index / 6) * 48, width: 40, height: 40, rotation: 0, brightness: 100 }]));
      const zones = all.filter((box) => box.shape !== 'Ring');
      const ringRows = Math.ceil(Object.keys(rings).length / 6);
      const step = Math.min(22, Math.max(4, (176 - ringRows * 48) / Math.max(1, zones.length)));
      const most = Math.max(1, ...zones.map((zone) => zone.ledCount));
      return Object.assign(rings, Object.fromEntries(zones.map((zone, index) => {
        const width = Math.round(Math.min(296, Math.max(60, 296 * zone.ledCount / most)));
        return [zone.key, light.canvas.placements[zone.key] ?? { x: Math.round((320 - width) / 2), y: Math.round(12 + index * step), width, height: Math.round(Math.max(4, step - 6)), rotation: 0, brightness: 100 }];
      })));
    };
    const lighting = () => {
      const devices = light.state === 'Connected' ? lightDevices() : [];
      emit({ type: 'lighting', enabled: light.enabled, startOpenRgb: light.startOpenRgb, memory: Boolean(light.memory), asrock: Boolean(light.asrock), gpu: Boolean(light.gpu), state: light.state,
        openRgbConnected: light.openRgbUp, openRgb: light.openRgbUp ? 'Running' : light.openRgb, effects: lightEffects, devices,
        conflicts: new URLSearchParams(location.search).getAll('conflict'),
        canvas: structuredClone({ ...light.canvas, placements: arrange(devices) }) });
    };
    const connectLighting = () => {
      const stuck = new URLSearchParams(location.search).get('openrgb');
      light.state = 'Connected';
      light.openRgbUp = false;
      if (stuck) { light.openRgb = stuck; lighting(); return; }
      lighting();
      setTimeout(() => { if (light.enabled) { light.openRgbUp = true; lighting(); } }, 1200);
    };
    if (message.type === 'ready' && new URLSearchParams(location.search).get('lighting') === 'on') { light.enabled = true; light.state = 'Connected'; light.openRgbUp = true; light.memory = true; }
    if (message.type === 'ready' || message.type === 'lighting') lighting();
    if (message.type === 'lightingEnabled') {
      light.enabled = message.enabled;
      if (message.enabled) connectLighting(); else { light.state = 'Off'; light.openRgbUp = false; lighting(); }
    }
    if (message.type === 'retryLighting') connectLighting();
    if (message.type === 'asrockLighting') { light.asrock = message.enabled; setTimeout(lighting, 300); }
    if (message.type === 'gpuLighting') { light.gpu = message.enabled; setTimeout(lighting, 300); }
    if (message.type === 'memoryLighting') { light.memory = message.enabled; setTimeout(lighting, 300); }
    if (message.type === 'startOpenRgb') light.startOpenRgb = message.enabled;
    if (message.type === 'openRgbSite') emit({ type: 'notice', message: 'openrgb.org opened in the browser' });
    if (message.type === 'zoneParts') {
      const key = `${message.device}|${message.zone}`;
      if (message.parts.length) light.parts[key] = message.parts.slice(0, 16); else delete light.parts[key];
      Object.keys(light.canvas.placements).filter((placed) => placed.startsWith(`${key}#`)).forEach((placed) => delete light.canvas.placements[placed]);
      lighting();
    }
    if (message.type === 'headerLeds') { light.headers[message.zone] = message.leds; lighting(); }
    if (message.type === 'setLighting') { if (message.choice) light.own[message.device] = message.choice; else delete light.own[message.device]; }
    if (message.type === 'canvasEffect') { light.canvas.effect = message.effect; lighting(); }
    if (message.type === 'effectValue') light.canvas.values[message.effect][message.key] = message.value;
    if (message.type === 'canvasBrightness') light.canvas.brightness = message.brightness;
    if (message.type === 'placement') { const { type, zone, ...place } = message; light.canvas.placements[zone] = place; }
    if (message.type === 'resetLayout') { light.canvas.placements = {}; lighting(); }
    if (message.type === 'saveEffectPreset') {
      light.savedEffects[message.name] = { effect: light.canvas.effect, values: structuredClone(light.canvas.values[light.canvas.effect]) };
      light.canvas.effectPresets = Object.entries(light.savedEffects).map(([name, saved]) => ({ name, effect: lightEffects.find((effect) => effect.id === saved.effect).name }));
      lighting();
    }
    if (message.type === 'loadEffectPreset' && light.savedEffects[message.name]) {
      const saved = light.savedEffects[message.name];
      light.canvas.effect = saved.effect;
      light.canvas.values[saved.effect] = structuredClone(saved.values);
      lighting();
    }
    if (message.type === 'deleteEffectPreset') {
      delete light.savedEffects[message.name];
      light.canvas.effectPresets = light.canvas.effectPresets.filter((preset) => preset.name !== message.name);
      lighting();
    }
    if (message.type === 'saveLayoutPreset') {
      light.savedLayouts[message.name] = structuredClone(arrange(lightDevices()));
      light.canvas.layoutPresets = Object.keys(light.savedLayouts);
      lighting();
    }
    if (message.type === 'loadLayoutPreset' && light.savedLayouts[message.name]) { Object.assign(light.canvas.placements, structuredClone(light.savedLayouts[message.name])); lighting(); }
    if (message.type === 'deleteLayoutPreset') { delete light.savedLayouts[message.name]; light.canvas.layoutPresets = Object.keys(light.savedLayouts); lighting(); }

    const hueColor = (turn) => { const f = (n) => { const k = (n + (turn - Math.floor(turn)) * 6) % 6; return 255 * (1 - Math.max(0, Math.min(k, 4 - k, 1))); }; return [f(5), f(3), f(1)]; };
    const hexColor = (hex) => [1, 3, 5].map((start) => parseInt(hex.slice(start, start + 2), 16));
    const mockCanvasColor = (effect, x, y, seconds) => {
      const values = light.canvas.values[effect];
      const along = values.vertical === 'true' ? y : x;
      const pace = 0.04 * 2 ** (Number(values.speed ?? 50) / 20);
      if (effect === 'solid') return hexColor(values.color);
      if (effect === 'sweep') {
        const width = Number(values.width) / 100;
        const center = width / 2 + (1 - width) * (0.5 - 0.5 * Math.cos(seconds * pace * Math.PI * 2));
        return Math.abs(along - center) <= width / 2 ? hexColor(values.color) : hexColor(values.background);
      }
      if (effect === 'aurora') { const level = 0.5 + 0.5 * Math.sin((along + seconds * pace) * Math.PI * 2); return hexColor(values.color).map((channel) => channel * level); }
      return hueColor(along * Number(values.bands ?? 1) - seconds * pace);
    };
    const mockPixels = (effect, seconds, width, height, level) => {
      const bytes = new Uint8Array(width * height * 3);
      for (let row = 0; row < height; row += 1) for (let column = 0; column < width; column += 1) {
        bytes.set(mockCanvasColor(effect, (column + 0.5) / width, (row + 0.5) / height, seconds).map((channel) => channel * level), (row * width + column) * 3);
      }
      let text = '';
      for (let at = 0; at < bytes.length; at += 8192) text += String.fromCharCode(...bytes.subarray(at, at + 8192));
      return btoa(text);
    };
    if (message.type === 'lightingThumbs') {
      emit({ type: 'lightingThumbs', width: 96, height: 60, thumbs: Object.fromEntries(lightEffects.map((effect) => [effect.id, mockPixels(effect.id, 1.3, 96, 60, 1)])) });
    }
    if (message.type === 'watchLighting') {
      clearInterval(window.mock.lightTimer);
      if (message.watching) window.mock.lightTimer = setInterval(() => {
        if (light.state !== 'Connected') return;
        const seconds = Date.now() / 1000;
        const devices = lightDevices();
        const places = arrange(devices);
        const colors = {};
        devices.forEach((device) => {
          if (device.override?.kind === 'DeviceEffect') return;
          colors[device.key] = device.boxes.map((zone) => {
            const place = places[zone.key];
            const wide = place.width >= place.height;
            const turn = place.rotation * Math.PI / 180;
            return Array.from({ length: zone.ledCount }, (_, led) => {
              const along = ((led + 0.5) / zone.ledCount - 0.5) * (wide ? place.width : place.height);
              const angle = led * Math.PI * 2 / zone.ledCount - Math.PI / 2;
              const [lx, ly] = zone.shape === 'Ring' ? [Math.cos(angle) * place.width / 2, Math.sin(angle) * place.height / 2] : wide ? [along, 0] : [0, along];
              const x = Math.min(1, Math.max(0, (place.x + place.width / 2 + lx * Math.cos(turn) - ly * Math.sin(turn)) / 320));
              const y = Math.min(1, Math.max(0, (place.y + place.height / 2 + lx * Math.sin(turn) + ly * Math.cos(turn)) / 200));
              const level = device.override?.kind === 'Off' ? 0 : light.canvas.brightness / 100 * place.brightness / 100;
              return mockCanvasColor(light.canvas.effect, x, y, seconds).map((channel) => Math.round(channel * level).toString(16).padStart(2, '0')).join('');
            }).join('');
          }).join('');
        });
        emit({ type: 'lightingFrame', colors, width: 320, height: 200, canvas: mockPixels(light.canvas.effect, seconds, 320, 200, light.canvas.brightness / 100) });
      }, 66);
    }
    if (message.type === 'stopMusic') { window.mock.playing = false; emit({ type: 'nowPlaying', media: null }); }
    if (message.type === 'pickMedia') emit({ type: 'media', kind: 'Image', path: 'C:\\Pictures\\wallpaper.jpg' });
  },
};
setInterval(() => {
  if (stats.CpuLoad !== undefined) stats.CpuLoad = Math.round(50 + 40 * Math.sin(Date.now() / 2300));
  emit({ type: 'frame', jpeg: render(), stats });
}, 500);
