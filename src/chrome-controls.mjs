export function createChromeControls(reader, language) {
  const elements = [...document.querySelectorAll('[data-chrome-icon]')];
  const buttons = ['new', 'open', 'outline-toggle', 'appearance', 'more'].map(id => document.getElementById(id));
  let key, loading;
  const align = () => {
    for (const el of [...buttons, ...elements]) {
      el.style.left = el.style.top = '0px';
      const rect = el.getBoundingClientRect(), dpr = devicePixelRatio;
      el.style.left = `${Math.round(rect.x * dpr) / dpr - rect.x}px`;
      el.style.top = `${Math.round(rect.y * dpr) / dpr - rect.y}px`;
    }
  };
  const load = () => {
    const nextKey = `${devicePixelRatio}:${language()}`;
    if (key === nextKey) { align(); return loading; }
    key = nextKey;
    const requested = devicePixelRatio;
    loading = reader.chromeControls(requested).then(async ({ icons, labels, frames, style }) => {
      await Promise.all([...icons.map(asset => asset.url), ...labels.map(asset => asset.url), ...frames.flatMap(frame => [frame.fill, frame.border])].map(url => { const image = new Image(); image.src = url; return image.decode(); }));
      if (nextKey !== key) return;
      for (const [name, value] of Object.entries(style)) document.documentElement.style.setProperty(`--chrome-${name}`, typeof value === 'number' ? `${value}px` : value);
      for (const label of labels) {
        const button = document.getElementById(label.id);
        button.style.width = `${label.width}px`;
        button.style.fontSize = `${label.fontSize}px`;
        button.style.setProperty('--chrome-label', `url("${label.url}")`);
      }
      for (const frame of frames) {
        const button = document.getElementById(frame.id);
        button.style.setProperty('--chrome-fill-mask', `url("${frame.fill}")`);
        button.style.setProperty('--chrome-border-mask', `url("${frame.border}")`);
      }
      for (const el of elements) {
        const icon = icons.find(icon => icon.name === el.dataset.chromeIcon);
        el.style.width = el.style.height = `${icon.pixels / requested}px`;
        el.style.maskImage = `url("${icon.url}")`;
      }
      align();
    });
    return loading;
  };
  window.addEventListener('resize', () => { load().catch(console.error); });
  return { ready: load(), align, refresh: load };
}
