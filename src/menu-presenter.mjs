import menuItems from './menu-items.json';

// Keep document commands and their state in the renderer; the host only draws
// the same native popover used before a document is opened.
export function createNativeMenus(reader, canAct) {
  if (!reader.showMenu) return null;
  const $ = id => document.getElementById(id);
  const ids = [...menuItems.map(item => item.id).filter(Boolean), 'font-down', 'font-up', 'reading-left', 'reading-center', 'reading-full', 'reading-width'];
  let opened = null, commands = Promise.resolve();
  const snapshot = () => ({
    controls: ids.map(id => {
      const el = $(id);
      return { id, disabled: el.disabled || !canAct(), selected: el.getAttribute('aria-pressed') === 'true', value: el.value || '' };
    }),
    fontSize: Number($('font-size').textContent), width: Number($('reading-width').value),
    layout: ['left', 'center', 'full'].find(value => $('reading-' + value).getAttribute('aria-pressed') === 'true') || 'left',
    fileState: $('file-state').textContent, stats: $('stats').textContent,
  });
  function markClosed(kind, focus, keyboard = false) {
    if (opened !== kind) return;
    opened = null; $(kind).setAttribute('aria-expanded', 'false');
    if (focus) {
      $(kind).classList.toggle('native-keyboard-focus', keyboard);
      $(kind).focus({ preventScroll: true });
    }
  }
  reader.onMenuClosed(({ kind, focus, keyboard }) => markClosed(kind, focus, keyboard));
  reader.onMenuKey(({ code, ctrlKey, shiftKey }) => {
    const key = code.startsWith('Key') ? code.slice(3).toLowerCase() : code.startsWith('Digit') ? code.slice(5) : code;
    document.dispatchEvent(new KeyboardEvent('keydown', { key, code, ctrlKey, shiftKey, bubbles: true, cancelable: true }));
  });
  reader.onMenuAction(action => {
    commands = commands.then(async () => {
      const el = ids.includes(action.id) && $(action.id);
      if (!el || el.disabled || !canAct()) return;
      if (action.value != null) {
        el.value = action.value;
        await (action.id === 'reading-width' ? el.oninput?.() : el.onchange?.());
      } else await el.onclick?.();
      if (action.keepOpen && opened) await reader.updateMenu(action.token, snapshot(), action.id);
    }).catch(error => { console.error(error); reader.closeMenu(false); });
  });
  const close = (focus = false, keyboard = false) => {
    if (!opened) return;
    const kind = opened; markClosed(kind, focus, keyboard); reader.closeMenu(focus).catch(console.error);
  };
  document.addEventListener('focusout', event => event.target.classList.remove('native-keyboard-focus'));
  document.addEventListener('pointerdown', event => {
    for (const id of ['appearance', 'more']) $(id).classList.remove('native-keyboard-focus');
    if (!event.target.closest('#appearance,#more')) close();
  });
  document.addEventListener('keydown', event => {
    if (!opened || event.ctrlKey || event.metaKey || event.altKey) return;
    if (event.key === 'Tab' || event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault(); event.stopImmediatePropagation();
      reader.focusMenu(event.shiftKey || event.key === 'ArrowUp').catch(console.error);
    } else if (event.key === 'Escape') {
      event.preventDefault(); event.stopImmediatePropagation(); close(true, true);
    }
  }, true);
  return {
    close,
    toggle(kind, closeOthers) {
      const shouldOpen = opened !== kind; closeOthers();
      if (!shouldOpen) return;
      opened = kind; $(kind).setAttribute('aria-expanded', 'true');
      reader.showMenu(kind, snapshot()).catch(error => { markClosed(kind, false); console.error(error); });
    },
  };
}
