export function sanitizeMarkdown(html, purify, fragment = false) {
  return purify.sanitize(html, {
    USE_PROFILES: { html: true },
    ADD_TAGS: ['eq', 'eqn', 'section'],
    FORBID_TAGS: ['style', 'form', 'textarea', 'select', 'iframe', 'object', 'embed', 'script', 'svg', 'math', 'video', 'audio', 'source', 'link', 'meta', 'base'],
    FORBID_ATTR: ['style', 'srcset', 'autofocus', 'contenteditable', 'formaction'],
    ALLOW_DATA_ATTR: true,
    RETURN_DOM_FRAGMENT: fragment,
  });
}
