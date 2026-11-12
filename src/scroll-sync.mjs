export function createScrollSync({ article, preview, getEditor, isActive }) {
  let map = null, leader = 'source', rendering = false, ticket = 0, frame = 0, timer;
  let expectedSource = null, expectedPreview = null;
  let holdAlignment = false, pendingScroll = false;
  const limits = () => {
    const editor = getEditor();
    return { source: Math.max(0, editor.scrollHeight - editor.clientHeight), preview: Math.max(0, preview.scrollHeight - preview.clientHeight) };
  };
  function ready() { return !rendering && isActive() && getEditor()?.clientWidth > 0; }
  function measureLines(lines) {
    return getEditor().measureLines(lines) || new Map();
  }
  function build() {
    if (!ready()) return null;
    const editor = getEditor(), max = limits(), candidates = new Map();
    // Compare blocks on the same screen line, including any offset between
    // the source and preview viewports from their headers and padding.
    const origin = editor.getBoundingClientRect().top - preview.scrollTop;
    const blocks = [];
    for (const node of article.querySelectorAll('[data-source-line]')) {
      if (node.closest('.footnotes') || !node.getClientRects().length) continue;
      const closed = node.closest('details:not([open])');
      if (closed && closed !== node) continue;
      blocks.push({ node, rect: node.getBoundingClientRect() });
    }
    // End anchors improve interpolation inside long code, tables and paragraphs.
    for (const end of [true, false]) {
      for (const { node, rect } of blocks) {
        const line = Number(end ? node.dataset.sourceEnd : node.dataset.sourceLine);
        if (!Number.isInteger(line) || line < 0) continue;
        if (end && (node.classList.contains('source-anchor') || rect.height < 1)) continue;
        candidates.set(line, Math.max(0, (end ? rect.bottom : rect.top) - origin));
      }
    }
    const measured = measureLines([...candidates.keys()]);
    const points = [{ source: 0, preview: 0 }];
    for (const [line, y] of [...candidates].sort((a, b) => a[0] - b[0])) {
      const x = measured.get(line), previous = points.at(-1);
      if (x <= previous.source + .5 || x >= max.source - .5 || y < previous.preview || y >= max.preview - .5) continue;
      points.push({ source: x, preview: y });
    }
    points.push(max);
    map = { editor, points, max };
    return map;
  }
  function mappedPosition(position, from, to, data) {
    if (position <= .5 || data.max[from] === 0) return 0;
    if (position >= data.max[from] - .5) return data.max[to];
    let low = 0, high = data.points.length - 1;
    while (high - low > 1) {
      const mid = (low + high) >> 1;
      if (data.points[mid][from] <= position) low = mid; else high = mid;
    }
    const a = data.points[low], b = data.points[high], span = b[from] - a[from];
    return span > .01 ? a[to] + (b[to] - a[to]) * (position - a[from]) / span : b[to];
  }
  function align(from = leader) {
    if (!ready()) return;
    const max = limits();
    if (!map || map.editor !== getEditor() || map.max.source !== max.source || map.max.preview !== max.preview) build();
    if (!map) return;
    const editor = getEditor(), to = from === 'source' ? 'preview' : 'source';
    const source = from === 'source' ? editor : preview;
    const target = to === 'source' ? editor : preview;
    const position = Math.max(0, Math.min(map.max[to], mappedPosition(source.scrollTop, from, to, map)));
    if (Math.abs(target.scrollTop - position) < 1) return;
    if (to === 'source') expectedSource = position; else expectedPreview = position;
    target.scrollTop = position;
    if (to === 'source') expectedSource = target.scrollTop; else expectedPreview = target.scrollTop;
  }
  function scrolled(from, field) {
    if (!getEditor() || (from === 'source' && field !== getEditor())) return;
    const element = from === 'source' ? getEditor() : preview;
    const expected = from === 'source' ? expectedSource : expectedPreview;
    if (from === 'source') expectedSource = null; else expectedPreview = null;
    if (expected !== null && Math.abs(element.scrollTop - expected) < 1.5) return;
    leader = from; holdAlignment = false; pendingScroll = true;
    if (!ready()) return;
    cancelAnimationFrame(frame); frame = requestAnimationFrame(() => { if (ready()) { align(from); pendingScroll = false; } });
  }
  function invalidate(nextLeader) {
    if (nextLeader) leader = nextLeader;
    map = null; clearTimeout(timer);
    timer = setTimeout(() => { build(); if (!holdAlignment && ready()) { align(); pendingScroll = false; } }, 75);
  }
  const observer = new ResizeObserver(() => invalidate());
  observer.observe(article); observer.observe(preview);
  preview.addEventListener('scroll', () => scrolled('preview'));
  preview.addEventListener('wheel', () => { leader = 'preview'; }, { passive: true });
  preview.addEventListener('pointerdown', () => { leader = 'preview'; });
  article.addEventListener('load', () => invalidate(), true);
  article.addEventListener('toggle', () => invalidate(), true);
  document.fonts.ready.then(() => invalidate());
  return {
    watch(field) {
      observer.observe(field);
      field.addEventListener('scroll', () => scrolled('source', field));
      field.addEventListener('editor-layout', () => { if (field === getEditor()) invalidate(); });
      for (const event of ['wheel', 'pointerdown', 'keydown']) field.addEventListener(event, () => { if (field === getEditor()) leader = 'source'; }, { passive: true });
    },
    unwatch(field) { observer.unobserve(field); },
    invalidate,
    reset() { map = null; expectedSource = expectedPreview = null; holdAlignment = pendingScroll = false; cancelAnimationFrame(frame); clearTimeout(timer); leader = 'source'; },
    freeze() { rendering = true; map = null; cancelAnimationFrame(frame); return ++ticket; },
    // Layout observers must not undo the viewport anchor after an edit. A real
    // scroll (including the editor following its caret) resumes synchronization.
    thaw(value, { preserve = false } = {}) { if (value === ticket) { rendering = false; holdAlignment = preserve && !pendingScroll; expectedSource = getEditor()?.scrollTop ?? null; expectedPreview = preview.scrollTop; invalidate(); } },
  };
}
