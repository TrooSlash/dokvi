import DOMPurify from 'dompurify';
import { parseMarkdown, releaseParser } from './markdown-client.mjs';
import { sanitizeMarkdown } from './sanitize.mjs';
import { TextHistory } from './history.mjs';
import { createScrollSync } from './scroll-sync.mjs';
import { preparePreviewPatch } from './preview-patch.mjs';
import { formatMarkdown } from './formatting.mjs';
import { t, getLanguage, setLanguage, resolveLanguage, bindStaticTranslations } from './i18n.mjs';
import { createNativeMenus } from './menu-presenter.mjs';
import { createChromeControls } from './chrome-controls.mjs';

const $ = id => document.getElementById(id);
const systemLanguage = window.__nativeSystemLanguage || navigator.language;
setLanguage(localStorage.getItem('language'), systemLanguage);
const translateStaticUI = bindStaticTranslations(document.body);
translateStaticUI();
const article = $('document');
const workspace = $('workspace');
const readPane = $('reading-pane');
$('license-text').textContent = DOKVI_LICENSE_TEXT;
$('app-version').textContent = DOKVI_VERSION;
$('author-link').onclick = event => { event.preventDefault(); operation(() => window.reader.link(null, 'https://github.com/TrooSlash')); };
$('support-author').onclick = event => { event.preventDefault(); operation(() => window.reader.link(null, 'https://tips.tips/000491268')); };
let editor = null, busy = false, previewTimer, previewDeadline, editorModule;
const nativeMenus = createNativeMenus(window.reader, () => !busy && !exporting);
const chromeControls = createChromeControls(window.reader, getLanguage);
const splitResizer = $('split-resizer');
const mainPane = $('main');
let preferredSplit = Number(localStorage.getItem('editor-split')) || 42;
preferredSplit = Math.max(30, Math.min(70, preferredSplit));
let splitDrag = null;
function splitLimits() {
  const width = mainPane.getBoundingClientRect().width - 9;
  return { width, min: Math.max(30, 240 / width * 100), max: Math.min(70, (width - 260) / width * 100) };
}
function updateSplit() {
  const limits = splitLimits(); if (limits.width < 500) return;
  const ratio = Math.max(limits.min, Math.min(limits.max, preferredSplit));
  workspace.style.setProperty('--editor-width', (limits.width * ratio / 100) + 'px');
  splitResizer.setAttribute('aria-valuemin', String(Math.round(limits.min)));
  splitResizer.setAttribute('aria-valuemax', String(Math.round(limits.max)));
  splitResizer.setAttribute('aria-valuenow', String(Math.round(ratio)));
  splitResizer.setAttribute('aria-valuetext', t('Исходник {0}%, предпросмотр {1}%', Math.round(ratio), Math.round(100 - ratio)));
}
function setSplit(ratio, persist = true) {
  const limits = splitLimits();
  preferredSplit = Math.max(limits.min, Math.min(limits.max, ratio));
  updateSplit();
  if (persist) localStorage.setItem('editor-split', String(preferredSplit));
}
function finishSplitDrag(cancel = false) {
  if (!splitDrag) return;
  const drag = splitDrag; splitDrag = null;
  if (cancel) preferredSplit = drag.original;
  updateSplit(); localStorage.setItem('editor-split', String(preferredSplit));
  document.body.classList.remove('resizing-editor');
  if (splitResizer.hasPointerCapture(drag.pointerId)) splitResizer.releasePointerCapture(drag.pointerId);
}
splitResizer.addEventListener('pointerdown', event => {
  if (event.button !== 0 || mode !== 'split' || busy || exporting) return;
  event.preventDefault(); splitResizer.focus({ preventScroll: true });
  splitDrag = { pointerId: event.pointerId, original: preferredSplit };
  splitResizer.setPointerCapture(event.pointerId);
  document.body.classList.add('resizing-editor');
});
splitResizer.addEventListener('pointermove', event => {
  if (splitDrag?.pointerId !== event.pointerId) return;
  const bounds = mainPane.getBoundingClientRect();
  setSplit((event.clientX - bounds.left - 4.5) / (bounds.width - 9) * 100, false);
});
splitResizer.addEventListener('pointerup', () => finishSplitDrag());
splitResizer.addEventListener('pointercancel', () => finishSplitDrag(true));
splitResizer.addEventListener('lostpointercapture', () => finishSplitDrag());
splitResizer.addEventListener('dblclick', () => setSplit(42));
splitResizer.addEventListener('keydown', event => {
  const step = event.shiftKey ? 10 : 2;
  const current = Number(splitResizer.getAttribute('aria-valuenow'));
  const values = { ArrowLeft: current - step, ArrowRight: current + step, Home: 0, End: 100, Enter: 42 };
  if (event.key in values) { event.preventDefault(); event.stopPropagation(); setSplit(values[event.key]); }
  if (event.key === 'Escape' && splitDrag) { event.preventDefault(); finishSplitDrag(true); }
});
window.addEventListener('blur', () => finishSplitDrag());
new ResizeObserver(updateSplit).observe(mainPane);
let current = null;
const tabs = [];
let sessionReady = false, sessionTimer, sessionDeadline, sessionFlight = Promise.resolve(), sessionFailed = false;
const sessionSent = new Map();
function scheduleSession() {
  if (!sessionReady || busy || exporting) return;
  clearTimeout(sessionTimer);
  const run = () => { flushSession().catch(() => {}); };
  sessionTimer = setTimeout(run, 550);
  sessionDeadline ||= setTimeout(run, 2000);
}
async function flushSession({ exclude = new Set(), discard = new Set() } = {}) {
  if (!sessionReady) return;
  clearTimeout(sessionTimer); clearTimeout(sessionDeadline); sessionDeadline = null;
  const work = sessionFlight.catch(() => {}).then(async () => {
    saveActiveState();
    const sent = new Map();
    const positions = tabs.filter(entry => !exclude.has(entry.doc.id) && !(entry.doc.untitled && entry.history.value === '') && !(discard.has(entry.doc.id) && (entry.doc.untitled || entry.doc.demo))).map(entry => {
      const id = entry.doc.id, forgotten = discard.has(id);
      const position = { id, mode: entry.mode, readTop: entry.readTop, sourceTop: entry.sourceTop, start: entry.editor.selectionStart, end: entry.editor.selectionEnd, forgetDraft: forgotten };
      if (forgotten || sessionSent.get(id) !== entry.sessionVersion) position.text = forgotten ? entry.savedText : entry.editor.value;
      sent.set(id, entry.sessionVersion); return position;
    });
    await window.reader.checkpoint({ version: 1, activeId: current?.id || null, tabs: positions });
    sessionSent.clear(); for (const [id, version] of sent) sessionSent.set(id, version);
    sessionFailed = false;
  });
  sessionFlight = work;
  try { await work; }
  catch (error) {
    if (!sessionFailed) toast(t('Не удалось сохранить резервную копию сессии. Сохраните важный текст через Ctrl+S. ') + error.message, true);
    sessionFailed = true;
    if (sessionReady) sessionTimer = setTimeout(() => flushSession().catch(() => {}), 5000);
    throw error;
  }
}
window.addEventListener('blur', () => { if (sessionReady && !busy && !exporting) flushSession().catch(() => {}); });
let activationRequest = 0;
let deferredDocuments = [];
let mode = 'read', fontSize = Number(localStorage.getItem('font-size')) || 16, generation = 0;
let diagramData = [], mermaidPromise, katexPromise, headings = [], searchRanges = [], searchIndex = -1, toastTimer, scrollFrame, searchTimer;
let headingOffsets = null, outlineButtons = [], activeHeading = -1, searchWindow = null;
let theme = localStorage.getItem('theme') || 'light';
let readingLayout = localStorage.getItem('reading-layout') || 'left';
if (!['left', 'center', 'full'].includes(readingLayout)) readingLayout = 'left';
let readingWidth = Math.max(50, Math.min(100, Number(localStorage.getItem('reading-width')) || 80));
const scrollSync = createScrollSync({ article, preview: readPane, getEditor: () => editor, isActive: () => mode === 'split' && !exporting && activeEntry()?.previewText === activeEntry()?.history.value });
function readingAnchor() {
  if (!current || mode === 'source') return null;
  const top = readPane.getBoundingClientRect().top + 24;
  let found = null, closest = -Infinity;
  for (const node of article.querySelectorAll('[data-source-line]')) {
    if (!node.getClientRects().length || node.closest('.footnotes')) continue;
    const y = node.getBoundingClientRect().top;
    if (y <= top && y > closest) { found = node; closest = y; }
  }
  return found ? { node: found, top: closest } : null;
}
function restoreReadingAnchor(anchor) {
  if (anchor?.node.isConnected) readPane.scrollTop += anchor.node.getBoundingClientRect().top - anchor.top;
}
function applyReadingLayout(persist = false) {
  const anchor = mode === 'read' ? readingAnchor() : null;
  workspace.dataset.readingLayout = readingLayout;
  workspace.style.setProperty('--reading-width', readingWidth + '%');
  for (const value of ['left', 'center', 'full']) $('reading-' + value).setAttribute('aria-pressed', String(value === readingLayout));
  $('reading-width').value = readingLayout === 'full' ? 100 : readingWidth;
  $('reading-width-value').textContent = $('reading-width').value + '%';
  restoreReadingAnchor(anchor);
  if (persist) { localStorage.setItem('reading-layout', readingLayout); localStorage.setItem('reading-width', String(readingWidth)); }
}
fontSize = Math.max(13, Math.min(25, fontSize));
document.documentElement.dataset.theme = theme;
$('theme').setAttribute('aria-pressed', String(theme === 'dark'));
$('theme').setAttribute('aria-label', theme === 'dark' ? t('Светлая тема') : t('Тёмная тема'));
function activeEntry() { return tabs.find(tab => tab.doc.id === current?.id); }
function saveActiveState() {
  const entry = activeEntry(); if (!entry) return;
  entry.mode = mode;
  if (mode !== 'source' && article.getAttribute('aria-busy') !== 'true') entry.readTop = readPane.scrollTop;
  if (mode !== 'read') entry.sourceTop = editor.scrollTop;
  entry.query = $('search-input').value;
  entry.searchOpen = !$('searchbar').hidden;
  entry.searchIndex = searchIndex;
}
function renderTabs() {
  $('tablist').replaceChildren(); $('tab-strip').hidden = tabs.length === 0;
  tabs.forEach(entry => {
    const selected = entry.doc.id === current?.id;
    const item = document.createElement('div'); item.className = 'tab-item' + (selected ? ' active' : ''); item.dataset.id = entry.doc.id;
    const button = document.createElement('button'); button.className = 'file-tab'; button.setAttribute('role', 'tab'); button.id = 'tab-' + entry.doc.id;
    button.setAttribute('aria-selected', String(selected)); button.setAttribute('aria-controls', 'main'); button.tabIndex = selected ? 0 : -1;
    button.title = entry.doc.path || (entry.doc.untitled ? t('Новый файл · ещё не сохранён') : t('Встроенный пример'));
    const label = document.createElement('span'); label.textContent = entry.doc.name; button.append(label);
    button.onclick = () => operation(() => activateTab(entry.doc.id));
    const close = document.createElement('button'); close.className = 'tab-close'; close.setAttribute('aria-label', t('Закрыть ') + entry.doc.name); close.title = t('Закрыть вкладку'); close.tabIndex = selected ? 0 : -1;
    close.innerHTML = '<svg aria-hidden="true"><use href="#i-close"/></svg>';
    close.onclick = () => operation(() => closeTab(entry.doc.id));
    item.addEventListener('auxclick', event => { if (event.button === 1) { event.preventDefault(); operation(() => closeTab(entry.doc.id)); } });
    item.append(button, close); $('tablist').append(item);
  });
  updateEditState();
  queueTabLayout();
}
let tabLayoutFrame = 0;
function revealActiveTab() { $('tablist').querySelector('.tab-item.active')?.scrollIntoView({ block: 'nearest', inline: 'nearest' }); }
function updateTabOverflow() {
  const list = $('tablist'), strip = $('tab-strip');
  // Include the space occupied by controls when deciding whether they can hide.
  const extra = strip.classList.contains('is-overflow') ? 3 * (26 + 3) : 0;
  const overflow = tabs.length > 1 && list.scrollWidth > list.clientWidth + extra + 1;
  strip.classList.toggle('is-overflow', overflow);
  $('tabs-prev').disabled = list.scrollLeft <= 1;
  $('tabs-next').disabled = list.scrollLeft >= list.scrollWidth - list.clientWidth - 1;
  if (!overflow && !$('tabs-menu').hidden) closePopovers();
}
function queueTabLayout() {
  cancelAnimationFrame(tabLayoutFrame);
  tabLayoutFrame = requestAnimationFrame(() => { updateTabOverflow(); revealActiveTab(); updateTabOverflow(); positionTabMenu(); chromeControls.align(); });
}
function positionTabMenu() {
  const menu = $('tabs-menu'); if (menu.hidden) return;
  menu.style.left = Math.max(8, Math.min($('tabs-menu-toggle').getBoundingClientRect().right - menu.offsetWidth, innerWidth - menu.offsetWidth - 8)) + 'px';
}
function renderTabMenu() {
  const query = $('tabs-filter').value.trim().toLocaleLowerCase();
  $('tabs-count').textContent = t('Открытые вкладки · ') + tabs.length;
  const list = $('tabs-menu-list'); list.replaceChildren();
  tabs.filter(entry => [entry.doc.name, entry.doc.path || ''].some(value => value.toLocaleLowerCase().includes(query))).forEach(entry => {
    const button = document.createElement('button'); button.className = 'tab-option'; button.dataset.id = entry.doc.id;
    if (entry.doc.id === current?.id) button.setAttribute('aria-current', 'page');
    const label = document.createElement('span'); label.className = 'tab-option-name'; label.textContent = entry.doc.name + (isDirty(entry) ? ' •' : '');
    const detail = document.createElement('span'); detail.className = 'tab-option-detail'; detail.textContent = entry.doc.path || (entry.doc.untitled ? t('Новый файл · не сохранён') : t('Встроенный пример'));
    button.title = detail.textContent;
    button.append(label, detail);
    button.onclick = () => operation(async () => { closePopovers(); await activateTab(entry.doc.id); revealActiveTab(); $('tablist').querySelector('[aria-selected="true"]')?.focus({ preventScroll: true }); });
    list.append(button);
  });
  if (!list.children.length) { const note = document.createElement('p'); note.className = 'tabs-no-results'; note.textContent = t('Вкладки не найдены'); list.append(note); }
}
new ResizeObserver(queueTabLayout).observe(document.querySelector('.tabbar'));
const isDirty = entry => entry.history.value !== entry.savedText;
const editorState = field => ({ start: field.selectionStart, end: field.selectionEnd, top: field.scrollTop });
function resetHistory(entry) { entry.history = new TextHistory(entry.editor.value); entry.lastSelection = editorState(entry.editor); entry.lastInputAt = 0; }
function cancelPreview() { clearTimeout(previewTimer); clearTimeout(previewDeadline); previewDeadline = null; }
function edited(entry) {
  entry.sessionVersion++; scheduleSession();
  updateEditState(); ++generation; clearTimeout(previewTimer);
  scrollSync.invalidate('source');
  if (entry === activeEntry() && mode === 'split') {
    const large = entry.history.value.length > 250000;
    const run = () => { cancelPreview(); operation(() => renderPreview(entry)); };
    previewTimer = setTimeout(run, large ? 450 : 150);
    previewDeadline ||= setTimeout(run, large ? 1200 : 450);
  }
  if (entry === activeEntry() && !$('searchbar').hidden) { clearTimeout(searchTimer); searchTimer = setTimeout(() => findMatches(false), 150); }
}
function historyStep(redo = false) {
  const entry = activeEntry();
  if (!entry || busy || exporting || mode === 'read' || $('help-dialog').open || document.activeElement === $('search-input')) return;
  const state = entry.history.step(redo); if (!state) return;
  entry.lastInputAt = 0;
  entry.editor.value = state.value; entry.editor.focus({ preventScroll: true });
  entry.editor.setSelectionRange(state.start, state.end); entry.editor.scrollTop = state.top;
  entry.lastSelection = editorState(entry.editor);
  edited(entry);
}
function updateEditState() {
  const items = new Map(Array.from($('tablist').children, item => [item.dataset.id, item]));
  tabs.forEach(entry => {
    const item = items.get(entry.doc.id), dirty = isDirty(entry);
    item?.classList.toggle('dirty', dirty);
    item?.querySelector('.file-tab')?.setAttribute('aria-label', entry.doc.name + (dirty ? t(', есть несохранённые изменения') : ''));
  });
  const entry = activeEntry(), dirty = entry && isDirty(entry);
  $('save').disabled = $('source-save').disabled = !entry || (!dirty && !entry.doc.demo && !entry.doc.untitled);
  $('save-as').disabled = !entry;
  $('edit-state').textContent = entry?.doc.untitled ? (dirty ? t('Новый файл · не сохранён') : t('Новый файл')) : dirty ? t('Изменения не сохранены') : entry?.doc.demo ? t('Пример · сохраните как файл') : 'MARKDOWN';
  $('file-state').textContent = entry?.doc.untitled ? (dirty ? t('Новый файл · не сохранён') : t('Новый файл')) : dirty ? t('Изменения не сохранены') : entry?.doc.demo ? t('Пример форматирования') : entry ? t('Сохранено') : '';
}
function addDocument(doc) {
  const found = tabs.find(tab => tab.doc.id === doc.id);
  if (found) return;
  const field = editorModule.createSourceEditor();
  field.className = 'source-editor'; field.spellcheck = false; field.setAttribute('aria-label', t('Исходный Markdown: ') + doc.name); field.hidden = true;
  field.value = doc.text;
  const entry = { doc, editor: field, savedText: doc.untitled ? '' : field.value, previewText: null, mode: doc.untitled || !field.value.trim() ? 'split' : 'read', readTop: 0, sourceTop: 0, query: '', searchOpen: false, searchIndex: -1 };
  entry.sessionVersion = 1;
  resetHistory(entry);
  field.addEventListener('beforeinput', event => {
    if (event.inputType === 'historyUndo' || event.inputType === 'historyRedo') { event.preventDefault(); historyStep(event.inputType === 'historyRedo'); return; }
    const previous = entry.lastSelection;
    const adjacent = field.selectionStart === previous.start && field.selectionEnd === previous.end && field.selectionStart === field.selectionEnd;
    entry.groupInput = adjacent && !entry.history.future.length && performance.now() - entry.lastInputAt < 650 && entry.lastInputType === event.inputType && ((event.inputType === 'insertText' && event.data?.length === 1 && event.data !== '\n') || event.inputType === 'deleteContentBackward');
    entry.beforeSelection = editorState(field);
  });
  field.addEventListener('input', event => {
    entry.history.record(field.value, entry.beforeSelection || entry.lastSelection, editorState(field), entry.groupInput);
    entry.beforeSelection = null; entry.lastSelection = editorState(field);
    entry.lastInputAt = performance.now(); entry.lastInputType = event.inputType; entry.groupInput = false;
    edited(entry);
  });
  $('editors').append(field); tabs.push(entry); scrollSync.watch(field);
  field.addEventListener('scroll', scheduleSession, { passive: true });
  field.addEventListener('keyup', scheduleSession);
  field.addEventListener('pointerup', scheduleSession);
}
async function receiveDocuments(batch) {
  if (!batch) return;
  if (exporting || busy) { deferredDocuments.push(batch); return; }
  if (batch.documents.length) editorModule ||= await import('./editor.mjs');
  batch.documents.forEach(addDocument);
  for (const restored of batch.restored || []) {
    const entry = tabs.find(tab => tab.doc.id === restored.id); if (!entry) continue;
    if (restored.text != null) entry.editor.value = restored.text;
    entry.mode = ['read', 'source', 'split'].includes(restored.mode) ? restored.mode : entry.mode;
    entry.readTop = Math.max(0, restored.readTop || 0); entry.sourceTop = Math.max(0, restored.sourceTop || 0);
    entry.editor.setSelectionRange(restored.start || 0, restored.end || 0); resetHistory(entry);
  }
  if (batch.documents.length) {
    const last = batch.documents.find(doc => doc.id === batch.activeId) || batch.documents.at(-1);
    await activateTab(last.id);
    if ((last.untitled || !last.text.trim()) && current?.id === last.id && mode !== 'read') activeEntry().editor.focus({ preventScroll: true });
  }
  if (batch.errors?.length) toast(batch.errors.join('\n'), true);
  else if (batch.restored?.length) toast(t('Восстановлено вкладок: ') + batch.restored.length);
  scheduleSession();
}
function showEmpty(releaseNative = false) {
  cancelPreview();
  releaseParser();
  scrollSync.reset();
  ++generation; current = null; syncViewButtons(); diagramData = []; headings = []; headingOffsets = null; outlineButtons = []; activeHeading = -1;
  clearMatches(); article.replaceChildren(); editor = null; $('outline').replaceChildren();
  workspace.hidden = true; $('empty-state').hidden = false; $('searchbar').hidden = true;
  $('filename').textContent = ''; $('file-path').textContent = ''; $('file-state').textContent = ''; $('stats').textContent = '';
  for (const id of ['outline-toggle','search-toggle','source-toggle','reload','pdf','encoding','view-read','view-source','view-split']) $(id).disabled = true;
  renderTabs(); operation(async () => { await window.reader.activate(null); if (releaseNative) await window.reader.idle?.(); });
}
async function activateTab(id, refresh = false) {
  if (exporting) return;
  const entry = tabs.find(tab => tab.doc.id === id); if (!entry) return;
  if (current?.id === id && !refresh) { renderTabs(); return; }
  saveActiveState(); closePopovers();
  const request = ++activationRequest;
  await window.reader.activate(id);
  if (request !== activationRequest || !tabs.includes(entry)) return;
  await showDocument(entry.doc, entry);
  scheduleSession();
  if (request === activationRequest) revealActiveTab();
}
async function closeTab(id) {
  if (exporting || busy) return;
  return exclusive(async () => {
  const index = tabs.findIndex(tab => tab.doc.id === id); if (index < 0) return;
  const entry = tabs[index];
  if (!await confirmDiscard(entry)) return;
  await flushSession({ exclude: new Set([entry.doc.id]) });
  // Keep the document intact until the native welcome screen covers it.
  if (await window.reader.close(entry.doc.id, tabs.length === 1)) { sessionReady = false; return; }
  const wasActive = entry === activeEntry();
  ++activationRequest; tabs.splice(index, 1); scrollSync.unwatch(entry.editor); entry.editor.destroy(); entry.editor.remove();
  if (wasActive) { ++generation; current = null; }
  if (!tabs.length) showEmpty(true);
  else if (wasActive) await activateTab(tabs[Math.min(index, tabs.length - 1)].doc.id);
  else renderTabs();
  if (tabs.length) await flushSession();
  });
}
function stepTab(delta) {
  if (!tabs.length) return;
  const index = tabs.findIndex(tab => tab.doc.id === current?.id);
  operation(() => activateTab(tabs[(index + delta + tabs.length) % tabs.length].doc.id));
}
function closePopovers(formatExcept) {
  nativeMenus?.close();
  closeFormattingMenus(formatExcept);
  for (const [trigger, panel] of [['appearance','appearance-menu'],['more','more-menu'],['tabs-menu-toggle','tabs-menu']]) { $(panel).hidden = true; $(trigger).setAttribute('aria-expanded','false'); }
}
function closeFormattingMenus(except) { document.querySelectorAll('.format-menu[open]').forEach(menu => { if (menu !== except) menu.open = false; }); }
$('format-toolbar').addEventListener('pointerdown', event => { if (event.button === 0 && event.target.closest('button[data-format]')) event.preventDefault(); });
$('format-toolbar').addEventListener('click', event => {
  const button = event.target.closest('button[data-format]');
  if (!button || !editor || mode === 'read' || busy || exporting) return;
  const change = formatMarkdown(editor.value, editor.selectionStart, editor.selectionEnd, button.dataset.format);
  closePopovers(); editor.applyFormatting(change);
});
$('format-toolbar').addEventListener('toggle', event => {
  const menu = event.target;
  if (!menu.matches('.format-menu') || !menu.open) return;
  closePopovers(menu);
  const box = menu.querySelector('.format-options'), anchor = menu.querySelector('summary').getBoundingClientRect();
  box.style.left = Math.max(12,Math.min(anchor.left,innerWidth-box.offsetWidth-12))+'px';
  box.style.top = Math.max(12,Math.min(anchor.bottom+5,innerHeight-box.offsetHeight-12))+'px';
},true);
$('format-toolbar').addEventListener('keydown', event => {
  if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); closeFormattingMenus(); editor?.focus(); }
});
window.addEventListener('resize', () => closeFormattingMenus());
function togglePopover(trigger, panel) {
  if (nativeMenus && (trigger === 'more' || trigger === 'appearance')) { nativeMenus.toggle(trigger, closePopovers); return; }
  const open = $(panel).hidden; closePopovers(); $(panel).hidden = !open; $(trigger).setAttribute('aria-expanded', String(open));
}
function toast(message, error = false) {
  clearTimeout(toastTimer); $('toast').textContent = message; $('toast').hidden = false;
  $('toast').setAttribute('role', error ? 'alert' : 'status');
  toastTimer = setTimeout(() => { $('toast').hidden = true; }, error ? 7500 : 3500);
}
function errorMessage(error) { toast(error.message || String(error), true); }
async function operation(callback) { try { return await callback(); } catch (error) { errorMessage(error); } }
async function exclusive(callback) {
  if (busy || exporting) return;
  const focused = document.activeElement;
  busy = true; document.body.inert = true;
  try { return await callback(); }
  finally {
    busy = false; document.body.inert = false;
    const queued = deferredDocuments; deferredDocuments = [];
    for (const batch of queued) await receiveDocuments(batch);
    if (document.activeElement === document.body && focused?.isConnected && focused.getClientRects().length) focused.focus({ preventScroll: true });
  }
}
async function saveEntry(entry, saveAs = false) {
  if (!entry) return false;
  const wasActive = entry === activeEntry();
  if (wasActive) saveActiveState();
  const doc = await window.reader.save(entry.doc.id, entry.editor.value, saveAs);
  if (!doc) return false;
  entry.doc = doc; entry.savedText = entry.editor.value;
  entry.sessionVersion++;
  entry.editor.setLabel(t('Исходный Markdown: ') + doc.name);
  if (wasActive) { await window.reader.activate(doc.id); await showDocument(doc, entry); }
  else renderTabs();
  await flushSession();
  toast(t('Файл сохранён')); return true;
}
async function save(saveAs = false) {
  if (!current || busy || exporting) return;
  await operation(() => exclusive(() => saveEntry(activeEntry(), saveAs)));
}
async function confirmDiscard(entry) {
  if (!isDirty(entry)) return true;
  const choice = await window.reader.confirmUnsaved(entry.doc.name);
  if (choice === 'save') return saveEntry(entry);
  return choice === 'discard';
}
window.reader.onCloseRequest(() => operation(() => exclusive(async () => {
  await flushSession();
  const discard = new Set();
  for (const entry of tabs) {
    if (!isDirty(entry)) continue;
    const choice = await window.reader.confirmUnsaved(entry.doc.name);
    if (choice === 'cancel') return;
    if (choice === 'save' && !await saveEntry(entry)) return;
    if (choice === 'discard') discard.add(entry.doc.id);
  }
  await flushSession({ discard });
  sessionReady = false;
  await window.reader.finishClose();
})));
function updateFont() { headingOffsets = null; scrollSync.invalidate(); document.documentElement.style.setProperty('--doc-size', fontSize + 'px'); $('font-size').textContent = fontSize; localStorage.setItem('font-size', fontSize); $('font-down').disabled = fontSize <= 13; $('font-up').disabled = fontSize >= 25; }
function setMode(next) {
  if (!current || exporting) return;
  cancelPreview();
  const previousMode = mode, anchor = mode === 'read' ? readingAnchor() : null, syncTicket = scrollSync.freeze();
  saveActiveState();
  mode = next; workspace.dataset.view = mode;
  headingOffsets = null;
  if (mode === 'source') {
    ++generation; releaseParser(); article.removeAttribute('aria-busy');
    article.replaceChildren(); headings = []; outlineButtons = []; $('outline').replaceChildren(); diagramData = [];
    activeEntry().previewText = null;
  }
  activeEntry().mode = next;
  scheduleSession();
  syncViewButtons(); closePopovers();
  updateSplit();
  if (mode !== 'read') editor.mount();
  readPane.scrollTop = activeEntry().readTop; editor.scrollTop = activeEntry().sourceTop;
  restoreReadingAnchor(anchor);
  if (mode !== 'source' && article.childNodes.length) activeEntry().readTop = readPane.scrollTop;
  scrollSync.invalidate(previousMode === 'read' ? 'preview' : 'source');
  scrollSync.thaw(syncTicket);
  if (mode === 'read') scrollSync.reset();
  if (mode !== 'source' && activeEntry().previewText !== editor.value) operation(() => renderPreview(activeEntry()));
  if (mode === 'source' || mode === 'split') editor.focus({ preventScroll: true });
  if (!$('searchbar').hidden) findMatches(false);
}
function syncViewButtons() {
  for (const view of ['read','source','split']) $('view-' + view).setAttribute('aria-pressed',String(view===mode));
  $('source-toggle').setAttribute('aria-pressed',String(mode!=='read'));
  $('format-toolbar').hidden = !current || mode === 'read';
  if ($('format-toolbar').hidden) closeFormattingMenus();
}
function toggleOutline() { if (!current) return; closePopovers(); workspace.classList.toggle('hide-outline'); $('outline-toggle').setAttribute('aria-expanded', String(!workspace.classList.contains('hide-outline'))); updateActiveHeading(); }
function assetUrl(value) {
  if (!value) return '';
  if (/^(https?:|data:image\/(?:png|jpe?g|gif|webp|avif|bmp);)/i.test(value)) return value;
  if (!current.assetBase || /^(?:[a-z][a-z\d+.-]*:|\/\/)/i.test(value)) return '';
  const prefix = current.relativeDirectory ? current.relativeDirectory + '/' : '';
  try {
    const url = new URL(encodeURI(prefix) + value.replace(/\\/g, '/'), current.assetBase);
    url.searchParams.set('v', current.assetToken);
    url.searchParams.set('r', current.assetVersion || '');
    return url.href;
  } catch { return ''; }
}
function prepareContent(root) {
  root.querySelectorAll('.markdown-gap[data-blank-lines]').forEach(gap => {
    gap.style.height = Number(gap.dataset.blankLines) + 'lh';
  });
  root.querySelectorAll('img').forEach(img => {
    const original = img.getAttribute('src');
    const next = assetUrl(original);
    img.loading = 'lazy'; img.referrerPolicy = 'no-referrer';
    img.addEventListener('error', () => { img.classList.add('broken'); img.alt = (img.alt || original || t('Изображение')) + t(' — не удалось загрузить'); }, { once: true });
    if (next) img.src = next; else { img.removeAttribute('src'); img.classList.add('broken'); img.alt = (img.alt || t('Изображение')) + t(' — недоступно'); }
  });
  root.querySelectorAll('table').forEach(table => { const wrapper = document.createElement('div'); wrapper.className = 'table-scroll'; table.replaceWith(wrapper); wrapper.append(table); });
  const titles = { NOTE: t('Примечание'), TIP: t('Совет'), IMPORTANT: t('Важно'), WARNING: t('Внимание'), CAUTION: t('Осторожно') };
  root.querySelectorAll('blockquote').forEach(block => {
    const first = block.querySelector(':scope > p');
    const textNode = first?.firstChild;
    if (!textNode || textNode.nodeType !== Node.TEXT_NODE) return;
    const match = textNode.textContent.match(/^\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*/);
    if (!match) return;
    textNode.textContent = textNode.textContent.slice(match[0].length);
    if (!textNode.textContent && textNode.nextSibling?.nodeName === 'BR') textNode.nextSibling.remove();
    block.classList.add('alert', 'alert-' + match[1].toLowerCase());
    const label = document.createElement('span'); label.className = 'alert-title'; label.dataset.ui = match[1]; label.textContent = titles[match[1]]; block.prepend(label);
  });
  root.querySelectorAll('input').forEach(input => { input.disabled = true; if (input.type !== 'checkbox') input.remove(); });
}
const previewLabels = { copy: 'Копировать', metadata: 'Метаданные документа', 'plain-code': 'текст', NOTE: 'Примечание', TIP: 'Совет', IMPORTANT: 'Важно', WARNING: 'Внимание', CAUTION: 'Осторожно', 'diagram-error': 'Не удалось построить диаграмму. Ниже показан исходный код.' };
function translatePreviewLabels() {
  for (const node of article.querySelectorAll('[data-ui]')) {
    if (previewLabels[node.dataset.ui]) node.textContent = t(previewLabels[node.dataset.ui]);
    if (node.dataset.ui === 'diagram') node.setAttribute('aria-label', t('Диаграмма Mermaid'));
  }
}
function updateStats(entry) {
  if (!entry?.previewStats) return;
  const { words, lines } = entry.previewStats, doc = entry.doc;
  const size = doc.bytes < 1024 ? doc.bytes + t(' Б') : (doc.bytes / 1024).toLocaleString(getLanguage(), { maximumFractionDigits: 1 }) + t(' КБ');
  $('stats').textContent = t('{0} · строк: {1} · ~{2} мин', size, lines, Math.max(1, Math.ceil(words / 200)));
}
function updateLanguageUI() {
  const anchor = readingAnchor();
  translateStaticUI();
  chromeControls.refresh().catch(console.error);
  $('language').value = ['ru', 'en'].includes(localStorage.getItem('language')) ? localStorage.getItem('language') : 'system';
  $('language-system').textContent = t('Как в системе ({0})', resolveLanguage('system', systemLanguage) === 'ru' ? 'Русский' : 'English');
  $('theme-label').textContent = t(theme === 'dark' ? 'Светлая тема' : 'Тёмная тема');
  $('theme').setAttribute('aria-label', $('theme-label').textContent);
  renderTabs(); if (!$('tabs-menu').hidden) renderTabMenu();
  for (const entry of tabs) entry.editor.setLabel(t('Исходный Markdown: ') + entry.doc.name);
  if (current) {
    $('filename').title = current.path || t(current.untitled ? 'Новый файл · ещё не сохранён' : 'Встроенный пример');
    if (!current.path) $('file-path').textContent = t(current.untitled ? 'Новый файл · ещё не сохранён' : 'Пример форматирования');
    $('file-path').title = $('filename').title;
    translatePreviewLabels(); updateStats(activeEntry());
    if (!headings.length && $('outline').firstElementChild) $('outline').firstElementChild.textContent = t('В документе нет заголовков');
  }
  if (!$('searchbar').hidden) findMatches(false);
  updateSplit(); restoreReadingAnchor(anchor); scrollSync.invalidate();
}
function prepareDocument() {
  headingOffsets = null; activeHeading = -1; outlineButtons = [];
  headings = Array.from(article.querySelectorAll('h1,h2,h3,h4,h5,h6'));
  const counts = new Map();
  headings.forEach((heading, index) => {
    let slug = heading.textContent.trim().toLowerCase().replace(/[^\p{L}\p{N}_\-\s]/gu, '').replace(/\s+/g, '-') || 'раздел';
    const count = counts.get(slug) || 0; counts.set(slug, count + 1); if (count) slug += '-' + count;
    heading.id = slug; heading.dataset.heading = String(index);
  });
  const outline = $('outline'), outlineFragment = document.createDocumentFragment(); outline.replaceChildren(); $('heading-count').textContent = headings.length;
  if (!headings.length) { const note = document.createElement('p'); note.className = 'muted small'; note.style.padding = '0 12px'; note.textContent = t('В документе нет заголовков'); outline.append(note); }
  const minLevel = Math.min(...headings.map(h => Number(h.tagName[1])));
  headings.forEach((heading, index) => {
    const button = document.createElement('button'); button.textContent = heading.textContent;
    button.style.paddingLeft = (10 + Math.min(3, Number(heading.tagName[1]) - minLevel) * 12) + 'px';
    button.dataset.target = index;
    button.addEventListener('click', () => { if (mode === 'source') setMode('read'); heading.scrollIntoView({ behavior: 'smooth', block: 'start' }); workspace.classList.add('hide-outline'); $('outline-toggle').setAttribute('aria-expanded','false'); });
    outlineFragment.append(button); outlineButtons.push(button);
  });
  outline.append(outlineFragment);
  updateActiveHeading();
}
function invalidateHeadings() {
  headingOffsets = null; searchWindow = null;
  if (searchRanges.length && mode !== 'source') requestAnimationFrame(() => updateSearchHighlights());
}
new ResizeObserver(invalidateHeadings).observe(article);
new ResizeObserver(invalidateHeadings).observe(readPane);
article.addEventListener('load', invalidateHeadings, true);
article.addEventListener('toggle', invalidateHeadings, true);
function updateActiveHeading() {
  if (!headings.length || mode === 'source' || workspace.classList.contains('hide-outline')) return;
  if (!headingOffsets) {
    const origin = readPane.getBoundingClientRect().top - readPane.scrollTop;
    headingOffsets = headings.map((heading, index) => ({ index, rect: heading.getBoundingClientRect() })).filter(item => item.rect.height > 0).map(item => ({ index: item.index, top: item.rect.top - origin }));
  }
  const target = readPane.scrollTop + 85;
  let low = 0, high = headingOffsets.length;
  while (low < high) { const mid = (low + high) >> 1; if (headingOffsets[mid].top <= target) low = mid + 1; else high = mid; }
  const active = headingOffsets[Math.max(0, low - 1)]?.index ?? 0;
  if (active === activeHeading) return;
  outlineButtons[activeHeading]?.classList.remove('active');
  outlineButtons[activeHeading]?.removeAttribute('aria-current');
  outlineButtons[active]?.classList.add('active');
  outlineButtons[active]?.setAttribute('aria-current', 'location');
  activeHeading = active;
}
async function renderDiagrams(version, root = article, data = diagramData) {
  const nodes = Array.from(root.querySelectorAll('[data-diagram-id]'));
  if (!nodes.length) return;
  mermaidPromise ||= import('mermaid').then(module => module.default);
  const mermaid = await mermaidPromise;
  if (version !== generation) return;
  mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: theme === 'dark' ? 'dark' : 'neutral', fontFamily: 'Segoe UI, sans-serif', suppressErrorRendering: true, maxTextSize: 50000, htmlLabels: false, flowchart: { htmlLabels: false }, secure: ['secure', 'securityLevel', 'startOnLoad', 'maxTextSize', 'suppressErrorRendering', 'themeCSS', 'themeVariables', 'theme', 'fontFamily', 'htmlLabels', 'flowchart'] });
  for (let i = 0; i < nodes.length; i++) {
    if (version !== generation) return;
    const node = nodes[i], code = data[Number(node.dataset.diagramId)];
    if (code === undefined) continue;
    node.querySelectorAll('.diagram-error').forEach(note => note.remove());
    const staging = document.createElement('div');
    staging.style.cssText = 'position:fixed;left:-100000px;top:0;visibility:hidden;pointer-events:none';
    staging.style.width = Math.max(260, article.clientWidth) + 'px';
    document.body.append(staging);
    try {
      const result = await mermaid.render('diagram-' + version + '-' + i, code, staging);
      if (version !== generation) return;
      node.innerHTML = DOMPurify.sanitize(result.svg, { USE_PROFILES: { svg: true, svgFilters: true, html: true }, FORBID_TAGS: ['script', 'iframe', 'foreignObject'], FORBID_ATTR: ['onload', 'onclick'] });
      node.querySelectorAll('a').forEach(a => { a.removeAttribute('href'); a.removeAttribute('xlink:href'); });
    } catch (error) {
      if (version !== generation) return;
      const note = document.createElement('div'); note.className = 'diagram-error'; note.dataset.ui = 'diagram-error'; note.textContent = t('Не удалось построить диаграмму. Ниже показан исходный код.'); node.prepend(note);
    } finally { staging.remove(); }
  }
  if (root === article && !$('searchbar').hidden) findMatches(false);
}
async function showDocument(doc, entry) {
  if (!doc) return;
  clearMatches();
  scrollSync.reset();
  ++generation; cancelPreview();
  current = doc; $('filename').textContent = doc.name; $('filename').title = doc.path || (doc.untitled ? t('Новый файл · ещё не сохранён') : t('Встроенный пример'));
  document.title = doc.name + ' — Dokvi';
  $('file-path').textContent = doc.untitled ? t('Новый файл · ещё не сохранён') : doc.demo ? t('Пример форматирования') : doc.path.replace(/\\/g,'/').split('/').slice(-3).join(' / ');
  $('file-path').title = $('filename').title;
  renderTabs(); workspace.hidden = false; $('empty-state').hidden = true;
  workspace.classList.add('hide-outline'); $('outline-toggle').setAttribute('aria-expanded','false');
  for (const id of ['outline-toggle','search-toggle','source-toggle','pdf','view-read','view-source','view-split']) $(id).disabled = false;
  $('reload').disabled = !doc.path; $('encoding').disabled = !doc.path;
  mode = entry.mode; workspace.dataset.view = mode; syncViewButtons(); updateSplit();
  $('search-input').value = entry.query; $('searchbar').hidden = !entry.searchOpen;
  $('encoding').value = doc.encoding;
  tabs.forEach(tab => { tab.editor.hidden = tab !== entry; tab.editor.id = tab === entry ? 'source' : ''; });
  editor = entry.editor; editor.scrollTop = entry.sourceTop;
  if (mode !== 'read') editor.mount();
  searchIndex = entry.searchIndex;
  updateEditState();
  article.setAttribute('aria-busy', 'true');
  article.replaceChildren(); headingOffsets = null; headings = []; diagramData = [];
  entry.previewText = null;
  if (mode !== 'source') await renderPreview(entry);
  else { article.removeAttribute('aria-busy'); if (entry.searchOpen) findMatches(false); }
}
async function loadMath() {
  if (!katexPromise) {
    const css = new Promise((resolve, reject) => {
      const link = document.createElement('link'); link.rel = 'stylesheet'; link.href = 'katex.min.css';
      link.onload = resolve; link.onerror = () => reject(new Error(t('Не удалось загрузить оформление формул.')));
      document.head.append(link);
    });
    katexPromise = Promise.all([import('katex'), css]).then(([module]) => module.default);
  }
  return katexPromise;
}
async function renderPreview(entry) {
  if (entry !== activeEntry()) return;
  const version = ++generation, text = entry.history.value, doc = entry.doc;
  article.setAttribute('aria-busy', 'true');
  try {
  const result = await parseMarkdown(text);
  if (!result || version !== generation || entry !== activeEntry() || text !== entry.history.value) return;
  entry.previewStats = { words: result.words, lines: result.lines }; updateStats(entry);
  const patch = preparePreviewPatch(article, sanitizeMarkdown(result.html, DOMPurify, true), result, theme);
  prepareContent(patch.changes);
  const mathNodes = patch.changes.querySelectorAll('[data-math-id]');
  const katex = mathNodes.length ? await loadMath() : null;
  if (version !== generation || entry !== activeEntry()) return;
  let sliceStarted = performance.now();
  for (const node of mathNodes) {
    const formula = result.formulas[Number(node.dataset.mathId)];
    if (!formula) continue;
    try { katex.render(formula.tex, node, { displayMode: formula.display, throwOnError: false, trust: false, strict: 'ignore', maxExpand: 1000, maxSize: 20, output: 'htmlAndMathml' }); }
    catch { node.textContent = formula.tex; }
    if (performance.now() - sliceStarted > 12) {
      await new Promise(resolve => setTimeout(resolve, 0));
      if (version !== generation || entry !== activeEntry()) return;
      sliceStarted = performance.now();
    }
  }
  await renderDiagrams(version, patch.changes, result.diagrams);
  if (version !== generation || entry !== activeEntry()) return;
  // No asynchronous work below: publish a complete revision in one frame.
  const updating = article.childNodes.length > 0;
  const top = updating ? readPane.scrollTop : entry.readTop;
  const anchor = updating ? readingAnchor() : null;
  const syncTicket = scrollSync.freeze();
  try {
    patch.commit(); translatePreviewLabels(); diagramData = result.diagrams;
    entry.previewText = text;
    prepareDocument();
    readPane.scrollTop = top; restoreReadingAnchor(anchor);
    entry.readTop = readPane.scrollTop;
    updateActiveHeading();
  } finally { scrollSync.thaw(syncTicket, { preserve: updating }); }
  if (!$('searchbar').hidden) findMatches(false); else clearMatches();
  } finally {
    if (version === generation) article.removeAttribute('aria-busy');
  }
}
function clearMatches() { CSS.highlights.delete('search-results'); CSS.highlights.delete('search-current'); searchRanges = []; searchIndex = -1; searchWindow = null; $('search-count').textContent = t('0 из 0'); }
function updateSearchHighlights() {
  if (!searchRanges.length || mode === 'source') return;
  const viewport = readPane.getBoundingClientRect(), from = viewport.top - 500, to = viewport.bottom + 500;
  let low = 0, high = searchRanges.length;
  while (low < high) {
    const mid = (low + high) >> 1;
    if (searchRanges[mid].getBoundingClientRect().bottom < from) low = mid + 1; else high = mid;
  }
  const start = low; high = searchRanges.length;
  while (low < high) {
    const mid = (low + high) >> 1;
    if (searchRanges[mid].getBoundingClientRect().top <= to) low = mid + 1; else high = mid;
  }
  const end = Math.min(low, start + 600);
  if (searchWindow?.start === start && searchWindow?.end === end) return;
  searchWindow = { start, end };
  CSS.highlights.set('search-results', new Highlight(...searchRanges.slice(start, end)));
}
function findMatches(scroll = true) {
  const previousIndex = searchIndex;
  clearMatches();
  const query = $('search-input').value.toLocaleLowerCase('ru-RU');
  if (!query) return;
  if (mode === 'source') {
    const text = activeEntry().history.value.toLocaleLowerCase('ru-RU'); let offset = 0;
    while ((offset = text.indexOf(query, offset)) !== -1 && searchRanges.length < 5000) { searchRanges.push({ start: offset, end: offset + query.length }); offset += query.length; }
    if (!scroll && previousIndex >= 0) searchIndex = Math.min(previousIndex, searchRanges.length - 1) - 1;
    nextMatch(1, scroll); return;
  }
  const root = article;
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT, { acceptNode(node) {
    // Prune hidden subtrees without forcing layout for each text node.
    const closedParent = node.parentElement?.tagName === 'DETAILS' && !node.parentElement.open;
    if (node.nodeType === Node.ELEMENT_NODE) {
      if (node.matches('style,script,button,.code-toolbar,.katex-mathml,annotation,svg,[hidden]') || (closedParent && node.tagName !== 'SUMMARY')) return NodeFilter.FILTER_REJECT;
      return NodeFilter.FILTER_SKIP;
    }
    return node.textContent && !closedParent ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT;
  } });
  let node, full = '', nodes = [];
  while ((node = walker.nextNode())) { nodes.push({ node, start: full.length, end: full.length + node.textContent.length }); full += node.textContent; }
  const haystack = full.toLocaleLowerCase('ru-RU');
  let offset = 0, startIndex = 0, endIndex = 0;
  while ((offset = haystack.indexOf(query, offset)) !== -1 && searchRanges.length < 5000) {
    const end = offset + query.length;
    while (startIndex < nodes.length && nodes[startIndex].end <= offset) startIndex++;
    endIndex = Math.max(endIndex, startIndex);
    while (endIndex < nodes.length && nodes[endIndex].end < end) endIndex++;
    const startNode = nodes[startIndex], endNode = nodes[endIndex];
    if (startNode && endNode) { const range = new Range(); range.setStart(startNode.node, offset - startNode.start); range.setEnd(endNode.node, end - endNode.start); searchRanges.push(range); }
    offset = end;
  }
  updateSearchHighlights();
  if (!scroll && previousIndex >= 0) searchIndex = Math.min(previousIndex, searchRanges.length - 1) - 1;
  nextMatch(1, scroll);
}
function nextMatch(direction, scroll = true) {
  if (!searchRanges.length) { $('search-count').textContent = t('0 из 0'); return; }
  searchIndex = (searchIndex + direction + searchRanges.length) % searchRanges.length;
  const range = searchRanges[searchIndex];
  $('search-count').textContent = t('{0} из {1}', searchIndex + 1, searchRanges.length + (searchRanges.length === 5000 ? '+' : ''));
  if (mode === 'source') {
    if (scroll) {
      editor.setSelectionRange(range.start, range.end);
      editor.revealSelection();
    }
    return;
  }
  CSS.highlights.set('search-current', new Highlight(range));
  const pane = readPane;
  const rect = range.getBoundingClientRect(), paneRect = pane.getBoundingClientRect();
  if (scroll) pane.scrollTo({ top: pane.scrollTop + rect.top - paneRect.top - pane.clientHeight / 3, behavior: 'smooth' });
}
function showSearch() { if (!current) return; closePopovers(); $('searchbar').hidden = false; $('search-input').focus(); $('search-input').select(); }
function hideSearch() { clearTimeout(searchTimer); $('searchbar').hidden = true; clearMatches(); (mode === 'source' ? editor : readPane)?.focus(); }
async function loadDocuments(loadBatch) {
  if (exporting || busy) return;
  closePopovers();
  await operation(async () => receiveDocuments(await loadBatch()));
}
const open = () => loadDocuments(() => window.reader.open());
const createDocument = () => loadDocuments(async () => ({ documents: [await window.reader.create()], errors: [] }));
async function reload(encoding = 'auto') {
  if (current?.path && !exporting && !busy) await operation(() => exclusive(async () => {
    const entry = activeEntry();
    if (!await confirmDiscard(entry)) { $('encoding').value = entry.doc.encoding; return; }
    const id = entry.doc.id; const doc = await window.reader.reload(id, encoding);
    entry.doc = doc; entry.editor.value = doc.text; entry.savedText = entry.editor.value; resetHistory(entry);
      entry.sessionVersion++;
    if (current?.id === id) await activateTab(id,true); toast(t('Файл обновлён'));
      await flushSession({ discard: new Set([id]) });
  }));
}
const printMath = new Set();
function restorePrintMath() {
  for (const node of printMath) node.style.removeProperty('--pdf-math-scale');
  printMath.clear();
}
window.addEventListener('dokvi-before-pdf', event => {
  restorePrintMath();
  const width = event.detail.width;
  const measure = document.createElement('div');
  measure.className = 'markdown';
  measure.style.cssText = `position:fixed;left:-100000px;top:0;visibility:hidden;pointer-events:none;padding:0;max-width:none;font-size:11pt;width:${width}px`;
  document.body.append(measure);
  try {
    for (const display of article.querySelectorAll('.katex-display')) {
      const clone = display.cloneNode(true);
      const node = display.querySelector('.katex-html');
      const content = clone.querySelector('.katex-html');
      content.style.whiteSpace = 'normal';
      measure.replaceChildren(clone);
      const contentWidth = Math.max(content.scrollWidth, ...Array.from(content.children, child => child.getBoundingClientRect().width));
      if (contentWidth > width) {
        node.style.setProperty('--pdf-math-scale', width / contentWidth);
        printMath.add(node);
      }
    }
  } finally { measure.remove(); }
});
window.addEventListener('afterprint', restorePrintMath);
async function waitForExportImages() {
  const pending = new Set(Array.from(article.querySelectorAll('img')).filter(img => !img.complete));
  if (!pending.size) return;
  await new Promise((resolve, reject) => {
    const images = [...pending];
    const cleanup = () => {
      clearTimeout(timer);
      for (const img of images) { img.removeEventListener('load', settled); img.removeEventListener('error', settled); }
    };
    const settled = event => {
      pending.delete(event.target);
      if (!pending.size) { cleanup(); resolve(); }
    };
    const timer = setTimeout(() => {
      cleanup(); reject(new Error(t('Изображения ещё загружаются. Повторите экспорт PDF через несколько секунд.')));
    }, 15000);
    for (const img of images) { img.addEventListener('load', settled); img.addEventListener('error', settled); }
  });
}
let exporting = false;
async function exportPdf() {
  if (exporting || busy || !current) return;
  closePopovers();
  const exportingId = current.id;
  exporting = true; $('pdf').disabled = true;
  for (const element of [document.querySelector('.tabbar'),document.querySelector('.reader-toolbar'),workspace]) element.inert = true;
  const previousTheme = theme;
  try {
    theme = 'light'; document.documentElement.dataset.theme = theme;
    if (activeEntry().previewText !== editor.value) await renderPreview(activeEntry());
    if (diagramData.length) await renderDiagrams(++generation);
    article.querySelectorAll('img').forEach(img => { img.loading = 'eager'; });
    await document.fonts.ready;
    await waitForExportImages();
    const result = await window.reader.pdf(exportingId); if (result) toast(t('PDF сохранён'));
  } catch (error) { errorMessage(error); }
  finally {
    restorePrintMath();
    theme = previousTheme; document.documentElement.dataset.theme = theme;
    if (diagramData.length) await operation(() => renderDiagrams(++generation));
    exporting = false; $('pdf').disabled = false;
    for (const element of [document.querySelector('.tabbar'),document.querySelector('.reader-toolbar'),workspace]) element.inert = false;
    const queued = deferredDocuments; deferredDocuments = []; for (const batch of queued) await receiveDocuments(batch);
  }
}
$('open').onclick = open;
$('open-more').onclick = open; $('empty-open').onclick = open;
$('new').onclick = createDocument; $('new-more').onclick = createDocument; $('empty-new').onclick = createDocument;
$('save').onclick = () => save(); $('source-save').onclick = () => save(); $('save-as').onclick = () => save(true);
$('appearance').onclick = () => togglePopover('appearance','appearance-menu');
for (const value of ['left', 'center', 'full']) $('reading-' + value).onclick = () => { readingLayout = value; applyReadingLayout(true); };
$('reading-width').oninput = () => { readingWidth = Number($('reading-width').value); if (readingLayout === 'full') readingLayout = 'left'; applyReadingLayout(true); };
$('more').onclick = () => togglePopover('more','more-menu');
$('source-toggle').onclick = () => setMode(mode === 'read' ? 'split' : 'read');
$('reload').onclick = () => reload();
$('encoding').onchange = () => reload($('encoding').value);
$('language').onchange = () => operation(async () => {
  const preference = $('language').value;
  if (busy || exporting) { updateLanguageUI(); return; }
  try {
    await window.reader.language(preference);
    localStorage.setItem('language', preference);
    setLanguage(preference, systemLanguage); updateLanguageUI();
  } catch (error) { updateLanguageUI(); throw error; }
});
$('pdf').onclick = exportPdf;
$('outline-toggle').onclick = toggleOutline;
$('font-up').onclick = () => { fontSize = Math.min(25, fontSize + 1); updateFont(); };
$('font-down').onclick = () => { fontSize = Math.max(13, fontSize - 1); updateFont(); };
for (const view of ['read', 'source', 'split']) $('view-' + view).onclick = () => setMode(view);
$('theme').onclick = () => {
  theme = theme === 'light' ? 'dark' : 'light'; localStorage.setItem('theme', theme); document.documentElement.dataset.theme = theme;
  $('theme').setAttribute('aria-pressed', String(theme === 'dark')); $('theme').setAttribute('aria-label', theme === 'dark' ? t('Светлая тема') : t('Тёмная тема'));
  $('theme-label').textContent = theme === 'dark' ? t('Светлая тема') : t('Тёмная тема'); operation(() => window.reader.theme(theme));
  if (activeEntry() && mode !== 'source') { cancelPreview(); operation(() => renderPreview(activeEntry())); }
};
const helpTabs = [...$('help-tabs').querySelectorAll('[role="tab"]')];
const helpContent = document.querySelector('#help-dialog .help-content');
const helpScrollPositions = new Map();
let activeHelpTab = helpTabs[0];
function selectHelpTab(tab) {
  if (tab !== activeHelpTab) {
    helpScrollPositions.set(activeHelpTab.id, helpContent.scrollTop);
    for (const item of helpTabs) {
      const selected = item === tab;
      item.setAttribute('aria-selected', String(selected));
      item.tabIndex = selected ? 0 : -1;
      $(item.getAttribute('aria-controls')).hidden = !selected;
    }
    activeHelpTab = tab;
    helpContent.scrollTop = helpScrollPositions.get(tab.id) || 0;
  }
  tab.focus({ preventScroll: true });
  tab.scrollIntoView({ block: 'nearest', inline: 'nearest' });
}
for (const tab of helpTabs) tab.onclick = () => selectHelpTab(tab);
$('help-tabs').addEventListener('keydown', event => {
  if (event.ctrlKey || event.altKey || event.metaKey) return;
  const index = helpTabs.indexOf(event.target);
  if (index < 0) return;
  const next = { ArrowRight: (index + 1) % helpTabs.length, ArrowLeft: (index + helpTabs.length - 1) % helpTabs.length, Home: 0, End: helpTabs.length - 1 }[event.key];
  if (next === undefined) return;
  event.preventDefault(); event.stopPropagation(); selectHelpTab(helpTabs[next]);
});
function showHelp() {
  closePopovers();
  if (!$('help-dialog').open) $('help-dialog').showModal();
  activeHelpTab.focus({ preventScroll: true });
}
$('help').onclick = showHelp;
$('default-apps').onclick = () => { closePopovers(); operation(() => window.reader.defaultApps()); };
$('help-close').onclick = () => $('help-dialog').close();
$('show-sample').onclick = () => { $('help-dialog').close(); operation(async () => receiveDocuments({ documents: [await window.reader.sample()], errors: [] })); };
$('empty-sample').onclick = $('show-sample').onclick;
$('search-toggle').onclick = showSearch; $('search-close').onclick = hideSearch;
$('search-input').oninput = () => { clearTimeout(searchTimer); if ((activeEntry()?.history.value.length || 0) < 100000) findMatches(); else searchTimer = setTimeout(() => findMatches(), 100); }; $('search-prev').onclick = () => nextMatch(-1); $('search-next').onclick = () => nextMatch(1);
$('search-input').onkeydown = event => { if (event.key === 'Enter') { event.preventDefault(); nextMatch(event.shiftKey ? -1 : 1); } };
readPane.addEventListener('scroll', () => { if (activeEntry() && mode !== 'source' && article.getAttribute('aria-busy') !== 'true') activeEntry().readTop = readPane.scrollTop; cancelAnimationFrame(scrollFrame); scrollFrame = requestAnimationFrame(() => { updateActiveHeading(); updateSearchHighlights(); }); });
readPane.addEventListener('scroll', scheduleSession, { passive: true });
article.addEventListener('click', event => {
  if (!current || exporting) return;
  const copy = event.target.closest('.copy-code');
  if (copy) { operation(async () => { await window.reader.copy(copy.closest('.code-block').querySelector('code').textContent); copy.textContent = t('Скопировано'); setTimeout(() => { copy.textContent = t('Копировать'); }, 1800); }); return; }
  const link = event.target.closest('a');
  if (!link) return; event.preventDefault();
  const href = link.getAttribute('href'); if (!href) return;
  if (href.startsWith('#')) {
    try { const target = article.querySelector('[id="' + CSS.escape(decodeURIComponent(href.slice(1))) + '"]'); if (target) { target.closest('details')?.setAttribute('open', ''); target.scrollIntoView({ behavior: 'smooth', block: 'start' }); } else toast(t('Раздел по этой ссылке не найден')); } catch { toast(t('Не удалось открыть ссылку')); }
  } else operation(async () => {
    const doc = await window.reader.link(current.id,href); if (doc) { addDocument(doc); await activateTab(doc.id); const hash = href.split('#')[1]; if (hash) document.getElementById(decodeURIComponent(hash))?.scrollIntoView({ block: 'start' }); }
  });
});
document.addEventListener('keydown', event => {
  if (exporting || busy) { event.preventDefault(); return; }
  if ($('help-dialog').open) { if (event.key === 'F1') event.preventDefault(); return; }
  if (event.key === 'Escape' && !$('tabs-menu').hidden) { event.preventDefault(); closePopovers(); $('tabs-menu-toggle').focus(); return; }
  const key = /^(Key[A-Z]|Digit[0-9])$/.test(event.code) ? event.code.replace(/^(Key|Digit)/, '').toLowerCase() : event.key.toLowerCase();
  if (event.key === 'Escape' && !$('searchbar').hidden) { event.preventDefault(); hideSearch(); return; }
  if (event.key === 'Escape') { closePopovers(); workspace.classList.add('hide-outline'); $('outline-toggle').setAttribute('aria-expanded','false'); }
  if (event.key === 'F1') { event.preventDefault(); showHelp(); return; }
  if (!(event.ctrlKey || event.metaKey)) return;
  if (key === 'f') { event.preventDefault(); showSearch(); }
  else if (key === 'p') { event.preventDefault(); exportPdf(); }
  else if (key === 'r') { event.preventDefault(); reload(); }
  else if (key === 'b') { event.preventDefault(); toggleOutline(); }
  else if (key === 'e') { event.preventDefault(); setMode(event.shiftKey ? (mode==='split'?'read':'split') : (mode==='read'?'split':'read')); }
  else if (key === 'y') { event.preventDefault(); historyStep(true); }
  else if (event.key === 'Tab') { event.preventDefault(); stepTab(event.shiftKey ? -1 : 1); }
  else if (/^[1-9]$/.test(key)) { event.preventDefault(); const index=key==='9'?tabs.length-1:Number(key)-1; if(tabs[index]) operation(()=>activateTab(tabs[index].doc.id)); }
});
document.addEventListener('click', event => {
  if (event.target.closest('#help-dialog')) return;
  if (!event.target.closest('.reader-popover,#appearance,#more,#tabs-menu-toggle,.format-toolbar')) closePopovers();
  if (!event.target.closest('#sidebar,#outline-toggle')) { workspace.classList.add('hide-outline'); $('outline-toggle').setAttribute('aria-expanded','false'); }
});
$('tabs-prev').onclick = () => $('tablist').scrollBy({ left: -Math.max(120, $('tablist').clientWidth * .8), behavior: 'smooth' });
$('tabs-next').onclick = () => $('tablist').scrollBy({ left: Math.max(120, $('tablist').clientWidth * .8), behavior: 'smooth' });
$('tablist').addEventListener('scroll', updateTabOverflow, { passive: true });
$('tab-strip').addEventListener('wheel', event => {
  if (event.ctrlKey || !$('tab-strip').classList.contains('is-overflow')) return;
  const delta = Math.abs(event.deltaX) > Math.abs(event.deltaY) ? event.deltaX : event.deltaY;
  if (!delta) return;
  event.preventDefault();
  $('tablist').scrollLeft += delta * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? $('tablist').clientWidth : 1);
}, { passive: false });
$('tabs-menu-toggle').onclick = () => {
  const opening = $('tabs-menu').hidden; closePopovers(); if (!opening) return;
  $('tabs-filter').value = ''; renderTabMenu(); $('tabs-menu').hidden = false; $('tabs-menu-toggle').setAttribute('aria-expanded', 'true');
  positionTabMenu(); $('tabs-menu-list').querySelector('[aria-current="page"]')?.scrollIntoView({ block: 'nearest' }); $('tabs-filter').focus({ preventScroll: true });
};
$('tabs-filter').oninput = renderTabMenu;
$('tabs-menu').addEventListener('keydown', event => {
  const options = [...$('tabs-menu-list').querySelectorAll('button')]; if (!options.length) return;
  const index = options.indexOf(document.activeElement);
  if (event.key === 'Enter' && event.target === $('tabs-filter')) { event.preventDefault(); options[0].click(); return; }
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault(); const step = event.key === 'ArrowDown' ? 1 : -1;
    options[index < 0 ? (step === 1 ? 0 : options.length - 1) : (index + step + options.length) % options.length].focus();
  }
});
$('tablist').addEventListener('keydown', event => {
  if (event.ctrlKey || !event.target.matches('.file-tab')) return;
  const index = tabs.findIndex(tab => 'tab-' + tab.doc.id === event.target.id);
  let next;
  if(event.key==='ArrowRight') next=(index+1)%tabs.length;
  if(event.key==='ArrowLeft') next=(index-1+tabs.length)%tabs.length;
  if(event.key==='Home') next=0; if(event.key==='End') next=tabs.length-1;
  if(next!==undefined) { event.preventDefault(); operation(async()=>{await activateTab(tabs[next].doc.id); $('tablist').querySelector('[aria-selected="true"]')?.focus();}); }
});
let dragDepth = 0;
document.addEventListener('dragenter', event => { event.preventDefault(); if (event.dataTransfer.types.includes('Files')) { dragDepth++; $('drop-overlay').hidden = false; } });
document.addEventListener('dragleave', event => { event.preventDefault(); if (--dragDepth <= 0) { dragDepth = 0; $('drop-overlay').hidden = true; } });
document.addEventListener('dragover', event => { event.preventDefault(); event.dataTransfer.dropEffect = 'copy'; });
document.addEventListener('drop', event => {
  event.preventDefault(); dragDepth = 0; $('drop-overlay').hidden = true;
  const files = Array.from(event.dataTransfer.files); if (files.length && !exporting) operation(async () => receiveDocuments(await window.reader.drop(files)));
});
window.reader.onDocuments(batch => operation(() => receiveDocuments(batch))); window.reader.onError(errorMessage); window.reader.onCommand(command => { if (busy || exporting) return; if (command === 'new') createDocument(); if (command === 'open') open(); if(command==='close' && current) operation(()=>closeTab(current.id)); if(command==='save') save(); if(command==='save-as') save(true); if(command==='undo') historyStep(); if(command==='redo') historyStep(true); });
updateFont();
applyReadingLayout();
updateLanguageUI();
$('theme-label').textContent = theme === 'dark' ? t('Светлая тема') : t('Тёмная тема');
operation(() => window.reader.theme(theme));
showEmpty();
operation(async () => {
  let batch, failed = false;
  const startupError = error => { failed = true; errorMessage(error); };
  try { batch = await window.reader.initial(); } catch (error) { startupError(error); }
  do {
    // Still reveal the error message if loading a document fails; keep its
    // recovery checkpoint instead of replacing it with a partial session.
    try { await receiveDocuments(batch); } catch (error) { startupError(error); }
    // Finish the editor's layout and a paint before replacing the native
    // welcome surface. NavigationCompleted alone is too early for module UI.
    await document.fonts.ready;
    await chromeControls.ready;
    chromeControls.align();
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    batch = await window.reader.ready();
  } while (batch);
  if (!failed) {
    if (current && (current.untitled || !current.text.trim()) && mode !== 'read') editor?.focus();
    sessionReady = true; await flushSession();
  }
});
