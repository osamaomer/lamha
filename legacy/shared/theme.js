/* Lamha — the Theme setting (storage.sync `theme`: "auto" | "light" | "dark") on Lamha's own pages: popup, settings,
 * and the desktop app's windows. It sets data-theme on <html>, which shared/ui.css reads; "auto" (no attribute) follows
 * the system. Loaded in <head>: the copy kept in localStorage paints the right colours before storage answers.
 * The card on web pages has its own (content.js, applyTheme). */
"use strict";
(() => {
  const root = document.documentElement;
  const apply = v => { if (v === "light" || v === "dark") root.dataset.theme = v; else delete root.dataset.theme; };
  const keep = v => { apply(v); try { localStorage.setItem("lamhaTheme", v || "auto"); } catch (_) { /* no storage here */ } };
  try { apply(localStorage.getItem("lamhaTheme")); } catch (_) { /* first paint follows the system */ }
  if (typeof browser === "undefined" || !browser.storage) return;
  browser.storage.sync.get({ theme: "auto" }).then(s => keep(s.theme), () => {});
  browser.storage.onChanged.addListener((changes, area) => { if (area === "sync" && changes.theme) keep(changes.theme.newValue); });
})();
