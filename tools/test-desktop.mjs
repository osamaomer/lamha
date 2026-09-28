// Tests for the desktop app's Write button on a double-click (desktop/double-click.js, desktop/uia-context.js):
// the decisions in plain Node, and on Windows the helper itself: its PowerShell parses, its C# mouse hook compiles,
// and it answers a text-box question. The real double-click in a real app is in the desktop self-test (npm run smoke).
//   node tools/test-desktop.mjs
import { createRequire } from "node:module";
import { spawnSync } from "node:child_process";
import { readFileSync, writeFileSync, mkdtempSync, rmSync, existsSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import vm from "node:vm";
import assert from "node:assert/strict";

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

let passed = 0, failed = 0, skipped = 0;
for (const { name, fn, title, windows } of queue) {
  if (title) { console.log(title); continue; }
  if (windows && process.platform !== "win32") { skipped++; console.log("  - " + name + " (Windows only)"); continue; }
  try { await fn(); passed++; console.log("  ✓ " + name); }
  catch (err) { failed++; console.log("  ✗ " + name + "\n    " + String(err.message).split("\n").join("\n    ")); }
}
rmSync(dir, { recursive: true, force: true });
console.log(`\n${passed}/${passed + failed} passed${skipped ? `, ${skipped} skipped (Windows only)` : ""}`);
process.exit(failed ? 1 : 0);
