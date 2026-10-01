// Tests for the desktop app: offline Wikipedia (desktop/zim.js, desktop/wiki-library.js), and the Write button on a
// double-click (desktop/double-click.js, desktop/uia-context.js):
// the decisions in plain Node, and on Windows the helper itself: its PowerShell parses, its C# mouse hook compiles,
// and it answers a text-box question. The real double-click in a real app is in the desktop self-test (npm run smoke).
//   node tools/test-desktop.mjs
import { createRequire } from "node:module";
import { spawnSync } from "node:child_process";
import { readFileSync, writeFileSync, mkdtempSync, mkdirSync, rmSync, existsSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import vm from "node:vm";
import assert from "node:assert/strict";
import { quiet, wanted, report, title as printTitle, notRun } from "./test-args.mjs";

const require = createRequire(import.meta.url);
const { ClickPairer, composeKind, wantsButton } = require("../desktop/double-click.js");
const { UiaContext, SCRIPT } = require("../desktop/uia-context.js");

const i18n = vm.createContext({});
vm.runInContext(readFileSync(new URL("../shared/i18n.js", import.meta.url), "utf8"), i18n);
const L = vm.runInContext("LamhaI18n", i18n);

const queue = [];
const test = (name, fn, { windows = false } = {}) => queue.push({ name, fn, windows });
const section = title => queue.push({ title });
const dir = mkdtempSync(path.join(tmpdir(), "lamha-desktop-test-"));

section("double-clicks (ClickPairer)");
const zone = { time: 500, width: 4, height: 4 };

test("two presses close in time and place are a double-click", () => {
  const p = new ClickPairer();
  assert.equal(p.press({ t: 1000, x: 100, y: 100 }, zone), false);
  assert.equal(p.press({ t: 1200, x: 101, y: 99 }, zone), true);
});

test("too slow, or too far apart, is two single clicks", () => {
  const slow = new ClickPairer();
  slow.press({ t: 1000, x: 100, y: 100 }, zone);
  assert.equal(slow.press({ t: 1501, x: 100, y: 100 }, zone), false);
  const far = new ClickPairer();
  far.press({ t: 1000, x: 100, y: 100 }, zone);
  assert.equal(far.press({ t: 1100, x: 103, y: 100 }, zone), false, "3 px right, the rectangle is 4 px wide around the first");
  assert.equal(far.press({ t: 1300, x: 104, y: 101 }, zone), true, "the slow second click starts a pair that the third completes");
});

test("a triple-click is one double-click, not two; a fourth press pairs with the third", () => {
  const p = new ClickPairer();
  const at = t => p.press({ t, x: 50, y: 50 }, zone);
  assert.deepEqual([at(0), at(150), at(300), at(450)], [false, true, false, true]);
});

test("Windows' tick count wrapping around (every 49.7 days) doesn't break a double-click", () => {
  const p = new ClickPairer();
  p.press({ t: 4294967290, x: 10, y: 10 }, zone);
  assert.equal(p.press({ t: 100, x: 10, y: 10 }, zone), true);
});

section("when the button shows (wantsButton, composeKind)");

test("an empty box in any program: yes; with text, not a box, or no answer: no", () => {
  assert.equal(wantsButton("notepad.exe", { empty: true, web: false, lamha: false }), true);
  assert.equal(wantsButton("notepad.exe", { empty: false }), false);
  assert.equal(wantsButton("notepad.exe", null), false);
});

test("never in Lamha itself or in Windows' own search and address boxes", () => {
  for (const exe of ["lamha", "explorer.exe", "searchhost.exe", "startmenuexperiencehost.exe", ""]) {
    assert.equal(wantsButton(exe, { empty: true, web: false }), false, exe);
  }
});

test("browsers: web page boxes only (not the address bar), and never where the Lamha extension showed its own", () => {
  assert.equal(wantsButton("firefox.exe", { empty: true, web: true, lamha: false }), true, "a browser without the extension");
  assert.equal(wantsButton("firefox.exe", { empty: true, web: true, lamha: true }), false, "the extension's button is there");
  assert.equal(wantsButton("chrome.exe", { empty: true, web: true, lamha: true }), false, "any browser, not only Firefox");
  assert.equal(wantsButton("msedge.exe", { empty: true, web: false }), false, "the address bar");
  assert.equal(wantsButton("slack.exe", { empty: true, web: true, lamha: false }), true, "web content in an app");
});

test("Write new starts as an email in mail programs, a message elsewhere", () => {
  assert.equal(composeKind("outlook.exe"), "email");
  assert.equal(composeKind("olk.exe"), "email", "the new Outlook");
  assert.equal(composeKind("thunderbird.exe"), "email");
  assert.equal(composeKind("whatsapp.exe"), "message");
  assert.equal(composeKind(""), "message");
});

test("the extension's button is recognised in both interface languages", () => {
  assert.deepEqual([...L.pair("c.writeNewHere")], ["اكتب نصًّا جديدًا في هذا المربع", "Write something new in this box"]);
  L.setLang("en");
  assert.equal(L.t("c.writeNewHere"), L.pair("c.writeNewHere")[1]);
});

section("what each window is told and may ask, and where it goes (app-rules.js)");
const rules = require("../desktop/app-rules.js");

test("storage changes: pages get everything; the card no API keys and no deck; the panel and the reader no deck", () => {
  const changes = { aiKey: { newValue: "sk-x" }, aiKeySet: { newValue: true }, cards: { newValue: { a: {} } }, history: { newValue: [] }, theme: { newValue: "dark" } };
  assert.deepEqual(Object.keys(rules.changesFor("page", changes)), ["aiKey", "aiKeySet", "cards", "history", "theme"]);
  assert.deepEqual(Object.keys(rules.changesFor("card", changes)), ["aiKeySet", "theme"]);
  assert.deepEqual(Object.keys(rules.changesFor("lean", changes)), ["aiKey", "aiKeySet", "theme"]);
  assert.equal(rules.changesFor("card", { cards: {}, geminiKey: {} }), null, "nothing left: nothing sent");
  assert.deepEqual(Object.keys(rules.withoutSecrets({ geminiKey: "x", geminiKeySet: true })), ["geminiKeySet"]);
});

test("the card and the reader may ask for what the card does, not for requests to an address or deleting data", () => {
  for (const type of ["lookup", "translateBatch", "wiki", "speak", "ai", "cardHas", "cardToggle", "openOptions"]) assert.equal(rules.allowedFromOutsideText({ type }), true, type);
  for (const type of ["aiTest", "ollamaModels", "cardRemove", "packInstall", "packRemove", "appLink", "reviewGrade", "reviewQueue", "getSettings", "__proto__", "constructor"]) {
    assert.equal(rules.allowedFromOutsideText({ type }), false, type);
  }
  for (const odd of [null, undefined, "lookup", 5, {}]) assert.equal(rules.allowedFromOutsideText(odd), false, String(odd));
});

test("Firefox's Wikipedia request: at most 6 short titles and a language code, or nothing", () => {
  assert.deepEqual(rules.wikiSummaryArgs({ titles: ["Paris", "باريس"], lang: "ar" }), { titles: ["Paris", "باريس"], lang: "ar" });
  assert.equal(rules.wikiSummaryArgs({ titles: ["x"], lang: "ar/../x" }), null);
  assert.equal(rules.wikiSummaryArgs({ titles: [], lang: "en" }), null);
  assert.equal(rules.wikiSummaryArgs(null), null);
  const a = rules.wikiSummaryArgs({ titles: ["a", 5, "", " ", "x".repeat(121), "b", "c", "d", "e", "f", "g"], lang: "en" });
  assert.deepEqual(a.titles, ["a", "b", "c", "d", "e", "f"]);
});

test("Settings' address: section and welcome", () => {
  assert.deepEqual(rules.optionsTarget("#journal"), { search: "", hash: "journal" });
  assert.deepEqual(rules.optionsTarget("?welcome=1"), { search: "welcome=1", hash: "ai" });
  assert.deepEqual(rules.optionsTarget("?welcome=1&perm=1#wikipedia"), { search: "welcome=1&perm=1", hash: "wikipedia" });
  assert.deepEqual(rules.optionsTarget(""), { search: "", hash: "" });
});

test("the card window stays on the mouse's monitor: below the mouse, above it near the bottom, the Write button over it", () => {
  const workArea = { x: 0, y: 0, width: 1920, height: 1040 }, w = 480, h = 620;
  const inside = b => b.x >= workArea.x && b.y >= workArea.y && b.x + b.width <= workArea.x + workArea.width && b.y + b.height <= workArea.y + workArea.height;
  const top = rules.cardPlacement({ cursor: { x: 960, y: 100 }, workArea, w, h });
  assert.deepEqual(top, { bounds: { x: 720, y: 116, width: w, height: h }, point: { x: 240, y: 12 } }, "below the mouse");
  const low = rules.cardPlacement({ cursor: { x: 960, y: 1000 }, workArea, w, h });
  assert.ok(inside(low.bounds) && low.bounds.y + low.point.y < 1000, "above the mouse near the bottom");
  const corner = rules.cardPlacement({ cursor: { x: 5, y: 5 }, workArea, w, h });
  assert.deepEqual([corner.bounds.x, corner.bounds.y], [0, 21], "never off the screen's left edge");
  const pill = rules.cardPlacement({ cursor: { x: 960, y: 300 }, workArea, w, h, pill: true });
  assert.deepEqual(pill.point, { x: 240, y: 72 }, "the Write button: the mouse 72 px from the window's top");
  const lowPill = rules.cardPlacement({ cursor: { x: 960, y: 1000 }, workArea, w, h, pill: true });
  assert.ok(inside(lowPill.bounds) && lowPill.point.y > 72 && lowPill.point.y < h, "near the bottom the window stays on screen, the button still under the mouse");
  const second = { x: -1920, y: 0, width: 1920, height: 1040 }; // a monitor to the left
  assert.ok(rules.cardPlacement({ cursor: { x: -10, y: 300 }, workArea: second, w, h }).bounds.x + w <= 0, "on the left monitor, not across two");
  const panel = rules.panelPlacement({ cursor: { x: 1910, y: 1030 }, workArea, w: 380, h: 460 });
  assert.ok(inside(panel) && panel.y + 460 <= 1030 - 16, "the panel goes above the mouse when there's no room below");
});

section("what's new (shared/changelog.js, tools/release-notes.mjs)");
const { changelog, releaseNotes } = await import("./release-notes.mjs");
const version = JSON.parse(readFileSync(new URL("../manifest.json", import.meta.url), "utf8")).version;
const semver = v => v.split(".").map(Number);
const newer = (a, b) => { const [x, y] = [semver(a), semver(b)]; for (let i = 0; i < 3; i++) if (x[i] !== y[i]) return x[i] > y[i]; return false; };

test("every version has a date and notes in both languages, newest first", () => {
  const log = changelog();
  assert.ok(log.length > 5);
  log.forEach((e, i) => {
    assert.match(e.v, /^\d+\.\d+\.\d+$/, e.v);
    assert.match(e.date, /^\d{4}-\d{2}-\d{2}$/, e.v);
    assert.ok(e.notes.length, e.v + " has notes");
    for (const [ar, en] of e.notes) {
      assert.match(ar, /[؀-ۿ]/, e.v + ": the first of each pair is Arabic");
      assert.ok(en && !/[؀-ۿ]/.test(en), e.v + ": the second is English");
    }
    if (i) assert.ok(newer(log[i - 1].v, e.v) && log[i - 1].date >= e.date, `${log[i - 1].v} comes before ${e.v}`);
  });
});

test("the version about to ship has its notes (the release workflow needs them)", () => {
  const desktop = JSON.parse(readFileSync(new URL("../desktop/package.json", import.meta.url), "utf8")).version;
  assert.equal(desktop, version, "manifest.json and desktop/package.json move together");
  const notes = releaseNotes(version);
  assert.ok(notes, `shared/changelog.js has no entry for ${version}`);
  assert.match(notes, /^## What's new\n\n- /);
  assert.match(notes, /<div dir="rtl">\n\n## ما الجديد\n\n- /);
  assert.equal(releaseNotes("0.0.1"), null);
});

section("language packs on the desktop (pack-store.js), and a real built pack");
const { createPackStore } = require("../desktop/pack-store.js");

test("the desktop keeps pack parts as files; only pack keys become file names", async () => {
  const store = createPackStore(path.join(dir, "packs"));
  await store.setMany({ "fr/meta": { lang: "fr" }, "fr/ma": { w: { maison: { s: [] } } } });
  assert.deepEqual(await store.get("fr/ma"), { w: { maison: { s: [] } } });
  assert.equal(await store.get("fr/zz"), undefined);
  await assert.rejects(store.setMany({ "../../evil": {} }), /bad pack key/);
  await assert.rejects(store.setMany({ "fr/../../x": {} }), /bad pack key/);
  await assert.rejects(store.removePrefix("../"), /bad pack prefix/);
  await store.removePrefix("fr/");
  assert.equal(await store.get("fr/meta"), undefined);
});

const built = new URL("../dist-packs/tr.json.gz", import.meta.url);
test("a pack built by tools/build_packs.py installs and finds words and their forms (dist-packs/tr.json.gz)", async () => {
  if (!existsSync(built)) { console.log("    (skipped: build it with  python tools/build_packs.py tr)"); return; }
  const file = readFileSync(built);
  const ctx = vm.createContext({
    LamhaPackStore: createPackStore(path.join(dir, "real")), Response, TransformStream, DecompressionStream, console,
    fetch: async () => new Response(file, { headers: { "content-length": String(file.length) } })
  });
  vm.runInContext(readFileSync(new URL("../packs.js", import.meta.url), "utf8"), ctx);
  const packs = vm.runInContext("LamhaPacks", ctx);
  const meta = await packs.install("tr");
  assert.ok(meta.words > 20000, meta.words + " words");
  const ev = await packs.find("tr", "evler");
  assert.deepEqual({ word: ev.word, form: ev.form }, { word: "ev", form: "evler" });
  assert.ok(ev.entry.s[0][1].length > 10, "a definition in Turkish");
  assert.equal((await packs.find("tr", "KİTAP")).word, "kitap", "Turkish capitals (İ) lowercase the Turkish way");
}, { windows: false });

section("offline Wikipedia (zim.js, wiki-library.js)");
const { ZimFile } = require("../desktop/zim.js");
const { WikiLibrary, leadText, isDisambiguation, parseCatalog, parseMeta4 } = require("../desktop/wiki-library.js");
const { writeZim, hasZstd } = await import("./zim-fixture.mjs");
const { createHash } = await import("node:crypto");

/** An article as Kiwix's files have it: a lead with an info box, references, coordinates and a hatnote, then more. */
const article = (title, lead, { disambig = false, img = "" } = {}) => `<!DOCTYPE html><html><head><title>${title}</title>` +
  `<link rel="canonical" href="https://ar.wikipedia.org/wiki/${encodeURIComponent(title)}"></head><body>` +
  (disambig ? '<meta property="mw:PageProp/disambiguation">' : "") +
  `<section data-mw-section-id="0"><style>.mw-parser-output p{}</style>` +
  `<div class="hatnote">لمعانٍ أخرى انظر <a class="mw-disambig" href="./x">صفحة التوضيح</a></div>` +
  `<table class="infobox"><tr><td><p>This paragraph is inside the info box, not the lead.</p></td></tr></table>${img}` +
  `<p><span id="coordinates">48°51′N 2°21′E</span></p><p>${lead}</p></section>` +
  `<section data-mw-section-id="1"><p>A later section, long enough to count as a paragraph of its own.</p></section></body></html>`;
const PARIS = "باريس<sup class=\"reference\"><a>[1]</a></sup> هي عاصمة فرنسا &amp; أكبر مدنها (<span></span>) وتقع على ضفاف نهر السين.";
const arFile = path.join(dir, "wikipedia_ar_top_mini_2026-07.zim");
const enFile = path.join(dir, "wikipedia_en_top_maxi_2026-07.zim");
const WEBP = Buffer.concat([Buffer.from("RIFF"), Buffer.alloc(4), Buffer.from("WEBPVP8 "), Buffer.alloc(40, 1)]);
if (hasZstd) {
  writeZim(arFile, { name: "wikipedia_ar_top", lang: "ara", flavour: "mini", title: "أفضل ما في ويكيبيديا", main: "باريس", entries: [
    { path: "باريس", html: article("باريس", PARIS) },
    { path: "Paris", redirect: "باريس" },
    { path: "الشمس", html: article("الشمس", "الشمس هي النجم المركزي للمجموعة الشمسية، وحولها تدور الأرض.") },
    { path: "عين", redirect: "عين_(توضيح)" },
    { path: "عين_(توضيح)", title: "عين (توضيح)", html: article("عين (توضيح)", "قد تشير كلمة عين إلى معانٍ كثيرة في اللغة والطب والجغرافيا.", { disambig: true }) },
    { path: "حلقة", redirect: "حلقة_2" }, { path: "حلقة_2", redirect: "حلقة" }
  ] });
  writeZim(enFile, { name: "wikipedia_en_top", lang: "eng", flavour: "maxi", entries: [
    { path: "Photosynthesis", html: article("Photosynthesis", "Photosynthesis is the process plants use to turn light into chemical energy.",
      { img: '<img src="./_assets_/abc/Leaf.jpg" width="250" height="180"><img src="./_assets_/abc/Icon.png" width="20">' }) },
    { path: "_assets_/abc/Leaf.jpg", data: WEBP, mime: "image/webp" }
  ] });
}
const zimTest = (name, fn) => test(name, async () => {
  if (!hasZstd) { console.log("    (skipped: this Node has no Zstandard; Electron's has)"); return; }
  await fn();
});

zimTest("a .zim file: paths, redirects, title search and metadata; pictures are read from their stored cluster", async () => {
  const z = await ZimFile.open(arFile);
  try {
    assert.equal(z.contentNs, "C");
    assert.equal(await z.meta("Name"), "wikipedia_ar_top");
    const paris = await z.find("C", "Paris");
    assert.equal(paris.redirect !== undefined, true);
    const target = await z.resolve(paris);
    assert.equal(target.path, "باريس");
    assert.match((await z.content(paris)).toString(), /عاصمة فرنسا/);
    assert.equal(await z.find("C", "Lyon"), null);
    assert.deepEqual((await z.titlesStartingWith("عين", 5)).map(e => e.title), ["عين", "عين (توضيح)"]);
    assert.equal((await z.main()).path, "باريس");
    await assert.rejects(z.content(await z.find("C", "حلقة")), e => e.code === "redirect_loop");
  } finally { await z.close(); }
  const en = await ZimFile.open(enFile);
  try { assert.deepEqual(await en.content(await en.find("C", "_assets_/abc/Leaf.jpg")), WEBP); } finally { await en.close(); }
});

zimTest("a broken or foreign file is refused with a reason, never a crash", async () => {
  const notZim = path.join(dir, "notes.zim");
  writeFileSync(notZim, "just some text, not a zim file at all ".repeat(5));
  await assert.rejects(ZimFile.open(notZim), e => e.code === "not_zim");
  const bytes = Buffer.from(readFileSync(arFile));
  const clusterPtrPos = Number(bytes.readBigUInt64LE(48));
  bytes.writeBigUInt64LE(BigInt(bytes.length * 4), clusterPtrPos); // the text cluster points past the end
  const broken = path.join(dir, "broken.zim");
  writeFileSync(broken, bytes);
  const z = await ZimFile.open(broken);
  try { await assert.rejects(z.content(await z.find("C", "باريس")), e => /^bad_/.test(e.code)); } finally { await z.close(); }
});

test("the card's opening paragraph: no references, boxes, styles or coordinates; later sections left out", () => {
  const lead = leadText(article("باريس", PARIS));
  assert.equal(lead, "باريس هي عاصمة فرنسا & أكبر مدنها وتقع على ضفاف نهر السين.");
  assert.ok(leadText(`<section data-mw-section-id="0"><p>${"كلمة ".repeat(400)}</p></section>`).length <= 901, "long leads are cut at a word");
});

test("only the page's own mark makes a disambiguation page (a normal article links to one)", () => {
  assert.equal(isDisambiguation(article("باريس", PARIS)), false, "the hatnote's mw-disambig link");
  assert.equal(isDisambiguation(article("عين", "…", { disambig: true })), true);
});

zimTest("the card's summary: the word, capitalised, redirects, the Arabic article (شمس → الشمس); no disambiguation pages; pictures from files that have them", async () => {
  const lib = new WikiLibrary({ dataDir: path.join(dir, "lib-summary") });
  await lib.init();
  await lib.addFile(arFile);
  await lib.addFile(enFile);
  try {
    assert.deepEqual(lib.langs().sort(), ["ar", "en"]);
    const paris = await lib.summary(["Paris"], "ar");
    assert.equal(paris.title, "باريس");
    assert.equal(paris.url, "https://ar.wikipedia.org/wiki/" + encodeURIComponent("باريس"));
    assert.deepEqual([paris.thumb, paris.offline.date, paris.offline.path], ["", "2026-07-10", "باريس"], "a mini file has no pictures");
    assert.equal((await lib.summary(["شمس"], "ar")).title, "الشمس");
    assert.equal(await lib.summary(["عين"], "ar"), null);
    assert.equal(await lib.summary(["حلقة", "باريس"], "ar").then(r => r.title), "باريس", "a broken entry: the next title");
    const photo = await lib.summary(["photosynthesis"], "en");
    assert.equal(photo.title, "Photosynthesis");
    assert.equal(photo.thumb, "data:image/webp;base64," + WEBP.toString("base64"), "the lead's first real picture, not the icon");
    assert.equal(await lib.summary(["Paris"], "fr"), null, "no French file");
  } finally { await lib.closeAll(); }
});

zimTest("the reader's calls: articles (redirects, the main page, any file), pictures only as pictures, title suggestions, a random article", async () => {
  const lib = new WikiLibrary({ dataDir: path.join(dir, "lib-reader") });
  await lib.init();
  await lib.addFile(arFile);
  await lib.addFile(enFile);
  const ar = lib.list().files.find(f => f.lang === "ar").id;
  try {
    const paris = await lib.article(ar, "Paris");
    assert.deepEqual([paris.path, paris.title, paris.lang, paris.flavour], ["باريس", "باريس", "ar", "mini"], "a redirect: the article it points to");
    assert.match(paris.html, /عاصمة فرنسا/);
    assert.equal((await lib.article(ar, "")).path, "باريس", "no path: the main page");
    assert.equal((await lib.article("", "Photosynthesis")).lang, "en", "no file named: whichever has it");
    assert.equal(await lib.article(ar, "Lyon"), null);
    await assert.rejects(lib.article("file:nope", "x"), e => e.code === "no_file");
    const en = lib.list().files.find(f => f.lang === "en").id;
    assert.deepEqual(await lib.asset(en, "_assets_/abc/Leaf.jpg"), { mime: "image/webp", data: WEBP });
    assert.equal(await lib.asset(en, "Photosynthesis"), null, "an article is never served as a picture");
    assert.equal(await lib.asset(en, "../../../etc/passwd"), null);
    assert.deepEqual((await lib.suggest("عين", { lang: "ar" })).map(s => s.title), ["عين", "عين (توضيح)"], "the word itself first, then longer titles");
    assert.deepEqual((await lib.suggest("photo")).map(s => [s.title, s.lang]), [["Photosynthesis", "en"]], "capitalised like Wikipedia's titles");
    assert.deepEqual(await lib.suggest("  "), []);
    const r = await lib.random(ar);
    assert.ok(r && r.fileId === ar && (await lib.article(ar, r.path)).html, "a real article, not a redirect");
  } finally { await lib.closeAll(); }
});

test("Kiwix's catalog: the files of one language, with sizes and kinds; odd names and mirrors are dropped", () => {
  const entries = parseCatalog(opds([
    { file: "wikipedia_ar_top_mini_2026-07.zim", size: 226337792, name: "wikipedia_ar_top", flavour: "mini", title: "ويكيبيديا &amp; أكثر" },
    { file: "../../evil.zim", size: 1, name: "wikipedia_ar_top", flavour: "mini" }
  ]));
  assert.equal(entries.length, 1);
  assert.deepEqual({ ...entries[0], summary: undefined }, { id: "wikipedia_ar_top_mini_2026-07", file: "wikipedia_ar_top_mini_2026-07.zim", name: "wikipedia_ar_top",
    flavour: "mini", lang: "ar", scope: "top", title: "ويكيبيديا & أكثر", summary: undefined, articles: 3, size: 226337792, date: "2026-07-10", pictures: false });
  const f = "wikipedia_ar_top_mini_2026-07.zim";
  assert.deepEqual(parseMeta4(`<url priority="2">https://b.example/${f}</url><url priority="1">https://a.example/x/${f}</url><url>http://plain.example/${f}</url><url>https://c.example/other.zim</url>`, f),
    [`https://a.example/x/${f}`, `https://b.example/${f}`]);
});

/** Kiwix's catalog feed (OPDS) for these files. */
function opds(list) {
  return `<feed>${list.map(e => `<entry><title>${e.title || "ويكيبيديا"}</title><summary>x</summary><language>ara</language><name>${e.name}</name>` +
    `<flavour>${e.flavour}</flavour><tags>wikipedia;_pictures:no</tags><articleCount>3</articleCount><dc:issued>2026-07-10T00:00:00Z</dc:issued>` +
    `<link rel="http://opds-spec.org/acquisition/open-access" type="application/x-zim" href="https://lb.download.kiwix.org/zim/wikipedia/${e.file}.meta4" length="${e.size}" /></entry>`).join("")}</feed>`;
}
/** Kiwix: its catalog, checksums, mirror lists and mirrors. `broken`: the fastest mirror drops the connection halfway.
 *  `listed`: the catalog's size (the real one is rounded up to 512-byte blocks); `metaSize`: the .meta4 has <size>. */
function kiwix(bytes, { broken = false, badSha = false, listed = bytes.length, metaSize = false } = {}) {
  const file = path.basename(arFile);
  const calls = [];
  const fetch = async (url, init = {}) => {
    const range = (init.headers || {}).Range || "";
    const u = new URL(url);
    calls.push({ host: u.host, range });
    if (u.host === "library.kiwix.org") return new Response(opds([{ file, size: listed, name: "wikipedia_ar_top", flavour: "mini" }]));
    const name = decodeURIComponent(u.pathname.split("/").pop());
    if (name === file + ".sha256") return new Response((badSha ? "0".repeat(64) : createHash("sha256").update(bytes).digest("hex")) + "  " + file);
    if (name === file + ".meta4") return new Response(`<file name="${file}">${metaSize ? `<size>${bytes.length}</size>` : ""}<url priority="1">https://first.example/${file}</url><url priority="2">https://second.example/${file}</url></file>`);
    if (name !== file) return new Response("", { status: 404 });
    const [, from = "0", to = ""] = /bytes=(\d+)-(\d*)/.exec(range) || [];
    const part = bytes.subarray(Number(from), to ? Number(to) + 1 : bytes.length);
    const probe = !!to;
    if (probe && u.host !== "first.example") await sleep(30); // the first mirror seems fastest
    if (broken && u.host === "first.example" && !probe) {
      let sent = false; // half the file, then the connection drops
      return new Response(new ReadableStream({ pull(c) { if (sent) c.error(new TypeError("connection reset")); else { sent = true; c.enqueue(part.subarray(0, part.length >> 1)); } } }),
        { status: range ? 206 : 200, headers: { "content-length": String(part.length) } });
    }
    const headers = { "content-length": String(part.length), ...(range && { "content-range": `bytes ${from}-${Number(from) + part.length - 1}/${bytes.length}` }) };
    return new Response(part, { status: range ? 206 : 200, headers });
  };
  return { calls, fetch };
}
const sleep = ms => new Promise(r => setTimeout(r, ms));
async function until(check, ms = 15000) {
  for (const t0 = Date.now(); !check(); await sleep(20)) if (Date.now() - t0 > ms) throw new Error("timed out");
}

zimTest("a download: the fastest mirror first, the next one when it breaks (from where it stopped), checked against Kiwix's SHA-256, then used", async () => {
  const bytes = readFileSync(arFile);
  const net = kiwix(bytes, { broken: true });
  const lib = new WikiLibrary({ dataDir: path.join(dir, "lib-dl"), fetch: net.fetch });
  await lib.init();
  try {
    const [entry] = await lib.catalog("ar");
    assert.equal(entry.id, "wikipedia_ar_top_mini_2026-07");
    await assert.rejects(lib.download("wikipedia_xx_evil"), e => e.code === "unknown", "only what the catalog listed");
    await lib.download(entry.id);
    await until(() => lib.list().files.length === 1);
    const half = bytes.length >> 1;
    assert.ok(net.calls.some(c => c.host === "first.example" && !c.range), "started on the fastest mirror");
    assert.ok(net.calls.some(c => c.host !== "first.example" && c.range === `bytes=${half}-`), "carried on elsewhere from where it broke");
    const [f] = lib.list().files;
    assert.deepEqual([f.id, f.lang, f.flavour, f.imported, f.missing], [entry.id, "ar", "mini", false, false]);
    assert.deepEqual(readFileSync(f.file), bytes);
    assert.equal(existsSync(f.file + ".part"), false);
    assert.equal((await lib.summary(["Paris"], "ar")).title, "باريس");
    await assert.rejects(lib.download(entry.id), e => e.code === "have_it");
    await lib.remove(f.id);
    assert.equal(existsSync(f.file), false, "a downloaded file is deleted");
  } finally { await lib.closeAll(); }
});

zimTest("Kiwix's catalog rounds sizes up to 512-byte blocks: the download takes the exact size from the .meta4 or the mirror", async () => {
  const bytes = readFileSync(arFile);
  const listed = (Math.floor(bytes.length / 512) + 1) * 512; // never the real size, as with Kiwix's files
  for (const metaSize of [true, false]) {
    const lib = new WikiLibrary({ dataDir: path.join(dir, "lib-rounded-" + metaSize), fetch: kiwix(bytes, { listed, metaSize }).fetch });
    await lib.init();
    try {
      const [entry] = await lib.catalog("ar");
      assert.equal(entry.size, listed);
      await lib.download(entry.id);
      await until(() => lib.list().files.length === 1 || (lib.list().downloads[0] || {}).state === "failed");
      assert.deepEqual(lib.list().downloads.map(d => d.error), [], metaSize ? "size from the .meta4" : "size from the mirror's answer");
      assert.deepEqual(readFileSync(lib.list().files[0].file), bytes);
    } finally { await lib.closeAll(); }
  }
  const net = kiwix(bytes, { listed: bytes.length + 5e6 }); // not this file at all
  const far = new WikiLibrary({ dataDir: path.join(dir, "lib-far"), fetch: net.fetch });
  await far.init();
  await far.catalog("ar");
  await far.download("wikipedia_ar_top_mini_2026-07");
  await until(() => net.calls.some(c => c.host.endsWith(".example") && !c.range)); // a mirror was asked for the whole file…
  await sleep(100);
  assert.equal(far.list().downloads[0].got, 0, "…and a file of another size is never written");
  await far.cancel("wikipedia_ar_top_mini_2026-07");
  await far.closeAll();
});

zimTest("a damaged download is deleted and says so; a download that was running when the app closed carries on at the next start", async () => {
  const bytes = readFileSync(arFile);
  const bad = new WikiLibrary({ dataDir: path.join(dir, "lib-bad"), fetch: kiwix(bytes, { badSha: true }).fetch });
  await bad.init();
  await bad.catalog("ar");
  await bad.download("wikipedia_ar_top_mini_2026-07");
  await until(() => bad.list().downloads[0].state === "failed");
  assert.equal(bad.list().downloads[0].error, "checksum");
  assert.equal(existsSync(path.join(bad.folder, path.basename(arFile) + ".part")), false);

  const data = path.join(dir, "lib-restart");
  const folder = path.join(data, "wikipedia");
  mkdirSync(folder, { recursive: true });
  const half = 1000;
  writeFileSync(path.join(folder, path.basename(arFile) + ".part"), bytes.subarray(0, half));
  writeFileSync(path.join(data, "wikipedia.json"), JSON.stringify({ folder: "", files: [], downloads: [{
    id: "wikipedia_ar_top_mini_2026-07", file: path.basename(arFile), lang: "ar", name: "wikipedia_ar_top", scope: "top", flavour: "mini",
    date: "2026-07-10", size: bytes.length, got: half, state: "running", part: path.join(folder, path.basename(arFile) + ".part"),
    sha256: createHash("sha256").update(bytes).digest("hex"), mirrors: ["https://second.example/" + path.basename(arFile)]
  }] }));
  const net = kiwix(bytes);
  const lib = new WikiLibrary({ dataDir: data, fetch: net.fetch });
  await lib.init();
  try {
    await until(() => lib.list().files.length === 1);
    assert.ok(net.calls.some(c => c.range === `bytes=${half}-`), "from where it was");
    assert.deepEqual(readFileSync(lib.list().files[0].file), bytes);
  } finally { await lib.closeAll(); }
});

zimTest("a file the user adds: used where it is, only Wikipedia; removing it forgets it without deleting it", async () => {
  const lib = new WikiLibrary({ dataDir: path.join(dir, "lib-add") });
  await lib.init();
  const other = path.join(dir, "wiktionary_ar_all_nopic_2026-07.zim");
  writeZim(other, { name: "wiktionary_ar_all", lang: "ara", entries: [{ path: "كتاب", html: article("كتاب", "الكتاب مجموعة من الأوراق المكتوبة المجموعة بين غلافين.") }] });
  await assert.rejects(lib.addFile(other), e => e.code === "not_wikipedia");
  await assert.rejects(lib.addFile(path.join(dir, "notes.zim")), e => e.code === "not_zim");
  const info = await lib.addFile(arFile);
  assert.deepEqual([info.lang, info.scope, info.flavour, info.articles], ["ar", "top", "mini", 7], "the file's own title list (redirects count, as in Kiwix's)");
  const [f] = lib.list().files;
  assert.equal(f.imported, true);
  await lib.remove(f.id);
  assert.equal(lib.list().files.length, 0);
  assert.ok(existsSync(arFile), "the user's own file stays");
  await lib.closeAll();
});

const realZim = process.env.LAMHA_TEST_ZIM; // a real Arabic file from Kiwix, e.g. wikipedia_ar_top_mini_2026-07.zim
test("a real Arabic Wikipedia from Kiwix (LAMHA_TEST_ZIM)", async () => {
  if (!realZim) { console.log("    (skipped: set LAMHA_TEST_ZIM to a downloaded wikipedia_ar_*.zim)"); return; }
  const lib = new WikiLibrary({ dataDir: path.join(dir, "lib-real") });
  await lib.init();
  await lib.addFile(realZim);
  try {
    for (const [word, title] of [["باريس", "باريس"], ["Paris", "باريس"], ["شمس", "الشمس"], ["بنك", "مصرف"], ["تفاحة", "تفاح"]]) {
      const r = await lib.summary([word], "ar");
      assert.equal(r && r.title, title, word);
      assert.ok(r.extract.length > 80 && !/\[\d+\]|<|&[a-z]+;/.test(r.extract), word + ": clean text");
    }
    assert.equal(await lib.summary(["عين"], "ar"), null, "a disambiguation page");
  } finally { await lib.closeAll(); }
});

section("the Firefox extension's link to the app (native-bridge.js)");
const { NativeBridge, HOST, EXTENSION_ID } = require("../desktop/native-bridge.js");
const net = await import("node:net");
const { spawn } = await import("node:child_process");

/** A bridge in a folder of its own, with a registry that only records what it was told. */
function makeBridge(handlers) {
  const reg = [];
  const bridge = new NativeBridge({
    dir: path.join(dir, "firefox-" + Math.random().toString(36).slice(2)), launch: { app: "C:\\Program Files\\Lamha\\Lamha.exe", args: ["--hidden"] },
    handlers, register: async f => { reg.push(["add", f]); }, unregister: async () => { reg.push(["delete"]); }
  });
  return { bridge, reg };
}
/** Talks to the bridge's pipe as the host does: a line each way. */
function pipeClient(bridge) {
  const sock = net.connect("\\\\.\\pipe\\" + bridge.pipe);
  let buf = "";
  const lines = [];
  sock.setEncoding("utf8");
  sock.on("data", d => { buf += d; let i; while ((i = buf.indexOf("\n")) >= 0) { lines.push(JSON.parse(buf.slice(0, i))); buf = buf.slice(i + 1); } });
  const closed = new Promise(r => sock.on("close", r));
  sock.on("error", () => {});
  return { sock, lines, closed, send: m => sock.write(JSON.stringify(m) + "\n"), ready: new Promise(r => sock.on("connect", r)) };
}

test("the host is registered for Lamha's extension only, and its files say how to start the app", async () => {
  const { bridge, reg } = makeBridge({});
  await bridge.enable();
  try {
    const manifest = JSON.parse(readFileSync(bridge.manifestPath, "utf8"));
    assert.deepEqual(reg, [["add", bridge.manifestPath]]);
    assert.deepEqual([manifest.name, manifest.type, manifest.allowed_extensions], [HOST, "stdio", [EXTENSION_ID]]);
    assert.equal(manifest.path, path.join(bridge.dir, "host.bat"));
    const cfg = JSON.parse(readFileSync(path.join(bridge.dir, "config.json"), "utf8"));
    assert.deepEqual([cfg.pipe, cfg.token.length, cfg.app, cfg.args], [bridge.pipe, 48, "C:\\Program Files\\Lamha\\Lamha.exe", "--hidden"]);
    assert.match(readFileSync(path.join(bridge.dir, "host.bat"), "utf8"), /powershell\.exe" .*-File "%~dp0host\.ps1"/);
    assert.match(bridge.pipe, /^lamha-firefox-[0-9a-f]{32}$/, "a name nobody can work out from the folder (another Windows account could take it first)");
    const other = makeBridge({}).bridge;
    await other.enable();
    assert.notEqual(other.pipe, bridge.pipe, "random, not from the data folder");
    await other.disable();
  } finally { await bridge.disable(); }
  assert.deepEqual(reg.at(-1), ["delete"]);
  assert.equal(existsSync(bridge.dir), false, "turned off: the files go too");
}, { windows: true });

test("the pipe answers only after the token; requests are answered in order, unknown ones refused, errors by their code", async () => {
  const { bridge } = makeBridge({
    hello: async () => ({ app: "lamha" }),
    slow: async m => { await new Promise(r => setTimeout(r, 60)); return m.n; },
    broken: async () => { throw Object.assign(new Error("x"), { code: "no_file" }); }
  });
  await bridge.enable();
  try {
    const stranger = pipeClient(bridge);
    await stranger.ready;
    stranger.send({ token: "0".repeat(48) });
    stranger.send({ id: 1, type: "hello" });
    await stranger.closed;
    assert.deepEqual(stranger.lines, [], "a wrong token: nothing, and the pipe closes");
    const host = pipeClient(bridge);
    await host.ready;
    host.send({ token: JSON.parse(readFileSync(path.join(bridge.dir, "config.json"), "utf8")).token });
    host.send({ id: 1, type: "slow", n: "first" });
    host.send({ id: 2, type: "hello" });
    host.send({ id: 3, type: "__proto__" });
    host.send({ id: 4, type: "broken" });
    for (let i = 0; i < 50 && host.lines.length < 4; i++) await new Promise(r => setTimeout(r, 20));
    assert.deepEqual(host.lines, [{ id: 1, ok: true, data: "first" }, { id: 2, ok: true, data: { app: "lamha" } }, { id: 3, ok: false, error: "unknown" }, { id: 4, ok: false, error: "no_file" }]);
    assert.equal(bridge.connected, true);
    host.sock.destroy();
  } finally { await bridge.disable(); }
}, { windows: true });

test("Firefox's side, for real: host.bat → PowerShell → the pipe, 4-byte-length messages; without the app it says so", async () => {
  const { bridge } = makeBridge({ echo: async m => m.text });
  await bridge.enable();
  const host = spawn("cmd.exe", ["/d", "/s", "/c", `"${path.join(bridge.dir, "host.bat")}"`], { windowsHide: true, windowsVerbatimArguments: true });
  const replies = [];
  let out = Buffer.alloc(0);
  host.stdout.on("data", d => {
    out = Buffer.concat([out, d]);
    while (out.length >= 4 && out.length >= 4 + out.readUInt32LE(0)) { replies.push(JSON.parse(out.subarray(4, 4 + out.readUInt32LE(0)).toString("utf8"))); out = out.subarray(4 + out.readUInt32LE(0)); }
  });
  const send = m => { const b = Buffer.from(JSON.stringify(m)); const n = Buffer.alloc(4); n.writeUInt32LE(b.length); host.stdin.write(Buffer.concat([n, b])); };
  const wait = async n => { for (let i = 0; i < 200 && replies.length < n; i++) await new Promise(r => setTimeout(r, 50)); };
  try {
    send({ id: 1, type: "echo", text: "باريس — Paris" });
    await wait(1);
    assert.deepEqual(replies[0], { id: 1, ok: true, data: "باريس — Paris" });
    await bridge.close();
    send({ id: 2, type: "echo", text: "x" }); // the app quit: the host answers for it (and never starts it: no launch)
    await wait(2);
    assert.deepEqual(replies[1], { ok: false, error: "no_app", id: 2 });
    const before = bridge.pipe;
    await bridge.enable(); // the app starts again: a new pipe name and token; the host, still running, finds them
    assert.notEqual(bridge.pipe, before);
    send({ id: 3, type: "echo", text: "back" });
    await wait(3);
    assert.deepEqual(replies[2], { id: 3, ok: true, data: "back" });
  } finally {
    host.stdin.end();
    await new Promise(r => host.on("exit", r));
    await bridge.disable();
  }
}, { windows: true });

section("the helper process (Windows)");
const ps = (args, input) => spawnSync("powershell.exe", ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", ...args], { encoding: "utf8", input, timeout: 60000 });

test("its PowerShell script parses", () => {
  const file = path.join(dir, "helper.ps1");
  writeFileSync(file, "﻿" + SCRIPT, "utf8");
  const r = ps(["-Command", `$e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile('${file}', [ref]$null, [ref]$e); $e | ForEach-Object { $_.Message }`]);
  assert.equal(r.status, 0, r.stderr);
  assert.equal(r.stdout.trim(), "", "parse errors");
}, { windows: true });

test("its C# mouse hook compiles with Windows' own .NET", () => {
  const cs = /\$MouseSource = @'\r?\n([\s\S]*?)\r?\n'@/.exec(SCRIPT);
  assert.ok(cs, "the C# source is in the script");
  const file = path.join(dir, "mouse.cs");
  writeFileSync(file, cs[1], "utf8");
  const r = ps(["-Command", `Add-Type -TypeDefinition ([IO.File]::ReadAllText('${file}')); [LamhaMouse].GetMethod('Start') -ne $null`]);
  assert.equal(r.status, 0, r.stderr);
  assert.equal(r.stdout.trim(), "True");
}, { windows: true });

test("it answers a text-box question; a click on no box gets no button", async () => {
  const helper = new UiaContext();
  try {
    assert.equal(await helper.field(-30000, -30000, L.pair("c.writeNewHere"), 20000), null);
    assert.ok(helper.proc, "still running");
  } finally {
    helper.stop();
  }
}, { windows: true });

let passed = 0, failed = 0, skipped = 0, filtered = 0;
for (const { name, fn, title, windows } of queue) {
  if (title) { printTitle(title); continue; }
  if (!wanted(name)) { filtered++; continue; }
  if (windows && process.platform !== "win32") { skipped++; report("  - " + name + " (Windows only)"); continue; }
  try { await fn(); passed++; report("  ✓ " + name); }
  catch (err) { failed++; report("  ✗ " + name + "\n    " + String(err.message).split("\n").join("\n    ")); }
}
rmSync(dir, { recursive: true, force: true });
console.log(`${quiet ? "" : "\n"}${passed}/${passed + failed} passed${skipped ? `, ${skipped} skipped (Windows only)` : ""}${notRun(filtered)}`);
process.exit(failed ? 1 : 0);
