import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const output = join(root, 'dist', 'site');
const siteUrl = 'https://trooslash.github.io/dokvi/';
const repository = 'https://github.com/TrooSlash/dokvi';
const copy = JSON.parse(await readFile(join(root, 'site', 'content.json'), 'utf8'));
const release = JSON.parse(await readFile(join(root, 'site', 'release.json'), 'utf8'));
const languages = {
  en: { path: '', prefix: './', name: 'English', og: 'en_US', guide: `${repository}#readme` },
  ru: { path: 'ru/', prefix: '../', name: 'Русский', og: 'ru_RU', guide: `${repository}/blob/main/docs/README.ru.md` },
};

if (!/^\d+\.\d+\.\d+$/.test(release.version) || !Number.isSafeInteger(release.sizeBytes) || release.sizeBytes <= 0) {
  throw new Error('Invalid published release version or size');
}
if (release.downloadUrl !== `${repository}/releases/download/v${release.version}/Dokvi-${release.version}.exe`
    || release.releaseUrl !== `${repository}/releases/tag/v${release.version}`) {
  throw new Error('Release links must point to the versioned Dokvi release');
}

const escape = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);
const stylesheet = await readFile(join(root, 'site', 'style.css'));
const styleVersion = createHash('sha256').update(stylesheet).digest('hex').slice(0, 10);
const alternates = Object.entries(languages).map(([lang, info]) => `<link rel="alternate" hreflang="${lang}" href="${siteUrl}${info.path}">`).join('\n');

function render(lang) {
  const text = copy[lang];
  const current = languages[lang];
  const url = siteUrl + current.path;
  const assets = current.prefix + 'assets/';
  const size = new Intl.NumberFormat(lang, { maximumFractionDigits: 1 }).format(release.sizeBytes / 1024 ** 2);
  const download = `<a class="button" href="${escape(release.downloadUrl)}">${escape(text.download)}</a>`;
  const metadata = `<p class="download-meta">v${escape(release.version)} · Windows x64 · ${size} ${escape(text.sizeUnit)}</p>`;
  const languageLinks = Object.entries(languages).map(([code, info]) => code === lang
    ? `<span lang="${code}" aria-current="page">${info.name}</span>`
    : `<a lang="${code}" hreflang="${code}" rel="alternate" href="${current.prefix}${info.path}">${info.name}</a>`).join('');
  const structuredData = {
    '@context': 'https://schema.org', '@type': 'SoftwareApplication',
    name: 'Dokvi', description: text.description, url,
    softwareVersion: release.version, operatingSystem: 'Windows 10 / Windows 11 (x64)',
    applicationCategory: 'ProductivityApplication', inLanguage: ['en', 'ru'],
    downloadUrl: release.downloadUrl, fileSize: `${release.sizeBytes} bytes`,
    screenshot: `${siteUrl}assets/editor.png`, image: `${siteUrl}assets/icon.svg`,
    license: `${repository}/blob/main/LICENSE`,
    author: { '@type': 'Person', name: 'TrooSlash', url: 'https://github.com/TrooSlash' },
    offers: { '@type': 'Offer', price: '0', priceCurrency: 'USD' },
  };
  return `<!doctype html>
<html lang="${lang}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${escape(text.title)}</title>
<meta name="description" content="${escape(text.description)}">
<meta name="robots" content="index, follow, max-image-preview:large">
<meta name="theme-color" content="#faf9f7">
<link rel="canonical" href="${url}">
${alternates}
<link rel="alternate" hreflang="x-default" href="${siteUrl}">
<link rel="sitemap" type="application/xml" href="${siteUrl}sitemap.xml">
<meta property="og:type" content="website">
<meta property="og:site_name" content="Dokvi">
<meta property="og:title" content="${escape(text.title)}">
<meta property="og:description" content="${escape(text.description)}">
<meta property="og:url" content="${url}">
<meta property="og:locale" content="${current.og}">
<meta property="og:locale:alternate" content="${languages[lang === 'en' ? 'ru' : 'en'].og}">
<meta property="og:image" content="${siteUrl}assets/editor.png">
<meta property="og:image:width" content="1120">
<meta property="og:image:height" content="820">
<meta property="og:image:alt" content="${escape(text.imageAlt)}">
<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:title" content="${escape(text.title)}">
<meta name="twitter:description" content="${escape(text.description)}">
<meta name="twitter:image" content="${siteUrl}assets/editor.png">
<meta name="twitter:image:alt" content="${escape(text.imageAlt)}">
<link rel="icon" type="image/svg+xml" href="${assets}icon.svg">
<link rel="stylesheet" href="${assets}style.css?v=${styleVersion}">
<script type="application/ld+json">${JSON.stringify(structuredData).replaceAll('<', '\\u003c')}</script>
</head>
<body>
<a class="skip" href="#main">${escape(text.skip)}</a>
<header class="header wrap">
  <a class="brand" href="./"><img src="${assets}icon.svg" alt="" width="28" height="28">Dokvi</a>
  <nav class="nav" aria-label="${escape(text.nav)}">
    <a href="#features">${escape(text.featuresLink)}</a>
    <a href="${repository}">GitHub</a>
    <div class="languages" role="group" aria-label="${escape(text.languageLabel)}">${languageLinks}</div>
  </nav>
</header>
<main id="main">
  <section class="hero wrap" aria-labelledby="title">
    <p class="platform">${escape(text.platform)}</p>
    <div class="hero-copy">
      <h1 id="title">${escape(text.heading)}</h1>
      <div>
        <p class="intro">${escape(text.intro)}</p>
        ${download}${metadata}
        <a class="requirements-link" href="#requirements">${escape(text.requirementsLink)}</a>
      </div>
    </div>
    <figure class="preview">
      <a class="screen-link" href="${assets}editor.png"><img src="${assets}editor.png" alt="${escape(text.imageAlt)}" width="1120" height="820" fetchpriority="high"></a>
      <figcaption>${escape(text.imageCaption)}</figcaption>
    </figure>
    <details class="reading">
      <summary>${escape(text.readingToggle)}</summary>
      <img src="${assets}reading.png" alt="${escape(text.readingAlt)}" width="1120" height="820" loading="lazy" decoding="async">
    </details>
  </section>
  <section class="features wrap" id="features" aria-labelledby="features-title">
    <div><h2 id="features-title">${escape(text.sectionHeading)}</h2><p class="section-intro">${escape(text.sectionIntro)}</p></div>
    <dl>${text.features.map(([title, body]) => `<div><dt>${escape(title)}</dt><dd>${escape(body)}</dd></div>`).join('')}</dl>
  </section>
  <section class="start" aria-labelledby="start-title">
    <div class="start-grid wrap">
      <div><h2 id="start-title">${escape(text.startHeading)}</h2><p class="section-intro">${escape(text.startIntro)}</p>${download}${metadata}</div>
      <div><ol>${text.steps.map(step => `<li>${escape(step)}</li>`).join('')}</ol>
        <p class="more"><a href="${current.guide}" aria-label="${escape(text.guide)} (${escape(text.guideNote)})">${escape(text.guide)}</a><a href="${escape(release.releaseUrl)}">${escape(text.releaseLink)}</a></p>
      </div>
      <div class="requirements" id="requirements">
        <h3>${escape(text.requirementsHeading)}</h3>
        <p>${escape(text.windowsRequirement)}</p>
        <p>${escape(text.runtimeIntro)} <a href="https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48">.NET Framework 4.8</a> ${escape(text.and)} <a href="https://developer.microsoft.com/microsoft-edge/webview2/">Microsoft Edge WebView2 Runtime</a>. ${escape(text.runtimeNote)}</p>
        <p>${escape(text.releaseNote)}</p>
      </div>
    </div>
  </section>
</main>
<footer class="footer wrap">
  <p>${escape(text.author)} <a href="https://github.com/TrooSlash">TrooSlash</a> · <a href="${repository}/blob/main/LICENSE">MIT</a></p>
  <nav aria-label="${escape(text.source)}"><a href="${repository}">${escape(text.source)}</a><a href="${repository}/issues">${escape(text.issue)}</a><a href="https://tips.tips/000491268">${escape(text.support)}</a></nav>
</footer>
</body>
</html>
`;
}

await mkdir(join(output, 'assets'), { recursive: true });
for (const [source, destination] of [
  ['site/style.css', 'assets/style.css'], ['src/icon.svg', 'assets/icon.svg'],
  ['docs/images/editor.png', 'assets/editor.png'], ['docs/images/reading.png', 'assets/reading.png'],
]) await copyFile(join(root, source), join(output, destination));

for (const [lang, info] of Object.entries(languages)) {
  await mkdir(join(output, info.path), { recursive: true });
  await writeFile(join(output, info.path, 'index.html'), render(lang));
}
const urls = Object.values(languages).map(info => `<url><loc>${siteUrl}${info.path}</loc></url>`).join('\n');
await writeFile(join(output, 'sitemap.xml'), `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${urls}\n</urlset>\n`);
await writeFile(join(output, '.nojekyll'), '');
console.log(`Built English and Russian pages in ${output}`);
