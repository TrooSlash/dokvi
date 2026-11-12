import { t } from './i18n.mjs';
// Return one replacement and its selection, so every toolbar command is one
// undo step and does not replace the editor's complete document.
export function formatMarkdown(text, start, end, command) {
  start = Math.max(0, Math.min(text.length, start));
  end = Math.max(start, Math.min(text.length, end));
  const edit = (from, to, insert, a = from, b = from + insert.length) => ({ from, to, insert, start: a, end: b });
  const selected = text.slice(start, end);
  const wraps = { bold: '**', italic: '*', strike: '~~', highlight: '==' };
  if (command in wraps || command === 'code') {
    let marker = wraps[command];
    if (command === 'code') {
      const left = text.slice(0, start).match(/`+$/)?.[0];
      if (selected && left && text.slice(end).startsWith(left) && text[end + left.length] !== '`')
        return edit(start-left.length, end+left.length, selected);
      const fenced = selected.match(/^(`+)([\s\S]*?)\1$/);
      if (fenced && !fenced[2].startsWith('`') && !fenced[2].endsWith('`')) return edit(start,end,fenced[2]);
      marker = '`'.repeat(Math.max(0, ...[...selected.matchAll(/`+/g)].map(match => match[0].length)) + 1);
    }
    const wrapped = value => value.length >= marker.length * 2 && value.startsWith(marker) && value.endsWith(marker)
      && !(command === 'italic' && (/^\*{2}(?!\*)/.test(value) || /(?<!\*)\*{2}$/.test(value)));
    const outside = text.slice(start-marker.length,start) === marker && text.slice(end,end+marker.length) === marker
      && !(command === 'italic' && (text[start-2] === '*' || text[end+1] === '*'));
    if (selected && outside) return edit(start-marker.length,end+marker.length,selected);
    if (selected && wrapped(selected)) return edit(start,end,selected.slice(marker.length,-marker.length));
    if (selected.includes('\n') && command !== 'code') {
      const lines = selected.split('\n'), remove = lines.filter(line => line.trim()).every(line => wrapped(line.trim()));
      return edit(start,end,lines.map(line => line.replace(/^(\s*)(\S[\s\S]*?)(\s*)$/, (_,before,body,after) => before + (remove ? body.slice(marker.length,-marker.length) : wrapped(body) ? body : marker+body+marker) + after)).join('\n'));
    }
    const before = selected.match(/^\s*/)?.[0] || '', after = selected.trim() ? selected.match(/\s*$/)[0] : '';
    const body = selected.trim() || (command === 'code' ? t('код') : t('текст'));
    if (wrapped(body)) { const inner=body.slice(marker.length,-marker.length); return edit(start,end,before+inner+after,start+before.length,start+before.length+inner.length); }
    const padding = command === 'code' && (body.startsWith('`') || body.endsWith('`')) ? ' ' : '';
    const insert = before + marker + padding + body + padding + marker + after;
    const at = start + before.length + marker.length + padding.length;
    return edit(start,end,insert,at,at+body.length);
  }
  if (command === 'link' || command === 'image') {
    const label = (selected || (command === 'image' ? t('Описание') : t('текст ссылки'))).replace(/\r?\n/g,' ').replace(/[\\\[\]]/g,'\\$&');
    const prefix = (command === 'image' ? '!' : '') + '[' + label + '](';
    const address = command === 'image' ? 'image.png' : 'https://';
    return edit(start,end,prefix+address+')',start+prefix.length,start+prefix.length+address.length);
  }
  if (command === 'table' || command === 'rule') {
    const prefix = start && !text.slice(0,start).endsWith('\n\n') ? (text[start-1] === '\n' ? '\n' : '\n\n') : '';
    const suffix = text.slice(end).startsWith('\n\n') ? '' : text[end] === '\n' ? '\n' : '\n\n';
    const body = command === 'rule' ? '---' : '| ' + t('Заголовок 1') + ' | ' + t('Заголовок 2') + ' |\n| --- | --- |\n| ' + (selected.replace(/\n/g,' ').replace(/\|/g,'\\|') || t('текст')) + ' |  |';
    const at = start + prefix.length + (command === 'table' ? 2 : body.length + suffix.length);
    return edit(start,end,prefix+body+suffix,at,at+(command === 'table' ? t('Заголовок 1').length : 0));
  }
  const from = start === 0 ? 0 : text.lastIndexOf('\n',start-1)+1;
  const boundary = end>start && text[end-1] === '\n' ? end-1 : end;
  const nextLine = text.indexOf('\n',boundary), to = nextLine<0 ? text.length : nextLine;
  const block = text.slice(from,to), lines = block.split('\n');
  const blockEdit = insert => {
    if (start !== end) return edit(from,to,insert,from,from+insert.length);
    const caret = Math.max(from,Math.min(from+insert.length,start+insert.length-block.length));
    return edit(from,to,insert,caret,caret);
  };
  if (command === 'codeblock') {
    const enclosing = text.slice(0,from).match(/(^|\n)(`{3,}|~{3,})[^\n]*\n$/);
    if (enclosing && text.slice(to).startsWith('\n'+enclosing[2]) && /^(\n|$)/.test(text.slice(to+1+enclosing[2].length)))
      return edit(from-enclosing[0].length+enclosing[1].length,to+1+enclosing[2].length,block);
    const fence = '`'.repeat(Math.max(2,...[...block.matchAll(/`+/g)].map(match=>match[0].length))+1);
    const body = block || t('код');
    return edit(from,to,fence+'\n'+body+'\n'+fence,from+fence.length+1,from+fence.length+1+body.length);
  }
  if (/^h[0-6]$/.test(command)) {
    const prefix = '#'.repeat(Number(command[1]));
    const insert = lines.map(line => {
      if (!line.trim() && lines.length>1) return line;
      const clean = line.replace(/^ {0,3}#{1,6}(?:\s+|$)/,'');
      return prefix ? prefix+' '+clean : clean;
    }).join('\n');
    return blockEdit(insert);
  }
  const listPrefix = /^(\s*)(?:[-+*]|\d+[.)])\s+(?:\[[ xX]\]\s+)?/;
  const patterns = { bullet:/^\s*[-+*]\s+(?!\[[ xX]\]\s)/, ordered:/^\s*\d+[.)]\s+/, task:/^\s*[-+*]\s+\[[ xX]\]\s+/, quote:/^\s*>\s?/ };
  if (!(command in patterns)) throw new Error('Unknown formatting command: '+command);
  const nonempty = lines.filter(line=>line.trim()), remove = nonempty.length>0 && nonempty.every(line=>patterns[command].test(line));
  let number = 0;
  const insert = lines.map(line => {
    if (!line.trim() && lines.length>1) return line;
    if (command === 'quote') return remove ? line.replace(/^(\s*)> ?/,'$1') : line.replace(/^(\s*)/,'$1> ');
    const indent = line.match(/^\s*/)[0], body = line.replace(listPrefix,'$1').slice(indent.length);
    return indent + (remove ? '' : command === 'bullet' ? '- ' : command === 'task' ? '- [ ] ' : (++number)+'. ') + body;
  }).join('\n');
  return blockEdit(insert);
}
