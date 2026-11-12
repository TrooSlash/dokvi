const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const catalog = require('../src/locales/en.json');
const root = path.join(__dirname, '..');

test('Windows locale resolution and explicit choice cover both languages and fallback', async () => {
  const { resolveLanguage, setLanguage, t } = await import('../src/i18n.mjs');
  for (const locale of ['ru', 'ru-RU', 'RU-ru', 'ru_BY']) assert.equal(resolveLanguage('system', locale), 'ru');
  for (const locale of ['en-US', 'en-GB', 'uk-UA', 'de-DE', '', null]) assert.equal(resolveLanguage('system', locale), 'en');
  assert.equal(resolveLanguage('en', 'ru-RU'), 'en');
  assert.equal(resolveLanguage('ru', 'en-US'), 'ru');
  assert.equal(resolveLanguage('invalid', 'ru-RU'), 'ru');
  setLanguage('en', 'ru-RU');
  assert.equal(t('Сохранить изменения в «{0}»?', 'Заметка.md'), 'Save changes to “Заметка.md”?');
  setLanguage('ru', 'en-US');
  assert.equal(t('Сохранить изменения в «{0}»?', 'Notes.md'), 'Сохранить изменения в «Notes.md»?');
});

test('English catalog covers static UI and native messages with matching placeholders', () => {
  for (const [source, translation] of Object.entries(catalog)) {
    assert.ok(translation.trim(), source);
    const placeholders = value => [...value.matchAll(/\{\d+\}/g)].map(match => match[0]).sort();
    assert.deepEqual(placeholders(translation), placeholders(source), source);
  }
  const html = fs.readFileSync(path.join(root, 'src/index.html'), 'utf8');
  const menu = JSON.parse(fs.readFileSync(path.join(root, 'src/menu-items.json'), 'utf8'));
  for (const item of menu.flatMap(item => [item, ...(item.options || [])])) {
    for (const value of [item.text, item.label].filter(Boolean)) {
      if (/[А-Яа-яЁё]/.test(value) && value !== 'Русский') assert.ok(Object.hasOwn(catalog, value), 'Untranslated menu: ' + value);
    }
  }
  for (const match of html.matchAll(/>([^<>]+)<|(?:title|aria-label|placeholder)="([^"]*)"/g)) {
    const value = (match[1] || match[2] || '').trim();
    if (/[А-Яа-яЁё]/.test(value) && value !== 'Русский') assert.ok(Object.hasOwn(catalog, value), 'Untranslated HTML: ' + value);
  }
  for (const file of fs.readdirSync(path.join(root, 'host')).filter(file => file.endsWith('.cs') && !file.includes('Tests'))) {
    const source = fs.readFileSync(path.join(root, 'host', file), 'utf8');
    for (const match of source.matchAll(/L\.T\("([^"\\]*)"/g)) assert.ok(Object.hasOwn(catalog, match[1]), file + ': ' + match[1]);
  }
});

test('formatting inserts localized placeholders while preserving selected document text', async () => {
  const { setLanguage } = await import('../src/i18n.mjs');
  const { formatMarkdown } = await import('../src/formatting.mjs');
  setLanguage('en');
  assert.equal(formatMarkdown('', 0, 0, 'bold').insert, '**text**');
  assert.equal(formatMarkdown('', 0, 0, 'codeblock').insert, '```\ncode\n```');
  const table = formatMarkdown('', 0, 0, 'table');
  assert.equal(table.insert.slice(table.start, table.end), 'Heading 1');
  assert.match(table.insert, /Heading 2/);
  assert.equal(formatMarkdown('Слово 🙂', 0, 8, 'bold').insert, '**Слово 🙂**');
  setLanguage('ru');
  assert.equal(formatMarkdown('', 0, 0, 'bold').insert, '**текст**');
});
