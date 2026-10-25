/* Lamha desktop — a hidden helper process that reads the sentence around a selection in another app, so a lookup
 * there can pick the meaning that fits ("Understand words from their sentence"), as the browser extension does with
 * the page's text. Windows UI Automation (what screen readers use) answers, in lamha-uia.exe (uia-helper.cs): its own
 * process, so a slow or hung app can't block Lamha's main process; each question takes a few tens of milliseconds.
 * Privacy: the sentence is read only on the lookup shortcut, only ~400 characters on each side, only when the app's
 * selection is the text Lamha just copied. Nothing is logged. */
"use strict";
const { spawn } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");

/** Where the helper is: next to the app's resources when installed (package.json extraResources), bin/ from source
 *  (scripts/build-helper.mjs builds it there). */
function helperPath() {
  const places = [process.resourcesPath && path.join(process.resourcesPath, "lamha-uia.exe"), path.join(__dirname, "bin", "lamha-uia.exe")];
  return places.find(p => p && fs.existsSync(p)) || "";
}

// One JSON line per question { id, hwnd, text }; one JSON line per answer { id, before?, after?, error? }.
class UiaContext {
  constructor({ exe = helperPath() } = {}) {
    this.exe = exe;
    this.proc = null;
    this.pending = new Map(); // id → resolve
    this.seq = 0;
    this.buf = "";
  }

  /** Starts the helper (a fraction of a second; done ahead of the first shortcut). */
  start() {
    if (this.proc || process.platform !== "win32" || !this.exe) return;
    let proc;
    try { proc = spawn(this.exe, [], { windowsHide: true, stdio: ["pipe", "pipe", "ignore"] }); } catch (_) { return; }
    this.proc = proc;
    proc.stdout.setEncoding("utf8");
    proc.stdout.on("data", chunk => {
      this.buf += chunk;
      let nl;
      while ((nl = this.buf.indexOf("\n")) >= 0) {
        const line = this.buf.slice(0, nl).trim();
        this.buf = this.buf.slice(nl + 1);
        let msg;
        try { msg = JSON.parse(line); } catch (_) { continue; }
        const done = msg && this.pending.get(msg.id);
        if (done) { this.pending.delete(msg.id); done(msg); }
      }
    });
    const gone = () => {
      if (this.proc !== proc) return;
      this.proc = null;
      this.buf = "";
      for (const done of this.pending.values()) done(null);
      this.pending.clear();
    };
    proc.on("exit", gone);
    proc.on("error", gone);
    proc.stdin.on("error", () => {}); // the helper exited while we wrote: `gone` answers for it
  }

  /** One question to the helper; resolves to its answer, or null (no helper, timed out). Never rejects. */
  ask(question, timeout) {
    this.start();
    if (!this.proc) return Promise.resolve(null);
    const id = ++this.seq;
    return new Promise(resolve => {
      const timer = setTimeout(() => { this.pending.delete(id); resolve(null); }, timeout);
      this.pending.set(id, msg => { clearTimeout(timer); resolve(msg); });
      this.proc.stdin.write(JSON.stringify({ id, ...question }) + "\n");
    });
  }

  /**
   * { before, after } around `text` selected in window `hwnd`, or null (the app has no accessible text, its selection
   * isn't `text`, it's too slow, or the helper isn't available). Never rejects.
   */
  async around(hwnd, text, timeout = 700) {
    if (!hwnd || !text) return null;
    const msg = await this.ask({ hwnd, text }, timeout);
    return msg && typeof msg.before === "string" && typeof msg.after === "string" ? { before: msg.before, after: msg.after } : null;
  }

  stop() {
    const proc = this.proc;
    this.proc = null;
    for (const done of this.pending.values()) done(null);
    this.pending.clear();
    if (proc) { try { proc.stdin.end(); proc.kill(); } catch (_) { /* already gone */ } }
  }
}

module.exports = { UiaContext, helperPath };
