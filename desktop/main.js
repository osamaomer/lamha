/* Lamha desktop — Electron main process.
 * Runs the extension's background logic (background.js + local-dict.js + shared/lamha-ai.js) unchanged,
 * on top of a small `browser.*` replacement, and shows the extension's popup as the main window.
 *   npm start               run the app
 *   npm run smoke           self-test: starts with a temporary profile, checks everything, quits */
"use strict";
const { app, BrowserWindow, Tray, Menu, ipcMain, shell, nativeImage, Notification, globalShortcut, screen, clipboard, safeStorage, nativeTheme, dialog, protocol, net } = require("electron");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const vm = require("node:vm");
const { Store } = require("./storage");
const { WikiLibrary } = require("./wiki-library");
const { NativeBridge } = require("./native-bridge");

// the extension files: the repository root while developing, a copy (ext/) inside the packaged app
const EXT_DIR = app.isPackaged ? path.join(__dirname, "ext") : path.join(__dirname, "..");
const BASE = "lamha://app/"; // what browser.runtime.getURL() returns
const SHOTS = process.argv.includes("--screenshots") && !app.isPackaged; // npm run shots: every screen as a picture
const SMOKE = (process.argv.includes("--smoke-test") || SHOTS) && !app.isPackaged; // the self-test runs from source only (npm run smoke)
const START_HIDDEN = process.argv.includes("--hidden"); // started with Windows
const ICON = path.join(__dirname, "assets", "icon-256.png");
const TRAY_ICON = path.join(__dirname, "assets", "tray-32.png");
const DESKTOP_CSS = fs.readFileSync(path.join(__dirname, "renderer", "desktop.css"), "utf8");

if (SMOKE) app.setPath("userData", fs.mkdtempSync(path.join(os.tmpdir(), "lamha-smoke-")));
// from source only: a profile of its own, so a tool can start the app as a user would without touching your data or
// the registry (scripts/memory.ps1). LAMHA_NO_GPU: without the graphics card's help, to compare the memory it takes.
const DEV_PROFILE = !app.isPackaged && !SMOKE && process.env.LAMHA_PROFILE;
if (DEV_PROFILE) app.setPath("userData", DEV_PROFILE);
// Settings → Appearance → the graphics card (storage.local useGpu, on unless turned off: off saves ~25 MB, measured with
// scripts/memory.ps1). Chromium can only be told before the app is ready, so it's read from the file here, and a change
// applies at the next start (Settings offers a restart).
const GPU = !(!app.isPackaged && process.env.LAMHA_NO_GPU === "1") && localAtStart().useGpu !== false;
if (!GPU) app.disableHardwareAcceleration();

/** storage.local as it is on disk, before the stores exist (plain values only: the API keys there stay encrypted). */
function localAtStart() {
  try { return JSON.parse(fs.readFileSync(path.join(app.getPath("userData"), "storage-local.json"), "utf8")) || {}; } catch (_) { return {}; }
}

let mainWin = null, optionsWin = null, tray = null, quitting = false;
/** Interface text (shared/i18n.js + renderer/i18n-desktop.js, loaded by startCore). */
const T = (key, vars) => globalThis.LamhaI18n.t(key, vars);
let stores, messageHandler = null, badgeCount = 0, lastReminder = 0, wikiLibrary = null, readerWin = null;
// the Wikipedia reader's pictures: lamha-wiki://zim/<file id>/<path> (registered before the app is ready)
protocol.registerSchemesAsPrivileged([{ scheme: "lamha-wiki", privileges: { standard: true, secure: true } }]);
// what each window is told and may ask, where the floating windows go: decisions tested in plain Node (app-rules.js)
const rules = require("./app-rules");
const { SECRET_KEYS, withoutSecrets } = rules;

/* ---------------- the browser.* replacement for the background logic ---------------- */

function broadcast(changes, areaName) {
  for (const f of storageListeners) { try { f(changes, areaName); } catch (err) { console.error(err); } }
  if (areaName === "sync" && changes.motion) updateTray(); // the Animations choice is also in the tray menu
  if (areaName === "sync" && changes.theme) applyThemeSetting();
  for (const w of BrowserWindow.getAllWindows()) {
    if (w.isDestroyed() || (clipboardMonitor && w === clipboardMonitor.win)) continue; // that one only hears the clipboard
    const out = rules.changesFor(w === cardWin ? "card" : w === panelWin || w === readerWin ? "lean" : "page", changes);
    if (out) w.webContents.send("lamha:storage-changed", out, areaName);
  }
}
const storageListeners = [];
const listeners = { installed: [], startup: [], alarm: [] };

/** fetch() for the background: lamha://app/… reads the extension's own files (the offline dictionary). */
const netFetch = globalThis.fetch;
async function extFetch(url, init) {
  const u = String(url);
  if (!u.startsWith(BASE)) return netFetch(u, init);
  const rel = decodeURIComponent(u.slice(BASE.length).split(/[?#]/)[0]);
  const file = path.join(EXT_DIR, rel);
  if (!isInside(EXT_DIR, file)) return { ok: false, status: 403, json: async () => ({}), text: async () => "" };
  try {
    const buf = await fs.promises.readFile(file, "utf8");
    return { ok: true, status: 200, json: async () => JSON.parse(buf), text: async () => buf };
  } catch (_) {
    return { ok: false, status: 404, json: async () => ({}), text: async () => "" };
  }
}

/** new Audio(url) for the background's speech: played by the main window (see preload.js). */
const audios = new Map();
let audioSeq = 0;
class RemoteAudio {
  constructor(src) { this.src = src; this.id = ++audioSeq; }
  play() {
    return new Promise((resolve, reject) => {
      const player = mainWin && !mainWin.isDestroyed() ? mainWin.webContents : null;
      if (!player) { reject(new Error("no audio player")); return; }
      audios.set(this.id, { audio: this, resolve, reject, started: false });
      player.send("lamha:audio-play", this.id, this.src);
    });
  }
  pause() { if (mainWin && !mainWin.isDestroyed()) mainWin.webContents.send("lamha:audio-pause", this.id); }
}
ipcMain.on("lamha:audio-event", (_e, id, type) => {
  const rec = audios.get(id);
  if (!rec) return;
  if (type === "playing") { rec.started = true; rec.resolve(); return; }
  audios.delete(id);
  if (!rec.started) { rec.reject(new Error("audio " + type)); return; }
  const handler = rec.audio["on" + type];
  if (typeof handler === "function") handler();
});

function installBrowserShim() {
  globalThis.browser = {
    i18n: { getUILanguage: () => app.getLocale() }, // "auto" interface language follows Windows (shared/i18n.js)
    storage: { local: stores.local, sync: stores.sync, onChanged: { addListener: f => storageListeners.push(f) } },
    runtime: {
      onMessage: { addListener: f => { messageHandler = f; } },
      onInstalled: { addListener: f => listeners.installed.push(f) },
      onStartup: { addListener: f => listeners.startup.push(f) },
      getURL: p => BASE + String(p).replace(/^\//, ""),
      openOptionsPage: async () => { openOptions(""); }
    },
    tabs: {
      query: async () => [],
      sendMessage: async () => { throw new Error("no tabs on the desktop"); },
      create: async ({ url }) => { openUrl(url); return {}; }
    },
    menus: { removeAll: async () => {}, create() {}, onClicked: { addListener() {} } },
    commands: { onCommand: { addListener() {} } },
    permissions: { contains: async () => true },
    action: {
      setBadgeText: async ({ text }) => setBadge(Number(text) || 0),
      setBadgeBackgroundColor: async () => {}
    },
    alarms: {
      create: (name, { periodInMinutes }) => {
        setInterval(() => listeners.alarm.forEach(f => f({ name })), periodInMinutes * 60e3).unref();
      },
      onAlarm: { addListener: f => listeners.alarm.push(f) }
    }
  };
  globalThis.Audio = RemoteAudio;
  globalThis.fetch = extFetch;
  // navigator.onLine, as in a browser: Windows' own network state through Chromium (background.js browserOffline()).
  // Node's navigator has none, so offline the background used to wait for Google to fail before the dictionary answered.
  if (typeof globalThis.navigator !== "object" || !globalThis.navigator) globalThis.navigator = {};
  Object.defineProperty(globalThis.navigator, "onLine", { configurable: true, get: () => net.isOnline() });
}

/** Loads the extension's background scripts into this process, as Firefox would. */
function startCore() {
  const dir = app.getPath("userData");
  stores = {
    local: new Store(path.join(dir, "storage-local.json"), "local", broadcast, { secrets: SECRET_KEYS, safeStorage }), // API keys: DPAPI-encrypted on disk
    sync: new Store(path.join(dir, "storage-sync.json"), "sync", broadcast)
  };
  installBrowserShim();
  const flag = path.join(dir, "installed.flag");
  const firstRun = !fs.existsSync(flag);
  // interface language: a profile from before the setting keeps Arabic; the self-test is pinned (LAMHA_SMOKE_LANG to change)
  if (SMOKE) stores.sync.set({ uiLang: process.env.LAMHA_SMOKE_LANG || "ar" });
  else if (!firstRun && !("uiLang" in stores.sync.data)) stores.sync.set({ uiLang: "ar" });
  globalThis.LamhaPackStore = require("./pack-store").createPackStore(path.join(dir, "packs")); // downloaded dictionaries (packs.js)
  // downloaded Wikipedia (wiki-library.js): the background reads it through these two calls only
  wikiLibrary = new WikiLibrary({ dataDir: dir, fetch: netFetch });
  globalThis.LamhaWikiOffline = {
    langs: () => wikiLibrary.langs(),
    summary: (titles, lang) => wikiLibrary.summary(titles, lang),
    open: (file, articlePath) => { openReader({ file: String(file || ""), path: String(articlePath || "") }); return true; } // the card's "Read the article in Lamha"
  };
  wikiLibrary.on("change", () => {
    for (const w of [optionsWin, readerWin]) if (w && !w.isDestroyed()) w.webContents.send("lamha:wiki-changed");
    updateTray(); // the Wikipedia item comes and goes with the files
  });
  wikiLibrary.init().catch(err => console.error("offline Wikipedia:", err && err.message));
  // Settings → Report a problem: what the background adds about the Windows app (background.js diagnostics())
  globalThis.LamhaDesktopInfo = () => ({
    version: app.getVersion(), electron: process.versions.electron, windows: os.release(),
    wiki: wikiLibrary ? wikiLibrary.langs() : [],
    memoryMB: Math.round(app.getAppMetrics().reduce((n, m) => n + (m.memory.privateBytes || m.memory.workingSetSize), 0) / 1024),
    gpu: GPU
  });
  for (const f of ["local-dict.js", "packs.js", "shared/i18n.js", "shared/lamha-ai.js", "background.js"]) {
    vm.runInThisContext(fs.readFileSync(path.join(EXT_DIR, f), "utf8"), { filename: path.join(EXT_DIR, f) });
  }
  const desktopStrings = path.join(__dirname, "renderer", "i18n-desktop.js");
  vm.runInThisContext(fs.readFileSync(desktopStrings, "utf8"), { filename: desktopStrings }); // tray, notifications, clipboard UI
  if (firstRun) {
    fs.writeFileSync(flag, new Date().toISOString());
    listeners.installed.forEach(f => f({ reason: "install" })); // opens Settings once: choose Ollama/Claude
  } else {
    listeners.startup.forEach(f => f());
  }
}

/** Settings → Theme for every window: Electron's own light / dark, which the pages' prefers-color-scheme then follows
 *  (the card, the clipboard panel, window backgrounds). "auto" is the system's. */
function applyThemeSetting() {
  const t = stores.sync.data.theme;
  nativeTheme.themeSource = t === "light" || t === "dark" ? t : "system";
}

/**
 * What "Automatic" animations means on this PC (shared/motion.js reads it): "subtle" without graphics acceleration
 * or on a weak machine (4 GB of memory or less, 2 cores or fewer), "full" otherwise. Windows' own "Animation effects"
 * switch is seen by the pages directly (prefers-reduced-motion) and turns animations off.
 * Checked a few seconds after startup: until the GPU process is up, Electron reports every PC as software-only.
 * Until then the hint saved last time applies.
 */
function updateMotionHint() {
  let hint = "full";
  try {
    const gpu = app.getGPUFeatureStatus() || {};
    const software = !/^enabled/.test(String(gpu.gpu_compositing || "enabled"));
    const weak = os.totalmem() <= 4.5 * 1024 ** 3 || os.cpus().length <= 2;
    if (software || weak) hint = "subtle";
  } catch (_) { /* keep full */ }
  if (stores.local.data.motionHint !== hint) stores.local.set({ motionHint: hint });
  return hint;
}
function watchMotionHint() {
  setTimeout(updateMotionHint, 6000).unref();
  // the GPU process can crash and come back without acceleration
  app.on("child-process-gone", (_e, d) => { if (d && d.type === "GPU") setTimeout(updateMotionHint, 4000).unref(); });
}

/* ---------------- IPC from the pages ---------------- */

ipcMain.on("lamha:info", e => {
  const role = cardWin && !cardWin.isDestroyed() && e.sender === cardWin.webContents ? "card" : "page";
  e.returnValue = { base: BASE, version: app.getVersion(), role, locale: app.getLocale() };
});

ipcMain.handle("lamha:message", async (e, msg) => {
  if (!messageHandler) return undefined;
  // the windows that show outside text (the card, the reader) may ask only for what the card does (app-rules.js)
  const outside = fromCard(e) || (readerWin && !readerWin.isDestroyed() && e.sender === readerWin.webContents);
  if (outside && !rules.allowedFromOutsideText(msg)) return { ok: false, error: "not_allowed" };
  const res = await messageHandler(msg, {});
  return res === undefined ? undefined : JSON.parse(JSON.stringify(res));
});

/** What the floating card may call: it reads settings and opens Settings, nothing else. */
const CARD_CALLS = new Set(["storage.get", "openOptions"]);
ipcMain.handle("lamha:call", async (e, method, args) => {
  args = Array.isArray(args) ? args : [];
  const card = fromCard(e);
  if (card && !CARD_CALLS.has(method)) throw new Error("not allowed from the card: " + method);
  if (String(method).startsWith("storage.") && !["local", "sync"].includes(args[0])) throw new Error("unknown storage area");
  switch (method) {
    case "storage.get": {
      const got = await stores[args[0]].get(args[1]);
      return card ? withoutSecrets(got) : got;
    }
    case "storage.set": return stores[args[0]].set(args[1]);
    case "storage.remove": return stores[args[0]].remove(args[1]);
    case "openOptions": openOptions(args[0] || ""); return true;
    case "openUrl": openUrl(args[0]); return true;
    case "commands": return [];
    case "update.state": return { ...updater.state, current: app.getVersion(), packaged: app.isPackaged, portable: !!process.env.PORTABLE_EXECUTABLE_DIR, releases: updater.releasesUrl };
    case "update.check": await updater.check(true); return { ...updater.state };
    case "update.restart": updater.restart(); return true;
    case "firefox.status": return { available: !!bridge, connected: !!(bridge && bridge.connected), lastSeen: bridge ? bridge.lastSeen : 0 };
    case "app.gpu": return GPU; // what this run draws with (Settings compares it with the setting)
    case "app.restart": restartApp(); return true;
    default: throw new Error("unknown call " + method);
  }
});

/** Starts Lamha again (Settings, after a change that applies at start). Portable runs from a copy it unpacked: the
 *  .exe the user started is the one to start again. */
function restartApp() {
  const portable = process.env.PORTABLE_EXECUTABLE_FILE;
  app.relaunch(portable ? { execPath: portable, args: [] } : undefined);
  app.quit();
}

/** Settings → Offline Wikipedia (the app's own pages, never the card). Downloads name a catalog id, never a URL. */
const WIKI_CALLS = {
  list: () => wikiLibrary.list(),
  catalog: lang => wikiLibrary.catalog(String(lang)),
  download: id => wikiLibrary.download(String(id)),
  pause: id => wikiLibrary.pause(String(id)),
  resume: id => wikiLibrary.resume(String(id)),
  cancel: id => wikiLibrary.cancel(String(id)),
  remove: id => wikiLibrary.remove(String(id)),
  addFile: async win => {
    const r = await dialog.showOpenDialog(win, { properties: ["openFile"], filters: [{ name: "Wikipedia (.zim)", extensions: ["zim"] }] });
    return r.canceled || !r.filePaths[0] ? null : wikiLibrary.addFile(r.filePaths[0]);
  },
  chooseFolder: async win => {
    const r = await dialog.showOpenDialog(win, { properties: ["openDirectory", "createDirectory"], defaultPath: wikiLibrary.folder });
    if (!r.canceled && r.filePaths[0]) await wikiLibrary.setFolder(r.filePaths[0]);
    return wikiLibrary.list();
  },
  defaultFolder: async () => { await wikiLibrary.setFolder(""); return wikiLibrary.list(); },
  openFolder: async () => {
    await fs.promises.mkdir(wikiLibrary.folder, { recursive: true });
    return shell.openPath(wikiLibrary.folder);
  },
  // the reader
  article: (fileId, articlePath) => wikiLibrary.article(String(fileId || ""), String(articlePath || "")),
  suggest: (query, lang) => wikiLibrary.suggest(String(query || ""), { lang: String(lang || "") }),
  random: fileId => wikiLibrary.random(String(fileId || "")),
  openReader: (fileId, articlePath) => { openReader({ file: String(fileId || ""), path: String(articlePath || "") }); return true; }
};
const WIKI_WINDOW = new Set(["addFile", "chooseFolder"]); // they open a dialog over the page's window
ipcMain.handle("lamha:wiki", async (e, method, args) => {
  if (!wikiLibrary || fromCard(e) || !Object.hasOwn(WIKI_CALLS, method)) return { ok: false, error: "unavailable" };
  try {
    const a = Array.isArray(args) ? args : [];
    return { ok: true, data: await WIKI_CALLS[method](...(WIKI_WINDOW.has(method) ? [BrowserWindow.fromWebContents(e.sender)] : a)) };
  } catch (err) {
    return { ok: false, error: err.code || "failed" };
  }
});

/* ---------------- permissions ---------------- */

/** What Lamha's pages may use: writing to the clipboard (the Copy buttons). Electron grants everything a page asks for
 *  unless told otherwise (camera, microphone, location, notifications…); the card shows text from any website or
 *  program, so nothing else is granted, asked or merely checked. */
const PAGE_PERMISSIONS = new Set(["clipboard-sanitized-write"]);
function lockPermissions() {
  const { session } = require("electron");
  const ses = session.defaultSession;
  ses.setPermissionRequestHandler((_wc, permission, done) => done(PAGE_PERMISSIONS.has(permission)));
  ses.setPermissionCheckHandler((_wc, permission) => PAGE_PERMISSIONS.has(permission));
  ses.setDevicePermissionHandler(() => false); // USB, serial, HID
}

/* ---------------- windows ---------------- */

function webPrefs() {
  return {
    preload: path.join(__dirname, "preload.js"),
    contextIsolation: true,
    sandbox: true,
    nodeIntegration: false,
    autoplayPolicy: "no-user-gesture-required",
    spellcheck: true
  };
}

/** Desktop look + keep links in the default browser. */
function prepare(win) {
  const wc = win.webContents;
  wc.on("dom-ready", () => {
    wc.insertCSS(DESKTOP_CSS);
    // pages call window.close() after opening Settings (popup behaviour) — keep the window open
    wc.executeJavaScript("document.documentElement.classList.add('desktop'); window.close = () => {}; true").catch(() => {});
  });
  wc.setWindowOpenHandler(({ url }) => { openUrl(url); return { action: "deny" }; });
  wc.on("will-navigate", (e, url) => {
    if (isAppPage(url)) return; // e.g. a page reloading itself when the interface language changes
    e.preventDefault();
    if (!url.startsWith("file:")) openUrl(url); // other local files never load here: they would get the preload's bridge
  });
}

/** A file: URL of Lamha's own pages (the extension folder or renderer/). */
function isAppPage(url) {
  let file;
  try { file = require("node:url").fileURLToPath(url); } catch (_) { return false; }
  return [EXT_DIR, path.join(__dirname, "renderer")].some(dir => isInside(dir, file));
}

/** `file` is `dir` itself or somewhere below it (not a sibling that merely starts with the same name). */
function isInside(dir, file) {
  const rel = path.relative(dir, file);
  return rel === "" || (!!rel && !rel.startsWith("..") && !path.isAbsolute(rel));
}

function createMain() {
  mainWin = new BrowserWindow({
    width: 480, height: 720, minWidth: 380, minHeight: 480,
    title: T("common.lamha"), icon: ICON, show: false, autoHideMenuBar: true,
    backgroundColor: "#f5f5f7",
    webPreferences: webPrefs()
  });
  prepare(mainWin);
  mainWin.loadFile(path.join(EXT_DIR, "popup", "popup.html"));
  if (clipboardMonitor) { // the الحافظة tab: added by the desktop app, popup.html itself is the extension's
    mainWin.webContents.on("did-finish-load", () => injectClipboardUi(mainWin.webContents, "tab.js").catch(err => console.error("clipboard tab failed:", err && err.message)));
  }
  mainWin.once("ready-to-show", () => { if (!START_HIDDEN && !SMOKE) mainWin.show(); });
  mainWin.on("close", e => { // the X button hides to the tray; "Exit" in the tray menu quits
    if (!quitting) { e.preventDefault(); mainWin.hide(); }
  });
}

const TAB_IDS = { translate: "tabTr", write: "tabWr", review: "tabRv", clipboard: "tabCb" };
function showMain(tab) {
  if (!mainWin || mainWin.isDestroyed()) createMain();
  if (mainWin.isMinimized()) mainWin.restore();
  if (!SMOKE) mainWin.show();
  mainWin.focus();
  if (TAB_IDS[tab]) mainWin.webContents.executeJavaScript(`document.getElementById("${TAB_IDS[tab]}").click(); true`).catch(() => {});
}

/** suffix: "?welcome=1", "#ai", "#journal" … */
function openOptions(suffix) {
  const target = rules.optionsTarget(suffix);
  if (optionsWin && !optionsWin.isDestroyed()) {
    optionsWin.loadFile(path.join(EXT_DIR, "options", "options.html"), target);
  } else {
    optionsWin = new BrowserWindow({
      width: 820, height: 860, minWidth: 420, title: T("d.settingsTitle"), icon: ICON, show: false,
      autoHideMenuBar: true, backgroundColor: "#f5f5f7", webPreferences: webPrefs()
    });
    prepare(optionsWin);
    if (clipboardMonitor) { // settings → الحافظة: added by the desktop app (options.html is the extension's)
      const wc = optionsWin.webContents;
      wc.on("did-finish-load", () => injectClipboardUi(wc, "settings.js").catch(err => console.error("clipboard settings failed:", err && err.message)));
    }
    optionsWin.loadFile(path.join(EXT_DIR, "options", "options.html"), target);
    optionsWin.once("ready-to-show", () => { if (!SMOKE) optionsWin.show(); });
    optionsWin.on("closed", () => { optionsWin = null; });
  }
  if (!SMOKE) { optionsWin.show(); optionsWin.focus(); }
}

function openUrl(url) {
  const u = String(url || "");
  if (u.startsWith(BASE)) {
    const rel = u.slice(BASE.length);
    if (rel.startsWith("options/options.html")) openOptions(rel.slice("options/options.html".length));
    else showMain();
  } else if (/^https?:\/\//i.test(u)) {
    shell.openExternal(u);
  }
}

/* ---------------- the Wikipedia reader (renderer/wiki/) ---------------- */

/** Opens the reader (or brings it forward) at an article; no path: its start page. */
function openReader({ file = "", path: articlePath = "" } = {}) {
  if (readerWin && !readerWin.isDestroyed()) {
    readerWin.webContents.send("lamha:wiki-open", { file, path: articlePath });
    if (readerWin.isMinimized()) readerWin.restore();
    if (!SMOKE) { readerWin.show(); readerWin.focus(); }
    return readerWin;
  }
  readerWin = new BrowserWindow({
    width: 1100, height: 820, minWidth: 420, minHeight: 420, title: T("d.rTitle"), icon: ICON, show: false,
    autoHideMenuBar: true, backgroundColor: nativeTheme.shouldUseDarkColors ? "#1c1c1e" : "#f5f5f7", webPreferences: webPrefs()
  });
  prepare(readerWin);
  const wc = readerWin.webContents;
  wc.on("did-finish-load", async () => { // again after a reload (the interface language changed)
    try {
      for (const f of ["ui.css", "motion.css"]) await wc.insertCSS(fs.readFileSync(path.join(EXT_DIR, "shared", f), "utf8"));
      const scripts = [
        ...["theme.js", "i18n.js", "motion.js"].map(f => path.join(EXT_DIR, "shared", f)),
        path.join(__dirname, "renderer", "i18n-desktop.js"),
        path.join(__dirname, "renderer", "wiki", "sanitize.js"), path.join(__dirname, "renderer", "wiki", "reader.js"),
        // Lamha's lookup in articles: the scripts Firefox puts into web pages
        ...["shared/lamha-ai.js", "content/styles.js", "content/page-translator.js", "content/content.js"].map(f => path.join(EXT_DIR, f))
      ];
      for (const f of scripts) await wc.executeJavaScript(fs.readFileSync(f, "utf8") + "\n;true");
    } catch (err) { console.error("wikipedia reader failed:", err && err.message); }
  });
  readerWin.on("app-command", (_e, cmd) => { // the mouse's back and forward buttons
    if (cmd === "browser-backward" || cmd === "browser-forward") wc.send("lamha:wiki-open", { nav: cmd === "browser-backward" ? "back" : "forward" });
  });
  readerWin.loadFile(path.join(__dirname, "renderer", "wiki", "reader.html"), { query: { file, path: articlePath } });
  readerWin.once("ready-to-show", () => { if (!SMOKE) readerWin.show(); });
  readerWin.on("closed", () => { readerWin = null; });
  return readerWin;
}

/** lamha-wiki://zim/<file id>/<path>: a picture from a downloaded Wikipedia, for the reader and the card. Pictures
 *  only (wiki-library.js asset()); anything else, or a file that isn't there, is "not found". */
function serveWikiAssets() {
  protocol.handle("lamha-wiki", async req => {
    try {
      const url = new URL(req.url);
      const [fileId, ...rest] = url.pathname.slice(1).split("/").map(decodeURIComponent);
      const a = url.host === "zim" && wikiLibrary && rest.length ? await wikiLibrary.asset(fileId, rest.join("/")) : null;
      if (a) return new Response(a.data, { headers: { "content-type": a.mime, "cache-control": "max-age=86400", "x-content-type-options": "nosniff" } });
    } catch (_) { /* a broken address */ }
    return new Response("", { status: 404 });
  });
}

/* ---------------- the card over other apps (global shortcuts) ----------------
 * Alt+Shift+L: look up / translate the selection in any app. Alt+Shift+W: writing tools on it;
 * "استبدال" (Replace) pastes the result back into that app. */

const HOTKEYS = { "Alt+Shift+L": "lookup", "Alt+Shift+W": "write", "Alt+Shift+V": "clipboard" };
const HOTKEY_LABELS = { lookup: "d.hkLookup", write: "d.hkWrite", clipboard: "d.hkClipboard" }; // keys: renderer/i18n-desktop.js
const CARD_W = 480, CARD_H = 620;
let cardWin = null, cardReady = null, cardSource = null, cardShownAt = 0, hotkeyBusy = false, hotkeyErrors = [];
const selection = process.platform === "win32" ? require("./selection") : null;
const native = process.platform === "win32" ? require("./native") : null;
/** The sentence around a word selected in another app (Settings → «فهم الكلمة من سياق الجملة»), see uia-context.js. */
const uiaContext = process.platform === "win32" ? new (require("./uia-context").UiaContext)() : null;
const isWordish = t => t.split(/\s+/).length <= 3 && t.length <= 40 && !/[.!?;:]\s|[\n\r]/.test(t); // as background.js decides

/** Keeps the helper (uia-context.js) running while the sentence setting is on, so the first shortcut finds it ready. */
function startContextHelper() {
  if (!uiaContext || SMOKE) return;
  const apply = async () => {
    const s = await stores.sync.get({ useContext: true });
    if (s.useContext !== false) uiaContext.start(); else uiaContext.stop();
  };
  apply().catch(err => console.error("helper settings failed:", err && err.name));
  storageListeners.push((changes, area) => {
    if (area === "sync" && changes.useContext) apply().catch(err => console.error("helper settings failed:", err && err.name));
  });
}

/* ---------------- clipboard history (الحافظة) ----------------
 * clipboard-monitor.js notices copies (only while storage.local.clipboardEnabled is on — off by default),
 * clipboard-store.js keeps them in clipboard.json (encrypted) and searches them; pages use them over "lamha:clip". */
const clipboardMonitor = process.platform === "win32" ? new (require("./clipboard-monitor").ClipboardMonitor)() : null;
let clipStore = null, clipActions = null;
const { DEFAULTS: CLIP_DEFAULTS, exeName, isCardNumber } = require("./clipboard-privacy");

async function startClipboard() {
  if (!clipboardMonitor) return;
  const { ClipboardStore } = require("./clipboard-store");
  const normalizer = path.join(EXT_DIR, "shared", "arabic-normalize.js");
  vm.runInThisContext(fs.readFileSync(normalizer, "utf8"), { filename: normalizer }); // defines LamhaArabic
  clipStore = new ClipboardStore({
    file: path.join(app.getPath("userData"), "clipboard.json"),
    normalize: globalThis.LamhaArabic.normalizeForSearch,
    detectLang: globalThis.LamhaArabic.detectLang,
    safeStorage
  });
  clipStore.on("changed", clipChanged);
  clipActions = require("./clipboard-actions").createClipActions({
    store: clipStore,
    send: msg => messageHandler(msg, {}),
    targetLang: async () => (await stores.sync.get({ targetLang: "ar" })).targetLang,
    readCards: async () => (await stores.local.get({ cards: {} })).cards
  });
  selection.setSuppressor(fn => clipboardMonitor.suppress(fn));
  clipboardMonitor.on("clip-captured", c => {
    const id = clipStore.ingest(c);
    if (!app.isPackaged) console.log(`[clipboard] captured ${id}: ${c.text.length} chars${c.html ? " + html" : ""} from ${c.sourceApp}`); // never the content
  });
  // privacy (settings → الحافظة): excluded programs are never read, bank-card numbers are skipped
  let excluded = new Set(CLIP_DEFAULTS.clipboardExcludedApps), skipCards = true;
  clipboardMonitor.skipApp = exe => excluded.has(exe);
  clipboardMonitor.skipText = text => skipCards && isCardNumber(text);
  const apply = s => { // a removed key comes back as its default
    const v = k => (s[k] === undefined ? CLIP_DEFAULTS[k] : s[k]);
    if ("clipboardMaxItems" in s) clipStore.setMaxItems(v("clipboardMaxItems"));
    if ("clipboardExcludedApps" in s) excluded = new Set([].concat(v("clipboardExcludedApps")).map(String));
    if ("clipboardSkipCards" in s) skipCards = v("clipboardSkipCards") !== false;
    if ("clipboardExpiryDays" in s) { expiryDays = Number(v("clipboardExpiryDays")) || 0; runClipExpiry(); }
    if ("clipboardEnabled" in s) {
      clipboardMonitor.setEnabled(v("clipboardEnabled"));
      // the quick panel's window is kept ready (it must show within 150 ms) only while there's a history to show;
      // with history off, Alt+Shift+V opens it the first time it's pressed (a moment slower, to say how to turn it on)
      if (v("clipboardEnabled") && (!panelWin || panelWin.isDestroyed())) createPanelWin();
      if (!v("clipboardEnabled")) resumeClipboard();
      updateTray();
    }
  };
  storageListeners.push((changes, area) => {
    if (area !== "local") return;
    const mine = Object.entries(changes).filter(([k]) => Object.hasOwn(CLIP_DEFAULTS, k));
    if (mine.length) apply(Object.fromEntries(mine.map(([k, c]) => [k, c.newValue])));
  });
  apply(await stores.local.get({ ...CLIP_DEFAULTS }));
  setInterval(() => runClipExpiry(), 3600e3).unref(); // and once at startup (apply above)
}

/* ---- expiry and pause ---- */
let expiryDays = CLIP_DEFAULTS.clipboardExpiryDays;
/** Deletes unpinned clips not copied or used for longer than the setting. `now` is for the self-test's mocked clock. */
function runClipExpiry(now = Date.now()) {
  if (!clipStore) return 0;
  const n = clipStore.expire(expiryDays, now);
  if (n && !app.isPackaged) console.log(`[clipboard] expired ${n} clip(s)`);
  return n;
}

let pausedUntil = 0, pauseTimer = null; // Infinity = until resumed; never kept across a restart
/** Tray → إيقاف الحافظة مؤقتًا: `ms` = 15 min / 1 h, or 0 for "until resumed". */
function pauseClipboard(ms) {
  if (!clipboardMonitor) return;
  clearTimeout(pauseTimer);
  pauseTimer = null;
  pausedUntil = ms ? Date.now() + ms : Infinity;
  clipboardMonitor.setPaused(true);
  if (ms) { pauseTimer = setTimeout(resumeClipboard, ms); pauseTimer.unref(); }
  updateTray();
  clipChanged();
}

function resumeClipboard() {
  clearTimeout(pauseTimer);
  pauseTimer = null;
  if (!pausedUntil) return;
  pausedUntil = 0;
  clipboardMonitor.setPaused(false);
  updateTray();
  clipChanged();
}

/** "changed" to every page, at most 5 times a second. */
let clipChangeTimer = null, clipChangeSent = 0;
function clipChanged() {
  if (clipChangeTimer) return;
  clipChangeTimer = setTimeout(() => {
    clipChangeTimer = null;
    clipChangeSent = Date.now();
    for (const w of BrowserWindow.getAllWindows()) if (!w.isDestroyed() && w !== cardWin) w.webContents.send("lamha:clip-changed");
  }, Math.max(0, 200 - (Date.now() - clipChangeSent)));
}

const notFound = () => Object.assign(new Error("no such clip"), { code: "not_found" });
const CLIP_CALLS = {
  list: async opts => clipStore.list({ ...opts, tl: (await stores.sync.get({ targetLang: "ar" })).targetLang }),
  get: id => clipStore.get(id),
  setPinned: (id, pinned) => clipStore.setPinned(id, pinned),
  setLabel: (id, text) => clipStore.setLabel(id, text),
  remove: id => clipStore.remove(id),
  restore: id => clipStore.restore(id),
  clear: opts => clipStore.clear(opts),
  markUsed: id => clipStore.markUsed(id),
  status: () => ({
    enabled: clipboardMonitor.enabled, encrypted: clipStore.canEncrypt,
    paused: !!pausedUntil, pausedUntil: pausedUntil === Infinity ? null : pausedUntil
  }),
  /** Off with deleteHistory: the history is emptied on disk too. */
  setEnabled: async (on, deleteHistory) => {
    await stores.local.set({ clipboardEnabled: !!on });
    if (!on && deleteHistory) { clipStore.clear(); clipStore.flush(); }
    return clipboardMonitor.enabled;
  },
  resume: () => { resumeClipboard(); return true; },
  apps: () => clipStore.apps(),
  removeApp: exe => clipStore.removeApp(String(exe)),
  /** Adds a program to «البرامج المستثناة» (by exe name); returns the normalized name. */
  excludeApp: async exe => {
    const name = exeName(exe);
    if (!name) throw Object.assign(new Error("bad name"), { code: "bad_app" });
    const { clipboardExcludedApps: list } = await stores.local.get({ clipboardExcludedApps: CLIP_DEFAULTS.clipboardExcludedApps });
    if (!list.includes(name)) await stores.local.set({ clipboardExcludedApps: [...list, name] });
    return name;
  },
  includeApp: async exe => {
    const { clipboardExcludedApps: list } = await stores.local.get({ clipboardExcludedApps: CLIP_DEFAULTS.clipboardExcludedApps });
    await stores.local.set({ clipboardExcludedApps: list.filter(x => x !== exeName(exe)) });
    return true;
  },
  /** The tab's نسخ: onto the clipboard with its formatting (or plain), not captured again. */
  copy: async (id, plain) => {
    const c = clipStore.get(id);
    if (!c) throw notFound();
    await selection.copyClip(plain ? { text: c.text } : c);
    clipStore.markUsed(id);
    return true;
  },
  paste: (id, plain) => pasteFromPanel(id, plain),
  closePanel: () => { hidePanel(); return true; },
  /** Lamha's tools on a clip: translate / english / proofread / summary / review (see clipboard-actions.js). */
  action: (id, action, fresh, lang) => clipActions.run(id, action, { fresh: !!fresh, lang }),
  /** ابحث: the lookup card, as Alt+Shift+L opens it; from the panel, its Replace goes to the remembered app. */
  lookup: (id, fromPanel) => lookupClip(id, fromPanel),
  /** لصق on a tool's result (after its preview) — the panel's target app, not captured as a new clip. */
  pasteResult: (id, text) => pasteResultFromPanel(id, text),
  /** نسخ on a tool's result: a normal copy, so it becomes a clip from "lamha" (like the card's نسخ). */
  copyText: async text => { await clipboard.writeText(String(text || "")); return true; }
};
const PANEL_ONLY = new Set(["paste", "closePanel", "pasteResult"]);
ipcMain.handle("lamha:clip", async (e, method, args) => {
  if (!clipStore || fromCard(e) || !Object.hasOwn(CLIP_CALLS, method)) return { ok: false, error: "unavailable" };
  if (PANEL_ONLY.has(method) && !(panelWin && !panelWin.isDestroyed() && e.sender === panelWin.webContents)) return { ok: false, error: "unavailable" };
  if (method === "lookup") args = [args && args[0], !!(panelWin && e.sender === panelWin.webContents)]; // not the page's word
  try {
    return { ok: true, data: await CLIP_CALLS[method](...(args || [])) };
  } catch (err) {
    return { ok: false, error: err.code || "failed" }; // never the message: it could carry clip text
  }
});

/* ---- the clipboard UI: quick panel (Alt+Shift+V) and the الحافظة tab ---- */
const PANEL_W = 380, PANEL_H = 460;
const CLIP_UI = path.join(__dirname, "renderer", "clipboard");
let panelWin = null, panelReady = null, panelTarget = 0, panelShownAt = 0;

/** Styles and scripts of the clipboard UI; the shared/ ones come from the extension folder, like the card's. */
async function injectClipboardUi(wc, entry, { standalone = false } = {}) {
  const read = f => fs.readFileSync(f, "utf8");
  if (standalone) { // popup.html has these already
    await wc.insertCSS(read(path.join(EXT_DIR, "shared", "ui.css")));
    await wc.insertCSS(read(path.join(EXT_DIR, "shared", "motion.css")));
  }
  await wc.insertCSS(read(path.join(CLIP_UI, "clipboard.css")));
  const scripts = [
    ...(standalone ? ["theme.js", "i18n.js", "lamha-ai.js", "motion.js"].map(f => path.join(EXT_DIR, "shared", f)) : []),
    path.join(__dirname, "renderer", "i18n-desktop.js"),
    ...(entry === "settings.js" ? [path.join(EXT_DIR, "shared", "changelog.js")] : []), // Settings → Updates → what's new
    ...(entry === "settings.js" ? [] : [path.join(EXT_DIR, "shared", "dialog.js")]), // options.html loads it itself
    path.join(EXT_DIR, "shared", "arabic-normalize.js"), path.join(CLIP_UI, "clip-list.js"), path.join(CLIP_UI, "clip-actions.js"), path.join(CLIP_UI, entry),
    ...(entry === "settings.js" ? [path.join(__dirname, "renderer", "wiki-settings.js")] : []) // Settings → Offline Wikipedia
  ];
  for (const f of scripts) {
    await wc.executeJavaScript(read(f) + "\n;true");
  }
}

function createPanelWin() {
  panelWin = new BrowserWindow({
    width: PANEL_W, height: PANEL_H, show: false, frame: false, resizable: false, skipTaskbar: true, alwaysOnTop: true,
    minimizable: false, maximizable: false, fullscreenable: false,
    title: T("d.panelTitle"), icon: ICON, backgroundColor: nativeTheme.shouldUseDarkColors ? "#2c2c2e" : "#ffffff",
    webPreferences: webPrefs()
  });
  prepare(panelWin);
  panelWin.setAlwaysOnTop(true, "pop-up-menu");
  const wc = panelWin.webContents;
  let firstLoad;
  panelReady = new Promise(resolve => { firstLoad = resolve; });
  wc.on("did-finish-load", () => { // again after a reload (the interface language changed)
    const done = injectClipboardUi(wc, "panel.js", { standalone: true }).catch(err => console.error("clipboard panel failed:", err && err.message));
    panelReady = done;
    firstLoad(done);
  });
  panelWin.loadFile(path.join(CLIP_UI, "panel.html"));
  panelWin.on("blur", () => { if (Date.now() - panelShownAt > 300) hidePanel(); });
  panelWin.on("closed", () => { panelWin = null; });
}

function hidePanel() {
  if (panelWin && !panelWin.isDestroyed() && panelWin.isVisible()) panelWin.hide();
}

/** Next to the mouse, inside the work area of the monitor it's on. */
function placePanel() {
  const cursor = screen.getCursorScreenPoint();
  panelWin.setBounds(rules.panelPlacement({ cursor, workArea: screen.getDisplayNearestPoint(cursor).workArea, w: PANEL_W, h: PANEL_H }));
}

/**
 * Opens the quick panel at the mouse and remembers the window in front as the paste target
 * (`target: 0` from the tray: nothing to paste into, so Enter copies). Pressed again, it closes.
 */
async function openPanel({ target } = {}) {
  if (!panelWin || panelWin.isDestroyed()) createPanelWin();
  if (panelWin.isVisible()) { hidePanel(); return; }
  const fg = target === undefined ? native.foreground() : target;
  panelTarget = fg && fg !== native.hwndOf(panelWin) ? fg : 0;
  await panelReady;
  placePanel();
  panelWin.webContents.send("lamha:clip-panel", { enabled: clipboardMonitor.enabled, paused: !!pausedUntil });
  panelShownAt = Date.now();
  panelWin.show();
  panelWin.focus();
  native.forceForeground(native.hwndOf(panelWin)); // so typing goes to the search box at once
}

/** ابحث on a clip: the lookup card at the mouse. From the panel, its Replace pastes into the panel's target app. */
async function lookupClip(id, fromPanel) {
  const c = clipStore.get(id);
  if (!c) throw notFound();
  const hwnd = fromPanel ? panelTarget : 0;
  hidePanel();
  await showCard("lookup", { hwnd, text: c.text.trim(), terminal: !!hwnd && native.isTerminal(hwnd) });
  return true;
}

/** لصق on a tool's result, after the user saw it: into the panel's target app. */
async function pasteResultFromPanel(id, text) {
  text = String(text || "");
  if (!text.trim()) return false;
  hidePanel();
  const ok = await selection.pasteClip(panelTarget, { text });
  if (clipStore.get(id)) clipStore.markUsed(id);
  if (!ok) notify(T("d.copiedTitle"), T("d.pasteYourself"));
  return ok;
}

/** Enter in the panel: paste into the remembered app; the clip stays on the clipboard, like Win+V. */
async function pasteFromPanel(id, plain) {
  const c = clipStore.get(id);
  if (!c) throw notFound();
  hidePanel();
  const ok = await selection.pasteClip(panelTarget, plain ? { text: c.text } : c);
  clipStore.markUsed(id);
  if (!ok) notify(T("d.copiedTitle"), T("d.pasteYourself"));
  return ok;
}

function createCardWin() {
  cardWin = new BrowserWindow({
    width: CARD_W, height: CARD_H, show: false, frame: false, transparent: true, resizable: false,
    skipTaskbar: true, alwaysOnTop: true, hasShadow: false, backgroundColor: "#00000000",
    title: T("common.lamha"), icon: ICON, webPreferences: webPrefs()
  });
  prepare(cardWin);
  cardWin.setAlwaysOnTop(true, "pop-up-menu");
  // the same scripts Firefox injects into web pages, in the same order
  const wc = cardWin.webContents;
  cardReady = new Promise(resolve => wc.once("did-finish-load", async () => {
    for (const f of ["shared/i18n.js", "shared/lamha-ai.js", "shared/motion.js", "content/styles.js", "content/page-translator.js", "content/content.js"]) {
      await wc.executeJavaScript(fs.readFileSync(path.join(EXT_DIR, f), "utf8") + "\n;true");
    }
    resolve();
  }));
  cardWin.loadFile(path.join(__dirname, "renderer", "card.html"));
  cardWin.on("blur", () => { if (Date.now() - cardShownAt > 400) hideCard(); });
  cardWin.on("closed", () => { cardWin = null; });
}

function hideCard() {
  if (cardWin && !cardWin.isDestroyed() && cardWin.isVisible()) cardWin.hide();
}

const fromCard = e => cardWin && !cardWin.isDestroyed() && e.sender === cardWin.webContents;
ipcMain.on("lamha:card-hover", (e, over) => { if (fromCard(e)) cardWin.setIgnoreMouseEvents(!over, { forward: true }); });
ipcMain.on("lamha:card-closed", e => { if (fromCard(e)) hideCard(); });
ipcMain.handle("lamha:card-replace", async (e, text) => {
  if (!fromCard(e) || !cardSource) return false;
  hideCard();
  const ok = await selection.pasteInto(cardSource.hwnd, text);
  if (!ok) { // the app went away or refused focus: leave the text on the clipboard instead
    await clipboard.writeText(text);
    notify(T("d.replaceFailed"), T("d.replaceFailedBody"));
  }
  return ok;
});

/** Places the card window next to the mouse; returns where the card should attach inside it. */
function placeCardWin({ toast = false } = {}) {
  const cursor = screen.getCursorScreenPoint();
  const { bounds, point } = rules.cardPlacement({ cursor, workArea: screen.getDisplayNearestPoint(cursor).workArea, w: CARD_W, h: CARD_H, toast });
  cardWin.setBounds(bounds);
  return point;
}

/** What a shortcut does: grab the selection from the app in front and open the card on it. */
async function onHotkey(kind) {
  if (hotkeyBusy || !selection) return;
  hotkeyBusy = true;
  try {
    if (!cardWin || cardWin.isDestroyed()) createCardWin();
    const cap = await selection.captureSelection(); // first: the user's app must still be in front
    const text = cap.text.trim();
    if (kind === "lookup" && uiaContext && text && isWordish(text) && (await stores.sync.get({ useContext: true })).useContext !== false) {
      cap.context = await uiaContext.around(cap.hwnd, text); // still before the card takes the focus; null after 700 ms
    }
    await showCard(kind, cap);
  } catch (err) {
    console.error("shortcut failed:", err && err.name); // the name only: a message could quote the selection
  } finally {
    hotkeyBusy = false;
  }
}

/** The card next to the mouse for `cap` ({ hwnd, text, terminal }); Replace pastes into cap.hwnd when there is one. */
async function showCard(kind, cap) {
  if (!cardWin || cardWin.isDestroyed()) createCardWin();
  const wc = cardWin.webContents;
  await cardReady;
  cardSource = cap;
  // nothing selected: the writing shortcut opens "Write new" (Insert pastes at the app's cursor); lookup shows a hint
  if (!cap.text && kind !== "write") { // a short hint next to the mouse
    placeCardWin({ toast: true });
    cardWin.setIgnoreMouseEvents(true);
    cardWin.showInactive();
    wc.send("lamha:page-message", { type: "showLookup", external: true });
    setTimeout(() => { if (!cardWin.isFocused()) hideCard(); }, 1600);
    return;
  }
  const point = placeCardWin();
  cardWin.setIgnoreMouseEvents(true, { forward: true }); // until the mouse is over the card
  cardShownAt = Date.now();
  cardWin.show();
  cardWin.focus();
  native.forceForeground(native.hwndOf(cardWin)); // so Esc and typing work at once
  wc.send("lamha:page-message", {
    type: kind === "write" ? "showWrite" : "showLookup",
    text: cap.text, external: true, replaceable: !cap.terminal && !!cap.hwnd, point,
    context: cap.context || null // { before, after } from the app, when it could be read
  });
}

function registerHotkeys() {
  hotkeyErrors = [];
  for (const [accel, kind] of Object.entries(HOTKEYS)) {
    const run = kind === "clipboard"
      ? () => openPanel().catch(err => console.error("clipboard panel failed:", err && err.message))
      : () => onHotkey(kind);
    if (!globalShortcut.register(accel, run)) hotkeyErrors.push(accel);
  }
  if (hotkeyErrors.length) notify(T("d.hotkeyTaken"), T("d.hotkeyTakenBody", { keys: hotkeyErrors }));
  if (tray) tray.setContextMenu(trayMenu());
}

function notify(title, body, onClick) {
  if (SMOKE || !Notification.isSupported()) { console.log(`[notify] ${title}: ${body}`); return; }
  const note = new Notification({ title: T("d.notePrefix") + title, body, icon: ICON });
  if (onClick) note.on("click", onClick);
  note.show();
}

/* ---------------- updates (GitHub Releases, see updater.js) ---------------- */

const updater = require("./updater").createUpdater({
  version: app.getVersion(),
  packaged: app.isPackaged,
  portable: !!process.env.PORTABLE_EXECUTABLE_DIR, // set by the Portable build's launcher
  autoUpdater: () => require("electron-updater").autoUpdater,
  fetchJson: async url => {
    const r = await netFetch(url, { headers: { Accept: "application/vnd.github+json", "User-Agent": "Lamha/" + app.getVersion() } });
    if (!r.ok) throw new Error("HTTP " + r.status);
    return r.json();
  },
  notify,
  t: (key, vars) => T(key, vars),
  onChange: () => {
    updateTray();
    for (const w of BrowserWindow.getAllWindows()) if (!w.isDestroyed()) w.webContents.send("lamha:update-changed");
  },
  openUrl
});

/** Tray item: check, or install / download what was found. */
function updateMenu() {
  const st = updater.state;
  if (st.status === "ready") return { label: T("d.upRestart", { v: st.version }), click: () => updater.restart() };
  if (st.status === "available") return { label: T("d.upDownload", { v: st.version }), click: () => updater.restart() };
  if (st.status === "downloading") return { label: T("d.upDownloading", { v: st.version }), enabled: false };
  if (st.status === "checking") return { label: T("d.upChecking"), enabled: false };
  return { label: T("d.upCheck"), click: () => updater.check(true) };
}

function startUpdates() {
  if (SMOKE) return; // the self-test never goes to GitHub
  stores.local.get({ updatesAuto: true }).then(s => updater.schedule(s.updatesAuto !== false));
  storageListeners.push((changes, area) => {
    if (area === "local" && changes.updatesAuto) updater.schedule(changes.updatesAuto.newValue !== false);
  });
}

/* ---------------- the Firefox extension's link (native-bridge.js) ---------------- */

let bridge = null;
/** What the extension may ask. Everything it sends is checked here: it arrives from another program. */
const BRIDGE_CALLS = {
  hello: () => ({ app: "lamha", version: app.getVersion(), wiki: { langs: wikiLibrary ? wikiLibrary.langs() : [] } }),
  wikiSummary: msg => {
    const a = rules.wikiSummaryArgs(msg);
    return a && wikiLibrary ? wikiLibrary.summary(a.titles, a.lang) : null;
  },
  openReader: msg => { openReader({ file: String(msg.file || "").slice(0, 200), path: String(msg.path || "").slice(0, 500) }); return true; },
  // the review deck shared with Firefox: its words merged in, or a page of this app's (background.js answerDeckSync,
  // which rebuilds every card it's given)
  deckSync: async msg => {
    const r = await messageHandler({ type: "deckSync", upload: msg.upload === true, cards: msg.cards, removed: msg.removed, since: msg.since }, {});
    if (!r || !r.ok) throw Object.assign(new Error("deck sync"), { code: "failed" });
    return r.data;
  }
};

/** Settings → Firefox (storage.local firefoxLink, on unless turned off): Firefox can reach this app only while it's on. */
function startBridge() {
  if (process.platform !== "win32") return;
  const launch = app.isPackaged
    ? { app: process.env.PORTABLE_EXECUTABLE_FILE || process.execPath, args: ["--hidden"] }
    : { app: process.execPath, args: [app.getAppPath(), "--hidden"] }; // from source: electron.exe and this folder
  bridge = new NativeBridge({
    dir: path.join(app.getPath("userData"), "firefox"), launch, handlers: BRIDGE_CALLS,
    ...(SMOKE || DEV_PROFILE ? { register: async () => {}, unregister: async () => {} } : {}) // the self-test never touches the real registry
  });
  bridge.on("change", () => { if (optionsWin && !optionsWin.isDestroyed()) optionsWin.webContents.send("lamha:firefox-changed"); });
  const apply = on => (on ? bridge.enable() : bridge.disable()).catch(err => console.error("Firefox link:", err && (err.code || err.message)));
  stores.local.get({ firefoxLink: true }).then(s => apply(s.firefoxLink !== false));
  storageListeners.push((changes, area) => {
    if (area === "local" && changes.firefoxLink) apply(changes.firefoxLink.newValue !== false);
  });
}

/* ---------------- tray, badge, reminders ---------------- */

function trayMenu() {
  const login = app.getLoginItemSettings();
  return Menu.buildFromTemplate([
    { label: T("d.trayOpen"), click: () => showMain() },
    ...(clipboardMonitor ? [{ label: T("d.trayClipboard"), click: () => openPanel({ target: 0 }).catch(() => {}) }] : []), // no app to paste into: Enter copies
    { label: T("d.trayReview", { n: badgeCount }), click: () => showMain("review") },
    { label: T("d.trayWrite"), click: () => showMain("write") },
    ...(wikiLibrary && wikiLibrary.langs().length ? [{ label: T("d.rTrayItem"), click: () => openReader() }] : []), // the downloaded Wikipedia
    ...pauseMenu(),
    motionMenu(),
    { label: T("d.traySettings"), click: () => openOptions("") },
    updateMenu(),
    { type: "separator" },
    ...Object.entries(HOTKEYS).map(([accel, kind]) => ({
      label: `${accel} — ${T(HOTKEY_LABELS[kind])}${hotkeyErrors.includes(accel) ? T("d.unavailable") : ""}`,
      enabled: false
    })),
    { type: "separator" },
    {
      label: T("d.trayLogin"), type: "checkbox", checked: login.openAtLogin,
      click: item => app.setLoginItemSettings({ openAtLogin: item.checked, args: ["--hidden"] })
    },
    { type: "separator" },
    { label: T("d.trayQuit"), click: () => { quitting = true; app.quit(); } }
  ]);
}

/** الحركة ▸ تلقائية / كاملة / خفيفة / متوقفة: the same choice as Settings → Appearance → Animations. */
function motionMenu() {
  const current = stores.sync.data.motion || "auto";
  return {
    label: T("d.trayMotion"),
    submenu: ["auto", "full", "subtle", "off"].map(v => ({
      label: T("motion." + v), type: "radio", checked: current === v,
      click: () => stores.sync.set({ motion: v })
    }))
  };
}

/** إيقاف الحافظة مؤقتًا ▸ ١٥ دقيقة / ساعة / حتى الاستئناف — or استئناف while paused. */
function pauseMenu() {
  if (!clipboardMonitor || !clipboardMonitor.enabled) return [];
  if (pausedUntil) {
    if (pausedUntil === Infinity) return [{ label: T("d.resume"), click: resumeClipboard }];
    const time = new Date(pausedUntil).toLocaleTimeString(LamhaI18n.lang() === "ar" ? "ar-EG" : "en-US", { hour: "numeric", minute: "2-digit" });
    return [{ label: T("d.resumeAt", { time }), click: resumeClipboard }];
  }
  return [{
    label: T("d.pause"),
    submenu: [
      { label: T("d.pause15"), click: () => pauseClipboard(15 * 60e3) },
      { label: T("d.pause60"), click: () => pauseClipboard(60 * 60e3) },
      { label: T("d.pauseUntil"), click: () => pauseClipboard(0) }
    ]
  }];
}

function trayTooltip() {
  const parts = [T("common.lamha")];
  if (badgeCount) parts.push(T("d.tipReview", { n: badgeCount }));
  if (pausedUntil) parts.push(T("d.tipPaused"));
  return parts.join(" — ");
}

function updateTray() {
  if (!tray) return;
  tray.setToolTip(trayTooltip());
  tray.setContextMenu(trayMenu());
}

function createTray() {
  tray = new Tray(nativeImage.createFromPath(TRAY_ICON));
  tray.setToolTip(trayTooltip());
  tray.setContextMenu(trayMenu());
  tray.on("click", () => (mainWin && mainWin.isVisible() && mainWin.isFocused() ? mainWin.hide() : showMain()));
}

/** Called by the background's updateBadge(): cards waiting for review. */
function setBadge(n) {
  const grew = n > badgeCount;
  badgeCount = n;
  updateTray();
  // a gentle reminder at most every 4 hours, only when the window isn't in use
  const idle = !mainWin || !mainWin.isVisible() || !mainWin.isFocused();
  if (!SMOKE && n > 0 && (grew || !lastReminder) && idle && Date.now() - lastReminder > 4 * 3600e3 && Notification.isSupported()) {
    lastReminder = Date.now();
    const note = new Notification({ title: T("d.notePrefix") + T("d.reviewTime"), body: T("d.reviewWaiting", { n }), icon: ICON });
    note.on("click", () => showMain("review"));
    note.show();
  }
}

/* ---------------- lifecycle ---------------- */

const gotLock = SMOKE || app.requestSingleInstanceLock();
if (!gotLock) {
  app.quit();
} else {
  app.on("second-instance", () => showMain());
  app.setAppUserModelId("com.artworklab.lamha"); // Windows notifications
  app.whenReady().then(() => {
    Menu.setApplicationMenu(null);
    lockPermissions(); // before any window opens
    startCore();
    serveWikiAssets();
    applyThemeSetting(); // before the windows open, so they start in the right colours
    createMain();
    if (selection) createCardWin(); // ready before the first shortcut, so it opens instantly
    startClipboard().catch(err => console.error("clipboard history failed to start:", err && err.name)); // runs synchronously up to its await
    // tray, notifications and window titles follow the interface language (pages redraw themselves)
    globalThis.LamhaI18n.init({ onChange: () => { updateTray(); if (mainWin && !mainWin.isDestroyed()) mainWin.setTitle(T("common.lamha")); } });
    startUpdates();
    startBridge();
    startContextHelper();
    watchMotionHint();
    if (!SMOKE) { createTray(); if (selection) registerHotkeys(); }
    if (DEV_PROFILE && process.env.LAMHA_MEMORY_USE === "1") { // scripts/memory.ps1 -Use
      require("./scripts/memory-use")({ showMain, showCard, hideCard, openOptions, getOptionsWin: () => optionsWin, getMainWin: () => mainWin })
        .catch(err => console.error("memory use failed:", err && err.name));
    }
    if (SMOKE) {
      return require(SHOTS ? "./scripts/screenshots" : "./scripts/smoke-test")({
        app, mainWin, openOptions, getOptionsWin: () => optionsWin, stores, send: msg => messageHandler(msg, {}),
        desktop: { onHotkey, uiaContext, getCardWin: () => cardWin, cardReady: () => cardReady, selection, native, clipboardMonitor, getClipStore: () => clipStore, updater,
          openPanel, hidePanel, getPanelWin: () => panelWin, panelReady: () => panelReady,
          pauseClipboard, resumeClipboard, runClipExpiry, trayTooltip, updateMotionHint, getWikiLibrary: () => wikiLibrary, openReader, getReaderWin: () => readerWin, getBridge: () => bridge }
      });
    }
  }).catch(err => {
    console.error("Lamha failed to start:", err);
    if (SMOKE) app.exit(1);
  });
  app.on("window-all-closed", () => { /* stay in the tray */ });
  app.on("will-quit", () => globalShortcut.unregisterAll());
  app.on("before-quit", () => {
    quitting = true;
    if (stores) { stores.local.flush(); stores.sync.flush(); }
    if (clipboardMonitor) clipboardMonitor.stop();
    if (clipStore) clipStore.flush();
    if (uiaContext) uiaContext.stop();
    if (wikiLibrary) wikiLibrary.saveSync();
    if (bridge) bridge.close().catch(() => {});
  });
}
