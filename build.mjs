import { build } from 'esbuild';
import { mkdir, copyFile, cp, rm, readFile, writeFile } from 'node:fs/promises';
import { renderMoreMenu } from './scripts/menu-template.mjs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import './scripts/notices.mjs';
const buildDefines = { DOKVI_LICENSE_TEXT: JSON.stringify(await readFile('LICENSE', 'utf8')), DOKVI_VERSION: JSON.stringify(JSON.parse(await readFile('package.json','utf8')).version) };
const projectRoot = fileURLToPath(new URL('.', import.meta.url));
const assetDirectory = path.resolve(projectRoot, 'ui/assets');
if (!assetDirectory.startsWith(projectRoot) || path.basename(assetDirectory) !== 'assets') throw new Error('Invalid build output path');
await rm(assetDirectory, { recursive: true, force: true });
await mkdir(assetDirectory, { recursive: true });
const injectedMathEngine = {
  name: 'injected-math-engine',
  setup(builder) {
    builder.onResolve({ filter: /^katex$/ }, args => {
      if (args.importer.replaceAll('\\', '/').endsWith('/markdown-it-texmath/texmath.js')) return { path: 'injected-math-engine', namespace: 'math-engine' };
    });
    builder.onLoad({ filter: /.*/, namespace: 'math-engine' }, () => ({ contents: 'export default { renderToString() { throw new Error("Math engine must be injected"); } };', loader: 'js' }));
  },
};
await Promise.all([
  build({ entryPoints: ['src/renderer.js', 'src/markdown-worker.js'], outdir: assetDirectory, bundle: true, format: 'esm', splitting: true, minify: true, target: 'chrome140', define: buildDefines, plugins: [injectedMathEngine], legalComments: 'linked', loader: { '.md': 'text', '.woff': 'file', '.woff2': 'file', '.ttf': 'file' }, assetNames: 'fonts/[name]-[hash]', metafile: true, logLevel: 'info' }).then(result => writeFile('build-meta.json', JSON.stringify(result.metafile))),
  readFile('src/index.html', 'utf8').then(html => writeFile('ui/index.html', html.replace('<!-- MORE_MENU -->', renderMoreMenu()))),
  copyFile('src/menu-items.json', 'ui/menu-items.json'),
  copyFile('src/icon.svg', 'ui/icon.svg'),
  copyFile('src/icon-dark.svg', 'ui/icon-dark.svg'),
  copyFile('src/style.css', 'ui/style.css'),
  copyFile('src/chrome.css', 'ui/chrome.css'),
  copyFile('src/sample.md', 'ui/sample.md'),
  copyFile('src/sample.en.md', 'ui/sample.en.md'),
  copyFile('src/native-bridge.js', 'ui/native-bridge.js'),
  copyFile('LICENSE', 'ui/LICENSE'),
  cp('node_modules/katex/dist/fonts', 'ui/fonts', { recursive: true }),
  copyFile('node_modules/katex/dist/katex.min.css', 'ui/katex.min.css')
]);
