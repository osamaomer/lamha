// Tests for clipboard history (desktop app): the Arabic-aware normalizer in shared/ and the clip store,
// with a fake safeStorage and a temporary folder — plain Node, no Electron.
//   node tools/test-clipboard.mjs
import { readFileSync, writeFileSync, mkdtempSync, readdirSync, rmSync, existsSync } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import path from "node:path";
import vm from "node:vm";
import assert from "node:assert/strict";
import { quiet as quietRun, wanted, report, title as printTitle, notRun } from "./test-args.mjs";

const root = new URL("..", import.meta.url);
const ctx = vm.createContext({});
vm.runInContext(readFileSync(new URL("shared/arabic-normalize.js", root), "utf8"), ctx, { filename: "arabic-normalize.js" });
const { normalizeForSearch: norm } = vm.runInContext("LamhaArabic", ctx);
const { ClipboardStore, MAX_PINNED } = createRequire(import.meta.url)("../desktop/clipboard-store.js");

const queue = []; // run in order at the end, so tests can be async
const test = (name, fn) => queue.push({ name, fn });
const section = title => queue.push({ title });

const dir = mkdtempSync(path.join(tmpdir(), "lamha-clip-test-"));
let n = 0;
/** DPAPI stand-in: reversible, but the file must not contain the plain text. */
const fakeSafe = { isEncryptionAvailable: () => true, encryptString: s => Buffer.from(s, "utf8").reverse(), decryptString: b => Buffer.from(b).reverse().toString("utf8") };
const newStore = (opts = {}) => new ClipboardStore({ file: path.join(dir, `clip-${++n}.json`), normalize: norm, ...opts });
const texts = store => store.list({ limit: 1000 }).items.map(i => i.text);
let clock = 1_700_000_000_000;
const cap = (text, extra = {}) => ({ text, sourceApp: "notepad.exe", capturedAt: ++clock, ...extra });

section("normalizeForSearch");
const CASES = [
  ["مصرف", ["مَصْرِف"]],
  ["احمد", ["أحمد", "إحمد", "آحمد"]],
  ["مدرسه", ["مدرسة"]],
  ["مستشفي", ["مستشفى"]],
  ["كتاب", ["كـتـاب"]],
  ["2024", ["٢٠٢٤"]],
  ["inv", ["Invoice_Q3"]]
];
for (const [query, targets] of CASES) {
  for (const t of targets) test(`${query} finds ${t}`, () => assert.ok(norm(t).includes(norm(query)), `${norm(t)} ∌ ${norm(query)}`));
}
test("hamza carriers ؤ ئ, ٱ, superscript alef, Persian digits, whitespace", () => {
  assert.equal(norm("مسؤول"), "مسوول");
  assert.equal(norm("قائمة"), "قايمه");
  assert.equal(norm("ٱلله"), "الله");
  assert.equal(norm("هٰذا"), "هذا");
  assert.equal(norm("۱۴۰۳"), "1403");
  assert.equal(norm("  Hello \n\t World  "), "hello world");
  assert.equal(norm(null), "");
});

const { matchRanges } = vm.runInContext("LamhaArabic", ctx);
const marked = (text, q) => Array.from(matchRanges(text, q), ([a, b]) => text.slice(a, b)); // a host array (the vm realm's differs)
test("highlight ranges map back to the original text (marks, tatweel, variants, case)", () => {
  assert.deepEqual(marked("حساب المَصْرِف الأهلي", "مصرف"), ["المَصْرِف".slice(2)]);
  assert.deepEqual(marked("كـتـاب جديد", "كتاب"), ["كـتـاب"]);
  assert.deepEqual(marked("سيذهب أحمد إلى المدرسة", "احمد مدرسه"), ["أحمد", "مدرسة"]);
  assert.deepEqual(marked("Invoice_Q3 for ٢٠٢٤", "inv 2024"), ["Inv", "٢٠٢٤"]);
  assert.deepEqual(marked("aaaa", "aa"), ["aaaa"]); // overlapping matches merged
  assert.deepEqual(marked("😀 hello", "hello"), ["hello"]); // surrogate pairs
  assert.equal(matchRanges("anything", "  ").length, 0);
});

section("store");
test("ingest, then get the full clip in the data model", () => {
  const s = newStore();
  const id = s.ingest(cap("Hello world", { html: "<b>Hello</b>" }));
  const c = s.get(id);
  assert.match(id, /^c_[0-9a-z]{15}$/);
  assert.equal(c.text, "Hello world");
  assert.equal(c.html, "<b>Hello</b>");
  assert.deepEqual([c.copyCount, c.useCount, c.pinned, c.label, c.lang, c.sourceApp], [1, 0, false, "", "other", "notepad.exe"]);
  assert.deepEqual(c.cache, { translation: {}, english: null, proofread: null, summary: null });
});

test("ids are unique and time-sortable", () => {
  const s = newStore();
  const ids = Array.from({ length: 300 }, (_, i) => s.ingest(cap("t" + i)));
  assert.equal(new Set(ids).size, 300);
  assert.deepEqual([...ids].sort(), ids);
});

test("duplicate (trimmed) text → one clip, copyCount 2, source and HTML refreshed", () => {
  const s = newStore();
  const a = s.ingest(cap("same text", { html: "<i>x</i>" }));
  const b = s.ingest(cap("  same text\n", { sourceApp: "winword.exe" }));
  assert.equal(a, b);
  const c = s.get(a);
  assert.equal(c.copyCount, 2);
  assert.equal(c.sourceApp, "winword.exe");
  assert.equal(c.html, undefined);
  assert.equal(s.list().total, 1);
});

test("order: newest activity first; using a clip moves it up", () => {
  const s = newStore();
  const a = s.ingest(cap("first"));
  s.ingest(cap("second"));
  assert.deepEqual(texts(s), ["second", "first"]);
  s.markUsed(a);
  assert.deepEqual(texts(s), ["first", "second"]);
  assert.equal(s.get(a).useCount, 1);
});

test("cap: maxItems 5 keeps the newest 5 unpinned; a pinned clip survives", () => {
  const s = newStore({ maxItems: 5 });
  const pinned = s.ingest(cap("keep me"));
  s.setPinned(pinned, true);
  for (let i = 1; i <= 6; i++) s.ingest(cap("clip " + i));
  assert.deepEqual(texts(s), ["clip 6", "clip 5", "clip 4", "clip 3", "clip 2", "keep me"]);
  assert.equal(s.list({ filter: "pinned" }).total, 1);
  s.setMaxItems(2);
  assert.deepEqual(texts(s), ["clip 6", "clip 5", "keep me"]);
});

test(`pinning more than ${MAX_PINNED} fails with pin_limit`, () => {
  const s = newStore({ maxItems: 1000 });
  for (let i = 0; i < MAX_PINNED; i++) s.setPinned(s.ingest(cap("p" + i)), true);
  const extra = s.ingest(cap("one too many"));
  assert.throws(() => s.setPinned(extra, true), e => e.code === "pin_limit");
  assert.equal(s.get(extra).pinned, false);
});

test("label: trimmed, 60 chars, searchable; remove; clear keeps pinned", () => {
  const s = newStore();
  const a = s.ingest(cap("IBAN SA03 8000 0000 6080 1016 7519"));
  const b = s.ingest(cap("something else"));
  assert.equal(s.setLabel(a, "  رقم   الحساب  " + "x".repeat(80)).length, 60);
  assert.equal(s.list({ query: "الحساب" }).items[0].id, a);
  s.setPinned(a, true);
  assert.equal(s.remove(b), true);
  assert.equal(s.remove(b), false);
  s.ingest(cap("temp"));
  assert.equal(s.clear({ keepPinned: true }), 1);
  assert.equal(s.clear(), 0);
  assert.throws(() => s.markUsed(a), e => e.code === "not_found");
});

test("search: Arabic variants, every token must match, label and word starts rank higher", () => {
  const s = newStore();
  const ar = s.ingest(cap("سيذهب أحمد إلى المدرسة غدًا"));
  s.ingest(cap("Invoice_Q3 total ٢٠٢٤"));
  s.ingest(cap("a reinvented wheel"));
  const labelled = s.ingest(cap("text without the word"));
  s.setLabel(labelled, "invoice مهم");
  assert.equal(s.list({ query: "احمد مدرسه" }).items[0].id, ar);
  assert.equal(s.list({ query: "احمد بيت" }).total, 0);
  const inv = s.list({ query: "inv" }).items.map(i => i.label || i.text);
  assert.deepEqual(inv, ["invoice مهم", "Invoice_Q3 total ٢٠٢٤", "a reinvented wheel"]); // label +30, then word start +10
  assert.equal(s.list({ query: "2024" }).total, 1);
  assert.equal(s.list({ query: "notepad" }).total, 4); // source app is searchable
});

test("remove, then restore (undo) brings the clip back with its id and pin", () => {
  const s = newStore();
  const id = s.ingest(cap("undo me"));
  s.setPinned(id, true);
  s.remove(id);
  assert.equal(s.list().total, 0);
  assert.equal(s.restore(id), id);
  assert.equal(s.get(id).pinned, true);
  assert.equal(s.list({ query: "undo" }).total, 1);
  assert.throws(() => s.restore(id), e => e.code === "not_found"); // only once
  s.remove(id);
  const again = s.ingest(cap("undo me")); // copied again before undo: that clip wins
  assert.equal(s.restore(id), again);
  assert.equal(s.list().total, 1);
});

test("list: previews carry 300 chars; filter pinned; limit / offset", () => {
  const s = newStore();
  const id = s.ingest(cap("x".repeat(1000)));
  for (let i = 0; i < 10; i++) s.ingest(cap("n" + i));
  s.setPinned(id, true);
  const long = s.list({ filter: "pinned" }).items[0];
  assert.equal(long.text.length, 300);
  assert.equal(long.length, 1000);
  const page = s.list({ limit: 4, offset: 4 });
  assert.equal(page.total, 11);
  assert.deepEqual(page.items.map(i => i.text), ["n5", "n4", "n3", "n2"]);
});

test("search over 1,000 clips takes under 16 ms", () => {
  const s = newStore({ maxItems: 1000 });
  const words = ["مصرف", "invoice", "meeting", "تقرير", "الموظفين", "budget", "مدرسة", "project", "أحمد", "report"];
  for (let i = 0; i < 1000; i++) {
    const body = Array.from({ length: 60 }, (_, j) => words[(i * 7 + j * 3) % words.length]).join(" ");
    s.ingest(cap(`${i} ${body} ${"lorem ipsum dolor ".repeat(i % 50)}`));
  }
  for (const q of ["مصرف", "invoice report", "احمد مدرسه", "zzz", ""]) s.list({ query: q }); // warm-up
  const times = ["مصرف", "invoice report", "احمد مدرسه", "zzz", "rep", ""].map(q => {
    const t0 = performance.now();
    s.list({ query: q });
    return performance.now() - t0;
  });
  const worst = Math.max(...times);
  assert.ok(worst < 16, `slowest query ${worst.toFixed(1)} ms`);
  console.log(`    (slowest query ${worst.toFixed(2)} ms)`);
});

test("persists: encrypted when available, restored by a new store", () => {
  const file = path.join(dir, "persist.json");
  const a = new ClipboardStore({ file, normalize: norm, safeStorage: fakeSafe });
  const id = a.ingest(cap("secret الحافظة text"));
  a.setPinned(id, true);
  a.flush();
  const raw = readFileSync(file, "utf8");
  assert.ok(!raw.includes("secret") && JSON.parse(raw).encrypted === true, "not encrypted");
  const b = new ClipboardStore({ file, normalize: norm, safeStorage: fakeSafe });
  assert.equal(b.get(id).text, "secret الحافظة text");
  assert.equal(b.get(id).pinned, true);
  assert.equal(b.list({ query: "الحافظه" }).total, 1); // index rebuilt on load
});

test("no encryption available → plain JSON marked encrypted: false", () => {
  const file = path.join(dir, "plain.json");
  const a = new ClipboardStore({ file, normalize: norm, safeStorage: { isEncryptionAvailable: () => false } });
  a.ingest(cap("plain"));
  a.flush();
  assert.equal(JSON.parse(readFileSync(file, "utf8")).encrypted, false);
  assert.equal(a.encrypted, false);
});

test("corrupt file → empty history, file kept as clipboard.corrupt-*.json", () => {
  const sub = mkdtempSync(path.join(dir, "corrupt-"));
  const file = path.join(sub, "clipboard.json");
  writeFileSync(file, "{ not json");
  const s = new ClipboardStore({ file, normalize: norm, safeStorage: fakeSafe });
  assert.equal(s.list().total, 0);
  assert.ok(!existsSync(file), "corrupt file still in place");
  const kept = readdirSync(sub).filter(f => /^clipboard\.corrupt-\d+\.json$/.test(f));
  assert.equal(kept.length, 1);
  assert.equal(readFileSync(path.join(sub, kept[0]), "utf8"), "{ not json");
  s.ingest(cap("works again"));
  s.flush();
  assert.equal(new ClipboardStore({ file, normalize: norm, safeStorage: fakeSafe }).list().total, 1);
});

test("nothing changed → nothing written", () => {
  const s = newStore();
  s.flush();
  assert.ok(!existsSync(s.file));
});

section("detectLang");
const { detectLang } = vm.runInContext("LamhaArabic", ctx);
for (const [text, want] of [
  ["مرحبًا بكم في لمحة، هذا نص عربي خالص", "ar"],
  ["Please send me the invoice before Friday.", "en"],
  ["الاجتماع مع team يوم Monday الساعة", "mixed"],
  ["2024-09-26 😀 !!! ٣٤٥", "other"],
  ["x", "other"],
  ["سأرسل لك التقرير النهائي اليوم عبر البريد الإلكتروني إن شاء الله PDF", "ar"], // a few Latin letters stay "ar"
  ["The word مصرف means bank", "en"]
]) test(`${JSON.stringify(text.slice(0, 24))} → ${want}`, () => assert.equal(detectLang(text), want));

test("store tags language on capture and backfills older clips on load", () => {
  const file = path.join(dir, "lang.json");
  writeFileSync(file, JSON.stringify({ v: 1, encrypted: false, clips: [{ id: "c_old", text: "Hello there", createdAt: 1 }] }));
  const s = new ClipboardStore({ file, normalize: norm, detectLang });
  assert.equal(s.get("c_old").lang, "en");
  assert.equal(s.get(s.ingest(cap("نص عربي"))).lang, "ar");
});

/* ---- Lamha's tools on clips: the real background.js in a VM, with a fake network (as tools/test-writing.mjs) ---- */
section("actions (real background.js, fake network)");
const { createClipActions } = createRequire(import.meta.url)("../desktop/clipboard-actions.js");
const bgSrc = p => readFileSync(new URL(p, root), "utf8");
const json = (status, body) => ({ ok: status >= 200 && status < 300, status, json: async () => body, text: async () => JSON.stringify(body) });
const claudeReply = obj => json(200, { stop_reason: "end_turn", content: [{ type: "text", text: JSON.stringify(obj) }] });
const PROOF = { corrected: "She doesn't like apples.", issues: [{ original: "dont", fix: "doesn't", category: "agreement", why: "مع she يأتي doesn't." }] };

/** Fake network: Google (lookup) and Claude; counts calls per service. */
function fakeNet() {
  const calls = { google: 0, claude: 0 };
  const fetchImpl = async (url, init = {}) => {
    url = String(url);
    if (url.includes("/translate_a/single")) {
      calls.google++;
      const q = new URL(url).searchParams.get("q") || "";
      if (q === "bank") {
        return json(200, { src: "en", sentences: [{ trans: "مصرف", orig: "bank" }],
          definitions: [{ pos: "noun", entry: [{ gloss: "a financial institution", example: "he cashed a check at the bank", definition_id: "d1" }] }] });
      }
      return json(200, { src: "en", sentences: [{ trans: "حافظ على وعدك.", orig: q }] });
    }
    if (url.startsWith("https://api.anthropic.com/")) {
      calls.claude++;
      const prompt = JSON.parse(init.body).messages[0].content;
      if (/^Proofread/.test(prompt)) return claudeReply(PROOF);
      if (/Summarize/.test(prompt)) return claudeReply({ text: /in English/.test(prompt) ? "• a short summary" : "• ملخص قصير" });
      return claudeReply({ text: "I will send you the final report today." });
    }
    return json(404, {});
  };
  return { calls, fetchImpl };
}

function bgEnv({ local = {}, sync = {} } = {}) {
  const net = fakeNet();
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
  let onMessage;
  const noop = { addListener() {} };
  const browser = {
    storage: { local: area({ cardsImported: true, ...local }), sync: area({ uiLang: "ar", dictSource: "online", translateDefinitions: false, ...sync }), onChanged: noop },
    runtime: { onMessage: { addListener: f => { onMessage = f; } }, onInstalled: noop, onStartup: noop, getURL: p => "moz-extension://x/" + p },
    menus: { removeAll: async () => {}, create() {}, onClicked: noop },
    commands: { onCommand: noop },
    tabs: { query: async () => [], sendMessage: async () => {}, create: async () => {} },
    permissions: { contains: async () => true }
  };
  const bg = vm.createContext({
    browser, fetch: net.fetchImpl, console, setTimeout, clearTimeout, AbortController, URLSearchParams, URL, structuredClone,
    LocalDict: { lookupEn: async () => null, lookupAr: async () => null }, Audio: class {},
    LamhaPackStore: { get: async () => undefined, setMany: async () => {}, removePrefix: async () => {} } // no language packs
  });
  vm.runInContext(bgSrc("packs.js"), bg);
  vm.runInContext(bgSrc("shared/i18n.js"), bg);
  vm.runInContext(bgSrc("shared/lamha-ai.js"), bg);
  vm.runInContext(bgSrc("background.js"), bg);
  const send = msg => onMessage(msg, {});
  const store = newStore({ detectLang });
  const actions = createClipActions({
    store,
    send: async msg => JSON.parse(JSON.stringify((await send(msg)) ?? null)), // out of the VM realm, as over IPC
    targetLang: async () => (await browser.storage.sync.get({ targetLang: "ar" })).targetLang,
    readCards: async () => (await browser.storage.local.get({ cards: {} })).cards
  });
  return { net, browser, send, store, actions, LamhaAI: vm.runInContext("LamhaAI", bg), flush: () => vm.runInContext("journalQueue", bg) };
}
const failsWith = async (p, code) => {
  try { await p; } catch (e) { assert.equal(e.code, code); return; }
  assert.fail("no error, expected " + code);
};

test("ترجم: translation cached on the clip; a second open makes no request; إعادة refreshes", async () => {
  const env = bgEnv();
  const id = env.store.ingest(cap("Keep your promise."));
  const a = await env.actions.run(id, "translate");
  assert.equal(a.text, "حافظ على وعدك.");
  assert.equal(env.net.calls.google, 1);
  assert.equal(env.store.get(id).cache.translation.ar, "حافظ على وعدك.");
  const b = await env.actions.run(id, "translate");
  assert.equal(b.cached, true);
  assert.equal(env.net.calls.google, 1, "second open went to the network");
  assert.equal(env.store.list({ tl: "ar" }).items[0].translation, "حافظ على وعدك.", "list preview line");
  const again = await env.actions.run(id, "translate", { fresh: true }); // background.js may answer from its own memory
  assert.ok(!again.cached && again.text === "حافظ على وعدك.");
});

test("ترجم respects «Local only»: a sentence is not sent anywhere", async () => {
  const env = bgEnv({ sync: { dictSource: "offline" } });
  const id = env.store.ingest(cap("Keep your promise."));
  await failsWith(env.actions.run(id, "translate"), "offline_mode");
  assert.equal(env.net.calls.google, 0);
});

test("اكتبه بالإنجليزية / لخّص: result cached, second call makes no request", async () => {
  const env = bgEnv({ local: { aiKey: "sk-test" } });
  const id = env.store.ingest(cap("سأرسل لك التقرير النهائي اليوم " + "مع كل التفاصيل المطلوبة ".repeat(20)));
  const en = await env.actions.run(id, "english");
  assert.equal(en.text, "I will send you the final report today.");
  const sum = await env.actions.run(id, "summary");
  assert.equal(sum.text, "• ملخص قصير");
  assert.equal(env.net.calls.claude, 2);
  assert.equal((await env.actions.run(id, "english")).cached, true);
  assert.equal((await env.actions.run(id, "summary")).cached, true);
  assert.equal(env.net.calls.claude, 2, "cached results went to the network");
  assert.equal(env.store.get(id).cache.english.text, en.text);
});

test("لخّص in either language: each cached on the clip; older saved summaries still count as Arabic", async () => {
  const env = bgEnv({ local: { aiKey: "sk-test" } });
  const id = env.store.ingest(cap("The quarterly report shows growth. ".repeat(20)));
  const ar = await env.actions.run(id, "summary");
  assert.deepEqual([ar.lang, ar.text], ["ar", "• ملخص قصير"]);
  const en = await env.actions.run(id, "summary", { lang: "en" });
  assert.deepEqual([en.lang, en.text], ["en", "• a short summary"]);
  assert.equal(env.net.calls.claude, 2);
  assert.equal((await env.actions.run(id, "summary", { lang: "en" })).cached, true);
  assert.equal((await env.actions.run(id, "summary", { lang: "ar" })).text, "• ملخص قصير");
  assert.equal(env.net.calls.claude, 2, "a cached language went to the network");
  const file = path.join(dir, "old-summary.json");
  writeFileSync(file, JSON.stringify({ v: 1, encrypted: false, clips: [{ id: "c_s", text: "x", cache: { summary: { text: "قديم" } } }] }));
  assert.deepEqual(new ClipboardStore({ file, normalize: norm }).get("c_s").cache.summary, { ar: { text: "قديم" } });
});

test("دقّق لغويًا: diff data cached; the mistake journal counts it once (إعادة isn't counted again)", async () => {
  const env = bgEnv({ local: { aiKey: "sk-test" } });
  const id = env.store.ingest(cap("She dont like apples."));
  const r = await env.actions.run(id, "proofread");
  assert.equal(r.corrected, PROOF.corrected);
  assert.equal(r.issues.length, 1);
  await env.flush();
  assert.equal(env.browser.storage.local.data.mistakes.checks, 1);
  assert.equal((await env.actions.run(id, "proofread")).cached, true);
  await env.actions.run(id, "proofread", { fresh: true });
  await env.flush();
  assert.equal(env.net.calls.claude, 2);
  assert.equal(env.browser.storage.local.data.mistakes.checks, 1, "a retry was counted again");
});

test("journal off: proofreading a clip records nothing", async () => {
  const env = bgEnv({ local: { aiKey: "sk-test" }, sync: { saveMistakes: false } });
  await env.actions.run(env.store.ingest(cap("She dont like apples.")), "proofread");
  await env.flush();
  assert.equal(env.browser.storage.local.data.mistakes, undefined);
});

test("no writing-tools provider: the existing Arabic message, pointing to Settings", async () => {
  const env = bgEnv();
  const id = env.store.ingest(cap("She dont like apples."));
  let code;
  try { await env.actions.run(id, "proofread"); } catch (e) { code = e.code; }
  assert.equal(code, "ai_no_key");
  const [title, , settings] = env.LamhaAI.errorInfo(code);
  assert.equal(title, "فعّل أدوات الكتابة");
  assert.equal(settings, true);
  assert.equal(env.net.calls.claude, 0);
  assert.equal(env.store.get(id).cache.proofread, null, "an error was cached");
});

for (const cardsAuto of [true, false]) {
  test(`أضف للمراجعة: adds with the dictionary example, again removes it (cardsAuto ${cardsAuto ? "on" : "off"})`, async () => {
    const env = bgEnv({ sync: { cardsAuto } });
    const id = env.store.ingest(cap("bank"));
    const first = await env.actions.run(id, "review");
    assert.equal(first.inDeck, true);
    const card = env.browser.storage.local.data.cards.bank;
    assert.equal(card.tr, "مصرف");
    assert.equal(card.ex, "he cashed a check at the bank");
    assert.equal(card.def, "a financial institution");
    const second = await env.actions.run(id, "review");
    assert.equal(second.inDeck, false);
    assert.equal(env.browser.storage.local.data.cards.bank, undefined, "not removed");
    assert.equal((await env.actions.run(id, "review")).inDeck, true, "third press adds again");
  });
}

test("أضف للمراجعة: a word named like Object's properties (constructor) is added, not taken as already there", async () => {
  const env = bgEnv({ sync: { cardsAuto: false } });
  const id = env.store.ingest(cap("constructor"));
  assert.equal((await env.actions.run(id, "review")).inDeck, true);
  assert.ok(Object.hasOwn(env.browser.storage.local.data.cards, "constructor"), "the card is in the deck");
  assert.equal((await env.actions.run(id, "review")).inDeck, false, "a second press removes it, as for any word");
});

test("أضف للمراجعة only for a single English word; copying words never adds cards", async () => {
  const env = bgEnv();
  await failsWith(env.actions.run(env.store.ingest(cap("two words")), "review"), "not_a_word");
  env.store.ingest(cap("serendipity")); // a capture alone
  assert.equal(Object.keys(env.browser.storage.local.data.cards || {}).length, 0);
  await failsWith(env.actions.run("c_missing", "translate"), "not_found");
  await failsWith(env.actions.run(env.store.ingest(cap("x y")), "rm -rf"), "unknown_action");
});

section("privacy");
const { isCardNumber, exeName, DEFAULTS: PRIVACY_DEFAULTS } = createRequire(import.meta.url)("../desktop/clipboard-privacy.js");
for (const [text, want] of [
  ["4111 1111 1111 1111", true], // Visa test number, spaces
  ["4111-1111-1111-1111", true],
  ["5555555555554444", true], // Mastercard, no separators
  ["378282246310005", true], // Amex, 15 digits
  ["  4012888888881881  ", true], // surrounding whitespace
  ["4111 1111 1111 1112", false], // fails Luhn
  ["+966 50 123 4567", false], // phone number
  ["0501234567", false], // too short
  ["SA03 8000 0000 6080 1016 7519", false], // IBAN (letters)
  ["Card: 4111 1111 1111 1111", false], // not a number on its own
  ["4111  1111 1111 1111", false], // double space
  ["41111111111111111111", false] // 20 digits
]) test(`card number ${JSON.stringify(text)} → ${want}`, () => assert.equal(isCardNumber(text), want));

test("exe names are normalized for «البرامج المستثناة»", () => {
  assert.equal(exeName("KeePassXC"), "keepassxc.exe");
  assert.equal(exeName("  C:\\Program Files\\Bitwarden\\Bitwarden.EXE "), "bitwarden.exe");
  assert.equal(exeName("\"notepad.exe\""), "notepad.exe");
  assert.equal(exeName(""), "");
  assert.equal(exeName("a<b>.exe"), "");
  assert.deepEqual(PRIVACY_DEFAULTS.clipboardExcludedApps.slice(0, 2), ["keepass.exe", "keepassxc.exe"]);
  assert.equal(PRIVACY_DEFAULTS.clipboardEnabled, false);
});

test("expiry with a mocked clock: old unpinned clips go, pinned and recent stay; 0 = never", () => {
  const s = newStore();
  const day = 86400e3, t0 = 1_800_000_000_000;
  const old = s.ingest({ text: "old", sourceApp: "a.exe", capturedAt: t0 });
  const oldPinned = s.ingest({ text: "old pinned", sourceApp: "a.exe", capturedAt: t0 });
  s.setPinned(oldPinned, true);
  const recent = s.ingest({ text: "recent", sourceApp: "a.exe", capturedAt: t0 + 25 * day });
  const usedLately = s.ingest({ text: "used lately", sourceApp: "a.exe", capturedAt: t0 });
  s.get(usedLately); // lastUsedAt counts as activity
  s.clips.get(usedLately).lastUsedAt = t0 + 20 * day;
  assert.equal(s.expire(0, t0 + 400 * day), 0, "never");
  assert.equal(s.expire(30, t0 + 31 * day), 1);
  assert.equal(s.get(old), null);
  assert.ok(s.get(oldPinned) && s.get(recent) && s.get(usedLately));
});

test("apps() lists programs by count; removeApp deletes that program's clips", () => {
  const s = newStore();
  s.ingest(cap("a1", { sourceApp: "word.exe" }));
  s.ingest(cap("a2", { sourceApp: "word.exe" }));
  s.ingest(cap("b1", { sourceApp: "chrome.exe" }));
  assert.deepEqual(JSON.parse(JSON.stringify(s.apps())), [{ app: "word.exe", count: 2 }, { app: "chrome.exe", count: 1 }]);
  assert.equal(s.removeApp("word.exe"), 2);
  assert.deepEqual(texts(s), ["b1"]);
});

section("settings store (storage-local.json)");

const { Store } = createRequire(import.meta.url)("../desktop/storage.js");
const quiet = fn => { const warn = console.warn; console.warn = () => {}; try { return fn(); } finally { console.warn = warn; } };

test("API keys are encrypted on disk and plain in memory; other settings stay readable", async () => {
  const file = path.join(dir, "local-keys.json");
  const a = new Store(file, "local", () => {}, { secrets: ["aiKey"], safeStorage: fakeSafe });
  await a.set({ aiKey: "sk-ant-secret", targetLang: "ar" });
  a.flush();
  const raw = readFileSync(file, "utf8");
  assert.ok(!raw.includes("sk-ant-secret"), "key in plain text");
  assert.equal(JSON.parse(raw).targetLang, "ar");
  const b = new Store(file, "local", () => {}, { secrets: ["aiKey"], safeStorage: fakeSafe });
  assert.deepEqual(await b.get(["aiKey", "targetLang"]), { aiKey: "sk-ant-secret", targetLang: "ar" });
});

test("a key saved in plain text by an older version is encrypted when the app starts", async () => {
  const file = path.join(dir, "local-old.json");
  writeFileSync(file, JSON.stringify({ geminiKey: "AIza-old", cards: { river: {} } }));
  const s = new Store(file, "local", () => {}, { secrets: ["geminiKey"], safeStorage: fakeSafe });
  assert.equal((await s.get("geminiKey")).geminiKey, "AIza-old");
  const raw = readFileSync(file, "utf8");
  assert.ok(!raw.includes("AIza-old") && raw.includes("river"));
});

test("a key that can't be decrypted (another Windows account) is dropped, the rest is kept", async () => {
  const file = path.join(dir, "local-foreign.json");
  writeFileSync(file, JSON.stringify({ aiKey: { $enc: "AAAA" }, saveHistory: false }));
  const broken = { ...fakeSafe, decryptString: () => { throw new Error("DPAPI failed"); } };
  const s = quiet(() => new Store(file, "local", () => {}, { secrets: ["aiKey"], safeStorage: broken }));
  assert.deepEqual(await s.get(null), { saveHistory: false });
});

test("an unreadable settings file is kept aside, not overwritten", async () => {
  const sub = mkdtempSync(path.join(dir, "corrupt-"));
  const file = path.join(sub, "storage-local.json");
  writeFileSync(file, "{ half a file");
  const s = quiet(() => new Store(file, "local", () => {}));
  assert.deepEqual(await s.get(null), {});
  await s.set({ enabled: true });
  s.flush();
  const kept = readdirSync(sub).filter(f => f.startsWith("storage-local.corrupt-"));
  assert.equal(kept.length, 1);
  assert.equal(readFileSync(path.join(sub, kept[0]), "utf8"), "{ half a file");
});

test("get() only reads own keys (\"constructor\" is not a setting)", async () => {
  const s = new Store(path.join(dir, "local-proto.json"), "local", () => {});
  assert.deepEqual(await s.get(["constructor", "toString"]), {});
  assert.deepEqual(await s.get({ constructor: 1 }), { constructor: 1 });
});

let passed = 0, failed = 0, filtered = 0;
for (const { name, fn, title } of queue) {
  if (title) { printTitle(title); continue; }
  if (!wanted(name)) { filtered++; continue; }
  try { await fn(); passed++; report("  ✓ " + name); }
  catch (err) { failed++; report("  ✗ " + name + "\n    " + String(err.message).split("\n").join("\n    ")); }
}
rmSync(dir, { recursive: true, force: true });
console.log(`${quietRun ? "" : "\n"}${passed}/${passed + failed} passed${notRun(filtered)}`);
process.exit(failed ? 1 : 0);
