const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

function bridge() {
  const handlers = new Map(), commands = [];
  class Storage {
    setItem() {}
  }
  const window = { chrome: { webview: { addEventListener() {}, postMessage() {} } },
    addEventListener: (name, handler) => handlers.set(name, handler) };
  window.top = window;
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../src/native-bridge.js'), 'utf8'),
    { window, Storage, localStorage: new Storage(), console });
  window.reader.onCommand(command => commands.push(command));
  return (overrides = {}) => {
    let prevented = false;
    handlers.get('keydown')({ ctrlKey: true, altKey: false, metaKey: false, shiftKey: false,
      code: '', key: '', target: { closest: () => false },
      preventDefault() { prevented = true; }, stopImmediatePropagation() {}, ...overrides });
    return { command: commands.pop(), prevented };
  };
}

test('save and editing commands accept virtual keyboards without a physical code', () => {
  const press = bridge();
  for (const [key, command] of Object.entries({ n: 'new', o: 'open', s: 'save', w: 'close', z: 'undo', y: 'redo' })) {
    for (const code of ['', 'Unidentified']) {
      assert.deepEqual(press({ key, code }), { command, prevented: true });
      assert.deepEqual(press({ key: key.toUpperCase(), code }), { command, prevented: true });
    }
  }
  assert.equal(press({ key: 's', shiftKey: true }).command, 'save-as');
  assert.equal(press({ key: 'z', shiftKey: true }).command, 'redo');
});

test('physical shortcuts keep the same commands with the Russian layout', () => {
  const press = bridge();
  for (const [code, key, command] of [['KeyN', 'т', 'new'], ['KeyO', 'щ', 'open'],
    ['KeyS', 'ы', 'save'], ['KeyW', 'ц', 'close'], ['KeyZ', 'я', 'undo'], ['KeyY', 'н', 'redo']]) {
    assert.deepEqual(press({ code, key }), { command, prevented: true });
  }
  assert.equal(press({ code: 'KeyQ', key: 's' }).command, undefined);
});

test('plain typing, AltGr and search-field undo remain native', () => {
  const press = bridge();
  for (const modifiers of [{ ctrlKey: false }, { altKey: true }, { metaKey: true }]) {
    assert.deepEqual(press({ key: 's', code: 'KeyS', ...modifiers }), { command: undefined, prevented: false });
  }
  for (const key of ['z', 'y']) {
    assert.deepEqual(press({ key, target: { closest: () => true } }), { command: undefined, prevented: false });
  }
});
