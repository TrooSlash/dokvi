import { t } from './i18n.mjs';
import { EditorState, EditorSelection, Transaction, countColumn } from '@codemirror/state';
import { EditorView, keymap } from '@codemirror/view';
import { defaultKeymap, indentMore, indentLess } from '@codemirror/commands';
import { indentUnit } from '@codemirror/language';

function tabIndent({ state, dispatch }) {
  if (state.selection.ranges.some(range => !range.empty)) return indentMore({ state, dispatch });
  dispatch(state.update(state.changeByRange(range => {
    const line = state.doc.lineAt(range.from);
    const column = countColumn(state.sliceDoc(line.from, range.from), state.tabSize);
    const spaces = ' '.repeat(state.tabSize - column % state.tabSize);
    return { changes: { from: range.from, insert: spaces }, range: EditorSelection.cursor(range.from + spaces.length) };
  }), { scrollIntoView: true, userEvent: 'input.indent' }));
  return true;
}

// The view is mounted only when the source is actually shown.
export function createSourceEditor() {
  const field = document.createElement('div');
  field.className = 'source-editor';
  let value = '', view = null, selection = { start: 0, end: 0 }, top = 0, silent = false;
  for (const name of ['beforeinput', 'input']) field.addEventListener(name, event => {
    if (event.target !== field) event.stopImmediatePropagation();
  });
  const range = () => view ? { start: view.state.selection.main.from, end: view.state.selection.main.to } : selection;
  Object.defineProperties(field, {
    value: {
      get: () => value,
      set(text) {
        value = String(text).replace(/\r\n?/g, '\n');
        if (view) {
          silent = true;
          try { view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: value } }); }
          finally { silent = false; }
        }
      },
    },
    selectionStart: { get: () => range().start },
    selectionEnd: { get: () => range().end },
    scrollTop: { get: () => view ? view.scrollDOM.scrollTop : top, set(y) { top = y; if (view) view.scrollDOM.scrollTop = y; } },
    scrollHeight: { get: () => view ? view.scrollDOM.scrollHeight : field.clientHeight },
    editorMounted: { get: () => Boolean(view) },
  });
  field.setSelectionRange = (start, end) => {
    selection = { start: Math.max(0, Math.min(value.length, start)), end: Math.max(0, Math.min(value.length, end)) };
    if (view) view.dispatch({ selection: { anchor: selection.start, head: selection.end } });
  };
  field.select = () => field.setSelectionRange(0, value.length);
  field.setLabel = label => { field.setAttribute('aria-label', label); if (view) view.dispatch({}); };
  field.applyFormatting = change => {
    field.mount();
    view.dispatch({ changes: { from: change.from, to: change.to, insert: change.insert },
      selection: { anchor: change.start, head: change.end }, annotations: Transaction.userEvent.of('input.format') });
    view.focus();
  };
  field.focus = () => { field.mount(); view.focus(); };
  field.mount = () => {
    if (view) { view.requestMeasure(); return; }
    view = new EditorView({
      parent: field,
      state: EditorState.create({
        doc: value,
        selection: { anchor: selection.start, head: selection.end },
        extensions: [
          EditorState.tabSize.of(4), indentUnit.of('    '), EditorView.lineWrapping,
          keymap.of([{ key: 'Tab', run: tabIndent, shift: indentLess }, ...defaultKeymap]),
          EditorView.contentAttributes.of(() => ({ 'aria-label': field.getAttribute('aria-label') || t('Исходный Markdown'), spellcheck: 'false', autocorrect: 'off', autocapitalize: 'off' })),
          EditorView.theme({
            '&': { height: '100%', fontSize: 'inherit', backgroundColor: 'transparent', color: 'inherit' },
            '&.cm-focused': { outline: 'none' },
            '.cm-scroller': { overflow: 'auto', fontFamily: 'inherit', fontSize: 'inherit', lineHeight: 'inherit' },
            '.cm-content': { padding: '4px 0 20px', caretColor: 'var(--text)' },
            '.cm-line': { padding: '0 8px 0 2px' },
          }),
          EditorView.updateListener.of(update => {
            if (update.geometryChanged) field.dispatchEvent(new Event('editor-layout'));
          }),
        ],
      }),
      dispatchTransactions(transactions, editor) {
        const changed = transactions.some(transaction => transaction.docChanged);
        const userEvent = transactions.find(transaction => transaction.docChanged)?.annotation(Transaction.userEvent) || '';
        const inputType = /format|indent|dedent/.test(userEvent) ? 'insertReplacementText' : userEvent.includes('delete') ? 'deleteContentBackward' : userEvent.includes('paste') ? 'insertFromPaste' : 'insertText';
        let data = null;
        if (changed) transactions[0].changes.iterChanges((_a, _b, _c, _d, inserted) => { if (inserted.length <= 1) data = inserted.toString(); });
        if (changed && !silent) field.dispatchEvent(new InputEvent('beforeinput', { bubbles: true, inputType, data }));
        editor.update(transactions);
        if (changed) value = editor.state.doc.toString();
        if (changed && !silent) field.dispatchEvent(new InputEvent('input', { bubbles: true, inputType, data }));
      },
    });
    view.scrollDOM.scrollTop = top;
    const mountedView = view;
    view.scrollDOM.addEventListener('scroll', () => {
      if (view !== mountedView) return;
      top = view.scrollDOM.scrollTop; field.dispatchEvent(new Event('scroll'));
    }, { passive: true });
  };
  field.measureLines = lines => {
    if (!view) return null;
    const measured = new Map();
    for (const line of lines) {
      const number = Math.min(view.state.doc.lines, Math.max(1, line + 1));
      const block = view.lineBlockAt(view.state.doc.line(number).from);
      measured.set(line, line >= view.state.doc.lines ? view.contentHeight : block.top);
    }
    return measured;
  };
  field.revealSelection = () => { if (view) view.dispatch({ effects: EditorView.scrollIntoView(view.state.selection.main, { y: 'center' }) }); };
  field.destroy = () => { view?.destroy(); view = null; };
  return field;
}
