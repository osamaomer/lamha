/* Lamha desktop — read the text selected in any app (simulated Ctrl+C) and put a result back (Ctrl+V),
 * leaving the user's clipboard as it was. Electron 44 clipboard API: async, W3C-style ClipboardItem/Blob. */
"use strict";
const { clipboard, ClipboardItem } = require("electron");
const native = require("./native");

/** Clipboard history: Lamha's own copy / restore / paste must not become clips (see clipboard-monitor.js). */
let suppress = fn => fn();
const setSuppressor = f => { suppress = f; };

/** A copy of everything on the clipboard (every entry, every type), to put back afterwards. */
async function saveClipboard() {
  try {
    const items = await clipboard.read();
    const saved = [];
    for (const item of items) {
      const entry = {};
      for (const type of item.types) {
        try { entry[type] = await item.getType(type); } catch (_) { /* type vanished meanwhile */ }
      }
      if (Object.keys(entry).length) saved.push(entry);
    }
    return saved;
  } catch (_) {
    const text = await clipboard.readText().catch(() => "");
    return text ? [{ "text/plain": text }] : [];
  }
}

async function restoreClipboard(saved) {
  try {
    if (saved.length) await clipboard.write(saved.map(entry => new ClipboardItem(entry)));
    else clipboard.clear();
  } catch (_) { /* a format Electron can't write back: keep at least the text */
    const t = saved.find(e => typeof e["text/plain"] !== "undefined");
    if (t) await clipboard.writeText(typeof t["text/plain"] === "string" ? t["text/plain"] : await t["text/plain"].text());
  }
}

/** Waits (up to `ms`) for the clipboard to stop holding `marker`. Returns the new text, or null. */
async function waitForChange(marker, ms) {
  for (const until = Date.now() + ms; Date.now() < until;) {
    await native.sleep(20);
    const t = await clipboard.readText().catch(() => marker);
    if (t !== marker) return t;
  }
  return null;
}

/**
 * The text selected in the app in front, plus that window's handle (to paste into later).
 * { hwnd, text, terminal } — text is "" when nothing was selected.
 */
function captureSelection() {
  return suppress(captureSelectionNow);
}

async function captureSelectionNow() {
  const hwnd = native.foreground();
  const terminal = native.isTerminal(hwnd);
  if (terminal) return { hwnd, text: "", terminal }; // Ctrl+C would interrupt the program running there
  await native.releaseModifiers(); // otherwise the app sees Ctrl+Alt+Shift+C
  const saved = await saveClipboard();
  const marker = "⁣lamha-" + Date.now(); // invisible, never a real selection
  await clipboard.writeText(marker);
  native.ctrlChord(native.VK.C);
  const text = await waitForChange(marker, 600);
  await restoreClipboard(saved);
  return { hwnd, text: text && text.trim() ? text : "", terminal };
}

/** Brings `hwnd` back to the front and pastes `text` over its selection. */
async function pasteInto(hwnd, text) {
  if (!native.isWindow(hwnd)) return false;
  return suppress(() => pasteNow(hwnd, text));
}

/** Brings `hwnd` to the front; false if Windows or the app refused within ~500 ms. */
async function focusWindow(hwnd) {
  native.forceForeground(hwnd);
  for (let i = 0; i < 25 && native.foreground() !== hwnd; i++) await native.sleep(20);
  return native.foreground() === hwnd;
}

async function pasteNow(hwnd, text) {
  if (!(await focusWindow(hwnd))) return false;
  await native.releaseModifiers(300);
  const saved = await saveClipboard();
  await clipboard.writeText(text);
  await native.sleep(40);
  native.ctrlChord(native.VK.V);
  await native.sleep(350); // let the app read the clipboard before we restore it
  await restoreClipboard(saved);
  return true;
}

/* ---- clipboard history: the clip stays on the clipboard afterwards (like Win+V), so nothing is restored ---- */

/** { text, html? } onto the clipboard, with its formatting when there is HTML. Not captured as a new clip. */
function copyClip(clip) {
  return suppress(() => writeClip(clip));
}

function writeClip({ text, html }) {
  return html ? clipboard.write([new ClipboardItem({ "text/plain": text, "text/html": html })]) : clipboard.writeText(text);
}

/**
 * Puts a clip on the clipboard and pastes it into `hwnd` with Ctrl+V, the way Replace does.
 * Returns false when it could only be copied: the window is gone, is a terminal, or refused focus.
 */
function pasteClip(hwnd, clip) {
  return suppress(async () => {
    await writeClip(clip);
    if (!native.isWindow(hwnd) || native.isTerminal(hwnd)) return false; // terminals: as with Replace, no keystrokes
    if (!(await focusWindow(hwnd))) return false;
    await native.releaseModifiers(300);
    await native.sleep(40);
    native.ctrlChord(native.VK.V);
    await native.sleep(350); // the app reads the clipboard now; stay suppressed until it has
    return true;
  });
}

module.exports = { captureSelection, pasteInto, saveClipboard, restoreClipboard, setSuppressor, copyClip, pasteClip };
