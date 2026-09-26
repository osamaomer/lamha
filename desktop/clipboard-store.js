/* Lamha desktop — clipboard history, storage side: the clips on disk (encrypted with Windows DPAPI through
 * Electron's safeStorage when available), duplicates merged, history kept to a size, and Arabic-aware search.
 * No Electron import: safeStorage and the normalizer are passed in, so tools/test-clipboard.mjs runs in plain Node.
 * Privacy: clip contents are never logged. */
"use strict";
const fs = require("node:fs");
const path = require("node:path");
const { EventEmitter } = require("node:events");

const SAVE_DELAY = 1000;
const MAX_PINNED = 300;
const LABEL_MAX = 60;
const PREVIEW_CHARS = 300;
const INDEX_CHARS = 5000;
const UNDO_MS = 30000; // a deleted clip can be restored for this long (the UI offers 5 s); memory only

/** Time-sortable unique ids: c_ + milliseconds (base 36, fixed width) + a counter within the same millisecond + noise. */
let lastMs = 0, sameMs = 0;
function newId() {
  const now = Date.now();
  sameMs = now === lastMs ? sameMs + 1 : 0;
  lastMs = now;
  const noise = Math.floor(Math.random() * 36 ** 3).toString(36).padStart(3, "0");
  return "c_" + now.toString(36).padStart(9, "0") + sameMs.toString(36).padStart(3, "0") + noise;
}

const activity = c => Math.max(c.lastCopiedAt, c.lastUsedAt);
const byRecency = (a, b) => activity(b) - activity(a) || (a.id < b.id ? 1 : -1);
const isWordStart = (s, i) => i === 0 || !/[\p{L}\p{N}]/u.test(s[i - 1]);

class ClipError extends Error {
  constructor(code, message) { super(message); this.code = code; }
}

class ClipboardStore extends EventEmitter {
  /**
   * @param {object} o
   * @param {string} o.file              …\clipboard.json
   * @param {(s: string) => string} o.normalize   LamhaArabic.normalizeForSearch
   * @param {object} [o.safeStorage]     Electron's safeStorage (or a test double); absent → plain JSON
   * @param {number} [o.maxItems]        cap for unpinned clips
   * @param {(s: string) => string} [o.detectLang]  LamhaArabic.detectLang: "ar" | "en" | "mixed" | "other"
   */
  constructor({ file, normalize, safeStorage = null, maxItems = 500, detectLang = () => "other" }) {
    super();
    this.file = file;
    this.normalize = normalize;
    this.detectLang = detectLang;
    this.safeStorage = safeStorage;
    this.maxItems = maxItems;
    this.clips = new Map(); // id → clip
    this.byText = new Map(); // trimmed text → id (dedup)
    this.index = new Map(); // id → { all, label } normalized, memory only
    this.trash = new Map(); // id → { clip, at }: recently deleted, for undo; never written to disk
    this.encrypted = false; // how the file on disk is stored now
    this.timer = null;
    this.dirty = false;
    this.load();
  }

  /* ---------------- disk ---------------- */

  get canEncrypt() {
    try { return !!(this.safeStorage && this.safeStorage.isEncryptionAvailable()); } catch (_) { return false; }
  }

  load() {
    let raw;
    try { raw = fs.readFileSync(this.file, "utf8"); } catch (_) { return; } // first run
    try {
      const head = JSON.parse(raw);
      let clips;
      if (head.encrypted) {
        if (!this.canEncrypt) throw new Error("cannot decrypt on this machine");
        clips = JSON.parse(this.safeStorage.decryptString(Buffer.from(head.data, "base64")));
      } else {
        clips = head.clips;
      }
      if (!Array.isArray(clips)) throw new Error("no clip list");
      for (const c of clips) {
        if (!c || typeof c.id !== "string" || typeof c.text !== "string") continue;
        const clip = upgrade(c);
        const lang = this.detectLang(clip.text); // backfill (clips saved before language tagging) and keep current
        if (lang !== clip.lang) { clip.lang = lang; this.dirty = true; }
        this.put(clip);
      }
      this.encrypted = !!head.encrypted;
    } catch (err) {
      // keep the unreadable file for the user / support, start empty — never crash
      const dir = path.dirname(this.file);
      const kept = path.join(dir, `clipboard.corrupt-${Date.now()}.json`);
      try { fs.renameSync(this.file, kept); } catch (_) { /* nothing to keep */ }
      // only the error's type: a JSON.parse message can quote the (decrypted) content
      console.warn(`[clipboard] history unreadable (${err && err.name}) — starting empty; old file kept as ${path.basename(kept)}`);
      this.clips.clear();
      this.byText.clear();
      this.index.clear();
    }
  }

  scheduleSave() {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.flush(), SAVE_DELAY);
    if (this.timer.unref) this.timer.unref();
  }

  /** Write pending changes now, atomically (temp file + rename). Called on quit too. */
  flush() {
    clearTimeout(this.timer);
    this.timer = null;
    if (!this.dirty) return;
    const clips = [...this.clips.values()];
    const encrypted = this.canEncrypt;
    const body = encrypted
      ? { v: 1, encrypted: true, data: this.safeStorage.encryptString(JSON.stringify(clips)).toString("base64") }
      : { v: 1, encrypted: false, clips };
    fs.mkdirSync(path.dirname(this.file), { recursive: true });
    const tmp = this.file + ".tmp";
    fs.writeFileSync(tmp, JSON.stringify(body));
    fs.renameSync(tmp, this.file);
    this.encrypted = encrypted;
    this.dirty = false;
  }

  changed() {
    this.dirty = true;
    this.scheduleSave();
    this.emit("changed");
  }

  /* ---------------- in memory ---------------- */

  put(c) {
    this.clips.set(c.id, c);
    this.byText.set(c.text.trim(), c.id);
    this.reindex(c);
  }

  drop(id) {
    const c = this.clips.get(id);
    if (!c) return false;
    this.clips.delete(id);
    if (this.byText.get(c.text.trim()) === id) this.byText.delete(c.text.trim());
    this.index.delete(id);
    return true;
  }

  reindex(c) {
    const label = this.normalize(c.label);
    // fields joined by newlines, which normalized query tokens never contain: a match can't straddle two fields
    this.index.set(c.id, { label, all: label + "\n" + this.normalize(c.text.slice(0, INDEX_CHARS)) + "\n" + this.normalize(c.sourceApp) });
  }

  /** Oldest unpinned clips go once there are more than maxItems. */
  enforceCap() {
    const unpinned = [...this.clips.values()].filter(c => !c.pinned);
    if (unpinned.length <= this.maxItems) return false;
    unpinned.sort(byRecency).slice(this.maxItems).forEach(c => this.drop(c.id));
    return true;
  }

  setMaxItems(n) {
    n = Math.max(1, Math.floor(Number(n) || 500));
    if (n === this.maxItems) return;
    this.maxItems = n;
    if (this.enforceCap()) this.changed();
  }

  /* ---------------- API ---------------- */

  /** A capture from the monitor ({ text, html?, sourceApp, capturedAt }). Returns the clip's id. */
  ingest({ text, html, sourceApp, capturedAt = Date.now() }) {
    const key = text.trim();
    let c = this.clips.get(this.byText.get(key));
    if (c) {
      c.lastCopiedAt = capturedAt;
      c.copyCount++;
      c.sourceApp = sourceApp || c.sourceApp;
      if (html) c.html = html; else delete c.html;
      this.reindex(c);
    } else {
      c = upgrade({ id: newId(), text, html, createdAt: capturedAt, lastCopiedAt: capturedAt, sourceApp, lang: this.detectLang(text) });
      this.put(c);
      this.enforceCap();
    }
    this.changed();
    return c.id;
  }

  /** { items: previews (first 300 chars), total } — newest first, or best match first for a query. */
  list({ query = "", filter = "all", limit = 100, offset = 0, tl = "ar" } = {}) {
    let pool = [...this.clips.values()];
    if (filter === "pinned") pool = pool.filter(c => c.pinned);
    const q = this.normalize(query);
    let ordered;
    if (!q) {
      ordered = pool.sort(byRecency);
    } else {
      const tokens = q.split(" ");
      const scored = [];
      for (const c of pool) {
        const s = this.score(this.index.get(c.id), q, tokens);
        if (s >= 0) scored.push({ c, s });
      }
      scored.sort((a, b) => b.s - a.s || byRecency(a.c, b.c));
      ordered = scored.map(x => x.c);
    }
    return { items: ordered.slice(offset, offset + limit).map(c => preview(c, tl)), total: ordered.length };
  }

  /** -1 when some token is missing; otherwise +100 whole query, +10 per token starting a word, +30 found in the label. */
  score(idx, q, tokens) {
    for (const t of tokens) if (!idx.all.includes(t)) return -1;
    let s = idx.all.includes(q) ? 100 : 0;
    for (const t of tokens) {
      for (let i = idx.all.indexOf(t); i !== -1; i = idx.all.indexOf(t, i + 1)) {
        if (isWordStart(idx.all, i)) { s += 10; break; }
      }
    }
    if (idx.label && tokens.every(t => idx.label.includes(t))) s += 30;
    return s;
  }

  /**
   * Stores an action result on the clip (saved encrypted with it): the translation per target language (`sub`),
   * the summary per summary language (`sub`: "ar" | "en"), or english / proofread.
   */
  setCache(id, kind, value, sub) {
    const c = this.need(id);
    if (kind === "translation") c.cache.translation[sub] = value;
    else if (kind === "summary") c.cache.summary = { ...(c.cache.summary || {}), [sub === "en" ? "en" : "ar"]: value };
    else if (["english", "proofread"].includes(kind)) c.cache[kind] = value;
    else throw new ClipError("bad_cache", "unknown cache kind");
    this.changed();
  }

  get(id) {
    const c = this.clips.get(id);
    return c ? structuredClone(c) : null;
  }

  setPinned(id, pinned) {
    const c = this.need(id);
    pinned = !!pinned;
    if (c.pinned === pinned) return true;
    if (pinned && [...this.clips.values()].filter(x => x.pinned).length >= MAX_PINNED) {
      throw new ClipError("pin_limit", `at most ${MAX_PINNED} pinned clips`);
    }
    c.pinned = pinned;
    this.changed();
    return true;
  }

  setLabel(id, label) {
    const c = this.need(id);
    c.label = String(label || "").replace(/\s+/g, " ").trim().slice(0, LABEL_MAX);
    this.reindex(c);
    this.changed();
    return c.label;
  }

  remove(id) {
    const c = this.clips.get(id);
    if (!c) return false;
    this.drop(id);
    const now = Date.now();
    for (const [k, t] of this.trash) if (now - t.at > UNDO_MS) this.trash.delete(k);
    this.trash.set(id, { clip: c, at: now });
    this.changed();
    return true;
  }

  /** Undo of remove(). If the same text was copied again meanwhile, that clip stays and its id is returned. */
  restore(id) {
    const t = this.trash.get(id);
    if (!t || Date.now() - t.at > UNDO_MS) throw new ClipError("not_found", "nothing to restore");
    this.trash.delete(id);
    const existing = this.byText.get(t.clip.text.trim());
    if (existing) return existing;
    this.put(t.clip);
    this.changed();
    return id;
  }

  clear({ keepPinned = false } = {}) {
    for (const c of [...this.clips.values()]) if (!(keepPinned && c.pinned)) this.drop(c.id);
    this.trash.clear();
    this.changed();
    return this.clips.size;
  }

  /** Expiry: unpinned clips whose last activity is older than `days` go; 0 = keep forever. Returns how many. */
  expire(days, now = Date.now()) {
    if (!(days > 0)) return 0;
    const cutoff = now - days * 86400e3;
    let n = 0;
    for (const c of [...this.clips.values()]) if (!c.pinned && activity(c) < cutoff && this.drop(c.id)) n++;
    if (n) this.changed();
    return n;
  }

  /** Programs seen in the history, most clips first: [{ app, count }]. */
  apps() {
    const counts = new Map();
    for (const c of this.clips.values()) counts.set(c.sourceApp, (counts.get(c.sourceApp) || 0) + 1);
    return [...counts].map(([app, count]) => ({ app, count })).sort((a, b) => b.count - a.count || (a.app < b.app ? -1 : 1));
  }

  /** Deletes every clip copied from `app` (pinned too: the user asked to forget that program). Returns how many. */
  removeApp(app) {
    let n = 0;
    for (const c of [...this.clips.values()]) if (c.sourceApp === app && this.drop(c.id)) n++;
    if (n) this.changed();
    return n;
  }

  markUsed(id) {
    const c = this.need(id);
    c.lastUsedAt = Date.now();
    c.useCount++;
    this.changed();
    return true;
  }

  need(id) {
    const c = this.clips.get(id);
    if (!c) throw new ClipError("not_found", "no such clip");
    return c;
  }
}

/** Fills in fields missing from older or partial records (the data model in the briefs' overview). */
function upgrade(c) {
  const out = {
    id: c.id,
    text: c.text,
    createdAt: c.createdAt || 0,
    lastCopiedAt: c.lastCopiedAt || c.createdAt || 0,
    lastUsedAt: c.lastUsedAt || 0,
    copyCount: c.copyCount || 1,
    useCount: c.useCount || 0,
    pinned: !!c.pinned,
    label: typeof c.label === "string" ? c.label : "",
    sourceApp: c.sourceApp || "unknown",
    lang: c.lang || "other", // filled in by Phase 4
    cache: upgradeCache(c.cache || {})
  };
  if (c.html) out.html = c.html;
  return out;
}

function upgradeCache(cache) {
  const out = { english: null, proofread: null, summary: null, ...cache, translation: { ...(cache.translation || {}) } };
  if (out.summary && typeof out.summary.text === "string") out.summary = { ar: out.summary }; // before summaries had a language
  return out;
}

/** A list row: 300 chars of text, and for English clips the cached translation into `tl` (muted second line). */
function preview(c, tl) {
  return {
    id: c.id,
    text: c.text.slice(0, PREVIEW_CHARS),
    length: c.text.length,
    hasHtml: !!c.html,
    label: c.label,
    pinned: c.pinned,
    sourceApp: c.sourceApp,
    lang: c.lang,
    createdAt: c.createdAt,
    lastCopiedAt: c.lastCopiedAt,
    lastUsedAt: c.lastUsedAt,
    copyCount: c.copyCount,
    useCount: c.useCount,
    translation: c.lang === "en" && c.cache.translation[tl] ? String(c.cache.translation[tl]).slice(0, 200) : ""
  };
}

module.exports = { ClipboardStore, ClipError, MAX_PINNED, LABEL_MAX };
