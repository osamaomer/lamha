/* Lamha — optional offline language packs: French–French, German–German, Spanish–Spanish and Turkish–Turkish
 * dictionaries (tools/build_packs.py, from Wiktionary), downloaded from Settings instead of being part of everyone's
 * copy of Lamha. With one, words in that language are explained in it with no internet and no AI.
 * Kept on this device only: in IndexedDB in the browser, in files in the desktop app (which defines `LamhaPackStore`
 * before this file loads, see desktop/pack-store.js). A background script: the manifest, desktop main.js, tests. */
// eslint-disable-next-line no-unused-vars
var LamhaPacks = (() => {
  "use strict";

  // the GitHub release that holds the packs: dist-packs/<lang>.json.gz from tools/build_packs.py, uploaded by hand
  const RELEASE = "https://github.com/osamaomer/lamha/releases/download/packs-v1/";
  /** What can be downloaded: its size and headword count, shown in Settings before downloading. */
  const CATALOG = {
    fr: { bytes: 5235475, words: 40000 },
    de: { bytes: 5152740, words: 40000 },
    es: { bytes: 3255297, words: 37539 },
    tr: { bytes: 1777104, words: 33902 }
  };
  const FORMAT = 1; // tools/build_packs.py FORMAT: a pack in another format isn't used

  /** Same as shard_key() in tools/build_packs.py: the first two letters, lowercase, without accents. */
  const shardOf = w => {
    const s = w.toLowerCase().replace(/ı/g, "i").replace(/ß/g, "ss").normalize("NFD").replace(/[^a-z]/g, "");
    return s ? (s + "__").slice(0, 2) : "__";
  };

  /** Key → value storage: `${lang}/meta` and `${lang}/${shard}`. */
  function idbStore() {
    let opening = null;
    const db = () => opening || (opening = new Promise((resolve, reject) => {
      const req = indexedDB.open("lamha-packs", 1);
      req.onupgradeneeded = () => req.result.createObjectStore("kv");
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => { opening = null; reject(req.error); };
    }));
    const run = async (mode, fn) => {
      const d = await db();
      return new Promise((resolve, reject) => {
        const t = d.transaction("kv", mode);
        const req = fn(t.objectStore("kv"));
        t.oncomplete = () => resolve(req ? req.result : undefined);
        t.onerror = t.onabort = () => reject(t.error);
      });
    };
    return {
      get: key => run("readonly", s => s.get(key)),
      setMany: entries => run("readwrite", s => { for (const [k, v] of Object.entries(entries)) s.put(v, k); }),
      removePrefix: prefix => run("readwrite", s => { s.delete(IDBKeyRange.bound(prefix, prefix + "￿")); })
    };
  }
  const store = typeof LamhaPackStore !== "undefined" ? LamhaPackStore : idbStore(); // eslint-disable-line no-undef

  let installed = null; // lang → meta, once read
  const progress = Object.create(null); // lang → 0…1 while it downloads (Settings asks for it)
  const shards = new Map(); // "lang/key" → Promise<shard>, the most recent 80

  async function metas() {
    if (installed) return installed;
    const found = Object.create(null);
    for (const lang of Object.keys(CATALOG)) {
      try {
        const m = await store.get(lang + "/meta");
        if (m && m.format === FORMAT) found[lang] = m;
      } catch (_) { /* storage unavailable: no packs */ }
    }
    return (installed = found);
  }
  const ready = metas();

  function shard(lang, word) {
    const key = lang + "/" + shardOf(word);
    if (!shards.has(key)) {
      if (shards.size >= 80) shards.delete(shards.keys().next().value);
      shards.set(key, store.get(key).then(s => s || {}, () => ({})));
    }
    return shards.get(key);
  }

  /** Is the `lang` pack on this device? (After `ready`.) */
  const has = lang => !!(installed && installed[lang]);
  /** The installed packs, for cache keys ("de,fr"). */
  const key = () => Object.keys(installed || {}).sort().join(",");
  const langs = () => Object.keys(installed || {});

  /** Settings' list: every pack with its size, whether it's here, and how far a download has got. */
  async function list() {
    const m = await metas();
    return Object.keys(CATALOG).map(lang => ({
      lang, bytes: m[lang] ? m[lang].bytes || CATALOG[lang].bytes : CATALOG[lang].bytes, words: m[lang] ? m[lang].words : CATALOG[lang].words,
      installed: !!m[lang], version: m[lang] ? m[lang].version : "", progress: Object.hasOwn(progress, lang) ? progress[lang] : null
    }));
  }

  /** Downloads the `lang` pack and keeps it. It counts as installed only once every part is stored. */
  async function install(lang) {
    if (!Object.hasOwn(CATALOG, lang)) throw new Error("pack_unknown");
    if (Object.hasOwn(progress, lang)) throw new Error("pack_busy");
    progress[lang] = 0;
    try {
      let res;
      try { res = await fetch(RELEASE + lang + ".json.gz"); } catch (_) { throw new Error("pack_download"); }
      if (!res.ok || !res.body) throw new Error("pack_download");
      const total = Number(res.headers.get("content-length")) || CATALOG[lang].bytes;
      let got = 0;
      const counted = res.body.pipeThrough(new TransformStream({
        transform(chunk, out) { got += chunk.byteLength; if (total) progress[lang] = Math.min(0.95, 0.95 * got / total); out.enqueue(chunk); }
      }));
      let data;
      try { data = JSON.parse(await new Response(counted.pipeThrough(new DecompressionStream("gzip"))).text()); }
      catch (_) { throw new Error("pack_bad"); }
      const meta = data && data.meta;
      if (!meta || meta.lang !== lang || meta.format !== FORMAT || !data.shards || typeof data.shards !== "object") throw new Error("pack_bad");
      await remove(lang); // an older version goes first
      const parts = {};
      for (const [k, v] of Object.entries(data.shards)) if (/^[a-z_]{2}$/.test(k) && v && typeof v === "object") parts[`${lang}/${k}`] = v;
      await store.setMany(parts);
      const kept = { ...meta, bytes: got, installedAt: Date.now() };
      await store.setMany({ [lang + "/meta"]: kept }); // last: a download cut short never looks installed
      (await metas())[lang] = kept;
      return kept;
    } finally {
      delete progress[lang];
    }
  }

  async function remove(lang) {
    if (!Object.hasOwn(CATALOG, lang)) throw new Error("pack_unknown");
    await store.removePrefix(lang + "/");
    delete (await metas())[lang];
    for (const k of [...shards.keys()]) if (k.startsWith(lang + "/")) shards.delete(k);
  }

  /**
   * `text` in the `lang` pack: { word (the headword), form (what was looked up, when it's an inflected form: "evler"
   * → "ev"), entry: { s: [[pos, gloss, example, synonyms], …], p: ipa } }, or null. Words are untrusted keys: hasOwn.
   */
  async function find(lang, text) {
    if (!(await metas())[lang]) return null;
    const t = String(text || "").trim();
    if (!t || t.length > 40) return null;
    const lower = t.toLocaleLowerCase(lang); // Turkish İ and I
    const cands = [...new Set([t, lower, lower.charAt(0).toLocaleUpperCase(lang) + lower.slice(1)])]; // German nouns: "Haus"
    for (const w of cands) {
      const s = await shard(lang, w);
      if (s.w && Object.hasOwn(s.w, w)) return { word: w, form: "", entry: s.w[w] };
    }
    for (const w of cands) {
      const s = await shard(lang, w);
      const lemma = s.f && Object.hasOwn(s.f, w) ? s.f[w] : "";
      if (!lemma) continue;
      const l = await shard(lang, lemma);
      if (l.w && Object.hasOwn(l.w, lemma)) return { word: lemma, form: w, entry: l.w[lemma] };
    }
    // Spanish and French Wiktionary don't list most plurals: "casas" → "casa", "maisons" → "maison", when that's a headword
    for (const end of PLURALS[lang] || []) {
      if (!lower.endsWith(end) || lower.length < end.length + 3) continue;
      const w = lower.slice(0, -end.length);
      const s = await shard(lang, w);
      if (s.w && Object.hasOwn(s.w, w)) return { word: w, form: t, entry: s.w[w] };
    }
    return null;
  }
  const PLURALS = { es: ["es", "s"], fr: ["s", "x"] };

  return { CATALOG, ready, has, key, langs, list, install, remove, find, shardOf };
})();
