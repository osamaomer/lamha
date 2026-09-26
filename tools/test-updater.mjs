// Tests for the desktop app's updater (desktop/updater.js) with a fake electron-updater and a fake GitHub API.
//   node tools/test-updater.mjs
import { createRequire } from "node:module";
import { EventEmitter } from "node:events";
import assert from "node:assert/strict";

const { createUpdater, newer } = createRequire(import.meta.url)("../desktop/updater.js");

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

function setup({ packaged = true, portable = false, result = "none", release = null } = {}) {
  const notes = [], opened = [];
  let changes = 0, fetched = 0;
  const au = fakeAutoUpdater(result);
  const up = createUpdater({
    version: "1.5.0", packaged, portable,
    autoUpdater: () => au,
    fetchJson: async url => {
      fetched++;
      assert.equal(url, "https://api.github.com/repos/osamaomer/lamha/releases/latest");
      if (release instanceof Error) throw release;
      return release;
    },
    notify: (title, body, onClick) => notes.push({ title, body, onClick }),
    onChange: () => { changes++; },
    openUrl: url => opened.push(url)
  });
  return { up, au, notes, opened, get changes() { return changes; }, get fetched() { return fetched; } };
}

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

test("development (not packaged): never checks", async () => {
  const t = setup({ packaged: false, result: "9.9.9" });
  await t.up.check(true);
  assert.equal(t.up.state.status, "dev");
  assert.equal(t.fetched, 0);
  assert.equal(t.au.listenerCount("update-available"), 0);
});

let passed = 0, failed = 0;
for (const { name, fn } of queue) {
  try { await fn(); passed++; console.log("  ✓ " + name); }
  catch (err) { failed++; console.log("  ✗ " + name + "\n    " + String(err.message).split("\n").join("\n    ")); }
}
console.log(`\n${passed}/${passed + failed} passed`);
process.exit(failed ? 1 : 0);
