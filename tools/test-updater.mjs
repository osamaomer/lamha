// Tests for the desktop app's updater (desktop/updater.js) with a fake electron-updater and a fake GitHub API.
//   node tools/test-updater.mjs
import { createRequire } from "node:module";
import { EventEmitter } from "node:events";
import assert from "node:assert/strict";
import { readFileSync, mkdtempSync, existsSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createHash } from "node:crypto";
import vm from "node:vm";
import { quiet, wanted, report, notRun } from "./test-args.mjs";

const { createUpdater, newer, lamha3Setup, LAMHA3_SETUP_FILE } = createRequire(import.meta.url)("../desktop/updater.js");

// the real interface strings (shared/i18n.js + the desktop's), Arabic unless a test switches
const i18n = vm.createContext({});
for (const f of ["../shared/i18n.js", "../desktop/renderer/i18n-desktop.js"]) vm.runInContext(readFileSync(new URL(f, import.meta.url), "utf8"), i18n);
const L = vm.runInContext("LamhaI18n", i18n);

const queue = [];
const test = (name, fn) => queue.push({ name, fn });
const tick = () => new Promise(r => setImmediate(r));

/** A stand-in for electron-updater's autoUpdater: `result` decides what checkForUpdates reports. */
function fakeAutoUpdater(result) {
  const au = new EventEmitter();
  au.installs = [];
  au.checkForUpdates = async () => {
    au.emit("checking-for-update");
    await tick();
    if (result === "error") { au.emit("error", new Error("net::ERR_INTERNET_DISCONNECTED")); throw new Error("failed"); }
    if (result === "none") { au.emit("update-not-available", { version: "1.5.0" }); return null; }
    au.emit("update-available", { version: result });
    await tick();
    au.emit("update-downloaded", { version: result });
    return {};
  };
  au.quitAndInstall = (silent, runAfter) => au.installs.push([silent, runAfter]);
  return au;
}

function setup({ packaged = true, portable = false, result = "none", release = null, machine = { arch: "x64", windowsBuild: 26200 },
  files = {}, lamha3Release = "" } = {}) {
  const notes = [], opened = [], setups = [], downloads = [];
  const prefs = {};
  let changes = 0, fetched = 0, lamha3Opened = 0, clock = Date.parse("2026-11-01T09:00:00Z");
  const au = fakeAutoUpdater(result);
  const up = createUpdater({
    version: "1.5.0", packaged, portable,
    autoUpdater: () => au,
    fetchJson: async url => {
      fetched++;
      assert.equal(url, lamha3Release || "https://api.github.com/repos/osamaomer/lamha/releases/latest");
      if (release instanceof Error) throw release;
      return release;
    },
    notify: (title, body, onClick) => notes.push({ title, body, onClick }),
    onChange: () => { changes++; },
    openUrl: url => opened.push(url),
    t: (key, vars) => L.t(key, vars),
    // Lamha 3: `files` maps a download address to its bytes (or an Error)
    fetch: async url => {
      downloads.push(url);
      const f = files[url];
      if (f instanceof Error) throw f;
      return f ? new Response(f) : new Response("", { status: 404 });
    },
    tempDir: TEMP, machine, lamha3Release,
    prefs: { get: async () => ({ ...prefs }), set: async patch => { Object.assign(prefs, patch); } },
    openLamha3: () => { lamha3Opened++; },
    runSetup: file => setups.push(file),
    now: () => clock
  });
  return {
    up, au, notes, opened, setups, downloads, prefs, files,
    later: ms => { clock += ms; },
    get changes() { return changes; }, get fetched() { return fetched; }, get lamha3Opened() { return lamha3Opened; }
  };
}

/* ---- Lamha 3 (the bridge) ---- */

const TEMP = mkdtempSync(join(tmpdir(), "lamha-updater-"));
const HOUR = 3600e3, DAY = 24 * HOUR;
const SETUP_BYTES = Buffer.from("MZ… Lamha 3's setup, as Velopack builds it");
const sha = b => "sha256:" + createHash("sha256").update(b).digest("hex");
const asset = (name, bytes = SETUP_BYTES) => ({
  name, size: bytes.length, digest: sha(bytes), browser_download_url: `https://github.com/osamaomer/lamha/releases/download/v3.0.0/${name}`
});
/** Lamha 3's release as GitHub's API describes it: Velopack's files for both processors. */
const lamha3Release = (extra = {}) => ({
  tag_name: "v3.0.0", html_url: "https://github.com/osamaomer/lamha/releases/tag/v3.0.0",
  assets: [asset("Lamha-win-x64-Setup.exe"), asset("Lamha-win-arm64-Setup.exe"), asset("Lamha-win-x64-Portable.zip"), asset("releases.win-x64.json")],
  ...extra
});
const filesOf = rel => Object.fromEntries(rel.assets.map(a => [a.browser_download_url, SETUP_BYTES]));

test("version comparison", () => {
  assert.equal(newer("1.5.1", "1.5.0"), true);
  assert.equal(newer("1.10.0", "1.9.9"), true);
  assert.equal(newer("v2.0.0", "1.99.99"), true);
  assert.equal(newer("1.5.0", "1.5.0"), false);
  assert.equal(newer("1.4.9", "1.5.0"), false);
  assert.equal(newer("1.5.0-beta.1", "1.5.0"), false);
});

test("installed: a new version downloads, then «ready» with a notification that restarts into it", async () => {
  const t = setup({ result: "1.6.0" });
  await t.up.check(false);
  await tick();
  assert.equal(t.up.state.status, "ready");
  assert.equal(t.up.state.version, "1.6.0");
  assert.equal(t.notes.length, 1);
  assert.match(t.notes[0].title, /تحديث جديد جاهز/);
  t.notes[0].onClick();
  assert.deepEqual(t.au.installs, [[true, true]], "silent install, then start Lamha again");
  assert.ok(t.changes >= 3, "tray / settings were told");
});

test("installed: up to date — silent when automatic, a notice when the user asked", async () => {
  const auto = setup({ result: "none" });
  await auto.up.check(false);
  assert.equal(auto.up.state.status, "latest");
  assert.equal(auto.notes.length, 0);
  const byUser = setup({ result: "none" });
  await byUser.up.check(true);
  assert.equal(byUser.notes.length, 1);
  assert.match(byUser.notes[0].title, /لديك أحدث إصدار/);
});

test("installed: no internet — error state, reported only when the user asked", async () => {
  const auto = setup({ result: "error" });
  await auto.up.check(false);
  assert.equal(auto.up.state.status, "error");
  assert.equal(auto.notes.length, 0);
  const byUser = setup({ result: "error" });
  await byUser.up.check(true);
  assert.match(byUser.notes[0].title, /تعذّر التحقق/);
});

test("Portable: a newer GitHub release → notice that opens its page; nothing downloaded", async () => {
  const t = setup({ portable: true, release: { tag_name: "v1.6.0", html_url: "https://github.com/osamaomer/lamha/releases/tag/v1.6.0" } });
  await t.up.check(false);
  assert.equal(t.up.state.status, "available");
  assert.equal(t.up.state.version, "1.6.0");
  assert.equal(t.fetched, 1);
  t.notes[0].onClick();
  assert.deepEqual(t.opened, ["https://github.com/osamaomer/lamha/releases/tag/v1.6.0"]);
  assert.equal(t.au.listenerCount("update-downloaded"), 0, "electron-updater not used by the Portable");
});

test("Portable: same or older release → up to date; network error → error", async () => {
  const same = setup({ portable: true, release: { tag_name: "v1.5.0" } });
  await same.up.check(true);
  assert.equal(same.up.state.status, "latest");
  const down = setup({ portable: true, release: new Error("offline") });
  await down.up.check(true);
  assert.equal(down.up.state.status, "error");
});

test("English interface: the notices are in English", async () => {
  L.setLang("en");
  try {
    const t = setup({ result: "1.6.0" });
    await t.up.check(false);
    await tick();
    assert.equal(t.notes[0].title, "Update ready");
    assert.match(t.notes[0].body, /^Version 1\.6\.0 — restart Lamha to install it/);
    const byUser = setup({ result: "none" });
    await byUser.up.check(true);
    assert.equal(byUser.notes[0].title, "You're up to date");
  } finally {
    L.setLang("ar");
  }
});

test("development (not packaged): never checks", async () => {
  const t = setup({ packaged: false, result: "9.9.9" });
  await t.up.check(true);
  assert.equal(t.up.state.status, "dev");
  assert.equal(t.fetched, 0);
  assert.equal(t.au.listenerCount("update-available"), 0);
});

test("Lamha 3: the setup for this PC's processor, a beta's names, nothing from a 2.x release or elsewhere", () => {
  const rel = lamha3Release();
  assert.match(lamha3Setup(rel, "x64").url, /Lamha-win-x64-Setup\.exe$/);
  assert.match(lamha3Setup(rel, "arm64").url, /Lamha-win-arm64-Setup\.exe$/);
  const x64Only = lamha3Release({ assets: [asset("Lamha-win-x64-Setup.exe")] });
  assert.match(lamha3Setup(x64Only, "arm64").url, /Lamha-win-x64-Setup\.exe$/, "an ARM PC runs the x64 one emulated");
  const beta = lamha3Release({ tag_name: "v3.0.0-beta.3", assets: [asset("Lamha-beta-win-x64-Setup.exe")] });
  assert.equal(lamha3Setup(beta, "x64").version, "3.0.0-beta.3");
  const both = lamha3Release({ assets: [asset("Lamha-beta-win-x64-Setup.exe"), asset("Lamha-win-x64-Setup.exe")] });
  assert.match(lamha3Setup(both, "x64").url, /\/Lamha-win-x64-Setup\.exe$/, "a release's own setup, never the beta channel's");
  assert.equal(lamha3Setup(lamha3Release({ assets: [asset("Lamha-beta-win-x64-Setup.exe")] }), "x64"), null, "a beta-channel install would follow the betas");
  const s = lamha3Setup(rel, "x64");
  assert.equal(s.size, SETUP_BYTES.length);
  assert.equal("sha256:" + s.sha256, sha(SETUP_BYTES));
  assert.equal(lamha3Setup({ ...rel, tag_name: "v2.1.1" }, "x64"), null, "a 2.x release goes to electron-updater");
  assert.equal(lamha3Setup(lamha3Release({ assets: [asset("Lamha-win-x64-Portable.zip")] }), "x64"), null);
  const elsewhere = lamha3Release({ assets: [{ ...asset("Lamha-win-x64-Setup.exe"), browser_download_url: "https://example.com/Lamha-win-x64-Setup.exe" }] });
  assert.equal(lamha3Setup(elsewhere, "x64"), null, "only GitHub's own downloads");
});

test("Lamha 3 on Windows 11: found instead of a 2.x update, one notification a day that opens its window", async () => {
  const t = setup({ release: lamha3Release() });
  await t.up.check(false);
  assert.equal(t.up.state.status, "lamha3");
  assert.equal(t.up.state.version, "3.0.0");
  assert.deepEqual(t.up.state.lamha3, { version: "3.0.0", size: SETUP_BYTES.length, page: "https://github.com/osamaomer/lamha/releases/tag/v3.0.0", windows11: true });
  assert.equal(t.au.listenerCount("update-available"), 0, "electron-updater can't install Lamha 3");
  assert.equal(t.notes.length, 1);
  assert.equal(t.notes[0].title, "وصلت لمحة 3");
  t.notes[0].onClick();
  assert.equal(t.lamha3Opened, 1);
  t.later(6 * HOUR);
  await t.up.check(false);
  assert.equal(t.notes.length, 1, "not again the same day");
  assert.equal(t.fetched, 1, "what was found is kept");
  t.later(DAY);
  await t.up.check(false);
  assert.equal(t.notes.length, 2, "the next day");
});

test("Lamha 3: asked from the tray or Settings, its window opens (no notification); Later is quiet for 3 days", async () => {
  const t = setup({ release: lamha3Release() });
  await t.up.check(true);
  assert.equal(t.lamha3Opened, 1);
  assert.equal(t.notes.length, 0);
  await t.up.lamha3Later();
  t.later(2 * DAY);
  await t.up.check(false);
  assert.equal(t.notes.length, 0, "2 days after Later");
  t.later(DAY + HOUR);
  await t.up.check(false);
  assert.equal(t.notes.length, 1, "3 days after Later");
  await t.up.check(true);
  assert.equal(t.lamha3Opened, 2, "the tray still opens it");
});

test("Lamha 3 on Windows 10: not offered, said in Settings, and Lamha 2's own updates go on", async () => {
  const t = setup({ release: lamha3Release(), machine: { arch: "x64", windowsBuild: 19045 }, result: "1.6.0" });
  await t.up.check(false);
  await tick();
  assert.equal(t.up.state.lamha3.windows11, false);
  assert.equal(t.up.state.status, "ready", "a 2.x update still installs");
  assert.ok(t.notes.every(n => !/لمحة 3/.test(n.title)));
});

test("Lamha 3: Install downloads the setup for this PC, checks it, then hands it over to quit and start it", async () => {
  const rel = lamha3Release();
  const t = setup({ release: rel, machine: { arch: "arm64", windowsBuild: 26200 }, files: filesOf(rel) });
  await t.up.check(true);
  const progress = [];
  const changes = t.changes;
  const done = t.up.installLamha3();
  progress.push(t.up.state.status);
  await done;
  assert.deepEqual(progress, ["lamha3-downloading"]);
  assert.equal(t.up.state.status, "lamha3-starting");
  assert.equal(t.up.state.progress, 1);
  assert.ok(t.changes > changes + 1, "the window and the tray follow it");
  assert.deepEqual(t.downloads, ["https://github.com/osamaomer/lamha/releases/download/v3.0.0/Lamha-win-arm64-Setup.exe"]);
  assert.deepEqual(t.setups, [join(TEMP, LAMHA3_SETUP_FILE)]);
  assert.deepEqual(readFileSync(t.setups[0]), SETUP_BYTES);
  await t.up.installLamha3();
  assert.equal(t.downloads.length, 1, "once");
});

test("Lamha 3: a damaged or cut-off download isn't started, and the file goes; no internet says so; it can be tried again", async () => {
  const rel = lamha3Release();
  const url = rel.assets[0].browser_download_url;
  const file = join(TEMP, LAMHA3_SETUP_FILE);
  rmSync(file, { force: true });
  const damaged = setup({ release: rel, files: { [url]: Buffer.from("MZ… something else, of a different length") } });
  await damaged.up.check(true);
  await damaged.up.installLamha3();
  assert.equal(damaged.up.state.status, "lamha3-error");
  assert.equal(damaged.up.state.error, "damaged");
  assert.equal(damaged.setups.length, 0);
  assert.equal(existsSync(file), false);
  const sameSize = Buffer.from(SETUP_BYTES);
  sameSize[0] ^= 1;
  const swapped = setup({ release: rel, files: { [url]: sameSize } });
  await swapped.up.check(true);
  await swapped.up.installLamha3();
  assert.equal(swapped.up.state.error, "damaged", "the size is right, the SHA-256 isn't");
  const missing = setup({ release: rel });
  await missing.up.check(true);
  await missing.up.installLamha3();
  assert.equal(missing.up.state.error, "network", "a 404");
  const offline = setup({ release: rel, files: { [url]: new TypeError("fetch failed") } });
  await offline.up.check(true);
  await offline.up.installLamha3();
  assert.equal(offline.up.state.error, "network");
  assert.equal((await offline.up.check(true)).status, "lamha3-error");
  assert.equal(offline.lamha3Opened, 2, "the tray's item opens the window again");
  offline.files[url] = SETUP_BYTES; // back online: Install again
  await offline.up.installLamha3();
  assert.equal(offline.up.state.status, "lamha3-starting");
  assert.equal(offline.setups.length, 1);
});

test("Lamha 3: the bridge tried from source on a beta (LAMHA_BRIDGE_TAG), which a 2.x build from source never checks", async () => {
  const api = "https://api.github.com/repos/osamaomer/lamha/releases/tags/v3.0.0-beta.2";
  const beta = lamha3Release({ tag_name: "v3.0.0-beta.2", assets: [asset("Lamha-beta-win-x64-Setup.exe")] });
  const t = setup({ packaged: false, release: beta, lamha3Release: api });
  await t.up.check(true);
  assert.equal(t.up.state.status, "lamha3");
  assert.equal(t.up.state.version, "3.0.0-beta.2");
  const notThere = setup({ packaged: false, release: { tag_name: "v2.0.1", assets: [] }, lamha3Release: api });
  await notThere.up.check(true);
  assert.equal(notThere.up.state.status, "dev");
});

let passed = 0, failed = 0, filtered = 0;
for (const { name, fn } of queue) {
  if (!wanted(name)) { filtered++; continue; }
  try { await fn(); passed++; report("  ✓ " + name); }
  catch (err) { failed++; report("  ✗ " + name + "\n    " + String(err.message).split("\n").join("\n    ")); }
}
rmSync(TEMP, { recursive: true, force: true });
console.log(`${quiet ? "" : "\n"}${passed}/${passed + failed} passed${notRun(filtered)}`);
process.exit(failed ? 1 : 0);
