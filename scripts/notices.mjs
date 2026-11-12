import { readFile, readdir, mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
const lock = JSON.parse(await readFile('package-lock.json', 'utf8'));
const notices = ['Dokvi — Third-party software notices\n\nThe following notices cover bundled libraries and build dependencies.\n'];
for (const [relative, meta] of Object.entries(lock.packages || {})) {
  if (!relative) continue;
  try {
    const files = await readdir(relative);
    const licenses = files.filter(file => /^(license|licence|copying|notice)(\.|$)/i.test(file));
    if (!licenses.length) continue;
    notices.push(`\n\n${'='.repeat(75)}\n${relative.replace(/^node_modules\//, '')} ${meta.version || ''}\n${'='.repeat(75)}\n`);
    for (const license of licenses) { try { notices.push(await readFile(path.join(relative, license), 'utf8')); } catch {} }
  } catch {}
}
await mkdir('ui', { recursive: true });
await writeFile('ui/THIRD-PARTY-NOTICES.txt', notices.join('\n'), 'utf8');
