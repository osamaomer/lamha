/* Lamha desktop — the Lamha 3 window (the bridge, updater.js): what changes, then "Install Lamha 3" downloads its
 * setup here, with its progress, and main.js quits Lamha 2 and starts it; "Later" closes the window and no notification
 * comes for 3 days. Opened by the notification, the tray and Settings → Updates. */
"use strict";
(async () => {
  await LamhaI18n.init({ onChange: () => location.reload() });
  LamhaI18n.applyDom(document);
  const L = (key, vars) => LamhaI18n.t(key, vars);
  const $ = id => document.getElementById(id);
  const install = $("l3Install"), later = $("l3Later"), status = $("l3Status"), bar = $("l3Bar");

  async function render() {
    const st = await lamhaUpdates.state();
    $("l3Wiki").hidden = !st.wikipedia;
    const busy = st.status === "lamha3-downloading" || st.status === "lamha3-starting";
    install.disabled = busy || !["lamha3", "lamha3-error"].includes(st.status);
    later.disabled = st.status === "lamha3-starting";
    bar.hidden = !busy;
    bar.firstElementChild.style.width = `${Math.round((st.progress || 0) * 100)}%`;
    status.classList.toggle("bad", st.status === "lamha3-error");
    status.textContent =
      st.status === "lamha3-downloading" ? L("d.l3Downloading", { p: Math.floor((st.progress || 0) * 100) }) :
      st.status === "lamha3-starting" ? L("d.l3Starting") :
      st.status === "lamha3-error" ? L(st.error === "damaged" ? "d.l3Damaged" : "d.l3Failed") :
      st.lamha3 && st.lamha3.size ? L("d.l3Size", { mb: Math.max(1, Math.round(st.lamha3.size / 1048576)) }) : "";
  }

  install.addEventListener("click", () => { lamhaUpdates.installLamha3(); render(); });
  later.addEventListener("click", () => lamhaUpdates.lamha3Later());
  document.addEventListener("keydown", e => { if (e.key === "Escape") lamhaUpdates.closeLamha3(); }); // like the X: a download goes on
  lamhaUpdates.onChanged(render);
  await render();
  install.focus();
})();
