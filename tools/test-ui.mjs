// UI test. Needs jsdom (a devDependency at the repo root: npm install), then  node tools/test-ui.mjs
// Real popup.html/options.html + popup.js/options.js in jsdom, wired to the real background.js
// (in a VM) through a shared fake storage. Fake network: Ollama answers the proofread.
import { readFileSync } from "node:fs";
import vm from "node:vm";
import assert from "node:assert/strict";
import { JSDOM, VirtualConsole } from "jsdom";
import { pathToFileURL } from "node:url";

// the extension folder: this file's parent, or $env:LAMHA_EXT when run from elsewhere
const EXT = process.env.LAMHA_EXT ? pathToFileURL(process.env.LAMHA_EXT.replace(/[\\/]?$/, "/")) : new URL("..", import.meta.url);
const src = p => readFileSync(new URL(p, EXT), "utf8");
const sleep = ms => new Promise(r => setTimeout(r, ms));
const json = (s, b) => ({ ok: s < 300, status: s, json: async () => b });

/* ---- shared fake storage + background ---- */
const listeners = [];
function area(name, data) {
  return {
    data,
    async get(k) {
      if (k == null) return { ...data };
      if (typeof k === "string") k = [k];
      if (Array.isArray(k)) return Object.fromEntries(k.filter(x => x in data).map(x => [x, structuredClone(data[x])]));
      return Object.fromEntries(Object.entries(k).map(([a, d]) => [a, a in data ? structuredClone(data[a]) : d]));
    },
    async set(o) {
      const changes = {};
      for (const [k, v] of Object.entries(o)) { changes[k] = { oldValue: data[k], newValue: v }; data[k] = structuredClone(v); }
      listeners.forEach(f => f(changes, name));
    },
    async remove(k) { [].concat(k).forEach(x => delete data[x]); }
  };
}
const now = Date.now();
const card = (q, tr, extra = {}) => ({ q, tr, ex: "", form: "", def: "", added: now, due: 0, interval: 0, ease: 2.5, reps: 0, lapses: 0, ...extra });
const local = area("local", {
  popupMode: "review",
  aiProvider: "ollama", ollamaModel: "qwen3.5:4b",
  cardsImported: true,
  cards: {
    bank: card("bank", "ضفة", { ex: "We sat on the bank of the river.", form: "bank", def: "the land alongside a river", reps: 2, interval: 5, last: now - 6 * 864e5, due: now - 1000 }),
    resilient: card("resilient", "مرن", { ex: "Kids are resilient.", form: "resilient", added: now - 5 }),
    thrive: card("thrive", "يزدهر", { added: now - 10 })
  },
  mistakes: { checks: 4, counts: { other: 2, prepositions: 3, articles: 6 }, recent: [{ cat: "articles", original: "market", fix: "the market", why: "مكان معروف.", t: now }] }
});
const sync = area("sync", { uiLang: "ar" }); // the English interface has its own steps below
const onChanged = { addListener: f => listeners.push(f) };
const ev = { addListener() {} };

let bgHandler;
const aiCalls = [];
const bgCtx = vm.createContext({
  browser: {
    storage: { local, sync, onChanged },
    runtime: { onMessage: { addListener: f => { bgHandler = f; } }, onInstalled: ev, onStartup: ev },
    menus: { removeAll: async () => {}, create() {}, onClicked: ev }, commands: { onCommand: ev }
  },
  fetch: async (url, init) => {
    aiCalls.push(url);
    if (url.endsWith("/api/chat")) {
      return json(200, { done_reason: "stop", message: { content: JSON.stringify({
        corrected: "I went to the market.",
        issues: [{ original: "go", fix: "went", category: "verb_tense", why: "الماضي." }, { original: "market", fix: "the market", category: "articles", why: "مكان معروف." }]
      }) } });
    }
    return json(404, {});
  },
  setTimeout, clearTimeout, AbortController, URLSearchParams, structuredClone, console, LocalDict: {}, Audio: class { play() { return Promise.resolve(); } }
});
vm.runInContext(src("shared/i18n.js"), bgCtx);
vm.runInContext(src("shared/lamha-ai.js"), bgCtx);
vm.runInContext(src("background.js"), bgCtx);

/* ---- page loader ---- */
async function openPage(path, scripts, { extra = {} } = {}) {
  const html = src(path).replace(/<script[\s\S]*?<\/script>/g, "").replace(/<link[^>]*>/g, "");
  // pages reload themselves when the interface language changes; jsdom can't navigate, which is fine here
  const virtualConsole = new VirtualConsole();
  virtualConsole.forwardTo ? virtualConsole.forwardTo(console, { jsdomErrors: "none" }) : virtualConsole.sendTo(console, { omitJSDOMErrors: true });
  virtualConsole.on("jsdomError", e => { if (!/Not implemented: navigation/.test(e.message)) console.error(e); });
  const dom = new JSDOM(html, { runScripts: "outside-only", url: "moz-extension://lamha/" + path + (extra.hash || ""), virtualConsole });
  const w = dom.window;
  w.browser = {
    storage: { local, sync, onChanged },
    runtime: {
      sendMessage: async msg => structuredClone(await bgHandler(msg, {})),
      getURL: p => "moz-extension://lamha/" + p, getManifest: () => ({ version: "test" }), openOptionsPage() {}
    },
    tabs: { query: async () => [{ id: 1, url: "https://example.com/" }], sendMessage: async () => ({ active: false }), create: async () => {} },
    permissions: { contains: async () => true, request: async () => true },
    commands: { getAll: async () => [] }
  };
  w.confirm = () => true;
  w.HTMLElement.prototype.scrollIntoView = () => {};
  for (const s of scripts) w.eval(src(s));
  await sleep(150);
  return w;
}
const key = (w, k) => w.document.dispatchEvent(new w.KeyboardEvent("keydown", { key: k, bubbles: true }));
const text = el => el.textContent.replace(/\s+/g, " ").trim();

const results = [];
async function step(name, fn) {
  try { await fn(); results.push("  ✓ " + name); }
  catch (e) { results.push("  ✗ " + name + "\n    " + String(e.stack || e).split("\n").slice(0, 3).join("\n    ")); }
}

/* ---- popup: review ---- */
const pop = await openPage("popup/popup.html", ["shared/i18n.js", "shared/lamha-ai.js", "shared/motion.js", "popup/popup.js"]);
const $ = id => pop.document.getElementById(id);

await step("popup opens on the review tab with counts and badge", async () => {
  assert.equal($("rvPane").hidden, false);
  assert.equal($("trPane").hidden, true);
  assert.equal($("tabRv").getAttribute("aria-selected"), "true");
  assert.match(text($("rvHead")), /للمراجعة ١/);
  assert.match(text($("rvHead")), /جديدة ٢/);
  assert.equal($("rvBadge").textContent, "٣");
});

await step("front: due word first, example sentence with the word marked, meaning hidden", async () => {
  assert.equal(text($("rvCard").querySelector(".rv-word")), "bank");
  assert.equal($("rvCard").querySelector(".rv-ex mark").textContent, "bank");
  assert.equal($("rvCard").querySelector(".rv-back"), null);
  assert.ok($("rvCard").querySelector(".rv-show"));
});

await step("Space reveals meaning, definition and 3 grades with intervals", async () => {
  key(pop, " ");
  await sleep(20);
  assert.equal(text($("rvCard").querySelector(".rv-tr")), "ضفة");
  assert.equal(text($("rvCard").querySelector(".rv-def")), "the land alongside a river");
  const g = [...$("rvCard").querySelectorAll(".rv-grades button")].map(text);
  assert.deepEqual(g, ["نسيت١٠ د", "صعبةستة أيام".replace("ستة أيام", "٦ أيام"), "عرفتها١٣ يومًا"]);
});

await step("key 3 grades 'good': card moves 13 days ahead, next card is a new word", async () => {
  key(pop, "3");
  await sleep(100);
  const b = local.data.cards.bank;
  assert.equal(b.interval, 13);
  assert.ok(b.due > Date.now() + 12 * 864e5);
  assert.equal(text($("rvCard").querySelector(".rv-word")), "resilient");
  assert.ok($("rvCard").querySelector(".rv-tag"), "'new word' tag");
  assert.equal($("rvBadge").textContent, "٢");
});

await step("remove from review deletes the card", async () => {
  key(pop, " "); await sleep(20);
  [...$("rvCard").querySelectorAll("button.link")].find(b => /إزالة/.test(b.textContent)).click();
  await sleep(100);
  assert.equal(local.data.cards.resilient, undefined);
  assert.equal(text($("rvCard").querySelector(".rv-word")), "thrive");
});

await step("finishing the queue shows the done message with next review time", async () => {
  key(pop, " "); await sleep(20);
  key(pop, "1"); // again → 10 minutes
  await sleep(100);
  assert.match(text($("rvCard")), /أحسنت! لا توجد كلمات للمراجعة الآن/);
  assert.match(text($("rvCard")), /المراجعة القادمة بعد ١٠ د/);
  assert.equal($("rvBadge").hidden, true);
});

/* ---- popup: compose ---- */
await step("compose: Arabic draft offers only 'to English'; English offers the editing tools", async () => {
  $("tabWr").click();
  await sleep(20);
  assert.equal($("wrPane").hidden, false);
  const d = $("draft");
  d.value = "أريد إجازة يوم الأحد";
  d.dispatchEvent(new pop.Event("input"));
  assert.deepEqual([...$("wrTools").querySelectorAll("button")].map(text), ["بالإنجليزية"]);
  d.value = "I go to market.";
  d.dispatchEvent(new pop.Event("input"));
  assert.deepEqual([...$("wrTools").querySelectorAll("button")].map(text), ["تدقيق لغوي", "تحسين", "رسمي", "ودّي", "أقصر"]);
});

await step("compose: Ctrl+Enter proofreads; diff, issues with type chips; journal updated", async () => {
  $("draft").dispatchEvent(new pop.KeyboardEvent("keydown", { key: "Enter", ctrlKey: true, bubbles: true }));
  await sleep(200);
  assert.ok(aiCalls.some(u => u.endsWith("/api/chat")), "Ollama called");
  const out = $("wrOut");
  assert.equal(out.hidden, false);
  assert.deepEqual([...out.querySelectorAll(".text del")].map(text), ["go"]);
  assert.deepEqual([...out.querySelectorAll(".text ins")].map(text), ["went", "the"]);
  assert.deepEqual([...out.querySelectorAll(".issues .cat")].map(text), ["أزمنة الأفعال", "أدوات التعريف والتنكير (a / an / the)"]);
  assert.equal(local.data.mistakes.counts.articles, 7);
  assert.equal(local.data.mistakes.checks, 5);
  assert.match(text($("weak")), /أكثر أخطائك: أدوات التعريف والتنكير \(a \/ an \/ the\) \(٧\)/);
});

await step("compose: 'use it' puts the correction back in the box; draft saved", async () => {
  [...$("wrOut").querySelectorAll("button")].find(b => text(b) === "استخدمه").click();
  await sleep(400);
  assert.equal($("draft").value, "I went to the market.");
  assert.equal(local.data.draft, "I went to the market.");
});

/* ---- options: journal + review ---- */
const opt = await openPage("options/options.html", ["shared/i18n.js", "options/i18n-options.js", "shared/lamha-ai.js", "shared/motion.js", "options/options.js"]);
const o = id => opt.document.getElementById(id);
await sleep(200);

await step("options: journal bars sorted by count, summary names the top weakness", async () => {
  assert.match(text(o("jSummary")), /دقّقت ٥ نصوص ووُجد فيها ١٣ خطأً/);
  assert.match(text(o("jSummary")), /أكثر أخطائك: أدوات التعريف/);
  const bars = [...o("jBars").querySelectorAll(".bar")].map(b => text(b));
  assert.equal(bars.length, 4);
  assert.match(bars[0], /^أدوات التعريف.*٧$/);
  // the seed lists "other" first: the bars must still come out most frequent first
  const counts = bars.map(b => Number(b.match(/[٠-٩]+$/)[0].replace(/[٠-٩]/g, d => "٠١٢٣٤٥٦٧٨٩".indexOf(d))));
  assert.deepEqual(counts, [...counts].sort((a, b) => b - a));
});

await step("options: clicking a bar shows its rule and only its examples", async () => {
  [...o("jBars").querySelectorAll(".bar")].find(b => /أزمنة الأفعال/.test(b.textContent)).click();
  await sleep(50);
  assert.equal(o("jTip").hidden, false);
  assert.match(text(o("jTip")), /الماضي البسيط/);
  assert.deepEqual([...o("jRecent").querySelectorAll("del")].map(text), ["go"]);
  assert.match(text(o("jRecentTitle")), /أمثلة من كتابتك — أزمنة الأفعال/);
});

await step("options: review section shows deck stats; clearing the deck works", async () => {
  assert.match(text(o("rvSummary")), /في قائمة مراجعتك كلمتان/);
  assert.equal(o("cardsAuto").checked, true);
  assert.equal(o("cardsNewPerDay").value, "10");
  o("rvClear").click();
  await sleep(400); // the background's badge refresh and the page's re-render
  assert.deepEqual(local.data.cards, {});
  assert.doesNotMatch(text(o("rvSummary")), /كلمتان/, "the old counts are gone");
  assert.match(text(o("rvSummary")), /تُضاف إلى بطاقات المراجعة/, "back to the introduction");
});

await step("options: translation service saves per device; model menu and status follow the translator", async () => {
  const radio = v => o("translation").querySelector(`input[name="trService"][value="${v}"]`);
  assert.equal(radio("auto").checked, true, "Automatic by default");
  assert.equal(o("trAiBox").hidden, false);
  assert.match(text(o("trStatus")), /Ollama/, "the writing tools' Ollama model translates");
  radio("google").checked = true;
  radio("google").dispatchEvent(new opt.Event("change"));
  await sleep(50);
  assert.equal(local.data.trService, "google");
  assert.equal(o("trAiBox").hidden, true, "nothing to choose for Google only");
  radio("ai").checked = true;
  radio("ai").dispatchEvent(new opt.Event("change"));
  o("trProvider").value = "gemini";
  o("trProvider").dispatchEvent(new opt.Event("change"));
  await sleep(50);
  assert.equal(local.data.trProvider, "gemini");
  assert.deepEqual([...o("trModel").options].map(x => x.value), ["gemini-3.5-flash-lite", "gemini-3.8-flash"], "Flash-Lite first: the translation default");
  assert.match(o("trStatus").className, /bad/, "no Gemini key yet");
  o("trModel").value = "gemini-3.8-flash";
  o("trModel").dispatchEvent(new opt.Event("change"));
  o("trPages").checked = true;
  o("trPages").dispatchEvent(new opt.Event("change"));
  await sleep(50);
  assert.deepEqual(local.data.trModels, { gemini: "gemini-3.8-flash" });
  assert.equal(local.data.trPages, true);
  await local.set({ geminiKey: "AIza-test" });
  await sleep(50);
  assert.match(o("trStatus").className, /ok/, "a key saved under Writing tools is picked up at once");
  for (const k of ["trService", "trProvider", "trModels", "trPages", "geminiKey"]) delete local.data[k];
});

/* ---- the English interface ---- */
await sync.set({ uiLang: "en" });
await sleep(50);
const enPop = await openPage("popup/popup.html", ["shared/i18n.js", "shared/lamha-ai.js", "shared/motion.js", "popup/popup.js"]);
const ep = id => enPop.document.getElementById(id);

await step("English: popup left to right, English labels, Western digits", async () => {
  assert.equal(enPop.document.documentElement.dir, "ltr");
  assert.equal(enPop.document.documentElement.lang, "en");
  assert.equal(enPop.document.documentElement.hasAttribute("data-i18n-pending"), false, "page left hidden");
  assert.deepEqual([...enPop.document.querySelectorAll(".tabs button")].map(b => text(b).replace(/\s*\d+$/, "")), ["Translate", "Write ✨", "Review"]);
  assert.equal(ep("q").placeholder, "Type a word or sentence to translate…");
  assert.equal(text(ep("openOptions")), "Settings");
  assert.ok(!/[\u0600-\u06FF]/.test(text(ep("rvHead"))), "Arabic left in the review header: " + text(ep("rvHead")));
});

const enOpt = await openPage("options/options.html", ["shared/i18n.js", "options/i18n-options.js", "shared/lamha-ai.js", "shared/motion.js", "options/options.js"]);
const eo = id => enOpt.document.getElementById(id);
await sleep(200);

await step("English: settings page translated, language menu shows the choice", async () => {
  assert.equal(enOpt.document.documentElement.dir, "ltr");
  assert.equal(eo("uiLang").value, "en");
  const headings = [...enOpt.document.querySelectorAll(".panel h2")].map(text);
  assert.ok(headings.includes("Dictionary") && headings.includes("Writing tools ✨") && headings.includes("Appearance"), headings.join(" | "));
  assert.match(text(eo("jSummary")), /^You proofread 5 texts and 13 mistakes were found\. Most frequent: Articles/);
  // no visible Arabic, except the native name of Arabic in the language menu
  const visible = [...enOpt.document.querySelectorAll("h1, h2, h3, b, small, p, button, label, option, li, span, footer")]
    .filter(el => !el.children.length && /[\u0600-\u06FF]/.test(el.textContent) && el.textContent.trim() !== "العربية")
    .filter(el => !el.closest('[aria-hidden="true"]')) // decoration: the Animations preview shows a sample translation
    .map(el => el.textContent.trim().slice(0, 40));
  assert.deepEqual(visible, []);
});

await step("switching back to Arabic: a new page is right to left again", async () => {
  await sync.set({ uiLang: "ar" });
  const arPop = await openPage("popup/popup.html", ["shared/i18n.js", "shared/lamha-ai.js", "shared/motion.js", "popup/popup.js"]);
  assert.equal(arPop.document.documentElement.dir, "rtl");
  assert.equal(text(arPop.document.getElementById("openOptions")), "الإعدادات");
});

await step("Animations: data-motion follows the setting (off / subtle / full) and, on Automatic, the device hint", async () => {
  await sync.set({ motion: "auto" });
  const w = await openPage("popup/popup.html", ["shared/i18n.js", "shared/lamha-ai.js", "shared/motion.js", "popup/popup.js"]);
  const level = () => w.document.documentElement.getAttribute("data-motion");
  assert.equal(level(), "full", "Automatic on a capable device");
  await local.set({ motionHint: "subtle" }); // the desktop app on a weak PC
  assert.equal(level(), "subtle");
  await sync.set({ motion: "off" });
  assert.equal(level(), "off");
  assert.equal(w.LamhaMotion.play(w.document.body, [{ opacity: 0 }, { opacity: 1 }]), null, "off: nothing is animated");
  let settled = false;
  await w.LamhaMotion.exit(w.document.body, [{ opacity: 1 }, { opacity: 0 }]).then(() => { settled = true; });
  assert.ok(settled, "exit() settles at once when off");
  await sync.set({ motion: "full" });
  assert.equal(level(), "full", "an explicit choice wins over the hint");
  assert.ok(w.document.querySelector(".tabs .tab-ink"), "the tab highlight");
  await sync.set({ motion: "auto" });
  await local.set({ motionHint: "" });
  assert.equal(level(), "full");
});

await step("Settings: the Animations menu saves the choice", async () => {
  const o = await openPage("options/options.html", ["shared/i18n.js", "options/i18n-options.js", "shared/lamha-ai.js", "shared/motion.js", "options/options.js"]);
  await sleep(100);
  const sel = o.document.getElementById("motion");
  assert.equal(sel.value, "auto");
  sel.value = "subtle";
  sel.dispatchEvent(new o.Event("change"));
  await sleep(20);
  assert.equal(sync.data.motion, "subtle");
  assert.equal(o.document.documentElement.getAttribute("data-motion"), "subtle");
  assert.ok(o.document.getElementById("mdNote").textContent.length > 0, "the preview explains the level");
  await sync.set({ motion: "auto" });
});

/* ---- page translation: failed batches are tried again, then reported ---- */

/** A page with the real page-translator.js; Google answers from `reply(texts)`, and every wait is shortened to ≤5 ms. */
function translatorPage(reply) {
  const dom = new JSDOM("<body><p>Hello <b>world</b>.</p><p>Good morning.</p></body>", { runScripts: "outside-only", url: "https://example.com/" });
  const w = dom.window;
  const realTimeout = w.setTimeout.bind(w);
  w.setTimeout = (fn, ms) => realTimeout(fn, Math.min(ms || 0, 5));
  w.IntersectionObserver = class { constructor(cb) { this.cb = cb; } observe(el) { this.cb([{ isIntersecting: true, target: el }]); } unobserve() {} disconnect() {} };
  const calls = [];
  w.browser = { runtime: { sendMessage: async msg => { calls.push(msg.texts); return reply(msg.texts, calls.length); } } };
  w.eval(src("shared/i18n.js")); // loaded before it everywhere (manifest, desktop card)
  w.eval(src("content/page-translator.js"));
  const states = [];
  w.eval("LamhaPage").onState(s => states.push(s));
  return { w, calls, states, page: w.eval("LamhaPage") };
}
const ok = texts => ({ ok: true, data: texts.map(t => t.replace(/Hello/, "مرحبا").replace(/world/, "عالم").replace(/Good morning\./, "صباح الخير.")) });

await step("page translation: a failed batch is tried again and the page ends up translated", async () => {
  const { w, calls, states, page } = translatorPage((texts, n) => (n === 1 ? { ok: false, error: "rate_limited" } : ok(texts)));
  page.start("ar");
  await sleep(200);
  assert.ok(calls.length >= 2, "retried");
  assert.match(w.document.body.textContent, /صباح الخير/);
  const last = states[states.length - 1];
  assert.equal(last.loading, false);
  assert.equal(last.failed, false);
  page.stop();
});

await step("page translation: after the retries run out the bar is told, and 'try again' finishes the job", async () => {
  let down = true;
  const { w, calls, states, page } = translatorPage(texts => (down ? { ok: false, error: "network" } : ok(texts)));
  page.start("ar");
  await sleep(300);
  assert.equal(calls.length, 4, "the first try and 3 retries, then it stops");
  assert.deepEqual({ ...states[states.length - 1], showingOriginal: undefined }, { active: true, showingOriginal: undefined, loading: false, failed: true });
  assert.match(w.document.body.textContent, /Good morning/, "untranslated text stays as it was");
  down = false;
  page.retry();
  await sleep(200);
  assert.match(w.document.body.textContent, /صباح الخير/);
  assert.equal(states[states.length - 1].failed, false);
  page.stop();
  assert.match(w.document.body.textContent, /Good morning/, "stop restores the original");
});

await step("page translation: right-to-left targets (Persian too, not only Arabic) set the paragraph's direction", async () => {
  const { w, page } = translatorPage(ok);
  page.start("fa");
  await sleep(100);
  assert.deepEqual([...w.document.querySelectorAll("p")].map(p => p.getAttribute("dir")), ["auto", "auto"]);
  page.stop();
  assert.deepEqual([...w.document.querySelectorAll("p")].map(p => p.getAttribute("dir")), [null, null], "stop puts it back");
  const fr = translatorPage(ok);
  fr.page.start("fr");
  await sleep(100);
  assert.equal(fr.w.document.querySelector("p").getAttribute("dir"), null, "left-to-right targets leave it alone");
  fr.page.stop();
});

/* ---- the card on a web page: AI translation ---- */

/** The real content scripts on a page; the background is `reply(msg)`. The card's shadow root is opened for the test. */
async function cardPage(reply, localData) {
  const dom = new JSDOM("<body><p>Some text.</p></body>", { runScripts: "outside-only", url: "https://example.com/", pretendToBeVisual: true });
  const w = dom.window;
  const attach = w.Element.prototype.attachShadow;
  w.Element.prototype.attachShadow = function () { return (this.__root = attach.call(this, { mode: "open" })); };
  w.matchMedia = () => ({ matches: false, addEventListener() {} });
  const sent = [];
  let onMessage;
  const store = data => ({ get: async k => (k && typeof k === "object" && !Array.isArray(k) ? { ...k, ...data } : { ...data }), set: async () => {} });
  w.browser = {
    storage: { sync: store({ uiLang: "ar" }), local: store(localData), onChanged: { addListener() {} } },
    runtime: { sendMessage: async msg => { sent.push(msg); return reply(msg); }, onMessage: { addListener: f => { onMessage = f; } } }
  };
  for (const s of ["shared/i18n.js", "shared/lamha-ai.js", "shared/motion.js", "content/styles.js", "content/page-translator.js", "content/content.js"]) w.eval(src(s));
  await sleep(50);
  const root = () => w.document.querySelector("lamha-ui").__root;
  return { w, sent, root, show: text => onMessage({ type: "showLookup", external: true, text }) };
}
const trResult = (engine, extra = {}) => ({ ok: true, data: { query: "It's a piece of cake.", type: "text", src: "en", tl: "ar", translation: engine === "ai" ? "الأمر في غاية السهولة." : "إنها قطعة من الكعكة.", dict: [], definitions: [], examples: [], source: engine === "ai" ? "ai" : "online", ai: engine === "ai" ? "Gemini" : undefined, ...extra } });

await step("card: Google's translation offers 'Better translation'; the AI's answer carries its badge", async () => {
  const c = await cardPage(msg => (msg.type === "lookup" ? trResult(msg.engine) : { ok: true }), { trProvider: "gemini", geminiKeySet: true });
  c.show("It's a piece of cake.");
  await sleep(100);
  const link = [...c.root().querySelectorAll(".foot a")].find(a => /ترجمة أدق/.test(a.textContent));
  assert.ok(link, "the link is there");
  assert.match(link.title, /Gemini/);
  link.click();
  await sleep(100);
  assert.equal(c.sent[c.sent.length - 1].engine, "ai");
  assert.match(text(c.root().querySelector(".hero")), /في غاية السهولة/);
  assert.match(text(c.root().querySelector(".bar .badge")), /Gemini/);
  assert.ok(![...c.root().querySelectorAll(".foot a")].some(a => /ترجمة أدق/.test(a.textContent)), "no second 'better' on the AI's own answer");
});

await step("card: no 'Better translation' without an AI translator; an AI quota error says so and links to Settings", async () => {
  const none = await cardPage(() => trResult("google"), {});
  none.show("It's a piece of cake.");
  await sleep(100);
  assert.ok(![...none.root().querySelectorAll(".foot a")].some(a => /ترجمة أدق/.test(a.textContent)));
  const quota = await cardPage(msg => (msg.engine === "ai" ? { ok: false, error: "gemini_quota" } : trResult("google")), { trProvider: "gemini", geminiKeySet: true });
  quota.show("It's a piece of cake.");
  await sleep(100);
  [...quota.root().querySelectorAll(".foot a")].find(a => /ترجمة أدق/.test(a.textContent)).click();
  await sleep(100);
  assert.match(text(quota.root().querySelector(".err")), /انتهى الحد المجاني من Gemini/);
  assert.ok(quota.root().querySelector(".err .btn"), "a way forward: try again");
});

await step("card: العربية ⇄ English switch flips a word to the English–English dictionary and back", async () => {
  let en = false;
  const word = () => ({ ok: true, data: en
    ? { query: "resilient", type: "word", src: "en", tl: "en", mode: "en", translation: "elastic; rebounds readily", heroExample: "clean bouncy hair", contextSense: true, ar: "مَرِن", srcTranslit: "rɪˈzɪljənt", dict: [], definitions: [{ pos: "صفة", entries: [{ gloss: "recovering readily from adversity" }, { gloss: "elastic; rebounds readily", best: true }] }], examples: [], source: "local" }
    : { query: "resilient", type: "word", src: "en", tl: "ar", translation: "مَرِن", srcTranslit: "rɪˈzɪljənt", dict: [], definitions: [], examples: [], source: "local" } });
  const c = await cardPage(msg => {
    if (msg.type === "setWordDict") { en = msg.en; return { ok: true }; }
    if (msg.type === "cardToggle") return true;
    return msg.type === "lookup" ? word() : undefined;
  }, {});
  c.show("resilient");
  await sleep(100);
  const sw = () => [...c.root().querySelectorAll(".bar .dsw button")];
  assert.deepEqual(sw().map(b => [text(b), b.getAttribute("aria-pressed")]), [["العربية", "true"], ["إنجليزي", "false"]]);
  assert.match(text(c.root().querySelector(".hero")), /مَرِن/);
  sw()[1].click();
  await sleep(150);
  assert.deepEqual(c.sent.filter(m => m.type === "setWordDict").map(m => m.en), [true], "remembered through the background");
  assert.match(text(c.root().querySelector(".hero")), /elastic; rebounds readily.*clean bouncy hair/, "the definition that fits, with its example");
  assert.match(text(c.root().querySelector(".hero")), /في هذا السياق/);
  assert.equal(sw()[1].getAttribute("aria-pressed"), "true");
  assert.ok([...c.root().querySelectorAll(".foot a")].some(a => a.href.includes("/dictionary/english/resilient")), "Cambridge's English dictionary");
  c.root().querySelector(".mark").click();
  await sleep(50);
  assert.deepEqual(JSON.parse(JSON.stringify(c.sent.find(m => m.type === "cardToggle").card)), { q: "resilient", tr: "مَرِن", def: "elastic; rebounds readily", en: true, form: "resilient", ex: "" });
});

await step("popup review: an English–English card shows its definition first and the Arabic under it", async () => {
  await local.set({ popupMode: "review", cards: { resilient: card("resilient", "مَرِن", { def: "elastic; rebounds readily", en: true, added: now }) } });
  const p = await openPage("popup/popup.html", ["shared/i18n.js", "shared/lamha-ai.js", "shared/motion.js", "popup/popup.js"]);
  await sleep(100);
  key(p, " ");
  await sleep(50);
  const back = p.document.querySelector(".rv-back");
  assert.equal(text(back.querySelector(".rv-tr.en")), "elastic; rebounds readily");
  assert.equal(text(back.querySelector(".rv-def")), "مَرِن");
});

console.log(results.join("\n"));
const failed = results.filter(r => r.includes("✗")).length;
console.log(`\n${results.length - failed}/${results.length} passed`);
process.exit(failed ? 1 : 0);
