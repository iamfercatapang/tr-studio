// TR Studio's own icon set, copied from src/CoreVision.App/wwwroot/app.js (ICONS):
// 16px viewBox, 1.5 stroke, round caps. The demo uses no other glyphs.
const PATHS = {
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
  Music: '<path d="M6 12.5V3.5l7.5-1.5v9"/><circle cx="4.25" cy="12.5" r="1.75"/><circle cx="11.75" cy="11" r="1.75"/>',
  Weather: '<circle cx="5.5" cy="5.5" r="2.25"/><path d="M5.5 1v1M1 5.5h1M2.3 2.3l.7.7M8.7 2.3l-.7.7"/><path d="M5.5 14h6.25a2.75 2.75 0 1 0-.6-5.43A3.75 3.75 0 0 0 4 9.5 2.25 2.25 0 0 0 5.5 14z"/>',
  Undo: '<path d="M2.75 8a5.25 5.25 0 1 0 1.55-3.7"/><path d="M2.5 2v3h3"/>',
  Check: '<path d="M3 8.5l3 3 7-7"/>',
}

export type IconName = keyof typeof PATHS

export function AppIcon({ name, size = 16, className }: { name: IconName; size?: number; className?: string }) {
  return (
    <svg className={className ? `trs-icon ${className}` : 'trs-icon'} width={size} height={size} viewBox="0 0 16 16" aria-hidden="true"
      dangerouslySetInnerHTML={{ __html: PATHS[name] }} />
  )
}
