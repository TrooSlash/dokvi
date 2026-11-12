import { renderMarkdown } from './markdown.mjs';
let highlighter, fullHighlighter = false;
self.onmessage = async ({ data: { id, text } }) => {
  try {
    // No language grammars are loaded for plain prose. All languages remain
    // available when a fenced code block is present.
    if (/`{3}|~{3}/.test(text)) highlighter ||= await import('highlight.js/lib/common').then(module => module.default);
    let result = renderMarkdown(text, null, highlighter);
    if (result.missingLanguages.length && !fullHighlighter) {
      highlighter = await import('highlight.js').then(module => module.default); fullHighlighter = true;
      result = renderMarkdown(text, null, highlighter);
    }
    let words = 0, lines = 1, inWord = false;
    for (let i = 0; i < text.length; i++) {
      const code = text.charCodeAt(i), space = code <= 32 || code === 160;
      if (!space && !inWord) words++;
      inWord = !space; if (code === 10) lines++;
    }
    delete result.missingLanguages;
    self.postMessage({ id, result: { ...result, words, lines } });
  } catch (error) { self.postMessage({ id, error: error.message || String(error) }); }
};
