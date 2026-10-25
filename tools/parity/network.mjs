// The network both Lamhas see: one function answers every request from Lamha 2 (in this process) and Lamha 3 (over
// the bridge), so the two get the same answers. Google's answers are recorded (recordings/google.json, filled by
// --record); the AI is a fake that answers from the prompt; a case's `net` says what is down.
import { readFileSync, writeFileSync, existsSync } from "node:fs";

/* What a case's network is like. Google answers from the recordings when it's "up"; the AI (Gemini, Ollama) is the fake.
 *   up           everything answers
 *   rate         Google is rate-limited (429); the AI answers
 *   quota        Google answers; Gemini's quota is used up (429)
 *   rateQuota    both are rate-limited
 *   offline      no network (the system says so): Google and Gemini fail at once; Ollama, on this PC, answers
 *   dead         connected, but nothing answers: Google and Gemini never reply; Ollama answers
 *   stray        Google is rate-limited; the AI's first translation has letters from another script, the second is clean
 *   strayAlways  Google is rate-limited; every AI translation has stray letters */
export const NETS = ["up", "rate", "quota", "rateQuota", "offline", "dead", "stray", "strayAlways"];

const googleState = { up: "up", rate: 429, quota: "up", rateQuota: 429, offline: "offline", dead: "hang", stray: 429, strayAlways: 429 };
const geminiState = { up: "up", rate: "up", quota: 429, rateQuota: 429, offline: "offline", dead: "hang", stray: "up", strayAlways: "up" };

export const kindOf = url =>
  url.includes("/translate_a/") ? "google"
    : url.includes("generativelanguage.googleapis.com") ? "gemini"
    : /\/api\/(chat|tags)$/.test(url) ? "ollama"
    : url.includes("api.anthropic.com") ? "claude"
    : url.includes("wikipedia.org") ? "wiki"
    : "other";

/** A Google request's identity: what was asked, whichever of its two addresses, GET or POST. */
export function googleKey(url, body) {
  const u = new URL(url);
  const form = new URLSearchParams(typeof body === "string" && !body.trimStart().startsWith("{") ? body : "");
  const all = n => [...u.searchParams.getAll(n), ...form.getAll(n)];
  return JSON.stringify([u.pathname.split("/").pop(), all("sl")[0] || "", all("tl")[0] || "", all("hl")[0] || "",
    all("dt").sort().join(","), all("format")[0] || "", all("q")]);
}

/** The prompt an AI request carries, whichever provider: { system, user }. */
export function promptOf(kind, body) {
  let j;
  try { j = JSON.parse(body); } catch (_) { return { system: "", user: "" }; }
  if (kind === "gemini") {
    return { system: j.systemInstruction?.parts?.map(p => p.text).join("") || "", user: j.contents?.at(-1)?.parts?.map(p => p.text).join("") || "" };
  }
  const msgs = j.messages || [];
  const sys = typeof j.system === "string" ? j.system : (msgs.find(m => m.role === "system") || {}).content || "";
  return { system: sys, user: (msgs.filter(m => m.role === "user").at(-1) || {}).content || "" };
}

const textsIn = user => { const m = /<texts>\n([\s\S]*)\n<\/texts>/.exec(user); return m ? JSON.parse(m[1]) : null; };
const wordIn = user => { const m = /<word>\n([\s\S]*?)\n<\/word>/.exec(user); return m ? m[1] : null; };

/** The fake AI's answer to a prompt, as the JSON text the model would write, or null for a prompt it doesn't know. */
function fakeAi(user, stray) {
  const texts = textsIn(user);
  if (texts) {
    // A translation that is visibly the AI's: the marked word of a sentence keeps its mark, translated on its own.
    const one = t => (/<a i="?0"?>/.test(t)
      ? t.replace(/(<a i="?0"?>)([\s\S]*?)(<\/a>)/, (_, a, w, z) => `${a}AI:${w}${z}`)
      : `AI<${t}>`) + (stray ? " 캐시" : "");
    return JSON.stringify({ translations: texts.map(one) });
  }
  const word = wordIn(user);
  if (word) {
    const ar = /in Arabic/.test(user.split("\n")[0]) || /written entirely in Arabic/.test(user);
    return JSON.stringify({
      senses: [
        { pos: "noun", gloss: `AI gloss one for ${word}`, example: `An example with ${word}.`, synonyms: ["syn one", "syn two"] },
        { pos: "verb", gloss: `AI gloss two for ${word}`, example: "", synonyms: [] }
      ],
      meaning: `AI meaning of ${word}`, root: ar ? "ك ت ب" : "", plural: ar ? "كتب" : ""
    });
  }
  return null;
}

const aiReply = (kind, text) => kind === "gemini"
  ? { candidates: [{ finishReason: "STOP", content: { role: "model", parts: [{ text }] } }] }
  : { done: true, done_reason: "stop", message: { role: "assistant", content: text } };

/**
 * The network. `answer(side, kase, req)` → { status, body } (body: JSON text) | { fail: "offline" } | { fail: "hang" }.
 * `req`: { method, url, body } (body: the request's text). Every request is logged per side and case.
 * `record`: Google requests missing from the recordings go to the real Google (Lamha 2's only), and are kept.
 */
export function makeNetwork({ file, record = false }) {
  const recordings = existsSync(file) ? JSON.parse(readFileSync(file, "utf8")) : {};
  const log = []; // { side, case, kind, url, body }
  const aiCount = new Map();
  const missing = []; // Google requests with no recorded answer: { side, case, key }
  let added = 0;
  let queue = Promise.resolve();

  async function live(url, init) {
    // One request at a time, a moment apart, so Google doesn't rate-limit the recording.
    const run = queue.then(async () => {
      for (let attempt = 0; attempt < 4; attempt++) {
        await new Promise(r => setTimeout(r, 150 + attempt * 3000));
        const res = await fetch(url, { method: init.method, headers: init.method === "POST" ? { "Content-Type": "application/x-www-form-urlencoded;charset=UTF-8" } : {}, body: init.method === "POST" ? init.body : undefined });
        if (res.status === 200) return { status: 200, json: await res.json() };
        if (res.status !== 429 && res.status < 500) return { status: res.status, json: {} };
      }
      throw new Error("Google kept refusing while recording: try --record again later");
    });
    queue = run.catch(() => {});
    return run;
  }

  async function answer(side, kase, req) {
    const kind = kindOf(req.url);
    log.push({ side, case: kase.id, kind, method: req.method, url: req.url, body: req.body || "" });
    if (kind === "google") {
      const state = googleState[kase.net];
      if (state === "offline") return { fail: "offline" };
      if (state === "hang") return { fail: "hang" };
      if (state === 429) return { status: 429, body: "{}" };
      const key = googleKey(req.url, req.body);
      if (!Object.hasOwn(recordings, key)) {
        if (record && side === "legacy") {
          recordings[key] = await live(req.url, req);
          added++;
        } else {
          missing.push({ side, case: kase.id, key });
          return { fail: "offline" };
        }
      }
      const r = recordings[key];
      return { status: r.status, body: JSON.stringify(r.json) };
    }
    if (kind === "gemini" || kind === "ollama") {
      if (kind === "gemini") {
        const state = geminiState[kase.net];
        if (state === "offline") return { fail: "offline" };
        if (state === "hang") return { fail: "hang" };
        if (state === 429) return { status: 429, body: JSON.stringify({ error: { code: 429, message: "quota", status: "RESOURCE_EXHAUSTED" } }) };
      }
      if (req.url.endsWith("/api/tags")) return { status: 200, body: JSON.stringify({ models: [{ name: "qwen3.5:4b" }] }) };
      const { user } = promptOf(kind, req.body);
      const n = (aiCount.get(side + "|" + kase.id) || 0) + 1;
      aiCount.set(side + "|" + kase.id, n);
      const stray = kase.net === "strayAlways" || (kase.net === "stray" && n === 1);
      const text = fakeAi(user, stray && !!textsIn(user));
      return text == null ? { status: 200, body: JSON.stringify(aiReply(kind, "{}")) } : { status: 200, body: JSON.stringify(aiReply(kind, text)) };
    }
    return { status: 404, body: "{}" };
  }

  function save() {
    if (!added) return 0;
    const keys = Object.keys(recordings).sort();
    // One answer per line: a new recording shows as added lines in a diff.
    writeFileSync(file, "{\n" + keys.map(k => JSON.stringify(k) + ": " + JSON.stringify(recordings[k])).join(",\n") + "\n}\n");
    return added;
  }

  return { answer, log, missing, save, recordings };
}
