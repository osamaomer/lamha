/* Lamha desktop — clipboard history, privacy rules: programs whose copies are never recorded, bank-card numbers,
 * and the settings' defaults. Pure functions, no Electron: tested by tools/test-clipboard.mjs. */
"use strict";

/** Password managers: excluded out of the box (they usually also mark their copies, but not always). */
const DEFAULT_EXCLUDED_APPS = ["keepass.exe", "keepassxc.exe", "1password.exe", "bitwarden.exe", "enpass.exe", "dashlane.exe", "nordpass.exe"];

const DEFAULTS = {
  clipboardEnabled: false,
  clipboardMaxItems: 500, // 100 | 250 | 500 | 1000
  clipboardExpiryDays: 30, // 1 | 7 | 30 | 90 | 0 = never
  clipboardSkipCards: true,
  clipboardExcludedApps: DEFAULT_EXCLUDED_APPS
};

/** "KeePassXC", "C:\\…\\KeePassXC.exe " → "keepassxc.exe"; "" when it can't be a program name. */
function exeName(input) {
  let s = String(input || "").trim().replace(/^"|"$/g, "");
  s = s.slice(Math.max(s.lastIndexOf("\\"), s.lastIndexOf("/")) + 1).toLowerCase();
  if (s && !s.endsWith(".exe")) s += ".exe";
  return /^[\p{L}\p{N} _.()+&'-]{1,80}\.exe$/u.test(s) && s !== ".exe" ? s : "";
}

/**
 * A single bank-card number: 13–19 digits, spaces or dashes allowed between them, passing the Luhn check.
 * Anything else (phone numbers, IBANs, amounts, text around the number) is not a card number.
 */
function isCardNumber(text) {
  const t = String(text || "").trim();
  if (!/^\d(?:[ -]?\d){12,18}$/.test(t)) return false;
  const digits = t.replace(/[ -]/g, "");
  let sum = 0;
  for (let i = 0; i < digits.length; i++) {
    let d = digits.charCodeAt(digits.length - 1 - i) - 48;
    if (i % 2 === 1) { d *= 2; if (d > 9) d -= 9; }
    sum += d;
  }
  return sum % 10 === 0;
}

module.exports = { DEFAULT_EXCLUDED_APPS, DEFAULTS, exeName, isCardNumber };
