// UI test. Needs jsdom (a devDependency at the repo root: npm install), then  node tools/test-ui.mjs
// Real popup.html/options.html + popup.js/options.js in jsdom, wired to the real background.js
// (in a VM) through a shared fake storage. Fake network: Ollama answers the proofread.
import { readFileSync } from "node:fs";
import { gzipSync } from "node:zlib";
import vm from "node:vm";
import assert from "node:assert/strict";
import { JSDOM, VirtualConsole } from "jsdom";
import { pathToFileURL } from "node:url";
import { quiet, only, report } from "./test-args.mjs";

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
const packFiles = {}; // what the language-pack release serves: { "tr.json.gz": Buffer }
const packParts = new Map(); // downloaded packs, in memory (desktop/pack-store.js keeps them in files)
// Firefox's link to Lamha for Windows: the permission, and the app answering hello (Settings → لمحة لـ Windows)
const fxApp = { granted: false, asked: [] };
const fxPort = () => {
  const onMsg = [];
  return {
    onMessage: { addListener: f => onMsg.push(f) }, onDisconnect: { addListener() {} }, disconnect() {},
    postMessage: m => { fxApp.asked.push([m.type, m.launch]); setTimeout(() => onMsg.forEach(f => f({ id: m.id, ok: true, data: { app: "lamha", version: "1.10.0", wiki: { langs: ["ar"] } } })), 0); }
  };
};
const bgCtx = vm.createContext({
  browser: {
    storage: { local, sync, onChanged },
    runtime: { onMessage: { addListener: f => { bgHandler = f; } }, onInstalled: ev, onStartup: ev, connectNative: fxPort },
    permissions: { contains: async q => !(q.permissions || []).includes("nativeMessaging") || fxApp.granted },
    menus: { removeAll: async () => {}, create() {}, onClicked: ev }, commands: { onCommand: ev }
  },
  fetch: async (url, init) => {
    const pack = /\/releases\/download\/packs-v1\/(\w+\.json\.gz)$/.exec(url);
    if (pack) return packFiles[pack[1]] ? new Response(packFiles[pack[1]], { headers: { "content-length": String(packFiles[pack[1]].length) } }) : json(404, {});
    aiCalls.push(url);
    if (url.endsWith("/api/chat")) {
      return json(200, { done_reason: "stop", message: { content: JSON.stringify({
        corrected: "I went to the market.",
        issues: [{ original: "go", fix: "went", category: "verb_tense", why: "الماضي." }, { original: "market", fix: "the market", category: "articles", why: "مكان معروف." }]
      }) } });
    }
    return json(404, {});
  },
  setTimeout, clearTimeout, AbortController, URLSearchParams, structuredClone, console, LocalDict: {}, Audio: class { play() { return Promise.resolve(); } },
  Response, TransformStream, DecompressionStream,
  LamhaPackStore: {
    get: async k => structuredClone(packParts.get(k)),
    setMany: async o => { for (const [k, v] of Object.entries(o)) packParts.set(k, structuredClone(v)); },
    removePrefix: async p => { for (const k of [...packParts.keys()]) if (k.startsWith(p)) packParts.delete(k); }
  }
});
vm.runInContext(src("packs.js"), bgCtx);
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
      getURL: p => "moz-extension://lamha/" + p, getManifest: () => ({ version: "test" }), openOptionsPage() {},
      ...(extra.native ? { connectNative() {} } : {}) // Firefox on a computer (not Android, not the Windows app)
    },
    tabs: { query: async () => [{ id: 1, url: "https://example.com/" }], sendMessage: async () => ({ active: false }), create: async () => {} },
    permissions: {
      contains: async () => true,
      request: async q => { if ((q.permissions || []).includes("nativeMessaging")) fxApp.granted = true; return true; },
      remove: async q => { if ((q.permissions || []).includes("nativeMessaging")) fxApp.granted = false; return true; }
    },
    commands: { getAll: async () => [] }
  };
  w.HTMLElement.prototype.scrollIntoView = () => {};
  if (src(path).includes("../shared/dialog.js")) w.eval(src("shared/dialog.js")); // the page's own questions (no confirm())
  for (const s of scripts) w.eval(src(s));
  await sleep(150);
  return w;
}
const key = (w, k) => w.document.dispatchEvent(new w.KeyboardEvent("keydown", { key: k, bubbles: true }));
const text = el => el.textContent.replace(/\s+/g, " ").trim();

const results = [];
// Steps share the pages opened above and build on each other, so this file always runs whole (no name filter).
if (only) console.log(`test-ui runs all its steps: they depend on each other (filter "${only}" ignored)`);
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
  assert.equal($("rvProgress").hidden, false, "a progress bar once the sitting has begun");
  assert.equal($("rvProgress").getAttribute("aria-valuenow"), "33", "1 answered, 2 to go");
});

await step("remove from review deletes the card", async () => {
  key(pop, " "); await sleep(20);
  [...$("rvCard").querySelectorAll("button.link")].find(b => /إزالة/.test(b.textContent)).click();
  await sleep(100);
  assert.equal(local.data.cards.resilient, undefined);
  assert.equal(text($("rvCard").querySelector(".rv-word")), "thrive");
  assert.equal($("rvProgress").getAttribute("aria-valuenow"), "50", "a removed card isn't an answer");
});

await step("finishing the queue shows the done message with next review time", async () => {
  key(pop, " "); await sleep(20);
  key(pop, "1"); // again → 10 minutes
  await sleep(100);
  assert.match(text($("rvCard")), /أحسنت! لا توجد كلمات للمراجعة الآن/);
  assert.match(text($("rvCard")), /المراجعة القادمة بعد ١٠ د/);
  assert.match(text($("rvCard").querySelector(".rv-sum")), /^راجعت كلمتين · عرفت ١ · صعبة ٠ · نسيت ١$/, "what this sitting did");
  assert.equal($("rvProgress").hidden, true);
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
  assert.deepEqual([...$("wrTools").querySelectorAll("button")].map(text), ["تدقيق لغوي", "تحسين", "رسمي", "ودّي", "أقصر", "أطول"]);
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

await step("today: the goal ring counts the review answers; the word of the day is one of yours that's due; a query hides it", async () => {
  $("tabTr").click();
  await sleep(100);
  const card = $("todayCard");
  assert.equal(card.hidden, false);
  assert.equal(text(card.querySelector(".td-n")), "٢", "two answers so far");
  assert.equal(text(card.querySelector(".td-text b")), "٢ من ١٠ اليوم");
  assert.equal(card.querySelector(".td-ring").getAttribute("aria-label"), "٢ من ١٠");
  assert.equal(text(card.querySelector(".td-label")), "كلمة اليوم · من كلماتك");
  assert.equal(text(card.querySelector(".td-w")), "bank", "chosen when the popup opened (then overdue), kept all day");
  assert.equal(text(card.querySelector(".td-tr")), "ضفة");
  $("q").value = "hello";
  $("q").dispatchEvent(new pop.Event("input"));
  assert.equal(card.hidden, true, "steps aside for a result");
  $("q").value = "";
  $("q").dispatchEvent(new pop.Event("input"));
  assert.equal(card.hidden, false);
});

await step("recent lookups follow words looked up elsewhere (the card) while the window stays open", async () => {
  await local.set({ history: [{ q: "candid", tr: "صريح", src: "en", t: now }] });
  await sleep(50);
  assert.equal($("histCard").hidden, false);
  assert.match(text($("hist")), /candid\s*صريح/);
  await local.set({ history: [] });
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
  const dlg = opt.document.querySelector("dialog.dlg");
  assert.ok(dlg, "Lamha's own question, not confirm()");
  assert.equal(text(dlg.querySelector("h2")), "حذف كل بطاقات المراجعة وتقدّمك في حفظها؟");
  const [del, cancel] = dlg.querySelectorAll("button");
  assert.deepEqual([text(del), text(cancel)], ["حذف", "إلغاء"]);
  assert.ok(cancel.autofocus && !del.autofocus, "the keyboard starts on Cancel: Enter can't delete by accident");
  cancel.click();
  await sleep(50);
  assert.equal(Object.keys(local.data.cards).length, 2, "Cancel keeps the cards");
  assert.equal(opt.document.querySelector("dialog.dlg"), null, "and the question is gone");
  o("rvClear").click();
  opt.document.querySelector("dialog.dlg .danger-solid").click();
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
  assert.deepEqual([...enPop.document.querySelectorAll(".tabs button")].map(b => text(b).replace(/\s*\d+$/, "")), ["Translate", "Write", "Review"]);
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
  // in the order users need them: languages first, how Lamha is called, what a lookup shows, the AI, then the rest
  assert.deepEqual(headings.slice(1, -1), ["Languages", "How it appears", "Keyboard shortcuts", "Excluded websites", "The lookup card", "Writing tools",
    "Translation", "Word review", "My mistake journal", "Without internet", "Appearance", "Privacy & history", "Lamha for Windows"], headings.join(" | "));
  assert.ok(eo("languages").contains(eo("uiLang")) && eo("languages").contains(eo("targetLang")) && eo("languages").contains(eo("reverseForArabic")), "the languages together");
  assert.ok(["useContext", "explainLangs", "translateDefinitions", "showWikipedia", "autoSpeak", "showInInputs"].every(id => eo("card").contains(eo(id))), "what a lookup shows, together");
  assert.ok(eo("dictionary").contains(eo("packs")), "the dictionaries to download, with the offline choice");
  const tocLinks = [...enOpt.document.querySelectorAll("#toc a")];
  const journalLink = tocLinks.find(a => a.hash === "#journal");
  enOpt.requestAnimationFrame = f => setTimeout(f, 16); // jsdom has none; the highlight uses it once
  journalLink.click(); // a clicked link is marked at once, and stays marked while the page scrolls there
  assert.equal(journalLink.getAttribute("aria-current"), "true");
  assert.equal(tocLinks.filter(a => a.getAttribute("aria-current") === "true").length, 1);
  const links = tocLinks.map(text);
  assert.ok(links.includes("Privacy & history") && links.includes("Writing tools"), "section links keep their punctuation: " + links.join(" | "));
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
async function cardPage(reply, localData, syncData = {}, url = "https://example.com/") {
  const dom = new JSDOM("<body><p>Some text.</p></body>", { runScripts: "outside-only", url, pretendToBeVisual: true });
  const w = dom.window;
  const attach = w.Element.prototype.attachShadow;
  w.Element.prototype.attachShadow = function () { return (this.__root = attach.call(this, { mode: "open" })); };
  // jsdom marks every dispatched event untrusted, as a browser does a page script's: here they stand for the user's own
  // mouse and keys, except the ones a test marks `byPage` (a page faking them)
  const listen = w.EventTarget.prototype.addEventListener;
  w.EventTarget.prototype.addEventListener = function (type, fn, opts) {
    if (typeof fn !== "function") return listen.call(this, type, fn, opts);
    return listen.call(this, type, function (e) {
      const trusted = !e.byPage;
      return fn.call(this, new Proxy(e, { get: (t, k) => (k === "isTrusted" ? trusted : typeof t[k] === "function" ? t[k].bind(t) : t[k]) }));
    }, opts);
  };
  w.matchMedia = () => ({ matches: false, addEventListener() {} });
  const sent = [];
  let onMessage;
  const store = data => ({ get: async k => (k && typeof k === "object" && !Array.isArray(k) ? { ...k, ...data } : { ...data }), set: async () => {} });
  w.browser = {
    storage: { sync: store({ uiLang: "ar", ...syncData }), local: store(localData), onChanged: { addListener() {} } },
    runtime: { sendMessage: async msg => { sent.push(msg); return reply(msg); }, onMessage: { addListener: f => { onMessage = f; } }, getURL: p => "moz-extension://x/" + p }
  };
  for (const s of ["shared/i18n.js", "shared/lamha-ai.js", "shared/motion.js", "content/styles.js", "content/page-translator.js", "content/content.js"]) w.eval(src(s));
  await sleep(50);
  const root = () => w.document.querySelector("lamha-ui").__root;
  return { w, sent, root, message: msg => onMessage(msg), show: text => onMessage({ type: "showLookup", external: true, text }), write: text => onMessage({ type: "showWrite", external: true, text }) };
}
const trResult = (engine, extra = {}) => ({ ok: true, data: { query: "It's a piece of cake.", type: "text", src: "en", tl: "ar", translation: engine === "ai" ? "الأمر في غاية السهولة." : "إنها قطعة من الكعكة.", dict: [], definitions: [], examples: [], source: engine === "ai" ? "ai" : "online", ai: engine === "ai" ? "Gemini" : undefined, ...extra } });

await step("card: a meaning chip opens that word, Arabic meanings too (their English meanings), and back returns", async () => {
  const words = {
    experience: { query: "experience", type: "word", src: "en", tl: "ar", translation: "خِبْرَة", dict: [{ pos: "اسم", terms: [{ word: "خِبْرَة" }, { word: "تَجْرِبَة" }] }], definitions: [], examples: [], source: "local" },
    "تَجْرِبَة": { query: "تَجْرِبَة", type: "word", src: "ar", tl: "en", translation: "experience", dict: [{ pos: "اسم", terms: [{ word: "experiment" }, { word: "test" }] }], definitions: [], examples: [], source: "local" },
    experiment: { query: "experiment", type: "word", src: "en", tl: "ar", translation: "تَجْرِبَة", dict: [], definitions: [], examples: [], source: "local" }
  };
  const c = await cardPage(msg => (msg.type === "lookup" ? { ok: true, data: words[msg.text] } : { ok: true }), {});
  c.show("experience");
  await sleep(100);
  const chip = name => [...c.root().querySelectorAll(".chips .chip")].find(b => text(b) === name);
  chip("تَجْرِبَة").click(); // it only copied the word before
  await sleep(100);
  assert.equal(c.sent.filter(m => m.type === "lookup").pop().text, "تَجْرِبَة");
  assert.match(text(c.root().querySelector(".hero")), /experience/, "its English meanings");
  chip("experiment").click(); // and on, the other way
  await sleep(100);
  assert.equal(c.sent.filter(m => m.type === "lookup").pop().text, "experiment");
  c.root().querySelector('.bar .icon-btn[aria-label="رجوع"]').click();
  await sleep(100);
  assert.equal(c.sent.filter(m => m.type === "lookup").pop().text, "تَجْرِبَة", "back to the Arabic word");
});

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
  const badge = c.root().querySelector(".bar .badge.ai");
  assert.equal(text(badge), "", "just the icon in the bar");
  assert.match(badge.title, /Gemini/, "who translated, on hover");
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

await step("card: copy turns its icon into a check mark for a moment; a goal reached shows its banner", async () => {
  const c = await cardPage(msg => (msg.type === "lookup" ? trResult("google", { goal: 10 }) : { ok: true }));
  let copied = "";
  Object.defineProperty(c.w.navigator, "clipboard", { value: { writeText: async t => { copied = t; } } });
  c.show("It's a piece of cake.");
  await sleep(100);
  assert.match(text(c.root().querySelector(".milestone")), /حققت هدف اليوم: ١٠/);
  const btn = [...c.root().querySelectorAll(".hero .actions .icon-btn")].find(b => /نسخ/.test(b.getAttribute("aria-label")));
  btn.click();
  await sleep(20);
  assert.equal(copied, "إنها قطعة من الكعكة.");
  assert.ok(btn.classList.contains("copied") && btn.querySelector('path[d="M20 6 9 17l-5-5"]'), "a check mark");
});

await step("card: selected English text gets Longer right after Shorter; its number key runs it", async () => {
  const c = await cardPage(msg => (msg.type === "ai" ? { ok: true, data: { text: "A longer version." } } : { ok: true }), { aiProvider: "ollama", ollamaModel: "qwen3.5:4b" });
  c.write("cant make sunday meeting");
  await sleep(50);
  const chips = [...c.root().querySelectorAll(".chip.tool")];
  assert.deepEqual(chips.map(ch => ch.dataset.tool), ["proofread", "improve", "formal", "friendly", "concise", "expand", "summarize", "explain", "reply"]);
  assert.match(text(chips[5]), /^٦\s*أطول$/);
  c.root().querySelector(".card").dispatchEvent(new c.w.KeyboardEvent("keydown", { key: "6", bubbles: true }));
  await sleep(50);
  assert.equal(c.sent.find(m => m.type === "ai").tool, "expand");
});

await step("card: while it waits, the Lamha mark turns pages (a word), reads lines (a sentence) or writes (the AI); never for a quick answer or with animations off", async () => {
  // conditions are waited for, not timed: a slow machine (CI) may run a 100 ms sleep for much longer
  const later = (ms, value) => new Promise(done => setTimeout(() => done(value), ms));
  const until = async (ok, what, ms = 3000) => { for (const end = Date.now() + ms; !ok(); await sleep(20)) if (Date.now() > end) assert.fail(what); };
  const SLOW = 1200;
  const word = { ok: true, data: { query: "bank", type: "word", src: "en", tl: "ar", translation: "بنك", source: "local" } };
  const reply = msg => msg.type === "lookup" ? later(/slow/.test(msg.text) || msg.text.includes(" ") ? SLOW : 0, word)
    : msg.type === "ai" ? later(SLOW, { ok: true, data: { text: "Fine." } }) : { ok: true };
  const c = await cardPage(reply, { aiProvider: "ollama", ollamaModel: "qwen3.5:4b" });
  const mark = () => c.root().querySelector(".brand .dot .wait");
  const is = kind => () => !!mark() && mark().classList.contains(kind);
  c.show("slow");
  await sleep(0);
  assert.equal(mark(), null, "not at once: a quick answer doesn't flicker");
  await until(is("book"), "a word: the book");
  assert.ok(c.root().querySelector(".brand .dot").classList.contains("waiting"), "the logo gives way to it");
  await until(() => !mark(), "gone with the answer");
  c.show("The bank is closed today.");
  await until(is("lens"), "a sentence: the lens");
  await until(() => !mark(), "gone with the answer");
  c.show("bank");
  await sleep(500);
  assert.equal(mark(), null, "an answer from the dictionary never shows it");
  c.write("cant make sunday meeting");
  await until(() => c.root().querySelector(".chip.tool"), "the writing tools");
  c.root().querySelector(".chip.tool").click();
  await until(is("write"), "the AI: lines are written");
  assert.equal(mark().querySelectorAll(".w")[2].getAttribute("d"), "M8 19.5h12", "Arabic: the short last line ends on the right");
  await until(() => !mark(), "gone with the answer");
  const en = await cardPage(reply, { aiProvider: "ollama", ollamaModel: "qwen3.5:4b" }, { uiLang: "en" });
  en.write("cant make sunday meeting");
  await until(() => en.root().querySelector(".chip.tool"), "the writing tools (English)");
  en.root().querySelector(".chip.tool").click();
  const enMark = () => en.root().querySelector(".brand .dot .wait.write");
  await until(enMark, "the AI, in English");
  assert.equal(enMark().querySelectorAll(".w")[2].getAttribute("d"), "M4 19.5h12", "English: it starts on the left, where it's written from");
  // the 2nd and 3rd lines start late: until then they stay unwritten (they showed whole, then vanished and began)
  for (const css of [src("content/styles.js"), src("shared/motion.css")]) assert.match(css, /\.wait \.w \{ animation: lm-write [^}]* backwards; \}/);

  const off = await cardPage(reply, {}, { motion: "off" });
  off.show("slow");
  await sleep(600);
  assert.equal(off.root().querySelector(".brand .dot .wait"), null, "Animations off: nothing is made");
});

await step("card: Write new offers Longer next to Short, for the message it writes", async () => {
  const c = await cardPage(msg => (msg.type === "ai" ? { ok: true, data: { text: "Hi! I can't make it on Sunday." } } : { ok: true }), { aiProvider: "ollama", ollamaModel: "qwen3.5:4b" });
  c.write(""); // nothing selected: Write new
  await sleep(50);
  const rows = [...c.root().querySelectorAll(".w-out .tools")];
  const tones = [...rows[1].querySelectorAll(".chip")];
  assert.deepEqual(tones.map(text), ["تلقائي", "ودّي", "رسمي", "مختصر", "مفصّل"]);
  tones[4].click();
  c.root().querySelector(".w-input").value = "أعتذر عن اجتماع الأحد";
  c.root().querySelector(".w-out > .btn").click();
  await sleep(50);
  const ai = c.sent.find(m => m.type === "ai");
  assert.equal(ai.tool, "compose");
  assert.equal(ai.extra.tone, "long");
});

/** A text box on the card test page, focused, then double-clicked like a user would (pointerup, then dblclick). */
async function dblClickIn(c, html) {
  const box = c.w.document.createRange().createContextualFragment(html).firstChild;
  c.w.document.body.append(box);
  box.focus();
  const at = { bubbles: true, composed: true, clientX: 60, clientY: 200, button: 0 };
  box.dispatchEvent(new c.w.MouseEvent("pointerup", at));
  box.dispatchEvent(new c.w.MouseEvent("dblclick", at));
  await sleep(40); // past the pointerup's own check, which must leave this button alone
  return box;
}
const writerAI = { aiProvider: "ollama", ollamaModel: "qwen3.5:4b" };
const pillOn = c => c.w.document.querySelector("lamha-ui")?.__root.querySelector(".pill") ?? null; // null before Lamha drew anything

await step("a page's own script can't fake the user: a scripted mouse-up (instant mode) or double-click opens nothing", async () => {
  const c = await cardPage(msg => (msg.type === "lookup" ? trResult("google") : { ok: true }), writerAI, { triggerMode: "instant" });
  const p = c.w.document.querySelector("p");
  c.w.Range.prototype.getClientRects = () => []; // jsdom doesn't lay text out
  c.w.Range.prototype.getBoundingClientRect = () => new c.w.DOMRect(20, 10, 80, 18);
  c.w.getSelection().selectAllChildren(p); // a script may select text too
  const fire = (el, type, byPage) => { const ev = new c.w.MouseEvent(type, { bubbles: true, composed: true, clientX: 40, clientY: 20, button: 0 }); ev.byPage = byPage; el.dispatchEvent(ev); };
  fire(p, "pointerup", true);
  await sleep(60);
  assert.equal(c.sent.filter(m => m.type === "lookup").length, 0, "the page's mouse-up sent nothing to the translator");
  fire(p, "pointerup", false);
  await sleep(60);
  assert.equal(c.sent.filter(m => m.type === "lookup").length, 1, "the user's own opens the card");
  c.w.document.dispatchEvent(new c.w.KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
  const box = c.w.document.createElement("textarea");
  c.w.document.body.append(box);
  box.focus();
  fire(box, "dblclick", true);
  await sleep(40);
  assert.equal(pillOn(c), null, "no Write button for a scripted double-click");
});

await step("double-click in an empty text box: only the Write button, a one-time tip, and Write new inserts into that box", async () => {
  const c = await cardPage(msg => (msg.type === "ai" ? { ok: true, data: { text: "See you on Sunday!" } } : { ok: true }), writerAI);
  const box = await dblClickIn(c, "<textarea></textarea>");
  const pill = c.root().querySelector(".pill");
  assert.ok(pill, "the button shows");
  const btns = [...pill.querySelectorAll(".pill-btn")];
  assert.equal(btns.length, 1, "no lookup button: there's nothing to look up");
  assert.equal(btns[0].getAttribute("aria-label"), "اكتب نصًّا جديدًا في هذا المربع", "says what it does");
  assert.match(text(pill.querySelector(".pill-tip")), /نقرتان على مربع فارغ/);
  assert.ok(c.sent.some(m => m.type === "writeTipSeen"), "the tip is remembered as seen");
  btns[0].click();
  await sleep(50);
  const kinds = [...c.root().querySelectorAll(".w-out .tools")][0];
  assert.equal(text(kinds.querySelector(".chip.on")), "رسالة", "a message outside webmail");
  c.root().querySelector(".w-input").value = "confirm Sunday";
  c.root().querySelector(".w-out > .btn").click();
  await sleep(50);
  assert.equal(c.sent.find(m => m.type === "ai").tool, "compose");
  const insert = [...c.root().querySelectorAll(".w-actions .btn")].find(b => /إدراج/.test(b.textContent));
  assert.ok(insert, "Insert, because the box is known");
  c.w.document.execCommand = () => false; // jsdom has none; browsers do (and keep the box's undo history)
  insert.click();
  await sleep(250);
  assert.equal(box.value, "See you on Sunday!", "inserted into the box: " + JSON.stringify(box.value));
  // the next double-click: no tip any more
  box.value = "";
  await dblClickIn(c, "<textarea></textarea>");
  assert.ok(c.root().querySelector(".pill") && !c.root().querySelector(".pill-tip"));
});

await step("double-click: nothing for a box with text, a password, a read-only box, the setting off or no AI; typing hides it", async () => {
  const c = await cardPage(() => ({ ok: true }), { ...writerAI, writeTipSeen: true });
  for (const html of ["<textarea>Hello</textarea>", '<input type="password">', "<textarea readonly></textarea>", '<input type="email">']) {
    await dblClickIn(c, html);
    assert.equal(pillOn(c), null, html);
  }
  await dblClickIn(c, '<input type="text">');
  assert.ok(pillOn(c), "an empty one-line box works");
  c.w.document.dispatchEvent(new c.w.KeyboardEvent("keydown", { key: "H", bubbles: true }));
  assert.equal(pillOn(c), null, "typing hides it");
  const noAI = await cardPage(() => ({ ok: true }), {});
  await dblClickIn(noAI, "<textarea></textarea>");
  assert.equal(pillOn(noAI), null, "no writing tools set up: nothing");
  const off = await cardPage(() => ({ ok: true }), writerAI, { writeOnDblClick: false });
  await dblClickIn(off, "<textarea></textarea>");
  assert.equal(pillOn(off), null, "the setting is off");
});

await step("double-click in webmail: Write new starts as an email", async () => {
  const c = await cardPage(() => ({ ok: true }), { ...writerAI, writeTipSeen: true }, {}, "https://mail.google.com/mail/u/0/");
  await dblClickIn(c, "<textarea></textarea>");
  c.root().querySelector(".pill-btn").click();
  await sleep(50);
  const kinds = [...c.root().querySelectorAll(".w-out .tools")][0];
  assert.equal(text(kinds.querySelector(".chip.on")), "بريد إلكتروني");
});

await step("desktop card: the writing shortcut with nothing selected opens Write new, as a message, and Insert goes back to the app", async () => {
  const c = await cardPage(msg => (msg.type === "ai" ? { ok: true, data: { text: "Dear team, …" } } : { ok: true }), writerAI);
  const pasted = [];
  c.w.lamhaDesktop = { replace: text => { pasted.push(text); return true; }, closed() {} };
  await c.message({ type: "showWrite", external: true, replaceable: true, point: { x: 240, y: 72 } });
  await sleep(50);
  assert.equal(text(c.root().querySelector(".w-out .tools .chip.on")), "رسالة", "another program: a message (the webmail rule is for web pages)");
  c.root().querySelector(".w-input").value = "tell the team the meeting moved";
  c.root().querySelector(".w-out > .btn").click();
  await sleep(50);
  [...c.root().querySelectorAll(".w-actions .btn")].find(b => /إدراج/.test(b.textContent)).click();
  await sleep(250);
  assert.deepEqual(pasted, ["Dear team, …"]);
});

await step("card: no stray 'false' text in a sentence's loading state or in the finished page bar", async () => {
  const c = await cardPage(msg => (msg.type === "lookup" ? new Promise(() => {}) : msg.type === "translateBatch" ? ok(msg.texts) : { ok: true }));
  c.show("The quick brown fox jumps over the lazy dog."); // a sentence: its skeleton has no word lines
  await sleep(50);
  assert.ok(c.root().querySelector(".card .sk"), "the skeleton shows");
  assert.doesNotMatch(c.root().querySelector(".card").textContent, /false/);
  c.w.IntersectionObserver = class { constructor(cb) { this.cb = cb; } observe(el) { this.cb([{ isIntersecting: true, target: el }]); } unobserve() {} disconnect() {} };
  c.w.eval("LamhaPage").start("ar");
  await sleep(150);
  const bar = c.root().querySelector(".pbar");
  assert.ok(bar && !bar.querySelector(".spin"), "the page is translated");
  assert.doesNotMatch(bar.textContent, /false/);
});

await step("card: العربية ⇄ English switch flips a word to the English–English dictionary and back", async () => {
  let en = false;
  const word = () => ({ ok: true, data: en
    ? { query: "resilient", type: "word", src: "en", tl: "en", other: "ar", mode: "explain", translation: "elastic; rebounds readily", heroExample: "clean bouncy hair", contextSense: true, heroPos: "صفة", ar: "مَرِن", srcTranslit: "rɪˈzɪljənt", dict: [], definitions: [{ pos: "صفة", entries: [{ gloss: "recovering readily from adversity" }, { gloss: "elastic; rebounds readily", best: true }] }], examples: [], source: "local" }
    : { query: "resilient", type: "word", src: "en", tl: "ar", translation: "مَرِن", srcTranslit: "rɪˈzɪljənt", dict: [], definitions: [], examples: [], source: "local" } });
  const c = await cardPage(msg => {
    if (msg.type === "setWordDict") { en = msg.lang === "en" && msg.on; return { ok: true }; }
    if (msg.type === "cardToggle") return true;
    if (msg.type !== "lookup") return undefined;
    return en ? new Promise(done => setTimeout(() => done(word()), 80)) : word(); // the switched view takes a moment
  }, {});
  c.show("resilient");
  await sleep(100);
  const sw = () => [...c.root().querySelectorAll(".bar .dsw button")];
  assert.deepEqual(sw().map(b => [text(b), b.getAttribute("aria-pressed")]), [["العربية", "true"], ["إنجليزي", "false"]]);
  const local = c.root().querySelector(".bar .badge.local");
  assert.ok(local && text(local) === "" && /قاموس محلي/.test(local.title), "offline dictionary: an icon, named on hover");
  assert.equal(text(c.root().querySelector(".bar .brand")), "", "with the switch, the logo alone");
  assert.match(text(c.root().querySelector(".hero")), /مَرِن/);
  sw()[1].click();
  await sleep(20);
  assert.match(text(c.root().querySelector(".hero")), /مَرِن/, "while the English view comes, the Arabic one stays (the card doesn't shrink to its bar)");
  assert.equal(c.root().querySelector(".card .sk"), null, "no placeholder in between");
  assert.equal(c.root().querySelector(".card").getAttribute("aria-busy"), "true", "dimmed while it waits");
  await sleep(150);
  assert.equal(c.root().querySelector(".card").getAttribute("aria-busy"), null);
  assert.deepEqual(c.sent.filter(m => m.type === "setWordDict").map(m => [m.lang, m.on]), [["en", true]], "remembered through the background, for English words");
  assert.match(text(c.root().querySelector(".hero")), /elastic; rebounds readily.*clean bouncy hair/, "the definition that fits, with its example");
  assert.match(text(c.root().querySelector(".hero .ctx-label")), /^صفة · في هذا السياق$/, "part of speech, then 'in this context'");
  assert.deepEqual([...c.root().querySelectorAll(".def .en-g")].map(text), ["recovering readily from adversity"], "the definition above isn't repeated below");
  assert.equal(sw()[1].getAttribute("aria-pressed"), "true");
  assert.ok([...c.root().querySelectorAll(".foot a")].some(a => a.href.includes("/dictionary/english/resilient")), "Cambridge's English dictionary");
  c.root().querySelector(".mark").click();
  await sleep(50);
  assert.deepEqual(JSON.parse(JSON.stringify(c.sent.find(m => m.type === "cardToggle").card)), { q: "resilient", tr: "مَرِن", def: "elastic; rebounds readily", en: true, form: "resilient", ex: "" });
});

await step("card: an Arabic word explained in Arabic reads right to left, with its root and plural; the switch goes back to English", async () => {
  let explain = true;
  const word = () => ({ ok: true, data: explain
    ? { query: "كتاب", type: "word", src: "ar", tl: "ar", other: "en", mode: "explain", translation: "مجموعة أوراق مطبوعة ومجلّدة.", heroExample: "قرأتُ كتابًا.", heroPos: "اسم", ar: "book", root: "ك ت ب", plural: "كُتُب",
      dict: [], examples: [], source: "ai", ai: "Ollama", definitions: [{ pos: "اسم", entries: [{ gloss: "مجموعة أوراق مطبوعة ومجلّدة." }, { gloss: "رسالة مكتوبة.", example: "وصلني كتابك.", synonyms: ["رسالة"] }] }] }
    : { query: "كتاب", type: "word", src: "ar", tl: "en", translation: "book", dict: [], definitions: [], examples: [], source: "local" } });
  const c = await cardPage(msg => {
    if (msg.type === "setWordDict") { explain = msg.on; return { ok: true }; }
    return msg.type === "lookup" ? word() : undefined;
  }, {});
  c.show("كتاب");
  await sleep(100);
  const sw = () => [...c.root().querySelectorAll(".bar .dsw button")];
  assert.deepEqual(sw().map(b => [text(b), b.getAttribute("aria-pressed")]), [["إنجليزي", "false"], ["العربية", "true"]]);
  assert.equal(c.root().querySelector(".hero .t").getAttribute("dir"), "rtl");
  assert.equal(text(c.root().querySelector(".hero .roots")), "الجذر: ك ت ب · الجمع: كُتُب");
  const def = c.root().querySelector(".def");
  assert.equal(def.querySelector(".ar-g").getAttribute("dir"), "rtl", "the definition reads right to left");
  assert.equal(def.querySelector(".ex").getAttribute("dir"), "rtl");
  assert.equal(def.querySelector(".syn").getAttribute("dir"), "rtl");
  assert.ok(!def.querySelector(".syn .chip.en"), "Arabic synonyms aren't styled as English");
  sw()[0].click();
  await sleep(150);
  assert.deepEqual(c.sent.filter(m => m.type === "setWordDict").map(m => [m.lang, m.on]), [["ar", false]], "remembered for Arabic words only");
  assert.match(text(c.root().querySelector(".hero")), /book/);
});

await step("card: a foreign word that should be explained but can't be shows the translation and says why", async () => {
  const c = await cardPage(msg => (msg.type === "lookup" ? { ok: true, data: { query: "maison", type: "word", src: "fr", tl: "ar", translation: "منزل", explainMissing: true, dict: [], definitions: [], examples: [], source: "online" } } : undefined), {});
  c.show("maison");
  await sleep(100);
  assert.match(text(c.root().querySelector(".hero")), /منزل/);
  assert.match(text(c.root().querySelector(".w-note")), /لا يوجد شرح لهذه الكلمة باللغة الفرنسية/);
  assert.equal([...c.root().querySelectorAll(".bar .dsw button")].find(b => b.getAttribute("aria-pressed") === "true").textContent, "الفرنسية", "the switch shows the choice");
});

await step("card: Wikipedia from the downloaded copy says so; the card sends the word's translations as titles, and asks on 'offline only' too", async () => {
  const c = await cardPage(msg => (msg.type === "lookup"
    ? { ok: true, data: { query: "Paris", type: "word", src: "en", tl: "ar", translation: "باريس، بارِس", dict: [{ pos: "اسم", terms: [{ word: "عاصمة فرنسا" }] }], definitions: [], examples: [], source: "online" } }
    : msg.type === "wiki" ? { ok: true, data: { lang: "ar", title: "باريس", extract: "باريس عاصمة فرنسا.", url: "", thumb: "", offline: { date: "2026-07-10", id: "x", path: "باريس" } } }
      : undefined), {}, { dictSource: "offline" });
  c.show("Paris");
  await sleep(150);
  const asked = c.sent.find(m => m.type === "wiki");
  assert.ok(asked, "offline only still asks: only a downloaded copy may answer");
  assert.deepEqual(JSON.parse(JSON.stringify({ alt: asked.alt, offline: asked.offline })), { alt: ["باريس", "بارِس", "عاصمة فرنسا"], offline: true });
  const chip = c.root().querySelector(".sec-h .wiki-offline");
  assert.ok(chip, "marked as the downloaded copy");
  assert.match(text(chip), /^من النسخة المنزّلة · يوليو/);
  assert.match(text(c.root().querySelector(".wiki .x")), /باريس عاصمة فرنسا/);
  const more = c.root().querySelector(".wiki a");
  assert.equal(text(more), "اقرأ المقالة في لمحة", "the article opens in Lamha's own reader: no internet needed");
  more.click();
  assert.deepEqual(JSON.parse(JSON.stringify(c.sent.at(-1))), { type: "wikiOpen", file: "x", path: "باريس" }, "the app's reader (from Firefox: through the app)");
});

await step("Settings → Lamha for Windows (Firefox on a computer): Connect asks for the permission and reaches the app; Disconnect gives it back", async () => {
  const scripts = ["shared/theme.js", "shared/i18n.js", "shared/lamha-ai.js", "shared/motion.js", "options/i18n-options.js", "options/options.js"];
  const plainPage = await openPage("options/options.html", scripts);
  assert.equal(plainPage.document.getElementById("appPanel").hidden, true, "no connectNative (Android, the Windows app): no section");
  const o = await openPage("options/options.html", scripts, { extra: { native: true } });
  const $o = id => o.document.getElementById(id);
  assert.equal($o("appPanel").hidden, false);
  assert.ok([...o.document.querySelectorAll("#toc a")].some(a => a.hash === "#appPanel"), "in the section bar");
  await sleep(100);
  assert.deepEqual([text($o("appState")), text($o("appBtn"))], ["غير متصل.", "اتصال"]);
  assert.deepEqual(fxApp.asked, [], "not connected: the app isn't asked");
  $o("appBtn").click();
  await sleep(150);
  assert.equal(fxApp.granted, true);
  assert.equal(text($o("appState")), "متصل بلمحة 1.10.0 · ويكيبيديا: العربية");
  assert.deepEqual(fxApp.asked.filter(([t]) => t === "hello"), [["hello", true]], "Connect may start the app");
  assert.ok(fxApp.asked.filter(([t]) => t === "deckSync").every(([, launch]) => !launch), "the decks meet, never by starting the app");
  assert.ok($o("appBtn").classList.contains("danger") && text($o("appBtn")) === "قطع الاتصال");
  $o("appBtn").click();
  await sleep(150);
  assert.equal(fxApp.granted, false, "the permission goes back");
  assert.equal((await local.get("appLink")).appLink, false);
  assert.deepEqual([text($o("appState")), text($o("appBtn"))], ["غير متصل.", "اتصال"]);
});

await step("Settings: 'Explain words in their own language' has English among its languages, the translation language always on, and follows the card's switch", async () => {
  const o = await openPage("options/options.html", ["shared/theme.js", "shared/i18n.js", "shared/lamha-ai.js", "shared/motion.js", "options/i18n-options.js", "options/options.js"]);
  const btn = name => [...o.document.querySelectorAll("#explainLangs button")].find(b => text(b) === name);
  assert.deepEqual([...o.document.querySelectorAll("#explainLangs button")].map(text), ["الإنجليزية", "العربية", "الفرنسية", "التركية", "الأردية", "الفارسية", "الإسبانية", "الألمانية"]);
  assert.equal(o.document.getElementById("enDict"), null, "no separate English–English switch");
  assert.ok(btn("العربية").disabled && btn("العربية").getAttribute("aria-pressed") === "true", "Arabic, the translation language: always explained");
  btn("الإنجليزية").click(); // English keeps its own setting, which older versions read
  await sleep(50);
  assert.equal(sync.data.enDict, true);
  assert.equal(btn("الإنجليزية").getAttribute("aria-pressed"), "true");
  btn("الفرنسية").click();
  await sleep(50);
  assert.deepEqual(sync.data.explainLangs, ["fr"]);
  await bgHandler({ type: "setWordDict", lang: "en", on: false }, {}); // the card's switch, with Settings open
  await bgHandler({ type: "setWordDict", lang: "de", on: true }, {});
  await sleep(50);
  assert.equal(btn("الإنجليزية").getAttribute("aria-pressed"), "false");
  assert.equal(btn("الألمانية").getAttribute("aria-pressed"), "true");
  assert.deepEqual(sync.data.explainLangs, ["fr", "de"]);
  await sync.set({ targetLang: "en" }); // translating into English: now English is the one always explained
  await sleep(50);
  assert.ok(btn("الإنجليزية").disabled && !btn("العربية").disabled);
  await sync.set({ explainLangs: [], enDict: false, targetLang: "ar" });
});

await step("Settings: dictionaries to download — sizes, a download with its progress, a failure explained, Remove", async () => {
  packFiles["tr.json.gz"] = gzipSync(Buffer.from(JSON.stringify({ meta: { lang: "tr", format: 1, version: "2026-09-28", words: 33902 }, shards: { ev: { w: { ev: { s: [["n", "yaşanılan yer", "", []]] } }, f: {} } } })));
  const o = await openPage("options/options.html", ["shared/i18n.js", "options/i18n-options.js", "shared/lamha-ai.js", "shared/motion.js", "options/options.js"]);
  await sleep(100);
  const rows = () => [...o.document.querySelectorAll("#packs li")];
  const row = name => rows().find(li => text(li.querySelector("b")) === name);
  assert.deepEqual(rows().map(li => text(li.querySelector("b"))), ["الفرنسية", "الألمانية", "الإسبانية", "التركية"]);
  assert.match(text(row("التركية").querySelector("small")), /^٣٣٬٩٠٢ كلمة · ١٫٨ ميغابايت$/);
  row("التركية").querySelector("button").click();
  await sleep(400);
  assert.equal(text(row("التركية").querySelector("button")), "إزالة", "installed: it can be removed");
  assert.ok(row("التركية").querySelector("button").classList.contains("danger"), "and the button says it deletes");
  assert.ok(packParts.has("tr/meta") && packParts.has("tr/ev"));
  row("الفرنسية").querySelector("button").click(); // not on the release (404)
  await sleep(400);
  assert.match(text(row("الفرنسية").querySelector(".err")), /تعذّر التنزيل/);
  assert.equal(text(row("الفرنسية").querySelector("button")), "تنزيل", "and it can be tried again");
  row("التركية").querySelector("button").click();
  await sleep(300);
  assert.equal(text(row("التركية").querySelector("button")), "تنزيل");
  assert.equal(packParts.size, 0);
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

await step("colours: the card (content/styles.js) and the pages (shared/ui.css) use the same palette in both themes", async () => {
  const vars = block => Object.fromEntries([...block.replace(/\/\*[\s\S]*?\*\//g, "").matchAll(/--([\w-]+):\s*([^;]+);/g)].map(m => [m[1], m[2].trim()]));
  const cut = (s, from) => { const i = s.indexOf(from); assert.ok(i >= 0, from); return s.slice(i, s.indexOf("}", i)); };
  const ui = src("shared/ui.css"), card = src("content/styles.js");
  const pages = { light: vars(cut(ui, ":root {")), dark: vars(cut(ui, ':root[data-theme="dark"] {')) };
  assert.deepEqual(vars(cut(ui, ':root:not([data-theme="light"]) {')), pages.dark, "the two dark blocks in ui.css are the same");
  const cards = { light: vars(cut(card, ".root {")), dark: vars(cut(card, ".root.dark {")) };
  const SHARED = ["fg", "muted", "faint", "line", "hover", "accent", "accent-soft", "accent-fg", "btn", "btn-fg", "ok", "ok-soft", "danger", "danger-soft", "toast-bg", "toast-fg", "toast-act", "celebrate", "scroll", "r-sm", "r-md", "r-lg"];
  for (const theme of ["light", "dark"]) {
    for (const k of SHARED) assert.equal(cards[theme][k], pages[theme][k], `${theme} --${k}`);
    assert.equal(cards[theme].bg, pages[theme].surface, `${theme}: the card is a surface`);
  }
});

await step("one design language: no system boxes (confirm / alert / prompt) in the pages, and dropdowns styled once, in ui.css", async () => {
  const pages = ["popup/popup.js", "options/options.js", "content/content.js", "desktop/renderer/clipboard/clip-list.js", "desktop/renderer/clipboard/clip-actions.js",
    "desktop/renderer/clipboard/panel.js", "desktop/renderer/clipboard/tab.js", "desktop/renderer/clipboard/settings.js", "desktop/renderer/wiki-settings.js", "desktop/renderer/wiki/reader.js"];
  for (const f of pages) {
    const code = src(f).replace(/\/\*[\s\S]*?\*\//g, "").replace(/\/\/.*$/gm, "");
    assert.doesNotMatch(code, /(^|[^.\w])(window\.)?(confirm|alert|prompt)\(/m, f + " uses a system box: use LamhaDialog");
  }
  const css = src("shared/ui.css");
  assert.match(css, /@supports \(appearance: base-select\)[\s\S]*::picker\(select\)/, "the Windows app draws the open list");
  assert.match(css, /select option \{ background-color: var\(--surface\); color: var\(--fg\); \}/, "Firefox's list in our colours");
  for (const f of ["options/options.css", "popup/popup.css", "desktop/renderer/desktop.css", "desktop/renderer/clipboard/clipboard.css"]) {
    assert.doesNotMatch(src(f), /(^|\})\s*select\s*\{[^}]*background/m, f + " restyles dropdowns: keep them in ui.css");
  }
});

await step("popup write: with no AI chosen, the tab says how to set one up (not in red) and the tools wait", async () => {
  await local.set({ popupMode: "write", draft: "She dont like apples.", aiProvider: "claude", aiKeySet: false });
  const p = await openPage("popup/popup.html", ["shared/i18n.js", "shared/lamha-ai.js", "shared/motion.js", "popup/popup.js"]);
  await sleep(100);
  const out = p.document.getElementById("wrOut");
  assert.equal(out.hidden, false, "shown as soon as the tab opens");
  assert.ok(out.querySelector(".setup") && !out.querySelector(".error"), "a setup step, not an error");
  assert.ok([...p.document.querySelectorAll("#wrTools button")].every(b => b.disabled), "the tools wait for an AI");
  await local.set({ popupMode: "translate", draft: "" });
});

await step("Theme: Settings and the popup follow the choice (not only the card); Automatic leaves it to the system", async () => {
  const o = await openPage("options/options.html", ["shared/theme.js", "shared/i18n.js", "options/i18n-options.js", "shared/lamha-ai.js", "shared/motion.js", "options/options.js"]);
  await sleep(100);
  const html = o.document.documentElement;
  assert.equal(html.dataset.theme, undefined, "Automatic: the system decides");
  const sel = o.document.getElementById("theme");
  sel.value = "light";
  sel.dispatchEvent(new o.Event("change"));
  await sleep(50);
  assert.equal(sync.data.theme, "light");
  assert.equal(html.dataset.theme, "light", "the settings page itself turns light, even on a dark system");
  sel.value = "dark";
  sel.dispatchEvent(new o.Event("change"));
  await sleep(50);
  assert.equal(html.dataset.theme, "dark");
  const p = await openPage("popup/popup.html", ["shared/theme.js", "shared/i18n.js", "shared/lamha-ai.js", "shared/motion.js", "popup/popup.js"]);
  assert.equal(p.document.documentElement.dataset.theme, "dark", "a page opened later starts in the chosen theme");
  sel.value = "auto";
  sel.dispatchEvent(new o.Event("change"));
  await sleep(50);
  assert.equal(html.dataset.theme, undefined);
  assert.equal(p.document.documentElement.dataset.theme, undefined, "open pages follow the change");
  const css = readFileSync(new URL("shared/ui.css", EXT), "utf8");
  assert.match(css, /:root\[data-theme="dark"\] \{/);
  assert.match(css, /:root:not\(\[data-theme="light"\]\) \{/, "a dark system no longer overrides Light");
});

/* ---- desktop: the tools on a clip in the quick panel (Alt+Shift+V → Tab) ---- */

await step("clipboard tools: numbered rows with icons and hints, one lit row, number keys, Esc steps back", async () => {
  const dom = new JSDOM(`<html dir="rtl"><body><section id="root"></section></body></html>`, { runScripts: "outside-only", url: "https://lamha.test/", pretendToBeVisual: true });
  const w = dom.window;
  const store = data => ({ get: async k => ({ ...(k && typeof k === "object" && !Array.isArray(k) ? k : {}), ...data }), set: async () => {} });
  w.browser = { storage: { sync: store({ uiLang: "ar", targetLang: "ar" }), local: store({}), onChanged: { addListener() {} } }, tabs: { create() {} }, runtime: { getURL: p => p } };
  const calls = [];
  w.lamhaClipboard = {
    action: async (id, act) => { calls.push(act); return { ok: true, data: { text: "حافظ على وعدك." } }; },
    paste: async () => ({ ok: true }), lookup: async () => ({ ok: true }), pasteResult: async () => ({ ok: true }), copyText: async () => ({ ok: true })
  };
  // one script, as the desktop's injected scripts share one global scope (a separate eval each would not)
  w.eval(["shared/i18n.js", "desktop/renderer/i18n-desktop.js", "shared/lamha-ai.js", "shared/motion.js", "desktop/renderer/clipboard/clip-list.js", "desktop/renderer/clipboard/clip-actions.js"]
    .map(f => readFileSync(new URL(f, EXT), "utf8")).join("\n;\n") + "\n;window.LamhaClipActions = LamhaClipActions;");
  await sleep(30);
  let back = 0;
  const root = w.document.getElementById("root");
  const clip = { id: "c1", text: "Keep your promise.", lang: "en", sourceApp: "code.exe", createdAt: Date.now() - 120e3, lastCopiedAt: Date.now() - 120e3, cache: { translation: {} } };
  w.LamhaClipActions.mount(root, clip, { mode: "panel", onBack: () => back++ }).focus();
  await sleep(30);
  const rows = [...root.querySelectorAll(".ca-item")];
  assert.deepEqual(rows.map(r => r.dataset.act), ["translate", "proofread", "lookup", "plain"]);
  assert.deepEqual(rows.map(r => text(r.querySelector(".ca-num"))), ["١", "٢", "٣", "٤"]);
  assert.ok(rows.every(r => r.querySelector(".ca-ico svg")), "an icon on every row");
  assert.equal(text(rows[0].querySelector(".ca-hint")), "إلى العربية");
  assert.equal(text(root.querySelector(".ca-title strong")), "أدوات النص");
  assert.match(text(root.querySelector(".ca-clip-meta")), /^code · .+ · الإنجليزية$/, "where it came from, when, its language");
  assert.match(text(root.querySelector(".ca-keys")), /Enter تشغيل.*١–٤ اختصار.*Esc رجوع/);
  rows[1].dispatchEvent(new w.MouseEvent("mouseenter"));
  assert.equal(w.document.activeElement, rows[1], "the mouse moves the keyboard's choice: one lit row");
  w.document.activeElement.dispatchEvent(new w.KeyboardEvent("keydown", { key: "1", bubbles: true }));
  await sleep(50);
  assert.deepEqual(calls, ["translate"], "1 runs the first tool");
  assert.equal(root.querySelector(".ca-menu").hidden, true, "the answer takes the tools' place");
  assert.equal(text(root.querySelector(".ca-text")), "حافظ على وعدك.");
  assert.match(text(root.querySelector(".ca-keys")), /Enter لصق.*Esc الأدوات/);
  w.document.activeElement.dispatchEvent(new w.KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
  assert.equal(root.querySelector(".ca-menu").hidden, false, "Esc: back to the tools");
  assert.equal(w.document.activeElement, rows[0], "on the tool that ran");
  assert.equal(back, 0);
  w.document.activeElement.dispatchEvent(new w.KeyboardEvent("keydown", { key: "٣", bubbles: true }));
  assert.equal(root.querySelector(".ca-menu").hidden, false, "٣ = look up: opens the card, no answer here");
  w.document.activeElement.dispatchEvent(new w.KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
  assert.equal(back, 1, "Esc again: back to the list");
  root.querySelector(".ca-back").click();
  assert.equal(back, 2, "the arrow goes back to the list");
});

await step("clipboard list: pinned clips sit on top, but the panel opens on the newest copy, so Enter pastes it", async () => {
  const dom = new JSDOM(`<html dir="rtl"><body><section id="root"></section></body></html>`, { runScripts: "outside-only", url: "https://lamha.test/", pretendToBeVisual: true });
  const w = dom.window;
  const store = data => ({ get: async k => ({ ...(k && typeof k === "object" && !Array.isArray(k) ? k : {}), ...data }), set: async () => {} });
  w.browser = { storage: { sync: store({ uiLang: "ar" }), local: store({}), onChanged: { addListener() {} } }, runtime: { getURL: p => p } };
  const now = Date.now();
  const clip = (id, pinned, ago) => ({ id, text: "clip " + id, pinned, sourceApp: "notepad.exe", lang: "en", createdAt: now - ago, lastCopiedAt: now - ago, copyCount: 1, useCount: 0 });
  const items = [clip("p1", true, 9e5), clip("p2", true, 8e5), clip("n1", false, 1e3), clip("n2", false, 6e4)]; // the store's order
  w.Element.prototype.scrollIntoView = () => {}; // jsdom has no layout
  w.lamhaClipboard = { onChanged() {}, list: async ({ filter }) => { const its = filter === "pinned" ? items.filter(i => i.pinned) : items; return { ok: true, data: { items: its, total: its.length } }; } };
  w.eval(["shared/i18n.js", "desktop/renderer/i18n-desktop.js", "shared/motion.js", "desktop/renderer/clipboard/clip-list.js"]
    .map(f => readFileSync(new URL(f, EXT), "utf8")).join("\n;\n") + "\n;window.LamhaClipList = LamhaClipList;");
  await sleep(30);
  const pasted = [];
  const list = w.LamhaClipList.create({ root: w.document.getElementById("root"), mode: "panel", onActivate: it => pasted.push(it.id) });
  list.reset();
  await sleep(50);
  const rows = [...w.document.querySelectorAll(".lc-row")];
  assert.deepEqual(rows.map(r => r.dataset.id), ["p1", "p2", "n1", "n2"]);
  assert.equal(list.selected.id, "n1", "the newest copy is chosen, not the first pinned clip");
  assert.equal(rows[2].getAttribute("aria-selected"), "true");
  list.reset();
  await sleep(50);
  assert.equal(list.selected.id, "n1", "each opening starts there again");
});

/* ---- the Windows app's Wikipedia reader (desktop/renderer/wiki/): the cleaner and the page, with a fake library ---- */
const PARIS_HTML = `<html><head><title>باريس</title><script>window.__pwned = 1</script></head><body>
<div id="mw-content-text"><div class="mw-parser-output">
<section data-mw-section-id="0"><p onclick="window.__pwned = 2" style="color:red">باريس <a href="%D9%81%D8%B1%D9%86%D8%B3%D8%A7">عاصمة فرنسا</a>
و<a href="Lyon">ليون</a> و<a href="#إسكان">السكان</a> و<a href="https://www.paris.fr/" class="external text">موقعها</a> و<a href="javascript:window.__pwned=3">رابط خطر</a>
<img src="./_assets_/a/Eiffel.jpg" onerror="window.__pwned = 4" width="250"><img src="https://tracker.example/t.gif"></p>
<script>window.__pwned = 5</script><style>p { color: red }</style><iframe src="https://evil.example/"></iframe>
<form action="https://evil.example/"><input name="x"></form><math><mi>x</mi></math>
<table class="infobox"><tr><td><table class="wikitable"><tr><td>داخل</td></tr></table></td></tr></table>
<details><summary>المزيد</summary><p>مطوي</p></details></section>
<section data-mw-section-id="1"><div class="mw-heading mw-heading2"><h2 id="إسكان">السكان<span class="mw-editsection">تعديل</span></h2></div>
<p id="rdQ">يبلغ عدد السكان أكثر من مليونين.</p><div class="navbox">روابط كثيرة</div></section>
</div></div><div class="zim-footer">This article is issued from Wikipedia via Kiwix</div></body></html>`;

/** The reader page in jsdom, with a fake lamhaWiki: `articles` by path ("" = the main page). */
async function readerPage({ articles = {}, files, uiLang = "ar" } = {}) {
  const dom = new JSDOM('<!DOCTYPE html><html><body><div id="app"></div></body></html>', { runScripts: "outside-only", url: "https://reader.lamha.test/", pretendToBeVisual: true /* jsdom keeps no storage for file: pages; Electron does */ });
  const w = dom.window;
  const calls = [], opened = [];
  let onOpen = null;
  const list = files || [{ id: "ar1", lang: "ar", scope: "top", flavour: "mini", date: "2026-07-10", articles: 231103, title: "x", missing: false }];
  w.browser = {
    storage: { sync: { get: async d => ({ ...d, uiLang }) }, onChanged: { addListener() {} } },
    tabs: { create: async ({ url }) => { opened.push(url); } },
    runtime: { getURL: p => "lamha://app/" + p }
  };
  w.lamhaWiki = {
    list: async () => ({ ok: true, data: { files: list } }),
    article: async (fileId, path) => {
      calls.push(["article", fileId, path]);
      const a = Object.hasOwn(articles, path) ? articles[path] : null;
      return { ok: true, data: a && { fileId: fileId || "ar1", path, lang: "ar", flavour: "mini", date: "2026-07-10", url: "https://ar.wikipedia.org/wiki/" + path, ...a } };
    },
    suggest: async (q, lang) => { calls.push(["suggest", q, lang]); return { ok: true, data: Object.keys(articles).filter(p => p && p.startsWith(q)).map(p => ({ fileId: "ar1", lang: "ar", path: p, title: p })) }; },
    random: async () => ({ ok: true, data: { fileId: "ar1", path: "باريس" } }),
    onOpen: f => { onOpen = f; },
    onChanged: () => {}
  };
  w.scrollTo = () => {};
  w.HTMLElement.prototype.scrollIntoView = function () { w.__scrolledTo = this.id; };
  for (const s of ["shared/i18n.js", "desktop/renderer/i18n-desktop.js", "desktop/renderer/wiki/sanitize.js", "desktop/renderer/wiki/reader.js"]) w.eval(src(s));
  await sleep(100);
  return { w, calls, opened, open: msg => onOpen(msg), $: id => w.document.getElementById(id) };
}
const ARTICLES = { "باريس": { title: "باريس", html: PARIS_HTML }, "فرنسا": { title: "فرنسا", html: "<p>فرنسا دولة في غرب أوروبا، عاصمتها باريس.</p>" } };

await step("Wikipedia reader: an article is rebuilt from an allow-list: no scripts, handlers, styles, frames or forms; links and pictures are the reader's", async () => {
  const r = await readerPage();
  const doc = r.w.document;
  const { fragment, headings } = r.w.LamhaWikiSanitize.article(PARIS_HTML, { path: "باريس", assetUrl: p => "lamha-wiki://zim/ar1/" + p, document: doc });
  const div = doc.createElement("div");
  div.append(fragment);
  assert.equal(div.querySelectorAll("script, style, iframe, form, input, link, meta, math, svg").length, 0);
  assert.ok(![...div.querySelectorAll("*")].some(el => [...el.attributes].some(a => /^on/i.test(a.name) || a.name === "style")), "no handlers, no inline styles");
  assert.deepEqual([...div.querySelectorAll("a")].map(a => [a.textContent, a.getAttribute("href"), a.dataset.path || a.dataset.anchor || a.dataset.ext]),
    [["عاصمة فرنسا", "#", "فرنسا"], ["ليون", "#", "Lyon"], ["السكان", "#", "wk-إسكان"], ["موقعها", "#", "https://www.paris.fr/"]], "javascript: is left as text");
  assert.match(div.textContent, /رابط خطر/);
  assert.deepEqual([...div.querySelectorAll("img")].map(i => i.getAttribute("src")), ["lamha-wiki://zim/ar1/_assets_/a/Eiffel.jpg"], "pictures from the file only: nothing from the web");
  assert.ok(div.querySelector("#wk-rdQ") && !div.querySelector("#rdQ"), "the article's ids never meet the reader's");
  assert.deepEqual(JSON.parse(JSON.stringify(headings)), [{ id: "wk-إسكان", text: "السكان", level: 2 }]);
  assert.doesNotMatch(div.textContent, /Kiwix|روابط كثيرة|تعديل/, "Kiwix's footer, navigation boxes and edit links are left out");
  assert.deepEqual([...div.querySelectorAll(".wk-scroll")].map(d => d.className), ["wk-scroll wk-infobox", "wk-scroll"], "tables scroll, inner ones too");
  assert.equal(div.querySelector("details").open, true);
  let deep = "";
  for (let i = 0; i < 3000; i++) deep += "<div>";
  assert.doesNotThrow(() => r.w.LamhaWikiSanitize.article(deep + "x", { path: "x", assetUrl: p => p, document: doc }), "very deep nesting is cut, not a crash");
  assert.equal(r.w.__pwned, undefined);
});

await step("Wikipedia reader: search suggests titles, Enter opens one; links open articles, places or the web; back and forward remember", async () => {
  const r = await readerPage({ articles: ARTICLES });
  const main = r.$("rdMain");
  assert.ok(text(main).includes(r.w.LamhaI18n.t("d.wTitle")), "the start page"); // the title's wording is the user's: read it, don't repeat it
  assert.ok(r.$("rdRandom"));
  const q = r.$("rdQ");
  q.value = "بار";
  q.dispatchEvent(new r.w.Event("input", { bubbles: true }));
  await sleep(250);
  assert.deepEqual([...r.$("rdSug").querySelectorAll("li")].map(text), ["باريس"]);
  q.dispatchEvent(new r.w.KeyboardEvent("keydown", { key: "Enter", bubbles: true }));
  await sleep(100);
  assert.equal(text(main.querySelector(".rd-title")), "باريس");
  assert.equal(main.querySelector(".rd-article").dir, "rtl");
  assert.match(r.w.document.title, /^باريس — /);
  assert.equal(r.$("rdToc").querySelectorAll("a").length, 1, "contents: the article's headings");
  main.querySelector('a[data-path="فرنسا"]').click();
  await sleep(100);
  assert.equal(text(main.querySelector(".rd-title")), "فرنسا");
  r.$("rdBack").click();
  await sleep(100);
  assert.equal(text(main.querySelector(".rd-title")), "باريس");
  assert.equal(r.$("rdFwd").disabled, false);
  main.querySelector('a[data-anchor="wk-إسكان"]:not([data-path])').click();
  assert.equal(r.w.__scrolledTo, "wk-إسكان", "a place in the article");
  main.querySelector("a[data-ext]").click();
  main.querySelector('a[data-path="Lyon"]').click();
  await sleep(100);
  assert.equal(text(main.querySelector(".rd-title")), "باريس", "an article the copy doesn't have: the page stays");
  const toast = r.w.document.querySelector(".rd-toast");
  assert.equal(toast.hidden, false);
  assert.match(text(toast), /ليست في النسخة المنزّلة/);
  toast.querySelector("button").click();
  assert.deepEqual(r.opened, ["https://www.paris.fr/", "https://ar.wikipedia.org/wiki/Lyon"], "the web, in the default browser");
  r.open({ file: "ar1", path: "فرنسا" }); // the card's "Read the article in Lamha"
  await sleep(100);
  assert.equal(text(main.querySelector(".rd-title")), "فرنسا");
});

await step("Wikipedia reader: text size starts in the middle and is remembered; English interface: the article keeps its own edge", async () => {
  const r = await readerPage({ articles: ARTICLES, uiLang: "en" });
  assert.equal(r.w.document.documentElement.style.getPropertyValue("--rd-size"), "17px");
  assert.equal(r.$("rdSmaller").disabled, false, "nothing saved yet: not the smallest size");
  r.$("rdSmaller").click();
  assert.equal(r.w.document.documentElement.style.getPropertyValue("--rd-size"), "16px");
  assert.equal(r.w.localStorage.getItem("lamhaWikiSize"), "2");
  r.open({ file: "ar1", path: "باريس" });
  await sleep(100);
  const meta = r.w.document.querySelector(".rd-meta");
  assert.equal(meta.dir, "rtl", "under an Arabic title, on its side");
  assert.equal(meta.firstElementChild.dir, "ltr", "in English words");
});

await step("Wikipedia reader: a link with a stray % in an article doesn't stop the rest of it from showing", async () => {
  const html = '<p>أول <a href="Foo%zz">رابط معطوب</a> و<a href="#x%E0%A4%A">مكان معطوب</a> و<a href="Lyon">ليون</a>.</p><p>الفقرة التالية.</p>';
  const r = await readerPage({ articles: { "معطوبة": { title: "معطوبة", html } } });
  r.open({ file: "ar1", path: "معطوبة" });
  await sleep(100);
  const main = r.$("rdMain");
  assert.equal(text(main.querySelector(".rd-title")), "معطوبة");
  assert.match(text(main), /الفقرة التالية/);
  assert.deepEqual([...main.querySelectorAll(".rd-body a")].map(a => a.dataset.path || a.dataset.anchor), ["Foo%zz", "wk-x%E0%A4%A", "Lyon"]);
});

await step("popup: the quick translation says why it failed, as the card does ('Local only', the AI translator's quota)", async () => {
  await local.set({ popupMode: "translate", trProvider: "gemini" });
  const p = await openPage("popup/popup.html", ["shared/i18n.js", "shared/lamha-ai.js", "shared/motion.js", "popup/popup.js"]);
  await sleep(100);
  let reply = null;
  const real = p.browser.runtime.sendMessage;
  p.browser.runtime.sendMessage = async msg => (msg.type === "lookup" ? reply : real(msg));
  const ask = async (error, textIn) => {
    reply = { ok: false, error };
    const q = p.document.getElementById("q");
    q.value = textIn;
    q.dispatchEvent(new p.KeyboardEvent("keydown", { key: "Enter", bubbles: true }));
    await sleep(50);
    return text(p.document.querySelector("#result .error"));
  };
  assert.match(await ask("not_found_offline", "zyxt"), /غير موجودة في القاموس المحلي.*القاموس المحلي فقط/);
  assert.match(await ask("offline_mode", "a whole sentence here."), /تحتاج إلى الإنترنت/);
  assert.match(await ask("gemini_quota", "hello"), /انتهى الحد المجاني من Gemini/);
  assert.match(await ask("ai_error:bad response", "hello"), /تعذّر الاتصال بـ Gemini — bad response/, "the translating AI, named");
  assert.equal(await ask("network", "hello"), "تعذّرت الترجمة. تحقق من الاتصال.");
  await local.remove("trProvider");
});

await step("popup 🔊: when Google's voice can't play, or there's no network, the system's voice says the word", async () => {
  await local.set({ popupMode: "review", cards: { resilient: card("resilient", "مرن", { added: now }) } });
  const p = await openPage("popup/popup.html", ["shared/i18n.js", "shared/lamha-ai.js", "shared/motion.js", "popup/popup.js"]);
  await sleep(100);
  const spoken = [], asked = [];
  p.SpeechSynthesisUtterance = class { constructor(t) { this.text = t; } };
  p.speechSynthesis = { cancel() {}, speak: u => { spoken.push([u.text, u.lang]); setTimeout(() => u.onend(), 0); } };
  const real = p.browser.runtime.sendMessage;
  p.browser.runtime.sendMessage = async msg => (msg.type === "speak" ? (asked.push(msg.text), { ok: false, error: "audio error" }) : real(msg));
  const btn = () => p.document.querySelector(".rv-word-row .icon-btn");
  btn().click();
  await sleep(50);
  assert.deepEqual(asked, ["resilient"], "Google's voice first");
  assert.deepEqual(spoken, [["resilient", "en-US"]], "then the system's");
  assert.equal(btn().classList.contains("playing"), false, "the waves stop when it's done");
  Object.defineProperty(p.navigator, "onLine", { configurable: true, get: () => false });
  btn().click();
  await sleep(50);
  assert.equal(asked.length, 1, "no network: Google isn't asked");
  assert.equal(spoken.length, 2);
});

await step("Settings → Privacy: restore a copy (merged), the report to read before sending, and 'delete all cards' noted as removals", async () => {
  await local.set({ cards: { bank: card("bank", "ضفة") }, cardsImported: true });
  const o = await openPage("options/options.html", ["shared/theme.js", "shared/i18n.js", "options/i18n-options.js", "shared/lamha-ai.js", "shared/motion.js", "options/options.js"]);
  await sleep(100);
  const $o = id => o.document.getElementById(id);
  const backup = { app: "lamha", format: 1, cards: { thrive: card("thrive", "يزدهر", { mod: Date.now() }) }, history: [{ q: "thrive", tr: "يزدهر", src: "en", t: Date.now() }] };
  const input = $o("backupFile");
  Object.defineProperty(input, "files", { configurable: true, value: [new o.File([JSON.stringify(backup)], "lamha.json", { type: "application/json" })] });
  input.dispatchEvent(new o.Event("change"));
  await sleep(150);
  assert.match(text($o("backupState")), /أُضيفت بطاقة واحدة/);
  assert.deepEqual(Object.keys(local.data.cards).sort(), ["bank", "thrive"], "merged, nothing lost");
  Object.defineProperty(input, "files", { configurable: true, value: [new o.File(["{\"not\": \"lamha\"}"], "other.json")] });
  input.dispatchEvent(new o.Event("change"));
  await sleep(100);
  assert.ok($o("backupState").classList.contains("err") && /ليس نسخة/.test(text($o("backupState"))), "another file is refused");

  $o("reportBox").open = true;
  $o("reportBox").dispatchEvent(new o.Event("toggle"));
  await sleep(100);
  const report = $o("reportText").textContent;
  assert.match(report, /^Lamha /);
  assert.ok(!/bank|thrive|ضفة/.test(report), "no words in the report");

  $o("rvClear").click();
  o.document.querySelector("dialog.dlg .danger-solid").click();
  await sleep(100);
  assert.deepEqual(Object.keys(local.data.cards), []);
  assert.ok(["bank", "thrive"].every(k => Object.hasOwn(local.data.cardsRemoved, k)), "each noted as removed, so a copy won't bring it back: " + Object.keys(local.data.cardsRemoved));
});

await step("Wikipedia reader: with nothing downloaded, the start page says so and leads to Settings", async () => {
  const r = await readerPage({ files: [] });
  assert.match(text(r.$("rdMain")), /لم تنزّل ويكيبيديا بعد/);
  r.$("rdMain").querySelector(".btn").click();
  assert.deepEqual(r.opened, ["lamha://app/options/options.html#wikipedia"]);
});

results.forEach(report);
const failed = results.filter(r => r.includes("✗")).length;
console.log(`${quiet ? "" : "\n"}${results.length - failed}/${results.length} passed`);
process.exit(failed ? 1 : 0);
