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

function makeEnv({ fetchImpl, local = {}, sync = { uiLang: "ar" } }) {
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
    LocalDict: {}, Audio: class {}
  });
  vm.runInContext(src("shared/i18n.js"), ctx, { filename: "i18n.js" });
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
