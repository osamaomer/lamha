/* Lamha desktop — browser.storage.local / .sync replacement: one JSON file per area in the user-data folder.
 * Same semantics as the WebExtension API (get with key / array / defaults object / null, set, remove). */
"use strict";
const fs = require("node:fs");
const path = require("node:path");

class Store {
  constructor(file, area, onChange) {
    this.file = file;
    this.area = area;
    this.onChange = onChange; // (changes, area) => void
    this.data = {};
    try { this.data = JSON.parse(fs.readFileSync(file, "utf8")); } catch (_) { /* first run or unreadable: start empty */ }
    this.timer = null;
  }

  async get(keys) {
    const d = this.data;
    const pick = k => structuredClone(d[k]);
    if (keys == null) return structuredClone(d);
    if (typeof keys === "string") keys = [keys];
    if (Array.isArray(keys)) return Object.fromEntries(keys.filter(k => k in d).map(k => [k, pick(k)]));
    return Object.fromEntries(Object.entries(keys).map(([k, def]) => [k, k in d ? pick(k) : def]));
  }

  async set(items) {
    const changes = {};
    for (const [k, v] of Object.entries(items || {})) {
      if (v === undefined) continue;
      changes[k] = { oldValue: this.data[k], newValue: structuredClone(v) };
      this.data[k] = structuredClone(v);
    }
    this.commit(changes);
  }

  async remove(keys) {
    const changes = {};
    for (const k of [].concat(keys)) {
      if (!(k in this.data)) continue;
      changes[k] = { oldValue: this.data[k] };
      delete this.data[k];
    }
    this.commit(changes);
  }

  async clear() { await this.remove(Object.keys(this.data)); }

  commit(changes) {
    if (!Object.keys(changes).length) return;
    this.scheduleSave();
    this.onChange(changes, this.area);
  }

  scheduleSave() {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.flush(), 250);
  }

  /** Write now (atomically: temp file + rename, so a crash never leaves half a file). */
  flush() {
    clearTimeout(this.timer);
    this.timer = null;
    fs.mkdirSync(path.dirname(this.file), { recursive: true });
    const tmp = this.file + ".tmp";
    fs.writeFileSync(tmp, JSON.stringify(this.data));
    fs.renameSync(tmp, this.file);
  }
}

module.exports = { Store };
