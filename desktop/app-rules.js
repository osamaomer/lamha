/* Lamha desktop — the decisions main.js makes that need no Windows or Electron calls, so they can be tested in plain
 * Node (tools/test-desktop.mjs): what each window is told, what the windows showing outside text and the Firefox
 * extension may ask, and where the floating windows go on the screen. */
"use strict";

/** storage.local keys that hold API keys: encrypted on disk, and never given to the floating card. */
const SECRET_KEYS = ["aiKey", "geminiKey"];

/** Data only Lamha's main window and Settings read (and read again from storage when it changes): the card, the clipboard
 *  panel and the reader never look at it, and with a big review deck it was a megabyte to each of them per lookup,
 *  unpacked by the card just before the lookup's answer. */
const PAGE_DATA = new Set(["cards", "cardStats", "cardsImported", "cardsRev", "history", "mistakes", "activity", "wotd", "lookupCount", "draft", "popupMode"]);

const without = (obj, drop) => Object.fromEntries(Object.entries(obj || {}).filter(([k]) => !drop(k)));
/** The floating card shows text from other apps and the web: it never gets the API keys (it has aiKeySet / geminiKeySet). */
const withoutSecrets = obj => without(obj, k => SECRET_KEYS.includes(k));

/**
 * The storage changes a window is sent: `role` "card" (the floating card), "lean" (the clipboard panel, the reader) or
 * "page" (the main window, Settings: everything). null: nothing to send it.
 */
function changesFor(role, changes) {
  if (role === "page") return changes;
  const out = without(changes, k => PAGE_DATA.has(k) || (role === "card" && SECRET_KEYS.includes(k)));
  return Object.keys(out).length ? out : null;
}

/** What the windows that show outside text (the floating card: any website or program; the reader: a downloaded article)
 *  may ask the background: what the card itself does. Not the calls that take an address (aiTest, ollamaModels: a request
 *  to any machine on the network), delete or change data (cardRemove, packRemove, appLink…) or read the deck. */
const OUTSIDE_TEXT_MESSAGES = new Set(["lookup", "translateBatch", "wiki", "wikiOpen", "speak", "stopSpeak", "ai", "cardHas", "cardToggle",
  "setWordDict", "writeTipSeen", "openOptions", "relayPage"]);
const allowedFromOutsideText = msg => !!msg && typeof msg === "object" && OUTSIDE_TEXT_MESSAGES.has(msg.type);

/** The Firefox extension's wikiSummary request, checked (it comes from another program): { titles, lang } or null. */
function wikiSummaryArgs(msg) {
  const titles = (msg && Array.isArray(msg.titles) ? msg.titles : []).filter(t => typeof t === "string" && t.trim() && t.length <= 120).slice(0, 6);
  const lang = String((msg && msg.lang) || "");
  return titles.length && /^[a-z]{2,3}$/.test(lang) ? { titles, lang } : null;
}

/** Settings' address suffix ("?welcome=1", "#ai", "?welcome=1#journal") → loadFile's { search, hash }; a welcome goes
 *  to the writing tools' section. */
function optionsTarget(suffix) {
  const [, search = "", hash = ""] = /^\??([^#]*)#?(.*)$/.exec(suffix || "") || [];
  return { search, hash: hash || (search.includes("welcome") ? "ai" : "") };
}

const clamp = (v, lo, hi) => Math.max(lo, Math.min(v, hi));

/**
 * The card window (w × h) next to the mouse (`cursor`), inside the monitor's `workArea`. Returns { bounds, point }: the
 * window's place, and where in it the card (or the Write button) attaches.
 * `toast`: a short hint just under the mouse. `pill`: the Write button, the mouse ~72 px from the window's top, with room
 * below it for the card it opens.
 */
function cardPlacement({ cursor, workArea: wa, w, h, toast = false, pill = false }) {
  const x = clamp(Math.round(cursor.x - w / 2), wa.x, wa.x + wa.width - w);
  if (pill) {
    const y = clamp(cursor.y - 72, wa.y, wa.y + wa.height - h);
    return { bounds: { x, y, width: w, height: h }, point: { x: cursor.x - x, y: cursor.y - y } };
  }
  const down = !toast && (wa.y + wa.height - cursor.y >= 380 || wa.y + wa.height - cursor.y >= cursor.y - wa.y);
  const y = clamp(down ? cursor.y + 16 : cursor.y + (toast ? 60 : -16) - h, wa.y, wa.y + wa.height - h);
  return { bounds: { x, y, width: w, height: h }, point: { x: cursor.x - x, y: down ? Math.max(12, cursor.y + 16 - y) : Math.min(h - 12, cursor.y - 16 - y) } };
}

/** The clipboard panel (w × h) under the mouse, or above it when there's no room below, inside the work area. */
function panelPlacement({ cursor, workArea: wa, w, h }) {
  const below = wa.y + wa.height - cursor.y >= h + 16;
  return {
    x: clamp(cursor.x - Math.round(w / 2), wa.x, wa.x + wa.width - w),
    y: clamp(below ? cursor.y + 16 : cursor.y - 16 - h, wa.y, wa.y + wa.height - h),
    width: w, height: h
  };
}

module.exports = { SECRET_KEYS, PAGE_DATA, withoutSecrets, changesFor, OUTSIDE_TEXT_MESSAGES, allowedFromOutsideText, wikiSummaryArgs, optionsTarget, cardPlacement, panelPlacement };
