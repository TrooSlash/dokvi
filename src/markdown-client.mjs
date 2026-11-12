import { t } from './i18n.mjs';
let worker, active, pending, nextId = 0, idle;
function dispose() { worker?.terminate(); worker = null; }
function launch(job) {
  clearTimeout(idle);
  if (!worker) {
    worker = new Worker(new URL('./markdown-worker.js', import.meta.url), { type: 'module' });
    worker.onmessage = ({ data }) => {
      if (!active || active.id !== data.id) return;
      const done = active; active = null;
      if (data.error) done.reject(new Error(data.error)); else done.resolve(data.result);
      if (pending) { const job = pending; pending = null; launch(job); }
      else idle = setTimeout(dispose, 30000);
    };
    worker.onerror = event => {
      const error = new Error(event.message || t('Не удалось обработать Markdown.'));
      active?.reject(error); pending?.reject(error); active = pending = null; dispose();
    };
  }
  active = job; worker.postMessage({ id: job.id, text: job.text });
}
export function parseMarkdown(text) {
  return new Promise((resolve, reject) => {
    const job = { id: ++nextId, text, resolve, reject };
    if (active) { pending?.resolve(null); pending = job; } else launch(job);
  });
}
export function releaseParser() {
  pending?.resolve(null); pending = null;
  active?.resolve(null); active = null; clearTimeout(idle); dispose();
}
