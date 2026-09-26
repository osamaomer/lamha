/* Lamha desktop — gives the extension's pages (popup, options) the `browser.*` API they expect,
 * backed by the main process. Also plays speech audio for the background logic. */
"use strict";
const { contextBridge, ipcRenderer } = require("electron");

const info = ipcRenderer.sendSync("lamha:info"); // { base, version, role: "page" | "card" }
const call = (method, ...args) => ipcRenderer.invoke("lamha:call", method, args);

// messages the main process sends to the page (like tabs.sendMessage in Firefox), e.g. "showWrite"
const messageListeners = [];
ipcRenderer.on("lamha:page-message", (_e, msg) => {
  for (const f of messageListeners) { try { f(msg, {}); } catch (err) { console.error(err); } }
});

const storageListeners = [];
ipcRenderer.on("lamha:storage-changed", (_e, changes, area) => {
  for (const f of storageListeners) { try { f(changes, area); } catch (err) { console.error(err); } }
});

const area = name => ({
  get: keys => call("storage.get", name, keys),
  set: items => call("storage.set", name, items),
  remove: keys => call("storage.remove", name, keys)
});

contextBridge.exposeInMainWorld("browser", {
  storage: {
    local: area("local"),
    sync: area("sync"),
    onChanged: { addListener: f => { storageListeners.push(f); } }
  },
  runtime: {
    sendMessage: msg => ipcRenderer.invoke("lamha:message", msg),
    getURL: p => info.base + String(p).replace(/^\//, ""),
    getManifest: () => ({ version: info.version, name: "Lamha" }),
    openOptionsPage: () => call("openOptions", ""),
    onMessage: { addListener: f => { messageListeners.push(f); } }
  },
  tabs: {
    query: async () => [{ id: -1, url: "about:lamha-desktop" }],
    sendMessage: () => Promise.reject(new Error("no tabs on the desktop")),
    create: ({ url }) => call("openUrl", url)
  },
  permissions: { contains: async () => true, request: async () => true },
  i18n: { getUILanguage: () => info.locale }, // "auto" interface language follows Windows (shared/i18n.js)
  commands: { getAll: () => call("commands") }
});

/* ---- updates (Settings → التحديثات): the app's own pages ---- */
if (info.role === "page") {
  const updateListeners = [];
  ipcRenderer.on("lamha:update-changed", () => {
    for (const f of updateListeners) { try { f(); } catch (err) { console.error(err); } }
  });
  contextBridge.exposeInMainWorld("lamhaUpdates", {
    state: () => call("update.state"), // { status, version, current, packaged, portable, releases, checkedAt }
    check: () => call("update.check"),
    restart: () => call("update.restart"), // install the downloaded update (or open the download page: Portable)
    onChanged: f => { updateListeners.push(f); }
  });
}

/* ---- clipboard history (الحافظة): the app's own pages, not the card ---- */
if (info.role === "page") {
  const clip = (method, ...args) => ipcRenderer.invoke("lamha:clip", method, args); // → { ok, data } | { ok: false, error }
  const changeListeners = [];
  ipcRenderer.on("lamha:clip-changed", () => {
    for (const f of changeListeners) { try { f(); } catch (err) { console.error(err); } }
  });
  contextBridge.exposeInMainWorld("lamhaClipboard", {
    list: opts => clip("list", opts || {}), // { query, filter: "all" | "pinned", limit, offset } → { items, total }
    get: id => clip("get", id),
    setPinned: (id, pinned) => clip("setPinned", id, !!pinned), // error "pin_limit" beyond 300
    setLabel: (id, text) => clip("setLabel", id, String(text || "")),
    remove: id => clip("remove", id),
    clear: opts => clip("clear", opts || {}),
    markUsed: id => clip("markUsed", id),
    restore: id => clip("restore", id), // undo a remove (a few seconds)
    copy: (id, plain) => clip("copy", id, !!plain), // onto the clipboard (not captured again), counts as used
    paste: (id, plain) => clip("paste", id, !!plain), // quick panel only: into the app that was in front
    status: () => clip("status"), // { enabled, encrypted }
    setEnabled: (on, deleteHistory) => clip("setEnabled", !!on, !!deleteHistory),
    resume: () => clip("resume"), // end a pause
    apps: () => clip("apps"), // programs in the history: [{ app, count }]
    removeApp: exe => clip("removeApp", String(exe)), // delete that program's clips
    excludeApp: exe => clip("excludeApp", String(exe)), // → البرامج المستثناة (returns the exe name)
    includeApp: exe => clip("includeApp", String(exe)),
    closePanel: () => clip("closePanel"),
    // translate | english | proofread | summary | review; lang: the summary's language ("ar" | "en")
    action: (id, action, fresh, lang) => clip("action", id, String(action), !!fresh, lang === "en" ? "en" : "ar"),
    lookup: id => clip("lookup", id), // the lookup card
    pasteResult: (id, text) => clip("pasteResult", id, String(text || "")), // quick panel only
    copyText: text => clip("copyText", String(text || "")),
    onChanged: f => { changeListeners.push(f); },
    onPanelOpen: f => { panelListeners.push(f); }
  });
  const panelListeners = [];
  ipcRenderer.on("lamha:clip-panel", (_e, state) => {
    for (const f of panelListeners) { try { f(state); } catch (err) { console.error(err); } }
  });
}

/* ---- the floating card over other apps (card window only) ---- */
if (info.role === "card") {
  contextBridge.exposeInMainWorld("lamhaDesktop", {
    replace: text => ipcRenderer.invoke("lamha:card-replace", String(text)), // paste into the app the text came from
    closed: () => ipcRenderer.send("lamha:card-closed")
  });
  // The window is transparent and bigger than the card: let clicks outside the card reach the app below.
  let over = false;
  window.addEventListener("mousemove", e => {
    const el = document.elementFromPoint(e.clientX, e.clientY);
    const now = !!el && el.tagName === "LAMHA-UI"; // the card lives in this element's (closed) shadow root
    if (now !== over) { over = now; ipcRenderer.send("lamha:card-hover", now); }
  });
}

/* ---- speech: the background asks this window to play audio (Google's voice) ---- */
const players = new Map();
ipcRenderer.on("lamha:audio-play", (_e, id, src) => {
  const audio = new Audio(src);
  players.set(id, audio);
  for (const type of ["playing", "ended", "error", "pause"]) {
    audio.addEventListener(type, () => {
      ipcRenderer.send("lamha:audio-event", id, type);
      if (type !== "playing") players.delete(id);
    });
  }
  audio.play().catch(() => { ipcRenderer.send("lamha:audio-event", id, "error"); players.delete(id); });
});
ipcRenderer.on("lamha:audio-pause", (_e, id) => { const a = players.get(id); if (a) a.pause(); });
