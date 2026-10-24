/* Lamha desktop — clipboard history, capture side: notices every copy the user makes in any app and emits
 * "clip-captured" { text, html?, sourceApp, capturedAt, seq }. Lamha's own clipboard use (the shortcut's Ctrl+C,
 * the restore, pasting) runs inside suppress() and is never captured.
 * Privacy: clip contents are never logged — only lengths and reasons. */
"use strict";
const { BrowserWindow, clipboard } = require("electron");
const { EventEmitter } = require("node:events");
const native = require("./native");

const WM_CLIPBOARDUPDATE = 0x031d;
const DEBOUNCE_MS = 150; // Office and browsers write several formats per copy
const GRACE_MS = 400; // after a suppressed operation, late updates still belong to it
const POLL_MS = 500; // fallback when the listener can't be registered
const MAX_TEXT = 200000;
const MAX_HTML_BYTES = 500 * 1024;
const RING_SIZE = 50;

const sleep = ms => new Promise(r => setTimeout(r, ms));

class ClipboardMonitor extends EventEmitter {
  constructor() {
    super();
    this.enabled = false;
    this.paused = false; // Phase 5: tray pause
    this.depth = 0; // suppress() nesting
    this.lastSeq = -1;
    this.seen = new Set(); // sequence numbers that belong to Lamha's own writes
    this.timer = null;
    this.poller = null;
    this.win = null;
    this.hwnd = 0;
    this.listening = false;
    this.recent = []; // in-memory ring buffer, newest first
    this.maxSyncMs = 0; // longest main-thread stretch of one capture (for the performance target)
    /** (exeName) => true: never record copies from that program (excluded apps). Checked before anything is read. */
    this.skipApp = () => false;
    /** (text) => true: don't record this text (bank-card numbers). */
    this.skipText = () => false;
    /** Self-test only: pretend every copy comes from this program. */
    this.sourceAppOverride = null;
  }

  /** Turns capture on or off (setting `clipboardEnabled`). */
  setEnabled(on) {
    on = !!on;
    if (on === this.enabled) return;
    this.enabled = on;
    if (on) this.listen(); else this.unlisten();
  }

  setPaused(on) { this.paused = !!on; }

  /** Runs Lamha's own clipboard work; nothing it (or the next 400 ms) writes becomes a clip. */
  async suppress(fn) {
    // A user copy still settling (e.g. copy, then Alt+Shift+L within 150 ms) is captured first — the app that
    // copied has already written it — instead of being swallowed by the suppression that starts now.
    if (this.timer && !this.suppressed) {
      clearTimeout(this.timer);
      this.timer = null;
      await this.process().catch(err => console.error("[clipboard] capture failed:", err && err.name));
    }
    this.depth++;
    try {
      return await fn();
    } finally {
      this.markSeen();
      setTimeout(() => { this.markSeen(); this.depth--; }, GRACE_MS);
    }
  }

  get suppressed() { return this.depth > 0; }

  markSeen() {
    const seq = native.clipboardSeq();
    this.seen.add(seq);
    if (this.seen.size > 200) this.seen.delete(this.seen.values().next().value);
  }

  listen() {
    if (this.listening) return;
    this.lastSeq = native.clipboardSeq(); // what is on the clipboard already isn't a new copy
    if (!this.win || this.win.isDestroyed()) {
      // a hidden window of our own: its only job is to receive WM_CLIPBOARDUPDATE
      this.win = new BrowserWindow({ show: false, width: 1, height: 1, skipTaskbar: true, focusable: false });
      this.hwnd = native.hwndOf(this.win);
      this.win.hookWindowMessage(WM_CLIPBOARDUPDATE, () => this.onUpdate());
    }
    if (!native.addClipboardListener(this.hwnd)) {
      console.warn("[clipboard] listener unavailable — polling instead");
      let last = native.clipboardSeq();
      this.poller = setInterval(() => {
        const seq = native.clipboardSeq();
        if (seq !== last) { last = seq; this.onUpdate(); }
      }, POLL_MS);
      this.poller.unref();
    }
    this.listening = true;
  }

  unlisten() {
    if (!this.listening) return;
    clearTimeout(this.timer);
    this.timer = null;
    if (this.poller) { clearInterval(this.poller); this.poller = null; } else native.removeClipboardListener(this.hwnd);
    this.listening = false;
  }

  /** On quit. */
  stop() {
    this.unlisten();
    if (this.win && !this.win.isDestroyed()) this.win.destroy();
    this.win = null;
  }

  onUpdate() {
    if (this.suppressed) { this.markSeen(); return; }
    clearTimeout(this.timer);
    this.timer = setTimeout(() => { this.timer = null; this.process().catch(err => console.error("[clipboard] capture failed:", err && err.name)); }, DEBOUNCE_MS);
  }

  /** Reads the clipboard once it has settled and applies the skip rules, in order. */
  async process() {
    const t0 = performance.now();
    const seq = native.clipboardSeq();
    if (seq === this.lastSeq || this.seen.has(seq)) return;
    this.lastSeq = seq;
    // 1. off or paused   2. Lamha's own write
    if (!this.enabled || this.paused) return;
    if (this.suppressed) { this.seen.add(seq); return; }
    // 3. the app asked clipboard viewers / history to ignore this content (password managers, like Win+V honours)
    if (native.hasClipboardFormat("ExcludeClipboardContentFromMonitorProcessing") || native.hasClipboardFormat("Clipboard Viewer Ignore")) return this.skip("excluded format");
    if (native.hasClipboardFormat("CanIncludeInClipboardHistory")) {
      const v = native.clipboardDword("CanIncludeInClipboardHistory", this.hwnd);
      if (v !== 1) return this.skip("excluded from history"); // 0, or unreadable: stay on the safe side
    }
    const hasHtml = native.hasClipboardFormat("HTML Format");
    // the owner now, before we yield: it may change while we read
    const sourceApp = this.sourceAppOverride || native.processNameOf(native.clipboardOwner()) || native.processNameOf(native.foreground()) || "unknown";
    this.maxSyncMs = Math.max(this.maxSyncMs, performance.now() - t0);
    // excluded programs: nothing of theirs is read, let alone stored
    if (this.skipApp(sourceApp)) return this.skip("excluded app");

    const text = await readWithRetry(() => clipboard.readText());
    if (text == null) return this.skip("clipboard busy");
    // 4. empty   5. too long
    if (!text.trim()) return;
    if (text.length > MAX_TEXT) return this.skip(`too long (${text.length} chars)`);
    if (this.skipText(text)) return this.skip("card number");
    // 6. HTML only when small enough
    let html;
    if (hasHtml) {
      html = await readWithRetry(readHtml);
      if (html && Buffer.byteLength(html) > MAX_HTML_BYTES) html = undefined;
    }
    // the clipboard changed or Lamha started writing while we read: the next update takes care of it
    if (native.clipboardSeq() !== seq || this.suppressed) return;

    const clip = { text, sourceApp, capturedAt: Date.now(), seq };
    if (html) clip.html = html;
    this.recent.unshift(clip);
    if (this.recent.length > RING_SIZE) this.recent.length = RING_SIZE;
    this.emit("clip-captured", clip);
  }

  skip(reason) {
    console.log(`[clipboard] skipped: ${reason}`);
  }
}

/** Another app may hold the clipboard open: try 3 more times, 50 ms apart, then give up silently. */
async function readWithRetry(read) {
  for (let i = 0; i < 4; i++) {
    try { return await read(); } catch (_) { if (i < 3) await sleep(50); }
  }
  return null;
}

async function readHtml() {
  for (const item of await clipboard.read()) {
    if (!item.types.includes("text/html")) continue;
    const v = await item.getType("text/html");
    return typeof v === "string" ? v : await v.text();
  }
  return undefined;
}

module.exports = { ClipboardMonitor };
