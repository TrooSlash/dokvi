const {test}=require('node:test');
const assert=require('node:assert/strict');
const run=async(text,start,end,command)=>{
  const {formatMarkdown}=await import('../src/formatting.mjs');
  const change=formatMarkdown(text,start,end,command);
  const value=text.slice(0,change.from)+change.insert+text.slice(change.to);
  assert.ok(change.start>=0 && change.end>=change.start && change.end<=value.length,'valid selection');
  return {value,...change};
};
test('inline formatting preserves surrounding text and UTF-16 selections; repeat removes it',async()=>{
  const original='До привет🙂 после', start=3,end=11;
  const bold=await run(original,start,end,'bold');
  assert.equal(bold.value,'До **привет🙂** после');
  assert.equal(bold.value.slice(bold.start,bold.end),'привет🙂');
  assert.equal((await run(bold.value,bold.start,bold.end,'bold')).value,original);
});
test('whitespace stays outside emphasis and bold can be combined with italic',async()=>{
  assert.equal((await run(' текст ',0,7,'bold')).value,' **текст** ');
  assert.equal((await run(' **текст** ',0,11,'bold')).value,' текст ');
  assert.equal((await run('**слово**',0,9,'italic')).value,'***слово***');
  const a=await run('текст',0,5,'bold');
  assert.equal((await run(a.value,0,a.value.length,'bold')).value,'текст');
});
test('multiple paragraphs are formatted separately and toggled without changing blank lines',async()=>{
  const original='первый\n\n  второй  ';
  const a=await run(original,0,original.length,'strike');
  assert.equal(a.value,'~~первый~~\n\n  ~~второй~~  ');
  assert.equal((await run(a.value,0,a.value.length,'strike')).value,original);
});
test('empty commands provide an editable placeholder or a caret after the line prefix',async()=>{
  const bold=await run('',0,0,'bold'); assert.equal(bold.value.slice(bold.start,bold.end),'текст');
  for(const [command,expected] of [['h2','## '],['bullet','- '],['ordered','1. '],['task','- [ ] '],['quote','> ']]) {
    const a=await run('',0,0,command); assert.equal(a.value,expected); assert.equal(a.start,expected.length); assert.equal(a.end,a.start);
  }
});
test('block formatting uses whole lines and excludes the next line at a selection boundary',async()=>{
  const original='один\nдва\nтри';
  assert.equal((await run(original,0,5,'h2')).value,'## один\nдва\nтри');
  assert.equal((await run(original,1,7,'ordered')).value,'1. один\n2. два\nтри');
  assert.equal((await run('\nтекст',0,0,'h1')).value,'# \nтекст');
});
test('heading levels and list kinds convert without accumulating prefixes or losing nested indentation',async()=>{
  assert.equal((await run('## текст',4,4,'h3')).value,'### текст');
  assert.equal((await run('### текст',4,4,'h0')).value,'текст');
  const list='- [x] один\n  - два';
  const a=await run(list,0,list.length,'ordered'); assert.equal(a.value,'1. один\n  2. два');
  assert.equal((await run(a.value,0,a.value.length,'ordered')).value,'один\n  два');
});
test('block commands retain the caret inside its line when adding and removing prefixes',async()=>{
  for (const [command, expected, caret] of [
    ['h2','до\n## текст\nпосле',9],
    ['bullet','до\n- текст\nпосле',8],
    ['ordered','до\n1. текст\nпосле',9],
    ['task','до\n- [ ] текст\nпосле',12],
    ['quote','до\n> текст\nпосле',8],
  ]) {
    const added=await run('до\nтекст\nпосле',6,6,command);
    assert.equal(added.value,expected,command);
    assert.equal(added.start,caret,command); assert.equal(added.end,caret,command);
    const removed=await run(added.value,caret,caret,command==='h2'?'h0':command);
    assert.equal(removed.value,'до\nтекст\nпосле',command);
    assert.equal(removed.start,6,command); assert.equal(removed.end,6,command);
  }
});
test('block selections include changed lines but leave the next line and empty rows intact',async()=>{
  const original='до\nодин\n\nдва\nпосле', start=4, end=original.indexOf('после');
  for (const [command,expected] of [
    ['h1','# один\n\n# два'],['bullet','- один\n\n- два'],
    ['ordered','1. один\n\n2. два'],['task','- [ ] один\n\n- [ ] два'],['quote','> один\n\n> два'],
  ]) {
    const changed=await run(original,start,end,command);
    assert.equal(changed.value,'до\n'+expected+'\nпосле',command);
    assert.equal(changed.start,3,command);
    assert.equal(changed.value.slice(changed.start,changed.end),expected,command);
  }
  await assert.rejects(run('текст',0,5,'unknown'),/Unknown formatting command/);
});
test('inline and fenced code support literal backticks and fences without breaking Markdown',async()=>{
  const {renderMarkdown}=await import('../src/markdown.mjs');
  const a=await run('a`b',0,3,'code'); assert.match(renderMarkdown(a.value).html,/<code>a`b<\/code>/);
  const content='let x=1;\n```\nlet y=2;'; const b=await run(content,0,content.length,'codeblock');
  assert.ok(b.value.startsWith('````\n')); assert.ok(renderMarkdown(b.value).html.includes('let y=2;'));
  assert.equal((await run(b.value,b.start,b.end,'codeblock')).value,content);
});
test('links escape brackets and select only the destination; image and table keep selected content',async()=>{
  const a=await run('Текст [1]',0,9,'link'); assert.equal(a.value,'[Текст \\[1\\]](https://)'); assert.equal(a.value.slice(a.start,a.end),'https://');
  const image=await run('Фото',0,4,'image'); assert.equal(image.value,'![Фото](image.png)'); assert.equal(image.value.slice(image.start,image.end),'image.png');
  const table=await run('A|B',0,3,'table'); assert.ok(table.value.includes('A\\|B')); assert.equal(table.value.slice(table.start,table.end),'Заголовок 1');
});
