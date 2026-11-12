import items from '../src/menu-items.json' with { type: 'json' };

const escape = value => String(value).replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
export function renderMoreMenu() {
  return items.map(item => {
    if (item.kind === 'separator') return '<hr>';
    if (item.kind === 'label') return `<span class="popover-label">${escape(item.text)}</span>`;
    if (item.kind === 'file-info') return '<div class="file-info"><span id="file-state"></span><span id="stats"></span></div>';
    if (item.kind === 'encoding' || item.kind === 'language') {
      const options = item.options.map(option => `<option${option.id ? ` id="${escape(option.id)}"` : ''} value="${escape(option.value)}"${item.kind === 'language' && option.value !== 'system' ? ` lang="${escape(option.value)}"` : ''}>${escape(option.text)}</option>`).join('');
      return `<label class="encoding-row${item.kind === 'language' ? ' language-row' : ''}"><span>${escape(item.text)}</span><select id="${escape(item.id)}" aria-label="${escape(item.label)}">${options}</select></label>`;
    }
    const pressed = item.pressed === undefined ? '' : ` aria-pressed="${item.pressed}"`;
    const icon = item.id === 'theme' ? '<svg><use href="#i-theme"/></svg>' : '';
    const label = `<span${item.id === 'theme' ? ' id="theme-label"' : ''}>${escape(item.text)}</span>`;
    return `<button id="${escape(item.id)}"${pressed}${item.id === 'theme' ? ` aria-label="${escape(item.text)}"` : ''}>${icon}${label}${item.shortcut ? `<kbd>${escape(item.shortcut)}</kbd>` : ''}</button>`;
  }).join('\n');
}
