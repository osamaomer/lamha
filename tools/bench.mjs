// Performance measurements (not a test: numbers, no pass/fail). Runs the real background.js + local-dict.js in a VM
// with a simulated network, and the desktop's storage, clipboard history and .zim reader in plain Node.
//   node tools/bench.mjs              everything
//   LAMHA_BENCH_ZIM=<file.zim>        also a real downloaded Wikipedia (else the first one in %APPDATA%\Lamha\wikipedia)
import { readFileSync, readdirSync, mkdtempSync, rmSync, statSync } from "node:fs";
import { createRequire } from "node:module";
import { performance } from "node:perf_hooks";
import os from "node:os";
import path from "node:path";
import vm from "node:vm";

const root = new URL("..", import.meta.url);
const src = p => readFileSync(new URL(p, root), "utf8");
const require = createRequire(import.meta.url);
const ms = n => (n >= 100 ? Math.round(n) : n.toFixed(1)) + " ms";
const kb = n => (n / 1024).toFixed(0) + " KB";
const row = (label, value, note = "") => console.log("  " + label.padEnd(58) + String(value).padStart(10) + (note ? "   " + note : ""));
const section = t => console.log("\n" + t);

/* ---------------- the background in a VM, with a simulated network ---------------- */

const json = (status, body) => ({ ok: status >= 200 && status < 300, status, json: async () => body });

/** Google as it answers a word (definitions, so glosses get translated), a sentence, or a batch (context, glosses). */
function googleAnswer(url) {
  if (url.includes("/translate_a/single")) {
    return json(200, { src: "en", sentences: [{ trans: "مرن", orig: "x" }],
      definitions: [{ pos: "adjective", entry: [{ gloss: "able to recover quickly", definition_id: "a" }, { gloss: "springing back into shape", definition_id: "b" }] }] });
  }
  if (url.includes("/translate_a/t")) return json(200, [["الأطفال <a i=0>مرنون</a> جدًا.", "en"], ["قادر على التعافي", "en"]]);
  if (url.includes("wikipedia.org/w/api.php")) return json(200, { query: { pages: { 1: { title: "X", langlinks: [{ "*": "س" }] } } } });
  if (url.includes("wikipedia.org/api/rest_v1")) return json(200, { type: "standard", title: "س", extract: "نص.", content_urls: { desktop: { page: "https://ar.wikipedia.org/wiki/x" } } });
  return json(404, {});
}

/**
 * net.mode: "rtt" (every request answers after `rtt` ms), "dead" (requests never answer: they wait for the caller's
 * timeout, as on a Wi-Fi with no internet behind it), "down" (fail at once: no network at all).
 * `scale` speeds the VM's clock up so the dead network's long timeouts can be measured quickly.
 */
function makeBackground({ net, sync = {}, local = {}, scale = 1 }) {
  const area = data => ({
    data,
    async get(keys) {
      if (keys == null) return structuredClone(data);
      if (typeof keys === "string") keys = [keys];
      if (Array.isArray(keys)) return Object.fromEntries(keys.filter(k => k in data).map(k => [k, structuredClone(data[k])]));
      return Object.fromEntries(Object.entries(keys).map(([k, d]) => [k, k in data ? structuredClone(data[k]) : d]));
    },
    async set(o) { Object.assign(data, structuredClone(o)); },
    async remove(k) { [].concat(k).forEach(x => delete data[x]); }
  });
  const T = f => (fn, t = 0, ...a) => f(fn, t / scale, ...a);
  const fetch = async (url, init = {}) => {
    url = String(url);
    if (url.startsWith("moz-extension://x/dict/")) { try { return json(200, JSON.parse(src(url.slice(18)))); } catch (_) { return json(404, {}); } }
    net.requests.push(url);
    if (net.mode === "down") throw new TypeError("NetworkError when attempting to fetch resource.");
    return new Promise((resolve, reject) => {
      const t = net.mode === "dead" ? null : setTimeout(() => resolve(googleAnswer(url)), net.rtt / scale);
      init.signal && init.signal.addEventListener("abort", () => { clearTimeout(t); reject(Object.assign(new Error("aborted"), { name: "AbortError" })); });
    });
  };
  let onMessage;
  const noop = { addListener() {} };
  const ctx = vm.createContext({
    browser: {
      storage: { local: area({ cardsImported: true, ...local }), sync: area({ uiLang: "ar", ...sync }), onChanged: noop },
      runtime: { onMessage: { addListener: f => { onMessage = f; } }, onInstalled: noop, onStartup: noop, getURL: p => "moz-extension://x/" + p },
      menus: { removeAll: async () => {}, create() {}, onClicked: noop }, commands: { onCommand: noop },
      tabs: { query: async () => [], sendMessage: async () => {}, create: async () => {} }, permissions: { contains: async () => true }
    },
    fetch, console, setTimeout: T(setTimeout), clearTimeout, AbortController, URLSearchParams, structuredClone, Audio: class {},
    Response, TransformStream, DecompressionStream,
    LamhaPackStore: { get: async () => undefined, setMany: async () => {}, removePrefix: async () => {} },
    ...(net.offline ? { navigator: { onLine: false } } : {})
  });
  for (const f of ["shared/i18n.js", "local-dict.js", "packs.js", "shared/lamha-ai.js", "background.js"]) vm.runInContext(src(f), ctx, { filename: f });
  return { send: msg => onMessage(msg, {}), ctx };
}

const SENTENCE = { before: "Kids are very ", after: " after all." };

/** One lookup on a fresh background (nothing cached): its time and how many requests went out. */
async function timeLookup({ text, context = null, sync = {}, mode = "rtt", rtt = 150, scale = 1 }) {
  const net = { mode, rtt, requests: [] };
  const bg = makeBackground({ net, sync: { dictSource: "local", ...sync }, scale });
  await bg.send({ type: "getSettings" }); // the background is up
  const t0 = performance.now();
  const r = await bg.send({ type: "lookup", text, context });
  return { time: (performance.now() - t0) * scale, requests: net.requests.length, ok: r.ok, source: r.ok ? r.data.source : r.error };
}

section("Lookups, simulated network (150 ms per request, nothing cached)");
for (const [label, opts] of [
  ["word in the offline dictionary, no sentence (Local first)", { text: "resilient" }],
  ["…with its sentence (Local first: the default)", { text: "resilient", context: SENTENCE }],
  ["…with its sentence (Online first)", { text: "resilient", context: SENTENCE, sync: { dictSource: "online" } }],
  ["word not in the offline dictionary, with its sentence", { text: "zeitgeisty", context: SENTENCE }],
  ["English–English view, with its sentence", { text: "resilient", context: SENTENCE, sync: { enDict: true } }],
  ["a sentence", { text: "Kids are very resilient after all." }]
]) {
  const r = await timeLookup(opts);
  row(label, ms(r.time), `${r.requests} request(s), ${r.source}`);
}

section("Lookups when the network is there but answers nothing (clock ×50: times are real-world)");
for (const [label, opts] of [
  ["word in the offline dictionary, with its sentence (Local first)", { text: "resilient", context: SENTENCE }],
  ["word in the offline dictionary, Online first, no sentence", { text: "resilient", sync: { dictSource: "online" } }],
  ["a sentence (nothing offline can answer it)", { text: "Kids are very resilient after all." }]
]) {
  const r = await timeLookup({ ...opts, mode: "dead", scale: 50 });
  row(label, ms(r.time), `${r.requests} request(s), ${r.source}`);
}

{ // the next lookups on the same background, once the first request has given up
  const net = { mode: "dead", rtt: 150, requests: [] };
  const bg = makeBackground({ net, sync: { dictSource: "local" }, scale: 50 });
  await bg.send({ type: "lookup", text: "resilient", context: SENTENCE });
  await new Promise(r => setTimeout(r, 13000 / 50));
  const before = net.requests.length, t0 = performance.now();
  const r = await bg.send({ type: "lookup", text: "tenacious", context: SENTENCE });
  row("…the next word, a few seconds later", ms((performance.now() - t0) * 50), `${net.requests.length - before} request(s), ${r.ok ? r.data.source : r.error}`);
}

section("Lookups with no network at all (fails at once)");
{
  const r = await timeLookup({ text: "resilient", context: SENTENCE, sync: { dictSource: "online" }, mode: "down" });
  row("word, Online first, with its sentence", ms(r.time), `${r.requests} request(s), ${r.source}`);
}

section("Offline dictionary (local-dict.js)");
{
  const bg = makeBackground({ net: { mode: "down", requests: [] }, sync: { dictSource: "offline" } });
  let t0 = performance.now();
  await bg.send({ type: "lookup", text: "resilient" });
  row("first English lookup (its forms part + word shard, read and parsed)", ms(performance.now() - t0));
  t0 = performance.now();
  await bg.send({ type: "lookup", text: "tenacity" });
  row("a word in another shard", ms(performance.now() - t0));
  const words = ["house", "running", "studied", "mentioned", "banks", "happier", "children", "went", "better", "quickly"];
  t0 = performance.now();
  for (const w of words) await bg.send({ type: "lookup", text: w + " " }); // a space: not the cache
  row("10 more words, shards already read (each)", ms((performance.now() - t0) / words.length));
  const parts = readdirSync(new URL("dict/forms/", root)).map(f => statSync(new URL("dict/forms/" + f, root)).size);
  row("inflected forms: parts / largest", `${parts.length} / ${kb(Math.max(...parts))}`, "one part read per first two letters");
}

section("Review deck (the background's read-modify-write of storage.local.cards)");
for (const n of [500, 3000]) {
  const cards = {};
  for (let i = 0; i < n; i++) {
    const q = "word" + i;
    cards[q] = { q, tr: "كلمة عربية للمراجعة", ex: "An example sentence where the word appears, as it was found on the page.", form: q, def: "a definition of the word, up to a line or so", added: i, due: 0, interval: 3, ease: 2.5, reps: 2, lapses: 0, last: 1 };
  }
  const bytes = JSON.stringify(cards).length;
  const bg = makeBackground({ net: { mode: "down", requests: [] }, local: { cards } });
  let t0 = performance.now();
  for (let i = 0; i < 20; i++) await bg.send({ type: "cardHas", q: "word" + i });
  const has = (performance.now() - t0) / 20;
  t0 = performance.now();
  await bg.send({ type: "reviewQueue" });
  const queue = performance.now() - t0;
  t0 = performance.now();
  await bg.send({ type: "cardToggle", card: { q: "brandnew", tr: "جديد" } });
  row(`${n} cards (${kb(bytes)}): cardHas / reviewQueue / add a card`, ms(has), `${ms(queue)} / ${ms(performance.now() - t0)}`);
}

/* ---------------- the Windows app: storage, clipboard history, Wikipedia ---------------- */

section("Windows app: storage (desktop/storage.js) with a big deck");
{
  const { Store } = require("../desktop/storage.js");
  const dir = mkdtempSync(path.join(os.tmpdir(), "lamha-bench-"));
  let sent = 0;
  const store = new Store(path.join(dir, "local.json"), "local", changes => { sent += JSON.stringify(changes).length; });
  const cards = {};
  for (let i = 0; i < 3000; i++) cards["word" + i] = { q: "word" + i, tr: "كلمة", ex: "An example sentence where the word appears.", def: "a definition", added: i, due: 0, interval: 3, ease: 2.5, reps: 2, lapses: 0, last: 1 };
  await store.set({ cards, history: Array.from({ length: 100 }, (_, i) => ({ q: "w" + i, tr: "ك", src: "en", t: i })) });
  sent = 0;
  let t0 = performance.now();
  await store.get(["cards", "cardStats", "cardsImported"]);
  row("get the deck (structuredClone, per call)", ms(performance.now() - t0));
  t0 = performance.now();
  await store.set({ cards: { ...cards, one: { q: "one", tr: "واحد" } } });
  row("set the deck (clone + change event)", ms(performance.now() - t0), `${kb(sent)} per window the change is sent to`);
  t0 = performance.now();
  store.flush();
  row("write the file (the whole storage-local.json)", ms(performance.now() - t0), kb(statSync(path.join(dir, "local.json")).size));
  rmSync(dir, { recursive: true, force: true });
}

section("Windows app: clipboard history (desktop/clipboard-store.js), 500 clips");
{
  const { ClipboardStore } = require("../desktop/clipboard-store.js");
  const ctx = vm.createContext({});
  vm.runInContext(src("shared/arabic-normalize.js"), ctx);
  const A = vm.runInContext("LamhaArabic", ctx);
  const dir = mkdtempSync(path.join(os.tmpdir(), "lamha-bench-"));
  const store = new ClipboardStore({ file: path.join(dir, "clipboard.json"), normalize: A.normalizeForSearch, detectLang: A.detectLang });
  const words = "the report meeting tomorrow سأرسل التقرير غدًا invoice project deadline حساب البنك please review attached".split(" ");
  let t0 = performance.now();
  for (let i = 0; i < 500; i++) store.ingest({ text: Array.from({ length: 40 + (i % 200) }, (_, k) => words[(i * 7 + k) % words.length]).join(" ") + " #" + i, sourceApp: "notepad.exe", capturedAt: i });
  row("ingest 500 clips (each)", ms((performance.now() - t0) / 500));
  for (const q of ["", "report", "التقرير غدا", "zzz"]) {
    t0 = performance.now();
    for (let i = 0; i < 20; i++) store.list({ query: q, limit: 100 });
    row(`list, query "${q}" (the panel redraws on each key)`, ms((performance.now() - t0) / 20));
  }
  t0 = performance.now();
  store.flush();
  row("save the history (plain; encrypted on Windows)", ms(performance.now() - t0), kb(statSync(path.join(dir, "clipboard.json")).size));
  rmSync(dir, { recursive: true, force: true });
}

section("Windows app: a downloaded Wikipedia (desktop/wiki-library.js, zim.js)");
{
  let file = process.env.LAMHA_BENCH_ZIM || "";
  if (!file) {
    const dir = path.join(process.env.APPDATA || "", "Lamha", "wikipedia");
    try { const f = readdirSync(dir).find(n => n.endsWith(".zim")); if (f) file = path.join(dir, f); } catch (_) { /* none */ }
  }
  if (!file) console.log("  (skipped: no .zim; set LAMHA_BENCH_ZIM)");
  else {
    const { ZimFile } = require("../desktop/zim.js");
    const { WikiLibrary } = require("../desktop/wiki-library.js");
    const dataDir = mkdtempSync(path.join(os.tmpdir(), "lamha-bench-"));
    const lib = new WikiLibrary({ dataDir });
    await lib.init();
    const z = await ZimFile.open(file);
    const lang = (/^wikipedia_([a-z]{2,3})_/.exec(path.basename(file)) || [])[1] || "en";
    lib.state.files.push({ id: "bench", file, lang, scope: "top", flavour: await z.meta("Flavour"), date: "" });
    await z.close();
    let t0 = performance.now();
    await lib.summary(["Paris"], lang);
    row(`${path.basename(file)}: first summary (opens the file)`, ms(performance.now() - t0));
    const titles = ["London", "Water", "Moon", "Apple", "Tokyo", "Einstein", "Music", "Heart", "Bank", "Sun"];
    t0 = performance.now();
    for (const t of titles) await lib.summary([t], lang);
    row("a summary, the file open (each)", ms((performance.now() - t0) / titles.length));
    t0 = performance.now();
    for (const t of titles) await lib.summary([t + "zzq"], lang);
    row("a word the file doesn't have (each)", ms((performance.now() - t0) / titles.length));
    t0 = performance.now();
    for (const q of ["Par", "Lon", "Wat", "Moo"]) await lib.suggest(q, { lang });
    row("reader: title suggestions (each)", ms((performance.now() - t0) / 4));
    await lib.closeAll();
    rmSync(dataDir, { recursive: true, force: true });
  }
}

section("Content scripts (loaded into every frame of every page)");
{
  const files = ["shared/i18n.js", "shared/lamha-ai.js", "shared/motion.js", "content/styles.js", "content/page-translator.js", "content/content.js"];
  const total = files.reduce((n, f) => n + statSync(new URL(f, root)).size, 0);
  row("their size together", kb(total));
  const { JSDOM } = await import("jsdom");
  const dom = new JSDOM("<!DOCTYPE html><p>x</p>", { runScripts: "outside-only", url: "https://example.com/" });
  const w = dom.window;
  w.browser = { storage: { sync: { get: async d => d }, local: { get: async d => d || {} }, onChanged: { addListener() {} } }, runtime: { sendMessage: async () => ({}), onMessage: { addListener() {} } } };
  w.matchMedia = () => ({ matches: false, addEventListener() {} });
  const t0 = performance.now();
  for (const f of files) w.eval(src(f));
  row("run once in a page (jsdom: slower than Firefox, for comparison)", ms(performance.now() - t0));
}
