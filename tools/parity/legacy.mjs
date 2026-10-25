// Lamha 2's half: legacy/background.js in a VM with a fake `browser` and `fetch`, as legacy/tools/test-writing.mjs's
// makeEnv runs it (the real local-dict.js reading legacy/dict). A fresh one per case, so no cache or cooldown carries
// over from one case to the next (Lamha 3's half starts fresh too).
import { readFileSync } from "node:fs";
import vm from "node:vm";

const legacy = new URL("../../legacy/", import.meta.url);
const files = new Map();
const src = p => { if (!files.has(p)) files.set(p, readFileSync(new URL(p, legacy), "utf8")); return files.get(p); };
const scripts = ["shared/i18n.js", "local-dict.js", "packs.js", "shared/lamha-ai.js", "background.js"].map(f => [f, src(f)]);

function storageArea(data) {
  return {
    data,
    async get(keys) {
      if (keys == null) return { ...data };
      if (typeof keys === "string") keys = [keys];
      if (Array.isArray(keys)) return Object.fromEntries(keys.filter(k => k in data).map(k => [k, data[k]]));
      return Object.fromEntries(Object.entries(keys).map(([k, d]) => [k, k in data ? data[k] : d]));
    },
    async set(obj) { Object.assign(data, structuredClone(obj)); },
    async remove(k) { [].concat(k).forEach(x => delete data[x]); }
  };
}

function memoryPackStore() {
  const data = new Map();
  return {
    get: async k => (data.has(k) ? structuredClone(data.get(k)) : undefined),
    setMany: async o => { for (const [k, v] of Object.entries(o)) data.set(k, structuredClone(v)); },
    removePrefix: async p => { for (const k of [...data.keys()]) if (k.startsWith(p)) data.delete(k); }
  };
}

const json = (status, body) => ({ ok: status >= 200 && status < 300, status, json: async () => JSON.parse(body) });

/** The case's settings as Lamha 2 keeps them: synced settings and the device's own (storage.local). */
export function legacyStorage(s) {
  const sync = {
    uiLang: s.uiLang, targetLang: s.targetLang, dictSource: s.dictSource, useContext: s.useContext,
    translateDefinitions: s.translateDefinitions, enDict: s.enDict, explainLangs: s.explainLangs,
    showWikipedia: s.showWikipedia, saveHistory: s.saveHistory
  };
  const local = { trService: s.trService };
  if (s.aiProvider) local.aiProvider = s.aiProvider;
  if (s.aiProvider === "gemini") local.geminiKey = "AIza-test";
  if (s.ollamaModel) local.ollamaModel = s.ollamaModel;
  return { sync, local };
}

/** Runs one case through Lamha 2: { data } | { error }, and the history it left. */
export async function runLegacy(kase, network) {
  const { sync, local } = legacyStorage(kase.settings);
  const fetchImpl = async (url, init = {}) => {
    url = String(url);
    if (url.startsWith("moz-extension://x/dict/")) {
      try { return json(200, src(url.slice("moz-extension://x/".length))); } catch (_) { return json(404, "{}"); }
    }
    const body = typeof init.body === "string" ? init.body : init.body ? String(init.body) : "";
    const a = await network.answer("legacy", kase, { method: init.method || "GET", url, body });
    if (a.fail === "offline") throw new TypeError("NetworkError when attempting to fetch resource.");
    if (a.fail === "hang") {
      return new Promise((_, reject) => {
        const stop = () => reject(new DOMException("The operation was aborted.", "AbortError"));
        if (init.signal?.aborted) stop(); else init.signal?.addEventListener("abort", stop);
      });
    }
    return json(a.status, a.body);
  };
  // Timers run faster where the network is down or silent: Lamha 2's retry pauses and timeouts, scaled down together.
  const scale = ["offline", "dead"].includes(kase.net) ? 40 : 1;
  let onMessage;
  const noopEvent = { addListener() {} };
  const browser = {
    storage: { local: storageArea(local), sync: storageArea(sync), onChanged: noopEvent },
    runtime: { onMessage: { addListener: f => { onMessage = f; } }, onInstalled: noopEvent, onStartup: noopEvent, getURL: p => "moz-extension://x/" + p },
    menus: { removeAll: async () => {}, create() {}, onClicked: noopEvent },
    commands: { onCommand: noopEvent },
    tabs: { query: async () => [], sendMessage: async () => {}, create: async () => {} },
    permissions: { contains: async () => true }
  };
  const ctx = vm.createContext({
    browser, fetch: fetchImpl, console,
    setTimeout: scale === 1 ? setTimeout : (f, t = 0, ...a) => setTimeout(f, t / scale, ...a), clearTimeout,
    AbortController, URLSearchParams, structuredClone, Response, TransformStream, DecompressionStream,
    LamhaPackStore: memoryPackStore(), navigator: { onLine: !kase.offline }, Audio: class {}
  });
  for (const [filename, code] of scripts) vm.runInContext(code, ctx, { filename });
  const res = await onMessage({ type: "lookup", text: kase.text, ...(kase.context ? { context: kase.context } : {}), ...(kase.forceAi ? { engine: "ai" } : {}) }, {});
  const history = (local.history || []).slice(0, 5).map(h => ({ q: h.q, tr: h.tr, src: h.src }));
  return res.ok ? { data: res.data, history } : { error: res.error, history };
}
