import MarkdownIt from 'markdown-it';
import footnote from 'markdown-it-footnote';
import tasks from 'markdown-it-task-lists';
import deflist from 'markdown-it-deflist';
import mark from 'markdown-it-mark';
import sub from 'markdown-it-sub';
import sup from 'markdown-it-sup';
import { full as emoji } from 'markdown-it-emoji';
import texmath from 'markdown-it-texmath';
import { sanitizeMarkdown } from './sanitize.mjs';

function createRenderer() {
  let formulas = [], diagrams = [], missingLanguages = [], sourceOffset = 0, hljs = null;
  const md = new MarkdownIt({ html: true, linkify: true, typographer: false, breaks: true });
  md.use(footnote).use(tasks).use(deflist).use(mark).use(sub).use(sup).use(emoji);
  const sourceAttrs = token => token.map ? ' data-source-line="' + (token.map[0] + sourceOffset) + '" data-source-end="' + (token.map[1] + sourceOffset) + '"' : '';
  md.core.ruler.after('block', 'blank-lines', state => {
    const lines = state.src.split('\n'), tokens = [];
    let depth = 0, end = 0, hasBlock = sourceOffset > 0;
    const gap = (from, to, interior) => {
      if (interior) from++; // The first blank line already separates Markdown blocks.
      if (to <= from || !lines.slice(from, to).every(line => /^\s*$/.test(line))) return;
      const token = new state.Token('blank_lines', 'div', 0);
      token.block = true; token.map = [from, to]; token.meta = to - from;
      tokens.push(token);
    };
    for (const token of state.tokens) {
      if (depth === 0 && token.map && token.nesting >= 0) {
        gap(end, token.map[0], hasBlock);
        end = Math.max(end, token.map[1]); hasBlock = true;
      }
      tokens.push(token); depth += token.nesting;
    }
    gap(end, lines.length, false);
    state.tokens = tokens;
  });
  md.renderer.rules.blank_lines = (tokens, index) => '<div class="markdown-gap" data-blank-lines="' + tokens[index].meta + '"' + sourceAttrs(tokens[index]) + '></div>';
  for (const type of ['th_open', 'td_open']) {
    md.renderer.rules[type] = (tokens, index, options, env, renderer) => {
      const token = tokens[index];
      const alignment = token.attrGet('style')?.match(/^text-align:(left|center|right)$/)?.[1];
      if (alignment) {
        // Generated alignment must survive sanitization without allowing inline styles.
        token.attrs.splice(token.attrIndex('style'), 1);
        token.attrJoin('class', 'table-align-' + alignment);
      }
      return renderer.renderToken(tokens, index, options);
    };
  }
  md.core.ruler.push('source-lines', state => {
    for (const token of state.tokens) {
      if (!token.map || !token.block || token.nesting < 0) continue;
      token.attrSet('data-source-line', String(token.map[0] + sourceOffset));
      token.attrSet('data-source-end', String(token.map[1] + sourceOffset));
    }
  });
  md.use(texmath, { delimiters: ['dollars', 'brackets'], engine: { renderToString: (tex, options) => {
    const index = formulas.push({ tex, display: Boolean(options.displayMode) }) - 1;
    return '<span class="math-slot" data-math-id="' + index + '"></span>';
  } } });
  for (const type of ['math_block', 'math_block_eqno', 'code_block']) {
    const render = md.renderer.rules[type];
    if (render) md.renderer.rules[type] = (tokens, index, ...args) => '<div' + sourceAttrs(tokens[index]) + '>' + render(tokens, index, ...args) + '</div>';
  }
  md.renderer.rules.fence = (tokens, index) => {
    const token = tokens[index];
    const language = token.info.trim().split(/\s+/)[0].toLowerCase();
    if (language === 'mermaid') {
      const id = diagrams.push(token.content) - 1;
      return '<div class="mermaid-diagram" data-diagram-id="' + id + '"' + sourceAttrs(token) + ' data-ui="diagram" aria-label="Диаграмма Mermaid"><pre><code>' + md.utils.escapeHtml(token.content) + '</code></pre></div>';
    }
    let code = md.utils.escapeHtml(token.content);
    if (language && hljs && !hljs.getLanguage(language)) missingLanguages.push(language);
    if (language && hljs?.getLanguage(language)) {
      try { code = hljs.highlight(token.content, { language, ignoreIllegals: true }).value; } catch {}
    }
    return '<div class="code-block"' + sourceAttrs(token) + '><div class="code-toolbar"><span' + (language ? '' : ' data-ui="plain-code"') + '>' + md.utils.escapeHtml(language || 'текст') + '</span><button type="button" class="copy-code" data-ui="copy">Копировать</button></div><pre><code>' + code + '</code></pre></div>';
  };
  // Raw HTML cannot forge the line markers used for synchronized scrolling.
  const cleanRaw = content => content.replace(/\s(?:class|id|name|data-[\w-]+)\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+)/gi, '');
  md.renderer.rules.html_block = (tokens, index) => '<span class="source-anchor" aria-hidden="true"' + sourceAttrs(tokens[index]) + '></span>' + cleanRaw(tokens[index].content);
  md.renderer.rules.html_inline = (tokens, index) => cleanRaw(tokens[index].content);
  return (text, purify, highlighter) => {
  formulas = []; diagrams = []; missingLanguages = []; sourceOffset = 0; hljs = highlighter;
  let metadata = '';
  const front = text.match(/^---\r?\n([\s\S]*?)\r?\n(?:---|\.\.\.)[\t ]*(?:\r?\n|$)/);
  if (front) {
    sourceOffset = (front[0].match(/\n/g) || []).length;
    metadata = '<details class="metadata" data-source-line="0" data-source-end="' + sourceOffset + '"><summary data-ui="metadata">Метаданные документа</summary><pre>' + md.utils.escapeHtml(front[1]) + '</pre></details>';
    text = text.slice(front[0].length);
  }
  const raw = metadata + md.render(text);
  return { html: purify ? sanitizeMarkdown(raw, purify) : raw, formulas, diagrams, missingLanguages };
  };
}
const renderer = createRenderer();
export function renderMarkdown(text, purify, highlighter) { return renderer(text, purify, highlighter); }
