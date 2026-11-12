import english from './locales/en.json' with { type: 'json' };

// Russian source strings are stable catalog keys shared with the native host.
// Only application-owned labels are translated, never document content.
export function resolveLanguage(preference, systemLanguage) {
  if (preference === 'ru' || preference === 'en') return preference;
  return /^ru(?:[-_]|$)/i.test(systemLanguage || '') ? 'ru' : 'en';
}
let language = 'ru';
export function setLanguage(preference, systemLanguage) { language = resolveLanguage(preference, systemLanguage); return language; }
export function getLanguage() { return language; }
export function t(source, ...args) {
  const value = language === 'en' ? (english[source] ?? source) : source;
  return value.replace(/\{(\d+)\}/g, (match, index) => index < args.length ? String(args[index]) : match);
}
export function bindStaticTranslations(root) {
  const records = [];
  const walker = root.ownerDocument.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  while (walker.nextNode()) {
    const node = walker.currentNode, original = node.nodeValue, source = original.trim();
    if (Object.hasOwn(english, source)) records.push(() => { node.nodeValue = original.replace(source, () => t(source)); });
  }
  for (const node of root.querySelectorAll('[title],[aria-label],[placeholder]')) {
    for (const attribute of ['title', 'aria-label', 'placeholder']) {
      const source = node.getAttribute(attribute);
      if (Object.hasOwn(english, source)) records.push(() => node.setAttribute(attribute, t(source)));
    }
  }
  return () => { for (const apply of records) apply(); root.ownerDocument.documentElement.lang = language; };
}
