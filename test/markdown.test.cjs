const { test } = require('node:test');
const assert = require('node:assert/strict');
test('single Enter creates a visible line break, while a blank line starts a paragraph', async () => {
  const { renderMarkdown } = await import('../src/markdown.mjs');
  for (const newline of ['\n', '\r\n']) {
    const { html } = renderMarkdown(['Первая', '**Вторая**', '*Третья*', '', 'Новый абзац'].join(newline));
    assert.match(html, /Первая<br>\n<strong>Вторая<\/strong><br>\n<em>Третья<\/em>/);
    assert.equal((html.match(/<p\b/g) || []).length, 2);
    assert.equal((html.match(/<br>/g) || []).length, 2);
    assert.match(html, /data-source-line="4" data-source-end="5">Новый абзац/);
  }
});

test('line breaks preserve list/quote continuations, hard breaks, code and table rows', async () => {
  const { renderMarkdown } = await import('../src/markdown.mjs');
  assert.equal((renderMarkdown('- [ ] Задача\n  Вторая строка\n  **Третья**').html.match(/<br>/g) || []).length, 2);
  assert.match(renderMarkdown('> Первая\n> Вторая').html, /Первая<br>\nВторая/);
  assert.equal((renderMarkdown('Первая  \nВторая\\\nТретья').html.match(/<br>/g) || []).length, 2);
  const code = renderMarkdown('~~~text\nпервая\nвторая\n~~~').html;
  assert.match(code, /<code>первая\nвторая\n<\/code>/);
  assert.ok(!code.includes('<br>'));
  const table = renderMarkdown('| A | B |\n| --- | --- |\n| 1 | 2 |\n| 3 | 4 |').html;
  assert.equal((table.match(/<tr\b/g) || []).length, 3);
  assert.ok(!table.includes('<br>'));
});

test('Markdown block locations keep original source lines, including front matter and custom blocks', async () => {
  const { renderMarkdown } = await import('../src/markdown.mjs');
  const lines = ['---', 'title: Test', '---', '', '# Header', '', '| A | B |', '| - | - |', '| 1 | 2 |', '', '~~~js', 'x=1', '~~~', '', '$$x^2$$', '', '~~~mermaid', 'graph LR', 'A-->B', '~~~', '', '<p data-source-line="999">Raw HTML</p>'];
  const { html } = renderMarkdown(lines.join('\n'), { sanitize: value => value });
  assert.match(html, /<h1 data-source-line="4" data-source-end="5">/);
  assert.match(html, /<table data-source-line="6"/);
  assert.match(html, /class="code-block" data-source-line="10" data-source-end="13"/);
  assert.match(html, /<div data-source-line="14"/);
  assert.match(html, /data-diagram-id="0" data-source-line="16"/);
  assert.ok(!html.includes('data-source-line="999"'));
});

test('table alignment covers headers, body, empty cells and defaults without inline styles', async () => {
  const { renderMarkdown } = await import('../src/markdown.mjs');
  for (const newline of ['\n', '\r\n']) {
    const text = ['| L | C | R | D |', '| :--- | :---: | ---: | --- |', '| **One** | Two | 3 | 4 |', '| | | | |'].join(newline);
    const html = renderMarkdown(text).html;
    for (const tag of ['th', 'td']) {
      const cells = [...html.matchAll(new RegExp('<' + tag + '\\b([^>]*)>', 'g'))];
      assert.equal(cells.length, tag === 'th' ? 4 : 8);
      cells.forEach((cell, index) => {
        const alignment = ['left', 'center', 'right', null][index % 4];
        if (alignment) assert.match(cell[1], new RegExp('class="table-align-' + alignment + '"'));
        else assert.doesNotMatch(cell[1], /table-align-/);
        assert.doesNotMatch(cell[1], /style=/);
      });
    }
    assert.match(html, /<strong>One<\/strong>/);
  }
  const plain = renderMarkdown('| A | B |\n| --- | --- |\n| 1 | 2 |').html;
  assert.doesNotMatch(plain, /table-align-/);
});

test('raw HTML cannot supply the table alignment classes generated from Markdown', async () => {
  const { renderMarkdown } = await import('../src/markdown.mjs');
  const html = renderMarkdown('<table><tr><td class="table-align-right" data-align="center">Raw</td></tr></table>').html;
  assert.doesNotMatch(html, /table-align-|data-align=/);
  assert.match(html, /<td>Raw<\/td>/);
});

test('reused Markdown parser resets math slots, diagrams and source offsets between documents', async () => {
  const { renderMarkdown } = await import('../src/markdown.mjs');
  for (const text of ['$x^2$', '$y^3$', '---\ntitle: Test\n---\n\n$$z^4$$', '$a+b$']) {
    const result = renderMarkdown(text);
    assert.equal(result.formulas.length, 1);
    assert.match(result.html, /data-math-id="0"/);
    assert.ok(result.formulas[0].tex.includes(text.includes('z^4') ? 'z^4' : text.slice(1, -1)));
  }
  assert.match(renderMarkdown('# Reset').html, /data-source-line="0"/);
});

test('front matter preserves extra blank lines and the first heading location', async () => {
  const { renderMarkdown } = await import('../src/markdown.mjs');
  for (const newline of ['\n', '\r\n']) {
    for (const delimiter of ['---', '...']) {
      const text = ['---', 'title: Test', delimiter + '  ', '', '', '', '# Heading'].join(newline);
      const { html } = renderMarkdown(text);
      assert.match(html, /data-blank-lines="2" data-source-line="4" data-source-end="6"/);
      assert.match(html, /<h1 data-source-line="6" data-source-end="7">Heading/);
      assert.match(html, /class="metadata" data-source-line="0" data-source-end="3"/);
    }
  }
});

test('extra blank lines keep their source ranges without expanding one DOM node per line', async () => {
  const { renderMarkdown } = await import('../src/markdown.mjs');
  for (const newline of ['\n','\r\n']) {
    const { html } = renderMarkdown(['a','','','','b','',''].join(newline));
    assert.match(html, /data-blank-lines="2" data-source-line="2" data-source-end="4"/);
    assert.match(html, /data-blank-lines="2" data-source-line="5" data-source-end="7"/);
    assert.equal((html.match(/class="markdown-gap"/g) || []).length, 2);
  }
  assert.match(renderMarkdown('\n\nfirst').html, /data-blank-lines="2" data-source-line="0" data-source-end="2"/);
  const large = renderMarkdown('\n'.repeat(5000)).html;
  assert.match(large, /data-blank-lines="5001"/);
  assert.equal((large.match(/class="markdown-gap"/g) || []).length, 1);
  assert.ok(!renderMarkdown('~~~\na\n\n\n\nb\n~~~').html.includes('markdown-gap'));
  assert.ok(!renderMarkdown('a\n\nb').html.includes('markdown-gap'));
});
