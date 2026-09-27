// Tests for the writing tools: runs the real shared/lamha-ai.js + background.js in a VM with a fake
// `browser` (storage, messaging) and a fake `fetch`, so no network or API key is needed.
//   node tools/test-writing.mjs            unit tests only
//   node tools/test-writing.mjs --ollama   also one real proofread through the local Ollama
//                                          (model: $env:OLLAMA_MODEL, else the first Qwen model)
import { readFileSync } from "node:fs";
import vm from "node:vm";
import assert from "node:assert/strict";

const root = new URL("..", import.meta.url);
const src = p => readFileSync(new URL(p, root), "utf8");

/* ---------- fake WebExtension environment ---------- */

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

/** `realDict`: the real local-dict.js, reading the dictionary files from dict/ (other requests go to `fetchImpl`). */
function makeEnv({ fetchImpl, local = {}, sync = { uiLang: "ar" }, realDict = false }) {
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
  const browser = {
    storage: { local: storageArea(local), sync: storageArea(sync), onChanged: noopEvent },
    runtime: { onMessage: { addListener: f => { onMessage = f; } }, onInstalled: { addListener: f => installed.push(f) }, onStartup: noopEvent, getURL: p => "moz-extension://x/" + p },
    menus: { removeAll: async () => {}, create() {}, onClicked: noopEvent },
    commands: { onCommand: noopEvent },
    tabs: { query: async () => [], sendMessage: async () => {}, create: async () => {} },
    permissions: { contains: async () => true }
  };
  const ctx = vm.createContext({
    browser, fetch: fetchImpl, console, setTimeout, clearTimeout, AbortController, URLSearchParams, structuredClone,
    ...(realDict ? {} : { LocalDict: {} }), Audio: class {}
  });
  vm.runInContext(src("shared/i18n.js"), ctx, { filename: "i18n.js" });
  if (realDict) vm.runInContext(src("local-dict.js"), ctx, { filename: "local-dict.js" });
  vm.runInContext(src("shared/lamha-ai.js"), ctx, { filename: "lamha-ai.js" });
  vm.runInContext(src("background.js"), ctx, { filename: "background.js" });
  return {
    ctx, browser, send: msg => onMessage(msg, {}), flush: () => vm.runInContext("journalQueue", ctx),
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
  assert.deepEqual({ mode: d.mode, tl: d.tl, source: d.source, ar: d.ar, ipa: d.srcTranslit, dict: d.dict.length }, { mode: "en", tl: "en", source: "local", ar: "مَرِن", ipa: "rɪˈzɪljənt", dict: 0 });
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
  assert.equal((await toEnglish.send({ type: "lookup", text: "resilient" })).data.mode, "en", "no more 'resilient → resilient'");
  const env = makeEnv({ fetchImpl: fakeGoogle().fetchImpl, realDict: true, sync: { uiLang: "ar", useContext: false } });
  assert.equal((await env.send({ type: "lookup", text: "resilient" })).data.translation, "مَرِن", "Arabic by default");
  await env.send({ type: "setWordDict", en: true });
  assert.equal(env.browser.storage.sync.data.enDict, true);
  assert.equal((await env.send({ type: "lookup", text: "resilient" })).data.mode, "en");
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
    { mode: "en", t: "able to recover quickly from difficult conditions.", ex: "a resilient economy", ar: "مرن", source: "online" });
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
for (const [name, fn] of tests) {
  try { await fn(); console.log("  ✓ " + name); }
  catch (err) { failed++; console.log("  ✗ " + name + "\n    " + (err.stack || err).toString().split("\n").slice(0, 4).join("\n    ")); }
}
console.log(`\n${tests.length - failed}/${tests.length} passed`);
process.exit(failed ? 1 : 0);
