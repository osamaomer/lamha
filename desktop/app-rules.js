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
 * window's place, and where in it the card attaches. `toast`: a short hint just under the mouse.
 */
function cardPlacement({ cursor, workArea: wa, w, h, toast = false }) {
  const x = clamp(Math.round(cursor.x - w / 2), wa.x, wa.x + wa.width - w);
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

/**
 * What "Automatic" animations means on this PC (shared/motion.js reads it as storage.local motionHint): "subtle" on a
 * weak machine (4 GB of memory or less, 2 cores or fewer), or when Lamha asked for the graphics card (`gpuWanted`) and
 * drawing still isn't accelerated (`gpuCompositing` from app.getGPUFeatureStatus(): its graphics don't work); "full"
 * otherwise. Drawing without the graphics card because the user left it off (the default since 1.9.8) says nothing
 * about the PC: measured, the marks and transitions still run at the screen's full rate. 1.9.8 counted it, and every
 * PC on Automatic got Subtle animations.
 */
function motionHint({ gpuWanted, gpuCompositing = "enabled", totalMem, cores }) {
  const weak = totalMem <= 4.5 * 1024 ** 3 || cores <= 2;
  const broken = gpuWanted && !/^enabled/.test(String(gpuCompositing));
  return weak || broken ? "subtle" : "full";
}

/* ---- Windows' accent colour (Settings → Appearance, accentWindows) ----
 * Windows lets people pick any colour, light yellow included, so it isn't used as it is: each theme gets a version
 * that keeps text readable (WCAG 4.5:1). --accent (links, labels, icons) against the cards' surface, --btn (filled
 * buttons, selected chips) under white text. The page palette's own values stay the fallbacks (shared/ui.css). */
const SURFACE = { light: [255, 255, 255], dark: [44, 44, 46] }; // --surface in ui.css and the card's --bg
const lum = rgb => {
  const c = rgb.map(v => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; });
  return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
};
const contrast = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };
const mix = (rgb, to, t) => rgb.map((v, i) => Math.round(v + (to[i] - v) * t));
const hex = rgb => "#" + rgb.map(v => v.toString(16).padStart(2, "0")).join("");
/** `rgb` moved toward `to` (black or white) just enough to reach 4.5:1 against `against`. */
function readable(rgb, against, to) {
  for (let t = 0; t <= 1.0001; t += 0.04) { const c = mix(rgb, to, t); if (contrast(c, against) >= 4.5) return c; }
  return to;
}

/**
 * The accent and button colours for both themes from Windows' accent (systemPreferences.getAccentColor(): "RRGGBB"
 * or "RRGGBBAA"), or null when it isn't a colour. { light: { accent, accentSoft, btn }, dark: { … } }.
 */
function accentPalette(windowsHex) {
  const m = /^#?([0-9a-f]{6})([0-9a-f]{2})?$/i.exec(String(windowsHex || ""));
  if (!m) return null;
  const rgb = [0, 2, 4].map(i => parseInt(m[1].slice(i, i + 2), 16));
  const white = [255, 255, 255], black = [0, 0, 0];
  const theme = (surface, toward) => {
    const accent = readable(rgb, surface, toward);
    const btn = readable(rgb, white, black); // white text on it, in both themes
    return { accent: hex(accent), accentSoft: `rgba(${accent.join(", ")}, ${toward === white ? 0.15 : 0.09})`, btn: hex(btn) };
  };
  return { light: theme(SURFACE.light, black), dark: theme(SURFACE.dark, white) };
}

/**
 * The stylesheet a page of the app gets for that palette: the pages' own tokens (stronger than ui.css's :root rules,
 * for both ways a page turns dark), and --lamha-app-* for the card, whose shadow root takes them from its page
 * (content/styles.js .root.app). "" without a palette: the pages keep Lamha's colours.
 */
function accentCss(p) {
  if (!p) return "";
  const vars = t => `--accent: ${t.accent}; --accent-soft: ${t.accentSoft}; --btn: ${t.btn};`;
  const card = (t, sfx) => `--lamha-app-accent${sfx}: ${t.accent}; --lamha-app-accent-soft${sfx}: ${t.accentSoft}; --lamha-app-btn${sfx}: ${t.btn};`;
  return `html:root { ${vars(p.light)} ${card(p.light, "")} ${card(p.dark, "-dark")} }
html:root[data-theme="dark"] { ${vars(p.dark)} }
@media (prefers-color-scheme: dark) { html:root:not([data-theme="light"]) { ${vars(p.dark)} } }`;
}

module.exports = { SECRET_KEYS, PAGE_DATA, withoutSecrets, changesFor, OUTSIDE_TEXT_MESSAGES, allowedFromOutsideText, wikiSummaryArgs, optionsTarget, cardPlacement, panelPlacement, motionHint, accentPalette, accentCss, contrast };
