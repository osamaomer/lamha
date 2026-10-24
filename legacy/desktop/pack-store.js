/* Lamha desktop — where downloaded language packs live (packs.js): one JSON file per part, in %APPDATA%\Lamha\packs.
 * The browser keeps them in IndexedDB instead; the background logic reaches either through the same three calls. */
"use strict";
const fs = require("node:fs");
const path = require("node:path");

const KEY = /^([a-z]{2,3})\/(meta|[a-z_]{2})$/; // "fr/meta", "fr/ma": nothing else becomes a file name

function createPackStore(dir) {
  const file = key => {
    const m = KEY.exec(String(key));
    if (!m) throw new Error("bad pack key");
    return path.join(dir, m[1], m[2] + ".json");
  };
  return {
    async get(key) {
      try { return JSON.parse(await fs.promises.readFile(file(key), "utf8")); } catch (_) { return undefined; }
    },
    async setMany(entries) {
      for (const [key, value] of Object.entries(entries)) {
        const f = file(key);
        await fs.promises.mkdir(path.dirname(f), { recursive: true });
        await fs.promises.writeFile(f + ".tmp", JSON.stringify(value));
        await fs.promises.rename(f + ".tmp", f); // never half a file
      }
    },
    async removePrefix(prefix) {
      const m = /^([a-z]{2,3})\/$/.exec(String(prefix));
      if (!m) throw new Error("bad pack prefix");
      await fs.promises.rm(path.join(dir, m[1]), { recursive: true, force: true });
    }
  };
}

module.exports = { createPackStore };
