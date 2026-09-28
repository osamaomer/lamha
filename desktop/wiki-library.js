/* Lamha desktop — offline Wikipedia: the files the user downloaded (or already had), and what the card reads from them.
 *
 * - The list of what can be downloaded comes from Kiwix's catalog (library.kiwix.org, OPDS), one language at a time.
 *   Settings only sends back a catalog id; the URL is always the catalog's, never the page's.
 * - Downloads go to the chosen folder (default %APPDATA%\Lamha\wikipedia) as `<file>.zim.part`, from the fastest of the
 *   file's mirrors (its .meta4 list, each tried briefly). They resume where they stopped (HTTP Range), move to another
 *   mirror when one stalls, survive a restart, and are checked against Kiwix's SHA-256 before they're used.
 * - The card asks summary(titles, lang): the opening paragraph of the first matching article (desktop/zim.js reads it).
 *
 * State lives in `wikipedia.json` next to the other data files: { folder, files: [...], downloads: [...] }. */
"use strict";
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");
const { EventEmitter, once } = require("node:events");
const { ZimFile } = require("./zim");

const CATALOG = "https://library.kiwix.org/catalog/v2/entries";
const DOWNLOADS = "https://download.kiwix.org/zim/wikipedia/";
const FILE_RE = /^wikipedia_[a-z0-9_-]+\.zim$/i;
/** Settings' languages → the catalog's (ISO 639-3). */
const LANG3 = {
  ar: "ara", en: "eng", fr: "fra", de: "deu", es: "spa", tr: "tur", fa: "fas", ur: "urd", it: "ita", pt: "por",
  ru: "rus", zh: "zho", ja: "jpn", ko: "kor", hi: "hin", id: "ind", ms: "msa", nl: "nld", sv: "swe", pl: "pol", he: "heb"
};
const LANG2 = Object.fromEntries(Object.entries(LANG3).map(([a, b]) => [b, a]));
const PROBE_BYTES = 1024 * 1024;
const STALL_MS = 30000;
const SPACE_MARGIN = 200 * 1024 * 1024;

/* ---------------- reading an article's opening ---------------- */

const ENTITIES = { amp: "&", lt: "<", gt: ">", quot: "\"", apos: "'", nbsp: " ", "#39": "'" };
function decodeEntities(s) {
  return s.replace(/&(#x[0-9a-f]+|#\d+|[a-z]+\d*);/gi, (m, e) => {
    if (e[0] === "#") {
      const n = e[1] === "x" || e[1] === "X" ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10);
      return n > 0 && n < 0x110000 ? String.fromCodePoint(n) : "";
    }
    return Object.hasOwn(ENTITIES, e.toLowerCase()) ? ENTITIES[e.toLowerCase()] : m;
  });
}

/** Removes every <tag>…</tag> of this name, the innermost first (tables inside tables). */
function dropElements(html, tag) {
  const re = new RegExp(`<${tag}\\b[^>]*>(?:(?!<${tag}\\b)[\\s\\S])*?</${tag}>`, "gi");
  for (let i = 0; i < 30; i++) {
    const next = html.replace(re, " ");
    if (next === html) break;
    html = next;
  }
  return html;
}

/** The article's lead: section 0 (the whole page in "mini" files). */
function leadSection(html) {
  const s = html.search(/data-mw-section-id="0"/);
  const from = s >= 0 ? s : Math.max(0, html.search(/<body\b/i));
  const next = html.slice(from + 1).search(/<section\b/i);
  return html.slice(from, next >= 0 ? from + 1 + next : html.length);
}

/**
 * The opening paragraph as plain text: no reference marks, info boxes or pronunciation boxes. A very short first
 * paragraph gets the next one too. At most `max` characters, cut at a word.
 */
function leadText(html, max = 900) {
  let lead = leadSection(String(html));
  for (const tag of ["style", "script", "table", "figure", "sup", "math"]) lead = dropElements(lead, tag);
  const out = [];
  for (const m of lead.matchAll(/<p\b[^>]*>([\s\S]*?)<\/p>/gi)) {
    const text = decodeEntities(m[1].replace(/<[^>]+>/g, ""))
      .replace(/\[\s*(\d+|[a-z]|\S{1,12})\s*\]/gi, "") // leftover [1], [a], [بحاجة لمصدر]
      .replace(/\(\s*[;,،]?\s*\)/g, "") // parentheses emptied by the removals
      .replace(/\(\s+/g, "(").replace(/\s+\)/g, ")")
      .replace(/\s+/g, " ").replace(/\s+([.,;:،؛])/g, "$1").trim();
    if (text.replace(/[^\p{L}]/gu, "").length < 25) continue; // coordinates, empty paragraphs
    out.push(text);
    if (out.join(" ").length >= 160) break;
  }
  let text = out.join(" ");
  if (text.length > max) text = text.slice(0, text.lastIndexOf(" ", max) > max * 0.6 ? text.lastIndexOf(" ", max) : max) + "…";
  return text;
}

/** Disambiguation pages ("X may refer to") are lists, not an answer: skipped, like the online summary does. Only the
 *  page's own mark counts: ordinary articles link to theirs ("for other meanings…", class "mw-disambig"). */
const isDisambiguation = html => /mw:PageProp\/disambiguation|id="disambigbox"/i.test(html);

/** The article's address on Wikipedia itself (read more, when online). */
function canonicalUrl(html) {
  const m = /<link\s+rel="canonical"\s+href="(https:\/\/[a-z0-9-]+\.wikipedia\.org\/wiki\/[^"]+)"/i.exec(html);
  return m ? decodeEntities(m[1]) : "";
}

/** The first real picture of the lead (not an icon or a flag in a sentence), as a path inside the file. */
function leadImage(html, articlePath) {
  const lead = leadSection(String(html));
  for (const m of lead.matchAll(/<img\b[^>]*>/gi)) {
    const attr = name => (new RegExp(`\\s${name}="([^"]*)"`, "i").exec(m[0]) || [])[1] || "";
    if (Number(attr("width")) && Number(attr("width")) < 60) continue;
    const src = decodeEntities(attr("src"));
    if (!src || /^[a-z]+:/i.test(src) || src.startsWith("//")) continue;
    try {
      const url = new URL(src, "http://zim/C/" + articlePath.split("/").map(encodeURIComponent).join("/"));
      const p = decodeURIComponent(url.pathname);
      if (p.startsWith("/C/")) return p.slice(3);
    } catch (_) { /* a broken address */ }
  }
  return "";
}

/* ---------------- Kiwix's catalog and mirrors ---------------- */

const tagText = (xml, tag) => { const m = new RegExp(`<${tag}>([^<]*)</${tag}>`).exec(xml); return m ? decodeEntities(m[1]).trim() : ""; };

/** The catalog's entries: { id, file, name, scope, flavour, lang, title, summary, articles, size, date, pictures }. */
function parseCatalog(xml) {
  const out = [];
  for (const [entry] of String(xml).matchAll(/<entry>[\s\S]*?<\/entry>/g)) {
    const link = /<link\s+rel="http:\/\/opds-spec\.org\/acquisition\/open-access"[^>]*href="([^"]+)"[^>]*length="(\d+)"/.exec(entry);
    if (!link) continue;
    const file = decodeURIComponent(link[1].split("/").pop()).replace(/\.meta4$/, "");
    if (!FILE_RE.test(file)) continue;
    const name = tagText(entry, "name");
    const lang = LANG2[tagText(entry, "language").split(",")[0]] || (/^wikipedia_([a-z]{2,3})_/.exec(name) || [])[1] || "";
    const flavour = tagText(entry, "flavour");
    const date = (tagText(entry, "dc:issued") || tagText(entry, "updated")).slice(0, 10);
    out.push({
      id: file.replace(/\.zim$/, ""), file, name, flavour, lang,
      scope: name.replace(/^wikipedia_[a-z]{2,3}_/, ""),
      title: tagText(entry, "title"), summary: tagText(entry, "summary"),
      articles: Number(tagText(entry, "articleCount")) || 0,
      size: Number(link[2]) || 0, date,
      pictures: /_pictures:yes/.test(tagText(entry, "tags"))
    });
  }
  return out;
}

/** The mirrors in a .meta4 file (https only, the same file name), best first as the file lists them. */
function parseMeta4(xml, file) {
  const urls = [];
  for (const m of String(xml).matchAll(/<url\b([^>]*)>(https:\/\/[^<\s]+)<\/url>/g)) {
    const url = decodeEntities(m[2]);
    const priority = Number((/\bpriority="(\d+)"/.exec(m[1]) || [])[1]) || 99;
    if (url.split("/").pop() === file && !urls.some(u => u.url === url)) urls.push({ url, priority });
  }
  return urls.sort((a, b) => a.priority - b.priority).map(u => u.url);
}

/* ---------------- the library ---------------- */

/** Which file answers first for a language: the most articles (all before top before a topic), then pictures. */
const SCOPE_RANK = { all: 0, top: 1 };
const FLAVOUR_RANK = { maxi: 0, nopic: 1, mini: 2 };
const rank = f => (Object.hasOwn(SCOPE_RANK, f.scope) ? SCOPE_RANK[f.scope] : 2) * 10 + (Object.hasOwn(FLAVOUR_RANK, f.flavour) ? FLAVOUR_RANK[f.flavour] : 3);

class WikiLibrary extends EventEmitter {
  /**
   * dataDir: where wikipedia.json lives, and the default folder's parent. fetch: for tests. now: for tests.
   */
  constructor({ dataDir, fetch = globalThis.fetch, now = Date.now }) {
    super();
    this.dataDir = dataDir;
    this.stateFile = path.join(dataDir, "wikipedia.json");
    this.defaultFolder = path.join(dataDir, "wikipedia");
    this.fetch = fetch;
    this.now = now;
    this.state = { folder: "", files: [], downloads: [] };
    this.zims = new Map(); // id → Promise<ZimFile>
    this.catalogs = new Map(); // lang → { at, entries }
    this.active = new Map(); // download id → AbortController
    this.live = new Map(); // download id → { speed, at, bytes } (not saved)
    this.changeTimer = null;
  }

  get folder() { return this.state.folder || this.defaultFolder; }

  async init() {
    try {
      const s = JSON.parse(await fs.promises.readFile(this.stateFile, "utf8"));
      this.state = {
        folder: typeof s.folder === "string" ? s.folder : "",
        files: Array.isArray(s.files) ? s.files.filter(f => f && typeof f.file === "string") : [],
        downloads: Array.isArray(s.downloads) ? s.downloads.filter(d => d && FILE_RE.test(d.file || "")) : []
      };
    } catch (_) { /* none yet */ }
    for (const d of this.state.downloads) {
      const was = d.state;
      d.state = was === "running" || was === "checking" ? "queued" : d.state === "failed" ? "failed" : "paused";
    }
    for (const d of this.state.downloads) if (d.state === "queued") this.run(d); // carry on after a restart
  }

  /** One write at a time (two renames of the same temporary file at once fail on Windows). */
  save() {
    const write = () => this.writeState();
    this.saving = (this.saving || Promise.resolve()).then(write, write);
    return this.saving;
  }

  async writeState() {
    const tmp = this.stateFile + ".tmp";
    await fs.promises.mkdir(this.dataDir, { recursive: true });
    await fs.promises.writeFile(tmp, JSON.stringify(this.state, (k, v) => (k === "done" ? undefined : v), 1));
    await fs.promises.rename(tmp, this.stateFile);
  }

  /** At quit: a running download is saved as running, so it carries on at the next start. */
  saveSync() {
    try { fs.writeFileSync(this.stateFile, JSON.stringify(this.state, (k, v) => (k === "done" ? undefined : v), 1)); } catch (_) { /* next time */ }
  }

  /** Tells Settings something changed (at most every 250 ms while a download runs). */
  changed(now = false) {
    if (now) { clearTimeout(this.changeTimer); this.changeTimer = null; this.emit("change"); return; }
    if (this.changeTimer) return;
    this.changeTimer = setTimeout(() => { this.changeTimer = null; this.emit("change"); }, 250);
  }

  /** What Settings shows. */
  list() {
    const plain = f => ({
      id: f.id, lang: f.lang, name: f.name, scope: f.scope, flavour: f.flavour, date: f.date, title: f.title,
      articles: f.articles, size: f.size, imported: !!f.imported, file: f.file, missing: !fs.existsSync(f.file)
    });
    return {
      folder: this.folder, isDefault: !this.state.folder,
      files: this.state.files.map(plain),
      downloads: this.state.downloads.map(d => ({
        id: d.id, lang: d.lang, name: d.name, scope: d.scope, flavour: d.flavour, date: d.date, title: d.title,
        articles: d.articles, size: d.size, got: d.got || 0, state: d.state, error: d.error || "",
        speed: (this.live.get(d.id) || {}).speed || 0
      }))
    };
  }

  /** The languages that have a file (for the background: is there anything to read offline?). */
  langs() {
    return [...new Set(this.state.files.filter(f => fs.existsSync(f.file)).map(f => f.lang))];
  }

  /** Kiwix's Wikipedia files in one language (kept for an hour). Throws "offline" without a connection. */
  async catalog(lang) {
    if (!Object.hasOwn(LANG3, lang)) throw codeError("bad_lang");
    const hit = this.catalogs.get(lang);
    if (hit && this.now() - hit.at < 3600e3) return hit.entries;
    let xml;
    try {
      const res = await this.fetch(`${CATALOG}?lang=${LANG3[lang]}&category=wikipedia&count=500`, { signal: AbortSignal.timeout(20000) });
      if (!res.ok) throw new Error("http " + res.status);
      xml = await res.text();
    } catch (_) { throw codeError("offline"); }
    const entries = parseCatalog(xml).filter(e => e.lang === lang);
    this.catalogs.set(lang, { at: this.now(), entries });
    return entries;
  }

  /** Starts downloading a catalog entry (by id: the catalog must have been read). */
  async download(id) {
    const entry = [...this.catalogs.values()].flatMap(c => c.entries).find(e => e.id === id);
    if (!entry) throw codeError("unknown");
    if (this.state.downloads.some(d => d.id === id)) return;
    if (this.state.files.some(f => f.id === id && fs.existsSync(f.file))) throw codeError("have_it");
    await this.checkFolder(this.folder);
    const d = {
      id, file: entry.file, lang: entry.lang, name: entry.name, scope: entry.scope, flavour: entry.flavour, date: entry.date,
      title: entry.title, articles: entry.articles, size: entry.size,
      part: path.join(this.folder, entry.file + ".part"), got: 0, state: "queued", error: "", mirrors: null, sha256: ""
    };
    this.state.downloads.push(d);
    await this.save();
    this.changed(true);
    this.run(d);
  }

  pause(id) {
    const d = this.state.downloads.find(x => x.id === id);
    if (!d) return;
    const ac = this.active.get(id);
    if (ac) ac.abort("pause");
    else if (d.state === "queued") d.state = "paused";
    this.changed(true);
  }

  resume(id) {
    const d = this.state.downloads.find(x => x.id === id);
    if (d && !this.active.has(id) && ["paused", "failed"].includes(d.state)) { d.error = ""; this.run(d); }
  }

  async cancel(id) {
    const d = this.state.downloads.find(x => x.id === id);
    if (!d) return;
    const ac = this.active.get(id);
    if (ac) { ac.abort("cancel"); await d.done; }
    this.state.downloads = this.state.downloads.filter(x => x !== d);
    await fs.promises.rm(d.part, { force: true });
    await this.save();
    this.changed(true);
  }

  /** Removes a file: deleted from disk when Lamha downloaded it; one the user added is only forgotten. */
  async remove(id) {
    const f = this.state.files.find(x => x.id === id);
    if (!f) return;
    await this.closeZim(id);
    if (!f.imported) await fs.promises.rm(f.file, { force: true });
    this.state.files = this.state.files.filter(x => x !== f);
    await this.save();
    this.changed(true);
  }

  /** A .zim file the user already has (Kiwix's): used where it is, never copied or deleted. */
  async addFile(file) {
    const z = await ZimFile.open(file).catch(() => { throw codeError("not_zim"); });
    try {
      const info = await describe(z, file);
      if (!info.name.startsWith("wikipedia_")) throw codeError("not_wikipedia");
      if (!info.lang) throw codeError("not_wikipedia");
      const main = await z.main();
      if (main) await z.content(main).catch(err => { throw codeError(err.code === "unsupported_compression" ? "too_old" : "not_zim"); });
      const id = "file:" + z.uuid; // the same file added twice (or a copy of it) is one entry
      await this.closeZim(id);
      this.state.files = this.state.files.filter(f => f.id !== id && path.resolve(f.file) !== path.resolve(file));
      this.state.files.push({ ...info, id, file, imported: true });
      await this.save();
      this.changed(true);
      return info;
    } finally { await z.close(); }
  }

  /** New downloads go to `dir` (files already there stay where they are). "" = back to the default folder. */
  async setFolder(dir) {
    if (dir) await this.checkFolder(dir);
    this.state.folder = dir && path.resolve(dir) !== path.resolve(this.defaultFolder) ? path.resolve(dir) : "";
    await this.save();
    this.changed(true);
  }

  async checkFolder(dir) {
    try {
      await fs.promises.mkdir(dir, { recursive: true });
      const probe = path.join(dir, ".lamha-write-test");
      await fs.promises.writeFile(probe, "");
      await fs.promises.rm(probe, { force: true });
    } catch (_) { throw codeError("folder"); }
  }

  /* ---- downloading ---- */

  run(d) {
    const ac = new AbortController();
    this.active.set(d.id, ac);
    d.state = "running";
    this.changed(true);
    d.done = this.fetchFile(d, ac.signal).then(async () => {
      this.state.downloads = this.state.downloads.filter(x => x !== d);
    }, err => {
      if (ac.signal.aborted) d.state = ac.signal.reason === "cancel" ? "cancelled" : "paused";
      else { d.state = "failed"; d.error = err.code || "failed"; }
    }).finally(async () => {
      this.active.delete(d.id);
      this.live.delete(d.id);
      delete d.done;
      await this.save().catch(() => {});
      this.changed(true);
    });
  }

  async fetchFile(d, signal) {
    const url = DOWNLOADS + d.file;
    if (!d.sha256) {
      const text = await this.getText(url + ".sha256", signal);
      const m = /^([0-9a-f]{64})\b/i.exec(text.trim());
      if (!m) throw codeError("offline");
      d.sha256 = m[1].toLowerCase();
    }
    if (!d.mirrors || !d.mirrors.length) {
      const meta = await this.getText(url + ".meta4", signal).catch(() => "");
      d.mirrors = parseMeta4(meta, d.file);
      for (const u of [url, "https://dumps.wikimedia.org/kiwix/zim/wikipedia/" + d.file]) if (!d.mirrors.includes(u)) d.mirrors.push(u);
    }
    await fs.promises.mkdir(path.dirname(d.part), { recursive: true });
    d.got = await fs.promises.stat(d.part).then(s => s.size, () => 0);
    if (d.got > d.size) { await fs.promises.rm(d.part, { force: true }); d.got = 0; }
    await this.checkSpace(d);

    if (d.got < d.size) {
      const order = await this.fastest(d.mirrors, signal);
      let tries = 0;
      for (let i = 0; d.got < d.size; i = (i + 1) % order.length) {
        if (signal.aborted) throw codeError("aborted");
        try {
          await this.stream(d, order[i], signal);
          tries = 0;
        } catch (err) {
          if (signal.aborted) throw err;
          if (err.code === "no_space") throw err;
          if (++tries >= order.length * 2) throw codeError("offline");
          await sleep(Math.min(20000, 1000 * tries), signal);
        }
      }
    }

    d.state = "checking";
    d.got = 0; // now: how much has been checked
    this.changed(true);
    const hash = crypto.createHash("sha256");
    for await (const chunk of fs.createReadStream(d.part, { signal })) {
      hash.update(chunk);
      d.got += chunk.length;
      this.changed();
    }
    if (hash.digest("hex") !== d.sha256) {
      await fs.promises.rm(d.part, { force: true });
      throw codeError("checksum");
    }
    const final = d.part.replace(/\.part$/, "");
    await fs.promises.rename(d.part, final);
    const z = await ZimFile.open(final);
    try {
      const info = await describe(z, final);
      this.state.files = this.state.files.filter(f => f.id !== d.id);
      this.state.files.push({ ...info, id: d.id, file: final, imported: false, lang: d.lang || info.lang, scope: d.scope || info.scope });
    } finally { await z.close(); }
  }

  /** The mirrors, fastest first: each fetches its first megabyte for up to 6 seconds, all at once. */
  async fastest(urls, signal) {
    const timed = await Promise.all(urls.slice(0, 6).map(async url => {
      const t0 = this.now();
      try {
        const res = await this.fetch(url, { headers: { Range: `bytes=0-${PROBE_BYTES - 1}` }, signal: AbortSignal.any([signal, AbortSignal.timeout(6000)]) });
        if (res.status !== 206) { await res.body?.cancel().catch(() => {}); return { url, speed: 0 }; }
        let n = 0;
        for await (const chunk of res.body) n += chunk.length;
        return { url, speed: n / Math.max(1, this.now() - t0) };
      } catch (_) { return { url, speed: -1 }; }
    }));
    const order = timed.filter(t => t.speed > 0).sort((a, b) => b.speed - a.speed).map(t => t.url);
    return order.length ? order : urls;
  }

  /** Appends from `url` to the .part file until it's complete, the mirror stalls, or the download is paused. */
  async stream(d, url, signal) {
    const stall = new AbortController();
    let timer = setTimeout(() => stall.abort(), STALL_MS);
    const res = await this.fetch(url, {
      headers: d.got ? { Range: `bytes=${d.got}-` } : {},
      signal: AbortSignal.any([signal, stall.signal])
    });
    if (!(res.status === 206 || (res.status === 200 && !d.got))) { clearTimeout(timer); await res.body?.cancel().catch(() => {}); throw codeError("mirror"); }
    const length = Number(res.headers.get("content-length")) || 0;
    if (length && d.got + length !== d.size) { clearTimeout(timer); await res.body?.cancel().catch(() => {}); throw codeError("mirror"); } // another file
    const out = fs.createWriteStream(d.part, { flags: "a" });
    let diskError = null; // a full disk: stop, and say so (an unhandled "error" would take the whole app down)
    out.on("error", err => { diskError = err; stall.abort(); });
    const live = { speed: 0, at: this.now(), bytes: 0 };
    this.live.set(d.id, live);
    let lastSave = this.now();
    try {
      for await (const chunk of res.body) {
        clearTimeout(timer);
        timer = setTimeout(() => stall.abort(), STALL_MS);
        if (!out.write(chunk)) await once(out, "drain");
        d.got += chunk.length;
        live.bytes += chunk.length;
        const t = this.now();
        if (t - live.at >= 1000) { live.speed = Math.round(live.bytes * 1000 / (t - live.at)); live.at = t; live.bytes = 0; }
        if (t - lastSave > 5000) { lastSave = t; this.save().catch(() => {}); }
        this.changed();
      }
    } catch (err) {
      if (!diskError) throw err;
    } finally {
      clearTimeout(timer);
      if (!diskError) await new Promise(resolve => out.end(resolve)); else out.destroy();
    }
    if (diskError) {
      d.got = await fs.promises.stat(d.part).then(s => s.size, () => 0); // what really reached the disk
      throw codeError(diskError.code === "ENOSPC" ? "no_space" : "folder");
    }
    if (d.got !== d.size) throw codeError("mirror");
  }

  async checkSpace(d) {
    try {
      const s = await fs.promises.statfs(path.dirname(d.part));
      if (s.bavail * s.bsize < d.size - d.got + SPACE_MARGIN) throw codeError("no_space");
    } catch (err) { if (err.code === "no_space") throw err; }
  }

  async getText(url, signal) {
    const res = await this.fetch(url, { signal: AbortSignal.any([signal, AbortSignal.timeout(20000)]) });
    if (!res.ok) throw codeError("offline");
    return res.text();
  }

  /* ---- reading ---- */

  zim(f) {
    if (!this.zims.has(f.id)) {
      const p = ZimFile.open(f.file);
      p.catch(() => this.zims.delete(f.id));
      this.zims.set(f.id, p);
    }
    return this.zims.get(f.id);
  }

  async closeZim(id) {
    const p = this.zims.get(id);
    this.zims.delete(id);
    if (p) await (await p.catch(() => null))?.close();
  }

  async closeAll() {
    for (const [id, ac] of this.active) { ac.abort("pause"); await this.state.downloads.find(d => d.id === id)?.done; }
    for (const id of [...this.zims.keys()]) await this.closeZim(id);
  }

  /**
   * The opening of the first article that matches one of `titles`, from this language's files:
   * { lang, title, extract, url, thumb, offline: { date, id, path } }, or null.
   */
  async summary(titles, lang) {
    const files = this.state.files.filter(f => f.lang === lang && fs.existsSync(f.file)).sort((a, b) => rank(a) - rank(b));
    const tries = [];
    for (const t of titles) {
      const clean = String(t || "").trim().replace(/\s+/g, " ");
      if (!clean || clean.length > 120) continue;
      const cap = clean[0].toLocaleUpperCase(lang) + clean.slice(1);
      for (const v of [clean, cap]) if (!tries.includes(v)) tries.push(v);
    }
    if (lang === "ar") { // Arabic titles often carry the article: شمس → الشمس (when the file has no redirect)
      for (const t of [...tries]) if (/^\p{Script=Arabic}/u.test(t) && !t.startsWith("ال") && !t.includes(" ")) tries.push("ال" + t);
    }
    for (const f of files) {
      let z;
      try { z = await this.zim(f); } catch (_) { continue; }
      for (const t of tries) {
        try {
          const found = await z.find(z.contentNs, t.replace(/ /g, "_"));
          const e = found && await z.resolve(found);
          if (!e || !/^text\/html/.test(e.mime)) continue;
          const html = (await z.content(e)).toString("utf8");
          if (isDisambiguation(html)) continue;
          const extract = leadText(html);
          if (!extract) continue;
          return {
            lang, title: e.title, extract, url: canonicalUrl(html),
            thumb: f.flavour === "maxi" ? await this.thumb(z, html, e.path) : "",
            offline: { date: f.date, id: f.id, path: e.path }
          };
        } catch (_) { /* a damaged entry: try the next */ }
      }
    }
    return null;
  }

  /* ---- the reader window (renderer/wiki/reader.js) ---- */

  /** A usable file by id (the first one when `id` is empty), or null. */
  fileById(id) {
    const usable = this.state.files.filter(f => fs.existsSync(f.file)).sort((a, b) => rank(a) - rank(b));
    return (id ? usable.find(f => f.id === id) : usable[0]) || null;
  }

  /**
   * An article for the reader, redirects followed: { fileId, path, title, html, lang, flavour, date, url } or null.
   * An empty path gives the file's main page. The HTML is the file's own: the reader cleans it before showing it.
   */
  async article(fileId, articlePath = "") {
    const files = fileId ? [this.fileById(fileId)].filter(Boolean) : this.state.files.filter(f => fs.existsSync(f.file)).sort((a, b) => rank(a) - rank(b));
    if (!files.length) throw codeError("no_file");
    for (const f of files) { // no file named: the first one that has it
      const z = await this.zim(f);
      const found = articlePath ? await z.find(z.contentNs, String(articlePath)) : await z.main();
      const e = found && await z.resolve(found);
      if (!e || !/^text\/html/.test(e.mime)) continue;
      const html = (await z.content(e)).toString("utf8");
      return { fileId: f.id, path: e.path, title: e.title, html, lang: f.lang, flavour: f.flavour, date: f.date, url: canonicalUrl(html) };
    }
    return null;
  }

  /** A picture from a file, for the reader's lamha-wiki: addresses: { mime, data } or null (never anything but images). */
  async asset(fileId, assetPath) {
    const f = this.fileById(fileId);
    if (!f) return null;
    try {
      const z = await this.zim(f);
      const e = await z.resolve(await z.find(z.contentNs, String(assetPath)));
      if (!e || !/^image\/(webp|png|jpeg|gif|svg\+xml)\b/.test(e.mime)) return null;
      return { mime: e.mime.split(";")[0], data: await z.content(e) };
    } catch (_) { return null; }
  }

  /**
   * Articles whose title starts with what was typed, from every file (the given language's first):
   * [{ fileId, lang, path, title }], at most `limit`, one per language and title.
   */
  async suggest(query, { lang = "", limit = 8 } = {}) {
    const q = String(query || "").trim().replace(/\s+/g, " ");
    if (!q || q.length > 120) return [];
    const files = this.state.files.filter(f => fs.existsSync(f.file))
      .sort((a, b) => (a.lang === lang ? 0 : 1) - (b.lang === lang ? 0 : 1) || rank(a) - rank(b));
    const out = [], seen = new Set();
    for (const f of files) {
      let z;
      try { z = await this.zim(f); } catch (_) { continue; }
      const cap = q[0].toLocaleUpperCase(f.lang) + q.slice(1);
      for (const v of [...new Set([q, cap])]) {
        const found = await z.titlesStartingWith(v, limit * 4).catch(() => []); // more than needed: the shortest win below
        for (const e of found) {
          const key = f.lang + "|" + e.title;
          if (seen.has(key)) continue;
          seen.add(key);
          out.push({ fileId: f.id, lang: f.lang, path: e.path, title: e.title });
        }
      }
      if (out.length >= limit * 4) break;
    }
    // an exact title first, then shorter titles (the word itself before "Word (film)")
    const norm = s => s.toLocaleLowerCase();
    return out.sort((a, b) => (norm(b.title) === norm(q)) - (norm(a.title) === norm(q)) || a.title.length - b.title.length).slice(0, limit);
  }

  /** A random article from a file: { fileId, path } or null. */
  async random(fileId) {
    const f = this.fileById(fileId);
    if (!f) return null;
    const z = await this.zim(f);
    const list = await z.titleList();
    for (let i = 0; i < 30 && list.length; i++) {
      try {
        const e = await z.entry(list[Math.floor(Math.random() * list.length)]);
        if (e.redirect === undefined && /^text\/html/.test(e.mime)) return { fileId: f.id, path: e.path };
      } catch (_) { /* try another */ }
    }
    return null;
  }

  /** The lead's picture as a data: URL (small pictures only: the card shows 64 px). */
  async thumb(z, html, articlePath) {
    const p = leadImage(html, articlePath);
    if (!p) return "";
    try {
      const e = await z.resolve(await z.find(z.contentNs, p));
      if (!e || !/^image\/(webp|png|jpeg|gif)$/.test(e.mime)) return "";
      const buf = await z.content(e);
      return buf.length <= 400 * 1024 ? `data:${e.mime};base64,${buf.toString("base64")}` : "";
    } catch (_) { return ""; }
  }
}

/** What a file says about itself. */
async function describe(z, file) {
  const name = await z.meta("Name");
  const language = (await z.meta("Language")).split(",")[0];
  const lang = (/^wikipedia_([a-z]{2,3})_/.exec(name) || [])[1] || LANG2[language] || "";
  return {
    name, lang, scope: name.replace(/^wikipedia_[a-z]{2,3}_/, ""),
    flavour: await z.meta("Flavour"), date: (await z.meta("Date")).slice(0, 10), title: await z.meta("Title"),
    articles: (await z.titleList()).length, size: (await fs.promises.stat(file)).size
  };
}

function codeError(code) { const e = new Error(code); e.code = code; return e; }

function sleep(ms, signal) {
  return new Promise((resolve, reject) => {
    const t = setTimeout(resolve, ms);
    signal.addEventListener("abort", () => { clearTimeout(t); reject(codeError("aborted")); }, { once: true });
  });
}

module.exports = { WikiLibrary, leadText, leadImage, canonicalUrl, isDisambiguation, parseCatalog, parseMeta4, LANG3 };
