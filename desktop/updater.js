/* Lamha desktop — updates from the GitHub Releases of osamaomer/lamha.
 * Installed app (Setup): electron-updater checks after startup and every 6 hours, downloads in the background,
 * and installs on the next restart or quit; "أعد التشغيل الآن" does it at once.
 * Portable: it can't replace itself, so it only says a new version exists and links to the release page.
 * Lamha 3 (the bridge, from 2.1.0): when the latest release is Lamha 3, a native app installed by Velopack that
 * electron-updater can't install, Lamha says so (a notification, at most once a day, not for 3 days after "Later") and
 * offers its window (main.js openLamha3). "Install Lamha 3" downloads its setup for this PC, checks it against the
 * release, and hands it to main.js, which quits Lamha 2 and starts it. Lamha 3 moves the data and removes Lamha 2.
 * Lamha 3 is for Windows 11 only: on Windows 10 Lamha 2 stays, with its own updates.
 * The update source and the network are passed in, so the self-test can use fakes. */
"use strict";
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");
const { Readable } = require("node:stream");
const { pipeline } = require("node:stream/promises");

const RELEASES = "https://github.com/osamaomer/lamha/releases";
const LATEST_API = "https://api.github.com/repos/osamaomer/lamha/releases/latest";
const FIRST_CHECK_MS = 15e3;
const EVERY_MS = 6 * 3600e3;
const WINDOWS_11 = 22000; // the first build of Windows 11
const LAMHA3_NOTE_EVERY_MS = 24 * 3600e3;
const LAMHA3_LATER_MS = 3 * 24 * 3600e3;
const LAMHA3_DOWNLOAD_MS = 15 * 60e3; // ~50 MB: a slow line has time, a stalled download ends
const LAMHA3_SETUP_FILE = "Lamha-3-Setup.exe";

/** 1.10.0 > 1.9.2; "v1.5.0" is fine; pre-release suffixes are ignored. */
function newer(a, b) {
  const parts = v => String(v || "").replace(/^v/, "").split("-")[0].split(".").map(n => parseInt(n, 10) || 0);
  const [x, y] = [parts(a), parts(b)];
  for (let i = 0; i < 3; i++) if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) > (y[i] || 0);
  return false;
}

/** Lamha 3's setup in a GitHub release (the API's shape), for a PC of `arch` ("x64" or "arm64"): Velopack's
 *  Lamha-win-<arch>-Setup.exe, or Lamha-beta-win-<arch>-Setup.exe in a beta's release only (a beta-channel install
 *  would follow the betas). An ARM PC takes the x64 one when the release has no other (Windows runs it emulated). null
 *  when the release isn't Lamha 3 or has no setup. */
function lamha3Setup(rel, arch) {
  const version = String((rel && rel.tag_name) || "").replace(/^v/, "");
  if (!(parseInt(version, 10) >= 3) || !Array.isArray(rel.assets)) return null;
  const prefix = version.includes("-") ? "Lamha-beta-" : "Lamha-";
  const find = a => rel.assets.find(x => x && String(x.name) === `${prefix}win-${a}-Setup.exe`);
  const asset = find(arch === "arm64" ? "arm64" : "x64") || find("x64");
  if (!asset || !String(asset.browser_download_url).startsWith("https://github.com/") || !(asset.size > 0)) return null;
  const digest = /^sha256:([0-9a-f]{64})$/i.exec(String(asset.digest || ""));
  return { version, url: asset.browser_download_url, size: asset.size, sha256: digest ? digest[1].toLowerCase() : "", page: rel.html_url || RELEASES };
}

const codeError = code => Object.assign(new Error(code), { code });

/**
 * @param {object} o
 * @param {string} o.version            this app's version
 * @param {boolean} o.packaged          false while developing: no checks (but Lamha 3's, with o.lamha3Release)
 * @param {boolean} o.portable          the Portable build (process.env.PORTABLE_EXECUTABLE_DIR)
 * @param {() => object} o.autoUpdater  electron-updater's autoUpdater (loaded lazily)
 * @param {(url: string) => Promise<object>} o.fetchJson
 * @param {(title: string, body: string, onClick?: () => void) => void} o.notify
 * @param {() => void} o.onChange       state changed (tray menu, settings)
 * @param {(url: string) => void} o.openUrl
 * @param {(key: string, vars?: object) => string} o.t  interface text (renderer/i18n-desktop.js)
 * @param {(url: string, init?: object) => Promise<Response>} [o.fetch]  Lamha 3's setup download
 * @param {string} [o.tempDir]          where the setup is downloaded
 * @param {{ arch: string, windowsBuild: number }} [o.machine]  the PC (native.js nativeArch(), os.release())
 * @param {{ get: () => Promise<object>, set: (patch: object) => Promise<void> }} [o.prefs]  lamha3NotifiedAt, lamha3Later (storage.local)
 * @param {() => void} [o.openLamha3]   the Lamha 3 window
 * @param {(file: string) => void} [o.runSetup]  quit Lamha 2 and start Lamha 3's setup
 * @param {string} [o.lamha3Release]    a release's API address instead of the latest (trying the bridge on a beta)
 * @param {() => number} [o.now]
 */
function createUpdater({ version, packaged, portable, autoUpdater, fetchJson, notify, onChange, openUrl, t,
  fetch, tempDir, machine = { arch: "x64", windowsBuild: 0 }, prefs, openLamha3 = () => {}, runSetup = () => {}, lamha3Release = "", now = Date.now }) {
  // status: idle | checking | available | downloading | ready | latest | error | dev
  //   and Lamha 3's: lamha3 (found) | lamha3-downloading | lamha3-starting | lamha3-error (error: "network" | "damaged")
  // lamha3: what was found ({ version, size, page }, + windows11: false on Windows 10, where it isn't offered)
  const state = { status: "idle", version: null, error: null, checkedAt: 0, lamha3: null, progress: 0 };
  let au = null, timer = null, manual = false, setup = null;
  const set = patch => { Object.assign(state, patch); onChange(); };
  const BUSY = ["checking", "downloading", "lamha3-downloading", "lamha3-starting"];

  function updater() {
    if (au) return au;
    au = autoUpdater();
    au.autoDownload = true;
    au.autoInstallOnAppQuit = true;
    au.logger = null; // nothing about the user's files in logs
    au.on("checking-for-update", () => set({ status: "checking", error: null }));
    au.on("update-not-available", () => {
      set({ status: "latest", checkedAt: now() });
      if (manual) notify(t("d.upLatestTitle"), t("d.upLatestBody", { v: version }));
    });
    au.on("update-available", info => set({ status: "downloading", version: info.version, checkedAt: now() }));
    au.on("update-downloaded", info => {
      set({ status: "ready", version: info.version });
      notify(t("d.upReadyTitle"), t("d.upReadyBody", { v: info.version }), restart);
    });
    au.on("error", err => {
      set({ status: "error", error: String((err && err.message) || err).slice(0, 200) });
      if (manual) notify(t("d.upFailedTitle"), t("d.upFailedBody"));
    });
    return au;
  }

  /** The latest release (or the one asked for); null when GitHub can't be reached. */
  async function latestRelease() {
    try { return await fetchJson(lamha3Release || LATEST_API); } catch (_) { return null; }
  }

  /** Portable: if the latest release on GitHub is newer, a notice that opens the release page. */
  function checkPortable(rel) {
    if (!rel) {
      set({ status: "error", error: "network" });
      if (manual) notify(t("d.upFailedTitle"), t("d.upFailedBody"));
      return;
    }
    const latest = String(rel.tag_name || "").replace(/^v/, "");
    if (latest && newer(latest, version)) {
      set({ status: "available", version: latest, checkedAt: now() });
      notify(t("d.upNewTitle"), t("d.upNewBody", { v: latest }), () => openUrl(rel.html_url || RELEASES));
    } else {
      set({ status: "latest", checkedAt: now() });
      if (manual) notify(t("d.upLatestTitle"), t("d.upLatestBody", { v: version }));
    }
  }

  /** `byUser`: from the tray or Settings, so "up to date" and errors are reported too. */
  async function check(byUser = false) {
    if (!packaged && !lamha3Release) { set({ status: "dev" }); if (byUser) notify(t("d.upTitle"), t("d.upDevBody")); return state; }
    if (BUSY.includes(state.status)) return state;
    if (state.status === "ready") { if (byUser) restart(); return state; }
    if (state.status === "lamha3" || state.status === "lamha3-error") { await offerLamha3(byUser); return state; }
    manual = byUser;
    set({ status: "checking", error: null });
    const rel = await latestRelease();
    const found = rel && lamha3Setup(rel, machine.arch);
    if (found && machine.windowsBuild >= WINDOWS_11) {
      setup = found;
      set({ status: "lamha3", version: found.version, checkedAt: now(), lamha3: { version: found.version, size: found.size, page: found.page, windows11: true } });
      await offerLamha3(byUser);
      return state;
    }
    // on Windows 10 Lamha 2 stays, and Settings says why Lamha 3 isn't offered
    set({ lamha3: found ? { version: found.version, size: found.size, page: found.page, windows11: false } : null });
    if (!packaged) set({ status: "dev" });
    else if (portable) checkPortable(rel);
    else await updater().checkForUpdates().catch(() => { /* reported through the "error" event */ });
    return state;
  }

  /** The user asked (tray, Settings): the window. Automatic: a notification at most once a day, none for 3 days after
   *  "Later"; the window opens from it. */
  async function offerLamha3(byUser) {
    if (byUser) { openLamha3(); return; }
    if (!prefs) return;
    const p = await prefs.get().catch(() => ({}));
    const at = now();
    if (at < (Number(p.lamha3Later) || 0) || at - (Number(p.lamha3NotifiedAt) || 0) < LAMHA3_NOTE_EVERY_MS) return;
    await prefs.set({ lamha3NotifiedAt: at }).catch(() => {});
    notify(t("d.l3NoteTitle"), t("d.l3NoteBody"), openLamha3);
  }

  /** The window's "Later": no notification for 3 days (the tray and Settings still offer it). */
  async function lamha3Later() {
    if (prefs) await prefs.set({ lamha3Later: now() + LAMHA3_LATER_MS }).catch(() => {});
  }

  /** "Install Lamha 3": its setup downloaded and checked, then main.js quits Lamha 2 and starts it. */
  async function installLamha3() {
    if (!setup || !["lamha3", "lamha3-error"].includes(state.status)) return state;
    set({ status: "lamha3-downloading", progress: 0, error: null });
    const file = path.join(tempDir, LAMHA3_SETUP_FILE);
    try {
      await downloadSetup(setup, file);
    } catch (err) {
      await fs.promises.rm(file, { force: true }).catch(() => {});
      set({ status: "lamha3-error", error: err && err.code === "damaged" ? "damaged" : "network" });
      return state;
    }
    set({ status: "lamha3-starting", progress: 1 });
    runSetup(file);
    return state;
  }

  /** Downloads the setup to `file`: exactly the release's size, and its SHA-256 when GitHub gives one. */
  async function downloadSetup(s, file) {
    let res;
    try { res = await fetch(s.url, { signal: AbortSignal.timeout(LAMHA3_DOWNLOAD_MS) }); } catch (_) { throw codeError("network"); }
    if (!res.ok || !res.body) throw codeError("network");
    const hash = crypto.createHash("sha256");
    let got = 0, told = 0;
    try {
      await pipeline(Readable.fromWeb(res.body), async function* (chunks) {
        for await (const chunk of chunks) {
          got += chunk.length;
          if (got > s.size) throw codeError("damaged");
          hash.update(chunk);
          if (got - told >= s.size / 100) { told = got; set({ progress: got / s.size }); } // once per percent
          yield chunk;
        }
      }, fs.createWriteStream(file));
    } catch (err) {
      throw err && err.code === "damaged" ? err : codeError("network");
    }
    if (got !== s.size || (s.sha256 && hash.digest("hex") !== s.sha256)) throw codeError("damaged");
  }

  function restart() {
    if (state.status === "ready" && au) au.quitAndInstall(true, true); // silent (per-user install), then Lamha starts again
    else if (state.status === "available") openUrl(RELEASES + "/latest");
  }

  /** Automatic checks on (setting «التحديثات التلقائية») or off. */
  function schedule(on) {
    clearTimeout(timer);
    clearInterval(timer);
    timer = null;
    if (!on || (!packaged && !lamha3Release)) return;
    timer = setTimeout(function tick() {
      check(false);
      timer = setInterval(() => check(false), EVERY_MS);
      timer.unref();
    }, FIRST_CHECK_MS);
    timer.unref();
  }

  return { check, restart, schedule, installLamha3, lamha3Later, state, releasesUrl: RELEASES };
}

module.exports = { createUpdater, newer, lamha3Setup, LAMHA3_SETUP_FILE };
