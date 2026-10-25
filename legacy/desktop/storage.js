/* Lamha desktop — browser.storage.local / .sync replacement: one JSON file per area in the user-data folder.
 * Same semantics as the WebExtension API (get with key / array / defaults object / null, set, remove).
 * `secrets` (the API keys) are encrypted on disk with Windows DPAPI through Electron's safeStorage, like the
 * clipboard history; in memory, and to the background logic, they are plain strings. */
"use strict";
const fs = require("node:fs");
const path = require("node:path");

class Store {
  /**
   * @param {string} file
   * @param {string} area                        "local" | "sync"
   * @param {(changes: object, area: string) => void} onChange
   * @param {object} [o]
   * @param {string[]} [o.secrets]               keys stored encrypted: { "$enc": base64 } on disk
   * @param {object} [o.safeStorage]             Electron's safeStorage (or a test double); absent → plain text
   */
  constructor(file, area, onChange, { secrets = [], safeStorage = null } = {}) {
    this.file = file;
    this.area = area;
    this.onChange = onChange; // (changes, area) => void
    this.secrets = new Set(secrets);
    this.safeStorage = safeStorage;
    this.data = {};
    this.timer = null;
    this.load();
  }

  get canEncrypt() {
    try { return !!(this.safeStorage && this.safeStorage.isEncryptionAvailable()); } catch (_) { return false; }
  }

  load() {
    let raw;
    try { raw = fs.readFileSync(this.file, "utf8"); } catch (_) { return; } // first run
    let data;
    try {
      data = JSON.parse(raw);
      if (!data || typeof data !== "object" || Array.isArray(data)) throw new Error("not an object");
    } catch (err) {
      // keep the unreadable file (settings, review cards, keys) instead of overwriting it with an empty one
      const kept = this.file.replace(/\.json$/, "") + `.corrupt-${Date.now()}.json`;
      try { fs.renameSync(this.file, kept); } catch (_) { /* nothing to keep */ }
      console.warn(`[storage] ${path.basename(this.file)} unreadable (${err && err.name}) — starting empty; old file kept as ${path.basename(kept)}`);
      return;
    }
    let rewrite = false;
    for (const k of this.secrets) {
      const v = data[k];
      if (v && typeof v === "object" && typeof v.$enc === "string") {
        try {
          if (!this.canEncrypt) throw new Error("cannot decrypt on this machine");
          data[k] = this.safeStorage.decryptString(Buffer.from(v.$enc, "base64"));
        } catch (err) {
          delete data[k]; // e.g. the profile was copied from another Windows account: the key must be entered again
          console.warn(`[storage] ${k} could not be decrypted (${err && err.name}) — removed`);
        }
      } else if (typeof v === "string" && this.canEncrypt) {
        rewrite = true; // saved in plain text by an older version: encrypt it now
      }
    }
    this.data = data;
    if (rewrite) this.flush();
  }

  async get(keys) {
    const d = this.data;
    const has = k => Object.hasOwn(d, k);
    const pick = k => structuredClone(d[k]);
    if (keys == null) return structuredClone(d);
    if (typeof keys === "string") keys = [keys];
    if (Array.isArray(keys)) return Object.fromEntries(keys.filter(has).map(k => [k, pick(k)]));
    return Object.fromEntries(Object.entries(keys).map(([k, def]) => [k, has(k) ? pick(k) : def]));
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
      if (!Object.hasOwn(this.data, k)) continue;
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

  /** What goes on disk: the data with the secrets encrypted (when Windows can). */
  serialize() {
    const out = { ...this.data };
    if (this.canEncrypt) {
      for (const k of this.secrets) {
        if (typeof out[k] === "string") out[k] = { $enc: this.safeStorage.encryptString(out[k]).toString("base64") };
      }
    }
    return JSON.stringify(out);
  }

  /** Write now (atomically: temp file + rename, so a crash never leaves half a file). */
  flush() {
    clearTimeout(this.timer);
    this.timer = null;
    fs.mkdirSync(path.dirname(this.file), { recursive: true });
    const tmp = this.file + ".tmp";
    fs.writeFileSync(tmp, this.serialize());
    fs.renameSync(tmp, this.file);
  }
}

module.exports = { Store };
