const copy = text => text ? JSON.parse(JSON.stringify(text)) : '';
const selection = state => ({ start: state.start, end: state.end, top: state.top });

// Keep changed text, not a complete copy of the document at every undo step.
export class TextHistory {
  constructor(value, { maxBytes = 8 * 1024 * 1024, maxSteps = 100 } = {}) {
    this.value = value; this.past = []; this.future = [];
    this.bytes = 0; this.maxBytes = maxBytes; this.maxSteps = maxSteps;
  }
  record(value, before, after, group = false) {
    const old = this.value;
    if (old === value) return;
    let at = 0, oldEnd = old.length, newEnd = value.length;
    while (at < oldEnd && at < newEnd && old.charCodeAt(at) === value.charCodeAt(at)) at++;
    while (oldEnd > at && newEnd > at && old.charCodeAt(oldEnd - 1) === value.charCodeAt(newEnd - 1)) { oldEnd--; newEnd--; }
    const change = { at, removed: copy(old.slice(at, oldEnd)), inserted: copy(value.slice(at, newEnd)), before: selection(before), after: selection(after) };
    const size = patch => (patch.removed.length + patch.inserted.length) * 2;
    this.bytes -= this.future.reduce((sum, patch) => sum + size(patch), 0);
    this.future = [];
    const previous = this.past.at(-1);
    if (group && previous && !change.removed && !previous.removed && change.at === previous.at + previous.inserted.length) {
      previous.inserted += change.inserted; previous.after = change.after;
    } else if (group && previous && !change.inserted && !previous.inserted && change.at + change.removed.length === previous.at) {
      previous.removed = change.removed + previous.removed; previous.at = change.at; previous.after = change.after;
    } else this.past.push(change);
    this.bytes += size(change); this.value = value;
    while (this.past.length > 1 && (this.past.length > this.maxSteps || this.bytes > this.maxBytes)) this.bytes -= size(this.past.shift());
  }
  step(redo = false) {
    const from = redo ? this.future : this.past, to = redo ? this.past : this.future;
    const patch = from.pop(); if (!patch) return null;
    const remove = redo ? patch.removed : patch.inserted, insert = redo ? patch.inserted : patch.removed;
    this.value = this.value.slice(0, patch.at) + insert + this.value.slice(patch.at + remove.length);
    to.push(patch);
    return { value: this.value, ...(redo ? patch.after : patch.before) };
  }
}
