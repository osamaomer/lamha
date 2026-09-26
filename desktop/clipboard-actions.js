/* Lamha desktop — clipboard history: Lamha's tools on a clip (translate, write in English, proofread, summarize,
 * add to review). Everything goes through the background's existing messages (lookup, ai, cardHas / cardToggle /
 * cardRemove), so no network or AI code is duplicated, and the user's settings apply as everywhere else
 * (Local first / Local only / Online first, AI provider, mistake journal). Results are cached on the clip.
 * No Electron import: tools/test-clipboard.mjs runs it against the real background.js with a fake network. */
"use strict";

const REVIEW_WORD = /^[A-Za-z][A-Za-z'-]{1,40}$/;
const AI_TOOLS = { english: "toEnglish", proofread: "proofread", summary: "summarize" };
const cardKey = q => String(q || "").trim().toLowerCase(); // as in background.js

class ActionError extends Error {
  constructor(code) { super("clip action failed"); this.code = code; } // the code only: messages could carry clip text
}

/**
 * @param {object} o
 * @param {import("./clipboard-store").ClipboardStore} o.store
 * @param {(msg: object) => Promise<any>} o.send          the background's runtime.onMessage handler
 * @param {() => Promise<string>} o.targetLang            settings → لغة الترجمة
 * @param {() => Promise<object>} o.readCards             storage.local "cards" (the review deck)
 */
function createClipActions({ store, send, targetLang, readCards }) {
  const fail = r => { throw new ActionError((r && r.error) || "failed"); };

  async function translate(c, fresh) {
    const tl = await targetLang();
    const hit = c.cache.translation[tl];
    if (hit && !fresh) return { action: "translate", text: hit, cached: true };
    const r = await send({ type: "lookup", text: c.text }); // respects the dictionary mode, like the popup's box
    if (!r || !r.ok) fail(r);
    const text = String(r.data.translation || "");
    if (!text) throw new ActionError("empty_result");
    store.setCache(c.id, "translation", text, tl);
    return { action: "translate", text };
  }

  async function ai(c, action, fresh, lang) {
    const summary = action === "summary";
    lang = lang === "en" ? "en" : "ar"; // the summary's language; the other tools have one output language
    const hit = summary ? (c.cache.summary || {})[lang] : c.cache[action];
    const tag = summary ? { lang } : {};
    if (hit && !fresh) return { action, ...tag, ...hit, cached: true };
    // fresh (إعادة): a new answer, and proofreading isn't counted twice in the mistake journal
    const extra = { ...(summary ? { lang } : {}), ...(fresh ? { fresh: true } : {}) };
    const r = await send({ type: "ai", tool: AI_TOOLS[action], text: c.text, extra });
    if (!r || !r.ok) fail(r);
    const data = action === "proofread"
      ? { corrected: String(r.data.corrected || ""), issues: Array.isArray(r.data.issues) ? r.data.issues : [] }
      : { text: String(r.data.text || "") };
    store.setCache(c.id, action, data, lang);
    return { action, ...tag, ...data };
  }

  /**
   * أضف للمراجعة 🔖: adds the word, or removes it if it was already in the deck (the lookup card's 🔖 toggle).
   * The deck is read *before* the lookup, because a lookup adds English words by itself when "cardsAuto" is on.
   * A clip has no surrounding sentence: the card gets the dictionary's example sentence, if there is one.
   */
  async function review(c) {
    const word = c.text.trim();
    if (!REVIEW_WORD.test(word)) throw new ActionError("not_a_word");
    const before = (await readCards()) || {};
    const r = await send({ type: "lookup", text: word });
    if (!r || !r.ok) fail(r);
    const res = r.data;
    const out = { action: "review", word: res.query, tr: String(res.translation || "") };
    if (before[cardKey(res.query)]) {
      await send({ type: "cardRemove", key: cardKey(res.query) });
      return { ...out, inDeck: false };
    }
    // just added by the lookup itself ("cardsAuto"): no progress yet, so replace it with the card that has the example
    if (await send({ type: "cardHas", q: res.query })) await send({ type: "cardRemove", key: cardKey(res.query) });
    const def = (res.definitions || []).flatMap(d => d.entries || [])[0] || null;
    const ex = (def && def.example) || (res.examples || [])[0] || "";
    const added = await send({ type: "cardToggle", card: { q: res.query, tr: res.translation, ex, form: word, def: def ? def.gloss : "" } });
    if (!added) throw new ActionError("no_translation");
    return { ...out, inDeck: true };
  }

  /** Runs `action` on clip `id`; `fresh` (إعادة) skips and refreshes the cache; `lang` is the summary's language. */
  async function run(id, action, { fresh = false, lang } = {}) {
    const c = store.get(id);
    if (!c) throw new ActionError("not_found");
    if (action === "translate") return translate(c, fresh);
    if (Object.hasOwn(AI_TOOLS, action)) return ai(c, action, fresh, lang);
    if (action === "review") return review(c);
    throw new ActionError("unknown_action");
  }

  return { run };
}

module.exports = { createClipActions, ActionError, REVIEW_WORD };
