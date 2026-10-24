/* Lamha desktop — quick panel (Alt+Shift+V): search the history and paste into the app that was in front.
 * The window is created hidden at startup and reused; main.js sends "open" each time it is shown. */
"use strict";
(async () => {
  // the panel's own markup (header, key hints) in the interface language; a change reloads it (main.js injects again)
  await Promise.all([LamhaI18n.init({ onChange: () => location.reload() }), LamhaMotion.ready]);
  LamhaMotion.attach(document.documentElement);
  LamhaI18n.applyDom(document);
  const body = document.getElementById("cpBody");
  const foot = document.getElementById("cpFoot");
  const head = document.querySelector(".cp-head");
  const listRoot = document.createElement("div");
  const list = LamhaClipList.create({
    root: listRoot,
    mode: "panel",
    onActivate: (it, { plain }) => lamhaClipboard.paste(it.id, plain),
    onActions: it => showActions(it.id),
    onEscape: () => lamhaClipboard.closePanel()
  });
  const optIn = LamhaClipList.optIn(() => show(true));

  /** Tab / → on a row: Lamha's tools for that clip; Esc comes back to the list. */
  async function showActions(id) {
    const r = await lamhaClipboard.get(id);
    if (!r.ok || !r.data) return;
    const actionsRoot = document.createElement("section"); // fresh each time: mount() adds its own key handling
    foot.hidden = true;
    head.hidden = true; // the tools have their own header (back, title, the clip)
    body.replaceChildren(actionsRoot);
    LamhaClipActions.mount(actionsRoot, r.data, { mode: "panel", onBack: backToList }).focus();
  }
  function backToList() {
    foot.hidden = false;
    head.hidden = false;
    body.replaceChildren(listRoot);
    list.reload();
    list.focus();
  }

  /** While paused (tray): a banner above the list with استئناف. */
  const banner = LamhaClipList.h("div", { class: "banner cp-banner", role: "status", hidden: true },
    LamhaClipList.h("div", null, LamhaClipList.h("b", null, LamhaI18n.t("d.pausedTitle")), LamhaI18n.t("d.pausedText")),
    LamhaClipList.h("button", { class: "btn small", type: "button", onclick: async () => { await lamhaClipboard.resume(); banner.hidden = true; list.focus(); } }, LamhaI18n.t("d.resumeBtn")));
  body.before(banner);

  function show(enabled, paused) {
    banner.hidden = !(enabled && paused);
    foot.hidden = !enabled;
    body.replaceChildren(enabled ? listRoot : optIn);
    if (enabled) { list.reset(); list.focus(); } else optIn.focusButton(); // reset() reloads, with the rows coming in one by one
  }

  lamhaClipboard.onPanelOpen(state => show(state.enabled, state.paused));
  // Esc anywhere (the opt-in card has no search box)
  document.addEventListener("keydown", e => {
    if (e.key === "Escape" && !listRoot.contains(e.target)) { e.preventDefault(); lamhaClipboard.closePanel(); }
  });
})();
