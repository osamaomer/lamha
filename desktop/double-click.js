/* Lamha desktop — the Write button on a double-click in an empty text box: the decisions that need no Windows calls,
 * so they can be tested in plain Node (tools/test-desktop.mjs). The mouse hook and the text box check are in
 * uia-context.js, the button itself in main.js. */
"use strict";

/**
 * Pairs left-button presses into double-clicks the way Windows does: the second press within the double-click time
 * (GetDoubleClickTime) and inside the double-click rectangle (SM_CXDOUBLECLK × SM_CYDOUBLECLK) centred on the first.
 * A third press starts a new pair, so a triple-click isn't two double-clicks.
 */
class ClickPairer {
  constructor() { this.last = null; }

  /** `down`: { t, x, y } (t: Windows' millisecond tick count). `zone`: { time, width, height }. True on a double-click. */
  press(down, zone) {
    const prev = this.last;
    const dt = prev ? (down.t - prev.t) >>> 0 : Infinity; // the tick count wraps after 49.7 days
    if (prev && dt <= zone.time && Math.abs(down.x - prev.x) * 2 <= zone.width && Math.abs(down.y - prev.y) * 2 <= zone.height) {
      this.last = null;
      return true;
    }
    this.last = down;
    return false;
  }
}

// Write new starts as an email in these, as a message elsewhere
const EMAIL_APPS = new Set(["outlook.exe", "olk.exe", "hxoutlook.exe", "thunderbird.exe", "betterbird.exe", "mailspring.exe", "mailclient.exe"]);
// Windows' own shell: its search and address boxes aren't for writing
const SHELL_APPS = new Set(["explorer.exe", "searchhost.exe", "searchapp.exe", "startmenuexperiencehost.exe", "shellexperiencehost.exe", "lockapp.exe", "textinputhost.exe"]);
// Browsers: only boxes in the web page count, not the address bar (any other browser is treated like any other app)
const BROWSERS = new Set(["firefox.exe", "chrome.exe", "msedge.exe", "brave.exe", "opera.exe", "vivaldi.exe", "librewolf.exe", "waterfox.exe", "floorp.exe", "zen.exe", "chromium.exe", "thorium.exe", "arc.exe"]);

/** "email" or "message", from the program's exe name (lowercase, as native.processNameOf gives it). */
const composeKind = exe => (EMAIL_APPS.has(exe) ? "email" : "message");

/**
 * Whether a double-click should show the Write button, given the program (`exe`) and the helper's answer about the
 * focused box (`field`, see UiaContext.field). The Lamha extension's own button in a browser wins.
 */
function wantsButton(exe, field) {
  if (!exe || exe === "lamha" || SHELL_APPS.has(exe)) return false;
  if (!field || !field.empty || field.lamha) return false;
  if (BROWSERS.has(exe) && !field.web) return false;
  return true;
}

module.exports = { ClickPairer, composeKind, wantsButton, EMAIL_APPS, SHELL_APPS, BROWSERS };
