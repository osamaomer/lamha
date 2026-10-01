// Tests for the writing tools: runs the real shared/lamha-ai.js + background.js in a VM with a fake
// `browser` (storage, messaging) and a fake `fetch`, so no network or API key is needed.
//   node tools/test-writing.mjs            unit tests only
//   node tools/test-writing.mjs --ollama   also one real proofread through the local Ollama
//                                          (model: $env:OLLAMA_MODEL, else the first Qwen model)
import { readFileSync } from "node:fs";
import { gzipSync } from "node:zlib";
import vm from "node:vm";
import assert from "node:assert/strict";
import { quiet, wanted, report, notRun } from "./test-args.mjs";

const root = new URL("..", import.meta.url);
const src = p => readFileSync(new URL(p, root), "utf8");

/* ---------- fake WebExtension environment ---------- */

/** `events`: listeners told about each set(), as storage.onChanged does (only in the tests that ask for it). */
function storageArea(data, name = "local", events = null) {
  return {
    data,
    async get(keys) {
      if (keys == null) return { ...data };
      if (typeof keys === "string") keys = [keys];
      if (Array.isArray(keys)) return Object.fromEntries(keys.filter(k => k in data).map(k => [k, data[k]]));
      return Object.fromEntries(Object.entries(keys).map(([k, d]) => [k, k in data ? data[k] : d]));
    },
    async set(obj) {
      const changes = Object.fromEntries(Object.entries(obj).map(([k, v]) => [k, { oldValue: data[k], newValue: structuredClone(v) }]));
      Object.assign(data, structuredClone(obj));
      if (events) events.forEach(f => f(changes, name));
    },
    async remove(k) { [].concat(k).forEach(x => delete data[x]); }
  };
}

/** Downloaded language packs (packs.js) in memory, as desktop/pack-store.js keeps them in files. */
function memoryPackStore() {
  const data = new Map();
  return {
    data,
    get: async k => (data.has(k) ? structuredClone(data.get(k)) : undefined),
    setMany: async o => { for (const [k, v] of Object.entries(o)) data.set(k, structuredClone(v)); },
    removePrefix: async p => { for (const k of [...data.keys()]) if (k.startsWith(p)) data.delete(k); }
  };
}

/** `realDict`: the real local-dict.js, reading the dictionary files from dict/ (other requests go to `fetchImpl`). */
function makeEnv({ fetchImpl, local = {}, sync = { uiLang: "ar" }, realDict = false, packStore = memoryPackStore(), wiki = null, connectNative = null, navigator = null, scale = 1, events = false }) {
  if (realDict) {
    const net = fetchImpl;
    fetchImpl = async (url, init) => {
      if (!String(url).startsWith("moz-extension://x/dict/")) return net(url, init);
      try { return json(200, JSON.parse(src(String(url).slice("moz-extension://x/".length)))); } catch (_) { return json(404, {}); }
    };
  }
  let onMessage;
  const installed = [];
  const noopEvent = { addListener() {} };
  const heard = events ? [] : null;
  const browser = {
    storage: { local: storageArea(local, "local", heard), sync: storageArea(sync, "sync", heard), onChanged: heard ? { addListener: f => heard.push(f) } : noopEvent },
    runtime: { onMessage: { addListener: f => { onMessage = f; } }, onInstalled: { addListener: f => installed.push(f) }, onStartup: noopEvent, getURL: p => "moz-extension://x/" + p,
      ...(connectNative ? { connectNative } : {}) }, // Firefox on a computer: the link to Lamha for Windows
    menus: { removeAll: async () => {}, create() {}, onClicked: noopEvent },
    commands: { onCommand: noopEvent },
    tabs: { query: async () => [], sendMessage: async () => {}, create: async () => {} },
    permissions: { contains: async () => true }
  };
  const ctx = vm.createContext({
    browser, fetch: fetchImpl, console, setTimeout: scale === 1 ? setTimeout : (f, t = 0, ...a) => setTimeout(f, t / scale, ...a), clearTimeout, AbortController, URLSearchParams, structuredClone,
    Response, TransformStream, DecompressionStream, LamhaPackStore: packStore,
    ...(wiki ? { LamhaWikiOffline: wiki } : {}), // a downloaded Wikipedia (the Windows app's desktop/wiki-library.js)
    ...(navigator ? { navigator } : {}), // navigator.onLine: the browser's, or the Windows app's (desktop/main.js, from Electron's net)
    ...(realDict ? {} : { LocalDict: {} }), Audio: class {}
  });
  vm.runInContext(src("shared/i18n.js"), ctx, { filename: "i18n.js" });
  if (realDict) vm.runInContext(src("local-dict.js"), ctx, { filename: "local-dict.js" });
  vm.runInContext(src("packs.js"), ctx, { filename: "packs.js" });
  vm.runInContext(src("shared/lamha-ai.js"), ctx, { filename: "lamha-ai.js" });
  vm.runInContext(src("background.js"), ctx, { filename: "background.js" });
  return {
    ctx, browser, packStore, send: msg => onMessage(msg, {}), flush: () => vm.runInContext("journalQueue", ctx),
    installed: details => Promise.all(installed.map(f => f(details))) // what Firefox fires on install / update
  };
}

const json = (status, body) => ({ ok: status >= 200 && status < 300, status, json: async () => body });

/** Records every request and answers with `reply(url, body)`. */
function recorder(reply) {
  const calls = [];
  const fetchImpl = async (url, init = {}) => {
    let body = null; // JSON for the AI APIs; Google's form-encoded bodies are kept as text
    if (typeof init.body === "string") { try { body = JSON.parse(init.body); } catch (_) { body = init.body; } }
    else if (init.body) body = String(init.body);
    calls.push({ url, headers: init.headers || {}, body });
    return reply(url, body, calls.length);
  };
  return { calls, fetchImpl };
}

const claudeReply = obj => json(200, { stop_reason: "end_turn", content: [{ type: "thinking", thinking: "" }, { type: "text", text: JSON.stringify(obj) }] });
const ollamaReply = obj => json(200, { done: true, done_reason: "stop", message: { role: "assistant", content: JSON.stringify(obj) } });

const PROOF = {
  corrected: "I have been to the market yesterday.",
  issues: [
    { original: "I go", fix: "I went", category: "verb_tense", why: "حدث في الماضي." },
    { original: "market", fix: "the market", category: "articles", why: "مكان معروف." },
    { original: "x", fix: "x", category: "articles", why: "no-op change must be dropped" },
    { original: "a", fix: "b", category: "made_up", why: "unknown category → other" }
  ]
};

/* ---------- tests ---------- */

const tests = [];
const test = (name, fn) => tests.push([name, fn]);

test("diffParts marks removed and inserted words", () => {
  const { ctx } = makeEnv({ fetchImpl: async () => json(200, {}) });
  const parts = JSON.parse(JSON.stringify(ctx.LamhaAI.diffParts("I go to market", "I went to the market"))); // out of the VM realm
  const del = parts.filter(p => p.tag === "del").map(p => p.t.trim()).filter(Boolean);
  const ins = parts.filter(p => p.tag === "ins").map(p => p.t.trim()).filter(Boolean);
  assert.deepEqual(del, ["go"]);
  assert.deepEqual(ins.join(" ").split(/\s+/), ["went", "the"]);
  const rebuilt = parts.filter(p => p.tag !== "del").map(p => p.t).join("");
  assert.equal(rebuilt, "I went to the market");
});

test("isArabicText detects Arabic-dominant drafts", () => {
  const { ctx } = makeEnv({ fetchImpl: async () => json(200, {}) });
  assert.equal(ctx.LamhaAI.isArabicText("أريد أن أطلب إجازة يوم الأحد"), true);
  assert.equal(ctx.LamhaAI.isArabicText("Please send the file today"), false);
});

test("ttsChunks keeps every word and stays under the voice limit", () => {
  const { ctx } = makeEnv({ fetchImpl: async () => json(200, {}) });
  const text = "4. Don't tell him \"I am not in the mood tonight, I am tired, my head is paining. Tomorrow morning we will make love\" then tomorrow morning you act like you forgot. Keep your promise. ".repeat(3).trim();
  const chunks = ctx.ttsChunks(text);
  assert.ok(chunks.length > 1);
  chunks.forEach(c => assert.ok(c.length <= 190, `chunk too long (${c.length})`));
  assert.equal(chunks.join(" "), text.replace(/\s+/g, " "));
});

test("Write new (compose): an email or a message from a description, no selected text", async () => {
  const r = recorder(() => claudeReply({ text: "Subject: Day off on Sunday\n\nHi Sam, …\n\n[Your name]" }));
  const env = makeEnv({ fetchImpl: r.fetchImpl, local: { aiKey: "sk-test" } });
  const idea = "بريد لمديري أطلب إجازة يوم الأحد";
  const email = await env.send({ type: "ai", tool: "compose", text: idea, extra: { kind: "email", tone: "formal" } });
  assert.equal(email.ok, true, email.error);
  assert.match(email.data.text, /^Subject:/);
  const prompt = r.calls[0].body.messages[0].content;
  assert.match(prompt, /"Subject: …"/);
  assert.match(prompt, /professional and polite/);
  assert.ok(prompt.includes("<text>\n" + idea + "\n</text>"), "the description is the material");
  const message = await env.send({ type: "ai", tool: "compose", text: idea, extra: { kind: "message" } });
  assert.equal(message.ok, true, message.error);
  assert.equal(r.calls.length, 2, "Email and Message aren't served from the same cache entry");
  assert.match(r.calls[1].body.messages[0].content, /no subject line and no sign-off/);
  assert.match(r.calls[1].body.messages[0].content, /a short message/, "a message is short unless asked otherwise");
  await env.send({ type: "ai", tool: "compose", text: idea, extra: { kind: "message", tone: "long" } }); // Longer
  const long = r.calls[2].body.messages[0].content;
  assert.match(long, /Tone: detailed and complete/);
  assert.doesNotMatch(long, /a short message/, "Longer isn't asked for a short message");
  await env.send({ type: "ai", tool: "compose", text: idea, extra: { kind: "message", tone: "constructor" } }); // not a tone
  assert.match(r.calls[3].body.messages[0].content, /Tone: natural and appropriate/);
});

test("Longer (expand): develops the text without inventing facts, like Shorter in reverse", async () => {
  const r = recorder(() => claudeReply({ text: "Hi Sam, could we move Sunday's meeting to [day]? Something came up on my side." }));
  const env = makeEnv({ fetchImpl: r.fetchImpl, local: { aiKey: "sk-test" } });
  const res = await env.send({ type: "ai", tool: "expand", text: "cant make sunday meeting", extra: {} });
  assert.equal(res.ok, true, res.error);
  const prompt = r.calls[0].body.messages[0].content;
  assert.match(prompt, /longer and more complete/);
  assert.match(prompt, /clearly longer than the original/);
  assert.match(prompt, /Never add reasons, events, names, dates/, "no invented facts");
  assert.match(prompt, /\[reason\]/, "a placeholder instead");
  assert.match(prompt, /Put only the rewritten text/);
});

test("Claude proofread: request shape, cleaned issues, journal recorded", async () => {
  const r = recorder(() => claudeReply(PROOF));
  const env = makeEnv({ fetchImpl: r.fetchImpl, local: { aiKey: "sk-test" } });
  const res = await env.send({ type: "ai", tool: "proofread", text: "I go to market yesterday.", extra: {} });
  assert.equal(res.ok, true, res.error);
  const { url, headers, body } = r.calls[0];
  assert.equal(url, "https://api.anthropic.com/v1/messages");
  assert.equal(headers["x-api-key"], "sk-test");
  assert.equal(body.model, "claude-opus-5");
  assert.equal(body.fallbacks, "default");
  assert.equal(headers["anthropic-beta"], "server-side-fallback-2026-07-01");
  assert.equal(body.output_config.effort, "medium");
  const item = body.output_config.format.schema.properties.issues.items;
  assert.ok(item.properties.category.enum.includes("prepositions"));
  assert.ok(item.required.includes("category"));
  assert.ok(!/often makes mistakes/.test(body.messages[0].content), "no weak points yet");

  const issues = res.data.issues;
  assert.equal(issues.length, 3, "the no-op change is dropped");
  assert.equal(issues[2].category, "other", "unknown category mapped to other");

  await env.flush();
  const j = env.browser.storage.local.data.mistakes;
  assert.equal(j.checks, 1);
  assert.deepEqual({ ...j.counts }, { verb_tense: 1, articles: 1, other: 1 });
  assert.deepEqual(j.recent.map(x => x.original), ["I go", "market", "a"], "in text order");
});

test("journal: cache hits and retries are not counted twice", async () => {
  const r = recorder(() => claudeReply(PROOF));
  const env = makeEnv({ fetchImpl: r.fetchImpl, local: { aiKey: "sk-test" } });
  const msg = { type: "ai", tool: "proofread", text: "I go to market yesterday.", extra: {} };
  await env.send(msg);
  await env.send(msg); // cache hit
  assert.equal(r.calls.length, 1, "second call served from cache");
  await env.send({ ...msg, extra: { fresh: true } }); // "try again"
  assert.equal(r.calls.length, 2);
  await env.flush();
  assert.equal(env.browser.storage.local.data.mistakes.checks, 1);
});

test("journal off: nothing recorded, no weak points in prompt", async () => {
  const r = recorder(() => claudeReply(PROOF));
  const env = makeEnv({
    fetchImpl: r.fetchImpl,
    local: { aiKey: "sk-test", mistakes: { checks: 5, counts: { articles: 9 }, recent: [] } },
    sync: { saveMistakes: false }
  });
  await env.send({ type: "ai", tool: "proofread", text: "Some text here.", extra: {} });
  await env.flush();
  assert.equal(env.browser.storage.local.data.mistakes.checks, 5);
  assert.ok(!/often makes mistakes/.test(r.calls[0].body.messages[0].content));
});

test("weak points (≥3, top 3, not 'other') are added to the proofread prompt only", async () => {
  const r = recorder((url, body) => claudeReply(/Proofread/.test(body.messages[0].content) ? PROOF : { text: "ok" }));
  const env = makeEnv({
    fetchImpl: r.fetchImpl,
    local: { aiKey: "sk-test", mistakes: { checks: 9, counts: { articles: 12, prepositions: 7, spelling: 2, other: 30, plurals: 4, word_order: 3 }, recent: [] } }
  });
  await env.send({ type: "ai", tool: "proofread", text: "Text one.", extra: {} });
  const p = r.calls[0].body.messages[0].content;
  assert.match(p, /often makes mistakes with: articles \(a\/an\/the\), prepositions, singular and plural nouns\./);
  assert.ok(!/spelling|other/.test(p.split("often makes mistakes with:")[1].split("\n")[0]));
  await env.send({ type: "ai", tool: "improve", text: "Text one.", extra: {} });
  assert.ok(!/often makes mistakes/.test(r.calls[1].body.messages[0].content));
});

test("Ollama: chat request shape and parsing", async () => {
  const r = recorder(() => ollamaReply({ text: "Could you send me the report today?" }));
  const env = makeEnv({ fetchImpl: r.fetchImpl, local: { aiProvider: "ollama", ollamaModel: "qwen3.5:latest" } });
  const res = await env.send({ type: "ai", tool: "formal", text: "send me report today", extra: {} });
  assert.equal(res.ok, true, res.error);
  assert.equal(res.data.text, "Could you send me the report today?");
  const { url, body } = r.calls[0];
  assert.equal(url, "http://localhost:11434/api/chat");
  assert.equal(body.model, "qwen3.5:latest");
  assert.equal(body.stream, false);
  assert.equal(body.think, false);
  assert.equal(body.format.required[0], "text");
  assert.equal(body.options.num_ctx, 4096);
  assert.equal(body.messages[0].role, "system");
});

test("Ollama: retries without `think` for models that reject it", async () => {
  const r = recorder((url, body) => (body.think === false
    ? json(400, { error: "\"gemma\" does not support thinking" })
    : ollamaReply({ text: "fine" })));
  const env = makeEnv({ fetchImpl: r.fetchImpl, local: { aiProvider: "ollama", ollamaModel: "gemma" } });
  const res = await env.send({ type: "ai", tool: "improve", text: "hello", extra: {} });
  assert.equal(res.ok, true, res.error);
  assert.equal(r.calls.length, 2);
  assert.ok(!("think" in r.calls[1].body));
});

test("Ollama errors map to Arabic-explained codes", async () => {
  const cases = [
    [async () => { throw new TypeError("NetworkError"); }, "ollama_offline"],
    [async () => json(403, {}), "ollama_origin"],
    [async () => json(404, { error: "model \"qwen9\" not found, try pulling it first" }), "ollama_model"]
  ];
  for (const [fetchImpl, code] of cases) {
    const env = makeEnv({ fetchImpl, local: { aiProvider: "ollama", ollamaModel: "qwen9" } });
    const res = await env.send({ type: "ai", tool: "improve", text: "hello", extra: {} });
    assert.equal(res.error, code);
    assert.ok(env.ctx.LamhaAI.ERRORS[code], `no Arabic message for ${code}`);
  }
  const noModel = makeEnv({ fetchImpl: async () => json(200, {}), local: { aiProvider: "ollama" } });
  assert.equal((await noModel.send({ type: "ai", tool: "improve", text: "hi", extra: {} })).error, "ollama_no_model");
});

test("Claude errors map to codes", async () => {
  const cases = [[401, {}, "ai_bad_key"], [400, { error: { message: "Your credit balance is too low" } }, "ai_no_credit"], [429, {}, "ai_rate_limited"]];
  for (const [status, body, code] of cases) {
    const env = makeEnv({ fetchImpl: async () => json(status, body), local: { aiKey: "k" } });
    assert.equal((await env.send({ type: "ai", tool: "improve", text: "hi", extra: {} })).error, code);
  }
});

/* ---------- Gemini ---------- */

const geminiReply = (obj, extra = {}) => json(200, {
  candidates: [{ finishReason: "STOP", content: { role: "model", parts: [{ text: "thinking…", thought: true }, { text: JSON.stringify(obj) }] }, ...extra }]
});

test("Gemini: request shape, schema converted, thought parts ignored", async () => {
  const r = recorder(() => geminiReply(PROOF));
  const env = makeEnv({ fetchImpl: r.fetchImpl, local: { aiProvider: "gemini", geminiKey: "AIza-test", geminiModel: "gemini-3.5-flash-lite" } });
  const res = await env.send({ type: "ai", tool: "proofread", text: "I go to market yesterday.", extra: {} });
  assert.equal(res.ok, true, res.error);
  assert.equal(res.data.issues.length, 3);
  const { url, headers, body } = r.calls[0];
  assert.equal(url, "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent");
  assert.equal(headers["x-goog-api-key"], "AIza-test");
  assert.match(body.systemInstruction.parts[0].text, /Arabic speakers/);
  assert.match(body.contents[0].parts[0].text, /<text>\nI go to market yesterday.\n<\/text>/);
  const s = body.generationConfig.responseSchema;
  assert.equal(body.generationConfig.responseMimeType, "application/json");
  assert.equal(s.type, "OBJECT");
  assert.equal(s.properties.issues.type, "ARRAY");
  assert.equal(s.properties.issues.items.properties.category.type, "STRING");
  assert.ok(s.properties.issues.items.properties.category.enum.includes("articles"));
  assert.ok(!JSON.stringify(s).includes("additionalProperties"), "Gemini rejects additionalProperties");
});

test("Gemini: default model, not-set-up, blocked and truncated answers", async () => {
  const r = recorder(() => geminiReply({ text: "ok" }));
  const env = makeEnv({ fetchImpl: r.fetchImpl, local: { aiProvider: "gemini", geminiKey: "k", geminiModel: "made-up" } });
  await env.send({ type: "ai", tool: "improve", text: "hi", extra: {} });
  assert.match(r.calls[0].url, /models\/gemini-3\.8-flash:generateContent/, "unknown model → default");

  const none = makeEnv({ fetchImpl: r.fetchImpl, local: { aiProvider: "gemini" } });
  assert.equal((await none.send({ type: "ai", tool: "improve", text: "hi", extra: {} })).error, "gemini_no_key");

  const cases = [
    [json(200, { promptFeedback: { blockReason: "SAFETY" } }), "ai_refused"],
    [geminiReply({ text: "x" }, { finishReason: "SAFETY" }), "ai_refused"],
    [geminiReply({ text: "x" }, { finishReason: "MAX_TOKENS" }), "ai_too_long"]
  ];
  for (const [reply, code] of cases) {
    const e = makeEnv({ fetchImpl: async () => reply, local: { aiProvider: "gemini", geminiKey: "k" } });
    assert.equal((await e.send({ type: "ai", tool: "improve", text: "hi", extra: {} })).error, code);
  }
});

test("Gemini errors map to Arabic-explained codes", async () => {
  const cases = [
    [400, { error: { code: 400, status: "INVALID_ARGUMENT", message: "API key not valid. Please pass a valid API key." } }, "gemini_bad_key"],
    [429, { error: { code: 429, status: "RESOURCE_EXHAUSTED", message: "Quota exceeded" } }, "gemini_quota"],
    [400, { error: { code: 400, status: "FAILED_PRECONDITION", message: "User location is not supported for the API use." } }, "gemini_region"],
    [404, { error: { code: 404, status: "NOT_FOUND", message: "models/x is not found" } }, "ai_model"]
  ];
  for (const [status, body, code] of cases) {
    const env = makeEnv({ fetchImpl: async () => json(status, body), local: { aiProvider: "gemini", geminiKey: "k" } });
    assert.equal((await env.send({ type: "ai", tool: "improve", text: "hi", extra: {} })).error, code);
    assert.ok(env.ctx.LamhaAI.ERRORS[code], `no Arabic message for ${code}`);
  }
});

test("Gemini key check: a working key passes even if thinking used the small test budget", async () => {
  const env = makeEnv({ fetchImpl: async () => geminiReply({ text: "x" }, { finishReason: "MAX_TOKENS" }) });
  assert.equal((await env.send({ type: "aiTest", provider: "gemini", key: "k", model: "gemini-3.8-flash" })).ok, true);
  const bad = makeEnv({ fetchImpl: async () => json(400, { error: { message: "API key not valid" } }) });
  assert.equal((await bad.send({ type: "aiTest", provider: "gemini", key: "k" })).error, "gemini_bad_key");
});

test("Gemini busy (503): retries, then the other free model answers", async () => {
  const r = recorder(url => (url.includes("gemini-3.8-flash:")
    ? json(503, { error: { code: 503, status: "UNAVAILABLE", message: "The model is overloaded. Please try again later." } })
    : geminiReply({ text: "fixed" })));
  const env = makeEnv({ fetchImpl: r.fetchImpl, local: { aiProvider: "gemini", geminiKey: "k" } });
  const res = await env.send({ type: "ai", tool: "improve", text: "hi", extra: {} });
  assert.equal(res.ok, true, res.error);
  assert.equal(res.data.text, "fixed");
  assert.deepEqual(r.calls.map(c => /models\/([^:]+)/.exec(c.url)[1]),
    ["gemini-3.8-flash", "gemini-3.8-flash", "gemini-3.8-flash", "gemini-3.5-flash-lite"], "3 tries, then Flash-Lite");
});

test("Gemini key check: both models busy → key saved with a note; errors carry Google's message", async () => {
  const busy = makeEnv({ fetchImpl: async () => json(503, { error: { message: "The model is overloaded." } }) });
  const r = await busy.send({ type: "aiTest", provider: "gemini", key: "k" });
  assert.equal(r.ok, true);
  assert.equal(r.note, "gemini_busy");
  const bad = makeEnv({ fetchImpl: async () => json(400, { error: { message: "API key not valid. Please pass a valid API key." } }) });
  const b = await bad.send({ type: "aiTest", provider: "gemini", key: "k" });
  assert.equal(b.error, "gemini_bad_key");
  assert.match(b.detail, /API key not valid/);
});

test("provider helper: which one is set up", () => {
  const { ctx } = makeEnv({ fetchImpl: async () => json(200, {}) });
  const p = local => JSON.parse(JSON.stringify(ctx.LamhaAI.provider(local)));
  assert.deepEqual(p({}), { id: "claude", name: "Claude", ready: false, notReady: "ai_no_key" });
  assert.equal(p({ aiProvider: "gemini", geminiKey: "k" }).ready, true);
  assert.equal(p({ aiProvider: "gemini", aiKey: "k" }).ready, false, "a Claude key doesn't make Gemini ready");
  assert.equal(p({ aiProvider: "ollama", ollamaModel: "q" }).name, "Ollama");
  assert.equal(p({ aiKeySet: true }).ready, true, "the flag alone is enough");
  assert.equal(p({ aiProvider: "gemini", geminiKeySet: true }).ready, true);
  assert.ok(!ctx.LamhaAI.PROVIDER_KEYS.some(k => /Key$/.test(k)), "pages never read the keys");
});

test("background keeps the key-saved flags in step with the keys", async () => {
  const env = makeEnv({ fetchImpl: async () => json(200, {}), local: { aiKey: "sk-ant-x", geminiKeySet: true } });
  await new Promise(r => setTimeout(r, 10));
  assert.equal(env.browser.storage.local.data.aiKeySet, true);
  assert.equal(env.browser.storage.local.data.geminiKeySet, false, "flag without a key is cleared");
});

/* ---------- flashcards ---------- */

const DAYMS = 864e5;
const newCard = { q: "bank", tr: "ضفة", due: 0, interval: 0, ease: 2.5, reps: 0, lapses: 0, added: 1 };

test("schedule: again 10 min, hard < good, intervals grow with each success", () => {
  const { ctx } = makeEnv({ fetchImpl: async () => json(200, {}) });
  const now = 1e12;
  const again = ctx.schedule(newCard, "again", now);
  assert.equal(again.due - now, 10 * 60e3);
  assert.equal(again.lapses, 1);
  assert.equal(again.ease, 2.3);
  assert.equal(ctx.schedule(newCard, "hard", now).interval, 1);
  let c = ctx.schedule(newCard, "good", now);
  assert.equal(c.interval, 2);
  c = ctx.schedule(c, "good", now);
  assert.equal(c.interval, 5);
  c = ctx.schedule(c, "good", now);
  assert.equal(c.interval, 13);
  assert.equal(c.due - now, 13 * DAYMS);
  // hard is never later than good, even with the lowest ease
  for (const card of [newCard, { ...newCard, reps: 1, interval: 2 }, { ...newCard, reps: 4, interval: 5, ease: 1.3 }, { ...newCard, reps: 6, interval: 40 }]) {
    assert.ok(ctx.schedule(card, "hard", now).due <= ctx.schedule(card, "good", now).due, JSON.stringify(card));
  }
  assert.equal(newCard.reps, 0, "schedule does not modify its input");
});

test("review queue: history imported once, due first, new words limited per day", async () => {
  const history = [
    { q: "serendipity", tr: "صدفة سعيدة", src: "en", t: 3 },
    { q: "مرحبا", tr: "hello", src: "ar", t: 2 },
    { q: "bank", tr: "مصرف", src: "en", t: 1 }
  ];
  const past = Date.now() - 1000;
  const cards = {};
  for (let i = 0; i < 15; i++) cards["new" + i] = { ...newCard, q: "new" + i, added: i };
  cards.old = { ...newCard, q: "old", reps: 2, interval: 5, last: past - 5 * DAYMS, due: past };
  cards.later = { ...newCard, q: "later", reps: 1, interval: 2, last: past, due: Date.now() + DAYMS };
  const env = makeEnv({ fetchImpl: async () => json(200, {}), local: { history, cards }, sync: { cardsNewPerDay: 5 } });

  const r = JSON.parse(JSON.stringify(await env.send({ type: "reviewQueue" })));
  assert.equal(r.counts.total, 19, "15 new + old + later + 2 English history words");
  assert.ok(!Object.keys(env.browser.storage.local.data.cards).includes("مرحبا"), "Arabic lookups are not cards");
  assert.equal(r.queue[0].key, "old", "due review comes first");
  assert.equal(r.counts.due, 1);
  assert.equal(r.counts.fresh, 5, "daily new-word limit");
  assert.equal(r.queue.length, 6);
  assert.ok(r.queue.slice(1).every(c => c.isNew));
  assert.ok(r.nextDue > Date.now());
  assert.equal(r.queue[0].next.again, 10 * 60e3);

  // import happens only once: a removed history word doesn't come back
  await env.send({ type: "cardRemove", key: "bank" });
  const r2 = await env.send({ type: "reviewQueue" });
  assert.equal(r2.counts.total, 18);
});

test("grading: new words count toward today's limit; due card moves to the future", async () => {
  const cards = {};
  for (let i = 0; i < 3; i++) cards["w" + i] = { ...newCard, q: "w" + i, added: i };
  const env = makeEnv({ fetchImpl: async () => json(200, {}), local: { cards, cardsImported: true }, sync: { cardsNewPerDay: 2 } });
  let r = await env.send({ type: "reviewQueue" });
  assert.equal(r.counts.fresh, 2);
  await env.send({ type: "reviewGrade", key: r.queue[0].key, grade: "good" });
  r = await env.send({ type: "reviewQueue" });
  assert.equal(r.counts.fresh, 1, "one new word left today");
  await env.send({ type: "reviewGrade", key: r.queue[0].key, grade: "again" });
  r = await env.send({ type: "reviewQueue" });
  assert.equal(r.counts.fresh, 0, "limit reached");
  assert.equal(r.counts.due, 0, "'again' comes back in 10 minutes, not now");
  const stored = env.browser.storage.local.data;
  assert.equal(stored.cardStats.newSeen, 2);
  assert.equal(Object.values(stored.cards).filter(c => c.last).length, 2);
});

test("bookmark toggles a card; hasCard sees it", async () => {
  const env = makeEnv({ fetchImpl: async () => json(200, {}), local: { cardsImported: true } });
  const card = { q: "Resilient", tr: "مرن", ex: "She is resilient.", form: "resilient", def: "able to recover" };
  assert.equal(await env.send({ type: "cardHas", q: "resilient" }), false);
  assert.equal(await env.send({ type: "cardToggle", card }), true);
  assert.equal(await env.send({ type: "cardHas", q: "RESILIENT" }), true);
  assert.equal(env.browser.storage.local.data.cards.resilient.ex, "She is resilient.");
  assert.equal(await env.send({ type: "cardToggle", card }), false);
  assert.equal(await env.send({ type: "cardHas", q: "resilient" }), false);
});

test("words named like Object's properties (constructor, __proto__) are ordinary cards", async () => {
  const env = makeEnv({ fetchImpl: async () => json(200, {}), local: { cardsImported: true } });
  for (const q of ["constructor", "__proto__", "toString"]) {
    assert.equal(await env.send({ type: "cardHas", q }), false, q);
    assert.equal(await env.send({ type: "cardToggle", card: { q, tr: "x", ex: "a " + q } }), true, q);
    assert.equal(await env.send({ type: "cardHas", q }), true, q);
  }
  assert.deepEqual(Object.keys(env.browser.storage.local.data.cards).sort(), ["__proto__", "constructor", "tostring"]);
  assert.equal(vm.runInContext("({}).ex", env.ctx), undefined); // Object.prototype untouched
  assert.equal(await env.send({ type: "cardToggle", card: { q: "constructor", tr: "x" } }), false);
  assert.equal(await env.send({ type: "cardHas", q: "constructor" }), false);
  assert.equal(env.ctx.LamhaAI.isCategory("constructor"), false);
  assert.equal(env.ctx.LamhaAI.isCategory("articles"), true);
});

test("review streak: days in a row count up once a day, a missed day starts again", async () => {
  const cards = {};
  for (const q of ["a", "b"]) cards[q] = { ...newCard, q, tr: "x" };
  const yesterday = new Date(Date.now() - DAYMS).toDateString();
  const env = makeEnv({ fetchImpl: async () => json(200, {}), local: { cards, cardsImported: true, cardStats: { reviewDay: yesterday, streak: 4 } } });
  assert.equal((await env.send({ type: "reviewQueue" })).streak, 0, "nothing reviewed today yet");
  await env.send({ type: "reviewGrade", key: "a", grade: "good" });
  assert.equal((await env.send({ type: "reviewQueue" })).streak, 5);
  await env.send({ type: "reviewGrade", key: "b", grade: "good" });
  assert.equal((await env.send({ type: "reviewQueue" })).streak, 5, "once per day");
  assert.equal(env.browser.storage.local.data.cardStats.newSeen, 2, "the daily new-word count is kept alongside");

  const gap = makeEnv({ fetchImpl: async () => json(200, {}), local: { cards: { a: { ...newCard, q: "a" } }, cardsImported: true, cardStats: { reviewDay: new Date(Date.now() - 3 * DAYMS).toDateString(), streak: 9 } } });
  await gap.send({ type: "reviewGrade", key: "a", grade: "again" });
  assert.equal((await gap.send({ type: "reviewQueue" })).streak, 1);
});

test("lookup milestones: the 10th word looked up says so (once), the cached result stays clean", async () => {
  const google = recorder(url => (url.includes("/translate_a/t") // the word in its sentence
    ? json(200, [["الأطفال <a i=0>مرنون</a> جدًا.", "en"]])
    : json(200, { src: "en", sentences: [{ trans: "مرن", orig: "resilient" }] })));
  const env = makeEnv({ fetchImpl: google.fetchImpl, local: { cardsImported: true, lookupCount: 9 }, sync: { dictSource: "online", translateDefinitions: false } });
  const first = await env.send({ type: "lookup", text: "resilient" });
  assert.equal(first.data.milestone, 10);
  const again = await env.send({ type: "lookup", text: "resilient" });
  assert.equal(again.data.milestone, undefined, "the cached result has no milestone");
  assert.equal(env.browser.storage.local.data.lookupCount, 10, "cached lookups aren't counted again");
  await env.send({ type: "lookup", text: "Resilient", context: { before: "Kids are very ", after: " after all." } });
  assert.equal(env.browser.storage.local.data.lookupCount, 10, "the same word in another sentence isn't a new word");
  const noHistory = makeEnv({ fetchImpl: google.fetchImpl, local: { lookupCount: 9 }, sync: { dictSource: "online", saveHistory: false } });
  assert.equal((await noHistory.send({ type: "lookup", text: "resilient" })).data.milestone, undefined, "history off: not counted");
  assert.equal(noHistory.browser.storage.local.data.lookupCount, 9);
});

test("today's goal: new words and review answers count; the one that reaches the goal says so, once", async () => {
  const google = recorder((url, body) => json(200, { src: "en", sentences: [{ trans: "كلمة", orig: "x" }] }));
  const cards = { a: { ...newCard, q: "a", tr: "أ" } };
  const env = makeEnv({ fetchImpl: google.fetchImpl, local: { cards, cardsImported: true }, sync: { dictSource: "online", translateDefinitions: false, dailyGoal: 3, cardsAuto: false } });
  assert.equal((await env.send({ type: "lookup", text: "resilient" })).data.goal, undefined);
  await env.send({ type: "lookup", text: "resilient" }); // the same word again: not counted
  assert.equal((await env.send({ type: "reviewGrade", key: "a", grade: "good" })).goal, 0, "a review answer counts too");
  const third = await env.send({ type: "lookup", text: "tenacious" });
  assert.equal(third.data.goal, 3, "the answer that reached the goal");
  assert.equal((await env.send({ type: "lookup", text: "candid" })).data.goal, undefined, "past the goal: no second celebration");
  const t = await env.send({ type: "today" });
  assert.deepEqual([t.done, t.goal, t.streak], [4, 3, 1]);
});

test("today's streak: days in a row with practice; not practised yet today keeps yesterday's", async () => {
  const day = back => new Date(Date.now() - back * DAYMS).toDateString();
  const activity = { [day(1)]: 4, [day(2)]: 1, [day(4)]: 7 };
  const env = makeEnv({ fetchImpl: async () => json(200, {}), local: { activity, cards: { a: { ...newCard, q: "a" } }, cardsImported: true } });
  assert.equal((await env.send({ type: "today" })).streak, 2, "yesterday and the day before; day 4 is past a gap");
  await env.send({ type: "reviewGrade", key: "a", grade: "good" });
  const t = await env.send({ type: "today" });
  assert.deepEqual([t.done, t.streak], [1, 3]);
  assert.ok(Object.keys(env.browser.storage.local.data.activity).length <= 60);
});

test("word of the day: a word of yours that's due soon, else a new one from the dictionary; the same all day", async () => {
  const soon = { ...newCard, q: "bank", tr: "ضفة", last: Date.now() - 5 * DAYMS, due: Date.now() + DAYMS, reps: 2, interval: 6 };
  const later = { ...newCard, q: "river", tr: "نهر", last: Date.now(), due: Date.now() + 30 * DAYMS, reps: 4, interval: 30 };
  const env = makeEnv({ fetchImpl: async () => json(200, {}), local: { cards: { bank: soon, river: later }, cardsImported: true } });
  const w = (await env.send({ type: "today" })).word;
  assert.deepEqual([w.q, w.tr, w.from], ["bank", "ضفة", "deck"]);

  const history = [{ q: "menace", tr: "تهديد", src: "en" }];
  const fresh = makeEnv({ fetchImpl: async () => json(200, {}), local: { cards: { river: later }, cardsImported: true, history }, realDict: true });
  const d = (await fresh.send({ type: "today" })).word;
  assert.equal(d.from, "dict");
  assert.match(d.q, /^[a-z]{5,12}$/);
  assert.ok(d.tr && d.def, "its meaning and definition");
  assert.ok(!d.ex || d.ex.toLowerCase().includes(d.q.slice(0, 4)), "an example only when it uses the word");
  assert.ok(!["river", "menace"].includes(d.q), "not a word the user already has");
  assert.equal((await fresh.send({ type: "today" })).word.q, d.q, "the same word all day");
});

test("a word lookup adds a card with its sentence and in-context meaning", async () => {
  const google = recorder(url => {
    if (url.includes("/translate_a/single")) {
      return json(200, {
        src: "en", sentences: [{ trans: "مصرف", orig: "bank" }],
        definitions: [{ pos: "noun", entry: [{ gloss: "the land alongside a river", definition_id: "d1" }] }]
      });
    }
    if (url.includes("/translate_a/t")) return json(200, ["جلسنا على <a i=0>ضفة</a> النهر."]); // context translation
    return json(404, {});
  });
  const env = makeEnv({
    fetchImpl: google.fetchImpl,
    local: { cardsImported: true },
    sync: { dictSource: "online", translateDefinitions: false }
  });
  const res = await env.send({ type: "lookup", text: "bank", context: { before: "We sat on the ", after: " of the river." } });
  assert.equal(res.ok, true, res.error);
  assert.equal(await env.send({ type: "cardHas", q: "bank" }), true, "cardHas waits for the automatic add");
  const c = env.browser.storage.local.data.cards.bank;
  assert.equal(c.tr, "ضفة", "the meaning in this sentence, not the first dictionary meaning");
  assert.equal(c.ex, "We sat on the bank of the river.");
  assert.equal(c.def, "the land alongside a river");

  const off = makeEnv({ fetchImpl: google.fetchImpl, local: { cardsImported: true }, sync: { dictSource: "online", cardsAuto: false } });
  await off.send({ type: "lookup", text: "bank" });
  assert.equal(await off.send({ type: "cardHas", q: "bank" }), false, "cardsAuto off");

  // a name the sentence's translation keeps as it is: flagged, not the meaning, not a review card
  const names = recorder(url => {
    if (url.includes("/translate_a/single")) return json(200, { src: "en", sentences: [{ trans: "الجوزاء", orig: "gemini" }] });
    if (url.includes("/translate_a/t")) return json(200, ["استخدم <a i=0>Gemini</a> أو Claude."]);
    return json(404, {});
  });
  const nameEnv = makeEnv({ fetchImpl: names.fetchImpl, local: { cardsImported: true }, sync: { dictSource: "online", translateDefinitions: false } });
  const named = await nameEnv.send({ type: "lookup", text: "gemini", context: { before: "Use ", after: " or Claude." } });
  assert.equal(named.data.context.untranslated, true);
  assert.equal(named.data.translation, "الجوزاء", "the dictionary meaning is kept");
  assert.equal(await nameEnv.send({ type: "cardHas", q: "gemini" }), false, "a name isn't added to review");

  const noHistory = makeEnv({ fetchImpl: google.fetchImpl, local: { cardsImported: true }, sync: { dictSource: "online", saveHistory: false } });
  await noHistory.send({ type: "lookup", text: "bank" });
  assert.equal(await noHistory.send({ type: "cardHas", q: "bank" }), false, "history off: no automatic card");
  assert.equal(noHistory.browser.storage.local.data.history, undefined, "history off: no history");
});

test("English interface: explanations, the explain tool and the default summary are in English", async () => {
  const r = recorder((url, body) => claudeReply(/Proofread/.test(body.messages[0].content) ? PROOF : { text: "ok" }));
  const env = makeEnv({ fetchImpl: r.fetchImpl, local: { aiKey: "sk-test" }, sync: { uiLang: "en" } });
  await env.send({ type: "ai", tool: "proofread", text: "I go to market yesterday.", extra: {} });
  await env.send({ type: "ai", tool: "explain", text: "It's raining cats and dogs.", extra: {} });
  await env.send({ type: "ai", tool: "summarize", text: "A long report about the quarter.", extra: {} });
  await env.send({ type: "ai", tool: "summarize", text: "A long report about the quarter.", extra: { lang: "ar" } });
  const prompts = r.calls.map(c => c.body.messages[0].content);
  assert.match(prompts[0], /one-sentence explanation in English/);
  assert.match(prompts[1], /in simple English/);
  assert.match(prompts[2], /Summarize the key points of the text in English/);
  assert.match(prompts[3], /in Arabic/, "an explicit summary language still wins");
  assert.equal(env.ctx.LamhaAI.errorInfo("ai_no_key")[0], "Turn on the writing tools");
  assert.equal(env.ctx.LamhaAI.catLabel("articles"), "Articles (a / an / the)");
});

/** Google, faked: `single` translates its whole q (split into sentence pieces), `t` translates each q. */
function fakeGoogle() {
  return recorder((url, body) => {
    const params = new URLSearchParams(typeof body === "string" ? body : "");
    const q = new URL(url).searchParams.get("q") ?? params.get("q");
    if (url.includes("/translate_a/single")) {
      const sentences = q.split(/(?<=\n)(?!\n)/).map(s => ({ trans: "AR<" + s.replace(/\n+$/, "") + ">" + (s.match(/\n+$/) || [""])[0], orig: s }));
      return json(200, { src: "en", sentences });
    }
    if (url.includes("/translate_a/t")) return json(200, params.getAll("q").map(x => ["AR<" + x + ">", "en"]));
    return json(404, {});
  });
}

test("translations keep paragraph breaks; spaces inside lines are tidied", async () => {
  const g = fakeGoogle();
  const env = makeEnv({ fetchImpl: g.fetchImpl });
  const res = await env.send({ type: "lookup", text: "Hello   there.\r\n\n\n\nGood morning.  " });
  assert.equal(res.ok, true, res.error);
  assert.equal(res.data.query, "Hello there.\n\nGood morning.");
  assert.equal(res.data.translation, "AR<Hello there.>\n\nAR<Good morning.>");
});

test("long text is translated in pieces and joined back whole; too long is refused, not cut", async () => {
  const g = fakeGoogle();
  const env = makeEnv({ fetchImpl: g.fetchImpl });
  const paras = Array.from({ length: 12 }, (_, i) => `Paragraph ${i}. ` + "Some words here. ".repeat(50).trim());
  const text = paras.join("\n\n");
  assert.ok(text.length > 9000);
  const res = await env.send({ type: "lookup", text });
  assert.equal(res.ok, true, res.error);
  assert.ok(g.calls.length >= 2, "more than one request");
  const back = res.data.translation.split("\n\n");
  assert.equal(back.length, 12, "every paragraph break survives");
  back.forEach((p, i) => assert.match(p, new RegExp(`Paragraph ${i}\\. `)));
  const long = await env.send({ type: "lookup", text: "word ".repeat(7000) });
  assert.deepEqual({ ok: long.ok, error: long.error }, { ok: false, error: "too_long" });
});

test("splitLong cuts at line breaks, then sentence ends, then spaces, and loses nothing", () => {
  const { ctx } = makeEnv({ fetchImpl: async () => json(200, {}) });
  const join = ps => ps.map(p => p.t + p.sep).join("");
  const sentences = "One sentence here. ".repeat(40).trim();
  const bySentence = ctx.splitLong(sentences, 100);
  assert.equal(join(bySentence), sentences);
  bySentence.forEach(p => { assert.ok(p.t.length <= 100); assert.ok(p.t.endsWith("."), p.t); });
  const noPunct = "word ".repeat(100).trim();
  assert.equal(join(ctx.splitLong(noPunct, 64)), noPunct);
});

/* ---- translation service: Google, the AI, or both ---- */

/** A fake network: Google either works (fakeGoogle's replies) or is rate-limited; Gemini and Ollama translate with
 *  `aiReply(texts, n)` → the list of translations (n counts AI requests). Records which service answered what. */
function trNet({ googleUp = true, aiReply = texts => texts.map(t => "AI<" + t + ">") } = {}) {
  const g = fakeGoogle();
  let aiN = 0;
  const textsOf = content => JSON.parse(/<texts>\n([\s\S]*)\n<\/texts>/.exec(content)[1]);
  return recorder((url, body, n) => {
    if (url.includes("translate_a")) return googleUp ? g.fetchImpl(url, { method: "POST", body: typeof body === "string" ? body : undefined }) : json(429, {});
    if (url.includes("generativelanguage")) {
      const translations = aiReply(textsOf(body.contents[0].parts[0].text), ++aiN);
      if (!translations) return json(429, { error: { message: "quota" } });
      return json(200, { candidates: [{ finishReason: "STOP", content: { parts: [{ text: JSON.stringify({ translations }) }] } }] });
    }
    if (url.endsWith("/api/chat")) return json(200, { done_reason: "stop", message: { content: JSON.stringify({ translations: aiReply(textsOf(body.messages[1].content), ++aiN) }) } });
    return json(404, {});
  });
}
const who = calls => calls.map(c => (c.url.includes("translate_a") ? "google" : c.url.includes("generativelanguage") ? "gemini" : c.url.includes("/api/chat") ? "ollama" : "other"));
const SENTENCE = "It's a piece of cake, honestly.";

test("translation, Automatic: Google first; the AI (Flash-Lite, a translator's prompt) when Google can't", async () => {
  const up = trNet();
  const env = makeEnv({ fetchImpl: up.fetchImpl, local: { geminiKey: "AIza-test", aiProvider: "gemini" } });
  const a = await env.send({ type: "lookup", text: SENTENCE });
  assert.deepEqual(who(up.calls), ["google"]);
  assert.equal(a.data.source, "online");

  const down = trNet({ googleUp: false });
  const env2 = makeEnv({ fetchImpl: down.fetchImpl, local: { geminiKey: "AIza-test", aiProvider: "gemini" } });
  const b = await env2.send({ type: "lookup", text: SENTENCE });
  assert.equal(b.ok, true, b.error);
  assert.equal(b.data.translation, "AI<" + SENTENCE + ">");
  assert.deepEqual({ source: b.data.source, ai: b.data.ai }, { source: "ai", ai: "Gemini" });
  const ai = down.calls.find(c => c.url.includes("generativelanguage"));
  assert.match(ai.url, /gemini-3\.5-flash-lite/, "translation defaults to the quick model");
  assert.match(ai.body.systemInstruction.parts[0].text, /professional translator/);
  assert.match(ai.body.contents[0].parts[0].text, /Idioms and slang become their natural equivalent/);
});

test("translation, Google only: no AI even when Google fails; AI: the AI first, Google when it fails", async () => {
  const down = trNet({ googleUp: false });
  const g = makeEnv({ fetchImpl: down.fetchImpl, local: { geminiKey: "AIza-test", aiProvider: "gemini", trService: "google" } });
  assert.equal((await g.send({ type: "lookup", text: SENTENCE })).ok, false);
  assert.ok(!who(down.calls).includes("gemini"));

  const quota = trNet({ aiReply: () => null }); // Gemini's free quota is used up
  const a = makeEnv({ fetchImpl: quota.fetchImpl, local: { geminiKey: "AIza-test", aiProvider: "gemini", trService: "ai" } });
  const res = await a.send({ type: "lookup", text: SENTENCE });
  assert.equal(res.ok, true, res.error);
  assert.equal(res.data.source, "online", "Google stepped in");
  assert.deepEqual([...new Set(who(quota.calls))], ["gemini", "google"]);
});

test("translation: words keep Google's dictionary first, even with the AI chosen", async () => {
  const net = trNet();
  const env = makeEnv({ fetchImpl: net.fetchImpl, local: { geminiKey: "AIza-test", aiProvider: "gemini", trService: "ai" }, sync: { uiLang: "ar", dictSource: "online", useContext: false, translateDefinitions: false } });
  await env.send({ type: "lookup", text: "resilient" });
  assert.deepEqual(who(net.calls), ["google"]);
});

test("translation: stray letters from another script are asked again, then Google, and shown only if nothing else works", async () => {
  const korean = t => t.replace(/cake/, "캐시");
  // once wrong, then right: the second answer is used
  const retry = trNet({ aiReply: (texts, n) => texts.map(t => "AI<" + (n === 1 ? korean(t) : t) + ">") });
  const e1 = makeEnv({ fetchImpl: retry.fetchImpl, local: { geminiKey: "AIza-test", aiProvider: "gemini", trService: "ai" } });
  assert.equal((await e1.send({ type: "lookup", text: SENTENCE })).data.translation, "AI<" + SENTENCE + ">");
  // always wrong, Google up: Google's answer
  const bad = trNet({ aiReply: texts => texts.map(t => korean(t)) });
  const e2 = makeEnv({ fetchImpl: bad.fetchImpl, local: { geminiKey: "AIza-test", aiProvider: "gemini", trService: "ai" } });
  const r2 = await e2.send({ type: "lookup", text: SENTENCE });
  assert.equal(r2.data.source, "online");
  // always wrong and Google down: the rough answer beats nothing, and isn't cached
  const alone = trNet({ googleUp: false, aiReply: texts => texts.map(t => korean(t)) });
  const e3 = makeEnv({ fetchImpl: alone.fetchImpl, local: { geminiKey: "AIza-test", aiProvider: "gemini" } });
  const r3 = await e3.send({ type: "lookup", text: SENTENCE });
  assert.match(r3.data.translation, /캐시/);
  const before = alone.calls.length;
  await e3.send({ type: "lookup", text: SENTENCE });
  assert.ok(alone.calls.length > before, "asked again next time");
  // letters that were in the source may stay
  assert.equal(e3.ctx.strayLetters("Σωκράτης said", "قال Σωκράτης", "ar"), false);
  assert.equal(e3.ctx.strayLetters("I saw her duck", "رأיתها تنحني", "ar"), true);
});

test("translation: pages use the AI only when allowed; the card's 'Better translation' asks the AI whatever the setting", async () => {
  const net = trNet();
  const off = makeEnv({ fetchImpl: net.fetchImpl, local: { geminiKey: "AIza-test", aiProvider: "gemini", trService: "ai" } });
  await off.send({ type: "translateBatch", texts: ["Hello <a i=0>world</a>"], tl: "ar", format: "html", kind: "page" });
  assert.deepEqual(who(net.calls), ["google"], "trPages off: Google");
  const on = makeEnv({ fetchImpl: net.fetchImpl, local: { geminiKey: "AIza-test", aiProvider: "gemini", trService: "ai", trPages: true } });
  const page = await on.send({ type: "translateBatch", texts: ["Hello <a i=0>world</a>"], tl: "ar", format: "html", kind: "page" });
  assert.equal(page.data[0], "AI<Hello <a i=0>world</a>>");
  assert.match(net.calls[net.calls.length - 1].body.contents[0].parts[0].text, /keep every tag/);

  const g = trNet();
  const googleOnly = makeEnv({ fetchImpl: g.fetchImpl, local: { geminiKey: "AIza-test", aiProvider: "gemini", trService: "google" } });
  const better = await googleOnly.send({ type: "lookup", text: SENTENCE, engine: "ai" });
  assert.equal(better.data.source, "ai");
  assert.deepEqual(who(g.calls), ["gemini"]);
});

test("translation offline: 'Local only' translates sentences with a local AI (Ollama), never with an online one", async () => {
  const net = trNet();
  const local = makeEnv({ fetchImpl: net.fetchImpl, local: { aiProvider: "ollama", ollamaModel: "qwen3.5:4b" }, sync: { uiLang: "ar", dictSource: "offline" } });
  const res = await local.send({ type: "lookup", text: SENTENCE });
  assert.equal(res.ok, true, res.error);
  assert.deepEqual({ source: res.data.source, ai: res.data.ai }, { source: "ai", ai: "Ollama" });
  assert.deepEqual(who(net.calls), ["ollama"]);

  const online = makeEnv({ fetchImpl: net.fetchImpl, local: { geminiKey: "AIza-test", aiProvider: "gemini" }, sync: { uiLang: "ar", dictSource: "offline" } });
  assert.equal((await online.send({ type: "lookup", text: SENTENCE })).error, "offline_mode");
});

test("translation: its own provider and model, separate from the writing tools", async () => {
  const net = trNet({ googleUp: false });
  const env = makeEnv({ fetchImpl: net.fetchImpl, local: {
    aiProvider: "ollama", ollamaModel: "qwen3.5:4b", geminiKey: "AIza-test", // writing on Ollama…
    trProvider: "gemini", trModels: { gemini: "gemini-3.8-flash" } // …translation on Gemini Flash
  } });
  await env.send({ type: "lookup", text: SENTENCE });
  assert.match(net.calls.find(c => c.url.includes("generativelanguage")).url, /gemini-3\.8-flash/);
  const claude = makeEnv({ fetchImpl: async () => json(429, {}), local: { aiKey: "sk-test", trProvider: "claude" } });
  assert.equal((await claude.ctx.trSettings()).ai.model, "claude-haiku-4-5", "Claude translates with Haiku unless told otherwise");
});

/* ---- English–English dictionary ---- */

test("English–English: offline definitions, the one that fits the sentence first, the Arabic kept for review", async () => {
  const net = recorder(() => json(429, {}));
  const env = makeEnv({ fetchImpl: net.fetchImpl, realDict: true, local: { cardsImported: true }, sync: { uiLang: "ar", enDict: true, dictSource: "offline" } });
  const plain = await env.send({ type: "lookup", text: "resilient" });
  assert.equal(plain.ok, true, plain.error);
  const d = plain.data;
  assert.deepEqual({ mode: d.mode, tl: d.tl, source: d.source, ar: d.ar, ipa: d.srcTranslit, dict: d.dict.length }, { mode: "explain", tl: "en", source: "local", ar: "مَرِن", ipa: "rɪˈzɪljənt", dict: 0 });
  assert.match(d.translation, /^recovering readily from adversity/);
  assert.equal(d.contextSense, false);
  assert.ok(d.definitions.every(p => p.entries.every(e => !e.ar)), "no Arabic in the English view");
  const inSentence = await env.send({ type: "lookup", text: "resilient", context: { before: "Her hair was soft, bouncy and ", after: "." } });
  assert.deepEqual({ t: inSentence.data.translation, fits: inSentence.data.contextSense, ex: inSentence.data.heroExample },
    { t: "elastic; rebounds readily", fits: true, ex: "clean bouncy hair" }, "the sense that fits the sentence leads");
  assert.equal(net.calls.length, 0, "no internet needed");
  const card = env.browser.storage.local.data.cards.resilient;
  assert.deepEqual({ en: card.en, tr: card.tr }, { en: true, tr: "مَرِن" });
  assert.match(card.def, /^recovering readily/, "review: the English definition, with the Arabic under it");
  assert.equal(env.browser.storage.local.data.history[0].tr, "مَرِن", "recent lookups list the short meaning");
  assert.equal(inSentence.data.ar, "مَرِن", "a sense without its own Arabic word keeps the word's usual meaning");
});

test("English–English: automatic when English is the translation language; the card's switch is remembered", async () => {
  const net = recorder(() => json(429, {}));
  const toEnglish = makeEnv({ fetchImpl: net.fetchImpl, realDict: true, sync: { uiLang: "en", targetLang: "en" } });
  assert.equal((await toEnglish.send({ type: "lookup", text: "resilient" })).data.mode, "explain", "no more 'resilient → resilient'");
  const env = makeEnv({ fetchImpl: fakeGoogle().fetchImpl, realDict: true, sync: { uiLang: "ar", useContext: false } });
  assert.equal((await env.send({ type: "lookup", text: "resilient" })).data.translation, "مَرِن", "Arabic by default");
  await env.send({ type: "setWordDict", en: true });
  assert.equal(env.browser.storage.sync.data.enDict, true);
  assert.equal((await env.send({ type: "lookup", text: "resilient" })).data.mode, "explain");
  await env.send({ type: "setWordDict", en: false });
  assert.equal((await env.send({ type: "lookup", text: "resilient" })).data.translation, "مَرِن");
  assert.equal((await env.send({ type: "lookup", text: "Can we talk tomorrow?" })).data.mode, undefined, "sentences still translate");
});

test("English–English online: Google's English definitions, the meaning kept; unknown words offline say so", async () => {
  const google = recorder(() => json(200, { src: "en", sentences: [{ trans: "مرن", orig: "resilient" }],
    definitions: [{ pos: "adjective", entry: [{ gloss: "able to recover quickly from difficult conditions.", example: "a <b>resilient</b> economy" }] }] }));
  const env = makeEnv({ fetchImpl: google.fetchImpl, realDict: true, sync: { uiLang: "ar", enDict: true, dictSource: "online" } });
  const d = (await env.send({ type: "lookup", text: "resilient" })).data;
  assert.deepEqual({ mode: d.mode, t: d.translation, ex: d.heroExample, ar: d.ar, source: d.source },
    { mode: "explain", t: "able to recover quickly from difficult conditions.", ex: "a resilient economy", ar: "مرن", source: "online" });
  const offline = makeEnv({ fetchImpl: google.fetchImpl, realDict: true, sync: { uiLang: "ar", enDict: true, dictSource: "offline" } });
  assert.equal((await offline.send({ type: "lookup", text: "qwxzyv" })).error, "not_found_offline");
});

test("the sense follows the grammar: 'I mentioned' is the verb in both views, 'a mention' the noun (offline)", async () => {
  const net = recorder(() => json(429, {}));
  const ctx = { before: "As promised here is the summary of everything I ", after: ", your 3 options for the validation." };
  const en = makeEnv({ fetchImpl: net.fetchImpl, realDict: true, sync: { uiLang: "ar", enDict: true, dictSource: "offline" } });
  const verb = (await en.send({ type: "lookup", text: "mentioned", context: ctx })).data;
  assert.deepEqual({ t: verb.translation, pos: verb.heroPos, fits: verb.contextSense }, { t: "make reference to", pos: "فعل", fits: true });
  assert.equal(verb.definitions[0].pos, "فعل", "the verb senses come first in the list too");
  const ar = makeEnv({ fetchImpl: net.fetchImpl, realDict: true, sync: { uiLang: "ar", dictSource: "offline" } });
  assert.equal((await ar.send({ type: "lookup", text: "mentioned", context: ctx })).data.translation, "ذَكَرَ", "the Arabic view agrees");
  const noun = (await en.send({ type: "lookup", text: "mention", context: { before: "It was only a ", after: " in passing." } })).data;
  assert.deepEqual({ t: noun.translation, pos: noun.heroPos }, { t: "a remark that calls attention to something or someone", pos: "اسم" });
  assert.equal(net.calls.length, 0);
});

test("online, Google's Arabic for the word in its sentence picks the same sense in the English view (bank / البنك / الضفة)", async () => {
  const withWord = ar => recorder(url => (url.includes("/translate_a/t") ? json(200, [[`التقينا قرب <a i=0>${ar}</a> صباحًا.`, "en"]]) : json(429, {})));
  const ctx = { before: "We met near the ", after: " this morning." };
  for (const [ar, lead] of [["البنك", /financial institution/], ["الضفة", /sloping land/]]) {
    const net = withWord(ar);
    const env = makeEnv({ fetchImpl: net.fetchImpl, realDict: true, sync: { uiLang: "ar", enDict: true } });
    const d = (await env.send({ type: "lookup", text: "bank", context: ctx })).data;
    assert.match(d.translation, lead, ar);
    assert.equal(d.contextSense, true);
    assert.ok(net.calls.some(c => c.url.includes("/translate_a/t")), "the sentence was translated, as in the Arabic view");
  }
});

test("online English definitions from Google lead with the part of speech the sentence suggests", async () => {
  const google = recorder(() => json(200, { src: "en", sentences: [{ trans: "ذكر", orig: "mentioned" }], definitions: [
    { pos: "noun", base_form: "mention", entry: [{ gloss: "a reference to someone or something." }] },
    { pos: "verb", base_form: "mention", entry: [{ gloss: "refer to something briefly.", example: "I mentioned it to her" }] }] }));
  const env = makeEnv({ fetchImpl: google.fetchImpl, realDict: true, sync: { uiLang: "ar", enDict: true, dictSource: "online", useContext: true } });
  const d = (await env.send({ type: "lookup", text: "mentioned", context: { before: "Everything I ", after: " yesterday." } })).data;
  assert.deepEqual({ t: d.translation, pos: d.heroPos, fits: d.contextSense }, { t: "refer to something briefly.", pos: "verb", fits: true });
});

/* ---- other languages explained in their own language ---- */

const frGoogle = (translation = "maison") => recorder(url => (url.includes("translate_a/single")
  ? json(200, { src: "fr", sentences: [{ trans: translation, orig: "maison" }],
    definitions: [{ pos: "nom", entry: [{ gloss: "Bâtiment servant d'habitation.", example: "une <b>maison</b> de campagne" }, { gloss: "Famille, lignée." }] }] })
  : json(429, {})));

test("a French word with French as the translation language is explained in French (not 'maison → maison')", async () => {
  const google = frGoogle();
  const env = makeEnv({ fetchImpl: google.fetchImpl, realDict: true, sync: { uiLang: "ar", targetLang: "fr", dictSource: "online" } });
  const d = (await env.send({ type: "lookup", text: "maison" })).data;
  assert.deepEqual({ mode: d.mode, src: d.src, tl: d.tl, t: d.translation, ex: d.heroExample, ar: d.ar },
    { mode: "explain", src: "fr", tl: "fr", t: "Bâtiment servant d'habitation.", ex: "une maison de campagne", ar: "" });
  assert.equal(d.definitions[0].entries[1].gloss, "Famille, lignée.");
});

test("the card's switch turns explanations on for one language at a time; the translation stays under the definition", async () => {
  const google = frGoogle("منزل");
  const env = makeEnv({ fetchImpl: google.fetchImpl, realDict: true, sync: { uiLang: "ar", dictSource: "online", useContext: false } });
  assert.equal((await env.send({ type: "lookup", text: "maison" })).data.translation, "منزل", "translated by default");
  await env.send({ type: "setWordDict", lang: "fr", on: true });
  assert.deepEqual(env.browser.storage.sync.data.explainLangs, ["fr"]);
  const d = (await env.send({ type: "lookup", text: "maison" })).data;
  assert.deepEqual({ mode: d.mode, t: d.translation, ar: d.ar, other: d.other }, { mode: "explain", t: "Bâtiment servant d'habitation.", ar: "منزل", other: "ar" });
  await env.send({ type: "setWordDict", lang: "fr", on: false });
  assert.deepEqual(env.browser.storage.sync.data.explainLangs, []);
  assert.equal(env.browser.storage.sync.data.enDict, undefined, "English has its own setting");
});

test("a Latin-letter word that isn't English isn't forced into the English dictionary", async () => {
  const google = frGoogle("منزل");
  const env = makeEnv({ fetchImpl: google.fetchImpl, realDict: true, sync: { uiLang: "ar", enDict: true, dictSource: "online", useContext: false } });
  const d = (await env.send({ type: "lookup", text: "maison" })).data;
  assert.deepEqual({ mode: d.mode, src: d.src, t: d.translation }, { mode: undefined, src: "fr", t: "منزل" }, "French, translated as usual");
});

test("Arabic explained in Arabic by the AI: definitions, example, root and plural, the English meaning under it", async () => {
  const asked = [];
  const net = recorder((url, body) => {
    if (url.endsWith("/api/chat")) {
      asked.push(body.messages.map(m => m.content).join("\n"));
      return ollamaReply({ senses: [
        { pos: "noun", gloss: "مجموعة أوراق مطبوعة ومجلّدة تُقرأ.", example: "قرأتُ كتابًا ممتعًا.", synonyms: ["مؤلَّف", "سِفر"] },
        { pos: "noun", gloss: "رسالة مكتوبة.", example: "وصلني كتابك.", synonyms: [] }
      ], meaning: "book", root: "ك ت ب", plural: "كُتُب" });
    }
    return json(429, {}); // Google is down: the offline dictionary gives the English meaning
  });
  const env = makeEnv({ fetchImpl: net.fetchImpl, realDict: true, local: { aiProvider: "ollama", ollamaModel: "qwen3.5:4b" },
    sync: { uiLang: "ar", explainLangs: ["ar"], useContext: false } });
  const r = await env.send({ type: "lookup", text: "كتاب" });
  assert.equal(r.ok, true, r.error);
  const d = r.data;
  assert.deepEqual({ mode: d.mode, src: d.src, tl: d.tl, other: d.other, source: d.source, t: d.translation, ex: d.heroExample, pos: d.heroPos, root: d.root, plural: d.plural },
    { mode: "explain", src: "ar", tl: "ar", other: "en", source: "ai", t: "مجموعة أوراق مطبوعة ومجلّدة تُقرأ.", ex: "قرأتُ كتابًا ممتعًا.", pos: "اسم", root: "ك ت ب", plural: "كُتُب" });
  assert.equal(d.ar, "book", "the English meaning, from the offline dictionary");
  assert.deepEqual([...d.definitions].map(p => [p.pos, p.entries.length]), [["اسم", 2]], "senses grouped by part of speech");
  assert.match(asked[0], /Arabic–Arabic learner's dictionary/);
  assert.match(asked[0], /<word>\nكتاب\n<\/word>/);
});

test("an AI explanation in the wrong script is asked again, then dropped: the translation stays and the card says why", async () => {
  let n = 0;
  const net = recorder(url => (url.endsWith("/api/chat")
    ? (n++, ollamaReply({ senses: [{ pos: "noun", gloss: "مجموعة 캐시 أوراق.", example: "", synonyms: [] }], meaning: "", root: "", plural: "" }))
    : json(429, {})));
  const env = makeEnv({ fetchImpl: net.fetchImpl, realDict: true, local: { aiProvider: "ollama", ollamaModel: "qwen3.5:4b" },
    sync: { uiLang: "ar", explainLangs: ["ar"], useContext: false } });
  const d = (await env.send({ type: "lookup", text: "كتاب" })).data;
  assert.equal(n, 2, "asked twice");
  assert.deepEqual({ mode: d.mode, t: d.translation, missing: d.explainMissing }, { mode: undefined, t: "book", missing: true });
});

test("no Google definitions and no AI: a same-language word says there's no explanation instead of giving itself back", async () => {
  const google = recorder(url => (url.includes("translate_a/single") ? json(200, { src: "fr", sentences: [{ trans: "maison", orig: "maison" }] }) : json(429, {})));
  const env = makeEnv({ fetchImpl: google.fetchImpl, realDict: true, sync: { uiLang: "ar", targetLang: "fr", dictSource: "online" } });
  const d = (await env.send({ type: "lookup", text: "maison" })).data;
  assert.deepEqual({ mode: d.mode, t: d.translation, missing: d.explainMissing }, { mode: "explain", t: "", missing: true });
});

/* ---- downloadable language packs (packs.js, tools/build_packs.py) ---- */

/** A pack file as tools/build_packs.py writes it: gzip'd { meta, shards }. */
function packFile(lang, { meta = {}, shards } = {}) {
  const data = { meta: { lang, format: 1, version: "2026-09-28", words: 2, forms: 1, ...meta }, shards: shards || {
    ma: { w: { maison: { s: [["n", "Bâtiment servant d'habitation.", "une maison de campagne", ["demeure", "logis"]], ["n", "Famille, lignée.", "", []], ["v", "(verbe fictif pour le test)", "", []]], p: "mɛ.zɔ̃" } },
      f: { maisons: "maison" } },
    fa: { w: { famille: { s: [["n", "Ensemble des parents.", "", []]] } }, f: {} }
  } };
  return gzipSync(Buffer.from(JSON.stringify(data)));
}
const PACK_URL = "https://github.com/osamaomer/lamha/releases/download/packs-v1/";
const plain = x => JSON.parse(JSON.stringify(x)); // an answer from the VM, comparable with deepEqual
/** The release serves `files` ({ "fr.json.gz": Buffer }); Google answers with `google(url)` (else 429). */
const packNet = (files, google = () => json(429, {})) => recorder(url => {
  if (url.startsWith(PACK_URL)) {
    const f = files[url.slice(PACK_URL.length)];
    return f ? new Response(f, { headers: { "content-length": String(f.length) } }) : json(404, {});
  }
  return google(url);
});

test("language packs: download one, find words and their forms with no internet, remove it", async () => {
  const net = packNet({ "fr.json.gz": packFile("fr") });
  const env = makeEnv({ fetchImpl: net.fetchImpl, realDict: true, sync: { uiLang: "ar", targetLang: "fr", dictSource: "offline" } });
  const before = (await env.send({ type: "packList" })).data.find(p => p.lang === "fr");
  assert.deepEqual({ installed: before.installed, progress: before.progress }, { installed: false, progress: null });
  assert.equal((await env.send({ type: "lookup", text: "maisons" })).ok, false, "nothing to answer with before");
  assert.deepEqual(plain(await env.send({ type: "packInstall", lang: "fr" })), { ok: true });
  const after = (await env.send({ type: "packList" })).data.find(p => p.lang === "fr");
  assert.deepEqual({ installed: after.installed, words: after.words, version: after.version }, { installed: true, words: 2, version: "2026-09-28" });
  const d = (await env.send({ type: "lookup", text: "maisons" })).data;
  assert.deepEqual({ mode: d.mode, source: d.source, q: d.query, inflected: d.inflected, t: d.translation, ex: d.heroExample, pos: d.heroPos, ipa: d.srcTranslit },
    { mode: "explain", source: "local", q: "maison", inflected: "maisons", t: "Bâtiment servant d'habitation.", ex: "une maison de campagne", pos: "اسم", ipa: "mɛ.zɔ̃" });
  assert.deepEqual([...d.definitions].map(p => [p.pos, p.entries.length]), [["اسم", 2], ["فعل", 1]], "grouped by part of speech");
  assert.deepEqual([...d.definitions[0].entries[0].synonyms], ["demeure", "logis"]);
  assert.equal((await env.send({ type: "lookup", text: "Famille" })).data.query, "famille", "any capitalisation");
  const toArabic = makeEnv({ fetchImpl: net.fetchImpl, realDict: true, packStore: env.packStore, sync: { uiLang: "ar", dictSource: "offline" } });
  assert.equal((await toArabic.send({ type: "lookup", text: "maison" })).data.source, "local", "offline, any installed pack beats an error");
  assert.deepEqual(plain(await env.send({ type: "packRemove", lang: "fr" })), { ok: true });
  assert.equal([...env.packStore.data.keys()].length, 0, "every part is gone");
  assert.equal((await env.send({ type: "lookup", text: "famille" })).ok, false);
  assert.ok(net.calls.every(c => c.url.startsWith(PACK_URL)), "only the download went online");
});

test("online, a pack explains a word before the AI is asked; Google's translation stays under it", async () => {
  const net = packNet({ "fr.json.gz": packFile("fr") }, url => (url.includes("translate_a/single") ? json(200, { src: "fr", sentences: [{ trans: "منزل", orig: "maison" }] }) : json(429, {})));
  const env = makeEnv({ fetchImpl: net.fetchImpl, realDict: true, local: { aiProvider: "ollama", ollamaModel: "qwen3.5:4b" },
    sync: { uiLang: "ar", explainLangs: ["fr"], dictSource: "online", useContext: false } });
  await env.send({ type: "packInstall", lang: "fr" });
  const d = (await env.send({ type: "lookup", text: "maison" })).data;
  assert.deepEqual({ mode: d.mode, source: d.source, t: d.translation, ar: d.ar, other: d.other }, { mode: "explain", source: "local", t: "Bâtiment servant d'habitation.", ar: "منزل", other: "ar" });
  assert.ok(!net.calls.some(c => c.url.endsWith("/api/chat")), "no AI call");
});

test("a pack that isn't there, is damaged or is for another language is refused, and nothing is kept", async () => {
  const net = packNet({ "fr.json.gz": packFile("de"), "de.json.gz": Buffer.from("not gzip at all") });
  const env = makeEnv({ fetchImpl: net.fetchImpl, realDict: true });
  assert.deepEqual(plain(await env.send({ type: "packInstall", lang: "fr" })), { ok: false, error: "pack_bad" }, "a German pack under the French name");
  assert.deepEqual(plain(await env.send({ type: "packInstall", lang: "de" })), { ok: false, error: "pack_bad" });
  assert.deepEqual(plain(await env.send({ type: "packInstall", lang: "es" })), { ok: false, error: "pack_download" }, "not on the release");
  assert.deepEqual(plain(await env.send({ type: "packInstall", lang: "../x" })), { ok: false, error: "pack_unknown" });
  assert.equal(env.packStore.data.size, 0);
  assert.ok((await env.send({ type: "packList" })).data.every(p => !p.installed && p.progress === null));
});

/** A downloaded Wikipedia, as the Windows app hands it to the background: articles by "lang|title". */
function fakeWiki(articles) {
  const asked = [];
  return {
    asked,
    langs: () => [...new Set(Object.keys(articles).map(k => k.split("|")[0]))],
    summary: async (titles, lang) => {
      asked.push([lang, ...titles]);
      for (const t of titles) {
        if (Object.hasOwn(articles, lang + "|" + t)) return { lang, title: t, extract: articles[lang + "|" + t], url: "", thumb: "", offline: { date: "2026-07-10", id: "x", path: t } };
      }
      return null;
    }
  };
}
/** Wikipedia itself: Paris has an Arabic article. `net.online = false` turns the internet off. */
function wikipediaNet() {
  const net = { online: true };
  const rec = recorder(url => {
    if (!net.online) throw new TypeError("NetworkError when attempting to fetch resource.");
    if (url.includes("en.wikipedia.org/w/api.php")) return json(200, { query: { pages: { 1: { title: "Paris", langlinks: [{ "*": "باريس" }] } } } });
    if (url.includes("ar.wikipedia.org/api/rest_v1/page/summary/")) return json(200, { type: "standard", title: "باريس", extract: "من ويكيبيديا نفسها.", content_urls: { desktop: { page: "https://ar.wikipedia.org/wiki/x" } } });
    return json(404, {});
  });
  return Object.assign(net, rec);
}

test("offline Wikipedia: with no internet the downloaded copy answers, found by the word's translation; it isn't kept, so Wikipedia answers once back online", async () => {
  const wiki = fakeWiki({ "ar|باريس": "باريس عاصمة فرنسا." });
  const net = wikipediaNet();
  net.online = false;
  const env = makeEnv({ fetchImpl: net.fetchImpl, wiki });
  const off = await env.send({ type: "wiki", title: "Paris", lang: "ar", alt: ["باريس"] });
  assert.equal(off.data.extract, "باريس عاصمة فرنسا.");
  assert.equal(off.data.offline.date, "2026-07-10", "the card can say it's the downloaded copy");
  assert.deepEqual(plain(wiki.asked), [["ar", "باريس", "Paris"]], "the translation first: no internet to ask Wikipedia for the Arabic title");
  net.online = true;
  const on = await env.send({ type: "wiki", title: "Paris", lang: "ar", alt: ["باريس"] });
  assert.equal(on.data.extract, "من ويكيبيديا نفسها.");
  assert.equal(on.data.offline, undefined);
});

test("offline Wikipedia: 'offline only' never asks Wikipedia; without a downloaded copy the card gets nothing, still without going online", async () => {
  const net = wikipediaNet();
  const withCopy = makeEnv({ fetchImpl: net.fetchImpl, wiki: fakeWiki({ "ar|باريس": "باريس عاصمة فرنسا." }) });
  assert.equal((await withCopy.send({ type: "wiki", title: "Paris", lang: "ar", alt: ["باريس"], offline: true })).data.extract, "باريس عاصمة فرنسا.");
  const without = makeEnv({ fetchImpl: net.fetchImpl });
  assert.equal((await without.send({ type: "wiki", title: "Paris", lang: "ar", offline: true })).data, null);
  assert.equal(net.calls.length, 0, "nothing went online");
});

test("offline Wikipedia first (a per-device setting): the copy answers before Wikipedia; what it doesn't have is asked online", async () => {
  const net = wikipediaNet();
  const env = makeEnv({ fetchImpl: net.fetchImpl, local: { wikiOfflineFirst: true }, wiki: fakeWiki({ "ar|باريس": "باريس عاصمة فرنسا." }) });
  assert.equal((await env.send({ type: "wiki", title: "Paris", lang: "ar", alt: ["باريس"] })).data.extract, "باريس عاصمة فرنسا.");
  assert.equal(net.calls.length, 0);
  await env.send({ type: "wiki", title: "Lyon", lang: "ar", alt: ["ليون"] });
  assert.ok(net.calls.length > 0, "not in the copy: Wikipedia is asked");
});

test("offline Wikipedia: the English copy answers when the translation language's copy has no such article", async () => {
  const wiki = fakeWiki({ "en|Photosynthesis": "Photosynthesis is how plants make food." });
  const env = makeEnv({ fetchImpl: wikipediaNet().fetchImpl, wiki });
  const r = await env.send({ type: "wiki", title: "Photosynthesis", lang: "ar", alt: ["تركيب ضوئي"], offline: true });
  assert.equal(r.data.lang, "en");
  assert.deepEqual(plain(wiki.asked), [["ar", "تركيب ضوئي", "Photosynthesis"], ["en", "Photosynthesis"]]);
  const bad = await env.send({ type: "wiki", title: "Photosynthesis", lang: "ar", alt: [{ toString: () => "x" }, "y".repeat(200)], offline: true });
  assert.equal(bad.data.lang, "en");
  assert.deepEqual(plain(wiki.asked[2]), ["ar", "Photosynthesis"], "only short text titles are passed on");
});

/** Lamha for Windows as Firefox reaches it (runtime.connectNative): answers by message type. missing: not installed. */
function fakeApp(handlers, { missing = false } = {}) {
  const sent = [];
  const connectNative = name => {
    const onMsg = [], onGone = [];
    const port = {
      name, error: null,
      onMessage: { addListener: f => onMsg.push(f) },
      onDisconnect: { addListener: f => onGone.push(f) },
      disconnect() {},
      postMessage: m => {
        sent.push(JSON.parse(JSON.stringify(m)));
        setTimeout(async () => {
          if (missing) { port.error = { message: "No such native application com.artworklab.lamha" }; onGone.forEach(f => f(port)); return; }
          const reply = Object.hasOwn(handlers, m.type) ? { id: m.id, ok: true, data: await handlers[m.type](m) } : { id: m.id, ok: false, error: "unknown" };
          onMsg.forEach(f => f(reply));
        }, 0);
      }
    };
    return port;
  };
  return { sent, connectNative };
}
const APP_HELLO = { hello: () => ({ app: "lamha", version: "1.10.0", wiki: { langs: ["ar"] } }) };

test("Firefox with Lamha for Windows connected: the card's Wikipedia comes from the app's copy, and only fields of the right kind are kept", async () => {
  const app = fakeApp({ ...APP_HELLO, wikiSummary: m => ({
    lang: "ar", title: "باريس", extract: "باريس عاصمة فرنسا.", url: "javascript:alert(1)", thumb: "data:text/html;base64,PHNjcmlwdD4=",
    offline: { date: "2026-07-10", id: "wikipedia_ar_top_mini_2026-07", path: "باريس", more: "x" }, extra: "<b>", asked: m.titles
  }) });
  const env = makeEnv({ fetchImpl: wikipediaNet().fetchImpl, local: { appLink: true }, connectNative: app.connectNative });
  const r = await env.send({ type: "wiki", title: "Paris", lang: "ar", alt: ["باريس"], offline: true });
  assert.deepEqual(plain(r.data), { lang: "ar", title: "باريس", extract: "باريس عاصمة فرنسا.", url: "", thumb: "",
    offline: { date: "2026-07-10", id: "wikipedia_ar_top_mini_2026-07", path: "باريس" } });
  assert.deepEqual(app.sent.map(m => [m.type, m.launch]), [["hello", false], ["wikiSummary", false]], "a lookup never starts the app");
  assert.deepEqual(app.sent[1].titles, ["باريس", "Paris"]);
});

test("Firefox not connected in Settings: the app is never asked", async () => {
  const app = fakeApp(APP_HELLO);
  const env = makeEnv({ fetchImpl: wikipediaNet().fetchImpl, connectNative: app.connectNative });
  assert.equal((await env.send({ type: "wiki", title: "Paris", lang: "ar", alt: ["باريس"], offline: true })).data, null);
  assert.deepEqual(plain((await env.send({ type: "appStatus" })).data), { supported: true, allowed: false });
  assert.equal(app.sent.length, 0);
});

test("Settings → Lamha for Windows: connecting remembers it and reaches the app (starting it); not installed says so", async () => {
  const app = fakeApp(APP_HELLO);
  const env = makeEnv({ fetchImpl: wikipediaNet().fetchImpl, connectNative: app.connectNative });
  const on = await env.send({ type: "appLink", on: true });
  assert.deepEqual(plain(on.data), { supported: true, allowed: true, connected: true, version: "1.10.0", langs: ["ar"], deckSyncedAt: 0 });
  assert.equal((await env.browser.storage.local.get("appLink")).appLink, true);
  assert.deepEqual(app.sent.filter(m => m.type === "hello").map(m => [m.type, m.launch]), [["hello", true]], "Connect may start the app");
  assert.ok(app.sent.filter(m => m.type === "deckSync").every(m => !m.launch), "the decks meet then, but never by starting the app");
  const none = makeEnv({ fetchImpl: wikipediaNet().fetchImpl, local: { appLink: true }, connectNative: fakeApp(APP_HELLO, { missing: true }).connectNative });
  assert.deepEqual(plain((await none.send({ type: "appStatus" })).data), { supported: true, allowed: true, connected: false, error: "not_installed" });
  assert.equal((await none.send({ type: "wiki", title: "Paris", lang: "ar", alt: ["باريس"], offline: true })).data, null, "and the card simply has no Wikipedia part");
});

test("'Read the article in Lamha' from Firefox asks the app to open it, starting the app if it isn't running", async () => {
  const opened = [];
  const app = fakeApp({ ...APP_HELLO, openReader: m => { opened.push([m.file, m.path]); return true; } });
  const env = makeEnv({ fetchImpl: wikipediaNet().fetchImpl, local: { appLink: true }, connectNative: app.connectNative });
  assert.deepEqual(plain(await env.send({ type: "wikiOpen", file: "wikipedia_ar_top_mini_2026-07", path: "باريس" })), { ok: true });
  assert.deepEqual(opened, [["wikipedia_ar_top_mini_2026-07", "باريس"]]);
  assert.equal(app.sent.at(-1).launch, true);
});

test("offline Arabic → English results label their word list in the interface language", async () => {
  const ctx = vm.createContext({
    browser: { runtime: { getURL: p => "moz-extension://x/" + p } },
    fetch: async url => json(200, /\/dict\/ar\//.test(url) ? { "كتاب": ["book", "volume"] } : {})
  });
  vm.runInContext(src("shared/i18n.js"), ctx, { filename: "i18n.js" });
  vm.runInContext(src("local-dict.js") + "\n;globalThis.LocalDict = LocalDict;", ctx, { filename: "local-dict.js" });
  ctx.LamhaI18n.setLang("en");
  assert.equal((await ctx.LocalDict.lookupAr("كتاب")).dict[0].pos, "in English");
  ctx.LamhaI18n.setLang("ar");
  assert.equal((await ctx.LocalDict.lookupAr("كتاب")).dict[0].pos, "بالإنجليزية");
});

test("Arabic interface: explanations and messages stay Arabic", async () => {
  const r = recorder(() => claudeReply(PROOF));
  const env = makeEnv({ fetchImpl: r.fetchImpl, local: { aiKey: "sk-test" } });
  await env.send({ type: "ai", tool: "proofread", text: "I go to market yesterday.", extra: {} });
  assert.match(r.calls[0].body.messages[0].content, /one-sentence explanation in Arabic/);
  assert.equal(env.ctx.LamhaAI.errorInfo("ai_no_key")[0], "فعّل أدوات الكتابة");
});

test("an update keeps Arabic for existing users; a new install follows the system", async () => {
  const updated = makeEnv({ fetchImpl: async () => json(200, {}), sync: {} });
  await updated.installed({ reason: "update" });
  assert.equal(updated.browser.storage.sync.data.uiLang, "ar");
  const kept = makeEnv({ fetchImpl: async () => json(200, {}), sync: { uiLang: "en" } });
  await kept.installed({ reason: "update" });
  assert.equal(kept.browser.storage.sync.data.uiLang, "en", "a chosen language is never overwritten");
  const fresh = makeEnv({ fetchImpl: async () => json(200, {}), sync: {} });
  await fresh.installed({ reason: "install" });
  assert.equal(fresh.browser.storage.sync.data.uiLang, undefined, "new installs stay on automatic");
  const resolve = (pref, sys) => vm.runInContext(`LamhaI18n.resolve(${JSON.stringify(pref)}, ${JSON.stringify(sys)})`, fresh.ctx);
  assert.equal(resolve("auto", "ar-SA"), "ar");
  assert.equal(resolve("auto", "en-US"), "en");
  assert.equal(resolve("auto", "fr-FR"), "en");
  assert.equal(resolve("ar", "en-US"), "ar");
});

test("words named like Object's properties look up offline like any other ('constructor' is a builder)", async () => {
  const env = makeEnv({ fetchImpl: async () => json(404, {}), sync: { uiLang: "ar", dictSource: "offline" }, realDict: true });
  const res = await env.send({ type: "lookup", text: "constructor" });
  assert.equal(res.ok, true, res.error);
  assert.equal(res.data.source, "local");
  assert.match(res.data.translation, /\p{Script=Arabic}/u);
  assert.deepEqual(plain(env.ctx.LamhaAI.errorInfo("constructor")), ["حدث خطأ", "أعد المحاولة."], "an unknown code, not Object's own");
});

/** Runs `fn` with the background's clock at `iso` (local time) in time zone `tz`. */
async function atTime(env, tz, iso, fn) {
  const before = process.env.TZ;
  process.env.TZ = tz; // Node reads it again when it's set
  try {
    const t = new Date(iso).getTime();
    vm.runInContext(`(() => { const R = Date, T = ${t}; globalThis.Date = class extends R { constructor(...a) { super(...(a.length ? a : [T])); } static now() { return T; } }; })()`, env.ctx);
    await fn();
  } finally {
    if (before === undefined) delete process.env.TZ; else process.env.TZ = before;
  }
}

test("days go by the calendar: the night the clocks go forward (23 hours) breaks no streak and loses no day", async () => {
  const cards = { a: { ...newCard, q: "a", tr: "أ" } };
  // Egypt moved its clocks forward at midnight on Friday 24 April 2026; it's 00:30 on Saturday
  const env = makeEnv({ fetchImpl: async () => json(200, {}), local: { cards, cardsImported: true,
    activity: { "Thu Apr 23 2026": 2, "Fri Apr 24 2026": 3 }, cardStats: { reviewDay: "Fri Apr 24 2026", streak: 4 } } });
  await atTime(env, "Africa/Cairo", "2026-04-25T00:30:00", async () => {
    assert.equal((await env.send({ type: "today" })).streak, 2, "Friday and Thursday, not a gap on Friday");
    await env.send({ type: "reviewGrade", key: "a", grade: "good" });
    assert.equal((await env.send({ type: "reviewQueue" })).streak, 5, "yesterday's review streak goes on");
    assert.equal(env.browser.storage.local.data.activity["Fri Apr 24 2026"], 3, "yesterday's practice is kept");
    assert.equal((await env.send({ type: "today" })).streak, 3);
  });
});

test("a word looked up again (answered from the cache) goes back to the top of the history, as a fresh lookup does", async () => {
  const google = recorder(url => json(200, { src: "en", sentences: [{ trans: url.includes("q=tenacious") ? "عنيد" : "مرن", orig: "x" }] }));
  const env = makeEnv({ fetchImpl: google.fetchImpl, local: { cardsImported: true }, sync: { dictSource: "online", translateDefinitions: false, cardsAuto: false } });
  await env.send({ type: "lookup", text: "resilient" });
  await env.send({ type: "lookup", text: "tenacious" });
  const asked = google.calls.length;
  await env.send({ type: "lookup", text: "resilient" });
  assert.equal(google.calls.length, asked, "answered from the cache");
  assert.deepEqual(plain(env.browser.storage.local.data.history.map(h => h.q)), ["resilient", "tenacious"]);
  env.browser.storage.local.data.history = []; // cleared in Settings (the Windows app keeps its cache for days)
  await env.send({ type: "lookup", text: "tenacious" });
  assert.deepEqual(plain(env.browser.storage.local.data.history.map(h => h.q)), ["tenacious"], "recorded again after clearing");
});

test("offline Wikipedia: 'nothing found' isn't kept, so a copy downloaded afterwards answers at once", async () => {
  const articles = {};
  const net = wikipediaNet();
  const env = makeEnv({ fetchImpl: net.fetchImpl, wiki: fakeWiki(articles) });
  const ask = () => env.send({ type: "wiki", title: "Paris", lang: "ar", alt: ["باريس"], offline: true });
  assert.equal((await ask()).data, null, "no copy yet");
  articles["ar|باريس"] = "باريس عاصمة فرنسا."; // downloaded in Settings meanwhile
  assert.equal((await ask()).data.extract, "باريس عاصمة فرنسا.");
  assert.equal(net.calls.length, 0, "never online");
});

test("no network at all (navigator.onLine false): the dictionary and the downloaded Wikipedia answer without asking the internet first", async () => {
  const net = recorder(() => { throw new TypeError("NetworkError when attempting to fetch resource."); });
  const env = makeEnv({ fetchImpl: net.fetchImpl, sync: { uiLang: "ar", dictSource: "online" }, realDict: true,
    navigator: { onLine: false }, wiki: fakeWiki({ "ar|بيت": "البيت مكان السكن." }) });
  const res = await env.send({ type: "lookup", text: "house" });
  assert.equal(res.ok, true, res.error);
  assert.equal(res.data.source, "local", "Online first, but there's no network: the dictionary");
  const w = await env.send({ type: "wiki", title: "house", lang: "ar", alt: ["بيت"] });
  assert.equal(w.data.extract, "البيت مكان السكن.");
  assert.deepEqual(net.calls.filter(c => !String(c.url).includes("/dict/")).map(c => c.url), [], "nothing was sent to the internet");
});

test("inflected forms come in small parts, each under the key the dictionary looks in; irregular ones still find their word", async () => {
  const { readdirSync, statSync } = await import("node:fs");
  const dir = new URL("dict/forms/", root);
  const shard = w => { const k = w.toLowerCase().replace(/[^a-z]/g, "").slice(0, 2); return k ? (k + "_").slice(0, 2) : "__"; }; // local-dict.js shardEn
  let forms = 0, largest = 0;
  for (const f of readdirSync(dir)) {
    const part = JSON.parse(src("dict/forms/" + f));
    for (const w of Object.keys(part)) assert.equal(shard(w) + ".json", f, `"${w}" is in ${f}`);
    forms += Object.keys(part).length;
    largest = Math.max(largest, statSync(new URL(f, dir)).size);
  }
  assert.ok(forms > 40000, forms + " forms");
  assert.ok(largest < 100 * 1024, "the largest part is " + largest + " bytes");
  const env = makeEnv({ fetchImpl: async () => json(404, {}), sync: { uiLang: "ar", dictSource: "offline" }, realDict: true });
  for (const [form, word] of [["went", "go"], ["children", "child"], ["mice", "mouse"], ["geese", "goose"], ["studied", "study"], ["taught", "teach"]]) {
    const r = await env.send({ type: "lookup", text: form });
    assert.equal(r.ok && r.data.query, word, `${form} → ${r.ok ? r.data.query : r.error}`);
  }
});

test("the deck is read from storage once, not for every lookup; deleting all cards in Settings is never undone", async () => {
  const cards = {};
  for (let i = 0; i < 50; i++) cards["w" + i] = { ...newCard, q: "w" + i, tr: "ك" };
  const google = recorder(() => json(200, { src: "en", sentences: [{ trans: "مرن", orig: "x" }] }));
  const env = makeEnv({ fetchImpl: google.fetchImpl, local: { cards, cardsImported: true }, sync: { uiLang: "ar", dictSource: "online", translateDefinitions: false }, events: true });
  let reads = 0;
  const get = env.browser.storage.local.get.bind(env.browser.storage.local);
  env.browser.storage.local.get = keys => { if ([].concat(keys || []).includes("cards")) reads++; return get(keys); };
  await env.send({ type: "lookup", text: "resilient" }); // adds a card (cardsAuto)
  await env.send({ type: "cardHas", q: "resilient" });
  await env.send({ type: "reviewQueue" });
  await env.send({ type: "lookup", text: "tenacious" });
  const q = await env.send({ type: "reviewQueue" });
  assert.equal(q.counts.total, 52);
  assert.equal(reads, 1, "one read of the deck");
  assert.equal(env.browser.storage.local.data.cards.tenacious.tr, "مرن", "every change still reaches storage");
  await env.browser.storage.local.set({ cards: {}, cardStats: {}, cardsImported: true }); // Settings → delete all cards
  assert.equal((await env.send({ type: "reviewQueue" })).counts.total, 0, "the deleted deck isn't kept in memory");
  await env.send({ type: "cardToggle", card: { q: "candid", tr: "صريح" } });
  assert.deepEqual(Object.keys(env.browser.storage.local.data.cards), ["candid"], "nor written back");
});

const ago = days => Date.now() - days * DAYMS;
const deckCard = (q, extra = {}) => ({ ...newCard, q, tr: "ك", added: ago(30), mod: ago(30), ...extra });

test("a copy of my data: saved, restored elsewhere; nothing there is lost, the copy reviewed last keeps its schedule, a removed word stays removed", async () => {
  const net = async () => json(404, {});
  const here = makeEnv({ fetchImpl: net, local: { cardsImported: true,
    cards: { apple: deckCard("apple", { last: ago(10), reps: 2, interval: 5, due: 9e12, ex: "An apple a day." }), pear: deckCard("pear") },
    history: [{ q: "apple", tr: "تفاحة", src: "en", t: ago(10) }], mistakes: { checks: 5, counts: { articles: 3 }, recent: [] } } });
  const saved = JSON.parse(JSON.stringify((await here.send({ type: "dataExport" })).data)); // through a file
  assert.deepEqual([saved.app, saved.format, Object.keys(saved.cards).sort()], ["lamha", 1, ["apple", "pear"]]);

  const there = makeEnv({ fetchImpl: net, local: { cardsImported: true,
    cards: { apple: deckCard("apple", { last: ago(2), reps: 3, interval: 12 }), kiwi: deckCard("kiwi") },
    cardsRemoved: { pear: ago(5) } } }); // pear was removed there after it was last changed
  const r = await there.send({ type: "dataImport", data: saved });
  assert.deepEqual(plain(r.data), { cards: 1, history: 1 });
  const cards = there.browser.storage.local.data.cards;
  assert.deepEqual(Object.keys(cards).sort(), ["apple", "kiwi"], "kiwi kept; pear stays removed");
  assert.deepEqual([cards.apple.reps, cards.apple.interval, cards.apple.ex], [3, 12, "An apple a day."], "the later review's schedule, the missing example filled in");
  assert.equal(there.browser.storage.local.data.mistakes.checks, 5, "the journal that has seen more");
  assert.deepEqual(plain((await there.send({ type: "dataImport", data: saved })).data), { cards: 0, history: 0 }, "restoring twice adds nothing");

  const hostile = { app: "lamha", format: 1, history: "no", activity: { __proto__: 5, "Mon Sep 28 2026": 3, x: 1e9 },
    cards: { a: { q: "__proto__", tr: "بروتو" }, b: { q: 5 }, c: { q: "okay", tr: "ع".repeat(5000), ease: 99, reps: -3, en: "yes" }, d: null } };
  assert.equal((await there.send({ type: "dataImport", data: hostile })).ok, true);
  const after = there.browser.storage.local.data.cards;
  assert.equal(Object.hasOwn(after, "__proto__") && after.__proto__.tr, "بروتو", "an ordinary word");
  assert.deepEqual([after.okay.tr.length, after.okay.ease, after.okay.reps, after.okay.en], [200, 5, 0, undefined]);
  assert.equal(({}).polluted, undefined);
  assert.deepEqual(Object.keys(there.browser.storage.local.data.activity), ["Mon Sep 28 2026"]);
  assert.equal((await there.send({ type: "dataImport", data: { cards: {} } })).error, "not_backup");
});

test("Firefox and the Windows app share the review deck: each gets the other's words, the later review's schedule and removals; a big deck goes in pages under 1 MB", async () => {
  const appEnv = makeEnv({ fetchImpl: async () => json(404, {}), wiki: fakeWiki({}), local: { cardsImported: true, // the app (it has LamhaWikiOffline)
    cards: { apple: deckCard("apple", { last: ago(2), reps: 3, interval: 12 }), cherry: deckCard("cherry") } } });
  let biggest = 0;
  const size = m => { biggest = Math.max(biggest, Buffer.byteLength(JSON.stringify(m))); return m; };
  const link = fakeApp({ deckSync: async m => { // desktop/main.js BRIDGE_CALLS.deckSync
    size(m);
    const r = await appEnv.send({ type: "deckSync", upload: m.upload === true, cards: m.cards, removed: m.removed, since: m.since });
    return size(JSON.parse(JSON.stringify(r.data)));
  } });
  const ff = makeEnv({ fetchImpl: async () => json(404, {}), connectNative: link.connectNative, local: { appLink: true, cardsImported: true,
    cards: { apple: deckCard("apple", { last: ago(10), reps: 2 }), banana: deckCard("banana") } } });
  const both = () => [ff, appEnv].map(e => Object.keys(e.browser.storage.local.data.cards).sort().join(","));

  await ff.send({ type: "deckSyncNow" });
  assert.deepEqual(both(), ["apple,banana,cherry", "apple,banana,cherry"]);
  assert.equal(ff.browser.storage.local.data.cards.apple.reps, 3, "the schedule of the copy reviewed last");
  await new Promise(r => setTimeout(r, 5));
  await ff.send({ type: "cardToggle", card: { q: "banana" } }); // removed in Firefox
  await appEnv.send({ type: "reviewGrade", key: "cherry", grade: "good" }); // reviewed in the app
  await ff.send({ type: "deckSyncNow" });
  assert.deepEqual(both(), ["apple,cherry", "apple,cherry"], "the removal reached the app");
  assert.equal(ff.browser.storage.local.data.cards.cherry.reps, 1, "the app's review reached Firefox");

  const many = {};
  for (let i = 0; i < 3000; i++) many["w" + i] = deckCard("w" + i, { tr: "معنى عربي طويل للكلمة ".repeat(6), ex: "An example sentence where the word appears, found on a page." });
  await appEnv.send({ type: "dataImport", data: { app: "lamha", format: 1, cards: many } }); // 3,000 words in the app
  const fresh = makeEnv({ fetchImpl: async () => json(404, {}), connectNative: link.connectNative, local: { appLink: true, cardsImported: true } });
  biggest = 0;
  await fresh.send({ type: "deckSyncNow" });
  assert.equal(Object.keys(fresh.browser.storage.local.data.cards).length, 3002, "every word arrived");
  assert.ok(biggest < 1000 * 1024, `the largest message was ${biggest} bytes`);
  assert.ok(link.sent.filter(m => m.type === "deckSync").every(m => !m.launch), "never by starting the app");
});

test("Report a problem: settings, counts and the latest errors — never a word, a text, a key or an address", async () => {
  const env = makeEnv({ fetchImpl: async () => { throw new TypeError("NetworkError when attempting to fetch resource."); },
    sync: { uiLang: "ar", dictSource: "online", disabledSites: ["secret-bank.example"] },
    local: { aiKey: "sk-ant-SECRET", aiKeySet: true, ollamaUrl: "http://192.168.1.7:11434", ollamaModel: "qwen3.5:4b", cardsImported: true,
      cards: { serendipity: deckCard("serendipity") }, history: [{ q: "privateword", tr: "خاص", src: "en", t: 1 }] } });
  await env.send({ type: "lookup", text: "A sentence the user selected." });
  await env.send({ type: "ai", tool: "proofread", text: "my private letter" });
  await new Promise(r => setTimeout(r, 20));
  const report = (await env.send({ type: "diagnostics" })).data;
  for (const secret of ["SECRET", "192.168", "privateword", "serendipity", "secret-bank", "sentence the user", "private letter"]) assert.ok(!report.includes(secret), "leaks " + secret);
  assert.match(report, /^Lamha /);
  assert.match(report, /1 cards, 1 in history/);
  assert.match(report, /lookup: network/);
  assert.match(report, /Ollama model qwen3\.5:4b at a custom address/);
});

const IN_SENTENCE = { before: "Kids are very ", after: " after all." };

test("a network that answers nothing: the dictionary's answer shows within seconds, and the next lookups don't wait at all", async () => {
  const SCALE = 50; // the background's timers run 50× faster: its 8–12 s timeouts take a fraction of a second here
  const calls = [];
  const dead = async (url, init = {}) => { // connected, but nothing ever answers (a Wi-Fi with no internet behind it)
    calls.push(String(url));
    return new Promise((_, reject) => init.signal && init.signal.addEventListener("abort", () => reject(Object.assign(new Error("aborted"), { name: "AbortError" }))));
  };
  const env = makeEnv({ fetchImpl: dead, realDict: true, scale: SCALE, sync: { uiLang: "ar", dictSource: "local" } });
  let t0 = Date.now();
  const first = await env.send({ type: "lookup", text: "resilient", context: IN_SENTENCE });
  assert.equal(first.data.source, "local");
  assert.ok((Date.now() - t0) * SCALE < 4000, `the card waited ${(Date.now() - t0) * SCALE} ms for the sentence`);
  await new Promise(r => setTimeout(r, 12000 / SCALE + 50)); // the sentence's request gives up: Google counts as unreachable
  const sent = calls.length;
  t0 = Date.now();
  const second = await env.send({ type: "lookup", text: "tenacious", context: IN_SENTENCE });
  assert.equal(second.data.source, "local");
  const sentence = await env.send({ type: "lookup", text: "Kids are very resilient after all." });
  assert.equal(sentence.error, "network", "a sentence says there's no connection");
  assert.ok(Date.now() - t0 < 300, "at once");
  assert.equal(calls.length, sent, "nothing sent while Google is unreachable");
});

test("Online first on a network that answers nothing: the dictionary answers a word it knows within ~2.5 s, sentence included, and that answer isn't kept", async () => {
  const SCALE = 50;
  const net = { up: false };
  const fetchImpl = async (url, init = {}) => {
    url = String(url);
    if (net.up) return url.includes("/translate_a/single") ? json(200, { src: "en", sentences: [{ trans: "مرن", orig: "resilient" }] }) : json(200, [["الأطفال <a i=0>مرنون</a> جدًا.", "en"]]);
    return new Promise((_, reject) => init.signal && init.signal.addEventListener("abort", () => reject(Object.assign(new Error("aborted"), { name: "AbortError" }))));
  };
  const env = makeEnv({ fetchImpl, realDict: true, scale: SCALE, sync: { uiLang: "ar", dictSource: "online", translateDefinitions: false } });
  const t0 = Date.now();
  const r = await env.send({ type: "lookup", text: "resilient", context: IN_SENTENCE });
  assert.equal(r.data.source, "local");
  assert.ok((Date.now() - t0) * SCALE < 3500, `the card waited ${(Date.now() - t0) * SCALE} ms`);
  net.up = true; // back before the first request has given up: Google answers the same word now
  const again = await env.send({ type: "lookup", text: "resilient", context: IN_SENTENCE });
  assert.equal(again.data.source, "online", "the dictionary's stand-in answer wasn't cached");
});

test("connected with no internet behind it (every request fails at once): no waiting on retries; just after Google answered, a quick failure is tried again", async () => {
  const calls = [];
  const env = makeEnv({ fetchImpl: async url => { calls.push(String(url)); throw new TypeError("NetworkError when attempting to fetch resource."); },
    realDict: true, sync: { uiLang: "ar", dictSource: "online", translateDefinitions: false } });
  const t0 = Date.now();
  const r = await env.send({ type: "lookup", text: "resilient", context: IN_SENTENCE });
  assert.equal(r.data.source, "local");
  assert.ok(Date.now() - t0 < 300, `the card waited ${Date.now() - t0} ms`);
  assert.equal(new Set(calls).size, calls.length, "no address asked twice: " + calls.length + " requests");

  let failNext = 0;
  const blip = makeEnv({ fetchImpl: async url => {
    if (failNext > 0) { failNext--; throw new TypeError("NetworkError when attempting to fetch resource."); }
    return url.includes("/translate_a/single") ? json(200, { src: "en", sentences: [{ trans: "جملة", orig: "x" }] }) : json(404, {});
  } });
  assert.equal((await blip.send({ type: "lookup", text: "One sentence here." })).ok, true);
  failNext = 2; // both addresses fail once, a moment after Google answered
  const after = await blip.send({ type: "lookup", text: "Another sentence here." });
  assert.equal(after.ok, true, "a blip is tried again: " + after.error);
});

test("Online first: the word's sentence is translated alongside the lookup, not after it", async () => {
  const events = [];
  const fetchImpl = async url => {
    const kind = url.includes("/translate_a/single") ? "single" : url.includes("/translate_a/t") ? "sentence" : "other";
    events.push("start " + kind);
    await new Promise(r => setTimeout(r, 30));
    events.push("end " + kind);
    return kind === "sentence" ? json(200, [["الأطفال <a i=0>مرنون</a> جدًا.", "en"]]) : json(200, { src: "en", sentences: [{ trans: "مرن", orig: "resilient" }] });
  };
  const env = makeEnv({ fetchImpl, sync: { uiLang: "ar", dictSource: "online", translateDefinitions: false } });
  const r = await env.send({ type: "lookup", text: "resilient", context: IN_SENTENCE });
  assert.equal(r.data.context.word, "مرنون");
  assert.ok(events.indexOf("start sentence") < events.indexOf("end single"), events.join(", "));
});

/* optional: one real request to the local Ollama */
if (process.argv.includes("--ollama")) {
  test("LIVE Ollama proofread (real model)", async () => {
    const tags = await (await fetch("http://localhost:11434/api/tags")).json();
    const model = process.env.OLLAMA_MODEL || (tags.models.find(m => /qwen/i.test(m.name)) || tags.models[0]).name;
    const env = makeEnv({ fetchImpl: fetch, local: { aiProvider: "ollama", ollamaModel: model } });
    const text = "Yesterday I go to the market for buy some vegetable, but the shop was close. I am very disappoint.";
    const t0 = Date.now();
    const res = await env.send({ type: "ai", tool: "proofread", text, extra: {} });
    assert.equal(res.ok, true, res.error);
    await env.flush();
    console.log(`\n    model: ${model} (${((Date.now() - t0) / 1000).toFixed(1)} s)`);
    console.log(`    corrected: ${res.data.corrected}`);
    res.data.issues.forEach(i => console.log(`    - [${i.category}] ${i.original} → ${i.fix} | ${i.why}`));
    console.log(`    journal: ${JSON.stringify(env.browser.storage.local.data.mistakes.counts)}`);
    assert.ok(res.data.issues.length >= 3, "should find several mistakes");
    assert.ok(res.data.issues.every(i => env.ctx.LamhaAI.CATEGORIES[i.category]));
  });
}

/* optional: one real proofread through Gemini ($env:GEMINI_API_KEY = "AIza…"; node tools/test-writing.mjs --gemini) */
if (process.argv.includes("--gemini")) {
  test("LIVE Gemini proofread (real API, free tier)", async () => {
    assert.ok(process.env.GEMINI_API_KEY, "set GEMINI_API_KEY first");
    for (const model of ["gemini-3.8-flash", "gemini-3.5-flash-lite"]) {
      const env = makeEnv({ fetchImpl: fetch, local: { aiProvider: "gemini", geminiKey: process.env.GEMINI_API_KEY, geminiModel: model } });
      const t0 = Date.now();
      const res = await env.send({ type: "ai", tool: "proofread", text: "Yesterday I go to the market for buy some vegetable, but the shop was close.", extra: {} });
      assert.equal(res.ok, true, `${model}: ${res.error}`);
      console.log(`\n    ${model} (${((Date.now() - t0) / 1000).toFixed(1)} s): ${res.data.corrected}`);
      res.data.issues.forEach(i => console.log(`    - [${i.category}] ${i.original} → ${i.fix} | ${i.why}`));
      assert.ok(res.data.issues.length >= 3);
    }
  });
}

let failed = 0;
const run = tests.filter(([name]) => wanted(name));
for (const [name, fn] of run) {
  try { await fn(); report("  ✓ " + name); }
  catch (err) { failed++; report("  ✗ " + name + "\n    " + (err.stack || err).toString().split("\n").slice(0, 4).join("\n    ")); }
}
console.log(`${quiet ? "" : "\n"}${run.length - failed}/${run.length} passed${notRun(tests.length - run.length)}`);
process.exit(failed ? 1 : 0);
