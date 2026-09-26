/* Lamha desktop — quick panel (Alt+Shift+V): search the history and paste into the app that was in front.
 * The window is created hidden at startup and reused; main.js sends "open" each time it is shown. */
"use strict";
(() => {
  const body = document.getElementById("cpBody");
  const foot = document.getElementById("cpFoot");
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
    body.replaceChildren(actionsRoot);
    LamhaClipActions.mount(actionsRoot, r.data, { mode: "panel", onBack: backToList }).focus();
  }
  function backToList() {
    foot.hidden = false;
    body.replaceChildren(listRoot);
    list.reload();
    list.focus();
  }

  /** While paused (tray): a banner above the list with استئناف. */
  const banner = LamhaClipList.h("div", { class: "banner cp-banner", role: "status", hidden: true },
    LamhaClipList.h("div", null, LamhaClipList.h("b", null, "الحافظة متوقفة مؤقتًا"), "لا يُسجَّل ما تنسخه الآن."),
    LamhaClipList.h("button", { class: "btn small", type: "button", onclick: async () => { await lamhaClipboard.resume(); banner.hidden = true; list.focus(); } }, "استئناف"));
  body.before(banner);

  function show(enabled, paused) {
    banner.hidden = !(enabled && paused);
    foot.hidden = !enabled;
    body.replaceChildren(enabled ? listRoot : optIn);
    if (enabled) { list.reset(); list.reload(); list.focus(); } else optIn.focusButton();
  }

  lamhaClipboard.onPanelOpen(state => show(state.enabled, state.paused));
  // Esc anywhere (the opt-in card has no search box)
  document.addEventListener("keydown", e => {
    if (e.key === "Escape" && !listRoot.contains(e.target)) { e.preventDefault(); lamhaClipboard.closePanel(); }
  });
})();
