// Reuse unchanged blocks to retain image loads, open details and code scroll.
// Source line numbers can change without changing the block's identity.
const signatures = new WeakMap();
const markers = ['data-source-line', 'data-source-end', 'data-math-id', 'data-diagram-id'];
const selector = markers.map(name => `[${name}]`).join(',');
const marked = node => node.nodeType === 1 ? [...(node.matches(selector) ? [node] : []), ...node.querySelectorAll(selector)] : [];
function signature(node, result, theme) {
  if (node.nodeType !== 1) return node.nodeType + ':' + node.textContent;
  const math = [], diagrams = [];
  for (const child of marked(node)) {
    if (child.hasAttribute('data-math-id')) math.push(result.formulas[Number(child.dataset.mathId)]);
    if (child.hasAttribute('data-diagram-id')) diagrams.push(result.diagrams[Number(child.dataset.diagramId)]);
  }
  return JSON.stringify([node.outerHTML.replace(/\sdata-(?:source-line|source-end|math-id|diagram-id)="[^"]*"/g, ''), math, diagrams, diagrams.length ? theme : null]);
}
function updateMarkers(live, fresh) {
  const before = marked(live), after = marked(fresh);
  for (let i = 0; i < after.length; i++) {
    if (!before[i]) break;
    for (const name of markers) {
      const value = after[i].getAttribute(name);
      if (value !== null && before[i].getAttribute(name) !== value) before[i].setAttribute(name, value);
    }
  }
}
function compatible(a, b) { return a?.nodeType === b.nodeType && a.nodeName === b.nodeName; }
function patchNode(live, fresh) {
  if (live.nodeType !== 1) { if (live.nodeValue !== fresh.nodeValue) live.nodeValue = fresh.nodeValue; return live; }
  // A changed diagram is already complete. Its generated IDs must stay together.
  if (live.namespaceURI !== 'http://www.w3.org/1999/xhtml') { live.replaceWith(fresh); return fresh; }
  const detailsOpen = live.tagName === 'DETAILS' ? live.open : null;
  for (const attribute of [...live.attributes]) if (!fresh.hasAttribute(attribute.name) && !(detailsOpen !== null && attribute.name === 'open')) live.removeAttribute(attribute.name);
  for (const attribute of fresh.attributes) if (live.getAttribute(attribute.name) !== attribute.value) live.setAttribute(attribute.name, attribute.value);
  if (detailsOpen !== null) live.open = detailsOpen;
  let cursor = live.firstChild;
  for (const child of [...fresh.childNodes]) {
    if (!cursor) { live.append(child); continue; }
    const next = cursor.nextSibling;
    if (compatible(cursor, child)) patchNode(cursor, child); else cursor.replaceWith(child);
    cursor = next;
  }
  while (cursor) { const next = cursor.nextSibling; cursor.remove(); cursor = next; }
  return live;
}
export function preparePreviewPatch(article, fragment, result, theme) {
  const oldNodes = [...article.childNodes], byKey = new Map(), used = new Set();
  for (const node of oldNodes) {
    const key = signatures.get(node); if (key === undefined) continue;
    if (!byKey.has(key)) byKey.set(key, { nodes: [], index: 0 });
    byKey.get(key).nodes.push(node);
  }
  const plan = [...fragment.childNodes].map(fresh => {
    const key = signature(fresh, result, theme), bucket = byKey.get(key);
    const live = bucket?.nodes[bucket.index++];
    if (live) used.add(live);
    return { fresh, key, live, unchanged: Boolean(live) };
  });
  const remaining = oldNodes.filter(node => !used.has(node)); let index = 0;
  const changes = document.createDocumentFragment();
  for (const item of plan) {
    if (item.unchanged) continue;
    const candidate = remaining[index++];
    item.live = candidate;
    changes.append(item.fresh);
  }
  return { changes, commit() {
    const prepared = [...changes.childNodes]; let preparedIndex = 0;
    let cursor = article.firstChild;
    for (const item of plan) {
      if (!item.unchanged) item.fresh = prepared[preparedIndex++];
      let node = item.live;
      if (item.unchanged) updateMarkers(node, item.fresh);
      else if (compatible(node, item.fresh)) node = patchNode(node, item.fresh);
      else node = item.fresh;
      signatures.set(node, item.key);
      if (node !== cursor) article.insertBefore(node, cursor);
      cursor = node.nextSibling;
    }
    while (cursor) { const next = cursor.nextSibling; cursor.remove(); cursor = next; }
  } };
}
