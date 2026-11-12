(() => {
  'use strict';
  if (window !== window.top || !window.chrome?.webview) return;
  const native = window.chrome.webview, requests = new Map(), listeners = new Map();
  let sequence = 0, keyboardFocus = false;
  const call = (method, args = [], files) => new Promise((resolve, reject) => {
    const id = String(++sequence); requests.set(id, { resolve, reject });
    try {
      const message = { id, method, args };
      if (files) native.postMessageWithAdditionalObjects(message, files); else native.postMessage(message);
    } catch (error) { requests.delete(id); reject(error); }
  });
  const on = (event, callback) => { const group = listeners.get(event) || []; group.push(callback); listeners.set(event, group); };
  const emit = (event, value) => { for (const callback of listeners.get(event) || []) callback(value); };
  native.addEventListener('message', ({ data }) => {
    if (data.reply) {
      const request = requests.get(data.reply); if (!request) return;
      requests.delete(data.reply);
      if (data.error) request.reject(new Error(data.error)); else request.resolve(data.value);
    } else if (data.eventName) emit(data.eventName, data.value);
  });
  const storageKeys = new Set(['theme', 'font-size', 'reading-layout', 'reading-width', 'editor-split', 'language']);
  const setItem = Storage.prototype.setItem;
  for (const [key, value] of Object.entries(window.__nativePreferences || {})) {
    if (storageKeys.has(key)) setItem.call(localStorage, key, value);
  }
  if (!window.__nativePreferences?.language) setItem.call(localStorage, 'language', 'system');
  delete window.__nativePreferences;
  Storage.prototype.setItem = function (key, value) {
    setItem.call(this, key, value);
    if (this === localStorage && storageKeys.has(key)) call('settings', [key, String(value)]).catch(console.error);
  };
  Object.defineProperty(window, 'reader', { value: Object.freeze({
    initial: () => call('initial'), ready: () => call('ready'), create: () => call('new'), sample: () => call('sample'), open: () => call('open'), drop: files => call('drop', [], files),
    activate: id => call('activate', [id]), close: (id, lastTab) => call('close', [id, !!lastTab, keyboardFocus]),
    save: (id, text, saveAs) => call('save', [id, text, !!saveAs]),
    confirmUnsaved: name => call('confirm-unsaved', [name]), finishClose: () => call('finish-close'),
    reload: (id, encoding) => call('reload', [id, encoding]), link: (id, href) => call('link', [id, href]),
    copy: text => call('copy', [text]), pdf: id => call('pdf', [id]), theme: theme => call('theme', [theme]),
    defaultApps: () => call('default-apps'), language: value => call('language', [value]),
    idle: () => call('idle', [keyboardFocus]),
    chromeControls: scale => call('chrome-controls', [scale]),
    showMenu: (kind, state) => call('menu', [kind, JSON.stringify(state)]),
    updateMenu: (token, state, focus) => call('menu-update', [token, JSON.stringify(state), focus]),
    closeMenu: focus => call('menu-close', [!!focus]), focusMenu: backwards => call('menu-focus', [!!backwards]),
    onMenuAction: callback => on('menu-action', callback), onMenuClosed: callback => on('menu-closed', callback),
    onMenuKey: callback => on('menu-key', callback),
    checkpoint: session => call('session', [session]),
    onCloseRequest: callback => on('close-request', callback), onDocuments: callback => on('documents', callback),
    onError: callback => on('error', callback), onCommand: callback => on('command', callback),
  }), writable: false, configurable: false });
  on('ui-action', action => { if (['appearance', 'more', 'help'].includes(action)) document.getElementById(action)?.click(); });
  window.addEventListener('pointerdown', () => { keyboardFocus = false; }, true);
  window.addEventListener('keydown', event => {
    keyboardFocus = true;
    if (!event.ctrlKey || event.altKey || event.metaKey) return;
    let code = event.code;
    if (!code || code === 'Unidentified') {
      const key = String(event.key || '').toUpperCase();
      if (/^[A-Z]$/.test(key)) code = 'Key' + key;
    }
    let command;
    if (code === 'KeyN') command = 'new';
    if (code === 'KeyO') command = 'open';
    if (code === 'KeyS') command = event.shiftKey ? 'save-as' : 'save';
    if (code === 'KeyW') command = 'close';
    // Preserve the text field's native undo outside the Markdown editor.
    if (!event.target.closest?.('input') && code === 'KeyZ') command = event.shiftKey ? 'redo' : 'undo';
    if (!event.target.closest?.('input') && code === 'KeyY') command = 'redo';
    if (command) { event.preventDefault(); event.stopImmediatePropagation(); emit('command', command); }
  }, true);
})();
